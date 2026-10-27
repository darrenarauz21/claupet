namespace Claupet;

using Avalonia.Media;
using Avalonia.Media.Immutable;

/// <summary>
/// Los colores y las letras de Claupet Console (los del diseño aprobado), en modo claro u oscuro, con el
/// acento de la paleta elegida en Ajustes.
/// </summary>
sealed record Ink(bool Dark, IBrush Window, IBrush Surface, IBrush Raised, IBrush Pill, IBrush Hairline, IBrush Text,
    IBrush Secondary, IBrush Tertiary, IBrush Track, IBrush Accent, IBrush Warn, IBrush Danger)
{
    public static readonly FontFamily Sans = new("Segoe UI Variable Text, Segoe UI, SF Pro Text, Helvetica Neue, Ubuntu, Cantarell, Noto Sans, DejaVu Sans, Arial");
    public static readonly FontFamily Display = new("Segoe UI Variable Display, Segoe UI, SF Pro Display, Helvetica Neue, Ubuntu, Noto Sans, Arial");

    public static Ink For(bool dark)
    {
        var palette = Theme.Palettes.FirstOrDefault(p => p.Id == Theme.SelectedPalette);
        if (palette.Id is null) palette = Theme.Palettes[0];
        var accent = dark ? palette.Dark : palette.Light;
        var accentBrush = new ImmutableSolidColorBrush(Color.FromRgb(accent.R, accent.G, accent.B));
        return dark
            ? new(true, B("#1E1E1C"), B("#262624"), B("#2F2F2C"), B("#3A3A37"), B("#12FFFFFF"), B("#F5F4EF"),
                B("#BFBDB5"), B("#9A988F"), B("#4A4946"), accentBrush, B("#E0A84B"), B("#F08A7A"))
            : new(false, B("#F3F1EB"), B("#F8F6F1"), B("#FFFFFF"), B("#ECE9E1"), B("#14000000"), B("#1F1E1D"),
                B("#5E5D59"), B("#6B6963"), B("#D9D6CC"), accentBrush, B("#8A5A12"), B("#A33A2A"));
    }

    public IBrush For(RowTone tone) => tone switch
    {
        RowTone.Muted => Tertiary,
        RowTone.Warn => Warn,
        RowTone.Accent => Accent,
        RowTone.Danger => Danger,
        _ => Text,
    };

    static ImmutableSolidColorBrush B(string hex) => new(Color.Parse(hex));
}
