using System.Reflection;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace Claupet.Tests;

/// <summary>
/// El historial de la Terminal: títulos cortos (los de Claude Code, los que escribe el asistente elegido o el pedido
/// recortado), la búsqueda y las páginas; y lo que pide la isla con «Abrir en la terminal».
/// </summary>
public sealed class HistorialTests
{
    static string Line(string json) => json + "\n";

    static string Prompt(string text) =>
        Line("""{"type":"user","message":{"role":"user","content":""" + System.Text.Json.JsonSerializer.Serialize(text) + "}}");

    /// <summary>Una sesión de Claude Code en la carpeta de proyecto de folder; devuelve su id.</summary>
    static string Session(TempDir account, string folder, string body, DateTime? when = null)
    {
        var id = Guid.NewGuid().ToString();
        var dir = ClaudeHistory.ProjectDir(account.Path, folder);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, id + ".jsonl");
        File.WriteAllText(file, body);
        if (when is { } w) File.SetLastWriteTime(file, w);
        return id;
    }

    [Theory]
    [InlineData("otra cosa [Imagen #1]  que cuando cierre la app no quiero que se cierren las sesiones", "Otra cosa que cuando cierre la app no quiero que se cierren…")]
    [InlineData("Agentes y Dots, ¿donde quedamos? y luego seguimos con lo demás", "Agentes y Dots, ¿donde quedamos?")]
    [InlineData("descarga los cambios del repositorio", "Descarga los cambios del repositorio")]
    [InlineData("[Image #2] arregla esto. Y lo otro también", "Arregla esto")]
    public void ElTituloProvisionalEsElPedidoRecortado(string prompt, string title) => Assert.Equal(title, SessionTitles.Short(prompt));

    [Theory]
    [InlineData("\"Terminal no abre pestañas.\"", "Terminal no abre pestañas")]
    [InlineData("Título: Historial con buscador\nEs un buen título porque…", "Historial con buscador")]
    [InlineData("**Isla abre la terminal**", "Isla abre la terminal")]
    [InlineData("«Rediseño del Canvas»", "Rediseño del Canvas")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void ElTituloDelModeloSeLimpia(string? answer, string? title) => Assert.Equal(title, SessionTitles.Clean(answer));

    sealed class FakeBrain(string? answer, bool fails = false) : IAssistantBrain
    {
        public string Name => "prueba";
        public int Calls;
        public BrainRequest? Last;

        public Task<BrainReply> RespondAsync(BrainRequest request, Action<string>? onText, CancellationToken cancel)
        {
            Calls++;
            Last = request;
            if (fails) throw new HttpRequestException("sin conexión");
            return Task.FromResult(new BrainReply(answer ?? "", null));
        }
    }

    [Fact]
    public async Task ElTituloLoEscribeElAsistenteElegido()
    {
        var external = new FakeBrain("Gemini: título");
        var local = new FakeBrain("Local");
        var claude = 0;
        var title = await SessionTitles.AskAsync("arregla el historial", external, local, cloud: true, (_, _) => { claude++; return Task.FromResult<string?>("Haiku"); }, default);
        Assert.Equal("Gemini: título", title);
        Assert.Equal(0, local.Calls);
        Assert.Equal(0, claude);
        Assert.Equal(SessionTitles.System, external.Last!.System);
        Assert.Equal("arregla el historial", external.Last.Messages.Single().Text);
    }

    [Fact]
    public async Task SinExtensionUsaElModeloLocalYDespuesClaude()
    {
        var local = new FakeBrain("Historial con buscador");
        Assert.Equal("Historial con buscador", await SessionTitles.AskAsync("x", null, local, cloud: true, (_, _) => Task.FromResult<string?>("Haiku"), default));

        var broken = new FakeBrain(null, fails: true);
        Assert.Equal("Haiku", await SessionTitles.AskAsync("x", null, broken, cloud: true, (_, _) => Task.FromResult<string?>("Haiku"), default));
        Assert.Equal(1, broken.Calls);
    }

    [Fact]
    public async Task ClaudeNoSeUsaSiLasRespuestasNoSalenDelEquipoNiConUnaExtension()
    {
        var claude = 0;
        Task<string?> Claude(string _, CancellationToken __) { claude++; return Task.FromResult<string?>("Haiku"); }
        Assert.Null(await SessionTitles.AskAsync("x", null, null, cloud: false, Claude, default));
        Assert.Null(await SessionTitles.AskAsync("x", new FakeBrain(null, fails: true), null, cloud: true, Claude, default));
        Assert.Equal(0, claude);
    }

    [Fact]
    public async Task AlAsistenteSoloLeLlegaElComienzoDelPedido()
    {
        var brain = new FakeBrain("Título");
        await SessionTitles.AskAsync(new string('a', 5000), brain, null, cloud: true, (_, _) => Task.FromResult<string?>(null), default);
        Assert.Equal(SessionTitles.PromptLimit, brain.Last!.Messages.Single().Text.Length);
    }

    [Fact]
    public void ElTituloEscritoSeRecuerda()
    {
        var id = Guid.NewGuid().ToString();
        Assert.Null(SessionTitles.Cached(id));
        SessionTitles.Remember(id, "Isla abre la terminal");
        Assert.Equal("Isla abre la terminal", SessionTitles.Cached(id));
    }

    [Fact]
    public void UsaElTituloQueGuardoClaudeCode()
    {
        using var account = new TempDir();
        var folder = Path.Combine(account.Path, "repo");
        var ai = Session(account, folder, Prompt("me gustaria rediseñar un poco el CANVA principalmente la parte de arriba")
            + Line("""{"type":"ai-title","aiTitle":"Rediseño del Canvas","sessionId":"x"}"""));
        var renamed = Session(account, folder, Prompt("hola")
            + Line("""{"type":"ai-title","aiTitle":"Saludo","sessionId":"x"}""")
            + Line("""{"type":"custom-title","customTitle":"Mi sesión","sessionId":"x"}"""));
        var plain = Session(account, folder, Prompt("otra cosa [Imagen #1] que cuando cierre la app no quiero que se cierren"));

        var list = ClaudeHistory.List(account.Path, folder).ToDictionary(p => p.Id);
        Assert.Equal(("Rediseño del Canvas", true), (list[ai].Title, list[ai].Named));
        Assert.Equal(("Mi sesión", true), (list[renamed].Title, list[renamed].Named));
        Assert.Equal(("Otra cosa que cuando cierre la app no quiero que se cierren", false), (list[plain].Title, list[plain].Named));
        Assert.StartsWith("otra cosa [Imagen #1]", list[plain].Prompt);

        SessionTitles.Remember(plain, "Sesiones al cerrar la app");
        var again = ClaudeHistory.List(account.Path, folder).Single(p => p.Id == plain);
        Assert.Equal(("Sesiones al cerrar la app", true), (again.Title, again.Named));
    }

    [Fact]
    public void BuscaEnElTituloYEnElPedidoSinTildes()
    {
        using var account = new TempDir();
        var folder = Path.Combine(account.Path, "repo");
        var canvas = Session(account, folder, Prompt("rediseñar el canvas") + Line("""{"type":"ai-title","aiTitle":"Rediseño del Canvas"}"""));
        var island = Session(account, folder, Prompt("abrir en la terminal en la isla debería abrir Claupet"));
        Session(account, folder, Prompt("descarga los cambios"));

        Assert.Equal([canvas], ClaudeHistory.List(account.Path, folder, query: "rediseno").Select(p => p.Id));
        Assert.Equal([island], ClaudeHistory.List(account.Path, folder, query: "DEBERIA").Select(p => p.Id));
        Assert.Empty(ClaudeHistory.List(account.Path, folder, query: "opencode"));
        Assert.Equal(3, ClaudeHistory.List(account.Path, folder, query: "  ").Count);
    }

    [Fact]
    public void LasPaginasVanDeLaMasReciente()
    {
        using var account = new TempDir();
        var folder = Path.Combine(account.Path, "repo");
        var ids = Enumerable.Range(0, 20).Select(i => Session(account, folder, Prompt($"pedido {i}"), DateTime.Now.AddMinutes(-i))).ToList();
        // Una sin pedido (solo un aviso interno) no cuenta.
        Session(account, folder, Line("""{"type":"user","message":{"role":"user","content":"<command-name>/model</command-name>"}}"""), DateTime.Now);

        Assert.Equal(ids.Take(16), ClaudeHistory.List(account.Path, folder, 16).Select(p => p.Id));
        Assert.Equal(ids, ClaudeHistory.List(account.Path, folder, 31).Select(p => p.Id));
    }

    [Fact]
    public void EncuentraUnaSesionPorSuId()
    {
        using var account = new TempDir();
        var folder = Path.Combine(account.Path, "repo");
        var id = Session(account, folder, Prompt("retomar esta"));
        Assert.Equal("Retomar esta", ClaudeHistory.Find(account.Path, folder, id)!.Title);
        Assert.Null(ClaudeHistory.Find(account.Path, folder, Guid.NewGuid().ToString()));
        Assert.Null(ClaudeHistory.Find(account.Path, folder, "..\\otra"));
    }

    [Fact]
    public void LaIslaPideLaSesionComoUnaSeccion()
    {
        var target = new SessionTarget(Guid.NewGuid().ToString(), @"C:\Users\x\Documents\mi repo", @"C:\Users\x\.claude-trabajo");
        Assert.StartsWith("sesion:", target.Section);
        Assert.Equal(target, SessionTarget.Parse(target.Section));
        Assert.Null(SessionTarget.Parse("general"));
        Assert.Null(SessionTarget.Parse(null));
        Assert.Null(SessionTarget.Parse("sesion:{roto"));
        Assert.Null(SessionTarget.Parse("""sesion:{"Id":"","Folder":"x"}"""));
    }

    [Fact]
    public void UnaSesionQueYaTerminoNoCorreFuera()
    {
        Assert.False(Terminals.RunsOutside(0, Environment.ProcessPath));
        // Este mismo proceso está «dentro» de sí mismo.
        Assert.False(Terminals.RunsOutside(Environment.ProcessId, Environment.ProcessPath));
    }

    [Fact]
    public void ElPanelMuestraQuinceYCargarMas() => Headless.Run(() =>
    {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        using var dir = new TempDir();
        var pane = new TerminalPane();
        pane.SetInk(Ink.For(true));
        var tab = new TerminalPane.Tab(new TerminalAgent(new AgentOptions(dir.Path), _ => { }), "cuenta");
        var asked = new List<string>();
        pane.AskTitle = (id, prompt) => { asked.Add(id); return true; };
        IReadOnlyList<PastSession> sessions = Enumerable.Range(0, 16)
            .Select(i => new PastSession($"s{i}", $"Pedido {i}", DateTime.Now, dir[$"s{i}.jsonl"], $"pedido {i}", Named: i % 2 == 0)).ToList();
        typeof(TerminalPane).GetMethod("ShowHistory", Private)!.Invoke(pane, [tab, sessions]);
        var body = (StackPanel)typeof(TerminalPane).GetField("_sideBody", Private)!.GetValue(pane)!;

        var texts = body.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Pedido 14", texts);
        Assert.DoesNotContain("Pedido 15", texts);
        Assert.Contains(L.T("Cargar más"), texts);
        Assert.True(body.Children[0].IsVisible);
        // Solo se piden los títulos que faltan, de las que se ven.
        Assert.Equal(Enumerable.Range(0, 15).Where(i => i % 2 == 1).Select(i => $"s{i}"), asked);

        pane.TitleArrived("s1", "\"Título corto.\"");
        Assert.Contains("Título corto", body.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));
        tab.Agent.Dispose();
    });
}
