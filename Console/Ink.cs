namespace Claupet;

using Avalonia.Media;
using Avalonia.Media.Immutable;

/// <summary>
/// Los colores y las letras de Claupet Console (los del diseño aprobado), en modo claro u oscuro, con el
/// acento de la paleta elegida en Ajustes. Keyword y String son los del código en el chat.
/// </summary>
sealed record Ink(bool Dark, IBrush Window, IBrush Surface, IBrush Raised, IBrush Pill, IBrush Hairline, IBrush Text,
    IBrush Secondary, IBrush Tertiary, IBrush Track, IBrush Accent, IBrush Warn, IBrush Danger, IBrush Keyword, IBrush String)
{
    const string SansChain = "Segoe UI Variable Text, Segoe UI, SF Pro Text, Helvetica Neue, Ubuntu, Cantarell, Noto Sans, DejaVu Sans, Arial";
    const string DisplayChain = "Segoe UI Variable Display, Segoe UI, SF Pro Display, Helvetica Neue, Ubuntu, Noto Sans, Arial";
    static readonly FontFamily SansDefault = new(SansChain), DisplayDefault = new(DisplayChain);
    static (string Font, FontFamily Sans, FontFamily Display)? _themed;

    /// <summary>La letra del texto: la del tema propio elegido (si está instalada) delante de la cadena de siempre.</summary>
    public static FontFamily Sans => Themed()?.Sans ?? SansDefault;
    /// <summary>La de los títulos, con la misma regla que Sans.</summary>
    public static FontFamily Display => Themed()?.Display ?? DisplayDefault;
    /// <summary>Sin ligaduras (Cascadia Mono y no Code): «&lt;=» se ve como se escribe, no como «≤».</summary>
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, SF Mono, Menlo, Ubuntu Mono, DejaVu Sans Mono, monospace");

    public static Ink For(bool dark) => Of(CustomThemes.Selected, dark);

    /// <summary>Los colores de ese tema propio o, con null, los de Claupet con la paleta elegida (las vistas previas de los temas).</summary>
    public static Ink Of(CustomTheme? custom, bool dark)
    {
        if (custom is not null) return From(dark ? custom.Dark : custom.Light, dark);
        var accent = Theme.PaletteAccent(dark);
        var accentBrush = new ImmutableSolidColorBrush(Color.FromRgb(accent.R, accent.G, accent.B));
        return dark
            ? new(true, B("#1E1E1C"), B("#262624"), B("#2F2F2C"), B("#3A3A37"), B("#12FFFFFF"), B("#F5F4EF"),
                B("#BFBDB5"), B("#9A988F"), B("#4A4946"), accentBrush, B("#E0A84B"), B("#F08A7A"), B("#E8987C"), B("#A8C7A0"))
            : new(false, B("#F3F1EB"), B("#F8F6F1"), B("#FFFFFF"), B("#ECE9E1"), B("#14000000"), B("#1F1E1D"),
                B("#5E5D59"), B("#6B6963"), B("#D9D6CC"), accentBrush, B("#8A5A12"), B("#A33A2A"), B("#B4532F"), B("#3F6B3A"));
    }

    /// <summary>
    /// Lo que va escrito sobre un relleno de color (un botón del acento o del peligro): blanco, como en el diseño, si
    /// llega a 3:1; si no (el acento claro de un tema propio), casi negro.
    /// </summary>
    public static IBrush On(IBrush fill)
    {
        if (fill is not ISolidColorBrush { Color: var c }) return Brushes.White;
        var color = System.Drawing.Color.FromArgb(255, c.R, c.G, c.B);
        return CustomThemes.Ratio(color, System.Drawing.Color.White) >= 3 ? Brushes.White : NearBlack;
    }

    static readonly IBrush NearBlack = new ImmutableSolidColorBrush(Color.FromRgb(0x14, 0x14, 0x13));

    public IBrush For(RowTone tone) => tone switch
    {
        RowTone.Muted => Tertiary,
        RowTone.Warn => Warn,
        RowTone.Accent => Accent,
        RowTone.Danger => Danger,
        _ => Text,
    };

    /// <summary>
    /// Los pinceles de un tema propio (ya corregido en CustomThemes): Window es el fondo un poco más hondo, Pill el
    /// Hover, la línea fina la de siempre y el código del chat toma el acento y el éxito.
    /// </summary>
    static Ink From(ThemeColors c, bool dark)
    {
        var window = System.Drawing.Color.FromArgb(255, Deeper(c.Background.R), Deeper(c.Background.G), Deeper(c.Background.B));
        byte Deeper(byte v) => (byte)Math.Round(v * (dark ? 0.8 : 0.975));
        return new(dark, C(window), C(c.Background), C(c.Raised), C(c.Hover), B(dark ? "#12FFFFFF" : "#14000000"), C(c.Text),
            C(c.Secondary), C(c.Tertiary), C(c.Track), C(c.Accent), C(c.Warn), C(c.Danger), C(c.Accent), C(c.Success));
    }

    /// <summary>Las letras del tema elegido, si trae una familia instalada; se arman una vez por familia.</summary>
    static (string Font, FontFamily Sans, FontFamily Display)? Themed()
    {
        if (CustomThemes.Selected?.Font is not { } font || !Installed(font)) return null;
        if (_themed is { } cached && cached.Font == font) return cached;
        _themed = (font, new FontFamily(font + ", " + SansChain), new FontFamily(font + ", " + DisplayChain));
        return _themed;
    }

    /// <summary>La letra de los títulos con esa familia delante (la vista previa de un tema); sin familia, la de siempre.</summary>
    public static FontFamily DisplayWith(string? font) =>
        font is not null && Installed(font) ? new FontFamily(font + ", " + DisplayChain) : DisplayDefault;

    /// <summary>¿Está instalada la familia? Con FontManager de Avalonia; sin él (sin la app iniciada), no.</summary>
    public static bool Installed(string font)
    {
        try { return FontManager.Current.SystemFonts.Any(f => string.Equals(f.Name, font, StringComparison.OrdinalIgnoreCase)); }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Las familias instaladas que sirven para leer (sin las de símbolos ni íconos) y que un tema acepta (letras, cifras,
    /// espacios y guiones), por nombre y hasta 150: las que se le ofrecen a Claude al crear un tema. Vacía si no se pueden leer.
    /// </summary>
    public static IReadOnlyList<string> Families()
    {
        string[] skip = ["symbol", "wingdings", "webdings", "marlett", "emoji", "mdl2", "icons", "holomdl", "dings", "math"];
        try
        {
            return FontManager.Current.SystemFonts.Select(f => f.Name)
                .Where(n => n.Length is > 0 and <= 48 && n.All(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-')
                    && !skip.Any(s => n.Contains(s, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Take(150).ToList();
        }
        catch (Exception) { return []; }
    }

    static ImmutableSolidColorBrush C(System.Drawing.Color c) => new(Color.FromRgb(c.R, c.G, c.B));

    static ImmutableSolidColorBrush B(string hex) => new(Color.Parse(hex));
}
