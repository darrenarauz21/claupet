namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SD = System.Drawing;

/// <summary>Las piezas chicas de la Console: textos, lo que se toca, el chevrón y la cabeza del chibi.</summary>
static class Ui
{
    public static readonly Cursor Hand = new(StandardCursorType.Hand);

    public static TextBlock Text(string text, double size, IBrush brush, FontWeight weight = FontWeight.Normal, FontFamily? family = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = brush,
        FontWeight = weight,
        FontFamily = family ?? Ink.Sans,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// Algo que se toca con el mouse o con Enter/Espacio (con Tab se llega). hover recibe si el puntero
    /// está encima, para cambiar el fondo.
    /// </summary>
    public static Border Press(Control child, Action onClick, Action<Border, bool>? hover = null, string? name = null)
    {
        var border = new Border { Child = child, Cursor = Hand, Focusable = true, Background = Brushes.Transparent };
        if (name is not null) AutomationName(border, name);
        var pressed = false;
        border.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;
            pressed = true;
            e.Handled = true;
        };
        border.PointerReleased += (_, e) =>
        {
            if (!pressed) return;
            pressed = false;
            e.Handled = true;
            if (border.Bounds.WithX(0).WithY(0).Contains(e.GetPosition(border))) onClick();
        };
        border.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            e.Handled = true;
            onClick();
        };
        if (hover is not null)
        {
            border.PointerEntered += (_, _) => hover(border, true);
            border.PointerExited += (_, _) => hover(border, false);
        }
        return border;
    }

    public static void AutomationName(Control control, string name) => Avalonia.Automation.AutomationProperties.SetName(control, name);

    /// <summary>La lista que mandó la bandeja, junto a lo que la pidió. reply: la elegida, o null si se cerró sin elegir.</summary>
    public static void ShowOptions(ConsoleMessage message, Control anchor, Action<string?> reply)
    {
        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        var picked = false;
        foreach (var option in message.Options ?? [])
        {
            var item = new MenuItem { Header = option.Label, ToggleType = MenuItemToggleType.Radio, IsChecked = option.Id == message.Value };
            var id = option.Id;
            item.Click += (_, _) =>
            {
                if (picked) return;
                picked = true;
                reply(id);
            };
            flyout.Items.Add(item);
        }
        // El clic llega antes o después del cierre según el caso: se espera una vuelta para saber si se eligió.
        flyout.Closed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (picked) return;
            picked = true;
            reply(null);
        });
        flyout.ShowAt(anchor);
    }

    public static Avalonia.Controls.Shapes.Path Chevron(IBrush brush) => new()
    {
        Data = Geometry.Parse("M3.25,4.9 L6.5,8.1 L9.75,4.9"),
        Stroke = brush,
        StrokeThickness = 1.6,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
        Width = 13,
        Height = 13,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Los cuadritos de la cabeza del chibi, recortados a lo que pinta.</summary>
    public static readonly (IBrush Brush, Rect Rect)[] Head = HeadPixels();

    static (IBrush, Rect)[] HeadPixels()
    {
        var rects = new List<(SD.Color Color, Rect Rect)>();
        ChibiSprite.PaintHead((color, x, y, w, h) => rects.Add((color, new Rect(x, y, w, h))), MascotFrame.Open);
        if (rects.Count == 0) return [];
        var left = rects.Min(r => r.Rect.Left);
        var top = rects.Min(r => r.Rect.Top);
        return rects.Select(r => ((IBrush)new SolidColorBrush(Color.FromArgb(r.Color.A, r.Color.R, r.Color.G, r.Color.B)),
            r.Rect.Translate(new Vector(-left, -top)))).ToArray();
    }

    static Size HeadSize => Head.Length == 0 ? new Size(1, 1) : new Size(Head.Max(h => h.Rect.Right), Head.Max(h => h.Rect.Bottom));

    public static void PaintHead(DrawingContext dc, Rect box)
    {
        var size = HeadSize;
        var unit = Math.Min(box.Width / size.Width, box.Height / size.Height);
        var x0 = box.X + (box.Width - size.Width * unit) / 2;
        var y0 = box.Y + (box.Height - size.Height * unit) / 2;
        using (dc.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Aliased }))
            foreach (var (brush, r) in Head)
                dc.FillRectangle(brush, new Rect(x0 + r.X * unit, y0 + r.Y * unit, r.Width * unit, r.Height * unit));
    }

    /// <summary>El ícono de la ventana: la cabeza del chibi.</summary>
    public static WindowIcon HeadIcon()
    {
        using var bmp = new RenderTargetBitmap(new PixelSize(64, 64), new Vector(96, 96));
        using (var dc = bmp.CreateDrawingContext()) PaintHead(dc, new Rect(0, 0, 64, 64));
        var stream = new MemoryStream();
        bmp.Save(stream);
        stream.Position = 0;
        return new WindowIcon(stream);
    }
}

/// <summary>La cabeza del chibi como logo, del tamaño que se le dé.</summary>
sealed class HeadLogo : Control
{
    public override void Render(DrawingContext context) => Ui.PaintHead(context, new Rect(Bounds.Size));
}
