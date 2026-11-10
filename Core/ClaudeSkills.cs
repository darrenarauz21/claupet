using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>Una skill de Claude Code: su nombre, lo que dice de sí misma y si es una de las que trae Claupet.</summary>
sealed record SkillState(string Name, string Description, bool Bundled);

/// <summary>
/// Las skills de Claude Code (una carpeta con su SKILL.md en «skills» de la carpeta de configuración), para Ajustes ›
/// Skills. Van en todas las cuentas: cada una tiene sus propias skills (aunque comparta la memoria con otra), y una skill
/// agregada desde Claupet tiene que estar en la sesión que se abra con cualquiera. Las de Claupet (claupet-plan) van
/// dentro del ejecutable y se instalan o se quitan con su interruptor.
/// </summary>
static class ClaudeSkills
{
    /// <summary>Las que trae Claupet, con lo que dice su interruptor.</summary>
    public static readonly (string Name, string Label)[] Bundled = [("claupet-plan", "Planes de Claupet")];

    /// <summary>Lo que se acepta al agregar una: el SKILL.md (o cualquier .md), o su carpeta en un .zip o un .skill.</summary>
    public const string FilePattern = "*.md;*.zip;*.skill";

    const long MaxSize = 50_000_000;

    /// <summary>Las carpetas de configuración de las cuentas (sin cuentas, la de siempre).</summary>
    public static IReadOnlyList<string> ConfigDirs(IEnumerable<Account> accounts)
    {
        var dirs = new List<string>();
        foreach (var dir in accounts.Select(a => a.ConfigDir).Where(d => d.Length > 0))
            if (!dirs.Any(d => Account.SamePath(d, dir))) dirs.Add(dir);
        if (dirs.Count == 0) dirs.Add(Account.DefaultDir);
        return dirs;
    }

    public static string Root(string configDir) => Path.Combine(configDir, "skills");

    /// <summary>Las skills de todas las cuentas, una vez cada una (por nombre), las de Claupet primero.</summary>
    public static IReadOnlyList<SkillState> List(IReadOnlyList<string> configDirs)
    {
        var found = new Dictionary<string, SkillState>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in configDirs)
        {
            var root = Root(dir);
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> folders;
            try { folders = Directory.EnumerateDirectories(root).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var folder in folders)
            {
                var file = Path.Combine(folder, "SKILL.md");
                if (!File.Exists(file)) continue;
                var name = Path.GetFileName(folder);
                if (found.ContainsKey(name)) continue;
                var description = "";
                try { description = Front(File.ReadAllText(file)).GetValueOrDefault("description") ?? ""; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                found[name] = new SkillState(name, description, IsBundled(name));
            }
        }
        return found.Values.OrderBy(s => !s.Bundled).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool IsBundled(string name) => Bundled.Any(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>El SKILL.md de una de las de Claupet (dentro del ejecutable); null si no es suya.</summary>
    public static string? BundledText(string name)
    {
        if (!IsBundled(name)) return null;
        // RecursiveDir trae la barra del sistema donde se compiló («skills/claupet-plan\SKILL.md» en Windows).
        var assembly = typeof(ClaudeSkills).Assembly;
        var resource = assembly.GetManifestResourceNames().FirstOrDefault(r => r.Replace('\\', '/') == $"skills/{name}/SKILL.md");
        using var stream = resource is null ? null : assembly.GetManifestResourceStream(resource);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Instala (o pone al día) una de las de Claupet en todas las cuentas; el problema, si lo hubo.</summary>
    public static string? InstallBundled(string name, IReadOnlyList<string> configDirs)
    {
        if (BundledText(name) is not { } text) return L.T("Claupet no trae la skill «{0}».", name);
        try
        {
            foreach (var dir in configDirs)
            {
                var folder = Path.Combine(Root(dir), name);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "SKILL.md"), text);
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }
    }

    /// <summary>La pone al día donde ya está (una versión nueva de Claupet puede traer otro texto); no la instala donde no.</summary>
    public static void RefreshBundled(IReadOnlyList<string> configDirs)
    {
        foreach (var (name, _) in Bundled)
        {
            if (BundledText(name) is not { } text) continue;
            foreach (var dir in configDirs)
            {
                var file = Path.Combine(Root(dir), name, "SKILL.md");
                try { if (File.Exists(file) && File.ReadAllText(file) != text) File.WriteAllText(file, text); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
            }
        }
    }

    /// <summary>La quita de todas las cuentas (su carpeta entera); el problema, si lo hubo.</summary>
    public static string? Remove(string name, IReadOnlyList<string> configDirs)
    {
        if (!ValidName(name)) return L.T("«{0}» no es el nombre de una skill.", name);
        try
        {
            foreach (var dir in configDirs)
            {
                var folder = Path.Combine(Root(dir), name);
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }
    }

    /// <summary>La carpeta de una skill en la primera cuenta que la tiene; null si no está.</summary>
    public static string? Folder(string name, IReadOnlyList<string> configDirs) =>
        !ValidName(name) ? null : configDirs.Select(d => Path.Combine(Root(d), name)).FirstOrDefault(Directory.Exists);

    /// <summary>
    /// Agrega (o reemplaza) una skill en todas las cuentas desde un archivo: un SKILL.md (con todo lo de su carpeta), otro
    /// .md (pasa a ser el SKILL.md de una carpeta nueva) o un .zip / .skill con el SKILL.md arriba o en su única carpeta.
    /// El nombre es el de su encabezado («name:») o, si no tiene, el de su carpeta o su archivo. Devuelve el nombre o el problema.
    /// </summary>
    public static (string? Name, string? Problem) Add(string path, IReadOnlyList<string> configDirs)
    {
        try
        {
            if (!File.Exists(path)) return (null, L.T("No se encontró el archivo."));
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is ".zip" or ".skill") return AddZip(path, configDirs);
            if (extension != ".md") return (null, L.T("Elija un SKILL.md, un .zip o un .skill."));

            var text = File.ReadAllText(path);
            var isSkillFile = Path.GetFileName(path).Equals("SKILL.md", StringComparison.OrdinalIgnoreCase);
            var source = isSkillFile ? Path.GetDirectoryName(path)! : null;
            var fallback = isSkillFile ? Path.GetFileName(source!) : Path.GetFileNameWithoutExtension(path);
            var (name, problem) = Check(text, fallback);
            if (problem is not null) return (null, problem);
            if (source is not null && Size(source) > MaxSize) return (null, L.T("Su carpeta pesa demasiado: pase solo la skill."));
            foreach (var dir in configDirs)
            {
                var place = Path.Combine(Root(dir), name!);
                // Elegida la que ya está instalada: esa cuenta ya la tiene (vaciarla la borraría).
                if (source is not null && Account.SamePath(source, place)) continue;
                var target = Fresh(place);
                if (source is not null) CopyFolder(source, target);
                File.WriteAllText(Path.Combine(target, "SKILL.md"), text);
            }
            return (name, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { return (null, ex.Message); }
    }

    static (string? Name, string? Problem) AddZip(string path, IReadOnlyList<string> configDirs)
    {
        using var zip = ZipFile.OpenRead(path);
        var files = zip.Entries.Where(e => e.Name.Length > 0).ToList();
        if (files.Sum(e => e.Length) > MaxSize) return (null, L.T("El archivo pesa demasiado para una skill."));
        // El SKILL.md, arriba o dentro de una sola carpeta: lo demás de esa carpeta va con él.
        var skill = files.Where(e => e.Name.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName.Count(c => c == '/')).FirstOrDefault();
        if (skill is null || skill.FullName.Count(c => c == '/') > 1) return (null, L.T("No tiene un SKILL.md."));
        var prefix = skill.FullName[..^skill.Name.Length];
        string text;
        using (var reader = new StreamReader(skill.Open())) text = reader.ReadToEnd();
        var fallback = prefix.Length > 0 ? prefix.TrimEnd('/') : Path.GetFileNameWithoutExtension(path);
        var (name, problem) = Check(text, fallback);
        if (problem is not null) return (null, problem);
        foreach (var dir in configDirs)
        {
            var target = Fresh(Path.Combine(Root(dir), name!));
            var full = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
            foreach (var entry in files.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal)))
            {
                var destination = Path.GetFullPath(Path.Combine(target, entry.FullName[prefix.Length..]));
                // Nada fuera de su carpeta (un «../» en el .zip).
                if (!destination.StartsWith(full, StringComparison.OrdinalIgnoreCase)) return (null, L.T("El archivo trae rutas fuera de la skill."));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
        }
        return (name, null);
    }

    /// <summary>El nombre que tendrá y si le falta algo (una skill sin descripción Claude Code no sabe cuándo usarla).</summary>
    static (string? Name, string? Problem) Check(string text, string fallback)
    {
        var front = Front(text);
        if (front.GetValueOrDefault("description") is not { Length: > 0 })
            return (null, L.T("Su SKILL.md no tiene «description» en el encabezado: Claude Code no sabría cuándo usarla."));
        var name = Slug(front.GetValueOrDefault("name") is { Length: > 0 } given ? given : fallback);
        return ValidName(name) ? (name, null) : (null, L.T("No se pudo sacar un nombre válido para la skill."));
    }

    /// <summary>Su carpeta, vacía: lo que tenía una versión anterior no se mezcla con la nueva.</summary>
    static string Fresh(string folder)
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        return folder;
    }

    static void CopyFolder(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    static long Size(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

    public static bool ValidName(string name) => Regex.IsMatch(name, "^[a-z0-9][a-z0-9-]{0,63}$");

    /// <summary>En minúsculas, con guiones: como lo pide Claude Code.</summary>
    internal static string Slug(string text)
    {
        var lower = text.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var clean = new string(lower.Where(c => char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        clean = Regex.Replace(clean, "[^a-z0-9]+", "-").Trim('-');
        return clean.Length > 64 ? clean[..64].TrimEnd('-') : clean;
    }

    /// <summary>El encabezado YAML de un SKILL.md (entre los «---»): solo las claves de primer nivel, como texto.</summary>
    internal static Dictionary<string, string> Front(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---") return values;
        string? key = null;
        var folded = new List<string>();
        void Flush()
        {
            if (key is not null && folded.Count > 0) values[key] = string.Join(" ", folded);
            folded.Clear();
        }
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim() == "---") break;
            // Una clave con «>» o «|» sigue en las líneas con sangría.
            if (key is not null && line.Length > 0 && char.IsWhiteSpace(line[0]))
            {
                if (line.Trim().Length > 0) folded.Add(line.Trim());
                continue;
            }
            Flush();
            key = null;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (value is "" or ">" or "|" or ">-" or "|-") { key = name; continue; }
            if (value.Length >= 2 && (value[0] is '"' or '\'') && value[^1] == value[0]) value = value[1..^1];
            values[name] = value;
        }
        Flush();
        return values;
    }
}
