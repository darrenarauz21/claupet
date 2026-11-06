using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Claupet.Tests;

/// <summary>Un canvas ligado a un plan: la operación, lo que se ve en la barra y el salto a Proyectos.</summary>
public sealed class CanvasPlanLinkTests
{
    const string Sample = """{ "titulo": "Lanzamiento", "tareas": [ { "id": "a", "titulo": "Una", "estado": "hecho", "dias": 1 }, { "id": "b", "titulo": "Otra", "dias": 1 } ] }""";

    [Fact]
    public void LigarEsUnaVersionMasYSePuedeQuitar()
    {
        using var dir = new TempDir();
        dir.Write("proyecto/lanzamiento.plan.json", Sample);
        var store = new CanvasStore(dir["canvas"]);
        var doc = store.Create("Notas", "Codex", folder: dir["proyecto"]);

        var linked = store.Apply(doc.Id, [new CanvasOp("plan", Content: dir["proyecto/lanzamiento.plan.json"])], "Codex").Doc;
        Assert.Equal(dir["proyecto/lanzamiento.plan.json"], linked.Plan);
        Assert.Equal(2, linked.Version);
        Assert.Equal(linked.Plan, store.List().Single().Plan);
        Assert.Contains($"plan=\"{linked.Plan}\"", CanvasExport.ForAgent(linked));

        var none = store.Apply(doc.Id, [new CanvasOp("plan")], "Codex").Doc;
        Assert.Null(none.Plan);

        // La de antes se puede recuperar, con su plan.
        Assert.Equal(linked.Plan, store.Restore(doc.Id, 2, CanvasJson.User).Plan);
    }

    [Fact]
    public void UnaRutaRelativaEsDeLaCarpetaDelCanvas()
    {
        using var dir = new TempDir();
        dir.Write("proyecto/planes/a.plan.json", Sample);
        var store = new CanvasStore(dir["canvas"]);
        var doc = store.Create("Notas", "Codex", folder: dir["proyecto"]);
        var linked = store.Apply(doc.Id, [new CanvasOp("plan", Content: "planes/a.plan.json")], "Codex").Doc;
        Assert.Equal(Path.GetFullPath(dir["proyecto/planes/a.plan.json"]), linked.Plan);
    }

    [Fact]
    public void NoSeLigaLoQueNoEsUnPlanNiLoQueNoExiste()
    {
        using var dir = new TempDir();
        dir.Write("notas.txt", "hola");
        var store = new CanvasStore(dir["canvas"]);
        var doc = store.Create("Notas", "Codex");
        Assert.Throws<CanvasException>(() => store.Apply(doc.Id, [new CanvasOp("plan", Content: dir["notas.txt"])], "Codex"));
        Assert.Throws<CanvasException>(() => store.Apply(doc.Id, [new CanvasOp("plan", Content: dir["falta.plan.json"])], "Codex"));
        Assert.Throws<CanvasException>(() => store.Apply(doc.Id, [new CanvasOp("plan", Content: "a.plan.json")], "Codex")); // sin carpeta
        Assert.Null(store.Load(doc.Id)!.Plan);
        Assert.Equal(1, store.Load(doc.Id)!.Version);
    }

    [Fact]
    public void UnAgenteLigaElPlanAlCrearElCanvas()
    {
        using var dir = new TempDir();
        dir.Write("proyecto/a.plan.json", Sample);
        var store = new CanvasStore(dir["canvas"]);
        var server = new CanvasMcp.Server(store, "Codex", dir["proyecto"]);
        var request = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "canvas_create", arguments = new { title = "Notas", plan = "a.plan.json", open = false } } });
        var reply = server.Handle(request);
        Assert.NotNull(reply);
        Assert.Equal(Path.GetFullPath(dir["proyecto/a.plan.json"]), store.List().Single().Plan);
    }

    sealed class Scene : IDisposable
    {
        public TempDir Dir { get; } = new();
        public CanvasStore Store { get; }
        public CanvasPane Pane { get; }
        public Window Window { get; }
        public List<string> Opened { get; } = [];

        public Scene()
        {
            Dir.Write("proyecto/lanzamiento.plan.json", Sample);
            Store = new CanvasStore(Dir["canvas"]);
            Store.Create("Notas", CanvasJson.User, folder: Dir["proyecto"]);
            var terminal = new TerminalPane();
            Pane = new CanvasPane((text, folder) => terminal.MarkdownPage(text, folder), Store);
            Pane.PlanOpened += Opened.Add;
            Window = new Window { Width = 1200, Height = 700, Content = Pane };
            Window.Show();
            Headless.Frame(Window);
        }

        public TextBlock? Text(string text) => Pane.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == text);

        public void Click(Avalonia.Visual visual)
        {
            var at = visual.TranslatePoint(new Avalonia.Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), Window)!.Value;
            Window.MouseMove(at);
            Window.MouseDown(at, MouseButton.Left);
            Window.MouseUp(at, MouseButton.Left);
            Headless.Frame(Window);
        }

        public void Dispose()
        {
            Pane.Close();
            Window.Close();
            Dir.Dispose();
        }
    }

    [Fact]
    public void LaBarraMuestraElPlanYLlevaAProyectos() => Headless.Run(() =>
    {
        using var scene = new Scene();
        Assert.Null(scene.Text("Lanzamiento"));

        scene.Pane.LinkPlan(scene.Dir["proyecto/lanzamiento.plan.json"]);
        Headless.Frame(scene.Window);
        var name = scene.Text("Lanzamiento");
        Assert.NotNull(name);
        Assert.Equal(CanvasJson.User, scene.Store.Load(scene.Pane.CurrentId!)!.Changes[^1].Author);

        scene.Click(name);
        Assert.Equal([scene.Dir["proyecto/lanzamiento.plan.json"]], scene.Opened);

        scene.Pane.LinkPlan(null);
        Headless.Frame(scene.Window);
        Assert.Null(scene.Text("Lanzamiento"));
    });

    [Fact]
    public void SiElPlanYaNoEstaSeAvisaYNoLlevaANingunLado() => Headless.Run(() =>
    {
        using var scene = new Scene();
        scene.Pane.LinkPlan(scene.Dir["proyecto/lanzamiento.plan.json"]);
        Headless.Frame(scene.Window);
        File.Delete(scene.Dir["proyecto/lanzamiento.plan.json"]);
        scene.Pane.SetInk(Ink.For(false));
        Headless.Frame(scene.Window);

        var name = scene.Text("lanzamiento.plan.json");
        Assert.NotNull(name);
        scene.Click(name);
        Assert.Empty(scene.Opened);
    });
}
