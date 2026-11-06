namespace Claupet;

using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Path = System.IO.Path;

/// <summary>
/// Claupet Proyectos: los planes (*.plan.json) de las carpetas vinculadas, cada uno con su Resumen, Tablero, Cronograma,
/// Diagrama, Métricas y Actividad. Se busca en la lista, dentro del plan y en todo con Ctrl+K; una tarea se abre a la
/// derecha, cambia de estado (lo escribe en el archivo, igual que una IA) y se manda a trabajar a una sesión de la Terminal.
/// Lo que cambia en el archivo se ve solo, y cualquier vista o el plan entero se exportan a PDF con este mismo diseño.
/// </summary>
sealed class ProjectsPane : Panel
{
    const string SearchIcon = "M17,11 A6,6 0 1 1 5,11 A6,6 0 1 1 17,11 Z M15.5,15.5 L20,20";
    const string FilterIcon = "M4,6 H20 M7.5,12 H16.5 M10.5,18 H13.5";
    const string SideIcon = "M6.5,4.5 H17.5 A3,3 0 0 1 20.5,7.5 V16.5 A3,3 0 0 1 17.5,19.5 H6.5 A3,3 0 0 1 3.5,16.5 V7.5 A3,3 0 0 1 6.5,4.5 Z M9,4.5 V19.5";
    const string PdfIcon = "M14,3 H7 A1,1 0 0 0 6,4 V20 A1,1 0 0 0 7,21 H17 A1,1 0 0 0 18,20 V7 L14,3 Z M14,3 V7 H18 M12,10.5 V17 M9.5,14.5 L12,17 L14.5,14.5";
    const string CloseIcon = "M6,6 L18,18 M18,6 L6,18";
    const string SparkIcon = "M12,3 L13.8,9.2 L20,11 L13.8,12.8 L12,19 L10.2,12.8 L4,11 L10.2,9.2 Z M19,3 V6 M17.5,4.5 H20.5";
    const string SendIcon = "M12,19 V5 M6,11 L12,5 L18,11";
    const string AllIcon = "M5,5 H10 V10 H5 Z M14,5 H19 V10 H14 Z M5,14 H10 V19 H5 Z M14,14 H19 V19 H14 Z";
    const string MinusIcon = "M5,12 H19";
    const string PlusIcon = "M12,5 V19 M5,12 H19";
    const string FitIcon = "M4,9 V4 H9 M15,4 H20 V9 M20,15 V20 H15 M9,20 H4 V15";
    const string ProjectIcon = "M4,6 H10 L12,8 H20 V19 H4 Z";
    const string BackIcon = "M19,12 H5 M11,6 L5,12 L11,18";
    const string NextIcon = "M5,12 H19 M13,6 L19,12 L13,18";
    const double SegWidth = 36, SegHeight = 28, DrawerWidth = 352;

    readonly Func<IReadOnlyList<string>> _folders;
    readonly Func<string, bool> _working;
    readonly Action<string, string> _ask, _work;
    readonly PlanIndex _index;
    readonly PlanActivity _activity;
    readonly CanvasStore _canvases;
    readonly object _activityLock = new();
    readonly Dictionary<string, DateTime> _seen = new(StringComparer.OrdinalIgnoreCase);

    readonly StackPanel _list = new() { Spacing = 2, Margin = new Thickness(2, 0, 2, 12) };
    readonly Grid _content = new() { RowDefinitions = new RowDefinitions("Auto,*") };
    readonly Border _barHost = new();
    readonly ContentControl _viewHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    readonly Border _drawer = new() { Width = 0, ClipToBounds = true, BorderThickness = new Thickness(1, 0, 0, 0) };
    readonly Border _zoomBar = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 0, 74), CornerRadius = new CornerRadius(999), Padding = new Thickness(3), BorderThickness = new Thickness(1) };
    readonly Border _askBar = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(20, 0, 20, 16), MaxWidth = 560, CornerRadius = new CornerRadius(999), BorderThickness = new Thickness(1) };
    readonly Border _toast = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 74), IsHitTestVisible = false, IsVisible = false };
    readonly Border _veil = new() { IsVisible = false };
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    Ink _ink = Ink.For(false);
    Control? _tools;
    TextBox? _search, _find, _askBox;
    Border? _findBox, _filterButton, _pdfButton, _canvasButton;
    IReadOnlyList<PlanEntry> _entries = [];
    IReadOnlyList<PlanEvent> _events = [];
    string? _selected;
    string _view;
    string _query = "", _taskQuery = "";
    string? _filter;
    string _chip = "all";
    string? _taskId;
    double? _zoom;
    PlanPanel? _panel;
    bool _scanning, _scanned;
    IReadOnlyList<CanvasSummary> _allCanvases = [];
    int _ticks;
    IDisposable? _settle;

    public event Action? ListToggled;
    /// <summary>Se pidió ver un canvas ligado al plan (o uno recién creado).</summary>
    public event Action<string>? CanvasOpened;

    /// <summary>folders: las carpetas vinculadas; working: si hay una IA trabajando en una; ask y work: mandarle algo a una sesión de esa carpeta.</summary>
    public ProjectsPane(Func<IReadOnlyList<string>> folders, Func<string, bool> working, Action<string, string> ask, Action<string, string> work, PlanIndex? index = null, PlanActivity? activity = null, CanvasStore? canvases = null)
    {
        _folders = folders;
        _working = working;
        _ask = ask;
        _work = work;
        _index = index ?? new PlanIndex();
        _activity = activity ?? new PlanActivity();
        _canvases = canvases ?? CanvasStore.Default;
        _selected = _index.Last;
        _view = PlanPanel.Views().Any(v => v.Id == _index.View) ? _index.View! : "resumen";

        var stage = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var viewArea = new Panel { Children = { _viewHost, _zoomBar } };
        stage.Children.Add(viewArea);
        Grid.SetColumn(_drawer, 1);
        stage.Children.Add(_drawer);
        Grid.SetRow(stage, 1);
        _content.Children.Add(_barHost);
        _content.Children.Add(stage);
        Children.Add(_content);
        Children.Add(_askBar);
        Children.Add(_toast);
        Children.Add(_veil);
        if (TerminalPane.Animate)
        {
            _drawer.Transitions = [new DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromMilliseconds(320), Easing = new Avalonia.Animation.Easings.CubicEaseOut() }];
            _viewHost.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) }];
        }

        _barHost.SizeChanged += (_, _) => FitBar();
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            _toast.IsVisible = false;
        };
        // El plan que se ve se mira cada segundo; la lista entera, cada tanto: así lo que escribe una IA aparece solo.
        _timer.Tick += (_, _) =>
        {
            CheckSelected();
            if (++_ticks % 8 == 0) Rescan();
        };
        AttachedToVisualTree += (_, _) =>
        {
            _timer.Start();
            Rescan();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _timer.Stop();
            _settle?.Dispose();
        };
    }

    public StackPanel List => _list;

    PlanEntry? Selected => _entries.FirstOrDefault(e => string.Equals(e.Path, _selected, StringComparison.OrdinalIgnoreCase));

    static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public void SetInk(Ink ink)
    {
        if (_ink == ink && _tools is not null) return;
        _ink = ink;
        _tools = null;
        _drawer.Background = ink.Raised;
        _drawer.BorderBrush = ink.Hairline;
        BuildZoomBar();
        BuildAskBar();
        RenderAll();
    }

    // ── Datos ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Vuelve a buscar los planes aparte, sin trabar la ventana.</summary>
    void Rescan()
    {
        if (_scanning) return;
        _scanning = true;
        var folders = _folders();
        _ = Task.Run(() =>
        {
            IReadOnlyList<PlanEntry> found = [];
            IReadOnlyList<CanvasSummary> canvases = [];
            try
            {
                found = ScanAndRecord(folders);
                canvases = _canvases.List();
            }
            finally
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _scanning = false;
                    Apply(found);
                    SetCanvases(canvases);
                });
            }
        });
    }

    /// <summary>Lo mismo, de una vez (para las pruebas y después de crear un plan).</summary>
    internal void RefreshNow()
    {
        Apply(ScanAndRecord(_folders()));
        SetCanvases(_canvases.List());
    }

    /// <summary>Los canvas ligados a este plan, para el botón de la barra: solo se redibuja si cambiaron.</summary>
    internal IReadOnlyList<CanvasSummary> LinkedCanvases =>
        Selected is { } entry ? _allCanvases.Where(c => string.Equals(c.Plan, entry.Path, StringComparison.OrdinalIgnoreCase)).ToList() : [];

    void SetCanvases(IReadOnlyList<CanvasSummary> canvases)
    {
        var before = LinkedCanvases;
        _allCanvases = canvases;
        if (!before.Select(c => (c.Id, c.Title)).SequenceEqual(LinkedCanvases.Select(c => (c.Id, c.Title)))) RenderBar();
    }

    /// <summary>Los planes que se ven aquí, sin trabar la ventana: los que Canvas ofrece para ligar.</summary>
    public Task<IReadOnlyList<PlanEntry>> FindPlansAsync()
    {
        var folders = _folders();
        return Task.Run(() => _index.Scan(folders));
    }

    /// <summary>Muestra un plan que pide otra sección. Si no está en una carpeta vinculada, se suma a la lista.</summary>
    public void ShowPlan(string path)
    {
        if (!_index.Scan(_folders()).Any(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase))) _index.Add(path);
        _selected = path;
        RefreshNow();
        if (Selected is { } entry) Open(entry);
    }

    /// <summary>El botón de los canvas del plan: los ligados y uno nuevo.</summary>
    void ShowCanvases()
    {
        if (_canvasButton is null) return;
        var entries = LinkedCanvases.Select(c => new DropList.Entry(c.Id, c.Title)).ToList();
        entries.Add(new DropList.Entry("\0new", L.T("Canvas nuevo…"), Action: true));
        DropList.Show(_canvasButton, _ink, entries, id =>
        {
            if (id == "\0new") NewLinkedCanvas();
            else if (id is not null) CanvasOpened?.Invoke(id);
        });
    }

    /// <summary>Un canvas nuevo, ya ligado a este plan y en su carpeta.</summary>
    internal void NewLinkedCanvas()
    {
        if (Selected is not { } entry) return;
        try
        {
            var doc = _canvases.Create(entry.Name, CanvasJson.User, [new CanvasOp("insert", Kind: CanvasKinds.Markdown, Content: "")], entry.Folder);
            _canvases.Apply(doc.Id, [new CanvasOp("plan", Content: entry.Path)], CanvasJson.User);
            SetCanvases(_canvases.List());
            CanvasOpened?.Invoke(doc.Id);
        }
        catch (CanvasException ex) { Toast(ex.Message, PlanPanel.PlanAlertIcon); }
    }

    IReadOnlyList<PlanEntry> ScanAndRecord(IReadOnlyList<string> folders)
    {
        var found = _index.Scan(folders);
        lock (_activityLock)
            foreach (var entry in found)
            {
                if (entry.Plan is null || (_seen.TryGetValue(entry.Path, out var at) && at == entry.Modified)) continue;
                _seen[entry.Path] = entry.Modified;
                _activity.Update(entry.Path, entry.Plan, entry.Modified);
            }
        return found;
    }

    void Apply(IReadOnlyList<PlanEntry> found)
    {
        var before = Selected;
        var changed = !_scanned || found.Count != _entries.Count || !found.Zip(_entries).All(p => p.First.Path == p.Second.Path && p.First.Modified == p.Second.Modified && p.First.Error == p.Second.Error);
        _scanned = true;
        _entries = found;
        if (Selected is null) _selected = found.FirstOrDefault(e => e.Plan is not null)?.Path ?? found.FirstOrDefault()?.Path;
        if (changed)
        {
            var now = Selected;
            var other = !string.Equals(before?.Path, now?.Path, StringComparison.OrdinalIgnoreCase);
            Fresh = other ? [] : PlanPanel.Changed(before?.Plan, now?.Plan);
            LoadEvents();
            RenderList();
            RenderBar();
            RenderView(keep: !other);
            RenderDrawer();
        }
        else RenderList(); // Solo por el punto de «trabajando».
    }

    HashSet<string> Fresh { get; set; } = [];

    void LoadEvents()
    {
        lock (_activityLock) _events = Selected is { } entry ? _activity.Read(entry.Path) : [];
    }

    /// <summary>¿Cambió el archivo del plan que se ve?</summary>
    void CheckSelected()
    {
        if (Selected is not { } entry) return;
        DateTime modified;
        try { modified = File.Exists(entry.Path) ? File.GetLastWriteTime(entry.Path) : DateTime.MinValue; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        if (modified != entry.Modified && modified != DateTime.MinValue) Reload(null);
    }

    /// <summary>Lee de nuevo el plan que se ve. who: quién lo cambió, si fue desde aquí.</summary>
    void Reload(string? who)
    {
        if (Selected is not { } old) return;
        var entry = PlanIndex.Read(old.Path, old.Folder);
        // A medio escribir: se conserva lo que había y se intenta en el próximo vistazo.
        if (entry.Plan is null && old.Plan is not null && entry.Modified == DateTime.MinValue) return;
        _entries = _entries.Select(e => e == old ? entry : e).ToList();
        Fresh = who is null ? PlanPanel.Changed(old.Plan, entry.Plan) : [];
        if (entry.Plan is not null)
            lock (_activityLock)
            {
                _seen[entry.Path] = entry.Modified;
                _activity.Update(entry.Path, entry.Plan, entry.Modified, who);
            }
        LoadEvents();
        RenderList();
        RenderBar();
        RenderView(keep: true);
        RenderDrawer();
    }

    // ── Lista ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Sin acentos ni mayúsculas: «metricas» encuentra «Métricas».</summary>
    internal static string Fold(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(char.ToLowerInvariant(c));
        return builder.ToString();
    }

    static bool Hit(string text, string query) => query.Length == 0 || Fold(text).Contains(Fold(query), StringComparison.Ordinal);

    static bool AtRisk(Plan plan) => plan.Tasks.Any(t => t.LateOn(Today) || t.State == PlanState.Blocked);

    /// <summary>El buscador y los filtros de la lista, que quedan quietos arriba de ella.</summary>
    public Control ListTools()
    {
        if (_tools is { Parent: Panel parent }) parent.Children.Remove(_tools);
        if (_tools is not null) return _tools;
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(2, 0, 2, 8) };
        _search = Input(L.T("Buscar"), 13.5);
        _search.Text = _query;
        _search.TextChanged += (_, _) =>
        {
            _query = _search.Text?.Trim() ?? "";
            RenderList();
        };
        stack.Children.Add(Pill(SearchIcon, _search, "Ctrl K"));
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        foreach (var (id, icon, name) in new[]
        {
            ("all", AllIcon, L.T("Todos")), ("live", PlanPanel.PlanClockIcon, L.T("Con una IA trabajando")),
            ("risk", PlanPanel.PlanAlertIcon, L.T("Con atrasos o bloqueos")), ("nearly", PlanPanel.CheckIcon, L.T("Casi listos")),
        })
        {
            var on = _chip == id;
            var chip = Ui.Press(PlanPanel.Icon(icon, on ? _ink.Accent : _ink.Tertiary, 13, 1.9), () =>
            {
                _chip = id;
                _tools = null;
                ReplaceTools();
                RenderList();
            }, (b, hover) => { if (!on) b.Background = hover ? _ink.Pill : Brushes.Transparent; }, name);
            chip.Width = 34;
            chip.Height = 28;
            chip.CornerRadius = new CornerRadius(999);
            chip.Background = on ? PlanPanel.Translucent(_ink.Accent, 0.13) : Brushes.Transparent;
            ToolTip.SetTip(chip, name);
            chips.Children.Add(chip);
        }
        stack.Children.Add(chips);
        _tools = stack;
        return stack;
    }

    /// <summary>Cambia el buscador y los filtros en su lugar (al elegir un filtro).</summary>
    void ReplaceTools()
    {
        var old = _search?.GetVisualAncestors().OfType<StackPanel>().Skip(1).FirstOrDefault(s => s.Parent is Panel);
        var parent = old?.Parent as Panel;
        var at = old is null || parent is null ? -1 : parent.Children.IndexOf(old);
        var fresh = ListTools();
        if (parent is not null && at >= 0)
        {
            parent.Children.RemoveAt(at);
            parent.Children.Insert(at, fresh);
        }
    }

    void RenderList()
    {
        _list.Children.Clear();
        var shown = _entries.Where(e => _chip switch
        {
            "live" => _working(e.Folder),
            "risk" => e.Plan is { } plan && AtRisk(plan),
            "nearly" => e.Plan is { } plan && Plan.Progress(plan.Tasks) >= 0.7,
            _ => true,
        }).Where(e => Hit(e.Name, _query) || (e.Plan?.Tasks.Any(t => Hit(t.Title, _query)) ?? false)).ToList();
        foreach (var entry in shown) _list.Children.Add(ListItem(entry));
        if (shown.Count == 0 && _scanned)
        {
            var none = Ui.Text(_entries.Count == 0 ? L.T("Sin planes todavía") : L.T("Nada con ese nombre"), 13, _ink.Tertiary);
            none.Margin = new Thickness(10, 8);
            _list.Children.Add(none);
        }
    }

    Control ListItem(PlanEntry entry)
    {
        var current = string.Equals(entry.Path, _selected, StringComparison.OrdinalIgnoreCase);
        var progress = entry.Plan is { } p ? Plan.Progress(p.Tasks) : 0;
        var ring = new Panel
        {
            Width = 36,
            Height = 36,
            Margin = new Thickness(0, 0, 11, 0),
            Children =
            {
                new PlanRing(progress, _ink.Accent, _ink.Hairline, 4),
                new TextBlock { Text = ((int)Math.Round(progress * 100)).ToString(CultureInfo.CurrentCulture), FontFamily = Ink.Sans, FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = _ink.Secondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Ui.Text(entry.Name, 14, _ink.Text, FontWeight.Medium));
        var sub = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        if (_working(entry.Folder))
            sub.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(999), Background = _ink.Accent, VerticalAlignment = VerticalAlignment.Center });
        sub.Children.Add(ItemNote(entry));
        words.Children.Add(sub);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { ring } };
        Grid.SetColumn(words, 1);
        row.Children.Add(words);
        var item = Ui.Press(row, () => Open(entry), current ? null : (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, entry.Name);
        item.Padding = new Thickness(10, 9);
        item.CornerRadius = new CornerRadius(12);
        item.Background = current ? _ink.Pill : Brushes.Transparent;
        ToolTip.SetTip(item, entry.Path);
        return item;
    }

    /// <summary>La línea de abajo: la tarea que coincide con lo buscado, lo atrasado o el próximo hito.</summary>
    TextBlock ItemNote(PlanEntry entry)
    {
        if (entry.Plan is not { } plan) return Ui.Text(L.T("No se pudo leer"), 12, _ink.Danger);
        if (_query.Length > 0 && !Hit(entry.Name, _query) && plan.Tasks.FirstOrDefault(t => Hit(t.Title, _query)) is { } match)
            return Ui.Text(match.Title, 12, _ink.Tertiary);
        var late = plan.Tasks.Count(t => t.LateOn(Today));
        if (late > 0) return Ui.Text(late == 1 ? L.T("1 atrasada") : L.T("{0} atrasadas", late), 12, _ink.Danger);
        if (plan.Milestones.FirstOrDefault(m => m.Date >= Today) is { } next) return Ui.Text($"{next.Title} · {PlanPanel.Until(next.Date, Today)}", 12, _ink.Tertiary);
        return Ui.Text(Path.GetFileName(Path.TrimEndingDirectorySeparator(entry.Folder)), 12, _ink.Tertiary);
    }

    void Open(PlanEntry entry, string? task = null)
    {
        _selected = entry.Path;
        _taskId = task;
        _filter = null;
        _taskQuery = "";
        _zoom = null;
        Fresh = [];
        _index.Remember(_selected, _view);
        LoadEvents();
        RenderList();
        RenderBar();
        RenderView(keep: false);
        RenderDrawer();
    }

    /// <summary>El «+»: un plan nuevo o uno que ya existe en otra carpeta.</summary>
    public void New()
    {
        if (_tools is null) return;
        DropList.Show(_tools, _ink, [new DropList.Entry("new", L.T("Plan nuevo…")), new DropList.Entry("open", L.T("Abrir un plan…"))], id =>
        {
            if (id == "new") _ = CreateAsync();
            if (id == "open") _ = OpenFileAsync();
        });
    }

    async Task CreateAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = L.T("Plan nuevo"), SuggestedFileName = "plan" + Plan.Extension, DefaultExtension = "json", ShowOverwritePrompt = true });
        if (file?.TryGetLocalPath() is not { } path) return;
        if (!Plan.IsPlanFile(path)) path = Path.ChangeExtension(path, null) + Plan.Extension;
        try
        {
            PlanTools.CreateEmpty(path, Path.GetFileName(path)[..^Plan.Extension.Length]);
            AddAndOpen(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Toast(ex.Message, PlanPanel.PlanAlertIcon); }
    }

    async Task OpenFileAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = L.T("Abrir un plan"), FileTypeFilter = [new FilePickerFileType(L.T("Planes")) { Patterns = ["*" + Plan.Extension] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) AddAndOpen(path);
    }

    void AddAndOpen(string path)
    {
        _index.Add(path);
        _selected = path;
        RefreshNow();
        if (Selected is { } entry) Open(entry);
    }

    // ── Barra ───────────────────────────────────────────────────────────────────────────────

    Border RoundButton(string icon, string name, Action click, bool on = false, double size = 34)
    {
        var button = Ui.Press(PlanPanel.Icon(icon, on ? _ink.Accent : _ink.Secondary, 15, 1.8), click, (b, hover) => { if (!on) b.Background = hover ? _ink.Pill : Brushes.Transparent; }, name);
        button.Width = size;
        button.Height = size;
        button.CornerRadius = new CornerRadius(999);
        button.Background = on ? PlanPanel.Translucent(_ink.Accent, 0.13) : Brushes.Transparent;
        ToolTip.SetTip(button, name);
        return button;
    }

    TextBox Input(string hint, double size)
    {
        var box = new TextBox { Watermark = hint, FontSize = size, FontFamily = Ink.Sans, Foreground = _ink.Text, CaretBrush = _ink.Accent, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), MinHeight = 0, VerticalContentAlignment = VerticalAlignment.Center, SelectionBrush = PlanPanel.Translucent(_ink.Accent, 0.35) };
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
            box.Resources[key] = Brushes.Transparent;
        box.Resources["TextControlBorderThemeThickness"] = new Thickness(0);
        box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        foreach (var key in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" })
            box.Resources[key] = _ink.Text;
        foreach (var key in new[] { "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused" })
            box.Resources[key] = _ink.Tertiary;
        return box;
    }

    /// <summary>Una caja de búsqueda: la lupa, el texto y, si tiene, su atajo.</summary>
    Border Pill(string icon, TextBox box, string? keys)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var glass = PlanPanel.Icon(icon, _ink.Tertiary, 14, 1.9);
        glass.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(glass);
        Grid.SetColumn(box, 1);
        row.Children.Add(box);
        if (keys is not null)
        {
            var kbd = new Border { Background = _ink.Raised, CornerRadius = new CornerRadius(5), Padding = new Thickness(5, 1), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = keys, FontFamily = Ink.Mono, FontSize = 10.5, Foreground = _ink.Tertiary } };
            Grid.SetColumn(kbd, 2);
            row.Children.Add(kbd);
        }
        return new Border { Background = _ink.Pill, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 0), Height = 34, Child = row };
    }

    void RenderBar()
    {
        var entry = Selected;
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*,Auto,Auto,Auto,Auto,Auto"), MinHeight = 58, Margin = new Thickness(10, 0, 12, 0) };
        _barTitle = null;
        _barBranch = null;
        _barFaces = null;
        bar.Children.Add(RoundButton(SideIcon, L.T("Ocultar o mostrar la lista"), () => ListToggled?.Invoke()));
        if (entry is not null)
        {
            var title = Ui.Text(entry.Name, 16, _ink.Text, FontWeight.SemiBold, Ink.Display);
            title.Margin = new Thickness(8, 0);
            _barTitle = title;
            Grid.SetColumn(title, 1);
            bar.Children.Add(title);
            if (GitBranch.Of(entry.Folder) is { } branch)
            {
                var pill = new Border { Background = _ink.Pill, CornerRadius = new CornerRadius(999), Padding = new Thickness(9, 2), VerticalAlignment = VerticalAlignment.Center, Child = Ui.Text(branch, 12, _ink.Tertiary, FontWeight.Normal, Ink.Mono), MaxWidth = 190 };
                ToolTip.SetTip(pill, L.T("La rama de este proyecto"));
                _barBranch = pill;
                Grid.SetColumn(pill, 2);
                bar.Children.Add(pill);
            }
            if (entry.Plan is { } plan)
            {
                var faces = new PlanPanel(_ink).Faces(plan);
                faces.Margin = new Thickness(8, 0, 0, 0);
                _barFaces = faces;
                Grid.SetColumn(faces, 3);
                bar.Children.Add(faces);

                // Buscar dentro del plan: la lupa se abre en una caja.
                _find = Input(L.T("Buscar en este plan"), 13);
                _find.Text = _taskQuery;
                var findOpen = _taskQuery.Length > 0 || _findOpen;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                row.Children.Add(RoundButton(SearchIcon, L.T("Buscar en este plan"), () =>
                {
                    if (FindOpen) CloseFind();
                    else
                    {
                        _findOpen = true;
                        RenderBar();
                        _find?.Focus();
                    }
                }));
                Grid.SetColumn(_find, 1);
                _find.IsVisible = findOpen;
                row.Children.Add(_find);
                _find.TextChanged += (_, _) =>
                {
                    var text = _find.Text?.Trim() ?? "";
                    if (text == _taskQuery) return;
                    _taskQuery = text;
                    RenderView(keep: true);
                };
                _findBox = new Border { Width = findOpen ? 200 : 34, Height = 34, CornerRadius = new CornerRadius(999), Background = findOpen ? _ink.Pill : Brushes.Transparent, ClipToBounds = true, Child = row, Margin = new Thickness(0, 0, 2, 0) };
                Grid.SetColumn(_findBox, 5);
                bar.Children.Add(_findBox);

                _filterButton = RoundButton(FilterIcon, L.T("Filtrar"), ShowFilters, on: _filter is not null);
                Grid.SetColumn(_filterButton, 6);
                bar.Children.Add(_filterButton);
                var linked = LinkedCanvases.Count;
                _canvasButton = RoundButton(CanvasIcons.Canvas, linked == 0 ? L.T("Canvas de este plan") : L.T("Canvas de este plan: {0}", linked), ShowCanvases, on: linked > 0);
                Grid.SetColumn(_canvasButton, 7);
                bar.Children.Add(_canvasButton);

                _pdfButton = RoundButton(PdfIcon, L.T("Exportar a PDF"), ShowExport);
                _pdfButton.Margin = new Thickness(0, 0, 8, 0);
                Grid.SetColumn(_pdfButton, 8);
                bar.Children.Add(_pdfButton);

                var seg = Segments();
                Grid.SetColumn(seg, 9);
                bar.Children.Add(seg);
            }
        }
        _barHost.BorderBrush = _ink.Hairline;
        _barHost.BorderThickness = new Thickness(0, 0, 0, 1);
        _barHost.Child = bar;
        FitBar();
    }

    bool _findOpen;
    internal bool FindOpen => _findOpen || _taskQuery.Length > 0;

    void CloseFind()
    {
        _findOpen = false;
        if (_taskQuery.Length > 0)
        {
            _taskQuery = "";
            RenderView(keep: true);
        }
        RenderBar();
    }

    TextBlock? _barTitle;
    Control? _barBranch, _barFaces;

    /// <summary>
    /// Lo de la derecha nunca se corta: si no hay lugar, primero se va la rama, después los avatares y por último se acorta el título.
    /// </summary>
    void FitBar()
    {
        if (_barTitle is null) return;
        var tools = 44 + (_findBox?.Width ?? 0) + 34 + 34 + 42 + PlanPanel.Views().Length * SegWidth + 6 + 22;
        var room = _barHost.Bounds.Width - tools;
        if (room <= 0) return;
        const double TitleWanted = 250;
        if (_barFaces is not null) _barFaces.IsVisible = room >= TitleWanted + 70;
        if (_barBranch is not null) _barBranch.IsVisible = room >= TitleWanted + 70 + 170;
        var taken = (_barFaces?.IsVisible == true ? 70 : 0) + (_barBranch?.IsVisible == true ? 170 : 0);
        _barTitle.MaxWidth = Math.Max(60, Math.Min(TitleWanted, room - taken - 16));
    }

    /// <summary>Las vistas, como íconos en una pastilla con una perilla que se desliza hacia la elegida.</summary>
    Control Segments()
    {
        var views = PlanPanel.Views();
        var index = Math.Max(0, Array.FindIndex(views, v => v.Id == _view));
        var knob = new Border { Width = SegWidth, Height = SegHeight, CornerRadius = new CornerRadius(999), Background = _ink.Raised, BoxShadow = BoxShadows.Parse("0 1 3 0 #24000000"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(index * SegWidth, 0, 0, 0) };
        if (TerminalPane.Animate) knob.Transitions = [new ThicknessTransition { Property = MarginProperty, Duration = TimeSpan.FromMilliseconds(320), Easing = new Avalonia.Animation.Easings.CubicEaseOut() }];
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (id, icon, name) in views)
        {
            var button = Ui.Press(PlanPanel.Icon(icon, id == _view ? _ink.Text : _ink.Tertiary, 15, 1.8), () => ShowView(id), null, name);
            button.Width = SegWidth;
            button.Height = SegHeight;
            button.CornerRadius = new CornerRadius(999);
            ToolTip.SetTip(button, name);
            buttons.Children.Add(button);
        }
        _knob = knob;
        _segButtons = buttons;
        return new Border { Background = _ink.Pill, CornerRadius = new CornerRadius(999), Padding = new Thickness(3), VerticalAlignment = VerticalAlignment.Center, Child = new Panel { Children = { knob, buttons } } };
    }

    Border? _knob;
    StackPanel? _segButtons;

    internal void ShowView(string id)
    {
        if (id == _view) return;
        _view = id;
        _index.Remember(_selected, _view);
        var views = PlanPanel.Views();
        var index = Array.FindIndex(views, v => v.Id == id);
        if (_knob is not null) _knob.Margin = new Thickness(index * SegWidth, 0, 0, 0);
        if (_segButtons is not null)
            for (var i = 0; i < views.Length && i < _segButtons.Children.Count; i++)
                ((Border)_segButtons.Children[i]).Child = PlanPanel.Icon(views[i].Icon, i == index ? _ink.Text : _ink.Tertiary, 15, 1.8);
        RenderView(keep: false);
    }

    void ShowFilters()
    {
        if (Selected?.Plan is not { } plan || _filterButton is null) return;
        var entries = new List<DropList.Entry> { new("all", L.T("Todas"), Selected: _filter is null) };
        foreach (var (id, name) in new[] { ("late", L.T("Atrasadas")), ("blocked", L.T("Bloqueadas")), ("ready", L.T("Listas")), ("doing", L.T("En curso")) })
            entries.Add(new DropList.Entry(id, $"{name} ({plan.Tasks.Count(t => Matches(plan, t, id))})", Selected: _filter == id));
        DropList.Show(_filterButton, _ink, entries, id =>
        {
            if (id is null) return;
            SetFilter(id == "all" ? null : id);
        });
    }

    void SetFilter(string? filter)
    {
        _filter = filter;
        RenderBar();
        RenderView(keep: true);
    }

    static bool Matches(Plan plan, PlanTask task, string filter) => filter switch
    {
        "late" => task.LateOn(Today),
        "blocked" => task.State == PlanState.Blocked,
        "ready" => task.State == PlanState.Pending && PlanGraph.Ready(plan, task),
        "doing" => task.State == PlanState.Doing,
        _ => true,
    };

    void ShowExport()
    {
        if (_pdfButton is null) return;
        var name = PlanPanel.Views().First(v => v.Id == _view).Name;
        DropList.Show(_pdfButton, _ink, [new DropList.Entry("view", name), new DropList.Entry("all", L.T("Todo el plan"))], id =>
        {
            if (id is not null) _ = ExportAsync(id == "all" ? PlanPanel.Views().Select(v => v.Id).ToList() : [_view]);
        });
    }

    async Task ExportAsync(IReadOnlyList<string> views)
    {
        if (Selected is not { Plan: not null } entry || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var name = string.Concat(entry.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = L.T("Exportar a PDF"), SuggestedFileName = (name.Length > 0 ? name : "Plan") + (views.Count == 1 ? " - " + PlanPanel.Views().First(v => v.Id == views[0]).Name : "") + ".pdf", DefaultExtension = "pdf", FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"], MimeTypes = ["application/pdf"], AppleUniformTypeIdentifiers = ["com.adobe.pdf"] }], ShowOverwritePrompt = true });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            await PlanPdf.SaveAsync(path, entry, _events, views);
            Toast(L.T("Exportado"), PlanPanel.CheckIcon);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { Toast(ex.Message, PlanPanel.PlanAlertIcon); }
    }

    // ── Vista ───────────────────────────────────────────────────────────────────────────────

    bool Shown(Plan plan, PlanTask task) => Hit(task.Title, _taskQuery) && (_filter is null || Matches(plan, task, _filter));

    void RenderAll()
    {
        RenderList();
        RenderBar();
        RenderView(keep: true);
        RenderDrawer();
    }

    void RenderView(bool keep)
    {
        var entry = Selected;
        _zoomBar.IsVisible = false;
        if (entry?.Plan is not { } plan)
        {
            _panel = null;
            _viewHost.Content = entry is not null ? Broken(entry) : _scanned ? Empty() : null;
            return;
        }
        var offsets = keep && _viewHost.Content is Visual shown ? shown.GetVisualDescendants().Prepend(shown).OfType<ScrollViewer>().Select(s => s.Offset).ToList() : null;
        var panel = new PlanPanel(_ink)
        {
            Spacious = true,
            Match = _taskQuery.Length == 0 && _filter is null ? null : t => Shown(plan, t),
            TaskClicked = t => { _taskId = _taskId == t.Id ? null : t.Id; RenderDrawer(); },
            TaskMoved = (t, state) => SetState(t, state),
            TaskWork = Work,
            FilterPicked = id => { _view = _view == "resumen" ? "tablero" : _view; _filter = _filter == id ? null : id; RenderBar(); RenderView(keep: false); },
            StartZoom = _zoom,
            ZoomChanged = z => _zoom = z,
        };
        panel.Fresh.UnionWith(Fresh);
        _panel = panel;
        if (!keep && TerminalPane.Animate)
        {
            _viewHost.Transitions = null;
            _viewHost.Opacity = 0;
            _viewHost.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) }];
        }
        _viewHost.Content = plan.Tasks.Count == 0 && _view != "actividad" ? Empty() : panel.View(_view, plan, _events);
        _viewHost.Opacity = 1;
        _zoomBar.IsVisible = _view == "diagrama" && plan.Tasks.Count > 0;
        _settle?.Dispose();
        if (Fresh.Count > 0) _settle = DispatcherTimer.RunOnce(panel.Settle, TimeSpan.FromSeconds(6));
        Fresh = [];
        // Se redibujó por un cambio: cada cosa sigue donde estaba.
        if (offsets is { Count: > 0 })
            Dispatcher.UIThread.Post(() =>
            {
                if (_viewHost.Content is not Visual now) return;
                var scrolls = now.GetVisualDescendants().Prepend(now).OfType<ScrollViewer>().ToList();
                for (var i = 0; i < scrolls.Count && i < offsets.Count; i++) scrolls[i].Offset = offsets[i];
            }, DispatcherPriority.Background);
    }

    Control Broken(PlanEntry entry)
    {
        var text = Ui.Text(L.T("No se pudo leer el plan: {0}", entry.Error ?? ""), 13, _ink.Danger);
        text.TextWrapping = TextWrapping.Wrap;
        text.Margin = new Thickness(26);
        return text;
    }

    Control Empty()
    {
        var mark = new Border { Width = 54, Height = 54, CornerRadius = new CornerRadius(999), Background = PlanPanel.Translucent(_ink.Accent, 0.13), Child = PlanPanel.Icon(SparkIcon, _ink.Accent, 24, 1.7) };
        return new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 90), Children = { mark, Ui.Text(L.T("Pedirle un plan a la IA"), 14, _ink.Tertiary) } };
    }

    void BuildZoomBar()
    {
        _zoomBar.Background = _ink.Raised;
        _zoomBar.BorderBrush = _ink.Hairline;
        _zoomBar.BoxShadow = BoxShadows.Parse("0 4 16 0 #24000000");
        _zoomBar.Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            Children =
            {
                RoundButton(MinusIcon, L.T("Alejar"), () => _panel?.ZoomBy(-0.15), size: 28),
                RoundButton(FitIcon, L.T("Ajustar"), () => _panel?.ZoomFit(), size: 28),
                RoundButton(PlusIcon, L.T("Acercar"), () => _panel?.ZoomBy(0.15), size: 28),
            },
        };
    }

    // ── Pedirle a la IA ─────────────────────────────────────────────────────────────────────

    void BuildAskBar()
    {
        _askBox = Input(L.T("Pedirle algo a la IA"), 14);
        _askBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            SendAsk();
        };
        var send = Ui.Press(PlanPanel.Icon(SendIcon, Ink.On(_ink.Accent), 15, 2), SendAsk, (b, on) => b.Opacity = on ? 0.88 : 1, L.T("Enviar"));
        send.Width = 32;
        send.Height = 32;
        send.CornerRadius = new CornerRadius(999);
        send.Background = _ink.Accent;
        ToolTip.SetTip(send, L.T("Enviar"));
        var spark = PlanPanel.Icon(SparkIcon, _ink.Accent, 15, 1.8);
        spark.Margin = new Thickness(0, 0, 9, 0);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { spark } };
        Grid.SetColumn(_askBox, 1);
        row.Children.Add(_askBox);
        Grid.SetColumn(send, 2);
        row.Children.Add(send);
        _askBar.Width = 560;
        _askBar.Height = 46;
        _askBar.Padding = new Thickness(14, 6, 6, 6);
        _askBar.Background = _ink.Raised;
        _askBar.BorderBrush = _ink.Hairline;
        _askBar.BoxShadow = BoxShadows.Parse("0 8 28 0 #24000000");
        _askBar.Child = row;
    }

    void SendAsk()
    {
        var text = _askBox?.Text?.Trim() ?? "";
        if (text.Length == 0) return;
        _askBox!.Text = "";
        if (Selected is { } entry) _ask(L.T("{0}\n\nEs sobre el plan {1}: cámbialo con plan_update.", text, entry.Path), entry.Folder);
        else _ask(L.T("{0}\n\nGuárdalo como un plan de Claupet con plan_create.", text), _folders().FirstOrDefault() ?? Account.Home);
    }

    void Work(PlanTask task)
    {
        if (Selected is not { } entry) return;
        var text = L.T("Trabaja en la tarea «{0}» ({1}) del plan {2}. Marca su estado con plan_update al empezar y al terminar.", task.Title, task.Id, entry.Path);
        if (task.Notes is { } notes) text += "\n\n" + notes;
        _work(text, entry.Folder);
    }

    // ── Detalle ─────────────────────────────────────────────────────────────────────────────

    void SetState(PlanTask task, PlanState state)
    {
        if (Selected is not { } entry) return;
        try
        {
            PlanTools.SetState(entry.Path, task.Id, state);
            Reload(L.T("Usted"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CanvasException) { Toast(ex.Message, PlanPanel.PlanAlertIcon); }
    }

    void RenderDrawer()
    {
        var entry = Selected;
        var task = entry?.Plan?.Tasks.FirstOrDefault(t => t.Id == _taskId);
        if (entry?.Plan is not { } plan || task is null)
        {
            _taskId = null;
            _drawer.Width = 0;
            return;
        }
        var panel = new PlanPanel(_ink);
        var stack = new StackPanel { Spacing = 15, Margin = new Thickness(18, 2, 18, 18) };

        var title = Ui.Text(task.Title, 18, _ink.Text, FontWeight.SemiBold, Ink.Display);
        title.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(title);

        var needs = task.Depends.Select(d => plan.Tasks.FirstOrDefault(t => t.Id == d)).OfType<PlanTask>().ToList();
        var waits = plan.Tasks.Where(t => t.Depends.Contains(task.Id)).ToList();
        var can = task.State != PlanState.Done && needs.All(n => n.State == PlanState.Done);
        var onAccent = can ? Ink.On(_ink.Accent) : _ink.Tertiary;
        var workRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { PlanPanel.Icon(PlanPanel.PlanPlayIcon, onAccent, 15, 2), Ui.Text(L.T("Trabajar en esto"), 14, onAccent, FontWeight.SemiBold) } };
        Border work = can ? Ui.Press(workRow, () => Work(task), (b, on) => b.Opacity = on ? 0.9 : 1, L.T("Trabajar en esto")) : new Border { Child = workRow };
        work.Height = 42;
        work.CornerRadius = new CornerRadius(12);
        work.Background = can ? _ink.Accent : _ink.Pill;
        ToolTip.SetTip(work, task.State == PlanState.Done ? L.T("Ya está hecha") : can ? L.T("Abre una sesión de Claude Code con esta tarea") : L.T("Primero, lo que necesita"));
        stack.Children.Add(work);

        var states = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*") };
        var look = new (PlanState State, string Icon, IBrush Tint)[]
        {
            (PlanState.Pending, PlanPanel.WaitIcon, _ink.Secondary), (PlanState.Doing, PlanPanel.PlanClockIcon, _ink.Accent), (PlanState.Review, PlanPanel.ReviewIcon, _ink.Secondary),
            (PlanState.Done, PlanPanel.CheckIcon, panel.Added), (PlanState.Blocked, PlanPanel.BlockedIcon, _ink.Warn),
        };
        for (var i = 0; i < look.Length; i++)
        {
            var (state, icon, tint) = look[i];
            var on = task.State == state;
            var name = PlanPanel.StateName(state);
            var button = Ui.Press(PlanPanel.Icon(icon, on ? tint : _ink.Tertiary, 15, 1.9), () => SetState(task, state), null, name);
            button.Height = 30;
            button.CornerRadius = new CornerRadius(999);
            button.Background = on ? _ink.Raised : Brushes.Transparent;
            ToolTip.SetTip(button, name);
            Grid.SetColumn(button, i);
            states.Children.Add(button);
        }
        stack.Children.Add(new Border { Background = _ink.Pill, CornerRadius = new CornerRadius(999), Padding = new Thickness(3), Child = states });

        var facts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*"), RowDefinitions = new RowDefinitions("Auto,8,Auto") };
        void Fact(int row, int column, string icon, string text, string tip)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { PlanPanel.Icon(icon, _ink.Secondary, 14, 1.8), Ui.Text(text, 12.5, _ink.Secondary) } };
            var fact = new Border { Background = _ink.Pill, CornerRadius = new CornerRadius(11), Padding = new Thickness(11, 8), Child = line };
            ToolTip.SetTip(fact, tip);
            Grid.SetRow(fact, row);
            Grid.SetColumn(fact, column);
            facts.Children.Add(fact);
        }
        Fact(0, 0, PlanPanel.PlanCalendarIcon, PlanPanel.Day(task.Start), L.T("Empieza"));
        Fact(0, 2, PlanPanel.TimelineIcon, task.Days == 1 ? L.T("1 día") : L.T("{0} días", task.Days), L.T("Dura"));
        if (task.Hours is not null || task.HoursSpent is not null)
            Fact(2, 0, PlanPanel.PlanClockIcon, $"{task.HoursSpent ?? 0:0.#}/{task.Hours ?? 0:0.#} h", L.T("Horas reales y estimadas"));
        if (task.Owner is { } owner) Fact(2, 2, ProjectIcon, owner, L.T("Responsable: {0}", owner));
        stack.Children.Add(facts);

        if (task.Notes is { } notes)
        {
            var text = Ui.Text(notes, 13, _ink.Secondary);
            text.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(text);
        }
        Control Links(string icon, string name, IEnumerable<PlanTask> tasks)
        {
            var group = new StackPanel { Spacing = 1 };
            group.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 4), Children = { PlanPanel.Icon(icon, _ink.Tertiary, 13, 2), Ui.Text(name, 12, _ink.Tertiary, FontWeight.SemiBold) } });
            foreach (var other in tasks)
            {
                var (mark, tint, state) = panel.StateLook(plan, other);
                var glyph = PlanPanel.Icon(mark, tint, 14, 2);
                ToolTip.SetTip(glyph, state);
                var label = Ui.Text(other.Title, 13, _ink.Text);
                label.Margin = new Thickness(9, 0);
                var when = Ui.Text(PlanPanel.Day(other.Start), 11.5, _ink.Tertiary);
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { glyph } };
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                Grid.SetColumn(when, 2);
                row.Children.Add(when);
                var id = other.Id;
                var link = Ui.Press(row, () => { _taskId = id; RenderDrawer(); }, (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, other.Title);
                link.Padding = new Thickness(9, 7);
                link.CornerRadius = new CornerRadius(9);
                group.Children.Add(link);
            }
            return group;
        }
        if (needs.Count > 0) stack.Children.Add(Links(BackIcon, L.T("Necesita"), needs));
        if (waits.Count > 0) stack.Children.Add(Links(NextIcon, L.T("La esperan"), waits));

        var close = RoundButton(CloseIcon, L.T("Cerrar"), () => { _taskId = null; RenderDrawer(); }, size: 30);
        var stageName = plan.Stages.FirstOrDefault(s => s.Id == task.Stage)?.Name ?? "";
        var label2 = Ui.Text(stageName, 12, _ink.Tertiary);
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(18, 10, 10, 4), Children = { label2 } };
        Grid.SetColumn(close, 1);
        head.Children.Add(close);
        var scroller = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = stack };
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Width = DrawerWidth - 1, HorizontalAlignment = HorizontalAlignment.Left, Children = { head } };
        Grid.SetRow(scroller, 1);
        body.Children.Add(scroller);
        _drawer.Child = body;
        _drawer.Width = DrawerWidth;
    }

    // ── Atajos y Ctrl+K ─────────────────────────────────────────────────────────────────────

    public void HandleKey(KeyEventArgs e)
    {
        if (_veil.IsVisible) return;
        if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            Palette();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && _taskId is not null)
        {
            _taskId = null;
            RenderDrawer();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && FindOpen)
        {
            CloseFind();
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers != KeyModifiers.None || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox) return;
        var number = e.Key is >= Key.D1 and <= Key.D6 ? e.Key - Key.D1 : e.Key is >= Key.NumPad1 and <= Key.NumPad6 ? e.Key - Key.NumPad1 : -1;
        if (number < 0 || Selected?.Plan is null) return;
        ShowView(PlanPanel.Views()[number].Id);
        e.Handled = true;
    }

    /// <summary>Busca en todos los planes: los planes, sus tareas, sus hitos y las vistas. Flechas, Intro y Esc.</summary>
    internal void Palette()
    {
        var query = Input(L.T("Planes, tareas, hitos…"), 15);
        var results = new StackPanel { Spacing = 1, Margin = new Thickness(6) };
        var picks = new List<(string Icon, string Name, string Where, Action Go)>();
        var selected = 0;
        void Close() => _veil.IsVisible = false;
        void Draw()
        {
            var text = query.Text?.Trim() ?? "";
            picks.Clear();
            foreach (var entry in _entries)
            {
                if (Hit(entry.Name, text)) picks.Add((ProjectIcon, entry.Name, Path.GetFileName(Path.TrimEndingDirectorySeparator(entry.Folder)), () => Open(entry)));
                if (text.Length == 0 || entry.Plan is not { } plan) continue;
                foreach (var task in plan.Tasks.Where(t => Hit(t.Title, text)))
                    picks.Add((new PlanPanel(_ink).StateLook(plan, task).Icon, task.Title, entry.Name, () => Open(entry, task.Id)));
                foreach (var milestone in plan.Milestones.Where(m => Hit(m.Title, text)))
                    picks.Add((PlanPanel.PlanFlagIcon, milestone.Title, entry.Name, () => { Open(entry); ShowView("cronograma"); }));
            }
            foreach (var (id, icon, name) in PlanPanel.Views())
                if (Hit(name, text)) picks.Add((icon, name, L.T("Vista"), () => ShowView(id)));
            if (picks.Count > 12) picks.RemoveRange(12, picks.Count - 12);
            selected = Math.Clamp(selected, 0, Math.Max(0, picks.Count - 1));
            results.Children.Clear();
            for (var i = 0; i < picks.Count; i++)
            {
                var (icon, name, where, go) = picks[i];
                var mark = PlanPanel.Icon(icon, _ink.Secondary, 15, 1.8);
                var label = Ui.Text(name, 14, _ink.Text);
                label.Margin = new Thickness(12, 0);
                var place = Ui.Text(where, 12, _ink.Tertiary);
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { mark } };
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                Grid.SetColumn(place, 2);
                row.Children.Add(place);
                var item = Ui.Press(row, () => { Close(); go(); }, null, name);
                item.Padding = new Thickness(12, 8);
                item.CornerRadius = new CornerRadius(10);
                item.Background = i == selected ? _ink.Pill : Brushes.Transparent;
                results.Children.Add(item);
            }
            if (picks.Count == 0)
            {
                var none = Ui.Text(L.T("Nada con ese nombre"), 13, _ink.Tertiary);
                none.Margin = new Thickness(12);
                none.HorizontalAlignment = HorizontalAlignment.Center;
                results.Children.Add(none);
            }
        }
        query.TextChanged += (_, _) => { selected = 0; Draw(); };
        query.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Down when picks.Count > 0: selected = (selected + 1) % picks.Count; Draw(); break;
                case Key.Up when picks.Count > 0: selected = (selected - 1 + picks.Count) % picks.Count; Draw(); break;
                case Key.Enter when picks.Count > 0: { var go = picks[selected].Go; Close(); go(); break; }
                case Key.Escape: Close(); break;
                default: return;
            }
            e.Handled = true;
        };
        Draw();
        var lens = PlanPanel.Icon(SearchIcon, _ink.Tertiary, 17, 1.9);
        lens.Margin = new Thickness(0, 0, 10, 0);
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(16, 13), Children = { lens } };
        Grid.SetColumn(query, 1);
        head.Children.Add(query);
        var card = new Border
        {
            Width = 560,
            MaxWidth = 560,
            CornerRadius = new CornerRadius(16),
            Background = _ink.Raised,
            BoxShadow = BoxShadows.Parse("0 20 60 0 #47000000"),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(20, 90, 20, 20),
            ClipToBounds = true,
            Child = new StackPanel { Children = { head, new Border { Height = 1, Background = _ink.Hairline }, new ScrollViewer { MaxHeight = 360, Content = results } } },
        };
        card.PointerPressed += (_, e) => e.Handled = true;
        _veil.Background = PlanPanel.Translucent(Brushes.Black, 0.2);
        _veil.Child = card;
        _veil.PointerPressed += (_, e) => { if (e.Source == _veil) Close(); };
        _veil.IsVisible = true;
        _paletteQuery = query;
        Dispatcher.UIThread.Post(() => query.Focus(), DispatcherPriority.Background);
    }

    TextBox? _paletteQuery;

    /// <summary>Para las pruebas: lo que ve la paleta.</summary>
    internal TextBox? PaletteQuery => _paletteQuery;

    void Toast(string text, string icon)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { PlanPanel.Icon(icon, _ink.Window, 14, 1.9), Ui.Text(text, 13, _ink.Window) } };
        _toast.Child = new Border { Background = _ink.Text, CornerRadius = new CornerRadius(999), Padding = new Thickness(14, 8), Child = row, MaxWidth = 520 };
        _toast.IsVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    /// <summary>Para las pruebas: lo mismo que hace la persona.</summary>
    internal void SelectTask(string? id)
    {
        _taskId = id;
        RenderDrawer();
    }

    internal bool DrawerOpen => _drawer.Width > 0;
    internal string? SelectedPath => _selected;
    internal IReadOnlyList<PlanEntry> Entries => _entries;
    internal IReadOnlyList<PlanEvent> Events => _events;
    internal void SearchTasks(string text)
    {
        _taskQuery = text;
        RenderView(keep: true);
    }
    internal void SearchList(string text)
    {
        _query = text;
        RenderList();
    }
    internal void FilterTasks(string? filter) => SetFilter(filter);
    internal void ChangeState(string id, PlanState state)
    {
        if (Selected?.Plan?.Tasks.FirstOrDefault(t => t.Id == id) is { } task) SetState(task, state);
    }
    internal Task Export(string path, IReadOnlyList<string> views) => PlanPdf.SaveAsync(path, Selected!, _events, views);
}
