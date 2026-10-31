using System.Text.Json.Nodes;

namespace Claupet.Tests;

/// <summary>
/// Codex en la Terminal: lo que CodexDriver le escribe a «codex app-server» y cómo arma los bloques con lo que responde.
/// Las líneas tienen la forma de las de codex-cli 0.164 (protocolo v2, capturadas del binario real; rutas cambiadas).
/// </summary>
public sealed class CodexDriverTests
{
    const string Thread = "01a19270-5ec5-7682-8d20-b93ef87c7830";
    const string Turn1 = "01a19270-5f04-7321-b6cd-1c5cadfa041a";

    const string ModelList = """{"id":2,"result":{"data":[{"id":"gpt-6.1-sol","model":"gpt-6.1-sol","upgrade":null,"displayName":"GPT-6.1-Sol","description":"Latest workhorse model for coding and everyday work.","hidden":false,"supportedReasoningEfforts":[{"reasoningEffort":"low","description":"Fast responses with lighter reasoning"},{"reasoningEffort":"medium","description":"Balances speed and reasoning depth for everyday tasks"},{"reasoningEffort":"high","description":"Greater reasoning depth for complex problems"},{"reasoningEffort":"xhigh","description":"Extra high reasoning depth for complex problems"},{"reasoningEffort":"max","description":"Maximum reasoning depth for the hardest problems"},{"reasoningEffort":"ultra","description":"Maximum reasoning with automatic task delegation"}],"defaultReasoningEffort":"low","inputModalities":["text","image"],"isDefault":true},{"id":"gpt-5.6-luna","model":"gpt-5.6-luna","upgrade":null,"displayName":"GPT-5.6-Luna","description":"","hidden":false,"supportedReasoningEfforts":[{"reasoningEffort":"low","description":""},{"reasoningEffort":"medium","description":""},{"reasoningEffort":"high","description":""},{"reasoningEffort":"xhigh","description":""},{"reasoningEffort":"max","description":""}],"defaultReasoningEffort":"medium","isDefault":false},{"id":"gpt-oculto","model":"gpt-oculto","hidden":true,"supportedReasoningEfforts":[],"isDefault":false}],"nextCursor":null}}""";

    const string ThreadStarted = """{"id":3,"result":{"thread":{"id":"01a19270-5ec5-7682-8d20-b93ef87c7830","sessionId":"01a19270-5ec5-7682-8d20-b93ef87c7830","preview":"","ephemeral":false,"modelProvider":"openai","model":"gpt-5.6-luna","reasoningEffort":"low","status":{"type":"idle"},"cwd":"C:\\proyecto","cliVersion":"0.164.0-alpha.11.1","turns":[]},"model":"gpt-5.6-luna","modelProvider":"openai","serviceTier":null,"disabledPluginIds":[],"cwd":"C:\\proyecto","instructionSources":[],"approvalPolicy":"untrusted","approvalsReviewer":"user","sandbox":{"type":"readOnly","networkAccess":false},"activePermissionProfile":null,"reasoningEffort":"low"}}""";

    static readonly string TurnStarted = $$$"""{"method":"turn/started","params":{"threadId":"{{{Thread}}}","turn":{"id":"{{{Turn1}}}","rootTurnId":"{{{Turn1}}}","items":[],"itemsView":"notLoaded","status":"inProgress","error":null,"startedAt":1793458200,"completedAt":null,"durationMs":null}},"emittedAtMs":1793458200363}""";

    static string Completed(string status = "completed", string error = "null", int ms = 938) =>
        $$$"""{"method":"turn/completed","params":{"threadId":"{{{Thread}}}","turn":{"id":"{{{Turn1}}}","rootTurnId":"{{{Turn1}}}","items":[],"itemsView":"summary","status":"{{{status}}}","error":{{{error}}},"startedAt":1793458200,"completedAt":1793458201,"durationMs":{{{ms}}}}},"emittedAtMs":1793458201}""";

    static string Item(string kind, string item) =>
        $$"""{"method":"item/{{kind}}","params":{"item":{{item}},"threadId":"{{Thread}}","turnId":"{{Turn1}}","startedAtMs":1793458200410},"emittedAtMs":1793458200410}""";

    static string Delta(string method, string itemId, string delta) =>
        $$"""{"method":"{{method}}","params":{"threadId":"{{Thread}}","turnId":"{{Turn1}}","itemId":"{{itemId}}","delta":{{JsonValue.Create(delta).ToJsonString()}}},"emittedAtMs":1}""";

    const string Command = """{"type":"commandExecution","id":"exec-b70e2dcf","command":"\"C:\\\\WINDOWS\\\\System32\\\\WindowsPowerShell\\\\v1.0\\\\powershell.exe\" -Command 'Write-Output hola; Set-Content -Path nota.txt -Value hola'","cwd":"C:\\proyecto","processId":null,"source":"agent","status":"inProgress","commandActions":[{"type":"unknown","command":"Write-Output hola; Set-Content -Path nota.txt -Value hola"}],"aggregatedOutput":null,"exitCode":null,"durationMs":null}""";

    const string CommandApproval = """{"method":"item/commandExecution/requestApproval","id":0,"params":{"kind":"command","threadId":"01a19270-5ec5-7682-8d20-b93ef87c7830","turnId":"01a19270-5f04-7321-b6cd-1c5cadfa041a","itemId":"exec-b70e2dcf","startedAtMs":1793458200786,"environmentId":"local","reason":"Para probar","command":"\"C:\\\\WINDOWS\\\\System32\\\\WindowsPowerShell\\\\v1.0\\\\powershell.exe\" -Command 'Write-Output hola; Set-Content -Path nota.txt -Value hola'","cwd":"C:\\proyecto","commandActions":[{"type":"unknown","command":"Write-Output hola; Set-Content -Path nota.txt -Value hola"}],"proposedExecpolicyAmendment":["Write-Output","hola"],"availableDecisions":["accept",{"acceptWithExecpolicyAmendment":{"execpolicy_amendment":["Write-Output","hola"]}},"cancel"]}}""";

    const string FileAdd = """{"type":"fileChange","id":"exec-fdd08519","changes":[{"path":"C:\\proyecto\\saludo.txt","kind":{"type":"add"},"diff":"hola\nmundo\n"}],"status":"inProgress"}""";

    const string FileApproval = """{"method":"item/fileChange/requestApproval","id":1,"params":{"threadId":"01a19270-5ec5-7682-8d20-b93ef87c7830","turnId":"01a19270-5f04-7321-b6cd-1c5cadfa041a","itemId":"exec-fdd08519","startedAtMs":1793458201388,"reason":null,"grantRoot":null}}""";

    /// <summary>Una sesión abierta: initialize, model/list y thread/start ya respondidos.</summary>
    static (AgentTranscript Transcript, CodexDriver Driver, List<string> Sent) Opened(string? model = "gpt-5.6-luna", string? effort = "low", string? mode = null)
    {
        var t = new AgentTranscript();
        var d = new CodexDriver(t);
        var sent = new List<string>(d.Opening(new AgentOptions(@"C:\proyecto", Model: model, Effort: effort, PermissionMode: mode, Kind: AgentKinds.Codex)));
        d.Apply("""{"id":1,"result":{"userAgent":"claupet/0.164.0-alpha.11.1","codexHome":"C:\\codex","platformFamily":"windows","platformOs":"windows"}}""", sent);
        d.Apply(ModelList, sent);
        d.Apply(ThreadStarted, sent);
        return (t, d, sent);
    }

    static JsonNode Parse(string line) => JsonNode.Parse(line)!;

    static List<AgentBlock> Feed(CodexDriver d, List<string> replies, params string[] lines)
    {
        var changed = new List<AgentBlock>();
        foreach (var line in lines) changed.AddRange(d.Apply(line, replies));
        return changed;
    }

    [Fact]
    public void AlAbrirSaludaPideLosModelosYAbreLaSesionEnLaCarpeta()
    {
        var t = new AgentTranscript();
        var d = new CodexDriver(t);
        var init = Parse(Assert.Single(d.Opening(new AgentOptions(@"C:\proyecto", Model: "gpt-5.6-luna", Effort: "low", Kind: AgentKinds.Codex))));
        Assert.Equal("initialize", (string?)init["method"]);
        Assert.Equal("claupet", (string?)init["params"]!["clientInfo"]!["name"]);
        Assert.True((bool)init["params"]!["capabilities"]!["experimentalApi"]!);

        // Lo que se pide antes de que exista la sesión queda en cola.
        Assert.Empty(d.Prompt("di hola"));

        var replies = new List<string>();
        d.Apply("""{"id":1,"result":{"userAgent":"claupet/0.164.0","codexHome":"C:\\codex","platformFamily":"windows","platformOs":"windows"}}""", replies);
        Assert.Equal(["initialized", "model/list"], replies.Select(r => (string?)Parse(r)["method"]));
        Assert.Null(Parse(replies[0])["id"]);

        replies.Clear();
        d.Apply(ModelList, replies);
        var start = Parse(Assert.Single(replies));
        Assert.Equal("thread/start", (string?)start["method"]);
        Assert.Equal(@"C:\proyecto", (string?)start["params"]!["cwd"]);
        Assert.Equal("gpt-5.6-luna", (string?)start["params"]!["model"]);
        Assert.Equal("untrusted", (string?)start["params"]!["approvalPolicy"]);
        Assert.Equal("workspace-write", (string?)start["params"]!["sandbox"]);
        // Los modelos ocultos no se ofrecen; los esfuerzos son los del modelo elegido.
        Assert.Equal(["gpt-6.1-sol", "gpt-5.6-luna"], d.Models);
        Assert.Equal(["low", "medium", "high", "xhigh", "max"], d.Efforts);
        Assert.Equal(["default", "acceptEdits", "plan", "auto"], d.Modes);

        replies.Clear();
        d.Apply(ThreadStarted, replies);
        Assert.Equal(Thread, t.SessionId);
        Assert.Equal("gpt-5.6-luna", t.Model);
        Assert.Equal(@"C:\proyecto", t.Cwd);
        Assert.Equal("default", t.PermissionMode);
        var turn = Parse(Assert.Single(replies));
        Assert.Equal("turn/start", (string?)turn["method"]);
        Assert.Equal(Thread, (string?)turn["params"]!["threadId"]);
        Assert.Equal("di hola", (string?)turn["params"]!["input"]![0]!["text"]);
        Assert.Equal("text", (string?)turn["params"]!["input"]![0]!["type"]);
        Assert.Equal("low", (string?)turn["params"]!["effort"]);
        Assert.Null(turn["params"]!["model"]);
        Assert.True(t.Busy);
    }

    [Fact]
    public void UnModeloQueCodexNoListaNoSePasa()
    {
        var t = new AgentTranscript();
        var d = new CodexDriver(t);
        var replies = new List<string>(d.Opening(new AgentOptions(@"C:\proyecto", Model: "sonnet", Kind: AgentKinds.Codex)));
        d.Apply("""{"id":1,"result":{}}""", replies);
        replies.Clear();
        d.Apply(ModelList, replies);
        Assert.Null(Parse(Assert.Single(replies))["params"]!["model"]);
    }

    [Fact]
    public void LaRespuestaLlegaPorPartesYElTurnoCierraConTokensYDuracion()
    {
        var (t, d, _) = Opened();
        var replies = new List<string>();
        t.Ask("di hola");
        d.Prompt("di hola");
        Feed(d, replies,
            TurnStarted,
            Item("started", """{"type":"userMessage","id":"u1","clientId":null,"content":[{"type":"text","text":"di hola","text_elements":[]}]}"""),
            Item("started", """{"type":"reasoning","id":"rs_1","summary":[],"content":[]}"""),
            Delta("item/reasoning/summaryTextDelta", "rs_1", "Pensando "));
        var think = Assert.Single(t.Blocks, b => b.Kind == AgentBlockKind.Think);
        Assert.True(think.Open);

        Feed(d, replies,
            Item("completed", """{"type":"reasoning","id":"rs_1","summary":["Pensando un poco"],"content":[]}"""),
            Item("started", """{"type":"agentMessage","id":"msg_1","text":"","phase":null}"""),
            Delta("item/agentMessage/delta", "msg_1", "ho"),
            Delta("item/agentMessage/delta", "msg_1", "la"));
        var say = Assert.Single(t.Blocks, b => b.Kind == AgentBlockKind.Say);
        Assert.Equal("hola", say.Text);
        Assert.True(say.Open);
        Assert.False(think.Open);
        Assert.Equal("Pensando un poco", think.Text);

        Feed(d, replies,
            Item("completed", """{"type":"agentMessage","id":"msg_1","text":"hola.","phase":null}"""),
            $$$$"""{"method":"thread/tokenUsage/updated","params":{"threadId":"{{{{Thread}}}}","turnId":"{{{{Turn1}}}}","tokenUsage":{"total":{"totalTokens":1230,"inputTokens":1200,"cachedInputTokens":200,"cacheWriteInputTokens":0,"outputTokens":30,"reasoningOutputTokens":10},"last":{"totalTokens":1230,"inputTokens":1200,"cachedInputTokens":200,"cacheWriteInputTokens":0,"outputTokens":30,"reasoningOutputTokens":10},"modelContextWindow":258400}}}""",
            """{"method":"account/rateLimits/updated","params":{"rateLimits":{"limitId":"codex","primary":{"usedPercent":23.0,"windowDurationMins":300,"resetsAt":1793470000},"secondary":{"usedPercent":61.5,"windowDurationMins":10080,"resetsAt":1793900000}}}}""",
            Completed(ms: 64));
        Assert.Equal("hola.", say.Text);
        Assert.False(say.Open);
        Assert.Equal(10, think.Tokens);
        Assert.Equal(1230, t.ContextUsed);
        Assert.Equal(258400, t.ContextWindow);
        Assert.Equal(23.0, t.FiveHour);
        Assert.Equal(61.5, t.SevenDay);
        Assert.False(t.Busy);
        var done = t.Blocks[^1];
        Assert.Equal(AgentBlockKind.Done, done.Kind);
        Assert.False(done.Failed);
        Assert.Equal(1230, done.Tokens);
        Assert.Equal(TimeSpan.FromMilliseconds(64), done.Duration);
        Assert.Empty(replies);
    }

    [Fact]
    public void UnComandoPidePermisoYSeVeSinElShellQueLoEnvuelve()
    {
        var (t, d, _) = Opened();
        var replies = new List<string>();
        Feed(d, replies, TurnStarted, Item("started", Command), CommandApproval);

        var step = Assert.Single(t.Blocks, b => b.Kind == AgentBlockKind.Step);
        Assert.Equal("Bash", step.Tool);
        Assert.Equal("Write-Output hola; Set-Content -Path nota.txt -Value hola", step.Text);
        Assert.Equal(StepState.Waiting, step.State);
        var permission = t.Blocks[^1];
        Assert.Equal(AgentBlockKind.Permission, permission.Kind);
        Assert.Equal("Bash", permission.Tool);
        Assert.Equal("0", permission.RequestId);
        Assert.Equal("exec-b70e2dcf", permission.StepId);
        Assert.Equal("Write-Output hola; Set-Content -Path nota.txt -Value hola", (string?)permission.Input!["command"]);
        Assert.Equal("Para probar", (string?)permission.Input!["description"]);
        Assert.Empty(replies);

        var allow = Parse(Assert.Single(d.Answer(permission, PermissionAnswer.Allow)));
        Assert.Equal(0, (int)allow["id"]!);
        Assert.Equal("accept", (string?)allow["result"]!["decision"]);
        // Este comando no ofrece acceptForSession ni decline: «siempre» es la regla que propone y «rechazar», cancel.
        var always = Parse(Assert.Single(d.Answer(permission, PermissionAnswer.AllowAlways)));
        Assert.Equal("Write-Output", (string?)always["result"]!["decision"]!["acceptWithExecpolicyAmendment"]!["execpolicy_amendment"]![0]);
        Assert.Equal("cancel", (string?)Parse(Assert.Single(d.Answer(permission, PermissionAnswer.Deny)))["result"]!["decision"]);

        Feed(d, replies,
            """{"method":"serverRequest/resolved","params":{"threadId":"01a19270-5ec5-7682-8d20-b93ef87c7830","requestId":0}}""",
            Delta("item/commandExecution/outputDelta", "exec-b70e2dcf", "hola"),
            Delta("item/commandExecution/outputDelta", "exec-b70e2dcf", "\r\n"));
        Assert.Equal("hola", step.Output);
        Feed(d, replies, Item("completed", Command.Replace("\"status\":\"inProgress\"", "\"status\":\"completed\"")
            .Replace("\"aggregatedOutput\":null", "\"aggregatedOutput\":\"hola\\r\\n\"").Replace("\"exitCode\":null", "\"exitCode\":0")));
        Assert.Equal(StepState.Done, step.State);
        Assert.Equal("hola", step.Output);
    }

    [Fact]
    public void UnaLecturaSeNombraComoLectura()
    {
        var (t, d, _) = Opened();
        Feed(d, [], TurnStarted, Item("started", """{"type":"commandExecution","id":"exec-1","command":"\"C:\\\\WINDOWS\\\\System32\\\\WindowsPowerShell\\\\v1.0\\\\powershell.exe\" -Command 'Get-Content -Path src\\app.cs'","cwd":"C:\\proyecto","source":"agent","status":"inProgress","commandActions":[{"type":"read","command":"Get-Content -Path src\\app.cs","name":"app.cs","path":"C:\\proyecto\\src\\app.cs"}]}"""));
        var step = Assert.Single(t.Blocks);
        Assert.Equal("Read", step.Tool);
        Assert.Equal("src/app.cs", step.Text);
    }

    [Fact]
    public void UnArchivoNuevoMuestraSuCambioYSiempreEsParaLaSesion()
    {
        var (t, d, _) = Opened();
        var replies = new List<string>();
        Feed(d, replies, TurnStarted, Item("started", FileAdd), FileApproval);

        var step = Assert.Single(t.Blocks, b => b.Kind == AgentBlockKind.Step);
        Assert.Equal("Write", step.Tool);
        Assert.Equal("saludo.txt", step.Text);
        Assert.Equal(["+hola", "+mundo"], step.Diff);
        Assert.Equal(2, step.Added);
        var permission = t.Blocks[^1];
        Assert.Equal("Write", permission.Tool);
        Assert.Equal("saludo.txt", permission.Text);
        Assert.Equal(@"C:\proyecto\saludo.txt", (string?)permission.Input!["file_path"]);
        Assert.Equal("hola\nmundo\n", (string?)permission.Input!["content"]);

        Assert.Equal("acceptForSession", (string?)Parse(Assert.Single(d.Answer(permission, PermissionAnswer.AllowAlways)))["result"]!["decision"]);
        Assert.Equal("decline", (string?)Parse(Assert.Single(d.Answer(permission, PermissionAnswer.Deny)))["result"]!["decision"]);

        Feed(d, replies, Item("completed", FileAdd.Replace("inProgress", "completed")));
        Assert.Equal(StepState.Done, step.State);
    }

    [Fact]
    public void UnaEdicionMuestraLasLineasQueCambian()
    {
        var (t, d, _) = Opened();
        Feed(d, [], TurnStarted, Item("started", """{"type":"fileChange","id":"exec-e1188b90","changes":[{"path":"C:\\proyecto\\saludo.txt","kind":{"type":"update","move_path":null},"diff":"@@ -1,2 +1,2 @@\n hola\n-mundo\n+gente\n"}],"status":"inProgress"}"""),
            """{"method":"item/fileChange/requestApproval","id":2,"params":{"threadId":"01a19270-5ec5-7682-8d20-b93ef87c7830","turnId":"01a19270-5f04-7321-b6cd-1c5cadfa041a","itemId":"exec-e1188b90","startedAtMs":1}}""");
        var step = Assert.Single(t.Blocks, b => b.Kind == AgentBlockKind.Step);
        Assert.Equal("Edit", step.Tool);
        Assert.Equal([" hola", "-mundo", "+gente"], step.Diff);
        Assert.Equal(1, step.Added);
        Assert.Equal(1, step.Removed);
        var permission = t.Blocks[^1];
        Assert.Equal("mundo", (string?)permission.Input!["old_string"]);
        Assert.Equal("gente", (string?)permission.Input!["new_string"]);
    }

    [Fact]
    public void LaIslaContestaComoElBloque()
    {
        var (t, d, _) = Opened();
        Feed(d, [], TurnStarted, Item("started", FileAdd), FileApproval);
        var permission = t.Blocks[^1];

        var allow = d.FromIsland(permission, """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"allow"}}}""");
        Assert.Equal(PermissionAnswer.Allow, allow!.Value.Answer);
        Assert.Equal("accept", (string?)Parse(Assert.Single(allow.Value.Lines))["result"]!["decision"]);
        var deny = d.FromIsland(permission, """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"deny","message":"no"}}}""");
        Assert.Equal(PermissionAnswer.Deny, deny!.Value.Answer);
        Assert.Equal("decline", (string?)Parse(Assert.Single(deny.Value.Lines))["result"]!["decision"]);
        Assert.Null(d.FromIsland(permission, "no es json"));
    }

    [Fact]
    public void LasPreguntasDelModoPlanSeRespondenPorSuId()
    {
        var (t, d, _) = Opened();
        Feed(d, [], TurnStarted, """{"method":"item/tool/requestUserInput","id":0,"params":{"threadId":"01a19270-5ec5-7682-8d20-b93ef87c7830","turnId":"01a19270-5f04-7321-b6cd-1c5cadfa041a","itemId":"call_1","questions":[{"id":"color","header":"Color","question":"¿Qué color prefiere?","isOther":false,"isSecret":false,"options":[{"label":"Rojo","description":"Cálido"},{"label":"Azul","description":"Frío"}]}],"isBlocking":true,"autoResolutionMs":null}}""");
        var permission = t.Blocks[^1];
        Assert.Equal("AskUserQuestion", permission.Tool);
        var question = Assert.Single(permission.Questions!);
        Assert.Equal("¿Qué color prefiere?", question.Question);
        Assert.Equal(["Rojo", "Azul"], question.Options.Select(o => o.Label));

        var reply = Parse(Assert.Single(d.Answer(permission, new Dictionary<string, IReadOnlyList<string>> { ["¿Qué color prefiere?"] = ["Azul"] })));
        Assert.Equal("Azul", (string?)reply["result"]!["answers"]!["color"]!["answers"]![0]);

        var island = d.FromIsland(permission, """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"allow","updatedInput":{"answers":{"¿Qué color prefiere?":"Rojo"}}}}}""");
        Assert.Equal("Rojo", (string?)Parse(Assert.Single(island!.Value.Lines))["result"]!["answers"]!["color"]!["answers"]![0]);
        Assert.Equal(["Rojo"], permission.Picks!["¿Qué color prefiere?"]);
    }

    [Fact]
    public void ElModeloElEsfuerzoYElModoVanEnElTurnoSiguiente()
    {
        var (t, d, sent) = Opened();
        Assert.Empty(d.SetModel("gpt-6.1-sol"));
        Assert.Empty(d.SetModel("modelo-inventado"));
        Assert.Equal(["low", "medium", "high", "xhigh", "max", "ultra"], d.Efforts);
        Assert.Empty(d.SetEffort("ultra"));
        Assert.Empty(d.SetMode("plan"));
        Assert.Empty(d.SetMode("inventado"));
        Assert.Equal("gpt-6.1-sol", t.Model);
        Assert.Equal("plan", t.PermissionMode);

        var turn = Parse(Assert.Single(d.Prompt("planea")))["params"]!;
        Assert.Equal("gpt-6.1-sol", (string?)turn["model"]);
        Assert.Equal("ultra", (string?)turn["effort"]);
        Assert.Equal("on-request", (string?)turn["approvalPolicy"]);
        Assert.Equal("readOnly", (string?)turn["sandboxPolicy"]!["type"]);
        Assert.Equal("plan", (string?)turn["collaborationMode"]!["mode"]);
        Assert.Equal("gpt-6.1-sol", (string?)turn["collaborationMode"]!["settings"]!["model"]);

        // Al volver a un modo que escribe: el sandbox que Codex dio al abrir y una vez el modo normal.
        var replies = new List<string>();
        Feed(d, replies, """{"id":4,"result":{"turn":{"id":"t-1","items":[],"status":"inProgress"}}}""", Completed());
        Assert.Empty(d.SetMode("auto"));
        turn = Parse(Assert.Single(d.Prompt("hazlo")))["params"]!;
        Assert.Equal("never", (string?)turn["approvalPolicy"]);
        Assert.Equal("readOnly", (string?)turn["sandboxPolicy"]!["type"]);
        Assert.Equal("default", (string?)turn["collaborationMode"]!["mode"]);
        Assert.Null(turn["model"]);

        Feed(d, replies, """{"id":5,"result":{"turn":{"id":"t-2","items":[],"status":"inProgress"}}}""", Completed());
        turn = Parse(Assert.Single(d.Prompt("otra")))["params"]!;
        Assert.Null(turn["collaborationMode"]);
        Assert.Null(turn["approvalPolicy"]);
    }

    [Fact]
    public void LoQueSePideDuranteUnTurnoSaleAlTerminar()
    {
        var (t, d, _) = Opened();
        Assert.Single(d.Prompt("uno"));
        Assert.Empty(d.Prompt("dos"));
        var replies = new List<string>();
        Feed(d, replies, """{"id":4,"result":{"turn":{"id":"01a19270-5f04-7321-b6cd-1c5cadfa041a","items":[],"status":"inProgress"}}}""", TurnStarted);
        Assert.Empty(replies);
        Feed(d, replies, Completed());
        var next = Parse(Assert.Single(replies));
        Assert.Equal("dos", (string?)next["params"]!["input"]![0]!["text"]);
        Assert.True(t.Busy);
    }

    [Fact]
    public void InterrumpirCortaElTurnoAunqueTodaviaNoSepaSuId()
    {
        var (t, d, _) = Opened();
        d.Prompt("escribe lento");
        // El turn/start salió pero su respuesta no llegó: se corta apenas llegue.
        Assert.Empty(d.Interrupt());
        var replies = new List<string>();
        Feed(d, replies, """{"id":4,"result":{"turn":{"id":"01a19270-6d9b-70c0-97e4-81d4a3b135bd","items":[],"status":"inProgress"}}}""");
        var interrupt = Parse(Assert.Single(replies));
        Assert.Equal("turn/interrupt", (string?)interrupt["method"]);
        Assert.Equal("01a19270-6d9b-70c0-97e4-81d4a3b135bd", (string?)interrupt["params"]!["turnId"]);
        Assert.Equal(Thread, (string?)interrupt["params"]!["threadId"]);
        // Con el id ya sabido, sale enseguida.
        Assert.Equal("turn/interrupt", (string?)Parse(Assert.Single(d.Interrupt()))["method"]);

        Feed(d, replies,
            Item("started", """{"type":"agentMessage","id":"msg_8","text":""}"""),
            Delta("item/agentMessage/delta", "msg_8", "palabra "),
            Completed("interrupted", ms: 3026));
        var say = Assert.Single(t.Blocks, b => b.Kind == AgentBlockKind.Say);
        Assert.False(say.Open);
        var done = t.Blocks[^1];
        Assert.True(done.Failed);
        Assert.Equal(L.T("Se interrumpió."), done.Text);
        Assert.False(t.Busy);
    }

    [Fact]
    public void LosReintentosSonUnSoloAvisoYElErrorVaEnElCierre()
    {
        var (t, d, _) = Opened();
        t.Ask("hola");
        d.Prompt("hola");
        Feed(d, [], TurnStarted,
            $$$"""{"method":"error","params":{"error":{"message":"Reconnecting... 2/5","codexErrorInfo":{"responseStreamDisconnected":{"httpStatusCode":null}},"additionalDetails":"workspace routing discovery unauthorized (401)","misalignment":null},"willRetry":true,"threadId":"{{{Thread}}}","turnId":"{{{Turn1}}}"}}""",
            $$$"""{"method":"error","params":{"error":{"message":"Reconnecting... 3/5","codexErrorInfo":{"responseStreamDisconnected":{"httpStatusCode":null}},"additionalDetails":"workspace routing discovery unauthorized (401)","misalignment":null},"willRetry":true,"threadId":"{{{Thread}}}","turnId":"{{{Turn1}}}"}}""",
            $$$"""{"method":"error","params":{"error":{"message":"workspace routing discovery unauthorized (401)","codexErrorInfo":"other","additionalDetails":null,"misalignment":null},"willRetry":false,"threadId":"{{{Thread}}}","turnId":"{{{Turn1}}}"}}""",
            Completed("failed", """{"message":"workspace routing discovery unauthorized (401)","codexErrorInfo":"other","additionalDetails":null,"misalignment":null}""", 26407));
        var notice = Assert.Single(t.Blocks, b => b.Kind == AgentBlockKind.Notice);
        Assert.Equal("Reconnecting... 3/5 · workspace routing discovery unauthorized (401)", notice.Text);
        var done = t.Blocks[^1];
        Assert.True(done.Failed);
        Assert.Equal("workspace routing discovery unauthorized (401)", done.Text);
        Assert.Equal(TimeSpan.FromMilliseconds(26407), done.Duration);
    }

    [Fact]
    public void UnTurnoRechazadoQuedaComoAvisoYSigueLaCola()
    {
        var (t, d, _) = Opened();
        d.Prompt("uno");
        d.Prompt("dos");
        var replies = new List<string>();
        Feed(d, replies, """{"id":4,"error":{"code":-32600,"message":"model not supported"}}""");
        Assert.Equal("model not supported", t.Blocks[^1].Text);
        Assert.Equal("dos", (string?)Parse(Assert.Single(replies))["params"]!["input"]![0]!["text"]);
    }

    [Fact]
    public void SiNoAbreLaSesionLoDiceYNoMandaNada()
    {
        var t = new AgentTranscript();
        var d = new CodexDriver(t);
        var replies = new List<string>(d.Opening(new AgentOptions(@"C:\proyecto", Kind: AgentKinds.Codex)));
        d.Prompt("hola");
        replies.Clear();
        d.Apply("""{"id":1,"error":{"code":-32603,"message":"not logged in"}}""", replies);
        Assert.Empty(replies);
        Assert.Equal(AgentBlockKind.Notice, t.Blocks[^1].Kind);
        Assert.Contains("not logged in", t.Blocks[^1].Text);
        Assert.False(t.Busy);
    }

    [Fact]
    public void LosPedidosQueNoSeMuestranSeContestanSolos()
    {
        var (t, d, _) = Opened();
        var replies = new List<string>();
        Feed(d, replies,
            """{"method":"account/chatgptAuthTokens/refresh","id":7,"params":{}}""",
            """{"method":"mcpServer/elicitation/request","id":"e-1","params":{"threadId":"01a19270-5ec5-7682-8d20-b93ef87c7830"}}""");
        Assert.Equal(-32601, (int)Parse(replies[0])["error"]!["code"]!);
        Assert.Equal(7, (int)Parse(replies[0])["id"]!);
        Assert.Equal("e-1", (string?)Parse(replies[1])["id"]);
        Assert.Equal("decline", (string?)Parse(replies[1])["result"]!["action"]);
        Assert.Empty(t.Blocks);
    }

    [Fact]
    public void UnPermisoRetiradoPorElServidorQuedaCerrado()
    {
        var (t, d, _) = Opened();
        Feed(d, [], TurnStarted, Item("started", Command), CommandApproval, Completed("interrupted"),
            """{"method":"serverRequest/resolved","params":{"threadId":"01a19270-5ec5-7682-8d20-b93ef87c7830","requestId":0}}""");
        var permission = Assert.Single(t.Blocks, b => b.Kind == AgentBlockKind.Permission);
        Assert.NotNull(permission.Answer);
        Assert.Equal(StepState.Failed, Assert.Single(t.Blocks, b => b.Kind == AgentBlockKind.Step).State);
        Assert.Empty(d.Answer(permission, PermissionAnswer.Allow));
    }

    [Fact]
    public void LoDeOtraSesionNoCambiaNada()
    {
        var (t, d, _) = Opened();
        var changed = Feed(d, [], """{"method":"item/started","params":{"item":{"type":"agentMessage","id":"m","text":""},"threadId":"otra","turnId":"x","startedAtMs":1}}""");
        Assert.Empty(changed);
        Assert.Empty(t.Blocks);
        Assert.Empty(d.Apply("no es json", []));
    }

    [Fact]
    public void ElPlanDeCodexEsLaListaDeTareas()
    {
        var (t, d, _) = Opened();
        Feed(d, [], TurnStarted, $$$"""{"method":"turn/plan/updated","params":{"threadId":"{{{Thread}}}","turnId":"{{{Turn1}}}","explanation":null,"plan":[{"step":"Leer","status":"completed"},{"step":"Escribir","status":"inProgress"},{"step":"Probar","status":"pending"}]}}""");
        var todo = Assert.Single(t.Blocks);
        Assert.Equal(AgentBlockKind.Todo, todo.Kind);
        Assert.Equal(["completed", "in_progress", "pending"], todo.Todos.Select(x => x.Status));
        Assert.Equal("Escribir", todo.Todos[1].Text);
    }

    [Fact]
    public void UnComandoQueTerminaMalQuedaConError()
    {
        var (t, d, _) = Opened();
        // En Windows el código de salida puede pasar de int (0xC0000005).
        Feed(d, [], TurnStarted, Item("started", Command), Item("completed", Command.Replace("\"status\":\"inProgress\"", "\"status\":\"completed\"")
            .Replace("\"aggregatedOutput\":null", "\"aggregatedOutput\":\"\"").Replace("\"exitCode\":null", "\"exitCode\":3221225477")));
        Assert.Equal(StepState.Failed, Assert.Single(t.Blocks).State);
    }

    [Fact]
    public void InterrumpirAntesDeQueAbraLaSesionVaciaLaCola()
    {
        var t = new AgentTranscript();
        var d = new CodexDriver(t);
        var replies = new List<string>(d.Opening(new AgentOptions(@"C:\proyecto", Kind: AgentKinds.Codex)));
        t.Ask("hola");
        Assert.Empty(d.Prompt("hola"));
        Assert.Empty(d.Interrupt());
        Assert.False(t.Busy);
        d.Apply("""{"id":1,"result":{}}""", replies);
        replies.Clear();
        d.Apply(ModelList, replies);
        replies.Clear();
        d.Apply(ThreadStarted, replies);
        Assert.Empty(replies);
    }
}
