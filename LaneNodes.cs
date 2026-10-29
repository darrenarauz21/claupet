namespace Claupet;

/// <summary>Los colores con que se dibuja un carril: los del tema en la bandeja, los de siempre en la isla.</summary>
readonly record struct LaneColors(Color Background, Color Border, Color Text, Color Secondary, Color Tertiary, Color Accent, Color Warn, Color Danger);

/// <summary>
/// Los nodos de un carril de Actividad: «Pedido», «+N» con lo anterior y los últimos grupos, unidos por líneas. El
/// último late (o «Pensando…» si ya volvió) y un punto viaja hacia él; cada paso nuevo entra creciendo. Los dibujan
/// la vista Actividad de la bandeja y la pestaña Actividad de la isla, cada una con sus colores y su forma de escribir.
/// </summary>
sealed class LaneNodes(float scale, Font glyph, Font small, Font tiny, LaneNodes.Writer write)
{
    /// <summary>Escribe el texto centrado en la caja, con «…» si no cabe.</summary>
    public delegate void Writer(Graphics g, string text, Font font, Color color, Rectangle box);

    /// <summary>Lo más ancha que va una columna (a 96 ppp): en la isla, que es ancha, los nodos no se separan de más.</summary>
    const int MaxColumn = 112;

    int S(int value) => (int)Math.Round(value * scale);

    public static string KindGlyph(ToolKind kind) => kind switch
    {
        ToolKind.Read => "",
        ToolKind.Search => "",
        ToolKind.Edit => "",
        ToolKind.Run => "",
        ToolKind.Agent => "",
        ToolKind.Web => "",
        ToolKind.Ask => "",
        _ => "",
    };

    public static double EaseOut(double p) => 1 - (1 - p) * (1 - p);

    /// <summary>Si el carril lleva nodos: hay pasos o la sesión está pensando el primero.</summary>
    public static bool Shows(SessionInfo s) => s.Flow.Count > 0 || Flows.Thinking(s);

    /// <summary>
    /// Dibuja los nodos desde y y devuelve el borde de abajo. node recibe cada grupo con su columna, también al
    /// medir, para que quien dibuja pueda hacerlo clicable.
    /// </summary>
    public int Compose(Graphics g, bool draw, SessionInfo s, int x0, int width, int y, LaneColors c, Flows.Arrivals arrivals,
        Action<FlowGroup, Rectangle>? node = null)
    {
        var groups = Flows.Groups(s.Flow);
        var thinking = Flows.Thinking(s);
        var shown = groups.TakeLast(thinking ? 2 : 3).ToList();
        var hidden = groups.Count - shown.Count;
        var columns = 1 + (hidden > 0 ? 1 : 0) + shown.Count + (thinking ? 1 : 0);
        var column = Math.Min(width / Math.Max(columns, 4), S(MaxColumn));
        var circle = S(26);
        var tinyHeight = tiny.Height;
        var height = circle + S(4) + 2 * tinyHeight;
        var cy = y + circle / 2;
        var centers = Enumerable.Range(0, columns).Select(i => x0 + i * column + column / 2).ToList();
        var active = Flows.Active(s);

        // Avance de entrada de cada columna (1 = ya está); la línea que llega a un nodo nuevo crece con él.
        var arrival = new double[columns];
        Array.Fill(arrival, 1);
        var first = 1 + (hidden > 0 ? 1 : 0);
        for (var i = 0; i < shown.Count; i++) arrival[first + i] = EaseOut(arrivals.Progress(shown[i].Last.Id));

        if (draw)
        {
            for (var i = 1; i < columns; i++)
            {
                var from = centers[i - 1] + circle / 2 + S(3);
                var to = centers[i] - circle / 2 - S(3);
                var end = from + (to - from) * (float)arrival[i];
                var live = i == columns - 1 && s.State is SessionState.Working or SessionState.Waiting;
                using (var pen = new Pen(live ? Blend(c.Border, c.Accent, 0.5) : c.Border, S(2)))
                    g.DrawLine(pen, from, cy, end, cy);
                if (live && s.State == SessionState.Working && to > from)
                {
                    var phase = Environment.TickCount64 % 1100 / 1100f;
                    var dot = S(6);
                    using var brush = new SolidBrush(c.Accent);
                    g.FillEllipse(brush, from + (to - from - dot) * phase, cy - dot / 2f, dot, dot);
                }
            }
        }

        var index = 0;
        void Node(string mark, string label, string? detail, Color fill, Color iconColor, Color? halo, bool outline, double grow = 1)
        {
            var cx = centers[index++];
            if (!draw) return;
            var size = (float)(circle * (0.45 + 0.55 * grow));
            var box = new RectangleF(cx - size / 2, y + (circle - size) / 2, size, size);
            if (halo is { } h) DrawHalo(g, box, h, S(9));
            using (var brush = new SolidBrush(fill)) g.FillEllipse(brush, box);
            if (outline) using (var pen = new Pen(c.Border)) g.DrawEllipse(pen, box);
            if (grow > 0.7) write(g, mark, mark.StartsWith('+') ? small : glyph, iconColor, Rectangle.Round(box));
            var labelBox = new Rectangle(cx - column / 2, y + circle + S(4), column, tinyHeight);
            write(g, label, tiny, Blend(c.Background, halo is null ? c.Tertiary : c.Text, grow), labelBox);
            if (detail is not null)
                write(g, detail, tiny, Blend(c.Background, c.Tertiary, grow), labelBox with { Y = labelBox.Bottom });
        }

        Node("", L.T("Pedido"), null, c.Background, c.Secondary, null, outline: true);
        if (hidden > 0) Node("+" + hidden, L.T("antes"), null, c.Background, c.Secondary, null, outline: true);
        foreach (var group in shown)
        {
            node?.Invoke(group, new Rectangle(centers[index] - column / 2, y, column, height));
            var isLast = group == shown[^1];
            var running = isLast && active is not null && group.Last == active && !thinking;
            var waiting = isLast && s.State == SessionState.Waiting;
            var failed = isLast && s.State == SessionState.Failed;
            var accent = waiting ? c.Warn : failed ? c.Danger : c.Accent;
            var lit = running || waiting || failed;
            var label = group.Count > 1 ? $"{group.Verb} ×{group.Count}" : group.Verb;
            Node(KindGlyph(group.Kind), label, lit || (isLast && thinking) ? group.Detail : null,
                lit ? accent : c.Background, lit ? c.Background : c.Secondary,
                running || waiting ? accent : null, outline: !lit, grow: arrival[index]);
        }
        if (thinking && draw)
        {
            // El modelo está generando el siguiente paso: el nodo late y tres puntos saltan dentro.
            var cx = centers[index];
            DrawThinking(g, new PointF(cx, cy), circle, c);
            write(g, L.T("Pensando…"), tiny, c.Text, new Rectangle(cx - column / 2, y + circle + S(4), column, tinyHeight));
        }
        return y + height;
    }

    /// <summary>Halo que se expande y se desvanece, en bucle.</summary>
    static void DrawHalo(Graphics g, RectangleF box, Color color, float spread)
    {
        var wave = Environment.TickCount64 % 1400 / 1400.0;
        var grow = (float)(spread * wave);
        using var brush = new SolidBrush(Color.FromArgb((int)(110 * (1 - wave)), color));
        g.FillEllipse(brush, box.X - grow, box.Y - grow, box.Width + 2 * grow, box.Height + 2 * grow);
    }

    /// <summary>Nodo «Pensando»: crece y suelta una onda con cada golpe del latido; tres puntos saltan dentro.</summary>
    void DrawThinking(Graphics g, PointF center, int circle, LaneColors c)
    {
        var tick = Environment.TickCount64;
        var beat = (float)Flows.Heartbeat(tick);
        var size = circle * (1 + 0.16f * beat);
        var box = new RectangleF(center.X - size / 2, center.Y - size / 2, size, size);
        var wave = S(10) * beat;
        using (var halo = new SolidBrush(Color.FromArgb((int)(120 * beat), c.Accent)))
            g.FillEllipse(halo, RectangleF.Inflate(box, wave, wave));
        using (var fill = new SolidBrush(c.Accent)) g.FillEllipse(fill, box);
        DrawDots(g, center, c.Background, tick);
    }

    /// <summary>Tres puntos que saltan uno detrás del otro, centrados en el punto dado.</summary>
    public void DrawDots(Graphics g, PointF center, Color color, long tick)
    {
        var r = Math.Max(1.5f, 1.8f * scale);
        var gap = 5.5f * scale;
        using var brush = new SolidBrush(color);
        for (var i = 0; i < 3; i++)
        {
            var x = center.X + (i - 1) * gap;
            var y = center.Y - (float)(3.5 * scale * Flows.DotBounce(tick, i));
            g.FillEllipse(brush, x - r, y - r, 2 * r, 2 * r);
        }
    }

    static Color Blend(Color from, Color to, double amount) => Color.FromArgb(
        (int)(from.R + (to.R - from.R) * amount),
        (int)(from.G + (to.G - from.G) * amount),
        (int)(from.B + (to.B - from.B) * amount));
}
