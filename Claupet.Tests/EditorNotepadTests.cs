using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit.Document;
using Control = Avalonia.Controls.Control;
using Panel = Avalonia.Controls.Panel;
using Point = Avalonia.Point;
using TextBox = Avalonia.Controls.TextBox;
using Window = Avalonia.Controls.Window;

namespace Claupet.Tests;

/// <summary>El editor al estilo de Notepad++: líneas, buscar y reemplazar, formato, grupos de pestañas y la sesión.</summary>
public sealed class EditorNotepadTests
{
    // ── Líneas ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DuplicarCopiaLaLineaDebajoOLoSeleccionado()
    {
        var doc = new TextDocument("uno\r\ndos\r\ntres");
        var (start, length) = EditorLines.Duplicate(doc, 6, 0);
        Assert.Equal("uno\r\ndos\r\ndos\r\ntres", doc.Text);
        Assert.Equal((11, 0), (start, length));

        var selected = new TextDocument("abc");
        Assert.Equal((2, 1), EditorLines.Duplicate(selected, 1, 1));
        Assert.Equal("abbc", selected.Text);

        var last = new TextDocument("a\nfin");
        EditorLines.Duplicate(last, 3, 0);
        Assert.Equal("a\nfin\nfin", last.Text);
    }

    [Fact]
    public void SubirYBajarLineasLasCambiaConLaVecina()
    {
        var doc = new TextDocument("a\nb\nc");
        Assert.Equal((0, 0), EditorLines.Move(doc, 2, 0, up: true));
        Assert.Equal("b\na\nc", doc.Text);
        Assert.Equal((0, 0), EditorLines.Move(doc, 0, 0, up: true));
        Assert.Equal("b\na\nc", doc.Text);
        Assert.Equal((4, 0), EditorLines.Move(doc, 2, 0, up: false));
        Assert.Equal("b\nc\na", doc.Text);
        Assert.Equal((4, 0), EditorLines.Move(doc, 4, 0, up: false));
        Assert.Equal("b\nc\na", doc.Text);

        // Varias líneas a la vez (la selección termina al principio de la siguiente: esa no cuenta).
        var block = new TextDocument("1\n2\n3\n4");
        var (start, length) = EditorLines.Move(block, 0, 4, up: false);
        Assert.Equal("3\n1\n2\n4", block.Text);
        Assert.Equal((2, 4), (start, length));
    }

    [Fact]
    public void ComentarPoneYQuitaElComentarioALaAlturaDeLaMenosSangrada()
    {
        var doc = new TextDocument("    if (x)\n        y();\n\n    z();");
        EditorLines.ToggleComment(doc, 0, doc.TextLength, ("//", null));
        Assert.Equal("    // if (x)\n    //     y();\n\n    // z();", doc.Text);
        EditorLines.ToggleComment(doc, 0, doc.TextLength, ("//", null));
        Assert.Equal("    if (x)\n        y();\n\n    z();", doc.Text);

        // Una sola línea, sin seleccionar: el cursor se corre con el texto.
        var one = new TextDocument("x = 1");
        Assert.Equal((4, 0), EditorLines.ToggleComment(one, 2, 0, ("#", null)));
        Assert.Equal("# x = 1", one.Text);

        var html = new TextDocument("  <b>hola</b>");
        EditorLines.ToggleComment(html, 0, 0, ("<!--", "-->"));
        Assert.Equal("  <!-- <b>hola</b> -->", html.Text);
        EditorLines.ToggleComment(html, 0, 0, ("<!--", "-->"));
        Assert.Equal("  <b>hola</b>", html.Text);
    }

    [Fact]
    public void BorrarLineasNoDejaUnaLineaVaciaAlFinal()
    {
        var doc = new TextDocument("a\nb\nc");
        EditorLines.Delete(doc, 2, 0);
        Assert.Equal("a\nc", doc.Text);
        EditorLines.Delete(doc, 2, 0);
        Assert.Equal("a", doc.Text);
        EditorLines.Delete(doc, 0, 0);
        Assert.Equal("", doc.Text);
    }

    // ── Buscar ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuscarRespetaMayusculasPalabraCompletaYExpresiones()
    {
        const string text = "Hola hola holanda, HOLA.";
        Assert.Equal(4, EditorSearch.FindAll(text, new EditorQuery("hola").Pattern()!).Count);
        Assert.Single(EditorSearch.FindAll(text, new EditorQuery("hola", MatchCase: true, WholeWord: true).Pattern()!));
        Assert.Equal(3, EditorSearch.FindAll(text, new EditorQuery("hola", WholeWord: true).Pattern()!).Count);
        Assert.Equal([(0, 4), (5, 4)], EditorSearch.FindAll(text, new EditorQuery(@"\b[Hh]ola\b", MatchCase: true, Regex: true).Pattern()!));
        // A medio escribir: no rompe, solo no busca.
        Assert.Null(new EditorQuery("(", Regex: true).Pattern());
        // Las tildes cuentan como letra para «palabra completa».
        Assert.Empty(EditorSearch.FindAll("canción", new EditorQuery("canci", WholeWord: true).Pattern()!));
    }

    [Fact]
    public void ReemplazarTodoSeDeshaceDeUnaVezYUsaLosGrupos()
    {
        var doc = new TextDocument("a=1, b=2");
        var count = EditorSearch.ReplaceAll(doc, new EditorQuery(@"(\w)=(\d)", Regex: true), "$2:$1");
        Assert.Equal(2, count);
        Assert.Equal("1:a, 2:b", doc.Text);
        doc.UndoStack.Undo();
        Assert.Equal("a=1, b=2", doc.Text);

        var literal = new TextDocument("x.x");
        EditorSearch.ReplaceAll(literal, new EditorQuery("."), "$1");
        Assert.Equal("x$1x", literal.Text);
    }

    // ── Formato ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FinDeLineaSeDetectaYSeConvierte()
    {
        Assert.Equal("\n", EditorFormat.DetectNewLine("a\nb\nc\r\nd"));
        Assert.Equal("\r\n", EditorFormat.DetectNewLine("a\r\nb"));
        var doc = new TextDocument("a\r\nb\nc\r\n");
        EditorFormat.ConvertNewLines(doc, "\n");
        Assert.Equal("a\nb\nc\n", doc.Text);
        doc.UndoStack.Undo();
        Assert.Equal("a\r\nb\nc\r\n", doc.Text);
    }

    [Fact]
    public void SangriaSeDetecta()
    {
        Assert.Equal((true, 4), EditorFormat.DetectIndent("a\n\tb\n\tc"));
        Assert.Equal((false, 2), EditorFormat.DetectIndent("{\n  a\n  b\n    c\n}"));
        Assert.Equal((false, 4), EditorFormat.DetectIndent("{\n    a\n        b\n}"));
    }

    [Fact]
    public void LaCodificacionSeNombraComoEnNotepad()
    {
        Assert.Equal("UTF-8", EditorFormat.EncodingLabel(new UTF8Encoding(false)));
        Assert.Equal("UTF-8 con BOM", EditorFormat.EncodingLabel(new UTF8Encoding(true)));
        Assert.Equal("UTF-16 LE", EditorFormat.EncodingLabel(new UnicodeEncoding(false, true)));
        Assert.StartsWith("ANSI", EditorFormat.EncodingLabel(TextFiles.Ansi));
    }

    [Fact]
    public void ElLenguajeSaleDeLaExtension()
    {
        Assert.Equal(EditorLanguages.Markdown, EditorLanguages.ForPath("notas.md"));
        Assert.Equal(EditorLanguages.Text, EditorLanguages.ForPath("notas.txt"));
        Assert.Equal(EditorLanguages.Text, EditorLanguages.ForPath(null));
        Assert.Equal("C#", EditorLanguages.ForPath("Program.cs"));
        Assert.Equal(EditorLanguages.Plain, EditorLanguages.ForPath("datos.xyz"));
        Assert.Contains("C#", EditorLanguages.All);
        Assert.DoesNotContain("XmlDoc", EditorLanguages.All);
        Assert.Equal(("//", null), EditorLanguages.Comment("C#"));
        Assert.True(EditorDoc.IsCode("a.cs"));
        Assert.False(EditorDoc.IsCode("a.md"));
    }

    [Fact]
    public void LaLlaveDelCursorEncuentraSuPareja()
    {
        var doc = new TextDocument("f(a[1], (b))");
        Assert.Equal((1, 11), EditorMarks.Brackets(doc, 2));
        Assert.Equal((11, 1), EditorMarks.Brackets(doc, 12));
        Assert.Equal((3, 5), EditorMarks.Brackets(doc, 3));
        Assert.Null(EditorMarks.Brackets(doc, 0));
    }

    // ── En el editor ────────────────────────────────────────────────────────────────────────

    static Window Show(Control pane)
    {
        var window = new Window { Width = 1200, Height = 900, Content = new Panel { Children = { pane } } };
        window.Show();
        Headless.Frame(window);
        return window;
    }

    static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        Headless.Frame(window);
    }

    static TextBlock? Find(Window window, string text) => window.GetVisualDescendants().OfType<TextBlock>()
        .FirstOrDefault(t => t.Text == text && t.IsEffectivelyVisible);

    /// <summary>Lo que se ve en las ventanas emergentes (la lista de un grupo) también cuenta.</summary>
    static TextBlock? FindAnywhere(Window window, string text) =>
        window.GetVisualDescendants().Concat(Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(window)?.GetVisualDescendants() ?? [])
            .OfType<TextBlock>().FirstOrDefault(t => t.Text == text && t.IsEffectivelyVisible);

    static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(point, MouseButton.Left);
        Settle(window);
    }

    static T Field<T>(EditorPane editor, string name) =>
        (T)typeof(EditorPane).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;

    static void WithEditor(Action<EditorPane, Window, TempDir> test) => Headless.Run(() =>
    {
        using var dir = new TempDir();
        EditorRecents.FileForTests = dir["recientes.json"];
        EditorSession.FolderForTests = dir["sesion"];
        var editor = new EditorPane();
        editor.SetInk(Ink.For(true));
        var window = Show(editor);
        // Como la ventana de la Console: las teclas que no llegan por el foco, se las manda.
        window.KeyDown += (_, e) => editor.HandleKey(e);
        try { test(editor, window, dir); }
        finally
        {
            window.Close();
            EditorRecents.FileForTests = null;
            EditorSession.FolderForTests = null;
        }
    });

    [Fact]
    public void CtrlFBuscaLoSeleccionadoYF3PasaAlSiguiente() => WithEditor((editor, window, dir) =>
    {
        editor.Open(dir.Write("a.cs", "var uno = 1;\nuno++;\nreturn uno;"));
        Settle(window);
        var text = Field<AvaloniaEdit.TextEditor>(editor, "_editor");
        text.TextArea.Focus();
        text.Select(4, 3);
        Settle(window);
        window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control);
        Settle(window);
        Assert.Equal("uno", Field<TextBox>(editor, "_findBox").Text);
        Assert.NotNull(Find(window, "1 de 3"));

        window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.None);
        Settle(window);
        Assert.NotNull(Find(window, "2 de 3"));
        Assert.Equal((13, 3), (text.SelectionStart, text.SelectionLength));

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Settle(window);
        Assert.False(Field<Border>(editor, "_findBar").IsVisible);
    });

    [Fact]
    public void CtrlGVaALaLineaYLaBarraLoMuestra() => WithEditor((editor, window, dir) =>
    {
        editor.Open(dir.Write("a.cs", "a\nb\nc\nd"));
        Settle(window);
        Assert.NotNull(Find(window, "Lín 1, col 1"));
        Assert.NotNull(Find(window, "C#"));
        Assert.NotNull(Find(window, "Espacios: 4"));
        window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
        Settle(window);
        var box = Field<TextBox>(editor, "_goToBox");
        Assert.True(Field<Border>(editor, "_goToBar").IsVisible);
        box.Text = "3";
        box.Focus();
        Settle(window);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Settle(window);
        Assert.NotNull(Find(window, "Lín 3, col 1"));
    });

    [Fact]
    public void LosAtajosDeLineaFuncionanConElFocoEnElTexto() => WithEditor((editor, window, dir) =>
    {
        editor.Open(dir.Write("a.py", "x = 1\ny = 2"));
        Settle(window);
        var text = Field<AvaloniaEdit.TextEditor>(editor, "_editor");
        text.TextArea.Focus();
        text.CaretOffset = 0;
        Settle(window);
        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Control);
        Settle(window);
        Assert.Equal("x = 1\nx = 1\ny = 2", editor.Current!.Document.Text);
        window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.Control);
        Settle(window);
        Assert.Equal("x = 1\n# x = 1\ny = 2", editor.Current.Document.Text);
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.Alt);
        Settle(window);
        Assert.Equal("x = 1\ny = 2\n# x = 1", editor.Current.Document.Text);
        // Un solo Ctrl+Z por cada atajo.
        editor.Current.Document.UndoStack.Undo();
        Assert.Equal("x = 1\n# x = 1\ny = 2", editor.Current.Document.Text);
    });

    [Fact]
    public void ElZoomCambiaLaLetraYCtrl0Vuelve() => WithEditor((editor, window, dir) =>
    {
        editor.Open(dir.Write("a.cs", "x"));
        Settle(window);
        var text = Field<AvaloniaEdit.TextEditor>(editor, "_editor");
        var size = text.FontSize;
        window.KeyPressQwerty(PhysicalKey.Equal, RawInputModifiers.Control);
        Settle(window);
        Assert.True(text.FontSize > size);
        Assert.NotNull(Find(window, "110 %"));
        window.KeyPressQwerty(PhysicalKey.Digit0, RawInputModifiers.Control);
        Settle(window);
        Assert.Equal(size, text.FontSize);
    });

    [Fact]
    public void LosSinTituloSeNumeran() => WithEditor((editor, window, _) =>
    {
        editor.New();
        editor.New();
        Settle(window);
        Assert.Equal(["Sin título 1", "Sin título 2"], editor.Docs.Select(d => d.Title));
        editor.Close(editor.Docs[0]);
        editor.New();
        Assert.Equal("Sin título 1", editor.Docs[^1].Title);
    });

    [Fact]
    public void UnGrupoSeVeComoUnaPildoraYSeDespliegaEnLista() => WithEditor((editor, window, dir) =>
    {
        editor.Open(dir.Write("a.cs", "a"));
        editor.Open(dir.Write("b.cs", "b"));
        editor.Open(dir.Write("c.md", "c"));
        Settle(window);
        var group = editor.NewGroup(editor.Docs[0]);
        editor.AddToGroup(editor.Docs.First(d => d.Title == "b.cs"), group.Id);
        Settle(window);
        // Las del grupo no se ven sueltas: está la píldora con el nombre y cuántas tiene.
        Assert.Equal("Grupo 1", group.Name);
        Assert.Equal(["a.cs", "b.cs", "c.md"], editor.Docs.Select(d => d.Title));
        IEnumerable<string?> InTabs() => Field<StackPanel>(editor, "_tabs").GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text);
        Assert.DoesNotContain("a.cs", InTabs());
        Assert.Contains("Grupo 1", InTabs());
        Assert.Contains("2", InTabs());
        // Recién hecho, la lista ya está abierta (con la caja del nombre); un clic en la píldora la cierra y otro la abre.
        Assert.NotNull(FindAnywhere(window, "a.cs"));
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Settle(window);
        Border Pill() => window.GetVisualDescendants().OfType<Border>().First(b => Equals(b.Tag, "grupo:" + group.Id));
        Click(window, Pill());
        Assert.Null(FindAnywhere(window, "a.cs"));
        // El clic que cierra la lista no la vuelve a abrir al soltarse; otro clic, un rato después, sí.
        Thread.Sleep(350);
        Click(window, Pill());
        Assert.NotNull(FindAnywhere(window, "a.cs"));
        Assert.NotNull(FindAnywhere(window, "b.cs"));

        editor.Ungroup(editor.Docs[0]);
        editor.Ungroup(editor.Docs[1]);
        Settle(window);
        Assert.Empty(editor.Groups);
        Assert.Contains("a.cs", InTabs());
    });

    [Fact]
    public void LaSesionVuelveConLoSinGuardarYLosGrupos() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        EditorRecents.FileForTests = dir["recientes.json"];
        EditorSession.FolderForTests = dir["sesion"];
        try
        {
            var path = dir.Write("nota.cs", "antes");
            var first = new EditorPane();
            first.SetInk(Ink.For(true));
            var window = Show(first);
            first.RestoreSession();
            first.Open(path);
            first.Current!.Document.Insert(0, "mío ");
            first.New();
            first.Current!.Document.Insert(0, "borrador");
            first.New(); // vacío: no se guarda
            var group = first.NewGroup(first.Docs[0]);
            group.Name = "Trabajo";
            first.Select(first.Docs[1]);
            Settle(window);
            // Con la sesión, cerrar no pregunta: lo sin guardar queda respaldado.
            Assert.False(first.HasUnsaved);
            window.Close();

            var second = new EditorPane();
            second.SetInk(Ink.For(true));
            var again = Show(second);
            second.RestoreSession();
            Settle(again);
            Assert.Equal(["nota.cs", "Sin título 1"], second.Docs.Select(d => d.Title));
            Assert.Equal("mío antes", second.Docs[0].Document.Text);
            Assert.True(second.Docs[0].Dirty);
            Assert.Equal("borrador", second.Docs[1].Document.Text);
            Assert.Equal("Sin título 1", second.Current!.Title);
            Assert.Equal("Trabajo", Assert.Single(second.Groups).Name);
            Assert.Equal(second.Groups[0].Id, second.Docs[0].Group);
            // Ctrl+Z vuelve a lo que hay en el disco.
            second.Docs[0].Document.UndoStack.Undo();
            Assert.Equal("antes", second.Docs[0].Document.Text);
            Assert.False(second.Docs[0].Dirty);
            again.Close();
        }
        finally
        {
            EditorRecents.FileForTests = null;
            EditorSession.FolderForTests = null;
        }
    });
}
