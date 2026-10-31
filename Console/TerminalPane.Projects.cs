namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

/// <summary>
/// La lista de proyectos de la Terminal: las carpetas vinculadas con sus pestañas, que se pliegan y despliegan (plegado:
/// cuántas pestañas y un punto por cada una; desplegado: la rama y cómo está el repositorio, «↑2 · 3 sin commit»). Su
/// menú «⋯» abre una sesión ahí, abre la carpeta o lo desvincula (solo lo quita de la lista).
/// </summary>
sealed partial class TerminalPane
{
    const string ChevronDownIcon = "M6,9 L12,15 L18,9";
    const string ChevronRightIcon = "M9,6 L15,12 L9,18";
    const string DotsIcon = "M5,12 H5.01 M12,12 H12.01 M19,12 H19.01";
    const string FoldAllIcon = "M7,4 L12,9 L17,4 M7,20 L12,15 L17,20";
    const string UnfoldAllIcon = "M7,9 L12,4 L17,9 M7,15 L12,20 L17,15";

    readonly TerminalProjects _projects = TerminalProjects.Load();
    /// <summary>Cómo está el repositorio de cada proyecto y cuándo se leyó.</summary>
    readonly Dictionary<string, (GitStatus? Status, DateTime At)> _git = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _gitReading = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Los proyectos en el orden de la lista: los vinculados y, por si acaso, las carpetas con pestañas que no lo estén.</summary>
    List<string> Projects() =>
        _projects.Folders.Concat(_tabs.Select(t => t.Agent.Options.Folder).Where(f => !_projects.Has(f)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    List<Tab> TabsOf(string folder) =>
        _tabs.Where(t => string.Equals(Path.TrimEndingDirectorySeparator(t.Agent.Options.Folder), Path.TrimEndingDirectorySeparator(folder),
            StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Lo arrastró el borde de la lista: se recuerda la medida.</summary>
    public void SetListWidth(double width)
    {
        _projects.ListWidth = Math.Clamp(width, TerminalProjects.MinWidth, TerminalProjects.MaxWidth);
        _projects.Save();
    }

    /// <summary>Al abrir una sesión, su carpeta queda vinculada.</summary>
    void LinkProject(string folder)
    {
        if (_projects.Link(folder)) _projects.Save();
        RefreshGit(folder, force: true);
    }

    void RenderList()
    {
        _list.Children.Clear();
        var projects = Projects();
        foreach (var folder in projects)
        {
            var tabs = TabsOf(folder);
            if (_look.IconsOnly)
            {
                // Solo íconos: una raya separa los proyectos y cada pestaña es su ícono, con el resto al pasar el cursor.
                if (tabs.Count == 0) continue;
                if (_list.Children.Count > 0)
                    _list.Children.Add(new Border { Height = 1, Background = _ink.Hairline, Margin = new Thickness(14, 6) });
                foreach (var tab in tabs) _list.Children.Add(TabIcon(tab));
                continue;
            }
            RefreshGit(folder);
            var folded = _projects.IsFolded(folder);
            _list.Children.Add(ProjectHead(folder, tabs, folded));
            if (folded) continue;
            foreach (var tab in tabs) _list.Children.Add(TabItem(tab));
            if (tabs.Count == 0)
            {
                var open = Ui.Press(Ui.Text(L.T("Sesión nueva aquí"), 12.5, _ink.Tertiary), () => ShowNew(folder),
                    (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, L.T("Sesión nueva aquí"));
                open.Padding = new Thickness(30, 5, 10, 5);
                open.CornerRadius = new CornerRadius(8);
                _list.Children.Add(open);
            }
        }
        if (projects.Count == 0 && !_look.IconsOnly)
        {
            var empty = Ui.Text(L.T("Sin sesiones abiertas"), 13, _ink.Tertiary);
            empty.Margin = new Thickness(10, 8);
            _list.Children.Add(empty);
        }
    }

    /// <summary>La fila de un proyecto: la flecha, el nombre, la rama, el estado del repositorio y el «⋯» al pasar el cursor.</summary>
    Control ProjectHead(string folder, IReadOnlyList<Tab> tabs, bool folded)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)) is { Length: > 0 } n ? n : folder;
        var status = _git.TryGetValue(folder, out var git) ? git.Status : null;
        var branch = status?.Branch ?? tabs.FirstOrDefault()?.Branch ?? GitBranch.Of(folder);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var chevron = Icon(folded ? ChevronRightIcon : ChevronDownIcon, _ink.Tertiary, 11, 2);
        chevron.Margin = new Thickness(0, 0, 7, 0);
        row.Children.Add(chevron);

        var text = new StackPanel { Spacing = 1 };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var label = Ui.Text(name, 12.5, _ink.Secondary, FontWeight.SemiBold);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Children.Add(label);
        if (branch is not null && !folded)
        {
            title.Children.Add(Icon(BranchIcon, _ink.Tertiary, 11, 1.8));
            var branchText = Ui.Text(branch, 12, _ink.Tertiary, family: Mono);
            branchText.TextTrimming = TextTrimming.CharacterEllipsis;
            branchText.MaxWidth = 110;
            title.Children.Add(branchText);
        }
        text.Children.Add(title);
        if (!folded && status is not null)
            text.Children.Add(Ui.Text(status.Summary, 11.5, status.Changed > 0 || status.Ahead > 0 ? _ink.Secondary : _ink.Tertiary));
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        if (folded && tabs.Count > 0)
        {
            // Plegado: cuántas pestañas y un punto por cada una, con su estado.
            right.Children.Add(Ui.Text(tabs.Count.ToString(), 11.5, _ink.Tertiary));
            foreach (var tab in tabs.Take(6))
            {
                var t = tab.Agent.Transcript;
                var dot = new Border
                {
                    Width = 6,
                    Height = 6,
                    CornerRadius = new CornerRadius(99),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = Waiting(t) ? _ink.Warn : t.Busy ? _ink.Accent : tab.Agent.Running ? _ink.Track : _ink.Hairline,
                };
                if (t.Busy) Pulse(dot);
                right.Children.Add(dot);
            }
        }
        Border? more = null;
        more = Ui.Press(Icon(DotsIcon, _ink.Secondary, 14, 3), () => ProjectMenu(more!, folder, tabs.Count),
            (b, on) => b.Background = on ? _ink.Track : Brushes.Transparent, L.T("Más opciones de {0}", name));
        more.Width = 24;
        more.Height = 24;
        more.CornerRadius = new CornerRadius(999);
        more.Opacity = 0;
        ToolTip.SetTip(more, L.T("Más opciones"));
        right.Children.Add(more);
        Grid.SetColumn(right, 2);
        row.Children.Add(right);

        var head = Ui.Press(row, () =>
        {
            _projects.SetFolded(folder, !folded);
            _projects.Save();
            RenderList();
        }, (b, on) =>
        {
            b.Background = on ? _ink.Pill : Brushes.Transparent;
            more.Opacity = on ? 1 : 0;
        }, folded ? L.T("Desplegar {0}", name) : L.T("Plegar {0}", name));
        head.Padding = new Thickness(8, 5, 6, 5);
        head.Margin = new Thickness(2, 8, 2, 2);
        head.CornerRadius = new CornerRadius(8);
        ToolTip.SetTip(head, folder);
        return head;
    }

    void ProjectMenu(Control anchor, string folder, int sessions)
    {
        DropList.Show(anchor, _ink,
        [
            new("nueva", L.T("Sesión nueva aquí"), Action: true),
            new("carpeta", L.T("Abrir la carpeta"), Action: true),
            new("desvincular", sessions > 0 ? L.T("Desvincular (cierra sus {0} sesiones)", sessions) : L.T("Desvincular el proyecto"), Danger: true),
        ], picked =>
        {
            switch (picked)
            {
                case "nueva": ShowNew(folder); break;
                case "carpeta": _ = TopLevel.GetTopLevel(this)?.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder)); break;
                case "desvincular": Unlink(folder); break;
            }
        });
    }

    /// <summary>Lo quita de la lista (y cierra sus sesiones). La carpeta no se toca.</summary>
    void Unlink(string folder)
    {
        foreach (var tab in TabsOf(folder))
        {
            tab.Agent.Dispose();
            _tabs.Remove(tab);
            if (_current == tab) _current = null;
        }
        _projects.Unlink(folder);
        _projects.Save();
        _git.Remove(folder);
        _current ??= _tabs.FirstOrDefault();
        if (_current is null) _creating = true;
        Build();
        RenderList();
        TabsChanged?.Invoke();
    }

    /// <summary>Pliega todos (o, si ya lo están, los despliega).</summary>
    void FoldAll()
    {
        var projects = Projects();
        var fold = !projects.All(_projects.IsFolded);
        foreach (var folder in projects) _projects.SetFolded(folder, fold);
        _projects.Save();
        RenderList();
        TabsChanged?.Invoke();
    }

    bool AllFolded => Projects() is { Count: > 0 } projects && projects.All(_projects.IsFolded);

    /// <summary>Vuelve a leer el estado del repositorio si es viejo (más de 20 s) o se pide; al llegar, rehace la lista.</summary>
    void RefreshGit(string folder, bool force = false)
    {
        if (_gitReading.Contains(folder)) return;
        if (!force && _git.TryGetValue(folder, out var known) && DateTime.UtcNow - known.At < TimeSpan.FromSeconds(20)) return;
        if (!Directory.Exists(folder)) return;
        _gitReading.Add(folder);
        _ = Task.Run(async () =>
        {
            var status = await GitStatus.ReadAsync(folder);
            Dispatcher.UIThread.Post(() =>
            {
                _gitReading.Remove(folder);
                var changed = !_git.TryGetValue(folder, out var old) || old.Status != status;
                _git[folder] = (status, DateTime.UtcNow);
                if (changed) RenderList();
            });
        });
    }
}
