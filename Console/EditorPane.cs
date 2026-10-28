namespace Claupet;

using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;

/// <summary>Un archivo abierto en el editor: su texto (con su historial de deshacer), dónde está y cómo se muestra.</summary>
sealed class EditorDoc
{
    public string? Path { get; set; }
    public required TextDocument Document { get; init; }
    /// <summary>Código: números de línea y colores; si no, documento (Markdown sin marcas fuera de la línea del cursor).</summary>
    public bool Code { get; set; }
    public int Caret { get; set; }
    public double Scroll { get; set; }
    public string Title => Path is null ? L.T("Sin título") : System.IO.Path.GetFileName(Path);
    public bool Dirty => !Document.UndoStack.IsOriginalFile;

    static readonly string[] DocumentTypes = [".md", ".markdown", ".txt", ".text", ""];

    /// <summary>Markdown y texto se abren como documento; lo demás, como código.</summary>
    public static bool IsCode(string? path) => !DocumentTypes.Contains(System.IO.Path.GetExtension(path ?? "").ToLowerInvariant());
}

/// <summary>Los archivos abiertos hace poco en el editor, en editor-recientes.json junto a los ajustes (no es un ajuste: lo guarda la Console).</summary>
static class EditorRecents
{
    static string FilePath => DataFolder.In("editor-recientes.json");

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
/// El editor de Claupet Console, como en el diseño: arriba, el botón de los archivos, las pestañas (con un punto si hay
/// cambios sin guardar), Documento o Código y más opciones; debajo, el texto. AvaloniaEdit es solo el motor: el aspecto
/// (Markdown sin marcas fuera de la línea del cursor, los colores del código) lo pone EditorLook.
/// </summary>
sealed class EditorPane : Grid
{
    /// <summary>Se pidió mostrar u ocultar la lista de archivos (la columna de la izquierda).</summary>
    public event Action? FilesToggled;
    /// <summary>Cambió algo de la lista: se abrió, cerró, guardó o eligió un archivo.</summary>
    public event Action? DocsChanged;

    const string PanelIcon = "M6.5,4.5 H17.5 A3,3 0 0 1 20.5,7.5 V16.5 A3,3 0 0 1 17.5,19.5 H6.5 A3,3 0 0 1 3.5,16.5 V7.5 A3,3 0 0 1 6.5,4.5 Z M9,4.5 V19.5";
    const string MoreIcon = "M3.4,12 A1.6,1.6 0 1 0 6.6,12 A1.6,1.6 0 1 0 3.4,12 Z M10.4,12 A1.6,1.6 0 1 0 13.6,12 A1.6,1.6 0 1 0 10.4,12 Z "
        + "M17.4,12 A1.6,1.6 0 1 0 20.6,12 A1.6,1.6 0 1 0 17.4,12 Z";
    const double DocumentSize = 16.5, CodeSize = 13.5, Column = 640;

    readonly List<EditorDoc> _docs = [];
    readonly TextEditor _editor = new();
    readonly Border _body = new();
    Ink _ink = Ink.For(false);
    EditorInk _colors = EditorInk.For(Ink.For(false));
    EditorDoc? _current;
    List<string> _recents = EditorRecents.Load();
    StackPanel _tabs = new();
    StackPanel _tools = new();
    StackPanel _confirm = new();
    Border _documentTab = new(), _codeTab = new();
    /// <summary>Lo que corre al contestar «¿Guardar los cambios?» (para cerrar un archivo o la ventana).</summary>
    Action? _afterConfirm;

    public EditorPane()
    {
        RowDefinitions = new RowDefinitions("56,*");
        _editor.Options.HighlightCurrentLine = false;
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.Options.ConvertTabsToSpaces = true;
        _editor.Options.IndentationSize = 4;
        _editor.Options.AllowScrollBelowDocument = true;
        _editor.Background = Brushes.Transparent;
        _editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        // En el modo Documento el texto va en una columna de 640 al centro, con la barra al borde.
        _editor.SizeChanged += (_, _) => Pad();
        // Las marcas de Markdown se muestran solo en la línea del cursor: al cambiar de línea, se redibuja.
        var lastLine = 0;
        _editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (_current is { Code: false } && _editor.TextArea.Caret.Line != lastLine) _editor.TextArea.TextView.Redraw();
            lastLine = _editor.TextArea.Caret.Line;
        };
        Grid.SetRow(_body, 1);
        Build();
    }

    public IReadOnlyList<EditorDoc> Docs => _docs;
    public EditorDoc? Current => _current;
    public IReadOnlyList<string> Recents => _recents;
    public bool HasUnsaved => _docs.Any(d => d.Dirty);

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
        var doc = new EditorDoc { Document = new TextDocument(), Code = false };
        doc.Document.UndoStack.MarkAsOriginalFile();
        Add(doc);
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
        try
        {
            if (new FileInfo(path).Length > 20 * 1024 * 1024)
            {
                Notice(L.T("«{0}» es demasiado grande para el editor (más de 20 MB).", System.IO.Path.GetFileName(path)));
                return;
            }
            var doc = new EditorDoc { Path = path, Document = new TextDocument(File.ReadAllText(path)), Code = EditorDoc.IsCode(path) };
            doc.Document.UndoStack.MarkAsOriginalFile();
            _recents = EditorRecents.Add(path);
            Add(doc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notice(L.T("No se pudo abrir «{0}»: {1}", System.IO.Path.GetFileName(path), ex.Message));
        }
    }

    /// <summary>Guarda el archivo (si no tiene lugar todavía, pregunta dónde). true si quedó guardado.</summary>
    public async Task<bool> SaveAsync(EditorDoc doc, bool asNew = false)
    {
        var path = doc.Path;
        if (path is null || asNew)
        {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return false;
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = L.T("Guardar como"),
                SuggestedFileName = doc.Path is null ? L.T("Documento") + ".md" : System.IO.Path.GetFileName(doc.Path),
                DefaultExtension = doc.Code ? null : "md",
                ShowOverwritePrompt = true,
            });
            if (file?.TryGetLocalPath() is not { } chosen) return false;
            path = chosen;
        }
        try
        {
            // Primero a un archivo de al lado y después se cambia: un corte a mitad no deja el archivo a medias.
            var temp = path + ".claupet-tmp";
            File.WriteAllText(temp, doc.Document.Text, new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
            doc.Path = path;
            doc.Document.UndoStack.MarkAsOriginalFile();
            _recents = EditorRecents.Add(path);
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
        if (_current is { } previous)
        {
            previous.Caret = _editor.CaretOffset;
            previous.Scroll = _editor.VerticalOffset;
        }
        _current = doc;
        Show(doc);
        Refresh();
    }

    void Add(EditorDoc doc)
    {
        _docs.Add(doc);
        doc.Document.UndoStack.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UndoStack.IsOriginalFile)) Dispatcher.UIThread.Post(Refresh);
        };
        Select(doc);
        FocusText();
    }

    void Remove(EditorDoc doc, bool select = true)
    {
        var at = _docs.IndexOf(doc);
        _docs.Remove(doc);
        if (_current == doc)
        {
            _current = null;
            if (select && _docs.Count > 0) Select(_docs[Math.Min(at, _docs.Count - 1)]);
            else Show(null);
        }
        Refresh();
    }

    // ── Teclas ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Los atajos del editor (los manda la ventana mientras se ve el editor).</summary>
    public void HandleKey(KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case Key.N: New(); break;
            case Key.O: _ = OpenAsync(); break;
            case Key.S when _current is { } doc: _ = SaveAsync(doc, asNew: shift); break;
            case Key.W when _current is { } doc: Close(doc); break;
            default: return;
        }
        e.Handled = true;
    }

    // ── Vista ───────────────────────────────────────────────────────────────────────────────

    void Build()
    {
        Children.Clear();
        Children.Add(Header());
        Children.Add(_body);
        _editor.Foreground = _ink.Text;
        _editor.LineNumbersForeground = _colors.LineNumber;
        _editor.TextArea.Caret.CaretBrush = _ink.Accent;
        _editor.TextArea.SelectionCornerRadius = 4;
        _editor.TextArea.SelectionBorder = null;
        _editor.TextArea.SelectionForeground = null;
        Show(_current);
        Refresh();
    }

    /// <summary>El archivo en el editor, con el aspecto de su modo; sin archivo, la pantalla de inicio.</summary>
    void Show(EditorDoc? doc)
    {
        if (doc is null)
        {
            _body.Child = Start();
            return;
        }
        var view = _editor.TextArea.TextView;
        view.LineTransformers.Clear();
        view.ElementGenerators.Clear();
        _editor.Document = doc.Document;
        if (doc.Code)
        {
            _editor.ShowLineNumbers = true;
            _editor.WordWrap = false;
            _editor.FontFamily = Ink.Mono;
            _editor.FontSize = CodeSize;
            _editor.TextArea.SelectionBrush = _colors.SelectionCode;
            // Los números con aire a los lados, como en el diseño, y sin la línea punteada que los separa.
            foreach (var margin in _editor.TextArea.LeftMargins)
            {
                if (margin is AvaloniaEdit.Editing.LineNumberMargin numbers) numbers.Margin = new Thickness(12, 0, 18, 0);
                else if (margin is Avalonia.Controls.Shapes.Line line) line.IsVisible = false;
            }
            if (HighlightingManager.Instance.GetDefinitionByExtension(System.IO.Path.GetExtension(doc.Path ?? "")) is { } definition)
                view.LineTransformers.Add(new ThemedColorizer(definition, _colors));
        }
        else
        {
            _editor.ShowLineNumbers = false;
            _editor.WordWrap = true;
            _editor.FontFamily = Ink.Sans;
            _editor.FontSize = DocumentSize;
            _editor.TextArea.SelectionBrush = _colors.SelectionDoc;
            view.LineTransformers.Add(new MarkdownLook(_editor.TextArea, _ink, _colors, DocumentSize));
            view.ElementGenerators.Add(new MarkdownHider(_editor.TextArea));
        }
        Pad();
        if (_body.Child != _editor) _body.Child = _editor;
        _editor.CaretOffset = Math.Min(doc.Caret, doc.Document.TextLength);
        var scroll = doc.Scroll;
        Dispatcher.UIThread.Post(() => _editor.ScrollToVerticalOffset(scroll), DispatcherPriority.Background);
        view.Redraw();
    }

    void Pad()
    {
        var width = _editor.Bounds.Width;
        var side = _current is { Code: false } && width > Column + 56 ? (width - Column) / 2 : 28;
        _editor.Padding = _current is { Code: true } ? new Thickness(4, 10, 20, 0) : new Thickness(side, 28, side, 0);
    }

    /// <summary>Las pestañas, el modo y el punto de «sin guardar», según lo de ahora.</summary>
    void Refresh()
    {
        _tabs.Children.Clear();
        foreach (var doc in _docs)
        {
            var current = doc == _current;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var title = Ui.Text(doc.Title, 13, current ? _ink.Text : _ink.Tertiary);
            title.MaxWidth = 180;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Children.Add(title);
            if (doc.Dirty)
            {
                var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = _ink.Accent, VerticalAlignment = VerticalAlignment.Center };
                ToolTip.SetTip(dot, L.T("Sin guardar"));
                row.Children.Add(dot);
            }
            var tab = Ui.Press(row, () => Select(doc), current ? null : (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, doc.Title);
            tab.Height = 32;
            tab.Padding = new Thickness(14, 0);
            tab.CornerRadius = new CornerRadius(999);
            if (current) tab.Background = _ink.Pill;
            ToolTip.SetTip(tab, doc.Path ?? doc.Title);
            _tabs.Children.Add(tab);
        }
        var code = _current?.Code == true;
        _documentTab.Background = _current is not null && !code ? _ink.Raised : Brushes.Transparent;
        _codeTab.Background = code ? _ink.Raised : Brushes.Transparent;
        _tools.IsVisible = _current is not null && !_confirm.IsVisible;
        if (_current is null && _body.Child == _editor) _body.Child = Start();
        DocsChanged?.Invoke();
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

        _tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var modes = new StackPanel { Orientation = Orientation.Horizontal };
        _documentTab = ModeTab(L.T("Documento"), code: false);
        _codeTab = ModeTab(L.T("Código"), code: true);
        modes.Children.Add(_documentTab);
        modes.Children.Add(_codeTab);
        _tools.Children.Add(new Border { Child = modes, Background = _ink.Pill, CornerRadius = new CornerRadius(999), Padding = new Thickness(3) });
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

    Border ModeTab(string text, bool code)
    {
        var tab = Ui.Press(Ui.Text(text, 12.5, _ink.Text), () =>
        {
            if (_current is not { } doc || doc.Code == code) return;
            doc.Code = code;
            doc.Caret = _editor.CaretOffset;
            doc.Scroll = _editor.VerticalOffset;
            Show(doc);
            Refresh();
        }, null, text);
        tab.Height = 28;
        tab.Padding = new Thickness(14, 0);
        tab.CornerRadius = new CornerRadius(999);
        return tab;
    }

    void ShowMore(Control anchor)
    {
        List<DropList.Entry> entries =
        [
            new("new", L.T("Nuevo · Ctrl+N")),
            new("open", L.T("Abrir… · Ctrl+O")),
        ];
        if (_current is not null)
            entries.AddRange([new("save", L.T("Guardar · Ctrl+S")), new("saveas", L.T("Guardar como… · Ctrl+Mayús+S")), new("close", L.T("Cerrar · Ctrl+W"))]);
        DropList.Show(anchor, _ink, entries, picked =>
        {
            switch (picked)
            {
                case "new": New(); break;
                case "open": _ = OpenAsync(); break;
                case "save" when _current is { } doc: _ = SaveAsync(doc); break;
                case "saveas" when _current is { } doc: _ = SaveAsync(doc, asNew: true); break;
                case "close" when _current is { } doc: Close(doc); break;
            }
        });
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
            if (await SaveAsync(doc)) EndConfirm(run: true);
        }));
        _confirm.IsVisible = true;
        _tools.IsVisible = false;
    }

    void EndConfirm(bool run)
    {
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
        panel.Children.Add(new HeadLogo { Width = 44, Height = 44, HorizontalAlignment = HorizontalAlignment.Center });
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
        buttons.Children.Add(Pill(L.T("Documento nuevo"), _ink.Accent, new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x13)), New));
        buttons.Children.Add(Pill(L.T("Abrir…"), _ink.Pill, _ink.Text, () => _ = OpenAsync()));
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

    Border Round(string path, string name, Action onClick, bool fill = false)
    {
        var icon = new Viewbox
        {
            Width = 17,
            Height = 17,
            Child = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(path),
                Stroke = fill ? null : _ink.Secondary,
                Fill = fill ? _ink.Secondary : null,
                StrokeThickness = 1.7,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                Width = 24,
                Height = 24,
            },
        };
        var button = Ui.Press(icon, onClick, (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, name);
        button.Width = 34;
        button.Height = 34;
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
}
