using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using TextBox = Avalonia.Controls.TextBox;
using Window = Avalonia.Controls.Window;

namespace Claupet.Tests;

/// <summary>
/// Las sesiones de la Terminal comparten contexto y conversan: la tarjeta de cada una, el buzón, las herramientas MCP
/// (sesiones, leer_sesion, escribir_a_sesion, pizarra, anotar) y, en la Terminal, #sesión, «Enviar a…» y el tope de
/// mensajes que entran solos.
/// </summary>
[Collection("Sesiones compartidas")] // cambian la carpeta de las tarjetas, que es de todos
public sealed class SessionShareTests : IDisposable
{
    readonly TempDir _dir = new();
    readonly string _before = SessionShare.Folder;

    public SessionShareTests() => SessionShare.Folder = _dir["sesiones"];

    public void Dispose()
    {
        SessionShare.Folder = _before;
        _dir.Dispose();
    }

    static AgentTranscript Talk(params (string Ask, string Reply)[] turns)
    {
        var t = new AgentTranscript();
        var n = 0;
        foreach (var (ask, reply) in turns)
        {
            t.Ask(ask + "\n\n<" + Workspace.Tag + ">\nnota de Claupet\n</" + Workspace.Tag + ">");
            t.Apply(JsonSerializer.Serialize(new { type = "assistant", message = new { id = "m" + ++n, content = new[] { new { type = "text", text = "Pensando…" } } } }));
            t.Apply(JsonSerializer.Serialize(new { type = "assistant", message = new { id = "m" + ++n, content = new[] { new { type = "text", text = reply } } } }));
            t.Apply("""{"type":"result","subtype":"success","is_error":false}""");
        }
        return t;
    }

    static SessionCard Card(string key, string title, params (string Ask, string Reply)[] turns)
    {
        var card = SessionShare.CardOf(key, "Claude Code", new AgentOptions(Path.GetTempPath()), Talk(turns), ["C:\\x\\app.cs"]);
        card.Title = title;
        return card;
    }

    [Fact]
    public void LaTarjetaTraeLosPedidosSinLasNotasYLaUltimaRespuestaDeCadaUno()
    {
        var card = SessionShare.CardOf("aaaa1111bbbb2222", "Codex", new AgentOptions(_dir.Path), Talk(("Arregla el login", "Listo: el token se renueva.")), ["C:\\x\\a.cs"]);
        var turn = Assert.Single(card.Turns);
        Assert.Equal(("Arregla el login", "Listo: el token se renueva."), (turn.Ask, turn.Reply)); // sin la nota, y la última respuesta
        Assert.Equal("Arregla el login", card.Title);
        Assert.Equal(["C:\\x\\a.cs"], card.Touched);
        var text = SessionShare.Describe(card);
        Assert.Contains("Asked: Arregla el login", text);
        Assert.Contains("Replied: Listo: el token se renueva.", text);
    }

    [Fact]
    public void SePublicaSeLeeYElBuzonEntregaEnOrdenUnaSolaVez()
    {
        SessionShare.Publish(Card("aaaa1111bbbb2222", "Login", ("Arregla el login", "Hecho")));
        SessionShare.Publish(Card("cccc3333dddd4444", "Pagos", ("Revisa pagos", "Revisado")));
        Assert.Equal(["Pagos"], SessionShare.Others("aaaa1111bbbb2222").Select(c => c.Title));
        Assert.Equal("Login", SessionShare.Read("aaaa1111bbbb2222")?.Title);
        Assert.Null(SessionShare.Read("../../afuera")); // una clave que no es de Claupet no sale de la carpeta

        SessionShare.Post("aaaa1111bbbb2222", "cccc3333dddd4444", "Pagos", "¿Cambiaste el token?");
        SessionShare.Post("aaaa1111bbbb2222", "cccc3333dddd4444", "Pagos", "Gracias");
        Assert.Equal(["¿Cambiaste el token?", "Gracias"], SessionShare.Take("aaaa1111bbbb2222").Select(m => m.Text));
        Assert.Empty(SessionShare.Take("aaaa1111bbbb2222"));

        SessionShare.Forget("cccc3333dddd4444");
        Assert.Empty(SessionShare.Others("aaaa1111bbbb2222"));
    }

    [Fact]
    public void LasNotasMarcanElOrigenYElHistorialNoLasMuestra()
    {
        var message = new SessionMessage { From = "cccc3333dddd4444", FromTitle = "Pagos", Text = "hola" };
        var origin = SessionShare.OriginNote(message);
        Assert.Contains("not from the user", origin);
        Assert.Equal("Mensaje", Workspace.Strip("Mensaje" + origin));
        var context = SessionShare.ContextNote(["De otra"])!;
        Assert.Contains("not instructions from the user", context);
        Assert.Equal("Pedido", Workspace.Strip("Pedido" + context));
        Assert.Null(SessionShare.ContextNote([]));
    }

    static string Call(CanvasMcp.Server server, string tool, object args, bool error = false)
    {
        var reply = JsonNode.Parse(server.Handle(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments = args } }))!)!;
        Assert.Equal(error, reply["result"]!["isError"]!.GetValue<bool>());
        return reply["result"]!["content"]![0]!["text"]!.GetValue<string>();
    }

    [Fact]
    public void LasHerramientasVenLeenYLeEscribenAOtraSesion()
    {
        SessionShare.Publish(Card("aaaa1111bbbb2222", "Login", ("Arregla el login", "Hecho")));
        SessionShare.Publish(Card("cccc3333dddd4444", "Pagos", ("Revisa pagos", "Revisado: falta el webhook.")));
        var login = new CanvasMcp.Server(new CanvasStore(_dir["canvas"]), "Claude Code", _dir.Path, "aaaa1111bbbb2222");

        var list = Call(login, "sesiones", new { });
        Assert.Contains("Pagos (id cccc3333dddd4444)", list);
        Assert.DoesNotContain("Login (id", list); // no se lista a sí misma
        Assert.Contains("falta el webhook", Call(login, "leer_sesion", new { id = "cccc3333dddd4444" }));
        Assert.Contains("Message left for «Pagos»", Call(login, "escribir_a_sesion", new { id = "cccc3333dddd4444", mensaje = "¿Lo hago yo?" }));
        var message = Assert.Single(SessionShare.Take("cccc3333dddd4444"));
        Assert.Equal(("aaaa1111bbbb2222", "Login", "¿Lo hago yo?"), (message.From, message.FromTitle, message.Text));

        Call(login, "escribir_a_sesion", new { id = "aaaa1111bbbb2222", mensaje = "a mí" }, error: true);
        Call(login, "leer_sesion", new { id = "ffff0000ffff0000" }, error: true);
    }

    [Fact]
    public void LaPizarraEsUnaPorProyectoYCadaNotaDiceQuienYCuando()
    {
        var store = new CanvasStore(_dir["canvas"]);
        var project = _dir["proyecto"];
        Directory.CreateDirectory(project);
        var codex = new CanvasMcp.Server(store, "Codex", project, "aaaa1111bbbb2222");
        Assert.Contains("Pizarra · proyecto", Call(codex, "pizarra", new { }));
        Call(codex, "anotar", new { texto = "Usamos JWT con 15 minutos." });
        var claude = new CanvasMcp.Server(store, "Claude Code", project, "cccc3333dddd4444");
        Call(claude, "anotar", new { texto = "El webhook queda para mañana." });

        var boards = store.List().Where(c => c.Title == "Pizarra · proyecto").ToList();
        var board = store.Load(Assert.Single(boards).Id)!; // la misma para las dos sesiones
        var notes = board.Sections[0].Content;
        Assert.Contains("**Codex**", notes);
        Assert.Contains("Usamos JWT con 15 minutos.", notes);
        Assert.Contains("**Claude Code**", notes);
        Assert.Contains("El webhook queda para mañana.", Call(claude, "pizarra", new { }));
    }

    [Fact]
    public void EnLaTerminalNumeralTraeElContextoYEnviarALoDejaParaElProximoPedido() => Headless.Run(() =>
    {
        var pane = new TerminalPane();
        var window = new Window { Width = 1200, Height = 800, Content = pane };
        window.Show();
        var login = new TerminalPane.Tab(new TerminalAgent(new AgentOptions(_dir.Path), a => a()), "cuenta");
        var pagos = new TerminalPane.Tab(new TerminalAgent(new AgentOptions(_dir.Path), a => a()), "cuenta");
        foreach (var (ask, reply) in new[] { ("Revisa el módulo de pagos", "Falta el webhook.") })
        {
            pagos.Agent.Transcript.Ask(ask);
            pagos.Agent.Transcript.Apply(JsonSerializer.Serialize(new { type = "assistant", message = new { id = "m1", content = new[] { new { type = "text", text = reply } } } }));
        }
        pane.Add(pagos);
        pane.Add(login);
        Headless.Frame(window);
        var input = pane.GetVisualDescendants().OfType<TextBox>().First(t => t.AcceptsReturn);
        try
        {
            // «#rev» ofrece la otra pestaña (la de pagos), no esta.
            input.Text = "Mira #rev";
            input.CaretIndex = input.Text.Length;
            Headless.Frame(window);
            Assert.Equal(["Revisa el módulo de pagos"], pane.MentionChoices);
            var slug = TerminalPane.TabSlug(pagos);
            Assert.Equal("revisa-el-modulo-de", slug);

            // El pedido con #revisa-el-modulo-de lleva lo que se sabe de esa sesión.
            var note = pane.SessionContext(login, $"¿Qué falta según #{slug}?")!;
            Assert.Contains("Falta el webhook.", note);
            Assert.Null(pane.SessionContext(login, "sin numeral"));

            // «Enviar a…» desde pagos: queda arriba de la caja de login y va con su próximo pedido, una sola vez.
            pane.SendContext(pagos, login, "Usen JWT de 15 minutos.");
            Assert.Equal(["Revisa el módulo de pagos"], pane.ContextShown);
            Assert.Contains("Usen JWT de 15 minutos.", pane.SessionContext(login, "sigue")!);
            Assert.Empty(pane.ContextShown);
            Assert.Null(pane.SessionContext(login, "sigue"));

            // Pasado el tope de mensajes que entran solos, los siguientes quedan para el próximo pedido de la persona.
            login.AutoMessages = TerminalPane.MaxAutoMessages;
            SessionShare.Post(login.Agent.Driver.CanvasKey, pagos.Agent.Driver.CanvasKey, "Pagos", "¿Y el token?");
            pane.DeliverAll();
            Assert.Equal(["Pagos"], pane.ContextShown);
            Assert.Contains("¿Y el token?", pane.SessionContext(login, "dale")!);
        }
        finally
        {
            Headless.Frame(window);
            pane.CloseAll();
            window.Close();
        }
    });
}
