using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet;

/// <summary>
/// Codex por «codex app-server»: JSON-RPC 2.0 por la entrada y la salida, una línea por mensaje (protocolo v2, el que
/// describe «codex app-server generate-ts»). Al abrir: initialize, model/list y thread/start; cada pedido es un
/// turn/start. Los pedidos que llegan antes de que exista la sesión, o mientras corre un turno, se guardan y salen en
/// orden. El modelo, el esfuerzo y el modo se aplican desde el turno siguiente (van en turn/start).
/// Los modos usan los mismos ids que Claude Code:
/// default = pregunta antes (approvalPolicy untrusted, sandbox workspace-write);
/// acceptEdits = edita y ejecuta dentro de la carpeta sin preguntar, pregunta para salir de ella (on-request, workspace-write);
/// plan = el modo plan de Codex, que solo lee y puede preguntar (collaborationMode plan, on-request, read-only);
/// auto = nunca pregunta, siempre dentro de la carpeta (never, workspace-write).
/// </summary>
sealed class CodexDriver(AgentTranscript transcript) : IAgentDriver
{
    /// <summary>Los esfuerzos si todavía no se sabe cuáles admite el modelo.</summary>
    static readonly string[] DefaultEfforts = ["minimal", "low", "medium", "high"];
    static readonly string[] CodexModes = ["default", "acceptEdits", "plan", "auto"];

    readonly Dictionary<string, string> _pending = [];
    readonly Queue<string> _queue = new();
    readonly List<(string Id, IReadOnlyList<string> Efforts, bool IsDefault)> _models = [];
    AgentOptions? _options;
    int _requests;
    string? _thread, _turn;
    /// <summary>Se mandó un turn/start y su turno todavía no terminó.</summary>
    bool _active;
    bool _interruptPending;
    string? _model, _effort;
    string _mode = "default";
    bool _modelChanged, _modeChanged;
    /// <summary>El último turno salió en el modo plan de Codex (collaborationMode).</summary>
    bool _planned;
    /// <summary>El sandbox que Codex dio al abrir en un modo que escribe (si se abrió en plan, no se sabe).</summary>
    JsonNode? _writeSandbox;

    /// <summary>El ejecutable de Codex: el del PATH o, si no está, el que trae la extensión de Codex para VS Code (la más nueva).</summary>
    public static string? Find() => AgentDriver.OnPath("codex") ?? FromEditors();

    static string? FromEditors()
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var system = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
            var arm = RuntimeInformation.OSArchitecture == Architecture.Arm64;
            var name = OperatingSystem.IsWindows() ? "codex.exe" : "codex";
            var extensions = new[] { ".vscode", ".vscode-insiders", ".cursor", ".windsurf" }
                .Select(editor => Path.Combine(home, editor, "extensions"))
                .Where(Directory.Exists)
                .SelectMany(dir => Directory.EnumerateDirectories(dir, "openai.chatgpt-*"))
                .OrderByDescending(ExtensionVersion);
            foreach (var extension in extensions)
            {
                var bin = Path.Combine(extension, "bin");
                if (!Directory.Exists(bin)) continue;
                // bin/windows-x86_64, bin/linux-x86_64 (para WSL), bin/macos-aarch64…: solo la de este sistema.
                foreach (var platform in Directory.EnumerateDirectories(bin))
                {
                    var folder = Path.GetFileName(platform).ToLowerInvariant();
                    var sameSystem = folder.StartsWith(system, StringComparison.Ordinal) || (system == "macos" && folder.StartsWith("darwin", StringComparison.Ordinal));
                    var sameArch = arm ? folder.Contains("aarch64") || folder.Contains("arm64") : folder.Contains("x86_64") || folder.Contains("x64");
                    var file = Path.Combine(platform, name);
                    if (sameSystem && sameArch && File.Exists(file)) return file;
                }
            }
        }
        catch (Exception) { }
        return null;
    }

    /// <summary>La versión de la carpeta de la extensión («openai.chatgpt-26.1021.22027-win32-x64»).</summary>
    static Version ExtensionVersion(string folder)
    {
        var name = Path.GetFileName(folder)["openai.chatgpt-".Length..];
        return Version.TryParse(name.Split('-')[0], out var version) ? version : new Version(0, 0);
    }

    public string Name => "Codex";
    public IReadOnlyList<string> Models => [.. _models.Select(m => m.Id)];
    public IReadOnlyList<string> Modes => CodexModes;

    /// <summary>Los esfuerzos del modelo elegido (o el de la sesión), según model/list.</summary>
    public IReadOnlyList<string> Efforts => ModelInfo(_model ?? transcript.Model) is { } model && model.Efforts.Count > 0 ? model.Efforts : DefaultEfforts;

    public ProcessStartInfo? Start(AgentOptions options)
    {
        if (Find() is not { } codex) return null;
        var start = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        if (codex.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || codex.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            // Instalado con npm: codex.cmd va por cmd.exe.
            start.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            foreach (var arg in new[] { "/d", "/s", "/c", codex }) start.ArgumentList.Add(arg);
        }
        else start.FileName = codex;
        start.ArgumentList.Add("app-server");
        return start;
    }

    public IEnumerable<string> Opening(AgentOptions options)
    {
        _options = options;
        _model = options.Model;
        _effort = options.Effort;
        _mode = options.PermissionMode is { } mode && CodexModes.Contains(mode) ? mode : "default";
        var version = typeof(CodexDriver).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        return [Request("initialize", new JsonObject
        {
            ["clientInfo"] = new JsonObject { ["name"] = "claupet", ["title"] = "Claupet", ["version"] = version },
            // Lo experimental hace falta para el modo plan (collaborationMode).
            ["capabilities"] = new JsonObject { ["experimentalApi"] = true, ["requestAttestation"] = false },
        })];
    }

    public IReadOnlyList<AgentBlock> Apply(string line, List<string> replies)
    {
        if (line.Length == 0 || line[0] != '{') return [];
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            var changed = new List<AgentBlock>();
            var method = Json.Str(r, "method");
            r.TryGetProperty("params", out var p);
            var hasId = r.TryGetProperty("id", out var id);
            if (method is null && hasId) Response(id.GetRawText(), r, replies, changed);
            else if (method is not null && hasId) ServerRequest(method, id.GetRawText(), p, replies, changed);
            else if (method is not null) Notification(method, p, replies, changed);
            return changed;
        }
        catch (JsonException) { return []; }
        catch (InvalidOperationException) { return []; }
    }

    /// <summary>La respuesta a un pedido de la app.</summary>
    void Response(string id, JsonElement r, List<string> replies, List<AgentBlock> changed)
    {
        if (!_pending.Remove(id, out var method)) return;
        if (r.TryGetProperty("error", out var error))
        {
            var message = Json.Str(error, "message") ?? L.T("Codex respondió con un error.");
            switch (method)
            {
                case "model/list":
                    // Sin la lista se abre igual, con el modelo de la configuración de Codex.
                    replies.Add(ThreadStart());
                    break;
                case "turn/start":
                    _active = false;
                    transcript.CodexFailure(message, true, changed);
                    Next(replies);
                    break;
                case "turn/interrupt":
                    break;
                default:
                    // initialize o thread/start: la sesión no se puede usar; lo que estaba en cola no sale.
                    _queue.Clear();
                    transcript.CodexFailure(L.T("No se pudo abrir la sesión de Codex: {0}", message), true, changed);
                    break;
            }
            return;
        }
        r.TryGetProperty("result", out var result);
        switch (method)
        {
            case "initialize":
                replies.Add(Line(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "initialized" }));
                replies.Add(Request("model/list", new JsonObject()));
                break;
            case "model/list":
                if (result.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                    foreach (var model in data.EnumerateArray())
                    {
                        if (Json.Str(model, "id") is not { } modelId || (model.TryGetProperty("hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True)) continue;
                        var efforts = model.TryGetProperty("supportedReasoningEfforts", out var list) && list.ValueKind == JsonValueKind.Array
                            ? list.EnumerateArray().Select(e => Json.Str(e, "reasoningEffort")).OfType<string>().ToList() : [];
                        _models.Add((modelId, efforts, model.TryGetProperty("isDefault", out var d) && d.ValueKind == JsonValueKind.True));
                    }
                if (Json.Str(result, "nextCursor") is { Length: > 0 } cursor) replies.Add(Request("model/list", new JsonObject { ["cursor"] = cursor }));
                else replies.Add(ThreadStart());
                break;
            case "thread/start":
                _thread = result.TryGetProperty("thread", out var thread) ? Json.Str(thread, "id") : null;
                transcript.CodexThread(result, _mode);
                if (_mode != "plan" && result.TryGetProperty("sandbox", out var sandbox) && sandbox.ValueKind == JsonValueKind.Object)
                    _writeSandbox = JsonNode.Parse(sandbox.GetRawText());
                // Lo que se eligió al abrir ya va en thread/start.
                _modelChanged = _modeChanged = false;
                Next(replies);
                break;
            case "turn/start":
                if (result.TryGetProperty("turn", out var turn)) _turn = Json.Str(turn, "id") ?? _turn;
                if (_interruptPending && _turn is not null)
                {
                    _interruptPending = false;
                    replies.AddRange(Interrupt());
                }
                break;
        }
    }

    void Notification(string method, JsonElement p, List<string> replies, List<AgentBlock> changed)
    {
        if (p.ValueKind != JsonValueKind.Object) return;
        // Solo lo de esta sesión (los subagentes de Codex abren otras).
        if (Json.Str(p, "threadId") is { } thread && _thread is not null && thread != _thread) return;
        switch (method)
        {
            case "turn/started" when p.TryGetProperty("turn", out var started):
                _turn = Json.Str(started, "id") ?? _turn;
                break;
            case "turn/completed":
                transcript.CodexNotification(method, p, changed);
                _turn = null;
                _active = false;
                _interruptPending = false;
                Next(replies);
                return;
        }
        transcript.CodexNotification(method, p, changed);
    }

    void ServerRequest(string method, string id, JsonElement p, List<string> replies, List<AgentBlock> changed)
    {
        if (p.ValueKind == JsonValueKind.Object && transcript.CodexRequest(method, id, p, changed)) return;
        // Lo que no se muestra se contesta enseguida, para que el turno no quede esperando.
        replies.Add(method switch
        {
            "mcpServer/elicitation/request" => Reply(id, new JsonObject { ["action"] = "decline", ["content"] = null, ["_meta"] = null }),
            "item/permissions/requestApproval" => Reply(id, new JsonObject { ["permissions"] = new JsonObject(), ["scope"] = "turn" }),
            _ => Line(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = JsonNode.Parse(id),
                ["error"] = new JsonObject { ["code"] = -32601, ["message"] = $"Claupet does not handle {method}" },
            }),
        });
    }

    public IEnumerable<string> Prompt(string text)
    {
        _queue.Enqueue(text);
        var lines = new List<string>();
        Next(lines);
        return lines;
    }

    /// <summary>Si la sesión ya existe y no corre un turno, manda el siguiente pedido de la cola.</summary>
    void Next(List<string> lines)
    {
        if (_thread is null || _active || !_queue.TryDequeue(out var text)) return;
        _active = true;
        transcript.CodexBusy();
        var turn = new JsonObject
        {
            ["threadId"] = _thread,
            ["input"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text, ["text_elements"] = new JsonArray() }),
        };
        if (_modelChanged)
        {
            _modelChanged = false;
            var model = _model ?? _models.FirstOrDefault(m => m.IsDefault).Id;
            if (model is not null)
            {
                turn["model"] = model;
                transcript.CodexModel(model);
            }
        }
        if (Effort(_model ?? transcript.Model) is { } effort) turn["effort"] = effort;
        if (_modeChanged)
        {
            _modeChanged = false;
            turn["approvalPolicy"] = Policy(_mode);
            // Para escribir se vuelve al sandbox que Codex dio al abrir: en Windows sin su sandbox, baja workspace-write a
            // read-only, y pedir workspaceWrite en el turno lo deja colgado al aplicar un parche (probado con 0.164).
            if (_mode == "plan") turn["sandboxPolicy"] = new JsonObject { ["type"] = "readOnly", ["networkAccess"] = false };
            else if (_writeSandbox is not null) turn["sandboxPolicy"] = _writeSandbox.DeepClone();
        }
        // El modo plan de Codex (experimental): solo planifica y puede preguntar (request_user_input). Va en cada turno
        // porque manda sobre el modelo y el esfuerzo; al salir de plan se vuelve una vez al modo normal.
        if ((_mode == "plan" || _planned) && (_model ?? transcript.Model) is { } current)
        {
            turn["collaborationMode"] = new JsonObject
            {
                ["mode"] = _mode == "plan" ? "plan" : "default",
                ["settings"] = new JsonObject { ["model"] = current, ["reasoning_effort"] = Effort(current), ["developer_instructions"] = null },
            };
            _planned = _mode == "plan";
        }
        lines.Add(Request("turn/start", turn));
    }

    string ThreadStart()
    {
        var start = new JsonObject
        {
            ["cwd"] = _options?.Folder,
            ["approvalPolicy"] = Policy(_mode),
            ["sandbox"] = _mode == "plan" ? "read-only" : "workspace-write",
        };
        // Un modelo que Codex no lista (por ejemplo, uno de Claude) no se pasa: abre con el de su configuración.
        if (_model is not null && ModelInfo(_model) is not null) start["model"] = _model;
        else _model = null;
        return Request("thread/start", start);
    }

    static string Policy(string mode) => mode switch { "default" => "untrusted", "auto" => "never", _ => "on-request" };

    (string Id, IReadOnlyList<string> Efforts, bool IsDefault)? ModelInfo(string? model)
    {
        foreach (var info in _models)
            if (info.Id == model) return info;
        return null;
    }

    /// <summary>El esfuerzo elegido, si el modelo lo admite (o si todavía no se sabe).</summary>
    string? Effort(string? model) =>
        _effort is null ? null : ModelInfo(model) is { } info && info.Efforts.Count > 0 && !info.Efforts.Contains(_effort) ? null : _effort;

    public IEnumerable<string> SetModel(string? model)
    {
        if (model is not null && _models.Count > 0 && ModelInfo(model) is null) return [];
        _model = model;
        _modelChanged = true;
        transcript.CodexModel(model ?? _models.FirstOrDefault(m => m.IsDefault).Id);
        return [];
    }

    public IEnumerable<string> SetEffort(string effort)
    {
        if (!Efforts.Contains(effort)) return [];
        _effort = effort;
        return [];
    }

    public IEnumerable<string> SetMode(string mode)
    {
        if (!CodexModes.Contains(mode)) return [];
        _mode = mode;
        _modeChanged = true;
        transcript.CodexMode(mode);
        return [];
    }

    public IEnumerable<string> Interrupt()
    {
        if (_thread is not null && _turn is not null)
            return [Request("turn/interrupt", new JsonObject { ["threadId"] = _thread, ["turnId"] = _turn })];
        // El turn/start salió pero su id todavía no llegó: se corta apenas llegue.
        if (_active) _interruptPending = true;
        // Si la sesión todavía no abre, lo que estaba en cola ya no sale.
        else
        {
            _queue.Clear();
            transcript.CodexIdle();
        }
        return [];
    }

    public IEnumerable<string> Answer(AgentBlock permission, PermissionAnswer answer)
    {
        if (permission.RequestId is not { } id || transcript.CodexPending(id) is not { } pending) return [];
        if (pending.Method == "item/tool/requestUserInput") return [Reply(id, new JsonObject { ["answers"] = new JsonObject() })];
        return [Reply(id, new JsonObject { ["decision"] = Decision(pending.Decisions, answer) })];
    }

    /// <summary>
    /// La decisión de Codex: «permitir» es accept; «siempre» es acceptForSession o, si para ese comando solo ofrece una
    /// regla, esa regla (acceptWithExecpolicyAmendment); «rechazar» es decline o, si no lo ofrece, cancel (corta el turno).
    /// </summary>
    static JsonNode Decision(JsonNode? offered, PermissionAnswer answer)
    {
        bool Offers(string decision) => offered is not JsonArray list
            || list.Any(x => x is JsonValue v && v.TryGetValue<string>(out var s) && s == decision);
        return answer switch
        {
            PermissionAnswer.Allow => "accept",
            PermissionAnswer.AllowAlways when Offers("acceptForSession") => "acceptForSession",
            PermissionAnswer.AllowAlways => (offered as JsonArray)?.FirstOrDefault(x => x is JsonObject o && o.ContainsKey("acceptWithExecpolicyAmendment"))?.DeepClone()
                ?? JsonValue.Create("accept"),
            _ => Offers("decline") ? "decline" : "cancel",
        };
    }

    /// <summary>Las respuestas a las preguntas (request_user_input): por id de pregunta, lo elegido.</summary>
    public IEnumerable<string> Answer(AgentBlock permission, IReadOnlyDictionary<string, IReadOnlyList<string>> picks)
    {
        if (permission.RequestId is not { } id || transcript.CodexPending(id) is not { } pending) return [];
        if (pending.Method != "item/tool/requestUserInput") return Answer(permission, PermissionAnswer.Allow);
        var answers = new JsonObject();
        foreach (var q in permission.Input?["questions"] as JsonArray ?? [])
        {
            var question = (string?)q?["question"] ?? "";
            if ((string?)q?["id"] is { } questionId && picks.TryGetValue(question, out var chosen))
                answers[questionId] = new JsonObject { ["answers"] = new JsonArray([.. chosen.Select(c => (JsonNode?)c)]) };
        }
        return [Reply(id, new JsonObject { ["answers"] = answers })];
    }

    public (PermissionAnswer Answer, IEnumerable<string> Lines)? FromIsland(AgentBlock permission, string reply)
    {
        if (AgentDriver.IslandDecision(reply) is not { } answer) return null;
        if (answer == PermissionAnswer.Allow && permission.Questions is not null && IslandPicks(reply) is { } picks)
        {
            permission.Picks = picks;
            return (answer, Answer(permission, picks));
        }
        return (answer, Answer(permission, answer));
    }

    /// <summary>Lo que se eligió en la isla (updatedInput.answers: por pregunta, una etiqueta o la lista).</summary>
    static IReadOnlyDictionary<string, IReadOnlyList<string>>? IslandPicks(string reply)
    {
        try
        {
            if (JsonNode.Parse(reply)?["hookSpecificOutput"]?["decision"]?["updatedInput"]?["answers"] is not JsonObject answers) return null;
            return answers.ToDictionary(a => a.Key, a => (IReadOnlyList<string>)(a.Value is JsonArray list
                ? [.. list.Select(x => (string?)x ?? "")] : [(string?)a.Value ?? ""]));
        }
        catch (Exception) { return null; }
    }

    string Request(string method, JsonObject parameters)
    {
        var id = Interlocked.Increment(ref _requests);
        _pending[id.ToString()] = method;
        return Line(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
    }

    static string Reply(string id, JsonObject result) =>
        Line(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = JsonNode.Parse(id), ["result"] = result });

    static string Line(JsonObject o) => o.ToJsonString();
}
