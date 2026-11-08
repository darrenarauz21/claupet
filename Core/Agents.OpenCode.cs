using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Claupet;

/// <summary>
/// OpenCode (~/.local/share/opencode): sus proyectos, sesiones y mensajes van en una base SQLite, opencode.db (o la de
/// $OPENCODE_DB; si es relativa, dentro de esa carpeta). Se abre solo para leer, con el SQLite del sistema (Db). Las
/// tablas, según su código (packages/core/src/database/schema.gen.ts y packages/schema/src/session-message.ts):
/// - OpenCode 2: session_v2 (id, parent_id, directory, model, time_updated…) y session_message (session_id, type:
///   «user», «assistant»…, time_created y data: el mensaje en JSON, con model {id, providerID} y tokens {input, output,
///   reasoning, cache}).
/// - Antes: session y message, con el rol y el modelo dentro de data (role, modelID).
/// Las sesiones con parent_id son de subagentes: suman a lo de hoy pero no salen como sesión. Las horas van en
/// milisegundos. No escribe sus límites: se ve lo que hizo.
/// </summary>
static class OpenCodeReader
{
    /// <summary>Las tablas de esa versión y qué columnas tienen.</summary>
    sealed record Layout(string Sessions, string Messages, bool TypeColumn, bool Parent, bool Folder, bool Model);

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

    /// <summary>Las tablas que hay: las de OpenCode 2 y, si no, las de antes. null si no es ninguna.</summary>
    static Layout? Find(SqliteConnection db)
    {
        foreach (var (sessions, messages) in new[] { ("session_v2", "session_message"), ("session", "message") })
        {
            var s = Columns(db, sessions);
            var m = Columns(db, messages);
            if (!s.IsSupersetOf(["id", "time_updated"]) || !m.IsSupersetOf(["session_id", "time_created", "data"])) continue;
            return new(sessions, messages, m.Contains("type"), s.Contains("parent_id"), s.Contains("directory"), s.Contains("model"));
        }
        return null;
    }

    /// <summary>Lo leído de una base ya abierta (las pruebas pasan una armada aquí).</summary>
    internal static AgentSnapshot Read(AgentAccount agent, SqliteConnection db, DateTimeOffset now)
    {
        if (Find(db) is not { } layout) return new(agent, agent.Label, true, [], [], 0, 0, 1);
        // Solo los mensajes de la conversación: los de la v2 que no lo son (cambios de modelo, de agente…) no cuentan.
        var spoken = layout.TypeColumn ? "AND type IN ('user', 'assistant')" : "";

        // Las sesiones más recientes (sin las de subagentes), con su carpeta y su modelo si los anota.
        var sessions = new List<(string Id, string? Folder, string? Model, DateTimeOffset Updated)>();
        using (var command = db.CreateCommand())
        {
            command.CommandText = $"""
                SELECT id, {(layout.Folder ? "directory" : "NULL")}, {(layout.Model ? "model" : "NULL")}, time_updated FROM {layout.Sessions}
                {(layout.Parent ? "WHERE parent_id IS NULL" : "")} ORDER BY time_updated DESC LIMIT 30
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                sessions.Add((reader.GetString(0), Text(reader, 1), ModelName(Text(reader, 2)), Time(reader.GetInt64(3))));
        }

        var list = new List<AgentSession>();
        foreach (var s in sessions)
        {
            // Cuántos mensajes tiene y, si la sesión no anota el modelo, el del último que respondió.
            using var count = db.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM {layout.Messages} WHERE session_id = $id {spoken}";
            count.Parameters.AddWithValue("$id", s.Id);
            var messages = Convert.ToInt32(count.ExecuteScalar());
            var model = s.Model ?? LastModel(db, layout, s.Id);
            list.Add(new AgentSession(Agents.OpenCode, s.Id, s.Folder, model, s.Updated, messages));
        }

        // Lo de hoy, con los subagentes: las respuestas, los tokens que generaron y los mensajes de las sesiones. Solo
        // las sesiones que se movieron hoy, así no se recorre toda la tabla de mensajes.
        int messagesToday = 0, responsesToday = 0;
        long tokensToday = 0;
        using (var command = db.CreateCommand())
        {
            var start = new DateTimeOffset(now.ToLocalTime().Date, now.ToLocalTime().Offset).ToUnixTimeMilliseconds();
            command.CommandText = $"""
                SELECT m.data, {(layout.TypeColumn ? "m.type" : "NULL")}, {(layout.Parent ? "s.parent_id IS NOT NULL" : "0")}
                FROM {layout.Sessions} s JOIN {layout.Messages} m ON m.session_id = s.id
                WHERE s.time_updated >= $start AND m.time_created >= $start {(layout.TypeColumn ? "AND m.type IN ('user', 'assistant')" : "")}
                """;
            command.Parameters.AddWithValue("$start", start);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (Parse(reader.GetString(0), Text(reader, 1)) is not { } message) continue;
                var subagent = !reader.IsDBNull(2) && reader.GetBoolean(2);
                if (message.Role == "assistant")
                {
                    responsesToday++;
                    tokensToday += message.Tokens;
                }
                if (!subagent && message.Role is "user" or "assistant") messagesToday++;
            }
        }

        var today = list.Count(s => Agents.SameDay(s.LastActivity, now));
        return new AgentSnapshot(agent, agent.Label, true, [], list, today, messagesToday, 0, responsesToday, tokensToday);
    }

    /// <summary>El modelo de la última respuesta de esa sesión (mira las últimas, no toda la sesión).</summary>
    static string? LastModel(SqliteConnection db, Layout layout, string session)
    {
        using var command = db.CreateCommand();
        command.CommandText = $"""
            SELECT data, {(layout.TypeColumn ? "type" : "NULL")} FROM {layout.Messages}
            WHERE session_id = $id {(layout.TypeColumn ? "AND type = 'assistant'" : "")} ORDER BY time_created DESC LIMIT 5
            """;
        command.Parameters.AddWithValue("$id", session);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (Parse(reader.GetString(0), Text(reader, 1)) is { Role: "assistant", Model: { } model }) return model;
        return null;
    }

    /// <summary>De un mensaje, lo que se usa: quién habla, el modelo y los tokens generados (salida y razonamiento).</summary>
    internal readonly record struct Message(string? Role, string? Model, long Tokens);

    /// <summary>
    /// Un mensaje: type es la columna de la v2 (si no, el rol va en data). El modelo es un objeto {id, providerID} en la
    /// v2 y modelID antes.
    /// </summary>
    internal static Message? Parse(string data, string? type = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 64 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var tokens = Agents.Child(root, "tokens") is { ValueKind: JsonValueKind.Object } t
                ? (long)((Agents.Number(t, "output") ?? 0) + (Agents.Number(t, "reasoning") ?? 0)) : 0;
            var model = Json.Str(root, "modelID") ?? (Agents.Child(root, "model") is { ValueKind: JsonValueKind.Object } m ? Json.Str(m, "id") : null);
            return new(type ?? Json.Str(root, "role") ?? Json.Str(root, "type"), model, tokens);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>El modelo que anota la sesión: un objeto {id, providerID} o «proveedor/modelo#variante»; se muestra el id.</summary>
    internal static string? ModelName(string? stored)
    {
        if (stored is not { Length: > 0 } text) return null;
        if (text[0] == '{')
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                return doc.RootElement.ValueKind == JsonValueKind.Object ? Json.Str(doc.RootElement, "id") : null;
            }
            catch (JsonException) { return null; }
        }
        var slash = text.IndexOf('/');
        var id = slash > 0 ? text[(slash + 1)..] : text;
        var hash = id.IndexOf('#');
        return (hash >= 0 ? id[..hash] : id) is { Length: > 0 } name ? name : null;
    }

    /// <summary>Una hora de la base: milisegundos desde 1970 (si parece en segundos, se toma en segundos).</summary>
    internal static DateTimeOffset Time(long value) =>
        value < 100_000_000_000 ? DateTimeOffset.FromUnixTimeSeconds(value) : DateTimeOffset.FromUnixTimeMilliseconds(value);

    static string? Text(SqliteDataReader reader, int column) => reader.IsDBNull(column) ? null : reader.GetValue(column)?.ToString();

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
