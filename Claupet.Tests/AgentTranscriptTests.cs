using System.Text.Json.Nodes;

namespace Claupet.Tests;

/// <summary>
/// Las sesiones de agente de la Terminal en bloques: cómo se leen los eventos de Claude Code en modo estructurado. Las
/// líneas tienen la forma de las de Claude Code 2.1 (-p con stream-json, --verbose y --include-partial-messages).
/// </summary>
public sealed class AgentTranscriptTests
{
    const string Cwd = @"C:\proyecto";

    static AgentTranscript Started()
    {
        var t = new AgentTranscript();
        t.Apply("""{"type":"system","subtype":"init","cwd":"C:\\proyecto","session_id":"s-1","model":"claude-haiku-5-5","permissionMode":"default"}""");
        return t;
    }

    static string Partial(string ev) => $$"""{"type":"stream_event","event":{{ev}}}""";

    [Fact]
    public void ElModeloYElModoCambianConLaRespuestaDeClaudeCode()
    {
        var t = Started();
        // El eco del cambio de modelo no es un pedido de la persona.
        var replay = t.Apply("""{"type":"user","isReplay":true,"message":{"role":"user","content":"<local-command-stdout>Set model to sonnet</local-command-stdout>"}}""");
        t.Apply("""{"type":"control_response","response":{"subtype":"success","request_id":"c-1","response":{"model":"claude-sonnet-5-5"}}}""");
        t.Apply("""{"type":"control_response","response":{"subtype":"success","request_id":"c-2","response":{"mode":"plan"}}}""");

        Assert.Empty(replay);
        Assert.Empty(t.Blocks);
        Assert.Equal("claude-sonnet-5-5", t.Model);
        Assert.Equal("plan", t.PermissionMode);

        t.Apply("""{"type":"system","subtype":"status","permissionMode":"acceptEdits"}""");
        t.Apply("""{"type":"assistant","message":{"id":"m1","model":"claude-opus-5-5","content":[{"type":"text","text":"Hecho."}]}}""");
        Assert.Equal("acceptEdits", t.PermissionMode);
        Assert.Equal("claude-opus-5-5", t.Model);
    }

    [Fact]
    public void UnCambioRechazadoQuedaComoAviso()
    {
        var t = Started();
        t.Apply("""{"type":"control_response","response":{"subtype":"error","request_id":"c-1","error":"Model not found: gpt"}}""");

        var notice = Assert.Single(t.Blocks);
        Assert.Equal(AgentBlockKind.Notice, notice.Kind);
        Assert.Equal("Model not found: gpt", notice.Text);
        Assert.Equal("claude-haiku-5-5", t.Model);
    }

    [Fact]
    public void LosCambiosDeLaSesionSonPedidosDeControl()
    {
        var model = JsonNode.Parse(AgentProtocol.SetModel("c-1", "sonnet"))!;
        Assert.Equal("set_model", (string?)model["request"]!["subtype"]);
        Assert.Equal("sonnet", (string?)model["request"]!["model"]);
        Assert.Null(JsonNode.Parse(AgentProtocol.SetModel("c-2", null))!["request"]!["model"]);
        Assert.Equal("plan", (string?)JsonNode.Parse(AgentProtocol.SetPermissionMode("c-3", "plan"))!["request"]!["mode"]);
        var effort = JsonNode.Parse(AgentProtocol.SetEffort("c-4", "high"))!["request"]!;
        Assert.Equal("apply_flag_settings", (string?)effort["subtype"]);
        Assert.Equal("high", (string?)effort["settings"]!["effortLevel"]);
    }

    [Fact]
    public void ArmaLaRespuestaPorPartesYLaCierraConElMensajeCompleto()
    {
        var t = Started();
        t.Ask("¿Qué hay aquí?");
        Assert.True(t.Busy);

        t.Apply(Partial("""{"type":"message_start","message":{"id":"m1","usage":{"input_tokens":3,"cache_read_input_tokens":1000}}}"""));
        t.Apply(Partial("""{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}"""));
        t.Apply(Partial("""{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hay "}}"""));
        var changed = t.Apply(Partial("""{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"dos"}}"""));

        var say = Assert.Single(changed);
        Assert.Equal(AgentBlockKind.Say, say.Kind);
        Assert.Equal("Hay dos", say.Text);
        Assert.True(say.Open);

        t.Apply("""{"type":"assistant","message":{"id":"m1","content":[{"type":"text","text":"Hay dos archivos."}]}}""");
        Assert.Equal([AgentBlockKind.Ask, AgentBlockKind.Say], t.Blocks.Select(b => b.Kind));
        Assert.Equal("Hay dos archivos.", say.Text);
        Assert.False(say.Open);
        Assert.Equal(1003, t.ContextUsed);
        Assert.Equal("claude-haiku-5-5", t.Model);
    }

    [Fact]
    public void SinMensajesParcialesTambienArmaLosBloques()
    {
        var t = Started();
        t.Ask("Imprime hola");
        t.Apply("""{"type":"assistant","message":{"id":"m1","content":[{"type":"tool_use","id":"tu1","name":"Bash","input":{"command":"echo hola\necho chao","description":"Imprime"}}]}}""");
        t.Apply("""{"type":"user","message":{"role":"user","content":[{"tool_use_id":"tu1","type":"tool_result","content":"hola\nchao","is_error":false}]}}""");
        t.Apply("""{"type":"assistant","message":{"id":"m2","content":[{"type":"text","text":"Listo."}]}}""");

        Assert.Equal([AgentBlockKind.Ask, AgentBlockKind.Step, AgentBlockKind.Say], t.Blocks.Select(b => b.Kind));
        var step = t.Blocks[1];
        Assert.Equal("Bash", step.Tool);
        Assert.Equal("echo hola …", step.Text);
        Assert.Equal(StepState.Done, step.State);
        Assert.Equal("hola\nchao", step.Output);
    }

    [Fact]
    public void UnPasoQueEditaGuardaElCambioYLaRutaRelativa()
    {
        var t = Started();
        t.Apply("""{"type":"assistant","message":{"id":"m1","content":[{"type":"tool_use","id":"tu1","name":"Edit","input":{"file_path":"C:\\proyecto\\src\\app.cs","old_string":"a","new_string":"b"}}]}}""");
        t.Apply("""
            {"type":"user","message":{"role":"user","content":[{"tool_use_id":"tu1","type":"tool_result","content":"ok"}]},
             "tool_use_result":{"structuredPatch":[{"oldStart":1,"oldLines":2,"newStart":1,"newLines":3,"lines":[" uno","-dos","+tres","+cuatro"]}]}}
            """.ReplaceLineEndings(""));

        var step = Assert.Single(t.Blocks);
        Assert.Equal("src/app.cs", step.Text);
        Assert.Equal((2, 1), (step.Added, step.Removed));
        Assert.Equal([" uno", "-dos", "+tres", "+cuatro"], step.Diff);
        Assert.Equal(L.T("editó"), AgentSteps.Verb(step.Tool));
    }

    [Fact]
    public void UnArchivoNuevoCuentaTodasSusLineasComoAgregadas()
    {
        var t = Started();
        t.Apply("""{"type":"assistant","message":{"id":"m1","content":[{"type":"tool_use","id":"tu1","name":"Write","input":{"file_path":"C:\\proyecto\\nota.txt","content":"a\nb\n"}}]}}""");
        t.Apply("""{"type":"user","message":{"role":"user","content":[{"tool_use_id":"tu1","type":"tool_result","content":"File created"}]},"tool_use_result":{"type":"create","content":"a\nb\n","structuredPatch":[]}}""");

        var step = Assert.Single(t.Blocks);
        Assert.Equal(2, step.Added);
        Assert.Equal(["+a", "+b"], step.Diff);
    }

    [Fact]
    public void UnPermisoQuedaEnSuBloqueYElPasoEspera()
    {
        var t = Started();
        t.Apply("""{"type":"assistant","message":{"id":"m1","content":[{"type":"tool_use","id":"tu1","name":"Write","input":{"file_path":"C:\\proyecto\\nota.txt","content":"hola"}}]}}""");
        t.Apply("""{"type":"control_request","request_id":"r-9","request":{"subtype":"can_use_tool","tool_name":"Write","input":{"file_path":"C:\\proyecto\\nota.txt","content":"hola"},"description":"nota.txt","permission_suggestions":[{"type":"setMode","mode":"acceptEdits","destination":"session"}],"tool_use_id":"tu1"}}""");

        Assert.Equal(StepState.Waiting, t.Blocks[0].State);
        var permission = t.Blocks[1];
        Assert.Equal(AgentBlockKind.Permission, permission.Kind);
        Assert.Equal("nota.txt", permission.Text);
        Assert.Equal("tu1", permission.StepId);

        var always = JsonNode.Parse(AgentProtocol.Answer(permission, PermissionAnswer.AllowAlways))!;
        Assert.Equal("control_response", (string?)always["type"]);
        Assert.Equal("r-9", (string?)always["response"]!["request_id"]);
        Assert.Equal("allow", (string?)always["response"]!["response"]!["behavior"]);
        Assert.Equal("hola", (string?)always["response"]!["response"]!["updatedInput"]!["content"]);
        Assert.Equal("acceptEdits", (string?)always["response"]!["response"]!["updatedPermissions"]![0]!["mode"]);

        var once = JsonNode.Parse(AgentProtocol.Answer(permission, PermissionAnswer.Allow))!;
        Assert.Null(once["response"]!["response"]!["updatedPermissions"]);
        var deny = JsonNode.Parse(AgentProtocol.Answer(permission, PermissionAnswer.Deny))!;
        Assert.Equal("deny", (string?)deny["response"]!["response"]!["behavior"]);
    }

    static AgentBlock Permission()
    {
        var t = Started();
        t.Apply("""{"type":"control_request","request_id":"r-9","request":{"subtype":"can_use_tool","tool_name":"Write","input":{"file_path":"C:\\proyecto\\nota.txt","content":"hola"},"tool_use_id":"tu1"}}""");
        return t.Blocks[^1];
    }

    const string Questions = """{"questions":[{"question":"¿Color?","header":"Color","options":[{"label":"Rojo","description":""},{"label":"Azul","description":"El del mar"}],"multiSelect":false},{"question":"¿Frutas?","header":"Frutas","options":[{"label":"Pera","description":""},{"label":"Uva","description":""}],"multiSelect":true}]}""";

    [Fact]
    public void LasPreguntasSeRespondenEnElBloque()
    {
        var t = Started();
        t.Apply($$$"""{"type":"assistant","message":{"id":"m1","content":[{"type":"tool_use","id":"tu1","name":"AskUserQuestion","input":{{{Questions}}}}]}}""");
        t.Apply($$$"""{"type":"control_request","request_id":"r-3","request":{"subtype":"can_use_tool","tool_name":"AskUserQuestion","input":{{{Questions}}},"tool_use_id":"tu1"}}""");

        Assert.Equal("¿Color? …", t.Blocks[0].Text);
        var permission = t.Blocks[1];
        Assert.Equal(["¿Color?", "¿Frutas?"], permission.Questions!.Select(q => q.Question));
        Assert.Equal("El del mar", permission.Questions![0].Options[1].Description);
        Assert.True(permission.Questions![1].MultiSelect);

        var line = JsonNode.Parse(AgentProtocol.Answer(permission, new Dictionary<string, IReadOnlyList<string>>
        {
            ["¿Color?"] = ["Azul"],
            ["¿Frutas?"] = ["Pera", "lo que haya"],
        }))!["response"]!;
        Assert.Equal("r-3", (string?)line["request_id"]);
        var input = line["response"]!["updatedInput"]!;
        Assert.Equal("allow", (string?)line["response"]!["behavior"]);
        Assert.Equal("Azul", (string?)input["answers"]!["¿Color?"]);
        Assert.Equal(["Pera", "lo que haya"], input["answers"]!["¿Frutas?"]!.AsArray().Select(x => (string?)x));
        Assert.Equal(2, input["questions"]!.AsArray().Count);
    }

    [Fact]
    public void LasRespuestasDeLaIslaQuedanEnElBloque()
    {
        var t = Started();
        t.Apply($$$"""{"type":"control_request","request_id":"r-3","request":{"subtype":"can_use_tool","tool_name":"AskUserQuestion","input":{{{Questions}}},"tool_use_id":"tu1"}}""");
        var permission = t.Blocks[^1];
        var request = Approvals.Parse(AgentProtocol.IslandRequest(permission, "s-1", Cwd))!;
        var reply = Approvals.Answer(request, new Dictionary<string, IReadOnlyList<string>> { ["¿Color?"] = ["Rojo"], ["¿Frutas?"] = ["Uva"] });

        Assert.Equal(PermissionAnswer.Allow, AgentProtocol.FromIsland(permission, reply)!.Value.Answer);
        Assert.Equal(["Rojo"], permission.Picks!["¿Color?"]);
        Assert.Equal(["Uva"], permission.Picks!["¿Frutas?"]);
    }

    [Fact]
    public void ElPermisoVaALaIslaComoElDelHook()
    {
        var request = Approvals.Parse(AgentProtocol.IslandRequest(Permission(), "s-1", Cwd))!;

        Assert.Equal("s-1", request.SessionId);
        Assert.Equal(Cwd, request.Cwd);
        Assert.Equal("Write", request.Tool);
        Assert.Equal(@"C:\proyecto\nota.txt", request.Target);
    }

    [Fact]
    public void LaRespuestaDeLaIslaContestaElPermiso()
    {
        var permission = Permission();

        var allow = AgentProtocol.FromIsland(permission, Approvals.Allow())!.Value;
        Assert.Equal(PermissionAnswer.Allow, allow.Answer);
        var line = JsonNode.Parse(allow.Line)!["response"]!;
        Assert.Equal("r-9", (string?)line["request_id"]);
        Assert.Equal("allow", (string?)line["response"]!["behavior"]);
        Assert.Equal("hola", (string?)line["response"]!["updatedInput"]!["content"]);

        var deny = AgentProtocol.FromIsland(permission, Approvals.Deny())!.Value;
        Assert.Equal(PermissionAnswer.Deny, deny.Answer);
        Assert.Equal("deny", (string?)JsonNode.Parse(deny.Line)!["response"]!["response"]!["behavior"]);

        Assert.Null(AgentProtocol.FromIsland(permission, "no es json"));
        Assert.Null(AgentProtocol.FromIsland(permission, """{"hookSpecificOutput":{"decision":{"behavior":"ask"}}}"""));
    }

    [Fact]
    public async Task LaIslaRespondeORetiraLaTarjetaSiSeContestoEnElBloque()
    {
        var pipe = "Claupet-prueba-" + Guid.NewGuid().ToString("N");
        var withdrawn = new TaskCompletionSource();
        var received = new TaskCompletionSource();
        using var server = new ApprovalServer(async (request, cancel) =>
        {
            if (request.Tool == "Write") return Approvals.Allow();
            received.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, cancel); }
            catch (OperationCanceledException) { withdrawn.TrySetResult(); }
            return null;
        }, pipe);
        var line = AgentProtocol.IslandRequest(Permission(), "s-1", Cwd);

        Assert.Equal(Approvals.Allow(), await Approvals.AskAppAsync(line, CancellationToken.None, pipe));

        using var answered = new CancellationTokenSource();
        var waiting = Approvals.AskAppAsync(line.Replace("\"Write\"", "\"Bash\""), answered.Token, pipe);
        // Se cancela cuando la isla ya muestra la tarjeta (no tras un tiempo fijo: con las pruebas en paralelo no alcanza).
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        answered.Cancel();
        Assert.Null(await waiting);
        await withdrawn.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SinLaAppElPermisoQuedaSoloEnElBloque() =>
        Assert.Null(await Approvals.AskAppAsync("{}", CancellationToken.None, "Claupet-prueba-" + Guid.NewGuid().ToString("N")));

    [Fact]
    public void UnPermisoNegadoPorLaConfiguracionMarcaElPaso()
    {
        var t = Started();
        t.Apply("""{"type":"assistant","message":{"id":"m1","content":[{"type":"tool_use","id":"tu1","name":"Write","input":{"file_path":"C:\\proyecto\\x.txt","content":""}}]}}""");
        t.Apply("""{"type":"system","subtype":"permission_denied","tool_name":"Write","tool_use_id":"tu1","message":"no"}""");
        t.Apply("""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","content":"no","is_error":true,"tool_use_id":"tu1"}]}}""");

        Assert.Equal(StepState.Denied, Assert.Single(t.Blocks).State);
    }

    [Fact]
    public void LoQuePiensaEsUnSoloBloqueConSusTokens()
    {
        var t = Started();
        t.Apply("""{"type":"system","subtype":"thinking_tokens","estimated_tokens":50}""");
        t.Apply(Partial("""{"type":"message_start","message":{"id":"m1"}}"""));
        t.Apply(Partial("""{"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":""}}"""));
        t.Apply("""{"type":"system","subtype":"thinking_tokens","estimated_tokens":219}""");
        Assert.True(Assert.Single(t.Blocks).Open);

        t.Apply("""{"type":"assistant","message":{"id":"m1","content":[{"type":"thinking","thinking":"","signature":"x"}]}}""");
        t.Apply("""{"type":"assistant","message":{"id":"m1","content":[{"type":"text","text":"Hecho."}]}}""");

        Assert.Equal([AgentBlockKind.Think, AgentBlockKind.Say], t.Blocks.Select(b => b.Kind));
        Assert.False(t.Blocks[0].Open);
        Assert.Equal(219, t.Blocks[0].Tokens);
    }

    [Fact]
    public void LaListaDeTareasSeActualizaEnUnSoloBloquePorTurno()
    {
        var t = Started();
        t.Ask("Haz dos cosas");
        t.Apply("""{"type":"assistant","message":{"id":"m1","content":[{"type":"tool_use","id":"tu1","name":"TodoWrite","input":{"todos":[{"content":"Uno","status":"in_progress"},{"content":"Dos","status":"pending"}]}}]}}""");
        t.Apply("""{"type":"assistant","message":{"id":"m2","content":[{"type":"tool_use","id":"tu2","name":"TodoWrite","input":{"todos":[{"content":"Uno","status":"completed"},{"content":"Dos","status":"in_progress"}]}}]}}""");

        Assert.Equal([AgentBlockKind.Ask, AgentBlockKind.Todo], t.Blocks.Select(b => b.Kind));
        Assert.Equal(["completed", "in_progress"], t.Blocks[1].Todos.Select(x => x.Status));
    }

    [Fact]
    public void ElResultadoCierraElTurnoConCostoUsoYContexto()
    {
        var t = Started();
        t.Ask("Hola");
        t.Apply("""{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","unifiedWindows":{"five_hour":{"utilization":0.27,"resetsAt":1,"status":"allowed"},"seven_day":{"utilization":0.63,"resetsAt":2,"status":"allowed"}}}}""");
        t.Apply("""{"type":"assistant","message":{"id":"m1","content":[{"type":"text","text":"Hola."}],"usage":{"input_tokens":2,"cache_creation_input_tokens":48000,"cache_read_input_tokens":2000}}}""");
        t.Apply("""{"type":"result","subtype":"success","is_error":false,"duration_ms":4420,"num_turns":2,"total_cost_usd":0.0074,"result":"Hola.","modelUsage":{"claude-haiku-5-5":{"contextWindow":1000000}}}""");

        Assert.False(t.Busy);
        var done = t.Blocks[^1];
        Assert.Equal(AgentBlockKind.Done, done.Kind);
        Assert.Equal(TimeSpan.FromMilliseconds(4420), done.Duration);
        Assert.Equal(2, done.Turns);
        Assert.False(done.Failed);
        Assert.Equal(27, t.FiveHour!.Value, 3);
        Assert.Equal(63, t.SevenDay!.Value, 3);
        Assert.Equal(5, t.ContextPercent!.Value, 3);
    }

    [Fact]
    public void LoQueNoEsJsonOSeDesconoceNoCambiaNada()
    {
        var t = Started();
        Assert.Empty(t.Apply("no es json"));
        Assert.Empty(t.Apply("{roto"));
        Assert.Empty(t.Apply("""{"type":"control_response","response":{"subtype":"success","request_id":"claupet-1"}}"""));
        Assert.Empty(t.Blocks);
        Assert.Equal("s-1", t.SessionId);
    }

    [Fact]
    public void LasOpcionesDeLaSesionLlevanSuModeloYEsfuerzoYNoAceptanOtraCosa()
    {
        var args = TerminalAgent.Arguments(new AgentOptions(Cwd, Model: "sonnet", Effort: "high", PermissionMode: "acceptEdits"));
        Assert.Equal(["--model", "sonnet"], args.SkipWhile(a => a != "--model").Take(2));
        Assert.Equal(["--effort", "high"], args.SkipWhile(a => a != "--effort").Take(2));
        Assert.Equal(["--permission-mode", "acceptEdits"], args.SkipWhile(a => a != "--permission-mode").Take(2));
        Assert.Contains("stdio", args);

        var odd = TerminalAgent.Arguments(new AgentOptions(Cwd, Model: "sonnet; rm", Effort: "turbo", PermissionMode: "bypassPermissions"));
        Assert.DoesNotContain("--model", odd);
        Assert.DoesNotContain("--effort", odd);
        Assert.DoesNotContain("--permission-mode", odd);
    }

    [Fact]
    public void LosMensajesALaEntradaSonUnaLineaDeJson()
    {
        var message = AgentProtocol.UserMessage("hola\n\"mundo\"");
        Assert.DoesNotContain('\n', message);
        Assert.Equal("hola\n\"mundo\"", (string?)JsonNode.Parse(message)!["message"]!["content"]);
        Assert.Equal("interrupt", (string?)JsonNode.Parse(AgentProtocol.Interrupt("i"))!["request"]!["subtype"]);
    }

    [Fact]
    public void LeeLaRamaDeLaCarpetaODeUnWorktree()
    {
        using var dir = new TempDir();
        dir.Write("repo/.git/HEAD", "ref: refs/heads/feat/terminal\n");
        dir.Write("repo/src/x.txt", "");
        Assert.Equal("feat/terminal", GitBranch.Of(dir["repo/src"]));

        dir.Write("main/.git/worktrees/otro/HEAD", "0123456789abcdef\n");
        dir.Write("otro/.git", "gitdir: " + dir["main/.git/worktrees/otro"]);
        Assert.Equal("0123456", GitBranch.Of(dir["otro"]));
        Assert.Null(GitBranch.Of(dir["nada"]));
    }
}
