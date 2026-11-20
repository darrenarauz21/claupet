using System.Text;
using System.Text.Json;

namespace Claupet;

/// <summary>
/// Una tarea delegada a un especialista: a quién, qué, en qué carpeta (la de quien lo llama, si el especialista no tiene
/// la suya), con qué cuenta si el especialista no tiene una (D3), quién la pidió (la clave de su sesión) y a qué
/// profundidad (1: la pidió una sesión; 2: la pidió un especialista).
/// </summary>
sealed record SpecialistRequest(Specialist Specialist, string Task, string Folder, string? ConfigDir = null, string? From = null, int Depth = 1);

/// <summary>Cómo va una corrida, tal como queda en su diario.</summary>
static class RunStates
{
    public const string Working = "trabajando", Waiting = "pide-permiso", Done = "listo", Failed = "fallo", Stopped = "detenido", TimedOut = "tiempo";

    /// <summary>Ya terminó (bien o mal): no va a cambiar más.</summary>
    public static bool Finished(string state) => state is Done or Failed or Stopped or TimedOut;
}

/// <summary>
/// El diario de una corrida: «especialistas/corridas/&lt;id&gt;.json». Lo escribe quien la corre y lo leen los demás (la
/// Terminal, la isla, Actividad, el servidor MCP que la pidió), aunque estén en otro proceso.
/// </summary>
sealed class SpecialistRun
{
    public string Id { get; set; } = "";
    public string Specialist { get; set; } = "";
    public string Name { get; set; } = "";
    public string Agent { get; set; } = "";
    public string Task { get; set; } = "";
    public string Folder { get; set; } = "";
    public string? From { get; set; }
    public int Depth { get; set; }
    public DateTimeOffset Started { get; set; }
    public DateTimeOffset? Ended { get; set; }
    public string State { get; set; } = RunStates.Working;
    /// <summary>Lo que hizo, resumido (los últimos pasos).</summary>
    public List<string> Steps { get; set; } = [];
    /// <summary>La respuesta final o, si no terminó bien, por qué.</summary>
    public string? Result { get; set; }
    public double? Cost { get; set; }
    public int? Turns { get; set; }
    /// <summary>La sesión del agente, para retomar su conversación (la pestaña hija).</summary>
    public string? SessionId { get; set; }
}

/// <summary>Una sesión de agente vista desde el runner (en la app, TerminalAgent; las pruebas ponen otra).</summary>
interface ISpecialistSession : IDisposable
{
    AgentTranscript Transcript { get; }
    bool Ended { get; }
    event Action<IReadOnlyList<AgentBlock>>? Changed;
    void Start();
    void Send(string text);
    void Stop();
}

/// <summary>
/// Corre un especialista en una tarea, sin interfaz, sobre TerminalAgent: el mismo camino para Claude Code, Codex, Gemini,
/// Copilot y OpenCode. Abre su agente con su modelo, esfuerzo, instrucciones y herramientas (si edita archivos, en su
/// propio worktree), le manda la tarea, sigue sus pasos en el diario y devuelve su respuesta. Los permisos se piden en la
/// isla, como en la Terminal; pasado su tope de minutos, se detiene.
/// </summary>
static class SpecialistRunner
{
    /// <summary>Una sesión llama a A y A puede llamar a B; B ya no delega (sin ciclos ni cadenas sin fin).</summary>
    public const int MaxDepth = 2;
    const int MaxSteps = 30, MaxResult = 20_000;

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Dónde quedan los diarios de las corridas (las pruebas ponen otra).</summary>
    public static string Folder { get; set; } = Path.Combine(Specialists.Folder, "corridas");

    /// <summary>Abre la sesión del especialista (las pruebas ponen otra). En la app: TerminalAgent, con la isla y la coordinación.</summary>
    public static Func<AgentOptions, ISpecialistSession> Open { get; set; } = options => new AgentSession(options);

    /// <summary>Cuánto puede trabajar en una tarea: su tope de minutos (las pruebas ponen uno más corto).</summary>
    public static Func<Specialist, TimeSpan> Limit { get; set; } = s => TimeSpan.FromMinutes(Math.Clamp(s.Limits.Minutes, 1, SpecialistLimits.MaxMinutes));

    /// <summary>Lo que el especialista recibe: la tarea, y que el resultado es lo que devuelve (lo lee quien se la pidió).</summary>
    internal static string Brief(SpecialistRequest request) =>
        request.Task.Trim() + "\n\n" + L.T("(Trabaja como especialista: esta tarea te la delegaron. Al terminar, tu última respuesta es el resultado que recibe quien te la pidió: que sea completa y concreta.)");

    /// <summary>
    /// Corre la tarea hasta que el especialista termine, falle, pase su tope de minutos o se cancele. progress recibe el
    /// diario cada vez que cambia (también desde otro hilo).
    /// </summary>
    public static async Task<SpecialistRun> RunAsync(SpecialistRequest request, Action<SpecialistRun>? progress = null, CancellationToken cancel = default)
    {
        var s = request.Specialist;
        var run = new SpecialistRun
        {
            Id = NewId(), Specialist = s.Id, Name = s.Name, Agent = s.Agent, Task = request.Task.Trim(), Folder = s.Folder ?? request.Folder,
            From = request.From, Depth = request.Depth, Started = DateTimeOffset.Now,
        };
        if (request.Depth > MaxDepth) return Finish(run, RunStates.Failed, L.T("Un especialista al que le delegaron una tarea no puede delegar otra vez."), progress);
        if (request.Task.Trim().Length == 0) return Finish(run, RunStates.Failed, L.T("La tarea está vacía."), progress);

        // Si edita archivos, en su propio worktree, desde la rama principal al día (D4).
        if (s.EditsFiles && SessionWorktrees.Locate(run.Folder) is not null)
        {
            var (path, _) = await SessionWorktrees.CreateAsync(run.Folder, DateTime.Now);
            if (path is not null) run.Folder = path;
        }
        Save(run, progress);

        var options = new AgentOptions(run.Folder, s.Account ?? request.ConfigDir, s.Model, s.Effort, Kind: s.Agent, Specialist: s);
        using var session = Open(options);
        var gate = new object();
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        AgentBlock? asked = null;

        void Changed(IReadOnlyList<AgentBlock> _)
        {
            lock (gate)
            {
                var blocks = session.Transcript.Blocks;
                var from = asked is null ? -1 : IndexOf(blocks, asked);
                var mine = from < 0 ? [] : blocks.Skip(from + 1).ToList();
                run.SessionId = session.Transcript.SessionId ?? run.SessionId;
                run.Steps = [.. mine.Where(b => b.Kind == AgentBlockKind.Step).Select(Describe).TakeLast(MaxSteps)];
                var waiting = mine.Any(b => b.Kind == AgentBlockKind.Permission && b.Answer is null);
                if (!RunStates.Finished(run.State)) run.State = waiting ? RunStates.Waiting : RunStates.Working;
                if (mine.LastOrDefault(b => b.Kind == AgentBlockKind.Done) is { } done)
                {
                    run.Cost = done.Cost > 0 ? done.Cost : null;
                    run.Turns = done.Turns > 0 ? done.Turns : null;
                    run.State = done.Failed ? RunStates.Failed : RunStates.Done;
                    run.Result = Clip(Answer(mine, done));
                    finished.TrySetResult(true);
                }
                else if (session.Ended)
                {
                    run.State = RunStates.Failed;
                    run.Result = Clip(Problem(session.Transcript.Blocks) ?? L.T("El agente se cerró antes de terminar."));
                    finished.TrySetResult(true);
                }
                Save(run, progress);
            }
        }

        session.Changed += Changed;
        session.Start();
        if (session.Ended)
            return Finish(run, RunStates.Failed, Problem(session.Transcript.Blocks) ?? L.T("No se pudo abrir {0}.", AgentKinds.Label(s.Agent)), progress);
        lock (gate)
        {
            session.Send(Brief(request));
            asked = session.Transcript.Blocks.LastOrDefault(b => b.Kind == AgentBlockKind.Ask);
        }

        var limit = Limit(s);
        var stop = RunStates.TimedOut;
        try { await finished.Task.WaitAsync(limit, cancel); }
        catch (TimeoutException) { }
        catch (OperationCanceledException) { stop = RunStates.Stopped; }

        session.Changed -= Changed;
        lock (gate)
        {
            if (RunStates.Finished(run.State)) return Finish(run, run.State, run.Result, progress);
            session.Stop();
            return Finish(run, stop, stop == RunStates.TimedOut
                ? L.T("Pasó su tope de {0} minutos y se detuvo. Lo que alcanzó a hacer: {1}", s.Limits.Minutes, run.Steps.Count == 0 ? L.T("nada todavía") : string.Join("; ", run.Steps.TakeLast(8)))
                : L.T("Se detuvo antes de terminar."), progress);
        }
    }

    /// <summary>Lee el diario de una corrida; null si no está o no se entiende.</summary>
    public static SpecialistRun? Read(string id)
    {
        if (!IsId(id)) return null;
        try
        {
            var file = Path.Combine(Folder, id + ".json");
            return File.Exists(file) ? JsonSerializer.Deserialize<SpecialistRun>(File.ReadAllText(file), Options) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    static SpecialistRun Finish(SpecialistRun run, string state, string? result, Action<SpecialistRun>? progress)
    {
        run.State = state;
        run.Result = result is null ? run.Result : Clip(result);
        run.Ended ??= DateTimeOffset.Now;
        Save(run, progress);
        return run;
    }

    static void Save(SpecialistRun run, Action<SpecialistRun>? progress)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var file = Path.Combine(Folder, run.Id + ".json");
            var temp = file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(run, Options), new UTF8Encoding(false));
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
        progress?.Invoke(run);
    }

    /// <summary>La respuesta: lo último que dijo después del pedido; si no dijo nada, lo que trae el cierre del turno.</summary>
    static string Answer(IReadOnlyList<AgentBlock> mine, AgentBlock done) =>
        mine.LastOrDefault(b => b.Kind == AgentBlockKind.Say && b.Text.Trim().Length > 0)?.Text.Trim()
        ?? (done.Text.Trim() is { Length: > 0 } text ? text : L.T("Terminó sin decir nada."));

    static string? Problem(IReadOnlyList<AgentBlock> blocks) =>
        blocks.LastOrDefault(b => b.Kind == AgentBlockKind.Notice && b.Text.Trim().Length > 0)?.Text.Trim();

    static string Describe(AgentBlock step) =>
        step.Text.Trim() is { Length: > 0 } text ? $"{step.Tool ?? "?"}: {(text.Length > 120 ? text[..120] + "…" : text)}" : step.Tool ?? "?";

    static int IndexOf(IReadOnlyList<AgentBlock> blocks, AgentBlock block)
    {
        for (var i = blocks.Count - 1; i >= 0; i--)
            if (ReferenceEquals(blocks[i], block)) return i;
        return -1;
    }

    static string Clip(string text) => text.Length <= MaxResult ? text : text[..MaxResult] + "\n…";

    static string NewId() => DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..6];

    static bool IsId(string id) => id.Length is > 0 and <= 40 && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    /// <summary>La sesión de verdad: TerminalAgent con la isla para los permisos y la coordinación con las otras sesiones de la carpeta.</summary>
    sealed class AgentSession(AgentOptions options) : ISpecialistSession
    {
        readonly object _post = new();
        TerminalAgent? _agent;

        TerminalAgent Agent => _agent ??= new TerminalAgent(options, action => { lock (_post) action(); }) { AskIsland = true, Coordinate = true };

        public AgentTranscript Transcript => Agent.Transcript;
        public bool Ended => Agent.Ended;

        public event Action<IReadOnlyList<AgentBlock>>? Changed
        {
            add => Agent.Changed += value;
            remove => Agent.Changed -= value;
        }

        public void Start() => Agent.Start();
        public void Send(string text) => Agent.Send(text);
        public void Stop() => Agent.Stop();
        public void Dispose() => _agent?.Dispose();
    }
}
