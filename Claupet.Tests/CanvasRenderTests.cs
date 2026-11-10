using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Control = Avalonia.Controls.Control;
using ScrollViewer = Avalonia.Controls.ScrollViewer;
using Window = Avalonia.Controls.Window;

namespace Claupet.Tests;

/// <summary>Canvas no rehace lo que no cambiÃ³, y lo que estÃ¡ lejos de la vista se dibuja al acercarse.</summary>
public sealed class CanvasRenderTests
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    static CanvasOp Text(string content) => new("insert", Kind: "markdown", Content: content);

    static void With(IReadOnlyList<CanvasOp> sections, Action<CanvasPane, CanvasStore, Window, string> test) => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var store = new CanvasStore(dir.Path);
        var doc = store.Create("Largo", CanvasJson.User, sections);
        var terminal = new TerminalPane();
        terminal.SetInk(Ink.For(true));
        var pane = new CanvasPane((text, folder) => terminal.MarkdownPage(text, folder), store);
        pane.SetInk(Ink.For(true));
        var window = new Window { Width = 1200, Height = 900, Content = pane };
        window.Show();
        pane.Select(doc.Id);
        Settle(window);
        try { test(pane, store, window, doc.Id); }
        finally
        {
            pane.Close();
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Headless.Frame(window);
        }
    }

    static Control Block(CanvasPane pane, string id) =>
        pane.GetVisualDescendants().OfType<Control>().Single(c => c.Tag is string t && t == "blk:" + id);

    static IDictionary Lazy(CanvasPane pane) => (IDictionary)typeof(CanvasPane).GetField("_lazy", Private)!.GetValue(pane)!;

    static void DiskChanged(CanvasPane pane, string id) => typeof(CanvasPane).GetMethod("OnDiskChanged", Private)!.Invoke(pane, [id]);

    [Fact]
    public void ASectionThatDidNotChangeIsReused() => With([Text("uno"), Text("dos")], (pane, store, window, id) =>
    {
        var first = Block(pane, "s1");
        var second = Block(pane, "s2");
        store.Apply(id, [new CanvasOp("replace", Id: "s2", Content: "dos, cambiado")], "Codex");
        DiskChanged(pane, id);
        Settle(window);

        Assert.Same(first, Block(pane, "s1"));
        Assert.NotSame(second, Block(pane, "s2"));
    });

    [Fact]
    public void TheButtonsOfAReusedSectionSeeTheCurrentDocument() => With([Text("uno"), Text("dos"), Text("tres")], (pane, store, window, id) =>
    {
        var first = Block(pane, "s1");
        // Otro agrega una secciÃ³n al final: s1 no cambia (se reusa), pero Â«BajarÂ» tiene que conocer el documento nuevo.
        store.Apply(id, [new CanvasOp("insert", Kind: "markdown", Content: "cuatro")], "Codex");
        DiskChanged(pane, id);
        Settle(window);
        Assert.Same(first, Block(pane, "s1"));

        var doc = (CanvasDoc)typeof(CanvasPane).GetField("_doc", Private)!.GetValue(pane)!;
        CanvasDoc? seen = null;
        typeof(CanvasPane).GetMethod("Current", Private)!.Invoke(pane, [store.Load(id)!.Section("s1")!, (Action<CanvasDoc, CanvasSection>)((d, _) => seen = d)]);
        Assert.Same(doc, seen);
        Assert.Equal(4, seen!.Sections.Count);
    });

    [Fact]
    public void FarSectionsWaitUntilTheyAreNear()
    {
        var lines = string.Join("\n\n", Enumerable.Range(1, 12).Select(n => $"PÃ¡rrafo {n} de una secciÃ³n larga."));
        With([.. Enumerable.Range(1, 40).Select(n => Text($"# SecciÃ³n {n}\n\n{lines}"))], (pane, store, window, id) =>
        {
            Assert.Contains("s40", Lazy(pane).Keys.Cast<string>());
            Assert.DoesNotContain("s1", Lazy(pane).Keys.Cast<string>());

            var scroll = (ScrollViewer)typeof(CanvasPane).GetField("_scroll", Private)!.GetValue(pane)!;
            for (var i = 0; i < 60 && Lazy(pane).Contains("s40"); i++)
            {
                scroll.Offset = new Vector(0, scroll.Extent.Height);
                Settle(window);
            }
            Assert.DoesNotContain("s40", Lazy(pane).Keys.Cast<string>());
        });
    }

    [Fact]
    public void TheListRereadsOnlyWhatChanged()
    {
        using var dir = new TempDir();
        var store = new CanvasStore(dir.Path);
        var a = store.Create("A", CanvasJson.User);
        var b = store.Create("B", CanvasJson.User);
        Assert.Equal(2, store.List().Count);

        store.Apply(a.Id, [new CanvasOp("title", Title: "A nuevo")], CanvasJson.User);
        Assert.Contains(store.List(), s => s.Title == "A nuevo");

        store.Delete(b.Id);
        Assert.Equal(["A nuevo"], store.List().Select(s => s.Title));
    }
}
