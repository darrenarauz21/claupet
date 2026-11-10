namespace Claupet;

using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;

/// <summary>Los íconos de la mesa de diseño.</summary>
static class DeskIcons
{
    public const string Desk = "M3.5,5.5 H11 V12 H3.5 Z M13,5.5 H20.5 V9.5 H13 Z M13,11.5 H20.5 V18.5 H13 Z M3.5,14 H11 V18.5 H3.5 Z";
    public const string Back = "M15,18 L9,12 L15,6";
    public const string Desktop = "M3,4.5 H21 V16.5 H3 Z M8,20 H16 M12,16.5 V20";
    public const string Tablet = "M5,3 H19 V21 H5 Z M11,18 H13";
    public const string Phone = "M7,3 H17 V21 H7 Z M11,18 H13";
    public const string Minus = "M6,12 H18";
    public const string Split = "M3.5,4.5 H20.5 V19.5 H3.5 Z M14,4.5 V19.5";
    public const string Pointer = "M5,4 L11.5,20 L13.8,13.8 L20,11.5 Z";
    public const string Chat = "M20,11.5 A8,8 0 0 1 8.3,18.6 L4,20 L5.4,15.8 A8,8 0 1 1 20,11.5 Z";
    public const string Chevron = "M6,9 L12,15 L18,9";
    public const string Arc = "M12,4 A8,8 0 0 1 20,12";
}

/// <summary>
/// La mesa de diseño: las pantallas del canvas (sus secciones HTML y SVG) en una mesa que se mueve y se acerca
/// (CanvasDesk, en una CanvasLivePage que ocupa todo), y encima, flotando sin mover nada, el chat con la sesión ligada:
/// se le pide un ajuste (a una pantalla o a un elemento señalado) y el agente lo hace con canvas_edit sin salir de aquí.
/// </summary>
sealed partial class CanvasPane
{
    /// <summary>Se abrió o se cerró la mesa: la Console esconde o vuelve a mostrar la lista de canvas.</summary>
    public event Action<bool>? MesaChanged;

    readonly CanvasSessions? _sessions;
    readonly Grid _deskArea = new() { IsVisible = false };
    readonly Border _chatBox = new();
    bool _mesa, _chatOpen = true, _rail = true, _picking;
    CanvasLivePage? _desk;
    string? _deskDoc, _deskSel;
    int _deskVersion, _deskWidth = 1280;
    string _deskView = "desk";
    double _deskZoom = 1;
    TextBlock? _zoomText;
    (string Id, string Element, string Path)? _picked;
    /// <summary>La sesión ligada a cada canvas (mientras la Console está abierta).</summary>
    readonly Dictionary<string, TerminalAgent> _linkedSession = [];
    TerminalAgent? _watched;
    /// <summary>La pantalla que el agente está cambiando por un pedido del chat (brilla en la mesa).</summary>
    string? _busyScreen;
    readonly StackPanel _chatList = new() { Spacing = 10 };
    readonly ScrollViewer _chatScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    readonly TextBox _chatInput = new();
    readonly DispatcherTimer _chatTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    readonly HashSet<AgentBlock> _chatSeen = [];
    readonly Dictionary<AgentBlock, (string Text, Control View)> _chatSay = [];

    public bool InMesa => _mesa;

    static bool DeskAvailable => CanvasLivePage.Available;

    void InitDesk()
    {
        Grid.SetRow(_deskArea, 1);
        _chatBox.HorizontalAlignment = HorizontalAlignment.Right;
        _chatBox.Width = 340;
        _chatBox.Margin = new Thickness(12);
        _chatBox.CornerRadius = new CornerRadius(16);
        _chatBox.BorderThickness = new Thickness(1);
        _chatBox.ClipToBounds = true;
        _chatBox.RenderTransformOrigin = new RelativePoint(1, 0, RelativeUnit.Relative);
        _chatBox.Transitions =
        [
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(320), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
        ];
        _deskArea.Children.Add(_chatBox);
        _chatTimer.Tick += (_, _) => { _chatTimer.Stop(); if (_mesa) RenderChatList(); };
        _chatInput.AcceptsReturn = true;
        _chatInput.TextWrapping = TextWrapping.Wrap;
        _chatInput.MinHeight = 40;
        _chatInput.MaxHeight = 140;
        _chatInput.FontSize = 13.5;
        _chatInput.BorderThickness = new Thickness(0);
        _chatInput.Padding = new Thickness(4, 4);
        _chatInput.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                e.Handled = true;
                SendFromChat();
            }
            else if (e.Key == Key.Escape && _picked is not null)
            {
                _picked = null;
                RenderChat();
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        if (_sessions is not null) _sessions.Changed += () => { if (_mesa) RenderChat(); };
    }

    /// <summary>Abre la mesa con las pantallas del canvas que se ve.</summary>
    public void EnterMesa()
    {
        if (_doc is not { } doc || !CanvasDesk.HasScreens(doc) || !DeskAvailable || _liveOff) return;
        SaveEditing();
        LeavePast();
        if (_history) ToggleHistory();
        _mesa = true;
        _deskSel = CanvasDesk.Screens(doc).First().Id;
        MesaChanged?.Invoke(true);
        Render();
    }

    public void LeaveMesa()
    {
        if (!_mesa) return;
        _mesa = false;
        DropDesk();
        Watch(null);
        MesaChanged?.Invoke(false);
        Render();
    }

    void DropDesk()
    {
        if (_desk is { } desk) _deskArea.Children.Remove(desk);
        _desk = null;
        _deskDoc = null;
        _picking = false;
        _picked = null;
        _busyScreen = null;
    }

    /// <summary>¿Se ve la mesa ahora? (si el canvas ya no tiene pantallas o no hay WebView2, vuelve al documento)</summary>
    bool ShowingMesa()
    {
        if (!_mesa) return false;
        if (_doc is { } doc && _past is null && !_liveOff && DeskAvailable && CanvasDesk.HasScreens(doc)) return true;
        _mesa = false;
        DropDesk();
        Watch(null);
        Dispatcher.UIThread.Post(() => MesaChanged?.Invoke(false));
        return false;
    }

    void RenderDesk(CanvasDoc doc)
    {
        if (_deskSel is null || doc.Section(_deskSel) is not { } s || !CanvasDesk.IsScreen(s)) _deskSel = CanvasDesk.Screens(doc).First().Id;
        if (_desk is null || _deskDoc != doc.Id)
        {
            DropDesk();
            if (CreateDesk(doc) is not { } page) return;
            _desk = page;
            _deskDoc = doc.Id;
            _deskVersion = doc.Version;
            _deskArea.Children.Insert(0, page);
        }
        else if (_deskVersion != doc.Version)
        {
            _deskVersion = doc.Version;
            _desk.Post(CanvasDesk.Update(doc));
        }
        RenderChat();
    }

    CanvasLivePage? CreateDesk(CanvasDoc doc)
    {
        var html = CanvasDesk.Page(doc, DeskTheme(), _deskSel);
        string file;
        try
        {
            var dir = System.IO.Path.Combine(_store.Root, "vista");
            Directory.CreateDirectory(dir);
            file = System.IO.Path.Combine(dir, $"{doc.Id}-mesa.html");
            File.WriteAllText(file, html, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex);
            return null;
        }
        var page = new CanvasLivePage(file, html, 0, PageBackground(), () => _overlaysOn, fill: true);
        page.Message += OnDeskMessage;
        page.Failed += () =>
        {
            if (_liveOff) return;
            _liveOff = true;
            Render();
        };
        return page;
    }

    /// <summary>Los colores de la mesa: los del tema, con los puntitos un poco más claros (u oscuros) que el fondo.</summary>
    CanvasDesk.Theme DeskTheme()
    {
        static string Css(IBrush brush, double alpha = 1)
        {
            var c = (brush as ISolidColorBrush)?.Color ?? Avalonia.Media.Colors.Gray;
            return FormattableString.Invariant($"rgba({c.R},{c.G},{c.B},{Math.Round(c.A / 255.0 * alpha, 3)})");
        }
        return new CanvasDesk.Theme(Css(_ink.Surface), Css(_ink.Raised), Css(_ink.Raised), Css(_ink.Pill), Css(_ink.Hairline), Css(_ink.Text),
            Css(_ink.Secondary), Css(_ink.Tertiary), Css(_ink.Accent), Css(_ink.Text, _ink.Dark ? 0.17 : 0.2), _ink.Dark);
    }

    void PostDesk(object message)
    {
        if (_desk is { } desk) desk.Post(JsonSerializer.Serialize(message));
    }

    void OnDeskMessage(JsonElement m)
    {
        if (_doc is not { } doc) return;
        string? Str(string name) => m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        switch (Str("t"))
        {
            case "ready":
                // Si la página se volvió a cargar, puede traer pantallas viejas: van las de ahora y lo elegido.
                _desk?.Post(CanvasDesk.Update(doc));
                if (!_rail) PostDesk(new { t = "rail", on = false });
                if (_deskView != "desk") PostDesk(new { t = "view", v = _deskView });
                if (_busyScreen is { } busy) PostDesk(new { t = "busy", id = busy, on = true });
                break;
            case "sel" when Str("id") is { } id && doc.Section(id) is not null:
                _deskSel = id;
                if (m.TryGetProperty("w", out var w) && w.TryGetInt32(out var width)) _deskWidth = width;
                _bar.Child = Bar();
                RenderChat();
                break;
            case "zoom" when m.TryGetProperty("z", out var z) && z.TryGetDouble(out var zoom):
                _deskZoom = zoom;
                if (_zoomText is not null) _zoomText.Text = FormattableString.Invariant($"{Math.Round(zoom * 100)}%");
                break;
            case "picking":
                _picking = m.TryGetProperty("on", out var on) && on.ValueKind == JsonValueKind.True;
                RenderChat();
                break;
            case "pick" when Str("id") is { } id && doc.Section(id) is not null:
                _picking = false;
                _picked = (id, Str("el") ?? "", Str("path") ?? "");
                _deskSel = id;
                if (!_chatOpen) ToggleChat();
                RenderChat();
                Dispatcher.UIThread.Post(() => _chatInput.Focus(), DispatcherPriority.Background);
                break;
            case "key" when Str("k") == "j":
                if (!_chatOpen) ToggleChat();
                Dispatcher.UIThread.Post(() => _chatInput.Focus(), DispatcherPriority.Background);
                break;
        }
    }

    // ── La barra de la mesa ─────────────────────────────────────────────────────────────

    Control MesaBar(CanvasDoc doc)
    {
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*,Auto"), Height = 56, Margin = new Thickness(12, 0, 14, 0) };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(IconButton(DeskIcons.Back, L.T("Volver al documento"), LeaveMesa));
        left.Children.Add(IconButton(CanvasIcons.Side, L.T("Pantallas"), () =>
        {
            _rail = !_rail;
            PostDesk(new { t = "rail", on = _rail });
            _bar.Child = Bar();
        }, on: _rail));
        var title = Ui.Text(doc.Title, 15, _ink.Text, FontWeight.SemiBold, Ink.Display);
        title.Margin = new Thickness(8, 0, 6, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.MaxWidth = 300;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        left.Children.Add(title);
        _versionPill = new Border
        {
            Background = _ink.Pill,
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(8, 1),
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = RelativePoint.Center,
            Child = Ui.Text("v" + doc.Version, 11.5, _ink.Tertiary),
        };
        left.Children.Add(_versionPill);
        bar.Children.Add(left);

        var center = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var device = Array.IndexOf(CanvasDesk.Widths, _deskWidth);
        center.Children.Add(Pills(
            [(DeskIcons.Desktop, L.T("Escritorio")), (DeskIcons.Tablet, L.T("Tableta")), (DeskIcons.Phone, L.T("Móvil"))], device, i =>
            {
                _deskWidth = CanvasDesk.Widths[i];
                PostDesk(new { t = "device", w = _deskWidth });
            }));
        center.Children.Add(new Border { Width = 1, Height = 20, Background = _ink.Hairline, Margin = new Thickness(2, 0) });
        var zoom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        zoom.Children.Add(IconButton(DeskIcons.Minus, L.T("Alejar"), () => PostDesk(new { t = "zoom", f = 0.8 }), size: 30, glyph: 15));
        _zoomText = Ui.Text(FormattableString.Invariant($"{Math.Round(_deskZoom * 100)}%"), 12.5, _ink.Secondary);
        _zoomText.HorizontalAlignment = HorizontalAlignment.Center;
        var fit = Ui.Press(_zoomText, () => PostDesk(new { t = "fit" }), (b, hover) => b.Background = hover ? _ink.Pill : Brushes.Transparent, L.T("Ajustar todo") + " · Shift+1");
        fit.Width = 50;
        fit.Padding = new Thickness(0, 4);
        fit.CornerRadius = new CornerRadius(7);
        ToolTip.SetTip(fit, L.T("Ajustar todo") + " · Shift+1");
        zoom.Children.Add(fit);
        zoom.Children.Add(IconButton(CanvasIcons.Plus, L.T("Acercar"), () => PostDesk(new { t = "zoom", f = 1.25 }), size: 30, glyph: 15));
        center.Children.Add(zoom);
        Grid.SetColumn(center, 2);
        bar.Children.Add(center);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        string[] views = ["desk", "split", "code"];
        right.Children.Add(Pills([(CanvasIcons.Eye, L.T("Mesa")), (DeskIcons.Split, L.T("Mesa y código")), (CanvasIcons.Code, L.T("Código"))],
            Array.IndexOf(views, _deskView), i =>
            {
                _deskView = views[i];
                PostDesk(new { t = "view", v = _deskView });
            }));
        right.Children.Add(new Border { Width = 6 });
        right.Children.Add(IconButton(CanvasIcons.Play, L.T("Ver en el navegador"), () => Preview(doc, _deskSel is { } id ? doc.Section(id) : null)));
        Border? export = null;
        export = IconButton(CanvasIcons.Export, L.T("Exportar"), () => ExportPopup(export!));
        right.Children.Add(export);
        right.Children.Add(IconButton(DeskIcons.Chat, L.T("Chat con la sesión") + " · Ctrl+J", ToggleChat, on: _chatOpen));
        Grid.SetColumn(right, 4);
        bar.Children.Add(right);
        PaintBarLine();
        return bar;
    }

    /// <summary>Botones de solo ícono en una píldora, con una perilla que se desliza al elegido (-1: ninguno).</summary>
    Control Pills(IReadOnlyList<(string Icon, string Name)> items, int current, Action<int> pick)
    {
        const double W = 32;
        var host = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(new Border { Background = _ink.Pill, CornerRadius = new CornerRadius(999) });
        var knob = new Border
        {
            Width = W, Height = 26, Margin = new Thickness(3), CornerRadius = new CornerRadius(999), Background = _ink.Raised,
            HorizontalAlignment = HorizontalAlignment.Left, BoxShadow = Shadow(soft: true), Opacity = current < 0 ? 0 : 1,
            RenderTransform = TransformOperations.Parse(FormattableString.Invariant($"translateX({Math.Max(0, current) * W}px)")),
            Transitions =
            [
                new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(280), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) },
            ],
        };
        host.Children.Add(knob);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3) };
        var glyphs = new List<Viewbox>();
        for (var i = 0; i < items.Count; i++)
        {
            var index = i;
            var glyph = Glyph(items[i].Icon, i == current ? _ink.Text : _ink.Tertiary, 15);
            glyphs.Add(glyph);
            var button = Ui.Press(glyph, () =>
            {
                knob.Opacity = 1;
                knob.RenderTransform = TransformOperations.Parse(FormattableString.Invariant($"translateX({index * W}px)"));
                for (var g = 0; g < glyphs.Count; g++) ((Path)glyphs[g].Child!).Stroke = g == index ? _ink.Text : _ink.Tertiary;
                pick(index);
            }, null, items[i].Name);
            button.Width = W;
            button.Height = 26;
            button.Background = Brushes.Transparent;
            ToolTip.SetTip(button, items[i].Name);
            row.Children.Add(button);
        }
        host.Children.Add(row);
        return host;
    }

    // ── El chat con la sesión ligada ────────────────────────────────────────────────────

    void ToggleChat()
    {
        _chatOpen = !_chatOpen;
        PaintChatBox();
        _bar.Child = Bar();
        if (_chatOpen) Dispatcher.UIThread.Post(() => _chatInput.Focus(), DispatcherPriority.Background);
    }

    /// <summary>
    /// Abre o cierra el chat con transición. Mientras se ve, la mesa (una ventana nativa) le deja el hueco; al cerrarse,
    /// el hueco se tapa recién cuando terminó de irse, para que no se corte la animación.
    /// </summary>
    void PaintChatBox()
    {
        _chatBox.Background = _ink.Raised;
        _chatBox.BorderBrush = _ink.Hairline;
        _chatBox.BoxShadow = Shadow();
        _chatBox.Opacity = _chatOpen ? 1 : 0;
        _chatBox.IsHitTestVisible = _chatOpen;
        _chatBox.RenderTransform = TransformOperations.Parse(_chatOpen ? "translateX(0px) scale(1)" : "translateX(16px) scale(0.97)");
        if (_chatOpen)
        {
            if (!_overlaysOn.Contains(_chatBox)) _overlaysOn.Add(_chatBox);
            RefitPages();
        }
        else DispatcherTimer.RunOnce(() =>
        {
            if (_chatOpen) return;
            _overlaysOn.Remove(_chatBox);
            RefitPages();
        }, TimeSpan.FromMilliseconds(330));
    }

    void RefitPages()
    {
        foreach (var page in _live.Values) page.Refit();
        _desk?.Refit();
    }

    /// <summary>La sesión ligada al canvas: la elegida, o la de su carpeta, o la primera que corre.</summary>
    CanvasSession? Linked()
    {
        if (_sessions is null || _doc is not { } doc) return null;
        var all = _sessions.List();
        if (_linkedSession.TryGetValue(doc.Id, out var agent) && all.FirstOrDefault(s => s.Agent == agent) is { } chosen) return chosen;
        var pick = all.FirstOrDefault(s => doc.Folder is { } f && global::Claupet.Account.SamePath(s.Folder, f)) ?? all.FirstOrDefault();
        if (pick is not null) _linkedSession[doc.Id] = pick.Agent;
        return pick;
    }

    void Watch(TerminalAgent? agent)
    {
        if (_watched == agent) return;
        if (_watched is not null) _watched.Changed -= OnAgentChanged;
        _watched = agent;
        _chatSeen.Clear();
        _chatSay.Clear();
        if (agent is not null)
        {
            agent.Changed += OnAgentChanged;
            foreach (var block in agent.Transcript.Blocks) _chatSeen.Add(block); // lo de antes no entra animado
        }
    }

    void OnAgentChanged(IReadOnlyList<AgentBlock> changed)
    {
        if (!_mesa) return;
        if (_watched is { } agent && !agent.Transcript.Busy && _busyScreen is { } busy)
        {
            _busyScreen = null;
            PostDesk(new { t = "busy", id = busy, on = false });
        }
        if (!_chatTimer.IsEnabled) _chatTimer.Start();
    }

    void RenderChat()
    {
        if (!_mesa || _doc is not { } doc) return;
        // La caja se mueve al rehacer el chat (un agente cambió el canvas mientras se escribía): no pierde el foco.
        var typing = _chatInput.IsFocused;
        var caret = _chatInput.CaretIndex;
        PaintChatBox();
        var session = Linked();
        Watch(session?.Agent);
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        layout.Children.Add(ChatHeader(doc, session));
        _chatScroll.Content = _chatList;
        _chatScroll.Padding = new Thickness(14, 12, 14, 8);
        if (_chatScroll.Parent is Panel old) old.Children.Remove(_chatScroll);
        Grid.SetRow(_chatScroll, 1);
        layout.Children.Add(_chatScroll);
        var input = ChatInput(doc, session);
        Grid.SetRow(input, 2);
        layout.Children.Add(input);
        _chatBox.Child = layout;
        RenderChatList();
        if (typing) Dispatcher.UIThread.Post(() =>
        {
            _chatInput.Focus();
            _chatInput.CaretIndex = Math.Min(caret, _chatInput.Text?.Length ?? 0);
        }, DispatcherPriority.Background);
    }

    Control ChatHeader(CanvasDoc doc, CanvasSession? session)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(12, 10, 8, 10) };
        if (session is null)
        {
            var spark = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(10), Background = _ink.Pill, Child = Glyph(CanvasIcons.Spark, _ink.Tertiary, 15) };
            row.Children.Add(spark);
            var none = Ui.Text(L.T("Sin sesión ligada"), 13.5, _ink.Secondary, FontWeight.SemiBold);
            none.VerticalAlignment = VerticalAlignment.Center;
            none.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(none, 1);
            row.Children.Add(none);
        }
        else
        {
            var avatar = Avatar(session.Name, 30);
            avatar.CornerRadius = new CornerRadius(10);
            var dot = new Border
            {
                Width = 10, Height = 10, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(2), BorderBrush = _ink.Raised,
                Background = session.Agent.Transcript.Busy ? _ink.Accent : new SolidColorBrush(Color.Parse("#4CB782")),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -3, -3),
            };
            if (session.Agent.Transcript.Busy) Pulse(dot, _ink.Accent);
            var face = new Grid { VerticalAlignment = VerticalAlignment.Center };
            face.Children.Add(avatar);
            face.Children.Add(dot);
            row.Children.Add(face);
            var text = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
            var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            name.Children.Add(Ui.Text(session.Name, 13.5, _ink.Text, FontWeight.SemiBold));
            name.Children.Add(Glyph(DeskIcons.Chevron, _ink.Tertiary, 12));
            text.Children.Add(name);
            var where = System.IO.Path.GetFileName(session.Folder.TrimEnd('\\', '/'));
            var sub = Ui.Text(L.T("ligada") + " · " + where + " · " + session.Title.ReplaceLineEndings(" "), 11.5, _ink.Tertiary);
            sub.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(sub);
            Border? who = null;
            who = Ui.Press(text, () => SessionMenu(who!, doc), (b, hover) => b.Background = hover ? _ink.Pill : Brushes.Transparent, L.T("Cambiar de sesión"));
            who.Padding = new Thickness(6, 2);
            who.Margin = new Thickness(4, 0, 0, 0);
            who.CornerRadius = new CornerRadius(8);
            ToolTip.SetTip(who, L.T("Cambiar de sesión"));
            Grid.SetColumn(who, 1);
            row.Children.Add(who);
            var terminal = IconButton(CanvasIcons.Folder, L.T("Ver en la Terminal"), () => _sessions?.Reveal(session.Agent), size: 30, glyph: 15);
            Grid.SetColumn(terminal, 2);
            row.Children.Add(terminal);
        }
        var close = IconButton(CanvasIcons.Close, L.T("Ocultar el chat"), ToggleChat, size: 30, glyph: 14);
        Grid.SetColumn(close, 3);
        row.Children.Add(close);
        var box = new StackPanel();
        box.Children.Add(row);
        box.Children.Add(new Border { Height = 1, Background = _ink.Hairline });
        return box;
    }

    void SessionMenu(Control anchor, CanvasDoc doc)
    {
        if (_sessions is null) return;
        var current = Linked();
        var list = new StackPanel { Spacing = 1, MinWidth = 260, MaxWidth = 290 };
        foreach (var session in _sessions.List())
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            var avatar = Avatar(session.Name, 24);
            avatar.CornerRadius = new CornerRadius(8);
            row.Children.Add(avatar);
            var text = new StackPanel { Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Ui.Text(session.Name, 13, _ink.Text));
            var sub = Ui.Text(System.IO.Path.GetFileName(session.Folder.TrimEnd('\\', '/')) + " · " + session.Title.ReplaceLineEndings(" "), 11.5, _ink.Tertiary);
            sub.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(sub);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            if (current?.Agent == session.Agent)
            {
                var check = Glyph(CanvasIcons.Check, _ink.Accent, 15);
                Grid.SetColumn(check, 2);
                row.Children.Add(check);
            }
            var chosen = session;
            var item = Ui.Press(row, () =>
            {
                ClosePopover();
                _linkedSession[doc.Id] = chosen.Agent;
                RenderChat();
            }, (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, session.Name);
            item.Padding = new Thickness(8, 7);
            item.CornerRadius = new CornerRadius(9);
            list.Children.Add(item);
        }
        if (list.Children.Count > 0) list.Children.Add(new Border { Height = 1, Background = _ink.Hairline, Margin = new Thickness(8, 4) });
        var plus = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        plus.Children.Add(new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(8), Background = _ink.Pill, Child = Glyph(CanvasIcons.Plus, _ink.Accent, 14) });
        plus.Children.Add(Ui.Text(L.T("Nueva sesión para este canvas"), 13, _ink.Text));
        var add = Ui.Press(plus, () => { ClosePopover(); NewSession(doc); }, (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, L.T("Nueva sesión para este canvas"));
        add.Padding = new Thickness(8, 7);
        add.CornerRadius = new CornerRadius(9);
        list.Children.Add(add);
        Popover(anchor, list, new Thickness(5));
    }

    CanvasSession? NewSession(CanvasDoc doc)
    {
        if (_sessions?.Open(doc.Folder) is not { } session)
        {
            Toast(L.T("No se pudo abrir la sesión"), CanvasIcons.Warn);
            return null;
        }
        _linkedSession[doc.Id] = session.Agent;
        RenderChat();
        return session;
    }

    Control ChatInput(CanvasDoc doc, CanvasSession? session)
    {
        var box = new Border
        {
            Margin = new Thickness(10, 4, 10, 10),
            Background = _ink.Pill,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(8, 6, 6, 6),
        };
        var stack = new StackPanel { Spacing = 4 };
        if (_picked is { } picked)
        {
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            chip.Children.Add(Glyph(DeskIcons.Pointer, _ink.Accent, 12));
            var label = new TextBlock { Text = picked.Element, FontFamily = Ink.Mono, FontSize = 11.5, Foreground = _ink.Accent, MaxWidth = 230, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(label, picked.Path);
            chip.Children.Add(label);
            var x = Ui.Press(Glyph(CanvasIcons.Close, _ink.Accent, 10), () => { _picked = null; RenderChat(); }, null, L.T("Quitar"));
            x.Padding = new Thickness(2);
            chip.Children.Add(x);
            var holder = new Border { Background = WithAlpha(_ink.Accent, 0x22), CornerRadius = new CornerRadius(7), Padding = new Thickness(7, 3), HorizontalAlignment = HorizontalAlignment.Left, Child = chip };
            Enter(holder, 0, scale: true);
            stack.Children.Add(holder);
        }
        if (_chatInput.Parent is Panel old) old.Children.Remove(_chatInput);
        else if (_chatInput.Parent is Decorator d) d.Child = null;
        var screen = _deskSel is { } id && doc.Section(id) is { } s ? CanvasDesk.Name(doc, s) : doc.Title;
        _chatInput.Watermark = _picked is not null ? L.T("¿Qué le cambio?") : L.T("Pedir un ajuste en {0}…", screen);
        _chatInput.Foreground = _ink.Text;
        _chatInput.CaretBrush = _ink.Accent;
        Plain(_chatInput, _ink.Pill);
        stack.Children.Add(_chatInput);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var pick = IconButton(DeskIcons.Pointer, L.T("Señalar un elemento") + " · E", () =>
        {
            _picking = !_picking;
            PostDesk(new { t = "pick", on = _picking });
            RenderChat();
        }, size: 30, glyph: 15, on: _picking);
        row.Children.Add(pick);
        var send = Ui.Press(Glyph(CanvasIcons.Send, Brushes.White, 15, 2), SendFromChat, null, L.T("Enviar") + " · Enter");
        send.Width = 32;
        send.Height = 32;
        send.CornerRadius = new CornerRadius(10);
        send.Background = _ink.Accent;
        send.Tag = "solid";
        ToolTip.SetTip(send, session is null ? L.T("Abrir una sesión y enviar") : L.T("Enviar") + " · Enter");
        Grid.SetColumn(send, 2);
        row.Children.Add(send);
        stack.Children.Add(row);
        box.Child = stack;
        return box;
    }

    void SendFromChat()
    {
        if (_doc is not { } doc || _chatInput.Text?.Trim() is not { Length: > 0 } text) return;
        var session = Linked() ?? NewSession(doc);
        if (session is null) return;
        var screenId = _picked?.Id ?? _deskSel;
        var screen = screenId is null ? null : doc.Section(screenId);
        session.Agent.Send(CanvasDesk.Prompt(doc, screen, _picked?.Element, _picked?.Path, text));
        _chatInput.Text = "";
        _picked = null;
        if (screen is not null)
        {
            _busyScreen = screen.Id;
            PostDesk(new { t = "busy", id = screen.Id, on = true });
        }
        RenderChat();
    }

    // ── Los mensajes ────────────────────────────────────────────────────────────────────

    /// <summary>Cuántos bloques de la sesión se ven en el chat (los últimos).</summary>
    const int ChatBlocks = 80;

    void RenderChatList()
    {
        _chatList.Children.Clear();
        if (_watched is not { } agent)
        {
            _chatList.Children.Add(ChatEmpty());
            return;
        }
        var near = _chatScroll.Offset.Y >= _chatScroll.Extent.Height - _chatScroll.Viewport.Height - 60;
        var blocks = agent.Transcript.Blocks.Where(b => b.Kind is not (AgentBlockKind.Think or AgentBlockKind.Todo)).TakeLast(ChatBlocks).ToList();
        if (blocks.Count == 0)
            _chatList.Children.Add(Hint(L.T("Pídale un ajuste: lo hace aquí mismo, sin salir de la mesa. Con la flecha puede señalar un elemento.")));
        var lastDone = blocks.LastOrDefault(b => b is { Kind: AgentBlockKind.Done, Failed: false });
        foreach (var block in blocks)
        {
            if (ChatBlock(agent, block, block == lastDone) is not { } view) continue;
            if (_chatSeen.Add(block)) Enter(view, 0, distance: 8);
            _chatList.Children.Add(view);
        }
        if (agent.Transcript.Busy && blocks.LastOrDefault() is not { Kind: AgentBlockKind.Say, Open: true }) _chatList.Children.Add(Dots());
        if (near) Dispatcher.UIThread.Post(() => _chatScroll.Offset = new Vector(0, double.MaxValue), DispatcherPriority.Background);
    }

    Control ChatEmpty()
    {
        var stack = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 60, 0, 0) };
        var icon = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(16), Background = WithAlpha(_ink.Accent, 0x22), HorizontalAlignment = HorizontalAlignment.Center, Child = Glyph(CanvasIcons.Spark, _ink.Accent, 22) };
        Enter(icon, 0, scale: true);
        stack.Children.Add(icon);
        var text = Ui.Text(_sessions is null ? L.T("Las sesiones están en la Terminal") : L.T("Ninguna sesión está ligada a este canvas"), 13, _ink.Secondary);
        text.TextAlignment = TextAlignment.Center;
        text.TextWrapping = TextWrapping.Wrap;
        text.MaxWidth = 240;
        stack.Children.Add(text);
        if (_sessions is not null && _doc is { } doc)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            label.Children.Add(Glyph(CanvasIcons.Plus, Brushes.White, 13, 2));
            label.Children.Add(Ui.Text(L.T("Abrir una sesión"), 13, Brushes.White, FontWeight.SemiBold));
            var open = Ui.Press(label, () => NewSession(doc), null, L.T("Abrir una sesión"));
            open.Background = _ink.Accent;
            open.CornerRadius = new CornerRadius(999);
            open.Padding = new Thickness(14, 7);
            open.HorizontalAlignment = HorizontalAlignment.Center;
            Enter(open, 120, scale: true);
            stack.Children.Add(open);
        }
        return stack;
    }

    TextBlock Hint(string text) => new() { Text = text, FontSize = 12.5, Foreground = _ink.Tertiary, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(10, 20) };

    /// <param name="latest">El último cierre de turno: solo ese lleva la versión de ahora y el botón para mirar la pantalla.</param>
    Control? ChatBlock(TerminalAgent agent, AgentBlock block, bool latest)
    {
        switch (block.Kind)
        {
            case AgentBlockKind.Ask:
            {
                var (screen, element, text) = CanvasDesk.Parse(block.Text);
                var stack = new StackPanel { Spacing = 5 };
                if (screen is not null || element is not null)
                {
                    var tag = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
                    tag.Children.Add(Glyph(element is null ? DeskIcons.Desk : DeskIcons.Pointer, _ink.Accent, 11));
                    tag.Children.Add(new TextBlock { Text = element ?? screen, FontFamily = element is null ? FontFamily.Default : Ink.Mono, FontSize = 11.5, Foreground = _ink.Accent, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis });
                    stack.Children.Add(new Border { Background = WithAlpha(_ink.Accent, 0x1E), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 2), HorizontalAlignment = HorizontalAlignment.Left, Child = tag });
                }
                stack.Children.Add(new SelectableTextBlock { Text = text.Trim(), FontSize = 13.5, Foreground = _ink.Text, TextWrapping = TextWrapping.Wrap });
                return new Border
                {
                    Background = _ink.Pill, CornerRadius = new CornerRadius(14, 14, 4, 14), Padding = new Thickness(12, 8),
                    HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 270, Margin = new Thickness(30, 4, 0, 0), Child = stack,
                };
            }
            case AgentBlockKind.Say when block.Text.Trim().Length > 0:
            {
                if (!block.Open && _chatSay.TryGetValue(block, out var cached) && cached.Text == block.Text) return Detach(cached.View);
                Control view;
                if (block.Open) view = new SelectableTextBlock { Text = block.Text.Trim(), FontSize = 13.5, Foreground = _ink.Text, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
                else
                {
                    try { view = _markdown(block.Text, agent.Options.Folder); }
                    catch (Exception ex) when (ex is not OutOfMemoryException) { view = new SelectableTextBlock { Text = block.Text.Trim(), FontSize = 13.5, Foreground = _ink.Text, TextWrapping = TextWrapping.Wrap }; }
                    _chatSay[block] = (block.Text, view);
                }
                return view;
            }
            case AgentBlockKind.Step:
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
                Control state = block.State switch
                {
                    StepState.Running => Spinner(),
                    StepState.Done => Glyph(CanvasIcons.Check, new SolidColorBrush(Color.Parse("#4CB782")), 13, 2),
                    StepState.Waiting => Glyph(CanvasIcons.Warn, _ink.Accent, 13),
                    _ => Glyph(CanvasIcons.Close, _ink.Danger, 12, 2),
                };
                row.Children.Add(state);
                var tool = Ui.Text(StepName(block), 12, _ink.Secondary, FontWeight.SemiBold);
                tool.Margin = new Thickness(8, 0, 6, 0);
                Grid.SetColumn(tool, 1);
                row.Children.Add(tool);
                var what = Ui.Text(block.Text.ReplaceLineEndings(" "), 12, _ink.Tertiary);
                what.TextTrimming = TextTrimming.CharacterEllipsis;
                ToolTip.SetTip(what, block.Text);
                Grid.SetColumn(what, 2);
                row.Children.Add(what);
                return row;
            }
            case AgentBlockKind.Permission when block.Answer is null:
            {
                var row = new StackPanel { Spacing = 8 };
                var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                head.Children.Add(Glyph(CanvasIcons.Warn, _ink.Accent, 15));
                head.Children.Add(Ui.Text(L.T("Necesita su permiso"), 13, _ink.Text, FontWeight.SemiBold));
                row.Children.Add(head);
                var what = Ui.Text(block.Text.ReplaceLineEndings(" "), 12, _ink.Secondary);
                what.TextWrapping = TextWrapping.Wrap;
                what.MaxHeight = 54;
                row.Children.Add(what);
                var go = Ui.Press(Ui.Text(L.T("Responder en la Terminal"), 12.5, _ink.Accent, FontWeight.SemiBold), () => _sessions?.Reveal(agent), (b, on) => b.Background = on ? WithAlpha(_ink.Accent, 0x22) : Brushes.Transparent);
                go.Padding = new Thickness(8, 4);
                go.CornerRadius = new CornerRadius(8);
                go.HorizontalAlignment = HorizontalAlignment.Left;
                row.Children.Add(go);
                return new Border { Background = WithAlpha(_ink.Accent, 0x14), BorderBrush = WithAlpha(_ink.Accent, 0x44), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 8), Child = row };
            }
            case AgentBlockKind.Done when block.Failed:
            case AgentBlockKind.Notice:
            {
                var text = Ui.Text(block.Kind == AgentBlockKind.Notice ? block.Text : L.T("El turno terminó con un error"), 12, _ink.Tertiary);
                text.TextWrapping = TextWrapping.Wrap;
                text.HorizontalAlignment = HorizontalAlignment.Center;
                text.TextAlignment = TextAlignment.Center;
                return text;
            }
            case AgentBlockKind.Done:
            {
                // El cierre del turno: la tarjeta del cambio, con la versión de ahora y el botón para mirar la pantalla.
                if (_doc is not { } doc) return null;
                if (!latest) return null;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
                row.Children.Add(Glyph(CanvasIcons.Check, new SolidColorBrush(Color.Parse("#4CB782")), 14, 2));
                var label = Ui.Text(L.T("Listo") + (block.Duration > TimeSpan.Zero ? FormattableString.Invariant($" · {Math.Max(1, (int)block.Duration.TotalSeconds)} s") : ""), 12.5, _ink.Secondary);
                label.Margin = new Thickness(8, 0);
                label.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                var version = new Border { Background = _ink.Pill, CornerRadius = new CornerRadius(999), Padding = new Thickness(7, 0), VerticalAlignment = VerticalAlignment.Center, Child = Ui.Text("v" + doc.Version, 11, _ink.Accent) };
                Grid.SetColumn(version, 2);
                row.Children.Add(version);
                var look = IconButton(CanvasIcons.Search, L.T("Ver la pantalla"), () => { if (_deskSel is { } id) PostDesk(new { t = "focus", id }); }, size: 26, glyph: 13);
                look.Margin = new Thickness(4, 0, 0, 0);
                Grid.SetColumn(look, 3);
                row.Children.Add(look);
                return new Border { Background = _ink.Pill, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 5, 5, 5), Child = row };
            }
            default:
                return null;
        }
    }

    static string StepName(AgentBlock block) => block.Tool switch
    {
        null => L.T("Paso"),
        var t when t.Contains("canvas_edit", StringComparison.OrdinalIgnoreCase) => L.T("Editando el canvas"),
        var t when t.Contains("canvas_read", StringComparison.OrdinalIgnoreCase) => L.T("Leyendo el canvas"),
        var t => t.StartsWith("mcp__", StringComparison.Ordinal) ? t.Split("__").Last() : t,
    };

    /// <summary>Un arco que gira (un paso que corre).</summary>
    Control Spinner()
    {
        var arc = Glyph(DeskIcons.Arc, _ink.Accent, 13, 2.4);
        var turn = new RotateTransform();
        arc.RenderTransformOrigin = RelativePoint.Center;
        arc.RenderTransform = turn;
        if (!TerminalPane.Animate) return arc;
        Loop(arc, new Animation
        {
            Duration = TimeSpan.FromSeconds(0.8),
            IterationCount = IterationCount.Infinite,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Avalonia.Styling.Setter(RotateTransform.AngleProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Avalonia.Styling.Setter(RotateTransform.AngleProperty, 360d) } },
            },
        });
        return arc;
    }

    /// <summary>Tres puntos que saltan: el agente está pensando o trabajando.</summary>
    Control Dots()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(2, 6), HorizontalAlignment = HorizontalAlignment.Left };
        for (var i = 0; i < 3; i++)
        {
            var hop = new TranslateTransform();
            var dot = new Ellipse { Width = 6, Height = 6, Fill = _ink.Tertiary, RenderTransform = hop };
            row.Children.Add(dot);
            if (!TerminalPane.Animate) continue;
            Loop(dot, new Animation
            {
                Duration = TimeSpan.FromSeconds(1),
                Delay = TimeSpan.FromMilliseconds(150 * i),
                IterationCount = IterationCount.Infinite,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Avalonia.Styling.Setter(TranslateTransform.YProperty, 0d) } },
                    new KeyFrame { Cue = new Cue(0.3), Setters = { new Avalonia.Styling.Setter(TranslateTransform.YProperty, -4d) } },
                    new KeyFrame { Cue = new Cue(0.6), Setters = { new Avalonia.Styling.Setter(TranslateTransform.YProperty, 0d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Avalonia.Styling.Setter(TranslateTransform.YProperty, 0d) } },
                },
            });
        }
        return row;
    }

    /// <summary>
    /// Corre una animación sin fin mientras host está en pantalla. Sus fotogramas cambian la transformación que ya tiene
    /// (RotateTransform, TranslateTransform): Avalonia la busca en host.RenderTransform.
    /// </summary>
    static void Loop(Control host, Animation animation)
    {
        var stop = new CancellationTokenSource();
        host.AttachedToVisualTree += (_, _) => _ = animation.RunAsync(host, stop.Token);
        host.DetachedFromVisualTree += (_, _) => { stop.Cancel(); stop = new CancellationTokenSource(); };
    }
}
