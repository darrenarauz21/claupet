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
        /// <summary>Los proyectos desvinculados (su carpeta): no se ven ni en Proyectos ni en Canvas.</summary>
        public List<string> Hidden { get; set; } = [];
        /// <summary>Los planes desvinculados uno por uno.</summary>
        public List<string> HiddenPlans { get; set; } = [];
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

    /// <summary>Lo agregado a mano se lee y se cambia desde varios hilos (las búsquedas van aparte de la interfaz).</summary>
    readonly object _extraLock = new();

    /// <summary>Suma un plan que no está en una carpeta vinculada (si estaba desvinculado, vuelve a verse).</summary>
    public void Add(string path)
    {
        bool shown;
        lock (_extraLock)
        {
            shown = Data.HiddenPlans.RemoveAll(p => Same(p, path)) > 0;
            var added = !Data.Extra.Contains(path, StringComparer.OrdinalIgnoreCase);
            if (added) Data.Extra.Add(path);
            if (added || shown) Save();
        }
        if (shown) HiddenChanged?.Invoke();
    }

    /// <summary>Lo desvinculado cambió: Proyectos y Canvas vuelven a dibujar su lista.</summary>
    public event Action? HiddenChanged;

    /// <summary>La clave de un proyecto: su carpeta, sin la barra del final.</summary>
    public static string Key(string folder) => Path.TrimEndingDirectorySeparator(folder);

    static bool Same(string a, string b) => string.Equals(Key(a), Key(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Si el proyecto de esa carpeta está desvinculado.</summary>
    public bool IsHidden(string? folder)
    {
        if (folder is not { Length: > 0 }) return false;
        lock (_extraLock) return Data.Hidden.Any(f => Same(f, folder));
    }

    public IReadOnlyList<string> HiddenFolders
    {
        get { lock (_extraLock) return [.. Data.Hidden]; }
    }

    public IReadOnlyList<string> HiddenPlans
    {
        get { lock (_extraLock) return [.. Data.HiddenPlans]; }
    }

    /// <summary>Desvincula un proyecto: deja de verse en Proyectos y en Canvas. La carpeta no se toca.</summary>
    public void Hide(string folder) => ChangeHidden(() =>
    {
        if (Data.Hidden.Any(f => Same(f, folder))) return false;
        Data.Hidden.Add(Key(folder));
        return true;
    });

    /// <summary>Desvincula un solo plan: deja de verse en la lista. El archivo no se toca.</summary>
    public void HidePlan(string path) => ChangeHidden(() =>
    {
        var changed = Data.Extra.RemoveAll(p => Same(p, path)) > 0;
        if (Data.HiddenPlans.Any(p => Same(p, path))) return changed;
        Data.HiddenPlans.Add(path);
        return true;
    });

    /// <summary>Vuelve a mostrar un proyecto o un plan desvinculado.</summary>
    public void Unhide(string path) => ChangeHidden(() => Data.Hidden.RemoveAll(f => Same(f, path)) + Data.HiddenPlans.RemoveAll(p => Same(p, path)) > 0);

    /// <summary>Un plan que se borró: sale de lo agregado a mano y de lo desvinculado.</summary>
    public void Forget(string path) => ChangeHidden(() => Data.Extra.RemoveAll(p => Same(p, path)) + Data.HiddenPlans.RemoveAll(p => Same(p, path)) > 0);

    void ChangeHidden(Func<bool> change)
    {
        bool changed;
        lock (_extraLock)
        {
            changed = change();
            if (changed) Save();
        }
        if (changed) HiddenChanged?.Invoke();
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            string json;
            lock (_extraLock) json = JsonSerializer.Serialize(Data);
            File.WriteAllText(_file, json);
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
        List<string> extra, hidden, hiddenPlans;
        lock (_extraLock)
        {
            extra = [.. Data.Extra];
            hidden = [.. Data.Hidden];
            hiddenPlans = [.. Data.HiddenPlans];
        }
        // Lo desvinculado no se lee: ni el proyecto entero ni el plan suelto.
        foreach (var path in hiddenPlans) seen.Add(path);
        foreach (var folder in folders)
            if (!hidden.Any(f => Same(f, folder)))
                foreach (var path in Find(folder))
                    if (seen.Add(path)) entries.Add(Read(path, folder));
        foreach (var path in extra)
            if (File.Exists(path) && !hidden.Any(f => Same(f, Path.GetDirectoryName(path)!)) && seen.Add(path)) entries.Add(Read(path, Path.GetDirectoryName(path)!));
        return entries;
    }
}
