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

    readonly StackPanel _sideBody = new() { Spacing = 2 };
    DispatcherTimer? _processTimer;
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
        foreach (var (id, icon, name) in new[] { ("ramas", RamasIcon, L.T("Ramas")), ("cambios", CambiosIcon, L.T("Cambios")), ("procesos", ProcesosIcon, L.T("Procesos")) })
        {
            var selected = _projects.PanelTab == id;
            var button = Ui.Press(Icon(icon, selected ? _ink.Text : _ink.Tertiary, 15, 1.7), () =>
            {
                _projects.PanelTab = id;
                _projects.Save();
                Build();
            }, selected ? null : (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, name);
            button.Width = 30;
            button.Height = 28;
            button.CornerRadius = new CornerRadius(8);
            if (selected) button.Background = _ink.Pill;
            ToolTip.SetTip(button, name);
            tabs.Children.Add(button);
        }
        bar.Children.Add(tabs);
        var title = Ui.Text(_projects.PanelTab switch { "cambios" => L.T("Cambios"), "procesos" => L.T("Procesos"), _ => L.T("Ramas") }, 12.5, _ink.Tertiary);
        title.HorizontalAlignment = HorizontalAlignment.Right;
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 1);
        bar.Children.Add(title);
        panel.Children.Add(bar);

        _sideBody.Children.Clear();
        _sideBody.Margin = new Thickness(8, 0, 10, 12);
        var scroll = new ScrollViewer { Content = _sideBody, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1);
        panel.Children.Add(scroll);

        var border = new Border { BorderBrush = _ink.Hairline, BorderThickness = new Thickness(1, 0, 0, 0), Child = panel };
        Grid.SetColumn(border, 1);
        grid.Children.Add(border);
        LoadSide(tab);
        return grid;
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
                row = Ui.Press(content, () => ShowNew(target), branch.Current ? null : (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, branch.Name);
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
    void ShowProcesses()
    {
        _sideBody.Children.Clear();
        var running = _tabs.Where(t => t.Agent.ProcessId is not null).ToList();
        if (running.Count == 0)
        {
            _sideBody.Children.Add(Hint(L.T("Ninguna sesión está corriendo.")));
            return;
        }
        var all = ProcessTree.Snapshot();
        foreach (var tab in running)
        {
            var pid = tab.Agent.ProcessId!.Value;
            var children = ProcessTree.Descendants(all, pid).Where(p => p.Name is not ("conhost" or "cmd")).ToList();
            // La memoria de la sesión es la de Claude Code: su proceso y los suyos de node/claude (no lo que abrió aparte).
            var own = new[] { pid }.Concat(children.Where(c => c.Name is "claude" or "node" && c.ParentPid == pid).Select(c => c.Pid));
            var memory = own.Sum(p => ProcessTree.Memory(p) ?? 0);
            _sideBody.Children.Add(ProcessRow("claude", $"{tab.Account} · {tab.Project}", memory, L.T("Detener la sesión"), () => tab.Agent.Stop(), 0));
            foreach (var child in children.Where(c => !(c.Name is "claude" or "node" && c.ParentPid == pid)))
                _sideBody.Children.Add(ProcessRow(child.Name, $"pid {child.Pid}", ProcessTree.Memory(child.Pid) ?? 0, L.T("Detener {0}", child.Name), () =>
                {
                    ProcessTree.Stop(child.Pid);
                    ShowProcesses();
                }, 1));
        }
        _sideBody.Children.Add(Hint(L.T("Se actualiza cada 3 segundos. Detener corta el proceso y lo que abrió.")));
    }

    Control ProcessRow(string name, string detail, long memory, string stopName, Action stop, int depth)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(depth * 14, 0, 0, 0) };
        var text = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Text(name, 12.5, depth == 0 ? _ink.Text : _ink.Secondary, family: Mono));
        var small = Ui.Text(detail, 11, _ink.Tertiary);
        small.TextTrimming = TextTrimming.CharacterEllipsis;
        small.VerticalAlignment = VerticalAlignment.Center;
        text.Children.Add(small);
        row.Children.Add(text);
        var mb = Ui.Text(L.T("{0:0} MB", memory / 1048576.0), 11.5, _ink.Tertiary, family: Mono);
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
