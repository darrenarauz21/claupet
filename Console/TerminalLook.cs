namespace Claupet;

using System.Text.Json;
using Avalonia.Media;
using Avalonia.Media.Immutable;

/// <summary>
/// Cómo se ve Claupet Terminal (Ajustes › Terminal): la letra de las sesiones, su tamaño, el interlineado, el color del
/// texto, la paleta de los programas, el cursor, el fondo, las animaciones, las ligaduras, la lista de pestañas (con
/// descripción o solo íconos) y la terminal por defecto. Se guarda en terminal.json, aparte de los ajustes de la bandeja:
/// la Terminal no la necesita.
/// </summary>
sealed class TerminalLook
{
    /// <summary>La letra elegida; null, la de siempre (Cascadia Mono o la que haya en el sistema).</summary>
    public string? Font { get; set; }
    public double Size { get; set; } = DefaultSize;
    public double LineSpacing { get; set; } = 1.55;
    /// <summary>El color del texto: «tema» (el del tema), «ambar», «verde» o «hielo».</summary>
    public string Text { get; set; } = "tema";
    /// <summary>El fondo: «tema», «profundo» (el de la ventana) o «negro» (consola, siempre oscuro).</summary>
    public string Background { get; set; } = "tema";
    public bool Animate { get; set; } = true;
    public bool IconsOnly { get; set; }
    /// <summary>Los 16 colores que usan los programas (los de la pantalla completa): «claupet», «suave» o «viva».</summary>
    public string Palette { get; set; } = "claupet";
    /// <summary>El cursor de la pantalla completa: «bloque», «barra» o «subrayado».</summary>
    public string Cursor { get; set; } = "bloque";
    /// <summary>Une -> y != en un solo signo (si la letra los tiene). Apagado, cada carácter se ve como se escribió.</summary>
    public bool Ligatures { get; set; }
    /// <summary>La terminal de «Nueva terminal aquí» (el id: «powershell», «bash», «wsl», «zsh»); null, la primera que haya.</summary>
    public string? DefaultShell { get; set; }
    /// <summary>
    /// Cuánto se transparentan, en %, la pantalla completa de las terminales, sus bloques y su caja: con el fondo del tema,
    /// se ve su escena detrás (como la opacidad de Windows Terminal, sin desenfoque). 0, sólidos.
    /// </summary>
    public int Transparency { get; set; }

    public const double DefaultSize = 13.5;
    public static readonly double[] Sizes = [11.5, 12.5, 13.5, 14.5, 16, 18];
    public static readonly double[] Spacings = [1.3, 1.55, 1.8];
    public static readonly string[] Texts = ["tema", "ambar", "verde", "hielo"];
    public static readonly string[] Backgrounds = ["tema", "profundo", "negro"];
    public static readonly string[] Palettes = ["claupet", "suave", "viva"];
    public static readonly string[] Cursors = ["bloque", "barra", "subrayado"];
    /// <summary>Lo más transparente que se puede elegir: más allá, el texto se pierde en la escena del tema.</summary>
    public const int MaxTransparency = 80;

    /// <summary>Las letras de ancho fijo conocidas; se ofrecen solo las instaladas.</summary>
    static readonly string[] KnownFonts = ["Cascadia Mono", "Cascadia Code", "Consolas", "JetBrains Mono", "Fira Code", "Source Code Pro",
        "Hack", "IBM Plex Mono", "SF Mono", "Menlo", "Ubuntu Mono", "DejaVu Sans Mono", "Courier New"];

    static string FilePath => DataFolder.In("terminal.json");

    public static TerminalLook Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<TerminalLook>(File.ReadAllText(FilePath)) is { } look)
            {
                // Lo que no se reconoce (un archivo editado a mano) vuelve a lo de siempre.
                if (!Sizes.Contains(look.Size)) look.Size = DefaultSize;
                if (!Spacings.Contains(look.LineSpacing)) look.LineSpacing = 1.55;
                if (!Texts.Contains(look.Text)) look.Text = "tema";
                if (!Backgrounds.Contains(look.Background)) look.Background = "tema";
                if (!Palettes.Contains(look.Palette)) look.Palette = "claupet";
                if (!Cursors.Contains(look.Cursor)) look.Cursor = "bloque";
                look.Transparency = Math.Clamp(look.Transparency, 0, MaxTransparency);
                return look;
            }
        }
        catch (Exception) { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception) { }
    }

    /// <summary>La letra de las sesiones: la elegida delante de la cadena de siempre (si falta, sigue la de siempre).</summary>
    public FontFamily Mono => Font is { Length: > 0 } font ? new FontFamily(font + ", Cascadia Mono, Consolas, SF Mono, Menlo, Ubuntu Mono, DejaVu Sans Mono, monospace") : Ink.Mono;

    public static IReadOnlyList<string> InstalledFonts()
    {
        try
        {
            var installed = FontManager.Current.SystemFonts.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return KnownFonts.Where(installed.Contains).ToList();
        }
        catch (Exception) { return []; }
    }

    /// <summary>Los colores de la Terminal: los del tema con el fondo y el color del texto elegidos.</summary>
    public Ink Apply(Ink ink)
    {
        if (Background == "negro")
            ink = (ink.Dark ? ink : Ink.For(true)) with { Window = B("#0C0C0C"), Surface = B("#0C0C0C") };
        else if (Background == "profundo")
            ink = ink with { Surface = ink.Window };
        return Text switch
        {
            "ambar" => ink with { Text = B(ink.Dark ? "#F2B35B" : "#8A5A12") },
            "verde" => ink with { Text = B(ink.Dark ? "#8FD18A" : "#2E6B2A") },
            "hielo" => ink with { Text = B(ink.Dark ? "#A9C9F0" : "#2F5B8C") },
            _ => ink,
        };
    }

    /// <summary>Hay un fondo propio (no el del tema ni su escena).</summary>
    public bool OwnBackground => Background != "tema";

    /// <summary>La opacidad de la pantalla, los bloques y la caja de las terminales: 1 sin transparencia.</summary>
    public double ShellOpacity => 1 - Math.Clamp(Transparency, 0, 100) / 100.0;

    /// <summary>
    /// Los 16 colores de la paleta elegida, para un fondo oscuro o claro: Claupet (los de la Terminal, cálidos y
    /// legibles), Suave (apagados) o Viva (saturados, como una consola clásica).
    /// </summary>
    public int[] Colors16(bool dark) => (Palette, dark) switch
    {
        ("suave", true) => [0x2B2B2A, 0xC98B7E, 0x9DB59A, 0xD3B98A, 0x8FA8C4, 0xB49AC2, 0x8DB8B4, 0xCFCBC2, 0x6B6A66, 0xD9A193, 0xB4C9B1, 0xE0CB9F, 0xA7BDD6, 0xC7B0D3, 0xA3CAC6, 0xEDEAE2],
        ("suave", false) => [0x3A3936, 0xA2584A, 0x5B7A57, 0x8C6D33, 0x4F6B8C, 0x7B5E8A, 0x4E7F7A, 0x8A8880, 0x5E5D59, 0xB86E60, 0x6E8F6A, 0x9E7E45, 0x627F9F, 0x8E719D, 0x61928D, 0x2B2B2A],
        ("viva", true) => [0x1E1E1E, 0xFF5F57, 0x5AF78E, 0xF3F99D, 0x57C7FF, 0xFF6AC1, 0x9AEDFE, 0xF1F1F0, 0x686868, 0xFF7A72, 0x7AFFA5, 0xF9FDB4, 0x7AD3FF, 0xFF8AD0, 0xB5F3FF, 0xFFFFFF],
        ("viva", false) => [0x1E1E1E, 0xD70000, 0x008700, 0xAF8700, 0x005FD7, 0xAF00AF, 0x00AFAF, 0x8A8A8A, 0x4E4E4E, 0xFF0000, 0x00AF00, 0xD7AF00, 0x0087FF, 0xD700D7, 0x00D7D7, 0x1E1E1E],
        (_, true) => [0x282C34, 0xF08A7A, 0xA8C7A0, 0xE0A84B, 0x7FB0E8, 0xC9A0DC, 0x7FC6C0, 0xDCDFE4, 0x5A6374, 0xF5A193, 0xBCD6B5, 0xEBC07B, 0x9CC3EF, 0xD7B6E6, 0x9BD5D0, 0xF5F4EF],
        (_, false) => [0x383A42, 0xA33A2A, 0x3F6B3A, 0x8A5A12, 0x2F5B8C, 0x8A3F8A, 0x2E7A73, 0x8A8880, 0x5E5D59, 0xB4532F, 0x4F7E49, 0x9E6A1C, 0x3F6EA3, 0x9C4F9C, 0x3B8C84, 0x1F1E1D],
    };

    public static string TextLabel(string text) => text switch
    {
        "ambar" => L.T("Ámbar"),
        "verde" => L.T("Verde"),
        "hielo" => L.T("Hielo"),
        _ => L.T("El del tema"),
    };

    public static string BackgroundLabel(string background) => background switch
    {
        "profundo" => L.T("Más profundo"),
        "negro" => L.T("Negro, de consola"),
        _ => L.T("El del tema"),
    };

    public static string PaletteLabel(string palette) => palette switch
    {
        "suave" => L.T("Suave"),
        "viva" => L.T("Viva"),
        _ => "Claupet",
    };

    public static string CursorLabel(string cursor) => cursor switch
    {
        "barra" => L.T("Barra"),
        "subrayado" => L.T("Subrayado"),
        _ => L.T("Bloque"),
    };

    /// <summary>La muestra del color del texto (Ajustes › Terminal): el del tema o el elegido, sobre un fondo oscuro.</summary>
    public static string TextSwatch(string text, bool dark) => text switch
    {
        "ambar" => dark ? "#F2B35B" : "#8A5A12",
        "verde" => dark ? "#8FD18A" : "#2E6B2A",
        "hielo" => dark ? "#A9C9F0" : "#2F5B8C",
        _ => dark ? "#F5F4EF" : "#1F1E1D",
    };

    public static string SpacingLabel(double spacing) => spacing switch
    {
        < 1.4 => L.T("Junto"),
        > 1.7 => L.T("Amplio"),
        _ => L.T("Normal"),
    };

    static ImmutableSolidColorBrush B(string hex) => new(Color.Parse(hex));
}

/// <summary>
/// Los proyectos de la Terminal: las carpetas vinculadas (quedan en la lista aunque no tengan sesiones), las plegadas y
/// el ancho de la lista. Desvincular solo quita la carpeta de la lista: no la borra. Se guarda en terminal-proyectos.json.
/// </summary>
sealed class TerminalProjects
{
    public List<string> Folders { get; set; } = [];
    public List<string> Folded { get; set; } = [];
    public double ListWidth { get; set; } = 250;
    /// <summary>El panel de la derecha (Ramas, Cambios, Procesos): si se ve, cuál y su ancho.</summary>
    public bool PanelOpen { get; set; } = true;
    public string PanelTab { get; set; } = "ramas";
    public double PanelWidth { get; set; } = 290;

    public const double MinWidth = 190, MaxWidth = 460, PanelMin = 220, PanelMax = 560;

    static string FilePath => DataFolder.In("terminal-proyectos.json");

    public static TerminalProjects Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<TerminalProjects>(File.ReadAllText(FilePath)) is { } projects)
            {
                projects.ListWidth = Math.Clamp(projects.ListWidth, MinWidth, MaxWidth);
                projects.PanelWidth = Math.Clamp(projects.PanelWidth, PanelMin, PanelMax);
                if (projects.PanelTab is not ("ramas" or "cambios" or "procesos" or "historial")) projects.PanelTab = "ramas";
                return projects;
            }
        }
        catch (Exception) { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception) { }
    }

    static bool Same(string a, string b) => string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    public bool Has(string folder) => Folders.Any(f => Same(f, folder));

    /// <summary>Lo agrega al final de la lista, si no estaba. True si se agregó.</summary>
    public bool Link(string folder)
    {
        if (Has(folder)) return false;
        Folders.Add(folder);
        return true;
    }

    public void Unlink(string folder)
    {
        Folders.RemoveAll(f => Same(f, folder));
        Folded.RemoveAll(f => Same(f, folder));
    }

    public bool IsFolded(string folder) => Folded.Any(f => Same(f, folder));

    public void SetFolded(string folder, bool folded)
    {
        Folded.RemoveAll(f => Same(f, folder));
        if (folded) Folded.Add(folder);
    }
}
