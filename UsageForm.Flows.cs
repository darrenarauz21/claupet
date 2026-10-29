using System.Drawing.Drawing2D;

namespace Claupet;

/// <summary>
/// Vista «Actividad» (antes «Sesiones» y «Flujos»): un carril por sesión con sus pasos del turno, y las inactivas
/// plegadas al final; clic en un carril abre el detalle de esa sesión con su mapa. Mientras una sesión trabaja
/// siempre hay algo animado: el paso en curso o «Pensando…», y cada paso nuevo entra creciendo. Todo sale del
/// .jsonl que ya se lee: no consulta la API.
/// </summary>
sealed partial class UsageForm
{
    readonly Flows.Arrivals _arrivals = new();
    string? _flowSession;
    /// <summary>Cuándo se copió el ID de la sesión (el botón dice «Copiado» un momento).</summary>
    long _copiedAt = -10_000;
    Font? _tiny;
    Font Tiny => _tiny ??= Fonts.Make("Segoe UI Variable Text", 7.75f, "Segoe UI");

    LaneNodes? _lanes;
    /// <summary>Los nodos de cada carril, los mismos que en la isla.</summary>
    LaneNodes Lanes => _lanes ??= new LaneNodes(_scale, _glyphSmall, _small, Tiny,
        (g, text, font, color, box) => Write(g, text, font, color, box, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis));

    LaneColors LanePalette => new(_t.Background, _t.Border, _t.Text, _t.Secondary, _t.Tertiary, _t.Accent, _t.Warn, _t.Danger);

    int ComposeActivity(Graphics g, bool draw, int y)
    {
        _rowBounds.Keys.Where(k => !_sessions.Any(s => "flow:" + s.SessionId == k)).ToList().ForEach(k => _rowBounds.Remove(k));
        if (_flowSession is { } id)
        {
            if (_sessions.FirstOrDefault(s => s.SessionId == id) is { } session) return ComposeFlowDetail(g, draw, session, y);
            _flowSession = null;
        }

        if (_sessions.Count == 0)
        {
            if (draw) Write(g, L.T("No hay sesiones abiertas."), _body, _t.Tertiary, new Rectangle(Pad, y, Inner, _body.Height));
            return y + _body.Height + S(24);
        }

        var lanes = _sessions.Where(s => s.State != SessionState.Idle).ToList();
        var idle = _sessions.Where(s => s.State == SessionState.Idle).ToList();
        if (lanes.Count == 0)
        {
            if (draw) Write(g, L.T("No hay sesiones activas."), _body, _t.Tertiary, new Rectangle(Pad, y, Inner, _body.Height));
            y += _body.Height + S(10);
        }
        foreach (var session in lanes)
            y = ComposeLane(g, draw, session, y) + S(8);

        // Las inactivas, plegadas al final: cada una con su título y desde cuándo no hace nada.
        if (idle.Count > 0)
        {
            var height = _small.Height + S(12);
            var toggle = new Rectangle(Pad - S(4), y, Inner + S(8), height);
            AddHit("idle", toggle, HitKind.IdleToggle);
            if (draw)
            {
                if (_hoverKey == "idle") Draw.Fill(g, _t.Hover, toggle, S(8));
                Write(g, L.T("Inactivas ({0})", idle.Count), _small, _t.Tertiary, new Rectangle(Pad + S(4), y, Inner - S(8), height), TextFormatFlags.VerticalCenter);
                Write(g, _showIdle ? GlyphChevronUp : GlyphChevronDown, _glyphSmall, _t.Tertiary,
                    new Rectangle(Pad + Inner - S(20), y, S(16), height), TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            }
            y += height + S(8);
            if (_showIdle)
                foreach (var session in idle)
                    y = ComposeLane(g, draw, session, y) + S(8);
        }

        var flags = TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
        var hint = L.T("Clic en una sesión: su detalle y su mapa. Sale de los archivos de Claude Code; no gasta tokens.");
        var hintHeight = TextRenderer.MeasureText(g, hint, _small, new Size(Inner, 0), flags).Height;
        if (draw) TextRenderer.DrawText(g, hint, _small, new Rectangle(Pad, y + S(2), Inner, hintHeight), _t.Tertiary, flags);
        return y + hintHeight + S(14);
    }

    int ComposeLane(Graphics g, bool draw, SessionInfo s, int y)
    {
        var key = "flow:" + s.SessionId;
        var compact = s.State is SessionState.Done or SessionState.Idle;
        var top = y;
        var x0 = Pad + S(4);
        var width = Inner - S(8);
        var bodyHeight = _body.Height;
        var smallHeight = _small.Height;

        if (draw && _rowBounds.TryGetValue(key, out var previous))
        {
            if (!compact) Draw.Fill(g, _hoverKey == key ? _t.Hover : _t.Raised, previous, S(10));
            else if (_hoverKey == key) Draw.Fill(g, _t.Hover, previous, S(10));
            Draw.Outline(g, _t.Border, previous, S(10));
        }

        y += S(10);
        var tag = s.Account.Label;
        var tagWidth = TextRenderer.MeasureText(g, tag, _small, Size.Empty, Line).Width;
        if (draw)
        {
            DrawStateIcon(g, s.State, new Rectangle(x0, y, S(16), bodyHeight));
            Write(g, s.DisplayTitle, _body, compact ? _t.Secondary : _t.Text,
                new Rectangle(x0 + S(22), y, width - S(22) - tagWidth - S(8), bodyHeight), TextFormatFlags.EndEllipsis);
            Write(g, tag, _small, _t.Tertiary, new Rectangle(x0 + width - tagWidth, y, tagWidth, bodyHeight), TextFormatFlags.VerticalCenter);
        }
        y += bodyHeight + S(2);

        var (status, color) = LaneStatus(s);
        if (draw) Write(g, status, _small, color, new Rectangle(x0 + S(22), y, width - S(22), smallHeight), TextFormatFlags.EndEllipsis);
        y += smallHeight;

        if (!compact && LaneNodes.Shows(s))
        {
            y += S(12);
            y = ComposeLaneNodes(g, draw, s, x0, width, y);
        }

        var bounds = new Rectangle(Pad - S(4), top, Inner + S(8), y - top + S(10));
        _rowBounds[key] = bounds;
        AddHit(key, bounds, HitKind.FlowSession, session: s);
        return bounds.Bottom;
    }

    (string Text, Color Color) LaneStatus(SessionInfo s)
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

    /// <summary>Los nodos del carril y, debajo, el último subagente del turno.</summary>
    int ComposeLaneNodes(Graphics g, bool draw, SessionInfo s, int x0, int width, int y)
    {
        y = Lanes.Compose(g, draw, s, x0, width, y, LanePalette, _arrivals);
        var tinyHeight = Tiny.Height;

        // Subagentes del turno: el último, con su estado.
        if (s.Flow.LastOrDefault(f => f.Kind == ToolKind.Agent) is { } agent)
        {
            y += S(6);
            var text = $"↳ {agent.AgentType ?? L.T("subagente")} · " + (agent.Done
                ? agent.AgentSteps is { } n ? L.T("volvió · {0} pasos", n) : L.T("volvió")
                : L.T("trabajando…"));
            if (draw) Write(g, text, Tiny, agent.Done ? _t.Tertiary : _t.Accent, new Rectangle(x0 + S(4), y, width, tinyHeight), TextFormatFlags.EndEllipsis);
            y += tinyHeight;
        }
        return y;
    }

    // ── Detalle de una sesión: lo que es, su mapa y qué hacer con ella ──────────────────────

    int ComposeFlowDetail(Graphics g, bool draw, SessionInfo s, int y)
    {
        var back = new Rectangle(Pad - S(4), y, S(28), S(28));
        AddHit("flow-back", back, HitKind.FlowBack);
        var bodyHeight = _body.Height;
        var smallHeight = _small.Height;
        if (draw)
        {
            Draw.Fill(g, _hoverKey == "flow-back" ? _t.Hover : _t.Raised, back, S(8));
            Write(g, GlyphBack, _glyphSmall, _t.Text, back, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Write(g, s.DisplayTitle, _body, _t.Text, new Rectangle(back.Right + S(10), y - S(2), Inner - S(40), bodyHeight), TextFormatFlags.EndEllipsis);
            var (status, color) = StatusLine(s);
            Write(g, status, _small, color, new Rectangle(back.Right + S(10), y + bodyHeight, Inner - S(40), smallHeight), TextFormatFlags.EndEllipsis);
        }
        y += Math.Max(S(28), bodyHeight + smallHeight) + S(14);

        // El pedido, la carpeta, el modelo y cuántas interacciones lleva.
        var labelWidth = S(84);
        foreach (var (label, value, lines) in Details(s))
        {
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis;
            var measured = TextRenderer.MeasureText(g, value, _small, new Size(Inner - labelWidth, 0), flags).Height;
            var height = Math.Min(measured, smallHeight * lines);
            if (draw)
            {
                Write(g, label, _small, _t.Tertiary, new Rectangle(Pad, y, labelWidth, smallHeight));
                TextRenderer.DrawText(g, value, _small, new Rectangle(Pad + labelWidth, y, Inner - labelWidth, height), _t.Secondary, flags);
            }
            y += height + S(5);
        }
        y += S(14);

        // Cuenta → sesión → herramientas; el subagente debajo de la sesión.
        var active = Flows.Active(s);
        var thinking = Flows.Thinking(s);
        var agent = s.Flow.LastOrDefault(f => f.Kind == ToolKind.Agent);
        var mapHeight = agent is null ? S(206) : S(226);
        var top = y;
        var mid = top + S(90);
        var account = new Rectangle(Pad, mid - S(14), S(64), S(28));
        var session = new Rectangle(Pad + S(76), mid - S(28), S(96), S(56));
        var toolWidth = S(92);
        var toolHeight = S(26);
        var toolX = Pad + Inner - toolWidth;
        var step = (S(194) - toolHeight) / (Flows.MapKinds.Length - 1);
        var bend = (toolX - session.Right) / 2;
        var agentBox = new Rectangle(session.X, session.Bottom + S(38), session.Width, S(44));
        var working = s.State is SessionState.Working or SessionState.Waiting;

        if (draw)
        {
            using (var pen = new Pen(_t.Border, 1.5f * _scale)) g.DrawLine(pen, account.Right, mid, session.X, mid);
            for (var i = 0; i < Flows.MapKinds.Length; i++)
            {
                var kind = Flows.MapKinds[i];
                var ty = top + i * step + toolHeight / 2;
                var count = s.Flow.Count(f => f.Kind == kind);
                var lit = active?.Kind == kind;
                using var path = new GraphicsPath();
                path.AddBezier(session.Right, mid, session.Right + bend, mid, toolX - bend, ty, toolX, ty);
                using var pen = new Pen(lit ? _t.Accent : count > 0 ? Blend(_t.Border, _t.Secondary, 0.35) : _t.Border,
                    lit ? 2.5f * _scale : count > 0 ? 1.5f * _scale : 1);
                if (lit)
                {
                    pen.DashPattern = [2.5f, 2.5f];
                    // Hacia la herramienta mientras la usa; de vuelta a la sesión mientras piensa.
                    var phase = Environment.TickCount64 % 700 / 700f * 5f;
                    pen.DashOffset = thinking ? phase : -phase;
                }
                g.DrawPath(pen, path);
            }
            if (agent is not null)
                using (var pen = new Pen(Blend(_t.Border, _t.Secondary, 0.35), 1.5f * _scale) { DashPattern = [2f, 3f] })
                    g.DrawLine(pen, session.X + session.Width / 2, session.Bottom, session.X + session.Width / 2, agentBox.Y);

            Draw.Fill(g, _t.Raised, account, account.Height / 2);
            Draw.Outline(g, _t.Border, account, account.Height / 2);
            Write(g, s.Account.Label, _small, _t.Text, account, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

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
                var wave = thinking ? Flows.Heartbeat(Environment.TickCount64) : Environment.TickCount64 % 1400 / 1400.0;
                var grow = (int)(S(thinking ? 8 : 6) * wave);
                var alpha = thinking ? (int)(120 * wave) : (int)(70 * (1 - wave));
                Draw.Fill(g, Color.FromArgb(alpha, color), Rectangle.Inflate(session, grow, grow), S(10) + grow);
            }
            Draw.Fill(g, _t.Raised, session, S(10));
            Draw.Outline(g, working ? color : _t.Border, session, S(10));
            var inner = Rectangle.Inflate(session, -S(9), -S(6));
            Write(g, L.T("Sesión"), _small, _t.Text, new Rectangle(inner.X, inner.Y, inner.Width, smallHeight));
            var statusBox = new Rectangle(inner.X, inner.Y + smallHeight + S(1), inner.Width, Tiny.Height);
            Write(g, status, Tiny, color, statusBox, TextFormatFlags.EndEllipsis);
            if (thinking)
            {
                var textWidth = TextRenderer.MeasureText(g, status, Tiny, Size.Empty, Line).Width;
                Lanes.DrawDots(g, new PointF(statusBox.X + textWidth + 9 * _scale, statusBox.Y + statusBox.Height * 0.62f), color, Environment.TickCount64);
            }
            var steps = s.Flow.Count == 1 ? L.T("1 paso") : L.T("{0} pasos", s.Flow.Count);
            Write(g, steps, Tiny, _t.Tertiary, new Rectangle(inner.X, inner.Y + smallHeight + Tiny.Height + S(2), inner.Width, Tiny.Height));

            for (var i = 0; i < Flows.MapKinds.Length; i++)
            {
                var kind = Flows.MapKinds[i];
                var box = new Rectangle(toolX, top + i * step, toolWidth, toolHeight);
                var count = s.Flow.Count(f => f.Kind == kind);
                var lit = active?.Kind == kind;
                if (lit)
                {
                    var wave = Environment.TickCount64 % 1400 / 1400.0;
                    var grow = (int)(S(6) * wave);
                    Draw.Fill(g, Color.FromArgb((int)(100 * (1 - wave)), _t.Accent), Rectangle.Inflate(box, grow, grow), S(8) + grow);
                    Draw.Fill(g, _t.Accent, box, S(8));
                }
                else Draw.Outline(g, count > 0 ? Blend(_t.Border, _t.Secondary, 0.35) : _t.Border, box, S(8));
                var textColor = lit ? _t.Background : count > 0 ? _t.Text : _t.Tertiary;
                Write(g, Flows.KindLabel(kind), _small, textColor, new Rectangle(box.X + S(9), box.Y, box.Width - S(36), box.Height), TextFormatFlags.VerticalCenter);
                Write(g, count.ToString(), _small, lit ? _t.Background : _t.Secondary, new Rectangle(box.Right - S(30), box.Y, S(21), box.Height),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            }

            if (agent is not null)
            {
                Draw.Fill(g, _t.Background, agentBox, S(10));
                using (var pen = new Pen(Blend(_t.Border, _t.Secondary, 0.35)) { DashPattern = [3f, 2f] })
                using (var path = Draw.Rounded(agentBox, S(10))) g.DrawPath(pen, path);
                var a = Rectangle.Inflate(agentBox, -S(9), -S(5));
                Write(g, L.T("Subagente {0}", agent.AgentType ?? ""), _small, _t.Secondary, new Rectangle(a.X, a.Y, a.Width, smallHeight), TextFormatFlags.EndEllipsis);
                var state = agent.Done ? agent.AgentSteps is { } n ? L.T("volvió · {0} pasos", n) : L.T("volvió") : L.T("trabajando…");
                Write(g, state, Tiny, agent.Done ? _t.Tertiary : _t.Accent, new Rectangle(a.X, a.Y + smallHeight + S(2), a.Width, Tiny.Height));
            }
        }
        y = top + mapHeight;

        // Los últimos pasos, con su hora; el más nuevo entra desde la izquierda.
        if (draw) Write(g, L.T("ÚLTIMOS PASOS DEL TURNO"), _small, _t.Tertiary, new Rectangle(Pad, y, Inner, smallHeight));
        y += smallHeight + S(6);
        var last = s.Flow.TakeLast(5).ToList();
        if (last.Count == 0)
        {
            if (draw) Write(g, L.T("Aún no hay pasos en este turno."), _small, _t.Tertiary, new Rectangle(Pad, y, Inner, smallHeight));
            y += smallHeight + S(4);
        }
        foreach (var f in last)
        {
            var isActive = working && f == active;
            if (draw)
            {
                var p = LaneNodes.EaseOut(_arrivals.Progress(f.Id));
                var shift = (int)(S(14) * (1 - p));
                var time = f.At is { } at ? $"{at.LocalDateTime:HH:mm:ss}" : "";
                Write(g, time, _small, Blend(_t.Background, isActive ? _t.Accent : _t.Tertiary, p), new Rectangle(Pad, y, S(62), smallHeight));
                var text = f.Detail is null ? f.Verb : $"{f.Verb} · {f.Detail}";
                if (isActive) text += "  · " + (thinking ? L.T("listo") : L.T("en curso"));
                Write(g, text, _small, Blend(_t.Background, isActive ? _t.Text : _t.Secondary, p),
                    new Rectangle(Pad + S(66) - shift, y, Inner - S(66), smallHeight), TextFormatFlags.EndEllipsis);
            }
            y += smallHeight + S(5);
        }
        return ComposeSessionActions(g, draw, s, y + S(10)) + S(14);
    }

    /// <summary>Los botones del detalle, los mismos del menú del clic derecho.</summary>
    int ComposeSessionActions(Graphics g, bool draw, SessionInfo s, int y)
    {
        var copied = Environment.TickCount64 - _copiedAt < 1500;
        (string Key, string Label)[] actions =
        [
            ("act:new", L.T("Nueva sesión aquí…")),
            ("act:folder", L.T("Abrir carpeta")),
            ("act:copy", copied ? L.T("Copiado") : L.T("Copiar ID")),
        ];
        var height = S(28);
        var x = Pad;
        foreach (var (key, label) in actions)
        {
            var width = TextRenderer.MeasureText(g, label, _small, Size.Empty, Line).Width + S(24);
            if (x + width > Pad + Inner && x > Pad) { x = Pad; y += height + S(6); }
            var box = new Rectangle(x, y, width, height);
            AddHit(key, box, HitKind.SessionAction, session: s);
            if (draw)
            {
                Draw.Fill(g, _hoverKey == key ? _t.Hover : _t.Raised, box, S(8));
                Draw.Outline(g, _t.Border, box, S(8));
                Write(g, label, _small, _t.Text, box, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            x += width + S(6);
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
                OpenFolder(s.Cwd);
                break;
            case "act:copy":
                Clipboard.SetText(s.SessionId);
                _copiedAt = Environment.TickCount64;
                Invalidate();
                // Vuelve a decir «Copiar ID» aunque no haya ninguna animación corriendo.
                _ = Task.Delay(1600).ContinueWith(_ => { if (IsHandleCreated) BeginInvoke(() => Invalidate()); });
                break;
        }
    }
}
