using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using SD = System.Drawing;

namespace Claupet;

/// <summary>
/// El dibujo de la isla: la compacta y, abierta, la barra de pestañas y la página (Actividad, Chat, el análisis de un
/// archivo, un permiso o una pregunta, el diff de un archivo, el resumen de la semana). Un mismo recorrido sirve para
/// medir el alto (sin contexto) y para dibujar, y deja las zonas clicables en _hits.
/// </summary>
sealed partial class IslandWindow
{
    /// <summary>Lo más alto que crece la conversación; lo demás se desplaza.</summary>
    const double ChatViewHeight = 300;

    static readonly string[] SpinnerFrames = ["·", "✢", "✳", "✶", "✻", "✽", "✻", "✶", "✳", "✢"];

    void Paint(DrawingContext dc)
    {
        var w = _w;
        var h = _h;
        var x = (_winW - w) / 2;
        var open = _shape == Shape.Open || (_toW >= OpenW - 1 && AnimProgress < 1);
        var radius = (open ? 22 : 18) * Math.Min(1, h / 36);
        DrawShadow(dc, x, _y, w, h, radius);
        var shape = IslandGeometry(x, _y, w, h, radius);
        dc.DrawGeometry(Gfx.B(Ground), null, shape);
        _hits.Clear();
        var inputBefore = _inputBounds;
        _inputBounds = default;
        var bounds = new Rect(x, _y, w, h);

        // El contenido aparece cuando el tamaño ya casi llegó; al cambiar de página, la barra queda y lo de abajo se funde.
        var (barAlpha, bodyAlpha) = ContentAlpha(open);
        using (dc.PushGeometryClip(shape))
        {
            if (open)
            {
                double y;
                if (barAlpha > 0.01)
                    using (dc.PushOpacity(barAlpha)) y = ComposeTopBar(dc, bounds);
                else y = ComposeTopBar(null, bounds);
                if (bodyAlpha > 0.01)
                    using (dc.PushOpacity(bodyAlpha)) ComposePage(dc, bounds, y);
            }
            else if (barAlpha > 0.01)
                using (dc.PushOpacity(barAlpha)) DrawCompact(dc, bounds);
        }
        // El campo de texto va donde quedó el dibujado: se acomoda después de dibujar.
        if (_inputBounds != inputBefore) Dispatcher.UIThread.Post(PlaceInput);
    }

    static Geometry IslandGeometry(double x, double y, double w, double h, double r)
    {
        r = Math.Max(1, Math.Min(r, Math.Min(w / 2, h)));
        var geometry = new StreamGeometry();
        using var c = geometry.Open();
        c.BeginFigure(new Point(x, y), true);
        c.LineTo(new Point(x + w, y));
        c.LineTo(new Point(x + w, y + h - r));
        c.ArcTo(new Point(x + w - r, y + h), new Size(r, r), 0, false, SweepDirection.Clockwise);
        c.LineTo(new Point(x + r, y + h));
        c.ArcTo(new Point(x, y + h - r), new Size(r, r), 0, false, SweepDirection.Clockwise);
        c.EndFigure(true);
        return geometry;
    }

    /// <summary>Una sombra suave bajo la isla: capas cada vez más anchas y más tenues.</summary>
    static void DrawShadow(DrawingContext dc, double x, double y, double w, double h, double radius)
    {
        const double m = ShadowMargin;
        for (var i = m; i >= 1; i -= Math.Max(1, m / 7))
        {
            var a = (int)(26 * (1 - i / (m + 1)));
            dc.DrawGeometry(Gfx.B(SD.Color.FromArgb(a, 0, 0, 0)), null, IslandGeometry(x - i, y, w + 2 * i, h + i * 0.9, radius + i));
        }
    }

    // ── Compacta ────────────────────────────────────────────────────────────────────────────

    void DrawCompact(DrawingContext dc, Rect b)
    {
        const double head = 32;
        DrawHead(dc, new Rect(b.X + 6, b.Y + (b.Height - head) / 2, head, head));
        var x = b.X + 6 + head + 8;
        var right = b.Right - 10;

        if (_sessionPercent is { } percent)
        {
            var label = L.T("{0:0} %", percent);
            var w = Gfx.Measure(label, SmallStrong).Width + 18;
            var pill = new Rect(right - w, b.Y + (b.Height - 22) / 2, w, 22);
            Gfx.Fill(dc, SD.Color.FromArgb(46, _accent), pill, pill.Height / 2);
            Gfx.Write(dc, label, SmallStrong, Lighten(_accent), pill, TextAlignment.Center, center: true);
            right = pill.X - 8;
        }
        if (_sessions.Any(s => s.State == SessionState.Waiting) || _choiceTask is not null)
        {
            const double r = 4;
            Pulse(dc, new Point(right - r, b.Y + b.Height / 2), r, Warn);
            right -= 2 * r + 8;
        }
        Gfx.Write(dc, CompactStatus(), Small, Soft, new Rect(x, b.Y, Math.Max(1, right - x), b.Height), center: true);
        Hit(b, "compact");
    }

    string CompactStatus()
    {
        if (_approval is { } pending) return pending.IsQuestion ? L.T("Una pregunta le espera · clic para verla") : L.T("Un permiso le espera · clic para verlo");
        if (_choiceTask is not null) return L.T("Claupet le pregunta algo · clic para verlo");
        if (_receiving) return L.T("Recibiendo archivo…");
        if (Analyzing) return L.T("Analizando {0}…", Path.GetFileName(_file));
        if (_asking) return L.T("Respondiendo…");
        if (_file is not null && !_analysisSeen) return _analysisError is null ? L.T("Análisis listo · clic para ver") : L.T("No pude analizarlo · clic para ver");
        var working = _sessions.Count(s => s.State == SessionState.Working);
        var waiting = _sessions.Count(s => s.State == SessionState.Waiting);
        var parts = new List<string>();
        if (working > 0) parts.Add(working == 1 ? L.T("1 trabajando") : L.T("{0} trabajando", working));
        if (waiting > 0) parts.Add(waiting == 1 ? L.T("1 le espera") : L.T("{0} le esperan", waiting));
        if (parts.Count > 0) return string.Join(" · ", parts);
        var done = _sessions.Where(s => s.State == SessionState.Done).OrderByDescending(s => s.TurnEndedAt).FirstOrDefault();
        return done is not null ? L.T("«{0}» terminó", Short(done.DisplayTitle, 28)) : L.T("Todo tranquilo");
    }

    /// <summary>La cabeza de la mascota: parpadea, mira a los lados mientras analiza y sonríe al terminar.</summary>
    void DrawHead(DrawingContext dc, Rect box, bool looking = false, bool talking = false)
    {
        var tick = Environment.TickCount64;
        var frame = Analyzing || looking ? (tick / 600 % 2 == 0 ? MascotFrame.Left : MascotFrame.Right)
            : talking ? (tick / 160 % 3 == 0 ? MascotFrame.Happy : MascotFrame.Open)
            : _file is not null && !_analysisSeen || tick - _analysisEnd < 3000 && _analysisDone ? MascotFrame.Happy
            : tick % 3600 > 3480 ? MascotFrame.Closed : MascotFrame.Open;
        if (_skin is { } skin)
        {
            var unit = Math.Max(1, Math.Floor(box.Height / ChibiSprite.HeadSize));
            var size = ChibiSprite.HeadSize * unit;
            Gfx.Pixels(dc, box.X + (box.Width - size) / 2, box.Y + (box.Height - size) / 2, unit, fill => ChibiSprite.PaintHead(fill, frame, skin: skin));
        }
        else
        {
            var unit = Math.Max(1, Math.Floor(box.Width / MascotSprite.Columns));
            Gfx.Pixels(dc, box.X + (box.Width - MascotSprite.Columns * unit) / 2, box.Y + (box.Height - MascotSprite.Rows * unit) / 2, unit,
                fill => MascotSprite.Paint(fill, frame, _accent));
        }
    }

    // ── Abierta ─────────────────────────────────────────────────────────────────────────────

    /// <summary>El alto de la isla abierta con la página actual (un recorrido sin dibujar, que no cambia lo dibujado).</summary>
    double MeasureOpen()
    {
        var hits = _hits.ToList();
        var (input, view, track, thumb) = (_inputBounds, _chatView, _chatTrack, _chatThumb);
        var b = new Rect(0, 0, OpenW, 2000);
        var bottom = ComposePage(null, b, ComposeTopBar(null, b)) + 14 - b.Y;
        _hits.Clear();
        _hits.AddRange(hits);
        (_inputBounds, _chatView, _chatTrack, _chatThumb) = (input, view, track, thumb);
        return bottom;
    }

    double ComposePage(DrawingContext? dc, Rect b, double y) => _page switch
    {
        Page.Chat => ComposeChat(dc, b, y),
        Page.Analysis => ComposeAnalysis(dc, b, y),
        Page.Permission => ComposePermission(dc, b, y),
        Page.Question => ComposeQuestion(dc, b, y),
        Page.Diff => ComposeDiff(dc, b, y),
        Page.Week => ComposeWeek(dc, b, y),
        Page.Choice => ComposeChoice(dc, b, y),
        _ => ComposeHome(dc, b, y),
    };

    double ComposeTopBar(DrawingContext? dc, Rect b)
    {
        var y = b.Y + 8;
        var x = b.X + 10;
        const double height = 28;
        foreach (var (key, label, page) in new (string, string, Page?)[] { ("tab:home", L.T("Actividad"), Page.Home), ("tab:chat", L.T("Chat"), Page.Chat), ("tab:new", L.T("+ Sesión"), null) })
        {
            var w = Gfx.Measure(label, Small).Width + 24;
            var rect = new Rect(x, y, w, height);
            Hit(rect, key);
            if (dc is not null)
            {
                var selected = page == _page;
                if (selected) Gfx.Fill(dc, Tab, rect, height / 2);
                else if (_hoverKey == key) Gfx.Fill(dc, Hex(0x242422), rect, height / 2);
                Gfx.Write(dc, label, Small, selected ? Ink : Dim, rect, TextAlignment.Center, center: true);
            }
            x = rect.Right + 4;
        }

        var right = b.Right - 10;
        foreach (var (key, glyph) in new[] { ("gear", Glyph.Settings), ("mute", _muted ? Glyph.Mute : Glyph.Volume) })
        {
            var rect = new Rect(right - 30, y - 1, 30, 30);
            Hit(rect, key);
            if (dc is not null)
            {
                if (_hoverKey == key) Gfx.Fill(dc, Raised, rect, 8);
                Gfx.Icon(dc, glyph, Dim, rect, 15);
            }
            right = rect.X;
        }
        right -= 4;
        if (_weekPercent is { } week) right = UsagePill(dc, L.T("Semana {0:0} %", week), week, right, y + (height - 22) / 2, accent: false) - 6;
        if (_sessionPercent is { } session) UsagePill(dc, L.T("Sesión {0:0} %", session), session, right, y + (height - 22) / 2, accent: true);
        return y + height + 8;
    }

    double UsagePill(DrawingContext? dc, string label, double percent, double right, double y, bool accent)
    {
        var font = accent ? SmallStrong : Small;
        var w = Gfx.Measure(label, font).Width + 18;
        var rect = new Rect(right - w, y, w, 22);
        if (dc is not null)
        {
            var danger = percent >= 90;
            var color = danger ? Bad : _accent;
            Gfx.Fill(dc, accent || danger ? SD.Color.FromArgb(46, color) : Raised, rect, rect.Height / 2);
            Gfx.Write(dc, label, font, accent || danger ? Lighten(color) : Soft, rect, TextAlignment.Center, center: true);
        }
        return rect.X;
    }

    // ── Actividad: las sesiones ─────────────────────────────────────────────────────────────

    double ComposeHome(DrawingContext? dc, Rect b, double y)
    {
        var list = _sessions.Where(s => s.State != SessionState.Idle).Take(4).ToList();
        var x = b.X + 12;
        var width = b.Width - 24;
        if (list.Count == 0)
        {
            Gfx.Write(dc, L.T("No hay sesiones activas."), Body, Soft, new Rect(x + 4, y + 6, width, 20), center: true);
            Gfx.Write(dc, L.T("Abra una con «+ Sesión», escríbale en el Chat o suelte un archivo aquí."), Small, Faint,
                new Rect(x + 4, y + 28, width, 18), center: true);
            return y + 50;
        }
        foreach (var s in list) y = ComposeSession(dc, s, x, y, width) + 6;
        return y - 6;
    }

    double ComposeSession(DrawingContext? dc, SessionInfo s, double x, double y, double width)
    {
        var top = y;
        const double pad = 10, lineH = 20;
        var key = "sess:" + s.SessionId;
        var row = y + pad;
        var textX = x + pad + 24;
        var textW = width - pad * 2 - 24;

        // Primera línea: estado, título, cuenta y modelo, y el botón de la terminal.
        var tag = s.Account.Label + (s.Model is { } model ? " · " + Format.Model(model) : "");
        var tagW = Gfx.Measure(tag, Small).Width;
        var termW = Gfx.Measure(L.T("Terminal"), Small).Width + 18;
        var bodyY = row + lineH + 4;
        var card = new Rect(x, top, width, ComposeSessionBody(null, s, textX, bodyY, textW) + pad - top);
        var term = new Rect(x + width - pad - termW, row - 1, termW, 22);
        if (dc is not null)
        {
            if (s.State == SessionState.Waiting)
            {
                Gfx.Fill(dc, Hex(0x24201A), card, 12);
                Gfx.Outline(dc, Hex(0x5A4527), card, 12);
            }
            else if (s.State == SessionState.Working) Gfx.Fill(dc, Card, card, 12);
            else if (_hoverKey == key) Gfx.Fill(dc, Hex(0x1A1A19), card, 12);
            if (_hoverKey == key && s.State != SessionState.Done) Gfx.Outline(dc, Edge, card, 12);

            StateIcon(dc, s.State, new Rect(x + pad, row, 16, lineH));
            Gfx.Write(dc, s.DisplayTitle, BodyStrong, Ink, new Rect(textX, row, Math.Max(1, textW - tagW - termW - 16), lineH), center: true);
            Gfx.Write(dc, tag, Small, Dim, new Rect(term.X - 8 - tagW, row, tagW + 2, lineH), center: true);
            Gfx.Outline(dc, _hoverKey == "term:" + s.SessionId ? Soft : Edge, term, 11);
            Gfx.Write(dc, L.T("Terminal"), Small, Soft, term, TextAlignment.Center, center: true);
            ComposeSessionBody(dc, s, textX, bodyY, textW);
        }
        Hit(card, key, s);
        Hit(term, "term:" + s.SessionId, s);
        return card.Bottom;
    }

    /// <summary>
    /// Lo de abajo del título: los nodos del carril (como en Actividad de la bandeja) y el avance, o cómo terminó.
    /// Un nodo con la edición de un archivo abre su diff.
    /// </summary>
    double ComposeSessionBody(DrawingContext? dc, SessionInfo s, double x, double y, double width)
    {
        const double lineH = 18;
        if (s.State != SessionState.Done && LaneNodes.Shows(s))
        {
            var ground = s.State switch { SessionState.Working => Card, SessionState.Waiting => Hex(0x24201A), _ => Ground };
            var colors = new LaneColors(ground, Edge, Ink, Soft, Dim, _accent, Warn, Bad);
            y = LaneNodes.Compose(dc, s, x, width, y, colors, _arrivals, maxColumn: 112, (group, column) =>
            {
                if (dc is not null && group is { Kind: ToolKind.Edit, Count: 1 } && s.Edits.LastOrDefault(e => e.StepId == group.Last.Id) is { } edit)
                    Hit(column, "diff:" + edit.StepId, (s, edit));
            }) + 6;
        }
        switch (s.State)
        {
            case SessionState.Working:
            {
                var progress = new List<string>();
                if (s.TodoTotal > 0) progress.Add(L.T("{0} de {1} tareas", s.TodoDone, s.TodoTotal));
                progress.Add(s.Steps == 1 ? L.T("1 paso") : L.T("{0} pasos", s.Steps));
                if (s.TurnStartedAt is { } start) progress.Add(Format.Duration(DateTimeOffset.Now - start));
                Gfx.Write(dc, string.Join(" · ", progress), Small, Faint, new Rect(x, y, width, lineH), center: true);
                return y + lineH;
            }
            case SessionState.Waiting:
                Gfx.Write(dc, s.AwaitingAnswer ? L.T("Le está preguntando · clic para ver") : L.T("Necesita su permiso · clic para ver"), Small, Hex(0xF0C27A),
                    new Rect(x, y, width, lineH), center: true);
                return y + lineH;
            case SessionState.Failed:
                Gfx.Write(dc, L.T("Falló · {0}", s.ErrorText ?? L.T("error de la API")), Small, Bad, new Rect(x, y, width, lineH), center: true);
                return y + lineH;
            default:
                var done = L.T("Terminó {0}", Format.Ago(s.TurnEndedAt ?? s.StatusAt)) + (s.LastTurn is { } d2 ? $" · {Format.Duration(d2)}" : "");
                Gfx.Write(dc, done, Small, Good, new Rect(x, y, width, lineH), center: true);
                if (s.LastAnswer is not { Length: > 0 } answer) return y + lineH;
                Gfx.Write(dc, answer.ReplaceLineEndings(" "), Small, Dim, new Rect(x, y + lineH + 2, width, lineH), center: true);
                return y + lineH * 2 + 2;
        }
    }

    void StateIcon(DrawingContext dc, SessionState state, Rect box)
    {
        switch (state)
        {
            case SessionState.Working:
                Gfx.Write(dc, SpinnerFrames[(int)(Environment.TickCount64 / 110 % SpinnerFrames.Length)], Spinner, _accent, box, TextAlignment.Center, center: true);
                break;
            case SessionState.Waiting:
                Pulse(dc, box.Center, 4, Warn);
                break;
            case SessionState.Failed:
                Gfx.Icon(dc, Glyph.Close, Bad, box, 13);
                break;
            default:
                Gfx.Icon(dc, Glyph.Check, Good, box, 14);
                break;
        }
    }

    // ── Chat ────────────────────────────────────────────────────────────────────────────────

    /// <summary>La barra de la conversación: fina y tenue, como la isla; se aclara con el cursor encima o al arrastrarla.</summary>
    void ChatScrollBar(DrawingContext? dc, Rect b, Rect view, double content, double maxScroll)
    {
        var track = new Rect(b.Right - 9, view.Y + 2, 4, view.Height - 4);
        var thumbH = Math.Max(28, track.Height * view.Height / content);
        var thumbY = track.Y + (track.Height - thumbH) * (maxScroll - _chatScroll) / maxScroll;
        _chatTrack = track;
        _chatThumb = new Rect(track.X, thumbY, track.Width, thumbH);
        Hit(track.Inflate(new Thickness(4, 0)), "chat:track");
        Hit(_chatThumb.Inflate(new Thickness(4, 0)), "chat:thumb");
        if (dc is null) return;
        var active = _chatDrag is not null || _hoverKey is "chat:thumb" or "chat:track";
        Gfx.Fill(dc, SD.Color.FromArgb(active ? 36 : 18, 255, 255, 255), track, 2);
        Gfx.Fill(dc, active ? Dim : Edge, _chatThumb, 2);
    }

    double ComposeChat(DrawingContext? dc, Rect b, double y)
    {
        var x = b.X + 12;
        var width = b.Width - 24;
        y += 2;

        if (_turns.Count == 0 && _streaming is null)
        {
            Para(dc, L.T("Pregúntele por su uso, sus sesiones o lo que quiera. También puede soltar un archivo aquí o pedirle una tarea."),
                Small, Faint, new Rect(x + 4, y, width - 8, 36));
            y += 40;
        }
        else
        {
            // Toda la conversación (ChatThread guarda las últimas 40 y la olvida a los 10 minutos sin escribir), en un
            // marco de ChatViewHeight como mucho: lo que no cabe se ve con la rueda o la barra, y arriba de todo se dice
            // cuándo se renueva. Cada mensaje lleva su índice en la conversación (para su animación de entrada) y la
            // respuesta en curso se ve hasta donde va escrita. Bajo cada respuesta, quién respondió (Ajustes › Respuestas).
            var items = _turns.Select((t, n) => (t.FromPerson, Text: VisibleText(n, t.Text), Index: n, Thinking: false,
                Who: t.Who is not null && !(n == _typedIndex && Typing) ? t.Who : null)).ToList();
            if (_streaming is not null)
                items.Add((false, _streaming.Length == 0 ? "" : _streaming[..Math.Min(_typedShown, _streaming.Length)], -1, _streaming.Length == 0 || _typedShown == 0, null));
            var heights = items.Select(i => (i.Thinking ? 36 : BubbleHeight(i.Text, i.FromPerson ? width - 140 : width - 80))
                + (i.Who is null ? 0 : 18)).ToList();
            const double gap = 8;
            var content = heights.Sum() + gap * items.Count;
            var note = content > ChatViewHeight ? L.T("La conversación se renueva a los 10 minutos sin escribir, o con «Nueva conversación».") : null;
            var noteH = note is null ? 0 : 26;
            content += noteH;
            var view = new Rect(x, y, width, Math.Min(content, ChatViewHeight));
            var maxScroll = content - view.Height;
            // Si se subió a leer y llega más abajo (la respuesta que se escribe), lo que se está leyendo no se mueve.
            if (_chatScroll > 0 && content > _chatContent) _chatScroll += content - _chatContent;
            _chatContent = content;
            _chatScroll = Math.Clamp(_chatScroll, 0, maxScroll);
            (_chatView, _chatMaxScroll) = (view, maxScroll);
            using (dc?.PushClip(new Rect(b.X, view.Y, b.Width, view.Height)))
            {
                var cy = y - (maxScroll - _chatScroll); // donde empieza la conversación, arriba del marco si no cabe
                if (note is not null)
                {
                    Gfx.Write(dc, note, Small, Faint, new Rect(x, cy, width, noteH - gap), TextAlignment.Center, center: true);
                    cy += noteH;
                }
                for (var i = 0; i < items.Count; i++)
                {
                    var (person, text, index, thinking, who) = items[i];
                    var max = person ? width - 140 : width - 80;
                    var bubbleH = heights[i] - (who is null ? 0 : 18);
                    var y0 = cy;
                    cy += heights[i] + gap;
                    if (dc is null || y0 + heights[i] < view.Y || y0 > view.Bottom) continue; // fuera del marco: no se dibuja
                    // Entrada: sube unos pixeles mientras aparece (el mensaje enviado, desde el campo).
                    var arrive = index >= 0 && _turnAt.TryGetValue(index, out var at) ? Ease(Math.Clamp((Environment.TickCount64 - at) / 240.0, 0, 1)) : 1;
                    var lift = (1 - arrive) * (person ? 14 : 8);
                    var alpha = (int)(255 * arrive);
                    const double head = 32;
                    if (thinking)
                    {
                        // Pensando: el asterisco de Claude gira y el texto tiene un brillo que lo recorre.
                        var rect = new Rect(x + head + 8, y0, 126, bubbleH);
                        DrawHead(dc, new Rect(x, rect.Bottom - head, head, head), looking: true);
                        Gfx.Fill(dc, Raised, rect, 12);
                        Gfx.Write(dc, SpinnerFrames[(int)((Environment.TickCount64 - _thinkingAt) / 110 % SpinnerFrames.Length)], Spinner, _accent,
                            new Rect(rect.X + 10, rect.Y, 18, rect.Height), TextAlignment.Center, center: true);
                        Shimmer(dc, L.T("Pensando…"), Body, new Rect(rect.X + 32, rect.Y, rect.Width - 38, rect.Height));
                    }
                    else if (person)
                    {
                        var bubbleW = MeasureWrapped(text, Body, max - 22).Width + 22;
                        var rect = new Rect(x + width - bubbleW, y0 + lift, bubbleW, bubbleH);
                        Gfx.Fill(dc, SD.Color.FromArgb(alpha, Line), rect, 12);
                        Para(dc, text, Body, SD.Color.FromArgb(alpha, Ink), rect.Deflate(new Thickness(11, 7)));
                    }
                    else
                    {
                        var bubbleW = MeasureWrapped(text, Body, max - 22).Width + 22;
                        var rect = new Rect(x + head + 8, y0 + lift, bubbleW, bubbleH);
                        var writing = index < 0 || index == _typedIndex && Typing;
                        DrawHead(dc, new Rect(x, rect.Bottom - head, head, head), talking: writing);
                        Gfx.Fill(dc, SD.Color.FromArgb(alpha, Raised), rect, 12);
                        if (who is not null)
                            Gfx.Write(dc, who, Small, Faint, new Rect(rect.X + 4, rect.Bottom + 3, width - head - 12, 15), center: true);
                        var inner = rect.Deflate(new Thickness(11, 7));
                        Para(dc, text, Body, SD.Color.FromArgb(alpha, Hex(0xE8E6DF)), inner);
                        if (writing && Environment.TickCount64 / 450 % 2 == 0)
                        {
                            // El cursor al final de lo escrito, parpadeando.
                            var caret = Wrapped(text, Body, Ink, inner.Width).HitTestTextPosition(text.Length);
                            dc.FillRectangle(Gfx.B(_accent), new Rect(inner.X + Math.Min(inner.Width - 6, caret.X + 2), inner.Y + caret.Y + 2, 6, Math.Max(4, caret.Height - 4)));
                        }
                    }
                }
            }
            if (maxScroll > 0) ChatScrollBar(dc, b, view, content, maxScroll);
            y = view.Bottom;
        }

        // El modelo (que despliega los otros), la cuenta y «Nueva conversación».
        var chipW = Gfx.Measure(_model, Small).Width + 30;
        var chip = new Rect(x, y, chipW, 24);
        Hit(chip, "model");
        if (dc is not null)
        {
            Gfx.Outline(dc, _hoverKey == "model" || _picking ? Soft : Edge, chip, 12);
            Gfx.Write(dc, _model, Small, Soft, new Rect(chip.X + 10, chip.Y, chipW - 26, chip.Height), center: true);
            Chevron(dc, new Point(chip.Right - 14, chip.Y + chip.Height / 2), Soft, up: _picking);
            if (_account.Length > 0)
                Gfx.Write(dc, L.T("con {0}", _account), Small, Faint, new Rect(chip.Right + 8, chip.Y, 160, chip.Height), center: true);
        }
        var reset = L.T("Nueva conversación");
        var resetW = Gfx.Measure(reset, Small).Width + 12;
        var resetRect = new Rect(x + width - resetW, y, resetW, 24);
        Hit(resetRect, "newchat");
        Gfx.Write(dc, reset, Small, _hoverKey == "newchat" ? Soft : Faint, resetRect, TextAlignment.Center, center: true);
        y += 24 + 8;

        if (_picking)
        {
            // Los modelos, en fila (y si no caben, en la de abajo); el elegido, lleno.
            var mx = x;
            foreach (var (id, label) in _models)
            {
                var w = Gfx.Measure(label, Small).Width + 22;
                if (mx > x && mx + w > x + width)
                {
                    mx = x;
                    y += 24 + 6;
                }
                var rect = new Rect(mx, y, w, 24);
                var key = "mdl:" + id;
                Hit(rect, key, id);
                if (dc is not null)
                {
                    if (id == _modelId) Gfx.Fill(dc, Tab, rect, 12);
                    Gfx.Outline(dc, id == _modelId ? Soft : _hoverKey == key ? Dim : Edge, rect, 12);
                    Gfx.Write(dc, label, Small, id == _modelId ? Ink : Soft, rect, TextAlignment.Center, center: true);
                }
                mx = rect.Right + 6;
            }
            y += 24 + 10;
        }

        // El campo (el texto lo escribe un TextBox encima), el micrófono y el botón de enviar.
        var field = new Rect(x, y, width, 42);
        var send = new Rect(field.Right - 36, field.Y + 6, 30, 30);
        var mic = _micEnabled ? new Rect(send.X - 34, send.Y, 30, 30) : default;
        var textRight = _micEnabled ? mic.X : send.X;
        if (dc is not null) _inputBounds = new Rect(field.X + 12, field.Y + 11, textRight - field.X - 20, 20);
        Hit(send, "send");
        if (_micEnabled) Hit(mic, "mic");
        if (dc is not null)
        {
            Gfx.Fill(dc, _listening ? Hex(0x241B1A) : Card, field, 10);
            Gfx.Outline(dc, _listening ? Hex(0xD94B3F) : InputFocused ? SD.Color.FromArgb(160, _accent) : Edge, field, 10);
            var text = new Rect(field.X + 12, field.Y, textRight - field.X - 20, field.Height);
            if (_listening) DrawListening(dc, text);
            else if (_converting) Shimmer(dc, _heard.Length > 0 ? Tail(_heard, Body, text.Width) : L.T("Pasando a texto…"), Body, text);
            else if (VoiceNoteShown) Gfx.Write(dc, _voiceNote!, Small, Warn, text, center: true);
            if (_micEnabled)
            {
                if (_listening) Gfx.Fill(dc, Hex(0xD94B3F), mic, 8);
                else if (_hoverKey == "mic") Gfx.Fill(dc, Raised, mic, 8);
                Gfx.Icon(dc, Glyph.Mic, _listening ? SD.Color.White : _hoverKey == "mic" ? Ink : Soft, mic, 16);
            }
            Gfx.Fill(dc, _asking ? Raised : _accent, send, 8);
            Gfx.Icon(dc, _asking ? Glyph.Stop : Glyph.Send, SD.Color.White, send, 15);
        }
        return field.Bottom;
    }

    /// <summary>Escuchando: el nivel de la voz en barritas, lo entendido hasta ahora y, a la derecha, cómo se termina.</summary>
    void DrawListening(DrawingContext dc, Rect text)
    {
        var tick = Environment.TickCount64;
        var middle = text.Y + text.Height / 2;
        var periods = Flows.BarPeriods;
        for (var i = 0; i < periods.Length; i++)
        {
            var wave = 0.5 + 0.5 * Math.Sin(tick % periods[i] / (double)periods[i] * 2 * Math.PI + i);
            var h = Math.Max(4, 20 * (0.35 + 0.65 * wave) * (0.2 + 0.8 * _level));
            Gfx.Fill(dc, Hex(0xE8695C), new Rect(text.X + i * 6, middle - h / 2, 3, h), 1.5);
        }
        var hintW = Gfx.Measure(_voiceHint, Small).Width + 4;
        var hint = new Rect(text.Right - hintW, text.Y, hintW, text.Height);
        Gfx.Write(dc, _voiceHint, Small, Hex(0xE8A39B), hint, TextAlignment.Right, center: true);
        var bars = periods.Length * 6 + 6;
        var said = new Rect(text.X + bars, text.Y, Math.Max(1, hint.X - text.X - bars - 8), text.Height);
        Gfx.Write(dc, _heard.Length > 0 ? Tail(_heard, Body, said.Width) : L.T("Escuchando…"), Body, _heard.Length > 0 ? Ink : Faint, said, center: true);
    }

    /// <summary>El final de un texto que no cabe en una línea («…lo último que se dijo»).</summary>
    static string Tail(string text, UiFont font, double width)
    {
        if (Gfx.Measure(text, font).Width <= width) return text;
        while (text.Length > 1 && Gfx.Measure("…" + text, font).Width > width)
            text = text.IndexOf(' ') is var space and > 0 ? text[(space + 1)..] : text[1..];
        return "…" + text;
    }

    static double BubbleHeight(string text, double max) => MeasureWrapped(text, Body, max - 22).Height + 14;

    /// <summary>El texto de un mensaje tal como se ve: la respuesta que se está escribiendo, hasta donde va.</summary>
    string VisibleText(int index, string text) =>
        index == _typedIndex && _streaming is null ? text[..Math.Min(_typedShown, text.Length)] : text;

    // ── Análisis ────────────────────────────────────────────────────────────────────────────

    double ComposeAnalysis(DrawingContext? dc, Rect b, double y)
    {
        var x = b.X + 12;
        var width = b.Width - 24;
        const double pad = 14, boxSize = 54, titleH = 20;
        var cardTop = y + 2;
        var tx = x + pad + boxSize + 14;
        var tw = x + width - pad - tx;
        var row = cardTop + pad;
        var name = Path.GetFileName(_file ?? "");

        // Título (con brillo mientras trabaja) y tiempo.
        var title = _analysisDone
            ? name
            : _stage switch
            {
                AssistantStage.Opening => L.T("Abriendo {0}", name),
                AssistantStage.Reading => L.T("Leyendo {0}", name),
                _ => L.T("Escribiendo el resumen"),
            };
        var elapsed = (_analysisDone ? _analysisEnd : Environment.TickCount64) - _analysisAt;
        var time = $"{elapsed / 60000}:{elapsed / 1000 % 60:00}";
        var timeW = Gfx.Measure(time, Small).Width;
        var stepsY = row + titleH + 8;
        var textY = stepsY + 16 + 10;
        var text = _analysisError ?? (_analysis.Length > 0 ? _analysis : _analysisDone ? "" : L.T("Leyendo; el resumen aparece aquí mientras se escribe."));
        var textH = Math.Max(38, MeasureWrapped(text, Body, tw).Height);
        var footY = textY + textH + 10;
        var card = new Rect(x, cardTop, width, footY + (_analysisDone ? 28 : 18) + pad - cardTop);

        if (dc is not null)
        {
            Gfx.Fill(dc, Card, card, 14);
            // La caja: se abre una vez y la hoja sube y baja mientras lee.
            var frame = _analysisDone ? ChibiSprite.BoxFrame.Open
                : (Environment.TickCount64 - _analysisAt) switch
                {
                    < 500 => ChibiSprite.BoxFrame.Closed,
                    < 660 => ChibiSprite.BoxFrame.Opening,
                    var t => (t - 660) / 360 % 2 == 0 ? ChibiSprite.BoxFrame.Open : ChibiSprite.BoxFrame.PaperUp,
                };
            var unit = Math.Max(1, Math.Floor(boxSize / ChibiSprite.BoxColumns));
            Gfx.Pixels(dc, card.X + pad + (boxSize - ChibiSprite.BoxColumns * unit) / 2, row, unit, fill => ChibiSprite.PaintBox(fill, frame));

            if (_analysisDone)
            {
                var nameW = Math.Min(Gfx.Measure(title, BodyStrong).Width + 4, tw - timeW - 30);
                Gfx.Write(dc, title, BodyStrong, Ink, new Rect(tx, row, nameW, titleH), center: true);
                var mark = new Rect(tx + nameW + 2, row, 16, titleH);
                Gfx.Icon(dc, _analysisError is null ? Glyph.Check : Glyph.Close, _analysisError is null ? Good : Bad, mark, 13);
            }
            else Shimmer(dc, title, BodyStrong, new Rect(tx, row, tw - timeW - 10, titleH));
            Gfx.Write(dc, time, Small, Faint, new Rect(card.Right - pad - timeW, row, timeW + 2, titleH), center: true);

            Stepper(dc, tx, stepsY, tw);
            Para(dc, text, Body, _analysisError is null ? Hex(0xE8E6DF) : Bad, new Rect(tx, textY, tw, textH));
            if (!_analysisDone && _stage == AssistantStage.Writing && Environment.TickCount64 / 450 % 2 == 0)
            {
                var caret = Wrapped(text, Body, Ink, tw).HitTestTextPosition(text.Length);
                dc.FillRectangle(Gfx.B(_accent), new Rect(tx + Math.Min(tw - 8, caret.X + 2), textY + caret.Y + 2, 7, 14));
            }
        }

        if (_analysisDone)
        {
            var ax = tx;
            foreach (var (key, label) in new[] { ("res:ask", L.T("Preguntar algo más")), ("res:copy", L.T("Copiar")), ("res:session", L.T("Seguir en una sesión")) })
            {
                var w = Gfx.Measure(label, Small).Width + 24;
                var rect = new Rect(ax, footY, w, 28);
                Hit(rect, key);
                if (dc is not null)
                {
                    Gfx.Outline(dc, _hoverKey == key ? Soft : Edge, rect, 14);
                    Gfx.Write(dc, label, Small, key == "res:ask" ? Lighten(_accent) : Soft, rect, TextAlignment.Center, center: true);
                }
                ax = rect.Right + 8;
            }
        }
        else
        {
            var info = L.T("{0} · solo puede leer la carpeta del archivo", _analysisModel);
            var cancel = L.T("Cancelar · Esc");
            var cw = Gfx.Measure(cancel, Small).Width + 12;
            var cancelRect = new Rect(card.Right - pad - cw, footY - 2, cw, 22);
            Hit(cancelRect, "cancel");
            Gfx.Write(dc, info, Small, Faint, new Rect(tx, footY, Math.Max(1, tw - cw - 10), 18), center: true);
            Gfx.Write(dc, cancel, Small, _hoverKey == "cancel" ? Soft : Dim, cancelRect, TextAlignment.Center, center: true);
        }
        return card.Bottom;
    }

    /// <summary>Los pasos reales del análisis: Abrir, Leer y Resumir (hecho, en curso o pendiente).</summary>
    void Stepper(DrawingContext dc, double x, double y, double width)
    {
        string[] labels = [L.T("Abrir"), L.T("Leer"), L.T("Resumir")];
        var current = _analysisDone ? 3 : (int)_stage;
        var cx = x;
        const double dot = 16;
        for (var i = 0; i < labels.Length; i++)
        {
            var done = i < current;
            var active = i == current;
            var circle = new Rect(cx, y, dot, dot);
            if (done)
            {
                dc.DrawEllipse(Gfx.B(Good), null, circle);
                Gfx.Icon(dc, Glyph.Check, Card, circle, 11);
            }
            else
            {
                var alpha = active ? (int)(140 + 115 * (0.5 + 0.5 * Math.Sin(Environment.TickCount64 / 160.0))) : 255;
                dc.DrawEllipse(null, Gfx.P(active ? SD.Color.FromArgb(alpha, _accent) : Hex(0x4A4946), 2), circle.Deflate(1));
            }
            cx += dot + 6;
            var lw = Gfx.Measure(labels[i], Small).Width;
            Gfx.Write(dc, labels[i], Small, done ? Hex(0xC3D4A5) : active ? Ink : Faint, new Rect(cx, y - 1, lw + 2, dot + 2), center: true);
            cx += lw + 8;
            if (i < labels.Length - 1)
            {
                var segment = Math.Max(18, (width - (3 * 16 + 6 * 3 + 8 * 3) - labels.Sum(l => Gfx.Measure(l, Small).Width)) / 2);
                dc.DrawLine(Gfx.P(done ? Good : Line, 2), new Point(cx, y + dot / 2), new Point(cx + segment - 8, y + dot / 2));
                cx += segment;
            }
        }
    }

    // ── Resumen de la semana ────────────────────────────────────────────────────────────────

    double ComposeWeek(DrawingContext? dc, Rect b, double y)
    {
        if (_week is not { } w) return y;
        var x = b.X + 12;
        var width = b.Width - 24;
        const double pad = 14, head = 48, rowH = 40;
        var top = y + 2;
        var tx = x + pad + head + 14;
        var tw = x + width - pad - tx;
        var row = top + pad;
        var stats = new (string Value, string Label)[]
        {
            (Format.Tokens(w.Tokens), L.T("tokens")),
            (w.Sessions.ToString(), w.Sessions == 1 ? L.T("sesión") : L.T("sesiones")),
            (w.Prompts.ToString(), w.Prompts == 1 ? L.T("pedido") : L.T("pedidos")),
            (w.ToolCalls.ToString(), L.T("pasos")),
            (w.BusiestDay is { } d ? $"{d:dddd}" : "—", L.T("día más activo")),
            (w.FavoriteModel ?? "—", L.T("modelo favorito")),
        };
        var cellW = tw / 3;
        var gridY = row + 28;
        var buttonsY = gridY + 2 * rowH + 4;
        var card = new Rect(x, top, width, buttonsY + 28 + pad - top);
        if (dc is not null)
        {
            Gfx.Fill(dc, Card, card, 14);
            DrawHead(dc, new Rect(x + pad, row, head, head));
            Gfx.Write(dc, L.T("Su semana"), BodyStrong, Ink, new Rect(tx, row, tw * 0.55, 22), center: true);
            Gfx.Write(dc, w.Range, Small, Dim, new Rect(tx + tw * 0.55, row, tw * 0.45, 22), TextAlignment.Right, center: true);
            for (var i = 0; i < stats.Length; i++)
            {
                var cx = tx + i % 3 * cellW;
                var cy = gridY + i / 3 * rowH;
                Gfx.Write(dc, stats[i].Value, BodyStrong, Ink, new Rect(cx, cy, cellW - 8, 20), center: true);
                Gfx.Write(dc, stats[i].Label, Small, Dim, new Rect(cx, cy + 19, cellW - 8, 18), center: true);
            }
        }
        var bx = tx;
        foreach (var (key, label) in new[] { ("week:copy", L.T("Copiar")), ("week:close", L.T("Listo")) })
        {
            var bw = Gfx.Measure(label, Small).Width + 26;
            var rect = new Rect(bx, buttonsY, bw, 28);
            Hit(rect, key);
            if (dc is not null)
            {
                Gfx.Outline(dc, _hoverKey == key ? Soft : Edge, rect, 14);
                Gfx.Write(dc, label, Small, key == "week:copy" ? Lighten(_accent) : Soft, rect, TextAlignment.Center, center: true);
            }
            bx = rect.Right + 8;
        }
        return card.Bottom;
    }

    // ── El diff de un archivo ───────────────────────────────────────────────────────────────

    double ComposeDiff(DrawingContext? dc, Rect b, double y)
    {
        if (_diff is not { } d) return y;
        var x = b.X + 12;
        var width = b.Width - 24;
        var row = y + 2;
        var back = new Rect(x, row, 28, 28);
        Hit(back, "diff:back");
        var open = L.T("Abrir en VS Code ↗");
        var openW = Gfx.Measure(open, Small).Width + 24;
        var openRect = new Rect(x + width - openW, row, openW, 28);
        Hit(openRect, "diff:open");
        if (dc is not null)
        {
            Gfx.Fill(dc, _hoverKey == "diff:back" ? Tab : Raised, back, 8);
            Gfx.Icon(dc, Glyph.Back, Soft, back, 14);
            var added = $"+{d.Edit.Added}";
            var counts = added + (d.Edit.Removed > 0 ? $"  −{d.Edit.Removed}" : "");
            var countsW = Gfx.Measure(counts, SmallStrong).Width;
            var name = Path.GetFileName(d.Edit.File);
            var nameW = Math.Max(1, Math.Min(Gfx.Measure(name, BodyStrong).Width, width - openW - countsW - 60));
            Gfx.Write(dc, name, BodyStrong, Ink, new Rect(back.Right + 10, row, nameW + 2, 28), center: true);
            var cx = back.Right + 10 + nameW + 10;
            Gfx.Write(dc, added, SmallStrong, Good, new Rect(cx, row, 50, 28), center: true);
            if (d.Edit.Removed > 0)
                Gfx.Write(dc, $"−{d.Edit.Removed}", SmallStrong, Bad, new Rect(cx + Gfx.Measure(added + "  ", SmallStrong).Width, row, 50, 28), center: true);
            Gfx.Outline(dc, _hoverKey == "diff:open" ? Soft : Edge, openRect, 14);
            Gfx.Write(dc, open, Small, Soft, openRect, TextAlignment.Center, center: true);
        }

        // Las líneas: verdes las que suma, rojas las que quita; como mucho 18, el resto se cuenta.
        var top = row + 36;
        var lineH = Mono.LineHeight + 2;
        var lines = d.Edit.Lines.Take(18).ToList();
        var rest = d.Edit.Lines.Count - lines.Count;
        var box = new Rect(x, top, width, 12 + lines.Count * lineH + (rest > 0 ? lineH : 0));
        if (dc is not null)
        {
            Gfx.Fill(dc, Card, box, 10);
            var ly = box.Y + 6;
            foreach (var line in lines)
            {
                var sign = line.Length > 0 ? line[0] : ' ';
                var band = new Rect(box.X, ly, box.Width, lineH);
                if (sign == '+') dc.FillRectangle(Gfx.B(SD.Color.FromArgb(46, Good)), band);
                if (sign == '-') dc.FillRectangle(Gfx.B(SD.Color.FromArgb(42, Bad)), band);
                var color = sign == '+' ? Hex(0xC3D4A5) : sign == '-' ? Hex(0xF2A79E) : Dim;
                Gfx.Write(dc, line.Replace('\t', ' '), Mono, color, new Rect(box.X + 10, ly, box.Width - 20, lineH), center: true);
                ly += lineH;
            }
            if (rest > 0) Gfx.Write(dc, L.T("… {0} líneas más", rest), Small, Faint, new Rect(box.X + 10, ly, box.Width - 20, lineH), center: true);
        }
        return box.Bottom;
    }

    // ── Tarjetas de permiso, de pregunta y de Claupet ───────────────────────────────────────

    /// <summary>La cabeza con el «!» ámbar de algo que espera respuesta.</summary>
    void AlertHead(DrawingContext dc, Rect box)
    {
        DrawHead(dc, box, looking: true);
        var badge = new Rect(box.Right - 13, box.Y - 3, 16, 16);
        dc.DrawEllipse(Gfx.B(Warn), null, badge);
        Gfx.Write(dc, "!", SmallStrong, Card, badge, TextAlignment.Center, center: true);
    }

    /// <summary>El botón ⌃ de la esquina: deja la tarjeta para después, sin responder.</summary>
    void FoldButton(DrawingContext? dc, double right, double y)
    {
        var rect = new Rect(right - 28, y - 4, 28, 28);
        Hit(rect, "ap:fold");
        if (dc is null) return;
        if (_hoverKey == "ap:fold") Gfx.Fill(dc, Raised, rect, 8);
        Gfx.Icon(dc, Glyph.ChevronUp, Dim, rect, 14);
    }

    /// <summary>Un botón redondeado de las tarjetas: el principal, claro; los demás, oscuros.</summary>
    double CardButton(DrawingContext? dc, string key, string label, bool primary, double x, double y, double height = 32, object? tag = null)
    {
        var font = primary ? SmallStrong : Small;
        var rect = new Rect(x, y, Gfx.Measure(label, font).Width + 26, height);
        Hit(rect, key, tag);
        if (dc is not null)
        {
            var hover = _hoverKey == key;
            Gfx.Fill(dc, primary ? (hover ? SD.Color.White : Ink) : (hover ? Tab : Line), rect, rect.Height / 2);
            Gfx.Write(dc, label, font, primary ? Card : Ink, rect, TextAlignment.Center, center: true);
        }
        return rect.Right + 8;
    }

    double ComposePermission(DrawingContext? dc, Rect b, double y)
    {
        if (_approval is not { } a) return y;
        var x = b.X + 12;
        var width = b.Width - 24;
        const double pad = 14, head = 48;
        var tx = x + pad + head + 14;
        var tw = x + width - pad - tx;
        var top = y + 2;
        var row = top + pad;

        var title = ApprovalSession?.DisplayTitle ?? _approvalTitle;
        var titleW = Math.Min(Gfx.Measure(title, BodyStrong).Width, tw * 0.55);
        var what = L.T("necesita su permiso para {0}", a.Tool);
        var code = a.Target.ReplaceLineEndings(" ⏎ ");
        var codeH = Math.Min(MeasureWrapped(code, Mono, tw - 20).Height, 52) + 14;
        var codeY = row + 24;
        var buttonsY = codeY + codeH + 10;
        var card = new Rect(x, top, width, buttonsY + 32 + pad - top);
        if (dc is not null)
        {
            Gfx.Fill(dc, Card, card, 14);
            AlertHead(dc, new Rect(x + pad, row, head, head));
            Gfx.Write(dc, title, BodyStrong, Ink, new Rect(tx, row, titleW + 2, 20), center: true);
            Gfx.Write(dc, what, Small, Dim, new Rect(tx + titleW + 6, row, Math.Max(1, tw - titleW - 40), 20), center: true);
            var box = new Rect(tx, codeY, tw, codeH);
            Gfx.Fill(dc, Raised, box, 8);
            Para(dc, code, Mono, Ink, box.Deflate(new Thickness(10, 7)));
        }
        FoldButton(dc, x + width - pad + 6, row);

        var bx = tx;
        bx = CardButton(dc, "ap:deny", L.T("Denegar"), false, bx, buttonsY);
        bx = CardButton(dc, "ap:allow", L.T("Permitir"), true, bx, buttonsY);
        bx = CardButton(dc, "ap:always", L.T("Siempre en esta sesión"), false, bx, buttonsY);
        var terminal = L.T("Abrir terminal");
        var termW = Gfx.Measure(terminal, Small).Width + 12;
        var termRect = new Rect(x + width - pad - termW, buttonsY, termW, 32);
        if (bx < termRect.X)
        {
            Hit(termRect, "ap:term");
            Gfx.Write(dc, terminal, Small, _hoverKey == "ap:term" ? Soft : Dim, termRect, TextAlignment.Center, center: true);
        }
        return card.Bottom;
    }

    double ComposeQuestion(DrawingContext? dc, Rect b, double y)
    {
        if (_approval?.Questions is not { } questions || _questionIndex >= questions.Count) return y;
        var q = questions[_questionIndex];
        var x = b.X + 12;
        var width = b.Width - 24;
        const double pad = 14;
        var top = y + 2;
        var row = top + pad;
        var inner = width - pad * 2;

        var title = ApprovalSession?.DisplayTitle ?? _approvalTitle;
        var header = questions.Count > 1 ? L.T("{0} pregunta · {1} de {2}", title, _questionIndex + 1, questions.Count) : L.T("{0} pregunta", title);
        var qy = row + 40;
        var qh = MeasureWrapped(q.Question, BodyStrong, inner).Height;
        var gridY = qy + qh + 10;
        const int cols = 2;
        var cellW = (inner - 8) / cols;
        var cells = new List<(Rect Rect, string Label, string Description)>();
        var rowY = gridY;
        for (var i = 0; i < q.Options.Count; i += cols)
        {
            var heights = Enumerable.Range(i, Math.Min(cols, q.Options.Count - i)).Select(k =>
                18 + (q.Options[k].Description.Length > 0 ? Math.Min(MeasureWrapped(q.Options[k].Description, Small, cellW - 22).Height, 34) : 0) + 16).ToList();
            var h = heights.Max();
            for (var c = 0; c < heights.Count; c++)
                cells.Add((new Rect(x + pad + c * (cellW + 8), rowY, cellW, h), q.Options[i + c].Label, q.Options[i + c].Description));
            rowY += h + 8;
        }
        var footY = rowY + 2;
        var card = new Rect(x, top, width, footY + 30 + pad - top);
        var chosen = _picks.TryGetValue(q.Question, out var list) ? list : [];

        if (dc is not null)
        {
            Gfx.Fill(dc, Card, card, 14);
            AlertHead(dc, new Rect(x + pad, row, 32, 32));
            Gfx.Write(dc, header, Small, Dim, new Rect(x + pad + 42, row, inner - 80, 32), center: true);
            Para(dc, q.Question, BodyStrong, Ink, new Rect(x + pad, qy, inner, qh + 2));
        }
        FoldButton(dc, x + width - pad + 6, row + 6);
        for (var i = 0; i < cells.Count; i++)
        {
            var (rect, label, description) = cells[i];
            var key = "q:opt:" + i;
            Hit(rect, key, label);
            if (dc is null) continue;
            var selected = chosen.Contains(label);
            Gfx.Fill(dc, selected ? SD.Color.FromArgb(30, _accent) : _hoverKey == key ? Raised : Card, rect, 10);
            Gfx.Outline(dc, selected ? _accent : _hoverKey == key ? Soft : Edge, rect, 10);
            Gfx.Write(dc, label, SmallStrong, Ink, new Rect(rect.X + 11, rect.Y + 8, rect.Width - 22, 18), center: true);
            if (description.Length > 0)
                Para(dc, description, Small, Dim, new Rect(rect.X + 11, rect.Y + 26, rect.Width - 22, rect.Height - 32));
        }

        // Elegir varias: «Siguiente» o «Enviar» confirma; una sola: el clic ya responde.
        var fx = x + pad;
        if (q.MultiSelect)
        {
            var label = _questionIndex == questions.Count - 1 ? L.T("Enviar") : L.T("Siguiente");
            var rect = new Rect(fx, footY, Gfx.Measure(label, SmallStrong).Width + 28, 30);
            if (chosen.Count > 0) Hit(rect, "q:next");
            if (dc is not null)
            {
                Gfx.Fill(dc, chosen.Count > 0 ? (_hoverKey == "q:next" ? SD.Color.White : Ink) : Line, rect, rect.Height / 2);
                Gfx.Write(dc, label, SmallStrong, chosen.Count > 0 ? Card : Faint, rect, TextAlignment.Center, center: true);
            }
            fx = rect.Right + 12;
        }
        var terminal = L.T("Responder en la terminal");
        var termRect = new Rect(fx, footY, Gfx.Measure(terminal, Small).Width + 12, 30);
        Hit(termRect, "ap:terminal");
        Gfx.Write(dc, terminal, Small, _hoverKey == "ap:terminal" ? Soft : Dim, termRect, TextAlignment.Center, center: true);
        return card.Bottom;
    }

    double ComposeChoice(DrawingContext? dc, Rect b, double y)
    {
        if (_choiceTask is null) return y;
        var x = b.X + 12;
        var width = b.Width - 24;
        const double pad = 14, head = 48;
        var tx = x + pad + head + 14;
        var tw = x + width - pad - tx;
        var top = y + 2;
        var row = top + pad;
        var titleH = MeasureWrapped(_choiceTitle, BodyStrong, tw).Height;
        var detailY = row + titleH + 4;
        var detailH = _choiceDetail.Length > 0 ? MeasureWrapped(_choiceDetail, Small, tw).Height : 0;
        var buttonsY = Math.Max(row + head, detailY + detailH) + 12;
        var card = new Rect(x, top, width, buttonsY + 32 + pad - top);
        if (dc is not null)
        {
            Gfx.Fill(dc, Card, card, 14);
            AlertHead(dc, new Rect(x + pad, row, head, head));
            Para(dc, _choiceTitle, BodyStrong, Ink, new Rect(tx, row, tw, titleH + 2));
            if (detailH > 0) Para(dc, _choiceDetail, Small, Dim, new Rect(tx, detailY, tw, detailH + 2));
        }
        var bx = tx;
        foreach (var (id, label) in _choiceOptions) bx = CardButton(dc, "ch:" + id, label, id == _choicePrimary, bx, buttonsY, tag: id);
        return card.Bottom;
    }

    // ── Ayudas de dibujo ────────────────────────────────────────────────────────────────────

    /// <summary>Un texto que se parte en líneas (maxLines: como mucho, con «…»; 0, todas).</summary>
    static TextLayout Wrapped(string text, UiFont font, SD.Color color, double width, int maxLines = 0) =>
        new(text.Length == 0 ? " " : text, font.Typeface, font.Size, Gfx.B(color), TextAlignment.Left, TextWrapping.Wrap,
            maxLines > 0 ? TextTrimming.CharacterEllipsis : null, maxWidth: Math.Max(1, width), maxLines: maxLines);

    static Size MeasureWrapped(string text, UiFont font, double width)
    {
        var layout = Wrapped(text, font, Ink, width);
        return new Size(Math.Ceiling(layout.Width), Math.Ceiling(layout.Height));
    }

    /// <summary>Un párrafo en el rectángulo: las líneas que quepan, con «…» en la última si no cabe.</summary>
    static void Para(DrawingContext? dc, string text, UiFont font, SD.Color color, Rect r)
    {
        if (dc is null || text.Length == 0) return;
        var lines = Math.Max(1, (int)((r.Height + 2) / font.LineHeight));
        Wrapped(text, font, color, r.Width, lines).Draw(dc, r.TopLeft);
    }

    /// <summary>Texto con un brillo que lo recorre de izquierda a derecha (como el «pensando» de las apps de Claude).</summary>
    static void Shimmer(DrawingContext dc, string text, UiFont font, Rect bounds)
    {
        var width = Math.Max(60, Gfx.Measure(text, font).Width);
        var phase = Environment.TickCount64 % 1800 / 1800.0;
        var center = -width * 0.3 + phase * width * 1.6; // desde el comienzo del texto
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(center - 70, 0, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(center + 70, 0, RelativeUnit.Absolute),
            GradientStops = { new GradientStop(Gfx.C(Dim), 0), new GradientStop(Colors.White, 0.5), new GradientStop(Gfx.C(Dim), 1) },
        };
        var ft = Gfx.Text(text, font, brush, bounds.Width);
        dc.DrawText(ft, new Point(bounds.X, bounds.Y + (bounds.Height - ft.Height) / 2));
    }

    static void Pulse(DrawingContext dc, Point center, double r, SD.Color color)
    {
        var wave = 0.5 + 0.5 * Math.Sin(Environment.TickCount64 % 1400 / 1400.0 * 2 * Math.PI);
        var halo = r + r * 0.8 * wave;
        dc.DrawEllipse(Gfx.B(SD.Color.FromArgb((int)(70 * (1 - wave)) + 20, color)), null, center, halo, halo);
        dc.DrawEllipse(Gfx.B(color), null, center, r, r);
    }

    static void Chevron(DrawingContext dc, Point center, SD.Color color, bool up)
    {
        const double w = 3;
        var dy = up ? -w / 2 : w / 2;
        dc.DrawGeometry(null, Gfx.P(color, 1.4), new PolylineGeometry([new(center.X - w, center.Y - dy), new(center.X, center.Y + dy), new(center.X + w, center.Y - dy)], isFilled: false));
    }

    static SD.Color Lighten(SD.Color c) => SD.Color.FromArgb(Math.Min(255, c.R + 30), Math.Min(255, c.G + 40), Math.Min(255, c.B + 40));

    static string Short(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
}
