using System.Globalization;

namespace Claupet.Tests;

/// <summary>
/// Los otros agentes, con archivos sintéticos armados con los formatos que documenta la comunidad (no copiados de un
/// equipo real): Codex CLI, Gemini CLI y GitHub Copilot CLI. Y la memoria compartida de una cuenta de Claude.
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
}
