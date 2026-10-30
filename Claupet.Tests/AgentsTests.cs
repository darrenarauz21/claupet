using System.Globalization;

namespace Claupet.Tests;

/// <summary>
/// Los otros agentes, con archivos sintéticos (no copiados de un equipo real): Codex CLI y GitHub Copilot CLI con los
/// formatos que documenta la comunidad, y Gemini CLI con el que escribe su código (chatRecordingService.ts, v0.66). Y la
/// memoria compartida de una cuenta de Claude.
/// </summary>
public sealed class AgentsTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.Now;

    static string Iso(DateTimeOffset t) => t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    static AgentAccount Agent(string kind, string folder) => new() { Kind = kind, Label = Agents.Name(kind), Folder = folder };

    [Fact]
    public void CodexDaSusLimitesDelUltimoEventoYSusSesiones()
    {
        using var dir = new TempDir();
        var older = Now.AddHours(-3);
        var newer = Now.AddMinutes(-10);
        static string At(string json, DateTimeOffset t) => json.Replace("@T", Iso(t));
        var old = dir.Write($"codex/sessions/{older:yyyy/MM/dd}/rollout-vieja.jsonl", string.Join("\n",
            At("""{"timestamp":"@T","type":"session_meta","payload":{"id":"s-vieja","cwd":"/home/x/vieja"}}""", older),
            At("""{"timestamp":"@T","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":90.0,"window_minutes":299,"resets_in_seconds":60}}}}""", older)));
        var file = dir.Write($"codex/sessions/{newer:yyyy/MM/dd}/rollout-nueva.jsonl", string.Join("\n",
            At("""{"timestamp":"@T","type":"session_meta","payload":{"id":"s-nueva","cwd":"/home/x/proyecto"}}""", newer.AddMinutes(-5)),
            At("""{"timestamp":"@T","type":"turn_context","payload":{"model":"gpt-5-codex"}}""", newer.AddMinutes(-5)),
            "{esto no es json",
            At("""{"timestamp":"@T","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":42.5,"window_minutes":299,"resets_in_seconds":3600},"secondary":{"used_percent":12.0,"window_minutes":10079,"resets_in_seconds":86400}}}}""", newer)));
        File.SetLastWriteTimeUtc(old, older.UtcDateTime);
        File.SetLastWriteTimeUtc(file, newer.UtcDateTime);
        dir.Write($"codex/sessions/{newer:yyyy/MM/dd}/rollout-comprimida.jsonl.zst", "zstd");

        var snapshot = Agents.Read(Agent(Agents.Codex, dir["codex"]), Now);
        Assert.True(snapshot.Exists);
        Assert.Equal(2, snapshot.Limits.Count);
        var session = snapshot.Limits[0];
        Assert.Equal(L.T("Sesión (5 h)"), session.Label);
        Assert.Equal(42.5, session.Percent);
        Assert.Equal(DateTimeOffset.Parse(Iso(newer), CultureInfo.InvariantCulture).AddSeconds(3600), session.ResetsAt);
        Assert.Equal(L.T("Semana"), snapshot.Limits[1].Label);
        Assert.Equal(1, snapshot.Unreadable); // el .zst no se lee

        var latest = snapshot.Sessions[0];
        Assert.Equal("s-nueva", latest.Id);
        Assert.Equal("/home/x/proyecto", latest.Folder);
        Assert.Equal("gpt-5-codex", latest.Model);
        Assert.Equal(2, snapshot.Sessions.Count);
    }

    [Fact]
    public void UnaVentanaQueYaSeReinicioVuelveACero()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""{"used_percent":97.0,"window_minutes":299,"resets_in_seconds":60}""");
        var limit = CodexReader.Limit(doc.RootElement, Now.AddHours(-1), Now)!;
        Assert.Equal(0, limit.Percent);
        Assert.Null(limit.ResetsAt);
        Assert.Equal(L.T("Ventana de {0} h", 2), CodexReader.WindowLabel(120));
    }

    [Fact]
    public void GeminiCuentaSusMensajesYDaLaCarpetaDelProyecto()
    {
        using var dir = new TempDir();
        dir.Write("gemini/projects.json", """{ "projects": { "/home/x/demo": "hash-demo" } }""");
        dir.Write("gemini/tmp/hash-demo/chats/session-hoy.json", $$"""
            {
              "sessionId": "g-1", "projectHash": "hash-demo", "startTime": "{{Iso(Now.AddMinutes(-30))}}", "lastUpdated": "{{Iso(Now.AddMinutes(-2))}}",
              "messages": [
                { "type": "user", "content": "hola" },
                { "type": "gemini", "content": "hola", "model": "gemini-2.5-pro", "toolCalls": [] },
                { "type": "info", "content": "aviso" },
                { "type": "user", "content": "otra" }
              ]
            }
            """);
        dir.Write("gemini/tmp/otro-hash/chats/session-ayer.json", $$"""
            { "sessionId": "g-0", "projectHash": "otro-hash", "lastUpdated": "{{Iso(Now.AddDays(-2))}}", "messages": [ { "type": "user" } ] }
            """);
        dir.Write("gemini/tmp/otro-hash/chats/session-rota.json", "{rota");

        var snapshot = Agents.Read(Agent(Agents.Gemini, dir["gemini"]), Now);
        Assert.Empty(snapshot.Limits);
        var today = snapshot.Sessions[0];
        Assert.Equal("g-1", today.Id);
        Assert.Equal("/home/x/demo", today.Folder);
        Assert.Equal("gemini-2.5-pro", today.Model);
        Assert.Equal(3, today.Messages);
        Assert.Equal("otro-hash", snapshot.Sessions[1].Folder); // sin ruta en projects.json: el nombre de su carpeta
        Assert.Equal(1, snapshot.SessionsToday);
        Assert.Equal(3, snapshot.MessagesToday);
        Assert.Equal(1, snapshot.Unreadable);
        Assert.Equal(L.T("Hoy: {0} · {1} mensajes", L.T("1 sesión"), 3), Agents.Today(snapshot));
    }

    [Fact]
    public void GeminiLeeElFormatoJsonlConSusTokensSubagentesYLoDeshecho()
    {
        using var dir = new TempDir();
        var noon = new DateTimeOffset(DateTime.Today.AddHours(12)); // lejos de la medianoche
        string At(int minutes) => Iso(noon.AddMinutes(minutes));
        const string Id = "abcd1234-0000-4000-8000-000000000001";
        dir.Write("gemini/projects.json", """{ "projects": { "/home/x/demo": "demo" } }""");

        // La de antes de 2026, que Gemini CLI copió a .jsonl al retomarla y dejó ahí: no se cuenta dos veces.
        var old = dir.Write("gemini/tmp/demo/chats/session-2026-10-29T16-30-abcd1234.json", $$"""
            { "sessionId": "{{Id}}", "projectHash": "h1", "startTime": "{{At(-30)}}", "lastUpdated": "{{At(-28)}}",
              "messages": [
                { "id": "u1", "timestamp": "{{At(-29)}}", "type": "user", "content": [ { "text": "hola" } ] },
                { "id": "g1", "timestamp": "{{At(-28)}}", "type": "gemini", "content": "hola", "model": "gemini-3-pro",
                  "tokens": { "input": 1000, "output": 200, "cached": 500, "thoughts": 50, "tool": 0, "total": 1250 } } ] }
            """);
        var current = dir.Write("gemini/tmp/demo/chats/session-2026-10-29T16-30-abcd1234.jsonl", string.Join("\n",
            $$"""{"sessionId":"{{Id}}","projectHash":"h1","startTime":"{{At(-30)}}","lastUpdated":"{{At(-28)}}","summary":"saludo"}""",
            $$"""{"id":"u1","timestamp":"{{At(-29)}}","type":"user","content":[{"text":"hola"}]}""",
            // La respuesta sale sin tokens y se vuelve a agregar entera cuando llegan: vale la última.
            $$"""{"id":"g1","timestamp":"{{At(-28)}}","type":"gemini","content":"hola","thoughts":[],"tokens":null,"model":"gemini-3-pro"}""",
            $$"""{"id":"g1","timestamp":"{{At(-28)}}","type":"gemini","content":"hola","thoughts":[],"tokens":{"input":1000,"output":200,"cached":500,"thoughts":50,"tool":0,"total":1250},"model":"gemini-3-pro"}""",
            $$$"""{"$set":{"sessionId":"{{{Id}}}"}}""",
            $$"""{"id":"u2","timestamp":"{{At(-10)}}","type":"user","content":"otra"}""",
            $$"""{"id":"g2","timestamp":"{{At(-9)}}","type":"gemini","content":"x","tokens":{"input":2000,"output":300,"cached":0,"thoughts":0,"tool":10,"total":2310},"model":"gemini-3-flash"}""",
            $$$"""{"$set":{"lastUpdated":"{{{At(-9)}}}"}}""",
            """{"$rewindTo":"u2"}""", // deshecha en pantalla, pero esos tokens ya se gastaron
            """{"$patch":{"id":"g1","content":"editado"}}""",
            $$"""{"id":"i1","timestamp":"{{At(-8)}}","type":"info","content":"aviso"}""",
            "{rota",
            $$$"""{"$set":{"lastUpdated":"{{{At(-8)}}}"}}"""));
        File.SetLastWriteTimeUtc(old, noon.AddMinutes(-28).UtcDateTime);
        File.SetLastWriteTimeUtc(current, noon.AddMinutes(-8).UtcDateTime);

        // Un subagente de esa sesión: suma a lo de hoy, no es una sesión aparte.
        dir.Write($"gemini/tmp/demo/chats/{Id}/sub-1.jsonl", string.Join("\n",
            $$"""{"sessionId":"sub-1","projectHash":"h1","startTime":"{{At(-20)}}","lastUpdated":"{{At(-19)}}","kind":"subagent"}""",
            $$"""{"id":"s-u","timestamp":"{{At(-20)}}","type":"user","content":"tarea"}""",
            $$"""{"id":"s-g","timestamp":"{{At(-19)}}","type":"gemini","content":"","tokens":{"input":400,"output":100,"cached":0,"thoughts":0,"tool":0,"total":500},"model":"gemini-3-flash"}"""));

        // De anteayer: es una sesión, pero no cuenta hoy.
        dir.Write("gemini/tmp/demo/chats/session-2026-10-27T10-00-ffff0000.jsonl", string.Join("\n",
            $$"""{"sessionId":"ffff0000","projectHash":"h1","startTime":"{{Iso(noon.AddDays(-2))}}","lastUpdated":"{{Iso(noon.AddDays(-2))}}"}""",
            $$$"""{"id":"g9","timestamp":"{{{Iso(noon.AddDays(-2))}}}","type":"gemini","content":"","tokens":{"input":9,"output":999,"cached":0,"total":1008}}"""));

        // Lo que no es un chat no se lee ni ocupa el lugar de las sesiones; un .jsonl sin una línea entendible no se entiende.
        dir.Write("gemini/tmp/demo/logs.json", "[]");
        dir.Write("gemini/tmp/demo/chats/session-2026-10-29T16-30-abcd1234.jsonl.unreadable-1", "{rota");
        dir.Write("gemini/tmp/demo/chats/session-rota.jsonl", "{rota");
        foreach (var file in Directory.EnumerateFiles(dir["gemini/tmp"], "*", SearchOption.AllDirectories).Except([old, current]))
            File.SetLastWriteTimeUtc(file, noon.AddHours(-1).UtcDateTime);
        File.SetLastWriteTimeUtc(dir["gemini/tmp/demo/logs.json"], noon.AddHours(1).UtcDateTime);

        var snapshot = Agents.Read(Agent(Agents.Gemini, dir["gemini"]), noon);
        Assert.Empty(snapshot.Limits);
        Assert.Equal(2, snapshot.Sessions.Count);
        var today = snapshot.Sessions[0];
        Assert.Equal(Id, today.Id);
        Assert.Equal("/home/x/demo", today.Folder); // por el nombre de su carpeta en tmp
        Assert.Equal("gemini-3-flash", today.Model);
        Assert.Equal(4, today.Messages); // u1, g1, u2, g2: el aviso no cuenta
        Assert.Equal(DateTimeOffset.Parse(At(-8), CultureInfo.InvariantCulture), today.LastActivity);

        Assert.Equal(1, snapshot.SessionsToday);
        Assert.Equal(4, snapshot.MessagesToday);
        Assert.Equal(3, snapshot.ResponsesToday); // g1 y g2, y la del subagente
        Assert.Equal(250 + 300 + 100, snapshot.TokensToday); // salida y razonamiento
        Assert.Equal(1, snapshot.Unreadable);
        Assert.Equal(L.T("Hoy: {0} · {1} respuestas · {2} tokens", L.T("1 sesión"), 3, "650"), Agents.Today(snapshot));
        Assert.True(Agents.ShowsInUsage(snapshot, noon));

        // La mascota mira la fecha del chat más nuevo, no la de logs.json (más nuevo todavía).
        Assert.Equal(noon.AddMinutes(-8), Agents.LastWrite(Agent(Agents.Gemini, dir["gemini"])));
    }

    [Fact]
    public void LaCarpetaDeGeminiSigueAGeminiCliHome()
    {
        var before = Environment.GetEnvironmentVariable("GEMINI_CLI_HOME");
        try
        {
            Environment.SetEnvironmentVariable("GEMINI_CLI_HOME", null);
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini"), Agents.DefaultFolder(Agents.Gemini));
            Environment.SetEnvironmentVariable("GEMINI_CLI_HOME", @"D:\otra");
            Assert.Equal(Path.Combine(@"D:\otra", ".gemini"), Agents.DefaultFolder(Agents.Gemini));
        }
        finally { Environment.SetEnvironmentVariable("GEMINI_CLI_HOME", before); }
    }

    [Fact]
    public void DeCopilotSoloSeCuentanSusEventos()
    {
        using var dir = new TempDir();
        var events = dir.Write("copilot/session-state/c-1/events.jsonl", "{\"a\":1}\n{\"b\":2}\nrota\n{\"c\":3}\n");
        File.SetLastWriteTimeUtc(events, Now.AddMinutes(-1).UtcDateTime);
        var snapshot = Agents.Read(Agent(Agents.Copilot, dir["copilot"]), Now);
        var session = Assert.Single(snapshot.Sessions);
        Assert.Equal("c-1", session.Id);
        Assert.Equal(3, session.Messages);
        Assert.Null(session.Model); // no se inventan campos
        Assert.Null(session.Folder);
        Assert.Equal(L.T("Hoy: {0} · {1} eventos", L.T("1 sesión"), 3), Agents.Today(snapshot));
    }

    [Fact]
    public void SeAgreganLosQueEstanYSeSiguenSoloLosElegidos()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir["codex"]);
        var settings = new AppSettings();
        string Folder(string kind) => dir[kind];
        Assert.True(Agents.Detect(settings, Folder));
        Assert.Equal([Agents.Codex], settings.Agents.Select(a => a.Kind));
        Assert.False(Agents.Detect(settings, Folder)); // ya está

        settings.Agents[0].Follow = false;
        Assert.False(Agents.Detect(settings, Folder)); // apagado no se vuelve a agregar
        Assert.Empty(Agents.ReadAll(settings.Agents, Now));

        // Una carpeta que ya no está se dice en Ajustes › Cuentas.
        var gone = Agent(Agents.Gemini, dir["no-existe"]);
        var read = Agents.Read(gone, Now);
        Assert.False(read.Exists);
        settings.Agents.Add(gone);
        var states = Agents.AgentStates(settings, [read]);
        Assert.Contains(L.T("{0} · no está en este equipo", dir["no-existe"]).Split(" · ")[1], states[1].Detail);
        Assert.False(states[0].Follow);
    }

    [Fact]
    public void AjustesCuentasMuestraLaMemoriaYLosAgentes()
    {
        var state = new SettingsState
        {
            Accounts = 2,
            AccountList = [new("principal", "Memoria propia"), new("trabajo", "Memoria compartida con «principal»")],
            AgentList = [new(Agents.Codex, "Codex CLI", true, "~/.codex · última sesión hace 5 min")],
        };
        var rows = SettingsPages.Build(state).Single(p => p.Id == "cuentas").Rows;
        Assert.Contains(rows, r => r is { Kind: RowKind.Info, Text: "trabajo", Value: "Memoria compartida con «principal»" });
        Assert.Contains(rows, r => r is { Kind: RowKind.Switch, Key: "agent:codex", On: true });
        Assert.Contains(rows, r => r is { Kind: RowKind.Link, Key: "add" });

        // Sin la lista (la bandeja de Windows), como antes.
        var plain = SettingsPages.Build(new SettingsState { Accounts = 1 }).Single(p => p.Id == "cuentas").Rows;
        Assert.Equal([RowKind.Link], plain.Select(r => r.Kind));
    }

    [Fact]
    public void LaMemoriaCompartidaEnlazaLaCarpetaProjects()
    {
        using var dir = new TempDir();
        var main = dir[".claude"];
        dir.Write(".claude/projects/demo/sesion.jsonl", "{}");
        var work = dir[".claude-trabajo"];
        Directory.CreateDirectory(work);
        dir.Write(".claude-trabajo/CLAUDE.md", "propio");

        Assert.True(AccountMemory.CanShare(work));
        AccountMemory.Share(work, main);
        Assert.True(File.Exists(Path.Combine(work, "projects", "demo", "sesion.jsonl")));
        Assert.Equal(Path.GetFullPath(main).TrimEnd('\\', '/'), Path.GetFullPath(AccountMemory.SharedWith(work)!).TrimEnd('\\', '/'));
        Assert.Null(AccountMemory.SharedWith(main));
        Assert.Equal("propio", File.ReadAllText(Path.Combine(work, "CLAUDE.md"))); // lo demás sigue siendo suyo

        var settings = new AppSettings { Accounts = [new() { Label = "principal", ConfigDir = main }, new() { Label = "trabajo", ConfigDir = work }] };
        var states = Agents.AccountStates(settings);
        Assert.Equal(L.T("Memoria propia"), states[0].Memory);
        Assert.Equal(L.T("Memoria compartida con «{0}»", "principal"), states[1].Memory);

        // Una cuenta que ya tiene su memoria no se enlaza (se perdería).
        Assert.False(AccountMemory.CanShare(main));
        Assert.Throws<IOException>(() => AccountMemory.Share(main, work));
        Directory.Delete(Path.Combine(work, "projects")); // el enlace, sin tocar lo de la otra cuenta
        Assert.True(File.Exists(Path.Combine(main, "projects", "demo", "sesion.jsonl")));
    }

    [Fact]
    public void En_Uso_solo_salen_los_que_hoy_hicieron_algo_o_tienen_un_limite_que_corre()
    {
        var now = new DateTimeOffset(2026, 10, 29, 21, 0, 0, TimeSpan.Zero);
        var copilot = new AgentAccount { Kind = Agents.Copilot, Label = "GitHub Copilot CLI", Folder = "x" };
        var codex = new AgentAccount { Kind = Agents.Codex, Label = "Codex CLI", Folder = "y" };
        // Sin actividad hoy y sin límites: no sale (ni el título, si no hay otro).
        Assert.False(Agents.ShowsInUsage(new AgentSnapshot(copilot, "GitHub Copilot CLI", true, [], [], 0, 0, 0), now));
        // Con actividad hoy, sí.
        Assert.True(Agents.ShowsInUsage(new AgentSnapshot(copilot, "GitHub Copilot CLI", true, [], [], 1, 3, 0), now));
        // Límites de hace días, ya reiniciados: no dicen nada y no sale.
        var stale = new AgentLimit("Semana", 32, now.AddDays(-20), 10080);
        Assert.False(Agents.ShowsInUsage(new AgentSnapshot(codex, "Codex CLI", true, [stale], [], 0, 0, 0), now));
        // Un límite que todavía corre: sale, solo con ese.
        var current = new AgentLimit("Sesión (5 h)", 5, now.AddHours(2), 300);
        var snapshot = new AgentSnapshot(codex, "Codex CLI", true, [stale, current], [], 0, 0, 0);
        Assert.True(Agents.ShowsInUsage(snapshot, now));
        Assert.Equal([current], Agents.CurrentLimits(snapshot, now));
        // Sin la carpeta en este equipo, nunca.
        Assert.False(Agents.ShowsInUsage(new AgentSnapshot(codex, "Codex CLI", false, [current], [], 2, 0, 0), now));
    }
}
