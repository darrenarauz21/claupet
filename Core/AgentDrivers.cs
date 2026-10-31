using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Claupet;

/// <summary>Los agentes que la Terminal abre en bloques.</summary>
static class AgentKinds
{
    public const string Claude = "claude", Codex = "codex", Gemini = "gemini", Copilot = "copilot";

    public static string Label(string kind) => kind switch
    {
        Codex => "Codex",
        Gemini => "Gemini CLI",
        Copilot => "Copilot CLI",
        _ => "Claude Code",
    };

    /// <summary>Los agentes instalados en este equipo, Claude Code primero (se busca una vez por llamada).</summary>
    public static IReadOnlyList<string> Available()
    {
        var list = new List<string>();
        if (Assistant.FindClaude() is not null) list.Add(Claude);
        if (CodexDriver.Find() is not null) list.Add(Codex);
        if (AcpDriver.Find(Gemini) is not null) list.Add(Gemini);
        if (AcpDriver.Find(Copilot) is not null) list.Add(Copilot);
        return list;
    }

    /// <summary>Cómo habla la sesión con ese agente (uno nuevo por sesión: guarda el estado del protocolo).</summary>
    public static IAgentDriver Driver(string kind, AgentTranscript transcript) => kind switch
    {
        Codex => new CodexDriver(transcript),
        Gemini or Copilot => new AcpDriver(kind, transcript),
        _ => new ClaudeDriver(transcript),
    };
}

/// <summary>
/// Cómo habla una sesión de la Terminal con un agente: qué proceso abre, qué le escribe y cómo lee lo que responde. No
/// abre procesos ni escribe: devuelve las líneas que TerminalAgent escribe por la entrada del agente, así se prueba con
/// líneas. Lo que lee lo vuelca en la transcripción (bloques), igual para todos los agentes.
/// </summary>
interface IAgentDriver
{
    /// <summary>El nombre del agente para los avisos («No encontré {0}»).</summary>
    string Name { get; }

    /// <summary>El proceso del agente para esas opciones, o null si no está instalado.</summary>
    ProcessStartInfo? Start(AgentOptions options);

    /// <summary>Lo que se le escribe apenas abre (initialize, la sesión nueva…).</summary>
    IEnumerable<string> Opening(AgentOptions options);

    /// <summary>Lee una línea de su salida: cambia la transcripción y puede dejar líneas para contestarle (replies).</summary>
    IReadOnlyList<AgentBlock> Apply(string line, List<string> replies);

    /// <summary>Un pedido de la persona (el bloque Ask ya lo puso TerminalAgent). Si la sesión aún no está lista, se guarda.</summary>
    IEnumerable<string> Prompt(string text);

    IEnumerable<string> SetModel(string? model);
    IEnumerable<string> SetEffort(string effort);
    IEnumerable<string> SetMode(string mode);
    IEnumerable<string> Interrupt();

    IEnumerable<string> Answer(AgentBlock permission, PermissionAnswer answer);
    IEnumerable<string> Answer(AgentBlock permission, IReadOnlyDictionary<string, IReadOnlyList<string>> picks);

    /// <summary>La respuesta de la isla (formato del hook) a ese permiso: qué se respondió y qué escribirle; null si no se entiende.</summary>
    (PermissionAnswer Answer, IEnumerable<string> Lines)? FromIsland(AgentBlock permission, string reply);

    /// <summary>Los modelos, esfuerzos y modos que se pueden elegir (los de ACP o app-server llegan al abrir).</summary>
    IReadOnlyList<string> Models { get; }
    IReadOnlyList<string> Efforts { get; }
    IReadOnlyList<string> Modes { get; }
}

/// <summary>Lo que comparten los agentes que no son Claude Code.</summary>
static class AgentDriver
{
    /// <summary>La decisión de la isla (hookSpecificOutput.decision.behavior): permitir o rechazar; null si no se entiende.</summary>
    public static PermissionAnswer? IslandDecision(string reply)
    {
        try
        {
            return (string?)JsonNode.Parse(reply)?["hookSpecificOutput"]?["decision"]?["behavior"] switch
            {
                "allow" => PermissionAnswer.Allow,
                "deny" => PermissionAnswer.Deny,
                _ => null,
            };
        }
        catch (Exception) { return null; }
    }

    /// <summary>Un programa en el PATH (en Windows, con sus extensiones: .exe, .cmd…).</summary>
    public static string? OnPath(string name)
    {
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""];
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var extension in extensions)
            {
                var file = Path.Combine(folder.Trim('"'), name + extension.ToLowerInvariant());
                if (File.Exists(file)) return file;
            }
        return null;
    }
}

/// <summary>Claude Code en modo estructurado (stream-json por la entrada y la salida, permisos por stdio).</summary>
sealed class ClaudeDriver(AgentTranscript transcript) : IAgentDriver
{
    int _requests;

    public string Name => "Claude Code";
    public IReadOnlyList<string> Models => Launcher.Models;
    public IReadOnlyList<string> Efforts => Launcher.Efforts;
    public IReadOnlyList<string> Modes => TerminalAgent.Modes;

    public ProcessStartInfo? Start(AgentOptions options)
    {
        if (Assistant.FindClaude() is not { } claude) return null;
        var start = Assistant.ClaudeStart(claude, options.ConfigDir, TerminalAgent.Arguments(options));
        // El hook de permisos de Claupet se aparta: la sesión pregunta en su bloque (y en la isla).
        start.Environment[Approvals.TerminalVariable] = "1";
        return start;
    }

    public IEnumerable<string> Opening(AgentOptions options) => [AgentProtocol.Initialize(NextId())];
    public IReadOnlyList<AgentBlock> Apply(string line, List<string> replies) => transcript.Apply(line);
    public IEnumerable<string> Prompt(string text) => [AgentProtocol.UserMessage(text)];

    public IEnumerable<string> SetModel(string? model) =>
        model is not null && !Launcher.IsValid(model) ? [] : [AgentProtocol.SetModel(NextId(), model)];

    public IEnumerable<string> SetEffort(string effort) => Launcher.Efforts.Contains(effort) ? [AgentProtocol.SetEffort(NextId(), effort)] : [];
    public IEnumerable<string> SetMode(string mode) => TerminalAgent.Modes.Contains(mode) ? [AgentProtocol.SetPermissionMode(NextId(), mode)] : [];
    public IEnumerable<string> Interrupt() => [AgentProtocol.Interrupt(NextId())];
    public IEnumerable<string> Answer(AgentBlock permission, PermissionAnswer answer) => [AgentProtocol.Answer(permission, answer)];
    public IEnumerable<string> Answer(AgentBlock permission, IReadOnlyDictionary<string, IReadOnlyList<string>> picks) => [AgentProtocol.Answer(permission, picks)];

    public (PermissionAnswer Answer, IEnumerable<string> Lines)? FromIsland(AgentBlock permission, string reply) =>
        AgentProtocol.FromIsland(permission, reply) is { } answer ? (answer.Answer, [answer.Line]) : null;

    string NextId() => $"claupet-{Interlocked.Increment(ref _requests)}";
}
