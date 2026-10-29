using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet;

/// <summary>Los colores de un modo (claro u oscuro) de un tema propio: los mismos campos que Theme.</summary>
sealed record ThemeColors(Color Background, Color Raised, Color Hover, Color Border, Color Text, Color Secondary,
    Color Tertiary, Color Track, Color Accent, Color Danger, Color Success, Color Warn)
{
    public static ThemeColors Of(Theme t) => new(t.Background, t.Raised, t.Hover, t.Border, t.Text, t.Secondary,
        t.Tertiary, t.Track, t.Accent, t.Danger, t.Success, t.Warn);
}

/// <summary>El fondo animado: una escena del catálogo (CustomThemes.Scenes), sus colores, la densidad (0,1–1) y la velocidad (0,25–2).</summary>
sealed record ThemeScene(string Kind, Color[] Colors, double Density, double Speed);

/// <summary>Un tema propio ya validado. Font: un nombre de familia, o null; Scene: el fondo animado de la Console, o null.</summary>
sealed record CustomTheme(string Id, string Name, ThemeColors Light, ThemeColors Dark, string? Font, ThemeScene? Scene)
{
    /// <summary>El id con que se elige en la misma lista que las paletas.</summary>
    public string PaletteId => CustomThemes.Prefix + Id;
}

/// <summary>
/// Los temas propios: un JSON por tema en temas/, junto a settings.json, que Claude crea a partir de un prompt. Se eligen
/// como una paleta más («tema:&lt;id&gt;»), y la bandeja y la Console los leen del disco por ese id. El diseño se respeta
/// aquí y no en el modelo: superficies sobrias, contraste WCAG (4,5:1 el texto; 3:1 el acento y los secundarios),
/// peligro, aviso y éxito con su tono de siempre y una escena del catálogo cerrado. Si el archivo falta o está roto,
/// no hay tema y todo vuelve a la terracota.
/// </summary>
static class CustomThemes
{
    public const string Prefix = "tema:";

    /// <summary>El catálogo cerrado de fondos animados, en pixel art como la mascota.</summary>
    public static readonly string[] Scenes = ["playa", "lluvia", "nieve", "estrellas", "hojas", "burbujas", "luciernagas"];

    static readonly string[] Fields = ["background", "raised", "hover", "border", "text", "secondary", "tertiary", "track", "accent", "danger", "success", "warn"];

    static string? _folder;
    /// <summary>La carpeta temas/, junto a settings.json. Las pruebas la cambian.</summary>
    public static string Folder { get => _folder ?? DataFolder.In("temas"); set => _folder = value; }

    /// <summary>Dice si una familia de letra está instalada (en la Console, FontManager); null: no se comprueba aquí.</summary>
    public static Func<string, bool>? FontInstalled { get; set; }

    /// <summary>El tema propio elegido en Ajustes, o null si se eligió una paleta o el archivo falta o está roto.</summary>
    public static CustomTheme? Selected => IdOf(Theme.SelectedPalette) is { } id ? Load(id) : null;

    /// <summary>El id del tema si la paleta es «tema:&lt;id&gt;»; si no, null.</summary>
    public static string? IdOf(string? palette) =>
        palette is not null && palette.StartsWith(Prefix, StringComparison.Ordinal) && palette.Length > Prefix.Length ? palette[Prefix.Length..] : null;

    // ── Disco ────────────────────────────────────────────────────────────────────────────────

    /// <summary>La ruta del tema, siempre dentro de temas/: el id se limpia antes (ni «..», ni barras, ni unidades).</summary>
    public static string PathFor(string id)
    {
        var folder = Path.GetFullPath(Folder);
        var path = Path.GetFullPath(Path.Combine(folder, SafeId(id) + ".json"));
        if (!path.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("id fuera de la carpeta de temas", nameof(id));
        return path;
    }

    static readonly Dictionary<string, (DateTime Stamp, CustomTheme? Theme)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Lee el tema del disco (con caché mientras no cambie el archivo); null si falta o está roto, sin excepción.</summary>
    public static CustomTheme? Load(string id)
    {
        try
        {
            var path = PathFor(id);
            if (!File.Exists(path)) return null;
            var stamp = File.GetLastWriteTimeUtc(path);
            lock (Cache)
                if (Cache.TryGetValue(path, out var hit) && hit.Stamp == stamp) return hit.Theme;
            var theme = Parse(File.ReadAllText(path)) is { } read ? read with { Id = SafeId(id) } : null;
            lock (Cache) Cache[path] = (stamp, theme);
            return theme;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Los temas guardados, por nombre.</summary>
    public static IReadOnlyList<CustomTheme> All()
    {
        try
        {
            if (!Directory.Exists(Folder)) return [];
            return Directory.EnumerateFiles(Folder, "*.json")
                .Select(f => Load(Path.GetFileNameWithoutExtension(f)))
                .OfType<CustomTheme>()
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception) { return []; }
    }

    /// <summary>Lo guarda en temas/; si ya hay uno con ese id, el nuevo lleva un número detrás. Devuelve el tema guardado.</summary>
    public static CustomTheme Save(CustomTheme theme)
    {
        Directory.CreateDirectory(Folder);
        var id = SafeId(theme.Id);
        for (var n = 2; File.Exists(PathFor(id)); n++) id = SafeId($"{SafeId(theme.Id)}-{n}");
        var saved = theme with { Id = id };
        File.WriteAllText(PathFor(id), ToJson(saved), new UTF8Encoding(false));
        return saved;
    }

    /// <summary>Borra el tema; true si había archivo.</summary>
    public static bool Delete(string id)
    {
        try
        {
            var path = PathFor(id);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            lock (Cache) Cache.Remove(path);
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// El id seguro para un nombre de archivo: minúsculas sin tildes, letras, cifras y guiones, hasta 40. Nada de
    /// barras, puntos ni nombres reservados de Windows; si no queda nada, «tema».
    /// </summary>
    public static string SafeId(string? raw)
    {
        var plain = Plain(raw ?? "");
        var sb = new StringBuilder();
        foreach (var ch in plain)
        {
            if (sb.Length >= 40) break;
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var id = sb.ToString().Trim('-');
        if (id is "con" or "prn" or "aux" or "nul" || (id.Length == 4 && (id.StartsWith("com") || id.StartsWith("lpt")) && char.IsDigit(id[3])))
            id = "tema-" + id;
        return id.Length == 0 ? "tema" : id;
    }

    // ── Análisis y corrección ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lee un tema con tolerancia (el primer objeto JSON del texto, aunque venga entre ``` o con comas de más) y lo
    /// valida y corrige. Null si no hay JSON o no trae colores de ningún modo.
    /// </summary>
    public static CustomTheme? Parse(string text, string? fallbackName = null)
    {
        if (ExtractJson(text) is not { } json) return null;
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject;
        }
        catch (JsonException) { return null; }
        if (root is null) return null;

        var lightRaw = root["light"] as JsonObject;
        var darkRaw = root["dark"] as JsonObject;
        if (lightRaw is null && darkRaw is null) return null;
        var name = (Str(root, "name") ?? fallbackName ?? "").Trim();
        if (name.Length > 40) name = name[..40].TrimEnd();
        if (name.Length == 0) name = L.T("Tema propio");
        // Si falta un modo, sale del de Claupet con el acento del otro.
        var light = Fix(Read(lightRaw ?? darkRaw, Theme.Light, lightRaw is null), dark: false);
        var dark = Fix(Read(darkRaw ?? lightRaw, Theme.DarkTheme, darkRaw is null), dark: true);
        return new CustomTheme(SafeId(Str(root, "id") ?? name), name, light, dark, Font(Str(root, "font")),
            Scene(root["scene"] as JsonObject, light.Accent));
    }

    /// <summary>El primer objeto JSON del texto, con las llaves emparejadas (fuera de las cadenas); null si no hay.</summary>
    public static string? ExtractJson(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var start = text.IndexOf('{');
        if (start < 0) return null;
        int depth = 0;
        bool inString = false, escaped = false;
        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (ch == '\\') escaped = true;
                else if (ch == '"') inString = false;
            }
            else if (ch == '"') inString = true;
            else if (ch == '{') depth++;
            else if (ch == '}' && --depth == 0) return text[start..(i + 1)];
        }
        return null;
    }

    static ThemeColors Read(JsonObject? raw, Theme fallback, bool onlyAccent)
    {
        var values = new Color[Fields.Length];
        var defaults = ThemeColors.Of(fallback);
        var current = new[] { defaults.Background, defaults.Raised, defaults.Hover, defaults.Border, defaults.Text, defaults.Secondary,
            defaults.Tertiary, defaults.Track, defaults.Accent, defaults.Danger, defaults.Success, defaults.Warn };
        for (var i = 0; i < Fields.Length; i++)
        {
            var take = !onlyAccent || Fields[i] == "accent";
            values[i] = take && raw is not null && ParseHex(Str(raw, Fields[i])) is { } c ? c : current[i];
        }
        return new(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8], values[9], values[10], values[11]);
    }

    /// <summary>
    /// Corrige un modo para que respete el diseño: superficies poco saturadas y con la luz de su modo; peligro, aviso y
    /// éxito con su tono (si no, los de Claupet); y el contraste WCAG contra el fondo y Raised, ajustando la luminosidad.
    /// </summary>
    public static ThemeColors Fix(ThemeColors c, bool dark)
    {
        var defaults = ThemeColors.Of(dark ? Theme.DarkTheme : Theme.Light);
        var background = Surface(c.Background, dark ? (0.07, 0.19) : (0.91, 0.985));
        var raised = Surface(c.Raised, dark ? (0.12, 0.27) : (0.94, 1.0));
        var hover = Surface(c.Hover, dark ? (0.10, 0.26) : (0.86, 0.97));
        var border = Surface(c.Border, dark ? (0.14, 0.32) : (0.80, 0.93));
        var track = Surface(c.Track, dark ? (0.14, 0.32) : (0.82, 0.94));
        Color[] under = [background, raised];
        var danger = Status(c.Danger, defaults.Danger, (340, 20));
        var warn = Status(c.Warn, defaults.Warn, (25, 55));
        var success = Status(c.Success, defaults.Success, (70, 160));
        return new(background, raised, hover, border,
            Contrast(c.Text, under, 4.5, dark), Contrast(c.Secondary, under, 3, dark), Contrast(c.Tertiary, under, 3, dark),
            track, Contrast(c.Accent, under, 3, dark),
            Contrast(danger, under, 3, dark), Contrast(success, under, 3, dark), Contrast(warn, under, 3, dark));
    }

    /// <summary>
    /// Una superficie sobria: la luminosidad dentro del rango del modo y poco color (el croma, la distancia entre el canal
    /// más alto y el más bajo, como mucho 0,10; la saturación HSL engaña cerca del blanco y del negro).
    /// </summary>
    static Color Surface(Color c, (double Min, double Max) light)
    {
        var (h, s, l) = ToHsl(c);
        var room = 1 - Math.Abs(2 * Math.Clamp(l, light.Min, light.Max) - 1);
        if (l >= light.Min && l <= light.Max && s * room <= 0.10) return c;
        l = Math.Clamp(l, light.Min, light.Max);
        if (room > 0 && s * room > 0.10) s = 0.10 / room;
        return FromHsl(h, s, l);
    }

    /// <summary>Peligro, aviso y éxito: si el tono se sale de su rango o el color está apagado, el de Claupet.</summary>
    static Color Status(Color c, Color fallback, (double From, double To) hue)
    {
        var (h, s, _) = ToHsl(c);
        var inside = hue.From <= hue.To ? h >= hue.From && h <= hue.To : h >= hue.From || h <= hue.To;
        return inside && s >= 0.3 ? c : fallback;
    }

    /// <summary>Oscurece (modo claro) o aclara (oscuro) el color hasta llegar al contraste con todos los fondos.</summary>
    public static Color Contrast(Color c, Color[] backgrounds, double ratio, bool dark)
    {
        var (h, s, l) = ToHsl(c);
        var color = c;
        for (var i = 0; i < 120 && backgrounds.Min(b => Ratio(color, b)) < ratio; i++)
        {
            l = Math.Clamp(l + (dark ? 0.01 : -0.01), 0, 1);
            color = FromHsl(h, s, l);
            if (l is 0 or 1) break;
        }
        return color;
    }

    /// <summary>La razón de contraste WCAG 2 entre dos colores (de 1 a 21).</summary>
    public static double Ratio(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    static double Luminance(Color c)
    {
        static double Channel(int v)
        {
            var x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary>La familia de letra: solo un nombre (letras, cifras, espacios y guiones, hasta 48) y, si se puede, instalada.</summary>
    static string? Font(string? raw)
    {
        var name = raw?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 48) return null;
        if (!name.All(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-')) return null;
        if (FontInstalled is { } installed && !installed(name)) return null;
        return name;
    }

    /// <summary>El fondo animado: una escena del catálogo, o null; la densidad y la velocidad, dentro de su rango.</summary>
    static ThemeScene? Scene(JsonObject? raw, Color accent)
    {
        if (raw is null || Str(raw, "kind") is not { } kindRaw) return null;
        var kind = Plain(kindRaw).Trim();
        if (!Scenes.Contains(kind)) return null;
        var colors = (raw["colors"] as JsonArray ?? [])
            .Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? ParseHex(s) : null)
            .OfType<Color>().Take(4).ToArray();
        if (colors.Length == 0) colors = [accent];
        return new ThemeScene(kind, colors, Math.Clamp(Num(raw, "density") ?? 0.5, 0.1, 1), Math.Clamp(Num(raw, "speed") ?? 1, 0.25, 2));
    }

    // ── JSON ─────────────────────────────────────────────────────────────────────────────────

    public static string ToJson(CustomTheme theme)
    {
        static JsonObject Mode(ThemeColors c)
        {
            Color[] values = [c.Background, c.Raised, c.Hover, c.Border, c.Text, c.Secondary, c.Tertiary, c.Track, c.Accent, c.Danger, c.Success, c.Warn];
            var obj = new JsonObject();
            for (var i = 0; i < Fields.Length; i++) obj[Fields[i]] = Hex(values[i]);
            return obj;
        }
        var root = new JsonObject
        {
            ["id"] = theme.Id,
            ["name"] = theme.Name,
            ["font"] = theme.Font,
            ["light"] = Mode(theme.Light),
            ["dark"] = Mode(theme.Dark),
        };
        if (theme.Scene is { } scene)
            root["scene"] = new JsonObject
            {
                ["kind"] = scene.Kind,
                ["colors"] = new JsonArray(scene.Colors.Select(c => (JsonNode)Hex(c)).ToArray()),
                ["density"] = Math.Round(scene.Density, 2),
                ["speed"] = Math.Round(scene.Speed, 2),
            };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>«#RRGGBB», «RRGGBB» o «#RGB»; null si no es un color.</summary>
    public static Color? ParseHex(string? raw)
    {
        var s = raw?.Trim().TrimStart('#');
        if (s is null) return null;
        if (s.Length == 3) s = string.Concat(s.Select(ch => $"{ch}{ch}"));
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return null;
        return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }

    static string? Str(JsonObject obj, string key) =>
        obj[key] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    static double? Num(JsonObject obj, string key) =>
        obj[key] is JsonValue v && (v.TryGetValue<double>(out var d) ? d
            : v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d
            : double.NaN) is var n && double.IsFinite(n) ? n : null;

    /// <summary>En minúsculas y sin tildes.</summary>
    static string Plain(string s) =>
        new(s.Normalize(NormalizationForm.FormD).Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .Select(char.ToLowerInvariant).ToArray());

    static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2;
        if (max == min) return (0, 0, l);
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    static Color FromHsl(double h, double s, double l)
    {
        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            return t < 1 / 6.0 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2 / 3.0 ? p + (q - p) * (2 / 3.0 - t) * 6 : p;
        }
        double r, g, b;
        if (s == 0) r = g = b = l;
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            var hk = h / 360;
            r = Hue(p, q, hk + 1 / 3.0); g = Hue(p, q, hk); b = Hue(p, q, hk - 1 / 3.0);
        }
        static int Byte(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return Color.FromArgb(255, Byte(r), Byte(g), Byte(b));
    }
}
