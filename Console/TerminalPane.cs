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

    internal sealed class Tab(TerminalAgent agent, string account)
    {
        public TerminalAgent Agent { get; } = agent;
        public string Account { get; } = account;
        public string? Branch { get; set; } = GitBranch.Of(agent.Options.Folder);
        public string Project => Path.GetFileName(Agent.Options.Folder.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : Agent.Options.Folder;
        public string Draft { get; set; } = "";
        /// <summary>Las imágenes pegadas en la caja («[Imagen #1]»…): van con el pedido si su marca sigue en el texto.</summary>
        public List<AgentImage> Images { get; } = [];
        /// <summary>El archivo que se ve en la tarjeta flotante (y su línea), si hay uno abierto (TerminalPane.Viewer.cs).</summary>
        public (string Path, int? Line)? Preview { get; set; }
        /// <summary>Los archivos fijados en el panel de la derecha, con su ícono junto a Ramas, Cambios y Procesos.</summary>
        public List<string> Docked { get; } = [];
        /// <summary>El archivo fijado que se mira en el panel (en vez de la vista de PanelTab); null, ninguno.</summary>
        public string? SideFile { get; set; }
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
    readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    readonly TextBox _input = new();
    readonly StackPanel _list = new() { Spacing = 1 };
    Ink _ink = Ink.For(false);
    Tab? _current;
    bool _creating = true;
    Border _send = new();
    TextBlock _context = new();
    Border _contextFill = new();
    Control _contextBox = new Panel();
    Border _interrupt = new();

    /// <summary>La lista de pestañas cambió (para la columna de la izquierda).</summary>
    public event Action? TabsChanged;

    /// <summary>Cambió el ancho de la lista (con descripción o solo íconos): la ventana vuelve a armar la columna.</summary>
    public event Action? ListChanged;

    public TerminalPane()
    {
        _zoom.Child = _blocks;
        _scroll.Content = new Border { Child = _zoom, Padding = new Thickness(20, 12, 20, 16) };
        _input.AddHandler(KeyDownEvent, OnInputKey, RoutingStrategies.Tunnel);
        // Ctrl+T: la pestaña nueva, desde cualquier lado de la Terminal (la pantalla completa se lo pasa al programa).
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.T || e.KeyModifiers != KeyModifiers.Control) return;
            e.Handled = true;
            ShowNew();
        });
        ApplyLook();
        Build();
        SizeChanged += (_, _) => FitShellSoon();
    }

    bool _fitPending;

    /// <summary>Al cambiar el tamaño el panel se acomoda varias veces seguidas: la consola cambia una vez, al final.</summary>
    void FitShellSoon()
    {
        if (_fitPending || _current?.Shell is null) return;
        _fitPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _fitPending = false;
            FitShell();
        }, DispatcherPriority.Background);
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
        if (_screen is { } screen) screen.Focus();
        else if (!_creating) _input.Focus();
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
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = _ink.Text,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var add = Round(PlusIcon, L.T("Nueva pestaña (Ctrl+T)"), ShowNew);
        add.Width = 28;
        add.Height = 28;
        add.CornerRadius = new CornerRadius(8);
        _addButton = add;
        var buttons = HeaderButtons(add);
        Grid.SetColumn(buttons, _look.IconsOnly ? 0 : 1);
        if (_look.IconsOnly) Grid.SetColumnSpan(buttons, 2);
        bar.Children.Add(buttons);
        return bar;
    }

    /// <summary>Las pestañas, agrupadas por proyecto.</summary>
    public Control List => _list;

    /// <summary>Una pestaña en la lista, en una línea: su marca, el nombre, lo que la describe (más chico) y su punto.</summary>
    Control TabItem(Tab tab)
    {
        var current = tab == _current && !_creating;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,Auto"), Height = 34 };
        var claude = tab.Shell is null && tab.Agent.Options.Kind == AgentKinds.Claude;
        var kind = Ui.Text(KindMark(tab), 11, claude ? _ink.Accent : _ink.Tertiary, family: Mono);
        kind.HorizontalAlignment = HorizontalAlignment.Center;
        kind.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(kind);
        var text = new TextBlock
        {
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 6, 0),
            FontFamily = Ink.Sans,
            Inlines =
            [
                new Run(Title(tab)) { FontSize = 13.5, FontWeight = FontWeight.Medium, Foreground = _ink.Text },
                new Run("  " + Describe(tab)) { FontSize = 12, Foreground = _ink.Tertiary },
            ],
        };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var dot = new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(4, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Dot(tab),
        };
        if (Busy(tab)) Pulse(dot);
        Grid.SetColumn(dot, 2);
        row.Children.Add(dot);
        var item = Ui.Press(row, () => Select(tab), current ? null : (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, Title(tab));
        item.Padding = new Thickness(8, 0);
        item.CornerRadius = new CornerRadius(9);
        if (current) item.Background = _ink.Pill;
        ToolTip.SetTip(item, $"{Title(tab)} · {Describe(tab)}");
        return item;
    }

    /// <summary>Una pestaña en la lista de íconos: el de Claude Code con el punto de su estado encima.</summary>
    Control TabIcon(Tab tab)
    {
        var current = tab == _current && !_creating;
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

    internal void Select(Tab tab)
    {
        if (_current is { } previous) previous.Draft = _input.Text ?? "";
        _current = tab;
        _creating = false;
        _input.Text = tab.Draft;
        Build();
        RenderList();
        _input.Focus();
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
        _screen = null;
        _blocks.Children.Clear();
        // Con un fondo propio, la Terminal lo pinta (tapa la escena del tema); si no, se ve el de la ventana.
        Background = _look.OwnBackground ? _ink.Surface : null;
        if (_creating || _current is null)
        {
            Children.Add(EmptyState());
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
        AddPreview(_current);
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

    void OnInputKey(object? sender, KeyEventArgs e)
    {
        if (_current?.Shell is { } shell && !_creating)
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
        // Ctrl+V: con una imagen en el portapapeles, va con el pedido («[Imagen #1]»); si no, se pega el texto.
        if (e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control && _current is { Shell: null } pasting)
        {
            e.Handled = true;
            _ = PasteIntoComposerAsync(pasting);
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
        if (_current is not { } tab) return;
        var text = _input.Text?.Trim() ?? "";
        if (text.Length == 0) return;
        // Los comandos de la Terminal: /resume abre el historial, /clear y /new una pestaña nueva como esta.
        if (Command(tab, text))
        {
            _input.Text = "";
            return;
        }
        if (!tab.Agent.Running) return;
        _input.Text = "";
        var images = tab.Images.Where((_, i) => text.Contains($"[Imagen #{i + 1}]", StringComparison.Ordinal)).ToList();
        tab.Images.Clear();
        tab.Agent.Send(text, images);
        Dispatcher.UIThread.Post(_scroll.ScrollToEnd, DispatcherPriority.Background);
    }

    /// <summary>Llegaron eventos de una sesión: se cambian solo sus bloques (y la lista, si cambió su estado).</summary>
    void OnChanged(Tab tab, IReadOnlyList<AgentBlock> changed)
    {
        // Al terminar un turno pudo cambiar el repositorio (archivos editados, un commit).
        if (changed.Any(b => b.Kind == AgentBlockKind.Done))
        {
            RefreshGit(tab.Agent.Options.Folder, force: true);
            if (tab == _current && !_creating && _projects.PanelOpen && _projects.PanelTab != "procesos") LoadSide(tab);
        }
        // La lista se rehace solo si cambió lo que muestra de la pestaña: con cada pedazo de texto la rehacía entera.
        var listChanged = ListStateChanged(tab);
        if (tab != _current || _creating)
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

    static string ModeLabel(string mode) => mode switch
    {
        "acceptEdits" => L.T("acepta ediciones"),
        "plan" => L.T("solo planifica"),
        "auto" => L.T("automático"),
        "default" or "" => L.T("pregunta antes"),
        _ => mode,
    };

    /// <summary>Una ruta larga se corta por el principio: lo que distingue es el final (el nombre de la carpeta).</summary>
    static string ShortPath(string path) => path.Length <= 56 ? path : "…" + path[^55..];

    /// <summary>Las cuentas con sesión iniciada: las de Ajustes y las carpetas ~/.claude* que tengan credenciales.</summary>
    static IReadOnlyList<Account> Accounts()
    {
        var settings = SettingsStore.Load();
        SettingsStore.DetectNew(settings);
        return settings.Accounts.Where(a => Credentials.Exists(a.ConfigDir)).ToList();
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
    /// <summary>Lo último que se abrió: «agente:claude», «agente:codex»… o el id de una terminal («powershell», «bash»…).</summary>
    public string? Kind { get; set; }

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

    public static void Remember(AgentOptions o, string? kind = null)
    {
        var recents = Load();
        if (kind is not null) recents.Kind = kind;
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
