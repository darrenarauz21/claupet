using System.Text.Json;

namespace Claupet;

/// <summary>Un plan encontrado: dónde está y lo que dice (o por qué no se pudo leer).</summary>
sealed record PlanEntry(string Path, string Folder, Plan? Plan, string? Error, DateTime Modified)
{
    /// <summary>El título del plan o, si no tiene, el nombre del archivo.</summary>
    public string Name => Plan?.Title is { Length: > 0 } title ? title : System.IO.Path.GetFileName(Path)[..^Plan.Extension.Length];
}

/// <summary>
/// Los planes que ve la sección Proyectos: los *.plan.json de las carpetas vinculadas y los que se agregaron a mano. El plan
/// sigue en su carpeta (con su repositorio): aquí, en proyectos.json, solo quedan esas rutas y lo último que se miró.
/// </summary>
sealed class PlanIndex(string? file = null)
{
    static readonly string[] Skip = [".git", "node_modules", "bin", "obj", ".vs", ".idea", "dist", "target"];
    const int MaxDepth = 4, MaxPerFolder = 40;

    sealed class Saved
    {
        public List<string> Extra { get; set; } = [];
        public string? Last { get; set; }
        public string? View { get; set; }
    }

    readonly string _file = file ?? DataFolder.In("proyectos.json");
    Saved? _saved;

    Saved Data
    {
        get
        {
            if (_saved is not null) return _saved;
            try { _saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(_file)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
            return _saved ??= new Saved();
        }
    }

    /// <summary>El plan y la vista que se miraron por última vez.</summary>
    public string? Last => Data.Last;
    public string? View => Data.View;

    public void Remember(string? path, string view)
    {
        if (Data.Last == path && Data.View == view) return;
        Data.Last = path;
        Data.View = view;
        Save();
    }

    /// <summary>Suma un plan que no está en una carpeta vinculada.</summary>
    public void Add(string path)
    {
        if (Data.Extra.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        Data.Extra.Add(path);
        Save();
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(Data));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Los planes de una carpeta y sus subcarpetas (sin lo generado ni las dependencias), hasta un tope por carpeta.</summary>
    public static IReadOnlyList<string> Find(string folder)
    {
        var found = new List<string>();
        Walk(folder, 0);
        return found;

        void Walk(string dir, int depth)
        {
            if (found.Count >= MaxPerFolder) return;
            try
            {
                foreach (var plan in Directory.EnumerateFiles(dir, "*" + Plan.Extension).Order(StringComparer.Ordinal))
                    if (found.Count < MaxPerFolder) found.Add(plan);
                if (depth >= MaxDepth) return;
                foreach (var sub in Directory.EnumerateDirectories(dir).Order(StringComparer.Ordinal))
                    if (!Skip.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase)) Walk(sub, depth + 1);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Lee un plan del disco; si no se puede, la entrada lleva el motivo (y se ve en la lista, no desaparece).</summary>
    public static PlanEntry Read(string path, string folder)
    {
        try
        {
            var modified = File.GetLastWriteTime(path);
            return new PlanEntry(path, folder, Plan.Parse(File.ReadAllText(path)), null, modified);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            return new PlanEntry(path, folder, null, ex.Message, DateTime.MinValue);
        }
    }

    /// <summary>Todos los planes de esas carpetas y los agregados a mano, sin repetir, en ese orden.</summary>
    public IReadOnlyList<PlanEntry> Scan(IEnumerable<string> folders)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<PlanEntry>();
        foreach (var folder in folders)
            foreach (var path in Find(folder))
                if (seen.Add(path)) entries.Add(Read(path, folder));
        foreach (var path in Data.Extra.ToList())
            if (File.Exists(path) && seen.Add(path)) entries.Add(Read(path, Path.GetDirectoryName(path)!));
        return entries;
    }
}
