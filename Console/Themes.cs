namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;

/// <summary>
/// Cómo se ven en toda la Console las piezas que trae Avalonia: la barra de desplazamiento, como la de la isla (fina,
/// sin flechas, tenue y más clara con el cursor encima o al arrastrarla), y el globo de ayuda, como lo que flota en el
/// diseño (borde fino, esquinas redondeadas, la letra de la app). Sus colores los pone la ventana según el tema (Paint).
/// </summary>
static class Themes
{
    const string ThumbKey = "ClaupetScrollThumb", ThumbHoverKey = "ClaupetScrollThumbHover";
    const string TipBackKey = "ClaupetTipBack", TipTextKey = "ClaupetTipText", TipLineKey = "ClaupetTipLine";

    /// <summary>Se registran una vez, en los recursos de la aplicación.</summary>
    public static void Register(IResourceDictionary resources)
    {
        resources[typeof(ScrollBar)] = ScrollBarTheme();
        resources[typeof(ToolTip)] = ToolTipTheme();
    }

    /// <summary>Los colores de este tema: la barra con el gris terciario (tenue o entero) y el globo como una tarjeta.</summary>
    public static void Paint(IResourceDictionary resources, Ink ink)
    {
        var color = ink.Tertiary is ISolidColorBrush solid ? solid.Color : Colors.Gray;
        resources[ThumbKey] = new SolidColorBrush(color, 0.45);
        resources[ThumbHoverKey] = new SolidColorBrush(color, 0.9);
        resources[TipBackKey] = ink.Raised;
        resources[TipTextKey] = ink.Text;
        resources[TipLineKey] = ink.Hairline;
    }

    static ControlTheme ToolTipTheme() => new(typeof(ToolTip))
    {
        Setters =
        {
            new Setter(TemplatedControl.BackgroundProperty, new DynamicResourceExtension(TipBackKey)),
            new Setter(TemplatedControl.ForegroundProperty, new DynamicResourceExtension(TipTextKey)),
            new Setter(TemplatedControl.BorderBrushProperty, new DynamicResourceExtension(TipLineKey)),
            new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
            new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(10)),
            new Setter(TemplatedControl.PaddingProperty, new Thickness(10, 6)),
            new Setter(TemplatedControl.FontSizeProperty, 12.5),
            new Setter(TemplatedControl.FontFamilyProperty, Ink.Sans),
            new Setter(Layoutable.MaxWidthProperty, 320.0),
            new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<ToolTip>((tip, _) => new Border
            {
                [!Border.BackgroundProperty] = tip[!TemplatedControl.BackgroundProperty],
                [!Border.BorderBrushProperty] = tip[!TemplatedControl.BorderBrushProperty],
                [!Border.BorderThicknessProperty] = tip[!TemplatedControl.BorderThicknessProperty],
                [!Border.CornerRadiusProperty] = tip[!TemplatedControl.CornerRadiusProperty],
                [!Decorator.PaddingProperty] = tip[!TemplatedControl.PaddingProperty],
                Child = new ContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    [!ContentPresenter.ContentProperty] = tip[!ContentControl.ContentProperty],
                    [!ContentPresenter.ContentTemplateProperty] = tip[!ContentControl.ContentTemplateProperty],
                },
            })),
        },
    };

    static ControlTheme ScrollBarTheme()
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
