using System.Text;
using System.Text.Json;

namespace Claupet;

/// <summary>
/// Las sesiones de la Terminal entre sí, como herramientas del servidor MCP de Claupet (en cualquier agente): ver las otras
/// sesiones, leer de qué trata una, dejarle un mensaje y la pizarra del proyecto (un canvas que leen y escriben todas sus
/// sesiones). Los textos van en inglés: son para el modelo.
/// </summary>
static class SessionTools
{
    public static readonly string[] Names = ["sesiones", "leer_sesion", "escribir_a_sesion", "pizarra", "anotar"];

    public const string Instructions =
        "\nOther sessions: the user may have other Claupet Terminal sessions open (other agents and models, maybe on the same " +
        "project). sesiones lists them; leer_sesion tells what one is about (its recent asks, replies and edited files); " +
        "escribir_a_sesion leaves it a message (it arrives as a message from you, not from the user). The project's pizarra is a " +
        "shared board every session of the project reads and writes: read it with pizarra, add decisions and agreements with anotar.";

    public static IEnumerable<JsonObjectTool> Tools() =>
    [
        new("sesiones", "List the user's other open Claupet Terminal sessions: id, title, agent, project and whether it is working now.", []),
        new("leer_sesion", "Read what another session is about: its recent asks and replies, and the files it edited.", [("id", "Session id (see sesiones).", true)]),
        new("escribir_a_sesion",
            "Leave a message for another session (a colleague agent). It sees it marked as coming from you; if it is idle it gets it " +
            "as a new turn, otherwise when its turn ends. Use it to ask or tell something concrete, not for chatter.",
            [("id", "Session id (see sesiones).", true), ("mensaje", "The message.", true)]),
        new("pizarra", "Read the project's shared board (a canvas every session of this project reads and writes): decisions, agreements, what is left. Creates it if missing.", []),
        new("anotar", "Add a note to the project's shared board (pizarra): a decision, an agreement or something left to do. Short and concrete.", [("texto", "The note.", true)]),
    ];

    public static string Run(string tool, JsonElement args, SpecialistTools.Caller caller, CanvasStore store, string? author) => tool switch
    {
        "sesiones" => List(caller),
        "leer_sesion" => SessionShare.Read(Arg(args, "id") ?? throw new CanvasException("Missing id.")) is { } card
            ? SessionShare.Describe(card)
            : throw new CanvasException("No open session with that id. Call sesiones to see the list."),
        "escribir_a_sesion" => Write(Arg(args, "id") ?? throw new CanvasException("Missing id."), Arg(args, "mensaje") ?? throw new CanvasException("Missing mensaje."), caller),
        "pizarra" => CanvasExport.ForAgent(Board(store, caller.Folder, author)),
        "anotar" => Note(store, caller.Folder, author, Arg(args, "texto") ?? throw new CanvasException("Missing texto.")),
        _ => throw new CanvasException($"Unknown tool: {tool}"),
    };

    static string List(SpecialistTools.Caller caller)
    {
        var others = SessionShare.Others(caller.Session);
        if (others.Count == 0) return "There are no other open sessions in Claupet Terminal right now.";
        var mine = caller.Session is { } key ? SessionShare.Read(key)?.Project : null;
        var text = new StringBuilder("Other open sessions:\n");
        foreach (var c in others)
            text.Append($"- {c.Title} (id {c.Key}) · {c.Agent} · project {c.Project}{(c.Project == mine ? " (same project)" : "")} · {(c.Busy ? "working" : "idle")}\n");
        return text.ToString().TrimEnd();
    }

    static string Write(string to, string message, SpecialistTools.Caller caller)
    {
        if (caller.Session is not { } from || !SessionShare.IsKey(from)) throw new CanvasException("This session has no Claupet id: it can't send messages.");
        if (to == from) throw new CanvasException("That is this same session.");
        if (SessionShare.Read(to) is not { } card) throw new CanvasException("No open session with that id. Call sesiones to see the list.");
        var title = SessionShare.Read(from)?.Title is { Length: > 0 } t ? t : "another session";
        SessionShare.Post(to, from, title, message);
        return $"Message left for «{card.Title}». It is {(card.Busy ? "working: it will get it when its turn ends" : "idle: it gets it now")}.";
    }

    // ── La pizarra del proyecto ─────────────────────────────────────────────────────────────

    /// <summary>El título de la pizarra de un proyecto (así se la encuentra).</summary>
    internal static string BoardTitle(string project) => "Pizarra · " + Path.GetFileName(Path.TrimEndingDirectorySeparator(project));

    /// <summary>La pizarra del proyecto de esa carpeta (el repositorio principal, también desde un worktree); si no está, se crea.</summary>
    internal static CanvasDoc Board(CanvasStore store, string folder, string? author)
    {
        var project = Path.TrimEndingDirectorySeparator(SessionWorktrees.ProjectOf(folder));
        var title = BoardTitle(project);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (store.List().FirstOrDefault(c => c.Title == title && comparer.Equals(c.Project, project)) is { } found && store.Load(found.Id) is { } doc)
            return doc;
        return store.Create(title, author ?? CanvasJson.User,
            [new CanvasOp("insert", Kind: CanvasKinds.Markdown, Title: "Notas",
                Content: "Lo que comparten las sesiones de este proyecto: decisiones, acuerdos y lo que falta. Cada nota dice quién la dejó y cuándo.")],
            project);
    }

    static string Note(CanvasStore store, string folder, string? author, string text)
    {
        var board = Board(store, folder, author);
        var section = board.Sections.FirstOrDefault() ?? throw new CanvasException("The board has no sections.");
        var who = CanvasJson.AuthorLabel(author);
        var line = $"\n\n- **{who}** · {DateTime.Now:yyyy-MM-dd HH:mm} — {text.Trim().ReplaceLineEndings(" ")}";
        store.Apply(board.Id, [new CanvasOp("append", Id: section.Id, Content: line)], author);
        return $"Noted on the project's board ({board.Title}, canvas {board.Id}).";
    }

    static string? Arg(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
}
