using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>
/// Gemini CLI y Copilot CLI por Agent Client Protocol: JSON-RPC 2.0 por la entrada y la salida («--acp»). Abre con
/// initialize y, con su respuesta, pide la sesión (session/new). Los pedidos que llegan antes de la sesión, o durante un
/// turno, se guardan y salen al terminar el turno: un session/prompt nuevo cancelaría el que corre. Lo que el agente
/// cuenta (session/update) y los permisos que pide (session/request_permission) los vuelve bloques la transcripción.
/// Sin sesión iniciada, session/new falla: queda el aviso de cómo iniciarla y el pedido siguiente lo vuelve a intentar.
/// </summary>
sealed class AcpDriver(string kind, AgentTranscript transcript) : IAgentDriver
{
    /// <summary>Los esfuerzos de Copilot CLI («--reasoning-effort» al abrir; por ACP no se cambia si no lo anuncia).</summary>
    static readonly string[] CopilotEfforts = ["low", "medium", "high", "xhigh", "max"];

    /// <summary>Los modos de Claude Code que se eligen al abrir y el modo de ACP que les corresponde, en orden de preferencia.</summary>
    static readonly Dictionary<string, string[]> ModeAliases = new()
    {
        ["default"] = ["default", "agent"],
        ["acceptEdits"] = ["autoEdit", "auto_edit", "acceptEdits"],
        ["plan"] = ["plan"],
    };

    // Los pedidos que esperan respuesta: el método y el valor que se pidió (el modo o el modelo).
    readonly Dictionary<long, (string Method, string? Value)> _pending = [];
    readonly Queue<(string Text, IReadOnlyList<AgentImage>? Images)> _queue = new();
    readonly Stopwatch _clock = new();
    long _requests;
    AgentOptions? _options;
    bool _initialized, _creating, _turn;
    string? _session;

    // Lo que anunció la sesión: los modos (el id corto y el de ACP), los modelos y los esfuerzos, y si se cambian como
    // opción de la sesión (session/set_config_option, su id) o con session/set_mode y session/set_model.
    readonly Dictionary<string, string> _modeIds = [];
    List<string> _modes = [], _models = [], _efforts = [];
    string? _modeConfig, _modelConfig, _effortConfig;

    public string Name => AgentKinds.Label(kind);
    public IReadOnlyList<string> Models => _models;
    public IReadOnlyList<string> Modes => _modes;

    /// <summary>Los que anunció la sesión; antes de abrirla, los de «--reasoning-effort» de Copilot.</summary>
    public IReadOnlyList<string> Efforts =>
        _effortConfig is not null ? _efforts : kind == AgentKinds.Copilot && _session is null ? CopilotEfforts : [];

    /// <summary>
    /// El ejecutable de ese agente (AgentKinds.Gemini o Copilot), o null si no está: en el PATH y, si no, donde npm deja
    /// los globales (en Windows, gemini.cmd y copilot.cmd en %APPDATA%\npm).
    /// </summary>
    public static string? Find(string kind)
    {
        var name = kind switch { AgentKinds.Gemini => "gemini", AgentKinds.Copilot => "copilot", _ => null };
        if (name is null) return null;
        if (AgentDriver.OnPath(name) is { } found) return found;
        string[] candidates = OperatingSystem.IsWindows()
            ? [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", name + ".cmd")]
            : [Path.Combine("/opt/homebrew/bin", name), Path.Combine("/usr/local/bin", name), Path.Combine(Account.Home, ".npm-global", "bin", name)];
        return candidates.FirstOrDefault(File.Exists);
    }

    public ProcessStartInfo? Start(AgentOptions options)
    {
        if (Find(kind) is not { } path) return null;
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
        Launch(start, path);
        start.ArgumentList.Add("--acp");
        // Copilot toma el esfuerzo al abrir (vale para toda la sesión); Gemini CLI no tiene esfuerzo.
        if (kind == AgentKinds.Copilot && options.Effort is { } effort && CopilotEfforts.Contains(effort))
        {
            start.ArgumentList.Add("--reasoning-effort");
            start.ArgumentList.Add(effort);
        }
        return start;
    }

    /// <summary>
    /// Cómo se abre: el .cmd de npm se salta y se corre lo que él corre (el .js con node, o el ejecutable de Copilot de su
    /// paquete de plataforma), así no pasa por cmd.exe. Si no se reconoce, va por cmd.exe; los argumentos son solo opciones.
    /// </summary>
    static void Launch(ProcessStartInfo start, string path)
    {
        if (!OperatingSystem.IsWindows() || !path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            start.FileName = path;
            return;
        }
        var dir = Path.GetDirectoryName(path) ?? "";
        if (ShimTarget(path) is { } target)
        {
            if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                start.FileName = target;
                return;
            }
            // Como el .cmd: el node.exe de al lado si lo hay, si no el del PATH.
            var node = Path.Combine(dir, "node.exe");
            if (!File.Exists(node)) node = AgentDriver.OnPath("node") ?? "";
            if (node.Length > 0)
            {
                start.FileName = node;
                start.ArgumentList.Add(target);
                return;
            }
        }
        start.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        foreach (var arg in new[] { "/d", "/s", "/c", path }) start.ArgumentList.Add(arg);
    }

    /// <summary>
    /// Lo que corre el .cmd de npm («"%dp0%\node_modules\…\gemini.js" %*»), si existe. El de Copilot corre un cargador que
    /// solo abre el ejecutable de @github/copilot-win32-x64: si ese ejecutable está, se abre directo.
    /// </summary>
    internal static string? ShimTarget(string cmd)
    {
        try
        {
            var dir = Path.GetDirectoryName(cmd) ?? "";
            var text = File.ReadAllText(cmd);
            var match = Regex.Matches(text, @"""%dp0%\\([^""]+\.(?:js|cjs|mjs|exe))""", RegexOptions.IgnoreCase).LastOrDefault();
            if (match is null) return null;
            var target = Path.Combine(dir, match.Groups[1].Value);
            var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
            var native = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(target) ?? "") ?? "", $"copilot-win32-{arch}", "copilot.exe");
            if (target.EndsWith(Path.Combine("@github", "copilot", "npm-loader.js"), StringComparison.OrdinalIgnoreCase) && File.Exists(native))
                return native;
            return File.Exists(target) ? target : null;
        }
        catch (Exception) { return null; }
    }

    public IEnumerable<string> Opening(AgentOptions options)
    {
        _options = options;
        return [Request("initialize", new JsonObject
        {
            ["protocolVersion"] = 1,
            // La app no lee ni escribe archivos por el agente ni le abre terminales: los hace él.
            ["clientCapabilities"] = new JsonObject
            {
                ["fs"] = new JsonObject { ["readTextFile"] = false, ["writeTextFile"] = false },
                ["terminal"] = false,
            },
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "claupet",
                ["title"] = "Claupet",
                ["version"] = typeof(AcpDriver).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            },
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
            transcript.AcpFlush(changed);
            var method = Json.Str(r, "method");
            var hasId = r.TryGetProperty("id", out var id) && id.ValueKind is JsonValueKind.Number or JsonValueKind.String;
            r.TryGetProperty("params", out var p);
            if (method is not null && hasId) AgentRequest(method, id, p, changed, replies);
            else if (method == "session/update" && p.ValueKind == JsonValueKind.Object) Update(p, changed);
            else if (method is null && hasId) Response(id, r, changed, replies);
            return changed;
        }
        catch (JsonException) { return []; }
    }

    /// <summary>Lo que pide el agente: los permisos; lo demás (archivos, terminales) no se ofreció y se contesta que no está.</summary>
    void AgentRequest(string method, JsonElement id, JsonElement p, List<AgentBlock> changed, List<string> replies)
    {
        if (method == "session/request_permission" && p.ValueKind == JsonValueKind.Object)
        {
            transcript.AcpPermission(id.GetRawText(), p, changed);
            return;
        }
        replies.Add(Line(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse(id.GetRawText()),
            ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Method not found: " + method },
        }));
    }

    /// <summary>Los avisos de la sesión: los modos y las opciones los lleva el driver; lo demás, la transcripción.</summary>
    void Update(JsonElement p, List<AgentBlock> changed)
    {
        if (!p.TryGetProperty("update", out var u) || u.ValueKind != JsonValueKind.Object) return;
        switch (Json.Str(u, "sessionUpdate"))
        {
            case "current_mode_update":
                transcript.AcpMode(Json.Str(u, "currentModeId") is { } mode ? AgentTranscript.AcpShortMode(mode) : null);
                break;
            case "config_option_update" when u.TryGetProperty("configOptions", out var options):
                ConfigOptions(options);
                break;
            default:
                transcript.AcpUpdate(u, changed);
                break;
        }
    }

    void Response(JsonElement id, JsonElement r, List<AgentBlock> changed, List<string> replies)
    {
        long key;
        var known = id.ValueKind == JsonValueKind.Number ? id.TryGetInt64(out key) : long.TryParse(id.GetString(), out key);
        if (!known || !_pending.Remove(key, out var request)) return;
        if (r.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            Failed(request.Method, error, changed, replies);
            return;
        }
        r.TryGetProperty("result", out var result);
        switch (request.Method)
        {
            case "initialize":
                _initialized = true;
                replies.Add(NewSession());
                break;
            case "session/new":
                _creating = false;
                if (result.ValueKind != JsonValueKind.Object || Json.Str(result, "sessionId") is not { } session) return;
                _session = session;
                transcript.AcpStarted(session, _options?.Folder);
                SessionState(result);
                replies.AddRange(Chosen());
                replies.AddRange(Next());
                break;
            case "session/prompt":
                _turn = false;
                transcript.AcpDone(result.ValueKind == JsonValueKind.Object ? Json.Str(result, "stopReason") : null, _clock.Elapsed, changed);
                replies.AddRange(Next());
                break;
            case "session/set_mode":
                transcript.AcpMode(request.Value is null ? null : AgentTranscript.AcpShortMode(request.Value));
                break;
            case "session/set_model":
                transcript.AcpModel(request.Value);
                break;
            case "session/set_config_option" when result.ValueKind == JsonValueKind.Object && result.TryGetProperty("configOptions", out var options):
                ConfigOptions(options);
                break;
        }
    }

    /// <summary>Un pedido que falló: si es por la sesión iniciada, el aviso dice cómo iniciarla.</summary>
    void Failed(string method, JsonElement error, List<AgentBlock> changed, List<string> replies)
    {
        var message = ErrorMessage(error);
        switch (method)
        {
            case "initialize":
            case "session/new":
                _creating = false;
                _queue.Clear();
                transcript.AcpNotice(IsAuthError(error)
                    ? L.T("{0} necesita iniciar sesión: ejecute «{1}» una vez en una terminal y vuelva a escribir aquí ({2}).", Name, LoginCommand, message)
                    : L.T("{0} no pudo abrir la sesión: {1}", Name, message), changed);
                break;
            case "session/prompt":
                _turn = false;
                transcript.AcpFailed(IsAuthError(error)
                    ? L.T("{0} necesita iniciar sesión: ejecute «{1}» una vez en una terminal y vuelva a escribir aquí ({2}).", Name, LoginCommand, message)
                    : message, _clock.Elapsed, changed);
                replies.AddRange(Next());
                break;
            default:
                // Un modo, un modelo o una opción que no tomó: queda el aviso, el turno sigue.
                transcript.AcpError(message, changed);
                break;
        }
    }

    string LoginCommand => kind == AgentKinds.Copilot ? "copilot login" : "gemini";

    /// <summary>El texto del error: el detalle si lo trae (Gemini lo pone en data.details), si no el mensaje.</summary>
    internal static string ErrorMessage(JsonElement error)
    {
        var data = error.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? d : default;
        var text = (data.ValueKind == JsonValueKind.Object ? Json.Str(data, "details") : null) ?? Json.Str(error, "message") ?? "";
        // Copilot lo manda entre comillas y con un «\n» escrito: 403 "unauthorized: …\n".
        text = text.Replace("\\n", " ").Replace("\"", "").ReplaceLineEndings(" ");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    /// <summary>
    /// Falta iniciar sesión: el código auth_required de ACP (-32000, Gemini CLI), un 401/403 (Copilot CLI sin sesión da
    /// 403 «not authorized») o un mensaje que lo dice.
    /// </summary>
    internal static bool IsAuthError(JsonElement error)
    {
        if (error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var c) && c == -32000) return true;
        if (error.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object && Json.Num(data, "status") is 401 or 403) return true;
        return Regex.IsMatch(ErrorMessage(error), @"\b(unauthori[sz]ed|not authorized|authenticat\w*|api key|log ?in|sign ?in|credentials?)\b", RegexOptions.IgnoreCase);
    }

    /// <summary>Los modos, modelos y opciones que trajo session/new (las opciones mandan sobre «modes» y «models»).</summary>
    void SessionState(JsonElement result)
    {
        if (result.TryGetProperty("modes", out var modes) && modes.ValueKind == JsonValueKind.Object)
        {
            SetModes(Ids(modes, "availableModes", "id"));
            transcript.AcpMode(Json.Str(modes, "currentModeId") is { } mode ? AgentTranscript.AcpShortMode(mode) : null);
        }
        if (result.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Object)
        {
            _models = Ids(models, "availableModels", "modelId");
            transcript.AcpModel(Json.Str(models, "currentModelId"));
        }
        if (result.TryGetProperty("configOptions", out var options)) ConfigOptions(options);
    }

    static List<string> Ids(JsonElement state, string list, string id) =>
        state.TryGetProperty(list, out var items) && items.ValueKind == JsonValueKind.Array
            ? [.. items.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object).Select(i => Json.Str(i, id)).OfType<string>()]
            : [];

    /// <summary>Las opciones de la sesión que se entienden: el modo, el modelo y el esfuerzo (thought_level), solo de lista.</summary>
    void ConfigOptions(JsonElement options)
    {
        if (options.ValueKind != JsonValueKind.Array) return;
        foreach (var option in options.EnumerateArray())
        {
            if (option.ValueKind != JsonValueKind.Object || Json.Str(option, "type") != "select" || Json.Str(option, "id") is not { } id) continue;
            var values = new List<string>();
            if (option.TryGetProperty("options", out var items) && items.ValueKind == JsonValueKind.Array)
                foreach (var item in items.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object))
                    if (item.TryGetProperty("options", out var group) && group.ValueKind == JsonValueKind.Array)
                        values.AddRange(group.EnumerateArray().Where(g => g.ValueKind == JsonValueKind.Object).Select(g => Json.Str(g, "value")).OfType<string>());
                    else if (Json.Str(item, "value") is { } value) values.Add(value);
            var current = Json.Str(option, "currentValue");
            switch (Json.Str(option, "category"))
            {
                case "mode":
                    _modeConfig = id;
                    SetModes(values);
                    transcript.AcpMode(current is null ? null : AgentTranscript.AcpShortMode(current));
                    break;
                case "model":
                    _modelConfig = id;
                    _models = values;
                    transcript.AcpModel(current);
                    break;
                case "thought_level":
                    _effortConfig = id;
                    _efforts = values;
                    break;
            }
        }
    }

    /// <summary>Los modos se muestran con el id corto: el de Copilot es una dirección («…/session-modes#plan» es «plan»).</summary>
    void SetModes(List<string> ids)
    {
        _modeIds.Clear();
        foreach (var id in ids) _modeIds[AgentTranscript.AcpShortMode(id)] = id;
        _modes = [.. _modeIds.Keys];
    }

    /// <summary>Lo elegido al abrir la sesión (modelo, modo y esfuerzo), si el agente lo anunció y no es lo que ya tiene.</summary>
    IEnumerable<string> Chosen()
    {
        var lines = new List<string>();
        if (_options is null) return lines;
        if (_options.Model is { } model && _models.Contains(model) && model != transcript.Model) lines.AddRange(SetModel(model));
        if (_options.PermissionMode is { } wanted)
        {
            var mode = _modes.Contains(wanted) ? wanted
                : ModeAliases.TryGetValue(wanted, out var aliases) ? aliases.FirstOrDefault(_modes.Contains) : null;
            if (mode is not null && mode != transcript.PermissionMode) lines.AddRange(SetMode(mode));
        }
        if (_effortConfig is not null && _options.Effort is { } effort && _efforts.Contains(effort)) lines.AddRange(SetEffort(effort));
        return lines;
    }

    public IEnumerable<string> Prompt(string text, IReadOnlyList<AgentImage>? images = null)
    {
        _queue.Enqueue((text, images));
        // Sin sesión (porque falló al abrir), el pedido la vuelve a pedir: quizá ya inició sesión en una terminal.
        if (_session is null) return _initialized && !_creating ? [NewSession()] : [];
        return Next();
    }

    /// <summary>El contenido del pedido en ACP: las imágenes (bloques image con sus bytes) y el texto.</summary>
    static JsonArray Content(string text, IReadOnlyList<AgentImage>? images)
    {
        var parts = new JsonArray();
        foreach (var image in images ?? [])
            parts.Add(new JsonObject { ["type"] = "image", ["mimeType"] = image.MediaType, ["data"] = image.Base64 });
        parts.Add(new JsonObject { ["type"] = "text", ["text"] = text });
        return parts;
    }

    /// <summary>El siguiente pedido guardado, si hay sesión y no corre un turno.</summary>
    IEnumerable<string> Next()
    {
        if (_session is null || _turn || !_queue.TryDequeue(out var next)) return [];
        var (text, images) = next;
        _turn = true;
        _clock.Restart();
        transcript.AcpTurn();
        return [Request("session/prompt", new JsonObject
        {
            ["sessionId"] = _session,
            ["prompt"] = Content(text, images),
        })];
    }

    string NewSession()
    {
        _creating = true;
        return Request("session/new", new JsonObject { ["cwd"] = _options?.Folder ?? Environment.CurrentDirectory, ["mcpServers"] = new JsonArray() });
    }

    /// <summary>Sin modelo, vuelve al automático del agente (el «auto» de Gemini CLI) si lo tiene.</summary>
    public IEnumerable<string> SetModel(string? model)
    {
        model ??= _models.FirstOrDefault(m => m == "auto");
        if (_session is null || model is null || !_models.Contains(model)) return [];
        return _modelConfig is not null
            ? [ConfigRequest(_modelConfig, model)]
            : [Request("session/set_model", new JsonObject { ["sessionId"] = _session, ["modelId"] = model }, model)];
    }

    public IEnumerable<string> SetEffort(string effort) =>
        _session is null || _effortConfig is null || !_efforts.Contains(effort) ? [] : [ConfigRequest(_effortConfig, effort)];

    public IEnumerable<string> SetMode(string mode)
    {
        if (_session is null || !_modeIds.TryGetValue(mode, out var id)) return [];
        return _modeConfig is not null
            ? [ConfigRequest(_modeConfig, id)]
            : [Request("session/set_mode", new JsonObject { ["sessionId"] = _session, ["modeId"] = id }, id)];
    }

    string ConfigRequest(string configId, string value) =>
        Request("session/set_config_option", new JsonObject { ["sessionId"] = _session, ["configId"] = configId, ["value"] = value }, value);

    /// <summary>
    /// Corta el turno (session/cancel). Los permisos que esperan se contestan «cancelled», como pide ACP: el agente no
    /// termina el turno mientras espera esa respuesta. Si aún no había sesión, se descartan los pedidos guardados.
    /// </summary>
    public IEnumerable<string> Interrupt()
    {
        if (_session is null || !_turn)
        {
            _queue.Clear();
            transcript.AcpIdle();
            return [];
        }
        var lines = transcript.AcpCancelPermissions()
            .Select(id => Reply(id, new JsonObject { ["outcome"] = new JsonObject { ["outcome"] = "cancelled" } })).ToList();
        lines.Add(Line(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "session/cancel", ["params"] = new JsonObject { ["sessionId"] = _session } }));
        return lines;
    }

    /// <summary>La opción del permiso que corresponde a la respuesta; si el agente no la ofrece, la más parecida.</summary>
    public IEnumerable<string> Answer(AgentBlock permission, PermissionAnswer answer)
    {
        if (permission.RequestId is null) return [];
        string[] kinds = answer switch
        {
            PermissionAnswer.AllowAlways => ["allow_always", "allow_once"],
            PermissionAnswer.Allow => ["allow_once", "allow_always"],
            _ => ["reject_once", "reject_always"],
        };
        var options = permission.Suggestions as JsonArray ?? [];
        var optionId = kinds.Select(k => options.FirstOrDefault(o => (string?)o?["kind"] == k)).FirstOrDefault(o => o is not null)?["optionId"]?.GetValue<string>();
        var outcome = optionId is null
            ? new JsonObject { ["outcome"] = "cancelled" }
            : new JsonObject { ["outcome"] = "selected", ["optionId"] = optionId };
        return [Reply(permission.RequestId, new JsonObject { ["outcome"] = outcome })];
    }

    /// <summary>ACP no tiene preguntas con opciones: si llegara a haberlas, se permite.</summary>
    public IEnumerable<string> Answer(AgentBlock permission, IReadOnlyDictionary<string, IReadOnlyList<string>> picks) =>
        Answer(permission, PermissionAnswer.Allow);

    public (PermissionAnswer Answer, IEnumerable<string> Lines)? FromIsland(AgentBlock permission, string reply) =>
        AgentDriver.IslandDecision(reply) is { } answer ? (answer, Answer(permission, answer)) : null;

    string Request(string method, JsonObject parameters, string? value = null)
    {
        var id = ++_requests;
        _pending[id] = (method, value);
        return Line(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
    }

    /// <summary>La respuesta a un pedido del agente, con su id tal cual llegó (número o texto).</summary>
    static string Reply(string rawId, JsonObject result) =>
        Line(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = JsonNode.Parse(rawId), ["result"] = result });

    static string Line(JsonObject o) => o.ToJsonString();
}
