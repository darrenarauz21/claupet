using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet.Tests;

/// <summary>
/// Gemini CLI y Copilot CLI por Agent Client Protocol (AcpDriver y AgentTranscript.Acp). Las líneas tienen la forma de las
/// de Gemini CLI 0.66 y Copilot CLI 1.0.105 con «--acp» (capturadas de los programas reales, con las rutas cambiadas).
/// </summary>
public sealed class AcpDriverTests
{
    const string Folder = @"C:\proyecto";
    const string Mode = "https://agentclientprotocol.com/protocol/session-modes#";

    const string GeminiInit = """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":1,"authMethods":[{"id":"oauth-personal","name":"Log in with Google","description":"Log in with your Google account"},{"id":"gemini-api-key","name":"Gemini API key","description":"Use an API key with Gemini Developer API","_meta":{"api-key":{"provider":"google"}}}],"agentInfo":{"name":"gemini-cli","title":"Gemini CLI","version":"0.66.0"},"agentCapabilities":{"loadSession":true,"promptCapabilities":{"image":true,"audio":true,"embeddedContext":true},"mcpCapabilities":{"http":true,"sse":true}}}}""";
    const string GeminiSession = """{"jsonrpc":"2.0","id":2,"result":{"sessionId":"s-1","modes":{"availableModes":[{"id":"default","name":"Default","description":"Prompts for approval"},{"id":"autoEdit","name":"Auto Edit","description":"Auto-approves edit tools"},{"id":"yolo","name":"YOLO","description":"Auto-approves all tools"},{"id":"plan","name":"Plan","description":"Read-only mode"}],"currentModeId":"default"},"models":{"availableModels":[{"modelId":"auto","name":"Auto"},{"modelId":"gemini-2.5-pro","name":"gemini-2.5-pro"},{"modelId":"gemini-3.8-flash","name":"gemini-3.8-flash"}],"currentModelId":"auto"}}}""";
    const string CopilotInit = """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":1,"agentCapabilities":{"auth":{"logout":{}},"loadSession":true,"mcpCapabilities":{"http":true,"sse":true},"promptCapabilities":{"image":true,"audio":false,"embeddedContext":true},"sessionCapabilities":{"close":{},"list":{}}},"agentInfo":{"name":"Copilot","title":"Copilot","version":"1.0.105"},"authMethods":[{"id":"copilot-login","name":"Log in with Copilot CLI","description":"Run `copilot login` in the terminal","_meta":{"terminal-auth":{"command":"copilot","args":["login"],"label":"Copilot Login"}}}]}}""";
    const string CopilotOptions = """[{"type":"select","id":"mode","name":"Mode","category":"mode","currentValue":"https://agentclientprotocol.com/protocol/session-modes#agent","options":[{"value":"https://agentclientprotocol.com/protocol/session-modes#agent","name":"Agent"},{"value":"https://agentclientprotocol.com/protocol/session-modes#plan","name":"Plan"},{"value":"https://agentclientprotocol.com/protocol/session-modes#autopilot","name":"Autopilot"}]},{"type":"select","id":"allow_all","name":"Allow All","category":"permissions","currentValue":"off","options":[{"value":"on","name":"On"},{"value":"off","name":"Off"}]}]""";
    static readonly string CopilotSession = Result(2, """{"configOptions":""" + CopilotOptions + ""","modes":{"availableModes":[{"id":"MODEagent","name":"Agent"},{"id":"MODEplan","name":"Plan"},{"id":"MODEautopilot","name":"Autopilot"}],"currentModeId":"MODEagent"},"sessionId":"s-1"}""".Replace("MODE", Mode));

    /// <summary>Una sesión de prueba: el driver, su transcripción y lo que el driver le escribió al agente.</summary>
    sealed class Session
    {
        public readonly AgentTranscript Transcript = new();
        public readonly AcpDriver Driver;
        public readonly List<JsonNode> Sent = [];

        public Session(string kind, AgentOptions? options = null)
        {
            Driver = new AcpDriver(kind, Transcript);
            Write(Driver.Opening(options ?? new AgentOptions(Folder, Kind: kind)));
        }

        public void Write(IEnumerable<string> lines) => Sent.AddRange(lines.Select(l => JsonNode.Parse(l)!));

        /// <summary>Le pasa una línea del agente: lo que respondió el driver queda en Sent; devuelve los bloques que cambiaron.</summary>
        public IReadOnlyList<AgentBlock> Feed(string line)
        {
            var replies = new List<string>();
            var changed = Driver.Apply(line, replies);
            Write(replies);
            return changed;
        }

        /// <summary>Como TerminalAgent.Send: el bloque Ask y el pedido.</summary>
        public void Ask(string text)
        {
            Transcript.Ask(text);
            Write(Driver.Prompt(text));
        }

        public JsonNode Last => Sent[^1];
        public JsonNode? LastOf(string method) => Sent.LastOrDefault(n => (string?)n["method"] == method);
    }

    static string Update(string update) => "{\"jsonrpc\":\"2.0\",\"method\":\"session/update\",\"params\":{\"sessionId\":\"s-1\",\"update\":" + update + "}}";

    /// <summary>La respuesta del agente a un pedido del driver (id) con ese resultado o ese error.</summary>
    static string Result(int id, string result) => "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":" + result + "}";
    static string Error(int id, string error) => "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"error\":" + error + "}";

    static Session Gemini(AgentOptions? options = null)
    {
        var s = new Session(AgentKinds.Gemini, options);
        s.Feed(GeminiInit);
        s.Feed(GeminiSession);
        return s;
    }

    static Session Copilot(AgentOptions? options = null)
    {
        var s = new Session(AgentKinds.Copilot, options);
        s.Feed(CopilotInit);
        s.Feed(CopilotSession);
        return s;
    }

    [Fact]
    public void AbreConInitializeYPideLaSesionAlRecibirLaRespuesta()
    {
        var s = new Session(AgentKinds.Gemini);
        var init = Assert.Single(s.Sent);
        Assert.Equal("initialize", (string?)init["method"]);
        Assert.Equal(1, (int)init["params"]!["protocolVersion"]!);
        Assert.False((bool)init["params"]!["clientCapabilities"]!["fs"]!["readTextFile"]!);
        Assert.False((bool)init["params"]!["clientCapabilities"]!["terminal"]!);

        // Un pedido antes de la sesión se guarda.
        s.Ask("hola");
        Assert.Single(s.Sent);

        s.Feed(GeminiInit);
        var created = s.Last;
        Assert.Equal("session/new", (string?)created["method"]);
        Assert.Equal(Folder, (string?)created["params"]!["cwd"]);
        Assert.Empty(created["params"]!["mcpServers"]!.AsArray());

        s.Feed(GeminiSession);
        var prompt = s.Last;
        Assert.Equal("session/prompt", (string?)prompt["method"]);
        Assert.Equal("s-1", (string?)prompt["params"]!["sessionId"]);
        Assert.Equal("hola", (string?)prompt["params"]!["prompt"]![0]!["text"]);
        Assert.Equal("s-1", s.Transcript.SessionId);
        Assert.Equal(Folder, s.Transcript.Cwd);
        Assert.Equal("auto", s.Transcript.Model);
        Assert.Equal("default", s.Transcript.PermissionMode);
        Assert.Equal(["auto", "gemini-2.5-pro", "gemini-3.8-flash"], s.Driver.Models);
        Assert.Equal(["default", "autoEdit", "yolo", "plan"], s.Driver.Modes);
        Assert.Empty(s.Driver.Efforts);
    }

    [Fact]
    public void SinSesionIniciadaDiceComoIniciarlaYElPedidoSiguienteLaVuelveAPedir()
    {
        var s = new Session(AgentKinds.Gemini);
        s.Feed(GeminiInit);
        s.Ask("hola");
        var changed = s.Feed("""{"jsonrpc":"2.0","id":2,"error":{"code":-32000,"message":"Gemini API key is missing or not configured."}}""");

        var notice = Assert.Single(changed, b => b.Kind == AgentBlockKind.Notice);
        Assert.Equal(L.T("{0} necesita iniciar sesión: ejecute «{1}» una vez en una terminal y vuelva a escribir aquí ({2}).",
            "Gemini CLI", "gemini", "Gemini API key is missing or not configured."), notice.Text);
        Assert.False(s.Transcript.Busy);

        // Quizá ya inició sesión en una terminal: el pedido siguiente pide la sesión otra vez y sale con ella.
        s.Ask("otra vez");
        Assert.Equal("session/new", (string?)s.Last["method"]);
        s.Feed(GeminiSession.Replace("\"id\":2", "\"id\":3"));
        Assert.Equal("otra vez", (string?)s.Last["params"]!["prompt"]![0]!["text"]);
        Assert.Single(s.Sent, n => (string?)n["method"] == "session/prompt");
    }

    [Fact]
    public void CopilotSinSesionDa403YElAvisoPideCopilotLogin()
    {
        var s = new Session(AgentKinds.Copilot);
        s.Feed(CopilotInit);
        // Llega un aviso de la sesión antes de que session/new falle: no deja bloques.
        Assert.Empty(s.Feed(Update("""{"sessionUpdate":"available_commands_update","availableCommands":[{"name":"context","description":"Show context window token usage"}]}""")));
        var changed = s.Feed("""{"jsonrpc":"2.0","id":2,"error":{"code":-32603,"message":"403 \"unauthorized: not authorized to use this Copilot feature\\n\"","data":{"category":"permission_denied","status":403}}}""");

        var notice = Assert.Single(changed);
        Assert.Equal(L.T("{0} necesita iniciar sesión: ejecute «{1}» una vez en una terminal y vuelva a escribir aquí ({2}).",
            "Copilot CLI", "copilot login", "403 unauthorized: not authorized to use this Copilot feature"), notice.Text);
        Assert.Null(s.Transcript.SessionId);
    }

    [Fact]
    public void OtroErrorAlAbrirNoHablaDeIniciarSesion()
    {
        var s = new Session(AgentKinds.Gemini);
        s.Feed(GeminiInit);
        var notice = Assert.Single(s.Feed("""{"jsonrpc":"2.0","id":2,"error":{"code":-32603,"message":"Internal error","data":{"details":"ENOENT: no such file or directory"}}}"""));
        Assert.Equal(L.T("{0} no pudo abrir la sesión: {1}", "Gemini CLI", "ENOENT: no such file or directory"), notice.Text);
    }

    [Fact]
    public void GeminiPiensaRespondePorFragmentosYEditaConPermiso()
    {
        var s = Gemini();
        s.Ask("cambia tres");
        s.Feed(Update("""{"sessionUpdate":"agent_thought_chunk","content":{"type":"text","text":"**Pensando**\nNecesito una herramienta."}}"""));
        s.Feed(Update("""{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"Voy a "}}"""));
        s.Feed(Update("""{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"editar."}}"""));
        s.Feed(Update("""{"sessionUpdate":"usage_update","used":120,"size":1048576}"""));
        // Gemini manda el archivo entero antes y después.
        s.Feed(Update("""{"sessionUpdate":"tool_call","toolCallId":"replace-1","status":"pending","title":"a.txt: tres => TRES","content":[{"type":"diff","path":"C:\\proyecto\\a.txt","oldText":"uno\ndos\ncuatro\ntres\n","newText":"uno\ndos\ncuatro\nTRES\nfin\n","_meta":{"kind":"modify"}}],"locations":[{"path":"C:\\proyecto\\a.txt"}],"kind":"edit"}"""));
        var changed = s.Feed("""{"jsonrpc":"2.0","id":0,"method":"session/request_permission","params":{"sessionId":"s-1","options":[{"optionId":"proceed_always","name":"Allow for this session","kind":"allow_always"},{"optionId":"proceed_once","name":"Allow","kind":"allow_once"},{"optionId":"cancel","name":"Reject","kind":"reject_once"}],"toolCall":{"toolCallId":"replace-1","status":"pending","title":"a.txt: tres => TRES","content":[{"type":"diff","path":"C:\\proyecto\\a.txt","oldText":"uno\ndos\ncuatro\ntres\n","newText":"uno\ndos\ncuatro\nTRES\nfin\n","_meta":{"kind":"modify"}}],"locations":[{"path":"C:\\proyecto\\a.txt"}],"kind":"edit"}}}""");

        Assert.Equal([AgentBlockKind.Ask, AgentBlockKind.Think, AgentBlockKind.Say, AgentBlockKind.Step, AgentBlockKind.Permission],
            s.Transcript.Blocks.Select(b => b.Kind));
        var think = s.Transcript.Blocks[1];
        Assert.False(think.Open);
        Assert.Equal("Voy a editar.", s.Transcript.Blocks[2].Text);
        Assert.Equal((120, 1048576), (s.Transcript.ContextUsed, s.Transcript.ContextWindow));

        var step = s.Transcript.Blocks[3];
        Assert.Equal("Edit", step.Tool);
        Assert.Equal("a.txt", step.Text);
        Assert.Equal(StepState.Waiting, step.State);
        Assert.Equal([" uno", " dos", " cuatro", "-tres", "+TRES", "+fin"], step.Diff);
        Assert.Equal((2, 1), (step.Added, step.Removed));

        var permission = Assert.Single(changed, b => b.Kind == AgentBlockKind.Permission);
        Assert.Equal("0", permission.RequestId);
        Assert.Equal("replace-1", permission.StepId);
        Assert.Equal("Edit", permission.Tool);
        Assert.Equal("a.txt", permission.Text);
        Assert.Equal(@"C:\proyecto\a.txt", (string?)permission.Input!["file_path"]);

        var allow = JsonNode.Parse(Assert.Single(s.Driver.Answer(permission, PermissionAnswer.Allow)))!;
        Assert.Equal(0, (int)allow["id"]!);
        Assert.Equal("selected", (string?)allow["result"]!["outcome"]!["outcome"]);
        Assert.Equal("proceed_once", (string?)allow["result"]!["outcome"]!["optionId"]);
        Assert.Equal("proceed_always", (string?)JsonNode.Parse(s.Driver.Answer(permission, PermissionAnswer.AllowAlways).Single())!["result"]!["outcome"]!["optionId"]);
        Assert.Equal("cancel", (string?)JsonNode.Parse(s.Driver.Answer(permission, PermissionAnswer.Deny).Single())!["result"]!["outcome"]!["optionId"]);

        s.Feed(Update("""{"sessionUpdate":"tool_call_update","toolCallId":"replace-1","status":"completed","title":"a.txt: tres => TRES","content":[{"type":"diff","path":"C:\\proyecto\\a.txt","oldText":"uno\ndos\ncuatro\ntres\n","newText":"uno\ndos\ncuatro\nTRES\nfin\n","_meta":{"kind":"modify"}}],"locations":[{"path":"C:\\proyecto\\a.txt"}],"kind":"edit"}"""));
        s.Feed(Update("""{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"Listo."}}"""));
        var done = s.Feed("""{"jsonrpc":"2.0","id":3,"result":{"stopReason":"end_turn","usage":{"inputTokens":200,"outputTokens":40,"totalTokens":240},"_meta":{"quota":{"token_count":{"input_tokens":200,"output_tokens":40}}}}}""");

        Assert.Equal(StepState.Done, step.State);
        Assert.Equal("Listo.", s.Transcript.Blocks[^2].Text);
        var end = Assert.Single(done, b => b.Kind == AgentBlockKind.Done);
        Assert.False(end.Failed);
        Assert.False(s.Transcript.Busy);
    }

    [Fact]
    public void ElComandoDeGeminiSaleDelTituloYSuExplicacionNoQuedaComoSalida()
    {
        var s = Gemini();
        s.Ask("lista");
        s.Feed(Update("""{"sessionUpdate":"tool_call","toolCallId":"shell-1","status":"in_progress","title":"dir /b","content":[{"type":"content","content":{"type":"text","text":"[current working directory C:\\proyecto] (Lista archivos)"}}],"locations":[],"kind":"execute"}"""));
        var step = s.Transcript.Blocks[^1];
        Assert.Equal("Bash", step.Tool);
        Assert.Equal(L.T("ejecutó"), AgentSteps.Verb(step.Tool));
        Assert.Equal("dir /b", step.Text);
        Assert.Equal(StepState.Running, step.State);

        // «content» reemplaza lo anterior: vacío, ya no hay salida.
        s.Feed(Update("""{"sessionUpdate":"tool_call_update","toolCallId":"shell-1","status":"completed","title":"dir /b","content":[],"locations":[],"kind":"execute"}"""));
        Assert.Equal(StepState.Done, step.State);
        Assert.Null(step.Output);
    }

    [Fact]
    public void ElAvisoDeModoDeGeminiNoEsRespuesta()
    {
        var s = Gemini();
        s.Ask("planea");
        Assert.Empty(s.Feed(Update("""{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"[MODE_UPDATE] plan"}}""")));
        Assert.Equal("plan", s.Transcript.PermissionMode);
        Assert.DoesNotContain(s.Transcript.Blocks, b => b.Kind == AgentBlockKind.Say);
    }

    [Fact]
    public void CopilotEjecutaConPermisoYMuestraLaSalida()
    {
        var s = Copilot();
        s.Ask("lista");
        s.Feed(Update("""{"sessionUpdate":"usage_update","used":11262,"size":64000}"""));
        s.Feed(Update("""{"sessionUpdate":"session_info_update","title":"lista","updatedAt":"2026-10-31T14:56:12.028Z"}"""));
        s.Feed(Update("""{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"Voy a usar una herramienta."}}"""));
        s.Feed(Update("""{"sessionUpdate":"tool_call","toolCallId":"call_1","title":"Lista archivos","kind":"execute","status":"pending","rawInput":{"command":"Get-ChildItem -Name","description":"Lista archivos"}}"""));
        var changed = s.Feed("""{"jsonrpc":"2.0","id":0,"method":"session/request_permission","params":{"sessionId":"s-1","toolCall":{"toolCallId":"call_1","title":"Lista archivos","kind":"execute","status":"pending","rawInput":{"command":"Get-ChildItem -Name","commands":["Get-ChildItem -Name"]}},"options":[{"optionId":"allow_once","kind":"allow_once","name":"Allow once"},{"optionId":"allow_always","kind":"allow_always","name":"Always allow"},{"optionId":"reject_once","kind":"reject_once","name":"Deny"}]}}""");

        var permission = Assert.Single(changed, b => b.Kind == AgentBlockKind.Permission);
        Assert.Equal("Bash", permission.Tool);
        Assert.Equal("Get-ChildItem -Name", permission.Text);
        Assert.Equal("Get-ChildItem -Name", (string?)permission.Input!["command"]);
        Assert.Equal("allow_always", (string?)JsonNode.Parse(s.Driver.Answer(permission, PermissionAnswer.AllowAlways).Single())!["result"]!["outcome"]!["optionId"]);

        // La isla responde con el formato del hook.
        var island = s.Driver.FromIsland(permission, Approvals.Allow());
        Assert.Equal(PermissionAnswer.Allow, island!.Value.Answer);
        Assert.Equal("allow_once", (string?)JsonNode.Parse(island.Value.Lines.Single())!["result"]!["outcome"]!["optionId"]);

        s.Feed(Update("""{"sessionUpdate":"tool_call_update","toolCallId":"call_1","content":[{"type":"content","content":{"type":"text","text":"\n\na.txt\n"}}]}"""));
        s.Feed(Update("""{"sessionUpdate":"tool_call_update","toolCallId":"call_1","status":"completed","content":[{"type":"content","content":{"type":"text","text":"a.txt\n<shellId: 0 completed with exit code 0>"}}],"rawOutput":{"content":"a.txt\n<shellId: 0 completed with exit code 0>"}}"""));
        s.Feed("""{"jsonrpc":"2.0","id":3,"result":{"stopReason":"end_turn","usage":{"inputTokens":220,"outputTokens":25,"totalTokens":245,"cachedReadTokens":0,"cachedWriteTokens":0}}}""");

        var step = Assert.Single(s.Transcript.Blocks, b => b.Kind == AgentBlockKind.Step);
        Assert.Equal(StepState.Done, step.State);
        Assert.Equal("a.txt\n<shellId: 0 completed with exit code 0>", step.Output);
        Assert.Equal((11262, 64000), (s.Transcript.ContextUsed, s.Transcript.ContextWindow));
        Assert.Equal(AgentBlockKind.Done, s.Transcript.Blocks[^1].Kind);
    }

    [Fact]
    public void CopilotEditaUnTrozoYCreaArchivos()
    {
        var s = Copilot();
        s.Ask("edita");
        s.Feed(Update("""{"sessionUpdate":"tool_call","toolCallId":"call_1","title":"Editing C:\\proyecto\\a.txt","kind":"edit","status":"pending","rawInput":{"path":"C:\\proyecto\\a.txt","old_str":"dos","new_str":"DOS\ncuatro"},"locations":[{"path":"C:\\proyecto\\a.txt"}],"content":[{"type":"diff","path":"C:\\proyecto\\a.txt","oldText":"dos","newText":"DOS\ncuatro"}]}"""));
        s.Feed(Update("""{"sessionUpdate":"tool_call","toolCallId":"call_2","title":"Creating C:\\proyecto\\b.txt","kind":"edit","status":"pending","rawInput":{"path":"C:\\proyecto\\b.txt","file_text":"linea 1\nlinea 2\n"},"locations":[{"path":"C:\\proyecto\\b.txt"}],"content":[{"type":"diff","path":"C:\\proyecto\\b.txt","oldText":"","newText":"linea 1\nlinea 2\n"}]}"""));

        var steps = s.Transcript.Blocks.Where(b => b.Kind == AgentBlockKind.Step).ToList();
        Assert.Equal(("Edit", "a.txt"), (steps[0].Tool, steps[0].Text));
        Assert.Equal(["-dos", "+DOS", "+cuatro"], steps[0].Diff);
        Assert.Equal(("Write", "b.txt"), (steps[1].Tool, steps[1].Text));
        Assert.Equal(L.T("creó"), AgentSteps.Verb(steps[1].Tool));
        Assert.Equal(["+linea 1", "+linea 2"], steps[1].Diff);

        // Rechazado por la persona: Copilot lo dice en rawOutput.code.
        s.Feed(Update("""{"sessionUpdate":"tool_call_update","toolCallId":"call_2","status":"failed","rawOutput":{"message":"The user rejected this tool call.","code":"rejected"}}"""));
        Assert.Equal(StepState.Denied, steps[1].State);
        Assert.Equal("The user rejected this tool call.", steps[1].Output);
        s.Feed(Update("""{"sessionUpdate":"tool_call_update","toolCallId":"call_1","status":"failed","content":[{"type":"content","content":{"type":"text","text":"No match found"}}]}"""));
        Assert.Equal(StepState.Failed, steps[0].State);
        Assert.Equal("No match found", steps[0].Output);
    }

    [Fact]
    public void LosModosDeCopilotSonOpcionesConIdCorto()
    {
        var s = Copilot();
        Assert.Equal(["agent", "plan", "autopilot"], s.Driver.Modes);
        Assert.Equal("agent", s.Transcript.PermissionMode);
        // Sin thought_level, el esfuerzo solo se elige al abrir (--reasoning-effort).
        Assert.Empty(s.Driver.Efforts);
        Assert.Empty(s.Driver.SetEffort("high"));

        s.Write(s.Driver.SetMode("plan"));
        var set = s.Last;
        Assert.Equal("session/set_config_option", (string?)set["method"]);
        Assert.Equal("mode", (string?)set["params"]!["configId"]);
        Assert.Equal(Mode + "plan", (string?)set["params"]!["value"]);
        Assert.Empty(s.Driver.SetMode("yolo"));

        s.Feed(Update("""{"sessionUpdate":"current_mode_update","currentModeId":"MODEplan"}""".Replace("MODE", Mode)));
        Assert.Equal("plan", s.Transcript.PermissionMode);
        s.Feed(Result((int)set["id"]!, """{"configOptions":""" + CopilotOptions.Replace("\"currentValue\":\"" + Mode + "agent\"", "\"currentValue\":\"" + Mode + "autopilot\"") + "}"));
        Assert.Equal("autopilot", s.Transcript.PermissionMode);
    }

    [Fact]
    public void ElEsfuerzoDeCopilotVaAlAbrirYPorOpcionSiLaAnuncia()
    {
        var before = new Session(AgentKinds.Copilot);
        Assert.Equal(["low", "medium", "high", "xhigh", "max"], before.Driver.Efforts);

        var s = Copilot();
        s.Feed(Update("""{"sessionUpdate":"config_option_update","configOptions":[{"type":"select","id":"model","name":"Model","category":"model","currentValue":"gpt-5.5","options":[{"group":"openai","name":"OpenAI","options":[{"value":"gpt-5.5","name":"GPT-5.5"}]},{"group":"anthropic","name":"Anthropic","options":[{"value":"claude-sonnet-5","name":"Claude Sonnet 5"}]}]},{"type":"select","id":"reasoning_effort","name":"Reasoning","category":"thought_level","currentValue":"medium","options":[{"value":"low","name":"Low"},{"value":"medium","name":"Medium"},{"value":"high","name":"High"}]}]}"""));
        Assert.Equal(["gpt-5.5", "claude-sonnet-5"], s.Driver.Models);
        Assert.Equal("gpt-5.5", s.Transcript.Model);
        Assert.Equal(["low", "medium", "high"], s.Driver.Efforts);

        var effort = JsonNode.Parse(s.Driver.SetEffort("high").Single())!;
        Assert.Equal(("reasoning_effort", "high"), ((string?)effort["params"]!["configId"], (string?)effort["params"]!["value"]));
        var model = JsonNode.Parse(s.Driver.SetModel("claude-sonnet-5").Single())!;
        Assert.Equal(("model", "claude-sonnet-5"), ((string?)model["params"]!["configId"], (string?)model["params"]!["value"]));
    }

    [Fact]
    public void GeminiCambiaElModeloYUnModoQueNoTomaQuedaComoAviso()
    {
        var s = Gemini();
        s.Write(s.Driver.SetModel("gemini-3.8-flash"));
        var set = s.Last;
        Assert.Equal("session/set_model", (string?)set["method"]);
        Assert.Equal("gemini-3.8-flash", (string?)set["params"]!["modelId"]);
        Assert.Empty(s.Driver.SetModel("opus"));
        s.Feed(Result((int)set["id"]!, "{}"));
        Assert.Equal("gemini-3.8-flash", s.Transcript.Model);
        // Sin modelo, vuelve al automático.
        Assert.Equal("auto", (string?)JsonNode.Parse(s.Driver.SetModel(null).Single())!["params"]!["modelId"]);

        s.Ask("algo");
        s.Write(s.Driver.SetMode("autoEdit"));
        var mode = s.Last;
        Assert.Equal(("session/set_mode", "autoEdit"), ((string?)mode["method"], (string?)mode["params"]!["modeId"]));
        var notice = Assert.Single(s.Feed(Error((int)mode["id"]!, """{"code":-32603,"message":"Internal error","data":{"details":"Cannot enable privileged approval modes in an untrusted folder."}}""")));
        Assert.Equal("Cannot enable privileged approval modes in an untrusted folder.", notice.Text);
        Assert.Equal("default", s.Transcript.PermissionMode);
        Assert.True(s.Transcript.Busy);
    }

    [Fact]
    public void LoElegidoAlAbrirSePoneAlTenerLaSesion()
    {
        var s = new Session(AgentKinds.Gemini, new AgentOptions(Folder, Model: "gemini-2.5-pro", PermissionMode: "acceptEdits", Kind: AgentKinds.Gemini));
        s.Feed(GeminiInit);
        s.Feed(GeminiSession);
        Assert.NotNull(s.Sent.SingleOrDefault(n => (string?)n["method"] == "session/set_model" && (string?)n["params"]!["modelId"] == "gemini-2.5-pro"));
        Assert.NotNull(s.Sent.SingleOrDefault(n => (string?)n["method"] == "session/set_mode" && (string?)n["params"]!["modeId"] == "autoEdit"));

        // Un modelo de Claude Code no se le pasa a Gemini.
        var other = new Session(AgentKinds.Gemini, new AgentOptions(Folder, Model: "opus", Kind: AgentKinds.Gemini));
        other.Feed(GeminiInit);
        other.Feed(GeminiSession);
        Assert.Null(other.LastOf("session/set_model"));
    }

    [Fact]
    public void UnPedidoDuranteElTurnoSaleAlTerminar()
    {
        var s = Gemini();
        s.Ask("uno");
        s.Ask("dos");
        Assert.Single(s.Sent, n => (string?)n["method"] == "session/prompt");

        var id = (int)s.LastOf("session/prompt")!["id"]!;
        s.Feed(Result(id, """{"stopReason":"end_turn"}"""));
        var next = s.LastOf("session/prompt")!;
        Assert.Equal("dos", (string?)next["params"]!["prompt"]![0]!["text"]);
        Assert.True(s.Transcript.Busy);
        Assert.Equal(1, s.Transcript.Blocks.Count(b => b.Kind == AgentBlockKind.Done));
    }

    [Fact]
    public void InterrumpirContestaLosPermisosYCancelaElTurno()
    {
        var s = Copilot();
        s.Ask("crea");
        s.Feed(Update("""{"sessionUpdate":"tool_call","toolCallId":"call_1","title":"Creating C:\\proyecto\\b.txt","kind":"edit","status":"pending","rawInput":{"path":"C:\\proyecto\\b.txt","file_text":"x\n"},"locations":[{"path":"C:\\proyecto\\b.txt"}],"content":[{"type":"diff","path":"C:\\proyecto\\b.txt","oldText":"","newText":"x\n"}]}"""));
        s.Feed("""{"jsonrpc":"2.0","id":7,"method":"session/request_permission","params":{"sessionId":"s-1","toolCall":{"toolCallId":"call_1","title":"Create file","kind":"edit","status":"pending","rawInput":{"fileName":"C:\\proyecto\\b.txt","diff":"@@ -1,0 +1,1 @@\n+x\n"},"locations":[{"path":"C:\\proyecto\\b.txt"}]},"options":[{"optionId":"allow_once","kind":"allow_once","name":"Allow once"},{"optionId":"reject_once","kind":"reject_once","name":"Deny"}]}}""");
        var permission = s.Transcript.Blocks.Single(b => b.Kind == AgentBlockKind.Permission);

        s.Write(s.Driver.Interrupt());
        var cancelled = s.Sent[^2];
        Assert.Equal(7, (int)cancelled["id"]!);
        Assert.Equal("cancelled", (string?)cancelled["result"]!["outcome"]!["outcome"]);
        Assert.Equal("session/cancel", (string?)s.Last["method"]);
        Assert.Null(s.Last["id"]);
        Assert.Equal(PermissionAnswer.Deny, permission.Answer);

        // Los bloques cambiados al cortar salen con la línea siguiente.
        var changed = s.Feed(Update("""{"sessionUpdate":"tool_call_update","toolCallId":"call_1","status":"failed","rawOutput":{"message":"The user rejected this tool call.","code":"rejected"}}"""));
        Assert.Contains(permission, changed);
        s.Feed(Result((int)s.LastOf("session/prompt")!["id"]!, """{"stopReason":"cancelled"}"""));
        var done = s.Transcript.Blocks[^1];
        Assert.Equal(AgentBlockKind.Done, done.Kind);
        Assert.True(done.Failed);
        Assert.Equal(L.T("Se interrumpió."), done.Text);
        Assert.Equal(StepState.Denied, s.Transcript.Blocks.Single(b => b.Kind == AgentBlockKind.Step).State);
    }

    [Fact]
    public void UnErrorDelTurnoLoCierraComoFallido()
    {
        var s = Gemini();
        s.Ask("hola");
        var id = (int)s.LastOf("session/prompt")!["id"]!;
        s.Feed(Error(id, """{"code":429,"message":"Rate limit exceeded. Try again later."}"""));
        var done = s.Transcript.Blocks[^1];
        Assert.Equal((AgentBlockKind.Done, true, "Rate limit exceeded. Try again later."), (done.Kind, done.Failed, done.Text));
        Assert.False(s.Transcript.Busy);
    }

    [Fact]
    public void ElPlanEsLaListaDeTareasDelTurno()
    {
        var s = Copilot();
        s.Ask("planea");
        s.Feed(Update("""{"sessionUpdate":"plan","entries":[{"content":"Leer","priority":"high","status":"completed"},{"content":"Editar","priority":"medium","status":"in_progress"}]}"""));
        s.Feed(Update("""{"sessionUpdate":"plan","entries":[{"content":"Leer","priority":"high","status":"completed"},{"content":"Editar","priority":"medium","status":"completed"}]}"""));
        var todo = Assert.Single(s.Transcript.Blocks, b => b.Kind == AgentBlockKind.Todo);
        Assert.Equal([new TodoItem("Leer", "completed"), new TodoItem("Editar", "completed")], todo.Todos);
    }

    [Fact]
    public void LoQueNoOfrecioLaAppSeContestaQueNoEsta()
    {
        var s = Gemini();
        s.Feed("""{"jsonrpc":"2.0","id":"r-1","method":"fs/read_text_file","params":{"sessionId":"s-1","path":"C:\\proyecto\\a.txt"}}""");
        Assert.Equal("r-1", (string?)s.Last["id"]);
        Assert.Equal(-32601, (int)s.Last["error"]!["code"]!);
        Assert.Empty(s.Feed("no es JSON"));
    }

    [Fact]
    public void ElCambioPorLineasSaltaLoIgualYComparaElMedio()
    {
        var old = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"l{i}")) + "\n";
        var changed = old.Replace("l10\n", "L10\n").Replace("l12\n", "");
        Assert.Equal([" l7", " l8", " l9", "-l10", "+L10", " l11", "-l12", " l13", " l14", " l15"], AgentTranscript.AcpDiff(old, changed));
        Assert.Equal(["+a", "+b"], AgentTranscript.AcpDiff(null, "a\r\nb\r\n"));
        Assert.Empty(AgentTranscript.AcpDiff("igual\n", "igual\n"));
    }

    [Fact]
    public void ReconoceLosErroresDeSesion()
    {
        static JsonElement E(string json) => JsonDocument.Parse(json).RootElement;
        Assert.True(AcpDriver.IsAuthError(E("""{"code":-32000,"message":"Authentication required."}""")));
        Assert.True(AcpDriver.IsAuthError(E("""{"code":-32603,"message":"x","data":{"status":403}}""")));
        Assert.False(AcpDriver.IsAuthError(E("""{"code":-32603,"message":"Internal error","data":{"details":"ENOENT"}}""")));
        Assert.Equal("ENOENT", AcpDriver.ErrorMessage(E("""{"code":-32603,"message":"Internal error","data":{"details":"ENOENT"}}""")));
    }

    [Fact]
    public void SeSaltaElCmdDeNpmYCorreLoQueElCorre()
    {
        var dir = Directory.CreateTempSubdirectory("claupet-acp-").FullName;
        try
        {
            const string Shim = "@ECHO off\r\nGOTO start\r\n:find_dp0\r\nSET dp0=%~dp0\r\nEXIT /b\r\n:start\r\nSETLOCAL\r\nCALL :find_dp0\r\n\r\nIF EXIST \"%dp0%\\node.exe\" (\r\n  SET \"_prog=%dp0%\\node.exe\"\r\n) ELSE (\r\n  SET \"_prog=node\"\r\n  SET PATHEXT=%PATHEXT:;.JS;=;%\r\n)\r\n\r\nendLocal & goto #_undefined_# 2>NUL || title %COMSPEC% & \"%_prog%\"  \"%dp0%\\{0}\" %*\r\n";
            var gemini = Path.Combine(dir, "node_modules", "@google", "gemini-cli", "bundle", "gemini.js");
            Directory.CreateDirectory(Path.GetDirectoryName(gemini)!);
            File.WriteAllText(gemini, "");
            File.WriteAllText(Path.Combine(dir, "gemini.cmd"), Shim.Replace("{0}", @"node_modules\@google\gemini-cli\bundle\gemini.js"));
            Assert.Equal(gemini, AcpDriver.ShimTarget(Path.Combine(dir, "gemini.cmd")));

            // El cargador de Copilot solo abre el ejecutable de su paquete de plataforma: se abre ese directo.
            var loader = Path.Combine(dir, "node_modules", "@github", "copilot", "npm-loader.js");
            Directory.CreateDirectory(Path.GetDirectoryName(loader)!);
            File.WriteAllText(loader, "");
            File.WriteAllText(Path.Combine(dir, "copilot.cmd"), Shim.Replace("{0}", @"node_modules\@github\copilot\npm-loader.js"));
            Assert.Equal(loader, AcpDriver.ShimTarget(Path.Combine(dir, "copilot.cmd")));
            var native = Path.Combine(dir, "node_modules", "@github", $"copilot-win32-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}", "copilot.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(native)!);
            File.WriteAllText(native, "");
            Assert.Equal(native, AcpDriver.ShimTarget(Path.Combine(dir, "copilot.cmd")));

            Assert.Null(AcpDriver.Find("otro"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
