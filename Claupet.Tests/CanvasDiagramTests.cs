using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
// El proyecto de pruebas también trae WinForms (nombres iguales a los de Avalonia).
using Border = Avalonia.Controls.Border;
using Window = Avalonia.Controls.Window;

namespace Claupet.Tests;

/// <summary>
/// Los diagramas de flujo que Canvas dibuja él mismo: los «&lt;br/&gt;» de Mermaid son saltos de línea y cada nodo
/// mide lo que su texto (antes se veían escritos y el nodo se cortaba con «…»).
/// </summary>
public sealed class CanvasDiagramTests
{
    [Theory]
    [InlineData("Persona en la Terminal<br/>@revisor", "Persona en la Terminal\n@revisor")]
    [InlineData("Anfitrión<br>Console", "Anfitrión\nConsole")]
    [InlineData("\"SpecialistRunner <BR /> Terminal oculta\"", "SpecialistRunner\nTerminal oculta")]
    [InlineData("Sin saltos", "Sin saltos")]
    public void LosSaltosDeMermaidSonSaltosDeLinea(string label, string text) =>
        Assert.Equal(text, CanvasPane.NodeText(label));

    [Fact]
    public void LosNodosConVariasLineasSeVenEnterosYSinBr() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var store = new CanvasStore(dir.Path);
        store.Create("Flujo", "Claude Code",
        [
            new CanvasOp("insert", Kind: "diagram", Language: "mermaid", Content:
                "graph LR\n" +
                "  P[Persona en la Terminal<br/>@revisor o /especialista] -.-> A[Anfitrión<br/>Console si está abierta; si no, la bandeja]\n" +
                "  A -.-> S[SpecialistRunner<br/>Terminal oculta con el especialista]\n"),
        ]);
        var terminal = new TerminalPane();
        terminal.SetInk(Ink.For(true));
        var pane = new CanvasPane((text, folder) => terminal.MarkdownPage(text, folder), store);
        pane.SetInk(Ink.For(true));
        var window = new Window { Width = 1400, Height = 900, Content = pane };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Headless.Frame(window);
        try
        {
            var diagram = window.GetVisualDescendants().OfType<Canvas>().Single(c => c.Children.OfType<Border>().Count() == 3);
            var nodes = diagram.Children.OfType<Border>().ToList();
            foreach (var node in nodes)
            {
                var text = Assert.IsType<TextBlock>(node.Child);
                Assert.DoesNotContain("<br", text.Text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains('\n', text.Text!);
                // Dos líneas o más, enteras: nada recortado con «…» y el nodo más alto que los de una línea (40).
                Assert.True(text.TextLayout.TextLines.Count >= 2, $"«{text.Text}» se ve en {text.TextLayout.TextLines.Count} línea(s)");
                Assert.DoesNotContain(text.TextLayout.TextLines, line => line.HasCollapsed);
                Assert.True(node.Bounds.Height > 40, $"El nodo de «{text.Text}» mide {node.Bounds.Height} de alto");
                Assert.True(text.Bounds.Height <= node.Bounds.Height && text.Bounds.Width <= node.Bounds.Width, "el texto cabe en su nodo");
            }
            Assert.Contains(nodes, n => ((TextBlock)n.Child!).Text == "Persona en la Terminal\n@revisor o /especialista");
            if (Environment.GetEnvironmentVariable("CLAUPET_DIAGRAM_SHOT") is { Length: > 0 } shot)
            {
                // Que termine la entrada de los nodos (aparecen de a uno) antes de la captura.
                for (var i = 0; i < 40; i++)
                {
                    Thread.Sleep(50);
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Dispatcher.UIThread.RunJobs();
                }
                window.CaptureRenderedFrame()?.Save(shot);
            }
        }
        finally
        {
            pane.Close();
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });
}
