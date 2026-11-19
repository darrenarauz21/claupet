using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
// El proyecto de pruebas también trae WinForms (nombres iguales a los de Avalonia).
using Border = Avalonia.Controls.Border;
using Control = Avalonia.Controls.Control;
using KeyEventArgs = Avalonia.Input.KeyEventArgs;
using Panel = Avalonia.Controls.Panel;
using Point = Avalonia.Point;
using TextBox = Avalonia.Controls.TextBox;
using Window = Avalonia.Controls.Window;

namespace Claupet.Tests;

/// <summary>
/// El menú del modelo y el esfuerzo de una pestaña de Claude Code u otro agente (fuera de la pantalla): con las decenas
/// de modelos que anuncia OpenCode, la tarjeta pasaba del borde de la ventana y tapaba la caja de abajo.
/// </summary>
public sealed class TerminalModelMenuTests
{
    const string Account = "cuenta-de-prueba";

    [Fact]
    public void LosModelosSeAgrupanPorProveedorEnElOrdenEnQueLlegan()
    {
        var groups = TerminalPane.ModelGroups(["", "opencode/gpt-5.5", "anthropic/claude-opus-5-5", "opencode/claude-fable-5-2", "sonnet", "raro/"]);

        Assert.Equal([null, "opencode", "anthropic"], groups.Select(g => g.Provider));
        Assert.Equal(["", "sonnet", "raro/"], groups[0].Models);
        Assert.Equal(["opencode/gpt-5.5", "opencode/claude-fable-5-2"], groups[1].Models);
        Assert.Equal(["anthropic/claude-opus-5-5"], groups[2].Models);
    }

    [Fact]
    public void ConMuchosModelosElMenuCabeEnLaVentanaSeDesplazaYLlevaBuscador() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var (pane, window) = Open(1400, 700);
        string[] named = ["opencode/claude-fable-5-2", "opencode/claude-opus-5-5", "opencode/gpt-5.5", "opencode/gpt-5.5-pro", "anthropic/claude-sonnet-5-5"];
        var models = named.Concat(Enumerable.Range(1, 65).Select(i => $"opencode/modelo-{i:00}")).ToList();
        var agent = new TerminalAgent(new AgentOptions(dir.Path, Kind: AgentKinds.OpenCode), a => a());
        agent.Driver.Apply(Options(models, current: "opencode/gpt-5.5"), []);
        Assert.Equal(70, agent.Driver.Models.Count);
        pane.Add(new TerminalPane.Tab(agent, Account));
        Headless.Frame(window);

        var anchor = Chip(Find(window, "opencode/gpt-5.5") ?? throw new Xunit.Sdk.XunitException("No se ve el modelo de la pestaña"));
        Click(window, anchor);
        Dispatcher.UIThread.RunJobs();
        Headless.Frame(window);

        var search = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Watermark == L.T("Buscar modelo"));
        var card = search.GetVisualAncestors().OfType<Border>().First(b => b.Child is DockPanel);
        var cardRect = In(card, window);
        var anchorRect = In(anchor, window);
        // Entre el borde de arriba de la ventana y la etiqueta del modelo, sin taparla.
        Assert.True(cardRect.Top >= 0, $"La tarjeta empieza en {cardRect.Top}");
        Assert.True(cardRect.Bottom <= anchorRect.Top + 0.5, $"La tarjeta termina en {cardRect.Bottom} y la etiqueta empieza en {anchorRect.Top}");

        // Los modelos se desplazan; el esfuerzo queda a la vista dentro de la tarjeta.
        // (el buscador trae el suyo, para el texto)
        var scroll = card.GetVisualDescendants().OfType<ScrollViewer>().Single(s => !s.GetVisualAncestors().OfType<TextBox>().Any());
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 1, "Con 70 modelos la lista tiene que desplazarse");
        var effort = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == L.T("Esfuerzo"));
        Assert.True(cardRect.Contains(In(effort, window)), "El esfuerzo tiene que verse dentro de la tarjeta");

        // El proveedor va de título y no se repite en cada botón; los de Claude, con su nombre.
        var texts = card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("opencode", texts);
        Assert.Contains("anthropic", texts);
        Assert.Contains("Fable 5.2", texts);
        Assert.Contains("Sonnet 5.5", texts);
        Assert.DoesNotContain("opencode/gpt-5.5", texts);

        // El buscador deja los que tienen todas las palabras y esconde los grupos vacíos.
        search.Text = "gpt 5.5";
        Dispatcher.UIThread.RunJobs();
        Headless.Frame(window);
        Assert.Equal(["gpt-5.5", "gpt-5.5-pro"], VisibleChips(scroll));
        Assert.False(card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "anthropic").IsEffectivelyVisible);

        // Intro elige el primero que quedó.
        search.Text = "pro";
        Dispatcher.UIThread.RunJobs();
        search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["gpt-5.5-pro"], VisibleChips(scroll));
        Assert.True(Selected(ChipOf(scroll, "gpt-5.5-pro")));
        Assert.False(Selected(ChipOf(scroll, "gpt-5.5")));

        search.Text = "nada-de-esto";
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(VisibleChips(scroll));
        Assert.True(card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == L.T("Ningún modelo coincide")).IsEffectivelyVisible);
        Close(pane, window);
    });

    [Fact]
    public void AntesDeQueClaudeCodeRespondaElMenuLlevaLosAliasSinLasVariantesDe1M() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var (pane, window) = Open(1400, 900);
        pane.Add(new TerminalPane.Tab(new TerminalAgent(new AgentOptions(dir.Path), a => a()), Account));
        Headless.Frame(window);

        Click(window, Chip(Find(window, L.T("modelo por defecto")) ?? throw new Xunit.Sdk.XunitException("No se ve el modelo de la pestaña")));
        Dispatcher.UIThread.RunJobs();
        Headless.Frame(window);

        Assert.NotNull(Find(window, "Opus 5.5"));
        Assert.NotNull(Find(window, "Fable 5.1"));
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("(1M)") == true);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(), t => t.Watermark == L.T("Buscar modelo"));
        Close(pane, window);
    });

    [Fact]
    public void ConLoQueOfreceClaudeCodeElMenuMuestraFable52YMarcaElAliasQueRespondio() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var (pane, window) = Open(1400, 900);
        var agent = new TerminalAgent(new AgentOptions(dir.Path, Model: "fable"), a => a());
        agent.Transcript.Apply("""{"type":"system","subtype":"init","session_id":"s-1","model":"claude-fable-5-1"}""");
        agent.Transcript.Apply(ClaudeModelsTests.InitializeResponse(ClaudeModelsOffered));
        pane.Add(new TerminalPane.Tab(agent, Account));
        Headless.Frame(window);

        Click(window, Chip(Find(window, "Fable 5.1") ?? throw new Xunit.Sdk.XunitException("No se ve el modelo de la pestaña")));
        Dispatcher.UIThread.RunJobs();
        Headless.Frame(window);

        var menu = window.GetVisualDescendants().OfType<DockPanel>().Single(d => d.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == L.T("Modelo")));
        var scroll = menu.GetVisualDescendants().OfType<ScrollViewer>().Single();
        Assert.Equal(["Haiku 5.5", "Sonnet 5.5", "Opus 5.5", "Fable 5.2", "Fable 5.1", "Opus 5.5 + Sonnet 5.5 (plan)"], VisibleChips(scroll).Skip(1));
        // Respondió la 5.1 y se abrió con «fable»: ese es el elegido, no la 5.2.
        Assert.True(Selected(ChipOf(scroll, "Fable 5.1")));
        Assert.False(Selected(ChipOf(scroll, "Fable 5.2")));
        Close(pane, window);
    });

    /// <summary>Lo que ofreció Claude Code (lo de ClaudeModelsTests, sin los modelos anteriores).</summary>
    const string ClaudeModelsOffered = """
        [
          {"value":"default","resolvedModel":"claude-sonnet-5-5","displayName":"Default (recommended)"},
          {"value":"opus","resolvedModel":"claude-opus-5-5","displayName":"Opus 5.5"},
          {"value":"claude-fable-5-2","resolvedModel":"claude-fable-5-2","displayName":"Fable 5.2"},
          {"value":"sonnet","resolvedModel":"claude-sonnet-5-5","displayName":"Sonnet 5.5"},
          {"value":"haiku","resolvedModel":"claude-haiku-5-5","displayName":"Haiku 5.5"},
          {"value":"fable","resolvedModel":"claude-fable-5-1","displayName":"Fable 5.1"}
        ]
        """;

    // ── Andamio ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Lo que anuncia un agente por ACP (config_option_update): sus modelos y sus esfuerzos.</summary>
    static string Options(IEnumerable<string> models, string current) =>
        "{\"jsonrpc\":\"2.0\",\"method\":\"session/update\",\"params\":{\"sessionId\":\"s-1\",\"update\":{\"sessionUpdate\":\"config_option_update\",\"configOptions\":["
        + "{\"type\":\"select\",\"id\":\"model\",\"name\":\"Model\",\"category\":\"model\",\"currentValue\":\"" + current + "\",\"options\":["
        + string.Join(",", models.Select(m => "{\"value\":\"" + m + "\",\"name\":\"" + m + "\"}")) + "]},"
        + "{\"type\":\"select\",\"id\":\"effort\",\"name\":\"Effort\",\"category\":\"thought_level\",\"currentValue\":\"medium\",\"options\":["
        + "{\"value\":\"low\",\"name\":\"Low\"},{\"value\":\"medium\",\"name\":\"Medium\"},{\"value\":\"high\",\"name\":\"High\"}]}]}}}";

    static (TerminalPane Pane, Window Window) Open(double width, double height)
    {
        var pane = new TerminalPane();
        pane.SetInk(Ink.For(true));
        // Como en la Console: la tarjeta flotante va en el panel de la ventana.
        var window = new Window { Width = width, Height = height, Content = new Panel { Children = { pane } } };
        window.Show();
        Headless.Frame(window);
        return (pane, window);
    }

    static void Close(TerminalPane pane, Window window)
    {
        pane.CloseAll();
        window.Close();
    }

    static TextBlock? Find(Window window, string text) => window.GetVisualDescendants().OfType<TextBlock>()
        .FirstOrDefault(t => t.Text == text && t.IsEffectivelyVisible);

    /// <summary>La etiqueta (o el botón) que lleva ese texto.</summary>
    static Border Chip(TextBlock text) => text.GetVisualAncestors().OfType<Border>().First();

    /// <summary>Los botones de modelo que se ven (el texto de cada uno), en orden.</summary>
    static List<string?> VisibleChips(ScrollViewer scroll) => [.. scroll.GetVisualDescendants().OfType<WrapPanel>()
        .Where(w => w.IsEffectivelyVisible)
        .SelectMany(w => w.Children.Where(c => c.IsVisible))
        .Select(c => c.GetVisualDescendants().OfType<TextBlock>().Last().Text)];

    static Border ChipOf(ScrollViewer scroll, string text) => scroll.GetVisualDescendants().OfType<WrapPanel>()
        .SelectMany(w => w.Children).OfType<Border>()
        .Single(c => c.GetVisualDescendants().OfType<TextBlock>().Last().Text == text);

    /// <summary>El botón elegido tiene fondo (el acento suave); los demás, transparente.</summary>
    static bool Selected(Border chip) => chip.Background is ISolidColorBrush { Color.A: > 0, Opacity: > 0 };

    static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Headless.Frame(window);
    }

    /// <summary>Dónde está un control dentro de otro.</summary>
    static Rect In(Visual visual, Visual root) => new(visual.TranslatePoint(default, root) ?? default, visual.Bounds.Size);
}
