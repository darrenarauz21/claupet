namespace Claupet;

using Avalonia.Threading;

/// <summary>
/// La búsqueda de la Console. Ctrl+K en Ajustes busca en sus secciones y filas, y lleva a la fila (que se marca un
/// momento). Ctrl+Shift+K, desde cualquier sección, busca además las secciones, las pestañas de la Terminal, los archivos
/// del editor, los canvas, los planes y los chats; los canvas y los planes se leen del disco aparte y se suman al llegar.
/// </summary>
sealed partial class ConsoleWindow
{
    /// <summary>Ctrl+K en Ajustes.</summary>
    void OpenSettingsSearch() => _search.Open(_ink, L.T("Buscar en Ajustes"), SettingsPicks(global: false));

    /// <summary>Ctrl+Shift+K en cualquier sección.</summary>
    void OpenGlobalSearch()
    {
        var plans = _projects.Entries;
        var opened = _search.Open(_ink, L.T("Buscar en Claupet: secciones, ajustes, sesiones, canvas, planes…"), GlobalPicks([], plans));
        // Lo que está en disco va aparte (los planes recorren las carpetas): la búsqueda no espera y se completa al llegar.
        var scan = _projects.FindPlansAsync();
        _ = Task.Run(async () =>
        {
            IReadOnlyList<CanvasSummary> canvases = [];
            try { canvases = CanvasStore.Default.List(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            try { plans = await scan; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            Dispatcher.UIThread.Post(() => _search.SetPicks(opened, GlobalPicks(canvases, plans)));
        });
    }

    /// <summary>Todo lo de la búsqueda global, en el orden en que se ve sin escribir nada.</summary>
    List<SearchPick> GlobalPicks(IReadOnlyList<CanvasSummary> canvases, IReadOnlyList<PlanEntry> plans)
    {
        var picks = new List<SearchPick>
        {
            new(ChatIcon, L.T("Chat"), L.T("Sección"), () => SetMode(Mode.Chat)),
            new(TerminalIcon, L.T("Terminal"), L.T("Sección"), () => SetMode(Mode.Terminal)),
            new(EditorIcon, L.T("Editor"), L.T("Sección"), () => SetMode(Mode.Editor)),
            new(CanvasIcon, "Canvas", L.T("Sección"), () => SetMode(Mode.Canvas)),
            new(ProjectsIcon, L.T("Proyectos"), L.T("Sección"), () => SetMode(Mode.Projects)),
            new(SpecialistsPane.Icon, L.T("Especialistas"), L.T("Sección"), () => SetMode(Mode.Specialists)),
            new(SettingsIcon, L.T("Ajustes"), L.T("Sección"), () => SetMode(Mode.Settings)),
        };
        foreach (var (title, project, detail, show) in _terminal.SearchTabs())
            picks.Add(new(TerminalIcon, title, $"{L.T("Terminal")} · {project}", () => { show(); SetMode(Mode.Terminal); }, detail));
        foreach (var doc in _editor.Docs)
        {
            var open = doc;
            picks.Add(new(EditorIcon, doc.Title, L.T("Editor"), () => { SetMode(Mode.Editor); _editor.Select(open); }, doc.Path ?? ""));
        }
        foreach (var canvas in canvases)
        {
            var id = canvas.Id;
            var where = canvas.Project is { Length: > 0 } project ? $"Canvas · {Path.GetFileName(Path.TrimEndingDirectorySeparator(project))}" : "Canvas";
            // Primero el canvas y después la sección: así se dibuja una sola vez, el pedido.
            picks.Add(new(CanvasIcon, canvas.Title, where, () => { _canvas.Show(id); SetMode(Mode.Canvas); }));
        }
        foreach (var plan in plans)
        {
            var path = plan.Path;
            picks.Add(new(ProjectsIcon, plan.Name, $"{L.T("Plan")} · {Path.GetFileName(Path.TrimEndingDirectorySeparator(plan.Folder))}",
                () => { _ = _projects.ShowPlanAsync(path); SetMode(Mode.Projects); }, path));
        }
        foreach (var chat in _chats)
        {
            var id = chat.Id;
            picks.Add(new(ChatIcon, chat.Title, L.T("Chat"), () => { SetMode(Mode.Chat); OpenChat(id); }, chat.Preview));
        }
        picks.AddRange(SettingsPicks(global: true));
        foreach (var path in _editor.Recents.Where(p => _editor.Docs.All(d => !string.Equals(d.Path, p, StringComparison.OrdinalIgnoreCase))).Take(20))
        {
            var file = path;
            picks.Add(new(EditorIcon, Path.GetFileName(path), L.T("Editor · reciente"), () => { SetMode(Mode.Editor); _editor.Open(file); }, path));
        }
        return picks;
    }

    /// <summary>Las secciones de Ajustes y sus filas (ConsoleSearch.Settings) y la de la Terminal, que es de la Console.</summary>
    List<SearchPick> SettingsPicks(bool global)
    {
        var settings = L.T("Ajustes");
        string Place(string where) => !global ? where : where == settings ? settings : $"{settings} › {where}";
        var picks = ConsoleSearch.Settings(_pages).Select(hit => new SearchPick(SettingsIcon, hit.Name, Place(hit.Where), () => GoToSetting(hit), hit.Extra)).ToList();
        if (_pages.Count > 0)
            picks.Add(new(TerminalIcon, L.T("Terminal"), Place(settings), () =>
            {
                SetMode(Mode.Settings);
                SelectTerminal();
            }, L.T("Paleta de la terminal")));
        return picks;
    }

    /// <summary>Lleva a lo elegido en Ajustes: su sección y, si es una fila, la fila (desplazada a la vista y marcada).</summary>
    void GoToSetting(SettingsHit hit)
    {
        if (_mode != Mode.Settings) SetMode(Mode.Settings);
        Select(hit.Page);
        if (hit.Index < 0)
        {
            _scroll.Offset = default;
            return;
        }
        Dispatcher.UIThread.Post(() => _view.Reveal(hit.Index, _ink), DispatcherPriority.Loaded);
    }

    /// <summary>Para las pruebas: la búsqueda de la ventana.</summary>
    internal SearchPalette Search => _search;
}
