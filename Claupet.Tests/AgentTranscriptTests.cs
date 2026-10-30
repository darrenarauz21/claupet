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
