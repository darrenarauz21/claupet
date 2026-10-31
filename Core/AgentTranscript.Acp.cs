using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet;

/// <summary>
/// La sesión de un agente por Agent Client Protocol (Gemini CLI, Copilot CLI) vista como bloques: AcpDriver lleva el
/// JSON-RPC y pasa aquí lo que el agente cuenta (session/update) y los permisos que pide. Las respuestas llegan por
/// fragmentos; los pasos (tool_call) traen su clase (read, edit, execute…), lo que tocan y, al editar, el texto de antes
/// y el de después, del que sale el cambio por líneas.
/// </summary>
sealed partial class AgentTranscript
{
    /// <summary>Hasta cuántas líneas por lado se compara un cambio línea a línea; más grande, se muestra entero.</summary>
    const int AcpDiffCells = 250_000;

    /// <summary>Lo que se sabe de cada paso para mostrarlo y para la isla: su rawInput, la ruta y el título.</summary>
    readonly Dictionary<string, JsonObject> _acpInputs = [];
    /// <summary>Bloques que cambiaron fuera de Apply (al cortar el turno): salen con la línea siguiente.</summary>
    readonly List<AgentBlock> _acpTouched = [];
    double _acpCost;

    /// <summary>El id corto de un modo: el de Copilot es una dirección («…/session-modes#plan» es «plan»).</summary>
    public static string AcpShortMode(string id) => id.LastIndexOf('#') is var hash and >= 0 && hash < id.Length - 1 ? id[(hash + 1)..] : id;

    internal void AcpFlush(List<AgentBlock> changed)
    {
        foreach (var block in _acpTouched) Touch(block, changed);
        _acpTouched.Clear();
    }

    internal void AcpStarted(string sessionId, string? cwd)
    {
        SessionId = sessionId;
        Cwd = cwd ?? Cwd;
    }

    internal void AcpModel(string? model) => Model = model ?? Model;
    internal void AcpMode(string? mode) => PermissionMode = mode ?? PermissionMode;

    /// <summary>Sale un pedido guardado: empieza otro turno (el bloque Ask ya lo puso la sesión).</summary>
    internal void AcpTurn() => Busy = true;
    internal void AcpIdle() => Busy = false;

    /// <summary>Un aviso que cierra lo que estaba en curso (la sesión no abrió).</summary>
    internal void AcpNotice(string text, List<AgentBlock> changed)
    {
        Close(changed);
        Busy = false;
        Add(new AgentBlock { Kind = AgentBlockKind.Notice, Text = text }, changed);
    }

    /// <summary>Un aviso que no corta el turno (un modo o un modelo que no se pudo poner).</summary>
    internal void AcpError(string text, List<AgentBlock> changed)
    {
        if (text.Length > 0) Add(new AgentBlock { Kind = AgentBlockKind.Notice, Text = text }, changed);
    }

    /// <summary>Un aviso de session/update (lo que no es de los permisos ni de las opciones de la sesión).</summary>
    internal void AcpUpdate(JsonElement u, List<AgentBlock> changed)
    {
        switch (Json.Str(u, "sessionUpdate"))
        {
            case "agent_message_chunk" when AcpText(u) is { } text:
                // Gemini CLI avisa así que cambió el modo (al entrar o salir del modo plan): no es respuesta.
                if (text.StartsWith("[MODE_UPDATE] ", StringComparison.Ordinal))
                {
                    PermissionMode = text["[MODE_UPDATE] ".Length..].Trim();
                    break;
                }
                var say = _blocks.LastOrDefault() is { Kind: AgentBlockKind.Say, Open: true } open ? open : null;
                if (say is null)
                {
                    Close(changed);
                    say = Add(new AgentBlock { Kind = AgentBlockKind.Say, Id = Json.Str(u, "messageId") ?? "", Open = true }, changed);
                }
                say.Text += text;
                Touch(say, changed);
                break;
            case "agent_thought_chunk" when AcpText(u) is { } thought:
                var think = OpenThink();
                if (think is null)
                {
                    Close(changed);
                    think = Add(new AgentBlock { Kind = AgentBlockKind.Think, Id = Json.Str(u, "messageId") ?? "", Open = true }, changed);
                }
                think.Text += thought;
                Touch(think, changed);
                break;
            case "tool_call" or "tool_call_update":
                AcpStep(u, changed);
                break;
            case "plan" when u.TryGetProperty("entries", out var entries):
                AcpPlan(entries, changed);
                break;
            case "plan_update" when u.TryGetProperty("plan", out var plan) && plan.ValueKind == JsonValueKind.Object
                && Json.Str(plan, "type") == "items" && plan.TryGetProperty("entries", out var items):
                AcpPlan(items, changed);
                break;
            case "usage_update":
                if (Json.Num(u, "used") is var used and > 0) ContextUsed = (long)used;
                if (Json.Num(u, "size") is var size and > 0) ContextWindow = (long)size;
                if (u.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Object && Json.Str(cost, "currency") == "USD")
                    _acpCost = Json.Num(cost, "amount");
                break;
            case "notice" when Json.Str(u, "title") is { Length: > 0 } title:
                Add(new AgentBlock { Kind = AgentBlockKind.Notice, Text = title }, changed);
                break;
        }
    }

    static string? AcpText(JsonElement u) =>
        u.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Object && Json.Str(c, "type") == "text" ? Json.Str(c, "text") : null;

    /// <summary>
    /// Un paso: tool_call lo abre y tool_call_update lo cambia (solo los campos que trae; «content» reemplaza lo anterior).
    /// La clase de ACP se nombra como la herramienta de Claude Code que hace lo mismo, así el verbo y la isla son iguales.
    /// </summary>
    AgentBlock? AcpStep(JsonElement u, List<AgentBlock> changed)
    {
        if (Json.Str(u, "toolCallId") is not { } id) return null;
        var content = u.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Array ? c : default;
        var step = Step(id);
        if (step is null)
        {
            Close(changed);
            step = Add(new AgentBlock { Kind = AgentBlockKind.Step, Id = id, Tool = AcpTool(Json.Str(u, "kind"), content), State = StepState.Running }, changed);
        }
        var input = AcpInput(id, u, step.Tool);
        var target = AgentSteps.Target(step.Tool, JsonSerializer.SerializeToElement(input), Cwd);
        if (target.Length > 0) step.Text = target;
        else if (Json.Str(u, "title") is { Length: > 0 } title) step.Text = title;

        var failed = false;
        switch (Json.Str(u, "status"))
        {
            case "in_progress" when step.State is StepState.Running or StepState.Waiting:
                step.State = StepState.Running;
                break;
            case "completed" when step.State != StepState.Denied:
                step.State = StepState.Done;
                break;
            case "failed":
                failed = true;
                // Copilot dice que la persona lo rechazó con rawOutput.code «rejected».
                var rejected = u.TryGetProperty("rawOutput", out var raw) && raw.ValueKind == JsonValueKind.Object && Json.Str(raw, "code") == "rejected";
                step.State = rejected || step.State == StepState.Denied ? StepState.Denied : StepState.Failed;
                break;
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            var texts = new List<string>();
            var lines = new List<string>();
            foreach (var part in content.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.Object))
                switch (Json.Str(part, "type"))
                {
                    case "content" when part.TryGetProperty("content", out var block) && block.ValueKind == JsonValueKind.Object
                        && Json.Str(block, "text") is { Length: > 0 } text:
                        texts.Add(text);
                        break;
                    case "diff":
                        lines.AddRange(AcpDiff(Json.Str(part, "oldText"), Json.Str(part, "newText") ?? ""));
                        break;
                }
            step.Output = FirstLines(string.Join("\n", texts));
            step.Added = lines.Count(l => l.StartsWith('+'));
            step.Removed = lines.Count(l => l.StartsWith('-'));
            step.Diff = lines.Take(DiffLines).ToList();
        }
        // Sin texto en «content», lo que devolvió (rawOutput) si es texto: el mensaje del error, por ejemplo.
        if (step.Output is null && Json.Str(u, "status") is "completed" or "failed" && u.TryGetProperty("rawOutput", out var output))
            step.Output = FirstLines(output.ValueKind == JsonValueKind.String ? output.GetString() ?? ""
                : output.ValueKind == JsonValueKind.Object ? Json.Str(output, failed ? "message" : "content") ?? Json.Str(output, "message") ?? "" : "");
        Touch(step, changed);
        return step;
    }

    /// <summary>La herramienta de Claude Code que hace lo mismo que esa clase de paso (edit sin texto anterior es crear).</summary>
    static string? AcpTool(string? kind, JsonElement content) => kind switch
    {
        "read" => "Read",
        "edit" when content.ValueKind == JsonValueKind.Array && content.EnumerateArray().Any(p =>
            p.ValueKind == JsonValueKind.Object && Json.Str(p, "type") == "diff" && string.IsNullOrEmpty(Json.Str(p, "oldText"))) => "Write",
        "edit" => "Edit",
        "execute" => "Bash",
        "search" => "Grep",
        "fetch" => "WebFetch",
        _ => null,
    };

    /// <summary>
    /// Lo que se sabe del paso como entrada de Claude Code (file_path, command, description), juntando lo que trae cada
    /// aviso: así AgentSteps lo nombra y la isla lo muestra igual que un permiso de Claude Code.
    /// </summary>
    JsonObject AcpInput(string id, JsonElement u, string? tool)
    {
        if (!_acpInputs.TryGetValue(id, out var input)) _acpInputs[id] = input = new JsonObject();
        if (u.TryGetProperty("rawInput", out var raw) && raw.ValueKind == JsonValueKind.Object)
            foreach (var property in raw.EnumerateObject()) input[property.Name] = Node(property.Value);
        var path = u.TryGetProperty("locations", out var locations) && locations.ValueKind == JsonValueKind.Array
            ? locations.EnumerateArray().Where(l => l.ValueKind == JsonValueKind.Object).Select(l => Json.Str(l, "path")).FirstOrDefault(p => p is not null)
            : null;
        path ??= AcpString(input["path"]) ?? AcpString(input["fileName"]);
        if (path is null && u.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            path = content.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.Object && Json.Str(p, "type") == "diff").Select(p => Json.Str(p, "path")).FirstOrDefault(p => p is not null);
        if (path is not null && input["file_path"] is null) input["file_path"] = path;
        if (Json.Str(u, "title") is { Length: > 0 } title)
        {
            if (input["description"] is null) input["description"] = title;
            // Gemini CLI no manda rawInput: el título de un comando es el comando.
            if (tool == "Bash" && input["command"] is null) input["command"] = title;
        }
        return input;
    }

    static string? AcpString(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>
    /// Un permiso (session/request_permission): el paso queda esperando y el bloque guarda el id del pedido (tal cual, para
    /// responderlo) y las opciones que ofrece el agente, de donde sale la respuesta.
    /// </summary>
    internal void AcpPermission(string requestId, JsonElement p, List<AgentBlock> changed)
    {
        var toolCall = p.TryGetProperty("toolCall", out var t) && t.ValueKind == JsonValueKind.Object ? t : default;
        var step = toolCall.ValueKind == JsonValueKind.Object ? AcpStep(toolCall, changed) : null;
        if (step is not null)
        {
            step.State = StepState.Waiting;
            Touch(step, changed);
        }
        var title = toolCall.ValueKind == JsonValueKind.Object ? Json.Str(toolCall, "title") : null;
        var input = step is not null && _acpInputs.TryGetValue(step.Id, out var known) ? known : new JsonObject();
        var target = AgentSteps.Target(step?.Tool, JsonSerializer.SerializeToElement(input), Cwd);
        p.TryGetProperty("options", out var options);
        Add(new AgentBlock
        {
            Kind = AgentBlockKind.Permission,
            Id = requestId,
            RequestId = requestId,
            StepId = step?.Id,
            Tool = step?.Tool ?? title,
            Text = target.Length > 0 ? target : title ?? "",
            Input = input.DeepClone(),
            Suggestions = Node(options),
        }, changed);
    }

    /// <summary>
    /// Al cortar el turno, los permisos sin responder quedan rechazados (se les contesta «cancelled»): sus ids para la
    /// respuesta. Sus bloques salen como cambiados con la línea siguiente.
    /// </summary>
    internal IReadOnlyList<string> AcpCancelPermissions()
    {
        var ids = new List<string>();
        foreach (var permission in _blocks.Where(b => b.Kind == AgentBlockKind.Permission && b.Answer is null && b.RequestId is not null).ToList())
        {
            permission.Answer = PermissionAnswer.Deny;
            _acpTouched.Add(permission);
            ids.Add(permission.RequestId!);
            if (Step(permission.StepId) is { } step)
            {
                step.State = StepState.Denied;
                _acpTouched.Add(step);
            }
        }
        return ids;
    }

    /// <summary>La lista de tareas (plan): una por turno, que se reemplaza entera con cada aviso.</summary>
    void AcpPlan(JsonElement entries, List<AgentBlock> changed)
    {
        if (entries.ValueKind != JsonValueKind.Array) return;
        var items = entries.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object)
            .Select(e => new TodoItem(Json.Str(e, "content") ?? "", Json.Str(e, "status") ?? "pending")).ToList();
        var lastAsk = _blocks.FindLastIndex(b => b.Kind == AgentBlockKind.Ask);
        var block = _blocks.Skip(lastAsk + 1).LastOrDefault(b => b.Kind == AgentBlockKind.Todo);
        if (block is null)
        {
            Close(changed);
            block = Add(new AgentBlock { Kind = AgentBlockKind.Todo }, changed);
        }
        block.Todos = items;
        Touch(block, changed);
    }

    /// <summary>El fin del turno (la respuesta a session/prompt): falla si el agente se negó, se quedó sin tokens o se cortó.</summary>
    internal void AcpDone(string? stopReason, TimeSpan duration, List<AgentBlock> changed)
    {
        Close(changed);
        Busy = false;
        var problem = stopReason switch
        {
            "refusal" => L.T("El agente se negó a responder."),
            "max_tokens" => L.T("Se alcanzó el límite de tokens de la respuesta."),
            "max_turn_requests" => L.T("Se alcanzó el límite de pasos del turno."),
            "cancelled" => L.T("Se interrumpió."),
            _ => null,
        };
        Add(new AgentBlock { Kind = AgentBlockKind.Done, Duration = duration, Cost = _acpCost, Failed = problem is not null, Text = problem ?? "" }, changed);
    }

    /// <summary>El turno terminó con un error del agente (la respuesta a session/prompt fue un error).</summary>
    internal void AcpFailed(string message, TimeSpan duration, List<AgentBlock> changed)
    {
        Close(changed);
        Busy = false;
        Add(new AgentBlock { Kind = AgentBlockKind.Done, Duration = duration, Cost = _acpCost, Failed = true, Text = message }, changed);
    }

    /// <summary>
    /// El cambio por líneas entre el texto de antes y el de después, con «+», «-» o « » adelante: lo igual del principio y
    /// del final se salta (con hasta tres líneas de contexto) y lo del medio se compara línea a línea si no es enorme.
    /// Gemini CLI manda el archivo entero; Copilot CLI, solo el trozo que cambia.
    /// </summary>
    internal static List<string> AcpDiff(string? oldText, string newText)
    {
        var a = AcpLines(oldText);
        var b = AcpLines(newText);
        var start = 0;
        while (start < a.Length && start < b.Length && a[start] == b[start]) start++;
        var end = 0;
        while (end < a.Length - start && end < b.Length - start && a[^(end + 1)] == b[^(end + 1)]) end++;
        var lines = new List<string>();
        if (start == a.Length && start == b.Length) return lines;
        for (var i = Math.Max(0, start - 3); i < start; i++) lines.Add(" " + a[i]);
        var oldMiddle = a[start..(a.Length - end)];
        var newMiddle = b[start..(b.Length - end)];
        if ((long)oldMiddle.Length * newMiddle.Length <= AcpDiffCells) lines.AddRange(AcpCompare(oldMiddle, newMiddle));
        else
        {
            lines.AddRange(oldMiddle.Select(l => "-" + l));
            lines.AddRange(newMiddle.Select(l => "+" + l));
        }
        for (var i = a.Length - end; i < Math.Min(a.Length, a.Length - end + 3); i++) lines.Add(" " + a[i]);
        return lines;
    }

    static string[] AcpLines(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        return lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    /// <summary>Línea a línea por la subsecuencia común más larga: lo común con « », lo que salió con «-» y lo nuevo con «+».</summary>
    static IEnumerable<string> AcpCompare(string[] a, string[] b)
    {
        var common = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                common[i, j] = a[i] == b[j] ? common[i + 1, j + 1] + 1 : Math.Max(common[i + 1, j], common[i, j + 1]);
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y])
            {
                yield return " " + a[x];
                x++;
                y++;
            }
            else if (common[x + 1, y] >= common[x, y + 1]) yield return "-" + a[x++];
            else yield return "+" + b[y++];
        }
        while (x < a.Length) yield return "-" + a[x++];
        while (y < b.Length) yield return "+" + b[y++];
    }
}
