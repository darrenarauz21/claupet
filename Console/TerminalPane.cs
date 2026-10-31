namespace Claupet;

using System.Text.Json;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;

/// <summary>
/// Claupet Terminal: las sesiones de agente en bloques, como los de PowerShell o bash (pedido, respuesta, pasos, permisos),
/// sin la pantalla de la TUI. Cada pestaña es un Claude Code con su cuenta, su modelo y su esfuerzo. A la izquierda, las
/// pestañas por proyecto (Aside); a la derecha, la sesión elegida o la sesión nueva. No necesita a la bandeja.
/// </summary>
sealed partial class TerminalPane : Grid
{
    // Los íconos, en un cuadro de 24.
    const string PlusIcon = "M12,5 V19 M5,12 H19";
    const string SendIcon = "M12,19 V5 M6,11 L12,5 L18,11";
    const string StopIcon = "M8.5,7 H15.5 A1.5,1.5 0 0 1 17,8.5 V15.5 A1.5,1.5 0 0 1 15.5,17 H8.5 A1.5,1.5 0 0 1 7,15.5 V8.5 A1.5,1.5 0 0 1 8.5,7 Z";
    const string CloseIcon = "M7,7 L17,17 M17,7 L7,17";
    const string BranchIcon = "M7,4 V20 M7,14 A6,6 0 0 0 13,8 H15 M17,8 A2,2 0 1 1 13,8 A2,2 0 1 1 17,8 Z M9,20 A2,2 0 1 1 5,20 A2,2 0 1 1 9,20 Z";
    const string CheckIcon = "M5,12.5 L10,17.5 L19,7";
    const string CrossIcon = "M7,7 L17,17 M17,7 L7,17";
    const string PauseIcon = "M9,6 V18 M15,6 V18";
    const string BlockedIcon = "M12,3 A9,9 0 1 0 12.01,3 Z M5.6,5.6 L18.4,18.4";

    /// <summary>Las animaciones (aparecer, pensando, el giro de un paso). Se apagan en Ajustes › Terminal.</summary>
    public static bool Animate { get; set; } = true;

    sealed class Tab(TerminalAgent agent, string account)
    {
        public TerminalAgent Agent { get; } = agent;
        public string Account { get; } = account;
        public string? Branch { get; set; } = GitBranch.Of(agent.Options.Folder);
        public string Project => Path.GetFileName(Agent.Options.Folder.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : Agent.Options.Folder;
        public string Draft { get; set; } = "";
        /// <summary>Una terminal de comandos (TerminalPane.Shell.cs). Entonces Agent no se abre: solo lleva la carpeta.</summary>
        public ShellSession? Shell { get; init; }
    }

    /// <summary>
    /// Cuántos bloques de una sesión se dibujan: los últimos. Con todos, cada cambio volvía a acomodar la sesión entera y
    /// una sesión larga (cientos de pasos) se ponía lenta. Los de antes se ven con «Ver los anteriores».
    /// </summary>
    const int ShownBlocks = 150;
    /// <summary>Cuántos bloques de más se juntan antes de dejar de dibujar los de arriba (para no hacerlo en cada cambio).</summary>
    const int TrimSlack = 50;

    readonly List<Tab> _tabs = [];
    /// <summary>El primer bloque de la sesión elegida que se dibuja; arriba de él, el botón de los anteriores.</summary>
    int _shownFrom;
    Control? _earlier;
    /// <summary>Lo que muestra la lista de cada pestaña, para rehacerla solo cuando cambia (no con cada pedazo de texto).</summary>
    readonly Dictionary<Tab, (bool Busy, bool Waiting, bool Running, string Title)> _tabState = [];
    readonly Dictionary<AgentBlock, Border> _views = [];
    /// <summary>Lo que se va escribiendo de una respuesta: se cambia solo el texto, sin rehacer el bloque.</summary>
    readonly Dictionary<AgentBlock, SelectableTextBlock> _live = [];
    readonly StackPanel _blocks = new() { Spacing = 6 };
    /// <summary>El tamaño elegido en Ajustes › Terminal agranda o achica los bloques enteros, como un zoom.</summary>
    readonly LayoutTransformControl _zoom = new();
    TerminalLook _look = TerminalLook.Load();
    /// <summary>Los colores del tema, antes de aplicarles el fondo y el color del texto de la Terminal.</summary>
    Ink? _theme;
    /// <summary>Se muestran los ajustes de la Terminal en vez de la sesión.</summary>
    bool _settings;
    readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    readonly TextBox _input = new();
    readonly StackPanel _list = new() { Spacing = 1 };
    Ink _ink = Ink.For(false);
    Tab? _current;
    bool _creating = true;
    /// <summary>La carpeta con la que se abre la sesión nueva (la del proyecto desde el que se pidió).</summary>
    string? _newFolder;
    Border _send = new();
    TextBlock _status = new();
    TextBlock _context = new();
    Border _interrupt = new();
    NewForm _form = new();

    /// <summary>La lista de pestañas cambió (para la columna de la izquierda).</summary>
    public event Action? TabsChanged;

    /// <summary>Cambió el ancho de la lista (con descripción o solo íconos): la ventana vuelve a armar la columna.</summary>
    public event Action? ListChanged;

    public TerminalPane()
    {
        _zoom.Child = _blocks;
        _scroll.Content = new Border { Child = _zoom, Padding = new Thickness(20, 12, 20, 16) };
        _input.AddHandler(KeyDownEvent, OnInputKey, RoutingStrategies.Tunnel);
        ApplyLook();
        Build();
    }

    public void SetInk(Ink ink)
    {
        if (ReferenceEquals(ink, _theme)) return;
        _theme = ink;
        _ink = _look.Apply(ink);
        Build();
        RenderList();
    }

    /// <summary>La letra de las sesiones (Ajustes › Terminal).</summary>
    FontFamily Mono => _look.Mono;

    /// <summary>El ancho de la columna de la izquierda: con descripción o solo los íconos.</summary>
    public double ListWidth => _look.IconsOnly ? 64 : _projects.ListWidth;

    void ApplyLook()
    {
        Animate = _look.Animate;
        var scale = _look.Size / TerminalLook.DefaultSize;
        _zoom.LayoutTransform = new ScaleTransform(scale, scale);
        if (_theme is { } theme) _ink = _look.Apply(theme);
    }

    public void FocusInput()
    {
        if (!_creating) _input.Focus();
    }

    /// <summary>Al cerrar la Console: ningún Claude Code queda corriendo.</summary>
    public void CloseAll()
    {
        foreach (var tab in _tabs)
        {
            tab.Agent.Dispose();
            tab.Shell?.Dispose();
        }
        _tabs.Clear();
        _tabState.Clear();
    }

    // ── Columna de la izquierda ─────────────────────────────────────────────────────────────

    /// <summary>«Terminal», el cambio de lista, los ajustes y el botón de sesión nueva (solo los botones, con la lista de íconos).</summary>
    public Control Header()
    {
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = _look.IconsOnly ? new Thickness(0, 2, 0, 10) : new Thickness(8, 2, 6, 10) };
        if (!_look.IconsOnly)
            bar.Children.Add(new TextBlock
        {
            Text = L.T("Terminal"),
            FontFamily = Ink.Display,
            FontSize = 19,
            FontWeight = FontWeight.SemiBold,
            Foreground = _ink.Text,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var add = Ui.Press(Icon(PlusIcon, Ink.On(_ink.Accent), 15, 2.2), ShowNew, (b, on) => b.Opacity = on ? 0.88 : 1, L.T("Sesión nueva"));
        add.Width = 34;
        add.Height = 34;
        add.CornerRadius = new CornerRadius(999);
        add.Background = _ink.Accent;
        ToolTip.SetTip(add, L.T("Sesión nueva"));
        var buttons = HeaderButtons(add);
        Grid.SetColumn(buttons, _look.IconsOnly ? 0 : 1);
        if (_look.IconsOnly) Grid.SetColumnSpan(buttons, 2);
        bar.Children.Add(buttons);
        return bar;
    }

    /// <summary>Las pestañas, agrupadas por proyecto.</summary>
    public Control List => _list;

    Control TabItem(Tab tab)
    {
        var current = tab == _current && !_creating && !_settings;
        var t = tab.Agent.Transcript;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var kind = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(6),
            Background = _ink.Pill,
            Child = Ui.Text(KindMark(tab), 12, _ink.Accent, FontWeight.Bold, Mono),
            Margin = new Thickness(0, 0, 9, 0),
        };
        ((TextBlock)kind.Child).HorizontalAlignment = HorizontalAlignment.Center;
        row.Children.Add(kind);
        var text = new StackPanel { Spacing = 1 };
        var title = Ui.Text(Title(tab), 13.5, _ink.Text);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(title);
        var info = Ui.Text(Describe(tab), 11.5, _ink.Tertiary);
        info.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(info);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var dot = new Border
        {
            Width = 7,
            Height = 7,
            CornerRadius = new CornerRadius(99),
            Margin = new Thickness(8, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Dot(tab),
        };
        if (Busy(tab)) Pulse(dot);
        Grid.SetColumn(dot, 2);
        row.Children.Add(dot);
        var item = Ui.Press(row, () => Select(tab), current ? null : (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, Title(tab));
        item.Padding = new Thickness(10, 6);
        item.CornerRadius = new CornerRadius(10);
        if (current) item.Background = _ink.Pill;
        ToolTip.SetTip(item, $"{Title(tab)} · {Describe(tab)}");
        return item;
    }

    /// <summary>Una pestaña en la lista de íconos: el de Claude Code con el punto de su estado encima.</summary>
    Control TabIcon(Tab tab)
    {
        var current = tab == _current && !_creating && !_settings;
        var t = tab.Agent.Transcript;
        var kind = Ui.Text(KindMark(tab), 13, _ink.Accent, FontWeight.Bold, Mono);
        kind.HorizontalAlignment = HorizontalAlignment.Center;
        kind.VerticalAlignment = VerticalAlignment.Center;
        var dot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(99),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -2, -2, 0),
            BorderBrush = _ink.Window,
            BorderThickness = new Thickness(1.5),
            Background = Dot(tab),
        };
        if (Busy(tab)) Pulse(dot);
        var badge = new Panel { Width = 30, Height = 30 };
        badge.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = current ? _ink.Raised : _ink.Pill, Child = kind });
        badge.Children.Add(dot);
        var item = Ui.Press(badge, () => Select(tab), current ? null : (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, Title(tab));
        item.Padding = new Thickness(6);
        item.Margin = new Thickness(8, 1);
        item.CornerRadius = new CornerRadius(10);
        item.HorizontalAlignment = HorizontalAlignment.Center;
        if (current) item.Background = _ink.Pill;
        ToolTip.SetTip(item, $"{Title(tab)}\n{tab.Project}{(tab.Branch is { } branch ? " · " + branch : "")}\n{Describe(tab)}");
        return item;
    }

    static bool Waiting(AgentTranscript t) => t.Blocks.Any(b => b.Kind == AgentBlockKind.Permission && b.Answer is null);

    static string Title(Tab tab) => tab.Shell is { } shell ? ShellTitle(shell) :
        tab.Agent.Transcript.Blocks.FirstOrDefault(b => b.Kind == AgentBlockKind.Ask)?.Text.ReplaceLineEndings(" ") is { Length: > 0 } first
            ? first : "Claude Code";

    static string Describe(Tab tab) => tab.Shell is { } shell ? ShellDescribe(shell) :
        string.Join(" · ", new[] { tab.Account, ModelLabel(tab.Agent.Options.Model), tab.Agent.Options.Effort }.Where(s => !string.IsNullOrEmpty(s)));

    static string ModelLabel(string? model) => model ?? L.T("modelo por defecto");

    void Select(Tab tab)
    {
        if (_current is { } previous && !_settings) previous.Draft = _input.Text ?? "";
        _current = tab;
        _creating = false;
        _settings = false;
        _input.Text = tab.Draft;
        Build();
        RenderList();
        _input.Focus();
    }

    /// <summary>La sesión nueva; con folder, en esa carpeta (desde el menú de un proyecto).</summary>
    void ShowNew() => ShowNew(null);

    void ShowNew(string? folder)
    {
        if (_current is { } previous && !_settings && !_creating) previous.Draft = _input.Text ?? "";
        _creating = true;
        _settings = false;
        _newFolder = folder;
        Build();
        RenderList();
    }

    void CloseTab(Tab tab)
    {
        tab.Agent.Dispose();
        tab.Shell?.Dispose();
        var index = _tabs.IndexOf(tab);
        _tabs.Remove(tab);
        _tabState.Remove(tab);
        if (_current == tab)
        {
            _current = _tabs.Count == 0 ? null : _tabs[Math.Clamp(index, 0, _tabs.Count - 1)];
            _creating = _current is null;
            _input.Text = _current?.Draft ?? "";
        }
        Build();
        RenderList();
        TabsChanged?.Invoke();
    }

    // ── La sesión ───────────────────────────────────────────────────────────────────────────

    void Build()
    {
        (_input.Parent as Panel)?.Children.Remove(_input);
        Children.Clear();
        RowDefinitions.Clear();
        ColumnDefinitions.Clear();
        StopSide();
        _views.Clear();
        _live.Clear();
        _earlier = null;
        _blocks.Children.Clear();
        // Con un fondo propio, la Terminal lo pinta (tapa la escena del tema); si no, se ve el de la ventana.
        Background = _look.OwnBackground ? _ink.Surface : null;
        if (_settings)
        {
            Children.Add(LookPage());
            return;
        }
        if (_creating || _current is null)
        {
            _form = new NewForm();
            if (_newFolder is { } folder && Directory.Exists(folder)) _form.Folder = folder;
            _newFolder = null;
            Children.Add(NewSession());
            return;
        }
        if (_current.Shell is not null)
        {
            BuildShell(_current);
            return;
        }
        RowDefinitions = new RowDefinitions("46,*,Auto");
        ColumnDefinitions = new ColumnDefinitions(_projects.PanelOpen ? "*,Auto" : "*");
        if (_projects.PanelOpen)
        {
            var side = SidePanel(_current);
            Grid.SetColumn(side, 1);
            Grid.SetRowSpan(side, 3);
            Children.Add(side);
        }
        Children.Add(SessionHeader(_current));
        Grid.SetRow(_scroll, 1);
        Children.Add(_scroll);
        var composer = Composer(_current);
        Grid.SetRow(composer, 2);
        Children.Add(composer);
        var blocks = _current.Agent.Transcript.Blocks;
        _shownFrom = Math.Max(0, blocks.Count - ShownBlocks);
        ShowBlocks(blocks);
        PaintStatus();
        Dispatcher.UIThread.Post(_scroll.ScrollToEnd, DispatcherPriority.Background);
    }

    /// <summary>Dibuja los bloques desde _shownFrom y, si hay de antes, el botón para verlos.</summary>
    void ShowBlocks(IReadOnlyList<AgentBlock> blocks)
    {
        _views.Clear();
        _live.Clear();
        _blocks.Children.Clear();
        _earlier = _shownFrom > 0 ? Earlier() : null;
        if (_earlier is not null) _blocks.Children.Add(_earlier);
        for (var i = _shownFrom; i < blocks.Count; i++) _blocks.Children.Add(View(blocks[i], appear: false));
        if (blocks.Count == 0) _blocks.Children.Add(Starting());
    }

    /// <summary>«Ver los N anteriores»: dibuja otros tantos de arriba.</summary>
    Control Earlier()
    {
        var label = Ui.Text(L.T("Ver los {0} bloques anteriores", _shownFrom), 12, _ink.Tertiary);
        var press = Ui.Press(label, () =>
        {
            if (_current is not { Shell: null } tab) return;
            _shownFrom = Math.Max(0, _shownFrom - ShownBlocks);
            ShowBlocks(tab.Agent.Transcript.Blocks);
        }, (b, on) => label.Foreground = on ? _ink.Secondary : _ink.Tertiary, L.T("Ver los bloques anteriores"));
        press.HorizontalAlignment = HorizontalAlignment.Center;
        press.Padding = new Thickness(10, 4);
        press.Margin = new Thickness(0, 2, 0, 6);
        press.CornerRadius = new CornerRadius(8);
        return press;
    }

    /// <summary>Con la sesión al final, los bloques de arriba que sobran dejan de dibujarse (siguen con «Ver los anteriores»).</summary>
    void TrimBlocks(IReadOnlyList<AgentBlock> blocks)
    {
        if (blocks.Count - _shownFrom <= ShownBlocks + TrimSlack) return;
        var from = blocks.Count - ShownBlocks;
        var gone = new List<Control>();
        for (var i = _shownFrom; i < from; i++)
        {
            if (!_views.Remove(blocks[i], out var view)) continue;
            _live.Remove(blocks[i]);
            gone.Add(view);
        }
        if (_earlier is not null) gone.Add(_earlier);
        _blocks.Children.RemoveAll(gone);
        _shownFrom = from;
        _earlier = Earlier();
        _blocks.Children.Insert(0, _earlier);
    }

    /// <summary>La posición del bloque en la sesión (se busca desde el final: lo que cambia suele ser lo último).</summary>
    static int IndexOf(IReadOnlyList<AgentBlock> blocks, AgentBlock block)
    {
        for (var i = blocks.Count - 1; i >= 0; i--)
            if (ReferenceEquals(blocks[i], block)) return i;
        return -1;
    }

    /// <summary>Si cambió lo que la lista muestra de la pestaña (su punto, su título). Lo recuerda para la próxima.</summary>
    bool ListStateChanged(Tab tab)
    {
        var t = tab.Agent.Transcript;
        var state = (t.Busy, Waiting(t), tab.Agent.Running, Title(tab));
        if (_tabState.TryGetValue(tab, out var before) && before == state) return false;
        _tabState[tab] = state;
        return true;
    }

    Control Starting()
    {
        var hint = Ui.Text(L.T("Escriba lo que necesita. Los pasos, los cambios y los permisos se muestran aquí, en bloques."), 13, _ink.Tertiary);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(4, 8);
        return hint;
    }

    Control SessionHeader(Tab tab)
    {
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto"), Margin = new Thickness(20, 0, 10, 0) };
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
        ToolTip.SetTip(where, tab.Agent.Options.Folder);
        bar.Children.Add(where);
        _status = Ui.Text("", 12, _ink.Tertiary);
        _status.Margin = new Thickness(12, 0, 0, 0);
        _status.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(_status, 1);
        bar.Children.Add(_status);
        _context = Ui.Text("", 12, _ink.Tertiary, family: Mono);
        _context.Margin = new Thickness(8, 0);
        Grid.SetColumn(_context, 2);
        bar.Children.Add(_context);
        _interrupt = Pill(L.T("Interrumpir"), _ink.Pill, _ink.Text, () => tab.Agent.Interrupt());
        Grid.SetColumn(_interrupt, 3);
        bar.Children.Add(_interrupt);
        var panel = Round(PanelIcon, L.T("Ramas, cambios y procesos"), ToggleSide);
        panel.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(panel, 4);
        bar.Children.Add(panel);
        var close = Round(CloseIcon, L.T("Cerrar la sesión"), () => CloseTab(tab));
        close.Margin = new Thickness(2, 0, 0, 0);
        Grid.SetColumn(close, 5);
        bar.Children.Add(close);
        return bar;
    }

    /// <summary>La franja de arriba: modelo, uso de la cuenta y contexto; e «Interrumpir» mientras trabaja.</summary>
    void PaintStatus()
    {
        if (_current is not { } tab) return;
        var t = tab.Agent.Transcript;
        _status.Text = t.Model is { } model ? model.Replace("claude-", "") + (tab.Agent.Effort is { } e ? " · " + e : "") : "";
        var parts = new List<string>();
        if (t.ContextPercent is { } context) parts.Add(L.T("contexto {0:0} %", context));
        if (t.FiveHour is { } five) parts.Add(L.T("5 h {0:0} %", five));
        _context.Text = string.Join("  ", parts);
        _interrupt.IsVisible = t.Busy && tab.Agent.Running;
        _send.Opacity = tab.Agent.Running ? 1 : 0.45;
    }

    Control Composer(Tab tab)
    {
        _input.AcceptsReturn = true;
        _input.TextWrapping = TextWrapping.Wrap;
        _input.MinHeight = 22;
        _input.MaxHeight = 180;
        Plain(_input);
        _input.FontSize = _look.Size;
        _input.Watermark = L.T("Pídale algo a Claude Code");
        Ui.AutomationName(_input, L.T("Mensaje para Claude Code"));

        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var prompt = Ui.Text("❯", 14, _ink.Accent, FontWeight.Bold, Mono);
        prompt.VerticalAlignment = VerticalAlignment.Top;
        prompt.Margin = new Thickness(0, 1, 10, 0);
        line.Children.Add(prompt);
        Grid.SetColumn(_input, 1);
        line.Children.Add(_input);
        _send = Ui.Press(Icon(SendIcon, Ink.On(_ink.Accent), 15, 2.2), Submit, null, L.T("Enviar"));
        _send.Width = 30;
        _send.Height = 30;
        _send.CornerRadius = new CornerRadius(999);
        _send.Background = _ink.Accent;
        _send.VerticalAlignment = VerticalAlignment.Bottom;
        ToolTip.SetTip(_send, L.T("Enviar"));
        Grid.SetColumn(_send, 2);
        line.Children.Add(_send);

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(22, 8, 0, 0) };
        chips.Children.Add(Chip(tab.Account));
        // El modelo, el esfuerzo y los permisos se cambian aquí durante la sesión; valen desde el turno siguiente. Las
        // listas son las del agente y se leen al tocar (Codex, Gemini y Copilot las dicen al abrir la sesión).
        var agent = tab.Agent;
        var driver = agent.Driver;
        TextBlock? modelText = null, effortText = null, modeText = null;
        void Pick(Control anchor, IReadOnlyList<DropList.Entry> entries, Action<string> chosen)
        {
            if (entries.Count == 0) return;
            DropList.Show(anchor, _ink, entries, picked =>
            {
                if (picked is not null) chosen(picked);
            });
        }
        chips.Children.Add(Chip(ModelLabel(agent.Transcript.Model?.Replace("claude-", "") ?? agent.Options.Model), anchor =>
            Pick(anchor, driver.Models.Count == 0 ? [] : driver.Models.Select(m => new DropList.Entry(m, m)).Prepend(new DropList.Entry("", L.T("modelo por defecto"))).ToList(), picked =>
            {
                agent.SetModel(picked.Length == 0 ? null : picked);
                modelText!.Text = picked.Length == 0 ? L.T("modelo por defecto") : picked;
            }), out modelText));
        chips.Children.Add(Chip(agent.Effort ?? L.T("esfuerzo por defecto"), anchor =>
            Pick(anchor, driver.Efforts.Select(e => new DropList.Entry(e, e, e == agent.Effort)).ToList(), picked =>
            {
                agent.SetEffort(picked);
                effortText!.Text = picked;
                PaintStatus();
            }), out effortText));
        var mode = agent.Transcript.PermissionMode ?? agent.Options.PermissionMode ?? "default";
        chips.Children.Add(Chip(ModeLabel(mode), anchor =>
            Pick(anchor, driver.Modes.Select(m => new DropList.Entry(m, ModeLabel(m), m == (agent.Transcript.PermissionMode ?? mode))).ToList(), picked =>
            {
                agent.SetPermissionMode(picked);
                modeText!.Text = ModeLabel(picked);
            }), out modeText));
        var hint = Ui.Text(L.T("Intro envía · Mayús+Intro, otra línea"), 11.5, _ink.Tertiary);
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

    Control Chip(string text)
    {
        var label = Ui.Text(text, 11.5, _ink.Secondary);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.MaxWidth = 220;
        return new Border { Background = _ink.Pill, CornerRadius = new CornerRadius(999), Padding = new Thickness(9, 2), Child = label };
    }

    /// <summary>Una etiqueta que abre su lista al tocarla (el modelo, el esfuerzo o los permisos de la sesión).</summary>
    Control Chip(string text, Action<Control> pick, out TextBlock label)
    {
        label = Ui.Text(text, 11.5, _ink.Secondary);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.MaxWidth = 220;
        Border? chip = null;
        chip = Ui.Press(label, () => pick(chip!), (b, on) => b.Background = on ? _ink.Track : _ink.Pill, text);
        chip.Background = _ink.Pill;
        chip.CornerRadius = new CornerRadius(999);
        chip.Padding = new Thickness(9, 2);
        return chip;
    }

    void OnInputKey(object? sender, KeyEventArgs e)
    {
        if (_current?.Shell is { } shell && !_creating && !_settings)
        {
            e.Handled = ShellKey(shell, e);
            return;
        }
        if (e.Key == Key.Escape && _current is { } tab && tab.Agent.Transcript.Busy)
        {
            e.Handled = true;
            tab.Agent.Interrupt();
            return;
        }
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        e.Handled = true;
        Submit();
    }

    void Submit()
    {
        if (_current?.Shell is { } shell)
        {
            SubmitShell(shell);
            return;
        }
        if (_current is not { } tab || !tab.Agent.Running) return;
        var text = _input.Text?.Trim() ?? "";
        if (text.Length == 0) return;
        _input.Text = "";
        tab.Agent.Send(text);
        Dispatcher.UIThread.Post(_scroll.ScrollToEnd, DispatcherPriority.Background);
    }

    /// <summary>Llegaron eventos de una sesión: se cambian solo sus bloques (y la lista, si cambió su estado).</summary>
    void OnChanged(Tab tab, IReadOnlyList<AgentBlock> changed)
    {
        // Al terminar un turno pudo cambiar el repositorio (archivos editados, un commit).
        if (changed.Any(b => b.Kind == AgentBlockKind.Done))
        {
            RefreshGit(tab.Agent.Options.Folder, force: true);
            if (tab == _current && !_creating && !_settings && _projects.PanelOpen && _projects.PanelTab != "procesos") LoadSide(tab);
        }
        // La lista se rehace solo si cambió lo que muestra de la pestaña: con cada pedazo de texto la rehacía entera.
        var listChanged = ListStateChanged(tab);
        if (tab != _current || _creating || _settings)
        {
            if (listChanged) RenderList();
            return;
        }
        var blocks = tab.Agent.Transcript.Blocks;
        var atEnd = _scroll.Offset.Y >= _scroll.Extent.Height - _scroll.Viewport.Height - 40;
        if (changed.Count > 0 && _blocks.Children.Count == 1 && _views.Count == 0 && _earlier is null) _blocks.Children.Clear();
        foreach (var block in changed)
        {
            if (_live.TryGetValue(block, out var text) && block.Open)
            {
                text.Text = block.Text;
                continue;
            }
            var old = _views.GetValueOrDefault(block);
            // Un bloque de arriba de los que se dibujan (el resultado de un paso viejo): se verá al pedir los anteriores.
            if (old is null && _shownFrom > 0 && IndexOf(blocks, block) is var at and >= 0 && at < _shownFrom) continue;
            var view = View(block, appear: old is null);
            if (old is not null && _blocks.Children.IndexOf(old) is var index and >= 0) _blocks.Children[index] = view;
            else _blocks.Children.Add(view);
        }
        // Bloques que ya no están en la sesión.
        if (_views.Count > 0)
        {
            var present = blocks.ToHashSet();
            foreach (var gone in _views.Keys.Where(b => !present.Contains(b)).ToList())
            {
                _blocks.Children.Remove(_views[gone]);
                _views.Remove(gone);
                _live.Remove(gone);
            }
        }
        if (atEnd) TrimBlocks(blocks);
        PaintStatus();
        if (listChanged || changed.Count == 0 || changed.Any(b => b.Kind is AgentBlockKind.Ask or AgentBlockKind.Done or AgentBlockKind.Permission or AgentBlockKind.Notice))
            RenderList();
        if (atEnd) Dispatcher.UIThread.Post(_scroll.ScrollToEnd, DispatcherPriority.Background);
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
        var view = new Border { Child = content };
        _views[block] = view;
        if (appear && Animate) Appear(view);
        return view;
    }

    Control AskView(AgentBlock block)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var prompt = Ui.Text("❯", 13.5, _ink.Accent, FontWeight.Bold, Mono);
        prompt.VerticalAlignment = VerticalAlignment.Top;
        prompt.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(prompt);
        var text = Selectable(block.Text, Mono, 13.5, _ink.Text);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return new Border
        {
            Background = _ink.Pill,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 10, 0, 2),
            Child = row,
        };
    }

    Control SayView(AgentBlock block)
    {
        var column = new StackPanel { Spacing = 8, Margin = new Thickness(22, 2, 4, 2) };
        if (block.Open)
        {
            // Mientras llega, texto plano (barato de cambiar); al terminar, con el código en su bloque.
            var live = Selectable(block.Text, Ink.Sans, 14, _ink.Text);
            _live[block] = live;
            column.Children.Add(live);
            return column;
        }
        _live.Remove(block);
        foreach (var segment in Conversations.Split(block.Text))
            column.Children.Add(segment.Code ? Code(segment.Text) : Paragraph(segment.Text));
        return column;
    }

    Control ThinkView(AgentBlock block)
    {
        var text = block.Open
            ? L.T("Pensando…")
            : block.Tokens > 0 ? L.T("Pensó · {0:N0} tokens", block.Tokens) : L.T("Pensó");
        var label = Ui.Text(block.Open && block.Tokens > 0 ? $"{text} {block.Tokens:N0}" : text, 12.5, _ink.Tertiary);
        label.FontStyle = FontStyle.Italic;
        label.Margin = new Thickness(22, 0, 0, 0);
        if (block.Open) Pulse(label);
        return label;
    }

    Control StepView(AgentBlock block)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("22,Auto,*,Auto"), MinHeight = 24 };
        row.Children.Add(StateIcon(block.State));
        var verb = Ui.Text(AgentSteps.Verb(block.Tool), 13, _ink.Secondary);
        verb.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(verb, 1);
        row.Children.Add(verb);
        var target = Ui.Text(block.Text, 12.5, _ink.Text, family: Mono);
        target.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(target, block.Text);
        Grid.SetColumn(target, 2);
        row.Children.Add(target);
        if (block.Added + block.Removed > 0)
        {
            var counts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 0, 0, 0) };
            if (block.Added > 0) counts.Children.Add(Ui.Text($"+{block.Added}", 12, Added, family: Mono));
            if (block.Removed > 0) counts.Children.Add(Ui.Text($"−{block.Removed}", 12, _ink.Danger, family: Mono));
            Grid.SetColumn(counts, 3);
            row.Children.Add(counts);
        }

        var hasDetail = block.Diff.Count > 0 || !string.IsNullOrEmpty(block.Output);
        var stack = new StackPanel();
        Control head = row;
        if (hasDetail)
        {
            Border? detail = null;
            // Al abrirse, lo que cambió (o lo que devolvió); cerrado, una sola línea.
            var open = block.Diff.Count > 0 && block.Diff.Count <= 12;
            var press = Ui.Press(row, () =>
            {
                open = !open;
                if (detail is not null) detail.IsVisible = open;
            }, (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, block.Text);
            press.CornerRadius = new CornerRadius(8);
            press.Padding = new Thickness(0, 1);
            head = press;
            detail = Detail(block);
            detail.IsVisible = open;
            stack.Children.Add(head);
            stack.Children.Add(detail);
            return stack;
        }
        stack.Children.Add(head);
        return stack;
    }

    Border Detail(AgentBlock block)
    {
        var lines = new StackPanel();
        if (block.Diff.Count > 0)
            foreach (var line in block.Diff)
            {
                var sign = line.Length > 0 ? line[0] : ' ';
                var text = Ui.Text(line.Length > 0 ? line : " ", 12, sign == '+' ? Added : sign == '-' ? _ink.Danger : _ink.Secondary, family: Mono);
                text.TextTrimming = TextTrimming.CharacterEllipsis;
                lines.Children.Add(new Border
                {
                    Background = sign == '+' ? Translucent(Added, 0.10) : sign == '-' ? Translucent(_ink.Danger, 0.10) : Brushes.Transparent,
                    Padding = new Thickness(10, 1),
                    Child = text,
                });
            }
        else
        {
            var output = Selectable(block.Output ?? "", Mono, 12, _ink.Secondary);
            output.TextWrapping = TextWrapping.NoWrap;
            output.TextTrimming = TextTrimming.CharacterEllipsis;
            lines.Children.Add(new Border { Padding = new Thickness(10, 6), Child = output });
        }
        return new Border
        {
            Margin = new Thickness(22, 4, 0, 6),
            CornerRadius = new CornerRadius(8),
            BorderBrush = _ink.Hairline,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = lines,
        };
    }

    Control TodoView(AgentBlock block)
    {
        var list = new StackPanel { Spacing = 3, Margin = new Thickness(22, 4, 0, 4) };
        foreach (var item in block.Todos)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var (mark, brush) = item.Status switch
            {
                "completed" => ("☑", Added),
                "in_progress" => ("◐", _ink.Accent),
                _ => ("☐", _ink.Tertiary),
            };
            row.Children.Add(Ui.Text(mark, 13, brush, family: Mono));
            var text = Ui.Text(item.Text, 13, item.Status == "completed" ? _ink.Tertiary : _ink.Text);
            if (item.Status == "completed") text.TextDecorations = TextDecorations.Strikethrough;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Children.Add(text);
            list.Children.Add(row);
        }
        return list;
    }

    Control PermissionView(AgentBlock block)
    {
        var panel = new StackPanel { Spacing = 8 };
        var question = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        question.Children.Add(Ui.Text(Wants(block.Tool), 13, _ink.Text, FontWeight.SemiBold));
        var target = Ui.Text(block.Text, 12.5, _ink.Secondary, family: Mono);
        target.TextTrimming = TextTrimming.CharacterEllipsis;
        target.Margin = new Thickness(8, 0, 0, 0);
        ToolTip.SetTip(target, block.Text);
        Grid.SetColumn(target, 1);
        question.Children.Add(target);
        panel.Children.Add(question);
        if (Preview(block) is { Length: > 0 } preview)
        {
            var code = Selectable(preview, Mono, 12, _ink.Secondary);
            code.TextWrapping = TextWrapping.NoWrap;
            code.TextTrimming = TextTrimming.CharacterEllipsis;
            panel.Children.Add(new Border { Background = _ink.Surface, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6), Child = code });
        }
        if (block.Answer is { } answer)
            panel.Children.Add(Ui.Text(answer switch
            {
                PermissionAnswer.Deny => block.FromIsland ? L.T("Rechazado en la isla") : L.T("Rechazado"),
                PermissionAnswer.AllowAlways => L.T("Permitido siempre en este proyecto"),
                _ => block.FromIsland ? L.T("Permitido en la isla") : L.T("Permitido"),
            }, 12, answer == PermissionAnswer.Deny ? _ink.Danger : _ink.Tertiary));
        else
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            buttons.Children.Add(Pill(L.T("Permitir"), _ink.Accent, Ink.On(_ink.Accent), () => Answer(block, PermissionAnswer.Allow)));
            if (block.Suggestions is System.Text.Json.Nodes.JsonArray { Count: > 0 })
                buttons.Children.Add(Pill(L.T("Siempre aquí"), _ink.Pill, _ink.Text, () => Answer(block, PermissionAnswer.AllowAlways)));
            buttons.Children.Add(Pill(L.T("Rechazar"), _ink.Pill, _ink.Danger, () => Answer(block, PermissionAnswer.Deny)));
            panel.Children.Add(buttons);
        }
        return new Border
        {
            Margin = new Thickness(22, 4, 0, 4),
            CornerRadius = new CornerRadius(10),
            BorderBrush = block.Answer is null ? _ink.Warn : _ink.Hairline,
            BorderThickness = new Thickness(1),
            Background = block.Answer is null ? Translucent(_ink.Warn, 0.06) : Brushes.Transparent,
            Padding = new Thickness(12, 10),
            Child = panel,
        };
    }

    /// <summary>Lo que pide el permiso: «Quiere editar», «Quiere ejecutar»…</summary>
    static string Wants(string? tool) => tool switch
    {
        "Read" or "NotebookRead" => L.T("Quiere leer"),
        "Edit" or "MultiEdit" or "NotebookEdit" => L.T("Quiere editar"),
        "Write" => L.T("Quiere crear"),
        "Bash" or "PowerShell" => L.T("Quiere ejecutar"),
        "WebFetch" => L.T("Quiere abrir"),
        "Grep" or "Glob" or "WebSearch" => L.T("Quiere buscar"),
        _ => L.T("Quiere usar {0}", tool ?? "?"),
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
    /// Las preguntas de Claude Code (AskUserQuestion): las opciones con su explicación y «Otra respuesta» para escribirla.
    /// Con una sola pregunta de una opción, el clic ya responde (como en la isla); si no, «Responder» cuando estén todas.
    /// </summary>
    Control QuestionView(AgentBlock block)
    {
        var questions = block.Questions!;
        var panel = new StackPanel { Spacing = 10 };
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
            panel.Children.Add(Ui.Text(answer == PermissionAnswer.Deny
                ? block.FromIsland ? L.T("Rechazado en la isla") : L.T("No quiso responder")
                : block.FromIsland ? L.T("Respondido en la isla") : L.T("Respondido"), 12, answer == PermissionAnswer.Deny ? _ink.Danger : _ink.Tertiary));
            return QuestionCard(panel, answered: true);
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
            var head = questions.Count > 1 ? L.T("Pregunta {0} de {1}", i + 1, questions.Count) : L.T("Pregunta");
            if (q.MultiSelect) head += " · " + L.T("puede elegir varias");
            panel.Children.Add(Ui.Text(head, 11.5, _ink.Tertiary));
            var text = Wrapped(q.Question, 13, _ink.Text, FontWeight.SemiBold);
            text.Margin = new Thickness(0, -6, 0, 0);
            panel.Children.Add(text);

            var options = new WrapPanel { Orientation = Orientation.Horizontal };
            var other = new TextBox { MinWidth = 220, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left };
            Plain(other);
            other.FontSize = 12.5;
            other.Watermark = L.T("Otra respuesta…");
            Ui.AutomationName(other, L.T("Otra respuesta…"));
            foreach (var (label, description) in q.Options)
            {
                var content = new StackPanel { Spacing = 2 };
                content.Children.Add(Ui.Text(label, 12.5, _ink.Text, FontWeight.SemiBold));
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
                option.CornerRadius = new CornerRadius(8);
                option.Padding = new Thickness(10, 7);
                option.Margin = new Thickness(0, 0, 6, 6);
                option.MaxWidth = 260;
                option.BorderThickness = new Thickness(1);
                refreshers.Add(() =>
                {
                    var selected = chosen.Contains(label);
                    option.BorderBrush = selected ? _ink.Accent : _ink.Hairline;
                    option.Background = selected ? Translucent(_ink.Accent, 0.12) : Brushes.Transparent;
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
                Background = _ink.Surface,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 5),
                Margin = new Thickness(0, -4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = other,
            });
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        send = Pill(L.T("Responder"), _ink.Accent, Ink.On(_ink.Accent), Submit);
        buttons.Children.Add(send);
        buttons.Children.Add(Pill(L.T("No responder"), _ink.Pill, _ink.Danger, () => Answer(block, PermissionAnswer.Deny)));
        panel.Children.Add(buttons);
        Refresh();
        return QuestionCard(panel, answered: false);
    }

    TextBlock Wrapped(string text, double size, IBrush brush, FontWeight weight = FontWeight.Normal)
    {
        var block = Ui.Text(text, size, brush, weight);
        block.TextWrapping = TextWrapping.Wrap;
        return block;
    }

    Border QuestionCard(Control content, bool answered) => new()
    {
        Margin = new Thickness(22, 4, 0, 4),
        CornerRadius = new CornerRadius(10),
        BorderBrush = answered ? _ink.Hairline : _ink.Warn,
        BorderThickness = new Thickness(1),
        Background = answered ? Brushes.Transparent : Translucent(_ink.Warn, 0.06),
        Padding = new Thickness(12, 10),
        Child = content,
    };

    /// <summary>Un cuadro de texto sin fondo ni borde propios, con la letra y los colores de la Terminal.</summary>
    void Plain(TextBox box)
    {
        box.Padding = new Thickness(0);
        box.FontFamily = Mono;
        box.Foreground = _ink.Text;
        box.CaretBrush = _ink.Accent;
        box.SelectionBrush = Translucent(_ink.Accent, 0.35);
        box.VerticalContentAlignment = VerticalAlignment.Center;
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
                     "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
            box.Resources[key] = Brushes.Transparent;
        box.Resources["TextControlBorderThemeThickness"] = new Thickness(0);
        box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        foreach (var key in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" })
            box.Resources[key] = _ink.Text;
        foreach (var key in new[] { "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused" })
            box.Resources[key] = _ink.Tertiary;
    }

    Control DoneView(AgentBlock block)
    {
        var parts = new List<string> { Duration(block.Duration) };
        if (block.Turns > 0) parts.Add(L.T("{0} turnos", block.Turns));
        if (block.Cost > 0) parts.Add(L.T("US$ {0:0.0000}", block.Cost));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 2, 0, 8) };
        row.Children.Add(Icon(block.Failed ? CrossIcon : CheckIcon, block.Failed ? _ink.Danger : Added, 13, 2.2));
        var text = Ui.Text(block.Failed && block.Text.Length > 0 ? block.Text : string.Join(" · ", parts), 12, block.Failed ? _ink.Danger : _ink.Tertiary);
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        row.Children.Add(text);
        return row;
    }

    static string Duration(TimeSpan d) => d.TotalSeconds < 60 ? L.T("{0:0.0} s", d.TotalSeconds) : L.T("{0} min {1} s", (int)d.TotalMinutes, d.Seconds);

    Control NoticeView(AgentBlock block)
    {
        var text = Ui.Text(block.Text, 12.5, _ink.Tertiary);
        text.TextWrapping = TextWrapping.Wrap;
        text.Margin = new Thickness(0, 6);
        return text;
    }

    Control StateIcon(StepState state)
    {
        Control icon = state switch
        {
            StepState.Running => Spinner(),
            StepState.Waiting => Icon(PauseIcon, _ink.Warn, 13, 2.2),
            StepState.Done => Icon(CheckIcon, Added, 13, 2.2),
            StepState.Failed => Icon(CrossIcon, _ink.Danger, 13, 2.2),
            _ => Icon(BlockedIcon, _ink.Danger, 13, 1.8),
        };
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        icon.VerticalAlignment = VerticalAlignment.Center;
        return icon;
    }

    IBrush Added => _ink.Dark ? new SolidColorBrush(Color.FromRgb(0x7F, 0xC8, 0x8A)) : new SolidColorBrush(Color.FromRgb(0x2E, 0x8B, 0x45));

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
        var block = Selectable(null, Ink.Sans, 14, _ink.Text);
        block.Inlines = inlines;
        return block;
    }

    Control Code(string code)
    {
        var text = Selectable(code.TrimEnd('\n'), Mono, 12.5, _ink.Text);
        text.TextWrapping = TextWrapping.NoWrap;
        return new Border
        {
            Background = _ink.Pill,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8),
            Child = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = text },
        };
    }

    SelectableTextBlock Selectable(string? text, FontFamily family, double size, IBrush brush) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = family,
        FontSize = size,
        LineHeight = size * _look.LineSpacing,
        Foreground = brush,
        SelectionBrush = Translucent(_ink.Accent, 0.35),
    };

    // ── Animaciones ─────────────────────────────────────────────────────────────────────────

    /// <summary>Aparece subiendo un poco y aclarándose (lo que llega no salta de golpe).</summary>
    static void Appear(Control view)
    {
        view.Opacity = 0;
        view.RenderTransform = TransformOperations.Parse("translateY(6px)");
        view.Transitions =
        [
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
        ];
        view.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            view.Opacity = 1;
            view.RenderTransform = TransformOperations.Identity;
        }, DispatcherPriority.Render);
    }

    /// <summary>Late suave mientras está vivo (pensando, trabajando); se detiene al salir de la pantalla.</summary>
    static void Pulse(Control control)
    {
        if (!Animate) return;
        Loop(control, new Animation
        {
            Duration = TimeSpan.FromMilliseconds(1100),
            IterationCount = IterationCount.Infinite,
            PlaybackDirection = PlaybackDirection.Alternate,
            Easing = new Avalonia.Animation.Easings.SineEaseInOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0.35) } },
            },
        });
    }

    /// <summary>Un arco que gira: el paso está trabajando.</summary>
    Control Spinner()
    {
        var spin = new RotateTransform();
        var arc = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M12,4 A8,8 0 1 1 4,12"),
            Stroke = _ink.Accent,
            StrokeThickness = 2.4,
            StrokeLineCap = PenLineCap.Round,
            Width = 24,
            Height = 24,
            RenderTransform = spin,
        };
        var box = new Viewbox { Width = 13, Height = 13, Child = arc };
        if (Animate)
            box.AttachedToVisualTree += (_, _) =>
            {
                var stop = new CancellationTokenSource();
                box.DetachedFromVisualTree += (_, _) => stop.Cancel();
                _ = new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(900),
                    IterationCount = IterationCount.Infinite,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(RotateTransform.AngleProperty, 0d) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(RotateTransform.AngleProperty, 360d) } },
                    },
                    // Se anima el arco (un Visual): Avalonia aplica el ángulo a su RenderTransform.
                }.RunAsync(arc, stop.Token);
            };
        return box;
    }

    static void Loop(Control control, Animation animation)
    {
        CancellationTokenSource? stop = null;
        void Start()
        {
            stop?.Cancel();
            stop = new CancellationTokenSource();
            _ = animation.RunAsync(control, stop.Token);
        }
        control.AttachedToVisualTree += (_, _) => Start();
        control.DetachedFromVisualTree += (_, _) => stop?.Cancel();
    }

    // ── Sesión nueva ────────────────────────────────────────────────────────────────────────

    /// <summary>Lo elegido en la sesión nueva (se recuerda lo último para la próxima).</summary>
    sealed class NewForm
    {
        public string Folder;
        public string? Account, Model, Effort;
        public string Mode;
        /// <summary>El agente de la sesión (AgentKinds); con un shell elegido no cuenta.</summary>
        public string Kind = AgentKinds.Claude;

        public NewForm()
        {
            var last = TerminalRecents.Load();
            Folder = last.Folders.FirstOrDefault(Directory.Exists) ?? global::Claupet.Account.Home;
            (Account, Model, Effort, Mode) = (last.Account, last.Model, last.Effort, last.Mode ?? "default");
        }
    }

    static string ModeLabel(string mode) => mode switch
    {
        "acceptEdits" => L.T("acepta ediciones"),
        "plan" => L.T("solo planifica"),
        "auto" => L.T("automático"),
        "default" or "" => L.T("pregunta antes"),
        _ => mode,
    };

    Control NewSession()
    {
        var accounts = Accounts();
        if (_form.Account is null || accounts.All(a => !global::Claupet.Account.SamePath(a.ConfigDir, _form.Account)))
            _form.Account = accounts.FirstOrDefault()?.ConfigDir;
        // Ancho fijo (hasta 520): no crece ni se achica con lo largo de la carpeta elegida.
        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(32, 28), HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(new TextBlock
        {
            Text = L.T("Sesión nueva"),
            FontFamily = Ink.Display,
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            Foreground = _ink.Text,
        });
        var about = Ui.Text(L.T("Claude Code en bloques, con su propia cuenta, modelo y esfuerzo. Puede tener varias abiertas a la vez, cada una con otra cuenta."), 13, _ink.Secondary);
        about.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(about);

        var folder = Ui.Text(_form.Folder, 13, _ink.Text, family: Mono);
        folder.TextTrimming = TextTrimming.PrefixCharacterEllipsis;
        panel.Children.Add(Field(L.T("Carpeta"), folder, async anchor =>
        {
            // Los proyectos vinculados primero y después las carpetas recientes.
            var recents = _projects.Folders.Concat(TerminalRecents.Load().Folders).Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
            var entries = recents.Select(f => new DropList.Entry(f, ShortPath(f), Selected: string.Equals(f, _form.Folder, StringComparison.OrdinalIgnoreCase)))
                .Append(new DropList.Entry("…", L.T("Elegir otra carpeta…"), Action: true)).ToList();
            DropList.Show(anchor, _ink, entries, async picked =>
            {
                if (picked is null) return;
                if (picked == "…")
                {
                    if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
                    var start = await storage.TryGetFolderFromPathAsync(Directory.Exists(_form.Folder) ? _form.Folder : global::Claupet.Account.Home);
                    var chosen = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L.T("Carpeta de la sesión"), SuggestedStartLocation = start });
                    if (chosen.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
                    picked = path;
                }
                _form.Folder = picked;
                folder.Text = picked;
            });
            await Task.CompletedTask;
        }));

        // Desde aquí, los campos que son solo de Claude Code (con un shell u otro agente se ocultan: TerminalPane.Shell.cs).
        var agentFields = panel.Children.Count;
        var accountLabel = Ui.Text(AccountLabel(accounts, _form.Account), 13, _ink.Text);
        panel.Children.Add(Field(L.T("Cuenta"), accountLabel, anchor =>
        {
            DropList.Show(anchor, _ink, accounts.Select(a => new DropList.Entry(a.ConfigDir, $"{a.Label} · {AssistantAccounts.StatusOf(a, new Dictionary<string, UsageSnapshot>()).Text}",
                Selected: global::Claupet.Account.SamePath(a.ConfigDir, _form.Account ?? ""))).ToList(), picked =>
            {
                if (picked is null) return;
                _form.Account = picked;
                accountLabel.Text = AccountLabel(accounts, picked);
            });
            return Task.CompletedTask;
        }));

        var model = Ui.Text(ModelLabel(_form.Model), 13, _ink.Text);
        panel.Children.Add(Field(L.T("Modelo"), model, anchor =>
        {
            DropList.Show(anchor, _ink, Launcher.Models.Select(m => new DropList.Entry(m, m, m == _form.Model))
                .Prepend(new DropList.Entry("", L.T("modelo por defecto"), _form.Model is null)).ToList(), picked =>
            {
                if (picked is null) return;
                _form.Model = picked.Length == 0 ? null : picked;
                model.Text = ModelLabel(_form.Model);
            });
            return Task.CompletedTask;
        }));

        var effort = Ui.Text(_form.Effort ?? L.T("esfuerzo por defecto"), 13, _ink.Text);
        panel.Children.Add(Field(L.T("Esfuerzo"), effort, anchor =>
        {
            DropList.Show(anchor, _ink, Launcher.Efforts.Select(e => new DropList.Entry(e, e, e == _form.Effort))
                .Prepend(new DropList.Entry("", L.T("esfuerzo por defecto"), _form.Effort is null)).ToList(), picked =>
            {
                if (picked is null) return;
                _form.Effort = picked.Length == 0 ? null : picked;
                effort.Text = _form.Effort ?? L.T("esfuerzo por defecto");
            });
            return Task.CompletedTask;
        }));

        var mode = Ui.Text(ModeLabel(_form.Mode), 13, _ink.Text);
        panel.Children.Add(Field(L.T("Permisos"), mode, anchor =>
        {
            DropList.Show(anchor, _ink, TerminalAgent.Modes.Select(m => new DropList.Entry(m, ModeLabel(m), m == _form.Mode)).ToList(), picked =>
            {
                if (picked is null) return;
                _form.Mode = picked;
                mode.Text = ModeLabel(picked);
            });
            return Task.CompletedTask;
        }));

        KindField(panel, about, agentFields);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(Pill(L.T("Abrir sesión"), _ink.Accent, Ink.On(_ink.Accent), () => Open(accounts)));
        if (_tabs.Count > 0)
            buttons.Children.Add(Pill(L.T("Cancelar"), _ink.Pill, _ink.Text, () =>
            {
                _creating = false;
                _current ??= _tabs[0];
                Build();
                RenderList();
            }));
        panel.Children.Add(buttons);
        var scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroll.SizeChanged += (_, e) => panel.Width = Math.Clamp(e.NewSize.Width - 64, 0, 520);
        return scroll;
    }

    /// <summary>Una ruta larga se corta por el principio: lo que distingue es el final (el nombre de la carpeta).</summary>
    static string ShortPath(string path) => path.Length <= 56 ? path : "…" + path[^55..];

    static string AccountLabel(IReadOnlyList<Account> accounts, string? dir) =>
        accounts.FirstOrDefault(a => global::Claupet.Account.SamePath(a.ConfigDir, dir ?? ""))?.Label ?? L.T("cuenta por defecto");

    /// <summary>Las cuentas con sesión iniciada: las de Ajustes y las carpetas ~/.claude* que tengan credenciales.</summary>
    static IReadOnlyList<Account> Accounts()
    {
        var settings = SettingsStore.Load();
        SettingsStore.DetectNew(settings);
        return settings.Accounts.Where(a => Credentials.Exists(a.ConfigDir)).ToList();
    }

    void Open(IReadOnlyList<Account> accounts)
    {
        if (_newShell is { } shell)
        {
            OpenShell(shell);
            return;
        }
        if (!Directory.Exists(_form.Folder)) return;
        var claude = _form.Kind == AgentKinds.Claude;
        var account = claude ? accounts.FirstOrDefault(a => global::Claupet.Account.SamePath(a.ConfigDir, _form.Account ?? "")) : null;
        // Codex, Gemini y Copilot abren con lo suyo: el modelo y lo demás se eligen en la sesión, con lo que dicen tener.
        var options = claude
            ? new AgentOptions(_form.Folder, account?.ConfigDir, _form.Model, _form.Effort, _form.Mode)
            : new AgentOptions(_form.Folder, Kind: _form.Kind);
        if (claude) TerminalRecents.Remember(options);
        else
        {
            var last = TerminalRecents.Load();
            TerminalRecents.Remember(new AgentOptions(_form.Folder, last.Account, last.Model, last.Effort, last.Mode));
        }
        LinkProject(options.Folder);
        // Una copia de prueba (--preview) no le pasa los permisos a la isla de la app de siempre.
        var agent = new TerminalAgent(options, action => Dispatcher.UIThread.Post(action)) { AskIsland = !Program.Args.Contains("--preview") };
        var tab = new Tab(agent, claude ? account?.Label ?? L.T("cuenta por defecto") : AgentKinds.Label(_form.Kind));
        agent.Changed += changed => OnChanged(tab, changed);
        _tabs.Add(tab);
        _current = tab;
        _creating = false;
        _input.Text = "";
        Build();
        RenderList();
        TabsChanged?.Invoke();
        agent.Start();
        _input.Focus();
    }

    /// <summary>Una fila de la sesión nueva: la etiqueta a la izquierda y lo elegido a la derecha, que abre su lista.</summary>
    Control Field(string label, TextBlock value, Func<Control, Task> pick)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*,Auto") };
        row.Children.Add(Ui.Text(label, 13, _ink.Secondary));
        value.TextTrimming = value.TextTrimming == TextTrimming.None ? TextTrimming.CharacterEllipsis : value.TextTrimming;
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        var chevron = Ui.Chevron(_ink.Tertiary);
        chevron.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(chevron, 2);
        row.Children.Add(chevron);
        Border? press = null;
        press = Ui.Press(row, () => _ = pick(press!), (b, on) => b.Background = on ? _ink.Track : _ink.Pill, label);
        press.Background = _ink.Pill;
        press.CornerRadius = new CornerRadius(10);
        press.Padding = new Thickness(14, 0, 12, 0);
        press.Height = 40;
        return press;
    }

    // ── Piezas ──────────────────────────────────────────────────────────────────────────────

    static Viewbox Icon(string path, IBrush brush, double size, double thickness) => new()
    {
        Width = size,
        Height = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(path),
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 24,
            Height = 24,
        },
    };

    Border Round(string path, string name, Action onClick)
    {
        var button = Ui.Press(Icon(path, _ink.Secondary, 14, 1.8), onClick, (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, name);
        button.Width = 30;
        button.Height = 30;
        button.CornerRadius = new CornerRadius(999);
        ToolTip.SetTip(button, name);
        return button;
    }

    Border Pill(string text, IBrush background, IBrush foreground, Action onClick)
    {
        var pill = Ui.Press(Ui.Text(text, 12.5, foreground, FontWeight.SemiBold), onClick, (b, on) => b.Opacity = on ? 0.86 : 1, text);
        pill.Background = background;
        pill.Height = 28;
        pill.Padding = new Thickness(12, 0);
        pill.CornerRadius = new CornerRadius(999);
        return pill;
    }

    static IBrush Translucent(IBrush brush, double opacity) =>
        brush is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, opacity) : brush;
}

/// <summary>Lo último que se eligió en la sesión nueva de la Terminal (carpetas recientes, cuenta, modelo, esfuerzo, permisos).</summary>
sealed class TerminalRecents
{
    public List<string> Folders { get; set; } = [];
    public string? Account { get; set; }
    public string? Model { get; set; }
    public string? Effort { get; set; }
    public string? Mode { get; set; }

    static string FilePath => DataFolder.In("terminal-recientes.json");

    public static TerminalRecents Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<TerminalRecents>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception) { }
        return new();
    }

    public static void Remember(AgentOptions o)
    {
        var recents = Load();
        recents.Folders.RemoveAll(f => string.Equals(f, o.Folder, StringComparison.OrdinalIgnoreCase));
        recents.Folders.Insert(0, o.Folder);
        if (recents.Folders.Count > 12) recents.Folders.RemoveRange(12, recents.Folders.Count - 12);
        recents.Account = o.ConfigDir;
        recents.Model = o.Model;
        recents.Effort = o.Effort;
        recents.Mode = o.PermissionMode;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(recents));
        }
        catch (Exception) { }
    }
}
