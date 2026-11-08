using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Claupet.Tests;

/// <summary>
/// OpenCode y el SQLite del sistema (Db), con una base sintética armada aquí con las tablas de OpenCode (session y
/// message, data en JSON y horas en milisegundos), no copiada de un equipo real.
/// </summary>
public sealed class OpenCodeTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.Now;

    static AgentAccount Agent(string folder) => new() { Kind = Agents.OpenCode, Label = Agents.Name(Agents.OpenCode), Folder = folder };

    static string Assistant(string model, int output, int reasoning) =>
        $$$"""{"role":"assistant","modelID":"{{{model}}}","providerID":"x","tokens":{"input":999,"output":{{{output}}},"reasoning":{{{reasoning}}},"cache":{"read":5,"write":0}},"cost":0}""";

    const string User = """{"role":"user"}""";

    /// <summary>Una base como la de OpenCode en esa carpeta: sesiones (id, carpeta, padre, hora) y sus mensajes.</summary>
    static void CreateDb(string path, IEnumerable<(string Id, string Folder, string? Parent, DateTimeOffset At)> sessions,
        IEnumerable<(string Session, DateTimeOffset At, string Data)> messages)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Assert.True(Db.Available);
        using var db = new SqliteConnection($"Data Source={path};Pooling=False");
        db.Open();
        using (var create = db.CreateCommand())
        {
            create.CommandText = """
                PRAGMA journal_mode = WAL;
                CREATE TABLE session (id TEXT PRIMARY KEY, project_id TEXT, parent_id TEXT, directory TEXT NOT NULL, title TEXT,
                    time_created INTEGER NOT NULL, time_updated INTEGER NOT NULL);
                CREATE TABLE message (id TEXT PRIMARY KEY, session_id TEXT NOT NULL, time_created INTEGER NOT NULL,
                    time_updated INTEGER NOT NULL, data TEXT NOT NULL);
                CREATE INDEX message_session_idx ON message (session_id);
                """;
            create.ExecuteNonQuery();
        }
        foreach (var s in sessions)
        {
            using var insert = db.CreateCommand();
            insert.CommandText = "INSERT INTO session VALUES ($id, 'p', $parent, $dir, 't', $at, $at)";
            insert.Parameters.AddWithValue("$id", s.Id);
            insert.Parameters.AddWithValue("$parent", (object?)s.Parent ?? DBNull.Value);
            insert.Parameters.AddWithValue("$dir", s.Folder);
            insert.Parameters.AddWithValue("$at", s.At.ToUnixTimeMilliseconds());
            insert.ExecuteNonQuery();
        }
        var n = 0;
        foreach (var m in messages)
        {
            using var insert = db.CreateCommand();
            insert.CommandText = "INSERT INTO message VALUES ($id, $session, $at, $at, $data)";
            insert.Parameters.AddWithValue("$id", "m" + n++);
            insert.Parameters.AddWithValue("$session", m.Session);
            insert.Parameters.AddWithValue("$at", m.At.ToUnixTimeMilliseconds());
            insert.Parameters.AddWithValue("$data", m.Data);
            insert.ExecuteNonQuery();
        }
        using var checkpoint = db.CreateCommand();
        checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        checkpoint.ExecuteNonQuery();
    }

    [Fact]
    public void ElSqliteDelSistemaSeCarga()
    {
        Assert.True(Db.Available);
        using var dir = new TempDir();
        Assert.Null(Db.OpenReadOnly(dir["no-existe.db"]));
    }

    [Fact]
    public void OpenCodeDaSusSesionesModelosYTokensDeHoy()
    {
        using var dir = new TempDir();
        var recent = Now.AddMinutes(-5);
        var earlier = Now.Date == Now.AddMinutes(-30).Date ? Now.AddMinutes(-30) : recent;
        var old = Now.AddDays(-3);
        var path = dir["opencode/opencode.db"];
        CreateDb(path,
            [("ses_hoy", "/home/x/proyecto", null, recent), ("ses_sub", "/home/x/proyecto", "ses_hoy", recent), ("ses_vieja", "/home/x/vieja", null, old)],
            [
                ("ses_hoy", earlier, User),
                ("ses_hoy", earlier, Assistant("claude-sonnet", 100, 20)),
                ("ses_hoy", recent, Assistant("gpt-5", 50, 0)),
                ("ses_sub", recent, Assistant("gpt-5-mini", 10, 0)),
                ("ses_vieja", old, User),
                ("ses_vieja", old, Assistant("viejo", 1000, 0)),
                ("ses_vieja", old, "{esto no es json"),
            ]);
        var before = SHA256.HashData(File.ReadAllBytes(path));

        var snapshot = Agents.Read(Agent(dir["opencode"]), Now);
        Assert.True(snapshot.Exists);
        Assert.Empty(snapshot.Limits);
        Assert.Equal(["ses_hoy", "ses_vieja"], snapshot.Sessions.Select(s => s.Id)); // el subagente no sale como sesión
        var today = snapshot.Sessions[0];
        Assert.Equal("/home/x/proyecto", today.Folder);
        Assert.Equal("gpt-5", today.Model); // el último que respondió
        Assert.Equal(3, today.Messages);
        Assert.Equal("viejo", snapshot.Sessions[1].Model);
        Assert.Equal(1, snapshot.SessionsToday);
        Assert.Equal(3, snapshot.ResponsesToday); // con la del subagente
        Assert.Equal(180, snapshot.TokensToday); // salida y razonamiento, sin la entrada
        Assert.Equal(L.T("Hoy: {0} · {1} respuestas · {2} tokens", L.T("1 sesión"), 3, Format.Tokens(180)), Agents.Today(snapshot));

        // Solo se lee: la base queda igual.
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.NotNull(Agents.LastWrite(Agent(dir["opencode"])));
    }

    [Fact]
    public void UnaBaseQueNoSeEntiendeNoRompe()
    {
        using var dir = new TempDir();
        dir.Write("opencode/opencode.db", "esto no es una base");
        var broken = Agents.Read(Agent(dir["opencode"]), Now);
        Assert.True(broken.Exists);
        Assert.Equal(1, broken.Unreadable);
        Assert.Empty(broken.Sessions);

        // Otra versión, sin las tablas que se esperan.
        var other = dir["otra/opencode.db"];
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        using (var db = new SqliteConnection($"Data Source={other};Pooling=False"))
        {
            db.Open();
            using var create = db.CreateCommand();
            create.CommandText = "CREATE TABLE cosa (id TEXT)";
            create.ExecuteNonQuery();
        }
        Assert.Equal(1, Agents.Read(Agent(dir["otra"]), Now).Unreadable);

        // Sin base todavía (OpenCode instalado pero sin usar): está, sin sesiones.
        Directory.CreateDirectory(dir["vacia"]);
        var empty = Agents.Read(Agent(dir["vacia"]), Now);
        Assert.Equal(0, empty.Unreadable);
        Assert.Null(Agents.LastWrite(Agent(dir["vacia"])));
    }

    [Fact]
    public void SeDetectaSuCarpetaDeDatosYSeAbrePorAcp()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir["opencode"]);
        var settings = new AppSettings();
        Assert.True(Agents.Detect(settings, kind => dir[kind]));
        Assert.Equal([Agents.OpenCode], settings.Agents.Select(a => a.Kind));
        Assert.Equal("OpenCode", settings.Agents[0].Label);
        Assert.EndsWith(Path.Combine(".local", "share", "opencode"), Agents.DefaultFolder(Agents.OpenCode));

        Assert.Equal("OpenCode", AgentKinds.Label(AgentKinds.OpenCode));
        Assert.IsType<AcpDriver>(AgentKinds.Driver(AgentKinds.OpenCode, new AgentTranscript()));
        Assert.Equal("OpenCode", CanvasMcp.Server.ClientName("opencode"));
    }
}
