using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
// El proyecto de pruebas también trae WinForms (nombres iguales a los de Avalonia).
using TextBlock = Avalonia.Controls.TextBlock;
using Window = Avalonia.Controls.Window;

namespace Claupet.Tests;

/// <summary>Los enlaces de las respuestas y la vista de archivos: la tarjeta flotante, fijarla en el panel y quitarla.</summary>
public sealed class TerminalViewerTests
{
    [Fact]
    public void LosArchivosYLasUrlDeUnaRespuestaVanEnAzulYElTextoQuedaIgual() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        dir.Write("docs/plan.md", "# Plan");
        var (pane, window) = Open(dir.Path);

        const string answer = "Escribí el plan en `docs/plan.md`.\nMás en https://claupet.dev/docs y en otro.md.";
        var block = pane.Paragraph(answer);
        var runs = block.Inlines!.OfType<Run>().ToList();

        // Lo que se ve es el texto sin las comillas invertidas, con su salto de línea.
        Assert.Equal("Escribí el plan en docs/plan.md.\nMás en https://claupet.dev/docs y en otro.md.", string.Concat(runs.Select(r => r.Text)));
        var links = runs.Where(r => r.TextDecorations == TextDecorations.Underline).Select(r => r.Text).ToList();
        Assert.Equal(["docs/plan.md", "https://claupet.dev/docs"], links); // otro.md no existe: no es enlace
        Close(pane, window);
    });

    [Fact]
    public void UnArchivoSeAbreEnLaTarjetaSeFijaALaDerechaYSeQuita() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var plan = dir.Write("docs/plan.md", "# Plan\n\n- Paso uno\n- Paso dos\n");
        var (pane, window) = Open(dir.Path);
        var tab = new TerminalPane.Tab(new TerminalAgent(new AgentOptions(dir.Path), a => a()), "cuenta-de-prueba");
        pane.Add(tab);
        Headless.Frame(window);

        pane.OpenFile(plan);
        Headless.Frame(window);
        Assert.Equal(plan, tab.Preview?.Path);
        Assert.Contains(Texts(pane), t => t == "plan.md"); // la tarjeta, con el nombre del archivo

        pane.Dock(plan);
        Headless.Frame(window);
        Assert.Null(tab.Preview);
        Assert.Equal([plan], tab.Docked);
        Assert.Equal(plan, tab.SideFile);
        Assert.Contains(Texts(pane), t => t == "plan.md"); // en el panel, con su ×

        // Abrirlo otra vez lo muestra en el panel (no otra tarjeta).
        pane.OpenFile(plan);
        Headless.Frame(window);
        Assert.Null(tab.Preview);

        pane.Undock(tab, plan);
        Headless.Frame(window);
        Assert.Empty(tab.Docked);
        Assert.Null(tab.SideFile);
        Close(pane, window);
    });

    [Fact]
    public void UnClicEnUnaUrlLaAbre() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var (pane, window) = Open(dir.Path);
        var opened = new List<string>();
        var before = TerminalPane.OpenUrl;
        TerminalPane.OpenUrl = opened.Add;
        try
        {
            const string text = "Vea https://claupet.dev/docs para más.";
            var block = pane.Paragraph(text);
            var host = new Window { Width = 600, Height = 200, Content = block };
            host.Show();
            Headless.Frame(host);
            var rect = block.TextLayout.HitTestTextPosition("Vea ".Length + 5);
            var point = block.TranslatePoint(rect.Center, host)!.Value;
            host.MouseDown(point, Avalonia.Input.MouseButton.Left);
            host.MouseUp(point, Avalonia.Input.MouseButton.Left);
            Headless.Frame(host);
            Assert.Equal(["https://claupet.dev/docs"], opened);
            host.Close();
        }
        finally
        {
            TerminalPane.OpenUrl = before;
            Close(pane, window);
        }
    });

    [Fact]
    public void UnMdSeLeeComoDocumento() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var (pane, window) = Open(dir.Path);
        const string md = """
            ---
            titulo: no se muestra
            ---
            # Plan de la 2.5.4

            Hay que **probar** todo; vea [la guía](https://claupet.dev/docs).

            - uno
            - dos
              1. dos punto uno
            - [x] hecho

            > Una cita.

            | Paso | Horas |
            |------|------:|
            | Caja | 2 |

            ```csharp
            var x = 1;
            ```
            """;
        var view = pane.MarkdownView(md, dir.Path);
        var host = new Window { Width = 900, Height = 900, Content = view };
        host.Show();
        Headless.Frame(host);
        var blocks = host.GetVisualDescendants().OfType<TextBlock>().ToList();
        string TextOf(TextBlock t) => t.Inlines is { Count: > 0 } inlines ? inlines.Text ?? "" : t.Text ?? "";
        var texts = blocks.Select(TextOf).ToList();

        var title = Assert.Single(blocks, t => TextOf(t) == "Plan de la 2.5.4");
        Assert.Equal(23, title.FontSize);
        Assert.DoesNotContain(texts, t => t.Contains("titulo") || t.Contains('#') || t.Contains("**"));
        Assert.Contains("Hay que probar todo; vea la guía.", texts);
        var paragraph = blocks.Single(t => TextOf(t).StartsWith("Hay que", StringComparison.Ordinal));
        var runs = paragraph.Inlines!.OfType<Run>().ToList();
        Assert.Equal(FontWeight.Bold, runs.Single(r => r.Text == "probar").FontWeight);
        Assert.Equal(TextDecorations.Underline, runs.Single(r => r.Text == "la guía").TextDecorations);
        Assert.Contains("•", texts);
        Assert.Contains("1.", texts);
        Assert.Contains("☑ hecho", texts);
        Assert.Contains("Una cita.", texts);
        Assert.Equal(FontWeight.SemiBold, blocks.Single(t => TextOf(t) == "Horas").FontWeight); // encabezado de la tabla
        Assert.Contains("Caja", texts);
        Assert.Contains("var x = 1;", texts);
        host.Close();
        Close(pane, window);
    });

    static IEnumerable<string?> Texts(Avalonia.Visual root) => root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text);

    static (TerminalPane Pane, Window Window) Open(string folder)
    {
        var pane = new TerminalPane();
        var window = new Window { Width = 1400, Height = 900, Content = pane };
        window.Show();
        Headless.Frame(window);
        // Paragraph busca los archivos en la carpeta de la pestaña elegida.
        pane.Add(new TerminalPane.Tab(new TerminalAgent(new AgentOptions(folder), a => a()), "cuenta-de-prueba"));
        Headless.Frame(window);
        return (pane, window);
    }

    static void Close(TerminalPane pane, Window window)
    {
        pane.CloseAll();
        window.Close();
    }
}
