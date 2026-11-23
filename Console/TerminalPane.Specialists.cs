namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

/// <summary>
/// Los especialistas desde la Terminal, sin salir de la sesión:
/// - En la caja se elige el especialista de la pestaña: desde el próximo pedido, la misma sesión trabaja como él (sus
///   instrucciones van una vez, en una nota que la persona no ve; con Claude Code, también su modelo y su esfuerzo).
/// - Al escribir «@» aparece la lista de especialistas (flechas, Intro o Tab para elegir, Esc para cerrar). Un pedido con
///   @especialista va con una nota para el agente de la pestaña: en Claude Code, que se lo pase a su subagente (los
///   especialistas van como subagentes al abrir, --agents) y el trabajo queda en la sesión; en los demás agentes, o si es
///   un especialista que la sesión no recibió, que se lo delegue con pedir_a_especialista (aparte). «Abrir aparte», en
///   la lista, lo abre en una pestaña propia.
/// Arriba de la caja se ven las corridas aparte de los especialistas que llamó esa pestaña (las lee del diario, aunque
/// corran en otro proceso): cómo van, su último paso, «Ver su sesión» y «Detener».
/// </summary>
sealed partial class TerminalPane
{
    // ── El especialista de la pestaña ───────────────────────────────────────────────────────

    /// <summary>Lo que dice su etiqueta en la caja: el especialista de la pestaña o «especialista».</summary>
    static string RoleText(Tab tab) => tab.Agent.Options.Specialist?.Name ?? tab.Role?.Name ?? L.T("especialista");

    /// <summary>La etiqueta de la caja: su color si hay uno; tocarla abre la lista (la de una pestaña abierta como especialista es fija).</summary>
    Border RoleChip(Tab tab)
    {
        var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
        void Paint()
        {
            var role = tab.Agent.Options.Specialist ?? tab.Role;
            dot.Background = role is null ? _ink.Tertiary : Brush(role.Color);
            dot.Opacity = role is null ? 0.5 : 1;
        }
        Border? chip = null;
        TextBlock? text = null;
        chip = Pick(RoleText(tab), tab.Agent.Options.Specialist is not null ? null : () => RoleMenu(chip!, tab, () =>
        {
            text!.Text = RoleText(tab);
            Paint();
        }), dot);
        text = chip.Child is StackPanel { Children: [.., TextBlock label] } ? label : null;
        Paint();
        ToolTip.SetTip(chip, tab.Agent.Options.Specialist is { } own
            ? L.T("Esta pestaña se abrió como el especialista «{0}».", own.Name)
            : L.T("El especialista con que trabaja esta sesión: desde el próximo pedido sigue sus instrucciones, sin abrir otra. Usa las herramientas de esta pestaña."));
        return chip;
    }

    /// <summary>La lista de especialistas para la pestaña, con «Sin especialista» arriba.</summary>
    void RoleMenu(Control anchor, Tab tab, Action changed)
    {
        const string None = "\u0001none";
        var all = Specialists.All();
        var entries = new List<DropList.Entry> { new(None, L.T("Sin especialista"), tab.Role is null) };
        entries.AddRange(all.Select(s => new DropList.Entry(s.Id, s.Name, tab.Role?.Id == s.Id)));
        DropList.Show(anchor, _ink, entries, picked =>
        {
            if (picked is null) return;
            ChooseRole(tab, picked == None ? null : all.FirstOrDefault(s => s.Id == picked));
            changed();
        });
    }

    /// <summary>
    /// Cambia el especialista de la pestaña. Su nota sale con el próximo pedido (si se cambia otra vez antes, solo la última).
    /// Con Claude Code, si el especialista trae modelo o esfuerzo, la sesión pasa a ellos y, al quitarlo, vuelve a los de antes.
    /// </summary>
    internal void ChooseRole(Tab tab, Specialist? role)
    {
        if (tab.Role?.Id == role?.Id) return;
        var before = tab.Role;
        tab.Role = role;
        // Si la nota del anterior no llegó a salir, la sesión nunca supo de él: no hay nada que quitarle.
        var told = before is not null && tab.RoleNote is null;
        tab.RoleNote = SpecialistTools.RoleNote(role, told ? before : null);
        var agent = tab.Agent;
        if (agent.Options.Kind == AgentKinds.Claude)
        {
            if (tab.RoleRestore is { } back && (role is null || (role.Model is null && role.Effort is null)))
            {
                agent.SetModel(back.Model);
                if (back.Effort is { } effort) agent.SetEffort(effort);
                tab.RoleRestore = null;
            }
            if (role is { Agent: AgentKinds.Claude } && (role.Model is not null || role.Effort is not null))
            {
                tab.RoleRestore ??= (ClaudeModels.ChoiceFor(agent.Transcript.Model, agent.Driver.Models, agent.Options.Model, agent.Transcript.Offered ?? ClaudeModels.Known), agent.Effort);
                if (role.Model is { } model) agent.SetModel(model);
                if (role.Effort is { } effort) agent.SetEffort(effort);
            }
            if (tab == _current && _modelText is { } label) label.Text = ModelAndEffort(tab);
        }
        agent.Transcript.Notice(role is null
            ? L.T("Sin especialista: desde el próximo pedido trabaja como siempre.")
            : L.T("Especialista «{0}»: desde el próximo pedido esta sesión sigue sus instrucciones.", role.Name));
        SaveOpen();
    }

    /// <summary>Los especialistas que la sesión de la pestaña recibió como subagentes (solo Claude Code).</summary>
    static IReadOnlyList<(string Name, Specialist Specialist)> SubagentsOf(Tab tab) => tab.Agent.Driver is ClaudeDriver claude ? claude.Subagents : [];

    /// <summary>
    /// Las notas de especialistas para un pedido (null si no hay): con el primero, que los especialistas son también sus
    /// subagentes; si cambió el de la pestaña, la de trabajar como él; si menciona a alguno, la de pasarle la tarea.
    /// </summary>
    internal static string? SpecialistNotes(Tab tab, string text)
    {
        var subagents = SubagentsOf(tab);
        var notes = new System.Text.StringBuilder();
        if (!tab.SubagentsTold && SpecialistTools.SubagentsNote(subagents) is { } told) notes.Append(told);
        tab.SubagentsTold = true;
        if (tab.RoleNote is { } role) notes.Append(role);
        tab.RoleNote = null;
        if (text.Contains('@') && SpecialistTools.Directive(SpecialistTools.Mentions(text, Specialists.All()), subagents) is { } directive) notes.Append(directive);
        return notes.Length == 0 ? null : notes.ToString();
    }

    // ── @ y # en la caja ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lo que ofrece la lista: lo que se escribe al elegirlo («@revisor-de-prs», «#arreglo-login»), su color, de qué es y,
    /// si es un especialista, cuál (para «Abrir aparte»).
    /// </summary>
    sealed record MentionItem(string Insert, string Name, string Detail, IBrush Color, Specialist? Specialist = null);

    readonly Border _mentionBar = new() { IsVisible = false, Margin = new Thickness(12, 0, 12, 6), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(4) };
    IReadOnlyList<MentionItem> _mentionAll = [];
    List<MentionItem> _mentionPicks = [];
    int _mentionIndex;
    /// <summary>Dónde empieza el @ o el # que se está escribiendo (su posición en la caja); -1 si no hay.</summary>
    int _mentionAt = -1;
    char _mentionMark;

    /// <summary>Para las pruebas: lo que ofrece la lista de @ o # (vacía si no se ve).</summary>
    internal IReadOnlyList<string> MentionChoices => _mentionBar.IsVisible ? [.. _mentionPicks.Select(s => s.Name)] : [];

    /// <summary>
    /// Lo escrito cambió: si el cursor está justo después de «@algo» (especialistas) o «#algo» (las otras sesiones de la
    /// Terminal), la lista de los que coinciden.
    /// </summary>
    void UpdateMentions()
    {
        if (_current is not { Shell: null } || _creating)
        {
            HideMentions();
            return;
        }
        var text = _input.Text ?? "";
        var caret = Math.Clamp(_input.CaretIndex, 0, text.Length);
        var at = caret == 0 ? -1 : text.LastIndexOfAny(['@', '#'], caret - 1);
        if (at < 0 || (at > 0 && !char.IsWhiteSpace(text[at - 1])) || text[at..caret].Any(char.IsWhiteSpace))
        {
            HideMentions();
            return;
        }
        var mark = text[at];
        // Al empezar uno nuevo: los de ahora (pudo crearse un especialista o abrirse una pestaña).
        if (_mentionAt != at || _mentionMark != mark) _mentionAll = mark == '@' ? SpecialistItems() : SessionItems();
        _mentionAt = at;
        _mentionMark = mark;
        var typed = SpecialistTools.Fold(text[(at + 1)..caret]);
        _mentionPicks = [.. _mentionAll.Where(m => typed.Length == 0 || SpecialistTools.Fold(m.Insert).Contains(typed, StringComparison.Ordinal)
            || SpecialistTools.Fold(m.Detail).Contains(typed, StringComparison.Ordinal)).Take(6)];
        if (_mentionPicks.Count == 0)
        {
            HideMentions();
            return;
        }
        _mentionIndex = Math.Clamp(_mentionIndex, 0, _mentionPicks.Count - 1);
        DrawMentions();
    }

    IReadOnlyList<MentionItem> SpecialistItems() =>
        [.. Specialists.All().Select(s => new MentionItem("@" + SpecialistTools.Slug(s.Name), s.Name, s.Purpose.Length > 0 ? s.Purpose : AgentKinds.Label(s.Agent), Brush(s.Color), s))];

    /// <summary>Las otras pestañas de agente de la Terminal, con lo que se escribe para traer su contexto («#arreglo-login»).</summary>
    IReadOnlyList<MentionItem> SessionItems() =>
        [.. _tabs.Where(t => t.Shell is null && t != _current).Select(t => new MentionItem("#" + TabSlug(t), Title(t),
            $"{t.Project} · {AgentKinds.Label(t.Agent.Options.Kind)}{(t.Agent.Transcript.Busy ? " · " + L.T("trabajando") : "")}", _ink.Accent))];

    /// <summary>Cómo se nombra una pestaña después de #: las primeras palabras de su título.</summary>
    internal static string TabSlug(Tab tab) =>
        SpecialistTools.Slug(string.Join(' ', Title(tab).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4))) is { Length: > 0 } slug ? slug : SpecialistTools.Slug(tab.Project);

    void HideMentions()
    {
        _mentionAt = -1;
        _mentionIndex = 0;
        _mentionBar.IsVisible = false;
    }

    void DrawMentions()
    {
        var list = new StackPanel { Spacing = 1 };
        for (var i = 0; i < _mentionPicks.Count; i++)
        {
            var m = _mentionPicks[i];
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto") };
            row.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = m.Color, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 9, 0) });
            var name = Ui.Text(m.Insert, 13, _ink.Text, FontWeight.SemiBold);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.MaxWidth = 280;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var detail = Ui.Text(m.Detail, 12, _ink.Tertiary);
            detail.TextTrimming = TextTrimming.CharacterEllipsis;
            detail.Margin = new Thickness(10, 0, 0, 0);
            detail.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(detail, 2);
            row.Children.Add(detail);
            if (m.Specialist is { } specialist)
            {
                // Para quien lo quiere en una pestaña propia, con su agente, sus herramientas y su carpeta.
                var label = Ui.Text(L.T("Abrir aparte"), 11.5, _ink.Tertiary);
                var apart = Ui.Press(label, () =>
                {
                    HideMentions();
                    OpenSpecialist(specialist);
                }, (b, on) =>
                {
                    b.Background = on ? _ink.Hairline : Brushes.Transparent;
                    label.Foreground = on ? _ink.Text : _ink.Tertiary;
                }, L.T("Abrir aparte"));
                apart.Padding = new Thickness(7, 2);
                apart.CornerRadius = new CornerRadius(6);
                apart.Margin = new Thickness(8, 0, 0, 0);
                apart.VerticalAlignment = VerticalAlignment.Center;
                ToolTip.SetTip(apart, L.T("Abre «{0}» en una pestaña propia, aparte de esta sesión.", specialist.Name));
                Grid.SetColumn(apart, 3);
                row.Children.Add(apart);
            }
            var pick = m;
            var item = Ui.Press(row, () => PickMention(pick), (b, on) => b.Background = on || pick == _mentionPicks[_mentionIndex] ? _ink.Pill : Brushes.Transparent, m.Name);
            ToolTip.SetTip(item, m.Name);
            item.Padding = new Thickness(8, 6);
            item.CornerRadius = new CornerRadius(8);
            item.Background = i == _mentionIndex ? _ink.Pill : Brushes.Transparent;
            list.Children.Add(item);
        }
        var hint = Ui.Text(_mentionMark == '@'
            ? _current is { } tab && SubagentsOf(tab).Count > 0
                ? L.T("Intro o Tab elige · Esc cierra · trabaja dentro de esta sesión, como subagente")
                : L.T("Intro o Tab elige · Esc cierra · el agente de esta pestaña le delega la tarea")
            : L.T("Intro o Tab elige · Esc cierra · el pedido lleva el contexto de esa sesión"), 11, _ink.Tertiary);
        hint.Margin = new Thickness(10, 4, 10, 2);
        list.Children.Add(hint);
        _mentionBar.Background = _ink.Raised;
        _mentionBar.BorderBrush = _ink.Hairline;
        _mentionBar.Child = list;
        _mentionBar.IsVisible = true;
    }

    /// <summary>Elige de la lista: el @ o # que se escribía pasa a ser su nombre, con un espacio para seguir.</summary>
    void PickMention(MentionItem m)
    {
        var text = _input.Text ?? "";
        if (_mentionAt < 0 || _mentionAt > text.Length) return;
        var caret = Math.Clamp(_input.CaretIndex, _mentionAt, text.Length);
        var insert = m.Insert + " ";
        var after = text[caret..].TrimStart();
        _input.Text = text[.._mentionAt] + insert + after;
        _input.CaretIndex = _mentionAt + insert.Length;
        HideMentions();
        _input.Focus();
    }

    /// <summary>Las teclas mientras se ve la lista: flechas, Intro o Tab para elegir y Esc para cerrar. true si la usó.</summary>
    bool MentionKey(KeyEventArgs e)
    {
        if (!_mentionBar.IsVisible || _mentionPicks.Count == 0) return false;
        switch (e.Key)
        {
            case Key.Down: _mentionIndex = (_mentionIndex + 1) % _mentionPicks.Count; DrawMentions(); return true;
            case Key.Up: _mentionIndex = (_mentionIndex - 1 + _mentionPicks.Count) % _mentionPicks.Count; DrawMentions(); return true;
            case Key.Enter or Key.Tab when !e.KeyModifiers.HasFlag(KeyModifiers.Shift): PickMention(_mentionPicks[_mentionIndex]); return true;
            case Key.Escape: HideMentions(); return true;
            default: return false;
        }
    }

    static IBrush Brush(string hex) => Color.TryParse(hex, out var c) ? new SolidColorBrush(c) : Brushes.Gray;

    // ── Las corridas de los especialistas ──────────────────────────────────────────────────

    readonly Border _runsBar = new() { IsVisible = false, Margin = new Thickness(12, 0, 12, 6) };
    /// <summary>Las corridas conocidas, por id: las que pidió alguna pestaña (por la clave de su sesión).</summary>
    readonly Dictionary<string, SpecialistRun> _runs = [];
    /// <summary>Las que la persona cerró con ×.</summary>
    readonly HashSet<string> _runsDismissed = [];
    FileSystemWatcher? _runsWatcher;
    DispatcherTimer? _runsTimer;

    /// <summary>Para las pruebas: las corridas que se ven arriba de la caja (el nombre y cómo van).</summary>
    internal IReadOnlyList<(string Name, string State)> RunsShown => _runsBar.IsVisible ? [.. RunsOf(_current).Select(r => (r.Name, r.State))] : [];

    /// <summary>Empieza a mirar el diario de las corridas (una vez).</summary>
    void WatchRuns()
    {
        if (_runsWatcher is not null) return;
        try
        {
            Directory.CreateDirectory(SpecialistRunner.Folder);
            _runsWatcher = new FileSystemWatcher(SpecialistRunner.Folder, "*.json") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
            _runsWatcher.Changed += (_, e) => RunChanged(e.FullPath);
            _runsWatcher.Created += (_, e) => RunChanged(e.FullPath);
            _runsWatcher.Renamed += (_, e) => RunChanged(e.FullPath);
            _runsWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { ErrorLog.Write(ex); }
    }

    void RunChanged(string path)
    {
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return;
        var id = Path.GetFileNameWithoutExtension(path);
        Dispatcher.UIThread.Post(() => Track(SpecialistRunner.Read(id)));
    }

    /// <summary>Una corrida nueva o que cambió: si la pidió una de estas pestañas, se guarda y se dibuja.</summary>
    internal void Track(SpecialistRun? run)
    {
        if (run?.From is not { Length: > 0 } from || !_tabs.Any(t => t.Shell is null && t.Agent.Driver.CanvasKey == from)) return;
        _runs[run.Id] = run;
        if (!RunStates.Finished(run.State) && _runsTimer is null)
        {
            // Mientras alguna trabaja, el tiempo que lleva se actualiza solo.
            _runsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _runsTimer.Tick += (_, _) =>
            {
                if (!_runs.Values.Any(r => !RunStates.Finished(r.State)))
                {
                    _runsTimer?.Stop();
                    _runsTimer = null;
                }
                DrawRuns();
            };
            _runsTimer.Start();
        }
        DrawRuns();
    }

    /// <summary>Las corridas que pidió esa pestaña y que siguen a la vista: las que trabajan y las que terminaron hace menos de 10 minutos.</summary>
    IEnumerable<SpecialistRun> RunsOf(Tab? tab) => tab is { Shell: null }
        ? _runs.Values.Where(r => r.From == tab.Agent.Driver.CanvasKey && !_runsDismissed.Contains(r.Id)
            && (!RunStates.Finished(r.State) || DateTimeOffset.Now - (r.Ended ?? r.Started) < TimeSpan.FromMinutes(10))).OrderBy(r => r.Started)
        : [];

    void DrawRuns()
    {
        var runs = RunsOf(_creating ? null : _current).ToList();
        if (runs.Count == 0)
        {
            _runsBar.IsVisible = false;
            return;
        }
        var colors = Specialists.All().ToDictionary(s => s.Id, s => s.Color);
        var list = new StackPanel { Spacing = 4 };
        foreach (var run in runs) list.Children.Add(RunRow(run, colors.GetValueOrDefault(run.Specialist)));
        _runsBar.Child = list;
        _runsBar.IsVisible = true;
    }

    Control RunRow(SpecialistRun run, string? color)
    {
        var working = !RunStates.Finished(run.State);
        var took = (run.Ended ?? DateTimeOffset.Now) - run.Started;
        var state = run.State switch
        {
            RunStates.Working => L.T("trabajando"),
            RunStates.Waiting => L.T("pide permiso"),
            RunStates.Done => L.T("listo"),
            RunStates.Failed => L.T("no pudo"),
            RunStates.Stopped => L.T("detenido"),
            RunStates.TimedOut => L.T("pasó su tope"),
            _ => run.State,
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = color is null ? _ink.Accent : Brush(color), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) };
        if (working) Pulse(dot);
        grid.Children.Add(dot);
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        var head = Ui.Text($"{run.Name} · {state} · {(int)took.TotalMinutes}:{took.Seconds:00}", 12.5, run.State == RunStates.Waiting ? _ink.Warn : _ink.Text, FontWeight.Medium);
        head.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(head);
        var detail = working ? run.Steps.LastOrDefault() ?? L.T("Empezando…") : (run.Result ?? "").ReplaceLineEndings(" ");
        if (detail.Length > 0)
        {
            var line = Ui.Text(detail, 11.5, _ink.Tertiary);
            line.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(line);
        }
        ToolTip.SetTip(text, run.Task);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        if (run.SessionId is not null) buttons.Children.Add(RunButton(L.T("Ver su sesión"), () => OpenRun(run)));
        if (working) buttons.Children.Add(RunButton(L.T("Detener"), () => SpecialistRunner.RequestStop(run.Id)));
        else buttons.Children.Add(RunButton("×", () => { _runsDismissed.Add(run.Id); DrawRuns(); }));
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        return new Border { Background = _ink.Raised, BorderBrush = _ink.Hairline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 6), Child = grid };
    }

    Control RunButton(string label, Action action)
    {
        var button = Ui.Press(Ui.Text(label, 12, _ink.Secondary), action, (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, label);
        button.Padding = new Thickness(8, 3);
        button.CornerRadius = new CornerRadius(7);
        return button;
    }

    /// <summary>«Ver su sesión»: la conversación del especialista en una pestaña (la que ya la tenga o una nueva que la retoma).</summary>
    internal void OpenRun(SpecialistRun run)
    {
        if (run.SessionId is not { } id || !Directory.Exists(run.Folder)) return;
        if (_tabs.FirstOrDefault(t => t.Shell is null && (t.Agent.Transcript.SessionId == id || t.Agent.Options.Resume == id)) is { } open)
        {
            Select(open);
            return;
        }
        var specialist = Specialists.All().FirstOrDefault(s => s.Id == run.Specialist);
        if (run.Agent == AgentKinds.Claude)
        {
            OpenSession(new SessionTarget(id, run.Folder, specialist?.Account));
            return;
        }
        var agent = NewAgent(new AgentOptions(run.Folder, specialist?.Account, specialist?.Model, specialist?.Effort, Kind: run.Agent, Resume: id, Specialist: specialist));
        var tab = new Tab(agent, specialist?.Name ?? run.Name);
        agent.Changed += changed => OnChanged(tab, changed);
        Add(tab);
        agent.Start();
        _input.Focus();
    }
}
