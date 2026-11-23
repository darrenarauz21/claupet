using System.Text.Json;

namespace Claupet.Tests;

/// <summary>
/// Ajustes › Consumo: lo que lee de cada agente (con archivos de mentira, en el formato de cada uno), que siga donde quedó
/// sin contar dos veces, el informe con sus filtros y la página.
/// </summary>
public sealed class ConsumptionTests : IDisposable
{
    readonly TempDir _dir = new();
    /// <summary>Las 12 del día de hoy: así ninguna prueba cruza la medianoche.</summary>
    static readonly DateTimeOffset Noon = new(DateTime.Today.AddHours(12));
    static readonly DateTimeOffset Since = Noon.AddDays(-3);

    public void Dispose() => _dir.Dispose();

    static string Ts(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    UsageLedger Ledger() => new(_dir["consumo.json"]);

    UsageSources Only(string kind) => new([], [new AgentAccount { Kind = kind, Label = kind, Folder = _dir[kind] }]);

    void Lines(string relative, params object[] lines)
    {
        var file = _dir[relative];
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.AppendAllText(file, string.Concat(lines.Select(l => JsonSerializer.Serialize(l) + "\n")));
    }

    [Fact]
    public void ClaudeCodeCuentaCadaRespuestaUnaVezYSigueDondeQuedo()
    {
        object Answer(string id, int output, string model = "claude-opus-5-5") => new
        {
            type = "assistant", timestamp = Ts(Noon.AddHours(-1)), requestId = "req-" + id, cwd = @"C:\repos\tienda",
            message = new { id, model, usage = new { input_tokens = 10, cache_read_input_tokens = 100, cache_creation_input_tokens = 5, output_tokens = output } },
        };
        // Una respuesta en dos líneas (dos bloques, el mismo id) y un mensaje local (<synthetic>): cuenta una sola.
        Lines("claude/projects/tienda/s1.jsonl", Answer("m1", 50), Answer("m1", 50), Answer("m2", 7, "<synthetic>"));
        var sources = new UsageSources([("principal", _dir["claude"])], []);
        var ledger = Ledger();
        var record = Assert.Single(ledger.Refresh(sources, Since));
        Assert.Equal((UsageAgents.Claude, "principal", "claude-opus-5-5", "tienda", "s1", "Anthropic"), (record.Agent, record.Account, record.Model, record.Project, record.Session, record.Provider));
        Assert.Equal((1, 10L, 100L, 5L, 50L), (record.Sums.Responses, record.Sums.Input, record.Sums.CacheRead, record.Sums.CacheWrite, record.Sums.Output));
        ledger.Save();

        // Crece: se lee solo lo nuevo, también al abrir de nuevo (desde lo guardado). La otra línea de m1 no vuelve a contar.
        Lines("claude/projects/tienda/s1.jsonl", Answer("m1", 50), Answer("m3", 20));
        var again = Ledger().Refresh(sources, Since);
        Assert.Equal(2, again.Sum(r => r.Sums.Responses));
        Assert.Equal(70, again.Sum(r => r.Sums.Output));
    }

    [Fact]
    public void ElProyectoEsElRepositorioOLaCarpetaDelUsuario()
    {
        var home = _dir["casa"];
        Directory.CreateDirectory(_dir["casa/Documents/tienda/.git"]);
        Directory.CreateDirectory(_dir["casa/Documents/tienda/src/api"]);
        // Un worktree: .git es un archivo que apunta al repositorio principal.
        _dir.Write("casa/Documents/tienda-arreglo/.git", "gitdir: " + _dir["casa/Documents/tienda/.git/worktrees/tienda-arreglo"].Replace('\\', '/') + "\n");
        Directory.CreateDirectory(_dir["casa/Documents/reportes/sql/2026-11-13"]);
        Assert.Equal("tienda", UsageLedger.ProjectName(_dir["casa/Documents/tienda/src/api"], home));
        Assert.Equal("tienda", UsageLedger.ProjectName(_dir["casa/Documents/tienda-arreglo"], home));
        Assert.Equal("reportes", UsageLedger.ProjectName(_dir["casa/Documents/reportes/sql/2026-11-13"], home));
        Assert.Equal("tienda", UsageLedger.ProjectName(_dir["casa/Documents/tienda-sesion-20261121-110412"], home)); // un worktree de sesión ya borrado
        Assert.Equal("Temp", UsageLedger.ProjectName(_dir["casa/AppData/Local/Temp"], home));
    }

    [Fact]
    public void DosCuentasConLaMemoriaCompartidaSeLeenUnaVez()
    {
        Lines("claude/projects/p/s.jsonl", new { type = "assistant", timestamp = Ts(Noon), message = new { id = "a", model = "claude-sonnet-5-5", usage = new { input_tokens = 1, output_tokens = 2 } } });
        var sources = new UsageSources([("principal", _dir["claude"]), ("copia", _dir["claude"])], []);
        var record = Assert.Single(Ledger().Refresh(sources, Since));
        Assert.Equal("principal", record.Account);
    }

    [Fact]
    public void CodexCuentaCadaAumentoDeSusTotales()
    {
        object Count(int input, int cached, int output, int reasoning, int minutes) => new
        {
            timestamp = Ts(Noon.AddMinutes(minutes)), type = "event_msg",
            payload = new { type = "token_count", info = new { total_token_usage = new { input_tokens = input, cached_input_tokens = cached, cache_write_input_tokens = 0, output_tokens = output, reasoning_output_tokens = reasoning } } },
        };
        Lines("codex/sessions/2026/11/23/rollout-uno.jsonl",
            new { timestamp = Ts(Noon), type = "session_meta", payload = new { cwd = @"C:\repos\api", model_provider = "openai" } },
            new { timestamp = Ts(Noon), type = "turn_context", payload = new { model = "gpt-5.5" } },
            Count(1000, 800, 50, 10, 1),
            Count(1000, 800, 50, 10, 2), // el mismo total otra vez (llega con los límites): no es otra respuesta
            Count(1600, 1300, 80, 15, 3));
        var records = Ledger().Refresh(Only(Agents.Codex), Since);
        var sums = new UsageSums();
        foreach (var r in records) sums.Add(r.Sums);
        Assert.All(records, r => Assert.Equal(("gpt-5.5", "OpenAI", "api"), (r.Model, r.Provider, r.Project)));
        // La caché va dentro de su entrada: se separa. El razonamiento es parte de la salida.
        Assert.Equal((2, 300L, 1300L, 80L, 15L), (sums.Responses, sums.Input, sums.CacheRead, sums.Output, sums.Reasoning));
    }

    [Fact]
    public void CodexLeeLaHoraDeReinicioDeLasVersionesNuevas()
    {
        var resets = DateTimeOffset.UtcNow.AddHours(2);
        using var doc = JsonDocument.Parse($$"""{"used_percent": 18, "window_minutes": 300, "resets_at": {{resets.ToUnixTimeSeconds()}}}""");
        var limit = CodexReader.Limit(doc.RootElement, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow)!;
        Assert.Equal(resets.ToUnixTimeSeconds(), limit.ResetsAt!.Value.ToUnixTimeSeconds());
        Assert.Equal(18, limit.Percent);
    }

    [Fact]
    public void CopilotDaSusSolicitudesPremiumYNoDolares()
    {
        Lines("copilot/session-state/abc/events.jsonl",
            new { type = "session.start", timestamp = Ts(Noon), data = new { context = new { cwd = "C:/repos/web" } } },
            new { type = "session.usage_record", timestamp = Ts(Noon.AddMinutes(1)), data = new { usage = new { model = "gpt-4.1", inputTokens = 100, outputTokens = 20, cacheReadTokens = 40, cacheWriteTokens = 0, cost = 1.0, providerType = "openai" } } });
        var r = Assert.Single(Ledger().Refresh(Only(Agents.Copilot), Since));
        Assert.Equal(("gpt-4.1", "OpenAI", "web", "abc"), (r.Model, r.Provider, r.Project, r.Session));
        Assert.Equal((60L, 40L, 20L, 1.0), (r.Sums.Input, r.Sums.CacheRead, r.Sums.Output, r.Sums.Premium!.Value));
        Assert.Null(r.Sums.Cost);
    }

    [Fact]
    public void GeminiTomaElUltimoDeCadaMensajeYLaCopiaMasNueva()
    {
        object Answer(int output) => new { id = "g1", type = "gemini", timestamp = Ts(Noon), model = "gemini-3-pro", tokens = new { input = 500, output, cached = 300, thoughts = 10, tool = 0, total = 0 } };
        // La sesión vieja (.json) y su copia al retomarla (.jsonl), donde la respuesta se reescribió.
        var old = _dir.Write("gemini/tmp/miapp/chats/session-uno.json", JsonSerializer.Serialize(new { sessionId = "S1", messages = new[] { Answer(40) } }));
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddMinutes(-10));
        Lines("gemini/tmp/miapp/chats/session-uno.jsonl", new { sessionId = "S1", startTime = Ts(Noon) }, new { id = "u1", type = "user", timestamp = Ts(Noon) }, Answer(40), Answer(60));
        var r = Assert.Single(Ledger().Refresh(Only(Agents.Gemini), Since));
        Assert.Equal(("gemini-3-pro", "Google", "miapp"), (r.Model, r.Provider, r.Project));
        Assert.Equal((1, 200L, 300L, 70L, 10L), (r.Sums.Responses, r.Sums.Input, r.Sums.CacheRead, r.Sums.Output, r.Sums.Reasoning));
    }

    [Fact]
    public void OpenCodeDaSuCostoEnDolares()
    {
        var at = Noon.AddHours(-2);
        string Data(double cost) => JsonSerializer.Serialize(new
        {
            time = new { created = at.ToUnixTimeMilliseconds() }, model = new { id = "claude-sonnet-5-5", providerID = "anthropic" }, cost,
            tokens = new { input = 100, output = 30, reasoning = 5, cache = new { read = 50, write = 0 } },
        });
        OpenCodeTests.CreateDbV2(_dir["opencode/opencode.db"],
            [("ses_1", @"C:\repos\tienda", null, null, at), ("ses_sub", @"C:\repos\tienda", "ses_1", null, at)],
            [("ses_1", "user", at, "{}"), ("ses_1", "assistant", at, Data(0.12)), ("ses_sub", "assistant", at, Data(0.03))]);
        var records = Ledger().Refresh(Only(Agents.OpenCode), Since);
        var r = Assert.Single(records); // el subagente cuenta en la sesión que lo llamó
        Assert.Equal(("ses_1", "claude-sonnet-5-5", "Anthropic", "tienda"), (r.Session, r.Model, r.Provider, r.Project));
        Assert.Equal((2, 200L, 100L, 70L, 10L), (r.Sums.Responses, r.Sums.Input, r.Sums.CacheRead, r.Sums.Output, r.Sums.Reasoning));
        Assert.Equal(0.15, r.Sums.Cost!.Value, 6);
    }

    static UsageRecord R(string agent, string account, string model, DateTimeOffset at, long output, string? session = null, string? project = null,
        double? cost = null, double? premium = null) =>
        new(agent, account, session, model, null, project, at, new UsageSums { Responses = 1, Output = output, Input = 10, Cost = cost, Premium = premium });

    static List<UsageRecord> Sample() =>
    [
        R(UsageAgents.Claude, "principal", "claude-opus-5-5", Noon.AddHours(-1), 100, "a", "p1"),
        R(UsageAgents.Claude, "personal", "claude-sonnet-5-5", Noon.AddHours(-1), 50, "b", "p2"),
        R(Agents.Codex, "Codex", "gpt-5.5", Noon.AddDays(-3), 30, "c", "p1"),
        R(Agents.Copilot, "Copilot", "gpt-4.1", Noon.AddHours(-2), 5, "d", premium: 2),
        R(Agents.OpenCode, "OpenCode", "deepseek", Noon.AddHours(-3), 7, "e", cost: 0.5),
    ];

    [Fact]
    public void ElInformeSumaElPeriodoConSusFiltros()
    {
        var today = UsageReport.Build(Sample(), UsagePeriod.Today, Noon);
        Assert.Equal(24, today.Slots.Count);
        Assert.Equal(162, today.Totals.Output); // sin lo de Codex, de hace tres días
        Assert.Equal(4, today.Sessions);
        Assert.Equal(["claude-opus-5-5", "claude-sonnet-5-5", "deepseek", "gpt-4.1"], today.Models.Select(m => m.Model));
        Assert.True(today.HasCost && today.HasPremium);
        Assert.Equal((0.5, 2.0), (today.Totals.Cost!.Value, today.Totals.Premium!.Value));
        Assert.Equal(150, today.ByAgentSlot[UsageAgents.Claude][11].Output); // las 11:00
        Assert.Equal(UsageAgents.Claude, today.Agents[0].Agent);

        var week = UsageReport.Build(Sample(), UsagePeriod.Days7, Noon);
        Assert.Equal(7, week.Slots.Count);
        Assert.Equal(192, week.Totals.Output);
        Assert.Equal(5, week.Heat.Cast<int>().Sum()); // las respuestas de los últimos 7 días, por día y hora

        // Apagar un agente lo saca de todo; elegir una cuenta de Claude deja a los demás agentes.
        var noClaude = UsageReport.Build(Sample(), UsagePeriod.Days7, Noon, new HashSet<string> { UsageAgents.Claude });
        Assert.Equal(42, noClaude.Totals.Output);
        Assert.Contains(UsageAgents.Claude, noClaude.AgentsSeen); // sigue en los filtros, para volver a encenderlo
        var principal = UsageReport.Build(Sample(), UsagePeriod.Days7, Noon, claudeAccount: "principal");
        Assert.Equal(142, principal.Totals.Output);

        Assert.Equal(Noon.Day, UsageReport.Build(Sample(), UsagePeriod.Month, Noon).Slots.Count);
        Assert.Equal(30, UsageReport.Build(Sample(), UsagePeriod.Days30, Noon).Slots.Count);
    }

    [Fact]
    public void ElCsvTieneUnaFilaPorModelo()
    {
        var csv = UsageCsv.Of(UsageReport.Build(Sample(), UsagePeriod.Days7, Noon)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("agente,modelo,proveedor,respuestas", csv[0]);
        Assert.Equal(6, csv.Length);
        Assert.Contains(csv, l => l.StartsWith("OpenCode,deepseek,") && l.Contains(",0.5,"));
        Assert.Contains(csv, l => l.StartsWith("Copilot CLI,gpt-4.1,") && l.EndsWith(",2"));
    }

    [Fact]
    public void LaPaginaSeArmaConYSinDatosYAngosta() => Headless.Run(() =>
    {
        var pane = new ConsumptionPane(Ink.For(true), read: false);
        var window = new Avalonia.Controls.Window { Width = 1300, Height = 900, Content = new Avalonia.Controls.ScrollViewer { Content = pane } };
        window.Show();
        try
        {
            pane.Show([], []);
            Headless.Frame(window);
            Assert.Equal(0, pane.Report!.Totals.Responses);

            pane.Show(Sample(), [new UsageLimit(Agents.Codex, "Codex", "Semana", 41, DateTimeOffset.Now.AddDays(2))]);
            Headless.Frame(window);
            Assert.Equal(192, pane.Report!.Totals.Output); // 7 días, lo de siempre al abrir

            window.Width = 760; // en una columna
            Headless.Frame(window);
            Assert.Equal(192, pane.Report!.Totals.Output);
        }
        finally { window.Close(); }
    });
}
