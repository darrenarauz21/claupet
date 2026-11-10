namespace Claupet;

using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;

/// <summary>Un archivo abierto en el editor: su texto (con su historial de deshacer), dónde está y cómo se muestra.</summary>
sealed class EditorDoc
{
    public string? Path { get; set; }
    public required TextDocument Document { get; init; }
    /// <summary>Texto, Markdown, Código sin colores o un lenguaje (EditorLanguages). Texto y Markdown se ven como documento.</summary>
    public string Language { get; set; } = EditorLanguages.Text;
    /// <summary>Código: letra fija, números de línea y colores; si no, documento (Markdown sin marcas fuera de la línea del cursor).</summary>
    public bool Code => !EditorLanguages.IsDocument(Language);
    public int Caret { get; set; }
    public double Scroll { get; set; }
    /// <summary>Cómo estaba en el disco la última vez que se leyó, se guardó o se decidió conservar lo de aquí.</summary>
    public DiskState? Disk { get; set; }
    /// <summary>Cuántas veces seguidas no se encontró: reemplazar un archivo lo hace desaparecer un momento.</summary>
    public int MissingChecks { get; set; }
    /// <summary>La codificación en que vino el archivo (se guarda igual si se puede: TextFiles.ForSaving); null, uno nuevo.</summary>
    public Encoding? Encoding { get; set; }
    /// <summary>Se eligió otra codificación en la barra de abajo: hay que guardar aunque el texto sea el mismo.</summary>
    public bool EncodingChanged { get; set; }
    /// <summary>El fin de línea del archivo («\r\n» o «\n»): el de Enter y el que se muestra abajo.</summary>
    public string NewLine { get; set; } = Environment.NewLine;
    public bool Tabs { get; set; }
    public int IndentSize { get; set; } = 4;
    /// <summary>El grupo de pestañas en que está (EditorGroup.Id), o null.</summary>
    public string? Group { get; set; }
    /// <summary>El número de «Sin título N».</summary>
    public int Number { get; set; } = 1;
    /// <summary>Con qué nombre se respalda lo que no se guardó (EditorSession).</summary>
    public string BackupId { get; set; } = Guid.NewGuid().ToString("N");
    public string Title => Path is null ? L.T("Sin título {0}", Number) : System.IO.Path.GetFileName(Path);
    public bool Dirty => !Document.UndoStack.IsOriginalFile || EncodingChanged;

    /// <summary>Markdown y texto se abren como documento; lo demás, como código.</summary>
    public static bool IsCode(string? path) => !EditorLanguages.IsDocument(EditorLanguages.ForPath(path));

    /// <summary>Sangría y fin de línea según lo que ya tiene el texto.</summary>
    public void DetectFormat()
    {
        var text = Document.Text;
        NewLine = EditorFormat.DetectNewLine(text);
        (Tabs, IndentSize) = EditorFormat.DetectIndent(text);
    }
}

/// <summary>Los archivos abiertos hace poco en el editor, en editor-recientes.json junto a los ajustes (no es un ajuste: lo guarda la Console).</summary>
static class EditorRecents
{
    /// <summary>Otro archivo en vez del de la persona: las pruebas no tocan su lista.</summary>
    internal static string? FileForTests { get; set; }

    static string FilePath => FileForTests ?? DataFolder.In("editor-recientes.json");

    public static List<string> Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? [] : []; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return []; }
    }

    public static List<string> Add(string path)
    {
        var list = Load();
        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        if (list.Count > 12) list.RemoveRange(12, list.Count - 12);
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list), new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
        return list;
    }
}

/// <summary>
/// El editor de Claupet Console, para texto y para código, al estilo de Notepad++: arriba, el botón de los archivos,
/// las pestañas (con grupos que se despliegan en lista), buscar y más opciones; en medio, el texto, con la barra de
/// buscar y reemplazar flotando arriba a la derecha; abajo, la barra de estado (línea, sangría, codificación, fin de
/// línea, lenguaje y zoom). AvaloniaEdit es solo el motor: el aspecto lo pone EditorLook.
/// </summary>
sealed partial class EditorPane : Grid
{
    /// <summary>Se pidió mostrar u ocultar la lista de archivos (la columna de la izquierda).</summary>
    public event Action? FilesToggled;
    /// <summary>Cambió algo de la lista: se abrió, cerró, guardó o eligió un archivo.</summary>
    public event Action? DocsChanged;

    const string NewFileIcon = "M7,3 H14 L19,8 V20 A1,1 0 0 1 18,21 H7 A1,1 0 0 1 6,20 V4 A1,1 0 0 1 7,3 Z M14,3 V8 H19 M12.5,11.5 V17.5 M9.5,14.5 H15.5";
    const string OpenFolderIcon = "M3.5,7 A2,2 0 0 1 5.5,5 H9.5 L11.5,7 H18.5 A2,2 0 0 1 20.5,9 V17 A2,2 0 0 1 18.5,19 H5.5 A2,2 0 0 1 3.5,17 Z";
    const string PanelIcon = "M6.5,4.5 H17.5 A3,3 0 0 1 20.5,7.5 V16.5 A3,3 0 0 1 17.5,19.5 H6.5 A3,3 0 0 1 3.5,16.5 V7.5 A3,3 0 0 1 6.5,4.5 Z M9,4.5 V19.5";
    const string MoreIcon = "M3.4,12 A1.6,1.6 0 1 0 6.6,12 A1.6,1.6 0 1 0 3.4,12 Z M10.4,12 A1.6,1.6 0 1 0 13.6,12 A1.6,1.6 0 1 0 10.4,12 Z "
        + "M17.4,12 A1.6,1.6 0 1 0 20.6,12 A1.6,1.6 0 1 0 17.4,12 Z";
    const string SearchIcon = "M10.5,4.5 A6,6 0 1 1 10.5,16.5 A6,6 0 1 1 10.5,4.5 Z M15,15 L20,20";
    const string CloseIcon = "M7,7 L17,17 M17,7 L7,17";
    const double DocumentSize = 16.5, CodeSize = 13.5, Column = 640;

    readonly List<EditorDoc> _docs = [];
    readonly TextEditor _editor = new();
    readonly Border _body = new();
    /// <summary>El texto con lo que flota encima (buscar, ir a la línea).</summary>
    readonly Grid _stage = new();
    readonly EditorMarks _marks;
    Ink _ink = Ink.For(false);
    EditorInk _colors = EditorInk.For(Ink.For(false));
    EditorDoc? _current;
    List<string> _recents = EditorRecents.Load();
    StackPanel _tabs = new();
    StackPanel _tools = new();
    StackPanel _confirm = new();
    /// <summary>Lo que corre al contestar «¿Guardar los cambios?» (para cerrar un archivo o la ventana).</summary>
    Action? _afterConfirm;
    /// <summary>El archivo por el que se está preguntando si cambió afuera.</summary>
    EditorDoc? _asking;
    /// <summary>Cada poco se mira si los abiertos cambiaron: un agente los edita sin que la ventana pierda el foco.</summary>
    readonly DispatcherTimer _diskTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    /// <summary>Zoom en porcentaje (Ctrl+rueda, Ctrl+más, Ctrl+menos; Ctrl+0 vuelve a 100).</summary>
    int _zoom = 100;
    /// <summary>Ajustar las líneas largas en el modo Código (Alt+Z); el modo Documento siempre las ajusta.</summary>
    bool _wrap;
    /// <summary>Mostrar los espacios, las tabulaciones y los fines de línea.</summary>
    bool _spaces;

    public EditorPane()
    {
        RowDefinitions = new RowDefinitions("56,*,Auto");
        _editor.Options.HighlightCurrentLine = false;
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.Options.ConvertTabsToSpaces = true;
        _editor.Options.IndentationSize = 4;
        _editor.Options.AllowScrollBelowDocument = true;
        _editor.Background = Brushes.Transparent;
        _editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _marks = new EditorMarks(_editor.TextArea);
        _editor.TextArea.TextView.BackgroundRenderers.Add(_marks);
        // En el modo Documento el texto va en una columna de 640 al centro, con la barra al borde.
        _editor.SizeChanged += (_, _) => Pad();
        // Las marcas de Markdown se muestran solo en la línea del cursor: al cambiar de línea, se redibuja.
        var lastLine = 0;
        _editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (_current is { Code: false } && _editor.TextArea.Caret.Line != lastLine) _editor.TextArea.TextView.Redraw();
            lastLine = _editor.TextArea.Caret.Line;
            _editor.TextArea.TextView.InvalidateLayer(_marks.Layer);
            UpdateStatus();
        };
        _editor.TextArea.SelectionChanged += (_, _) =>
        {
            _editor.TextArea.TextView.InvalidateLayer(_marks.Layer);
            UpdateStatus();
        };
        _editor.TextChanged += (_, _) =>
        {
            if (_findBar.IsVisible) SearchLater();
            _sessionChanged = true;
        };
        // Ctrl+rueda: zoom (antes de que la rueda mueva el texto).
        _editor.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            Zoom(e.Delta.Y > 0 ? 10 : -10);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        // Los atajos llegan aquí primero, también con el foco en buscar; la ventana manda los que se le escapan.
        AddHandler(KeyDownEvent, (_, e) => HandleKey(e), RoutingStrategies.Tunnel);
        Grid.SetRow(_body, 1);
        _stage.Children.Add(_editor);
        _diskTimer.Tick += (_, _) =>
        {
            if (IsEffectivelyVisible) CheckDisk();
            SaveSessionIfChanged();
        };
        AttachedToVisualTree += (_, _) => _diskTimer.Start();
        DetachedFromVisualTree += (_, _) =>
        {
            _diskTimer.Stop();
            SaveSession();
        };
        Build();
    }

    public IReadOnlyList<EditorDoc> Docs => _docs;
    public EditorDoc? Current => _current;
    public IReadOnlyList<string> Recents => _recents;

    /// <summary>
    /// Si al cerrar se perdería algo. Con la sesión activa, lo que no se guardó queda respaldado y vuelve al abrir (como
    /// en Notepad++): no hace falta preguntar, salvo que el respaldo no se haya podido escribir.
    /// </summary>
    public bool HasUnsaved => _docs.Any(d => d.Dirty) && !(_sessionOn && SaveSession());

    public void SetInk(Ink ink)
    {
        if (ReferenceEquals(ink, _ink)) return;
        _ink = ink;
        _colors = EditorInk.For(ink);
        Build();
    }

    public void FocusText()
    {
        if (_current is not null) _editor.TextArea.Focus();
    }

    // ── Archivos ────────────────────────────────────────────────────────────────────────────

    public void New()
    {
        var doc = new EditorDoc { Document = new TextDocument(), Number = NextNumber() };
        doc.Document.UndoStack.MarkAsOriginalFile();
        Add(doc);
    }

    /// <summary>El número de «Sin título» más chico que no está en uso.</summary>
    int NextNumber()
    {
        var used = _docs.Where(d => d.Path is null).Select(d => d.Number).ToHashSet();
        var n = 1;
        while (used.Contains(n)) n++;
        return n;
    }

    public async Task OpenAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = L.T("Abrir"), AllowMultiple = true });
        foreach (var file in files)
            if (file.TryGetLocalPath() is { } path) Open(path);
    }

    /// <summary>Abre ese archivo (o, si ya está abierto, lo trae al frente).</summary>
    public void Open(string path)
    {
        if (_docs.FirstOrDefault(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)) is { } open)
        {
            Select(open);
            return;
        }
        if (Read(path) is { } doc)
        {
            _recents = EditorRecents.Add(path);
            Add(doc);
        }
    }

    /// <summary>El archivo como EditorDoc, sin agregarlo; null (y un aviso) si no se pudo.</summary>
    EditorDoc? Read(string path)
    {
        try
        {
            if (new FileInfo(path).Length > TextFiles.MaxEditable)
            {
                Notice(L.T("«{0}» es demasiado grande para el editor (más de 20 MB).", System.IO.Path.GetFileName(path)));
                return null;
            }
            var (text, encoding) = TextFiles.Read(path);
            var doc = new EditorDoc { Path = path, Document = new TextDocument(text), Language = EditorLanguages.ForPath(path), Encoding = encoding, Disk = TextFiles.StateOf(path) };
            doc.DetectFormat();
            doc.Document.UndoStack.MarkAsOriginalFile();
            return doc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notice(L.T("No se pudo abrir «{0}»: {1}", System.IO.Path.GetFileName(path), ex.Message));
            return null;
        }
    }

    /// <summary>Guarda el archivo (si no tiene lugar todavía, pregunta dónde). true si quedó guardado.</summary>
    public async Task<bool> SaveAsync(EditorDoc doc, bool asNew = false, Action? saved = null)
    {
        var path = doc.Path;
        // Alguien más lo cambió (un agente, git): guardar ahora borraría eso sin que se note, así que se pregunta.
        if (path is not null && !asNew && TextFiles.Compare(path, doc.Disk) is { Change: DiskChange.Changed } outside)
        {
            AskChanged(doc, outside, saving: true, saved);
            return false;
        }
        if (path is null || asNew)
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return false;
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = L.T("Guardar como"),
                SuggestedFileName = doc.Path is null ? L.T("Texto") + (doc.Language == EditorLanguages.Markdown ? ".md" : ".txt") : System.IO.Path.GetFileName(doc.Path),
                DefaultExtension = doc.Code ? null : doc.Language == EditorLanguages.Markdown ? "md" : "txt",
                ShowOverwritePrompt = true,
            });
            if (file?.TryGetLocalPath() is not { } chosen) return false;
            path = chosen;
        }
        try
        {
            // Primero a un archivo de al lado y después se cambia: un corte a mitad no deja el archivo a medias.
            var temp = path + ".claupet-tmp";
            var encoding = TextFiles.ForSaving(doc.Document.Text, doc.Encoding);
            File.WriteAllText(temp, doc.Document.Text, encoding);
            doc.Encoding = encoding;
            doc.EncodingChanged = false;
            File.Move(temp, path, overwrite: true);
            var named = doc.Path is null;
            doc.Path = path;
            // Un «Sin título» que se guarda toma el lenguaje de su extensión (si no se eligió otro a mano).
            if (named && doc.Language is EditorLanguages.Text or EditorLanguages.Markdown) doc.Language = EditorLanguages.ForPath(path);
            doc.Disk = TextFiles.StateOf(path);
            doc.MissingChecks = 0;
            doc.Document.UndoStack.MarkAsOriginalFile();
            _recents = EditorRecents.Add(path);
            if (doc == _current) Reshow();
            Refresh();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notice(L.T("No se pudo guardar «{0}»: {1}", System.IO.Path.GetFileName(path), ex.Message));
            return false;
        }
    }

    /// <summary>Cierra el archivo; si tiene cambios, pregunta antes.</summary>
    public void Close(EditorDoc doc)
    {
        if (doc.Dirty)
        {
            Select(doc);
            AskSave(doc, () => Remove(doc));
            return;
        }
        Remove(doc);
    }

    /// <summary>Antes de cerrar la ventana con cambios sin guardar: pregunta por el primero; exit corre si se puede salir.</summary>
    public void AskBeforeExit(Action exit)
    {
        if (_docs.FirstOrDefault(d => d.Dirty) is not { } dirty)
        {
            exit();
            return;
        }
        Select(dirty);
        AskSave(dirty, () =>
        {
            Remove(dirty, select: false);
            AskBeforeExit(exit);
        });
    }

    public void Select(EditorDoc doc)
    {
        Remember();
        _current = doc;
        Show(doc);
        Refresh();
        CheckDisk(doc);
    }

    /// <summary>Dónde estaban el cursor y el texto en el que se ve, para volver ahí.</summary>
    void Remember()
    {
        if (_current is not { } previous || _body.Child != _stage) return;
        previous.Caret = _editor.CaretOffset;
        previous.Scroll = _editor.VerticalOffset;
    }

    void Add(EditorDoc doc)
    {
        _docs.Add(doc);
        Watch(doc);
        Select(doc);
        FocusText();
    }

    void Watch(EditorDoc doc) => doc.Document.UndoStack.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName == nameof(UndoStack.IsOriginalFile)) Dispatcher.UIThread.Post(Refresh);
    };

    void Remove(EditorDoc doc, bool select = true)
    {
        if (_asking == doc) EndConfirm(run: false);
        var at = _docs.IndexOf(doc);
        _docs.Remove(doc);
        if (doc.Group is { } group && _docs.All(d => d.Group != group)) _groups.RemoveAll(g => g.Id == group);
        if (_current == doc)
        {
            _current = null;
            if (select && _docs.Count > 0) Select(_docs[Math.Min(at, _docs.Count - 1)]);
            else Show(null);
        }
        Refresh();
    }

    // ── Teclas ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Los atajos del editor. Llegan primero aquí (de camino al foco) y, si el foco está afuera, los manda la ventana.</summary>
    public void HandleKey(KeyEventArgs e)
    {
        if (e.Handled) return;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        // Lo que cambia el texto, solo con el foco en el texto (no en buscar).
        var inText = _current is not null && _editor.TextArea.IsKeyboardFocusWithin;
        var open = _current is not null;
        switch (e.Key)
        {
            case Key.N when ctrl && !alt: New(); break;
            case Key.O when ctrl && !alt: _ = OpenAsync(); break;
            case Key.S when ctrl && !alt && _current is { } doc: _ = SaveAsync(doc, asNew: shift); break;
            case Key.W when ctrl && !alt && _current is { } doc: Close(doc); break;
            case Key.Tab when ctrl && _docs.Count > 1: Cycle(shift ? -1 : 1); break;
            case Key.F when ctrl && open: ShowFind(replace: false); break;
            case Key.H when ctrl && open: ShowFind(replace: true); break;
            case Key.G when ctrl && open: ShowGoTo(); break;
            case Key.F3 when open: FindNext(!shift); break;
            case Key.Escape when _findBar.IsVisible || _goToBar.IsVisible: HideBars(); break;
            case Key.D when ctrl && !shift && inText: EditLines(EditorLines.Duplicate); break;
            case Key.K when ctrl && shift && inText: EditLines(EditorLines.Delete); break;
            case Key.Up or Key.Down when alt && !ctrl && inText: EditLines((d, s, l) => EditorLines.Move(d, s, l, e.Key == Key.Up)); break;
            // Ctrl+/ con el teclado de EE. UU.; Ctrl+Q (como en Notepad++) con cualquiera: en español «/» es Mayús+7.
            case Key.OemQuestion or Key.Divide or Key.Q when ctrl && inText: ToggleComment(); break;
            case Key.Z when alt && !ctrl && open: ToggleWrap(); break;
            case Key.OemPlus or Key.Add when ctrl && open: Zoom(10); break;
            case Key.OemMinus or Key.Subtract when ctrl && open: Zoom(-10); break;
            case Key.D0 or Key.NumPad0 when ctrl && open: Zoom(0); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>Ctrl+Tab: la pestaña siguiente (o la anterior, con Mayús), también las que están dentro de un grupo.</summary>
    void Cycle(int step)
    {
        var at = _current is null ? 0 : _docs.IndexOf(_current);
        Select(_docs[((at + step) % _docs.Count + _docs.Count) % _docs.Count]);
        FocusText();
    }

    /// <summary>Un cambio de líneas enteras (duplicar, mover, borrar), que se deshace con un solo Ctrl+Z.</summary>
    void EditLines(Func<TextDocument, int, int, (int Start, int Length)> edit)
    {
        if (_current is not { } doc) return;
        (int Start, int Length) selection;
        using (doc.Document.RunUpdate()) selection = edit(doc.Document, _editor.SelectionStart, _editor.SelectionLength);
        _editor.Select(selection.Start, selection.Length);
        _editor.TextArea.Caret.BringCaretToView();
    }

    void ToggleComment()
    {
        if (_current is not { } doc) return;
        if (EditorLanguages.Comment(doc.Language) is not { } comment)
        {
            Toast(L.T("{0} no tiene comentarios.", EditorLanguages.Label(doc.Language)));
            return;
        }
        EditLines((d, s, l) => EditorLines.ToggleComment(d, s, l, comment));
    }

    // ── Vista ───────────────────────────────────────────────────────────────────────────────

    void Build()
    {
        Children.Clear();
        Children.Add(Header());
        Children.Add(_body);
        Children.Add(StatusBar());
        BuildBars();
        _editor.Foreground = _ink.Text;
        _editor.LineNumbersForeground = _colors.LineNumber;
        _editor.TextArea.Caret.CaretBrush = _ink.Accent;
        _editor.TextArea.SelectionCornerRadius = 4;
        _editor.TextArea.SelectionBorder = null;
        _editor.TextArea.SelectionForeground = null;
        _editor.TextArea.TextView.CurrentLineBackground = _colors.CurrentLine;
        _editor.TextArea.TextView.CurrentLineBorder = null;
        _editor.TextArea.TextView.NonPrintableCharacterBrush = _colors.Marks;
        _marks.Colors = _colors;
        Show(_current);
        Refresh();
    }

    /// <summary>El archivo en el editor, con el aspecto de su modo; sin archivo, la pantalla de inicio.</summary>
    void Show(EditorDoc? doc)
    {
        if (doc is null)
        {
            HideBars();
            _body.Child = Start();
            UpdateStatus();
            return;
        }
        var view = _editor.TextArea.TextView;
        view.LineTransformers.Clear();
        view.ElementGenerators.Clear();
        if (_editor.Document != doc.Document) _editor.Document = doc.Document;
        var zoom = _zoom / 100.0;
        var options = _editor.Options;
        options.ConvertTabsToSpaces = !doc.Tabs;
        options.IndentationSize = doc.IndentSize;
        options.ShowSpaces = options.ShowTabs = options.ShowEndOfLine = _spaces;
        options.HighlightCurrentLine = doc.Code;
        _marks.Code = doc.Code;
        if (doc.Code)
        {
            _editor.ShowLineNumbers = true;
            _editor.WordWrap = _wrap;
            _editor.FontFamily = WithSymbols(Ink.Mono);
            _editor.FontSize = CodeSize * zoom;
            _editor.TextArea.SelectionBrush = _colors.SelectionCode;
            // Los números con aire a los lados, como en el diseño, y sin la línea punteada que los separa.
            foreach (var margin in _editor.TextArea.LeftMargins)
            {
                if (margin is AvaloniaEdit.Editing.LineNumberMargin numbers) numbers.Margin = new Thickness(12, 0, 18, 0);
                else if (margin is Avalonia.Controls.Shapes.Line line) line.IsVisible = false;
            }
            if (EditorLanguages.Definition(doc.Language) is { } definition)
                view.LineTransformers.Add(new ThemedColorizer(definition, _colors));
        }
        else
        {
            _editor.ShowLineNumbers = false;
            _editor.WordWrap = true;
            _editor.FontFamily = WithSymbols(Ink.Sans);
            _editor.FontSize = DocumentSize * zoom;
            _editor.TextArea.SelectionBrush = _colors.SelectionDoc;
            if (doc.Language == EditorLanguages.Markdown)
            {
                view.LineTransformers.Add(new MarkdownLook(_editor.TextArea, _ink, _colors, DocumentSize * zoom));
                view.ElementGenerators.Add(new MarkdownHider(_editor.TextArea));
            }
        }
        Pad();
        if (_body.Child != _stage) _body.Child = _stage;
        _editor.CaretOffset = Math.Min(doc.Caret, doc.Document.TextLength);
        var scroll = doc.Scroll;
        Dispatcher.UIThread.Post(() => _editor.ScrollToVerticalOffset(scroll), DispatcherPriority.Background);
        if (_findBar.IsVisible)
        {
            _marks.Current = -1;
            Search(keepPlace: true);
        }
        view.Redraw();
        UpdateStatus();
    }

    /// <summary>Vuelve a mostrar el que se ve, sin perder dónde estaba (al cambiar el lenguaje, el zoom o el ajuste).</summary>
    void Reshow()
    {
        if (_current is not { } doc) return;
        Remember();
        Show(doc);
    }

    /// <summary>
    /// La letra con las de emojis y símbolos detrás: lo pegado de otro lado (✓, →, ★, un emoji) se dibuja con ellas
    /// en vez de salir como un cuadro, si la letra del texto no lo tiene.
    /// </summary>
    static FontFamily WithSymbols(FontFamily family) =>
        new(string.Join(", ", family.FamilyNames) + ", Segoe UI Emoji, Segoe UI Symbol, Noto Color Emoji, Apple Color Emoji, Noto Sans Symbols, Noto Sans Symbols 2");

    void Pad()
    {
        var width = _editor.Bounds.Width;
        var column = Column * _zoom / 100.0;
        var side = _current is { Code: false } && width > column + 56 ? (width - column) / 2 : 28;
        _editor.Padding = _current is { Code: true } ? new Thickness(4, 10, 20, 0) : new Thickness(side, 28, side, 0);
    }

    Control Header()
    {
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(14, 0) };
        var files = Round(PanelIcon, L.T("Mostrar los archivos"), () => FilesToggled?.Invoke());
        bar.Children.Add(files);
        _tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(4, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var tabs = new ScrollViewer
        {
            Content = _tabs,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetColumn(tabs, 1);
        bar.Children.Add(tabs);

        _tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        _tools.Children.Add(Round(SearchIcon, L.T("Buscar · Ctrl+F"), () => ShowFind(replace: false)));
        Border? more = null;
        more = Round(MoreIcon, L.T("Más opciones"), () => ShowMore(more!), fill: true);
        _tools.Children.Add(more);
        Grid.SetColumn(_tools, 2);
        bar.Children.Add(_tools);

        _confirm = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        Grid.SetColumn(_confirm, 2);
        bar.Children.Add(_confirm);
        return bar;
    }

    void ShowMore(Control anchor)
    {
        List<DropList.Entry> entries =
        [
            new("new", L.T("Nuevo · Ctrl+N")),
            new("open", L.T("Abrir… · Ctrl+O")),
        ];
        if (_current is not null)
            entries.AddRange(
            [
                new("save", L.T("Guardar · Ctrl+S")),
                new("saveas", L.T("Guardar como… · Ctrl+Mayús+S")),
                new("replace", L.T("Buscar y reemplazar · Ctrl+H")),
                new("goto", L.T("Ir a la línea · Ctrl+G")),
                new("wrap", L.T("Ajustar las líneas · Alt+Z"), Selected: _wrap),
                new("spaces", L.T("Mostrar los espacios"), Selected: _spaces),
                new("close", L.T("Cerrar · Ctrl+W")),
            ]);
        DropList.Show(anchor, _ink, entries, picked =>
        {
            switch (picked)
            {
                case "new": New(); break;
                case "open": _ = OpenAsync(); break;
                case "save" when _current is { } doc: _ = SaveAsync(doc); break;
                case "saveas" when _current is { } doc: _ = SaveAsync(doc, asNew: true); break;
                case "replace": ShowFind(replace: true); break;
                case "goto": ShowGoTo(); break;
                case "wrap": ToggleWrap(); break;
                case "spaces": ToggleSpaces(); break;
                case "close" when _current is { } doc: Close(doc); break;
            }
        });
    }

    // ── Cambios de afuera ───────────────────────────────────────────────────────────────────

    /// <summary>Mira todos los abiertos (cada poco, mientras se ve el editor).</summary>
    internal void CheckDisk()
    {
        foreach (var doc in _docs.ToList()) CheckDisk(doc);
    }

    /// <summary>
    /// Si el archivo cambió afuera y aquí no hay nada sin guardar, se recarga solo; si hay, se pregunta (solo por el que se
    /// ve: los demás, al elegirlos). Mientras se pregunta otra cosa, se espera.
    /// </summary>
    void CheckDisk(EditorDoc doc)
    {
        if (doc.Path is not { } path) return;
        var check = TextFiles.Compare(path, doc.Disk);
        switch (check.Change)
        {
            case DiskChange.Same:
                doc.MissingChecks = 0;
                if (check.State is { } state) doc.Disk = state;
                break;
            case DiskChange.Changed:
                doc.MissingChecks = 0;
                if (!doc.Dirty) Reload(doc, check);
                else if (doc == _current && _afterConfirm is null) AskChanged(doc, check, saving: false, null);
                break;
            case DiskChange.Missing:
                if (++doc.MissingChecks >= 2 && doc == _current && _afterConfirm is null) AskMissing(doc);
                break;
        }
    }

    /// <summary>Pone lo que hay en el disco, donde estaba el cursor. Con Ctrl+Z vuelve lo de antes.</summary>
    void Reload(EditorDoc doc, DiskCheck check)
    {
        var showing = doc == _current;
        var (caret, scroll) = showing ? (_editor.CaretOffset, _editor.VerticalOffset) : (doc.Caret, doc.Scroll);
        doc.Document.Replace(0, doc.Document.TextLength, check.Text ?? "");
        doc.Document.UndoStack.MarkAsOriginalFile();
        doc.Encoding = check.Encoding;
        doc.EncodingChanged = false;
        doc.Disk = check.State;
        doc.Caret = Math.Min(caret, doc.Document.TextLength);
        if (showing)
        {
            _editor.CaretOffset = doc.Caret;
            Dispatcher.UIThread.Post(() => _editor.ScrollToVerticalOffset(scroll), DispatcherPriority.Background);
        }
        Refresh();
    }

    /// <summary>El archivo cambió afuera y aquí hay algo distinto: se elige qué queda; nada se pierde sin decidirlo.</summary>
    void AskChanged(EditorDoc doc, DiskCheck check, bool saving, Action? saved)
    {
        AskAbout(doc, L.T("«{0}» cambió fuera del editor.", doc.Title));
        _confirm.Children.Add(Pill(L.T("Recargar"), _ink.Pill, _ink.Text, () =>
        {
            EndConfirm(run: false);
            Reload(doc, check);
        }));
        _confirm.Children.Add(Pill(saving ? L.T("Sobrescribir") : L.T("Conservar lo mío"), _ink.Accent, new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x13)), async () =>
        {
            EndConfirm(run: false);
            doc.Disk = check.State;
            if (saving && await SaveAsync(doc, saved: saved)) saved?.Invoke();
        }));
    }

    /// <summary>El archivo ya no está (se borró o se movió). Lo de aquí puede ser la única copia: no se cierra solo.</summary>
    void AskMissing(EditorDoc doc)
    {
        AskAbout(doc, L.T("«{0}» ya no está en el disco.", doc.Title));
        _confirm.Children.Add(Pill(doc.Dirty ? L.T("Cerrar sin guardar") : L.T("Cerrar"), _ink.Pill, _ink.Text, () =>
        {
            EndConfirm(run: false);
            Remove(doc);
        }));
        _confirm.Children.Add(Pill(L.T("Conservar"), _ink.Accent, new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x13)), () =>
        {
            EndConfirm(run: false);
            doc.Disk = null;
            doc.MissingChecks = 0;
        }));
    }

    void AskAbout(EditorDoc doc, string text)
    {
        _asking = doc;
        _afterConfirm = () => { };
        _confirm.Children.Clear();
        var question = Ui.Text(text, 13, _ink.Secondary);
        question.Margin = new Thickness(0, 0, 4, 0);
        question.MaxWidth = 340;
        ToolTip.SetTip(question, text);
        _confirm.Children.Add(question);
        _confirm.IsVisible = true;
        _tools.IsVisible = false;
    }

    /// <summary>«¿Guardar los cambios de x?» en la cabecera, en lugar de los botones; then corre si se guardó o se descartó.</summary>
    void AskSave(EditorDoc doc, Action then)
    {
        _afterConfirm = then;
        _confirm.Children.Clear();
        var question = Ui.Text(L.T("¿Guardar los cambios de «{0}»?", doc.Title), 13, _ink.Secondary);
        question.Margin = new Thickness(0, 0, 4, 0);
        question.MaxWidth = 340;
        question.TextTrimming = TextTrimming.CharacterEllipsis;
        _confirm.Children.Add(question);
        _confirm.Children.Add(Pill(L.T("Cancelar"), _ink.Pill, _ink.Text, () => EndConfirm(run: false)));
        _confirm.Children.Add(Pill(L.T("No guardar"), _ink.Pill, _ink.Text, () => EndConfirm(run: true)));
        _confirm.Children.Add(Pill(L.T("Guardar"), _ink.Accent, new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x13)), async () =>
        {
            if (await SaveAsync(doc, saved: then)) EndConfirm(run: true);
        }));
        _confirm.IsVisible = true;
        _tools.IsVisible = false;
    }

    void EndConfirm(bool run)
    {
        _asking = null;
        _confirm.IsVisible = false;
        var then = _afterConfirm;
        _afterConfirm = null;
        Refresh();
        if (run) then?.Invoke();
    }

    /// <summary>Un aviso corto en la cabecera (no se pudo abrir o guardar), que se va solo.</summary>
    void Notice(string text)
    {
        _confirm.Children.Clear();
        var message = Ui.Text(text, 13, _ink.Danger);
        message.MaxWidth = 520;
        message.TextTrimming = TextTrimming.CharacterEllipsis;
        _confirm.Children.Add(message);
        _confirm.IsVisible = true;
        _tools.IsVisible = false;
        DispatcherTimer.RunOnce(() =>
        {
            if (_afterConfirm is not null) return; // ya se está preguntando otra cosa
            _confirm.IsVisible = false;
            Refresh();
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>Sin archivos abiertos: escribir algo nuevo, abrir uno o seguir con uno reciente.</summary>
    Control Start()
    {
        var panel = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 90, 0, 0), MaxWidth = 460 };
        panel.Children.Add(new AppLogo { Width = 44, Height = 44, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock
        {
            Text = L.T("Escriba algo nuevo o abra un archivo"),
            FontFamily = Ink.Display,
            FontSize = 21,
            FontWeight = FontWeight.SemiBold,
            Foreground = _ink.Text,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 10) };
        buttons.Children.Add(StartButton(NewFileIcon, L.T("Texto nuevo"), _ink.Accent, new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x13)), New));
        buttons.Children.Add(StartButton(OpenFolderIcon, L.T("Abrir…"), _ink.Pill, _ink.Text, () => _ = OpenAsync()));
        panel.Children.Add(buttons);
        var recents = _recents.Where(File.Exists).Take(6).ToList();
        if (recents.Count > 0)
        {
            panel.Children.Add(Ui.Text(L.T("Recientes"), 12, _ink.Tertiary));
            foreach (var path in recents)
                panel.Children.Add(FileRow(path, () => Open(path)));
        }
        return new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>Una fila con el nombre del archivo y su carpeta (la pantalla de inicio y la lista de archivos).</summary>
    public Border FileRow(string path, Action open, bool current = false, bool dirty = false)
    {
        var stack = new StackPanel { Spacing = 2 };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new TextBlock
        {
            Text = System.IO.Path.GetFileName(path),
            FontFamily = Ink.Sans,
            FontSize = 14,
            Foreground = _ink.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (dirty)
            title.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = _ink.Accent, VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(title);
        stack.Children.Add(new TextBlock
        {
            Text = (System.IO.Path.GetDirectoryName(path) ?? "").Replace(Account.Home, "~"),
            FontFamily = Ink.Sans,
            FontSize = 12.5,
            Foreground = _ink.Tertiary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var row = Ui.Press(stack, open, current ? null : (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, System.IO.Path.GetFileName(path));
        row.Padding = new Thickness(12, 8);
        row.CornerRadius = new CornerRadius(12);
        if (current) row.Background = _ink.Pill;
        return row;
    }

    // ── Piezas ──────────────────────────────────────────────────────────────────────────────

    static Viewbox Glyph(string path, IBrush brush, double size, bool fill = false) => new()
    {
        Width = size,
        Height = size,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(path),
            Stroke = fill ? null : brush,
            Fill = fill ? brush : null,
            StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 24,
            Height = 24,
        },
    };

    Border Round(string path, string name, Action onClick, bool fill = false)
    {
        var button = Ui.Press(Glyph(path, _ink.Secondary, 17, fill), onClick, (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, name);
        button.Width = 34;
        button.Height = 34;
        button.CornerRadius = new CornerRadius(999);
        ToolTip.SetTip(button, name);
        return button;
    }

    /// <summary>Un botón chico de ícono (buscar, los grupos): 28 de lado, con su descripción al pasar el puntero.</summary>
    Border Small(string path, string name, Action onClick, double size = 15)
    {
        var button = Ui.Press(Glyph(path, _ink.Tertiary, size), onClick, (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, name);
        button.Width = 28;
        button.Height = 28;
        button.CornerRadius = new CornerRadius(8);
        ToolTip.SetTip(button, name);
        return button;
    }

    /// <summary>Un botón redondo de la pantalla de inicio: el ícono, y lo que hace al pasar el puntero.</summary>
    static Border StartButton(string path, string name, IBrush background, IBrush foreground, Action onClick)
    {
        var icon = new Viewbox
        {
            Width = 20,
            Height = 20,
            Child = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(path),
                Stroke = foreground,
                StrokeThickness = 1.8,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                Width = 24,
                Height = 24,
            },
        };
        var button = Ui.Press(icon, onClick, (b, on) => b.Opacity = on ? 0.86 : 1, name);
        button.Background = background;
        button.Width = 44;
        button.Height = 44;
        button.CornerRadius = new CornerRadius(999);
        ToolTip.SetTip(button, name);
        return button;
    }

    Border Pill(string text, IBrush background, IBrush foreground, Action onClick)
    {
        var pill = Ui.Press(Ui.Text(text, 13, foreground, FontWeight.SemiBold), onClick, null, text);
        pill.Background = background;
        pill.Height = 32;
        pill.Padding = new Thickness(14, 0);
        pill.CornerRadius = new CornerRadius(999);
        return pill;
    }

    /// <summary>Una tarjeta flotante (buscar, ir a la línea, la lista de un grupo), como las listas de la Console.</summary>
    Border Card(Control child, double radius = 14) => new()
    {
        Child = child,
        Background = _ink.Raised,
        BorderBrush = _ink.Hairline,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(radius),
        Padding = new Thickness(6),
        BoxShadow = BoxShadows.Parse(_ink.Dark ? "0 12 32 0 #47000000" : "0 12 32 0 #24000000"),
    };

    /// <summary>Una caja de texto sin el borde ni el fondo del tema, dentro de una píldora.</summary>
    (Border Pill, TextBox Box) Field(string hint, double width)
    {
        var box = new TextBox
        {
            Watermark = hint,
            FontSize = 13,
            FontFamily = Ink.Sans,
            Foreground = _ink.Text,
            CaretBrush = _ink.Accent,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            MinHeight = 0,
            VerticalContentAlignment = VerticalAlignment.Center,
            SelectionBrush = _colors.SelectionCode,
        };
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
            box.Resources[key] = Brushes.Transparent;
        foreach (var key in new[] { "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
            box.Resources[key] = Brushes.Transparent;
        box.Resources["TextControlBorderThemeThickness"] = new Thickness(0);
        box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        foreach (var key in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" })
            box.Resources[key] = _ink.Text;
        foreach (var key in new[] { "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused" })
            box.Resources[key] = _ink.Tertiary;
        var pill = new Border { Child = box, Background = _ink.Pill, CornerRadius = new CornerRadius(999), Height = 30, Width = width, Padding = new Thickness(12, 0) };
        return (pill, box);
    }

    /// <summary>Un aviso chico abajo, al centro, que se va solo («12 reemplazos»).</summary>
    void Toast(string text)
    {
        var toast = new Border
        {
            Child = Ui.Text(text, 13, _ink.Text),
            Background = _ink.Raised,
            BorderBrush = _ink.Hairline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(14, 7),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 14),
            IsHitTestVisible = false,
        };
        _stage.Children.Add(toast);
        DispatcherTimer.RunOnce(() => _stage.Children.Remove(toast), TimeSpan.FromSeconds(2));
    }
}
