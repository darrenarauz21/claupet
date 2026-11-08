using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Control = Avalonia.Controls.Control;
using Border = Avalonia.Controls.Border;
using Grid = Avalonia.Controls.Grid;
using Panel = Avalonia.Controls.Panel;
using Point = Avalonia.Point;
using Brushes = Avalonia.Media.Brushes;
using Avalonia.LogicalTree;
using SkiaSharp;
using TextBlock = Avalonia.Controls.TextBlock;
using Window = Avalonia.Controls.Window;

namespace Claupet.Tests;

/// <summary>La sección Proyectos: encuentra los planes, busca, filtra, cambia estados en el archivo y exporta a PDF.</summary>
public sealed class ProjectsPaneTests
{
    static string Sample(string title = "Tienda", string state = "en-curso") => $$"""
        {
          "claupet": "plan/1",
          "titulo": "{{title}}",
          "inicio": "2026-11-03",
          "etapas": [ { "id": "e1", "nombre": "Diseño", "color": "azul" }, { "id": "e2", "nombre": "Pagos", "color": "morado" } ],
          "tareas": [
            { "id": "t1", "etapa": "e1", "titulo": "Catálogo", "estado": "hecho", "dias": 2, "horas": 3, "horasReales": 2, "por": "Codex" },
            { "id": "t2", "etapa": "e1", "titulo": "Página de producto", "estado": "{{state}}", "dias": 3, "depende": ["t1"], "por": "Claude Code" },
            { "id": "t3", "etapa": "e2", "titulo": "Métricas de ventas", "dias": 5, "depende": ["t2"] },
            { "id": "t4", "etapa": "e2", "titulo": "Pasarela de pago", "estado": "bloqueado", "dias": 4, "depende": ["t3"], "notas": "Faltan las credenciales." }
          ],
          "hitos": [ { "id": "h1", "titulo": "Lanzamiento", "depende": ["t4"] } ]
        }
        """;

    sealed class Scene : IDisposable
    {
        public TempDir Dir { get; } = new();
        public Window Window { get; }
        public ProjectsPane Pane { get; }
        public List<(string Text, string Folder)> Asked { get; } = [];
        public List<(string Text, string Folder)> Worked { get; } = [];

        public Scene(Action<TempDir> files)
        {
            files(Dir);
            Pane = new ProjectsPane(() => [Dir["uno"], Dir["dos"]], _ => false,
                (text, folder) => Asked.Add((text, folder)), (text, folder) => Worked.Add((text, folder)),
                new PlanIndex(Dir["datos/proyectos.json"]), new PlanActivity(Dir["datos/actividad"]), new CanvasStore(Dir["datos/canvas"]));
            Pane.SetInk(Ink.For(true));
            var list = new Border { Width = 264, Child = new StackPanel { Children = { Pane.ListTools(), Pane.List } } };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("264,*"), Children = { list } };
            Grid.SetColumn(Pane, 1);
            grid.Children.Add(Pane);
            Window = new Window { Width = 1360, Height = 860, Content = grid };
            Window.Show();
            Pane.RefreshNow();
            Headless.Frame(Window);
        }

        public void Dispose()
        {
            Window.Close();
            Dir.Dispose();
        }

        public IEnumerable<string> Texts(Visual? under = null) =>
            (under ?? Pane).GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "");

        public IEnumerable<string> ListTexts() => Pane.List.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "");

        public TextBlock Title(string text) => Pane.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == text);

        public Point Center(Visual visual) => visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), Window)!.Value;

        public void Click(Visual visual)
        {
            var at = Center(visual);
            Window.MouseMove(at);
            Window.MouseDown(at, MouseButton.Left);
            Window.MouseUp(at, MouseButton.Left);
            Headless.Frame(Window);
        }
    }

    static Scene Two() => new(dir =>
    {
        dir.Write("uno/planes/tienda.plan.json", Sample());
        dir.Write("dos/notas.plan.json", Sample("Notas", "hecho"));
        dir.Write("uno/node_modules/otro.plan.json", Sample("No se ve"));
    });

    [Fact]
    public void EncuentraLosPlanesDeLasCarpetasYSeSaltaLasDependencias() => Headless.Run(() =>
    {
        using var scene = Two();
        Assert.Equal(["Tienda", "Notas"], scene.Pane.Entries.Select(e => e.Name));
        Assert.Contains("Tienda", scene.ListTexts());
        Assert.DoesNotContain("No se ve", scene.ListTexts());
    });

    [Fact]
    public void CadaVistaSeDibujaYTambienSinTareas() => Headless.Run(() =>
    {
        using var scene = new Scene(dir =>
        {
            dir.Write("uno/tienda.plan.json", Sample());
            dir.Write("dos/vacio.plan.json", """{ "titulo": "Vacío" }""");
        });
        foreach (var (id, _, _) in PlanPanel.Views())
        {
            scene.Pane.ShowView(id);
            Headless.Frame(scene.Window);
        }
        scene.Pane.ShowView("tablero");
        Headless.Frame(scene.Window);
        Assert.Contains("Página de producto", scene.Texts());

        scene.Click(scene.Pane.List.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Vacío"));
        foreach (var (id, _, _) in PlanPanel.Views())
        {
            scene.Pane.ShowView(id);
            Headless.Frame(scene.Window);
        }
        Assert.EndsWith("vacio.plan.json", scene.Pane.SelectedPath);
    });

    [Fact]
    public void BuscarEnLaListaEncuentraPorTareaYSinAcentos() => Headless.Run(() =>
    {
        using var scene = new Scene(dir =>
        {
            dir.Write("uno/tienda.plan.json", Sample());
            dir.Write("dos/otro.plan.json", """{ "titulo": "Otro", "tareas": [ { "id": "a", "titulo": "Nada que ver" } ] }""");
        });
        scene.Pane.SearchList("metricas");
        Headless.Frame(scene.Window);
        var texts = scene.ListTexts().ToList();
        Assert.Contains("Tienda", texts);
        Assert.Contains("Métricas de ventas", texts); // la tarea que coincide, debajo del nombre
        Assert.DoesNotContain("Otro", texts);
    });

    [Fact]
    public void BuscarYFiltrarAtenuaLoQueNoCoincide() => Headless.Run(() =>
    {
        using var scene = Two();
        scene.Pane.ShowView("tablero");
        Headless.Frame(scene.Window);
        double Shade(string title) => scene.Title(title).GetVisualAncestors().OfType<Panel>().First(p => p.Opacity < 1 || p.Parent is StackPanel).Opacity;

        scene.Pane.SearchTasks("metricas");
        Headless.Frame(scene.Window);
        Assert.Equal(1, Shade("Métricas de ventas"));
        Assert.True(Shade("Catálogo") < 1);

        scene.Pane.SearchTasks("");
        scene.Pane.FilterTasks("blocked");
        Headless.Frame(scene.Window);
        Assert.Equal(1, Shade("Pasarela de pago"));
        Assert.True(Shade("Catálogo") < 1);
    });

    [Fact]
    public void UnCanvasNuevoDesdeElPlanQuedaLigadoYSeAbre() => Headless.Run(() =>
    {
        using var scene = Two();
        var opened = new List<string>();
        scene.Pane.CanvasOpened += opened.Add;
        Assert.Empty(scene.Pane.LinkedCanvases);

        scene.Pane.NewLinkedCanvas();

        var store = new CanvasStore(scene.Dir["datos/canvas"]);
        var doc = store.Load(Assert.Single(opened))!;
        Assert.Equal("Tienda", doc.Title);
        Assert.Equal(scene.Pane.SelectedPath, doc.Plan);
        Assert.Equal(doc.Id, Assert.Single(scene.Pane.LinkedCanvases).Id);

        // Solo de ese plan: en otro no aparece.
        scene.Pane.ShowPlan(scene.Dir["dos/notas.plan.json"]);
        Assert.Empty(scene.Pane.LinkedCanvases);
    });

    [Fact]
    public void UnPlanFueraDeLasCarpetasSeSumaAlMostrarlo() => Headless.Run(() =>
    {
        using var scene = Two();
        scene.Dir.Write("otro/aparte.plan.json", Sample("Aparte"));
        scene.Pane.ShowPlan(scene.Dir["otro/aparte.plan.json"]);
        Assert.Equal(scene.Dir["otro/aparte.plan.json"], scene.Pane.SelectedPath);
        Assert.Contains("Aparte", scene.ListTexts());
    });

    [Fact]
    public void EscCierraLaBusquedaDelPlan() => Headless.Run(() =>
    {
        using var scene = Two();
        scene.Pane.SearchTasks("metricas");
        Headless.Frame(scene.Window);
        Assert.True(scene.Pane.FindOpen);

        scene.Pane.HandleKey(new Avalonia.Input.KeyEventArgs { Key = Avalonia.Input.Key.Escape });
        Headless.Frame(scene.Window);
        Assert.False(scene.Pane.FindOpen);
    });

    [Fact]
    public void TocarUnaTarjetaAbreSuDetalleYElEstadoQuedaEnElArchivo() => Headless.Run(() =>
    {
        using var scene = Two();
        scene.Pane.ShowView("tablero");
        Headless.Frame(scene.Window);
        scene.Click(scene.Title("Métricas de ventas"));
        Assert.True(scene.Pane.DrawerOpen);

        scene.Pane.ChangeState("t3", PlanState.Doing);
        Headless.Frame(scene.Window);
        var path = scene.Pane.SelectedPath!;
        Assert.Equal(PlanState.Doing, Plan.Parse(File.ReadAllText(path)).Tasks.First(t => t.Id == "t3").State);
        // Lo que no se tocó sigue igual, incluso quién la cambió antes.
        Assert.Equal("Codex", Plan.Parse(File.ReadAllText(path)).Tasks.First(t => t.Id == "t1").Editor);

        var happened = scene.Pane.Events.First();
        Assert.Equal(("state", "t3", PlanState.Doing), (happened.Kind, happened.Task, happened.State!.Value));
        Assert.Equal(L.T("Usted"), happened.Who);
    });

    [Fact]
    public void ArrastrarUnaTarjetaAOtraColumnaCambiaSuEstado() => Headless.Run(() =>
    {
        using var scene = Two();
        scene.Pane.ShowView("tablero");
        Headless.Frame(scene.Window);
        var from = scene.Center(scene.Title("Métricas de ventas"));
        var to = scene.Center(scene.Title("Hecho"));
        scene.Window.MouseMove(from);
        scene.Window.MouseDown(from, MouseButton.Left);
        scene.Window.MouseMove(new Point(from.X + 30, from.Y));
        scene.Window.MouseMove(to);
        scene.Window.MouseUp(to, MouseButton.Left);
        Headless.Frame(scene.Window);
        Assert.Equal(PlanState.Done, Plan.Parse(File.ReadAllText(scene.Pane.SelectedPath!)).Tasks.First(t => t.Id == "t3").State);
        Assert.False(scene.Pane.DrawerOpen); // arrastrar no es tocar
    });

    [Fact]
    public void LoQueUnaIaEscribeEnElArchivoSeVeYQuedaEnLaActividad() => Headless.Run(() =>
    {
        using var scene = Two();
        var path = scene.Pane.SelectedPath!;
        scene.Pane.ShowView("tablero");
        Headless.Frame(scene.Window);
        File.WriteAllText(path, Sample(state: "revision"));
        File.SetLastWriteTime(path, DateTime.Now.AddSeconds(5));
        scene.Pane.RefreshNow();
        Headless.Frame(scene.Window);
        Assert.Equal(PlanState.Review, scene.Pane.Entries[0].Plan!.Tasks.First(t => t.Id == "t2").State);
        var happened = scene.Pane.Events.First();
        Assert.Equal("t2", happened.Task);
        Assert.Equal("Claude Code", happened.Who);
    });

    [Fact]
    public void LaPrimeraVezNoSeInventaActividad()
    {
        using var dir = new TempDir();
        var plan = Plan.Parse(Sample());
        var activity = new PlanActivity(dir["actividad"]);
        Assert.Empty(activity.Update(dir["a.plan.json"], plan, DateTime.Now));
        Assert.Empty(activity.Update(dir["a.plan.json"], plan, DateTime.Now));
        var moved = Plan.Parse(Sample(state: "hecho"));
        Assert.Single(activity.Update(dir["a.plan.json"], moved, DateTime.Now));
        // Otra instancia (otro día) sigue sabiendo lo que pasó.
        Assert.Single(new PlanActivity(dir["actividad"]).Read(dir["a.plan.json"]));
    }

    [Fact]
    public void ElAvanceCuentaCuandoSeTerminoYNuncaDespuesDeHoy()
    {
        var plan = Plan.Parse(Sample(state: "hecho"));
        var today = new DateOnly(2026, 11, 4);
        var burn = new PlanBurn(plan, today);
        Assert.Equal(14, burn.Total);
        // t1 terminaba el 4 (cuenta); t2 terminaba el 7, después de hoy: cuenta hoy, porque ya está hecha.
        Assert.Equal(5, burn.DoneOn(today));
        Assert.Equal(0, burn.DoneOn(today.AddDays(-2)));
        // Lo que Claupet vio terminar cuenta ese día.
        var seen = new PlanBurn(plan, new DateOnly(2026, 11, 20), [new PlanEvent(new DateTime(2026, 11, 15, 9, 0, 0), null, "state", "t2", "x", PlanState.Done)]);
        Assert.Equal(2, seen.DoneOn(new DateOnly(2026, 11, 14)));
        Assert.Equal(5, seen.DoneOn(new DateOnly(2026, 11, 15)));
    }

    [Fact]
    public void PedirYTrabajarVanALaCarpetaDelPlan() => Headless.Run(() =>
    {
        using var scene = Two();
        scene.Pane.ShowView("tablero");
        Headless.Frame(scene.Window);
        scene.Click(scene.Title("Página de producto"));
        scene.Click(scene.Title(L.T("Trabajar en esto")));
        var (text, folder) = Assert.Single(scene.Worked);
        Assert.Contains("Página de producto", text);
        Assert.Contains("tienda.plan.json", text);
        Assert.Equal(scene.Dir["uno"], folder);
    });

    [Fact]
    public void TrabajarEnEstoLaPoneEnCursoYMandaElPedidoDelPlan() => Headless.Run(() =>
    {
        using var scene = new Scene(dir => dir.Write("uno/planes/tienda.plan.json", Sample(state: "por-hacer")));
        scene.Pane.ShowView("tablero");
        Headless.Frame(scene.Window);
        scene.Click(scene.Title("Página de producto"));
        scene.Click(scene.Title(L.T("Trabajar en esto")));

        var (text, _) = Assert.Single(scene.Worked);
        Assert.Contains("«Tienda»", text);
        Assert.Contains("«Catálogo»", text); // lo que ya está hecho y necesita
        Assert.Contains("plan_update", text);
        var path = Path.Combine(scene.Dir["uno"], "planes", "tienda.plan.json");
        Assert.Equal(PlanState.Doing, Plan.Parse(File.ReadAllText(path)).Tasks.First(t => t.Id == "t2").State);
    });

    [Fact]
    public void ElPedidoDeUnaEtapaTraeSusTareasYLoQueYaEstaHecho()
    {
        var plan = Plan.Parse(Sample(state: "por-hacer"));
        var text = PlanWork.Prompt(plan, @"C:\uno\tienda.plan.json", plan.Tasks.Where(t => t.Stage == "e2").ToList());
        Assert.Contains("Pagos", text);
        Assert.Contains("(id t3)", text);
        Assert.Contains("(id t4)", text);
        Assert.Contains("Faltan las credenciales.", text);
        Assert.Contains(@"C:\uno\tienda.plan.json", text);
        // t3 depende de t2, que no está hecha: no se da como punto de partida; t4 depende de t3, que va en el mismo pedido.
        Assert.DoesNotContain("«Página de producto»", text);
        Assert.Contains("«en-curso»", text); // con varias, la sesión marca cada una al empezarla
    }

    [Fact]
    public void UnaTareaQueEsperaOtraNoSePuedeMandarATrabajar() => Headless.Run(() =>
    {
        using var scene = Two();
        scene.Pane.ShowView("tablero");
        Headless.Frame(scene.Window);
        scene.Click(scene.Title("Pasarela de pago"));
        scene.Click(scene.Title(L.T("Trabajar en esto")));
        Assert.Empty(scene.Worked);
    });

    [Fact]
    public void CtrlKVaAUnaTareaDeOtroPlan() => Headless.Run(() =>
    {
        using var scene = new Scene(dir =>
        {
            dir.Write("uno/tienda.plan.json", Sample());
            dir.Write("dos/otro.plan.json", """{ "titulo": "Otro", "tareas": [ { "id": "a", "titulo": "Lo que busco" } ] }""");
        });
        Assert.EndsWith("tienda.plan.json", scene.Pane.SelectedPath);
        scene.Pane.Palette();
        Headless.Frame(scene.Window);
        scene.Pane.PaletteQuery!.Text = "busco";
        Headless.Frame(scene.Window);
        scene.Click(scene.Pane.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Lo que busco"));
        Assert.EndsWith("otro.plan.json", scene.Pane.SelectedPath);
        Assert.True(scene.Pane.DrawerOpen);
    });

    static int PdfPages(string file) => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file, System.Text.Encoding.Latin1), @"/Type\s*/Page\b").Count;

    [Fact]
    public void ElInformeEsUnPdfVectorialEnHojasCarta() => Headless.Run(() =>
    {
        using var scene = Two();
        var one = scene.Dir["uno.pdf"];
        var all = scene.Dir["todo.pdf"];
        scene.Pane.Export(one, ["cronograma"]).GetAwaiter().GetResult();
        scene.Pane.Export(all, PlanPanel.Views().Select(v => v.Id).ToList()).GetAwaiter().GetResult();

        var raw = File.ReadAllText(one, System.Text.Encoding.Latin1);
        Assert.StartsWith("%PDF-", raw);
        Assert.Equal(1, PdfPages(one));
        Assert.True(PdfPages(all) >= 2);
        Assert.False(File.Exists(one + ".tmp"));
        Assert.Matches(@"/MediaBox\s*\[\s*0\s+0\s+612\s+792", raw);

        // Es vectorial: la letra va incrustada, no una imagen de la página.
        Assert.Contains("/Font", raw);
        Assert.DoesNotContain("/Subtype /Image", raw);

        var shot = Environment.GetEnvironmentVariable("CLAUPET_PROYECTOS_PDF");
        if (shot is not null)
        {
            File.Copy(one, shot + ".cronograma.pdf", true);
            File.Copy(all, shot + ".todo.pdf", true);
        }
    });

    /// <summary>Una hoja del informe, dibujada de verdad, en una imagen del tamaño del PDF (un píxel por punto).</summary>
    static SKBitmap Drawn(Control page, Avalonia.Size size)
    {
        var bitmap = new SKBitmap((int)Math.Round(size.Width * 0.75), (int)Math.Round(size.Height * 0.75));
        using var canvas = new SKCanvas(bitmap);
        PlanPdf.DrawAsync(canvas, PlanPdf.Lay(page, size), size).GetAwaiter().GetResult();
        return bitmap;
    }

    static SKBitmap DrawnSheet(string title, Plan plan, string view)
    {
        var sheets = PlanReport.Build(title, plan, [], [view]);
        return Drawn(PlanReport.Compose(sheets[0], PlanReport.Paper(), title, 1, sheets.Count), new Avalonia.Size(PlanReport.PortraitWidth, PlanReport.PortraitHeight));
    }

    static IEnumerable<string> SheetTexts(ReportSheet sheet) =>
        sheet.Blocks.SelectMany(b => b.GetLogicalDescendants().Prepend(b)).OfType<TextBlock>().Select(t => t.Text ?? "");

    [Fact]
    public void LaHojaDelPdfSaleEnteraYASuEscala() => Headless.Run(() =>
    {
        // Una regresión: la escala se perdía al dibujar y solo se veía un pedazo de la página.
        var size = new Avalonia.Size(816, 1056);
        using var whole = Drawn(new Border { Background = Brushes.Red }, size);
        Assert.Equal((612, 792), (whole.Width, whole.Height));
        Assert.Equal(SKColors.Red, whole.GetPixel(611, 791));

        using var left = Drawn(new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Children = { new Border { Background = Brushes.Red } } }, size);
        Assert.Equal(SKColors.Red, left.GetPixel(300, 300));
        Assert.NotEqual(SKColors.Red, left.GetPixel(312, 300));
    });

    [Fact]
    public void ElInformeEsBlancoYSinLogoAunqueElTemaSeaOscuro() => Headless.Run(() =>
    {
        // Sea cual sea el tema de la app, el papel no cambia: blanco, sin marca.
        var plan = Plan.Parse(Sample());
        foreach (var (id, _, _) in PlanPanel.Views())
        {
            using var image = DrawnSheet("Tienda", plan, id);
            Assert.Equal(SKColors.White, image.GetPixel(2, 2));
            Assert.Equal(SKColors.White, image.GetPixel(image.Width - 3, image.Height - 3));
            var sheets = PlanReport.Build("Tienda", plan, [], [id]);
            Assert.DoesNotContain(sheets.SelectMany(s => s.Blocks).SelectMany(b => b.GetLogicalDescendants().Prepend(b)), c => c is AppLogo);
        }
    });

    [Fact]
    public void LasBarrasDeAvanceSeLlenanEnElInforme() => Headless.Run(() =>
    {
        // Una regresión: se llenaban al cambiar de tamaño en pantalla, y en el PDF salían vacías.
        var plan = Plan.Parse("""
            { "titulo": "Barras", "etapas": [ { "id": "e", "nombre": "Etapa", "color": "#1234CD" } ],
              "tareas": [ { "id": "a", "titulo": "Una", "etapa": "e", "estado": "hecho", "dias": 1 }, { "id": "b", "titulo": "Otra", "etapa": "e", "dias": 1 } ] }
            """);
        using var image = DrawnSheet("Barras", plan, "resumen");
        var tint = new SKColor(0x12, 0x34, 0xCD);
        var painted = image.Pixels.Count(p => p == tint);
        Assert.True(painted > 60, $"{painted} píxeles del color de la etapa");
    });

    [Fact]
    public void UnPlanLargoSigueEnOtrasHojasSinCortarNiPerderNada() => Headless.Run(() =>
    {
        var tasks = string.Join(",", Enumerable.Range(1, 120).Select(i => $$"""{ "id": "t{{i}}", "titulo": "Tarea {{i}}", "dias": 6, "notas": "Una nota larga para que la fila ocupe más de una línea en la tabla del informe y haya que repartirla bien."{{(i > 1 ? $", \"depende\": [\"t{i - 1}\"]" : "")}} }"""));
        var plan = Plan.Parse($$"""{ "titulo": "Largo", "inicio": "2026-01-05", "tareas": [{{tasks}}] }""");

        foreach (var view in new[] { "cronograma", "tablero" })
        {
            var sheets = PlanReport.Build("Largo", plan, [], [view]);
            Assert.True(sheets.Count > 1, view);
            // Nada se pasa de la hoja y ninguna hoja sale vacía.
            foreach (var sheet in sheets)
            {
                Assert.True(sheet.Used <= PlanReport.PortraitHeight - PlanReport.Top - PlanReport.Bottom, $"{view}: {sheet.Used}");
                Assert.NotEmpty(sheet.Blocks);
            }
            var texts = sheets.SelectMany(SheetTexts).ToList();
            foreach (var i in Enumerable.Range(1, 120)) Assert.Contains($"Tarea {i}", texts);
            // El encabezado se repite en cada hoja.
            Assert.Equal(sheets.Count, texts.Count(t => t == L.T("Tarea")));
        }

        var file = Path.Combine(Path.GetTempPath(), $"claupet-largo-{Guid.NewGuid():N}.pdf");
        try
        {
            PlanPdf.SaveAsync(file, new PlanEntry(file, "", plan, null, DateTime.Now), [], ["cronograma"]).GetAwaiter().GetResult();
            var raw = File.ReadAllText(file, System.Text.Encoding.Latin1);
            Assert.True(PdfPages(file) > 1);
            Assert.DoesNotMatch(@"/MediaBox\s*\[\s*0\s+0\s+(?!612\s+792)", raw);
        }
        finally { File.Delete(file); }
    });

    [Fact]
    public void LasDependenciasSeDibujanYSiNoSeLeeriaVanEnTabla() => Headless.Run(() =>
    {
        var small = PlanReport.Build("Tienda", Plan.Parse(Sample()), [], ["diagrama"]);
        Assert.Contains(small.SelectMany(s => s.Blocks), b => b is Canvas);

        // Treinta tareas una tras otra no caben legibles en una hoja: la cadena se cuenta como tabla, entera.
        var tasks = string.Join(",", Enumerable.Range(1, 30).Select(i => $$"""{ "id": "t{{i}}", "titulo": "Tarea {{i}}", "dias": 1{{(i > 1 ? $", \"depende\": [\"t{i - 1}\"]" : "")}} }"""));
        var big = PlanReport.Build("Largo", Plan.Parse($$"""{ "titulo": "Largo", "inicio": "2026-01-05", "tareas": [{{tasks}}] }"""), [], ["diagrama"]);
        Assert.DoesNotContain(big.SelectMany(s => s.Blocks), b => b is Canvas);
        var texts = big.SelectMany(SheetTexts).ToList();
        foreach (var i in Enumerable.Range(1, 30)) Assert.Contains($"Tarea {i}", texts);

        // Sin dependencias no hay nada que dibujar.
        var none = PlanReport.Build("Suelto", Plan.Parse("""{ "titulo": "Suelto", "tareas": [ { "id": "a", "titulo": "Una" } ] }"""), [], ["diagrama"]);
        Assert.Contains(L.T("Sin dependencias"), none.SelectMany(SheetTexts));
    });

    [Fact]
    public void LasVistasNoDependenDeLoQueNoExiste() => Headless.Run(() =>
    {
        // Sin horas ni actividad: nada se rompe y lo que no hay no se inventa.
        using var scene = new Scene(dir => dir.Write("uno/simple.plan.json", """{ "titulo": "Simple", "tareas": [ { "id": "a", "titulo": "Una" } ] }"""));
        scene.Pane.ShowView("metricas");
        Headless.Frame(scene.Window);
        Assert.DoesNotContain(L.T("Horas por tarea"), scene.Texts());
        scene.Pane.ShowView("actividad");
        Headless.Frame(scene.Window);
        Assert.Contains(L.T("Sin actividad todavía"), scene.Texts());
    });

    [Fact]
    public void CapturaParaRevisarElDiseno() => Headless.Run(() =>
    {
        var shot = Environment.GetEnvironmentVariable("CLAUPET_PROYECTOS_SHOT");
        if (shot is null) return;
        var plan = File.ReadAllText(Environment.GetEnvironmentVariable("CLAUPET_PROYECTOS_PLAN")!);
        using var scene = new Scene(dir =>
        {
            dir.Write("uno/claupet.plan.json", plan);
            dir.Write("dos/tienda.plan.json", Sample());
        });
        var dark = true;
        foreach (var round in new[] { "oscuro", "claro" })
        {
            scene.Pane.SetInk(Ink.For(dark));
            scene.Window.Background = Ink.For(dark).Window;
            ((Border)((Grid)scene.Window.Content!).Children[0]).Background = Ink.For(dark).Window;
            foreach (var (id, _, _) in PlanPanel.Views())
            {
                if (!dark && id is not ("resumen" or "cronograma")) continue;
                scene.Pane.ShowView(id);
                for (var i = 0; i < 8; i++)
                {
                    Thread.Sleep(60);
                    Headless.Frame(scene.Window);
                }
                scene.Window.CaptureRenderedFrame()?.Save($"{shot}.{id}.{round}.png");
            }
            if (round == "oscuro")
            {
                scene.Pane.Export($"{shot}.informe.pdf", PlanPanel.Views().Select(v => v.Id).ToList()).GetAwaiter().GetResult();
                scene.Pane.Export($"{shot}.informe.cronograma.pdf", ["cronograma"]).GetAwaiter().GetResult();
            }
            dark = false;
        }
    });
}
