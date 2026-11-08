namespace Claupet;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

/// <summary>
/// Especialistas (experimental): agentes con nombre, rol, instrucciones y herramientas, como en el prototipo aprobado. A la
/// izquierda la lista (los de la persona y, aparte, los de las extensiones); a la derecha el que se elige, que se edita en
/// el lugar y se guarda solo. ▶ lo abre como una pestaña de la Terminal. Los de una extensión son de solo lectura: se
/// duplican para cambiarlos.
/// </summary>
sealed class SpecialistsPane : Panel
{
    // Los íconos, en un cuadro de 24 como los del riel.
    public const string Icon = "M9.5,11 A3.2,3.2 0 1 0 9.5,4.6 A3.2,3.2 0 1 0 9.5,11 Z M3.8,19.5 C3.8,16.2 6.3,14.2 9.5,14.2 C11.3,14.2 12.8,14.8 13.8,15.9 "
        + "M17.6,10.2 L18.4,12.2 L20.4,13 L18.4,13.8 L17.6,15.8 L16.8,13.8 L14.8,13 L16.8,12.2 Z";
    const string PlayIcon = "M8.5,6 V18 L18,12 Z";
    const string PlusIcon = "M12,5.5 V18.5 M5.5,12 H18.5";
    const string CopyIcon = "M9,9 H18 A1,1 0 0 1 19,10 V19 A1,1 0 0 1 18,20 H9 A1,1 0 0 1 8,19 V10 A1,1 0 0 1 9,9 Z M5,15 V5 A1,1 0 0 1 6,4 H15";
    const string MoreIcon = "M3.4,12 A1.6,1.6 0 1 0 6.6,12 A1.6,1.6 0 1 0 3.4,12 Z M10.4,12 A1.6,1.6 0 1 0 13.6,12 A1.6,1.6 0 1 0 10.4,12 Z "
        + "M17.4,12 A1.6,1.6 0 1 0 20.6,12 A1.6,1.6 0 1 0 17.4,12 Z";
    const string CheckIcon = "M5,12.5 L10,17 L19,7.5";
    const string LockIcon = "M7,11 H17 A1,1 0 0 1 18,12 V19 A1,1 0 0 1 17,20 H7 A1,1 0 0 1 6,19 V12 A1,1 0 0 1 7,11 Z M8.5,11 V8 A3.5,3.5 0 0 1 15.5,8 V11";
    const string ShieldIcon = "M12,3.5 L19,6.5 V11.5 C19,15.8 16,19 12,20.5 C8,19 5,15.8 5,11.5 V6.5 Z";
    const string ShieldCheckIcon = ShieldIcon + " M9.2,12 L11.2,14 L15,10";
    const string PackageIcon = "M12,3.5 L19.5,7.5 V16.5 L12,20.5 L4.5,16.5 V7.5 Z M4.5,7.5 L12,11.5 L19.5,7.5 M12,11.5 V20.5";
    const string GlobeIcon = "M12,3.5 A8.5,8.5 0 1 0 12,20.5 A8.5,8.5 0 1 0 12,3.5 Z M3.5,12 H20.5 M12,3.5 C9.5,6 9.5,18 12,20.5 C14.5,18 14.5,6 12,3.5";
    const string CanvasIcon = "M4,4 H20 A1,1 0 0 1 21,5 V19 A1,1 0 0 1 20,20 H4 A1,1 0 0 1 3,19 V5 A1,1 0 0 1 4,4 Z M3,9 H21 M10,9 V20 M13,13 H18 M13,16.5 H16.5";
    const string FolderIcon = "M4,6.5 H10 L12,8.5 H20 V18.5 H4 Z";
    const string FlaskIcon = "M9.5,3.5 H14.5 M10.5,3.5 V9.5 L5.5,18 A1.5,1.5 0 0 0 6.8,20.5 H17.2 A1.5,1.5 0 0 0 18.5,18 L13.5,9.5 V3.5 M7.8,14.5 H16.2";

    /// <summary>Los íconos que se pueden elegir (los de Specialist.Glyphs).</summary>
    public static readonly IReadOnlyDictionary<string, string> Glyphs = new Dictionary<string, string>
    {
        ["search"] = "M10.5,4 A6.5,6.5 0 1 0 10.5,17 A6.5,6.5 0 1 0 10.5,4 Z M15.5,15.5 L20,20",
        ["pen"] = "M15,5 L19,9 L9,19 H5 V15 Z M13,7 L17,11",
        ["bug"] = "M9,7.5 A3,3 0 0 1 15,7.5 M8,10 H16 V15 A4,4 0 0 1 8,15 Z M12,10 V19 M4.5,12.5 H8 M16,12.5 H19.5 M5,18 L8.5,16 M19,18 L15.5,16 M5,7.5 L8.3,9.5 M19,7.5 L15.7,9.5",
        ["chart"] = "M4.5,19.5 H19.5 M7.5,16 V11 M12,16 V7 M16.5,16 V13",
        ["code"] = "M9,7.5 L4.5,12 L9,16.5 M15,7.5 L19.5,12 L15,16.5 M13.2,5.5 L10.8,18.5",
        ["book"] = "M5,5.5 C7.5,4.5 10,4.8 12,6.5 C14,4.8 16.5,4.5 19,5.5 V18.5 C16.5,17.5 14,17.8 12,19.5 C10,17.8 7.5,17.5 5,18.5 Z M12,6.5 V19.5",
        ["rocket"] = "M12,3.5 C15.5,6 17,10 15.5,15 H8.5 C7,10 8.5,6 12,3.5 Z M12,9 A1.5,1.5 0 1 0 12,12 A1.5,1.5 0 1 0 12,9 M8.5,15 L6,18 L9,17.5 M15.5,15 L18,18 L15,17.5 M10.5,17.5 L12,20.5 L13.5,17.5",
        ["flask"] = FlaskIcon,
        ["shield"] = ShieldIcon,
        ["spark"] = "M12,3.5 L13.8,10.2 L20.5,12 L13.8,13.8 L12,20.5 L10.2,13.8 L3.5,12 L10.2,10.2 Z",
        ["globe"] = GlobeIcon,
        ["heart"] = "M12,19.5 C6,15.5 3.5,12.5 3.5,9.2 A4.3,4.3 0 0 1 12,7.5 A4.3,4.3 0 0 1 20.5,9.2 C20.5,12.5 18,15.5 12,19.5 Z",
    };

    static readonly (string Kind, string Mark)[] Agents =
        [(AgentKinds.Claude, "✻"), (AgentKinds.Codex, "cx"), (AgentKinds.Gemini, "✦"), (AgentKinds.Copilot, "gh")];

    public static string MarkOf(string kind) => Agents.FirstOrDefault(a => a.Kind == kind).Mark ?? "✻";

    Ink _ink = Ink.For(true);
    List<Specialist> _all = [];
    IReadOnlyList<McpServer> _servers = [];
    string? _selected;
    readonly StackPanel _list = new() { Spacing = 2 };
    readonly Border _host = new();
    TextBlock? _saved;
    TextBlock? _barName;
    Border? _barAvatar, _heroAvatar;
    readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly DispatcherTimer _savedTimer = new() { Interval = TimeSpan.FromMilliseconds(1400) };
    readonly Dictionary<string, (TextBlock Name, TextBlock Role, TextBlock Mark, Border Avatar)> _rows = [];

    /// <summary>La lista de la columna de la izquierda.</summary>
    public Control List => _list;

    /// <summary>Se pidió abrir este especialista en la Terminal.</summary>
    public event Action<Specialist>? Launch;

    /// <summary>Se pidió ver Ajustes › Extensiones (para sumar herramientas).</summary>
    public event Action? ExtensionsRequested;

    public SpecialistsPane()
    {
        Children.Add(_host);
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            if (Current is { ReadOnly: false } s) Specialists.Save(s);
            ShowSaved();
        };
        _savedTimer.Tick += (_, _) =>
        {
            _savedTimer.Stop();
            if (_saved is not null) _saved.Opacity = 0;
        };
    }

    Specialist? Current => _all.FirstOrDefault(s => s.Id == _selected);

    public void SetInk(Ink ink)
    {
        _ink = ink;
        RenderList();
        RenderMain();
    }

    /// <summary>Vuelve a leer los especialistas y las herramientas instaladas (al entrar a la vista).</summary>
    public void Reload()
    {
        Flush();
        _all = Specialists.All();
        _servers = ExtensionMcp.Servers();
        if (Current is null) _selected = _all.FirstOrDefault()?.Id;
        RenderList();
        RenderMain();
    }

    /// <summary>Guarda ya lo que esperaba guardarse (al salir de la vista o cerrar).</summary>
    public void Flush()
    {
        if (!_saveTimer.IsEnabled) return;
        _saveTimer.Stop();
        if (Current is { ReadOnly: false } s) Specialists.Save(s);
    }

    public void HandleKey(KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.N)
        {
            New();
            e.Handled = true;
        }
    }

    /// <summary>Uno nuevo, con el nombre listo para escribirse.</summary>
    public void New()
    {
        Flush();
        var count = _all.Count(s => !s.ReadOnly);
        var s = new Specialist
        {
            Id = Specialists.NewId(),
            Name = L.T("Especialista nuevo"),
            Color = Specialist.Colors[count % Specialist.Colors.Length],
            Glyph = Specialist.Glyphs[count % Specialist.Glyphs.Length],
        };
        Specialists.Save(s);
        _all.Insert(count, s);
        _selected = s.Id;
        RenderList();
        RenderMain(focusName: true);
    }

    void Select(string id)
    {
        if (id == _selected) return;
        Flush();
        _selected = id;
        RenderList(animate: false);
        RenderMain();
    }

    void Changed()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    void ShowSaved()
    {
        if (_saved is null) return;
        _saved.Opacity = 1;
        _savedTimer.Stop();
        _savedTimer.Start();
    }

    // ── La lista ────────────────────────────────────────────────────────────────────────────

    void RenderList(bool animate = true)
    {
        _list.Children.Clear();
        _rows.Clear();
        var index = 0;
        foreach (var s in _all.Where(s => !s.ReadOnly)) _list.Children.Add(Appear(ListItem(s), animate ? index++ * 35 : -1));
        if (_all.Any(s => s.ReadOnly))
        {
            var caption = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(10, 14, 10, 5) };
            caption.Children.Add(Glyph(PackageIcon, _ink.Tertiary, 12, 1.8));
            caption.Children.Add(Ui.Text(L.T("De extensiones"), 12, _ink.Tertiary, FontWeight.SemiBold));
            _list.Children.Add(caption);
            foreach (var s in _all.Where(s => s.ReadOnly)) _list.Children.Add(Appear(ListItem(s), animate ? index++ * 35 : -1));
        }
    }

    Control ListItem(Specialist s)
    {
        var current = s.Id == _selected;
        var avatar = Avatar(s, 34, 11, 17);
        avatar.Margin = new Thickness(0, 0, 11, 0);
        var name = Ui.Text(s.Name, 14, _ink.Text, FontWeight.Medium);
        var mark = Ui.Text(MarkOf(s.Agent), 11, _ink.Tertiary, FontWeight.SemiBold, Ink.Mono);
        var role = Ui.Text(s.Role, 12, _ink.Tertiary);
        var sub = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        mark.Margin = new Thickness(0, 0, 5, 0);
        sub.Children.Add(mark);
        Grid.SetColumn(role, 1);
        sub.Children.Add(role);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, sub } };

        var run = Ui.Press(Glyph(PlayIcon, _ink.Secondary, 14, 1.8), () => Launch?.Invoke(s),
            (b, on) => b.Background = on ? _ink.Raised : Brushes.Transparent, L.T("Abrir sesión"));
        run.Width = run.Height = 30;
        run.CornerRadius = new CornerRadius(999);
        run.Opacity = current ? 1 : 0;
        if (TerminalPane.Animate) run.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(200) }];
        ToolTip.SetTip(run, L.T("Abrir sesión"));

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { avatar } };
        Grid.SetColumn(words, 1);
        row.Children.Add(words);
        Grid.SetColumn(run, 2);
        row.Children.Add(run);
        var item = Ui.Press(row, () => Select(s.Id), (b, on) =>
        {
            b.Background = on || s.Id == _selected ? _ink.Pill : Brushes.Transparent;
            run.Opacity = on || s.Id == _selected ? 1 : 0;
        }, s.Name);
        item.Padding = new Thickness(10, 8, 8, 8);
        item.CornerRadius = new CornerRadius(12);
        item.Background = current ? _ink.Pill : Brushes.Transparent;
        if (TerminalPane.Animate) item.Transitions = [new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(180) }];
        _rows[s.Id] = (name, role, mark, avatar);
        return item;
    }

    /// <summary>La fila de la lista sin rehacerla (mientras se escribe el nombre o el rol).</summary>
    void PaintRow(Specialist s)
    {
        if (!_rows.TryGetValue(s.Id, out var row)) return;
        row.Name.Text = s.Name;
        row.Role.Text = s.Role;
        row.Mark.Text = MarkOf(s.Agent);
        PaintAvatar(row.Avatar, s, 17);
    }

    // ── El panel ────────────────────────────────────────────────────────────────────────────

    void RenderMain(bool focusName = false)
    {
        _saved = null;
        _barName = null;
        _barAvatar = _heroAvatar = null;
        if (Current is not { } s)
        {
            _host.Child = Appear(EmptyState(), 0);
            return;
        }
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        grid.Children.Add(Bar(s));
        var doc = Document(s, out var name);
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = Appear(doc, 0) };
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);
        _host.Child = grid;
        if (focusName)
            Dispatcher.UIThread.Post(() =>
            {
                name.Focus();
                name.SelectAll();
            }, DispatcherPriority.Loaded);
    }

    Control Bar(Specialist s)
    {
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"), MinHeight = 58, Margin = new Thickness(14, 0, 12, 0) };
        // El ícono, el nombre y la etiqueta, pegados a la izquierda; con poco lugar el nombre se acorta con «…» (los botones no se salen).
        var crumb = new DockPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        _barAvatar = Avatar(s, 24, 8, 14);
        _barAvatar.Margin = new Thickness(0, 0, 9, 0);
        DockPanel.SetDock(_barAvatar, Dock.Left);
        crumb.Children.Add(_barAvatar);
        if (s.ReadOnly)
        {
            var chip = Chip(LockIcon, L.T("De una extensión"), _ink.Tertiary, _ink.Pill);
            chip.Margin = new Thickness(9, 0, 0, 0);
            DockPanel.SetDock(chip, Dock.Right);
            crumb.Children.Add(chip);
        }
        _barName = new TextBlock { Text = s.Name, FontFamily = Ink.Display, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = _ink.Text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        crumb.Children.Add(_barName);
        bar.Children.Add(crumb);

        _saved = new TextBlock { Text = "✓ " + L.T("Guardado"), FontFamily = Ink.Sans, FontSize = 12, Foreground = _ink.Tertiary, VerticalAlignment = VerticalAlignment.Center, Opacity = 0, Margin = new Thickness(0, 0, 6, 0) };
        _saved.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(300) }];
        Grid.SetColumn(_saved, 1);
        bar.Children.Add(_saved);
        var copy = Round(CopyIcon, L.T("Duplicar"), () => Duplicate(s));
        Grid.SetColumn(copy, 2);
        bar.Children.Add(copy);
        Border? more = null;
        more = Round(MoreIcon, L.T("Más opciones"), () => ShowMore(more!, s), fill: true);
        Grid.SetColumn(more, 3);
        bar.Children.Add(more);

        var runRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        runRow.Children.Add(Glyph(PlayIcon, Ink.On(_ink.Accent), 13, 0, fill: true));
        runRow.Children.Add(Ui.Text(L.T("Abrir"), 13, Ink.On(_ink.Accent), FontWeight.SemiBold));
        var run = Ui.Press(runRow, () => Launch?.Invoke(s), (b, on) => b.Opacity = on ? 0.9 : 1, L.T("Abrir sesión"));
        run.Background = _ink.Accent;
        run.Height = 34;
        run.Padding = new Thickness(11, 0, 14, 0);
        run.CornerRadius = new CornerRadius(999);
        run.Margin = new Thickness(4, 0, 0, 0);
        run.VerticalAlignment = VerticalAlignment.Center;
        run.RenderTransformOrigin = RelativePoint.Center;
        if (TerminalPane.Animate) run.Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(160) }];
        run.PointerPressed += (_, _) => run.RenderTransform = TransformOperations.Parse("scale(0.95)");
        run.PointerReleased += (_, _) => run.RenderTransform = TransformOperations.Identity;
        run.PointerExited += (_, _) => run.RenderTransform = TransformOperations.Identity;
        ToolTip.SetTip(run, L.T("Abrir sesión en la Terminal"));
        Grid.SetColumn(run, 4);
        bar.Children.Add(run);

        return new Border { BorderBrush = _ink.Hairline, BorderThickness = new Thickness(0, 0, 0, 1), Child = bar };
    }

    Control Document(Specialist s, out TextBox nameBox)
    {
        var ro = s.ReadOnly;
        var doc = new StackPanel { Spacing = 26, MaxWidth = 760, Margin = new Thickness(32, 28, 32, 90) };

        // El nombre, el rol y el ícono: se cambian en el lugar.
        var hero = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        _heroAvatar = Avatar(s, 64, 19, 30);
        Control avatar = _heroAvatar;
        if (!ro)
        {
            Border? press = null;
            press = Ui.Press(_heroAvatar, () => ShowLooks(press!, s), null, L.T("Color e ícono"));
            press.CornerRadius = new CornerRadius(19);
            press.RenderTransformOrigin = RelativePoint.Center;
            if (TerminalPane.Animate) press.Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(250), Easing = new CubicEaseOut() }];
            press.PointerEntered += (_, _) => press.RenderTransform = TransformOperations.Parse("scale(1.05) rotate(-3deg)");
            press.PointerExited += (_, _) => press.RenderTransform = TransformOperations.Identity;
            ToolTip.SetTip(press, L.T("Color e ícono"));
            avatar = press;
        }
        avatar.Margin = new Thickness(0, 0, 18, 0);
        avatar.VerticalAlignment = VerticalAlignment.Center;
        hero.Children.Add(avatar);
        var words = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var name = Inline(s.Name, L.T("Nombre"), 25, Ink.Display, FontWeight.SemiBold, _ink.Text, Specialist.MaxName, ro);
        nameBox = name;
        name.TextChanged += (_, _) =>
        {
            var text = name.Text ?? "";
            s.Name = text.Trim().Length > 0 ? text : L.T("Sin nombre");
            if (_barName is not null) _barName.Text = s.Name;
            PaintRow(s);
            Changed();
        };
        var roleBox = Inline(s.Role, L.T("Qué hace, en una línea"), 14, Ink.Sans, FontWeight.Normal, _ink.Secondary, Specialist.MaxRole, ro);
        roleBox.TextChanged += (_, _) =>
        {
            s.Role = roleBox.Text ?? "";
            PaintRow(s);
            Changed();
        };
        words.Children.Add(name);
        words.Children.Add(roleBox);
        Grid.SetColumn(words, 1);
        hero.Children.Add(words);
        doc.Children.Add(hero);

        if (ro)
        {
            var from = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            var icon = Glyph(PackageIcon, _ink.Secondary, 15, 1.8);
            icon.Margin = new Thickness(0, 0, 8, 0);
            from.Children.Add(icon);
            var note = Ui.Text(L.T("Viene con {0}. Para cambiarlo, duplíquelo.", s.Extension!), 13, _ink.Secondary);
            Grid.SetColumn(note, 1);
            from.Children.Add(note);
            var dup = Ui.Press(Ui.Text(L.T("Duplicar"), 13, _ink.Accent, FontWeight.SemiBold), () => Duplicate(s), (b, on) => b.Opacity = on ? 0.8 : 1, L.T("Duplicar"));
            dup.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(dup, 2);
            from.Children.Add(dup);
            doc.Children.Add(new Border { Background = _ink.Pill, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 9), Margin = new Thickness(0, -10, 0, 0), Child = from });
        }

        // Las instrucciones.
        var count = Ui.Text($"{s.Instructions.Length} / {Specialist.MaxInstructions}", 12, _ink.Tertiary);
        var text = new TextBox
        {
            Text = s.Instructions,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 120,
            MaxHeight = 340,
            MaxLength = Specialist.MaxInstructions,
            IsReadOnly = ro,
            Watermark = L.T("Cómo trabaja, qué cuida, qué evita…"),
            FontFamily = Ink.Sans,
            FontSize = 14,
            Padding = new Thickness(14, 12),
            CaretBrush = _ink.Accent,
            SelectionBrush = PlanPanel.Translucent(_ink.Accent, 0.35),
        };
        Paint(text, _ink.Raised, ro ? _ink.Secondary : _ink.Text, framed: true);
        text.TextChanged += (_, _) =>
        {
            s.Instructions = text.Text ?? "";
            count.Text = $"{s.Instructions.Length} / {Specialist.MaxInstructions}";
            Changed();
        };
        doc.Children.Add(Section(L.T("Instrucciones"), text, count));

        // El agente: cuál, y con Claude Code el modelo y el esfuerzo; la carpeta.
        var rows = new StackPanel { Spacing = 6 };
        var model = Row(L.T("Modelo"), ValuePick(s.Model is { } m ? Format.ModelChoice(m) : L.T("por defecto"), ro, anchor => PickModel(anchor, s)));
        var effort = Row(L.T("Esfuerzo"), ValuePick(EffortLabel(s.Effort), ro, anchor => PickEffort(anchor, s)));
        void Claude(bool animate)
        {
            var claude = s.Agent == AgentKinds.Claude;
            foreach (var row in new[] { model, effort }) Collapse(row, !claude, animate);
        }
        rows.Children.Add(Row(L.T("Agente"), AgentSegments(s, ro, () =>
        {
            Claude(animate: true);
            RenderTools(s);
            PaintRow(s);
            Changed();
        })));
        rows.Children.Add(model);
        rows.Children.Add(effort);
        rows.Children.Add(Row(L.T("Carpeta"), ValuePick(s.Folder ?? L.T("la del proyecto abierto"), ro, anchor => PickFolder(anchor, s), FolderIcon, s.Folder is null ? null : Ink.Mono)));
        Claude(animate: false);
        doc.Children.Add(Section(L.T("Agente"), rows));

        // Las herramientas.
        _tools = new StackPanel { Spacing = 6 };
        RenderTools(s);
        var tools = new StackPanel { Spacing = 6, Children = { _tools } };
        var more = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(4, 2, 0, 0) };
        more.Children.Add(Glyph(PackageIcon, _ink.Tertiary, 12, 1.8));
        more.Children.Add(Ui.Text(L.T("Más herramientas con extensiones MCP."), 12.5, _ink.Tertiary));
        var see = Ui.Press(Ui.Text(L.T("Ver extensiones"), 12.5, _ink.Accent, FontWeight.SemiBold), () => ExtensionsRequested?.Invoke(), (b, on) => b.Opacity = on ? 0.8 : 1, L.T("Ver extensiones"));
        more.Children.Add(see);
        tools.Children.Add(more);
        doc.Children.Add(Section(L.T("Herramientas"), tools));

        return new Border { Child = doc, HorizontalAlignment = HorizontalAlignment.Stretch };
    }

    StackPanel? _tools;

    void RenderTools(Specialist s)
    {
        if (_tools is null) return;
        _tools.Children.Clear();
        var index = 0;
        _tools.Children.Add(Appear(ToolRow(s, Specialist.CanvasTool, L.T("Canvas de Claupet"), L.T("Integrado · documentos y planes"), CanvasIcon, builtin: true), index++ * 40));
        foreach (var server in _servers)
        {
            var http = server.Type == McpServerSpec.Http;
            var where = http && Uri.TryCreate(server.Url, UriKind.Absolute, out var u) ? u.Host : null;
            var note = L.T("Extensión {0}", server.Extension) + (where is null ? "" : " · " + where);
            if (http && s.Agent is AgentKinds.Gemini or AgentKinds.Copilot) note += " · " + L.T("si {0} acepta servidores por internet", AgentKinds.Label(s.Agent));
            _tools.Children.Add(Appear(ToolRow(s, server.Name, server.Name, note, http ? GlobeIcon : PackageIcon), index++ * 40));
        }
        // Las que eligió y ya no están instaladas: se ven apagadas, para que no se pierdan sin aviso.
        foreach (var missing in s.Tools.Where(t => t.Value.On && t.Key != Specialist.CanvasTool && _servers.All(x => x.Name != t.Key)).Select(t => t.Key))
            _tools.Children.Add(Appear(ToolRow(s, missing, missing, L.T("No está instalada"), PackageIcon, missing: true), index++ * 40));
    }

    Control ToolRow(Specialist s, string id, string name, string note, string icon, bool builtin = false, bool missing = false)
    {
        var ro = s.ReadOnly || missing;
        var on = s.Uses(id);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        var tile = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Background = _ink.Pill, Margin = new Thickness(0, 0, 12, 0), Child = Glyph(icon, _ink.Secondary, 15, 1.8) };
        row.Children.Add(tile);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Ui.Text(name, 13.5, _ink.Text, FontWeight.Medium));
        words.Children.Add(Ui.Text(note, 12, missing ? _ink.Warn : _ink.Tertiary));
        Grid.SetColumn(words, 1);
        row.Children.Add(words);
        if (!builtin)
        {
            var asks = s.AsksFor(id);
            var shieldName = asks ? L.T("Pide permiso antes de usarla") : L.T("La usa sin preguntar");
            var shield = Ui.Press(Glyph(asks ? ShieldIcon : ShieldCheckIcon, asks ? _ink.Tertiary : _ink.Accent, 15, 1.8), () =>
            {
                if (ro || !on) return;
                Tool(s, id).Ask = !asks;
                Changed();
                RenderTools(s);
            }, ro || !on ? null : (b, hover) => b.Background = hover ? _ink.Pill : asks ? Brushes.Transparent : PlanPanel.Translucent(_ink.Accent, 0.13), shieldName);
            shield.Width = shield.Height = 30;
            shield.CornerRadius = new CornerRadius(999);
            shield.Margin = new Thickness(6, 0, 6, 0);
            shield.Background = asks ? Brushes.Transparent : PlanPanel.Translucent(_ink.Accent, 0.13);
            shield.Opacity = ro || !on ? 0.4 : 1;
            ToolTip.SetTip(shield, shieldName);
            Grid.SetColumn(shield, 2);
            row.Children.Add(shield);
        }
        var toggle = Switch(on, ro, value =>
        {
            Tool(s, id).On = value;
            Changed();
            RenderTools(s);
        }, name);
        Grid.SetColumn(toggle, 3);
        row.Children.Add(toggle);
        var card = new Border
        {
            Background = _ink.Raised,
            BorderBrush = _ink.Hairline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 9, 8, 9),
            Opacity = on && !missing ? 1 : 0.62,
            Child = row,
        };
        return card;
    }

    static SpecialistTool Tool(Specialist s, string id)
    {
        if (!s.Tools.TryGetValue(id, out var tool)) s.Tools[id] = tool = new SpecialistTool();
        return tool;
    }

    /// <summary>El agente: las marcas en una pastilla con una perilla que se desliza; con lugar, también el nombre.</summary>
    Control AgentSegments(Specialist s, bool ro, Action changed)
    {
        var host = new Border { HorizontalAlignment = HorizontalAlignment.Right };
        var wide = true;
        void Build()
        {
            var width = wide ? 104.0 : 40.0;
            var index = Math.Max(0, Array.FindIndex(Agents, a => a.Kind == s.Agent));
            var knob = new Border { Width = width, Height = 28, CornerRadius = new CornerRadius(999), Background = _ink.Raised, BoxShadow = BoxShadows.Parse("0 1 3 0 #24000000"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(index * width, 0, 0, 0) };
            if (TerminalPane.Animate) knob.Transitions = [new ThicknessTransition { Property = MarginProperty, Duration = TimeSpan.FromMilliseconds(320), Easing = new CubicEaseOut() }];
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (kind, mark) in Agents)
            {
                var chosen = kind == s.Agent;
                var face = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
                face.Children.Add(Ui.Text(mark, 11.5, chosen ? _ink.Text : _ink.Tertiary, FontWeight.SemiBold, Ink.Mono));
                if (wide) face.Children.Add(Ui.Text(AgentKinds.Label(kind), 12.5, chosen ? _ink.Text : _ink.Tertiary));
                var k = kind;
                var button = Ui.Press(face, () =>
                {
                    if (ro || s.Agent == k) return;
                    s.Agent = k;
                    if (k != AgentKinds.Claude) s.Model = s.Effort = null;
                    knob.Margin = new Thickness(Array.FindIndex(Agents, a => a.Kind == k) * width, 0, 0, 0);
                    // Los colores de las letras, después de que llega la perilla.
                    DispatcherTimer.RunOnce(Build, TimeSpan.FromMilliseconds(TerminalPane.Animate ? 200 : 0));
                    changed();
                }, null, AgentKinds.Label(kind));
                button.Width = width;
                button.Height = 28;
                button.CornerRadius = new CornerRadius(999);
                ToolTip.SetTip(button, AgentKinds.Label(kind));
                buttons.Children.Add(button);
            }
            host.Child = new Border
            {
                Background = _ink.Surface,
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(3),
                Opacity = ro ? 0.8 : 1,
                Child = new Panel { Children = { knob, buttons } },
            };
        }
        Build();
        // Sin lugar para los nombres, solo las marcas (el nombre queda en el tooltip).
        host.AttachedToVisualTree += (_, _) =>
        {
            if (host.Parent is Control parent)
                parent.SizeChanged += (_, e) =>
                {
                    var fits = e.NewSize.Width >= 4 * 104 + 6 + 8;
                    if (fits == wide) return;
                    wide = fits;
                    Build();
                };
        };
        return host;
    }

    // ── Menús ───────────────────────────────────────────────────────────────────────────────

    void PickModel(Control anchor, Specialist s)
    {
        string[] models = ["", "haiku", "sonnet", "opus", "fable"];
        DropList.Show(anchor, _ink, [.. models.Select(m => new DropList.Entry(m, m.Length == 0 ? L.T("por defecto") : Format.ModelChoice(m), (s.Model ?? "") == m))], picked =>
        {
            if (picked is null) return;
            s.Model = picked.Length == 0 ? null : picked;
            Changed();
            RenderMain();
        });
    }

    void PickEffort(Control anchor, Specialist s)
    {
        DropList.Show(anchor, _ink, [.. Launcher.Efforts.Prepend("").Select(e => new DropList.Entry(e, EffortLabel(e.Length == 0 ? null : e), (s.Effort ?? "") == e))], picked =>
        {
            if (picked is null) return;
            s.Effort = picked.Length == 0 ? null : picked;
            Changed();
            RenderMain();
        });
    }

    void PickFolder(Control anchor, Specialist s)
    {
        var folders = TerminalRecents.Load().Folders.Where(Directory.Exists).Take(6).ToList();
        if (s.Folder is { } mine && !folders.Contains(mine, StringComparer.OrdinalIgnoreCase)) folders.Insert(0, mine);
        List<DropList.Entry> entries = [new("", L.T("La del proyecto abierto"), s.Folder is null)];
        entries.AddRange(folders.Select(f => new DropList.Entry("f:" + f, f, string.Equals(f, s.Folder, StringComparison.OrdinalIgnoreCase))));
        entries.Add(new DropList.Entry("browse", L.T("Elegir otra carpeta…"), Action: true));
        DropList.Show(anchor, _ink, entries, async picked =>
        {
            if (picked is null) return;
            if (picked == "browse")
            {
                if (TopLevel.GetTopLevel(this) is not { } top) return;
                if (await TerminalPane.PickPath(top, s.Folder ?? global::Claupet.Account.Home, false) is not { } chosen) return;
                s.Folder = chosen;
            }
            else s.Folder = picked.Length == 0 ? null : picked[2..];
            Changed();
            RenderMain();
        });
    }

    void ShowMore(Control anchor, Specialist s)
    {
        List<DropList.Entry> entries = [new("export", L.T("Exportar como .json…"))];
        if (!s.ReadOnly) entries.Add(new("delete", L.T("Borrar…"), Action: true, Danger: true));
        DropList.Show(anchor, _ink, entries, picked =>
        {
            if (picked == "export") _ = ExportAsync(s);
            if (picked == "delete") _ = DeleteAsync(s);
        });
    }

    async Task ExportAsync(Specialist s)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = string.Concat(s.Name.Split(System.IO.Path.GetInvalidFileNameChars())) + ".json",
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try { await File.WriteAllTextAsync(path, Specialists.Export(s)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
    }

    async Task DeleteAsync(Specialist s)
    {
        var yes = await ConfirmCard.Ask(this, _ink, L.T("¿Borrar «{0}»?", s.Name), L.T("Sus sesiones abiertas siguen; solo se quita de la lista."), L.T("Borrar"), L.T("Cancelar"));
        if (!yes) return;
        _saveTimer.Stop();
        Specialists.Delete(s);
        var at = _all.IndexOf(s);
        _all.Remove(s);
        _selected = _all.Count == 0 ? null : _all[Math.Clamp(at, 0, _all.Count - 1)].Id;
        RenderList(animate: false);
        RenderMain();
    }

    void Duplicate(Specialist s)
    {
        Flush();
        var copy = s.Copy(L.T("{0} (copia)", s.Name));
        Specialists.Save(copy);
        _all.Insert(_all.Count(x => !x.ReadOnly), copy);
        _selected = copy.Id;
        RenderList(animate: false);
        RenderMain(focusName: true);
    }

    /// <summary>El color y el ícono, en una tarjeta que flota bajo el ícono grande.</summary>
    void ShowLooks(Control anchor, Specialist s)
    {
        if (TopLevel.GetTopLevel(anchor)?.Content is not Panel host) return;
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.BottomEdgeAlignedLeft, VerticalOffset = 6, IsLightDismissEnabled = true, ShouldUseOverlayLayer = true };
        var body = new StackPanel { Spacing = 2 };
        void Draw()
        {
            body.Children.Clear();
            body.Children.Add(Caption(L.T("Color")));
            var colors = new WrapPanel { Width = 6 * 36, Margin = new Thickness(8, 4, 8, 6) };
            foreach (var hex in Specialist.Colors)
            {
                var brush = new SolidColorBrush(Color.Parse(hex));
                var chosen = hex == s.Color;
                var dot = new Panel { Width = 28, Height = 28 };
                if (chosen) dot.Children.Add(new Ellipse { Width = 34, Height = 34, Margin = new Thickness(-3), Stroke = brush, StrokeThickness = 2 });
                dot.Children.Add(new Ellipse { Width = 28, Height = 28, Fill = brush });
                var h = hex;
                var press = Ui.Press(dot, () =>
                {
                    s.Color = h;
                    Looked(s);
                    Draw();
                }, null, L.T("Color"));
                press.Margin = new Thickness(0, 0, 8, 8);
                press.CornerRadius = new CornerRadius(999);
                colors.Children.Add(press);
            }
            body.Children.Add(colors);
            body.Children.Add(Caption(L.T("Ícono")));
            var glyphs = new WrapPanel { Width = 6 * 38, Margin = new Thickness(6, 2, 6, 6) };
            foreach (var glyph in Specialist.Glyphs)
            {
                var chosen = glyph == s.Glyph;
                var g = glyph;
                var press = Ui.Press(Glyph(Glyphs[glyph], chosen ? _ink.Accent : _ink.Secondary, 18, 1.7), () =>
                {
                    s.Glyph = g;
                    Looked(s);
                    Draw();
                }, (b, on) => b.Background = on && !chosen ? _ink.Pill : chosen ? PlanPanel.Translucent(_ink.Accent, 0.13) : Brushes.Transparent, L.T("Ícono"));
                press.Width = press.Height = 34;
                press.Margin = new Thickness(0, 0, 4, 4);
                press.CornerRadius = new CornerRadius(9);
                press.Background = chosen ? PlanPanel.Translucent(_ink.Accent, 0.13) : Brushes.Transparent;
                glyphs.Children.Add(press);
            }
            body.Children.Add(glyphs);
        }
        Draw();
        popup.Child = new Border
        {
            Background = _ink.Raised,
            BorderBrush = _ink.Hairline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(6),
            BoxShadow = BoxShadows.Parse(_ink.Dark ? "0 14 34 0 #40000000" : "0 14 34 0 #24000000"),
            Child = body,
        };
        popup.Closed += (_, _) => host.Children.Remove(popup);
        host.Children.Add(popup);
        popup.IsOpen = true;
    }

    /// <summary>Cambió el color o el ícono: se pintan los tres avatares (sin rehacer nada) y se guarda.</summary>
    void Looked(Specialist s)
    {
        if (_heroAvatar is not null)
        {
            PaintAvatar(_heroAvatar, s, 30);
            if (TerminalPane.Animate)
            {
                _heroAvatar.RenderTransformOrigin = RelativePoint.Center;
                _heroAvatar.Transitions = null;
                _heroAvatar.RenderTransform = TransformOperations.Parse("scale(0.85)");
                _heroAvatar.Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(320), Easing = new BackEaseOut() }];
                Dispatcher.UIThread.Post(() => _heroAvatar.RenderTransform = TransformOperations.Identity, DispatcherPriority.Render);
            }
        }
        if (_barAvatar is not null) PaintAvatar(_barAvatar, s, 14);
        PaintRow(s);
        Changed();
    }

    // ── Vacío ───────────────────────────────────────────────────────────────────────────────

    Control EmptyState()
    {
        var panel = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 360, Margin = new Thickness(24) };
        var avatar = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(19), Background = PlanPanel.Translucent(_ink.Accent, 0.15), Child = Glyph(Icon, _ink.Accent, 30, 1.6), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
        panel.Children.Add(avatar);
        panel.Children.Add(new TextBlock { Text = L.T("Sus especialistas"), FontFamily = Ink.Display, FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = _ink.Text, HorizontalAlignment = HorizontalAlignment.Center });
        var badge = Badge(_ink);
        badge.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(badge);
        panel.Children.Add(new TextBlock
        {
            Text = L.T("Un agente con su rol, sus instrucciones y sus herramientas, listo para abrir en la Terminal."),
            FontFamily = Ink.Sans, FontSize = 14, Foreground = _ink.Secondary, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 2, 0, 10),
        });
        var add = Ui.Press(Glyph(PlusIcon, Ink.On(_ink.Accent), 15, 2.2), New, (b, on) => b.Opacity = on ? 0.88 : 1, L.T("Especialista nuevo"));
        add.Width = add.Height = 34;
        add.CornerRadius = new CornerRadius(999);
        add.Background = _ink.Accent;
        add.HorizontalAlignment = HorizontalAlignment.Center;
        ToolTip.SetTip(add, L.T("Especialista nuevo"));
        panel.Children.Add(add);
        if (TerminalPane.Animate)
        {
            // Flota apenas, como en el prototipo.
            var up = true;
            avatar.Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(1600), Easing = new SineEaseInOut() }];
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
            timer.Tick += (_, _) =>
            {
                if (avatar.GetVisualRoot() is null) { timer.Stop(); return; }
                avatar.RenderTransform = TransformOperations.Parse(up ? "translateY(-5px)" : "translateY(0px)");
                up = !up;
            };
            avatar.AttachedToVisualTree += (_, _) => timer.Start();
            avatar.DetachedFromVisualTree += (_, _) => timer.Stop();
        }
        return panel;
    }

    /// <summary>La etiqueta «Experimental»: el módulo todavía no está terminado.</summary>
    public static Border Badge(Ink ink)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Children.Add(Glyph(FlaskIcon, ink.Accent, 11, 2));
        row.Children.Add(new TextBlock { Text = L.T("Experimental"), FontFamily = Ink.Sans, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = ink.Accent, VerticalAlignment = VerticalAlignment.Center });
        var badge = new Border
        {
            Child = row,
            Height = 19,
            Padding = new Thickness(6, 0, 8, 0),
            CornerRadius = new CornerRadius(999),
            Background = PlanPanel.Translucent(ink.Accent, 0.15),
            HorizontalAlignment = HorizontalAlignment.Left,
            Focusable = true,
        };
        ToolTip.SetTip(badge, L.T("Aún en desarrollo: puede cambiar o fallar"));
        return badge;
    }

    // ── Piezas ──────────────────────────────────────────────────────────────────────────────

    Border Avatar(Specialist s, double size, double radius, double icon)
    {
        var avatar = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(radius), VerticalAlignment = VerticalAlignment.Center };
        PaintAvatar(avatar, s, icon);
        return avatar;
    }

    void PaintAvatar(Border avatar, Specialist s, double icon)
    {
        var color = new SolidColorBrush(Color.Parse(s.Color));
        avatar.Background = PlanPanel.Translucent(color, 0.15);
        avatar.Child = Glyph(Glyphs.TryGetValue(s.Glyph, out var path) ? path : Glyphs["search"], color, icon, 1.7);
    }

    /// <summary>Un TextBox sin caja que se marca al pasar y al escribir (el nombre y el rol).</summary>
    TextBox Inline(string value, string hint, double size, FontFamily family, FontWeight weight, IBrush brush, int max, bool ro)
    {
        var box = new TextBox
        {
            Text = value,
            Watermark = hint,
            FontSize = size,
            FontFamily = family,
            FontWeight = weight,
            MaxLength = max,
            IsReadOnly = ro,
            IsHitTestVisible = !ro,
            Padding = new Thickness(8, 2),
            Margin = new Thickness(-8, 0, 0, 0),
            MinHeight = 0,
            CaretBrush = _ink.Accent,
            SelectionBrush = PlanPanel.Translucent(_ink.Accent, 0.35),
            // Un nombre o un rol largos pasan a la línea siguiente en vez de cortarse.
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false,
        };
        Paint(box, Brushes.Transparent, brush, framed: false);
        if (!ro)
        {
            box.Resources["TextControlBackgroundPointerOver"] = _ink.Pill;
            box.Resources["TextControlBackgroundFocused"] = _ink.Raised;
            box.Resources["TextControlBorderBrushFocused"] = _ink.Accent;
            box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(1.5);
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape) TopLevel.GetTopLevel(box)?.FocusManager?.ClearFocus();
        };
        return box;
    }

    /// <summary>Los colores del TextBox en todos sus estados (el tema de Fluent pone los suyos).</summary>
    void Paint(TextBox box, IBrush background, IBrush foreground, bool framed)
    {
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
            box.Resources[key] = background;
        IBrush border = framed ? _ink.Hairline : Brushes.Transparent;
        foreach (var key in new[] { "TextControlBorderBrush", "TextControlBorderBrushPointerOver" })
            box.Resources[key] = border;
        box.Resources["TextControlBorderBrushFocused"] = framed ? _ink.Accent : Brushes.Transparent;
        box.Resources["TextControlBorderThemeThickness"] = new Thickness(framed ? 1 : 0);
        box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(framed ? 1.5 : 0);
        foreach (var key in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" })
            box.Resources[key] = foreground;
        foreach (var key in new[] { "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused" })
            box.Resources[key] = _ink.Tertiary;
        box.Resources["ControlCornerRadius"] = new CornerRadius(framed ? 12 : 8);
        box.CornerRadius = new CornerRadius(framed ? 12 : 8);
    }


    Control Section(string title, Control body, Control? right = null)
    {
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(Ui.Text(title, 12, _ink.Tertiary, FontWeight.SemiBold));
        if (right is not null)
        {
            Grid.SetColumn(right, 1);
            head.Children.Add(right);
        }
        return new StackPanel { Children = { head, body } };
    }

    Border Row(string label, Control value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*") };
        var text = Ui.Text(label, 13, _ink.Secondary);
        grid.Children.Add(text);
        Grid.SetColumn(value, 1);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        grid.Children.Add(value);
        return new Border { Background = _ink.Pill, CornerRadius = new CornerRadius(10), MinHeight = 42, Padding = new Thickness(14, 4, 6, 4), ClipToBounds = true, Child = grid };
    }

    /// <summary>Esconde o muestra una fila; con animación, se encoge y se desvanece.</summary>
    static void Collapse(Border row, bool hide, bool animate)
    {
        if (!animate || !TerminalPane.Animate)
        {
            row.IsVisible = !hide;
            row.Opacity = hide ? 0 : 1;
            return;
        }
        row.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) }];
        if (hide)
        {
            row.Opacity = 0;
            DispatcherTimer.RunOnce(() => { if (row.Opacity == 0) row.IsVisible = false; }, TimeSpan.FromMilliseconds(220));
        }
        else
        {
            row.IsVisible = true;
            Dispatcher.UIThread.Post(() => row.Opacity = 1, DispatcherPriority.Render);
        }
    }

    Control ValuePick(string value, bool ro, Action<Control> pick, string? icon = null, FontFamily? family = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var text = Ui.Text(value, 13, ro ? _ink.Secondary : _ink.Text, family: family);
        text.TextTrimming = family is null ? TextTrimming.CharacterEllipsis : TextTrimming.PrefixCharacterEllipsis;
        text.MaxWidth = 340;
        row.Children.Add(text);
        Control mark = icon is null ? Ui.Chevron(_ink.Tertiary) : Glyph(icon, _ink.Tertiary, 14, 1.8);
        mark.VerticalAlignment = VerticalAlignment.Center;
        if (!ro) row.Children.Add(mark);
        Border? button = null;
        button = Ui.Press(row, () => { if (!ro) pick(button!); }, ro ? null : (b, on) => b.Background = on ? _ink.Raised : Brushes.Transparent, value);
        button.Height = 32;
        button.Padding = new Thickness(10, 0, 8, 0);
        button.CornerRadius = new CornerRadius(8);
        if (ro) button.Cursor = Cursor.Default;
        ToolTip.SetTip(button, value);
        return button;
    }

    Control Switch(bool on, bool ro, Action<bool> changed, string name)
    {
        var knob = new Ellipse { Width = 16, Height = 16, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(on ? 19 : 3, 0, 0, 0) };
        var track = Ui.Press(knob, () =>
        {
            if (ro) return;
            on = !on;
            changed(on);
        }, null, name);
        track.Width = 38;
        track.Height = 22;
        track.CornerRadius = new CornerRadius(11);
        track.Background = on ? _ink.Accent : _ink.Track;
        track.Opacity = ro ? 0.5 : 1;
        if (ro) track.Cursor = Cursor.Default;
        Avalonia.Automation.AutomationProperties.SetName(track, name);
        return track;
    }

    Border Chip(string icon, string text, IBrush foreground, IBrush background)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        row.Children.Add(Glyph(icon, foreground, 12, 1.8));
        row.Children.Add(Ui.Text(text, 11.5, foreground));
        return new Border { Child = row, Background = background, CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 2, 9, 2), VerticalAlignment = VerticalAlignment.Center };
    }

    TextBlock Caption(string text) => new() { Text = text, FontFamily = Ink.Sans, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = _ink.Tertiary, Margin = new Thickness(10, 6, 10, 3) };

    Border Round(string path, string name, Action onClick, bool fill = false)
    {
        var button = Ui.Press(Glyph(path, _ink.Secondary, fill ? 17 : 15, fill ? 0 : 1.8, fill), onClick, (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, name);
        button.Width = button.Height = 34;
        button.CornerRadius = new CornerRadius(999);
        button.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(button, name);
        return button;
    }

    /// <summary>Un ícono del cuadro de 24: con trazo o, con fill, relleno.</summary>
    static Viewbox Glyph(string path, IBrush brush, double size, double thickness, bool fill = false) => new()
    {
        Width = size,
        Height = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(path),
            Stroke = fill ? null : brush,
            Fill = fill ? brush : null,
            StrokeThickness = thickness,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 24,
            Height = 24,
        },
    };

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

    /// <summary>Aparece subiendo un poco y aclarándose, después de delay ms (con delay negativo, sin animación).</summary>
    static T Appear<T>(T control, int delay) where T : Control
    {
        if (delay < 0 || !TerminalPane.Animate) return control;
        control.Opacity = 0;
        control.RenderTransform = TransformOperations.Parse("translateY(8px)");
        control.Transitions =
        [
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(380), Easing = new CubicEaseOut() },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(420), Easing = new CubicEaseOut() },
        ];
        DispatcherTimer.RunOnce(() =>
        {
            control.Opacity = 1;
            control.RenderTransform = TransformOperations.Identity;
        }, TimeSpan.FromMilliseconds(20 + delay));
        return control;
    }
}
