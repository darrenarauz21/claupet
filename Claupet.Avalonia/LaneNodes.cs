using Avalonia;
using Avalonia.Media;
using SD = System.Drawing;

namespace Claupet;

/// <summary>Los colores con que se dibuja un carril: los del tema en la bandeja, los de siempre en la isla.</summary>
readonly record struct LaneColors(SD.Color Background, SD.Color Border, SD.Color Text, SD.Color Secondary, SD.Color Tertiary,
    SD.Color Accent, SD.Color Warn, SD.Color Danger);

/// <summary>
/// Los nodos de un carril de Actividad: «Pedido», «+N» con lo anterior y los últimos grupos, unidos por líneas. El
/// último late (o «Pensando…» si ya volvió) y un punto viaja hacia él; cada paso nuevo entra creciendo. Los dibujan
/// la vista Actividad de la bandeja y la pestaña Actividad de la isla, cada una con sus colores (como en Windows).
/// </summary>
static class LaneNodes
{
    const double Circle = 26;

    public static Glyph KindGlyph(ToolKind kind) => kind switch
    {
        ToolKind.Read => Glyph.Read,
        ToolKind.Search => Glyph.Search,
        ToolKind.Edit => Glyph.Edit,
        ToolKind.Run => Glyph.Run,
        ToolKind.Agent => Glyph.Agent,
        ToolKind.Web => Glyph.Web,
        ToolKind.Ask => Glyph.Ask,
        _ => Glyph.Other,
    };

    public static double EaseOut(double p) => 1 - (1 - p) * (1 - p);

    public static double Wave(int periodMs) => Environment.TickCount64 % periodMs / (double)periodMs;

    /// <summary>Si el carril lleva nodos: hay pasos o la sesión está pensando el primero.</summary>
    public static bool Shows(SessionInfo s) => s.Flow.Count > 0 || Flows.Thinking(s);

    /// <summary>
    /// Dibuja los nodos desde y (sin contexto, solo mide) y devuelve el borde de abajo. maxColumn: lo más ancha que va una
    /// columna (en la isla, que es ancha, los nodos no se separan de más). node recibe cada grupo con su columna, también
    /// al medir, para que quien dibuja pueda hacerlo clicable.
    /// </summary>
    public static double Compose(DrawingContext? dc, SessionInfo s, double x0, double width, double y, LaneColors c, Flows.Arrivals arrivals,
        double maxColumn = double.MaxValue, Action<FlowGroup, Rect>? node = null)
    {
        var groups = Flows.Groups(s.Flow);
        var thinking = Flows.Thinking(s);
        var shown = groups.TakeLast(thinking ? 2 : 3).ToList();
        var hidden = groups.Count - shown.Count;
        var columns = 1 + (hidden > 0 ? 1 : 0) + shown.Count + (thinking ? 1 : 0);
        var column = Math.Min(width / Math.Max(columns, 4), maxColumn);
        var tiny = Gfx.Tiny.LineHeight;
        var height = Circle + 4 + 2 * tiny;
        var cy = y + Circle / 2;
        var centers = Enumerable.Range(0, columns).Select(i => x0 + i * column + column / 2).ToList();
        var active = Flows.Active(s);

        // Avance de entrada de cada columna (1 = ya está); la línea que llega a un nodo nuevo crece con él.
        var arrival = new double[columns];
        Array.Fill(arrival, 1);
        var first = 1 + (hidden > 0 ? 1 : 0);
        for (var i = 0; i < shown.Count; i++) arrival[first + i] = EaseOut(arrivals.Progress(shown[i].Last.Id));

        if (dc is not null)
        {
            for (var i = 1; i < columns; i++)
            {
                var from = centers[i - 1] + Circle / 2 + 3;
                var to = centers[i] - Circle / 2 - 3;
                var live = i == columns - 1 && s.State is SessionState.Working or SessionState.Waiting;
                dc.DrawLine(Gfx.P(live ? Gfx.Blend(c.Border, c.Accent, 0.5) : c.Border, 2), new Point(from, cy), new Point(from + (to - from) * arrival[i], cy));
                if (live && s.State == SessionState.Working && to > from)
                    dc.DrawEllipse(Gfx.B(c.Accent), null, new Point(from + 3 + (to - from - 6) * Wave(1100), cy), 3, 3);
            }
        }

        var index = 0;
        void Node(Glyph? glyph, string? chip, string label, string? detail, SD.Color fill, SD.Color iconColor, SD.Color? halo, bool outline, double grow = 1)
        {
            var cx = centers[index++];
            if (dc is null) return;
            var size = Circle * (0.45 + 0.55 * grow);
            var box = new Rect(cx - size / 2, y + (Circle - size) / 2, size, size);
            if (halo is { } h) DrawHalo(dc, box, h, 9);
            dc.DrawEllipse(Gfx.B(fill), outline ? Gfx.P(c.Border) : null, box);
            if (grow > 0.7)
            {
                if (glyph is { } g) Gfx.Icon(dc, g, iconColor, box, 13);
                else if (chip is not null) Gfx.Write(dc, chip, Gfx.Small, iconColor, box, TextAlignment.Center, center: true);
            }
            var labelBox = new Rect(cx - column / 2, y + Circle + 4, column, tiny);
            Gfx.Write(dc, label, Gfx.Tiny, Gfx.Blend(c.Background, halo is null ? c.Tertiary : c.Text, grow), labelBox, TextAlignment.Center);
            if (detail is not null)
                Gfx.Write(dc, detail, Gfx.Tiny, Gfx.Blend(c.Background, c.Tertiary, grow), new Rect(labelBox.X, labelBox.Bottom, column, tiny), TextAlignment.Center);
        }

        Node(Glyph.Message, null, L.T("Pedido"), null, c.Background, c.Secondary, null, outline: true);
        if (hidden > 0) Node(null, "+" + hidden, L.T("antes"), null, c.Background, c.Secondary, null, outline: true);
        foreach (var group in shown)
        {
            node?.Invoke(group, new Rect(centers[index] - column / 2, y, column, height));
            var isLast = group == shown[^1];
            var running = isLast && active is not null && group.Last == active && !thinking;
            var waiting = isLast && s.State == SessionState.Waiting;
            var failed = isLast && s.State == SessionState.Failed;
            var accent = waiting ? c.Warn : failed ? c.Danger : c.Accent;
            var lit = running || waiting || failed;
            var label = group.Count > 1 ? $"{group.Verb} ×{group.Count}" : group.Verb;
            Node(KindGlyph(group.Kind), null, label, lit || (isLast && thinking) ? group.Detail : null,
                lit ? accent : c.Background, lit ? c.Background : c.Secondary,
                running || waiting ? accent : null, outline: !lit, grow: arrival[index]);
        }
        if (thinking && dc is not null)
        {
            // El modelo está generando el siguiente paso: el nodo late y tres puntos saltan dentro.
            var cx = centers[index];
            DrawThinking(dc, new Point(cx, cy), c);
            Gfx.Write(dc, L.T("Pensando…"), Gfx.Tiny, c.Text, new Rect(cx - column / 2, y + Circle + 4, column, tiny), TextAlignment.Center);
        }
        return y + height;
    }

    /// <summary>Halo que se expande y se desvanece, en bucle.</summary>
    public static void DrawHalo(DrawingContext dc, Rect box, SD.Color color, double spread)
    {
        var wave = Wave(1400);
        var grow = spread * wave;
        dc.DrawEllipse(Gfx.B(SD.Color.FromArgb((int)(110 * (1 - wave)), color)), null, box.Inflate(grow));
    }

    /// <summary>Nodo «Pensando»: crece y suelta una onda con cada golpe del latido; tres puntos saltan dentro.</summary>
    static void DrawThinking(DrawingContext dc, Point center, LaneColors c)
    {
        var tick = Environment.TickCount64;
        var beat = Flows.Heartbeat(tick);
        var radius = Circle / 2 * (1 + 0.16 * beat);
        dc.DrawEllipse(Gfx.B(SD.Color.FromArgb((int)(120 * beat), c.Accent)), null, center, radius + 10 * beat, radius + 10 * beat);
        dc.DrawEllipse(Gfx.B(c.Accent), null, center, radius, radius);
        DrawDots(dc, center, c.Background, tick);
    }

    /// <summary>Tres puntos que saltan uno detrás del otro, centrados en el punto dado.</summary>
    public static void DrawDots(DrawingContext dc, Point center, SD.Color color, long tick)
    {
        for (var i = 0; i < 3; i++)
            dc.DrawEllipse(Gfx.B(color), null, new Point(center.X + (i - 1) * 5.5, center.Y - 3.5 * Flows.DotBounce(tick, i)), 1.8, 1.8);
    }
}
