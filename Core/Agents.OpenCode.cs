using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Claupet;

/// <summary>
/// OpenCode (~/.local/share/opencode): sus proyectos, sesiones y mensajes van en una base SQLite, opencode.db (o la de
/// $OPENCODE_DB; si es relativa, dentro de esa carpeta). Se abre solo para leer, con el SQLite del sistema (Db). Tablas:
/// session (id, directory, parent_id, time_updated…) y message (id, session_id, time_created, data: el mensaje en JSON,
/// con role, modelID y tokens {input, output, reasoning, cache}). Las sesiones con parent_id son de subagentes: suman a lo
/// de hoy pero no salen como sesión. Las horas van en milisegundos. No escribe sus límites: se ve lo que hizo.
/// </summary>
static class OpenCodeReader
{
    /// <summary>La base de ese agente: $OPENCODE_DB (relativa a su carpeta) u opencode.db.</summary>
    internal static string DbPath(AgentAccount agent) =>
        Environment.GetEnvironmentVariable("OPENCODE_DB") is { Length: > 0 } custom
            ? Path.GetFullPath(Path.Combine(agent.Folder, custom)) : Path.Combine(agent.Folder, "opencode.db");

    /// <summary>Cuándo escribió por última vez: la base o su diario (-wal), que es donde escribe mientras trabaja.</summary>
    public static DateTimeOffset? LastWrite(AgentAccount agent)
    {
        var path = DbPath(agent);
        DateTimeOffset? last = null;
        foreach (var file in new[] { path, path + "-wal" })
        {
            var info = new FileInfo(file);
            if (!info.Exists) continue;
            var at = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            if (!(last >= at)) last = at;
        }
        return last;
    }

    public static AgentSnapshot Read(AgentAccount agent, DateTimeOffset now)
    {
        var path = DbPath(agent);
        if (!File.Exists(path)) return new(agent, agent.Label, true, [], [], 0, 0, 0);
        try
        {
            using var db = Db.OpenReadOnly(path);
            // Sin SQLite en el sistema: no se puede leer, pero el agente está.
            return db is null ? new(agent, agent.Label, true, [], [], 0, 0, 1) : Read(agent, db, now);
        }
        catch (SqliteException ex)
        {
            ErrorLog.Write(ex);
            return new(agent, agent.Label, true, [], [], 0, 0, 1);
        }
    }

    /// <summary>Lo leído de una base ya abierta (las pruebas pasan una armada en memoria).</summary>
    internal static AgentSnapshot Read(AgentAccount agent, SqliteConnection db, DateTimeOffset now)
    {
        var sessionColumns = Columns(db, "session");
        var messageColumns = Columns(db, "message");
        if (!sessionColumns.Contains("id") || !sessionColumns.Contains("time_updated")) return new(agent, agent.Label, true, [], [], 0, 0, 1);
        var hasParent = sessionColumns.Contains("parent_id");
        var hasMessages = messageColumns.IsSupersetOf(["session_id", "time_created", "data"]);

        // Las sesiones más recientes (sin las de subagentes).
        var sessions = new List<(string Id, string? Folder, DateTimeOffset Updated)>();
        using (var command = db.CreateCommand())
        {
            var folder = sessionColumns.Contains("directory") ? "directory" : "NULL";
            command.CommandText = $"SELECT id, {folder}, time_updated FROM session {(hasParent ? "WHERE parent_id IS NULL" : "")} ORDER BY time_updated DESC LIMIT 30";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                sessions.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), Time(reader.GetInt64(2))));
        }

        // Por sesión: cuántos mensajes y el último modelo que respondió.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var models = new Dictionary<string, string>(StringComparer.Ordinal);
        int messagesToday = 0, responsesToday = 0;
        long tokensToday = 0;
        if (hasMessages && sessions.Count > 0)
        {
            using (var command = db.CreateCommand())
            {
                var ids = new List<string>();
                for (var i = 0; i < sessions.Count; i++)
                {
                    ids.Add("$s" + i);
                    command.Parameters.AddWithValue("$s" + i, sessions[i].Id);
                }
                command.CommandText = $"SELECT session_id, data FROM message WHERE session_id IN ({string.Join(", ", ids)}) ORDER BY time_created";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var id = reader.GetString(0);
                    counts[id] = counts.GetValueOrDefault(id) + 1;
                    if (Parse(reader.GetString(1)) is { Model: { } model }) models[id] = model;
                }
            }

            // Lo de hoy, con los subagentes: las respuestas, los tokens que generaron y los mensajes de las sesiones.
            using (var command = db.CreateCommand())
            {
                var start = new DateTimeOffset(now.ToLocalTime().Date, now.ToLocalTime().Offset).ToUnixTimeMilliseconds();
                // Solo las sesiones que se movieron hoy: así no se recorre toda la tabla de mensajes.
                command.CommandText = $"""
                    SELECT m.data, {(hasParent ? "s.parent_id IS NOT NULL" : "0")} FROM session s JOIN message m ON m.session_id = s.id
                    WHERE s.time_updated >= $start AND m.time_created >= $start
                    """;
                command.Parameters.AddWithValue("$start", start);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (Parse(reader.GetString(0)) is not { } message) continue;
                    var subagent = !reader.IsDBNull(1) && reader.GetBoolean(1);
                    if (message.Role == "assistant")
                    {
                        responsesToday++;
                        tokensToday += message.Tokens;
                    }
                    if (!subagent && message.Role is "user" or "assistant") messagesToday++;
                }
            }
        }

        var list = sessions.Select(s => new AgentSession(Agents.OpenCode, s.Id, s.Folder, models.GetValueOrDefault(s.Id), s.Updated,
            counts.GetValueOrDefault(s.Id))).ToList();
        var today = list.Count(s => Agents.SameDay(s.LastActivity, now));
        return new AgentSnapshot(agent, agent.Label, true, [], list, today, messagesToday, 0, responsesToday, tokensToday);
    }

    /// <summary>De un mensaje, lo que se usa: quién habla, el modelo y los tokens generados (salida y razonamiento).</summary>
    internal readonly record struct Message(string? Role, string? Model, long Tokens);

    internal static Message? Parse(string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 64 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var tokens = Agents.Child(root, "tokens") is { ValueKind: JsonValueKind.Object } t
                ? (long)((Agents.Number(t, "output") ?? 0) + (Agents.Number(t, "reasoning") ?? 0)) : 0;
            return new(Json.Str(root, "role"), Json.Str(root, "modelID"), tokens);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Una hora de la base: milisegundos desde 1970 (si parece en segundos, se toma en segundos).</summary>
    internal static DateTimeOffset Time(long value) =>
        value < 100_000_000_000 ? DateTimeOffset.FromUnixTimeSeconds(value) : DateTimeOffset.FromUnixTimeMilliseconds(value);

    static HashSet<string> Columns(SqliteConnection db, string table)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = db.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        using var reader = command.ExecuteReader();
        while (reader.Read()) columns.Add(reader.GetString(1));
        return columns;
    }
}
