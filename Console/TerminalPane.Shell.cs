namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

/// <summary>
/// Las terminales de comandos de Claupet Terminal: PowerShell o bash en una consola de mentira (ConPTY), con cada
/// comando en su bloque (el comando, su salida, el código de salida y lo que tardó), como en Warp. La pestaña es la misma
/// que la de Claude Code (Tab) con Shell puesto; aquí está todo lo que es solo del shell.
/// </summary>
sealed partial class TerminalPane
{
    /// <summary>Cuántas líneas de la salida se ven en un bloque antes de «… N líneas más».</summary>
    const int ShellPreview = 30;

    /// <summary>El shell elegido en la sesión nueva; null es Claude Code.</summary>
    ShellChoice? _newShell;
    readonly Dictionary<ShellBlock, Border> _shellViews = [];
    /// <summary>La salida de los comandos que corren: se rellena en su lugar, sin rehacer el bloque (ni su giro).</summary>
    readonly Dictionary<ShellBlock, Action> _shellLive = [];
    /// <summary>Los bloques abiertos con «… N líneas más».</summary>
    readonly HashSet<ShellBlock> _shellOpen = [];
    /// <summary>Si cada shell estaba corriendo un comando, para rehacer la lista solo cuando cambia.</summary>
    readonly Dictionary<ShellSession, (bool Busy, bool Running)> _shellState = [];
    Border _shellInterrupt = new();
    TextBlock _shellStatus = new();
    /// <summary>Al recorrer los comandos anteriores con ↑ y ↓: cuál se muestra (-1, ninguno).</summary>
    int _history = -1;

    // ── La pestaña ──────────────────────────────────────────────────────────────────────────

    /// <summary>La marca de la pestaña: la inicial del agente («C» Claude Code, «X» Codex, «G» Gemini, «P» Copilot) o «>_» para un shell.</summary>
    static string KindMark(Tab tab) => tab.Shell is not null ? ">_" : tab.Agent.Options.Kind switch
    {
        AgentKinds.Codex => "X",
        AgentKinds.Gemini => "G",
        AgentKinds.Copilot => "P",
        _ => "C",
    };

    /// <summary>Trabajando: Claude Code en un turno o el shell con un comando corriendo.</summary>
    static bool Busy(Tab tab) => tab.Shell is { } shell ? shell.Transcript.Busy : tab.Agent.Transcript.Busy;

    /// <summary>El punto de estado de la pestaña: esperando un permiso, trabajando, abierta o cerrada.</summary>
    IBrush Dot(Tab tab)
    {
        if (tab.Shell is { } shell) return shell.Transcript.Busy ? _ink.Accent : shell.Running ? _ink.Track : _ink.Hairline;
        var t = tab.Agent.Transcript;
        return Waiting(t) ? _ink.Warn : t.Busy ? _ink.Accent : tab.Agent.Running ? _ink.Track : _ink.Hairline;
    }

    /// <summary>El título de un shell: su último comando o, sin comandos, su nombre.</summary>
    static string ShellTitle(ShellSession shell) =>
        shell.Transcript.Blocks.LastOrDefault(b => b.Kind == ShellBlockKind.Command && b.Command.Length > 0)?.Command.ReplaceLineEndings(" ")
            ?? shell.Shell.Label;

    static string ShellDescribe(ShellSession shell) =>
        shell.Transcript.Cwd is { } cwd ? $"{shell.Shell.Label} · {ShortPath(cwd)}" : shell.Shell.Label;

    // ── La sesión nueva ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// «Tipo» en la sesión nueva: Claude Code, otro agente instalado (Codex, Gemini CLI, Copilot CLI) o uno de los shells
    /// del equipo. Con otra cosa que Claude Code se ocultan sus campos (desde agentFields) y cambia la explicación de arriba.
    /// </summary>
    void KindField(StackPanel panel, TextBlock about, int agentFields)
    {
        var shells = Shells.Available();
        var agents = AgentKinds.Available().Where(k => k != AgentKinds.Claude).ToList();
        if (shells.Count == 0 && agents.Count == 0) return;
        if (_newShell is { } chosen && !shells.Contains(chosen)) _newShell = null;
        if (!agents.Contains(_form.Kind)) _form.Kind = AgentKinds.Claude;
        var agentOnly = panel.Children.Skip(agentFields).ToList();
        var claudeAbout = about.Text;
        var value = Ui.Text("", 13, _ink.Text);
        void Show()
        {
            var claude = _newShell is null && _form.Kind == AgentKinds.Claude;
            value.Text = _newShell?.Label ?? AgentKinds.Label(_form.Kind);
            foreach (var field in agentOnly) field.IsVisible = claude;
            about.Text = claude ? claudeAbout
                : _newShell is null ? L.T("{0} en bloques, con sus permisos aquí y en la isla. El modelo, el esfuerzo y los permisos se eligen abajo, en la sesión, con lo que el agente dice tener.", AgentKinds.Label(_form.Kind))
                : PseudoConsole.IsSupported
                ? L.T("Un shell en bloques: cada comando con su salida, su código de salida y lo que tardó. Los programas de pantalla completa (vim, htop) todavía no se pueden usar.")
                : L.T("La terminal de comandos todavía no está disponible en este sistema. Por ahora, solo en Windows 10 (1809) o más nuevo.");
        }
        var field = Field(L.T("Tipo"), value, anchor =>
        {
            // Los agentes van con «agente:» adelante para no confundirlos con el id de un shell.
            var entries = agents.Select(k => new DropList.Entry("agente:" + k, AgentKinds.Label(k), _newShell is null && _form.Kind == k))
                .Prepend(new DropList.Entry("agente:" + AgentKinds.Claude, "Claude Code", _newShell is null && _form.Kind == AgentKinds.Claude))
                .Concat(shells.Select(s => new DropList.Entry(s.Id, s.Label, s == _newShell))).ToList();
            DropList.Show(anchor, _ink, entries, picked =>
            {
                if (picked is null) return;
                if (picked.StartsWith("agente:", StringComparison.Ordinal))
                {
                    _newShell = null;
                    _form.Kind = picked["agente:".Length..];
                }
                else _newShell = shells.FirstOrDefault(s => s.Id == picked);
                Show();
            });
            return Task.CompletedTask;
        });
        panel.Children.Insert(panel.Children.IndexOf(about) + 1, field);
        Show();
    }

    void OpenShell(ShellChoice choice)
    {
        if (!Directory.Exists(_form.Folder)) return;
        var folder = _form.Folder;
        // Se recuerda la carpeta; la cuenta, el modelo y lo demás quedan como estaban para Claude Code.
        var last = TerminalRecents.Load();
        TerminalRecents.Remember(new AgentOptions(folder, last.Account, last.Model, last.Effort, last.Mode));
        LinkProject(folder);
        var shell = new ShellSession(choice, folder, action => Dispatcher.UIThread.Post(action));
        // El agente de la pestaña no se abre nunca: solo lleva la carpeta (la lista agrupa por ella).
        var tab = new Tab(new TerminalAgent(new AgentOptions(folder), _ => { }), choice.Label) { Shell = shell };
        shell.Changed += changed => OnShellChanged(tab, changed);
        _tabs.Add(tab);
        _current = tab;
        _creating = false;
        _input.Text = "";
        Build();
        RenderList();
        TabsChanged?.Invoke();
        shell.Start();
        _input.Focus();
    }

    // ── La sesión ───────────────────────────────────────────────────────────────────────────

    void BuildShell(Tab tab)
    {
        var shell = tab.Shell!;
        _shellViews.Clear();
        _shellLive.Clear();
        _history = -1;
        RowDefinitions = new RowDefinitions("46,*,Auto");
        Children.Add(ShellHeader(tab, shell));
        Grid.SetRow(_scroll, 1);
        Children.Add(_scroll);
        var composer = ShellComposer(shell);
        Grid.SetRow(composer, 2);
        Children.Add(composer);
        foreach (var block in shell.Transcript.Blocks) _blocks.Children.Add(ShellView(block, appear: false));
        if (shell.Transcript.Blocks.Count == 0)
        {
            var hint = Ui.Text(L.T("Escriba un comando. Cada uno queda en su bloque, con su salida y su código de salida."), 13, _ink.Tertiary);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(4, 8);
            _blocks.Children.Add(hint);
        }
        PaintShellStatus(shell);
        Dispatcher.UIThread.Post(_scroll.ScrollToEnd, DispatcherPriority.Background);
    }

    Control ShellHeader(Tab tab, ShellSession shell)
    {
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(20, 0, 10, 0) };
        var where = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        where.Children.Add(Ui.Text(tab.Project, 14, _ink.Text, FontWeight.SemiBold));
        if (tab.Branch is { } branch)
        {
            where.Children.Add(Icon(BranchIcon, _ink.Tertiary, 12, 1.8));
            var name = Ui.Text(branch, 12.5, _ink.Secondary, family: Mono);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.MaxWidth = 220;
            where.Children.Add(name);
        }
        ToolTip.SetTip(where, shell.Folder);
        bar.Children.Add(where);
        _shellStatus = Ui.Text("", 12, _ink.Tertiary, family: Mono);
        _shellStatus.Margin = new Thickness(12, 0, 0, 0);
        _shellStatus.TextTrimming = TextTrimming.PrefixCharacterEllipsis;
        Grid.SetColumn(_shellStatus, 1);
        bar.Children.Add(_shellStatus);
        _shellInterrupt = Pill(L.T("Interrumpir"), _ink.Pill, _ink.Text, shell.Interrupt);
        ToolTip.SetTip(_shellInterrupt, "Ctrl+C");
        Grid.SetColumn(_shellInterrupt, 2);
        bar.Children.Add(_shellInterrupt);
        var close = Round(CloseIcon, L.T("Cerrar la sesión"), () => CloseTab(tab));
        close.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(close, 3);
        bar.Children.Add(close);
        return bar;
    }

    /// <summary>La franja de arriba: el shell y su carpeta (o que está abriendo); «Interrumpir» mientras corre un comando.</summary>
    void PaintShellStatus(ShellSession shell)
    {
        var t = shell.Transcript;
        _shellStatus.Text = !shell.Running ? shell.Shell.Label
            : !t.Ready ? L.T("Abriendo {0}…", shell.Shell.Label)
            : $"{shell.Shell.Label} · {t.Cwd ?? shell.Folder}";
        ToolTip.SetTip(_shellStatus, _shellStatus.Text);
        _shellInterrupt.IsVisible = t.Busy && shell.Running;
    }

    Control ShellComposer(ShellSession shell)
    {
        _input.AcceptsReturn = true;
        _input.TextWrapping = TextWrapping.Wrap;
        _input.MinHeight = 22;
        _input.MaxHeight = 180;
        Plain(_input);
        _input.FontSize = _look.Size;
        _input.Watermark = shell.Transcript.Busy ? L.T("Lo que escriba le llega al comando que corre") : L.T("Escriba un comando");
        Ui.AutomationName(_input, L.T("Comando para {0}", shell.Shell.Label));

        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var prompt = Ui.Text("❯", 14, _ink.Accent, FontWeight.Bold, Mono);
        prompt.VerticalAlignment = VerticalAlignment.Top;
        prompt.Margin = new Thickness(0, 1, 10, 0);
        line.Children.Add(prompt);
        Grid.SetColumn(_input, 1);
        line.Children.Add(_input);
        var send = Ui.Press(Icon(SendIcon, Ink.On(_ink.Accent), 15, 2.2), () => SubmitShell(shell), null, L.T("Ejecutar"));
        send.Width = 30;
        send.Height = 30;
        send.CornerRadius = new CornerRadius(999);
        send.Background = _ink.Accent;
        send.VerticalAlignment = VerticalAlignment.Bottom;
        send.Opacity = shell.Running ? 1 : 0.45;
        ToolTip.SetTip(send, L.T("Ejecutar"));
        Grid.SetColumn(send, 2);
        line.Children.Add(send);

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(22, 8, 0, 0) };
        chips.Children.Add(Chip(shell.Shell.Label));
        var hint = Ui.Text(L.T("Intro ejecuta · ↑ los anteriores · Ctrl+C o Esc interrumpe"), 11.5, _ink.Tertiary);
        hint.Margin = new Thickness(6, 0, 0, 0);
        chips.Children.Add(hint);

        var card = new StackPanel();
        card.Children.Add(line);
        card.Children.Add(chips);
        return new Border
        {
            Margin = new Thickness(16, 4, 16, 14),
            CornerRadius = new CornerRadius(14),
            Background = _ink.Raised,
            BorderBrush = _ink.Hairline,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 8, 8),
            Child = card,
        };
    }

    /// <summary>Las teclas de la caja en un shell: Intro ejecuta, Esc o Ctrl+C (sin selección) interrumpen, ↑ y ↓ recorren los comandos.</summary>
    bool ShellKey(ShellSession shell, KeyEventArgs e)
    {
        var selection = _input.SelectionEnd != _input.SelectionStart;
        if (e.Key == Key.Escape || (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control && !selection))
        {
            if (!shell.Transcript.Busy && e.Key == Key.Escape) return false;
            shell.Interrupt();
            return true;
        }
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            SubmitShell(shell);
            return true;
        }
        if (e.Key is Key.Up or Key.Down && e.KeyModifiers == KeyModifiers.None && !(_input.Text ?? "").Contains('\n'))
        {
            var commands = shell.Transcript.Blocks.Where(b => b.Kind == ShellBlockKind.Command && b.Command.Length > 0)
                .Select(b => b.Command).Reverse().Distinct().ToList();
            if (commands.Count == 0) return false;
            _history = Math.Clamp(_history + (e.Key == Key.Up ? 1 : -1), -1, commands.Count - 1);
            _input.Text = _history < 0 ? "" : commands[_history];
            _input.CaretIndex = _input.Text.Length;
            return true;
        }
        return false;
    }

    void SubmitShell(ShellSession shell)
    {
        if (!shell.Running) return;
        var text = _input.Text ?? "";
        // Con un comando corriendo, un Intro vacío también sirve (un «presione Intro para seguir»).
        if (text.Trim().Length == 0 && !shell.Transcript.Busy) return;
        _input.Text = "";
        _history = -1;
        shell.Send(text.TrimEnd('\r', '\n'));
        Dispatcher.UIThread.Post(_scroll.ScrollToEnd, DispatcherPriority.Background);
    }

    /// <summary>Llegó salida de un shell: se cambian solo sus bloques; la lista, si empezó o terminó un comando.</summary>
    void OnShellChanged(Tab tab, IReadOnlyList<ShellBlock> changed)
    {
        var shell = tab.Shell!;
        var state = (shell.Transcript.Busy, shell.Running);
        var listChanged = !_shellState.TryGetValue(shell, out var before) || before != state
            || changed.Any(b => b.Kind == ShellBlockKind.Command && !b.Running);
        _shellState[shell] = state;
        if (tab != _current || _creating || _settings)
        {
            if (listChanged) RenderList();
            return;
        }
        var atEnd = _scroll.Offset.Y >= _scroll.Extent.Height - _scroll.Viewport.Height - 40;
        if (changed.Count > 0 && _shellViews.Count == 0) _blocks.Children.Clear();
        foreach (var block in changed)
        {
            if (block.Running && _shellLive.TryGetValue(block, out var fill))
            {
                fill();
                continue;
            }
            _shellLive.Remove(block);
            var old = _shellViews.GetValueOrDefault(block);
            var view = ShellView(block, appear: old is null);
            if (old is not null && _blocks.Children.IndexOf(old) is var index and >= 0) _blocks.Children[index] = view;
            else if (block.Kind == ShellBlockKind.Output && _blocks.Children.Count > 0) _blocks.Children.Insert(0, view);
            else _blocks.Children.Add(view);
        }
        PaintShellStatus(shell);
        if (before.Busy != state.Busy)
            _input.Watermark = shell.Transcript.Busy ? L.T("Lo que escriba le llega al comando que corre") : L.T("Escriba un comando");
        if (listChanged) RenderList();
        if (atEnd) Dispatcher.UIThread.Post(_scroll.ScrollToEnd, DispatcherPriority.Background);
    }

    // ── Bloques ─────────────────────────────────────────────────────────────────────────────

    Border ShellView(ShellBlock block, bool appear)
    {
        var content = block.Kind switch
        {
            ShellBlockKind.Command => CommandView(block),
            ShellBlockKind.Output => OutputView(block, _ink.Tertiary),
            _ => ShellNotice(block),
        };
        var view = new Border { Child = content };
        _shellViews[block] = view;
        if (appear && Animate) Appear(view);
        return view;
    }

    /// <summary>Un comando: dónde corrió, «❯ comando», cómo terminó y su salida; al pasar el cursor, Copiar y Repetir.</summary>
    Control CommandView(ShellBlock block)
    {
        var panel = new StackPanel { Spacing = 6 };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        var prompt = Ui.Text("❯", 13.5, block.Failed ? _ink.Danger : _ink.Accent, FontWeight.Bold, Mono);
        prompt.VerticalAlignment = VerticalAlignment.Top;
        prompt.Margin = new Thickness(0, 0, 10, 0);
        head.Children.Add(prompt);
        var command = Selectable(block.Command, Mono, 13.5, _ink.Text);
        Grid.SetColumn(command, 1);
        head.Children.Add(command);

        // Copiar y Repetir aparecen al pasar el cursor (sin mover nada: ya ocupan su lugar).
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 0, 0, 0), Opacity = 0, VerticalAlignment = VerticalAlignment.Top };
        actions.Children.Add(Pill(L.T("Copiar"), _ink.Raised, _ink.Secondary, () => Copy(block)));
        if (!block.Running) actions.Children.Add(Pill(L.T("Repetir"), _ink.Raised, _ink.Secondary, () => Repeat(block)));
        Grid.SetColumn(actions, 2);
        head.Children.Add(actions);

        var state = ExitBadge(block);
        state.Margin = new Thickness(8, 0, 0, 0);
        state.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(state, 3);
        head.Children.Add(state);

        if (block.Cwd is { } cwd)
        {
            var where = Ui.Text(ShortPath(cwd), 11.5, _ink.Tertiary, family: Mono);
            where.TextTrimming = TextTrimming.PrefixCharacterEllipsis;
            ToolTip.SetTip(where, cwd);
            panel.Children.Add(where);
        }
        panel.Children.Add(head);
        var output = OutputView(block, _ink.Secondary);
        panel.Children.Add(output);
        if (block.Running) _shellLive[block] = () => FillOutput(output, block, _ink.Secondary);

        var card = new Border
        {
            Background = _ink.Pill,
            CornerRadius = new CornerRadius(10),
            BorderBrush = block.Failed ? Translucent(_ink.Danger, 0.45) : Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 4, 0, 2),
            Child = panel,
        };
        card.PointerEntered += (_, _) => actions.Opacity = 1;
        card.PointerExited += (_, _) => actions.Opacity = 0;
        return card;
    }

    /// <summary>Cómo terminó: el giro mientras corre, ✓ y lo que tardó, o ✕ con su código.</summary>
    Control ExitBadge(ShellBlock block)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (block.Running)
        {
            row.Children.Add(Spinner());
            return row;
        }
        if (block.Duration is { } took)
            row.Children.Add(Ui.Text(Duration(took), 11.5, _ink.Tertiary));
        if (block.ExitCode is not { } code) return row;
        if (code == 0)
        {
            var ok = Icon(CheckIcon, Added, 13, 2.2);
            ToolTip.SetTip(ok, L.T("Terminó bien (código 0)"));
            row.Children.Add(ok);
            return row;
        }
        var failed = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        failed.Children.Add(Icon(CrossIcon, _ink.Danger, 11, 2.4));
        failed.Children.Add(Ui.Text(code.ToString(), 11.5, _ink.Danger, FontWeight.SemiBold, Mono));
        var badge = new Border
        {
            Background = Translucent(_ink.Danger, 0.12),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(7, 1),
            Child = failed,
        };
        ToolTip.SetTip(badge, L.T("Terminó con el código {0}", code));
        row.Children.Add(badge);
        return row;
    }

    /// <summary>La salida en letra fija: las últimas líneas y, si hay más, «… N líneas más» para verlas.</summary>
    StackPanel OutputView(ShellBlock block, IBrush brush)
    {
        var panel = new StackPanel { Spacing = 2, Margin = new Thickness(22, 0, 0, 0) };
        FillOutput(panel, block, brush);
        return panel;
    }

    void FillOutput(StackPanel panel, ShellBlock block, IBrush brush)
    {
        panel.Children.Clear();
        panel.IsVisible = block.Lines.Count > 0;
        if (block.Lines.Count == 0) return;
        var open = _shellOpen.Contains(block);
        var shown = open || block.Lines.Count <= ShellPreview ? block.Lines : block.Lines.Skip(block.Lines.Count - ShellPreview).ToList();
        var more = block.Hidden + block.Lines.Count - shown.Count;
        if (more > 0)
        {
            var label = Ui.Text(L.T("… {0} líneas más", more), 11.5, _ink.Tertiary);
            if (shown.Count < block.Lines.Count)
            {
                var press = Ui.Press(label, () =>
                {
                    _shellOpen.Add(block);
                    FillOutput(panel, block, brush);
                }, (b, on) => label.Foreground = on ? _ink.Secondary : _ink.Tertiary, L.T("Ver más líneas"));
                ToolTip.SetTip(press, block.Hidden > 0 ? L.T("Se guardan las últimas {0} líneas", ShellTranscript.MaxLines) : null);
                press.HorizontalAlignment = HorizontalAlignment.Left;
                panel.Children.Add(press);
            }
            else panel.Children.Add(label);
        }
        panel.Children.Add(Selectable(string.Join("\n", shown), Mono, 12.5, brush));
    }

    Control ShellNotice(ShellBlock block)
    {
        var text = Ui.Text(block.Command, 12.5, _ink.Tertiary);
        text.TextWrapping = TextWrapping.Wrap;
        text.Margin = new Thickness(0, 6);
        return text;
    }


    /// <summary>Copia la salida; si no tuvo, el comando.</summary>
    void Copy(ShellBlock block)
    {
        var text = block.Lines.Count > 0 ? block.Output : block.Command;
        _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text);
    }

    void Repeat(ShellBlock block)
    {
        if (_current?.Shell is not { } shell || !shell.Running || shell.Transcript.Busy) return;
        shell.Send(block.Command);
        Dispatcher.UIThread.Post(_scroll.ScrollToEnd, DispatcherPriority.Background);
    }
}
