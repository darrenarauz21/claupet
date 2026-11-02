namespace Claupet;

using System.Text;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

/// <summary>
/// Claupet Canvas en la Console: el documento vivo que la persona y los agentes (por MCP, CanvasMcp) editan a la vez.
/// Como un documento, sin tarjetas: los controles de cada sección aparecen al pasar el mouse (a la izquierda, su tipo y
/// el asa para moverla; arriba, editar, pedir a la IA, comentar y más), entre sección y sección un «+» inserta otra, y a
/// la derecha va quién la escribió y sus comentarios. Lo que cambia un agente se dibuja solo y se marca con su color.
/// Arriba, el título, la versión, quiénes editan y los botones; abajo, la línea de las versiones.
/// </summary>
sealed partial class CanvasPane : Grid
{
    /// <summary>Cambió la lista de canvas (uno nuevo, otro título, otra versión): la columna de la izquierda se redibuja.</summary>
    public event Action? ListChanged;
    /// <summary>Un agente pidió mostrar un canvas: la Console pasa a Canvas.</summary>
    public event Action? OpenRequested;
    /// <summary>Se pidió mostrar u ocultar la lista de canvas.</summary>
    public event Action? ListToggled;

    readonly CanvasStore _store;
    readonly CanvasWatcher? _watcher;
    readonly Func<string, string, Control> _markdown;
    /// <summary>Manda un pedido a un agente de la Terminal (texto, carpeta). Null en las pruebas.</summary>
    readonly Func<string, string?, bool>? _askAgent;
    readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    readonly Border _bar = new();
    readonly Border _timeline = new();
    readonly Border _viewing = new();
    readonly Border _toast = new();
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(1.8) };
    readonly TextBox _search = new();
    readonly StackPanel _listView = new() { Spacing = 2 };
    Ink _ink = Ink.For(false);
    IReadOnlyList<CanvasSummary> _list = [];
    CanvasDoc? _doc;
    /// <summary>Una versión anterior que se está mirando (solo lectura), o null.</summary>
    CanvasDoc? _past;
    bool _history;
    /// <summary>La sección que se está editando y su caja (se conserva al redibujar, si otro cambia otra sección).</summary>
    string? _editing;
    TextBox? _editor;
    DateTimeOffset _editingSince;
    bool _renamingTitle;
    /// <summary>Lo que cambió otro hace un momento: la sección y el color de quien la cambió.</summary>
    readonly Dictionary<string, string?> _flash = [];
    /// <summary>Quién editó hace un momento (su cara late arriba).</summary>
    readonly Dictionary<string, DateTime> _busy = [];
    /// <summary>Las secciones ya mostradas: solo las nuevas entran con animación.</summary>
    readonly HashSet<string> _seen = [];
    /// <summary>La vista elegida de cada sección que tiene varias (página o código, tabla o gráfico…). No se guarda.</summary>
    readonly Dictionary<string, string> _views = [];
    int _shownVersion;

    public CanvasPane(Func<string, string, Control> markdown, CanvasStore? store = null, Func<string, string?, bool>? askAgent = null)
    {
        _store = store ?? CanvasStore.Default;
        _markdown = markdown;
        _askAgent = askAgent;
        RowDefinitions = new RowDefinitions("Auto,*");
        ClipToBounds = true;
        Grid.SetRow(_scroll, 1);
        Children.Add(_bar);
        Children.Add(_scroll);
        foreach (var overlay in new[] { _timeline, _viewing, _toast })
        {
            Grid.SetRow(overlay, 1);
            Children.Add(overlay);
        }
        _scroll.ScrollChanged += (_, _) => PaintBarLine();
        // Un clic fuera de la sección que se edita la guarda.
        _scroll.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (_editing is { } id && e.Source is Visual v && !IsInside(v, id)) SaveEditing();
        }, RoutingStrategies.Tunnel);
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ShowOverlay(_toast, false); };
        _glowTimer.Tick += (_, _) => Calm();
        _busyTimer.Tick += (_, _) => Rest();
        _bumpTimer.Tick += (_, _) => { _bumpTimer.Stop(); if (_versionPill is not null) _versionPill.RenderTransform = TransformOperations.Parse("scale(1)"); };
        _search.TextChanged += (_, _) => RenderList();
        try
        {
            _watcher = new CanvasWatcher(_store);
            _watcher.Changed += id => Dispatcher.UIThread.Post(() => OnDiskChanged(id));
            _watcher.OpenRequested += () => Dispatcher.UIThread.Post(CheckOpenRequest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { ErrorLog.Write(ex); }
        Reload();
        _doc = _list.Count > 0 ? _store.Load(_list[0].Id) : null;
        Render();
    }

    public IReadOnlyList<CanvasSummary> List => _list;
    public string? CurrentId => _doc?.Id;

    public void SetInk(Ink ink)
    {
        _ink = ink;
        Render(keepScroll: true);
    }

    /// <summary>Un canvas que un agente pidió mostrar (al abrirse la Console o mientras está abierta).</summary>
    public void CheckOpenRequest()
    {
        if (_store.TakeOpenRequest() is not { } id) return;
        Reload();
        Select(id);
        OpenRequested?.Invoke();
    }

    public void Select(string id)
    {
        if (_doc?.Id == id) return;
        CancelEditing();
        LeavePast();
        _doc = _store.Load(id);
        _seen.Clear();
        _views.Clear();
        _scroll.Offset = default;
        Render();
        ListChanged?.Invoke();
    }

    /// <summary>Un canvas nuevo, con una sección de texto lista para escribir.</summary>
    public void New()
    {
        try
        {
            var doc = _store.Create(L.T("Canvas sin título"), CanvasJson.User, [new CanvasOp("insert", Kind: CanvasKinds.Markdown, Content: "")]);
            Reload();
            LeavePast();
            _doc = doc;
            _seen.Clear();
            StartEditing(doc.Sections[0]);
            _renamingTitle = true;
            Render();
            ListChanged?.Invoke();
        }
        catch (CanvasException ex) { Toast(ex.Message, CanvasIcons.Warn); }
    }

    /// <summary>Los atajos mientras se ve Canvas: Ctrl+K (comandos), Ctrl+N (nuevo), Ctrl+J (pedir a la IA).</summary>
    public void HandleKey(KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        switch (e.Key)
        {
            case Key.K: Palette(); break;
            case Key.N: New(); break;
            case Key.J when _doc is not null: AskPopup(_bar, null); break;
            default: return;
        }
        e.Handled = true;
    }

    void Reload() => _list = _store.List();

    void OnDiskChanged(string id)
    {
        var before = _list;
        Reload();
        if (_doc is { } doc && doc.Id == id)
        {
            var fresh = _store.Load(id);
            if (fresh is null)
            {
                CancelEditing();
                _doc = _list.Count > 0 ? _store.Load(_list[0].Id) : null;
                Render();
            }
            else if (fresh.Version != doc.Version)
            {
                // Lo que tocaron otros desde la versión que se veía: se marca con su color y su cara late.
                foreach (var change in fresh.Changes.Where(c => c.Version > doc.Version && c.Author != CanvasJson.User))
                {
                    foreach (var section in change.Sections) _flash[section] = change.Author;
                    if (change.Author is { } author) _busy[author] = DateTime.UtcNow;
                }
                if (_editing is { } editing && fresh.Section(editing) is { } s && s.Updated > _editingSince && s.Author != CanvasJson.User)
                    Toast(L.T("{0} también cambió esta sección", CanvasJson.AuthorLabel(s.Author)), CanvasIcons.Warn);
                _doc = fresh;
                Render(keepScroll: true);
                ScheduleCalm();
            }
        }
        else if (_doc is null && _list.Count > 0)
        {
            _doc = _store.Load(_list[0].Id);
            Render();
        }
        if (!before.SequenceEqual(_list)) ListChanged?.Invoke();
        else RenderList();
    }

    /// <summary>Al rato, las marcas se apagan (con transición) y las caras dejan de latir.</summary>
    void ScheduleCalm()
    {
        _glowTimer.Stop();
        _glowTimer.Start();
        _busyTimer.Stop();
        _busyTimer.Start();
    }

    readonly DispatcherTimer _glowTimer = new() { Interval = TimeSpan.FromSeconds(2.4) };
    readonly DispatcherTimer _busyTimer = new() { Interval = TimeSpan.FromSeconds(6) };

    void Calm()
    {
        _glowTimer.Stop();
        _flash.Clear();
        foreach (var glow in this.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is "glow")) glow.Opacity = 0;
    }

    void Rest()
    {
        _busyTimer.Stop();
        foreach (var key in _busy.Where(p => DateTime.UtcNow - p.Value > TimeSpan.FromSeconds(5)).Select(p => p.Key).ToList()) _busy.Remove(key);
        _bar.Child = Bar();
        RenderList();
    }

    /// <summary>Al cerrar la ventana: deja de mirar la carpeta y apaga los temporizadores.</summary>
    public void Close()
    {
        _watcher?.Dispose();
        _glowTimer.Stop();
        _busyTimer.Stop();
        _toastTimer.Stop();
        _bumpTimer.Stop();
        ClosePopover();
    }

    // ── Cambios de la persona ───────────────────────────────────────────────────────────

    /// <summary>Aplica operaciones como «user»; un error sale en un aviso, sin perder lo que se escribía.</summary>
    bool Apply(params CanvasOp[] ops)
    {
        if (_doc is null || _past is not null) return false;
        try
        {
            _doc = _store.Apply(_doc.Id, ops, CanvasJson.User).Doc;
            Reload();
            ListChanged?.Invoke();
            return true;
        }
        catch (CanvasException ex)
        {
            Toast(ex.Message, CanvasIcons.Warn);
            return false;
        }
    }

    void StartEditing(CanvasSection section)
    {
        if (_past is not null) return;
        if (_editing is not null && _editing != section.Id) SaveEditing();
        _editing = section.Id;
        _editingSince = section.Updated;
        var prose = section.Kind == CanvasKinds.Markdown;
        _editor = new TextBox
        {
            Text = section.Content,
            AcceptsReturn = true,
            AcceptsTab = !prose,
            TextWrapping = prose ? TextWrapping.Wrap : TextWrapping.NoWrap,
            FontFamily = prose ? Ink.Sans : Ink.Mono,
            FontSize = prose ? 14.5 : 13,
            MinHeight = 90,
            MaxHeight = 620,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Watermark = section.Kind switch
            {
                CanvasKinds.Markdown => L.T("Escriba…"),
                CanvasKinds.Image => "https://… · C:\\… · data:image/…",
                _ => "",
            },
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_editor, ScrollBarVisibility.Auto);
        _editor.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Enter or Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { SaveEditing(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelEditing(); Render(keepScroll: true); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        Render(keepScroll: true);
        Dispatcher.UIThread.Post(() => _editor?.Focus(), DispatcherPriority.Background);
    }

    void SaveEditing()
    {
        if (_editing is not { } id || _editor is null) return;
        var text = _editor.Text ?? "";
        var changed = _doc?.Section(id) is { } s && s.Content != text;
        CancelEditing();
        if (changed) Apply(new CanvasOp("replace", Id: id, Content: text));
        Render(keepScroll: true);
    }

    void CancelEditing()
    {
        _editing = null;
        _editor = null;
    }

    bool IsInside(Visual visual, string sectionId)
    {
        for (Visual? v = visual; v is not null; v = v.GetVisualParent())
            if (v is Control { Tag: string tag } && tag == "blk:" + sectionId) return true;
        // Lo que está en una ventana emergente (un menú) no cuenta como «afuera».
        return visual.GetVisualRoot() is PopupRoot;
    }

    /// <summary>Un plan de partida: una etapa y dos tareas, para ver el resumen, el tablero y el cronograma de una vez.</summary>
    static string PlanTemplate() => new System.Text.Json.Nodes.JsonObject
    {
        ["claupet"] = Plan.Format,
        ["titulo"] = L.T("Plan nuevo"),
        ["inicio"] = DateTime.Today.ToString("yyyy-MM-dd"),
        ["etapas"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["id"] = "e1", ["nombre"] = L.T("Etapa 1") }),
        ["tareas"] = new System.Text.Json.Nodes.JsonArray(
            new System.Text.Json.Nodes.JsonObject { ["id"] = "t1", ["titulo"] = L.T("Primera tarea"), ["etapa"] = "e1", ["dias"] = 2, ["estado"] = "en-curso" },
            new System.Text.Json.Nodes.JsonObject { ["id"] = "t2", ["titulo"] = L.T("Segunda tarea"), ["etapa"] = "e1", ["dias"] = 3, ["depende"] = new System.Text.Json.Nodes.JsonArray("t1") }),
    }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";

    void AddSection(string kind, string? after, string? view = null)
    {
        var language = kind switch { CanvasKinds.Diagram => "mermaid", CanvasKinds.Data => view == "chart" ? "csv" : "json", CanvasKinds.Plan => "json", _ => null };
        var content = kind switch
        {
            CanvasKinds.Html => "<!doctype html>\n<html>\n<body style=\"font-family:system-ui;display:grid;place-items:center;height:100vh;margin:0\">\n  <h1>Hola</h1>\n</body>\n</html>\n",
            CanvasKinds.Diagram => "graph LR\n  A[Idea] --> B[Diseño] --> C[Listo]\n",
            CanvasKinds.Svg => "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 200 100\">\n  <circle cx=\"50\" cy=\"50\" r=\"36\" fill=\"#D97757\"/>\n</svg>\n",
            CanvasKinds.Data when view == "chart" => "Día,Valor\nLun,42\nMar,58\nMié,51\nJue,77\nVie,93\n",
            CanvasKinds.Data => "{\n  \n}\n",
            CanvasKinds.Plan => PlanTemplate(),
            _ => "",
        };
        if (!Apply(new CanvasOp("insert", Kind: kind, Language: language, Content: content, After: after))) return;
        var added = _doc!.Sections.MaxBy(s => int.TryParse(s.Id.AsSpan(1), out var n) ? n : 0)!;
        if (view is not null) _views[added.Id] = view;
        if (kind is CanvasKinds.Markdown or CanvasKinds.Code or CanvasKinds.Image) StartEditing(added);
        else Render(keepScroll: true);
    }

    /// <summary>Le pide a un agente de la Terminal un cambio en este canvas (o en una sección).</summary>
    void Ask(CanvasSection? section, string request)
    {
        if (_doc is not { } doc || string.IsNullOrWhiteSpace(request)) return;
        var where = section is null ? "" : L.T(", sección {0}", section.Id);
        var text = L.T("En el canvas «{0}» (id {1}){2}: {3}. Usa canvas_read y canvas_edit de claupet-canvas.", doc.Title, doc.Id, where, request.Trim());
        if (_askAgent is null) return;
        Toast(_askAgent(text, doc.Folder) ? L.T("Enviado al agente") : L.T("Listo en la Terminal"), CanvasIcons.Spark);
    }

    // ── Dibujo ──────────────────────────────────────────────────────────────────────────

    void Render(bool keepScroll = false)
    {
        var offset = _scroll.Offset;
        _bar.Child = Bar();
        var shown = _past ?? _doc;
        _scroll.Content = shown is null ? Empty() : Document(shown);
        if (keepScroll) Dispatcher.UIThread.Post(() => _scroll.Offset = offset, DispatcherPriority.Loaded);
        if (_doc is { } doc && doc.Version != _shownVersion && _shownVersion != 0 && _past is null) Bump();
        _shownVersion = _doc?.Version ?? 0;
        _timeline.Child = _history && _doc is not null ? Timeline(_doc) : null;
        ShowOverlay(_timeline, _history && _doc is not null);
        PaintViewing();
        RenderList();
    }

    Border? _versionPill;

    Control Bar()
    {
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = 56, Margin = new Thickness(12, 0, 14, 0) };
        var toggle = IconButton(CanvasIcons.Side, L.T("Lista"), () => ListToggled?.Invoke());
        toggle.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(toggle);
        if (_doc is not { } doc) return bar;

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        if (_renamingTitle && _past is null)
        {
            var box = new TextBox
            {
                Text = doc.Title, FontFamily = Ink.Display, FontSize = 16, FontWeight = FontWeight.SemiBold,
                MinWidth = 220, MaxWidth = 460, Padding = new Thickness(8, 4), BorderThickness = new Thickness(0), Background = _ink.Pill,
            };
            void Commit()
            {
                if (!_renamingTitle) return;
                _renamingTitle = false;
                if (box.Text?.Trim() is { Length: > 0 } title && title != doc.Title) Apply(new CanvasOp("title", Title: title));
                Render(keepScroll: true);
            }
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) Commit();
                else if (e.Key == Key.Escape) { _renamingTitle = false; Render(keepScroll: true); }
            };
            box.LostFocus += (_, _) => Commit();
            left.Children.Add(box);
            Dispatcher.UIThread.Post(() => { box.Focus(); box.SelectAll(); }, DispatcherPriority.Background);
        }
        else
        {
            var title = Ui.Press(Ui.Text(doc.Title, 16, _ink.Text, FontWeight.SemiBold, Ink.Display),
                () => { _renamingTitle = true; Render(keepScroll: true); }, (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, L.T("Cambiar el título"));
            title.Padding = new Thickness(8, 4);
            title.CornerRadius = new CornerRadius(8);
            title.MaxWidth = 460;
            left.Children.Add(title);
        }
        _versionPill = new Border
        {
            Background = _ink.Pill,
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(8, 1),
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = RelativePoint.Center,
            Child = Ui.Text("v" + doc.Version, 11.5, _ink.Tertiary),
        };
        ToolTip.SetTip(_versionPill, doc.Changes.LastOrDefault() is { } last ? $"{CanvasJson.AuthorLabel(last.Author)} · {last.Summary} · {Format.Ago(last.Time)}" : null);
        left.Children.Add(_versionPill);
        Grid.SetColumn(left, 1);
        bar.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(Faces(doc));
        Border? ask = null, export = null, more = null;
        ask = IconButton(CanvasIcons.Spark, L.T("Pedir a la IA") + " · Ctrl+J", () => AskPopup(ask!, null));
        right.Children.Add(ask);
        right.Children.Add(IconButton(CanvasIcons.History, L.T("Versiones"), ToggleHistory, on: _history));
        right.Children.Add(IconButton(CanvasIcons.Play, L.T("Ver en el navegador"), () => Preview(doc, null)));
        export = IconButton(CanvasIcons.Export, L.T("Exportar"), () => ExportPopup(export!));
        right.Children.Add(export);
        more = IconButton(CanvasIcons.More, L.T("Más"), () => MoreMenu(more!), fill: true);
        right.Children.Add(more);
        Grid.SetColumn(right, 2);
        bar.Children.Add(right);
        PaintBarLine();
        return bar;
    }

    void PaintBarLine()
    {
        _bar.BorderBrush = _ink.Hairline;
        _bar.BorderThickness = new Thickness(0, 0, 0, _scroll.Offset.Y > 4 ? 1 : 0);
    }

    /// <summary>Quiénes editaron hace poco (hasta cuatro, usted al final); la de quien edita ahora late.</summary>
    Control Faces(CanvasDoc doc)
    {
        var authors = doc.Changes.AsEnumerable().Reverse().Select(c => c.Author ?? CanvasJson.User).Where(a => a != CanvasJson.User)
            .Distinct().Take(3).Append(CanvasJson.User).ToList();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var author in authors)
        {
            var face = Avatar(author, 28);
            face.Margin = new Thickness(row.Children.Count == 0 ? 0 : -7, 0, 0, 0);
            face.BorderBrush = _ink.Surface;
            face.BorderThickness = new Thickness(2);
            var busy = _busy.TryGetValue(author, out var at) && DateTime.UtcNow - at < TimeSpan.FromSeconds(5);
            ToolTip.SetTip(face, busy ? L.T("{0} · editando", CanvasJson.AuthorLabel(author)) : CanvasJson.AuthorLabel(author));
            if (busy) Pulse(face, AuthorColor(author));
            row.Children.Add(face);
        }
        return row;
    }

    /// <summary>La píldora de la versión crece y vuelve, como un latido, cuando sube la versión (con transiciones).</summary>
    void Bump()
    {
        if (_versionPill is null) return;
        _versionPill.Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new Avalonia.Animation.Easings.CubicEaseOut() }];
        _versionPill.RenderTransform = TransformOperations.Parse("scale(1.22)");
        _bumpTimer.Stop();
        _bumpTimer.Start();
    }

    readonly DispatcherTimer _bumpTimer = new() { Interval = TimeSpan.FromMilliseconds(190) };

    Control Empty()
    {
        var panel = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 110, 24, 24) };
        var mark = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(18), Background = _ink.Pill, HorizontalAlignment = HorizontalAlignment.Center, Child = Glyph(CanvasIcons.Canvas, _ink.Accent, 28) };
        panel.Children.Add(mark);
        panel.Children.Add(new TextBlock { Text = "Canvas", FontFamily = Ink.Display, FontSize = 22, FontWeight = FontWeight.SemiBold, Foreground = _ink.Text, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = L.T("Usted y cualquier IA, en el mismo documento."), Foreground = _ink.Secondary, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        var create = IconButton(CanvasIcons.Plus, L.T("Canvas nuevo") + " · Ctrl+N", New, size: 44, glyph: 20);
        create.Background = _ink.Accent;
        create.Tag = "solid";
        ((Avalonia.Controls.Shapes.Path)((Viewbox)create.Child!).Child!).Stroke = Ink.On(_ink.Accent);
        buttons.Children.Add(create);
        var connect = IconButton(CanvasIcons.Link, L.T("Usar desde otras apps"), CopyConfig, size: 44, glyph: 20);
        connect.Background = _ink.Pill;
        connect.Tag = "solid";
        buttons.Children.Add(connect);
        panel.Children.Add(buttons);
        Enter(panel, 0);
        return panel;
    }

    // ── Versiones ───────────────────────────────────────────────────────────────────────

    void ToggleHistory()
    {
        _history = !_history;
        if (!_history) LeavePast();
        Render(keepScroll: true);
    }

    Control Timeline(CanvasDoc doc)
    {
        var available = _store.Versions(doc.Id).ToHashSet();
        var track = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) };
        track.Children.Add(new Border { Height = 2, Background = _ink.Pill, CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) });
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        foreach (var change in doc.Changes.TakeLast(24))
        {
            var version = change.Version;
            var current = (_past?.Version ?? doc.Version) == version;
            var color = AuthorColor(change.Author);
            var dot = new Border
            {
                Width = 12, Height = 12, CornerRadius = new CornerRadius(6),
                BorderBrush = color, BorderThickness = new Thickness(2),
                Background = current ? color : _ink.Raised,
                RenderTransformOrigin = RelativePoint.Center,
                RenderTransform = current ? TransformOperations.Parse("scale(1.3)") : TransformOperations.Parse("scale(1)"),
                Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(180) }],
                Cursor = new Cursor(StandardCursorType.Hand),
                Opacity = version == doc.Version || available.Contains(version) ? 1 : 0.35,
            };
            ToolTip.SetTip(dot, $"v{version} · {CanvasJson.AuthorLabel(change.Author)} · {change.Summary} · {Format.Ago(change.Time)}");
            dot.PointerEntered += (_, _) => dot.RenderTransform = TransformOperations.Parse("scale(1.35)");
            dot.PointerExited += (_, _) => dot.RenderTransform = TransformOperations.Parse(current ? "scale(1.3)" : "scale(1)");
            dot.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                if (version == doc.Version) { LeavePast(); Render(keepScroll: true); }
                else if (available.Contains(version) && _store.LoadVersion(doc.Id, version) is { } past) { CancelEditing(); _past = past; Render(keepScroll: true); }
            };
            dots.Children.Add(dot);
        }
        track.Children.Add(dots);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Glyph(CanvasIcons.History, _ink.Tertiary, 15));
        row.Children.Add(track);
        row.Children.Add(IconButton(CanvasIcons.Close, L.T("Cerrar"), ToggleHistory, size: 28, glyph: 14));
        return Floating(row, new Thickness(14, 6, 6, 6));
    }

    void PaintViewing()
    {
        if (_past is not { } past || _doc is not { } doc)
        {
            ShowOverlay(_viewing, false);
            return;
        }
        var author = doc.Changes.FirstOrDefault(c => c.Version == past.Version)?.Author;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Avatar(author, 20));
        row.Children.Add(Ui.Text($"v{past.Version}", 12.5, _ink.Text, FontWeight.SemiBold));
        row.Children.Add(IconButton(CanvasIcons.Undo, L.T("Restaurar esta versión"), () =>
        {
            try
            {
                _doc = _store.Restore(doc.Id, past.Version, CanvasJson.User);
                _past = null;
                Reload();
                Render(keepScroll: true);
                Toast(L.T("Restaurado"), CanvasIcons.Undo);
                ListChanged?.Invoke();
            }
            catch (CanvasException ex) { Toast(ex.Message, CanvasIcons.Warn); }
        }, size: 28, glyph: 14));
        row.Children.Add(IconButton(CanvasIcons.Close, L.T("Volver a la de ahora"), () => { LeavePast(); Render(keepScroll: true); }, size: 28, glyph: 14));
        _viewing.Child = Floating(row, new Thickness(8, 4, 4, 4));
        _viewing.VerticalAlignment = VerticalAlignment.Top;
        _viewing.Margin = new Thickness(0, 10, 0, 0);
        ShowOverlay(_viewing, true);
    }

    void LeavePast() => _past = null;

    // ── Lista de la izquierda ───────────────────────────────────────────────────────────

    /// <summary>La búsqueda y los canvas (la columna de la izquierda de la Console).</summary>
    public Control ListView()
    {
        if (_listView.Parent is Panel old) old.Children.Remove(_listView);
        RenderList();
        return _listView;
    }

    void RenderList()
    {
        _listView.Children.Clear();
        _search.Watermark = L.T("Buscar");
        _search.Background = _ink.Pill;
        _search.BorderThickness = new Thickness(0);
        _search.CornerRadius = new CornerRadius(10);
        _search.Padding = new Thickness(32, 7, 10, 7);
        _search.FontSize = 13.5;
        var search = new Panel { Margin = new Thickness(2, 0, 2, 10) };
        if (_search.Parent is Panel previous) previous.Children.Remove(_search);
        search.Children.Add(_search);
        var lens = Glyph(CanvasIcons.Search, _ink.Tertiary, 14);
        lens.HorizontalAlignment = HorizontalAlignment.Left;
        lens.Margin = new Thickness(11, 0, 0, 0);
        lens.IsHitTestVisible = false;
        search.Children.Add(lens);
        _listView.Children.Add(search);
        var filter = _search.Text?.Trim() ?? "";
        var i = 0;
        foreach (var summary in _list.Where(s => filter.Length == 0 || s.Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase)))
        {
            var row = ListRow(summary);
            Enter(row, i++ * 30, distance: 6);
            _listView.Children.Add(row);
        }
    }

    Border ListRow(CanvasSummary summary)
    {
        var current = summary.Id == _doc?.Id;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        // Una miniatura: líneas de un documento, la primera del color de quien lo cambió.
        var thumb = new StackPanel { Spacing = 3, Width = 34, Height = 40, Margin = new Thickness(0, 0, 10, 0) };
        var lines = new StackPanel { Spacing = 3 };
        lines.Children.Add(new Border { Height = 3, Width = 15, CornerRadius = new CornerRadius(2), Background = AuthorColor(summary.LastAuthor), HorizontalAlignment = HorizontalAlignment.Left });
        foreach (var w in new[] { 24.0, 20, 24, 14 }) lines.Children.Add(new Border { Height = 3, Width = w, CornerRadius = new CornerRadius(2), Background = _ink.Pill, HorizontalAlignment = HorizontalAlignment.Left });
        var sheet = new Border
        {
            Width = 34, Height = 40, CornerRadius = new CornerRadius(7), Background = _ink.Raised, BorderBrush = _ink.Hairline,
            BorderThickness = new Thickness(1), Padding = new Thickness(5, 6), Margin = new Thickness(0, 0, 10, 0), Child = lines,
        };
        grid.Children.Add(sheet);
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Text(summary.Title, 14, _ink.Text, FontWeight.Medium));
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        var live = summary.LastAuthor is { } a && a != CanvasJson.User && DateTimeOffset.Now - summary.Updated < TimeSpan.FromSeconds(30);
        var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = AuthorColor(summary.LastAuthor), VerticalAlignment = VerticalAlignment.Center };
        if (live) Pulse(dot, AuthorColor(summary.LastAuthor));
        meta.Children.Add(dot);
        meta.Children.Add(Ui.Text(CanvasJson.AuthorLabel(summary.LastAuthor) + " · " + Format.Ago(summary.Updated), 12, _ink.Tertiary));
        text.Children.Add(meta);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var id = summary.Id;
        var row = Ui.Press(grid, () => Select(id), current ? null : (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, summary.Title);
        row.Padding = new Thickness(8, 7);
        row.CornerRadius = new CornerRadius(12);
        if (current) row.Background = _ink.Pill;
        return row;
    }

    // ── Exportar, vista previa, configuración ───────────────────────────────────────────

    void CopyConfig()
    {
        _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(CanvasMcp.ConfigSnippet());
        Toast(L.T("Configuración MCP copiada"), CanvasIcons.Link);
    }

    /// <summary>El canvas (o una sección) como página, en el navegador: el HTML corre ahí, aislado, y los diagramas se dibujan.</summary>
    void Preview(CanvasDoc doc, CanvasSection? section)
    {
        try
        {
            var dir = Path.Combine(_store.Root, "vista");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, doc.Id + (section is null ? "" : "-" + section.Id) + ".html");
            File.WriteAllText(file, CanvasExport.Html(doc, section?.Id), new UTF8Encoding(false));
            Shell.Open(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Toast(ex.Message, CanvasIcons.Warn); }
    }

    async Task SaveAsAsync(CanvasDoc doc, string format)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var name = string.Concat(doc.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L.T("Exportar"),
            SuggestedFileName = (name.Length > 0 ? name : "Canvas") + CanvasExport.Extension(format),
            ShowOverwritePrompt = true,
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        // El PDF tarda unos segundos (lo imprime el navegador): se hace aparte y se avisa al empezar.
        if (format == "pdf") Toast(L.T("Creando el PDF…"), CanvasIcons.Pdf);
        var copy = doc.Clone();
        try
        {
            await Task.Run(() => CanvasExport.Save(format, copy, path));
            Toast(L.T("Exportado"), CanvasIcons.Check);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CanvasException) { Toast(ex.Message, CanvasIcons.Warn); }
    }

    // ── Avisos ──────────────────────────────────────────────────────────────────────────

    void Toast(string text, string icon)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(Glyph(icon, _ink.Window, 14));
        row.Children.Add(Ui.Text(text, 13, _ink.Window));
        _toast.Child = new Border { Background = _ink.Text, CornerRadius = new CornerRadius(999), Padding = new Thickness(14, 8), Child = row, MaxWidth = 520 };
        _toast.HorizontalAlignment = HorizontalAlignment.Center;
        _toast.VerticalAlignment = VerticalAlignment.Bottom;
        _toast.Margin = new Thickness(0, 0, 0, _history ? 74 : 22);
        _toast.IsHitTestVisible = false;
        ShowOverlay(_toast, true);
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    /// <summary>Muestra u oculta una pieza flotante subiendo o bajando un poco, con transición.</summary>
    void ShowOverlay(Border overlay, bool on)
    {
        overlay.Transitions ??=
        [
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(320), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
        ];
        if (overlay == _timeline)
        {
            overlay.HorizontalAlignment = HorizontalAlignment.Center;
            overlay.VerticalAlignment = VerticalAlignment.Bottom;
            overlay.Margin = new Thickness(0, 0, 0, 16);
        }
        overlay.HorizontalAlignment = HorizontalAlignment.Center;
        overlay.Opacity = on ? 1 : 0;
        overlay.IsHitTestVisible = on && overlay != _toast;
        var away = overlay.VerticalAlignment == VerticalAlignment.Top ? "translateY(-12px)" : "translateY(16px)";
        overlay.RenderTransform = TransformOperations.Parse(on ? "translateY(0px)" : away);
    }
}
