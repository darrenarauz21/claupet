namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

/// <summary>
/// El panel de la derecha de una sesión, con tres íconos: Ramas (qué agente trabaja en cada rama, su worktree y lo que
/// falta subir o bajar), Cambios (los archivos sin commit con sus líneas; al tocar uno se ve su cambio) y Procesos (lo
/// que abrió cada sesión, con su memoria y el botón para detenerlo). Se oculta con su botón y su borde se arrastra.
/// </summary>
sealed partial class TerminalPane
{
    const string PanelIcon = "M4,5 H20 V19 H4 Z M15,5 V19";
    const string RamasIcon = "M6,3.8 A2.2,2.2 0 1 0 6.01,3.8 Z M6,15.8 A2.2,2.2 0 1 0 6.01,15.8 Z M18,5.8 A2.2,2.2 0 1 0 18.01,5.8 Z M6,8.2 V15.8 M18,10.2 C18,14.2 12,13.2 7,16.2";
    const string CambiosIcon = "M7,3 H14 L19,8 V20 A1,1 0 0 1 18,21 H7 A1,1 0 0 1 6,20 V4 A1,1 0 0 1 7,3 Z M12.5,9 V15 M9.5,12 H15.5 M9.5,18 H15.5";
    const string ProcesosIcon = "M8,8 H16 V16 H8 Z M5,5 H19 V19 H5 Z M9,2 V5 M15,2 V5 M9,19 V22 M15,19 V22 M2,9 H5 M2,15 H5 M19,9 H22 M19,15 H22";
    const string StopSquare = "M8,8 H16 V16 H8 Z";
    const string HistoryIcon = "M12,3.5 A8.5,8.5 0 1 0 12.01,3.5 Z M12,7.5 V12 L15,14";
    const string SearchIcon = "M17,11 A6,6 0 1 1 5,11 A6,6 0 1 1 17,11 Z M15.5,15.5 L20,20";

    /// <summary>
    /// Lo de la vista elegida. Se crea uno nuevo con cada panel: el de antes sigue dentro de su ScrollViewer aunque ya
    /// no se vea, y ponerlo en otro hacía fallar el acomodo de la ventana entera (dejaba de mostrar lo nuevo).
    /// </summary>
    StackPanel _sideBody = new() { Spacing = 2 };
    DispatcherTimer? _processTimer;
    /// <summary>El botón apretado sobre el panel: Procesos no lo rehace en ese momento.</summary>
    PressGuard? _sideGuard;
    /// <summary>Los procesos que se dibujaron (en qué panel y cuáles): si siguen los mismos, solo cambian los MB.</summary>
    (StackPanel Body, string Key)? _processShown;
    readonly Dictionary<int, TextBlock> _processMb = [];

    sealed record ProcessLine(int Pid, string Name, string Detail, long Memory, int Depth, Tab? Session);
    /// <summary>Sube al cambiar de pestaña o de vista: lo que llega de una lectura vieja se descarta.</summary>
    int _sideVersion;
    /// <summary>El archivo de Cambios que se está mirando y sus líneas.</summary>
    string? _peek;
    IReadOnlyList<string>? _peekLines;

    void ToggleSide()
    {
        _projects.PanelOpen = !_projects.PanelOpen;
        _projects.Save();
        Build();
    }

    void StopSide()
    {
        _processTimer?.Stop();
        _processTimer = null;
        _sideVersion++;
    }

    Control SidePanel(Tab tab)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Width = _projects.PanelWidth };
        grid.Children.Add(SideGrip(grid));

        var panel = new Grid { RowDefinitions = new RowDefinitions("44,*") };
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(10, 0, 12, 0) };
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        // Un archivo fijado que ya no está en la lista (se quitó) no se muestra.
        if (tab.SideFile is { } stale && !tab.Docked.Contains(stale)) tab.SideFile = null;
        var file = tab.SideFile;
        foreach (var (id, icon, name) in new[] { ("ramas", RamasIcon, L.T("Ramas")), ("cambios", CambiosIcon, L.T("Cambios")), ("procesos", ProcesosIcon, L.T("Procesos")), ("historial", HistoryIcon, L.T("Historial")) })
            tabs.Children.Add(SideTab(icon, name, file is null && _projects.PanelTab == id, () =>
            {
                tab.SideFile = null;
                _projects.PanelTab = id;
                _projects.Save();
                Build();
            }));
        // Los archivos fijados, cada uno con su ícono (al costado, su nombre).
        foreach (var docked in tab.Docked)
            tabs.Children.Add(SideTab(IsImage(docked) ? ImageFileIcon : FileIcon, Path.GetFileName(docked), file == docked, () =>
            {
                tab.SideFile = docked;
                Build();
            }, LinkBrush));
        bar.Children.Add(tabs);
        Control title;
        if (file is not null)
        {
            // El nombre del archivo y su × (lo quita del panel).
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var name = Ui.Text(Path.GetFileName(file), 12.5, _ink.Secondary);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.MaxWidth = 150;
            name.VerticalAlignment = VerticalAlignment.Center;
            ToolTip.SetTip(name, file);
            row.Children.Add(name);
            var remove = Round(CloseIcon, L.T("Quitar del panel"), () => Undock(tab, file));
            remove.Width = 24;
            remove.Height = 24;
            row.Children.Add(remove);
            title = row;
        }
        else
        {
            var text = Ui.Text(_projects.PanelTab switch { "cambios" => L.T("Cambios"), "procesos" => L.T("Procesos"), "historial" => L.T("Historial"), _ => L.T("Ramas") }, 12.5, _ink.Tertiary);
            text.HorizontalAlignment = HorizontalAlignment.Right;
            text.VerticalAlignment = VerticalAlignment.Center;
            title = text;
            if (_projects.PanelTab == "historial") title = HistoryTitle(tab);
        }
        Grid.SetColumn(title, 1);
        bar.Children.Add(title);
        panel.Children.Add(bar);

        if (file is not null)
        {
            // La vista del archivo tiene su propio desplazamiento: va sin el ScrollViewer de las listas.
            var view = new Border { BorderBrush = _ink.Hairline, BorderThickness = new Thickness(0, 1, 0, 0), Child = FileView(file, null) };
            Grid.SetRow(view, 1);
            panel.Children.Add(view);
        }
        else
        {
            _sideBody = new StackPanel { Spacing = 2, Margin = new Thickness(8, 0, 10, 12) };
            _sideGuard = new PressGuard(_sideBody, () => { });
            var scroll = new ScrollViewer { Content = _sideBody, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetRow(scroll, 1);
            panel.Children.Add(scroll);
        }

        var border = new Border { BorderBrush = _ink.Hairline, BorderThickness = new Thickness(1, 0, 0, 0), Child = panel };
        Grid.SetColumn(border, 1);
        grid.Children.Add(border);
        if (file is null) LoadSide(tab);
        return grid;
    }

    /// <summary>Un ícono de la franja del panel (una vista o un archivo fijado), resaltado si es el que se ve.</summary>
    Border SideTab(string icon, string name, bool selected, Action onClick, IBrush? brush = null)
    {
        var button = Ui.Press(Icon(icon, brush ?? (selected ? _ink.Text : _ink.Tertiary), 15, 1.7), onClick,
            selected ? null : (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, name);
        button.Width = 30;
        button.Height = 28;
        button.CornerRadius = new CornerRadius(8);
        if (selected) button.Background = _ink.Pill;
        ToolTip.SetTip(button, name);
        return button;
    }

    /// <summary>El borde izquierdo del panel: arrastrándolo cambia su ancho, que se recuerda.</summary>
    Control SideGrip(Grid panel)
    {
        var line = new Border { Width = 2, CornerRadius = new CornerRadius(1), Background = _ink.Accent, Opacity = 0, HorizontalAlignment = HorizontalAlignment.Center };
        var grip = new Border { Width = 7, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeWestEast), Child = line, Margin = new Thickness(0, 0, -4, 0), ZIndex = 1 };
        double? from = null;
        double width = 0;
        grip.PointerEntered += (_, _) => line.Opacity = 0.6;
        grip.PointerExited += (_, _) => { if (from is null) line.Opacity = 0; };
        grip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) return;
            from = e.GetPosition(this).X;
            width = panel.Width;
            e.Pointer.Capture(grip);
            e.Handled = true;
        };
        grip.PointerMoved += (_, e) =>
        {
            if (from is not { } start) return;
            // Hacia la izquierda se ensancha.
            panel.Width = Math.Clamp(width - (e.GetPosition(this).X - start), TerminalProjects.PanelMin, Math.Min(TerminalProjects.PanelMax, Bounds.Width - 320));
        };
        grip.PointerReleased += (_, e) =>
        {
            if (from is null) return;
            from = null;
            e.Pointer.Capture(null);
            line.Opacity = 0;
            _projects.PanelWidth = panel.Width;
            _projects.Save();
        };
        return grip;
    }

    /// <summary>Lee lo de la vista elegida (git en segundo plano; los procesos cada 3 s mientras se ven).</summary>
    void LoadSide(Tab tab)
    {
        if (tab.SideFile is not null) return; // se ve un archivo fijado, no una lista
        var version = ++_sideVersion;
        var folder = tab.Agent.Options.Folder;
        switch (_projects.PanelTab)
        {
            case "procesos":
                ShowProcesses();
                _processTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                _processTimer.Tick += (_, _) => ShowProcesses();
                _processTimer.Start();
                return;
            case "historial":
                _deletingSession = null;
                _confirmClearHistory = false;
                // Otra carpeta u otra cuenta: vuelve a la primera página y sin búsqueda.
                var where = (tab.Agent.Options.ConfigDir ?? global::Claupet.Account.DefaultDir) + "|" + folder;
                if (_historyWhere != where)
                {
                    _historyWhere = where;
                    _historyShown = HistoryPage;
                    _historyQuery = "";
                }
                _historySearch = null;
                Loading();
                ReadHistory(tab, version);
                return;
            case "cambios":
                Loading();
                _ = Task.Run(async () =>
                {
                    var changes = await GitRepo.ChangesAsync(folder);
                    Dispatcher.UIThread.Post(() => { if (version == _sideVersion) ShowChanges(folder, changes); });
                });
                return;
            default:
                Loading();
                _ = Task.Run(async () =>
                {
                    var branches = await GitRepo.BranchesAsync(folder);
                    Dispatcher.UIThread.Post(() => { if (version == _sideVersion) ShowBranches(tab, branches); });
                });
                return;
        }
    }

    /// <summary>Mientras se lee: unas filas que brillan.</summary>
    void Loading()
    {
        _sideBody.Children.Clear();
        foreach (var width in new[] { 0.8, 0.6, 0.7 })
        {
            var row = new Border { Height = 30, Margin = new Thickness(4, 4), CornerRadius = new CornerRadius(8), Background = _ink.Pill, HorizontalAlignment = HorizontalAlignment.Left };
            row.Width = Math.Max(80, (_projects.PanelWidth - 40) * width);
            Pulse(row);
            _sideBody.Children.Add(row);
        }
    }

    Control Hint(string text)
    {
        var hint = Ui.Text(text, 12, _ink.Tertiary);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(6, 10, 6, 0);
        return hint;
    }

    // ── Ramas ───────────────────────────────────────────────────────────────────────────────

    void ShowBranches(Tab tab, IReadOnlyList<GitBranchInfo> branches)
    {
        _sideBody.Children.Clear();
        if (branches.Count == 0)
        {
            _sideBody.Children.Add(Hint(L.T("Esta carpeta no es un repositorio de git (o git no está instalado).")));
            return;
        }
        var folder = tab.Agent.Options.Folder;
        var status = _git.TryGetValue(folder, out var git) ? git.Status : null;
        foreach (var branch in branches)
        {
            // Las pestañas que trabajan en esa rama: en esta carpeta (si es la actual) o en su worktree.
            var agents = _tabs.Where(t => t.Branch == branch.Name
                && (Same(t.Agent.Options.Folder, folder) || branch.Worktree is { } w && Same(t.Agent.Options.Folder, w))).ToList();
            var details = new List<string>();
            details.AddRange(agents.Select(a => $"Claude Code · {a.Account}"));
            if (branch.Worktree is { } worktree && !Same(worktree, folder)) details.Add(L.T("worktree"));
            if (branch.Current && status is { Changed: > 0 }) details.Add(L.T("{0} sin commit", status.Changed));
            if (branch.Gone) details.Add(L.T("su remota ya no está"));
            if (agents.Count == 0 && details.Count == 0) details.Add(L.T("sin agente"));

            var dot = new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(99),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = agents.Any(a => Waiting(a.Agent.Transcript)) ? _ink.Warn : agents.Any(a => a.Agent.Transcript.Busy) ? _ink.Accent : _ink.Hairline,
            };
            if (agents.Any(a => a.Agent.Transcript.Busy)) Pulse(dot);
            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            top.Children.Add(dot);
            var name = Ui.Text(branch.Name, 12.5, branch.Current ? _ink.Text : _ink.Secondary, branch.Current ? FontWeight.SemiBold : FontWeight.Normal, Mono);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(name, 1);
            top.Children.Add(name);
            var nums = string.Join(" ", new[] { branch.Ahead > 0 ? $"↑{branch.Ahead}" : "", branch.Behind > 0 ? $"↓{branch.Behind}" : "" }.Where(s => s.Length > 0));
            if (nums.Length > 0)
            {
                var n = Ui.Text(nums, 11.5, _ink.Secondary, family: Mono);
                n.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(n, 2);
                top.Children.Add(n);
            }
            var line2 = Ui.Text(string.Join(" · ", details), 11.5, _ink.Tertiary);
            line2.TextTrimming = TextTrimming.CharacterEllipsis;
            line2.Margin = new Thickness(15, 1, 0, 0);
            var content = new StackPanel { Children = { top, line2 } };

            // Se abre una sesión en la rama actual o en el worktree de otra; las demás no (habría que cambiar de rama).
            var target = branch.Worktree ?? (branch.Current ? folder : null);
            Border row;
            if (target is not null)
            {
                Border? pressed = null;
                pressed = Ui.Press(content, () => ShowNew(target, pressed), branch.Current ? null : (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, branch.Name);
                row = pressed;
                ToolTip.SetTip(row, L.T("Abrir una sesión en {0}", target));
            }
            else row = new Border { Child = content };
            row.Padding = new Thickness(8, 6);
            row.CornerRadius = new CornerRadius(8);
            if (branch.Current) row.Background = _ink.Pill;
            _sideBody.Children.Add(row);
        }
        _sideBody.Children.Add(Hint(L.T("Toque la rama actual o una con worktree para abrir ahí una sesión.")));
    }

    static bool Same(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    // ── Historial ───────────────────────────────────────────────────────────────────────────

    /// <summary>Cuántas sesiones muestra el historial de una vez; «Cargar más» suma otras tantas.</summary>
    const int HistoryPage = 15;
    int _historyShown = HistoryPage;
    string _historyQuery = "";
    /// <summary>La cuenta y la carpeta del historial que se ve (al cambiar, vuelve a la primera página).</summary>
    string? _historyWhere;
    /// <summary>El buscador y las filas: al buscar se rehacen solo las filas (el buscador no pierde el foco).</summary>
    TextBox? _historySearch;
    StackPanel? _historyRows;
    DispatcherTimer? _historyTyping;
    /// <summary>Sube con cada lectura: lo que llega de una vieja (se siguió escribiendo) se descarta.</summary>
    int _historyRead;
    /// <summary>El texto del título de cada fila, para cambiarlo cuando llega el título corto.</summary>
    readonly Dictionary<string, TextBlock> _historyTitles = [];
    /// <summary>Las sesiones cuyo título ya se pidió (una vez por sesión mientras la Console sigue abierta).</summary>
    static readonly HashSet<string> TitlesAsked = [];

    /// <summary>Pide el título corto de una sesión a la bandeja (con el asistente elegido); false si no se pudo pedir. Lo pone la ventana.</summary>
    public Func<string, string, bool>? AskTitle { get; set; }

    /// <summary>Llegó el título corto de una sesión: se guarda y se ve en su fila.</summary>
    public void TitleArrived(string id, string? title)
    {
        if (SessionTitles.Clean(title) is not { } clean) return;
        SessionTitles.Remember(id, clean);
        _historySessions = _historySessions.Select(p => p.Id == id ? p with { Title = clean, Named = true } : p).ToList();
        if (_historyTitles.TryGetValue(id, out var text)) text.Text = clean;
    }

    /// <summary>Lee la página del historial (con la búsqueda) aparte y la muestra.</summary>
    void ReadHistory(Tab tab, int version)
    {
        var configDir = tab.Agent.Options.ConfigDir ?? global::Claupet.Account.DefaultDir;
        var folder = tab.Agent.Options.Folder;
        var (shown, query, read) = (_historyShown, _historyQuery, ++_historyRead);
        _ = Task.Run(() =>
        {
            // Una más de las que se muestran: si llega, hay más para cargar.
            var sessions = ClaudeHistory.List(configDir, folder, shown + 1, query);
            Dispatcher.UIThread.Post(() => { if (version == _sideVersion && read == _historyRead) ShowHistory(tab, sessions); });
        });
    }

    /// <summary>
    /// Las sesiones anteriores de Claude Code en esta carpeta (con esta cuenta), de a HistoryPage, con un buscador arriba:
    /// al tocar una, se retoma. Las que no tienen título se lo piden al asistente; mientras, se ve el pedido recortado.
    /// </summary>
    void ShowHistory(Tab tab, IReadOnlyList<PastSession> sessions)
    {
        if (tab.Agent.Options.Kind != AgentKinds.Claude)
        {
            _sideBody.Children.Clear();
            _historySearch = null;
            _sideBody.Children.Add(Hint(L.T("El historial es el de Claude Code: se ve en una pestaña de Claude Code.")));
            return;
        }
        var (field, rows) = HistoryFrame(tab);
        rows.Children.Clear();
        _historyTitles.Clear();
        var more = sessions.Count > _historyShown;
        IReadOnlyList<PastSession> page = more ? sessions.Take(_historyShown).ToList() : sessions;
        // Sin ninguna sesión (y sin buscar) el buscador no tiene qué buscar.
        field.IsVisible = page.Count > 0 || _historyQuery.Length > 0;
        if (page.Count == 0)
        {
            rows.Children.Add(Hint(_historyQuery.Length > 0
                ? L.T("Ninguna sesión habla de «{0}».", _historyQuery)
                : L.T("Todavía no hay sesiones de Claude Code en esta carpeta con esta cuenta.")));
            return;
        }
        var here = tab.Agent.Transcript.SessionId ?? tab.Agent.Options.Resume;
        var open = OpenSessions();
        var configDir = tab.Agent.Options.ConfigDir ?? global::Claupet.Account.DefaultDir;
        _historySessions = sessions;
        _historyClearable = !page.All(s => open.Contains(s.Id));
        // Borrar todo el historial de esta carpeta (se pide desde el título): pregunta aquí arriba, a la vista.
        if (_confirmClearHistory && _historyClearable)
        {
            var confirm = ConfirmRow(L.T("¿Borrar todo el historial de esta carpeta? No se puede deshacer."), () =>
            {
                _confirmClearHistory = false;
                ShowHistory(tab, sessions);
            }, () =>
            {
                _confirmClearHistory = false;
                var folder = tab.Agent.Options.Folder;
                DeleteHistory(tab, () => ClaudeHistory.DeleteAll(configDir, folder, open) > 0);
            });
            rows.Children.Add(confirm);
            rows.Children.Add(Hint(L.T("Las sesiones abiertas en una pestaña se quedan.")));
            Dispatcher.UIThread.Post(() => confirm.BringIntoView());
        }
        foreach (var past in page)
        {
            if (_deletingSession == past.Id)
            {
                rows.Children.Add(ConfirmRow(L.T("¿Borrarla del historial?"), () =>
                {
                    _deletingSession = null;
                    ShowHistory(tab, sessions);
                }, () =>
                {
                    _deletingSession = null;
                    DeleteHistory(tab, () => ClaudeHistory.Delete(configDir, past.Path));
                }));
                continue;
            }
            rows.Children.Add(HistoryRow(tab, past, sessions, here, open));
        }
        if (more)
        {
            var load = Pill(L.T("Cargar más"), _ink.Pill, _ink.Text, () =>
            {
                _historyShown += HistoryPage;
                ReadHistory(tab, _sideVersion);
            });
            load.HorizontalAlignment = HorizontalAlignment.Center;
            load.Margin = new Thickness(0, 8, 0, 0);
            ToolTip.SetTip(load, L.T("Mostrar {0} sesiones más", HistoryPage));
            rows.Children.Add(load);
        }
        rows.Children.Add(Hint(L.T("Toque una para retomarla en una pestaña nueva, con lo de antes a la vista (también con /resume).")));
        // Los títulos cortos de las que se ven y todavía no tienen (una vez por sesión).
        foreach (var past in page)
            if (!past.Named && past.Prompt.Length > 0 && !TitlesAsked.Contains(past.Id) && AskTitle?.Invoke(past.Id, past.Prompt) == true)
                TitlesAsked.Add(past.Id);
    }

    /// <summary>El buscador arriba y el panel de las filas debajo; si ya están, se dejan (no se pierde lo escrito ni el foco).</summary>
    (Control Field, StackPanel Rows) HistoryFrame(Tab tab)
    {
        if (_historySearch?.Parent is Panel { Parent: { } holder } kept && holder == _sideBody && _historyRows?.Parent == _sideBody)
            return (kept, _historyRows);
        _sideBody.Children.Clear();
        var box = _historySearch = new TextBox { Text = _historyQuery, Watermark = L.T("Buscar en el historial"), MinHeight = 0 };
        Plain(box);
        box.FontFamily = Ink.Sans;
        box.FontSize = 12.5;
        box.Padding = new Thickness(30, 6, 8, 6);
        var field = new Panel { Margin = new Thickness(0, 2, 0, 6) };
        field.Children.Add(new Border { Background = _ink.Pill, CornerRadius = new CornerRadius(9) });
        field.Children.Add(box);
        var lens = Icon(SearchIcon, _ink.Tertiary, 13, 1.8);
        lens.HorizontalAlignment = HorizontalAlignment.Left;
        lens.VerticalAlignment = VerticalAlignment.Center;
        lens.Margin = new Thickness(10, 0, 0, 0);
        lens.IsHitTestVisible = false;
        field.Children.Add(lens);
        ToolTip.SetTip(field, L.T("Busca en los títulos y en lo que se pidió"));
        box.TextChanged += (_, _) =>
        {
            // Se busca al dejar de escribir un momento, no con cada tecla.
            _historyTyping?.Stop();
            _historyTyping = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _historyTyping.Tick += (_, _) =>
            {
                _historyTyping?.Stop();
                var query = box.Text?.Trim() ?? "";
                if (query == _historyQuery || _historySearch != box) return;
                _historyQuery = query;
                _historyShown = HistoryPage;
                ReadHistory(tab, _sideVersion);
            };
            _historyTyping.Start();
        };
        box.KeyDown += (_, e) =>
        {
            // Esc borra la búsqueda.
            if (e.Key != Avalonia.Input.Key.Escape || string.IsNullOrEmpty(box.Text)) return;
            box.Text = "";
            e.Handled = true;
        };
        _sideBody.Children.Add(field);
        _historyRows = new StackPanel { Spacing = 2 };
        _sideBody.Children.Add(_historyRows);
        return (field, _historyRows);
    }

    /// <summary>Una sesión del historial: su título, cuándo y la papelera al pasar el mouse; al detenerse encima, lo que se pidió.</summary>
    Control HistoryRow(Tab tab, PastSession past, IReadOnlyList<PastSession> sessions, string? here, HashSet<string> open)
    {
        var text = new StackPanel { Spacing = 1 };
        var title = Ui.Text(past.Title, 12.5, _ink.Text);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        _historyTitles[past.Id] = title;
        text.Children.Add(title);
        var isOpen = open.Contains(past.Id);
        var when = Ago(past.LastUsed) + (past.Id == here ? " · " + L.T("esta pestaña") : isOpen ? " · " + L.T("abierta") : "");
        text.Children.Add(Ui.Text(when, 11.5, past.Id == here ? _ink.Accent : _ink.Tertiary));
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        content.Children.Add(text);
        // La papelera al pasar el mouse; la de una sesión abierta en una pestaña no está (Claude Code la sigue escribiendo).
        Border? trash = null;
        if (!isOpen)
        {
            trash = Tool(ChatPane.TrashIcon, L.T("Borrar del historial"), () =>
            {
                _deletingSession = past.Id;
                _confirmClearHistory = false;
                ShowHistory(tab, sessions);
            });
            trash.Opacity = 0;
            trash.Margin = new Thickness(6, 0, -2, 0);
            trash.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(trash, 1);
            content.Children.Add(trash);
        }
        var row = Ui.Press(content, () => Resume(tab, past), (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, past.Title);
        row.Padding = new Thickness(8, 6);
        row.CornerRadius = new CornerRadius(8);
        if (trash is not null)
        {
            row.PointerEntered += (_, _) => trash.Opacity = 1;
            row.PointerExited += (_, _) => trash.Opacity = 0;
        }
        var tip = past.Prompt.Length > 0 ? past.Prompt : past.Title;
        ToolTip.SetTip(row, tip.Length > 300 ? tip[..300] + "…" : tip);
        return row;
    }

    /// <summary>Lo último que mostró el historial, para volver a mostrarlo desde el título.</summary>
    IReadOnlyList<PastSession> _historySessions = [];
    /// <summary>Hay alguna sesión que no esté abierta en una pestaña: se puede borrar.</summary>
    bool _historyClearable;

    /// <summary>El título «Historial»: al tocarlo, sus opciones.</summary>
    Border HistoryTitle(Tab tab)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Ui.Text(L.T("Historial"), 12.5, _ink.Secondary));
        row.Children.Add(Icon(ChevronDownIcon, _ink.Tertiary, 9, 2));
        Border? title = null;
        title = Ui.Press(row, () => DropList.Show(title!, _ink,
            [_historyClearable
                ? new DropList.Entry("borrar", L.T("Borrar todo el historial"), Danger: true)
                : new DropList.Entry("nada", L.T("No hay sesiones que se puedan borrar"), Action: true)], picked =>
            {
                if (picked != "borrar") return;
                _confirmClearHistory = true;
                _deletingSession = null;
                ShowHistory(tab, _historySessions);
            }), (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, L.T("Opciones del historial"));
        title.Padding = new Thickness(8, 4);
        title.CornerRadius = new CornerRadius(7);
        title.HorizontalAlignment = HorizontalAlignment.Right;
        title.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(title, L.T("Opciones del historial"));
        return title;
    }

    /// <summary>La sesión del historial que pregunta «¿Borrarla?» en su fila (null: ninguna).</summary>
    string? _deletingSession;
    /// <summary>«Borrar todo el historial» está preguntando si se borra.</summary>
    bool _confirmClearHistory;

    /// <summary>Las sesiones de Claude Code abiertas en alguna pestaña: no se borran.</summary>
    HashSet<string> OpenSessions()
    {
        var open = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in _tabs)
        {
            if (t.Agent.Transcript.SessionId is { } id) open.Add(id);
            if (t.Agent.Options.Resume is { } resumed) open.Add(resumed);
        }
        return open;
    }

    /// <summary>Borra en segundo plano y vuelve a leer el historial.</summary>
    void DeleteHistory(Tab tab, Func<bool> delete)
    {
        Loading();
        _ = Task.Run(() =>
        {
            delete();
            Dispatcher.UIThread.Post(() => LoadSide(tab));
        });
    }

    /// <summary>Una pregunta con Cancelar y Borrar (en rojo), ahí mismo en la lista.</summary>
    Control ConfirmRow(string question, Action cancel, Action confirm)
    {
        var panel = new StackPanel { Spacing = 8 };
        var text = Ui.Text(question, 12.5, _ink.Text);
        text.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        var no = Ui.Press(Ui.Text(L.T("Cancelar"), 12, _ink.Secondary), cancel, (b, on) => b.Background = on ? _ink.Track : Brushes.Transparent, L.T("Cancelar"));
        no.Padding = new Thickness(10, 4);
        no.CornerRadius = new CornerRadius(999);
        buttons.Children.Add(no);
        IBrush onDanger = _ink.Dark ? new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x13)) : Brushes.White;
        var yes = Ui.Press(Ui.Text(L.T("Borrar"), 12, onDanger, FontWeight.SemiBold), confirm, (b, on) => b.Opacity = on ? 0.88 : 1, L.T("Borrar"));
        yes.Background = _ink.Danger;
        yes.Padding = new Thickness(12, 4);
        yes.CornerRadius = new CornerRadius(999);
        buttons.Children.Add(yes);
        panel.Children.Add(buttons);
        return new Border { Child = panel, Padding = new Thickness(8, 8), CornerRadius = new CornerRadius(8), Background = _ink.Pill };
    }

    /// <summary>Cuándo: «hace 5 min», «hace 3 h», «ayer» o la fecha.</summary>
    static string Ago(DateTime when)
    {
        var span = DateTime.Now - when;
        return span.TotalMinutes < 1 ? L.T("recién")
            : span.TotalHours < 1 ? L.T("hace {0} min", (int)span.TotalMinutes)
            : span.TotalDays < 1 && when.Date == DateTime.Today ? L.T("hace {0} h", (int)span.TotalHours)
            : when.Date == DateTime.Today.AddDays(-1) ? L.T("ayer, {0:HH:mm}", when)
            : when.ToString("d MMM, HH:mm", System.Globalization.CultureInfo.CurrentCulture);
    }

    // ── Cambios ─────────────────────────────────────────────────────────────────────────────

    void ShowChanges(string folder, IReadOnlyList<GitChange> changes)
    {
        _sideBody.Children.Clear();
        if (changes.Count == 0)
        {
            _sideBody.Children.Add(Hint(L.T("Sin cambios: todo está en un commit.")));
            return;
        }
        foreach (var change in changes)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var file = Ui.Text(Path.GetFileName(change.Path), 12.5, _ink.Text, family: Mono);
            file.TextTrimming = TextTrimming.CharacterEllipsis;
            name.Children.Add(file);
            var where = change.New ? L.T("nuevo") : Path.GetDirectoryName(change.Path)?.Replace('\\', '/') ?? "";
            if (where.Length > 0)
            {
                var small = Ui.Text(where, 11, _ink.Tertiary);
                small.TextTrimming = TextTrimming.CharacterEllipsis;
                small.MaxWidth = 120;
                small.VerticalAlignment = VerticalAlignment.Center;
                name.Children.Add(small);
            }
            row.Children.Add(name);
            var nums = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            if (change.Binary) nums.Children.Add(Ui.Text(L.T("binario"), 11.5, _ink.Tertiary));
            if (change.Added > 0) nums.Children.Add(Ui.Text($"+{change.Added}", 11.5, Added, family: Mono));
            if (change.Removed > 0) nums.Children.Add(Ui.Text($"−{change.Removed}", 11.5, _ink.Danger, family: Mono));
            Grid.SetColumn(nums, 1);
            row.Children.Add(nums);
            var open = _peek == change.Path;
            var press = Ui.Press(row, () => Peek(folder, change, changes), open ? null : (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, change.Path);
            press.Padding = new Thickness(8, 6);
            press.CornerRadius = new CornerRadius(8);
            if (open) press.Background = _ink.Pill;
            ToolTip.SetTip(press, change.Path);
            _sideBody.Children.Add(press);
            if (open && _peekLines is { } lines) _sideBody.Children.Add(PeekView(lines));
        }
        _sideBody.Children.Add(Hint(L.T("{0} archivos · +{1} −{2}", changes.Count, changes.Sum(c => c.Added), changes.Sum(c => c.Removed))));
    }

    /// <summary>Toca un archivo: se ve su cambio debajo (o se cierra, si ya se veía).</summary>
    void Peek(string folder, GitChange change, IReadOnlyList<GitChange> changes)
    {
        if (_peek == change.Path)
        {
            _peek = null;
            _peekLines = null;
            ShowChanges(folder, changes);
            return;
        }
        _peek = change.Path;
        _peekLines = null;
        var version = _sideVersion;
        _ = Task.Run(async () =>
        {
            var lines = await GitRepo.DiffAsync(folder, change);
            Dispatcher.UIThread.Post(() =>
            {
                if (version != _sideVersion || _peek != change.Path) return;
                _peekLines = lines;
                ShowChanges(folder, changes);
            });
        });
    }

    Control PeekView(IReadOnlyList<string> lines)
    {
        var panel = new StackPanel();
        foreach (var line in lines)
        {
            var sign = line.Length > 0 ? line[0] : ' ';
            var text = Ui.Text(line.Length > 0 ? line : " ", 11.5, sign == '+' ? Added : sign == '-' ? _ink.Danger : _ink.Secondary, family: Mono);
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            panel.Children.Add(new Border
            {
                Background = sign == '+' ? Translucent(Added, 0.10) : sign == '-' ? Translucent(_ink.Danger, 0.10) : Brushes.Transparent,
                Padding = new Thickness(6, 0),
                Child = text,
            });
        }
        return new Border
        {
            Background = _ink.Surface,
            BorderBrush = _ink.Hairline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(0, 4),
            Margin = new Thickness(4, 2, 4, 6),
            ClipToBounds = true,
            Child = panel,
        };
    }

    // ── Procesos ────────────────────────────────────────────────────────────────────────────

    /// <summary>Cada sesión de la Terminal con su memoria y, debajo, lo que abrió (un servidor, sus MCP…).</summary>
    /// <summary>
    /// Lee los procesos fuera del hilo de la interfaz (cada 3 s). Si son los mismos de antes solo cambian los MB: rehacer
    /// las filas con cada vuelta hacía parpadear el puntero y perder el clic en «Detener».
    /// </summary>
    void ShowProcesses()
    {
        if (_sideGuard?.Held == true) return; // se intenta en la próxima vuelta
        var version = _sideVersion;
        var running = _tabs.Where(t => t.Agent.ProcessId is not null)
            .Select(t => (Tab: t, Pid: t.Agent.ProcessId!.Value, Label: $"{t.Account} · {t.Project}")).ToList();
        _ = Task.Run(() =>
        {
            var lines = new List<ProcessLine>();
            if (running.Count == 0) return lines;
            var all = ProcessTree.Snapshot();
            foreach (var (tab, pid, label) in running)
            {
                var children = ProcessTree.Descendants(all, pid).Where(p => p.Name is not ("conhost" or "cmd")).ToList();
                // La memoria de la sesión es la de Claude Code: su proceso y los suyos de node/claude (no lo que abrió aparte).
                var own = new[] { pid }.Concat(children.Where(c => c.Name is "claude" or "node" && c.ParentPid == pid).Select(c => c.Pid));
                lines.Add(new(pid, "claude", label, own.Sum(p => ProcessTree.Memory(p) ?? 0), 0, tab));
                foreach (var child in children.Where(c => !(c.Name is "claude" or "node" && c.ParentPid == pid)))
                    lines.Add(new(child.Pid, child.Name, $"pid {child.Pid}", ProcessTree.Memory(child.Pid) ?? 0, 1, null));
            }
            return lines;
        }).ContinueWith(read =>
        {
            if (!read.IsCompletedSuccessfully) return;
            Dispatcher.UIThread.Post(() => { if (version == _sideVersion) PaintProcesses(read.Result); });
        }, TaskScheduler.Default);
    }

    static string Megabytes(long memory) => L.T("{0:0} MB", memory / 1048576.0);

    void PaintProcesses(IReadOnlyList<ProcessLine> lines)
    {
        var key = string.Join("|", lines.Select(l => $"{l.Depth}:{l.Pid}:{l.Name}:{l.Detail}"));
        if (_processShown is { } shown && shown.Body == _sideBody && shown.Key == key)
        {
            foreach (var line in lines)
                if (_processMb.TryGetValue(line.Pid, out var mb)) mb.Text = Megabytes(line.Memory);
            return;
        }
        if (_sideGuard?.Held == true) return;
        _processShown = (_sideBody, key);
        _processMb.Clear();
        _sideBody.Children.Clear();
        if (lines.Count == 0)
        {
            _sideBody.Children.Add(Hint(L.T("Ninguna sesión está corriendo.")));
            return;
        }
        foreach (var line in lines)
        {
            var pid = line.Pid;
            var row = line.Session is { } tab
                ? ProcessRow(line.Name, line.Detail, line.Memory, L.T("Detener la sesión"), () => tab.Agent.Stop(), 0, out var mb)
                : ProcessRow(line.Name, line.Detail, line.Memory, L.T("Detener {0}", line.Name), () =>
                {
                    ProcessTree.Stop(pid);
                    ShowProcesses();
                }, 1, out mb);
            _processMb[pid] = mb;
            _sideBody.Children.Add(row);
        }
        _sideBody.Children.Add(Hint(L.T("Se actualiza cada 3 segundos. Detener corta el proceso y lo que abrió.")));
    }

    Control ProcessRow(string name, string detail, long memory, string stopName, Action stop, int depth, out TextBlock mb)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(depth * 14, 0, 0, 0) };
        var text = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Text(name, 12.5, depth == 0 ? _ink.Text : _ink.Secondary, family: Mono));
        var small = Ui.Text(detail, 11, _ink.Tertiary);
        small.TextTrimming = TextTrimming.CharacterEllipsis;
        small.VerticalAlignment = VerticalAlignment.Center;
        text.Children.Add(small);
        row.Children.Add(text);
        mb = Ui.Text(Megabytes(memory), 11.5, _ink.Tertiary, family: Mono);
        mb.VerticalAlignment = VerticalAlignment.Center;
        mb.Margin = new Thickness(8, 0, 6, 0);
        Grid.SetColumn(mb, 1);
        row.Children.Add(mb);
        var button = Ui.Press(Icon(StopSquare, _ink.Danger, 11, 2), stop, (b, on) => b.Background = on ? _ink.Track : Brushes.Transparent, stopName);
        button.Width = 24;
        button.Height = 24;
        button.CornerRadius = new CornerRadius(999);
        ToolTip.SetTip(button, stopName);
        Grid.SetColumn(button, 2);
        row.Children.Add(button);
        return new Border { Padding = new Thickness(8, 5), CornerRadius = new CornerRadius(8), Child = row };
    }
}
