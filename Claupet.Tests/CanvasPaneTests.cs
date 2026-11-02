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
        var window = new Avalonia.Controls.Window { Width = 1200, Height = 1500, Content = pane };
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
