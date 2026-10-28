namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;

/// <summary>
/// La barra de desplazamiento de toda la Console, como la de la isla: fina, sin flechas, tenue, y más clara con el
/// cursor encima o al arrastrarla. Sus colores (ThumbKey y ThumbHoverKey) los pone la ventana según el tema.
/// </summary>
static class ScrollTheme
{
    public const string ThumbKey = "ClaupetScrollThumb", ThumbHoverKey = "ClaupetScrollThumbHover";

    /// <summary>Los colores de la barra para este tema: el gris terciario, tenue o entero.</summary>
    public static void Paint(IResourceDictionary resources, Ink ink)
    {
        var color = ink.Tertiary is ISolidColorBrush solid ? solid.Color : Colors.Gray;
        resources[ThumbKey] = new SolidColorBrush(color, 0.45);
        resources[ThumbHoverKey] = new SolidColorBrush(color, 0.9);
    }

    public static ControlTheme Theme()
    {
        var thumb = new ControlTheme(typeof(Thumb))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, new DynamicResourceExtension(ThumbKey)),
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<Thumb>((t, _) => new Border
                {
                    CornerRadius = new CornerRadius(3),
                    [!Border.BackgroundProperty] = t[!TemplatedControl.BackgroundProperty],
                })),
            },
            Children =
            {
                new Style(x => x.Nesting().Class(":pointerover"))
                {
                    Setters = { new Setter(TemplatedControl.BackgroundProperty, new DynamicResourceExtension(ThumbHoverKey)) },
                },
                new Style(x => x.Nesting().Class(":pressed"))
                {
                    Setters = { new Setter(TemplatedControl.BackgroundProperty, new DynamicResourceExtension(ThumbHoverKey)) },
                },
            },
        };
        return new ControlTheme(typeof(ScrollBar))
        {
            Setters =
            {
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<ScrollBar>((bar, _) =>
                {
                    var track = new Track
                    {
                        Thumb = new Thumb { Theme = thumb },
                        [!Track.MinimumProperty] = bar[!RangeBase.MinimumProperty],
                        [!Track.MaximumProperty] = bar[!RangeBase.MaximumProperty],
                        [!!Track.ValueProperty] = bar[!!RangeBase.ValueProperty],
                        [!Track.ViewportSizeProperty] = bar[!ScrollBar.ViewportSizeProperty],
                        [!Track.OrientationProperty] = bar[!ScrollBar.OrientationProperty],
                    };
                    var box = new Border { Background = Brushes.Transparent, Child = track };
                    void Fit()
                    {
                        // Vertical, el valor crece hacia abajo; la barra mide 10 y el cuadradito 4 de ancho.
                        var vertical = bar.Orientation == Orientation.Vertical;
                        track.IsDirectionReversed = vertical;
                        box.Width = vertical ? 10 : double.NaN;
                        box.Height = vertical ? double.NaN : 10;
                        box.Padding = vertical ? new Thickness(3, 4) : new Thickness(4, 3);
                    }
                    Fit();
                    bar.PropertyChanged += (_, e) =>
                    {
                        if (e.Property == ScrollBar.OrientationProperty) Fit();
                    };
                    return box;
                })),
            },
        };
    }
}
