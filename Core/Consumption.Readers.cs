using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Claupet;

/// <summary>
/// Lo leído de un archivo de sesión: de qué agente y cuenta es, hasta dónde se leyó (Offset, para seguir desde ahí cuando
/// crece) y lo que sumó por hora. Model, Project, Provider, Totals y Recent son lo que hace falta para seguir leyendo donde
/// quedó: el modelo y la carpeta que se anotaron antes, los totales acumulados de Codex y las últimas respuestas de
/// Claude Code (una respuesta va en varias líneas con el mismo id).
/// </summary>
sealed class UsageFile
{
    public string Agent { get; set; } = "";
    public string Account { get; set; } = "";
    public string? Session { get; set; }
    public long Length { get; set; }
    public DateTime Modified { get; set; }
    public long Offset { get; set; }
    public List<UsageFileBucket> Buckets { get; set; } = [];
    public string? Model { get; set; }
    public string? Project { get; set; }
    public string? Provider { get; set; }
    public long[]? Totals { get; set; }
    public List<string>? Recent { get; set; }
}

/// <summary>Lo de una hora (horas desde 1970, en UTC) para un modelo y un proyecto.</summary>
sealed class UsageFileBucket
{
    public string Model { get; set; } = "";
    public string? Provider { get; set; }
    public string? Project { get; set; }
    public long Hour { get; set; }
    public UsageSums Sums { get; set; } = new();
}

/// <summary>De dónde se lee: las cuentas de Claude Code (nombre y carpeta) y los otros agentes que se siguen.</summary>
sealed record UsageSources(IReadOnlyList<(string Label, string ConfigDir)> Claude, IReadOnlyList<AgentAccount> Agents)
{
    public static UsageSources From(AppSettings s) =>
        new(s.Accounts.Select(a => (a.Label, a.ConfigDir)).ToList(), s.Agents.Where(a => a.Follow).ToList());
}

/// <summary>
/// El consumo de todos los agentes, leído de sus archivos en este equipo (nada se escribe en sus carpetas ni se usan sus
/// cuentas). Los registros de Claude Code pueden sumar gigas en un mes: cada archivo se lee una vez y, si crece, solo lo
/// nuevo; lo leído se guarda en consumo/cache.json para no volver a leerlo al abrir. OpenCode guarda todo en su base y se
/// consulta cada vez. Formatos, según lo que cada agente escribe:
/// - Claude Code: projects/**/*.jsonl, las respuestas («assistant») con message.usage y message.model; cwd, la carpeta.
/// - Codex: sessions/**/rollout-*.jsonl, los eventos token_count con los totales acumulados de la sesión
///   (info.total_token_usage); cada aumento es una respuesta. El modelo, de turn_context; la carpeta, de session_meta.
/// - Gemini CLI: tmp/&lt;proyecto&gt;/chats/session-*.json(l), las respuestas («gemini») con model y tokens (input incluye la
///   caché, como en su API). Un archivo se vuelve a leer entero al cambiar: sus mensajes se reescriben por id.
/// - OpenCode: opencode.db, los mensajes del asistente con tokens {input, output, reasoning, cache} y cost (dólares).
/// - Copilot CLI: session-state/&lt;id&gt;/events.jsonl, los eventos session.usage_record (data.usage: model, inputTokens,
///   outputTokens, cacheReadTokens, cacheWriteTokens y cost, que son solicitudes premium y no dólares). Se toma la
///   lectura de caché como parte de la entrada, como en la API de OpenAI que usa.
/// </summary>
sealed class UsageLedger
{
    const int Version = 1;
    /// <summary>Lo que se guarda: los archivos que cambiaron en este tiempo (los periodos llegan hasta 31 días).</summary>
    static readonly TimeSpan Keep = TimeSpan.FromDays(45);
    static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>La caché; null, solo en memoria.</summary>
    readonly string? _path;
    readonly object _lock = new();
    Dictionary<string, UsageFile> _files = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string?> _projects = new(StringComparer.OrdinalIgnoreCase);

    public UsageLedger(string? path = null)
    {
        _path = path ?? DataFolder.In("consumo", "cache.json");
        Load();
    }

    UsageLedger(bool memory) => _path = memory ? null : DataFolder.In("consumo", "cache.json");

    /// <summary>Solo en memoria, sin leer ni escribir la caché: la bandeja (lo de hoy), para no pisar la de la Console.</summary>
    public static UsageLedger InMemory() => new(memory: true);

    sealed class Stored
    {
        public int Version { get; set; }
        public Dictionary<string, UsageFile> Files { get; set; } = [];
    }

    void Load()
    {
        try
        {
            if (_path is null || !File.Exists(_path)) return;
            using var stream = File.OpenRead(_path);
            if (JsonSerializer.Deserialize<Stored>(stream, Options) is { Version: Version } stored)
                _files = new(stored.Files, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { ErrorLog.Write(ex); }
    }

    /// <summary>Guarda lo leído (sin los archivos que ya no cambian hace más de Keep).</summary>
    public void Save()
    {
        if (_path is null) return;
        lock (_lock)
        {
            var old = DateTime.UtcNow - Keep;
            foreach (var key in _files.Where(f => f.Value.Modified < old).Select(f => f.Key).ToList()) _files.Remove(key);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temp = _path + ".tmp";
                using (var stream = File.Create(temp)) JsonSerializer.Serialize(stream, new Stored { Version = Version, Files = _files }, Options);
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
        }
    }

    /// <summary>Un archivo por leer: su ruta, de quién es y cómo se lee.</summary>
    sealed record Source(FileInfo File, string Agent, string Account, Action<UsageFile, FileInfo> Read, bool Appends);

    /// <summary>
    /// Lee lo nuevo de los archivos que cambiaron desde since y devuelve lo gastado de ahí en adelante, por hora.
    /// progress: lo leído, de 0 a 1 (la primera vez puede tardar).
    /// </summary>
    public List<UsageRecord> Refresh(UsageSources sources, DateTimeOffset since, CancellationToken cancel = default, Action<double>? progress = null)
    {
        lock (_lock)
        {
            var files = Sources(sources, since);
            var pending = files.Where(s => !_files.TryGetValue(s.File.FullName, out var known)
                || known.Length != s.File.Length || known.Modified != s.File.LastWriteTimeUtc).ToList();
            long total = pending.Sum(s => Math.Max(1, s.File.Length - (s.Appends && _files.TryGetValue(s.File.FullName, out var k) && s.File.Length >= k.Offset ? k.Offset : 0)));
            long done = 0;
            foreach (var source in pending)
            {
                cancel.ThrowIfCancellationRequested();
                var path = source.File.FullName;
                if (!_files.TryGetValue(path, out var state) || !source.Appends || source.File.Length < state.Offset)
                    state = new UsageFile { Agent = source.Agent, Account = source.Account };
                var from = state.Offset;
                try { source.Read(state, source.File); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                state.Agent = source.Agent;
                state.Account = source.Account;
                state.Length = source.File.Length;
                state.Modified = source.File.LastWriteTimeUtc;
                _files[path] = state;
                done += Math.Max(1, source.File.Length - (source.Appends ? from : 0));
                progress?.Invoke(total == 0 ? 1 : Math.Min(1, (double)done / total));
            }

            var start = since.ToUnixTimeSeconds() / 3600;
            var records = new List<UsageRecord>();
            // Gemini CLI copia una sesión vieja (.json) a .jsonl al retomarla: de cada sesión vale el archivo más nuevo.
            var states = files.Select(s => _files.GetValueOrDefault(s.File.FullName)).OfType<UsageFile>()
                .GroupBy(f => f.Agent == Agents.Gemini && f.Session is { } id ? "gemini|" + id : Guid.NewGuid().ToString())
                .Select(g => g.MaxBy(f => f.Modified)!);
            foreach (var f in states)
                foreach (var b in f.Buckets)
                    if (b.Hour >= start)
                        records.Add(new UsageRecord(f.Agent, f.Account, f.Session, b.Model, b.Provider, b.Project,
                            DateTimeOffset.FromUnixTimeSeconds(b.Hour * 3600), b.Sums));
            foreach (var agent in sources.Agents.Where(a => a.Kind == Agents.OpenCode))
                records.AddRange(OpenCode(agent, since));
            progress?.Invoke(1);
            return records;
        }
    }

    /// <summary>Los archivos de cada fuente que cambiaron desde since (los demás no tienen nada de ese periodo).</summary>
    List<Source> Sources(UsageSources sources, DateTimeOffset since)
    {
        var after = since.UtcDateTime.AddHours(-1);
        var list = new List<Source>();
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        void Add(string folder, string pattern, string agent, string account, Action<UsageFile, FileInfo> read, bool appends, Func<FileInfo, bool>? keep = null)
        {
            if (!Directory.Exists(folder)) return;
            try
            {
                foreach (var file in new DirectoryInfo(folder).EnumerateFiles(pattern, options))
                    if (file.LastWriteTimeUtc >= after && (keep?.Invoke(file) ?? true)) list.Add(new Source(file, agent, account, read, appends));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
        }

        // Dos cuentas con la memoria compartida apuntan a la misma carpeta projects: se lee una vez, a nombre de la primera.
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (label, configDir) in sources.Claude)
        {
            var projects = new DirectoryInfo(Path.Combine(configDir, "projects"));
            if (!projects.Exists) continue;
            string real;
            try { real = projects.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? projects.FullName; }
            catch (Exception) { real = projects.FullName; }
            if (!roots.Add(Account.Normalize(real))) continue;
            Add(real, "*.jsonl", UsageAgents.Claude, label, ReadClaude, appends: true);
        }
        foreach (var agent in sources.Agents)
        {
            switch (agent.Kind)
            {
                case Agents.Codex: Add(Path.Combine(agent.Folder, "sessions"), "rollout-*.jsonl", agent.Kind, agent.Label, ReadCodex, appends: true); break;
                case Agents.Copilot: Add(Path.Combine(agent.Folder, "session-state"), "events.jsonl", agent.Kind, agent.Label, ReadCopilot, appends: true); break;
                case Agents.Gemini:
                    var projects = GeminiReader.Projects(Path.Combine(agent.Folder, "projects.json"));
                    Add(Path.Combine(agent.Folder, "tmp"), "*.json*", agent.Kind, agent.Label, (f, file) => ReadGemini(f, file, projects), appends: false, GeminiReader.IsChat);
                    break;
            }
        }
        return list;
    }

    // ── Cada agente ─────────────────────────────────────────────────────────────────────────

    /// <summary>Junta lo de cada hora en los baldes del archivo (un índice para no recorrerlos con cada línea).</summary>
    sealed class Buckets(UsageFile file)
    {
        readonly Dictionary<(string, string?, string?, long), UsageFileBucket> _index =
            file.Buckets.ToDictionary(b => (b.Model, b.Provider, b.Project, b.Hour));

        public void Add(string model, string? provider, string? project, DateTimeOffset at, UsageSums sums)
        {
            var hour = at.ToUnixTimeSeconds() / 3600;
            var key = (model, provider, project, hour);
            if (!_index.TryGetValue(key, out var bucket))
            {
                _index[key] = bucket = new UsageFileBucket { Model = model, Provider = provider, Project = project, Hour = hour };
                file.Buckets.Add(bucket);
            }
            bucket.Sums.Add(sums);
        }
    }

    /// <summary>Lee desde donde quedó, línea por línea (la última, si está a medio escribir, queda para la próxima).</summary>
    static void Lines(UsageFile f, FileInfo file, Action<ReadOnlyMemory<byte>> line)
    {
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.Seek(f.Offset, SeekOrigin.Begin);
        f.Offset += JsonLines.ForEach(stream, line);
    }

    /// <summary>El proyecto de una carpeta de trabajo, por su nombre (UsageLedger.ProjectName), una vez por carpeta.</summary>
    string? Project(string? cwd)
    {
        if (cwd is not { Length: > 0 }) return null;
        if (_projects.TryGetValue(cwd, out var known)) return known;
        string? name;
        try { name = ProjectName(cwd, Account.Home); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { name = null; }
        return _projects[cwd] = name ?? (Path.GetFileName(Path.TrimEndingDirectorySeparator(cwd)) is { Length: > 0 } last ? last : null);
    }

    static readonly System.Text.RegularExpressions.Regex SessionSuffix = new(@"-sesion-\d{8}-\d{6}(-\d+)?$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// El proyecto de una carpeta de trabajo, para agrupar: un agente trabaja en subcarpetas, worktrees o carpetas sueltas
    /// (reportes/sql/2026-11-13) y todo es del mismo proyecto.
    /// - Dentro de un repositorio git: el nombre del repositorio; si es un worktree (.git es un archivo «gitdir: …»), el
    ///   del repositorio principal.
    /// - Fuera de git (o si la carpeta ya no está), bajo la carpeta del usuario: la de segundo nivel (Documents\tienda\…
    ///   → «tienda»). Un worktree de sesión de Claupet que ya se borró pierde su «-sesion-…».
    /// - Si no, la carpeta misma.
    /// </summary>
    internal static string? ProjectName(string cwd, string home)
    {
        var full = Path.GetFullPath(cwd.Replace('/', Path.DirectorySeparatorChar));
        for (var dir = new DirectoryInfo(full); dir is not null; dir = dir.Parent)
        {
            var git = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(git)) return dir.Name;
            if (!File.Exists(git)) continue;
            var line = File.ReadLines(git).FirstOrDefault() ?? "";
            var target = line.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase) ? line[7..].Trim().Replace('/', Path.DirectorySeparatorChar) : "";
            var mark = target.IndexOf(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            return mark > 0 ? Path.GetFileName(target[..mark]) : dir.Name;
        }
        var root = Path.TrimEndingDirectorySeparator(home) + Path.DirectorySeparatorChar;
        string? name = null;
        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            var parts = full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            // AppData y las carpetas ocultas (.claude, .codex…) no son de proyectos: ahí vale la carpeta misma.
            if (parts.Length >= 2 && !parts[0].Equals("AppData", StringComparison.OrdinalIgnoreCase) && !parts[0].StartsWith('.')) name = parts[1];
        }
        name ??= Path.GetFileName(Path.TrimEndingDirectorySeparator(full));
        return name is { Length: > 0 } ? SessionSuffix.Replace(name, "") : null;
    }

    void ReadClaude(UsageFile f, FileInfo file)
    {
        // Las de un subagente van en <sesión>/subagents/: cuentan en la sesión que los llamó.
        f.Session ??= file.Directory?.Name == "subagents" ? file.Directory.Parent?.Name : Path.GetFileNameWithoutExtension(file.Name);
        var recent = new HashSet<string>(f.Recent ?? [], StringComparer.Ordinal);
        var order = new Queue<string>(f.Recent ?? []);
        var buckets = new Buckets(f);
        Lines(f, file, line =>
        {
            var bytes = line.Span;
            if (bytes.IndexOf("\"usage\""u8) < 0 || bytes.IndexOf("\"assistant\""u8) < 0) return;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var r = doc.RootElement;
                if (Json.Str(r, "type") != "assistant" || Json.Date(r, "timestamp") is not { } at) return;
                if (!r.TryGetProperty("message", out var m) || m.ValueKind != JsonValueKind.Object) return;
                if (!m.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object) return;
                // Una respuesta se escribe en varias líneas (una por bloque) con el mismo id: se cuenta una vez, como en Uso.
                var key = Json.Str(m, "id") + ":" + Json.Str(r, "requestId");
                if (key != ":")
                {
                    if (!recent.Add(key)) return;
                    order.Enqueue(key);
                    if (order.Count > 400) recent.Remove(order.Dequeue());
                }
                var model = Json.Str(m, "model") ?? "?";
                if (model.StartsWith('<')) return; // <synthetic>: mensajes locales, sin llamada al modelo
                if (Project(Json.Str(r, "cwd")) is { } project) f.Project = project;
                buckets.Add(model, "Anthropic", f.Project, at, new UsageSums
                {
                    Responses = 1,
                    Input = (long)Json.Num(u, "input_tokens"),
                    CacheRead = (long)Json.Num(u, "cache_read_input_tokens"),
                    CacheWrite = (long)Json.Num(u, "cache_creation_input_tokens"),
                    Output = (long)Json.Num(u, "output_tokens"),
                });
            }
            catch (JsonException) { }
        });
        f.Recent = [.. order];
    }

    void ReadCodex(UsageFile f, FileInfo file)
    {
        f.Session ??= Path.GetFileNameWithoutExtension(file.Name);
        var buckets = new Buckets(f);
        Lines(f, file, line =>
        {
            var bytes = line.Span;
            if (bytes.IndexOf("token_count"u8) < 0 && bytes.IndexOf("turn_context"u8) < 0 && bytes.IndexOf("session_meta"u8) < 0) return;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var r = doc.RootElement;
                if (Agents.Child(r, "payload") is not { ValueKind: JsonValueKind.Object } payload) return;
                switch (Json.Str(r, "type"))
                {
                    case "session_meta":
                        if (Project(Json.Str(payload, "cwd")) is { } project) f.Project = project;
                        if (Json.Str(payload, "model_provider") is { Length: > 0 } provider) f.Provider = ProviderName(provider);
                        break;
                    case "turn_context":
                        if (Json.Str(payload, "model") is { Length: > 0 } model) f.Model = model;
                        break;
                    case "event_msg" when Json.Str(payload, "type") == "token_count":
                        if (Agents.Child(payload, "info") is not { ValueKind: JsonValueKind.Object } info
                            || Agents.Child(info, "total_token_usage") is not { ValueKind: JsonValueKind.Object } total || Agents.Time(r, "timestamp") is not { } at) return;
                        long[] now = [N(total, "input_tokens"), N(total, "cached_input_tokens"), N(total, "cache_write_input_tokens"), N(total, "output_tokens"), N(total, "reasoning_output_tokens")];
                        var before = f.Totals ?? new long[5];
                        var delta = now.Select((v, i) => v - before[i]).ToArray();
                        // Los totales bajan si la sesión se compactó o empezó de nuevo: entonces vale lo de la última respuesta.
                        if (delta.Any(d => d < 0) && Agents.Child(info, "last_token_usage") is { ValueKind: JsonValueKind.Object } last)
                            delta = [N(last, "input_tokens"), N(last, "cached_input_tokens"), N(last, "cache_write_input_tokens"), N(last, "output_tokens"), N(last, "reasoning_output_tokens")];
                        f.Totals = now;
                        if (delta.All(d => d <= 0)) return; // el mismo total repetido (llega también con los límites)
                        buckets.Add(f.Model ?? "?", f.Provider ?? "OpenAI", f.Project, at, new UsageSums
                        {
                            Responses = 1,
                            Input = Math.Max(0, delta[0] - delta[1] - delta[2]),
                            CacheRead = Math.Max(0, delta[1]),
                            CacheWrite = Math.Max(0, delta[2]),
                            Output = Math.Max(0, delta[3]),
                            Reasoning = Math.Max(0, delta[4]),
                        });
                        break;
                }
            }
            catch (JsonException) { }
        });
    }

    void ReadCopilot(UsageFile f, FileInfo file)
    {
        f.Session ??= file.Directory?.Name;
        var buckets = new Buckets(f);
        Lines(f, file, line =>
        {
            var bytes = line.Span;
            if (bytes.IndexOf("usage_record"u8) < 0 && bytes.IndexOf("session.start"u8) < 0) return;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var r = doc.RootElement;
                if (Agents.Child(r, "data") is not { ValueKind: JsonValueKind.Object } data) return;
                switch (Json.Str(r, "type"))
                {
                    case "session.start":
                        if (Agents.Child(data, "context") is { ValueKind: JsonValueKind.Object } context && Project(Json.Str(context, "cwd")) is { } project) f.Project = project;
                        break;
                    case "session.usage_record":
                        if (Agents.Child(data, "usage") is not { ValueKind: JsonValueKind.Object } usage || Agents.Time(r, "timestamp") is not { } at) return;
                        var input = N(usage, "inputTokens");
                        var cached = N(usage, "cacheReadTokens");
                        buckets.Add(Json.Str(usage, "model") ?? "?", ProviderName(Json.Str(usage, "providerType")), f.Project, at, new UsageSums
                        {
                            Responses = 1,
                            Input = cached <= input ? input - cached : input,
                            CacheRead = cached,
                            CacheWrite = N(usage, "cacheWriteTokens"),
                            Output = N(usage, "outputTokens"),
                            Reasoning = N(usage, "reasoningTokens"),
                            Premium = Agents.Number(usage, "cost") ?? 0,
                        });
                        break;
                }
            }
            catch (JsonException) { }
        });
    }

    /// <summary>Gemini CLI: el archivo entero (sus mensajes se reescriben por id; vale el último), en los baldes de cero.</summary>
    void ReadGemini(UsageFile f, FileInfo file, Dictionary<string, string> projects)
    {
        f.Buckets = [];
        f.Offset = 0;
        var subagent = file.Directory?.Parent?.Name == "chats";
        var dir = (subagent ? file.Directory?.Parent?.Parent : file.Directory?.Parent)?.Name;
        f.Project = dir is null ? null : Project(projects.TryGetValue(dir, out var path) ? path : null) ?? dir;
        var messages = new Dictionary<string, (DateTimeOffset At, string Model, UsageSums Sums)>(StringComparer.Ordinal);
        string? session = null;
        void Message(JsonElement m)
        {
            if (m.ValueKind != JsonValueKind.Object || Json.Str(m, "type") != "gemini" || Json.Str(m, "id") is not { } id) return;
            if (Agents.Child(m, "tokens") is not { ValueKind: JsonValueKind.Object } t || Agents.Time(m, "timestamp") is not { } at) return;
            var input = N(t, "input");
            var cached = N(t, "cached");
            var thoughts = N(t, "thoughts");
            messages[id] = (at, Json.Str(m, "model") ?? "?", new UsageSums
            {
                Responses = 1,
                Input = Math.Max(0, input - cached),
                CacheRead = cached,
                Output = N(t, "output") + thoughts,
                Reasoning = thoughts,
            });
        }
        void Meta(JsonElement record)
        {
            session ??= Json.Str(record, "sessionId");
            if (Agents.Child(record, "messages") is { ValueKind: JsonValueKind.Array } list)
                foreach (var m in list.EnumerateArray()) Message(m);
        }
        try
        {
            if (file.Extension == ".json")
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 64 });
                Meta(doc.RootElement);
            }
            else
                foreach (var record in Agents.Lines(file.FullName))
                {
                    if (Agents.Child(record, "$set") is { ValueKind: JsonValueKind.Object } set) Meta(set);
                    else if (Agents.Child(record, "id") is { ValueKind: JsonValueKind.String } && Agents.Child(record, "$patch") is null) Message(record);
                    else if (Agents.Child(record, "sessionId") is { ValueKind: JsonValueKind.String }) Meta(record);
                }
        }
        catch (JsonException) { }
        var key = session ?? Path.GetFileNameWithoutExtension(file.Name);
        f.Session = subagent ? "subagente:" + key : key;
        var buckets = new Buckets(f);
        foreach (var (at, model, sums) in messages.Values) buckets.Add(model, "Google", f.Project, at, sums);
    }

    /// <summary>OpenCode: sus respuestas desde since, de su base (solo para leer), sumadas por hora.</summary>
    List<UsageRecord> OpenCode(AgentAccount agent, DateTimeOffset since)
    {
        var path = OpenCodeReader.DbPath(agent);
        var sums = new Dictionary<(string? Session, string Model, string? Provider, string? Project, long Hour), UsageSums>();
        if (!File.Exists(path)) return [];
        try
        {
            using var db = Db.OpenReadOnly(path);
            if (db is null || OpenCodeReader.Find(db) is not { } layout) return [];
            using var command = db.CreateCommand();
            command.CommandText = $"""
                SELECT m.data, m.time_created, {(layout.Parent ? "COALESCE(s.parent_id, s.id)" : "s.id")}, {(layout.Folder ? "s.directory" : "NULL")}
                FROM {layout.Messages} m JOIN {layout.Sessions} s ON s.id = m.session_id
                WHERE m.time_created >= $start {(layout.TypeColumn ? "AND m.type = 'assistant'" : "")}
                """;
            command.Parameters.AddWithValue("$start", since.ToUnixTimeMilliseconds());
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                using var doc = JsonDocument.Parse(reader.GetString(0), new JsonDocumentOptions { MaxDepth = 64 });
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                if (!layout.TypeColumn && (Json.Str(root, "role") ?? Json.Str(root, "type")) != "assistant") continue;
                if (Agents.Child(root, "tokens") is not { ValueKind: JsonValueKind.Object } t) continue;
                var model = Json.Str(root, "modelID") ?? (Agents.Child(root, "model") is { ValueKind: JsonValueKind.Object } m ? Json.Str(m, "id") : null) ?? "?";
                var provider = Json.Str(root, "providerID") ?? (Agents.Child(root, "model") is { ValueKind: JsonValueKind.Object } p ? Json.Str(p, "providerID") : null);
                var cache = Agents.Child(t, "cache") is { ValueKind: JsonValueKind.Object } c ? c : default;
                var reasoning = N(t, "reasoning");
                var at = OpenCodeReader.Time(reader.GetInt64(1));
                var key = (reader.IsDBNull(2) ? null : reader.GetString(2), model, ProviderName(provider), Project(reader.IsDBNull(3) ? null : reader.GetString(3)), at.ToUnixTimeSeconds() / 3600);
                if (!sums.TryGetValue(key, out var s)) sums[key] = s = new UsageSums();
                s.Add(new UsageSums
                {
                    Responses = 1,
                    Input = N(t, "input"),
                    CacheRead = cache.ValueKind == JsonValueKind.Object ? N(cache, "read") : 0,
                    CacheWrite = cache.ValueKind == JsonValueKind.Object ? N(cache, "write") : 0,
                    Output = N(t, "output") + reasoning,
                    Reasoning = reasoning,
                    Cost = Agents.Number(root, "cost") ?? 0,
                });
            }
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or InvalidOperationException) { ErrorLog.Write(ex); }
        return [.. sums.Select(s => new UsageRecord(Agents.OpenCode, agent.Label, s.Key.Session, s.Key.Model, s.Key.Provider, s.Key.Project,
            DateTimeOffset.FromUnixTimeSeconds(s.Key.Hour * 3600), s.Value))];
    }

    static long N(JsonElement e, string name) => (long)(Agents.Number(e, name) ?? 0);

    /// <summary>El proveedor como se escribe («openai» → OpenAI); los que no se conocen, tal cual.</summary>
    internal static string? ProviderName(string? id) => id?.ToLowerInvariant() switch
    {
        null or "" => null,
        "openai" => "OpenAI",
        "anthropic" => "Anthropic",
        "google" or "gemini" or "google-vertex" => "Google",
        "azure" or "azure-openai" => "Azure",
        "github-copilot" or "copilot" => "GitHub",
        "opencode" => "OpenCode",
        "deepseek" => "DeepSeek",
        "openrouter" => "OpenRouter",
        "xai" => "xAI",
        "mistral" => "Mistral",
        _ => id,
    };
}

/// <summary>Los límites que se conocen: los de Claude Code (lo último que respondió su servicio) y los de Codex.</summary>
static class UsageLimits
{
    public static List<UsageLimit> Read(AppSettings settings, DateTimeOffset now)
    {
        var list = new List<UsageLimit>();
        foreach (var account in settings.Accounts)
            if (UsageGate.Last(account.ConfigDir) is { } snapshot)
                foreach (var row in snapshot.Limits)
                    list.Add(row.ResetsAt is { } at && at <= now
                        ? new UsageLimit(UsageAgents.Claude, account.Label, row.Label, 0, null)
                        : new UsageLimit(UsageAgents.Claude, account.Label, row.Label, row.Percent, row.ResetsAt));
        foreach (var agent in settings.Agents.Where(a => a.Follow && a.Kind == Agents.Codex))
            foreach (var limit in Agents.CurrentLimits(Agents.Read(agent, now), now))
                list.Add(new UsageLimit(Agents.Codex, agent.Label, limit.Label, limit.Percent, limit.ResetsAt));
        return list;
    }
}

/// <summary>El consumo por modelo en CSV (con punto decimal y comas, para cualquier hoja de cálculo).</summary>
static class UsageCsv
{
    public static string Of(UsageReport report)
    {
        var text = new StringBuilder("agente,modelo,proveedor,respuestas,entrada,cache_leida,cache_escrita,salida,razonamiento,costo_usd,premium\n");
        static string Cell(string? s) => s is null ? "" : s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        foreach (var m in report.Models)
            text.Append(string.Join(',', Cell(UsageAgents.Name(m.Agent)), Cell(m.Model), Cell(m.Provider), m.Sums.Responses, m.Sums.Input, m.Sums.CacheRead,
                m.Sums.CacheWrite, m.Sums.Output, m.Sums.Reasoning, m.Sums.Cost?.ToString("0.####", CultureInfo.InvariantCulture) ?? "",
                m.Sums.Premium?.ToString("0.##", CultureInfo.InvariantCulture) ?? "")).Append('\n');
        return text.ToString();
    }
}
