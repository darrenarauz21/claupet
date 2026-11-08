using System.Text;

namespace Claupet.Tests;

/// <summary>
/// Los permisos y las preguntas que esperan en la isla: se van solo cuando se respondieron (en la isla, en el bloque de
/// Claupet Terminal o en la terminal de la persona, que se sabe por el registro de la sesión), nunca por cerrar la isla.
/// </summary>
public sealed class PendingApprovalsTests
{
    const string Session = "0d5c6f0e-1111-4222-8333-944445555666";
    const string BashInput = """{"command":"rm -rf compilación","description":"Borra la compilación"}""";

    static readonly Account Cuenta = new() { Label = "principal", ConfigDir = @"C:\cuenta" };

    static FlowStep Step(string id, string tool, bool done, string input = "{}") =>
        new(id, ToolKind.Other, tool, null, null) { Tool = tool, Done = done, InputKey = Approvals.InputKey(input) };

    static SessionInfo Info(SessionState state, int turns = 1, DateTimeOffset? ended = null, params FlowStep[] flow) => new()
    {
        Account = Cuenta, SessionId = Session, Name = "sesión", Cwd = @"C:\repo", State = state,
        Turns = turns, TurnEndedAt = ended, Flow = flow,
    };

    static ApprovalRequest Request(string tool = "Bash", string input = BashInput, bool terminal = false) =>
        new(Session, @"C:\repo", tool, "rm -rf compilación", input, null) { FromTerminal = terminal };

    [Fact]
    public void SiNadaCambiaElPedidoSigueEsperando()
    {
        var pending = new PendingApprovals();
        var item = pending.Add(Request(), "sesión");
        var waiting = Info(SessionState.Working, flow: [Step("t1", "Read", true), Step("t2", "Bash", false, BashInput)]);

        for (var i = 0; i < 5; i++) Assert.False(pending.Sessions([waiting]));

        Assert.Same(item, pending.Current);
        Assert.False(item.Reply.IsCompleted);
    }

    [Fact]
    public void RespondidoEnLaTerminalSeQuita()
    {
        var pending = new PendingApprovals();
        var item = pending.Add(Request(), "sesión");
        pending.Sessions([Info(SessionState.Working, flow: [Step("t2", "Bash", false, BashInput)])]);

        // Claude Code escribe el resultado del paso (también si se negó): se respondió allí.
        Assert.True(pending.Sessions([Info(SessionState.Working, flow: [Step("t2", "Bash", true, BashInput)])]));
        Assert.Empty(pending.Items);
        Assert.True(item.Reply.IsCompletedSuccessfully);
        Assert.Null(item.Reply.Result); // el hook no escribe nada: ya decidió la terminal
    }

    [Fact]
    public void RespondidoEnLaIslaLaRespuestaVaAlHook()
    {
        var pending = new PendingApprovals();
        var item = pending.Add(Request(), "sesión");
        var changes = 0;
        pending.Changed += () => changes++;

        pending.Answer(item, Approvals.Allow());

        Assert.Empty(pending.Items);
        Assert.Equal(Approvals.Allow(), item.Reply.Result);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void SiElHookDejaDeEsperarLaTarjetaSigueHastaQueSeResponda()
    {
        var pending = new PendingApprovals();
        var item = pending.Add(Request(), "sesión");
        pending.Sessions([Info(SessionState.Working, flow: [Step("t2", "Bash", false, BashInput)])]);

        // Se le acabó el tiempo al hook (o Claude Code lo cortó): la pregunta sigue en la terminal, y en la isla.
        pending.Gone(item);
        Assert.Same(item, pending.Current);
        Assert.True(item.Detached);
        Assert.Null(item.Reply.Result);

        Assert.False(pending.Sessions([Info(SessionState.Working, flow: [Step("t2", "Bash", false, BashInput)])]));
        Assert.True(pending.Sessions([Info(SessionState.Working, flow: [Step("t2", "Bash", true, BashInput)])]));
        Assert.Empty(pending.Items);
    }

    [Fact]
    public void SiNoSeVeLaSesionYElHookSeVaSeQuita()
    {
        var pending = new PendingApprovals();
        var item = pending.Add(Request(), "sesión");
        pending.Gone(item); // sin el registro de la sesión no habría cómo saber cuándo se respondió
        Assert.Empty(pending.Items);
    }

    [Fact]
    public void ClaupetTerminalCierraElPipeAlResponderEnSuBloque()
    {
        var pending = new PendingApprovals();
        var item = pending.Add(Request(terminal: true), "sesión");
        pending.Sessions([Info(SessionState.Working, flow: [Step("t2", "Bash", false, BashInput)])]);

        pending.Gone(item);

        Assert.Empty(pending.Items);
    }

    [Fact]
    public void VariosPedidosEsperanEnOrden()
    {
        var pending = new PendingApprovals();
        var first = pending.Add(Request(), "sesión");
        var second = pending.Add(Request("Write", """{"file_path":"C:\\repo\\a.txt","content":"hola"}"""), "sesión");

        Assert.Same(first, pending.Current);
        pending.Answer(first, Approvals.Deny());
        Assert.Same(second, pending.Current);
    }

    [Fact]
    public void ElTurnoQueTerminaOLaSesionQueSeCierraLoDanPorRespondido()
    {
        var watch = new AnswerWatch(Session, "Bash", BashInput);
        Assert.False(watch.Answered([Info(SessionState.Working)]));
        Assert.True(watch.Answered([Info(SessionState.Done, ended: DateTimeOffset.Now)]));

        var gone = new AnswerWatch(Session, "Bash", BashInput);
        Assert.False(gone.Answered([Info(SessionState.Working)]));
        Assert.False(gone.Answered([])); // una lectura sin la sesión puede ser un tropiezo
        Assert.True(gone.Answered([]));
    }

    [Fact]
    public void PedidoYRespuestaEntreDosLecturas()
    {
        var watch = new AnswerWatch(Session, "Bash", BashInput);
        Assert.False(watch.Answered([Info(SessionState.Working, flow: [Step("t1", "Bash", true, """{"command":"ls"}""")])]));
        // El paso aparece ya hecho: se pidió, se respondió en la terminal y corrió entre lectura y lectura.
        Assert.True(watch.Answered([Info(SessionState.Working, flow: [Step("t1", "Bash", true, """{"command":"ls"}"""), Step("t2", "Bash", true, BashInput)])]));
    }

    [Fact]
    public void ConDosPasosDeLaMismaHerramientaMiraElDeSuPedido()
    {
        var watch = new AnswerWatch(Session, "Bash", BashInput);
        var other = """{"command":"git status"}""";
        Assert.False(watch.Answered([Info(SessionState.Working, flow: [Step("t1", "Bash", false, other), Step("t2", "Bash", false, BashInput)])]));
        // Terminó el otro: este sigue esperando.
        Assert.False(watch.Answered([Info(SessionState.Working, flow: [Step("t1", "Bash", true, other), Step("t2", "Bash", false, BashInput)])]));
        Assert.True(watch.Answered([Info(SessionState.Working, flow: [Step("t1", "Bash", true, other), Step("t2", "Bash", true, BashInput)])]));
    }

    [Fact]
    public void ElAvisoDeUnaPreguntaSeVaAlResponderla()
    {
        // El aviso «Le está preguntando» (sin el pedido, solo la herramienta).
        var watch = new AnswerWatch(Session, "AskUserQuestion");
        Assert.False(watch.Answered([Info(SessionState.Waiting, flow: [Step("q1", "AskUserQuestion", false)])]));
        Assert.True(watch.Answered([Info(SessionState.Working, flow: [Step("q1", "AskUserQuestion", true)])]));

        // El aviso «Necesita su permiso» (del hook Notification, sin herramienta): el último paso que espera.
        var permission = new AnswerWatch(Session);
        Assert.False(permission.Answered([Info(SessionState.Working, flow: [Step("a1", "Read", true), Step("a2", "Edit", false)])]));
        Assert.True(permission.Answered([Info(SessionState.Working, flow: [Step("a1", "Read", true), Step("a2", "Edit", true)])]));
    }

    [Fact]
    public void ElAvisoQueLlegaDespuesDeResponderSeVa()
    {
        // El hook Notification llegó tarde: cuando el aviso sale, el paso ya se respondió y no hay ninguno esperando.
        var late = new AnswerWatch(Session);
        var answered = Info(SessionState.Working, flow: [Step("a1", "Read", true), Step("a2", "Edit", true)]);
        Assert.False(late.Answered([answered])); // una lectura sola puede ser antes de que se escriba el paso
        Assert.True(late.Answered([answered]));
    }

    [Fact]
    public void ElPedidoDelHookYElDelRegistroSeReconocenAunqueSeEscapenDistinto()
    {
        // El hook lo vuelve a escribir con System.Text.Json (escapa «ó»); el registro de Claude Code lo trae tal cual.
        var fromHook = System.Text.Json.Nodes.JsonNode.Parse(BashInput)!.ToJsonString();
        Assert.NotEqual(BashInput, fromHook);
        Assert.Equal(Approvals.InputKey(BashInput), Approvals.InputKey(fromHook));
        Assert.NotEqual(0, Approvals.InputKey(BashInput));
    }

    [Fact]
    public void ElRegistroDeVerdadAvisaCuandoSeRespondioEnLaTerminal()
    {
        // Una sesión de verdad para SessionMonitor: el registro sessions/<pid>.json (con el pid de esta prueba, que está
        // vivo) y su .jsonl, donde Claude Code escribe el paso que pide permiso y, al responderse, su resultado.
        var dir = Path.Combine(Path.GetTempPath(), "claupet-permisos-" + Guid.NewGuid().ToString("N"));
        try
        {
            var account = new Account { Label = "prueba", ConfigDir = dir };
            var pid = Environment.ProcessId;
            Directory.CreateDirectory(Path.Combine(dir, "sessions"));
            File.WriteAllText(Path.Combine(dir, "sessions", pid + ".json"),
                $$"""{"pid":{{pid}},"sessionId":"{{Session}}","cwd":"C:\\repo","status":"busy","startedAt":1793458201388}""");
            var project = Path.Combine(dir, "projects", "C--repo");
            Directory.CreateDirectory(project);
            var transcript = Path.Combine(project, Session + ".jsonl");
            void Append(string line) => File.AppendAllText(transcript, line + "\n", new UTF8Encoding(false));
            Append("""{"type":"user","timestamp":"2026-11-01T10:00:00Z","message":{"role":"user","content":[{"type":"text","text":"Limpia"}]}}""");
            Append($$$"""{"type":"assistant","timestamp":"2026-11-01T10:00:05Z","message":{"model":"claude-opus-5-5","content":[{"type":"tool_use","id":"toolu_1","name":"Bash","input":{{{BashInput}}}}]}}""");

            var monitor = new SessionMonitor();
            var pending = new PendingApprovals();
            // Lo que manda el hook: el evento vuelto a escribir en una línea.
            var line = System.Text.Json.Nodes.JsonNode.Parse($$"""
                {"hook_event_name":"PermissionRequest","session_id":"{{Session}}","cwd":"C:\\repo","tool_name":"Bash","tool_input":{{BashInput}}}
                """)!.ToJsonString();
            var item = pending.Add(Approvals.Parse(line)!, "sesión");

            Assert.False(pending.Sessions(monitor.Scan([account])));
            Assert.False(pending.Sessions(monitor.Scan([account])));
            Assert.Same(item, pending.Current);

            Append("""{"type":"user","timestamp":"2026-11-01T10:00:20Z","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"ok"}]}}""");
            Assert.True(pending.Sessions(monitor.Scan([account])));
            Assert.Empty(pending.Items);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
        }
    }
}
