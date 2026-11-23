using Avalonia.Headless;

namespace Claupet.Tests;

/// <summary>Uso en la bandeja con otros agentes: una fila por agente, lo de hoy de Claude y el pie con todos.</summary>
public sealed class UsageAgentsViewTests
{
    static AgentSnapshot Snapshot(string kind, IReadOnlyList<AgentLimit> limits) =>
        new(new AgentAccount { Kind = kind, Label = Agents.Name(kind), Folder = "x" }, Agents.Name(kind), true, limits,
            [new AgentSession(kind, "s", null, null, DateTimeOffset.Now.AddMinutes(-12), 3)], 1, 3, 0);

    static UsageRecord R(string agent, string model, long output, double? cost = null, double? premium = null) =>
        new(agent, agent, "s", model, null, null, DateTimeOffset.Now, new UsageSums { Responses = 2, Output = output, Cost = cost, Premium = premium });

    [Fact]
    public void CadaAgenteEnUnaFilaYElPieSumaATodos() => Headless.Run(() =>
    {
        var view = new UsageView { Palette = Theme.DarkTheme };
        var window = new Avalonia.Controls.Window { Width = UsageView.ViewWidth, Height = 900, Content = view };
        window.Show();
        try
        {
            var account = new Account { Label = "principal", ConfigDir = "C:\\cuenta" };
            var local = new LocalTotals { Responses = 470, Output = 505_000 };
            local.OutputByModel["opus-5-5"] = 313_000;
            local.OutputByModel["sonnet-5-5"] = 192_000;
            var snapshot = new UsageSnapshot { Limits = [new LimitRow("session", "Sesión", 5, null, DateTimeOffset.Now.AddHours(4))], FetchedAt = DateTimeOffset.Now };
            view.Render([account], account.ConfigDir, new Dictionary<string, UsageSnapshot> { [account.ConfigDir] = snapshot }, new Dictionary<string, Profile>(), local, DateTime.Now);
            Assert.Equal("Hoy: 470 respuestas · 505 k tokens", view.FooterText); // sin otros agentes, como antes

            var now = DateTimeOffset.Now;
            var report = UsageReport.Build(
            [
                R(Agents.Codex, "gpt-5.5", 61_000),
                R(Agents.Gemini, "gemini-3-pro", 15_000), R(Agents.Gemini, "gemini-3-flash", 3_000),
                R(Agents.OpenCode, "claude-sonnet-5-5", 94, cost: 0.01),
                R(Agents.Copilot, "gpt-4.1", 245, premium: 0),
            ], UsagePeriod.Today, now.AddMinutes(1));
            view.SetAgents(
            [
                Snapshot(Agents.Codex, [new AgentLimit("Sesión (5 h)", 18, now.AddHours(2), 300), new AgentLimit("Semana", 41, now.AddDays(4), 10080)]),
                Snapshot(Agents.Gemini, []), Snapshot(Agents.OpenCode, []), Snapshot(Agents.Copilot, []),
            ], report);
            Headless.Frame(window);
            Assert.Contains("detail", view.HitKeys); // «Ver detalle» lleva a Ajustes › Consumo
            Assert.Equal("Todos hoy: 584.3 k tokens", view.FooterText); // los 505 k de Claude y los de los otros agentes
            if (Environment.GetEnvironmentVariable("CLAUPET_USO_SHOT") is { Length: > 0 } shot) window.CaptureRenderedFrame()?.Save(shot);
        }
        finally { window.Close(); }
    });
}
