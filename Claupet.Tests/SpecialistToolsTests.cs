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
    public void LosEspecialistasDeClaudeCodeVanComoSubagentesEnUnArchivo()
    {
        Specialists.Save(new Specialist { Id = "cccccccc3333", Name = "Plan", Role = "Tapa el de Claude Code" });
        Specialists.Save(new Specialist { Id = "dddddddd4444", Name = "Buscador", Role = "Busca", Instructions = "Solo lee.", Effort = "low" });
        Specialists.Save(new Specialist { Id = "eeeeeeee5555", Name = "buscador", Role = "Otro con el mismo nombre" });

        var subagents = Specialists.Subagents(Specialists.All());
        // Solo los de Claude Code (Tester es de Codex), sin tapar los que trae Claude Code ni repetir nombres.
        Assert.Equal(["buscador", "revisor-de-prs"], subagents.Select(s => s.Name).Order());
        Specialists.Delete(Specialists.Mine().Single(s => s.Id == "eeeeeeee5555"));
        subagents = Specialists.Subagents(Specialists.All());

        var json = Specialists.SubagentsJson(subagents);
        Assert.Equal("Cuando haya que revisar un diff", (string?)json["revisor-de-prs"]!["description"]);
        Assert.Equal("sonnet", (string?)json["revisor-de-prs"]!["model"]);
        Assert.Equal("inherit", (string?)json["buscador"]!["model"]); // sin modelo, el de la sesión
        Assert.Equal("low", (string?)json["buscador"]!["effort"]);
        Assert.Contains("Solo lee.", (string?)json["buscador"]!["prompt"]);

        // En un archivo (el JSON no pasa por cmd.exe); el mismo contenido, el mismo archivo.
        var file = Specialists.SubagentsFile(subagents)!;
        Assert.StartsWith(Path.Combine(Specialists.Folder, "sesiones"), file);
        Assert.Equal(file, Specialists.SubagentsFile(subagents));
        Assert.Equal("Busca", (string?)JsonNode.Parse(File.ReadAllText(file))!["buscador"]!["description"]);
        Assert.Null(Specialists.SubagentsFile([]));
    }

    [Fact]
    public void ConSubagenteElArrobaTrabajaEnLaSesionYSinElSeDelegaAparte()
    {
        var all = Specialists.All();
        var text = "@revisor revisa y @tester prueba";
        var subagents = Specialists.Subagents(all);
        var note = SpecialistTools.Directive(SpecialistTools.Mentions(text, all), subagents)!;
        // El revisor es subagente de la sesión: va a él. El tester (Codex) no lo es: se delega aparte.
        Assert.Contains("your subagent \"revisor-de-prs\"", note);
        Assert.Contains("«Tester» (@tester, id bbbbbbbb2222)", note);
        Assert.Contains("pedir_a_especialista", note);
        Assert.Equal(text, Workspace.Strip(text + note));

        var only = SpecialistTools.Directive(SpecialistTools.Mentions("@revisor mira", all), subagents)!;
        Assert.DoesNotContain("pedir_a_especialista", only);
        Assert.Contains("pedir_a_especialista", SpecialistTools.SubagentsNote(subagents)); // prefiera sus subagentes a delegar aparte
        Assert.Null(SpecialistTools.SubagentsNote([]));
    }

    [Fact]
    public void ElEspecialistaDeLaPestanaVaUnaVezPorCambio() => Headless.Run(() =>
    {
        var all = Specialists.All();
        var reviewer = all.Single(s => s.Name == "Revisor de PRs");
        var tester = all.Single(s => s.Name == "Tester");
        Assert.Contains("«Revisor de PRs»", SpecialistTools.RoleNote(reviewer, null));
        Assert.Contains("stopped using the specialist «Tester»", SpecialistTools.RoleNote(null, tester));
        Assert.Null(SpecialistTools.RoleNote(null, null));

        var pane = new TerminalPane();
        var tab = new TerminalPane.Tab(new TerminalAgent(new AgentOptions(_dir.Path), a => a()), "cuenta");
        // Elegido: la nota sale con el próximo pedido y no con los siguientes.
        pane.ChooseRole(tab, reviewer);
        var first = TerminalPane.SpecialistNotes(tab, "revisa esto")!;
        Assert.Contains("work as that specialist", first);
        Assert.Contains("Revisa", first); // su rol e instrucciones
        Assert.Equal("revisa esto", Workspace.Strip("revisa esto" + first));
        Assert.Null(TerminalPane.SpecialistNotes(tab, "y esto otro"));

        // Cambiado dos veces antes de escribir: solo el último.
        pane.ChooseRole(tab, tester);
        pane.ChooseRole(tab, reviewer);
        var again = TerminalPane.SpecialistNotes(tab, "sigue")!;
        Assert.Contains("«Revisor de PRs»", again);
        Assert.DoesNotContain("«Tester»", again);

        // Quitado después de que la sesión lo supo: que deje su rol. Elegido y quitado sin escribir: nada.
        pane.ChooseRole(tab, null);
        Assert.Contains("stopped using", TerminalPane.SpecialistNotes(tab, "listo"));
        pane.ChooseRole(tab, tester);
        pane.ChooseRole(tab, null);
        Assert.Null(TerminalPane.SpecialistNotes(tab, "nada"));
        Assert.Null(tab.Role);
    });

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
