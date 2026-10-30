using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet;

/// <summary>Qué es cada bloque de una sesión de agente en la Terminal.</summary>
enum AgentBlockKind { Ask, Say, Think, Step, Todo, Permission, Done, Notice }

/// <summary>Cómo va un paso (una herramienta): corriendo, esperando un permiso, listo, con error o rechazado.</summary>
enum StepState { Running, Waiting, Done, Failed, Denied }

/// <summary>Lo que se responde a un permiso desde su bloque.</summary>
enum PermissionAnswer { Allow, AllowAlways, Deny }

sealed record TodoItem(string Text, string Status);

/// <summary>
/// Un bloque de la sesión: un pedido, una respuesta, lo que pensó, un paso (leyó, buscó, editó…), la lista de tareas,
/// un permiso o el cierre del turno. AgentTranscript lo cambia al llegar los eventos; la vista lo vuelve a pintar.
/// </summary>
sealed class AgentBlock
{
    public required AgentBlockKind Kind { get; init; }
    /// <summary>El id del paso (tool_use_id) o de la parte del mensaje («msg_…:2»).</summary>
    public string Id { get; init; } = "";
    /// <summary>El pedido, la respuesta o el aviso; en un paso, sobre qué trabaja (el archivo, el comando); en un permiso, qué pide.</summary>
    public string Text { get; set; } = "";
    public string? Tool { get; init; }
    public StepState State { get; set; }
    /// <summary>Todavía llega texto (respuesta) o sigue pensando.</summary>
    public bool Open { get; set; }
    /// <summary>Las primeras líneas de lo que devolvió el paso.</summary>
    public string? Output { get; set; }
    /// <summary>Las líneas del cambio, con «+», «-» o « » adelante (solo las primeras).</summary>
    public IReadOnlyList<string> Diff { get; set; } = [];
    public int Added { get; set; }
    public int Removed { get; set; }
    public long Tokens { get; set; }
    public IReadOnlyList<TodoItem> Todos { get; set; } = [];

    // Un permiso: a qué pedido responde, a qué paso pertenece y lo que hace falta para contestarlo.
    public string? RequestId { get; init; }
    public string? StepId { get; init; }
    public JsonNode? Input { get; init; }
    public JsonNode? Suggestions { get; init; }
    public PermissionAnswer? Answer { get; set; }
    /// <summary>Se respondió en la isla, no en el bloque.</summary>
    public bool FromIsland { get; set; }

    // El cierre del turno.
    public double Cost { get; init; }
    public TimeSpan Duration { get; init; }
    public int Turns { get; init; }
    public bool Failed { get; init; }
}

/// <summary>
/// La sesión de un agente vista como bloques: lee los eventos de Claude Code en modo estructurado (-p con
/// --input-format y --output-format stream-json) y arma con ellos los bloques, sin la pantalla de la TUI. Cada
/// evento es una línea de JSON; Apply devuelve los bloques que cambiaron. No abre procesos: se prueba con líneas.
/// </summary>
sealed class AgentTranscript
{
    /// <summary>Cuántas líneas de la salida de un paso y del cambio se guardan para mostrar.</summary>
    const int OutputLines = 6, DiffLines = 40;

    readonly List<AgentBlock> _blocks = [];
    readonly Dictionary<string, int> _parts = [];
    string? _message;

    public IReadOnlyList<AgentBlock> Blocks => _blocks;
    public string? SessionId { get; private set; }
    public string? Model { get; private set; }
    public string? Cwd { get; private set; }
    public string? PermissionMode { get; private set; }
    /// <summary>Hay un turno en curso: desde que se envía un pedido hasta su resultado.</summary>
    public bool Busy { get; private set; }
    /// <summary>El uso de la cuenta (0–100), si llegó: la sesión de 5 horas y la semana.</summary>
    public double? FiveHour { get; private set; }
    public double? SevenDay { get; private set; }
    /// <summary>Lo que ocupa la conversación en el contexto (tokens del último mensaje) y cuánto cabe.</summary>
    public long ContextUsed { get; private set; }
    public long ContextWindow { get; private set; }

    /// <summary>El contexto usado, 0–100, si ya se sabe cuánto cabe.</summary>
    public double? ContextPercent => ContextWindow > 0 ? Math.Min(100, ContextUsed * 100.0 / ContextWindow) : null;

    /// <summary>Un pedido de la persona: su bloque y empieza un turno.</summary>
    public AgentBlock Ask(string text)
    {
        var block = Add(new AgentBlock { Kind = AgentBlockKind.Ask, Text = text });
        Busy = true;
        return block;
    }

    /// <summary>Un aviso de la app (la sesión terminó, no se encontró Claude Code…).</summary>
    public AgentBlock Notice(string text)
    {
        Close();
        Busy = false;
        return Add(new AgentBlock { Kind = AgentBlockKind.Notice, Text = text });
    }

    /// <summary>Lee una línea de la salida. Las que no son JSON (o no se conocen) no cambian nada.</summary>
    public IReadOnlyList<AgentBlock> Apply(string line)
    {
        if (line.Length == 0 || line[0] != '{') return [];
        try
        {
            using var doc = JsonDocument.Parse(line);
            var changed = new List<AgentBlock>();
            Apply(doc.RootElement, changed);
            return changed;
        }
        catch (JsonException) { return []; }
    }

    void Apply(JsonElement r, List<AgentBlock> changed)
    {
        switch (Json.Str(r, "type"))
        {
            case "system":
                System(r, changed);
                break;
            case "stream_event" when r.TryGetProperty("event", out var e):
                Stream(e, changed);
                break;
            case "assistant" when r.TryGetProperty("message", out var m):
                Assistant(m, changed);
                break;
            case "user" when r.TryGetProperty("message", out var m):
                Results(r, m, changed);
                break;
            case "control_request" when r.TryGetProperty("request", out var req) && Json.Str(req, "subtype") == "can_use_tool":
                Permission(r, req, changed);
                break;
            case "rate_limit_event" when r.TryGetProperty("rate_limit_info", out var info)
                && info.TryGetProperty("unifiedWindows", out var windows):
                if (windows.TryGetProperty("five_hour", out var five)) FiveHour = Json.Num(five, "utilization") * 100;
                if (windows.TryGetProperty("seven_day", out var seven)) SevenDay = Json.Num(seven, "utilization") * 100;
                break;
            case "result":
                Result(r, changed);
                break;
        }
    }

    void System(JsonElement r, List<AgentBlock> changed)
    {
        switch (Json.Str(r, "subtype"))
        {
            case "init":
                SessionId = Json.Str(r, "session_id") ?? SessionId;
                Model = Json.Str(r, "model") ?? Model;
                Cwd = Json.Str(r, "cwd") ?? Cwd;
                PermissionMode = Json.Str(r, "permissionMode") ?? PermissionMode;
                break;
            case "thinking_tokens":
                // Antes del bloque de lo que pensó (o sin mensajes parciales) llega solo la cuenta de tokens.
                var think = OpenThink() ?? Add(new AgentBlock { Kind = AgentBlockKind.Think, Open = true, Id = "" }, changed);
                think.Tokens = (long)Json.Num(r, "estimated_tokens");
                Touch(think, changed);
                break;
            case "permission_denied" when Step(Json.Str(r, "tool_use_id")) is { } step:
                step.State = StepState.Denied;
                Touch(step, changed);
                break;
        }
    }

    /// <summary>Los eventos parciales (--include-partial-messages): el texto llega a medida que se escribe.</summary>
    void Stream(JsonElement e, List<AgentBlock> changed)
    {
        switch (Json.Str(e, "type"))
        {
            case "message_start" when e.TryGetProperty("message", out var m):
                _message = Json.Str(m, "id");
                Usage(m);
                break;
            case "content_block_start" when e.TryGetProperty("content_block", out var block):
                var key = $"{_message}:{(int)Json.Num(e, "index")}";
                switch (Json.Str(block, "type"))
                {
                    case "thinking":
                        // Si ya llegó la cuenta de tokens, ese es su bloque.
                        if (OpenThink() is null) Add(new AgentBlock { Kind = AgentBlockKind.Think, Id = key, Open = true }, changed);
                        break;
                    case "text":
                        Close(changed);
                        Add(new AgentBlock { Kind = AgentBlockKind.Say, Id = key, Open = true }, changed);
                        break;
                    case "tool_use" when Json.Str(block, "id") is { } id:
                        Close(changed);
                        StepFor(id, Json.Str(block, "name"), changed);
                        break;
                }
                break;
            case "content_block_delta" when e.TryGetProperty("delta", out var delta)
                && Json.Str(delta, "type") == "text_delta" && Json.Str(delta, "text") is { } piece:
                if (_blocks.LastOrDefault(b => b.Kind == AgentBlockKind.Say && b.Open) is { } say)
                {
                    say.Text += piece;
                    Touch(say, changed);
                }
                break;
        }
    }

    /// <summary>
    /// Un mensaje completo: llega una parte por evento y en orden, así que la enésima parte de un mensaje es la del
    /// índice n de los eventos parciales. Lo completo reemplaza a lo que se armó por partes.
    /// </summary>
    void Assistant(JsonElement m, List<AgentBlock> changed)
    {
        Usage(m);
        var message = Json.Str(m, "id") ?? "";
        if (!m.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return;
        foreach (var part in content.EnumerateArray())
        {
            _parts.TryGetValue(message, out var n);
            _parts[message] = n + 1;
            var key = $"{message}:{n}";
            switch (Json.Str(part, "type"))
            {
                case "text" when Json.Str(part, "text") is { } text:
                    var say = _blocks.LastOrDefault(b => b.Kind == AgentBlockKind.Say && b.Id == key);
                    if (say is null)
                    {
                        Close(changed);
                        say = Add(new AgentBlock { Kind = AgentBlockKind.Say, Id = key }, changed);
                    }
                    say.Text = text;
                    say.Open = false;
                    Touch(say, changed);
                    break;
                case "thinking":
                    var think = _blocks.LastOrDefault(b => b.Kind == AgentBlockKind.Think && (b.Id == key || b.Open))
                        ?? Add(new AgentBlock { Kind = AgentBlockKind.Think, Id = key }, changed);
                    think.Open = false;
                    Touch(think, changed);
                    break;
                case "tool_use" when Json.Str(part, "id") is { } id:
                    Close(changed);
                    var name = Json.Str(part, "name");
                    part.TryGetProperty("input", out var input);
                    if (name == "TodoWrite") Todos(input, changed);
                    else
                    {
                        var step = StepFor(id, name, changed);
                        step.Text = AgentSteps.Target(name, input, Cwd);
                        Touch(step, changed);
                    }
                    break;
            }
        }
    }

    /// <summary>Los resultados de los pasos (llegan como un mensaje de la persona).</summary>
    void Results(JsonElement r, JsonElement m, List<AgentBlock> changed)
    {
        if (!m.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return;
        foreach (var part in content.EnumerateArray())
        {
            if (Json.Str(part, "type") != "tool_result" || Step(Json.Str(part, "tool_use_id")) is not { } step) continue;
            var failed = part.TryGetProperty("is_error", out var error) && error.ValueKind == JsonValueKind.True;
            if (step.State != StepState.Denied) step.State = failed ? StepState.Failed : StepState.Done;
            step.Output = FirstLines(ResultText(part));
            if (r.TryGetProperty("tool_use_result", out var detail) && detail.ValueKind == JsonValueKind.Object) Changes(step, detail);
            Touch(step, changed);
        }
    }

    void Permission(JsonElement r, JsonElement req, List<AgentBlock> changed)
    {
        var tool = Json.Str(req, "tool_name");
        var stepId = Json.Str(req, "tool_use_id");
        req.TryGetProperty("input", out var input);
        req.TryGetProperty("permission_suggestions", out var suggestions);
        if (Step(stepId) is { } step)
        {
            step.State = StepState.Waiting;
            Touch(step, changed);
        }
        var target = AgentSteps.Target(tool, input, Cwd);
        Add(new AgentBlock
        {
            Kind = AgentBlockKind.Permission,
            Id = Json.Str(r, "request_id") ?? "",
            RequestId = Json.Str(r, "request_id"),
            StepId = stepId,
            Tool = tool,
            Text = target.Length > 0 ? target : Json.Str(req, "description") ?? "",
            Input = Node(input),
            Suggestions = Node(suggestions),
        }, changed);
    }

    void Result(JsonElement r, List<AgentBlock> changed)
    {
        Close(changed);
        Busy = false;
        if (r.TryGetProperty("modelUsage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            foreach (var model in usage.EnumerateObject())
                ContextWindow = Math.Max(ContextWindow, (long)Json.Num(model.Value, "contextWindow"));
        var failed = r.TryGetProperty("is_error", out var error) && error.ValueKind == JsonValueKind.True;
        Add(new AgentBlock
        {
            Kind = AgentBlockKind.Done,
            Cost = Json.Num(r, "total_cost_usd"),
            Duration = TimeSpan.FromMilliseconds(Json.Num(r, "duration_ms")),
            Turns = (int)Json.Num(r, "num_turns"),
            Failed = failed,
            Text = failed ? Json.Str(r, "result") ?? "" : "",
        }, changed);
    }

    /// <summary>La lista de tareas: una por turno, que se actualiza cada vez que el agente la reescribe.</summary>
    void Todos(JsonElement input, List<AgentBlock> changed)
    {
        var items = input.ValueKind == JsonValueKind.Object && input.TryGetProperty("todos", out var todos) && todos.ValueKind == JsonValueKind.Array
            ? todos.EnumerateArray().Select(t => new TodoItem(Json.Str(t, "content") ?? "", Json.Str(t, "status") ?? "pending")).ToList()
            : [];
        var lastAsk = _blocks.FindLastIndex(b => b.Kind == AgentBlockKind.Ask);
        var block = _blocks.Skip(lastAsk + 1).LastOrDefault(b => b.Kind == AgentBlockKind.Todo)
            ?? Add(new AgentBlock { Kind = AgentBlockKind.Todo }, changed);
        block.Todos = items;
        Touch(block, changed);
    }

    /// <summary>Lo que cambió un paso que edita: el parche de Edit o el archivo nuevo de Write.</summary>
    void Changes(AgentBlock step, JsonElement detail)
    {
        var lines = new List<string>();
        if (detail.TryGetProperty("structuredPatch", out var patch) && patch.ValueKind == JsonValueKind.Array && patch.GetArrayLength() > 0)
        {
            foreach (var hunk in patch.EnumerateArray())
                if (hunk.TryGetProperty("lines", out var hunkLines) && hunkLines.ValueKind == JsonValueKind.Array)
                    lines.AddRange(hunkLines.EnumerateArray().Select(l => l.GetString() ?? ""));
        }
        else if (Json.Str(detail, "type") == "create" && Json.Str(detail, "content") is { } created)
            lines.AddRange(created.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n').Select(l => "+" + l));
        if (lines.Count == 0) return;
        step.Added = lines.Count(l => l.StartsWith('+'));
        step.Removed = lines.Count(l => l.StartsWith('-'));
        step.Diff = lines.Take(DiffLines).ToList();
    }

    void Usage(JsonElement message)
    {
        if (!message.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object) return;
        var used = (long)(Json.Num(u, "input_tokens") + Json.Num(u, "cache_creation_input_tokens") + Json.Num(u, "cache_read_input_tokens"));
        if (used > 0) ContextUsed = used;
    }

    static string ResultText(JsonElement part)
    {
        if (!part.TryGetProperty("content", out var c)) return "";
        if (c.ValueKind == JsonValueKind.String) return c.GetString() ?? "";
        if (c.ValueKind != JsonValueKind.Array) return "";
        return string.Join("\n", c.EnumerateArray().Select(p => Json.Str(p, "text")).OfType<string>());
    }

    static string? FirstLines(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Trim('\n').Split('\n');
        if (lines.Length == 1 && lines[0].Length == 0) return null;
        var shown = string.Join("\n", lines.Take(OutputLines).Select(l => l.Length > 200 ? l[..200] + "…" : l));
        return lines.Length > OutputLines ? shown + "\n" + L.T("… {0} líneas más", lines.Length - OutputLines) : shown;
    }

    static JsonNode? Node(JsonElement e) => e.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : JsonNode.Parse(e.GetRawText());

    AgentBlock? OpenThink() => _blocks.LastOrDefault() is { Kind: AgentBlockKind.Think, Open: true } think ? think : null;

    AgentBlock? Step(string? id) =>
        id is null ? null : _blocks.LastOrDefault(b => b.Kind == AgentBlockKind.Step && b.Id == id);

    AgentBlock StepFor(string id, string? tool, List<AgentBlock> changed) =>
        Step(id) ?? Add(new AgentBlock { Kind = AgentBlockKind.Step, Id = id, Tool = tool, State = StepState.Running }, changed);

    /// <summary>Lo que se estaba escribiendo o pensando ya terminó.</summary>
    void Close(List<AgentBlock>? changed = null)
    {
        foreach (var block in _blocks.Where(b => b.Open))
        {
            block.Open = false;
            if (changed is not null) Touch(block, changed);
        }
    }

    AgentBlock Add(AgentBlock block, List<AgentBlock>? changed = null)
    {
        _blocks.Add(block);
        if (changed is not null) Touch(block, changed);
        return block;
    }

    static void Touch(AgentBlock block, List<AgentBlock> changed)
    {
        if (!changed.Contains(block)) changed.Add(block);
    }
}

/// <summary>Cómo se nombra cada paso en su bloque: el verbo («leyó», «buscó») y sobre qué trabajó.</summary>
static class AgentSteps
{
    public static string Verb(string? tool) => tool switch
    {
        "Read" or "NotebookRead" => L.T("leyó"),
        "Edit" or "MultiEdit" or "NotebookEdit" => L.T("editó"),
        "Write" => L.T("creó"),
        "Bash" or "PowerShell" => L.T("ejecutó"),
        "Grep" => L.T("buscó"),
        "Glob" => L.T("buscó archivos"),
        "WebSearch" => L.T("buscó en internet"),
        "WebFetch" => L.T("abrió"),
        "Task" or "Agent" => L.T("delegó"),
        null => L.T("usó una herramienta"),
        _ => tool,
    };

    /// <summary>Sobre qué trabaja: la ruta (relativa a la carpeta de la sesión), el comando, lo que busca.</summary>
    public static string Target(string? tool, JsonElement input, string? cwd)
    {
        if (input.ValueKind != JsonValueKind.Object) return "";
        if ((Json.Str(input, "file_path") ?? Json.Str(input, "notebook_path")) is { } path) return Relative(path, cwd);
        var text = tool switch
        {
            "Bash" or "PowerShell" => Json.Str(input, "command"),
            "Grep" or "Glob" => Json.Str(input, "pattern"),
            "WebSearch" => Json.Str(input, "query"),
            "WebFetch" => Json.Str(input, "url"),
            "Task" or "Agent" => Json.Str(input, "description"),
            _ => Json.Str(input, "description") ?? Json.Str(input, "path"),
        } ?? "";
        var first = text.ReplaceLineEndings("\n").Split('\n')[0].Trim();
        return first.Length < text.Trim().Length ? first + " …" : first;
    }

    static string Relative(string path, string? cwd)
    {
        if (string.IsNullOrEmpty(cwd)) return Path.GetFileName(path);
        try
        {
            var relative = Path.GetRelativePath(cwd, path);
            return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? path : relative.Replace('\\', '/');
        }
        catch (Exception) { return path; }
    }
}

/// <summary>Los mensajes que la app le escribe a Claude Code por su entrada estándar (una línea de JSON cada uno).</summary>
static class AgentProtocol
{
    public static string Initialize(string id) => Line(new JsonObject
    {
        ["type"] = "control_request",
        ["request_id"] = id,
        ["request"] = new JsonObject { ["subtype"] = "initialize" },
    });

    public static string Interrupt(string id) => Line(new JsonObject
    {
        ["type"] = "control_request",
        ["request_id"] = id,
        ["request"] = new JsonObject { ["subtype"] = "interrupt" },
    });

    public static string UserMessage(string text) => Line(new JsonObject
    {
        ["type"] = "user",
        ["message"] = new JsonObject { ["role"] = "user", ["content"] = text },
    });

    /// <summary>La respuesta a un permiso. «Siempre aquí» devuelve las sugerencias que trajo el pedido (las guarda Claude Code).</summary>
    public static string Answer(AgentBlock permission, PermissionAnswer answer)
    {
        var response = answer == PermissionAnswer.Deny
            ? new JsonObject { ["behavior"] = "deny", ["message"] = "The user rejected this action." }
            : new JsonObject { ["behavior"] = "allow", ["updatedInput"] = permission.Input?.DeepClone() ?? new JsonObject() };
        if (answer == PermissionAnswer.AllowAlways && permission.Suggestions is JsonArray suggestions)
            response["updatedPermissions"] = suggestions.DeepClone();
        return Line(new JsonObject
        {
            ["type"] = "control_response",
            ["response"] = new JsonObject { ["subtype"] = "success", ["request_id"] = permission.RequestId, ["response"] = response },
        });
    }

    /// <summary>El permiso como lo manda el hook PermissionRequest, para que la isla lo muestre igual que los demás.</summary>
    public static string IslandRequest(AgentBlock permission, string? sessionId, string cwd) => Line(new JsonObject
    {
        ["hook_event_name"] = "PermissionRequest",
        ["session_id"] = sessionId ?? "",
        ["cwd"] = cwd,
        ["tool_name"] = permission.Tool,
        ["tool_input"] = permission.Input?.DeepClone() ?? new JsonObject(),
    });

    /// <summary>
    /// La respuesta de la isla (la del hook: hookSpecificOutput.decision) como respuesta al permiso. Se pasa tal cual,
    /// con las respuestas de una pregunta en updatedInput; si no trae updatedInput, va el pedido sin cambios.
    /// </summary>
    public static (string Line, PermissionAnswer Answer)? FromIsland(AgentBlock permission, string reply)
    {
        try
        {
            if (JsonNode.Parse(reply)?["hookSpecificOutput"]?["decision"] is not JsonObject decision) return null;
            var allow = (string?)decision["behavior"] == "allow";
            if (!allow && (string?)decision["behavior"] != "deny") return null;
            var response = (JsonObject)decision.DeepClone();
            if (allow && response["updatedInput"] is null) response["updatedInput"] = permission.Input?.DeepClone() ?? new JsonObject();
            return (Line(new JsonObject
            {
                ["type"] = "control_response",
                ["response"] = new JsonObject { ["subtype"] = "success", ["request_id"] = permission.RequestId, ["response"] = response },
            }), allow ? PermissionAnswer.Allow : PermissionAnswer.Deny);
        }
        catch (Exception) { return null; }
    }

    static string Line(JsonObject o) => o.ToJsonString();
}

/// <summary>Con qué se abre una sesión de la Terminal: la cuenta, la carpeta, el modelo, el esfuerzo y el modo de permisos.</summary>
sealed record AgentOptions(string Folder, string? ConfigDir = null, string? Model = null, string? Effort = null, string? PermissionMode = null);

/// <summary>
/// Una sesión de Claude Code de la Terminal: el proceso en modo estructurado, con la cuenta (CLAUDE_CONFIG_DIR), el
/// modelo y el esfuerzo de esa sesión. Los permisos se contestan en su bloque y, si la isla está, también en ella: el
/// hook de Claupet se aparta (Approvals.TerminalVariable) y la sesión le pasa el pedido a la isla; vale lo que se
/// responda primero. Cada línea se aplica con post, en el hilo de la vista: los bloques solo cambian ahí.
/// </summary>
sealed class TerminalAgent : IDisposable
{
    readonly AgentOptions _options;
    readonly Action<Action> _post;
    readonly object _write = new();
    /// <summary>Los permisos que esperan también en la isla; al cancelarlos, la isla quita la tarjeta.</summary>
    readonly Dictionary<AgentBlock, CancellationTokenSource> _island = [];
    Process? _process;
    int _requests;

    /// <summary>Los permisos también se preguntan en la isla de la app (si está). Una copia de prueba no la usa.</summary>
    public bool AskIsland { get; init; }

    public TerminalAgent(AgentOptions options, Action<Action> post)
    {
        _options = options;
        _post = post;
    }

    public AgentOptions Options => _options;
    public AgentTranscript Transcript { get; } = new();
    public bool Running => _process is { HasExited: false };

    /// <summary>Cambiaron esos bloques (o el estado de la sesión). Se avisa en el hilo de post.</summary>
    public event Action<IReadOnlyList<AgentBlock>>? Changed;

    /// <summary>Las opciones de claude para una sesión: nada de lo que escribe la persona va aquí (va por la entrada).</summary>
    public static List<string> Arguments(AgentOptions o)
    {
        var args = new List<string> { "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
            "--include-partial-messages", "--permission-prompts", "host", "--permission-prompt-tool", "stdio" };
        if (Launcher.IsValid(o.Model) && o.Model is { } model) args.AddRange(["--model", model]);
        if (o.Effort is { } effort && Launcher.Efforts.Contains(effort)) args.AddRange(["--effort", effort]);
        if (o.PermissionMode is { } mode && Modes.Contains(mode)) args.AddRange(["--permission-mode", mode]);
        return args;
    }

    /// <summary>Los modos de permisos que se pueden elegir para una sesión.</summary>
    public static readonly string[] Modes = ["default", "acceptEdits", "plan", "auto"];

    /// <summary>Abre Claude Code. Si no está o no abre, queda un aviso en la sesión.</summary>
    public void Start()
    {
        if (Assistant.FindClaude() is not { } claude)
        {
            Notice(L.T("No encontré Claude Code. ¿Está instalado y en el PATH?"));
            return;
        }
        var start = Assistant.ClaudeStart(claude, _options.ConfigDir, Arguments(_options));
        start.WorkingDirectory = _options.Folder;
        start.Environment[Approvals.TerminalVariable] = "1";
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        try { process.Start(); }
        catch (Exception ex)
        {
            Notice(L.T("No se pudo abrir Claude Code: {0}", ex.Message));
            return;
        }
        _process = process;
        Write(AgentProtocol.Initialize(NextId()));
        _ = Task.Run(() => ReadAsync(process));
    }

    /// <summary>Envía un pedido. Si el agente está en un turno, Claude Code lo encola y lo toma al terminar.</summary>
    public void Send(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !Running) return;
        Changed?.Invoke([Transcript.Ask(text)]);
        Write(AgentProtocol.UserMessage(text));
    }

    /// <summary>Corta el turno en curso (como Esc en la TUI). La sesión sigue abierta.</summary>
    public void Interrupt()
    {
        if (Running && Transcript.Busy) Write(AgentProtocol.Interrupt(NextId()));
    }

    /// <summary>Lo respondió en el bloque: va a Claude Code y la isla, si también lo mostraba, lo quita.</summary>
    public void Answer(AgentBlock permission, PermissionAnswer answer)
    {
        if (permission.Kind != AgentBlockKind.Permission || permission.Answer is not null || !Running) return;
        Resolve(permission, answer, AgentProtocol.Answer(permission, answer));
    }

    void Resolve(AgentBlock permission, PermissionAnswer answer, string line)
    {
        permission.Answer = answer;
        StopIsland(permission);
        var changed = new List<AgentBlock> { permission };
        if (Transcript.Blocks.LastOrDefault(b => b.Kind == AgentBlockKind.Step && b.Id == permission.StepId) is { } step)
        {
            step.State = answer == PermissionAnswer.Deny ? StepState.Denied : StepState.Running;
            changed.Add(step);
        }
        Write(line);
        Changed?.Invoke(changed);
    }

    /// <summary>Le pasa el permiso a la isla; si lo responde ahí antes que en el bloque, vale esa respuesta.</summary>
    async Task AskIslandAsync(AgentBlock permission)
    {
        var cancel = _island[permission] = new CancellationTokenSource();
        var request = AgentProtocol.IslandRequest(permission, Transcript.SessionId, Transcript.Cwd ?? _options.Folder);
        var reply = await Task.Run(() => Approvals.AskAppAsync(request, cancel.Token));
        _post(() =>
        {
            if (cancel.IsCancellationRequested || reply is null || permission.Answer is not null || !Running) return;
            if (AgentProtocol.FromIsland(permission, reply) is not { } answer) return;
            permission.FromIsland = true;
            Resolve(permission, answer.Answer, answer.Line);
        });
    }

    void StopIsland(AgentBlock permission)
    {
        if (!_island.Remove(permission, out var cancel)) return;
        cancel.Cancel();
        cancel.Dispose();
    }

    async Task ReadAsync(Process process)
    {
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
                // Se avisa aunque no cambie un bloque: puede cambiar el modelo, el uso o el contexto.
                _post(() =>
                {
                    var changed = Transcript.Apply(line);
                    if (AskIsland)
                        foreach (var permission in changed.Where(b => b.Kind == AgentBlockKind.Permission && b.Answer is null && !_island.ContainsKey(b)))
                            _ = AskIslandAsync(permission);
                    Changed?.Invoke(changed);
                });
        }
        catch (Exception) { }
        await process.WaitForExitAsync();
        var problem = (await stderr).Trim();
        var code = process.ExitCode;
        _post(() =>
        {
            if (!ReferenceEquals(_process, process)) return;
            foreach (var permission in _island.Keys.ToList()) StopIsland(permission);
            var reason = code == 0 || problem.Length == 0 ? L.T("La sesión terminó.") : problem.Split('\n')[0].Trim();
            Notice(reason);
        });
    }

    void Notice(string text) => Changed?.Invoke([Transcript.Notice(text)]);

    string NextId() => $"claupet-{Interlocked.Increment(ref _requests)}";

    void Write(string line)
    {
        if (_process is not { } process) return;
        lock (_write)
        {
            try
            {
                process.StandardInput.Write(line + "\n");
                process.StandardInput.Flush();
            }
            catch (Exception) { }
        }
    }

    public void Dispose()
    {
        foreach (var permission in _island.Keys.ToList()) StopIsland(permission);
        var process = _process;
        _process = null;
        if (process is null) return;
        try { process.StandardInput.Close(); } catch (Exception) { }
        Assistant.Kill(process);
        process.Dispose();
    }
}

/// <summary>La rama de una carpeta, leída de .git/HEAD (sin abrir git): también en un worktree, donde .git es un archivo.</summary>
static class GitBranch
{
    public static string? Of(string? folder)
    {
        try
        {
            for (var dir = folder is null ? null : new DirectoryInfo(folder); dir is not null; dir = dir.Parent)
            {
                var git = Path.Combine(dir.FullName, ".git");
                string? head = null;
                if (Directory.Exists(git)) head = Path.Combine(git, "HEAD");
                else if (File.Exists(git) && File.ReadAllText(git).Trim() is var link && link.StartsWith("gitdir:", StringComparison.Ordinal))
                    head = Path.Combine(Path.GetFullPath(link["gitdir:".Length..].Trim(), dir.FullName), "HEAD");
                if (head is null) continue;
                if (!File.Exists(head)) return null;
                var text = File.ReadAllText(head).Trim();
                const string Ref = "ref: refs/heads/";
                return text.StartsWith(Ref, StringComparison.Ordinal) ? text[Ref.Length..] : text.Length >= 7 ? text[..7] : null;
            }
        }
        catch (Exception) { }
        return null;
    }
}
