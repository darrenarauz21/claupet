using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
// El proyecto de pruebas también trae WinForms (nombres iguales a los de Avalonia).
using Border = Avalonia.Controls.Border;
using Control = Avalonia.Controls.Control;
using Panel = Avalonia.Controls.Panel;
using TextBox = Avalonia.Controls.TextBox;
using Point = Avalonia.Point;
using Window = Avalonia.Controls.Window;

namespace Claupet.Tests;

/// <summary>
/// La lista de Canvas en la Console: agrupada por el proyecto de cada canvas (como las sesiones de la Terminal), y con
/// todos los que trabajaron en él, no solo el último.
/// </summary>
public sealed class CanvasListTests
{
    static CanvasOp Text(string content) => new("insert", Kind: "markdown", Content: content);

    [Fact]
    public void ElResumenLlevaElProyectoYATodosLosQueTrabajaron()
    {
        using var dir = new TempDir();
        var store = new CanvasStore(dir.Path);
        var folder = Path.Combine(dir.Path, "proyectos", "alfa");
        var doc = store.Create("Informe", "Claude Code", [Text("uno")], folder: folder + Path.DirectorySeparatorChar);
        store.Apply(doc.Id, [Text("dos")], "OpenCode");
        store.Apply(doc.Id, [Text("tres")], CanvasJson.User);
        store.Apply(doc.Id, [Text("cuatro")], "Claude Code");

        var summary = Assert.Single(store.List());
        Assert.Equal(folder, summary.Project);
        Assert.Equal("Claude Code", summary.LastAuthor);
        // Del más reciente al más viejo, sin repetir.
        Assert.Equal(["Claude Code", CanvasJson.User, "OpenCode"], summary.AuthorList);
        // En la lista, los agentes primero y usted al final.
        Assert.Equal(["Claude Code", "OpenCode", CanvasJson.User], CanvasPane.AuthorsOf(summary));
    }

    [Fact]
    public void UnCanvasSinProyectoNiCambiosQuedaConQuienLoHizo()
    {
        using var dir = new TempDir();
        var store = new CanvasStore(dir.Path);
        store.Create("Suelto", null);

        var summary = Assert.Single(store.List());
        Assert.Null(summary.Project);
        Assert.Equal([CanvasJson.User], CanvasPane.AuthorsOf(summary));
        // Dos lecturas del mismo archivo dan resúmenes iguales: la lista no se rehace si nada cambió.
        Assert.Equal(summary, CanvasSummary.Of(store.Load(summary.Id)!));
    }

    [Fact]
    public void LaListaSeAgrupaPorProyectoSePliegaYSeBuscaPorQuienTrabajo() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var store = new CanvasStore(dir.Path);
        var alfa = Path.Combine(dir.Path, "proyectos", "alfa");
        var beta = Path.Combine(dir.Path, "proyectos", "beta");
        var informe = store.Create("Informe A", "Claude Code", [Text("uno")], folder: alfa);
        store.Apply(informe.Id, [Text("dos")], "OpenCode");
        store.Create("Plan A", "Claude Code", [Text("x")], folder: alfa);
        store.Create("Informe B", "Codex", [Text("x")], folder: beta);
        store.Create("Suelto", CanvasJson.User, [Text("x")]);

        var terminal = new TerminalPane();
        terminal.SetInk(Ink.For(true));
        var pane = new CanvasPane((text, f) => terminal.MarkdownPage(text, f), store);
        pane.SetInk(Ink.For(true));
        var list = Assert.IsAssignableFrom<Panel>(pane.ListView());
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("330,*") };
        layout.Children.Add(new ScrollViewer { Content = list });
        Grid.SetColumn(pane, 1);
        layout.Children.Add(pane);
        var window = new Window { Width = 1200, Height = 900, Content = layout };
        window.Show();
        Settle(window);
        try
        {
            // Un título por proyecto, con cuántos canvas tiene; los que no son de ninguno, al final.
            var heads = Heads(list);
            Assert.Equal(3, heads.Count);
            Assert.Equal([("alfa", "2"), ("beta", "1")], heads.Take(2).OrderBy(h => h.Name));
            Assert.Equal((L.T("Sin proyecto"), "1"), heads[2]);

            // Cada canvas, bajo su proyecto, con todos los que trabajaron en él.
            var titles = Titles(list);
            Assert.Equal(4, titles.Count);
            Assert.Equal(1, Math.Abs(titles.IndexOf("Informe A") - titles.IndexOf("Plan A")));
            Assert.Equal("Suelto", titles[^1]);
            Assert.Contains("OpenCode, Claude Code", Texts(list));

            // Plegar «alfa» esconde sus canvas; su título queda.
            Click(window, Head(list, "alfa"));
            Assert.DoesNotContain("Informe A", Titles(list));
            Assert.DoesNotContain("Plan A", Titles(list));
            Assert.Contains("alfa", Heads(list).Select(h => h.Name));

            // Al buscar se ve lo que coincide aunque esté plegado: por título, por proyecto o por quién trabajó.
            Search(list, "OpenCode");
            Assert.Equal(["Informe A"], Titles(list));
            Search(list, "beta");
            Assert.Equal(["Informe B"], Titles(list));
            Search(list, "");
            Assert.DoesNotContain("Informe A", Titles(list));

            Click(window, Head(list, "alfa"));
            Assert.Contains("Informe A", Titles(list));
        }
        finally
        {
            pane.Close();
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    // ── Andamio ─────────────────────────────────────────────────────────────────────────────

    static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        Headless.Frame(window);
    }

    static List<string?> Texts(Panel list) =>
        [.. list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text)];

    /// <summary>Los títulos de los canvas que se ven (el texto grande de cada fila).</summary>
    static List<string?> Titles(Panel list) =>
        [.. list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.FontSize == 14).Select(t => t.Text)];

    /// <summary>Los títulos de proyecto (el nombre y el número de canvas), en orden.</summary>
    static List<(string? Name, string? Count)> Heads(Panel list) =>
        [.. list.Children.OfType<Border>().Select(b => b.Child).OfType<Grid>()
            .Where(g => g.Children.OfType<TextBlock>().Count() == 2 && g.Children.OfType<TextBlock>().First().FontSize == 12)
            .Select(g => (g.Children.OfType<TextBlock>().First().Text, g.Children.OfType<TextBlock>().Last().Text))];

    static Control Head(Panel list, string name) =>
        list.Children.OfType<Border>().First(b => b.Child is Grid g && g.Children.OfType<TextBlock>().FirstOrDefault()?.Text == name);

    static void Search(Panel list, string text)
    {
        list.GetVisualDescendants().OfType<TextBox>().First().Text = text;
        Dispatcher.UIThread.RunJobs();
    }

    static void Click(Window window, Control control)
    {
        Settle(window);
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(point, MouseButton.Left);
        Settle(window);
    }
}
