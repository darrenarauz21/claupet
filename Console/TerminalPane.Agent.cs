namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

/// <summary>
/// La sesión de un agente, como en el diseño: arriba la carpeta y la rama, el contexto usado (una barra), interrumpir, el
/// panel y cerrar; en el medio, los bloques separados por una línea fina (lo pedido, lo que respondió y lo que hizo, los
/// permisos con botones de texto, las tareas en una línea); abajo, una caja de una línea con la cuenta, el modelo y el
/// esfuerzo, y los permisos. Todo el texto se puede seleccionar y copiar (Ctrl+C o clic derecho), y lo pedido y cada
/// respuesta tienen su botón de copiar al pasar el mouse.
/// </summary>
sealed partial class TerminalPane
{
    const string FolderIcon = "M3.5,7 A2,2 0 0 1 5.5,5 H9.5 L11.5,7 H18.5 A2,2 0 0 1 20.5,9 V17 A2,2 0 0 1 18.5,19 H5.5 A2,2 0 0 1 3.5,17 Z";
    const string PauseBars = "M8.5,6 V18 M15.5,6 V18";

    /// <summary>Las listas de tareas abiertas (por omisión se ven en una línea: la que se hace y cuántas van).</summary>
    readonly HashSet<AgentBlock> _todoOpen = [];

    // ── Arriba ──────────────────────────────────────────────────────────────────────────────

    Control SessionHeader(Tab tab)
    {
        // La carpeta y la rama se achican (la rama se corta con «…») para que el contexto y los botones siempre quepan.
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"), Margin = new Thickness(16, 0, 8, 0) };
        var where = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*,Auto"), VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        var folder = Icon(FolderIcon, _ink.Tertiary, 13, 1.8);
        folder.Margin = new Thickness(0, 0, 5, 0);
        where.Children.Add(folder);
        var project = Ui.Text(tab.Project, 12.5, _ink.Text, FontWeight.Medium, Mono);
        project.TextTrimming = TextTrimming.CharacterEllipsis;
        project.MaxWidth = 180;
        project.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(project, 1);
        where.Children.Add(project);
        if (tab.Branch is { } branch)
        {
            var sep = Ui.Text("/", 12.5, _ink.Track);
            sep.Margin = new Thickness(6, 0);
            sep.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(sep, 2);
            where.Children.Add(sep);
            var icon = Icon(BranchIcon, _ink.Tertiary, 13, 1.8);
            icon.Margin = new Thickness(0, 0, 5, 0);
            Grid.SetColumn(icon, 3);
            where.Children.Add(icon);
            var name = Ui.Text(branch, 12.5, _ink.Secondary, family: Mono);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.VerticalAlignment = VerticalAlignment.Center;
            ToolTip.SetTip(name, branch);
            Grid.SetColumn(name, 4);
            where.Children.Add(name);
        }
        if (_git.TryGetValue(tab.Agent.Options.Folder, out var git) && git.Status is { } status)
        {
            var summary = Ui.Text(status.Summary, 11.5, _ink.Tertiary, family: Mono);
            summary.Margin = new Thickness(6, 0, 0, 0);
            summary.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(summary, 5);
            where.Children.Add(summary);
        }
        ToolTip.SetTip(where, tab.Agent.Options.Folder);
        bar.Children.Add(where);

        // El contexto usado: una barra corta con su porcentaje.
        _context = Ui.Text("", 12, _ink.Secondary);
        _context.VerticalAlignment = VerticalAlignment.Center;
        _contextFill = new Border { Background = _ink.Accent, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        var track = new Border { Width = 44, Height = 4, CornerRadius = new CornerRadius(2), Background = _ink.Track, ClipToBounds = true, Child = _contextFill, VerticalAlignment = VerticalAlignment.Center };
        var context = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) };
        context.Children.Add(Ui.Text(L.T("contexto"), 12, _ink.Secondary));
        context.Children.Add(track);
        context.Children.Add(_context);
        _contextBox = context;
        Grid.SetColumn(context, 1);
        bar.Children.Add(context);
        _interrupt = Small(Round(PauseBars, L.T("Interrumpir (Esc)"), () => tab.Agent.Interrupt()));
        Grid.SetColumn(_interrupt, 2);
        bar.Children.Add(_interrupt);
        var panel = Small(Round(PanelIcon, L.T("Ramas, cambios y procesos"), ToggleSide));
        Grid.SetColumn(panel, 3);
        bar.Children.Add(panel);
        var close = Small(Round(CloseIcon, L.T("Cerrar la pestaña"), () => CloseTab(tab)));
        Grid.SetColumn(close, 4);
        bar.Children.Add(close);
        return bar;
    }

    /// <summary>El contexto usado (y el uso de la cuenta, al pasar el mouse) e Interrumpir mientras trabaja.</summary>
    void PaintStatus()
    {
        if (_current is not { } tab) return;
        var t = tab.Agent.Transcript;
        _contextBox.IsVisible = t.ContextPercent is not null;
        if (t.ContextPercent is { } context)
        {
            _context.Text = L.T("{0:0} %", context);
            _contextFill.Width = 44 * Math.Clamp(context / 100, 0, 1);
            ToolTip.SetTip(_contextBox, t.FiveHour is { } five
                ? L.T("Contexto usado de esta sesión · la cuenta lleva {0:0} % de su sesión de 5 horas", five)
                : L.T("Contexto usado de esta sesión"));
        }
        _interrupt.IsVisible = t.Busy && tab.Agent.Running;
        _send.Opacity = tab.Agent.Running ? 1 : 0.45;
    }

    // ── Abajo ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// La caja de abajo es una sola para todas las pestañas y cada una la acomoda a su manera: antes de ponerla en otra se
    /// le quita lo que le dejó la anterior. La de un shell la pone en la columna 1, que en la de Claude Code es la de la
    /// cuenta y el modelo, y la caja quedaba encima de ellos; la de Claude Code, angosta, la deja ocupando tres columnas.
    /// </summary>
    void ResetInput()
    {
        foreach (var property in new AvaloniaProperty[]
            { Grid.RowProperty, Grid.ColumnProperty, Grid.RowSpanProperty, Grid.ColumnSpanProperty, MarginProperty, MinWidthProperty, VerticalAlignmentProperty })
            _input.ClearValue(property);
    }

    Control Composer(Tab tab)
    {
        var agent = tab.Agent;
        var claude = agent.Options.Kind == AgentKinds.Claude;
        ResetInput();
        _input.AcceptsReturn = true;
        _input.TextWrapping = TextWrapping.Wrap;
        _input.MinHeight = 22;
        _input.MaxHeight = 180;
        Plain(_input);
        _input.FontFamily = Ink.Sans;
        _input.FontSize = 13.5;
        _input.Watermark = L.T("Escriba a {0}…", AgentKinds.Label(agent.Options.Kind));
        _input.VerticalAlignment = VerticalAlignment.Center;
        _input.Margin = new Thickness(8, 0, 4, 0);
        Ui.AutomationName(_input, L.T("Mensaje para {0}", AgentKinds.Label(agent.Options.Kind)));
        // Las teclas, al pasar el cursor por la caja (no una línea más debajo).
        ToolTip.SetTip(_input, L.T("Intro envía · Mayús+Intro, otra línea · Esc interrumpe"));

        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), MinHeight = 34 };
        _input.MinWidth = 160;
        line.Children.Add(_input);

        // La cuenta (cambiarla abre otra pestaña), el modelo con el esfuerzo y los permisos: se tocan para cambiarlos.
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        if (claude)
        {
            var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = Added, VerticalAlignment = VerticalAlignment.Center };
            Border? account = null;
            account = Pick(tab.Account, () => AccountMenu(account!, tab), dot);
            ToolTip.SetTip(account, L.T("La cuenta de esta pestaña. Cambiarla abre una pestaña nueva con esa cuenta."));
            chips.Children.Add(account);
        }
        else chips.Children.Add(Pick(AgentKinds.Label(agent.Options.Kind), null));
        TextBlock? modelText = null;
        Border? model = null;
        model = Pick(ModelAndEffort(tab), () => ModelMenu(model!, tab, () => modelText!.Text = ModelAndEffort(tab)));
        modelText = model.Child is StackPanel { Children: [.., TextBlock label] } ? label : null;
        ToolTip.SetTip(model, L.T("El modelo y el esfuerzo de esta pestaña: valen desde el turno siguiente."));
        chips.Children.Add(model);
        var mode = agent.Transcript.PermissionMode ?? agent.Options.PermissionMode ?? "default";
        if (agent.Driver.Modes.Count > 0)
        {
            TextBlock? modeText = null;
            Border? modeChip = null;
            modeChip = Pick(ModeLabel(mode), () => DropList.Show(modeChip!, _ink,
                agent.Driver.Modes.Select(m => new DropList.Entry(m, ModeLabel(m), m == (agent.Transcript.PermissionMode ?? mode))).ToList(), picked =>
                {
                    if (picked is null) return;
                    agent.SetPermissionMode(picked);
                    modeText!.Text = ModeLabel(picked);
                }));
            modeText = modeChip.Child is StackPanel { Children: [.., TextBlock text] } ? text : null;
            ToolTip.SetTip(modeChip, L.T("Los permisos de esta pestaña"));
            chips.Children.Add(modeChip);
        }
        Grid.SetColumn(chips, 1);
        line.Children.Add(chips);
        var hint = Ui.Text(L.T("Intro envía"), 11.5, _ink.Tertiary);
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Margin = new Thickness(6, 0, 4, 0);
        Grid.SetColumn(hint, 2);
        line.Children.Add(hint);
        var arrow = Icon(SendIcon, _ink.Tertiary, 15, 2);
        _send = Ui.Press(arrow, Submit, (b, on) =>
        {
            b.Background = on ? _ink.Pill : Brushes.Transparent;
            ((Avalonia.Controls.Shapes.Path)arrow.Child!).Stroke = on ? _ink.Accent : _ink.Tertiary;
        }, L.T("Enviar"));
        _send.Width = 30;
        _send.Height = 30;
        _send.CornerRadius = new CornerRadius(9);
        _send.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(_send, L.T("Enviar (Intro)"));
        Grid.SetColumn(_send, 3);
        line.Children.Add(_send);

        // Angosta (con el panel de la derecha abierto, una ventana chica): la cuenta, el modelo y los permisos pasan debajo
        // de lo que se escribe, para que la caja no se parta en varias líneas.
        void Arrange(double width)
        {
            var narrow = width < 600;
            Grid.SetRow(chips, narrow ? 1 : 0);
            Grid.SetColumn(chips, narrow ? 0 : 1);
            chips.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            chips.Margin = narrow ? new Thickness(2, 2, 0, 2) : default;
            Grid.SetColumnSpan(_input, narrow ? 3 : 1);
            Grid.SetRow(hint, narrow ? 1 : 0);
            Grid.SetColumn(hint, narrow ? 2 : 2);
            hint.IsVisible = !narrow;
        }
        var card = new Border
        {
            Margin = new Thickness(12, 0, 12, 12),
            CornerRadius = new CornerRadius(12),
            Background = _ink.Raised,
            BorderBrush = _ink.Hairline,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4),
            MinHeight = 42,
            Child = line,
        };
        card.SizeChanged += (_, e) => Arrange(e.NewSize.Width);
        Arrange(Bounds.Width > 0 ? Bounds.Width - (_projects.PanelOpen ? _projects.PanelWidth : 0) - 24 : 800);
        return card;
    }

    /// <summary>Una etiqueta fija (la terminal de la caja de un shell).</summary>
    Control Chip(string text)
    {
        var label = Ui.Text(text, 11.5, _ink.Secondary);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.MaxWidth = 220;
        return new Border { Background = _ink.Pill, CornerRadius = new CornerRadius(999), Padding = new Thickness(9, 2), Child = label };
    }

    /// <summary>«opus · alto»: el modelo que contestó (o el elegido) y el esfuerzo.</summary>
    static string ModelAndEffort(Tab tab)
    {
        var model = tab.Agent.Transcript.Model?.Replace("claude-", "") ?? tab.Agent.Options.Model ?? L.T("modelo por defecto");
        return tab.Agent.Effort is { } effort ? $"{model} · {EffortLabel(effort)}" : model;
    }

    /// <summary>Una etiqueta de la caja: texto chico que se resalta al pasar el mouse y, si hace algo, abre su lista.</summary>
    Border Pick(string text, Action? onClick, Control? lead = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        if (lead is not null) row.Children.Add(lead);
        var label = Ui.Text(text, 12, _ink.Secondary);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.MaxWidth = 180;
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        var chip = onClick is null
            ? new Border { Child = row }
            : Ui.Press(row, onClick, (b, on) =>
            {
                b.Background = on ? _ink.Pill : Brushes.Transparent;
                label.Foreground = on ? _ink.Text : _ink.Secondary;
            }, text);
        chip.Height = 24;
        chip.Padding = new Thickness(7, 0);
        chip.CornerRadius = new CornerRadius(7);
        chip.VerticalAlignment = VerticalAlignment.Center;
        return chip;
    }

    /// <summary>La cuenta de la pestaña: elegir otra abre una pestaña nueva con esa cuenta (la de ahora sigue).</summary>
    void AccountMenu(Control anchor, Tab tab)
    {
        var accounts = Accounts();
        if (accounts.Count == 0) return;
        var current = tab.Agent.Options.ConfigDir;
        DropList.Show(anchor, _ink, accounts.Select(a => new DropList.Entry(a.ConfigDir,
            $"{a.Label} · {AssistantAccounts.StatusOf(a, new Dictionary<string, UsageSnapshot>()).Text}",
            global::Claupet.Account.SamePath(a.ConfigDir, current ?? ""))).ToList(), picked =>
            {
                if (picked is null || global::Claupet.Account.SamePath(picked, current ?? "")) return;
                var account = accounts.First(a => a.ConfigDir == picked);
                OpenAgent(AgentKinds.Claude, tab.Agent.Options.Folder, account, tab.Agent.Options.Model, tab.Agent.Effort);
            });
    }

    /// <summary>El modelo y el esfuerzo de la pestaña, en botones; valen desde el turno siguiente.</summary>
    void ModelMenu(Control anchor, Tab tab, Action changed)
    {
        var agent = tab.Agent;
        var driver = agent.Driver;
        if (driver.Models.Count == 0 && driver.Efforts.Count == 0) return;
        var list = new StackPanel { Spacing = 1 };
        var paints = new List<Action>();
        string? model = agent.Transcript.Model?.Replace("claude-", "") is { } shown && driver.Models.Contains(shown) ? shown : agent.Options.Model;
        if (driver.Models.Count > 0)
        {
            list.Children.Add(MenuLabel(L.T("Modelo")));
            var row = MenuChips();
            foreach (var m in driver.Models.Prepend(""))
            {
                var value = m.Length == 0 ? null : m;
                var chip = ChoiceChip(value ?? L.T("por defecto"), () =>
                {
                    model = value;
                    agent.SetModel(value);
                    foreach (var paint in paints) paint();
                    changed();
                });
                paints.Add(() => PaintChip(chip, model == value));
                row.Children.Add(chip);
            }
            list.Children.Add(row);
        }
        if (driver.Efforts.Count > 0)
        {
            list.Children.Add(MenuLabel(L.T("Esfuerzo")));
            var row = MenuChips();
            foreach (var e in driver.Efforts)
            {
                var chip = ChoiceChip(EffortLabel(e), () =>
                {
                    agent.SetEffort(e);
                    foreach (var paint in paints) paint();
                    changed();
                });
                paints.Add(() => PaintChip(chip, agent.Effort == e));
                row.Children.Add(chip);
            }
            list.Children.Add(row);
        }
        foreach (var paint in paints) paint();
        Floating(anchor, list, 300);
    }

    /// <summary>Una tarjeta flotante bajo anchor (en la capa de arriba de la ventana); se cierra al tocar afuera o con Esc.</summary>
    void Floating(Control anchor, Control content, double width)
    {
        if (TopLevel.GetTopLevel(anchor)?.Content is not Panel host) return;
        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.TopEdgeAlignedRight,
            VerticalOffset = -6,
            IsLightDismissEnabled = true,
            ShouldUseOverlayLayer = true,
        };
        var card = new Border
        {
            Child = content,
            Background = _ink.Raised,
            BorderBrush = _ink.Hairline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(5),
            Width = width,
            BoxShadow = BoxShadows.Parse(_ink.Dark ? "0 14 34 0 #40000000" : "0 14 34 0 #24000000"),
            Focusable = true,
        };
        card.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            popup.IsOpen = false;
        };
        popup.Child = card;
        popup.Closed += (_, _) => host.Children.Remove(popup);
        host.Children.Add(popup);
        popup.IsOpen = true;
        Dispatcher.UIThread.Post(() => card.Focus(), DispatcherPriority.Loaded);
    }

    // ── Bloques ─────────────────────────────────────────────────────────────────────────────

    Border View(AgentBlock block, bool appear)
    {
        var content = block.Kind switch
        {
            AgentBlockKind.Ask => AskView(block),
            AgentBlockKind.Say => SayView(block),
            AgentBlockKind.Think => ThinkView(block),
            AgentBlockKind.Step => StepView(block),
            AgentBlockKind.Todo => TodoView(block),
            AgentBlockKind.Permission => block.Questions is null ? PermissionView(block) : QuestionView(block),
            AgentBlockKind.Done => DoneView(block),
            _ => NoticeView(block),
        };
        // Una línea fina separa lo pedido, la respuesta que sigue y cada permiso (como las filas de la terminal).
        var rule = RuleBefore(block);
        var view = new Border
        {
            Child = content,
            BorderBrush = _ink.Hairline,
            BorderThickness = new Thickness(0, rule ? 1 : 0, 0, 0),
            Padding = new Thickness(0, rule ? 8 : 0, 0, 0),
            Margin = new Thickness(0, rule ? 4 : 0, 0, 0),
        };
        _views[block] = view;
        if (appear && Animate) Appear(view);
        return view;
    }

    /// <summary>Si el bloque empieza una fila nueva: lo pedido, lo primero después de lo pedido y los permisos.</summary>
    bool RuleBefore(AgentBlock block)
    {
        if (_current?.Shell is not null || _current?.Agent.Transcript.Blocks is not { } blocks) return false;
        var index = IndexOf(blocks, block);
        if (index <= 0) return false;
        var previous = blocks[index - 1];
        return block.Kind is AgentBlockKind.Ask or AgentBlockKind.Permission
            || previous.Kind is AgentBlockKind.Ask or AgentBlockKind.Permission;
    }

    /// <summary>Lo que se escribió: «›» en acento y el pedido en negrita; Copiar al pasar el mouse.</summary>
    Control AskView(AgentBlock block)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*,Auto") };
        var sign = Ui.Text("›", 16, _ink.Accent, FontWeight.Bold);
        sign.VerticalAlignment = VerticalAlignment.Top;
        sign.Margin = new Thickness(2, -2, 0, 0);
        row.Children.Add(sign);
        var text = Selectable(block.Text, Ink.Sans, 13.5, _ink.Text);
        text.FontWeight = FontWeight.SemiBold;
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var copy = Tool(CopyIcon, L.T("Copiar"), () => CopyToClipboard(block.Text));
        copy.Opacity = 0;
        copy.VerticalAlignment = VerticalAlignment.Top;
        copy.Margin = new Thickness(6, -4, 0, -4);
        Grid.SetColumn(copy, 2);
        row.Children.Add(copy);
        ShowOnHover(row, copy);
        return row;
    }

    /// <summary>La respuesta: texto (con su código en bloques) a la altura de lo pedido; Copiar al pasar el mouse.</summary>
    Control SayView(AgentBlock block)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*,Auto") };
        var column = new StackPanel { Spacing = 8, Margin = new Thickness(0, 2, 0, 4), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        Grid.SetColumn(column, 1);
        row.Children.Add(column);
        var copy = Tool(CopyIcon, L.T("Copiar la respuesta"), () => CopyToClipboard(block.Text));
        copy.Opacity = 0;
        copy.VerticalAlignment = VerticalAlignment.Top;
        copy.Margin = new Thickness(6, -2, 0, 0);
        Grid.SetColumn(copy, 2);
        row.Children.Add(copy);
        ShowOnHover(row, copy);
        if (block.Open)
        {
            // Mientras llega, texto plano (barato de cambiar); al terminar, con el código en su bloque.
            var live = Selectable(block.Text, Ink.Sans, 13.5, _ink.Text);
            _live[block] = live;
            column.Children.Add(live);
            return row;
        }
        _live.Remove(block);
        foreach (var segment in Conversations.Split(block.Text))
            column.Children.Add(segment.Code ? Code(segment.Text) : Paragraph(segment.Text));
        return row;
    }

    /// <summary>Pensando: tres puntos que suben y bajan y la palabra que late; al terminar, cuánto pensó.</summary>
    Control ThinkView(AgentBlock block)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(24, 2, 0, 2), Height = 24 };
        if (!block.Open)
        {
            var done = Ui.Text(block.Tokens > 0 ? L.T("Pensó · {0:N0} tokens", block.Tokens) : L.T("Pensó"), 12, _ink.Tertiary);
            done.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(done);
            return row;
        }
        row.Children.Add(Bubbles());
        var word = Ui.Text(L.T("Pensando"), 12.5, _ink.Secondary);
        word.VerticalAlignment = VerticalAlignment.Center;
        Pulse(word);
        row.Children.Add(word);
        if (block.Tokens > 0)
        {
            var tokens = Ui.Text(L.T("{0:N0} tokens", block.Tokens), 11.5, _ink.Tertiary);
            tokens.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(tokens);
        }
        return row;
    }

    /// <summary>Tres puntos de acento que suben y bajan, uno detrás del otro.</summary>
    Control Bubbles()
    {
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < 3; i++)
        {
            var dot = new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), Background = _ink.Accent, VerticalAlignment = VerticalAlignment.Center };
            if (Animate)
                Loop(dot, new Avalonia.Animation.Animation
                {
                    Duration = TimeSpan.FromMilliseconds(1100),
                    Delay = TimeSpan.FromMilliseconds(150 * i),
                    IterationCount = Avalonia.Animation.IterationCount.Infinite,
                    Children =
                    {
                        new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0), Setters = { new Avalonia.Styling.Setter(Visual.OpacityProperty, 0.35) } },
                        new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0.4), Setters = { new Avalonia.Styling.Setter(Visual.OpacityProperty, 1d) } },
                        new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0.8), Setters = { new Avalonia.Styling.Setter(Visual.OpacityProperty, 0.35) } },
                        new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(1), Setters = { new Avalonia.Styling.Setter(Visual.OpacityProperty, 0.35) } },
                    },
                });
            dots.Children.Add(dot);
        }
        return dots;
    }

    /// <summary>Un paso: su signo (✓, girando, en pausa, ✕), el verbo, sobre qué y sus números; abre su cambio o lo que devolvió.</summary>
    Control StepView(AgentBlock block)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,Auto,*,Auto"), Height = 26 };
        row.Children.Add(StateIcon(block.State));
        var verbText = AgentSteps.Verb(block.Tool);
        var verb = Ui.Text(verbText.Length > 0 ? char.ToUpper(verbText[0]) + verbText[1..] : verbText, 12.5, _ink.Secondary);
        verb.VerticalAlignment = VerticalAlignment.Center;
        verb.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(verb, 1);
        row.Children.Add(verb);
        var target = Ui.Text(block.Text, 12, _ink.Text, family: Mono);
        target.TextTrimming = TextTrimming.CharacterEllipsis;
        target.VerticalAlignment = VerticalAlignment.Center;
        ApplyLigatures(target);
        ToolTip.SetTip(target, block.Text);
        Grid.SetColumn(target, 2);
        row.Children.Add(target);
        if (block.Added + block.Removed > 0)
        {
            var counts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            if (block.Added > 0) counts.Children.Add(Ui.Text($"+{block.Added}", 11.5, Added, family: Mono));
            if (block.Removed > 0) counts.Children.Add(Ui.Text($"−{block.Removed}", 11.5, _ink.Danger, family: Mono));
            Grid.SetColumn(counts, 3);
            row.Children.Add(counts);
        }

        var stack = new StackPanel();
        if (block.Diff.Count == 0 && string.IsNullOrEmpty(block.Output))
        {
            stack.Children.Add(row);
            return stack;
        }
        // Al abrirse, lo que cambió (o lo que devolvió); cerrado, una sola línea.
        Border? detail = null;
        var open = block.Diff.Count > 0 && block.Diff.Count <= 12;
        var press = Ui.Press(row, () =>
        {
            open = !open;
            if (detail is not null) detail.IsVisible = open;
        }, (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, block.Text);
        press.CornerRadius = new CornerRadius(7);
        press.Margin = new Thickness(-6, 0, 0, 0);
        press.Padding = new Thickness(6, 0, 0, 0);
        detail = Detail(block);
        detail.IsVisible = open;
        stack.Children.Add(press);
        stack.Children.Add(detail);
        return stack;
    }

    /// <summary>El cambio de un paso (las líneas sumadas y quitadas) o lo que devolvió, en letra fija sobre el fondo de código.</summary>
    Border Detail(AgentBlock block)
    {
        var lines = new StackPanel();
        if (block.Diff.Count > 0)
            foreach (var line in block.Diff)
            {
                var sign = line.Length > 0 ? line[0] : ' ';
                var text = Ui.Text(line.Length > 0 ? line : " ", 11.5, sign == '+' ? Added : sign == '-' ? _ink.Danger : _ink.Secondary, family: Mono);
                text.TextTrimming = TextTrimming.CharacterEllipsis;
                text.LineHeight = 11.5 * 1.7;
                ApplyLigatures(text);
                lines.Children.Add(new Border
                {
                    Background = sign == '+' ? Translucent(Added, 0.13) : sign == '-' ? Translucent(_ink.Danger, 0.11) : Brushes.Transparent,
                    Padding = new Thickness(10, 0),
                    Child = text,
                });
            }
        else
        {
            var output = Selectable(block.Output ?? "", Mono, 11.5, _ink.Secondary);
            output.TextWrapping = TextWrapping.NoWrap;
            output.TextTrimming = TextTrimming.CharacterEllipsis;
            lines.Children.Add(new Border { Padding = new Thickness(10, 2), Child = output });
        }
        return new Border
        {
            Margin = new Thickness(20, 2, 0, 4),
            CornerRadius = new CornerRadius(9),
            Background = _ink.Window,
            Padding = new Thickness(0, 6),
            ClipToBounds = true,
            Child = lines,
        };
    }

    /// <summary>Las tareas, en una línea: la que se hace y cuántas van; al tocarla, la lista entera.</summary>
    Control TodoView(AgentBlock block)
    {
        var todos = block.Todos;
        var done = todos.Count(t => t.Status == "completed");
        var doing = todos.FirstOrDefault(t => t.Status == "in_progress") ?? todos.FirstOrDefault(t => t.Status != "completed");
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*,Auto"), Height = 26 };
        var dot = new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = doing is null ? Added : _ink.Accent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
        };
        if (doing is not null && block == _current?.Agent.Transcript.Blocks.LastOrDefault(b => b.Kind == AgentBlockKind.Todo) && _current.Agent.Transcript.Busy) Pulse(dot);
        row.Children.Add(dot);
        var text = Ui.Text(doing?.Text ?? L.T("Todas las tareas listas"), 12, _ink.Secondary);
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var count = Ui.Text(L.T("{0} de {1}", done, todos.Count), 11.5, _ink.Tertiary, family: Mono);
        count.VerticalAlignment = VerticalAlignment.Center;
        count.Margin = new Thickness(8, 0, 4, 0);
        Grid.SetColumn(count, 2);
        row.Children.Add(count);

        var list = new StackPanel { Spacing = 3, Margin = new Thickness(24, 2, 0, 6), IsVisible = _todoOpen.Contains(block) };
        foreach (var item in todos)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var (mark, brush) = item.Status switch
            {
                "completed" => ("☑", Added),
                "in_progress" => ("◐", _ink.Accent),
                _ => ("☐", _ink.Tertiary),
            };
            line.Children.Add(Ui.Text(mark, 12.5, brush, family: Mono));
            var name = Ui.Text(item.Text, 12.5, item.Status == "completed" ? _ink.Tertiary : _ink.Text);
            if (item.Status == "completed") name.TextDecorations = TextDecorations.Strikethrough;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            line.Children.Add(name);
            list.Children.Add(line);
        }
        var press = Ui.Press(row, () =>
        {
            if (!_todoOpen.Remove(block)) _todoOpen.Add(block);
            list.IsVisible = _todoOpen.Contains(block);
        }, (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, L.T("Tareas"));
        press.CornerRadius = new CornerRadius(7);
        press.Margin = new Thickness(-6, 2, 0, 0);
        press.Padding = new Thickness(6, 0, 0, 0);
        ToolTip.SetTip(press, L.T("Ver todas las tareas"));
        var stack = new StackPanel();
        stack.Children.Add(press);
        stack.Children.Add(list);
        return stack;
    }

    /// <summary>
    /// Un permiso: «?» en ámbar, sobre qué y «pide permiso»; lo que va a hacer, y una franja con Permitir, Siempre aquí
    /// y Rechazar. Ya contestado, queda su signo y la respuesta.
    /// </summary>
    Control PermissionView(AgentBlock block)
    {
        var panel = new StackPanel { Spacing = 6 };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*,Auto"), Height = 26 };
        var (sign, signBrush) = block.Answer switch
        {
            null => ("?", _ink.Warn),
            PermissionAnswer.Deny => ("✕", _ink.Danger),
            _ => ("✓", Added),
        };
        var mark = Ui.Text(sign, 13, signBrush, FontWeight.Bold);
        mark.VerticalAlignment = VerticalAlignment.Center;
        mark.Margin = new Thickness(3, 0, 0, 0);
        head.Children.Add(mark);
        var target = Ui.Text(block.Text, 13, _ink.Text, FontWeight.Medium, Mono);
        target.TextTrimming = TextTrimming.CharacterEllipsis;
        target.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(target, block.Text);
        Grid.SetColumn(target, 1);
        head.Children.Add(target);
        var meta = Ui.Text(block.Answer switch
        {
            null => L.T("pide permiso"),
            PermissionAnswer.Deny => block.FromIsland ? L.T("rechazado en la isla") : L.T("rechazado"),
            PermissionAnswer.AllowAlways => L.T("permitido siempre aquí"),
            _ => block.FromIsland ? L.T("permitido en la isla") : L.T("permitido"),
        }, 11.5, block.Answer is null ? _ink.Warn : block.Answer == PermissionAnswer.Deny ? _ink.Danger : _ink.Tertiary);
        meta.VerticalAlignment = VerticalAlignment.Center;
        meta.Margin = new Thickness(8, 0, 4, 0);
        Grid.SetColumn(meta, 2);
        head.Children.Add(meta);
        panel.Children.Add(head);

        if (Preview(block) is { Length: > 0 } preview && block.Answer is null)
        {
            var code = Selectable(preview, Mono, 11.5, _ink.Secondary);
            code.TextWrapping = TextWrapping.NoWrap;
            code.TextTrimming = TextTrimming.CharacterEllipsis;
            panel.Children.Add(new Border { Background = _ink.Window, CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 6), Margin = new Thickness(24, 0, 0, 0), Child = code });
        }
        if (block.Answer is null)
        {
            var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), Height = 34 };
            var says = Ui.Text(WantsSentence(block.Tool), 12.5, _ink.Secondary);
            says.VerticalAlignment = VerticalAlignment.Center;
            says.TextTrimming = TextTrimming.CharacterEllipsis;
            bar.Children.Add(says);
            var allow = TextButton(L.T("Permitir"), () => Answer(block, PermissionAnswer.Allow), accent: true);
            Grid.SetColumn(allow, 1);
            bar.Children.Add(allow);
            if (block.Suggestions is System.Text.Json.Nodes.JsonArray { Count: > 0 })
            {
                var always = TextButton(L.T("Siempre aquí"), () => Answer(block, PermissionAnswer.AllowAlways));
                Grid.SetColumn(always, 2);
                bar.Children.Add(always);
            }
            var deny = TextButton(L.T("Rechazar"), () => Answer(block, PermissionAnswer.Deny));
            Grid.SetColumn(deny, 3);
            bar.Children.Add(deny);
            panel.Children.Add(new Border
            {
                Background = _ink.Pill,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 0, 4, 0),
                Margin = new Thickness(24, 0, 0, 2),
                Child = bar,
            });
        }
        return panel;
    }

    /// <summary>Lo que pide el permiso, en una frase: «Quiere ejecutar este comando.», «Quiere editar este archivo.»…</summary>
    static string WantsSentence(string? tool) => tool switch
    {
        "Read" or "NotebookRead" => L.T("Quiere leer este archivo."),
        "Edit" or "MultiEdit" or "NotebookEdit" => L.T("Quiere editar este archivo."),
        "Write" => L.T("Quiere crear este archivo."),
        "Bash" or "PowerShell" => L.T("Quiere ejecutar este comando."),
        "WebFetch" => L.T("Quiere abrir esta página."),
        "Grep" or "Glob" or "WebSearch" => L.T("Quiere buscar."),
        _ => L.T("Quiere usar {0}.", tool ?? "?"),
    };

    /// <summary>Lo que va a hacer, para decidir: el comando entero o el comienzo de lo que escribe.</summary>
    static string? Preview(AgentBlock block)
    {
        if (block.Input is not System.Text.Json.Nodes.JsonObject input) return null;
        var text = (string?)(input["command"] ?? input["content"] ?? input["new_string"]);
        if (string.IsNullOrEmpty(text)) return null;
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        return string.Join("\n", lines.Take(6)) + (lines.Length > 6 ? "\n…" : "");
    }

    void Answer(AgentBlock block, PermissionAnswer answer) => _current?.Agent.Answer(block, answer);

    /// <summary>
    /// Las preguntas del agente (AskUserQuestion): las opciones en botones redondos con su explicación y «Otra respuesta»
    /// para escribirla. Con una sola pregunta de una opción, el clic ya responde (como en la isla); si no, «Responder».
    /// </summary>
    Control QuestionView(AgentBlock block)
    {
        var questions = block.Questions!;
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(24, 2, 0, 4) };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        head.Children.Add(Ui.Text(block.Answer is null ? "?" : block.Answer == PermissionAnswer.Deny ? "✕" : "✓", 13,
            block.Answer is null ? _ink.Warn : block.Answer == PermissionAnswer.Deny ? _ink.Danger : Added, FontWeight.Bold));
        head.Children.Add(Ui.Text(block.Answer is null ? L.T("Pregunta") : L.T("Respondido"), 11.5, block.Answer is null ? _ink.Warn : _ink.Tertiary));
        head.Margin = new Thickness(-21, 0, 0, 0);
        panel.Children.Add(head);
        if (block.Answer is { } answer)
        {
            foreach (var q in questions)
            {
                var row = new StackPanel { Spacing = 2 };
                row.Children.Add(Wrapped(q.Question, 12.5, _ink.Secondary));
                var chosen = block.Picks?.TryGetValue(q.Question, out var list) == true ? string.Join(", ", list) : "";
                if (answer != PermissionAnswer.Deny && chosen.Length > 0) row.Children.Add(Wrapped("→ " + chosen, 13, _ink.Text, FontWeight.SemiBold));
                panel.Children.Add(row);
            }
            if (answer == PermissionAnswer.Deny)
                panel.Children.Add(Ui.Text(block.FromIsland ? L.T("Rechazado en la isla") : L.T("No quiso responder"), 12, _ink.Danger));
            else if (block.FromIsland) panel.Children.Add(Ui.Text(L.T("Respondido en la isla"), 12, _ink.Tertiary));
            return panel;
        }

        var picks = questions.ToDictionary(q => q.Question, _ => new List<string>());
        var instant = questions.Count == 1 && !questions[0].MultiSelect;
        var refreshers = new List<Action>();
        Border? send = null;
        void Refresh()
        {
            foreach (var refresh in refreshers) refresh();
            if (send is not null) send.Opacity = picks.Values.All(p => p.Count > 0) ? 1 : 0.45;
        }
        void Submit()
        {
            if (picks.Values.Any(p => p.Count == 0)) return;
            _current?.Agent.Answer(block, picks.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value.ToList()));
        }

        for (var i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            var chosen = picks[q.Question];
            var title = questions.Count > 1 ? L.T("Pregunta {0} de {1}", i + 1, questions.Count) : "";
            if (q.MultiSelect) title += (title.Length > 0 ? " · " : "") + L.T("puede elegir varias");
            if (title.Length > 0) panel.Children.Add(Ui.Text(title, 11.5, _ink.Tertiary));
            panel.Children.Add(Wrapped(q.Question, 13, _ink.Text, FontWeight.SemiBold));

            var options = new WrapPanel { Orientation = Orientation.Horizontal };
            var other = new TextBox { MinWidth = 220, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left };
            Plain(other);
            other.FontFamily = Ink.Sans;
            other.FontSize = 12.5;
            other.Watermark = L.T("Otra respuesta…");
            Ui.AutomationName(other, L.T("Otra respuesta…"));
            foreach (var (label, description) in q.Options)
            {
                var content = new StackPanel { Spacing = 2 };
                content.Children.Add(Ui.Text(label, 12.5, _ink.Text, FontWeight.Medium));
                if (description.Length > 0) content.Children.Add(Wrapped(description, 11.5, _ink.Tertiary));
                var option = Ui.Press(content, () =>
                {
                    if (q.MultiSelect)
                    {
                        if (!chosen.Remove(label)) chosen.Add(label);
                    }
                    else
                    {
                        other.Text = "";
                        chosen.Clear();
                        chosen.Add(label);
                    }
                    Refresh();
                    if (instant) Submit();
                }, null, label);
                option.CornerRadius = new CornerRadius(10);
                option.Padding = new Thickness(12, 7);
                option.Margin = new Thickness(0, 0, 6, 6);
                option.MaxWidth = 260;
                option.BorderThickness = new Thickness(1);
                refreshers.Add(() =>
                {
                    var selected = chosen.Contains(label);
                    option.BorderBrush = selected ? Brushes.Transparent : _ink.Hairline;
                    option.Background = selected ? Translucent(_ink.Accent, 0.16) : Brushes.Transparent;
                });
                options.Children.Add(option);
            }
            panel.Children.Add(options);

            // Lo escrito es la respuesta (en una de varias, se suma a lo elegido).
            string? typed = null;
            other.TextChanged += (_, _) =>
            {
                if (typed is not null) chosen.Remove(typed);
                typed = other.Text?.Trim() is { Length: > 0 } t ? t : null;
                if (typed is not null)
                {
                    if (!q.MultiSelect) chosen.Clear();
                    chosen.Add(typed);
                }
                Refresh();
            };
            other.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                Submit();
            };
            panel.Children.Add(new Border
            {
                Background = _ink.Pill,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 5),
                Margin = new Thickness(0, -4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = other,
            });
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        send = TextButton(L.T("Responder"), Submit, accent: true);
        buttons.Children.Add(send);
        buttons.Children.Add(TextButton(L.T("No responder"), () => Answer(block, PermissionAnswer.Deny)));
        panel.Children.Add(buttons);
        Refresh();
        return panel;
    }

    TextBlock Wrapped(string text, double size, IBrush brush, FontWeight weight = FontWeight.Normal)
    {
        var block = Ui.Text(text, size, brush, weight);
        block.TextWrapping = TextWrapping.Wrap;
        return block;
    }

    Control DoneView(AgentBlock block)
    {
        var parts = new List<string> { Duration(block.Duration) };
        if (block.Turns > 0) parts.Add(block.Turns == 1 ? L.T("1 turno") : L.T("{0} turnos", block.Turns));
        if (block.Cost > 0) parts.Add(L.T("US$ {0:0.0000}", block.Cost));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(24, 2, 0, 6) };
        row.Children.Add(Icon(block.Failed ? CrossIcon : CheckIcon, block.Failed ? _ink.Danger : Added, 12, 2.2));
        var text = Ui.Text(block.Failed && block.Text.Length > 0 ? block.Text : string.Join(" · ", parts), 11.5, block.Failed ? _ink.Danger : _ink.Tertiary);
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        row.Children.Add(text);
        return row;
    }

    static string Duration(TimeSpan d) => d.TotalSeconds < 60 ? L.T("{0:0.0} s", d.TotalSeconds) : L.T("{0} min {1} s", (int)d.TotalMinutes, d.Seconds);

    Control NoticeView(AgentBlock block)
    {
        var text = Selectable(block.Text, Ink.Sans, 12.5, _ink.Tertiary);
        text.Margin = new Thickness(24, 6, 0, 6);
        return text;
    }

    Control StateIcon(StepState state)
    {
        Control icon = state switch
        {
            StepState.Running => Spinner(),
            StepState.Waiting => Icon(PauseIcon, _ink.Warn, 12, 2.2),
            StepState.Done => Icon(CheckIcon, Added, 12, 2.2),
            StepState.Failed => Icon(CrossIcon, _ink.Danger, 12, 2.2),
            _ => Icon(BlockedIcon, _ink.Danger, 12, 1.8),
        };
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Margin = new Thickness(2, 0, 0, 0);
        return icon;
    }

    IBrush Added => _ink.Dark ? new SolidColorBrush(Color.FromRgb(0xA8, 0xC7, 0xA0)) : new SolidColorBrush(Color.FromRgb(0x3F, 0x6B, 0x3A));

    SelectableTextBlock Paragraph(string text)
    {
        var inlines = new InlineCollection();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) inlines.Add(new LineBreak());
            foreach (var span in Conversations.Spans(lines[i]))
                inlines.Add(span.Code ? new Run(span.Text) { FontFamily = Mono, FontSize = 12.5, Background = _ink.Pill } : new Run(span.Text));
        }
        var block = Selectable(null, Ink.Sans, 13.5, _ink.Text);
        block.Inlines = inlines;
        return block;
    }

    Control Code(string code)
    {
        var text = Selectable(code.TrimEnd('\n'), Mono, 12.5, _ink.Text);
        text.TextWrapping = TextWrapping.NoWrap;
        return new Border
        {
            Background = _ink.Window,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 8),
            Child = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = text },
        };
    }

    /// <summary>Texto que se puede seleccionar y copiar: con el mouse y Ctrl+C, o con el clic derecho (Copiar, Seleccionar todo).</summary>
    SelectableTextBlock Selectable(string? text, FontFamily family, double size, IBrush brush)
    {
        var block = new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = family,
            FontSize = size,
            LineHeight = size * _look.LineSpacing,
            Foreground = brush,
            SelectionBrush = Translucent(_ink.Accent, 0.35),
            Focusable = true,
        };
        if (family == Mono) ApplyLigatures(block);
        // Con el foco en el texto (y no en la caja de abajo), Ctrl+C copia lo seleccionado.
        block.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(block).Properties.IsLeftButtonPressed) block.Focus();
        }, RoutingStrategies.Tunnel);
        var copy = new MenuItem { Header = L.T("Copiar") };
        copy.Click += (_, _) => CopySelection(block);
        var all = new MenuItem { Header = L.T("Seleccionar todo") };
        all.Click += (_, _) =>
        {
            block.Focus();
            block.SelectAll();
        };
        block.ContextMenu = new ContextMenu { Items = { copy, all } };
        return block;
    }

    /// <summary>Copia lo seleccionado; sin selección, el texto entero.</summary>
    void CopySelection(SelectableTextBlock block)
    {
        var selected = block.SelectedText;
        CopyToClipboard(string.IsNullOrEmpty(selected) ? block.Inlines?.Text ?? block.Text ?? "" : selected);
    }

    void CopyToClipboard(string text)
    {
        if (text.Length == 0) return;
        _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text);
    }

    /// <summary>Lo que aparece solo al pasar el mouse (Copiar): ya ocupa su lugar, así nada se mueve.</summary>
    static void ShowOnHover(Panel area, Control shown)
    {
        area.Background ??= Brushes.Transparent;
        area.PointerEntered += (_, _) => shown.Opacity = 1;
        area.PointerExited += (_, _) => shown.Opacity = 0;
    }
}
