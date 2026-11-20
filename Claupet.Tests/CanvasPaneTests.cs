using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Claupet.Tests;

/// <summary>El panel de Claupet Canvas en la Console: se dibuja con cada tipo de sección y sigue los cambios de los agentes.</summary>
public class CanvasPaneTests
{
    static CanvasOp Insert(string kind, string content, string? language = null, string? title = null) =>
        new("insert", Kind: kind, Content: content, Language: language, Title: title);

    [Fact]
    public void Dibuja_todos_los_tipos_y_lo_que_cambia_un_agente() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var store = new CanvasStore(dir.Path);
        var doc = store.Create("Informe de prueba", "Claude Code",
        [
            Insert("markdown", "# Resumen\n\nTodo **bien**, con una tabla:\n\n| a | b |\n|---|---|\n| 1 | 2 |\n"),
            Insert("code", "def hola():\n    return 'mundo'\n", "python", "Ejemplo"),
            Insert("html", "<h1>App</h1>"),
            Insert("diagram", "graph TD\n  A --> B\n", "mermaid"),
            Insert("data", "nombre,edad\nAna,30\n\"Pérez, Luis\",41\n", "csv"),
            Insert("data", "{\"a\":1,\"b\":[1,2]}", "json"),
            Insert("svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"),
            Insert("image", "https://example.com/x.png"),
        ]);
        store.Apply(doc.Id, [new("comment", Section: "s1", Text: "¿Más corto?", Quote: "Todo")], CanvasJson.User);

        var terminal = new TerminalPane();
        terminal.SetInk(Ink.For(false));
        var pane = new CanvasPane((text, folder) => terminal.MarkdownPage(text, folder), store);
        pane.SetInk(Ink.For(false));
        var window = new Avalonia.Controls.Window { Width = SizeW(), Height = 1500, Content = pane };
        window.Show();
        Headless.Frame(window);
        Assert.Equal(doc.Id, pane.CurrentId);

        // Un agente edita desde otro proceso: el panel lo ve solo.
        store.Apply(doc.Id, [new("edit", Id: "s1", Find: "Todo **bien**", Replace: "Todo **muy bien**")], "Codex");
        var until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until && !Glows(window)) { Thread.Sleep(50); Dispatcher.UIThread.RunJobs(); }
        Headless.Frame(window);
        Assert.True(Glows(window), "la sección que cambió Codex se marca con su color");
        Assert.Contains(Texts(window), t => t == "v3");
        Assert.Contains(Texts(window), t => t == "X");

        if (Environment.GetEnvironmentVariable("CLAUPET_CANVAS_SHOT") is { Length: > 0 } shot)
            window.CaptureRenderedFrame()?.Save(shot);
        pane.Close();
        window.Close();
        Dispatcher.UIThread.RunJobs();
    });

    [Fact]
    public void Al_abrir_la_Console_no_se_lee_ni_se_dibuja_hasta_que_se_ve() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var store = new CanvasStore(dir.Path);
        store.Create("Viejo", "Claude Code", [Insert("markdown", "uno")]);
        var reciente = store.Create("Reciente", "Codex", [Insert("markdown", "Lo de **ahora**")]);

        var terminal = new TerminalPane();
        terminal.SetInk(Ink.For(true));
        var pane = new CanvasPane((text, folder) => terminal.MarkdownPage(text, folder), store);
        pane.SetInk(Ink.For(true));
        // Construido y con sus colores (lo que hace la Console al abrir), todavía nada: ni la lista ni el documento.
        Assert.False(pane.DocumentLoaded);
        Assert.Empty(Texts(pane));

        // Al verse, el más reciente, con sus colores.
        var window = new Avalonia.Controls.Window { Width = 1200, Height = 900, Content = pane };
        window.Show();
        Headless.Frame(window);
        Assert.True(pane.DocumentLoaded);
        Assert.Equal(reciente.Id, pane.CurrentId);
        Assert.Contains(Texts(window), t => t.Contains("Reciente"));
        pane.Close();
        window.Close();
        Dispatcher.UIThread.RunJobs();
    });

    [Fact]
    public void Un_canvas_pedido_antes_de_verse_se_abre_ese_y_no_el_mas_reciente() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var store = new CanvasStore(dir.Path);
        var pedido = store.Create("Pedido", "Claude Code", [Insert("markdown", "uno")]);
        store.Create("Reciente", "Codex", [Insert("markdown", "dos")]);
        var terminal = new TerminalPane();
        var pane = new CanvasPane((text, folder) => terminal.MarkdownPage(text, folder), store);
        var listed = 0;
        pane.ListChanged += () => listed++;

        pane.Show(pedido.Id);
        Assert.True(pane.DocumentLoaded);
        Assert.Equal(pedido.Id, pane.CurrentId);
        Assert.True(listed > 0, "la lista de la Console se entera");
        pane.Close();
    });

    [Fact]
    public void Con_los_mismos_colores_no_se_vuelve_a_dibujar() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var store = new CanvasStore(dir.Path);
        store.Create("Informe", "Claude Code", [Insert("markdown", "# Hola\n\nTexto")]);
        var terminal = new TerminalPane();
        var pane = new CanvasPane((text, folder) => terminal.MarkdownPage(text, folder), store);
        var ink = Ink.For(false);
        pane.SetInk(ink);
        var window = new Avalonia.Controls.Window { Width = 1200, Height = 900, Content = pane };
        window.Show();
        Headless.Frame(window);
        object? Page() => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(pane).OfType<ScrollViewer>().First().Content;
        var drawn = Page();
        Assert.NotNull(drawn);

        // La Console lo llama en cada cambio de pestaña: con los mismos colores, el documento queda como estaba.
        pane.SetInk(ink);
        Assert.Same(drawn, Page());
        // Con otros colores (otro tema, modo oscuro), sí se vuelve a dibujar.
        pane.SetInk(Ink.For(true));
        Assert.NotSame(drawn, Page());
        pane.Close();
        window.Close();
        Dispatcher.UIThread.RunJobs();
    });

    static double SizeW() => double.TryParse(Environment.GetEnvironmentVariable("CLAUPET_CANVAS_WIDTH"), out var w) ? w : 1200;

    static bool Glows(Avalonia.Controls.Control root) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<Avalonia.Controls.Border>().Any(b => b.Tag is "glow");

    static IEnumerable<string> Texts(Avalonia.Controls.Control root) =>
        root.GetLogicalDescendantsOfType<Avalonia.Controls.TextBlock>().Select(t => t.Text ?? "");
}

static class LogicalExtensions
{
    public static IEnumerable<T> GetLogicalDescendantsOfType<T>(this Avalonia.LogicalTree.ILogical root) =>
        Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root).OfType<T>();
}
