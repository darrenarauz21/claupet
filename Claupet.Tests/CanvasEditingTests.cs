using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TextBox = Avalonia.Controls.TextBox;
using Window = Avalonia.Controls.Window;

namespace Claupet.Tests;

/// <summary>Lo que se está escribiendo en una sección no se pierde, y los menús de Canvas responden al teclado.</summary>
public sealed class CanvasEditingTests
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    static CanvasOp Text(string content) => new("insert", Kind: "markdown", Content: content);

    static void With(Action<CanvasPane, CanvasStore, Window, string, string> test) => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var store = new CanvasStore(dir.Path);
        var first = store.Create("Primero", CanvasJson.User, [Text("antes")]);
        var second = store.Create("Segundo", CanvasJson.User, [Text("otro")]);
        var terminal = new TerminalPane();
        terminal.SetInk(Ink.For(true));
        var pane = new CanvasPane((text, folder) => terminal.MarkdownPage(text, folder), store);
        pane.SetInk(Ink.For(true));
        var window = new Window { Width = 1200, Height = 900, Content = pane };
        window.Show();
        pane.Select(first.Id);
        Settle(window);
        try { test(pane, store, window, first.Id, second.Id); }
        finally
        {
            pane.Close();
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        Headless.Frame(window);
    }

    /// <summary>Abre la sección para editar y escribe en ella, sin guardar.</summary>
    static TextBox Write(CanvasPane pane, CanvasStore store, Window window, string id, string text)
    {
        var section = store.Load(id)!.Section("s1")!;
        typeof(CanvasPane).GetMethod("StartEditing", Private)!.Invoke(pane, [section]);
        Settle(window);
        var box = (TextBox)typeof(CanvasPane).GetField("_editor", Private)!.GetValue(pane)!;
        box.Text = text;
        return box;
    }

    static string Saved(CanvasStore store, string id) => store.Load(id)!.Section("s1")!.Content;

    [Fact]
    public void CambiarDeCanvasGuardaLoEscrito() => With((pane, store, window, first, second) =>
    {
        Write(pane, store, window, first, "escrito");
        pane.Select(second);
        Settle(window);
        Assert.Equal("escrito", Saved(store, first));
        Assert.Equal(second, pane.CurrentId);
    });

    [Fact]
    public void CerrarLaVentanaGuardaLoEscrito() => With((pane, store, window, first, _) =>
    {
        Write(pane, store, window, first, "escrito");
        pane.Close();
        Assert.Equal("escrito", Saved(store, first));
    });

    [Fact]
    public void CopiarLlevaLoQueSeAcabaDeEscribir() => With((pane, store, window, first, _) =>
    {
        Write(pane, store, window, first, "escrito");
        typeof(CanvasPane).GetMethod("CopyMarkdown", Private)!.Invoke(pane, [store.Load(first)!]);
        Settle(window);
        Assert.Equal("escrito", Saved(store, first));
    });

    [Fact]
    public void EscDescartaLoEscrito() => With((pane, store, window, first, _) =>
    {
        var box = Write(pane, store, window, first, "escrito");
        box.Focus();
        Settle(window);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Settle(window);
        Assert.Equal("antes", Saved(store, first));
    });

    [Fact]
    public void GuardarSinCambiosNoCreaUnaVersion() => With((pane, store, window, first, _) =>
    {
        var version = store.Load(first)!.Version;
        Write(pane, store, window, first, "antes");
        pane.Close();
        Assert.Equal(version, store.Load(first)!.Version);
    });

    static IEnumerable<string?> Visible(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text);

    [Fact]
    public void EscCierraLosMenusAunqueNoTenganCaja() => With((pane, _, window, _, _) =>
    {
        typeof(CanvasPane).GetMethod("MoreMenu", Private)!.Invoke(pane, [pane]);
        Settle(window);
        Assert.Contains(L.T("Abrir la carpeta"), Visible(window));
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Settle(window);
        Assert.DoesNotContain(L.T("Abrir la carpeta"), Visible(window));
    });

    [Fact]
    public void ExportarMuestraElNombreDeCadaFormato() => With((pane, _, window, _, _) =>
    {
        typeof(CanvasPane).GetMethod("ExportPopup", Private)!.Invoke(pane, [pane]);
        Settle(window);
        var shown = Visible(window).ToList();
        foreach (var name in new[] { "PDF", "Word", "Markdown", L.T("Página HTML"), "JSON", L.T("Copiar como Markdown") })
            Assert.Contains(name, shown);
    });

    [Fact]
    public void LaCajaEnfocadaNoSePintaDeNegro() => With((pane, store, window, first, _) =>
    {
        var box = Write(pane, store, window, first, "escrito");
        Assert.Equal(Avalonia.Media.Brushes.Transparent, box.Resources["TextControlBackgroundFocused"]);
    });

    [Fact]
    public void ElPdfNoDejaEspacioQueEmpujeUnaPaginaEnBlanco()
    {
        using var dir = new TempDir();
        var store = new CanvasStore(dir.Path);
        var html = CanvasExport.Html(store.Create("Uno", CanvasJson.User, [Text("hola")]));
        Assert.Contains("section:last-child{margin-bottom:0}", html);
        Assert.Contains("iframe{display:block;box-sizing:border-box", html);
    }
}
