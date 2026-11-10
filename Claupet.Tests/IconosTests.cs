using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Border = Avalonia.Controls.Border;
using Control = Avalonia.Controls.Control;
using ScrollViewer = Avalonia.Controls.ScrollViewer;
using StackPanel = Avalonia.Controls.StackPanel;
using TextBlock = Avalonia.Controls.TextBlock;
using Viewbox = Avalonia.Controls.Viewbox;

namespace Claupet.Tests;

/// <summary>Las acciones se encuentran donde se las busca, con ícono y su descripción al pasar el puntero.</summary>
public sealed class IconosTests
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    static StackPanel History(TerminalPane pane, TerminalPane.Tab tab, string file)
    {
        IReadOnlyList<PastSession> sessions = [new("otra", "Una sesión anterior", DateTime.Now, file)];
        typeof(TerminalPane).GetMethod("ShowHistory", Private)!.Invoke(pane, [tab, sessions]);
        return (StackPanel)typeof(TerminalPane).GetField("_sideBody", Private)!.GetValue(pane)!;
    }

    [Fact]
    public void BorrarTodoElHistorialSePideDesdeElTituloYPreguntaArriba() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var pane = new TerminalPane();
        pane.SetInk(Ink.For(true));
        var tab = new TerminalPane.Tab(new TerminalAgent(new AgentOptions(dir.Path), _ => { }), "cuenta");

        var body = History(pane, tab, dir["otra.jsonl"]);
        Assert.DoesNotContain(body.GetLogicalDescendants().OfType<Control>(), c => AutomationProperties.GetName(c) == L.T("Borrar todo el historial"));

        typeof(TerminalPane).GetField("_confirmClearHistory", Private)!.SetValue(pane, true);
        body = History(pane, tab, dir["otra.jsonl"]);
        var question = L.T("¿Borrar todo el historial de esta carpeta? No se puede deshacer.");
        // Arriba va el buscador; la pregunta, primera debajo de él.
        Assert.Single(body.Children[0].GetLogicalDescendants().OfType<Avalonia.Controls.TextBox>());
        var rows = Assert.IsType<StackPanel>(body.Children[1]);
        Assert.Contains(rows.Children[0].GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == question);

        var title = (Border)typeof(TerminalPane).GetMethod("HistoryTitle", Private)!.Invoke(pane, [tab])!;
        Assert.Equal(L.T("Opciones del historial"), AutomationProperties.GetName(title));
        tab.Agent.Dispose();
    });

    static Control Head(TerminalPane pane, string folder) =>
        (Control)typeof(TerminalPane).GetMethod("ProjectHead", Private)!.Invoke(pane, [folder, Array.Empty<TerminalPane.Tab>(), false])!;

    [Fact]
    public void UnProyectoSinPestanasTieneElMasEnSuFila() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var pane = new TerminalPane();
        pane.SetInk(Ink.For(true));
        var add = Head(pane, dir.Path).GetLogicalDescendants().OfType<Border>().First(b => AutomationProperties.GetName(b) == L.T("Nueva pestaña aquí"));
        Assert.IsType<Viewbox>(add.Child);
        Assert.Equal(1, add.Opacity);
    });

    [Fact]
    public void TocarLaRamaAbreUnaPestanaYDiceQueEs() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir[".git"]);
        File.WriteAllText(Path.Combine(dir[".git"], "HEAD"), "ref: refs/heads/mi-rama\n");
        var pane = new TerminalPane();
        pane.SetInk(Ink.For(true));
        var head = Assert.IsType<StackPanel>(Head(pane, dir.Path));
        var branch = Assert.IsType<Border>(head.Children[1]);
        Assert.Equal(L.T("Nueva pestaña aquí"), AutomationProperties.GetName(branch));
        Assert.Contains("git", (string)Avalonia.Controls.ToolTip.GetTip(branch)!);
    });

    [Fact]
    public void LaPantallaDeInicioDelEditorUsaIconos() => Headless.Run(() =>
    {
        var editor = new EditorPane();
        editor.SetInk(Ink.For(true));
        var start = (ScrollViewer)typeof(EditorPane).GetMethod("Start", Private)!.Invoke(editor, [])!;
        var buttons = ((StackPanel)start.Content!).Children.OfType<StackPanel>().First().Children.Cast<Border>().ToList();

        Assert.Equal([L.T("Texto nuevo"), L.T("Abrir…")], buttons.Select(AutomationProperties.GetName));
        Assert.All(buttons, b => Assert.IsType<Viewbox>(b.Child));
        Assert.All(buttons, b => Assert.Equal(AutomationProperties.GetName(b), Avalonia.Controls.ToolTip.GetTip(b)));
    });
}
