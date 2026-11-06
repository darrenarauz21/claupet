using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
// El proyecto de pruebas también trae WinForms (nombres iguales a los de Avalonia).
using Border = Avalonia.Controls.Border;
using Orientation = Avalonia.Layout.Orientation;
using Point = Avalonia.Point;
using ScrollBar = Avalonia.Controls.Primitives.ScrollBar;
using Window = Avalonia.Controls.Window;

namespace Claupet.Tests;

/// <summary>
/// La barra de desplazamiento del tema propio (Themes): sigue al contenido cuando se desplaza con la rueda o el panel
/// táctil, y al arrastrarla mueve el contenido.
/// </summary>
public sealed class ScrollBarThemeTests
{
    [Fact]
    public void LaBarraSigueALaRuedaDespuesDelPrimerPaso() => Headless.Run(() =>
    {
        var (window, scroll, bar, thumb) = Open();
        var before = thumb.TranslatePoint(default, bar)!.Value.Y;

        // Con el enlace de antes solo el primer paso movía la barra: después quedaba quieta y el contenido seguía.
        for (var i = 0; i < 20; i++) window.MouseWheel(new Point(150, 150), new Vector(0, -1));
        Headless.Frame(window);

        Assert.True(scroll.Offset.Y > 100, $"offset {scroll.Offset.Y}");
        Assert.Equal(scroll.Offset.Y, bar.Value, 1);
        Assert.True(thumb.TranslatePoint(default, bar)!.Value.Y > before + 50);
        window.Close();
    });

    [Fact]
    public void ArrastrarLaBarraMueveElContenidoYLuegoSigueALaRueda() => Headless.Run(() =>
    {
        var (window, scroll, _, thumb) = Open();
        var start = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Point(0, 100), RawInputModifiers.LeftMouseButton);
        window.MouseUp(start + new Point(0, 100), MouseButton.Left);
        Headless.Frame(window);
        var dragged = scroll.Offset.Y;
        Assert.True(dragged > 100, $"offset {dragged}");

        var bar = thumb.FindAncestorOfType<ScrollBar>()!;
        for (var i = 0; i < 5; i++) window.MouseWheel(new Point(150, 150), new Vector(0, -1));
        Headless.Frame(window);
        Assert.True(scroll.Offset.Y > dragged, $"offset {scroll.Offset.Y}");
        Assert.Equal(scroll.Offset.Y, bar.Value, 1);
        window.Close();
    });

    static (Window, ScrollViewer, ScrollBar, Thumb) Open()
    {
        var scroll = new ScrollViewer { Content = new Border { Height = 3000 } };
        var window = new Window { Width = 400, Height = 400, Content = scroll };
        Themes.Register(window.Resources);
        Themes.Paint(window.Resources, Ink.For(false));
        window.Show();
        Headless.Frame(window);
        var bar = scroll.GetVisualDescendants().OfType<ScrollBar>().First(b => b.Orientation == Orientation.Vertical);
        return (window, scroll, bar, bar.GetVisualDescendants().OfType<Thumb>().First());
    }
}
