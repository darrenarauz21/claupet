using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using TextBlock = Avalonia.Controls.TextBlock;
using Window = Avalonia.Controls.Window;

namespace Claupet.Tests;

/// <summary>Los planes (*.plan.json): se leen con tolerancia, las fechas que faltan se calculan y la tarjeta los dibuja.</summary>
public sealed class PlanTests
{
    static readonly DateOnly Today = new(2026, 11, 10);

    const string Sample = """
        {
          "claupet": "plan/1",
          "titulo": "Lanzar 2.4",
          "inicio": "2026-11-03",
          "etapas": [ { "id": "e1", "nombre": "Correcciones", "color": "azul" }, { "id": "e2", "nombre": "Vista nueva" } ],
          "tareas": [
            { "id": "t1", "etapa": "e1", "titulo": "Historial", "estado": "hecho", "dias": 2, "horas": 3, "horasReales": 2 },
            { "id": "t2", "etapa": "e2", "titulo": "Formato", "estado": "en-curso", "dias": 3, "depende": ["t1"] },
            { "id": "t3", "etapa": "e2", "titulo": "Tarjeta", "dias": 5, "depende": ["t2"] },
          ],
          "hitos": [ { "id": "h1", "titulo": "Publicar", "depende": ["t3"] } ]
        }
        """;

    [Fact]
    public void LasFechasQueFaltanSaleDeLoQueDepende()
    {
        var plan = Plan.Parse(Sample, Today);
        Assert.Equal("Lanzar 2.4", plan.Title);
        Assert.Equal(["t1", "t2", "t3"], plan.Tasks.Select(t => t.Id));
        Assert.Equal(new DateOnly(2026, 11, 3), plan.Tasks[0].Start);
        Assert.Equal(new DateOnly(2026, 11, 5), plan.Tasks[1].Start); // al terminar t1 (2 días)
        Assert.Equal(new DateOnly(2026, 11, 8), plan.Tasks[2].Start); // al terminar t2 (3 días)
        Assert.Equal(new DateOnly(2026, 11, 13), plan.Milestones[0].Date); // al terminar t3
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void ElAvancePesaPorDiasYLoAtrasadoSeVe()
    {
        var plan = Plan.Parse(Sample, Today);
        Assert.Equal(2.0 / 10, Plan.Progress(plan.Tasks), 3); // 2 de 10 días hechos
        Assert.True(plan.Tasks[1].LateOn(Today)); // t2 terminaba el 8 y sigue en curso
        Assert.False(plan.Tasks[0].LateOn(Today)); // hecha
        Assert.False(plan.Tasks[2].LateOn(Today));
    }

    [Fact]
    public void LoQueNoSeEntiendeQuedaEnAvisosYNoRompe()
    {
        var plan = Plan.Parse("""
            { "claupet": "plan/9", "tareas": [
              { "id": "a", "titulo": "A", "estado": "rarísimo", "depende": ["b", "fantasma"], "etapa": "nada" },
              { "id": "b", "titulo": "B", "depende": ["a"] } ] }
            """, Today);
        Assert.Equal(2, plan.Tasks.Count);
        Assert.Equal(PlanState.Pending, plan.Tasks[0].State);
        Assert.Contains(plan.Warnings, w => w.Contains("plan/9"));
        Assert.Contains(plan.Warnings, w => w.Contains("rarísimo"));
        Assert.Contains(plan.Warnings, w => w.Contains("fantasma"));
        Assert.Contains(plan.Warnings, w => w.Contains("nada"));
        Assert.Contains(plan.Warnings, w => w.Contains("una de la otra"));
    }

    [Fact]
    public void LoQueNoEsUnPlanDiceElMotivo()
    {
        Assert.Throws<FormatException>(() => Plan.Parse("no es json"));
        Assert.Throws<FormatException>(() => Plan.Parse("[1, 2]"));
    }

    [Theory]
    [InlineData("lanzamiento.plan.json", true)]
    [InlineData("C:\\x\\LANZAMIENTO.PLAN.JSON", true)]
    [InlineData("package.json", false)]
    [InlineData("plan.md", false)]
    public void SeReconocePorSuNombre(string path, bool plan) => Assert.Equal(plan, Plan.IsPlanFile(path));

    [Fact]
    public void LaTarjetaMuestraElPlanYNoSuJson() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var file = dir.Write("lanzar.plan.json", Sample);
        var pane = new TerminalPane();
        var window = new Window { Width = 1400, Height = 900, Content = pane };
        window.Show();
        var tab = new TerminalPane.Tab(new TerminalAgent(new AgentOptions(dir.Path), a => a()), "cuenta-de-prueba");
        pane.Add(tab);
        Headless.Frame(window);

        pane.OpenFile(file);
        Headless.Frame(window);
        var texts = Texts(pane).ToList();
        Assert.Contains("Lanzar 2.4", texts);
        Assert.Contains("Correcciones", texts);
        // Las vistas son íconos: se las reconoce por su nombre accesible (el mismo de su descripción).
        var names = pane.GetVisualDescendants().OfType<Avalonia.Controls.Control>().Select(Avalonia.Automation.AutomationProperties.GetName).Where(n => !string.IsNullOrEmpty(n)).ToList();
        Assert.Contains("Resumen", names);
        Assert.Contains("Tablero", names);
        Assert.Contains("Cronograma", names);
        Assert.Contains("Diagrama", names);
        Assert.Contains("Ver JSON", names);
        Assert.DoesNotContain(texts, t => t?.Contains("\"tareas\"") == true);

        pane.CloseAll();
        window.Close();
    });

    [Fact]
    public void UnaSeccionPlanDeCanvasSeDibujaConSusVistasYSeExportaComoJson() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var store = new CanvasStore(dir.Path);
        var doc = store.Create("Proyecto", "Claude Code",
        [
            new("insert", Kind: "markdown", Content: "# Proyecto"),
            new("insert", Kind: "plan", Language: "json", Content: Sample),
        ]);
        Assert.True(CanvasKinds.IsValid("plan"));
        Assert.Contains("```json", CanvasExport.Markdown(doc));

        var terminal = new TerminalPane();
        var pane = new CanvasPane((text, folder) => terminal.MarkdownPage(text, folder), store);
        var window = new Window { Width = 1100, Height = 1100, Content = pane };
        window.Show();
        Headless.Frame(window);
        Assert.Equal(doc.Id, pane.CurrentId);

        Assert.Contains("Lanzar 2.4", Texts(window)); // el resumen, no el JSON
        Assert.DoesNotContain(Texts(window), t => t?.Contains("\"tareas\"") == true);
        var names = pane.GetVisualDescendants().OfType<Avalonia.Controls.Control>().Select(Avalonia.Automation.AutomationProperties.GetName).Where(n => !string.IsNullOrEmpty(n)).ToList();
        foreach (var view in new[] { "Resumen", "Tablero", "Cronograma", "Diagrama", "Código" }) Assert.Contains(view, names);

        if (Environment.GetEnvironmentVariable("CLAUPET_PLAN_SHOT") is { Length: > 0 } shot)
        {
            // Las secciones entran con una animación: se espera a que termine.
            for (var i = 0; i < 8; i++)
            {
                Thread.Sleep(120);
                Headless.Frame(window);
            }
            window.CaptureRenderedFrame()?.Save(shot);
        }
        pane.Close();
        window.Close();
    });

    static Plan Tasks(string tasks, string milestones = "") =>
        Plan.Parse($$"""{ "inicio": "2026-11-03", "tareas": [ {{tasks}} ], "hitos": [ {{milestones}} ] }""", Today);

    [Fact]
    public void ElDiagramaPoneCadaTareaALaDerechaDeLoQueEspera()
    {
        var graph = PlanGraph.Build(Plan.Parse(Sample, Today));
        Assert.Equal(4, graph.Columns);
        Assert.Equal(1, graph.Rows);
        Assert.Equal(["t1", "t2", "t3", "◆h1"], graph.Nodes.OrderBy(n => n.Column).Select(n => n.Id));
        Assert.Equal(3, graph.Edges.Count);
        Assert.All(graph.Edges, e => Assert.False(e.Loop));

        // Un rombo: la última espera a las dos del medio, y la que llega de más lejos marca la columna.
        var diamond = PlanGraph.Build(Tasks("""
            { "id": "a", "titulo": "A" }, { "id": "b", "titulo": "B", "depende": ["a"] },
            { "id": "c", "titulo": "C", "depende": ["a"] }, { "id": "d", "titulo": "D", "depende": ["b", "c"] }
            """));
        Assert.Equal(3, diamond.Columns);
        Assert.Equal(2, diamond.Rows);
        Assert.Equal(2, diamond.Nodes.Single(n => n.Id == "d").Column);
        Assert.Equal(0.5, diamond.Nodes.Single(n => n.Id == "a").Row); // centrada respecto de las dos
    }

    [Fact]
    public void UnaLineaQueSaltaColumnasPasaPorSuPropioLugar()
    {
        var graph = PlanGraph.Build(Tasks("""
            { "id": "a", "titulo": "A" }, { "id": "b", "titulo": "B", "depende": ["a"] },
            { "id": "c", "titulo": "C", "depende": ["b"] }, { "id": "d", "titulo": "D", "depende": ["c", "a"] }
            """));
        var jump = graph.Edges.Single(e => e.From == "a" && e.To == "d");
        Assert.Equal([1, 2], jump.Through.Select(t => t.Column));
        // Ninguno de sus lugares coincide con una tarea.
        foreach (var (column, row) in jump.Through)
            Assert.DoesNotContain(graph.Nodes, n => n.Column == column && n.Row == row);
    }

    [Fact]
    public void ElDiagramaOrdenaLaColumnaParaQueLasLineasNoSeCrucen()
    {
        // A espera a Y y B a X, escritas en orden cruzado: al ordenar, cada una queda frente a la que espera.
        var graph = PlanGraph.Build(Tasks("""
            { "id": "x", "titulo": "X" }, { "id": "y", "titulo": "Y" },
            { "id": "a", "titulo": "A", "depende": ["y"] }, { "id": "b", "titulo": "B", "depende": ["x"] }
            """));
        double Row(string id) => graph.Nodes.Single(n => n.Id == id).Row;
        Assert.Equal(Row("x") < Row("y"), Row("b") < Row("a"));
    }

    [Fact]
    public void UnCirculoEnElDiagramaSeMarcaYNoSeCuelga()
    {
        // Con fechas escritas, ni siquiera Parse lo avisa: el diagrama es donde se ve.
        var graph = PlanGraph.Build(Tasks("""
            { "id": "a", "titulo": "A", "inicio": "2026-11-03", "depende": ["c"] },
            { "id": "b", "titulo": "B", "inicio": "2026-11-03", "depende": ["a"] },
            { "id": "c", "titulo": "C", "inicio": "2026-11-03", "depende": ["b", "c"] }
            """));
        Assert.Equal(3, graph.Nodes.Count);
        Assert.Equal(1, graph.Edges.Count(e => e.Loop));
        Assert.DoesNotContain(graph.Edges, e => e.From == e.To); // depender de sí misma no se dibuja
        Assert.Equal(["a", "b", "c"], graph.Chain("a").Order());
    }

    [Fact]
    public void LaCadenaDeUnaTareaEsLoQueNecesitaYLoQueLaEspera()
    {
        var graph = PlanGraph.Build(Tasks("""
            { "id": "a", "titulo": "A" }, { "id": "b", "titulo": "B", "depende": ["a"] },
            { "id": "c", "titulo": "C", "depende": ["a"] }, { "id": "d", "titulo": "D", "depende": ["b"] }, { "id": "z", "titulo": "Z" }
            """, """{ "id": "b", "titulo": "Hito", "depende": ["d"] }"""));
        Assert.Equal(["a", "b", "d", "◆b"], graph.Chain("b").Order(StringComparer.Ordinal)); // un hito puede llamarse igual que una tarea
        Assert.DoesNotContain("c", graph.Chain("b"));
        Assert.Equal(["z"], graph.Chain("z"));
    }

    [Fact]
    public void SeSabeQueTareaSePuedeEmpezar()
    {
        var plan = Tasks("""
            { "id": "a", "titulo": "A", "estado": "hecho" }, { "id": "b", "titulo": "B", "depende": ["a"] },
            { "id": "c", "titulo": "C", "depende": ["b"] }, { "id": "d", "titulo": "D", "estado": "en-curso" }
            """);
        Assert.Equal(["b"], plan.Tasks.Where(t => PlanGraph.Ready(plan, t)).Select(t => t.Id));
    }

    [Fact]
    public void ElDiagramaDibujaLasTareasYAlPasarElMouseApagaLoAjeno() => Headless.Run(() =>
    {
        var plan = Tasks("""
            { "id": "a", "titulo": "Primera", "estado": "hecho" }, { "id": "b", "titulo": "Segunda", "depende": ["a"] },
            { "id": "z", "titulo": "Suelta" }
            """, """{ "id": "h", "titulo": "Publicar", "depende": ["b"] }""");
        var view = new PlanPanel(Ink.For(true)).Graph(plan);
        var window = new Window { Width = 1000, Height = 600, Content = view };
        window.Show();
        Headless.Frame(window);
        var texts = Texts(window).ToList();
        foreach (var title in new[] { "Primera", "Segunda", "Suelta", "Publicar" }) Assert.Contains(title, texts);

        Avalonia.Controls.Control Card(string title) => window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == title)
            .GetVisualAncestors().OfType<Avalonia.Controls.Control>().Last(c => c.Parent is Avalonia.Controls.Canvas);
        var center = Card("Segunda").TranslatePoint(new Avalonia.Point(Card("Segunda").Bounds.Width / 2, Card("Segunda").Bounds.Height / 2), window)!.Value;
        window.MouseMove(center);
        Headless.Frame(window);
        Assert.Equal(1, Card("Primera").Opacity);
        Assert.Equal(1, Card("Publicar").Opacity);
        Assert.True(Card("Suelta").Opacity < 1);

        // Vuelve con una transición corta.
        window.MouseMove(new Avalonia.Point(990, 590));
        for (var i = 0; i < 20 && Card("Suelta").Opacity < 1; i++)
        {
            Thread.Sleep(30);
            Headless.Frame(window);
        }
        Assert.Equal(1, Card("Suelta").Opacity);

        if (Environment.GetEnvironmentVariable("CLAUPET_GRAFO_SHOT") is { Length: > 0 } shot)
        {
            window.Width = 1560;
            window.Height = 420;
            foreach (var dark in new[] { true, false })
            {
                var ink = Ink.For(dark);
                window.Background = ink.Window;
                window.Content = new PlanPanel(ink).Graph(Plan.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("CLAUPET_GRAFO_PLAN")!)));
                for (var i = 0; i < 5; i++) { Thread.Sleep(80); Headless.Frame(window); }
                window.CaptureRenderedFrame()?.Save(shot + (dark ? ".oscuro.png" : ".claro.png"));
            }
        }
        window.Close();
    });

    [Fact]
    public void UnPlanSinTareasDiceQueNoTieneYNoSeRompe() => Headless.Run(() =>
    {
        var view = new PlanPanel(Ink.For(true)).Graph(Tasks(""));
        var window = new Window { Width = 400, Height = 200, Content = view };
        window.Show();
        Headless.Frame(window);
        Assert.Contains("Este plan todavía no tiene tareas.", Texts(window).Append((view as TextBlock)?.Text));
        window.Close();
    });

    static IEnumerable<string?> Texts(Avalonia.Visual root) => root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text);
}
