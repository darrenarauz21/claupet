using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;

namespace Claupet.Tests;

/// <summary>Las acciones que se entienden con un ícono no ocupan lugar con texto: la descripción sale al pasar el puntero.</summary>
public sealed class IconosTests
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public void BorrarTodoElHistorialEsUnIconoConSuDescripcion() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var pane = new TerminalPane();
        pane.SetInk(Ink.For(true));
        var tab = new TerminalPane.Tab(new TerminalAgent(new AgentOptions(dir.Path), _ => { }), "cuenta");
        IReadOnlyList<PastSession> sessions = [new("otra", "Una sesión anterior", DateTime.Now, dir["otra.jsonl"])];
        typeof(TerminalPane).GetMethod("ShowHistory", Private)!.Invoke(pane, [tab, sessions]);
        var body = (StackPanel)typeof(TerminalPane).GetField("_sideBody", Private)!.GetValue(pane)!;

        var clear = Assert.IsType<Border>(body.Children[^1]);
        Assert.Equal(L.T("Borrar todo el historial"), AutomationProperties.GetName(clear));
        Assert.IsType<Viewbox>(clear.Child);
        Assert.StartsWith(L.T("Borrar todo el historial"), (string)Avalonia.Controls.ToolTip.GetTip(clear)!);
        tab.Agent.Dispose();
    });
}
