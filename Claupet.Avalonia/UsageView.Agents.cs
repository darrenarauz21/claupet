using Avalonia;
using Avalonia.Media;

namespace Claupet;

/// <summary>
/// Los otros agentes (Codex CLI, Gemini CLI, GitHub Copilot CLI) en Uso y en Actividad, debajo de lo de Claude Code. En
/// Uso, sus límites si los escribe (Codex) y lo de hoy; en Actividad, sus sesiones de las últimas 24 horas. Solo se ven
/// los que se siguen (Ajustes › Cuentas) y están en este equipo.
/// </summary>
sealed partial class UsageView
{
    IReadOnlyList<AgentSnapshot> _agents = [];

    public void SetAgents(IReadOnlyList<AgentSnapshot> agents)
    {
        _agents = agents.Where(a => a.Exists).ToList();
        if (_view is View.Usage or View.Activity) Relayout();
    }

    /// <summary>Un encabezado chico, como el de las inactivas.</summary>
    double AgentsCaption(DrawingContext? dc, double y)
    {
        var small = Gfx.Small.LineHeight;
        Gfx.Write(dc, L.T("Otros agentes"), Gfx.Small, _t.Tertiary, new Rect(Pad, y, Inner, small));
        if (dc is not null) Gfx.Fill(dc, _t.Border, new Rect(Pad, y + small + 6, Inner, 1), 0);
        return y + small + 16;
    }

    double ComposeAgentUsage(DrawingContext? dc, double y)
    {
        if (_agents.Count == 0) return y;
        var body = Gfx.Body.LineHeight;
        var small = Gfx.Small.LineHeight;
        y = AgentsCaption(dc, y);
        foreach (var agent in _agents)
        {
            var ago = Format.Ago(agent.LastActivity);
            var agoWidth = ago.Length == 0 ? 0 : Gfx.Measure(ago, Gfx.Small).Width;
            Gfx.Write(dc, agent.Account.Label, Gfx.BodyStrong, _t.Text, new Rect(Pad, y, Inner - agoWidth - 12, body));
            if (ago.Length > 0) Gfx.Write(dc, ago, Gfx.Small, _t.Tertiary, new Rect(ViewWidth - Pad - agoWidth, y, agoWidth + 1, body), center: true);
            y += body + 8;

            foreach (var limit in agent.Limits)
            {
                Gfx.Write(dc, limit.Label, Gfx.Body, _t.Text, new Rect(Pad, y, Inner - 60, body));
                Gfx.Write(dc, L.T("{0:0} %", limit.Percent), Gfx.BodyStrong, _t.Text, new Rect(ViewWidth - Pad - 60, y, 60, body), TextAlignment.Right);
                y += body + 8;
                if (dc is not null)
                {
                    var bar = new Rect(Pad, y, Inner, 6);
                    Gfx.Fill(dc, _t.Track, bar, 3);
                    var fill = bar.Width * Math.Clamp(limit.Percent, 0, 100) / 100;
                    if (fill > 0) Gfx.Fill(dc, _t.For(limit.Percent), new Rect(bar.X, bar.Y, Math.Max(fill, 6), 6), 3);
                }
                y += 6 + 8;
                if (limit.ResetsAt is { } resets)
                {
                    Gfx.Write(dc, Format.Reset(resets), Gfx.Small, _t.Tertiary, new Rect(Pad, y, Inner, small));
                    y += small;
                }
                y += 12;
            }

            var today = Agents.Today(agent);
            Gfx.Write(dc, today, Gfx.Small, _t.Secondary, new Rect(Pad, y, Inner, small));
            y += small + 4;
            if (agent.Limits.Count == 0)
            {
                var note = agent.Account.Kind == Agents.Codex
                    ? L.T("Sus límites salen cuando responde; todavía no hay ninguno escrito.")
                    : L.T("No escribe sus límites: se ve lo que hizo.");
                Gfx.Write(dc, note, Gfx.Small, _t.Tertiary, new Rect(Pad, y, Inner, small));
                y += small + 4;
            }
            y += 14;
        }
        return y;
    }

    double ComposeAgentActivity(DrawingContext? dc, double y)
    {
        if (_flowSession is not null) return y; // el detalle de una sesión de Claude ocupa la vista
        var since = DateTimeOffset.Now.AddHours(-24);
        var recent = _agents.SelectMany(a => a.Sessions.Select(s => (Agent: a, Session: s)))
            .Where(p => p.Session.LastActivity >= since).OrderByDescending(p => p.Session.LastActivity).Take(8).ToList();
        if (recent.Count == 0) return y;
        var body = Gfx.Body.LineHeight;
        var small = Gfx.Small.LineHeight;
        y = AgentsCaption(dc, y);
        foreach (var (agent, session) in recent)
        {
            var ago = Format.Ago(session.LastActivity);
            var agoWidth = Gfx.Measure(ago, Gfx.Small).Width;
            var title = session.Folder is { Length: > 0 } folder ? Path.GetFileName(folder.TrimEnd('/', '\\')) is { Length: > 0 } name ? name : folder
                : L.T("Sesión {0}", session.Id.Length > 8 ? session.Id[..8] : session.Id);
            Gfx.Write(dc, title, Gfx.Body, _t.Text, new Rect(Pad, y, Inner - agoWidth - 12, body));
            Gfx.Write(dc, ago, Gfx.Small, _t.Tertiary, new Rect(ViewWidth - Pad - agoWidth, y, agoWidth + 1, body), center: true);
            y += body + 2;
            var detail = session.Model is { Length: > 0 } model ? $"{agent.Account.Label} · {model}" : agent.Account.Label;
            Gfx.Write(dc, detail, Gfx.Small, _t.Tertiary, new Rect(Pad, y, Inner, small));
            y += small + 12;
        }
        var hint = L.T("Salen de sus archivos en este equipo; no gasta tokens.");
        var height = Gfx.MeasureWrapped(hint, Gfx.Small, Inner, 2);
        Gfx.Write(dc, hint, Gfx.Small, _t.Tertiary, new Rect(Pad, y, Inner, height), maxLines: 2);
        return y + height + 14;
    }
}
