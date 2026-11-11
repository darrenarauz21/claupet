namespace Claupet;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

/// <summary>
/// La pestaña nueva, como en el diseño: el botón + abre un menú chico, no un formulario. Arriba, el proyecto (se cambia tocándolo); después, qué abrir (Claude Code,
/// los otros agentes instalados o una de las terminales del equipo); con Claude Code, la cuenta, el modelo y el esfuerzo
/// en botones; abajo, «Abrir». Sin pestañas, la sesión muestra un vacío con el mismo
/// botón. Ctrl+T lo abre desde cualquier lado de la Terminal.
/// </summary>
sealed partial class TerminalPane
{
    /// <summary>El botón + de la lista: de ahí sale el menú.</summary>
    Border? _addButton;
    /// <summary>El menú abierto (otro toque al + lo cierra).</summary>
    Popup? _newMenu;

    /// <summary>La marca de cada agente, como en la lista: ✻ Claude Code, cx Codex, ✦ Gemini, gh Copilot, oc OpenCode.</summary>
    static string AgentMark(string kind) => kind switch
    {
        AgentKinds.Codex => "cx",
        AgentKinds.Gemini => "✦",
        AgentKinds.Copilot => "gh",
        AgentKinds.OpenCode => "oc",
        _ => "✻",
    };

    /// <summary>El esfuerzo en palabras (Claude Code los llama low, medium, high, xhigh y max).</summary>
    static string EffortLabel(string? effort) => effort switch
    {
        null or "" => L.T("por defecto"),
        "low" => L.T("bajo"),
        "medium" => L.T("medio"),
        "high" => L.T("alto"),
        "xhigh" => L.T("muy alto"),
        "max" => L.T("máximo"),
        _ => effort,
    };

    /// <summary>La terminal que abre «Nueva terminal aquí»: la de Ajustes › Terminal o, si no está, la primera del equipo.</summary>
    ShellChoice? DefaultShell =>
        Shells.Available().FirstOrDefault(s => s.Id == _look.DefaultShell) ?? Shells.Available().FirstOrDefault();

    void ShowNew() => ShowNew(null);

    /// <summary>Abre el menú de la pestaña nueva bajo anchor (el +, si no se dice), para abrir en folder (o donde se está).</summary>
    void ShowNew(string? folder, Control? anchor = null)
    {
        if (_newMenu is { IsOpen: true } open)
        {
            open.IsOpen = false;
            return;
        }
        anchor ??= _addButton is { } add && TopLevel.GetTopLevel(add) is not null && add.IsEffectivelyVisible ? add : this;
        if (TopLevel.GetTopLevel(anchor)?.Content is not Panel host) return;

        var recents = TerminalRecents.Load();
        var where = folder is not null && Directory.Exists(folder) ? folder
            : _current?.Agent.Options.Folder is { } here && Directory.Exists(here) ? here
            : recents.Folders.FirstOrDefault(Directory.Exists) ?? global::Claupet.Account.Home;
        // Desde una pestaña en su propio worktree, la sesión nueva es del mismo proyecto.
        where = SessionWorktrees.ProjectOf(where);
        // Con un agente o un especialista, la sesión empieza en su propio worktree, desde la rama principal al día.
        var ownWorktree = true;
        var accounts = Accounts();
        var shells = Shells.Available();
        var kinds = new List<(string Id, string Mark, string Name, string? Note, bool Accent)> { ("agente:" + AgentKinds.Claude, "✻", "Claude Code", null, true) };
        kinds.AddRange(AgentKinds.Available().Where(k => k != AgentKinds.Claude).Select(k => ("agente:" + k, AgentMark(k), AgentKinds.Label(k), (string?)null, false)));
        kinds.AddRange(shells.Select(s => (s.Id, ">_", s.Label, s == DefaultShell && shells.Count > 1 ? L.T("por defecto") : null, false)));

        // Los especialistas (experimental), debajo de los agentes.
        var specialists = Specialists.All();
        var choice = kinds.Any(k => k.Id == recents.Kind) ? recents.Kind! : kinds[0].Id;
        var account = accounts.FirstOrDefault(a => global::Claupet.Account.SamePath(a.ConfigDir, recents.Account ?? "")) ?? accounts.FirstOrDefault();
        string? model = recents.Model, effort = recents.Effort;

        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            VerticalOffset = 6,
            IsLightDismissEnabled = true,
            ShouldUseOverlayLayer = true,
        };
        _newMenu = popup;
        var paints = new List<Action>();
        var choosing = false;
        Border card = null!; // se arma al final: lo usa la lista de proyectos, que se abre con el menú ya a la vista
        // En una ventana baja el menú se desplaza: si no, «Abrir» quedaba fuera de la pantalla.
        var scroller = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = Math.Max(240, host.Bounds.Height - 32) };
        void Paint()
        {
            foreach (var paint in paints) paint();
        }

        var list = new StackPanel { Spacing = 1 };
        // Primero, en qué proyecto: es lo que más cambia y lo primero que se busca.
        list.Children.Add(MenuLabel(L.T("Proyecto")));
        var placeName = Ui.Text("", 13, _ink.Text, FontWeight.SemiBold);
        var placePath = Ui.Text("", 11.5, _ink.Tertiary, family: Mono);
        placePath.TextTrimming = TextTrimming.PrefixCharacterEllipsis;
        bool Isolated() => ownWorktree && !shells.Any(s => s.Id == choice) && SessionWorktrees.Locate(where) is not null;
        void PaintPlace()
        {
            var name = FolderName(where);
            // El proyecto, con su rama principal; sin worktree propio, con la rama en que está su carpeta.
            var branch = Isolated() ? SessionWorktrees.Default(where) ?? GitBranch.Of(where) : GitBranch.Of(where);
            placeName.Text = branch is not null ? $"{name} · {branch}" : name;
            placePath.Text = Isolated() ? SessionWorktrees.NewName(SessionWorktrees.Locate(where)!.MainRoot ?? where, DateTime.Now).Path : where;
            ToolTip.SetTip(placePath, placePath.Text);
        }
        PaintPlace();
        var placeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var placeIcon = Icon(FolderIcon, _ink.Accent, 16, 1.8);
        placeIcon.Margin = new Thickness(0, 0, 10, 0);
        placeRow.Children.Add(placeIcon);
        var placeText = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        placeText.Children.Add(placeName);
        placeText.Children.Add(placePath);
        Grid.SetColumn(placeText, 1);
        placeRow.Children.Add(placeText);
        var placeChange = Ui.Text(L.T("Cambiar"), 12, _ink.Accent);
        placeChange.Margin = new Thickness(10, 0, 2, 0);
        Grid.SetColumn(placeChange, 2);
        placeRow.Children.Add(placeChange);
        var placeButton = Ui.Press(placeRow, () => ShowProjects(), (b, on) => b.Background = on ? _ink.Track : _ink.Pill, L.T("Cambiar de proyecto"));
        placeButton.Background = _ink.Pill;
        placeButton.Padding = new Thickness(10, 7);
        placeButton.Margin = new Thickness(4, 0, 4, 2);
        placeButton.CornerRadius = new CornerRadius(9);
        ToolTip.SetTip(placeButton, L.T("Cambiar de proyecto"));
        list.Children.Add(placeButton);

        // El worktree propio: solo con un agente o un especialista, y en un repositorio de git.
        var isolate = new StackPanel { Spacing = 0, Margin = new Thickness(4, 2, 4, 0) };
        var isolateRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(10, 4, 4, 0) };
        var isolateIcon = Icon(BranchIcon, _ink.Tertiary, 14, 1.8);
        isolateIcon.Margin = new Thickness(0, 0, 10, 0);
        isolateRow.Children.Add(isolateIcon);
        var isolateName = Ui.Text(L.T("Worktree propio"), 13, _ink.Text);
        isolateName.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(isolateName, 1);
        isolateRow.Children.Add(isolateName);
        var isolateSwitch = new Border { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(isolateSwitch, 2);
        isolateRow.Children.Add(isolateSwitch);
        ToolTip.SetTip(isolateRow, L.T("Empezar desde la rama principal, en una carpeta y una rama propias"));
        isolate.Children.Add(isolateRow);
        var isolateNote = Ui.Text("", 11.5, _ink.Tertiary);
        isolateNote.TextWrapping = TextWrapping.Wrap;
        isolateNote.Margin = new Thickness(34, 2, 6, 4);
        isolate.Children.Add(isolateNote);
        list.Children.Add(isolate);
        paints.Add(() =>
        {
            isolate.IsVisible = !shells.Any(s => s.Id == choice) && SessionWorktrees.Locate(where) is not null;
            isolateSwitch.Child = Switch(ownWorktree, on =>
            {
                ownWorktree = on;
                Paint();
            });
            var main = SessionWorktrees.Default(where) ?? "main";
            isolateNote.Text = ownWorktree
                ? L.T("Empieza desde {0} al día, en su propia rama. La carpeta del proyecto no se toca.", main)
                : GitBranch.Of(where) is { } now && now != main
                    ? L.T("Abre en la carpeta del proyecto tal como está: en la rama {0}.", now)
                    : L.T("Abre en la carpeta del proyecto.");
        });
        paints.Add(PaintPlace);
        list.Children.Add(MenuRule());

        list.Children.Add(MenuLabel(L.T("Abrir con")));
        foreach (var kind in kinds)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("26,*,Auto") };
            var mark = Ui.Text(kind.Mark, 11.5, kind.Accent ? _ink.Accent : _ink.Tertiary, family: Mono);
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            mark.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(mark);
            var name = Ui.Text(kind.Name, 13, _ink.Text);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            if (kind.Note is { } note)
            {
                var small = Ui.Text(note, 11.5, _ink.Tertiary);
                small.VerticalAlignment = VerticalAlignment.Center;
                small.Margin = new Thickness(8, 0, 2, 0);
                Grid.SetColumn(small, 2);
                row.Children.Add(small);
            }
            var id = kind.Id;
            Border? item = null;
            item = Ui.Press(row, () =>
            {
                // Un segundo toque en la que ya está elegida la abre.
                if (choice == id) OpenChosen();
                else
                {
                    choice = id;
                    Paint();
                }
            }, (b, on) => b.Background = on || choice == id ? _ink.Pill : Brushes.Transparent, kind.Name);
            item.Height = 32;
            item.Padding = new Thickness(4, 0, 8, 0);
            item.CornerRadius = new CornerRadius(8);
            paints.Add(() => item.Background = choice == id ? _ink.Pill : Brushes.Transparent);
            list.Children.Add(item);
        }

        // Los especialistas (experimental): cada uno abre su agente con sus instrucciones y sus herramientas.
        if (specialists.Count > 0)
        {
            list.Children.Add(MenuRule());
            list.Children.Add(MenuLabel(L.T("Especialistas")));
        }
        foreach (var specialist in specialists)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("26,*,Auto") };
            var color = new SolidColorBrush(Color.Parse(specialist.Color));
            var face = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(7),
                Background = Translucent(color, 0.15),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = Icon(SpecialistsPane.Glyphs.TryGetValue(specialist.Glyph, out var glyph) ? glyph : SpecialistsPane.Icon, color, 12, 1.9),
            };
            row.Children.Add(face);
            var name = Ui.Text(specialist.Name, 13, _ink.Text);
            name.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var mark = Ui.Text(SpecialistsPane.MarkOf(specialist.Agent), 11, _ink.Tertiary, family: Mono);
            mark.Margin = new Thickness(8, 0, 2, 0);
            Grid.SetColumn(mark, 2);
            row.Children.Add(mark);
            var id = "especialista:" + specialist.Id;
            Border? item = null;
            item = Ui.Press(row, () =>
            {
                if (choice == id) OpenChosen();
                else
                {
                    choice = id;
                    Paint();
                }
            }, (b, on) => b.Background = on || choice == id ? _ink.Pill : Brushes.Transparent, specialist.Name);
            item.Height = 32;
            item.Padding = new Thickness(4, 0, 8, 0);
            item.CornerRadius = new CornerRadius(8);
            ToolTip.SetTip(item, specialist.Role.Length > 0 ? specialist.Role : specialist.Name);
            paints.Add(() => item.Background = choice == id ? _ink.Pill : Brushes.Transparent);
            list.Children.Add(item);
        }
        // Lo que se recorre con las flechas: los agentes, las terminales y los especialistas.
        var order = kinds.Select(k => k.Id).Concat(specialists.Select(x => "especialista:" + x.Id)).ToList();

        // Con Claude Code: la cuenta, el modelo y el esfuerzo. Los otros agentes los eligen en la sesión, con lo que dicen tener.
        var claudeOnly = new StackPanel { Spacing = 1 };
        claudeOnly.Children.Add(MenuRule());
        if (accounts.Count > 0)
        {
            claudeOnly.Children.Add(MenuLabel(L.T("Con la cuenta")));
            var row = MenuChips();
            foreach (var a in accounts)
            {
                var state = AssistantAccounts.StatusOf(a, new Dictionary<string, UsageSnapshot>());
                var dot = state.State == AccountReady.Ready ? Added : state.State == AccountReady.AtLimit ? _ink.Warn : _ink.Danger;
                var chip = ChoiceChip(a.Label, () =>
                {
                    account = a;
                    Paint();
                }, dot);
                ToolTip.SetTip(chip, state.Text);
                paints.Add(() => PaintChip(chip, account == a));
                row.Children.Add(chip);
            }
            claudeOnly.Children.Add(row);
        }
        claudeOnly.Children.Add(MenuLabel(L.T("Modelo y esfuerzo")));
        var models = MenuChips();
        // Los de siempre; las variantes (opus[1m], sonnet[1m], opusplan) se eligen en la sesión, con su etiqueta.
        string[] main = ["", "haiku", "sonnet", "opus", "fable"];
        foreach (var m in model is { Length: > 0 } last && !main.Contains(last) ? [.. main, last] : main)
        {
            var value = m.Length == 0 ? null : m;
            var chip = ChoiceChip(value is null ? L.T("por defecto") : Format.ModelChoice(value), () =>
            {
                model = value;
                Paint();
            });
            paints.Add(() => PaintChip(chip, model == value));
            models.Children.Add(chip);
        }
        claudeOnly.Children.Add(models);
        var efforts = MenuChips();
        foreach (var e in Launcher.Efforts.Prepend(""))
        {
            var value = e.Length == 0 ? null : e;
            var chip = ChoiceChip(EffortLabel(value), () =>
            {
                effort = value;
                Paint();
            });
            paints.Add(() => PaintChip(chip, effort == value));
            efforts.Children.Add(chip);
        }
        claudeOnly.Children.Add(efforts);
        paints.Add(() => claudeOnly.IsVisible = choice == "agente:" + AgentKinds.Claude);
        list.Children.Add(claudeOnly);

        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
        var go = TextButton(L.T("Abrir"), OpenChosen, accent: true);
        Grid.SetColumn(go, 1);
        foot.Children.Add(go);
        list.Children.Add(foot);

        card = new Border
        {
            Background = _ink.Raised,
            BorderBrush = _ink.Hairline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(5),
            Width = 318,
            BoxShadow = BoxShadows.Parse(_ink.Dark ? "0 14 34 0 #40000000" : "0 14 34 0 #24000000"),
            Focusable = true,
            Opacity = 0,
            Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(110) }],
        };
        card.KeyDown += (_, e) =>
        {
            if (choosing)
            {
                if (e.Key != Key.Escape) return;
                ShowMain();
                e.Handled = true;
                return;
            }
            var at = order.IndexOf(choice);
            switch (e.Key)
            {
                case Key.Enter:
                    OpenChosen();
                    break;
                case Key.Escape:
                    popup.IsOpen = false;
                    break;
                case Key.Down:
                    choice = order[(at + 1) % order.Count];
                    Paint();
                    break;
                case Key.Up:
                    choice = order[at <= 0 ? order.Count - 1 : at - 1];
                    Paint();
                    break;
                default:
                    return;
            }
            e.Handled = true;
        };

        // Los proyectos van dentro de este mismo menú: una lista encima de él quedaba colgando de algo que se cierra solo.
        void ShowMain()
        {
            choosing = false;
            scroller.Content = list;
            card.Focus();
        }

        void Chosen(string folder)
        {
            where = SessionWorktrees.ProjectOf(folder);
            Paint();
            ShowMain();
        }

        void ShowProjects()
        {
            choosing = true;
            var page = new StackPanel { Spacing = 1 };
            var backRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            backRow.Children.Add(Icon("M15,6 L9,12 L15,18", _ink.Secondary, 13, 2));
            backRow.Children.Add(Ui.Text(L.T("Elegir proyecto"), 13, _ink.Text, FontWeight.SemiBold));
            var back = Ui.Press(backRow, ShowMain, (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, L.T("Volver"));
            back.Padding = new Thickness(10, 7);
            back.CornerRadius = new CornerRadius(8);
            page.Children.Add(back);
            var folders = RecentFolders();
            if (folders.Count == 0)
            {
                var none = Ui.Text(L.T("Todavía no hay proyectos recientes."), 12.5, _ink.Tertiary);
                none.Margin = new Thickness(10, 8);
                page.Children.Add(none);
            }
            foreach (var folder in folders) page.Children.Add(ProjectRow(folder, string.Equals(folder, where, StringComparison.OrdinalIgnoreCase), () => Chosen(folder)));
            page.Children.Add(MenuRule());
            page.Children.Add(BrowseRow(FolderIcon, L.T("Buscar una carpeta…"), () => Browse(file: false)));
            page.Children.Add(BrowseRow(FileIcon, L.T("Buscar un archivo del proyecto…"), () => Browse(file: true)));
            scroller.Content = page;
            back.Focus();
        }

        async void Browse(bool file)
        {
            if (TopLevel.GetTopLevel(card) is not { } top) return;
            // El diálogo de Windows le quita el foco a la ventana y eso cerraba este menú: lo elegido no tenía dónde quedar.
            popup.IsLightDismissEnabled = false;
            try
            {
                if (await PickPath(top, where, file) is { } path) Chosen(file ? ProjectOfFile(path) : path);
            }
            finally { popup.IsLightDismissEnabled = true; }
        }

        void OpenChosen()
        {
            // La carpeta pudo desaparecer con el menú abierto: se pide otra en vez de no hacer nada.
            if (!Directory.Exists(where))
            {
                ShowProjects();
                return;
            }
            popup.IsOpen = false;
            if (shells.FirstOrDefault(s => s.Id == choice) is { } shell)
            {
                OpenShell(shell, where);
                return;
            }
            var picked = choice;
            void Open(string folder)
            {
                if (picked.StartsWith("agente:", StringComparison.Ordinal))
                    OpenAgent(picked["agente:".Length..], folder, account, model, effort);
                else if (specialists.FirstOrDefault(s => "especialista:" + s.Id == picked) is { } chosen)
                    OpenSpecialist(chosen, folder);
            }
            if (!Isolated())
            {
                Open(where);
                return;
            }
            _ = OpenIsolatedAsync(where, Open);
        }

        Paint();
        scroller.Content = list;
        card.Child = scroller;
        popup.Child = card;
        popup.Closed += (_, _) =>
        {
            host.Children.Remove(popup);
            if (_newMenu == popup) _newMenu = null;
        };
        host.Children.Add(popup);
        popup.IsOpen = true;
        card.Opacity = 1;
        Dispatcher.UIThread.Post(() => card.Focus(), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Crea el worktree de la sesión (desde la rama principal al día) y la abre ahí. Mientras git trabaja se ve un aviso;
    /// si no se puede, lo dice y abre en la carpeta del proyecto.
    /// </summary>
    async Task OpenIsolatedAsync(string folder, Action<string> open)
    {
        var (path, error) = await SessionWorktrees.CreateAsync(folder, DateTime.Now);
        Dispatcher.UIThread.Post(() =>
        {
            open(path ?? folder);
            if (error is not null) _current?.Agent.Transcript.Notice(error + " " + L.T("Se abrió en la carpeta del proyecto."));
        });
    }

    /// <summary>El diálogo del sistema para buscar una carpeta o un archivo (las pruebas ponen otro).</summary>
    internal static Func<TopLevel, string, bool, Task<string?>> PickPath = PickPathNative;

    static async Task<string?> PickPathNative(TopLevel top, string current, bool file)
    {
        var storage = top.StorageProvider;
        var start = await storage.TryGetFolderFromPathAsync(Directory.Exists(current) ? current : global::Claupet.Account.Home);
        if (file)
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = L.T("Elija un archivo del proyecto"), SuggestedStartLocation = start });
            return files.FirstOrDefault()?.TryGetLocalPath();
        }
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L.T("Elija la carpeta del proyecto"), SuggestedStartLocation = start });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    /// <summary>El proyecto de un archivo: la raíz de su repositorio de git o, sin repositorio, la carpeta donde está.</summary>
    internal static string ProjectOfFile(string file)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(file)) ?? file;
        for (var up = folder; up is not null; up = Path.GetDirectoryName(up))
            if (Directory.Exists(Path.Combine(up, ".git")) || File.Exists(Path.Combine(up, ".git"))) return up;
        return folder;
    }

    static string FolderName(string folder) => Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)) is { Length: > 0 } name ? name : folder;

    /// <summary>Los proyectos vinculados primero y después las carpetas recientes, las que todavía existen.</summary>
    List<string> RecentFolders() => _projects.Folders.Concat(TerminalRecents.Load().Folders).Where(Directory.Exists)
        .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();

    /// <summary>Un proyecto de la lista: su nombre y, debajo, dónde está; con un visto el que ya está elegido.</summary>
    Border ProjectRow(string folder, bool selected, Action onClick)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(Ui.Text(FolderName(folder), 13, _ink.Text, selected ? FontWeight.SemiBold : FontWeight.Normal));
        var path = Ui.Text(folder, 11.5, _ink.Tertiary, family: Mono);
        path.TextTrimming = TextTrimming.PrefixCharacterEllipsis;
        text.Children.Add(path);
        row.Children.Add(text);
        if (selected)
        {
            var check = Icon(CheckIcon, _ink.Accent, 14, 2.2);
            check.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(check, 1);
            row.Children.Add(check);
        }
        var item = Ui.Press(row, onClick, (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, FolderName(folder));
        item.Padding = new Thickness(10, 6);
        item.CornerRadius = new CornerRadius(8);
        ToolTip.SetTip(item, folder);
        return item;
    }

    Border BrowseRow(string icon, string text, Action onClick)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(Icon(icon, _ink.Secondary, 15, 1.8));
        row.Children.Add(Ui.Text(text, 13, _ink.Text));
        var item = Ui.Press(row, onClick, (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, text);
        item.Height = 34;
        item.Padding = new Thickness(10, 0);
        item.CornerRadius = new CornerRadius(8);
        return item;
    }

    /// <summary>Una sesión nueva de Claude Code: sin modo de permisos, usa el que tenga configurado (el de la sesión).</summary>
    internal static AgentOptions NewClaudeOptions(string folder, string? configDir, string? model, string? effort) =>
        new(folder, configDir, model, effort);

    /// <summary>Abre un agente en bloques (Claude Code con su cuenta, su modelo y su esfuerzo; los otros, con lo suyo). Null si la carpeta no existe.</summary>
    Tab? OpenAgent(string kind, string folder, Account? account, string? model, string? effort)
    {
        if (!Directory.Exists(folder)) return null;
        var claude = kind == AgentKinds.Claude;
        var last = TerminalRecents.Load();
        var options = claude ? NewClaudeOptions(folder, account?.ConfigDir, model, effort) : new AgentOptions(folder, Kind: kind);
        // Se recuerda lo elegido para la próxima; con otro agente, la cuenta y el modelo de Claude Code quedan como estaban.
        TerminalRecents.Remember(claude ? options : new AgentOptions(folder, last.Account, last.Model, last.Effort, last.Mode), "agente:" + kind);
        LinkProject(folder);
        // Una copia de prueba (--preview) no le pasa los permisos a la isla de la app de siempre.
        var agent = NewAgent(options);
        var tab = new Tab(agent, claude ? account?.Label ?? L.T("cuenta por defecto") : AgentKinds.Label(kind));
        agent.Changed += changed => OnChanged(tab, changed);
        Add(tab);
        agent.Start();
        _input.Focus();
        return tab;
    }

    /// <summary>
    /// Abre un especialista: su agente con sus instrucciones y solo sus herramientas, en folder, en su carpeta o en la del
    /// proyecto que se está usando. Con Claude Code, en la última cuenta elegida.
    /// </summary>
    internal void OpenSpecialist(Specialist specialist, string? folder = null)
    {
        var last = TerminalRecents.Load();
        var where = folder is not null && Directory.Exists(folder) ? folder
            : specialist.Folder is { } own && Directory.Exists(own) ? own
            : _current?.Agent.Options.Folder is { } here && Directory.Exists(here) ? here
            : last.Folders.FirstOrDefault(Directory.Exists) ?? global::Claupet.Account.Home;
        var options = specialist.Agent == AgentKinds.Claude
            ? NewClaudeOptions(where, last.Account, specialist.Model, specialist.Effort) with { Specialist = specialist }
            : new AgentOptions(where, Kind: specialist.Agent, Specialist: specialist);
        LinkProject(where);
        var agent = NewAgent(options);
        var tab = new Tab(agent, specialist.Name);
        agent.Changed += changed => OnChanged(tab, changed);
        Add(tab);
        agent.Start();
        _input.Focus();
    }

    /// <summary>
    /// El agente de una pestaña, abierta de la forma que sea (nueva, especialista, del historial o al reabrir Claupet): todas
    /// se coordinan con las otras sesiones de su carpeta. Una copia de prueba (--preview) no le pasa los permisos a la isla.
    /// </summary>
    internal static TerminalAgent NewAgent(AgentOptions options, bool? askIsland = null) =>
        new(options, action => Dispatcher.UIThread.Post(action))
        {
            AskIsland = askIsland ?? !Program.Args.Contains("--preview"),
            Coordinate = true,
        };

    /// <summary>La pestaña nueva pasa a ser la elegida.</summary>
    internal void Add(Tab tab)
    {
        if (_current is { } previous && !_creating) previous.Draft = _input.Text ?? "";
        _tabs.Add(tab);
        _current = tab;
        _creating = false;
        _input.Text = "";
        Build();
        RenderList();
        TabsChanged?.Invoke();
    }

    /// <summary>Sin pestañas: qué es esto y el botón de la pestaña nueva.</summary>
    Control EmptyState()
    {
        var panel = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 380, Margin = new Thickness(24) };
        var title = new TextBlock { Text = L.T("Claupet Terminal"), FontFamily = Ink.Display, FontSize = 19, FontWeight = FontWeight.SemiBold, Foreground = _ink.Text, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(title);
        var about = Ui.Text(L.T("Abra una pestaña: Claude Code u otro agente, en bloques, o una terminal."), 13, _ink.Secondary);
        about.TextWrapping = TextWrapping.Wrap;
        about.TextAlignment = TextAlignment.Center;
        panel.Children.Add(about);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        content.Children.Add(Icon(PlusIcon, Ink.On(_ink.Accent), 13, 2.2));
        content.Children.Add(Ui.Text(L.T("Nueva pestaña"), 13, Ink.On(_ink.Accent), FontWeight.SemiBold));
        Border? button = null;
        button = Ui.Press(content, () => ShowNew(null, button), (b, on) => b.Opacity = on ? 0.88 : 1, L.T("Nueva pestaña"));
        button.Background = _ink.Accent;
        button.Height = 34;
        button.Padding = new Thickness(16, 0);
        button.CornerRadius = new CornerRadius(999);
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(button);
        var hint = Ui.Text("Ctrl+T", 11.5, _ink.Tertiary);
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(hint);
        return panel;
    }

    // ── Piezas del menú ─────────────────────────────────────────────────────────────────────

    TextBlock MenuLabel(string text)
    {
        var label = Ui.Text(text, 11.5, _ink.Tertiary);
        label.Margin = new Thickness(10, 6, 10, 4);
        return label;
    }

    Border MenuRule() => new() { Height = 1, Background = _ink.Hairline, Margin = new Thickness(6, 4) };

    static WrapPanel MenuChips() => new() { Margin = new Thickness(6, 0, 6, 4) };

    /// <summary>Un botón redondo para elegir (la cuenta, el modelo, el esfuerzo), con su punto de color si lleva.</summary>
    Border ChoiceChip(string text, Action onClick, IBrush? dot = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (dot is not null)
            row.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = dot, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(Ui.Text(text, 12.5, _ink.Secondary));
        var chip = Ui.Press(row, onClick, null, text);
        chip.Height = 28;
        chip.Padding = new Thickness(10, 0);
        chip.Margin = new Thickness(0, 0, 4, 4);
        chip.CornerRadius = new CornerRadius(999);
        chip.BorderThickness = new Thickness(1);
        return chip;
    }

    void PaintChip(Border chip, bool selected)
    {
        chip.Background = selected ? Translucent(_ink.Accent, 0.16) : Brushes.Transparent;
        chip.BorderBrush = selected ? Brushes.Transparent : _ink.Hairline;
        if (chip.Child is StackPanel { Children: [.., TextBlock text] }) text.Foreground = selected ? _ink.Text : _ink.Secondary;
    }

    /// <summary>Un botón de texto sin fondo (los de los menús y los permisos); accent: el que sigue, en acento y negrita.</summary>
    Border TextButton(string text, Action onClick, bool accent = false, IBrush? brush = null)
    {
        var label = Ui.Text(text, 12.5, brush ?? (accent ? _ink.Accent : _ink.Secondary), accent ? FontWeight.SemiBold : FontWeight.Normal);
        label.VerticalAlignment = VerticalAlignment.Center;
        var button = Ui.Press(label, onClick, (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, text);
        button.Height = 28;
        button.Padding = new Thickness(10, 0);
        button.CornerRadius = new CornerRadius(7);
        button.VerticalAlignment = VerticalAlignment.Center;
        return button;
    }
}
