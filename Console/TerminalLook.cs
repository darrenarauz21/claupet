namespace Claupet;

using System.Text.Json;
using Avalonia.Media;
using Avalonia.Media.Immutable;

/// <summary>
/// Cómo se ve Claupet Terminal (Ajustes › Terminal): la letra de las sesiones, su tamaño, el interlineado, el color del
/// texto, el fondo, las animaciones y la lista de sesiones (con descripción o solo íconos). Se guarda en terminal.json,
/// aparte de los ajustes de la bandeja: la Terminal no la necesita.
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

    public const double DefaultSize = 13.5;
    public static readonly double[] Sizes = [11.5, 12.5, 13.5, 14.5, 16, 18];
    public static readonly double[] Spacings = [1.3, 1.55, 1.8];
    public static readonly string[] Texts = ["tema", "ambar", "verde", "hielo"];
    public static readonly string[] Backgrounds = ["tema", "profundo", "negro"];

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
                if (projects.PanelTab is not ("ramas" or "cambios" or "procesos")) projects.PanelTab = "ramas";
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
