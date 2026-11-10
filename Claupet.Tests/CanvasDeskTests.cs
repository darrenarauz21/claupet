using System.Text.Json;
using Avalonia.Headless;
using System.Text.RegularExpressions;

namespace Claupet.Tests;

/// <summary>La mesa de diseño de Canvas: qué pantallas lleva, su página y el pedido que va a la sesión ligada.</summary>
public class CanvasDeskTests
{
    static CanvasOp Insert(string kind, string content, string? title = null) => new("insert", Kind: kind, Content: content, Title: title);

    static CanvasDoc Doc(TempDir dir) => new CanvasStore(dir.Path).Create("Landing", "Claude Code",
    [
        Insert("markdown", "# Notas"),
        Insert("html", "<div style=\"font:16px system-ui;padding:40px 48px\"><h1 style=\"font-size:52px;margin:60px 0 12px\">Inicio</h1><p>Todas tus IA en una sola mesa.</p><a style=\"display:inline-block;background:#111;color:#fff;padding:12px 20px;border-radius:10px\">Descargar</a></div><script>var x='</script>';</script>", "Inicio"),
        Insert("svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"),
        Insert("html", ""),
    ]);

    [Fact]
    public void Solo_las_secciones_html_y_svg_con_contenido_son_pantallas()
    {
        using var dir = new TempDir();
        var doc = Doc(dir);
        Assert.True(CanvasDesk.HasScreens(doc));
        Assert.Equal(["s2", "s3"], CanvasDesk.Screens(doc).Select(s => s.Id));
        Assert.Equal("Inicio", CanvasDesk.Name(doc, doc.Section("s2")!));
        Assert.Equal(L.T("Pantalla {0}", 2), CanvasDesk.Name(doc, doc.Section("s3")!));
    }

    [Fact]
    public void La_pagina_lleva_las_pantallas_aisladas_y_un_script_que_no_se_corta()
    {
        using var dir = new TempDir();
        var doc = Doc(dir);
        var theme = new CanvasDesk.Theme("#111", "#222", "#333", "#444", "#555", "#eee", "#aaa", "#777", "#d97757", "rgba(255,255,255,.16)", true);
        var page = CanvasDesk.Page(doc, theme, "s3");

        // Los datos van en su <script> y se leen enteros: el «</script>» de una pantalla no corta la página.
        var data = Regex.Match(page, "<script type=\"application/json\" id=\"data\">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        using var json = JsonDocument.Parse(data);
        var boards = json.RootElement.GetProperty("boards");
        Assert.Equal(2, boards.GetArrayLength());
        Assert.Equal("s3", json.RootElement.GetProperty("sel").GetString());
        Assert.Contains("Todas tus IA en una sola mesa.", boards[0].GetProperty("html").GetString());
        Assert.Contains("claupetPicked", boards[0].GetProperty("html").GetString());
        Assert.DoesNotContain("claupetPicked", boards[0].GetProperty("thumb").GetString());
        Assert.Equal("allow-scripts allow-forms allow-modals allow-popups", json.RootElement.GetProperty("sandbox").GetString());
        Assert.Contains("--acc:#d97757", page);

        // Para mirarla en un navegador: CLAUPET_DESK_PAGE=ruta.html
        if (Environment.GetEnvironmentVariable("CLAUPET_DESK_PAGE") is { Length: > 0 } file) File.WriteAllText(file, page);

        using var update = JsonDocument.Parse(CanvasDesk.Update(doc));
        Assert.Equal("boards", update.RootElement.GetProperty("t").GetString());
        Assert.Equal(2, update.RootElement.GetProperty("boards").GetArrayLength());
    }

    [Fact]
    public void El_pedido_dice_donde_cambiar_y_en_el_chat_se_ve_solo_lo_escrito()
    {
        using var dir = new TempDir();
        var doc = Doc(dir);
        var screen = doc.Section("s2")!;

        var prompt = CanvasDesk.Prompt(doc, screen, "h1 Inicio", "div.hero > h1", "Más grande\ny en naranja");
        Assert.StartsWith($"[Claupet Canvas] Canvas «Landing» (id {doc.Id}) · pantalla «Inicio» (sección s2) · elemento «h1 Inicio» (div.hero > h1).", prompt);
        Assert.Contains("canvas_edit", prompt);
        var (name, element, text) = CanvasDesk.Parse(prompt);
        Assert.Equal("Inicio", name);
        Assert.Equal("h1 Inicio", element);
        Assert.Equal("Más grande\ny en naranja", text);

        var (onlyScreen, none, plain) = CanvasDesk.Parse(CanvasDesk.Prompt(doc, screen, null, null, "  Modo oscuro "));
        Assert.Equal("Inicio", onlyScreen);
        Assert.Null(none);
        Assert.Equal("Modo oscuro", plain);

        // Lo que no se pidió desde la mesa se ve tal cual.
        Assert.Equal((null, null, "hola"), CanvasDesk.Parse("hola"));
    }

    [Fact]
    public void La_mesa_tapa_la_lista_y_el_chat_muestra_lo_pedido_a_la_sesion() => Headless.Run(() =>
    {
        if (!CanvasLivePage.Available) return; // la mesa vive en WebView2 (Windows)
        using var dir = new TempDir();
        var store = new CanvasStore(dir.Path);
        var doc = store.Create("Landing", "Claude Code", [Insert("markdown", "Notas"), Insert("html", "<h1>Hola</h1>", "Inicio")]);
        var agent = new TerminalAgent(new AgentOptions(dir.Path), _ => { });
        agent.Transcript.Ask(CanvasDesk.Prompt(doc, doc.Section("s2"), "h1 Hola", "h1", "Más grande"));
        var session = new CanvasSession(agent, "Claude Code", dir.Path, "mesa de diseño");
        var revealed = 0;
        var sessions = new CanvasSessions(() => [session], _ => null, _ => revealed++);
        var terminal = new TerminalPane();
        terminal.SetInk(Ink.For(true));
        var pane = new CanvasPane((text, folder) => terminal.MarkdownPage(text, folder), store, sessions: sessions);
        pane.SetInk(Ink.For(true));
        bool? mesa = null;
        pane.MesaChanged += on => mesa = on;
        var window = new Avalonia.Controls.Window { Width = 1400, Height = 820, Content = pane };
        window.Show();
        Headless.Frame(window);

        pane.EnterMesa();
        Headless.Frame(window);
        Assert.True(mesa);
        var texts = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Más grande", texts);
        Assert.Contains("h1 Hola", texts);
        Assert.DoesNotContain(texts, t => t?.StartsWith("[Claupet Canvas]") == true);
        if (Environment.GetEnvironmentVariable("CLAUPET_DESK_SHOT") is { Length: > 0 } shot) window.CaptureRenderedFrame()?.Save(shot);

        pane.LeaveMesa();
        Headless.Frame(window);
        Assert.False(mesa);
        pane.Close();
        window.Close();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    });
}
