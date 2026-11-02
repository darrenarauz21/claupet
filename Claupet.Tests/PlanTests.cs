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
        foreach (var view in new[] { "Resumen", "Tablero", "Cronograma", "Código" }) Assert.Contains(view, names);

        if (Environment.GetEnvironmentVariable("CLAUPET_PLAN_SHOT") is { Length: > 0 } shot)
            window.CaptureRenderedFrame()?.Save(shot);
        pane.Close();
        window.Close();
    });

    static IEnumerable<string?> Texts(Avalonia.Visual root) => root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text);
}
