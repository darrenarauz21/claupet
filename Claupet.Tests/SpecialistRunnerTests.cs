namespace Claupet.Tests;

/// <summary>
/// SpecialistRunner: un especialista trabaja en una tarea delegada, sin interfaz, y devuelve su resultado. La sesión del
/// agente es de mentira: responde con las mismas líneas que Claude Code (el runner no distingue agentes).
/// </summary>
public sealed class SpecialistRunnerTests : IDisposable
{
    readonly TempDir _dir = new();
    readonly (string Folder, Func<AgentOptions, ISpecialistSession> Open, Func<Specialist, TimeSpan> Limit) _before =
        (SpecialistRunner.Folder, SpecialistRunner.Open, SpecialistRunner.Limit);
    readonly List<FakeSession> _opened = [];

    public SpecialistRunnerTests()
    {
        SpecialistRunner.Folder = _dir["corridas"];
        SpecialistRunner.Limit = _ => TimeSpan.FromSeconds(5);
    }

    public void Dispose()
    {
        (SpecialistRunner.Folder, SpecialistRunner.Open, SpecialistRunner.Limit) = _before;
        _dir.Dispose();
    }

    /// <summary>Una sesión de mentira: al recibir la tarea, «el agente» responde con las líneas de script (en otro hilo, como el proceso).</summary>
    sealed class FakeSession(AgentOptions options, Action<FakeSession>? script, bool fails = false) : ISpecialistSession
    {
        public AgentOptions Options { get; } = options;
        public AgentTranscript Transcript { get; } = new();
        public bool Ended { get; private set; }
        public bool Stopped { get; private set; }
        public string? Sent { get; private set; }
        public event Action<IReadOnlyList<AgentBlock>>? Changed;

        public void Start()
        {
            if (!fails) return;
            Ended = true;
            Transcript.Notice("No encontré Claude Code.");
        }

        public void Send(string text)
        {
            Sent = text;
            Changed?.Invoke([Transcript.Ask(text)]);
            if (script is not null) _ = Task.Run(() => script(this));
        }

        public void Line(string json) => Changed?.Invoke(Transcript.Apply(json));
        public void Stop() => Stopped = true;
        public void Dispose() { }
    }

    void Agent(Action<FakeSession>? script, bool fails = false) =>
        SpecialistRunner.Open = options =>
        {
            var session = new FakeSession(options, script, fails);
            _opened.Add(session);
            return session;
        };

    static Specialist Reviewer() => new()
    {
        Id = "abcdef123456", Name = "Revisor", Role = "Revisa cambios", When = "Cuando haya que revisar un diff",
        Agent = AgentKinds.Claude, Model = "sonnet", Effort = "high", Instructions = "Revise primero las pruebas.",
    };

    [Fact]
    public async Task DevuelveLaUltimaRespuestaConSusPasosYQuedaEnElDiario()
    {
        Agent(s =>
        {
            s.Line("""{"type":"system","subtype":"init","session_id":"11111111-2222-3333-4444-555555555555","model":"claude-sonnet-5-5"}""");
            s.Line("""{"type":"assistant","message":{"id":"m1","content":[{"type":"text","text":"Voy a revisar."}]}}""");
            s.Line("""{"type":"assistant","message":{"id":"m2","content":[{"type":"tool_use","id":"tu1","name":"Bash","input":{"command":"dotnet test","description":"Pruebas"}}]}}""");
            s.Line("""{"type":"user","message":{"role":"user","content":[{"tool_use_id":"tu1","type":"tool_result","content":"ok","is_error":false}]}}""");
            s.Line("""{"type":"assistant","message":{"id":"m3","content":[{"type":"text","text":"Todo bien: 2 comentarios."}]}}""");
            s.Line("""{"type":"result","subtype":"success","is_error":false,"duration_ms":4420,"num_turns":3,"total_cost_usd":0.02,"result":"Todo bien: 2 comentarios."}""");
        });
        var updates = new List<string>();
        var run = await SpecialistRunner.RunAsync(new SpecialistRequest(Reviewer(), "Revisa el PR 12", _dir.Path, From: "sesion1"), r => updates.Add(r.State));

        Assert.Equal(RunStates.Done, run.State);
        Assert.Equal("Todo bien: 2 comentarios.", run.Result); // la última respuesta, no la primera
        Assert.Contains(run.Steps, s => s.StartsWith("Bash", StringComparison.Ordinal));
        Assert.Equal(0.02, run.Cost);
        Assert.Equal("11111111-2222-3333-4444-555555555555", run.SessionId);
        Assert.Contains(RunStates.Working, updates);

        // El agente se abrió como el especialista, en la carpeta de quien lo llamó, y recibió la tarea con la aclaración.
        var session = Assert.Single(_opened);
        Assert.Equal((AgentKinds.Claude, "sonnet", "high", _dir.Path), (session.Options.Kind, session.Options.Model, session.Options.Effort, session.Options.Folder));
        Assert.Same("Revisor", session.Options.Specialist?.Name);
        Assert.StartsWith("Revisa el PR 12", session.Sent);

        // El diario lo pueden leer los demás (la Terminal, el servidor MCP).
        var saved = SpecialistRunner.Read(run.Id);
        Assert.Equal((RunStates.Done, "Todo bien: 2 comentarios.", "sesion1"), (saved?.State, saved?.Result, saved?.From));
        Assert.NotNull(saved?.Ended);
    }

    [Fact]
    public async Task UnPermisoSinResponderSeVeComoPidePermiso()
    {
        var asked = new TaskCompletionSource();
        Agent(s =>
        {
            s.Line("""{"type":"assistant","message":{"id":"m1","content":[{"type":"tool_use","id":"tu1","name":"Bash","input":{"command":"rm -rf obj"}}]}}""");
            s.Line("""{"type":"control_request","request_id":"r1","request":{"subtype":"can_use_tool","tool_name":"Bash","input":{"command":"rm -rf obj"},"tool_use_id":"tu1"}}""");
            asked.TrySetResult();
        });
        SpecialistRunner.Limit = _ => TimeSpan.FromSeconds(1);
        var states = new List<string>();
        var run = await SpecialistRunner.RunAsync(new SpecialistRequest(Reviewer(), "Limpia", _dir.Path), r => { lock (states) states.Add(r.State); });

        Assert.Contains(RunStates.Waiting, states);
        // Nadie respondió: pasado su tope se detiene, y lo dice.
        Assert.Equal(RunStates.TimedOut, run.State);
        Assert.True(Assert.Single(_opened).Stopped);
    }

    [Fact]
    public async Task PasadoSuTopeSeDetieneYDiceLoQueAlcanzoAHacer()
    {
        Agent(s => s.Line("""{"type":"assistant","message":{"id":"m1","content":[{"type":"tool_use","id":"tu1","name":"Read","input":{"file_path":"C:\\x\\a.cs"}}]}}"""));
        SpecialistRunner.Limit = _ => TimeSpan.FromMilliseconds(400);
        var run = await SpecialistRunner.RunAsync(new SpecialistRequest(Reviewer(), "Lee todo", _dir.Path));

        Assert.Equal(RunStates.TimedOut, run.State);
        Assert.Contains("Read", run.Result);
        Assert.True(Assert.Single(_opened).Stopped);
    }

    [Fact]
    public async Task CancelarLoDetiene()
    {
        Agent(_ => { });
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var run = await SpecialistRunner.RunAsync(new SpecialistRequest(Reviewer(), "Algo largo", _dir.Path), cancel: cancel.Token);
        Assert.Equal(RunStates.Stopped, run.State);
        Assert.True(Assert.Single(_opened).Stopped);
    }

    [Fact]
    public async Task SiElAgenteNoAbreFallaConElMotivo()
    {
        Agent(null, fails: true);
        var run = await SpecialistRunner.RunAsync(new SpecialistRequest(Reviewer(), "Revisa", _dir.Path));
        Assert.Equal(RunStates.Failed, run.State);
        Assert.Equal("No encontré Claude Code.", run.Result);
    }

    [Fact]
    public async Task UnEspecialistaDelegadoNoDelegaOtraVezNiSeAbreSinTarea()
    {
        Agent(_ => { });
        var deep = await SpecialistRunner.RunAsync(new SpecialistRequest(Reviewer(), "Revisa", _dir.Path, Depth: SpecialistRunner.MaxDepth + 1));
        Assert.Equal(RunStates.Failed, deep.State);
        var empty = await SpecialistRunner.RunAsync(new SpecialistRequest(Reviewer(), "   ", _dir.Path));
        Assert.Equal(RunStates.Failed, empty.State);
        Assert.Empty(_opened); // ni siquiera se abrió el agente
    }

    [Fact]
    public void LosEspecialistasDeAntesSiguenSirviendoYLoNuevoSeAcota()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<Specialist>("""{ "name": "Viejo", "role": "Hace cosas" }""",
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase })!;
        old.Normalize();
        Assert.Equal(("", false, null, 15, 40), (old.When, old.EditsFiles, old.Account, old.Limits.Minutes, old.Limits.Turns));
        Assert.Equal("Hace cosas", old.Purpose); // sin «cuándo usarlo», el rol

        var odd = new Specialist { Name = "Raro", When = "línea uno\nlínea dos" + new string('x', 400), Limits = new() { Minutes = 0, Turns = 9999 }, Account = "" };
        odd.Normalize();
        Assert.DoesNotContain('\n', odd.When);
        Assert.Equal(Specialist.MaxWhen, odd.When.Length);
        Assert.Equal((1, SpecialistLimits.MaxTurns, (string?)null), (odd.Limits.Minutes, odd.Limits.Turns, odd.Account));
        Assert.StartsWith("línea uno", odd.Purpose);
    }
}
