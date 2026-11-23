using Avalonia;
using Avalonia.Media;
using SD = System.Drawing;

namespace Claupet;

/// <summary>
/// Los otros agentes (Codex, Gemini CLI, OpenCode, Copilot CLI) en Uso y en Actividad, debajo de lo de Claude Code.
/// En Uso, primero lo de hoy de Claude Code en este equipo con la franja de sus modelos; después, los agentes que hoy
/// hicieron algo o tienen un límite que todavía corre (Agents.ShowsInUsage), uno por fila: sus límites en miniatura (o la
/// franja de sus modelos y su costo, si lo escriben), los tokens de hoy y una línea con el modelo principal, las
/// respuestas y cuándo reinicia o cuándo trabajó. En el pie, lo de hoy de todos y «Ver detalle» (Ajustes › Consumo). En
/// Actividad, sus sesiones de las últimas 24 horas. Solo se ven los que se siguen (Ajustes › Cuentas) y están en este equipo.
/// </summary>
sealed partial class UsageView
{
    IReadOnlyList<AgentSnapshot> _agents = [];
    /// <summary>Lo de hoy de los otros agentes por modelo (UsageLedger); null si todavía no se leyó.</summary>
    UsageReport? _today;

    public void SetAgents(IReadOnlyList<AgentSnapshot> agents, UsageReport? today = null)
    {
        _agents = agents.Where(a => a.Exists).ToList();
        _today = today ?? _today;
        if (_view is View.Usage or View.Activity) Relayout();
    }

    /// <summary>El pie: lo de hoy de Claude Code y, si hoy trabajó otro agente, la suma de todos.</summary>
    string TodayText()
    {
        var others = _today?.Agents.Where(a => a.Agent != UsageAgents.Claude && a.Sums.Responses > 0).ToList() ?? [];
        if (others.Count == 0) return Format.Local(_local);
        // Corto, para que quepa junto a «Ver detalle»: las respuestas de cada uno ya van en su fila.
        var output = (_local?.Output ?? 0) + others.Sum(a => a.Sums.Output);
        return L.T("Todos hoy: {0} tokens", Format.Tokens(output));
    }

    static SD.Color AgentColor(string agent) => SD.ColorTranslator.FromHtml(UsageAgents.Color(agent));

    /// <summary>Un color por modelo: el del agente, más claro u oscuro según su lugar.</summary>
    static SD.Color Shade(SD.Color c, int i)
    {
        if (i == 0) return c;
        var toward = i % 2 == 1 ? 255 : 0;
        var amount = Math.Min(0.6, 0.22 * ((i + 1) / 2));
        int Mix(int from) => (int)Math.Round(from + (toward - from) * amount);
        return SD.Color.FromArgb(Mix(c.R), Mix(c.G), Mix(c.B));
    }

    /// <summary>Una franja con las partes de cada modelo, de izquierda a derecha.</summary>
    void Mix(DrawingContext dc, Rect bar, IReadOnlyList<(double Value, SD.Color Color)> parts)
    {
        Gfx.Fill(dc, _t.Track, bar, bar.Height / 2);
        var sum = parts.Sum(p => p.Value);
        if (sum <= 0) return;
        using (dc.PushClip(new RoundedRect(bar, bar.Height / 2)))
        {
            var x = bar.X;
            foreach (var (value, color) in parts)
            {
                var width = value / sum * bar.Width;
                dc.FillRectangle(Gfx.B(color), new Rect(x, bar.Y, width, bar.Height));
                x += width;
            }
        }
    }

    /// <summary>
    /// Lo de hoy de Claude Code en este equipo, debajo de sus límites: respuestas y tokens, y la franja de sus modelos con
    /// cuánto de la salida fue de cada uno (Activity ya lo cuenta para el pie).
    /// </summary>
    double ComposeClaudeToday(DrawingContext? dc, double y)
    {
        if (_local is not { Responses: > 0 } local || local.Output <= 0) return y;
        var small = Gfx.Small.LineHeight;
        var models = local.OutputByModel.Where(m => m.Value > 0).OrderByDescending(m => m.Value).ToList();
        var line = L.T("Hoy: {0} resp. · {1} tokens", local.Responses, Format.Tokens(local.Output));
        Gfx.Write(dc, line, Gfx.Small, _t.Secondary, new Rect(Pad, y, Inner - 96, small));
        if (dc is not null && models.Count > 0)
            Mix(dc, new Rect(ViewWidth - Pad - 86, y + small / 2 - 2.5, 86, 5),
                [.. models.Select((m, i) => ((double)m.Value, Shade(AgentColor(UsageAgents.Claude), i)))]);
        y += small + 3;
        var shares = string.Join(" · ", models.Take(3).Select(m => $"{ClaudeModel(m.Key)} {100.0 * m.Value / local.Output:0} %"));
        if (shares.Length > 0)
        {
            Gfx.Write(dc, shares, Gfx.Small, _t.Tertiary, new Rect(Pad, y, Inner, small));
            y += small;
        }
        return y + 16;
    }

    /// <summary>El nombre de un modelo de Claude como en el resto de la app («opus-5-5» → «Opus 5.5»).</summary>
    static string ClaudeModel(string key) => UsageAgents.ModelName(UsageAgents.Claude, key.StartsWith("claude-", StringComparison.Ordinal) ? key : "claude-" + key);

    /// <summary>Un encabezado chico, como el de las inactivas; a la derecha, lo que dice la columna de los números.</summary>
    double AgentsCaption(DrawingContext? dc, double y, string? right = null)
    {
        var small = Gfx.Small.LineHeight;
        Gfx.Write(dc, L.T("Otros agentes"), Gfx.Small, _t.Tertiary, new Rect(Pad, y, Inner, small));
        if (right is not null) Gfx.Write(dc, right, Gfx.Small, _t.Tertiary, new Rect(Pad, y, Inner, small), TextAlignment.Right);
        if (dc is not null) Gfx.Fill(dc, _t.Border, new Rect(Pad, y + small + 6, Inner, 1), 0);
        return y + small + 16;
    }

    /// <summary>«Sesión (5 h)» → «5 h», «Semana» → «sem.»: lo que cabe junto a una barra chica.</summary>
    static string ShortLimit(AgentLimit limit) => limit.WindowMinutes switch
    {
        >= 295 and <= 305 => L.T("5 h"),
        >= 10070 and <= 10090 => L.T("sem."),
        _ => limit.Label,
    };

    double ComposeAgentUsage(DrawingContext? dc, double y)
    {
        y = ComposeClaudeToday(dc, y);
        var now = DateTimeOffset.Now;
        var shown = _agents.Where(a => Agents.ShowsInUsage(a, now)).ToList();
        if (shown.Count == 0) return y;
        var body = Gfx.Body.LineHeight;
        var small = Gfx.Small.LineHeight;
        y = AgentsCaption(dc, y, L.T("tokens hoy"));
        const double nameWidth = 88, numberWidth = 44;
        var middleX = Pad + nameWidth;
        var middleWidth = Inner - nameWidth - numberWidth - 8;
        foreach (var agent in shown)
        {
            var kind = agent.Account.Kind;
            var limits = Agents.CurrentLimits(agent, now);
            var row = _today?.Agents.FirstOrDefault(a => a.Agent == kind);
            var models = _today?.Models.Where(m => m.Agent == kind && m.Sums.Output > 0).ToList() ?? [];
            var color = AgentColor(kind);

            // Primera línea: el agente, sus límites (o la franja de sus modelos y su costo) y los tokens de hoy.
            if (dc is not null) Gfx.Fill(dc, color, new Rect(Pad, y + body / 2 - 3.5, 7, 7), 3.5);
            Gfx.Write(dc, UsageAgents.Name(kind), Gfx.BodyStrong, _t.Text, new Rect(Pad + 13, y, nameWidth - 15, body));
            var tokens = row is { Sums.Output: > 0 } ? Format.Tokens(row.Sums.Output) : row is { Sums.Responses: > 0 } ? "0" : "—";
            Gfx.Write(dc, tokens, Gfx.Body, row is { Sums.Responses: > 0 } ? _t.Text : _t.Tertiary,
                new Rect(ViewWidth - Pad - numberWidth, y, numberWidth, body), TextAlignment.Right);
            if (dc is not null)
            {
                var x = middleX;
                if (limits.Count > 0)
                    foreach (var limit in limits.Take(2))
                    {
                        var label = ShortLimit(limit);
                        var labelWidth = Gfx.Measure(label, Gfx.Small).Width;
                        var percent = L.T("{0:0} %", limit.Percent);
                        var percentWidth = Gfx.Measure(percent, Gfx.Small).Width;
                        var barWidth = Math.Max(16, Math.Min(36, middleWidth / 2 - labelWidth - percentWidth - 14));
                        Gfx.Write(dc, label, Gfx.Small, _t.Secondary, new Rect(x, y, labelWidth + 1, body), center: true);
                        x += labelWidth + 4;
                        var bar = new Rect(x, y + body / 2 - 2.5, barWidth, 5);
                        Gfx.Fill(dc, _t.Track, bar, 2.5);
                        var fill = bar.Width * Math.Clamp(limit.Percent, 0, 100) / 100;
                        if (fill > 0) Gfx.Fill(dc, _t.For(limit.Percent), new Rect(bar.X, bar.Y, Math.Max(fill, 5), 5), 2.5);
                        x += barWidth + 4;
                        Gfx.Write(dc, percent, Gfx.Small, _t.Text, new Rect(x, y, percentWidth + 1, body), center: true);
                        x += percentWidth + 10;
                    }
                else
                {
                    var cost = row?.Sums.Cost is { } usd ? L.T("US$ {0:0.00}", usd) : row?.Sums.Premium is { } premium ? L.T("{0:0.##} premium", premium) : null;
                    var costWidth = cost is null ? 0 : Gfx.Measure(cost, Gfx.Small).Width + 8;
                    if (models.Count > 0)
                    {
                        var barWidth = Math.Min(90, middleWidth - costWidth - 4);
                        Mix(dc, new Rect(x, y + body / 2 - 2.5, barWidth, 5), [.. models.Select((m, i) => ((double)m.Sums.Output, Shade(color, i)))]);
                        x += barWidth + 8;
                    }
                    if (cost is not null) Gfx.Write(dc, cost, Gfx.Small, _t.Secondary, new Rect(x, y, costWidth, body), center: true);
                }
            }
            y += body + 2;

            // Segunda: el modelo principal, las respuestas y cuándo reinicia su límite o cuándo trabajó.
            var parts = new List<string>();
            if (models.Count > 0)
            {
                var top = models[0];
                var share = row is { Sums.Output: > 0 } && models.Count > 1 ? $" {100.0 * top.Sums.Output / row.Sums.Output:0} %" : "";
                parts.Add(UsageAgents.ModelName(kind, top.Model) + share);
            }
            parts.Add(row is { Sums.Responses: > 0 } ? L.T("{0} resp.", row.Sums.Responses) : Agents.Today(agent));
            if (limits.FirstOrDefault(l => l.ResetsAt is not null) is { ResetsAt: { } resets }) parts.Add(Format.Reset(resets));
            else if (Format.Ago(agent.LastActivity) is { Length: > 0 } ago) parts.Add(ago);
            if (limits.Count == 0 && kind != Agents.Codex && models.Count == 0) parts.Add(L.T("no escribe sus límites"));
            Gfx.Write(dc, string.Join(" · ", parts), Gfx.Small, _t.Tertiary, new Rect(Pad + 13, y, Inner - 13, small));
            y += small + 12;
        }
        return y + 2;
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
