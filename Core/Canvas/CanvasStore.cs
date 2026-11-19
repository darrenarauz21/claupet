using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Claupet;

/// <summary>
/// Lo que se muestra de un canvas en la lista, sin cargar su contenido. Project: la carpeta del proyecto con que se hizo
/// (la del repositorio principal si fue en un worktree de sesión), o null. Authors: quiénes trabajaron en él y con qué
/// modelo, del más reciente al más viejo: una línea cada uno, «autor» o «autor⇥modelo», en un solo texto para que dos
/// resúmenes iguales sigan siendo iguales.
/// </summary>
sealed record CanvasSummary(string Id, string Title, DateTimeOffset Updated, int Version, int Sections, string? LastAuthor, string? Plan = null,
    string? Project = null, string Authors = "")
{
    /// <summary>Cada autor con un modelo (o sin él, si no se sabe), del más reciente al más viejo.</summary>
    public IReadOnlyList<(string Author, string? Model)> Entries => Authors.Length == 0 ? []
        : [.. Authors.Split('\n').Select(line => line.IndexOf('\t') is var tab and >= 0 ? (line[..tab], (string?)line[(tab + 1)..]) : (line, null))];

    /// <summary>Quiénes trabajaron en él, sin repetir, del más reciente al más viejo.</summary>
    public IReadOnlyList<string> AuthorList => [.. Entries.Select(e => e.Author).Distinct()];

    /// <summary>Los modelos con que trabajó ese autor, del más reciente al más viejo (vacío si no se sabe).</summary>
    public IReadOnlyList<string> ModelsOf(string author) =>
        [.. Entries.Where(e => e.Author == author && e.Model is not null).Select(e => e.Model!).Distinct()];

    /// <summary>Del documento: los autores (con su modelo) de sus cambios, del último al primero, y los de sus secciones.</summary>
    public static CanvasSummary Of(CanvasDoc doc) => new(doc.Id, doc.Title, doc.Updated, doc.Version, doc.Sections.Count,
        doc.Changes.LastOrDefault()?.Author, doc.Plan,
        doc.Folder is { Length: > 0 } folder ? Path.TrimEndingDirectorySeparator(SessionWorktrees.ProjectOf(folder)) : null,
        string.Join('\n', doc.Changes.AsEnumerable().Reverse().Select(c => (c.Author, c.Model))
            .Concat(doc.Sections.OrderByDescending(s => s.Updated).Select(s => (s.Author, s.Model)))
            .Select(e => (e.Author ?? CanvasJson.User) + (e.Model is { Length: > 0 } m ? "\t" + m : ""))
            .Distinct()));
}

/// <summary>
/// Los canvas en disco: «Canvas\&lt;id&gt;.json» en la carpeta de datos de Claupet, y en «Canvas\historial\&lt;id&gt;\» una copia
/// de cada versión anterior (las últimas MaxVersions). Los escriben la app y los servidores MCP (otros procesos, uno por
/// agente): cada cambio lee, aplica y guarda con un candado de archivo, y se guarda entero o nada (un .tmp que se mueve).
/// </summary>
sealed class CanvasStore(string root)
{
    public const int MaxVersions = 60;
    const string Extension = ".json";

    public static CanvasStore Default => new(DataFolder.In("Canvas"));

    public string Root { get; } = root;
    string HistoryRoot => Path.Combine(Root, "historial");
    /// <summary>Lo que un agente pidió mostrar (canvas_open): la Console lo abre.</summary>
    public string OpenRequestPath => Path.Combine(Root, "abrir.txt");

    string FileOf(string id) => Path.Combine(Root, SafeId(id) + Extension);

    /// <summary>Un id es lo que se generó aquí (letras, números, guiones): nada que salga de la carpeta.</summary>
    static string SafeId(string id) =>
        id.Length is > 0 and <= 64 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? id : throw new CanvasException(L.T("Id de canvas no válido: {0}", id));

    /// <summary>
    /// El resumen de cada archivo ya leído, con su fecha y tamaño: la lista solo vuelve a leer (y a interpretar) los que
    /// cambiaron. Sin esto, cada cambio de una sección leía todos los canvas.
    /// </summary>
    readonly Dictionary<string, (DateTime Time, long Length, CanvasSummary Summary)> _summaries = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CanvasSummary> List()
    {
        if (!Directory.Exists(Root)) return [];
        var list = new List<CanvasSummary>();
        lock (_summaries)
        {
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in new DirectoryInfo(Root).EnumerateFiles("*" + Extension))
            {
                present.Add(info.FullName);
                if (_summaries.TryGetValue(info.FullName, out var known) && known.Time == info.LastWriteTimeUtc && known.Length == info.Length)
                {
                    list.Add(known.Summary);
                    continue;
                }
                if (Read(info.FullName) is not { } doc) continue; // a medio escribir o dañado: no se guarda, se lee la próxima vez
                var summary = CanvasSummary.Of(doc);
                _summaries[info.FullName] = (info.LastWriteTimeUtc, info.Length, summary);
                list.Add(summary);
            }
            foreach (var gone in _summaries.Keys.Where(k => !present.Contains(k)).ToList()) _summaries.Remove(gone);
        }
        return [.. list.OrderByDescending(s => s.Updated)];
    }

    public CanvasDoc? Load(string id) => Read(FileOf(id));

    /// <summary>
    /// Un canvas nuevo, vacío o con sus primeras secciones (las de un modelo: se validan como cualquier insert). model: con
    /// qué modelo trabajaba el agente, si se sabe; queda en el cambio y en las secciones que hizo.
    /// </summary>
    public CanvasDoc Create(string? title, string? author, IReadOnlyList<CanvasOp>? sections = null, string? folder = null, string? model = null)
    {
        var now = DateTimeOffset.Now;
        var doc = new CanvasDoc
        {
            Id = NewId(),
            Title = string.IsNullOrWhiteSpace(title) ? L.T("Canvas sin título") : title.Trim()[..Math.Min(title.Trim().Length, CanvasEngine.MaxTitle)],
            Folder = folder,
            Created = now,
            Updated = now,
        };
        var touched = new List<string>();
        if (sections is { Count: > 0 })
        {
            var applied = CanvasEngine.Apply(doc, [.. sections.Select(s => s with { Op = "insert", After = null })], author, now);
            doc = applied.Doc;
            touched.AddRange(applied.Sections);
        }
        doc.Version = 1;
        StampModel(doc, touched, model);
        doc.Changes.Add(new CanvasChange { Version = 1, Author = author, Model = model, Time = now, Summary = L.T("creó el canvas"), Sections = touched });
        WithLock(() => Write(doc));
        return doc;
    }

    /// <summary>Las secciones que tocó un cambio llevan el modelo de quien las cambió (null si fue la persona o no se sabe).</summary>
    static void StampModel(CanvasDoc doc, IEnumerable<string> touched, string? model)
    {
        foreach (var id in touched)
            if (doc.Section(id) is { } section) section.Model = model;
    }

    /// <summary>
    /// Aplica operaciones y guarda una versión nueva. baseVersion: la que leyó quien edita; si el canvas cambió desde
    /// entonces, las operaciones que dependen de posiciones podrían caer mal, así que se rechazan (las de «edit» buscan
    /// el texto y siguen valiendo: con baseVersion null no se compara).
    /// </summary>
    public CanvasApplied Apply(string id, IReadOnlyList<CanvasOp> ops, string? author, int? baseVersion = null, string? model = null) => WithLock(() =>
    {
        var current = Load(id) ?? throw new CanvasException(L.T("No hay un canvas {0}.", id));
        if (baseVersion is { } expected && expected != current.Version)
            throw new CanvasException(L.T("El canvas cambió (va en la versión {0}, usted leyó la {1}): léalo otra vez y vuelva a intentar.", current.Version, expected));
        var now = DateTimeOffset.Now;
        var applied = CanvasEngine.Apply(current, ops, author, now);
        StampModel(applied.Doc, applied.Sections, model);
        Save(current, applied.Doc, author, now, applied.Summary, applied.Sections, model);
        return applied;
    });

    /// <summary>Vuelve a una versión anterior, como un cambio más (la de ahora queda en el historial).</summary>
    public CanvasDoc Restore(string id, int version, string? author) => WithLock(() =>
    {
        var current = Load(id) ?? throw new CanvasException(L.T("No hay un canvas {0}.", id));
        var old = LoadVersion(id, version) ?? throw new CanvasException(L.T("No está la versión {0}.", version));
        var doc = old.Clone();
        doc.Changes = current.Changes;
        var now = DateTimeOffset.Now;
        Save(current, doc, author, now, L.T("volvió a la versión {0}", version), [.. doc.Sections.Select(s => s.Id)]);
        return doc;
    });

    void Save(CanvasDoc previous, CanvasDoc doc, string? author, DateTimeOffset now, string summary, IReadOnlyList<string> sections, string? model = null)
    {
        // La anterior, al historial (las más viejas se borran).
        var history = Path.Combine(HistoryRoot, SafeId(previous.Id));
        Directory.CreateDirectory(history);
        WriteFile(Path.Combine(history, previous.Version + Extension), previous);
        foreach (var old in Versions(previous.Id).Skip(MaxVersions)) TryDelete(Path.Combine(history, old + Extension));

        doc.Id = previous.Id;
        doc.Created = previous.Created;
        doc.Version = previous.Version + 1;
        doc.Updated = now;
        doc.Changes.Add(new CanvasChange { Version = doc.Version, Author = author, Model = model, Time = now, Summary = summary, Sections = [.. sections] });
        if (doc.Changes.Count > CanvasDoc.MaxChanges) doc.Changes.RemoveRange(0, doc.Changes.Count - CanvasDoc.MaxChanges);
        Write(doc);
    }

    /// <summary>Las versiones guardadas en el historial, de la más nueva a la más vieja.</summary>
    public IReadOnlyList<int> Versions(string id)
    {
        var history = Path.Combine(HistoryRoot, SafeId(id));
        if (!Directory.Exists(history)) return [];
        return [.. Directory.EnumerateFiles(history, "*" + Extension)
            .Select(f => int.TryParse(Path.GetFileNameWithoutExtension(f), out var v) ? v : -1)
            .Where(v => v > 0).OrderByDescending(v => v)];
    }

    public CanvasDoc? LoadVersion(string id, int version) =>
        Load(id) is { } current && current.Version == version ? current : Read(Path.Combine(HistoryRoot, SafeId(id), version + Extension));

    /// <summary>Lo borrado queda aquí, con su historial, para poder deshacerlo; pasado TrashKeep se borra de verdad.</summary>
    string TrashRoot => Path.Combine(Root, "papelera");
    public static readonly TimeSpan TrashKeep = TimeSpan.FromDays(30);

    /// <summary>Lo pasa a la papelera con su historial (Undelete lo devuelve) y vacía lo que lleva más de TrashKeep ahí.</summary>
    public void Delete(string id) => WithLock(() =>
    {
        var safe = SafeId(id);
        var file = FileOf(id);
        if (File.Exists(file))
        {
            Directory.CreateDirectory(TrashRoot);
            var trashed = Path.Combine(TrashRoot, safe + Extension);
            File.Move(file, trashed, overwrite: true);
            // La fecha del archivo pasa a ser la de cuando se borró: con ella se vacía la papelera.
            File.SetLastWriteTimeUtc(trashed, DateTime.UtcNow);
        }
        MoveFolder(Path.Combine(HistoryRoot, safe), Path.Combine(TrashRoot, "historial", safe));
        EmptyTrash(DateTime.UtcNow - TrashKeep);
        return 0;
    });

    /// <summary>Devuelve de la papelera un canvas borrado, con su historial. False si ya no está ahí o si hay otro con su id.</summary>
    public bool Undelete(string id) => WithLock(() =>
    {
        var safe = SafeId(id);
        var trashed = Path.Combine(TrashRoot, safe + Extension);
        var file = FileOf(id);
        if (!File.Exists(trashed) || File.Exists(file)) return false;
        File.Move(trashed, file);
        MoveFolder(Path.Combine(TrashRoot, "historial", safe), Path.Combine(HistoryRoot, safe));
        return true;
    });

    /// <summary>Borra de la papelera lo que se borró antes de esa fecha (UTC).</summary>
    void EmptyTrash(DateTime before)
    {
        if (!Directory.Exists(TrashRoot)) return;
        foreach (var old in new DirectoryInfo(TrashRoot).EnumerateFiles("*" + Extension).Where(f => f.LastWriteTimeUtc < before).ToList())
        {
            var id = Path.GetFileNameWithoutExtension(old.Name);
            TryDelete(old.FullName);
            try { Directory.Delete(Path.Combine(TrashRoot, "historial", id), recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Mueve una carpeta (pisando la de destino); si no está o no se puede, se deja como está.</summary>
    static void MoveFolder(string from, string to)
    {
        try
        {
            if (!Directory.Exists(from)) return;
            if (Directory.Exists(to)) Directory.Delete(to, recursive: true);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            Directory.Move(from, to);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Que la Console muestre este canvas (lo pide un agente; si la Console no está abierta, lo verá al abrirla).</summary>
    public void RequestOpen(string id)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(OpenRequestPath, SafeId(id), new UTF8Encoding(false));
    }

    /// <summary>El canvas que se pidió mostrar, una sola vez (se borra el pedido).</summary>
    public string? TakeOpenRequest()
    {
        try
        {
            if (!File.Exists(OpenRequestPath)) return null;
            var id = File.ReadAllText(OpenRequestPath).Trim();
            File.Delete(OpenRequestPath);
            return id.Length > 0 && File.Exists(FileOf(id)) ? id : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CanvasException) { return null; }
    }

    // ── Archivos ────────────────────────────────────────────────────────────────────────────

    static string NewId() => DateTime.Now.ToString("yyyyMMdd") + "-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();

    static CanvasDoc? Read(string file)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (!File.Exists(file)) return null;
                var doc = JsonSerializer.Deserialize<CanvasDoc>(File.ReadAllText(file), CanvasJson.Options);
                return doc is { Id.Length: > 0 } ? doc : null;
            }
            catch (IOException) when (attempt < 5) { Thread.Sleep(40); }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
        }
    }

    void Write(CanvasDoc doc)
    {
        Directory.CreateDirectory(Root);
        WriteFile(FileOf(doc.Id), doc);
    }

    static void WriteFile(string file, CanvasDoc doc)
    {
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(doc, CanvasJson.Options), new UTF8Encoding(false));
        File.Move(tmp, file, overwrite: true);
    }

    static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Un candado entre procesos: un archivo abierto sin compartir. Si otro lo tiene, se espera (hasta 5 s).</summary>
    T WithLock<T>(Func<T> action)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, ".candado");
        var until = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            FileStream? handle = null;
            try { handle = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < until) { Thread.Sleep(25); continue; }
            catch (IOException) { throw new CanvasException(L.T("El canvas está ocupado; intente de nuevo.")); }
            using (handle) return action();
        }
    }

    void WithLock(Action action) => WithLock(() => { action(); return 0; });
}

/// <summary>
/// Avisa cuando cambia un canvas en disco (lo cambió la app, otro proceso o un agente por MCP), o cuando un agente pide
/// mostrar uno. Los avisos llegan en otro hilo y juntos (varios cambios seguidos, un solo aviso por canvas).
/// </summary>
sealed class CanvasWatcher : IDisposable
{
    readonly FileSystemWatcher _watcher;
    readonly Timer _timer;
    readonly HashSet<string> _pending = [];
    bool _open;

    public event Action<string>? Changed;
    public event Action? OpenRequested;

    public CanvasWatcher(CanvasStore store)
    {
        Directory.CreateDirectory(store.Root);
        _timer = new Timer(_ => Flush());
        _watcher = new FileSystemWatcher(store.Root) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
        FileSystemEventHandler onChange = (_, e) => Queue(e.Name);
        _watcher.Changed += onChange;
        _watcher.Created += onChange;
        _watcher.Deleted += onChange;
        _watcher.Renamed += (_, e) => Queue(e.Name);
        _watcher.EnableRaisingEvents = true;
    }

    void Queue(string? name)
    {
        if (name is null) return;
        lock (_pending)
        {
            if (name == "abrir.txt") _open = true;
            else if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) _pending.Add(Path.GetFileNameWithoutExtension(name));
            else return;
        }
        _timer.Change(120, Timeout.Infinite);
    }

    void Flush()
    {
        string[] ids;
        bool open;
        lock (_pending)
        {
            ids = [.. _pending];
            _pending.Clear();
            open = _open;
            _open = false;
        }
        foreach (var id in ids) Changed?.Invoke(id);
        if (open) OpenRequested?.Invoke();
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _timer.Dispose();
    }
}
