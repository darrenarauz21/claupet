namespace Claupet;

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

/// <summary>Una serie de un gráfico: su nombre, su color y un valor por tramo.</summary>
sealed record ChartSeries(string Name, IBrush Color, double[] Values);

/// <summary>Las piezas comunes de los gráficos de Ajustes › Consumo: texto y colores.</summary>
static class ChartDraw
{
    public static FormattedText Text(string text, double size, IBrush brush, FontWeight weight = FontWeight.Normal) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Ink.Sans, FontStyle.Normal, weight), size, brush);

    public static Color ColorOf(string hex) => Color.TryParse(hex, out var c) ? c : Colors.Gray;

    public static IBrush Brush(string hex) => new SolidColorBrush(ColorOf(hex));

    /// <summary>
    /// El color del modelo número i de un agente: el del agente y, los siguientes, cada vez más claros o más oscuros,
    /// para que se distingan sin perder de quién son.
    /// </summary>
    public static IBrush Shade(string hex, int i) => Shade(ColorOf(hex), i);

    public static IBrush Shade(Color c, int i)
    {
        if (i == 0) return new SolidColorBrush(c);
        var (toward, amount) = (i % 2 == 1 ? (byte)255 : (byte)0, Math.Min(0.6, 0.22 * ((i + 1) / 2)));
        byte Mix(byte from) => (byte)Math.Round(from + (toward - from) * amount);
        return new SolidColorBrush(Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B)));
    }

    public static IBrush Faded(IBrush brush, double opacity) =>
        brush is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, opacity) : brush;
}

/// <summary>
/// Áreas apiladas, una por serie, con una línea por cada tercio del máximo. Al pasar el mouse, una línea punteada marca el
/// tramo y una tarjeta dice lo de cada serie en él.
/// </summary>
sealed class UsageAreaChart : Control
{
    const double Left = 46, Right = 8, Top = 8, Bottom = 20;

    readonly IReadOnlyList<ChartSeries> _series;
    readonly IReadOnlyList<string> _labels;
    readonly IReadOnlyList<string> _titles;
    readonly Func<double, string> _format;
    readonly Ink _ink;
    int? _hover;

    /// <param name="labels">Lo que dice el eje en cada tramo (vacío: nada).</param>
    /// <param name="titles">El título de cada tramo en la tarjeta («Hoy», «Hace 3 días», «14:00»).</param>
    public UsageAreaChart(IReadOnlyList<ChartSeries> series, IReadOnlyList<string> labels, IReadOnlyList<string> titles, Func<double, string> format, Ink ink)
    {
        _series = series;
        _labels = labels;
        _titles = titles;
        _format = format;
        _ink = ink;
        Height = 170;
        ClipToBounds = false;
    }

    int Count => _labels.Count;

    double X(int i) => Left + (Count <= 1 ? 0 : i * (Bounds.Width - Left - Right) / (Count - 1));

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Count == 0) return;
        var step = Count <= 1 ? 1 : (Bounds.Width - Left - Right) / (Count - 1);
        var i = (int)Math.Clamp(Math.Round((e.GetPosition(this).X - Left) / step), 0, Count - 1);
        if (i == _hover) return;
        _hover = i;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        context.FillRectangle(Brushes.Transparent, new Rect(size)); // para recibir el mouse en todo el gráfico
        if (Count == 0 || size.Width <= Left + Right) return;
        var stacks = new double[Count][];
        var max = 0.0;
        for (var i = 0; i < Count; i++)
        {
            stacks[i] = new double[_series.Count];
            var sum = 0.0;
            for (var j = 0; j < _series.Count; j++) stacks[i][j] = sum += _series[j].Values[i];
            max = Math.Max(max, sum);
        }
        max = max <= 0 ? 1 : max * 1.1;
        var height = size.Height - Top - Bottom;
        double Y(double v) => Top + height * (1 - v / max);

        var grid = new Pen(_ink.Hairline, 1);
        foreach (var k in new[] { 0, 0.5, 1 })
        {
            var y = Y(max * k);
            context.DrawLine(grid, new Point(Left, y), new Point(size.Width - Right, y));
            var label = ChartDraw.Text(_format(max * k), 10, _ink.Tertiary);
            context.DrawText(label, new Point(Left - 6 - label.Width, y - label.Height / 2));
        }

        // De arriba hacia abajo, para que cada capa tape solo su parte.
        for (var j = _series.Count - 1; j >= 0; j--)
        {
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(new Point(X(0), Y(stacks[0][j])), true);
                for (var i = 1; i < Count; i++) path.LineTo(new Point(X(i), Y(stacks[i][j])));
                for (var i = Count - 1; i >= 0; i--) path.LineTo(new Point(X(i), Y(j == 0 ? 0 : stacks[i][j - 1])));
                path.EndFigure(true);
            }
            context.DrawGeometry(ChartDraw.Faded(_series[j].Color, 0.78), new Pen(_series[j].Color, 1), geometry);
        }

        for (var i = 0; i < Count; i++)
        {
            if (_labels[i].Length == 0) continue;
            var label = ChartDraw.Text(_labels[i], 10, _ink.Tertiary);
            var x = i == 0 ? X(i) : i == Count - 1 ? X(i) - label.Width : X(i) - label.Width / 2;
            context.DrawText(label, new Point(x, size.Height - label.Height));
        }

        if (_hover is not { } h) return;
        var cross = new Pen(_ink.Secondary, 1, new DashStyle([2, 3], 0));
        context.DrawLine(cross, new Point(X(h), Top), new Point(X(h), Top + height));
        // La tarjeta: el tramo con su total y cada serie con lo suyo (las que tienen algo).
        var lines = new List<(IBrush? Color, FormattedText Name, FormattedText? Value)>
        {
            (null, ChartDraw.Text($"{_titles[h]} · {_format(stacks[h].LastOrDefault())}", 11.5, _ink.Text, FontWeight.SemiBold), null),
        };
        for (var j = 0; j < _series.Count; j++)
            if (_series[j].Values[h] > 0)
                lines.Add((_series[j].Color, ChartDraw.Text(_series[j].Name, 11.5, _ink.Secondary), ChartDraw.Text(_format(_series[j].Values[h]), 11.5, _ink.Text)));
        var width = lines.Max(l => (l.Color is null ? 0 : 14) + l.Name.Width + (l.Value is null ? 0 : 14 + l.Value.Width)) + 16;
        var tall = lines.Sum(l => l.Name.Height + 2) + 10;
        var left = X(h) + 10 + width > size.Width ? X(h) - 10 - width : X(h) + 10;
        var box = new Rect(Math.Max(0, left), Top, width, tall);
        context.DrawRectangle(_ink.Raised, new Pen(_ink.Hairline, 1), box, 8, 8);
        var top = box.Y + 5;
        foreach (var (color, name, value) in lines)
        {
            var x = box.X + 8;
            if (color is not null)
            {
                context.DrawEllipse(color, null, new Point(x + 3.5, top + name.Height / 2), 3.5, 3.5);
                x += 14;
            }
            context.DrawText(name, new Point(x, top));
            if (value is not null) context.DrawText(value, new Point(box.Right - 8 - value.Width, top));
            top += name.Height + 2;
        }
    }
}

/// <summary>Una dona con el reparto: un arco por parte, y en el centro el total con lo que mide.</summary>
sealed class UsageDonut(IReadOnlyList<(string Name, IBrush Color, double Value)> parts, string total, string caption, Ink ink) : Control
{
    const double Thickness = 14;

    public override void Render(DrawingContext context)
    {
        var radius = (Math.Min(Bounds.Width, Bounds.Height) - Thickness) / 2;
        if (radius <= 0) return;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        context.DrawEllipse(null, new Pen(ink.Track, Thickness), center, radius, radius);
        var sum = parts.Sum(p => p.Value);
        if (sum > 0)
        {
            var angle = 0.0;
            foreach (var (_, color, value) in parts)
            {
                var sweep = value / sum * Math.PI * 2;
                if (sweep <= 0) continue;
                var pen = new Pen(color, Thickness);
                if (sweep >= Math.PI * 2 - 0.001) context.DrawEllipse(null, pen, center, radius, radius);
                else
                {
                    // Un poco de aire entre las partes.
                    var gap = parts.Count(p => p.Value > 0) > 1 ? Math.Min(0.03, sweep / 4) : 0;
                    var start = angle + gap / 2;
                    var end = angle + sweep - gap / 2;
                    var geometry = new StreamGeometry();
                    using (var path = geometry.Open())
                    {
                        path.BeginFigure(new Point(center.X + radius * Math.Sin(start), center.Y - radius * Math.Cos(start)), false);
                        path.ArcTo(new Point(center.X + radius * Math.Sin(end), center.Y - radius * Math.Cos(end)), new Size(radius, radius), 0, end - start > Math.PI, SweepDirection.Clockwise);
                        path.EndFigure(false);
                    }
                    context.DrawGeometry(null, pen, geometry);
                }
                angle += sweep;
            }
        }
        var big = ChartDraw.Text(total, 15, ink.Text, FontWeight.SemiBold);
        var small = ChartDraw.Text(caption, 9.5, ink.Tertiary);
        context.DrawText(big, new Point(center.X - big.Width / 2, center.Y - big.Height + 2));
        context.DrawText(small, new Point(center.X - small.Width / 2, center.Y + 2));
    }
}

/// <summary>El mapa de calor: siete filas (de lunes a domingo) y 24 columnas (las horas), más intenso donde hubo más respuestas.</summary>
sealed class UsageHeat(int[,] heat, IReadOnlyList<string> days, Ink ink) : Control
{
    const double Label = 28, Head = 14, Gap = 2;

    public UsageHeat(int[,] heat, IReadOnlyList<string> days, Ink ink, int rows) : this(heat, days, ink) => Height = Head + rows * 12 + (rows - 1) * Gap;

    public override void Render(DrawingContext context)
    {
        var max = 0;
        foreach (var v in heat) max = Math.Max(max, v);
        var cell = (Bounds.Width - Label - Gap * 23) / 24;
        if (cell <= 1) return;
        var accent = ink.Accent is ISolidColorBrush solid ? solid.Color : Colors.SteelBlue;
        for (var h = 0; h < 24; h += 6)
        {
            var label = ChartDraw.Text(h.ToString(CultureInfo.CurrentCulture), 9.5, ink.Tertiary);
            context.DrawText(label, new Point(Label + h * (cell + Gap), 0));
        }
        for (var d = 0; d < 7; d++)
        {
            var y = Head + d * (12 + Gap);
            var name = ChartDraw.Text(days[d], 9.5, ink.Tertiary);
            context.DrawText(name, new Point(0, y + (12 - name.Height) / 2));
            for (var h = 0; h < 24; h++)
            {
                var v = max == 0 ? 0 : (double)heat[d, h] / max;
                var brush = heat[d, h] == 0 ? ink.Track : new SolidColorBrush(accent, 0.18 + 0.82 * v);
                context.FillRectangle(brush, new Rect(Label + h * (cell + Gap), y, cell, 12), 2);
            }
        }
    }
}

/// <summary>La minilínea de una fila: su tendencia en el periodo.</summary>
sealed class UsageSpark : Control
{
    readonly double[] _values;
    readonly IBrush _color;

    public UsageSpark(double[] values, IBrush color)
    {
        _values = values;
        _color = color;
        Width = 56;
        Height = 16;
    }

    public override void Render(DrawingContext context)
    {
        var max = _values.Length == 0 ? 0 : _values.Max();
        if (_values.Length < 2) return;
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            for (var i = 0; i < _values.Length; i++)
            {
                var point = new Point(i * Bounds.Width / (_values.Length - 1), Bounds.Height - 1 - (max <= 0 ? 0 : _values[i] / max) * (Bounds.Height - 3));
                if (i == 0) path.BeginFigure(point, false);
                else path.LineTo(point);
            }
            path.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(_color, 1.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
    }
}

/// <summary>Una barra de partes, de izquierda a derecha (los proyectos), o una sola parte sobre su pista (un límite).</summary>
sealed class UsageBar(IReadOnlyList<(double Value, IBrush Color)> parts, IBrush track, double total = 0) : Control
{
    public override void Render(DrawingContext context)
    {
        var radius = Bounds.Height / 2;
        var whole = new Rect(Bounds.Size);
        context.DrawRectangle(track, null, whole, radius, radius);
        var sum = total > 0 ? total : parts.Sum(p => p.Value);
        if (sum <= 0) return;
        using (context.PushClip(new RoundedRect(whole, radius)))
        {
            var x = 0.0;
            foreach (var (value, color) in parts)
            {
                var width = Math.Max(0, value / sum * Bounds.Width);
                context.FillRectangle(color, new Rect(x, 0, width, Bounds.Height));
                x += width;
            }
        }
    }
}
