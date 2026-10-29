using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using SD = System.Drawing;

namespace Claupet;

/// <summary>
/// Vista «Actividad», antes «Sesiones» y «Flujos» (igual que en Windows): un carril por sesión con sus pasos del turno,
/// y las inactivas plegadas al final; clic en un carril abre el detalle de esa sesión con su mapa. Mientras una sesión
/// trabaja siempre hay algo animado.
/// </summary>
sealed partial class UsageView
{
    readonly Flows.Arrivals _arrivals = new();
    string? _flowSession;
    /// <summary>Cuándo se copió el ID de la sesión (el botón dice «Copiado» un momento).</summary>
    long _copiedAt = -10_000;

    static double EaseOut(double p) => LaneNodes.EaseOut(p);

    static double Wave(int periodMs) => LaneNodes.Wave(periodMs);

    LaneColors LaneColors => new(_t.Background, _t.Border, _t.Text, _t.Secondary, _t.Tertiary, _t.Accent, _t.Warn, _t.Danger);

    double ComposeActivity(DrawingContext? dc, double y)
    {
        foreach (var key in _rowBounds.Keys.Where(k => !_sessions.Any(s => "flow:" + s.SessionId == k)).ToList())
            _rowBounds.Remove(key);
        if (_flowSession is { } id)
        {
            if (_sessions.FirstOrDefault(s => s.SessionId == id) is { } session) return ComposeFlowDetail(dc, session, y);
            _flowSession = null;
        }

        if (_sessions.Count == 0)
        {
            Gfx.Write(dc, L.T("No hay sesiones abiertas."), Gfx.Body, _t.Tertiary, new Rect(Pad, y, Inner, Gfx.Body.LineHeight));
            return y + Gfx.Body.LineHeight + 24;
        }

        var lanes = _sessions.Where(s => s.State != SessionState.Idle).ToList();
        var idle = _sessions.Where(s => s.State == SessionState.Idle).ToList();
        if (lanes.Count == 0)
        {
            Gfx.Write(dc, L.T("No hay sesiones activas."), Gfx.Body, _t.Tertiary, new Rect(Pad, y, Inner, Gfx.Body.LineHeight));
            y += Gfx.Body.LineHeight + 10;
        }
        foreach (var session in lanes)
            y = ComposeLane(dc, session, y) + 8;

        // Las inactivas, plegadas al final: cada una con su título y desde cuándo no hace nada.
        if (idle.Count > 0)
        {
            var toggleHeight = Gfx.Small.LineHeight + 12;
            var toggle = new Rect(Pad - 4, y, Inner + 8, toggleHeight);
            AddHit("idle", toggle, HitKind.IdleToggle);
            if (dc is not null)
            {
                if (_hoverKey == "idle") Gfx.Fill(dc, _t.Hover, toggle, 8);
                Gfx.Write(dc, L.T("Inactivas ({0})", idle.Count), Gfx.Small, _t.Tertiary, new Rect(Pad + 4, y, Inner - 8, toggleHeight), center: true);
                Gfx.Icon(dc, _showIdle ? Glyph.ChevronUp : Glyph.ChevronDown, _t.Tertiary, new Rect(Pad + Inner - 20, y, 16, toggleHeight), 13);
            }
            y += toggleHeight + 8;
            if (_showIdle) foreach (var session in idle) y = ComposeLane(dc, session, y) + 8;
        }

        var hint = L.T("Clic en una sesión: su detalle y su mapa. Sale de los archivos de Claude Code; no gasta tokens.");
        var height = Gfx.MeasureWrapped(hint, Gfx.Small, Inner, 3);
        Gfx.Write(dc, hint, Gfx.Small, _t.Tertiary, new Rect(Pad, y + 2, Inner, height), maxLines: 3);
        return y + height + 14;
    }

    double ComposeLane(DrawingContext? dc, SessionInfo s, double y)
    {
        var key = "flow:" + s.SessionId;
        var compact = s.State is SessionState.Done or SessionState.Idle;
        var top = y;
        var x0 = Pad + 4;
        var width = Inner - 8;
        var body = Gfx.Body.LineHeight;
        var small = Gfx.Small.LineHeight;

        if (dc is not null && _rowBounds.TryGetValue(key, out var previous))
        {
            if (!compact) Gfx.Fill(dc, _hoverKey == key ? _t.Hover : _t.Raised, previous, 10);
            else if (_hoverKey == key) Gfx.Fill(dc, _t.Hover, previous, 10);
            Gfx.Outline(dc, _t.Border, previous, 10);
        }

        y += 10;
        var tag = s.Account.Label;
        var tagWidth = Gfx.Measure(tag, Gfx.Small).Width;
        if (dc is not null)
        {
            DrawStateIcon(dc, s.State, new Rect(x0, y, 16, body));
            Gfx.Write(dc, s.DisplayTitle, Gfx.Body, compact ? _t.Secondary : _t.Text, new Rect(x0 + 22, y, width - 22 - tagWidth - 8, body));
            Gfx.Write(dc, tag, Gfx.Small, _t.Tertiary, new Rect(x0 + width - tagWidth, y, tagWidth + 1, body), center: true);
        }
        y += body + 2;

        var (status, color) = LaneStatus(s);
        Gfx.Write(dc, status, Gfx.Small, color, new Rect(x0 + 22, y, width - 22, small));
        y += small;

        if (!compact && (s.Flow.Count > 0 || Flows.Thinking(s)))
        {
            y += 12;
            y = ComposeLaneNodes(dc, s, x0, width, y);
        }

        var bounds = new Rect(Pad - 4, top, Inner + 8, y - top + 10);
        _rowBounds[key] = bounds;
        AddHit(key, bounds, HitKind.FlowSession, session: s);
        return bounds.Bottom;
    }

    (string Text, SD.Color Color) LaneStatus(SessionInfo s)
    {
        var parts = new List<string>();
        if (Path.GetFileName(s.Cwd.TrimEnd('\\', '/')) is { Length: > 0 } folder) parts.Add(folder);
        switch (s.State)
        {
            case SessionState.Waiting:
                parts.Add(s.AwaitingAnswer ? L.T("esperando su respuesta") : L.T("necesita su atención"));
                return (string.Join(" · ", parts), _t.Warn);
            case SessionState.Failed:
                parts.Add(L.T("falló: {0}", s.ErrorText ?? L.T("error de la API")));
                return (string.Join(" · ", parts), _t.Danger);
            case SessionState.Done:
                parts.Add(L.T("terminó {0}", Format.Ago(s.TurnEndedAt ?? s.StatusAt)));
                parts.Add(s.Steps == 1 ? L.T("1 paso") : L.T("{0} pasos", s.Steps));
                if (s.LastTurn is { } d) parts.Add(Format.Duration(d));
                return (string.Join(" · ", parts), _t.Tertiary);
            case SessionState.Idle:
                parts.Add(L.T("última actividad {0}", Format.Ago(s.TurnEndedAt ?? s.StatusAt)));
                return (string.Join(" · ", parts), _t.Tertiary);
            default:
                if (s.TodoTotal > 0) parts.Add(L.T("{0} de {1} tareas", s.TodoDone, s.TodoTotal));
                if (s.Model is { } model) parts.Add(Format.Model(model) + (s.Effort is { } e ? " " + e : ""));
                if (s.TurnStartedAt is { } start) parts.Add(Format.Duration(DateTimeOffset.Now - start));
                return (string.Join(" · ", parts), _t.Tertiary);
        }
    }

    double ComposeLaneNodes(DrawingContext? dc, SessionInfo s, double x0, double width, double y)
    {
        y = LaneNodes.Compose(dc, s, x0, width, y, LaneColors, _arrivals);
        var tiny = Gfx.Tiny.LineHeight;

        if (s.Flow.LastOrDefault(f => f.Kind == ToolKind.Agent) is { } agent)
        {
            y += 6;
            var text = $"↳ {agent.AgentType ?? L.T("subagente")} · " + (agent.Done
                ? agent.AgentSteps is { } n ? L.T("volvió · {0} pasos", n) : L.T("volvió")
                : L.T("trabajando…"));
            Gfx.Write(dc, text, Gfx.Tiny, agent.Done ? _t.Tertiary : _t.Accent, new Rect(x0 + 4, y, width, tiny));
            y += tiny;
        }
        return y;
    }

    // ── Detalle de una sesión: lo que es, su mapa y qué hacer con ella ──────────────────────

    double ComposeFlowDetail(DrawingContext? dc, SessionInfo s, double y)
    {
        var back = new Rect(Pad - 4, y, 28, 28);
        AddHit("flow-back", back, HitKind.FlowBack);
        var body = Gfx.Body.LineHeight;
        var small = Gfx.Small.LineHeight;
        var tiny = Gfx.Tiny.LineHeight;
        if (dc is not null)
        {
            Gfx.Fill(dc, _hoverKey == "flow-back" ? _t.Hover : _t.Raised, back, 8);
            Gfx.Icon(dc, Glyph.Back, _t.Text, back, 14);
            Gfx.Write(dc, s.DisplayTitle, Gfx.Body, _t.Text, new Rect(back.Right + 10, y - 2, Inner - 40, body));
            var (status, color) = StatusLine(s);
            Gfx.Write(dc, status, Gfx.Small, color, new Rect(back.Right + 10, y + body, Inner - 40, small));
        }
        y += Math.Max(28, body + small) + 14;

        // El pedido, la carpeta, el modelo y cuántas interacciones lleva.
        const double labelWidth = 84;
        foreach (var (label, value, lines) in Details(s))
        {
            var height = Gfx.MeasureWrapped(value, Gfx.Small, Inner - labelWidth, lines);
            Gfx.Write(dc, label, Gfx.Small, _t.Tertiary, new Rect(Pad, y, labelWidth, small));
            Gfx.Write(dc, value, Gfx.Small, _t.Secondary, new Rect(Pad + labelWidth, y, Inner - labelWidth, height), maxLines: lines);
            y += height + 5;
        }
        y += 14;

        var active = Flows.Active(s);
        var thinking = Flows.Thinking(s);
        var working = s.State is SessionState.Working or SessionState.Waiting;
        var agent = s.Flow.LastOrDefault(f => f.Kind == ToolKind.Agent);
        var mapHeight = agent is null ? 206.0 : 226.0;
        var top = y;
        var mid = top + 90;
        var account = new Rect(Pad, mid - 14, 64, 28);
        var session = new Rect(Pad + 76, mid - 28, 96, 56);
        const double toolWidth = 92, toolHeight = 26;
        var toolX = Pad + Inner - toolWidth;
        var step = (194 - toolHeight) / (Flows.MapKinds.Length - 1);
        var bend = (toolX - session.Right) / 2;
        var agentBox = new Rect(session.X, session.Bottom + 38, session.Width, 44);

        if (dc is not null)
        {
            dc.DrawLine(Gfx.P(_t.Border, 1.5), new Point(account.Right, mid), new Point(session.X, mid));
            for (var i = 0; i < Flows.MapKinds.Length; i++)
            {
                var kind = Flows.MapKinds[i];
                var ty = top + i * step + toolHeight / 2;
                var count = s.Flow.Count(f => f.Kind == kind);
                var lit = active?.Kind == kind;
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(new Point(session.Right, mid), false);
                    ctx.CubicBezierTo(new Point(session.Right + bend, mid), new Point(toolX - bend, ty), new Point(toolX, ty));
                    ctx.EndFigure(false);
                }
                IPen pen = lit
                    // Hacia la herramienta mientras la usa; de vuelta a la sesión mientras piensa.
                    ? new Pen(Gfx.B(_t.Accent), 2.5, new DashStyle([1, 1], (thinking ? 1 : -1) * Wave(700) * 2))
                    : Gfx.P(count > 0 ? Gfx.Blend(_t.Border, _t.Secondary, 0.35) : _t.Border, count > 0 ? 1.5 : 1);
                dc.DrawGeometry(null, pen, geometry);
            }
            if (agent is not null)
                dc.DrawLine(new Pen(Gfx.B(Gfx.Blend(_t.Border, _t.Secondary, 0.35)), 1.5, new DashStyle([1.3, 2], 0)),
                    new Point(session.Center.X, session.Bottom), new Point(session.Center.X, agentBox.Y));

            Gfx.Fill(dc, _t.Raised, account, 14);
            Gfx.Outline(dc, _t.Border, account, 14);
            Gfx.Write(dc, s.Account.Label, Gfx.Small, _t.Text, account, TextAlignment.Center, center: true);

            var (status, color) = s.State switch
            {
                SessionState.Working => (thinking || active is null ? L.T("Pensando") : active.Verb, _t.Accent),
                SessionState.Waiting => (L.T("Le espera a usted"), _t.Warn),
                SessionState.Failed => (L.T("Falló"), _t.Danger),
                SessionState.Done => (L.T("Terminó"), _t.Success),
                _ => (L.T("Inactiva"), _t.Tertiary),
            };
            if (working)
            {
                // Mientras piensa, el recuadro late con el mismo ritmo que el nodo «Pensando».
                var wave = thinking ? Flows.Heartbeat(Environment.TickCount64) : Wave(1400);
                var grow = (thinking ? 8 : 6) * wave;
                var alpha = thinking ? (int)(120 * wave) : (int)(70 * (1 - wave));
                Gfx.Fill(dc, SD.Color.FromArgb(alpha, color), session.Inflate(grow), 10 + grow);
            }
            Gfx.Fill(dc, _t.Raised, session, 10);
            Gfx.Outline(dc, working ? color : _t.Border, session, 10);
            var inner = session.Deflate(new Thickness(9, 6));
            Gfx.Write(dc, L.T("Sesión"), Gfx.Small, _t.Text, new Rect(inner.X, inner.Y, inner.Width, small));
            Gfx.Write(dc, status, Gfx.Tiny, color, new Rect(inner.X, inner.Y + small + 1, inner.Width, tiny));
            if (thinking)
                LaneNodes.DrawDots(dc, new Point(inner.X + Gfx.Measure(status, Gfx.Tiny).Width + 9, inner.Y + small + 1 + tiny * 0.62), color, Environment.TickCount64);
            Gfx.Write(dc, s.Flow.Count == 1 ? L.T("1 paso") : L.T("{0} pasos", s.Flow.Count), Gfx.Tiny, _t.Tertiary, new Rect(inner.X, inner.Y + small + tiny + 2, inner.Width, tiny));

            for (var i = 0; i < Flows.MapKinds.Length; i++)
            {
                var kind = Flows.MapKinds[i];
                var box = new Rect(toolX, top + i * step, toolWidth, toolHeight);
                var count = s.Flow.Count(f => f.Kind == kind);
                var lit = active?.Kind == kind;
                if (lit)
                {
                    var wave = Wave(1400);
                    Gfx.Fill(dc, SD.Color.FromArgb((int)(100 * (1 - wave)), _t.Accent), box.Inflate(6 * wave), 8 + 6 * wave);
                    Gfx.Fill(dc, _t.Accent, box, 8);
                }
                else Gfx.Outline(dc, count > 0 ? Gfx.Blend(_t.Border, _t.Secondary, 0.35) : _t.Border, box, 8);
                Gfx.Write(dc, Flows.KindLabel(kind), Gfx.Small, lit ? _t.Background : count > 0 ? _t.Text : _t.Tertiary,
                    new Rect(box.X + 9, box.Y, box.Width - 36, box.Height), center: true);
                Gfx.Write(dc, count.ToString(), Gfx.Small, lit ? _t.Background : _t.Secondary,
                    new Rect(box.Right - 30, box.Y, 21, box.Height), TextAlignment.Right, center: true);
            }

            if (agent is not null)
            {
                Gfx.Fill(dc, _t.Background, agentBox, 10);
                dc.DrawRectangle(null, new Pen(Gfx.B(Gfx.Blend(_t.Border, _t.Secondary, 0.35)), 1, new DashStyle([3, 2], 0)), agentBox.Deflate(0.5), 10, 10);
                var a = agentBox.Deflate(new Thickness(9, 5));
                Gfx.Write(dc, L.T("Subagente {0}", agent.AgentType ?? ""), Gfx.Small, _t.Secondary, new Rect(a.X, a.Y, a.Width, small));
                var state = agent.Done ? agent.AgentSteps is { } n ? L.T("volvió · {0} pasos", n) : L.T("volvió") : L.T("trabajando…");
                Gfx.Write(dc, state, Gfx.Tiny, agent.Done ? _t.Tertiary : _t.Accent, new Rect(a.X, a.Y + small + 2, a.Width, tiny));
            }
        }
        y = top + mapHeight;

        Gfx.Write(dc, L.T("ÚLTIMOS PASOS DEL TURNO"), Gfx.Small, _t.Tertiary, new Rect(Pad, y, Inner, small));
        y += small + 6;
        var last = s.Flow.TakeLast(5).ToList();
        if (last.Count == 0)
        {
            Gfx.Write(dc, L.T("Aún no hay pasos en este turno."), Gfx.Small, _t.Tertiary, new Rect(Pad, y, Inner, small));
            y += small + 4;
        }
        foreach (var f in last)
        {
            var isActive = working && f == active;
            if (dc is not null)
            {
                var p = EaseOut(_arrivals.Progress(f.Id));
                var time = f.At is { } at ? $"{at.LocalDateTime:HH:mm:ss}" : "";
                Gfx.Write(dc, time, Gfx.Small, Gfx.Blend(_t.Background, isActive ? _t.Accent : _t.Tertiary, p), new Rect(Pad, y, 62, small));
                var text = f.Detail is null ? f.Verb : $"{f.Verb} · {f.Detail}";
                if (isActive) text += thinking ? L.T("  · listo") : L.T("  · en curso");
                Gfx.Write(dc, text, Gfx.Small, Gfx.Blend(_t.Background, isActive ? _t.Text : _t.Secondary, p),
                    new Rect(Pad + 66 - 14 * (1 - p), y, Inner - 66, small));
            }
            y += small + 5;
        }
        return ComposeSessionActions(dc, s, y + 10) + 14;
    }

    /// <summary>Los botones del detalle, los mismos del menú del clic derecho.</summary>
    double ComposeSessionActions(DrawingContext? dc, SessionInfo s, double y)
    {
        var copied = Environment.TickCount64 - _copiedAt < 1500;
        (string Key, string Label)[] actions =
        [
            ("act:new", L.T("Nueva sesión aquí…")),
            ("act:folder", L.T("Abrir carpeta")),
            ("act:copy", copied ? L.T("Copiado") : L.T("Copiar ID")),
        ];
        const double height = 28;
        var x = Pad;
        foreach (var (key, label) in actions)
        {
            var width = Gfx.Measure(label, Gfx.Small).Width + 24;
            if (x + width > Pad + Inner && x > Pad) { x = Pad; y += height + 6; }
            var box = new Rect(x, y, width, height);
            AddHit(key, box, HitKind.SessionAction, session: s);
            if (dc is not null)
            {
                Gfx.Fill(dc, _hoverKey == key ? _t.Hover : _t.Raised, box, 8);
                Gfx.Outline(dc, _t.Border, box, 8);
                Gfx.Write(dc, label, Gfx.Small, _t.Text, box, TextAlignment.Center, center: true);
            }
            x += width + 6;
        }
        return y + height;
    }

    void RunSessionAction(string key, SessionInfo s)
    {
        switch (key)
        {
            case "act:new":
                NewSessionRequested?.Invoke(s);
                break;
            case "act:folder":
                Gfx.OpenFolder(s.Cwd);
                break;
            case "act:copy":
                _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(s.SessionId);
                _copiedAt = Environment.TickCount64;
                InvalidateVisual();
                // Vuelve a decir «Copiar ID» aunque no haya ninguna animación corriendo.
                DispatcherTimer.RunOnce(InvalidateVisual, TimeSpan.FromMilliseconds(1600));
                break;
        }
    }
}
