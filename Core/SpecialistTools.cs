using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Claupet;

/// <summary>
/// Los especialistas como herramientas del servidor MCP de Claupet: así cualquier agente (Claude Code, Codex, Gemini,
/// Copilot, OpenCode) les delega una tarea igual. La corrida va en este mismo proceso (el servidor de la sesión que la
/// pide): funciona con la Console cerrada y termina con la sesión. Los textos van en inglés: son para el modelo.
/// </summary>
static class SpecialistTools
{
    public static readonly string[] Names = ["especialistas", "pedir_a_especialista", "ver_corrida"];

    /// <summary>
    /// Lo que se espera dentro de una llamada antes de devolver el id de la corrida: por debajo del tope con que los agentes
    /// cortan una herramienta MCP (Codex corta a los 60 s por defecto).
    /// </summary>
    public static TimeSpan Wait { get; set; } = TimeSpan.FromSeconds(50);

    public const string Instructions =
        "\nSpecialists: the user keeps specialists (agents with their own role, instructions, model and tools). When a task " +
        "matches a specialist's purpose, delegate it with pedir_a_especialista instead of doing it yourself, give it everything " +
        "it needs in the task (it does not see this conversation) and use its result. Call especialistas to see who is available. " +
        "Long tasks return a run id: check it later with ver_corrida.";

    /// <summary>Las corridas de este proceso que siguen: se pueden consultar después de devolver su id.</summary>
    static readonly ConcurrentDictionary<string, Task<SpecialistRun>> Running = new();

    /// <summary>De dónde viene el pedido: la carpeta de la sesión, su clave (CanvasAgentModel) y su cuenta de Claude Code.</summary>
    internal sealed record Caller(string Folder, string? Session, string? ConfigDir);

    public static IEnumerable<JsonObjectTool> Tools() =>
    [
        new("especialistas", "List the user's specialists you can delegate to: name, purpose (when to use it), agent and model.", []),
        new("pedir_a_especialista",
            "Delegate a task to one of the user's specialists and get its result. It works on its own, with its own agent, model, " +
            "instructions and tools, in the project folder (or its own worktree if it edits files); it does not see this " +
            "conversation, so include all the context it needs. Waits up to about 50 seconds; if it is still working, returns a run " +
            "id to check with ver_corrida.",
            [("especialista", "Name or id of the specialist (see especialistas).", true), ("tarea", "The task, with all the context it needs.", true)]),
        new("ver_corrida", "Check a specialist's run (from pedir_a_especialista): its state, what it did and its result. Waits up to about 50 seconds for it to finish.",
            [("id", "Run id.", true)]),
    ];

    public static string Run(string tool, JsonElement args, Caller caller) => tool switch
    {
        "especialistas" => List(caller),
        "pedir_a_especialista" => Ask(Arg(args, "especialista") ?? throw new CanvasException("Missing especialista."),
            Arg(args, "tarea") ?? throw new CanvasException("Missing tarea."), caller),
        "ver_corrida" => Check(Arg(args, "id") ?? throw new CanvasException("Missing id.")),
        _ => throw new CanvasException($"Unknown tool: {tool}"),
    };

    static string List(Caller caller)
    {
        var mine = Mine(caller);
        var all = Specialists.All().Where(s => s.Id != mine?.Specialist).ToList();
        if (all.Count == 0) return "The user has no specialists yet (they are created in Claupet Console › Specialists).";
        var text = new StringBuilder("Specialists you can delegate to with pedir_a_especialista:\n");
        foreach (var s in all)
        {
            text.Append($"- {s.Name} (id {s.Id}): {(s.Purpose.Length > 0 ? s.Purpose : "no purpose written")}");
            text.Append($" · {AgentKinds.Label(s.Agent)}{(s.Model is { } m ? $" · {m}" : "")}{(s.EditsFiles ? " · edits files" : "")}\n");
        }
        if (mine is not null) text.Append("\n(You are working as a delegated specialist: a specialist you delegate to cannot delegate again.)");
        return text.ToString().TrimEnd();
    }

    static string Ask(string who, string task, Caller caller)
    {
        var specialist = Find(who) ?? throw new CanvasException($"No specialist «{who}». Call especialistas to see the list.");
        var mine = Mine(caller);
        if (mine?.Specialist == specialist.Id) throw new CanvasException("A specialist cannot delegate to itself.");
        var request = new SpecialistRequest(specialist, task, caller.Folder, caller.ConfigDir, caller.Session, (mine?.Depth ?? 0) + 1);
        var started = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = Task.Run(() => SpecialistRunner.RunAsync(request, r => started.TrySetResult(r.Id)));
        var id = started.Task.Wait(TimeSpan.FromSeconds(30)) ? started.Task.Result : null;
        if (id is not null) Running[id] = run;
        if (!run.Wait(Wait)) return $"{specialist.Name} is still working. Run id: {id}. Call ver_corrida with this id to get the result.";
        if (id is not null) Running.TryRemove(id, out _);
        return Describe(run.Result);
    }

    static string Check(string id)
    {
        if (Running.TryGetValue(id, out var task))
        {
            if (task.Wait(Wait)) Running.TryRemove(id, out _);
            else return Describe(SpecialistRunner.Read(id) ?? throw new CanvasException($"No run {id}."));
        }
        return Describe(SpecialistRunner.Read(id) ?? throw new CanvasException($"No run {id}."));
    }

    /// <summary>La corrida, para el modelo: quién, cómo va, lo que hizo y su resultado.</summary>
    internal static string Describe(SpecialistRun run)
    {
        var text = new StringBuilder();
        var took = (run.Ended ?? DateTimeOffset.Now) - run.Started;
        var state = run.State switch
        {
            RunStates.Done => "finished",
            RunStates.Failed => "failed",
            RunStates.Stopped => "was stopped",
            RunStates.TimedOut => "reached its time limit",
            RunStates.Waiting => "is waiting for the user's permission",
            _ => "is still working",
        };
        text.Append($"{run.Name} {state} ({(int)took.TotalMinutes}m{took.Seconds:00}s). Run id: {run.Id}.");
        if (run.Steps.Count > 0) text.Append("\nWhat it did: ").Append(string.Join("; ", run.Steps.TakeLast(8)));
        if (run.Result is { Length: > 0 } result) text.Append(RunStates.Finished(run.State) ? "\n\nResult:\n" : "\n\n").Append(result);
        else if (!RunStates.Finished(run.State)) text.Append("\nCall ver_corrida again later.");
        return text.ToString();
    }

    /// <summary>El especialista por su id, su nombre o el comienzo de su nombre («revisor» → «Revisor de PRs»), sin tildes ni mayúsculas.</summary>
    internal static Specialist? Find(string who)
    {
        var all = Specialists.All();
        var key = Fold(who.Trim().TrimStart('@'));
        return all.FirstOrDefault(s => s.Id == who.Trim())
            ?? all.FirstOrDefault(s => Fold(s.Name) == key)
            ?? all.FirstOrDefault(s => Slug(s.Name) == Slug(key))
            ?? all.FirstOrDefault(s => Fold(s.Name).StartsWith(key, StringComparison.Ordinal) && key.Length >= 3);
    }

    /// <summary>La corrida que está haciendo esta sesión, si es la de un especialista al que le delegaron algo.</summary>
    static SpecialistRun? Mine(Caller caller) => caller.Session is { Length: > 0 } key ? SpecialistRunner.ActiveFor(key) : null;

    static string? Arg(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    /// <summary>Sin tildes ni mayúsculas.</summary>
    internal static string Fold(string text)
    {
        var plain = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) plain.Append(char.ToLowerInvariant(c));
        return plain.ToString();
    }

    static readonly System.Text.RegularExpressions.Regex Mention = new(@"(?<![\w@./\\])@([\p{L}\p{N}][\p{L}\p{N}_-]*)", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Los especialistas que menciona un pedido de la persona («@revisor revisa esto»), sin repetir. Un @ que no es de un
    /// especialista (un correo, una ruta, @archivo) se deja como está: el agente lo recibe igual.
    /// </summary>
    public static IReadOnlyList<(Specialist Specialist, string Token)> Mentions(string text, IReadOnlyList<Specialist> all)
    {
        var found = new List<(Specialist, string)>();
        foreach (System.Text.RegularExpressions.Match m in Mention.Matches(text))
        {
            var token = m.Groups[1].Value;
            var key = Fold(token);
            var specialist = all.FirstOrDefault(s => Slug(s.Name) == key || Fold(s.Name) == key)
                ?? (key.Length >= 3 ? all.FirstOrDefault(s => Slug(s.Name).StartsWith(key, StringComparison.Ordinal)) : null);
            if (specialist is not null && found.All(f => f.Item1.Id != specialist.Id)) found.Add((specialist, token));
        }
        return found;
    }

    /// <summary>
    /// La nota que acompaña un pedido con @especialista (la persona no la ve): que el agente de la sesión le delegue la
    /// tarea con pedir_a_especialista, con el contexto de la conversación, y le cuente el resultado. Null si no menciona a nadie.
    /// </summary>
    public static string? Directive(IReadOnlyList<(Specialist Specialist, string Token)> mentions)
    {
        if (mentions.Count == 0) return null;
        var who = string.Join(", ", mentions.Select(m => $"«{m.Specialist.Name}» (@{m.Token}, id {m.Specialist.Id})"));
        return $"\n\n<{Workspace.Tag}>\nThe user mentions the specialist{(mentions.Count > 1 ? "s" : "")} {who}. Delegate what they ask of " +
            $"{(mentions.Count > 1 ? "each one" : "it")} with the tool pedir_a_especialista (claupet-canvas server), giving it all the context " +
            $"it needs from this conversation (it does not see it), and then tell the user the result.\n</{Workspace.Tag}>";
    }

    /// <summary>El nombre como se escribe después de @: «Revisor de PRs» → «revisor-de-prs».</summary>
    internal static string Slug(string name) =>
        string.Join('-', System.Text.RegularExpressions.Regex.Split(Fold(name), @"[^\p{L}\p{N}]+").Where(w => w.Length > 0));
}

/// <summary>Una herramienta MCP descrita sin JSON: nombre, descripción y sus argumentos de texto (nombre, qué es, si va siempre).</summary>
sealed record JsonObjectTool(string Name, string Description, (string Name, string Description, bool Required)[] Args);
