using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet.Tests;

/// <summary>
/// Los especialistas en el servidor MCP de Claupet: cualquier agente ve la lista, les delega una tarea y recibe el
/// resultado (o el id de la corrida, si tarda). Las sesiones de los especialistas son de mentira.
/// </summary>
[Collection("Especialistas")]
public sealed class SpecialistToolsTests : IDisposable
{
    readonly TempDir _dir = new();
    readonly (string Folder, Func<IReadOnlyList<Specialist>> Extensions, string Runs, Func<AgentOptions, ISpecialistSession> Open, TimeSpan Wait) _before =
        (Specialists.Folder, Specialists.FromExtensions, SpecialistRunner.Folder, SpecialistRunner.Open, SpecialistTools.Wait);
    readonly List<AgentOptions> _opened = [];

    public SpecialistToolsTests()
    {
        Specialists.Folder = _dir["especialistas"];
        Specialists.FromExtensions = () => [];
        SpecialistRunner.Folder = _dir["corridas"];
        Specialists.Save(new Specialist { Id = "aaaaaaaa1111", Name = "Revisor de PRs", Role = "Revisa", When = "Cuando haya que revisar un diff", Model = "sonnet" });
        Specialists.Save(new Specialist { Id = "bbbbbbbb2222", Name = "Tester", Role = "Escribe pruebas", Agent = AgentKinds.Codex, EditsFiles = true });
    }

    public void Dispose()
    {
        (Specialists.Folder, Specialists.FromExtensions, SpecialistRunner.Folder, SpecialistRunner.Open, SpecialistTools.Wait) = _before;
        _dir.Dispose();
    }

    /// <summary>Una sesión que responde answer después de delay (en otro hilo, como el proceso del agente).</summary>
    sealed class Quick(AgentOptions options, string answer, TimeSpan delay) : ISpecialistSession
    {
        public string Key { get; } = "clave-" + Guid.NewGuid().ToString("N")[..8];
        public AgentTranscript Transcript { get; } = new();
        public bool Ended => false;
        public event Action<IReadOnlyList<AgentBlock>>? Changed;
        public AgentOptions Options => options;
        public void Start() { }
        public void Send(string text)
        {
            Changed?.Invoke([Transcript.Ask(text)]);
            _ = Task.Run(async () =>
            {
                await Task.Delay(delay);
                Changed?.Invoke(Transcript.Apply(JsonSerializer.Serialize(new { type = "assistant", message = new { id = "m1", content = new[] { new { type = "text", text = answer } } } })));
                Changed?.Invoke(Transcript.Apply("""{"type":"result","subtype":"success","is_error":false,"num_turns":1,"total_cost_usd":0.01}"""));
            });
        }
        public void Stop() { }
        public void Dispose() { }
    }

    void Answers(string answer, TimeSpan delay) =>
        SpecialistRunner.Open = options =>
        {
            lock (_opened) _opened.Add(options);
            return new Quick(options, answer, delay);
        };

    CanvasMcp.Server Server(string session = "sesion-principal") => new(new CanvasStore(_dir["canvas"]), "Claude Code", _dir.Path, session);

    static string Call(CanvasMcp.Server server, string tool, object args)
    {
        var request = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments = args } });
        var reply = JsonNode.Parse(server.Handle(request)!)!;
        return reply["result"]!["content"]![0]!["text"]!.GetValue<string>();
    }

    [Fact]
    public void LasHerramientasEstanEnTodasLasSesionesYSeUsanSinPreguntar()
    {
        var reply = JsonNode.Parse(Server().Handle("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""")!)!;
        var names = reply["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToList();
        Assert.Contains("especialistas", names);
        Assert.Contains("pedir_a_especialista", names);
        Assert.Contains("ver_corrida", names);
        Assert.All(SpecialistTools.Names, n => Assert.Contains(n, CanvasMcp.SafeTools));
    }

    [Fact]
    public void LaListaDiceCuandoUsarCadaUno()
    {
        var text = Call(Server(), "especialistas", new { });
        Assert.Contains("Revisor de PRs (id aaaaaaaa1111): Cuando haya que revisar un diff", text);
        Assert.Contains("Tester (id bbbbbbbb2222): Escribe pruebas", text); // sin «cuándo usarlo», el rol
        Assert.Contains("edits files", text);
    }

    [Fact]
    public void DelegarDevuelveElResultadoConLaCarpetaYLaCuentaDeLaSesion()
    {
        Answers("Revisado: sin problemas.", TimeSpan.FromMilliseconds(50));
        var text = Call(Server(), "pedir_a_especialista", new { especialista = "revisor", tarea = "Revisa el diff de app.cs" });

        Assert.Contains("Revisor de PRs finished", text);
        Assert.Contains("Revisado: sin problemas.", text);
        var options = Assert.Single(_opened);
        Assert.Equal((_dir.Path, "sonnet", AgentKinds.Claude), (options.Folder, options.Model, options.Kind));
    }

    [Fact]
    public void SiTardaDevuelveElIdYDespuesElResultado()
    {
        SpecialistTools.Wait = TimeSpan.FromMilliseconds(200);
        Answers("Listo, tardé.", TimeSpan.FromMilliseconds(900));
        var server = Server();
        var first = Call(server, "pedir_a_especialista", new { especialista = "Revisor de PRs", tarea = "Algo largo" });
        Assert.Contains("still working", first);
        var id = System.Text.RegularExpressions.Regex.Match(first, @"Run id: ([\w-]+)").Groups[1].Value;

        SpecialistTools.Wait = TimeSpan.FromSeconds(5);
        var later = Call(server, "ver_corrida", new { id });
        Assert.Contains("finished", later);
        Assert.Contains("Listo, tardé.", later);
    }

    [Fact]
    public void UnEspecialistaQueNoExisteOSinTareaEsUnErrorParaElModelo()
    {
        var reply = JsonNode.Parse(Server().Handle(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call",
            @params = new { name = "pedir_a_especialista", arguments = new { especialista = "nadie", tarea = "x" } } }))!)!;
        Assert.True(reply["result"]!["isError"]!.GetValue<bool>());
        Assert.Contains("especialistas", reply["result"]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void UnDelegadoNoSeLlamaASiMismoYElQueLlamaNoDelegaOtraVez()
    {
        // El revisor trabaja en una tarea delegada: su sesión (su servidor MCP) tiene la clave de esa corrida.
        Directory.CreateDirectory(SpecialistRunner.Folder);
        File.WriteAllText(Path.Combine(SpecialistRunner.Folder, "20261120-100000-abc123.json"), JsonSerializer.Serialize(new SpecialistRun
        {
            Id = "20261120-100000-abc123", Specialist = "aaaaaaaa1111", Name = "Revisor de PRs", SessionKey = "clave-del-revisor",
            Depth = 1, State = RunStates.Working, Started = DateTimeOffset.Now,
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var reviewer = Server("clave-del-revisor");

        Assert.DoesNotContain("Revisor de PRs", Call(reviewer, "especialistas", new { })); // no se ve a sí mismo
        var self = JsonNode.Parse(reviewer.Handle(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call",
            @params = new { name = "pedir_a_especialista", arguments = new { especialista = "revisor", tarea = "x" } } }))!)!;
        Assert.True(self["result"]!["isError"]!.GetValue<bool>());

        // Puede delegar al tester (profundidad 2), pero el tester ya no delega: la corrida lo dice.
        Answers("ok", TimeSpan.FromMilliseconds(10));
        Assert.Contains("finished", Call(reviewer, "pedir_a_especialista", new { especialista = "tester", tarea = "Escribe una prueba" }));
        var run = Directory.GetFiles(SpecialistRunner.Folder, "*.json").Select(f => SpecialistRunner.Read(Path.GetFileNameWithoutExtension(f))).Single(r => r?.Name == "Tester");
        Assert.Equal(2, run!.Depth);
    }

    [Fact]
    public void UnPedidoConArrobaLlevaLaNotaDeDelegarYElHistorialNoLaMuestra()
    {
        var all = Specialists.All();
        var text = "@revisor revisa esto, avisa a juan@correo.com, lee @src/app.cs y pregúntale a @nadie";
        var mentions = SpecialistTools.Mentions(text, all);
        Assert.Equal(["Revisor de PRs"], mentions.Select(m => m.Specialist.Name)); // ni el correo, ni la ruta, ni quien no existe
        Assert.Equal("revisor", mentions[0].Token);

        var note = SpecialistTools.Directive(mentions)!;
        Assert.Contains("pedir_a_especialista", note);
        Assert.Contains("«Revisor de PRs»", note);
        Assert.Equal(text, Workspace.Strip(text + note)); // en el historial y los títulos, lo que escribió la persona
        Assert.Null(SpecialistTools.Directive(SpecialistTools.Mentions("sin menciones", all)));
        Assert.Equal(2, SpecialistTools.Mentions("@tester y @revisor-de-prs", all).Count);
    }

    [Fact]
    public void EnLaCajaArrobaMuestraLaListaYLasCorridasSeVenArriba() => Headless.Run(() =>
    {
        var pane = new TerminalPane();
        var window = new Avalonia.Controls.Window { Width = 1200, Height = 800, Content = pane };
        window.Show();
        var tab = new TerminalPane.Tab(new TerminalAgent(new AgentOptions(_dir.Path), a => a()), "cuenta");
        pane.Add(tab);
        Headless.Frame(window);
        var input = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(pane).OfType<Avalonia.Controls.TextBox>().First(t => t.AcceptsReturn);
        try
        {
            // Escribir «@rev» ofrece al revisor; Intro lo elige (no envía) y deja su nombre con un espacio.
            input.Text = "Por favor @rev";
            input.CaretIndex = input.Text.Length;
            Headless.Frame(window);
            Assert.Equal(["Revisor de PRs"], pane.MentionChoices);
            input.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter, Source = input });
            Assert.Equal("Por favor @revisor-de-prs ", input.Text);
            Assert.Empty(pane.MentionChoices);
            // Un @ pegado a una palabra (un correo) no abre la lista.
            input.Text = "juan@rev";
            input.CaretIndex = input.Text.Length;
            Assert.Empty(pane.MentionChoices);

            // Una corrida que pidió esta pestaña se ve arriba de la caja, con cómo va.
            pane.Track(new SpecialistRun { Id = "20261120-120000-aaaaaa", Specialist = "aaaaaaaa1111", Name = "Revisor de PRs", From = tab.Agent.Driver.CanvasKey,
                State = RunStates.Working, Started = DateTimeOffset.Now, Steps = ["Bash: dotnet test"], Folder = _dir.Path });
            Assert.Equal([("Revisor de PRs", RunStates.Working)], pane.RunsShown);
            // La de otra sesión, no.
            pane.Track(new SpecialistRun { Id = "20261120-120001-bbbbbb", Name = "Tester", From = "otra-clave", State = RunStates.Working, Started = DateTimeOffset.Now });
            Assert.Single(pane.RunsShown);
        }
        finally
        {
            Headless.Frame(window);
            pane.CloseAll();
            window.Close();
        }
    });

    [Fact]
    public async Task DetenerDesdeOtroProcesoLaDetiene()
    {
        SpecialistRunner.Open = options => new Quick(options, "nunca", TimeSpan.FromMinutes(5));
        string? id = null;
        var run = SpecialistRunner.RunAsync(new SpecialistRequest(Specialists.All()[0], "Algo largo", _dir.Path), r => id ??= r.Id);
        await Task.Delay(300);
        SpecialistRunner.RequestStop(id!);
        var done = await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(RunStates.Stopped, done.State);
        Assert.False(File.Exists(Path.Combine(SpecialistRunner.Folder, id + ".detener"))); // el aviso se limpia
    }

    [Theory]
    [InlineData("revisor-de-prs")]
    [InlineData("@Revisor de PRs")]
    [InlineData("REVISOR")]
    [InlineData("aaaaaaaa1111")]
    public void SeEncuentraPorNombreSlugComienzoOId(string who) => Assert.Equal("aaaaaaaa1111", SpecialistTools.Find(who)?.Id);
}
