using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>
/// Una sesión de agente que abrió Claupet (una pestaña de la Terminal, de cualquier agente), anotada para que las demás
/// sepan que está: su agente, su carpeta, el proceso de Claupet que la lleva, los archivos que editó y los que ya tenían
/// cambios cuando empezó (esos no son suyos).
/// </summary>
sealed record WorkspaceSession(string Id, string Agent, string Folder, int Pid, DateTimeOffset Started,
    string? SessionId = null, string? Title = null, IReadOnlyList<string>? Touched = null, IReadOnlyList<string>? DirtyAtStart = null);

/// <summary>Otra sesión que trabaja en el mismo árbol de trabajo: su agente, de qué trata y lo que editó (si se sabe).</summary>
sealed record WorkspacePeer(string Agent, string? Title, DateTimeOffset? Since, IReadOnlyList<string> Touched, bool FromClaupet);

/// <summary>Lo que una sesión necesita saber de su carpeta: quién más trabaja ahí, la rama y los cambios que no hizo ella.</summary>
sealed record WorkspaceState(string Root, string? Branch, IReadOnlyList<WorkspacePeer> Peers, IReadOnlyList<string> Foreign)
{
    /// <summary>Hay algo que cuidar: otra sesión en la carpeta o cambios que no son de esta.</summary>
    public bool Shared => Peers.Count > 0 || Foreign.Count > 0;
}

/// <summary>
/// La coordinación entre sesiones de una misma carpeta, igual para todos los agentes (Claude Code, Codex, Gemini, Copilot
/// o cualquiera por ACP) y para cualquier modelo: Claupet está en medio de cada pedido y de cada permiso, así que es quien
/// les cuenta quién más trabaja ahí y quien frena lo que pisaría su trabajo. Las sesiones de Claupet se anotan en
/// sesiones-activas/ (una por archivo, compartida entre procesos); las de Claude Code abiertas fuera se leen de su
/// registro (sessions/&lt;pid&gt;.json). Dos worktrees del mismo repositorio no comparten nada: cuenta el árbol de trabajo.
/// </summary>
static class Workspace
{
    static string Folder => DataFolder.In("sesiones-activas");

    static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    // ── El registro de las sesiones de Claupet ──────────────────────────────────────────────

    public static void Save(WorkspaceSession session)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var file = Path.Combine(Folder, session.Id + ".json");
            var temp = file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(session, Options));
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
    }

    public static void Remove(string id)
    {
        try { File.Delete(Path.Combine(Folder, id + ".json")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Las anotadas cuyo proceso de Claupet sigue vivo; las de un Claupet que se cerró de golpe se borran.</summary>
    public static IReadOnlyList<WorkspaceSession> Registered()
    {
        var list = new List<WorkspaceSession>();
        if (!Directory.Exists(Folder)) return list;
        foreach (var file in Directory.EnumerateFiles(Folder, "*.json"))
        {
            WorkspaceSession? session = null;
            try { session = JsonSerializer.Deserialize<WorkspaceSession>(File.ReadAllText(file), Options); }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
            if (session is null) continue;
            if (!Alive(session.Pid))
            {
                try { File.Delete(file); } catch (Exception) { }
                continue;
            }
            list.Add(session);
        }
        return list;
    }

    static bool Alive(int pid)
    {
        if (pid == Environment.ProcessId) return true;
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception) { return false; }
    }

    // ── La carpeta ──────────────────────────────────────────────────────────────────────────

    /// <summary>El árbol de trabajo de git que contiene la carpeta (donde está su .git, carpeta o archivo); si no hay, la carpeta.</summary>
    public static string Root(string folder)
    {
        try
        {
            for (var dir = new DirectoryInfo(Path.GetFullPath(folder)); dir is not null; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName.TrimEnd('\\', '/');
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return Account.Normalize(folder);
    }

    static bool SameRoot(string folder, string root) => Account.SamePath(Root(folder), root);

    /// <summary>
    /// Lo que la sesión self (en folder) tiene que saber: las otras sesiones en su árbol de trabajo (las de Claupet y las de
    /// Claude Code abiertas fuera) y los cambios sin commit que no hizo ella: los que ya estaban al empezar y los que no editó.
    /// </summary>
    public static async Task<WorkspaceState> ReadAsync(string folder, WorkspaceSession? self, IEnumerable<string> claudeDirs)
    {
        var root = Root(folder);
        var peers = new List<WorkspacePeer>();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var other in Registered())
        {
            if (other.SessionId is { } sid) known.Add(sid);
            if (other.Id == self?.Id || !SameRoot(other.Folder, root)) continue;
            peers.Add(new WorkspacePeer(other.Agent, other.Title, other.Started, other.Touched ?? [], FromClaupet: true));
        }
        if (self?.SessionId is { } own) known.Add(own);
        peers.AddRange(ClaudeOutside(root, known, claudeDirs));

        var branch = (await GitRepo.RunAsync(root, "branch", "--show-current"))?.Trim();
        var dirty = await DirtyAsync(root);
        var mine = new HashSet<string>((self?.Touched ?? []).Select(f => Relative(root, f)), StringComparer.OrdinalIgnoreCase);
        var before = new HashSet<string>(self?.DirtyAtStart ?? [], StringComparer.OrdinalIgnoreCase);
        var foreign = dirty.Where(f => before.Contains(f) || !mine.Contains(f)).ToList();
        return new WorkspaceState(root, string.IsNullOrEmpty(branch) ? null : branch, peers, foreign);
    }

    /// <summary>Los archivos con cambios sin commit (también los nuevos), relativos al árbol de trabajo y con «/».</summary>
    public static async Task<IReadOnlyList<string>> DirtyAsync(string root)
    {
        var status = await GitRepo.RunAsync(root, "status", "--porcelain", "--untracked-files=all");
        return status is null ? [] : ParseStatus(status);
    }

    /// <summary>Las rutas de «git status --porcelain» (en un renombre, la nueva; sin las comillas de git).</summary>
    public static IReadOnlyList<string> ParseStatus(string porcelain) => porcelain.Split('\n')
        .Select(l => l.TrimEnd('\r'))
        .Where(l => l.Length > 3)
        .Select(l => l[3..])
        .Select(p => p.Contains(" -> ", StringComparison.Ordinal) ? p[(p.IndexOf(" -> ", StringComparison.Ordinal) + 4)..] : p)
        .Select(p => p.Length >= 2 && p[0] == '"' && p[^1] == '"' ? p[1..^1] : p)
        .Distinct().ToList();

    static string Relative(string root, string file)
    {
        try { return (Path.IsPathRooted(file) ? Path.GetRelativePath(root, file) : file).Replace('\\', '/'); }
        catch (Exception) { return file.Replace('\\', '/'); }
    }

    /// <summary>Las sesiones de Claude Code vivas en ese árbol que no abrió Claupet (en una terminal, en el IDE…).</summary>
    static IEnumerable<WorkspacePeer> ClaudeOutside(string root, HashSet<string> known, IEnumerable<string> claudeDirs)
    {
        foreach (var dir in claudeDirs)
        {
            var sessions = Path.Combine(dir, "sessions");
            if (!Directory.Exists(sessions)) continue;
            List<string> files;
            try { files = Directory.EnumerateFiles(sessions, "*.json").ToList(); }
            catch (Exception) { continue; }
            foreach (var file in files)
            {
                WorkspacePeer? peer = null;
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    var r = doc.RootElement;
                    var id = Json.Str(r, "sessionId");
                    var cwd = Json.Str(r, "cwd");
                    var pid = r.TryGetProperty("pid", out var p) && p.TryGetInt32(out var value) ? value : 0;
                    if (id is null || cwd is null || pid == 0 || known.Contains(id) || Assistant.IsOwn(cwd)) continue;
                    if (!SameRoot(cwd, root) || !SessionMonitor.IsAlive(pid, Json.Str(r, "procStart"))) continue;
                    known.Add(id);
                    peer = new WorkspacePeer("Claude Code", null, null, [], FromClaupet: false);
                }
                catch (Exception) { }
                if (peer is not null) yield return peer;
            }
        }
    }

    // ── Lo que se le dice al agente ─────────────────────────────────────────────────────────

    /// <summary>La etiqueta de la nota: no se muestra en el chat ni en el historial (Strip la quita).</summary>
    public const string Tag = "claupet-context";

    /// <summary>Las reglas, en inglés (son para el modelo). Van al empezar cada sesión y con cada nota.</summary>
    public const string Rules =
        "Other sessions (Claude Code, Codex, Gemini, Copilot or other agents and models) may work in this same folder at the same time, " +
        "and the user may have their own unsaved work. Uncommitted changes you did not make belong to someone else:\n" +
        "- Never run git stash, git switch or git checkout of another branch, git checkout -- / git restore on files you did not change, " +
        "git reset --hard or git clean without asking the user first.\n" +
        "- When committing, stage only the files you changed (git add <paths>); never git add -A, git add . or git commit -a.\n" +
        "- If you need another branch, propose your own git worktree instead of switching this folder.";

    /// <summary>
    /// Lo que se agrega a un pedido: al empezar, las reglas; después, solo cuando cambió lo que hay que saber (state); null si
    /// no hay nada nuevo que decir. Va al final, para que el comienzo del pedido (el título de la sesión) sea el de la persona.
    /// </summary>
    public static string? Note(WorkspaceState? state, bool first)
    {
        if (!first && state is not { Shared: true }) return null;
        var text = new StringBuilder();
        text.Append("Context from Claupet, the app that runs this session (the user does not see this note; follow it without mentioning it).\n");
        text.Append(Rules);
        if (state is not null)
        {
            if (state.Branch is { } branch) text.Append($"\nThis working tree is on branch {branch}.");
            if (state.Peers.Count > 0)
            {
                text.Append("\nOther sessions working in this folder right now:");
                foreach (var peer in state.Peers.Take(6))
                {
                    text.Append($"\n- {peer.Agent}");
                    if (peer.Title is { Length: > 0 } title) text.Append($" («{Short(title, 60)}»)");
                    if (peer.Touched.Count > 0) text.Append(": edited " + string.Join(", ", peer.Touched.Take(10).Select(f => Relative(state.Root, f))));
                }
            }
            if (state.Foreign.Count > 0)
                text.Append("\nUncommitted files you have not edited in this session (treat them as someone else's): " +
                    string.Join(", ", state.Foreign.Take(30)) + (state.Foreign.Count > 30 ? $" and {state.Foreign.Count - 30} more" : "") + ".");
        }
        return $"\n\n<{Tag}>\n{text}\n</{Tag}>";
    }

    /// <summary>Lo mismo que state, en una huella: la nota se repite solo si cambió.</summary>
    public static string Fingerprint(WorkspaceState state) =>
        string.Join("|", state.Peers.Select(p => p.Agent + ":" + string.Join(",", p.Touched)).Order()) + "#" + string.Join(",", state.Foreign.Order()) + "#" + state.Branch;

    static string Short(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    static readonly Regex NoteBlock = new($@"\s*<{Tag}>.*?</{Tag}>\s*", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>El pedido como lo escribió la persona: sin la nota de Claupet (en el historial, los títulos y la transcripción).</summary>
    public static string Strip(string text) => text.Contains("<" + Tag + ">", StringComparison.Ordinal) ? NoteBlock.Replace(text, "\n").Trim() : text;

    // ── El freno ────────────────────────────────────────────────────────────────────────────

    static readonly Regex Risky = new(
        @"\bgit\s+(?:-C\s+\S+\s+)?(?:stash(?!\s+(?:list|show)\b)|switch\b|checkout\b|restore\b|reset\s+[^\n;&|]*--hard|clean\b|add\s+(?:-A\b|--all\b|\.(?:\s|$|[;&|]))|commit\s+[^\n;&|]*(?:-a\b|--all\b|-am\b))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Un comando de git que puede pisar o llevarse trabajo ajeno sin commit (stash, cambiar de rama, reset --hard…).</summary>
    public static bool IsRisky(string? command) => command is { Length: > 0 } && Risky.IsMatch(command);

    /// <summary>El comando de un permiso, sea de quien sea el agente («command» en Bash, en ACP o en Codex).</summary>
    public static string? CommandOf(JsonNode? input) => input switch
    {
        JsonObject o when o["command"] is JsonValue v && v.TryGetValue<string>(out var s) => s,
        JsonObject o when o["command"] is JsonArray a => string.Join(" ", a.Select(x => (string?)x)),
        JsonObject o when o["cmd"] is JsonValue v && v.TryGetValue<string>(out var s) => s,
        _ => null,
    };

    /// <summary>El aviso del freno para la persona; null si no hace falta (nadie más y nada ajeno en la carpeta).</summary>
    public static string? Warning(WorkspaceState state)
    {
        if (!state.Shared) return null;
        var who = state.Peers.Count switch
        {
            0 => null,
            1 => L.T("Hay otra sesión en esta carpeta ({0}).", state.Peers[0].Agent),
            var n => L.T("Hay otras {0} sesiones en esta carpeta.", n),
        };
        var files = state.Foreign.Count switch
        {
            0 => null,
            1 => L.T("1 archivo con cambios que esta sesión no hizo."),
            var n => L.T("{0} archivos con cambios que esta sesión no hizo.", n),
        };
        return string.Join(" ", new[] { who, files, L.T("Este comando puede llevárselos o pisarlos.") }.Where(t => t is not null));
    }
}
