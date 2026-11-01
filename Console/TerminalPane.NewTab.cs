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
/// La pestaña nueva, como en el diseño: el botón + abre un menú chico, no un formulario. Arriba, qué abrir (Claude Code,
/// los otros agentes instalados o una de las terminales del equipo); con Claude Code, la cuenta, el modelo y el esfuerzo
/// en botones; abajo, la carpeta (se cambia tocándola) y «Abrir». Sin pestañas, la sesión muestra un vacío con el mismo
/// botón. Ctrl+T lo abre desde cualquier lado de la Terminal.
/// </summary>
sealed partial class TerminalPane
{
    /// <summary>El botón + de la lista: de ahí sale el menú.</summary>
    Border? _addButton;
    /// <summary>El menú abierto (otro toque al + lo cierra).</summary>
    Popup? _newMenu;

    /// <summary>La marca de cada agente, como en la lista: ✻ Claude Code, cx Codex, ✦ Gemini, gh Copilot.</summary>
    static string AgentMark(string kind) => kind switch
    {
        AgentKinds.Codex => "cx",
        AgentKinds.Gemini => "✦",
        AgentKinds.Copilot => "gh",
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
        var accounts = Accounts();
        var shells = Shells.Available();
        var kinds = new List<(string Id, string Mark, string Name, string? Note, bool Accent)> { ("agente:" + AgentKinds.Claude, "✻", "Claude Code", null, true) };
        kinds.AddRange(AgentKinds.Available().Where(k => k != AgentKinds.Claude).Select(k => ("agente:" + k, AgentMark(k), AgentKinds.Label(k), (string?)null, false)));
        kinds.AddRange(shells.Select(s => (s.Id, ">_", s.Label, s == DefaultShell && shells.Count > 1 ? L.T("por defecto") : null, false)));

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
        void Paint()
        {
            foreach (var paint in paints) paint();
        }

        var list = new StackPanel { Spacing = 1 };
        list.Children.Add(MenuLabel(L.T("Abrir")));
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
            var chip = ChoiceChip(value ?? L.T("por defecto"), () =>
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

        // Abajo: dónde se abre (se toca para elegir otra carpeta) y «Abrir».
        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
        var place = Ui.Text("", 12, _ink.Tertiary, family: Mono);
        place.TextTrimming = TextTrimming.CharacterEllipsis;
        place.VerticalAlignment = VerticalAlignment.Center;
        void PaintPlace()
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(where)) is { Length: > 0 } n ? n : where;
            place.Text = GitBranch.Of(where) is { } branch ? $"{name} · {branch}" : name;
            ToolTip.SetTip(place, where);
        }
        PaintPlace();
        Border? placeButton = null;
        placeButton = Ui.Press(place, () => PickFolder(placeButton!, where, picked =>
        {
            where = picked;
            PaintPlace();
        }), (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, L.T("Carpeta"));
        placeButton.Padding = new Thickness(8, 4);
        placeButton.CornerRadius = new CornerRadius(7);
        placeButton.HorizontalAlignment = HorizontalAlignment.Left;
        foot.Children.Add(placeButton);
        var go = TextButton(L.T("Abrir"), OpenChosen, accent: true);
        Grid.SetColumn(go, 1);
        foot.Children.Add(go);
        list.Children.Add(foot);

        var card = new Border
        {
            Child = list,
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
            var at = kinds.FindIndex(k => k.Id == choice);
            switch (e.Key)
            {
                case Key.Enter:
                    OpenChosen();
                    break;
                case Key.Escape:
                    popup.IsOpen = false;
                    break;
                case Key.Down:
                    choice = kinds[(at + 1) % kinds.Count].Id;
                    Paint();
                    break;
                case Key.Up:
                    choice = kinds[at <= 0 ? kinds.Count - 1 : at - 1].Id;
                    Paint();
                    break;
                default:
                    return;
            }
            e.Handled = true;
        };

        void OpenChosen()
        {
            popup.IsOpen = false;
            if (choice.StartsWith("agente:", StringComparison.Ordinal))
                OpenAgent(choice["agente:".Length..], where, account, model, effort);
            else if (shells.FirstOrDefault(s => s.Id == choice) is { } shell)
                OpenShell(shell, where);
        }

        Paint();
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

    /// <summary>Elegir la carpeta: los proyectos y las recientes, o «Elegir otra carpeta…».</summary>
    void PickFolder(Control anchor, string current, Action<string> picked)
    {
        var recents = _projects.Folders.Concat(TerminalRecents.Load().Folders).Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
        var entries = recents.Select(f => new DropList.Entry(f, ShortPath(f), Selected: string.Equals(f, current, StringComparison.OrdinalIgnoreCase)))
            .Append(new DropList.Entry("…", L.T("Elegir otra carpeta…"), Action: true)).ToList();
        DropList.Show(anchor, _ink, entries, async choice =>
        {
            if (choice is null) return;
            if (choice == "…")
            {
                if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
                var start = await storage.TryGetFolderFromPathAsync(Directory.Exists(current) ? current : global::Claupet.Account.Home);
                var chosen = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L.T("Carpeta de la pestaña"), SuggestedStartLocation = start });
                if (chosen.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
                choice = path;
            }
            picked(choice);
        });
    }

    /// <summary>Abre un agente en bloques (Claude Code con su cuenta, su modelo y su esfuerzo; los otros, con lo suyo).</summary>
    void OpenAgent(string kind, string folder, Account? account, string? model, string? effort)
    {
        if (!Directory.Exists(folder)) return;
        var claude = kind == AgentKinds.Claude;
        var last = TerminalRecents.Load();
        var options = claude
            ? new AgentOptions(folder, account?.ConfigDir, model, effort, last.Mode ?? "default")
            : new AgentOptions(folder, Kind: kind);
        // Se recuerda lo elegido para la próxima; con otro agente, la cuenta y el modelo de Claude Code quedan como estaban.
        TerminalRecents.Remember(claude ? options : new AgentOptions(folder, last.Account, last.Model, last.Effort, last.Mode), "agente:" + kind);
        LinkProject(folder);
        // Una copia de prueba (--preview) no le pasa los permisos a la isla de la app de siempre.
        var agent = new TerminalAgent(options, action => Dispatcher.UIThread.Post(action)) { AskIsland = !Program.Args.Contains("--preview") };
        var tab = new Tab(agent, claude ? account?.Label ?? L.T("cuenta por defecto") : AgentKinds.Label(kind));
        agent.Changed += changed => OnChanged(tab, changed);
        Add(tab);
        agent.Start();
        _input.Focus();
    }

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
