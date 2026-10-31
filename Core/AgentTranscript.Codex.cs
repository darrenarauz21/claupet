using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>
/// Codex en bloques: lee las notificaciones y los pedidos de «codex app-server» (JSON-RPC 2.0, v2) y arma los mismos
/// bloques que con Claude Code. CodexDriver reparte cada mensaje; aquí solo se cambia la transcripción.
/// </summary>
sealed partial class AgentTranscript
{
    /// <summary>Lo que se guarda de la salida de un comando mientras corre (sus primeras líneas son las que se ven).</summary>
    const int CodexOutputChars = 16_000;

    /// <summary>Los pedidos del servidor que esperan respuesta: el método y las decisiones que ofrece (availableDecisions).</summary>
    readonly Dictionary<string, (string Method, JsonNode? Decisions)> _codexRequests = [];
    readonly Dictionary<string, StringBuilder> _codexOutput = [];
    /// <summary>Por paso que edita: los archivos y sus cambios (para el permiso, que solo trae el id del paso).</summary>
    readonly Dictionary<string, JsonNode> _codexChanges = [];
    /// <summary>El aviso de «reconectando…» del turno, que se actualiza en vez de repetirse.</summary>
    AgentBlock? _codexRetry;
    string? _codexError;
    long _codexTotal, _codexTurnStart;

    /// <summary>Cómo pidió el servidor ese permiso, si todavía espera respuesta.</summary>
    internal (string Method, JsonNode? Decisions)? CodexPending(string? requestId) =>
        requestId is not null && _codexRequests.TryGetValue(requestId, out var pending) ? pending : null;

    /// <summary>La sesión quedó abierta (respuesta de thread/start): su id, el modelo, la carpeta y el modo.</summary>
    internal void CodexThread(JsonElement result, string mode)
    {
        if (result.TryGetProperty("thread", out var thread)) SessionId = Json.Str(thread, "id") ?? SessionId;
        Model = Json.Str(result, "model") ?? Model;
        Cwd = Json.Str(result, "cwd") ?? Cwd;
        PermissionMode = mode;
    }

    internal void CodexModel(string? model) => Model = model ?? Model;
    internal void CodexMode(string mode) => PermissionMode = mode;

    /// <summary>Empieza un turno que estaba en cola (el anterior ya cerró).</summary>
    internal void CodexBusy() => Busy = true;

    /// <summary>Se cortó antes de empezar (la sesión todavía no abría): ya no hay turno.</summary>
    internal void CodexIdle() => Busy = false;

    /// <summary>Un error de un pedido de la app (thread/start, turn/start…): queda como aviso.</summary>
    internal void CodexFailure(string text, bool turn, List<AgentBlock> changed)
    {
        Close(changed);
        if (turn) Busy = false;
        Add(new AgentBlock { Kind = AgentBlockKind.Notice, Text = text }, changed);
    }

    /// <summary>Una notificación del servidor.</summary>
    internal void CodexNotification(string method, JsonElement p, List<AgentBlock> changed)
    {
        switch (method)
        {
            case "turn/started":
                Busy = true;
                _codexTurnStart = _codexTotal;
                _codexRetry = null;
                _codexError = null;
                break;
            case "item/started" when p.TryGetProperty("item", out var item):
                CodexItem(item, false, changed);
                break;
            case "item/completed" when p.TryGetProperty("item", out var item):
                CodexItem(item, true, changed);
                break;
            case "item/agentMessage/delta" when Json.Str(p, "itemId") is { } id && Json.Str(p, "delta") is { } piece:
                var say = _blocks.LastOrDefault(b => b.Kind == AgentBlockKind.Say && b.Id == id);
                if (say is null)
                {
                    Close(changed);
                    say = Add(new AgentBlock { Kind = AgentBlockKind.Say, Id = id, Open = true }, changed);
                }
                say.Text += piece;
                Touch(say, changed);
                break;
            case "item/reasoning/summaryTextDelta" or "item/reasoning/textDelta" when Json.Str(p, "itemId") is { } id && Json.Str(p, "delta") is { } piece:
                var think = CodexThink(id, changed);
                think.Text += piece;
                Touch(think, changed);
                break;
            case "item/commandExecution/outputDelta" when Json.Str(p, "itemId") is { } id && Json.Str(p, "delta") is { } piece
                && Step(id) is { } running:
                if (!_codexOutput.TryGetValue(id, out var output)) _codexOutput[id] = output = new StringBuilder();
                if (output.Length < CodexOutputChars) output.Append(piece);
                running.Output = FirstLines(output.ToString());
                Touch(running, changed);
                break;
            case "item/fileChange/patchUpdated" when Json.Str(p, "itemId") is { } id && Step(id) is { } editing
                && p.TryGetProperty("changes", out var changes):
                CodexChanges(editing, changes);
                Touch(editing, changed);
                break;
            case "turn/plan/updated" when p.TryGetProperty("plan", out var plan) && plan.ValueKind == JsonValueKind.Array:
                var todos = new JsonArray();
                foreach (var step in plan.EnumerateArray())
                    todos.Add(new JsonObject
                    {
                        ["content"] = Json.Str(step, "step") ?? "",
                        ["status"] = Json.Str(step, "status") switch { "inProgress" => "in_progress", "completed" => "completed", _ => "pending" },
                    });
                Todos(CodexElement(new JsonObject { ["todos"] = todos }), changed);
                break;
            case "thread/tokenUsage/updated" when p.TryGetProperty("tokenUsage", out var usage):
                CodexUsage(usage, changed);
                break;
            case "account/rateLimits/updated" when p.TryGetProperty("rateLimits", out var limits):
                CodexLimits(limits);
                break;
            case "model/rerouted":
                Model = Json.Str(p, "toModel") ?? Model;
                break;
            case "serverRequest/resolved" when p.TryGetProperty("requestId", out var resolved):
                // Si el servidor lo retiró sin respuesta (el turno se cortó), el bloque queda cerrado.
                var key = resolved.GetRawText();
                _codexRequests.Remove(key);
                if (_blocks.LastOrDefault(b => b.Kind == AgentBlockKind.Permission && b.RequestId == key) is { Answer: null } withdrawn)
                {
                    withdrawn.Answer = PermissionAnswer.Deny;
                    Touch(withdrawn, changed);
                }
                break;
            case "error" when p.TryGetProperty("error", out var error):
                CodexErrorNotice(error, p.TryGetProperty("willRetry", out var retry) && retry.ValueKind == JsonValueKind.True, changed);
                break;
            case "configWarning" or "deprecationNotice" when Json.Str(p, "summary") is { Length: > 0 } summary:
                var details = Json.Str(p, "details");
                Add(new AgentBlock { Kind = AgentBlockKind.Notice, Text = string.IsNullOrEmpty(details) ? summary : summary + " · " + details }, changed);
                break;
            case "turn/completed" when p.TryGetProperty("turn", out var turn):
                CodexTurnDone(turn, changed);
                break;
        }
    }

    /// <summary>
    /// Un pedido del servidor que espera respuesta (un permiso o una pregunta). Devuelve false si no es de los que se
    /// muestran: CodexDriver lo contesta solo.
    /// </summary>
    internal bool CodexRequest(string method, string requestId, JsonElement p, List<AgentBlock> changed)
    {
        var stepId = Json.Str(p, "itemId");
        AgentBlock permission;
        switch (method)
        {
            case "item/commandExecution/requestApproval":
            {
                var command = CodexCommandText(Json.Str(p, "command") ?? "", p);
                var host = p.TryGetProperty("networkApprovalContext", out var network) && network.ValueKind == JsonValueKind.Object
                    ? Json.Str(network, "host") : null;
                var input = new JsonObject { ["command"] = command };
                if (Json.Str(p, "reason") is { Length: > 0 } reason) input["description"] = reason;
                var target = host ?? AgentSteps.Target("Bash", CodexElement(input), Cwd);
                permission = new AgentBlock
                {
                    Kind = AgentBlockKind.Permission,
                    Id = requestId,
                    RequestId = requestId,
                    StepId = stepId,
                    Tool = "Bash",
                    Text = target.Length > 0 ? target : Json.Str(p, "reason") ?? "",
                    Input = input,
                };
                break;
            }
            case "item/fileChange/requestApproval":
            {
                var step = Step(stepId);
                var input = CodexEditInput(stepId);
                if (Json.Str(p, "reason") is { Length: > 0 } reason) input["description"] = reason;
                permission = new AgentBlock
                {
                    Kind = AgentBlockKind.Permission,
                    Id = requestId,
                    RequestId = requestId,
                    StepId = stepId,
                    Tool = step?.Tool ?? "Edit",
                    Text = Json.Str(p, "grantRoot") ?? step?.Text ?? Json.Str(p, "reason") ?? "",
                    Input = input,
                };
                break;
            }
            case "item/tool/requestUserInput" when p.TryGetProperty("questions", out var list) && list.ValueKind == JsonValueKind.Array:
            {
                // Las preguntas con la forma de AskUserQuestion (question, options, multiSelect), más su id para responder.
                var questions = new JsonArray();
                foreach (var q in list.EnumerateArray())
                {
                    var options = new JsonArray();
                    if (q.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Array)
                        foreach (var option in o.EnumerateArray())
                            options.Add(new JsonObject { ["label"] = Json.Str(option, "label") ?? "", ["description"] = Json.Str(option, "description") ?? "" });
                    questions.Add(new JsonObject
                    {
                        ["id"] = Json.Str(q, "id") ?? "",
                        ["header"] = Json.Str(q, "header") ?? "",
                        ["question"] = Json.Str(q, "question") ?? "",
                        ["options"] = options,
                        ["multiSelect"] = false,
                    });
                }
                var input = new JsonObject { ["questions"] = questions };
                var element = CodexElement(input);
                permission = new AgentBlock
                {
                    Kind = AgentBlockKind.Permission,
                    Id = requestId,
                    RequestId = requestId,
                    StepId = stepId,
                    Tool = "AskUserQuestion",
                    Text = AgentSteps.Target("AskUserQuestion", element, Cwd),
                    Input = input,
                    Questions = Approvals.QuestionsOf(element),
                };
                break;
            }
            default:
                return false;
        }
        Close(changed);
        if (Step(stepId) is { } waiting)
        {
            waiting.State = StepState.Waiting;
            Touch(waiting, changed);
        }
        _codexRequests[requestId] = (method, p.TryGetProperty("availableDecisions", out var decisions) ? Node(decisions) : null);
        Add(permission, changed);
        return true;
    }

    void CodexItem(JsonElement item, bool completed, List<AgentBlock> changed)
    {
        if (Json.Str(item, "id") is not { } id) return;
        switch (Json.Str(item, "type"))
        {
            case "agentMessage":
            {
                var say = _blocks.LastOrDefault(b => b.Kind == AgentBlockKind.Say && b.Id == id);
                if (say is null)
                {
                    Close(changed);
                    say = Add(new AgentBlock { Kind = AgentBlockKind.Say, Id = id, Open = true }, changed);
                }
                // Lo completo reemplaza a lo que se armó por partes.
                if (completed)
                {
                    say.Text = Json.Str(item, "text") ?? say.Text;
                    say.Open = false;
                }
                Touch(say, changed);
                break;
            }
            case "reasoning":
            {
                var think = CodexThink(id, changed);
                if (completed)
                {
                    if (item.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.Array && summary.GetArrayLength() > 0)
                        think.Text = string.Join("\n\n", summary.EnumerateArray().Select(s => s.GetString()).OfType<string>());
                    think.Open = false;
                }
                Touch(think, changed);
                break;
            }
            case "commandExecution":
            {
                var step = Step(id);
                if (step is null)
                {
                    Close(changed);
                    var (tool, target) = CodexCommandStep(item);
                    step = Add(new AgentBlock { Kind = AgentBlockKind.Step, Id = id, Tool = tool, Text = target, State = StepState.Running }, changed);
                }
                if (completed)
                {
                    // En Windows el código puede no caber en un int (0xC0000005…).
                    var exit = item.TryGetProperty("exitCode", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt64(out var n) ? n : 0;
                    CodexState(step, Json.Str(item, "status"), exit != 0);
                    var output = Json.Str(item, "aggregatedOutput") ?? (_codexOutput.TryGetValue(id, out var partial) ? partial.ToString() : "");
                    step.Output = FirstLines(output);
                    _codexOutput.Remove(id);
                }
                Touch(step, changed);
                break;
            }
            case "fileChange":
            {
                var step = Step(id);
                item.TryGetProperty("changes", out var changes);
                if (step is null)
                {
                    Close(changed);
                    var first = changes.ValueKind == JsonValueKind.Array ? changes.EnumerateArray().FirstOrDefault() : default;
                    var kind = first.ValueKind == JsonValueKind.Object && first.TryGetProperty("kind", out var k) ? Json.Str(k, "type") : null;
                    var count = changes.ValueKind == JsonValueKind.Array ? changes.GetArrayLength() : 0;
                    var target = first.ValueKind == JsonValueKind.Object
                        ? AgentSteps.Target("Edit", CodexElement(new JsonObject { ["file_path"] = Json.Str(first, "path") ?? "" }), Cwd) : "";
                    if (count > 1) target += " " + L.T("y {0} más", count - 1);
                    step = Add(new AgentBlock { Kind = AgentBlockKind.Step, Id = id, Tool = kind == "add" ? "Write" : "Edit", Text = target, State = StepState.Running }, changed);
                }
                if (changes.ValueKind == JsonValueKind.Array) CodexChanges(step, changes);
                if (completed) CodexState(step, Json.Str(item, "status"), false);
                Touch(step, changed);
                break;
            }
            case "mcpToolCall":
            {
                var step = Step(id);
                if (step is null)
                {
                    Close(changed);
                    item.TryGetProperty("arguments", out var arguments);
                    var tool = $"mcp__{Json.Str(item, "server")}__{Json.Str(item, "tool")}";
                    step = Add(new AgentBlock { Kind = AgentBlockKind.Step, Id = id, Tool = tool, Text = AgentSteps.Target(tool, arguments, Cwd), State = StepState.Running }, changed);
                }
                if (completed)
                {
                    CodexState(step, Json.Str(item, "status"), false);
                    if (item.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object) step.Output = FirstLines(Json.Str(error, "message") ?? "");
                    else if (item.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object) step.Output = FirstLines(ResultText(result));
                }
                Touch(step, changed);
                break;
            }
            case "webSearch":
            {
                var step = Step(id);
                if (step is null)
                {
                    Close(changed);
                    step = Add(new AgentBlock { Kind = AgentBlockKind.Step, Id = id, Tool = "WebSearch", State = StepState.Running }, changed);
                }
                if (Json.Str(item, "query") is { Length: > 0 } query) step.Text = query;
                if (completed) step.State = StepState.Done;
                Touch(step, changed);
                break;
            }
        }
    }

    AgentBlock CodexThink(string id, List<AgentBlock> changed)
    {
        if (_blocks.LastOrDefault(b => b.Kind == AgentBlockKind.Think && b.Id == id) is { } think) return think;
        Close(changed);
        return Add(new AgentBlock { Kind = AgentBlockKind.Think, Id = id, Open = true }, changed);
    }

    /// <summary>El estado del paso según el de Codex: inProgress, completed, failed o declined.</summary>
    static void CodexState(AgentBlock step, string? status, bool failed)
    {
        if (step.State == StepState.Denied) return;
        step.State = status switch
        {
            "declined" => StepState.Denied,
            "failed" => StepState.Failed,
            "inProgress" => step.State,
            _ => failed ? StepState.Failed : StepState.Done,
        };
    }

    /// <summary>
    /// Cómo se nombra un comando: si Codex lo entendió como una lectura, un listado o una búsqueda, con ese verbo; si no,
    /// «ejecutó» y el comando (sin el shell que lo envuelve).
    /// </summary>
    (string Tool, string Target) CodexCommandStep(JsonElement item)
    {
        if (item.TryGetProperty("commandActions", out var actions) && actions.ValueKind == JsonValueKind.Array && actions.GetArrayLength() == 1)
        {
            var action = actions[0];
            switch (Json.Str(action, "type"))
            {
                case "read" when Json.Str(action, "path") is { } path:
                    return ("Read", AgentSteps.Target("Read", CodexElement(new JsonObject { ["file_path"] = path }), Cwd));
                case "search" when (Json.Str(action, "query") ?? Json.Str(action, "path")) is { } query:
                    return ("Grep", query);
                case "listFiles":
                    return ("Glob", Json.Str(action, "path") ?? ".");
            }
        }
        var command = CodexCommandText(Json.Str(item, "command") ?? "", item);
        return ("Bash", AgentSteps.Target("Bash", CodexElement(new JsonObject { ["command"] = command }), Cwd));
    }

    /// <summary>
    /// El comando que pidió el modelo: Codex lo manda envuelto en el shell («"…powershell.exe" -Command '…'», «bash -lc '…'»).
    /// Con una sola acción, su texto ya viene sin envolver; si no, se quita el envoltorio.
    /// </summary>
    static string CodexCommandText(string command, JsonElement owner)
    {
        if (owner.TryGetProperty("commandActions", out var actions) && actions.ValueKind == JsonValueKind.Array && actions.GetArrayLength() == 1
            && Json.Str(actions[0], "command") is { Length: > 0 } single)
            return single;
        var match = CodexShell().Match(command);
        if (!match.Success) return command;
        var inner = match.Groups["inner"].Value.Trim();
        if (inner.Length >= 2 && inner[0] == '\'' && inner[^1] == '\'') return inner[1..^1].Replace("'\\''", "'").Replace("''", "'");
        if (inner.Length >= 2 && inner[0] == '"' && inner[^1] == '"') return inner[1..^1];
        return inner;
    }

    [GeneratedRegex("""^(?:"[^"]*(?:powershell|pwsh|bash|zsh|sh|cmd)(?:\.exe)?"|\S*(?:powershell|pwsh|bash|zsh|sh|cmd)(?:\.exe)?)\s+(?:-NoProfile\s+|-NoLogo\s+)*(?:-Command|-lc|-c|/c)\s+(?<inner>.+)$""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CodexShell();

    /// <summary>Lo que cambia un paso que edita: el contenido nuevo («add»), lo que se borra («delete») o el parche unificado.</summary>
    void CodexChanges(AgentBlock step, JsonElement changes)
    {
        if (changes.ValueKind != JsonValueKind.Array) return;
        var lines = new List<string>();
        foreach (var change in changes.EnumerateArray())
        {
            var kind = change.TryGetProperty("kind", out var k) ? Json.Str(k, "type") : null;
            var diff = (Json.Str(change, "diff") ?? "").ReplaceLineEndings("\n").TrimEnd('\n');
            if (diff.Length == 0) continue;
            var split = diff.Split('\n');
            lines.AddRange(kind switch
            {
                "add" => split.Select(l => "+" + l),
                "delete" => split.Select(l => "-" + l),
                _ => split.Where(l => !l.StartsWith("@@", StringComparison.Ordinal) && !l.StartsWith("---", StringComparison.Ordinal)
                    && !l.StartsWith("+++", StringComparison.Ordinal)),
            });
        }
        _codexChanges[step.Id] = Node(changes)!;
        if (lines.Count == 0) return;
        step.Added = lines.Count(l => l.StartsWith('+'));
        step.Removed = lines.Count(l => l.StartsWith('-'));
        step.Diff = lines.Take(DiffLines).ToList();
    }

    /// <summary>El pedido de un paso que edita con la forma de Write o Edit (file_path, content o old_string/new_string), para la isla.</summary>
    JsonObject CodexEditInput(string? stepId)
    {
        var input = new JsonObject();
        if (stepId is null || !_codexChanges.TryGetValue(stepId, out var changes) || changes is not JsonArray list || list.Count == 0) return input;
        var first = list[0]!;
        input["file_path"] = (string?)first["path"] ?? "";
        var diff = ((string?)first["diff"] ?? "").ReplaceLineEndings("\n");
        if ((string?)first["kind"]?["type"] == "add") input["content"] = diff;
        else
        {
            var lines = diff.Split('\n');
            input["old_string"] = string.Join("\n", lines.Where(l => l.StartsWith('-') && !l.StartsWith("---", StringComparison.Ordinal)).Select(l => l[1..]));
            input["new_string"] = string.Join("\n", lines.Where(l => l.StartsWith('+') && !l.StartsWith("+++", StringComparison.Ordinal)).Select(l => l[1..]));
        }
        if (list.Count > 1) input["description"] = L.T("y {0} más", list.Count - 1);
        return input;
    }

    void CodexUsage(JsonElement usage, List<AgentBlock> changed)
    {
        if (usage.TryGetProperty("total", out var total)) _codexTotal = (long)Json.Num(total, "totalTokens");
        if (usage.TryGetProperty("last", out var last))
        {
            var used = (long)Json.Num(last, "totalTokens");
            if (used > 0) ContextUsed = used;
            // Lo que pensó en esa respuesta, en el último bloque de pensamiento que todavía no tiene cuenta.
            var reasoning = (long)Json.Num(last, "reasoningOutputTokens");
            var lastAsk = _blocks.FindLastIndex(b => b.Kind == AgentBlockKind.Ask);
            if (reasoning > 0 && _blocks.Skip(lastAsk + 1).LastOrDefault(b => b.Kind == AgentBlockKind.Think) is { Tokens: 0 } think)
            {
                think.Tokens = reasoning;
                Touch(think, changed);
            }
        }
        var window = (long)Json.Num(usage, "modelContextWindow");
        if (window > 0) ContextWindow = window;
    }

    /// <summary>El uso de la cuenta: la ventana de 5 horas y la semanal (por su duración; si no la trae, primaria y secundaria).</summary>
    void CodexLimits(JsonElement limits)
    {
        foreach (var (name, fallback) in new[] { ("primary", 300.0), ("secondary", 10080.0) })
        {
            if (!limits.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object
                || !window.TryGetProperty("usedPercent", out var used) || used.ValueKind != JsonValueKind.Number) continue;
            var minutes = window.TryGetProperty("windowDurationMins", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetDouble() : fallback;
            if (minutes <= 300) FiveHour = used.GetDouble();
            else SevenDay = used.GetDouble();
        }
    }

    void CodexErrorNotice(JsonElement error, bool willRetry, List<AgentBlock> changed)
    {
        var message = Json.Str(error, "message") ?? "";
        var details = Json.Str(error, "additionalDetails");
        var text = string.IsNullOrEmpty(details) ? message : message + " · " + details;
        if (text.Length == 0) return;
        if (willRetry)
        {
            // «Reconnecting... 2/5»: un solo aviso por turno, con el último intento.
            _codexRetry ??= Add(new AgentBlock { Kind = AgentBlockKind.Notice }, changed);
            _codexRetry.Text = text;
            Touch(_codexRetry, changed);
            return;
        }
        _codexError = message;
        // En un turno, el error va en su cierre; fuera de uno, como aviso.
        if (!Busy) Add(new AgentBlock { Kind = AgentBlockKind.Notice, Text = text }, changed);
    }

    void CodexTurnDone(JsonElement turn, List<AgentBlock> changed)
    {
        Close(changed);
        Busy = false;
        _codexRetry = null;
        var status = Json.Str(turn, "status");
        var failed = status != "completed";
        // Lo que quedó a medias ya no corre: Codex no avisó que terminara (el turno se cortó o el paso se colgó).
        foreach (var step in _blocks.Where(b => b.Kind == AgentBlockKind.Step && b.State is StepState.Running or StepState.Waiting))
        {
            step.State = StepState.Failed;
            Touch(step, changed);
        }
        var error = turn.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object ? Json.Str(e, "message") : null;
        Add(new AgentBlock
        {
            Kind = AgentBlockKind.Done,
            Duration = TimeSpan.FromMilliseconds(Json.Num(turn, "durationMs")),
            Tokens = Math.Max(0, _codexTotal - _codexTurnStart),
            Failed = failed,
            Text = !failed ? "" : error ?? _codexError ?? (status == "interrupted" ? L.T("Se interrumpió.") : ""),
        }, changed);
        _codexTurnStart = _codexTotal;
        _codexError = null;
    }

    static JsonElement CodexElement(JsonNode node)
    {
        using var doc = JsonDocument.Parse(node.ToJsonString());
        return doc.RootElement.Clone();
    }
}
