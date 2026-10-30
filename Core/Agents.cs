using System.Globalization;
using System.Text.Json;

namespace Claupet;

/// <summary>
/// Otro agente que Claupet sigue además de Claude Code (Codex CLI, Gemini CLI o GitHub Copilot CLI): su carpeta en este
/// equipo. Va en una lista aparte de las cuentas de Claude (AppSettings.Agents); Follow apagado lo deja de mostrar sin que
/// la detección lo vuelva a agregar.
/// </summary>
sealed class AgentAccount
{
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    public string Folder { get; set; } = "";
    public bool Follow { get; set; } = true;
}

/// <summary>Dónde quedó la mascota de un agente en el escritorio, en pixeles.</summary>
sealed record PetSpot(int X, int Y);

/// <summary>Un límite de uso que el agente deja escrito (Codex: la ventana de 5 h y la semanal).</summary>
sealed record AgentLimit(string Label, double Percent, DateTimeOffset? ResetsAt, int WindowMinutes);

/// <summary>Una sesión de otro agente: lo que se pudo leer de sus archivos (lo que no está, va vacío).</summary>
sealed record AgentSession(string Kind, string Id, string? Folder, string? Model, DateTimeOffset LastActivity, int Messages);

/// <summary>
/// Lo leído de un agente: sus límites (si los escribe), sus sesiones recientes y lo de hoy. ResponsesToday y TokensToday,
/// solo de los que los anotan (Gemini): las respuestas del modelo y los tokens que generó (salida y razonamiento).
/// </summary>
sealed record AgentSnapshot(AgentAccount Account, string Name, bool Exists, IReadOnlyList<AgentLimit> Limits,
    IReadOnlyList<AgentSession> Sessions, int SessionsToday, int MessagesToday, int Unreadable, int ResponsesToday = 0, long TokensToday = 0)
{
    public DateTimeOffset? LastActivity => Sessions.Count == 0 ? null : Sessions.Max(s => s.LastActivity);
}

/// <summary>
/// Los otros agentes: cuáles hay en este equipo y qué dicen sus archivos. Solo se leen (nada se escribe en sus carpetas),
/// sin sesión ni tokens. Los formatos salen de lo que documenta la comunidad, no de una especificación: se leen con
/// tolerancia y lo que no se entiende se salta.
/// </summary>
static class Agents
{
    public const string Codex = "codex", Gemini = "gemini", Copilot = "copilot";

    /// <summary>Cuántas sesiones de cada agente se leen (las más recientes): sus archivos pueden ser grandes.</summary>
    const int Recent = 30;

    public static readonly string[] Kinds = [Codex, Gemini, Copilot];

    public static string Name(string kind) => kind switch
    {
        Codex => "Codex CLI",
        Gemini => "Gemini CLI",
        Copilot => "GitHub Copilot CLI",
        _ => kind,
    };

    /// <summary>
    /// Su carpeta de siempre: $CODEX_HOME o ~/.codex, $COPILOT_HOME o ~/.copilot, y .gemini en $GEMINI_CLI_HOME o en la
    /// carpeta del usuario (Gemini CLI toma esa variable como su «home», no como su carpeta).
    /// </summary>
    public static string DefaultFolder(string kind)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return kind switch
        {
            Codex => Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } codex ? codex : Path.Combine(home, ".codex"),
            Copilot => Environment.GetEnvironmentVariable("COPILOT_HOME") is { Length: > 0 } copilot ? copilot : Path.Combine(home, ".copilot"),
            _ => Path.Combine(Environment.GetEnvironmentVariable("GEMINI_CLI_HOME") is { Length: > 0 } gemini ? gemini : home, ".gemini"),
        };
    }

    /// <summary>Agrega los agentes que están en su carpeta de siempre y todavía no están en la lista. folders: para las pruebas.</summary>
    public static bool Detect(AppSettings settings, Func<string, string>? folders = null)
    {
        var changed = false;
        foreach (var kind in Kinds)
        {
            if (settings.Agents.Any(a => a.Kind == kind)) continue;
            var folder = (folders ?? DefaultFolder)(kind);
            if (!Directory.Exists(folder)) continue;
            settings.Agents.Add(new AgentAccount { Kind = kind, Label = Name(kind), Folder = folder });
            changed = true;
        }
        return changed;
    }

    /// <summary>Lo que Ajustes › Cuentas dice de cada cuenta de Claude Code: con quién comparte la memoria, o que es propia.</summary>
    public static IReadOnlyList<AccountState> AccountStates(AppSettings settings) => settings.Accounts.Select(a =>
        new AccountState(a.Label, AccountMemory.SharedWith(a.ConfigDir) is { } with
            ? L.T("Memoria compartida con «{0}»", settings.Accounts.FirstOrDefault(o => Account.SamePath(o.ConfigDir, with))?.Label ?? Account.LabelFor(with))
            : L.T("Memoria propia"))).ToList();

    /// <summary>Lo que Ajustes › Cuentas dice de cada agente: su carpeta y, si se leyó, su última sesión.</summary>
    public static IReadOnlyList<AgentState> AgentStates(AppSettings settings, IReadOnlyList<AgentSnapshot> read) => settings.Agents.Select(a =>
    {
        var folder = a.Folder.StartsWith(Account.Home, StringComparison.OrdinalIgnoreCase) ? "~" + a.Folder[Account.Home.Length..] : a.Folder;
        var snapshot = read.FirstOrDefault(s => s.Account == a);
        var detail = snapshot is { Exists: false } ? L.T("{0} · no está en este equipo", folder)
            : snapshot?.LastActivity is { } last ? L.T("{0} · última sesión {1}", folder, Format.Ago(last))
            : folder;
        return new AgentState(a.Kind, a.Label, a.Follow, detail, ChibiSprite.SkinOf(PetOf(settings, a.Kind))?.Name ?? L.T("Ninguna"));
    }).ToList();

    /// <summary>El personaje de cada agente si no se eligió otro: Ciel para Codex y Gemma para Gemini; Copilot, ninguno.</summary>
    public static string DefaultPet(string kind) => kind switch { Codex => "ciel", Gemini => "gemma", _ => "" };

    /// <summary>El personaje elegido para ese agente (cualquiera sirve para cualquiera), o vacío si no lleva mascota.</summary>
    public static string PetOf(AppSettings settings, string kind) =>
        settings.AgentPet.TryGetValue(kind, out var id) && (id.Length == 0 || ChibiSprite.SkinOf(id) is not null) ? id : DefaultPet(kind);

    /// <summary>
    /// Cuándo escribió el agente por última vez en sus sesiones. Solo mira la fecha de los archivos, sin leerlos, así que se
    /// puede preguntar seguido (la mascota de cada agente teclea mientras trabaja). null si no hay sesiones.
    /// </summary>
    public static DateTimeOffset? LastWrite(AgentAccount agent)
    {
        var (folder, pattern, depth) = agent.Kind switch
        {
            Codex => ("sessions", "rollout-*.jsonl", 4),
            Gemini => ("tmp", "*.json*", 3),
            Copilot => ("session-state", "events.jsonl", 1),
            _ => ("", "", 0),
        };
        if (pattern.Length == 0) return null;
        Func<FileInfo, bool>? keep = agent.Kind == Gemini ? GeminiReader.IsChat : null;
        try
        {
            return Newest(Path.Combine(agent.Folder, folder), pattern, depth, keep).FirstOrDefault() is { } file
                ? new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Si sale en Uso un agente que se sigue: solo si hoy hizo algo o tiene un límite que todavía corre (los de una sesión
    /// de hace días ya se reiniciaron y no dicen nada). Sin nada que mostrar no se ve, y si no queda ninguno, tampoco el
    /// título «Otros agentes». Los que no se siguen (Ajustes › Cuentas) nunca se leen.
    /// </summary>
    public static bool ShowsInUsage(AgentSnapshot s, DateTimeOffset now) => s.Exists && (s.SessionsToday > 0 || CurrentLimits(s, now).Count > 0);

    /// <summary>Los límites que todavía corren: el reinicio todavía no llegó (sin fecha de reinicio, se toman como vigentes).</summary>
    public static IReadOnlyList<AgentLimit> CurrentLimits(AgentSnapshot s, DateTimeOffset now) =>
        s.Limits.Where(l => l.ResetsAt is not { } at || at > now).ToList();

    /// <summary>
    /// Lo de hoy de un agente, para Uso: sesiones y, si se pueden contar, respuestas y tokens (Gemini, como los de Claude
    /// en el pie), mensajes (Gemini sin tokens anotados) o eventos (Copilot).
    /// </summary>
    public static string Today(AgentSnapshot s)
    {
        if (s.SessionsToday == 0) return L.T("Hoy: sin actividad");
        var sessions = s.SessionsToday == 1 ? L.T("1 sesión") : L.T("{0} sesiones", s.SessionsToday);
        return s.Account.Kind switch
        {
            Gemini when s.TokensToday > 0 => L.T("Hoy: {0} · {1} respuestas · {2} tokens", sessions, s.ResponsesToday, Format.Tokens(s.TokensToday)),
            Gemini => L.T("Hoy: {0} · {1} mensajes", sessions, s.MessagesToday),
            Copilot => L.T("Hoy: {0} · {1} eventos", sessions, s.MessagesToday),
            _ => L.T("Hoy: {0}", sessions),
        };
    }

    /// <summary>Lee los que se siguen. now: el momento de «hoy» (las pruebas lo fijan).</summary>
    public static IReadOnlyList<AgentSnapshot> ReadAll(IEnumerable<AgentAccount> agents, DateTimeOffset now) =>
        agents.Where(a => a.Follow).Select(a => Read(a, now)).ToList();

    public static AgentSnapshot Read(AgentAccount agent, DateTimeOffset now)
    {
        if (!Directory.Exists(agent.Folder)) return new(agent, Name(agent.Kind), false, [], [], 0, 0, 0);
        try
        {
            return agent.Kind switch
            {
                Codex => CodexReader.Read(agent, now),
                Gemini => GeminiReader.Read(agent, now),
                Copilot => CopilotReader.Read(agent, now),
                _ => new(agent, agent.Label, true, [], [], 0, 0, 0),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex);
            return new(agent, Name(agent.Kind), true, [], [], 0, 0, 0);
        }
    }

    /// <summary>
    /// Los archivos más recientes que cumplen el patrón, bajo esa carpeta (sin seguir enlaces). keep descarta antes de
    /// elegir los más recientes, para que otros archivos que cambian seguido no ocupen su lugar.
    /// </summary>
    internal static List<FileInfo> Newest(string folder, string pattern, int depth, Func<FileInfo, bool>? keep = null)
    {
        if (!Directory.Exists(folder)) return [];
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = depth, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        return new DirectoryInfo(folder).EnumerateFiles(pattern, options).Where(f => keep?.Invoke(f) ?? true)
            .OrderByDescending(f => f.LastWriteTimeUtc).Take(Recent).ToList();
    }

    /// <summary>Las líneas de un JSONL, una por una; las rotas se saltan.</summary>
    internal static IEnumerable<JsonElement> Lines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] != '{') continue;
            JsonElement element;
            try
            {
                using var doc = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 64 });
                element = doc.RootElement.Clone();
            }
            catch (JsonException) { continue; }
            yield return element;
        }
    }

    internal static JsonElement? Child(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var c) ? c : null;

    internal static DateTimeOffset? Time(JsonElement e, string name) =>
        Child(e, name) is { ValueKind: JsonValueKind.String } t && DateTimeOffset.TryParse(t.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var when) ? when : null;

    internal static double? Number(JsonElement e, string name) =>
        Child(e, name) is { ValueKind: JsonValueKind.Number } n && n.TryGetDouble(out var d) ? d : null;

    internal static bool SameDay(DateTimeOffset a, DateTimeOffset now) => a.ToLocalTime().Date == now.ToLocalTime().Date;
}

/// <summary>
/// Codex CLI ($CODEX_HOME o ~/.codex): una sesión por archivo, sessions/AAAA/MM/DD/rollout-*.jsonl. Los límites van en
/// los eventos «event_msg» con payload.type «token_count»: payload.rate_limits.primary y .secondary, con used_percent,
/// window_minutes (299: 5 h; 10079: la semana) y resets_in_seconds, que se cuentan desde el timestamp del evento. Del
/// primer evento («session_meta») y del contexto del turno («turn_context») se toman la carpeta y el modelo si están. Los
/// comprimidos (.jsonl.zst) no se leen: se cuentan aparte.
/// </summary>
static class CodexReader
{
    public static AgentSnapshot Read(AgentAccount agent, DateTimeOffset now)
    {
        var sessions = new List<AgentSession>();
        (DateTimeOffset At, JsonElement Limits)? latest = null;
        foreach (var file in Agents.Newest(Path.Combine(agent.Folder, "sessions"), "rollout-*.jsonl", 4))
        {
            string? folder = null, model = null, id = null;
            var last = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
            DateTimeOffset? first = null;
            foreach (var line in Agents.Lines(file.FullName))
            {
                var at = Agents.Time(line, "timestamp");
                if (at is { } t)
                {
                    first ??= t;
                    if (t > last) last = t;
                }
                var payload = Agents.Child(line, "payload");
                switch (Agents.Child(line, "type")?.GetString())
                {
                    case "session_meta" when payload is { } meta:
                        id ??= Agents.Child(meta, "id")?.ToString();
                        folder ??= Agents.Child(meta, "cwd") is { ValueKind: JsonValueKind.String } cwd ? cwd.GetString() : null;
                        break;
                    case "turn_context" when payload is { } context:
                        if (Agents.Child(context, "model") is { ValueKind: JsonValueKind.String } m) model = m.GetString();
                        break;
                    case "event_msg" when payload is { } message && Agents.Child(message, "type")?.ToString() == "token_count":
                        if (at is { } when && Agents.Child(message, "rate_limits") is { ValueKind: JsonValueKind.Object } written
                            && (latest is null || when > latest.Value.At))
                            latest = (when, written);
                        break;
                }
            }
            sessions.Add(new AgentSession(Agents.Codex, id ?? Path.GetFileNameWithoutExtension(file.Name), folder, model, last, 0));
        }
        var compressed = Agents.Newest(Path.Combine(agent.Folder, "sessions"), "rollout-*.jsonl.zst", 4).Count;
        var limits = new List<AgentLimit>();
        if (latest is { } read)
            foreach (var name in new[] { "primary", "secondary" })
                if (Agents.Child(read.Limits, name) is { ValueKind: JsonValueKind.Object } window && Limit(window, read.At, now) is { } limit)
                    limits.Add(limit);
        var today = sessions.Where(s => Agents.SameDay(s.LastActivity, now)).ToList();
        return new AgentSnapshot(agent, agent.Label, true, limits, sessions.OrderByDescending(s => s.LastActivity).ToList(), today.Count,
            today.Sum(s => s.Messages), compressed);
    }

    /// <summary>Una ventana: si ya se reinició desde que se escribió, vuelve a 0.</summary>
    internal static AgentLimit? Limit(JsonElement window, DateTimeOffset at, DateTimeOffset now)
    {
        if (Agents.Number(window, "used_percent") is not { } used) return null;
        var minutes = (int)(Agents.Number(window, "window_minutes") ?? 0);
        DateTimeOffset? resets = Agents.Number(window, "resets_in_seconds") is { } seconds ? at.AddSeconds(seconds) : null;
        if (resets is { } r && r <= now) (used, resets) = (0, null);
        return new AgentLimit(WindowLabel(minutes), Math.Clamp(used, 0, 100), resets, minutes);
    }

    /// <summary>299 o 300 minutos: la sesión de 5 h; 10079 o 10080: la semana; lo demás, en horas o días.</summary>
    internal static string WindowLabel(int minutes) => minutes switch
    {
        >= 295 and <= 305 => L.T("Sesión (5 h)"),
        >= 10070 and <= 10090 => L.T("Semana"),
        <= 0 => L.T("Límite"),
        < 1440 => L.T("Ventana de {0} h", (int)Math.Round(minutes / 60.0)),
        _ => L.T("Ventana de {0} días", (int)Math.Round(minutes / 1440.0)),
    };
}

/// <summary>
/// Gemini CLI (~/.gemini), según su código (packages/core/src/services/chatRecordingService.ts y chatRecordingTypes.ts,
/// v0.66): una sesión por archivo en tmp/&lt;proyecto&gt;/chats/.
/// - session-*.jsonl, el formato actual: un registro JSON por línea que solo se agrega. Los datos de la sesión (sessionId,
///   projectHash, startTime, lastUpdated); cada mensaje con su id, que se vuelve a agregar entero si cambia (vale el
///   último); $set con datos nuevos (o, antes, todos los mensajes), y $rewindTo o $patch al deshacer o editar.
/// - session-*.json, el anterior: un solo documento con messages. Al retomarla, Gemini CLI la copia a .jsonl y deja la
///   vieja: se juntan por sessionId.
/// - Los subagentes, en chats/&lt;sesión&gt;/&lt;id&gt;.jsonl: suman a lo de hoy, pero no son sesiones aparte.
/// Cada respuesta («gemini») anota model y tokens (input, output, cached, thoughts, tool, total); se cuentan los que
/// generó, output y thoughts, como los de Claude en el pie. Lo deshecho o borrado sigue contando: ya se gastó. La carpeta
/// del proyecto sale de projects.json ({"projects": {ruta: nombre}}), por el nombre de su carpeta en tmp o, en las
/// versiones viejas, por su hash. No escribe sus límites: se ve lo que hizo.
/// </summary>
static class GeminiReader
{
    /// <summary>De un mensaje, lo que se usa: su tipo, su hora, el modelo y los tokens que generó.</summary>
    readonly record struct Message(string? Type, DateTimeOffset? At, string? Model, long Tokens);

    /// <summary>Una sesión con todo lo leído de sus archivos.</summary>
    sealed class Conversation(FileInfo file, bool subagent)
    {
        public string? Id, ProjectHash;
        public string? ProjectDir = (subagent ? file.Directory?.Parent?.Parent : file.Directory?.Parent)?.Name;
        public bool Subagent = subagent;
        public DateTimeOffset? Updated;
        public DateTimeOffset LastWrite = new(file.LastWriteTimeUtc, TimeSpan.Zero);
        public readonly Dictionary<string, Message> Messages = new(StringComparer.Ordinal);
        int _unnamed;

        public void Meta(JsonElement record)
        {
            if (Agents.Child(record, "sessionId") is { ValueKind: JsonValueKind.String } id) Id ??= id.GetString();
            if (Agents.Child(record, "projectHash") is { ValueKind: JsonValueKind.String } hash) ProjectHash ??= hash.GetString();
            foreach (var name in new[] { "startTime", "lastUpdated" })
                if (Agents.Time(record, name) is { } at && !(Updated >= at)) Updated = at;
            if (Agents.Child(record, "messages") is { ValueKind: JsonValueKind.Array } list)
                foreach (var message in list.EnumerateArray()) Add(message);
        }

        public void Add(JsonElement message)
        {
            if (message.ValueKind != JsonValueKind.Object) return;
            var id = Agents.Child(message, "id") is { ValueKind: JsonValueKind.String } i ? i.GetString()! : "#" + _unnamed++;
            var tokens = Agents.Child(message, "tokens") is { ValueKind: JsonValueKind.Object } t
                ? (long)((Agents.Number(t, "output") ?? 0) + (Agents.Number(t, "thoughts") ?? 0)) : 0;
            Messages[id] = new(Agents.Child(message, "type") is { ValueKind: JsonValueKind.String } type ? type.GetString() : null,
                Agents.Time(message, "timestamp"),
                Agents.Child(message, "model") is { ValueKind: JsonValueKind.String } model ? model.GetString() : null, tokens);
        }

        /// <summary>Suma otro archivo de la misma sesión (el .json viejo y su copia .jsonl); lo del más nuevo vale.</summary>
        public void Merge(Conversation newer)
        {
            foreach (var (id, message) in newer.Messages) Messages[id] = message;
            ProjectHash ??= newer.ProjectHash;
            if (newer.Updated is { } at && !(Updated >= at)) Updated = at;
            if (newer.LastWrite > LastWrite) LastWrite = newer.LastWrite;
        }

        public DateTimeOffset LastActivity => Updated ?? LastWrite;
    }

    /// <summary>Un archivo de chat: chats/session-*.json o .jsonl, o el .jsonl de un subagente en chats/&lt;sesión&gt;/.</summary>
    internal static bool IsChat(FileInfo file) => IsSession(file) || IsSubagent(file);

    static bool IsSession(FileInfo file) => file.Extension is ".json" or ".jsonl"
        && file.Name.StartsWith("session-", StringComparison.Ordinal) && file.Directory?.Name == "chats";

    static bool IsSubagent(FileInfo file) => file.Extension == ".jsonl" && file.Directory?.Parent?.Name == "chats";

    public static AgentSnapshot Read(AgentAccount agent, DateTimeOffset now)
    {
        var projects = Projects(Path.Combine(agent.Folder, "projects.json"));
        var tmp = Path.Combine(agent.Folder, "tmp");
        var conversations = new Dictionary<string, Conversation>(StringComparer.Ordinal);
        var unreadable = 0;
        // Del más viejo al más nuevo, para que al juntar los archivos de una sesión valga lo último.
        var files = Agents.Newest(tmp, "session-*.json*", 3, IsSession).Concat(Agents.Newest(tmp, "*.jsonl", 3, IsSubagent))
            .OrderBy(f => f.LastWriteTimeUtc);
        foreach (var file in files)
        {
            var read = Parse(file);
            if (read?.Id is not { } id)
            {
                unreadable++;
                continue;
            }
            var key = (read.Subagent ? "subagente:" : "") + id;
            if (conversations.TryGetValue(key, out var known)) known.Merge(read);
            else conversations[key] = read;
        }

        var sessions = new List<AgentSession>();
        int messagesToday = 0, responsesToday = 0;
        long tokensToday = 0;
        foreach (var c in conversations.Values)
        {
            foreach (var message in c.Messages.Values)
            {
                if (!Agents.SameDay(message.At ?? c.LastActivity, now)) continue;
                if (message.Type == "gemini")
                {
                    responsesToday++;
                    tokensToday += message.Tokens;
                }
                if (!c.Subagent && message.Type is "user" or "gemini") messagesToday++;
            }
            if (c.Subagent) continue;
            var folder = c.ProjectDir is { } dir && projects.TryGetValue(dir, out var path) ? path
                : c.ProjectHash is { } hash && projects.TryGetValue(hash, out var hashed) ? hashed : c.ProjectDir;
            var model = c.Messages.Values.LastOrDefault(m => m.Model is not null).Model;
            sessions.Add(new AgentSession(Agents.Gemini, c.Id!, folder, model, c.LastActivity,
                c.Messages.Values.Count(m => m.Type is "user" or "gemini")));
        }
        var today = sessions.Count(s => Agents.SameDay(s.LastActivity, now));
        return new AgentSnapshot(agent, agent.Label, true, [], sessions.OrderByDescending(s => s.LastActivity).ToList(), today,
            messagesToday, unreadable, responsesToday, tokensToday);
    }

    /// <summary>Lo de un archivo; null si no se pudo leer (ni una línea entendible, o no es JSON).</summary>
    static Conversation? Parse(FileInfo file)
    {
        var c = new Conversation(file, IsSubagent(file));
        try
        {
            if (file.Extension == ".json")
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 64 });
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
                c.Meta(doc.RootElement);
            }
            else
            {
                var any = false;
                foreach (var record in Agents.Lines(file.FullName))
                {
                    any = true;
                    if (Agents.Child(record, "$set") is { ValueKind: JsonValueKind.Object } set) c.Meta(set);
                    else if (Agents.Child(record, "id") is { ValueKind: JsonValueKind.String } && Agents.Child(record, "$patch") is null) c.Add(record);
                    else if (Agents.Child(record, "sessionId") is { ValueKind: JsonValueKind.String }) c.Meta(record);
                    // $rewindTo y $patch deshacen o editan lo que se ve, no lo que ya se gastó.
                }
                if (!any) return null;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
        // Sin sessionId (las versiones viejas no siempre lo ponían en el .json): el nombre del archivo, el mismo en su copia .jsonl.
        c.Id ??= Path.GetFileNameWithoutExtension(file.Name);
        return c;
    }

    /// <summary>
    /// projects.json: no está documentado. Se toma cualquier par ruta ↔ texto, en una o en otra dirección, a cualquier
    /// profundidad: lo que se busca después es el hash del proyecto.
    /// </summary>
    internal static Dictionary<string, string> Projects(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return map;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { MaxDepth = 16 });
            void Walk(JsonElement e)
            {
                if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) Walk(item);
                if (e.ValueKind != JsonValueKind.Object) return;
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 } value)
                    {
                        if (LooksLikePath(p.Name)) map.TryAdd(value, p.Name);
                        else if (LooksLikePath(value)) map.TryAdd(p.Name, value);
                    }
                    else Walk(p.Value);
                }
            }
            Walk(doc.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or IOException) { }
        return map;
    }

    static bool LooksLikePath(string text) => text.Contains('/') || text.Contains('\\');
}

/// <summary>
/// GitHub Copilot CLI ($COPILOT_HOME o ~/.copilot): una carpeta por sesión, session-state/&lt;id&gt;/events.jsonl. Su formato
/// no está documentado: solo se cuentan sus eventos (una línea JSON cada uno) y la última vez que cambió el archivo.
/// </summary>
static class CopilotReader
{
    public static AgentSnapshot Read(AgentAccount agent, DateTimeOffset now)
    {
        var sessions = new List<AgentSession>();
        foreach (var file in Agents.Newest(Path.Combine(agent.Folder, "session-state"), "events.jsonl", 1))
        {
            if (file.Directory is not { } folder) continue;
            var events = Agents.Lines(file.FullName).Count();
            sessions.Add(new AgentSession(Agents.Copilot, folder.Name, null, null, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero), events));
        }
        var today = sessions.Where(s => Agents.SameDay(s.LastActivity, now)).ToList();
        return new AgentSnapshot(agent, agent.Label, true, [], sessions, today.Count, today.Sum(s => s.Messages), 0);
    }
}

/// <summary>
/// La memoria de una cuenta de Claude Code: compartida (su carpeta projects es un enlace a la de otra cuenta: las
/// conversaciones y la memoria de cada proyecto son las mismas; CLAUDE.md, skills, agentes, ajustes y credenciales siguen
/// siendo propios) o separada (su propia carpeta projects).
/// </summary>
static class AccountMemory
{
    /// <summary>La cuenta (carpeta) con la que comparte la memoria; null si la suya es propia.</summary>
    public static string? SharedWith(string configDir)
    {
        try
        {
            var projects = new DirectoryInfo(Path.Combine(configDir, "projects"));
            if (!projects.Exists || projects.LinkTarget is null) return null;
            var target = projects.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
            return target is null ? null : Path.GetDirectoryName(target.TrimEnd('\\', '/'));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Se puede compartir: la cuenta todavía no tiene memoria propia (sin carpeta projects, o vacía).</summary>
    public static bool CanShare(string configDir)
    {
        var projects = Path.Combine(configDir, "projects");
        return !Directory.Exists(projects) || (new DirectoryInfo(projects).LinkTarget is null && !Directory.EnumerateFileSystemEntries(projects).Any());
    }

    /// <summary>
    /// Enlaza la carpeta projects de la cuenta nueva con la de otra. En Windows es un junction (no pide permisos de
    /// administrador); en Linux y macOS, un enlace simbólico. Lanza IOException si no se puede.
    /// </summary>
    public static void Share(string configDir, string withDir)
    {
        if (!CanShare(configDir)) throw new IOException(L.T("La cuenta ya tiene su propia memoria: no se enlaza para no perderla."));
        var target = Path.Combine(withDir, "projects");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(configDir);
        var link = Path.Combine(configDir, "projects");
        if (Directory.Exists(link)) Directory.Delete(link);
        if (OperatingSystem.IsWindows())
        {
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(a);
            using var process = System.Diagnostics.Process.Start(start) ?? throw new IOException("mklink");
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(10_000);
            if (process.ExitCode != 0 || !Directory.Exists(link)) throw new IOException(error.Trim().Length > 0 ? error.Trim() : "mklink /J");
        }
        else Directory.CreateSymbolicLink(link, target);
    }
}
