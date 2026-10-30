namespace Claupet;

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

/// <summary>
/// La ventana de Claupet Console: bordes redondeados, la barra de arriba propia (con el ícono de Claupet), el riel
/// con Chat, Editor y Ajustes y, al lado, la lista de chats, la de archivos o las secciones de los ajustes. Lo del chat
/// y los ajustes lo manda la bandeja (aquí no se guardan ajustes); el editor trabaja con los archivos directamente.
/// </summary>
sealed class ConsoleWindow : Window
{
    enum Mode { Chat, Editor, Settings }

    readonly ConsoleClient _client = new();
    readonly Grid _root = new() { RowDefinitions = new RowDefinitions("48,*") };
    readonly Grid _body = new() { ColumnDefinitions = new ColumnDefinitions("60,210,*"), Margin = new Thickness(0, 0, 10, 10) };
    readonly Border _header = new();
    readonly Border _rail = new();
    readonly Border _asideTop = new();
    readonly Grid _aside = new() { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(4, 6, 10, 0) };
    readonly StackPanel _sections = new() { Spacing = 2 };
    readonly StackPanel _chatList = new() { Spacing = 2 };
    readonly ScrollViewer _asideScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    readonly SettingsView _view = new() { MaxWidth = 700, HorizontalAlignment = HorizontalAlignment.Left };
    readonly ScrollViewer _scroll = new();
    readonly ChatPane _chat = new();
    readonly EditorPane _editor = new();
    readonly StackPanel _fileList = new() { Spacing = 2 };
    /// <summary>La lista de archivos del editor, a la izquierda (se muestra con el botón de la cabecera del editor).</summary>
    bool _editorFiles;
    /// <summary>Ya se preguntó por los cambios sin guardar: ahora sí se cierra.</summary>
    bool _closing;
    readonly Border _main = new() { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), ClipToBounds = true };
    /// <summary>El fondo animado del tema propio, si trae: debajo de _main, que entonces queda transparente.</summary>
    readonly SceneLayer _scene = new();
    readonly Border _sceneBox = new() { CornerRadius = new CornerRadius(14), ClipToBounds = true, IsHitTestVisible = false };
    readonly TextBlock _heading = new() { FontFamily = Ink.Display, FontSize = 19, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    Ink _ink;
    Mode _mode;
    IReadOnlyList<SettingsPage> _pages = [];
    IReadOnlyList<ChatSummary> _chats = [];
    string _section;
    /// <summary>La sección de la página que se muestra, si es una de adentro (el detalle de una extensión).</summary>
    string? _sectionParent;
    /// <summary>La conversación que se muestra: los mensajes de otra que lleguen tarde se ignoran.</summary>
    string _chatId = ConsoleLink.QuickChat;
    bool _connected;

    public ConsoleWindow(string? section)
    {
        // Con archivos se abre en el editor; sin sección, en el chat; con una, en esa sección de Ajustes.
        _mode = ConsoleApp.Files.Count > 0 ? Mode.Editor : section is null ? Mode.Chat : Mode.Settings;
        _section = section ?? "general";
        Title = "Claupet Console";
        Icon = Ui.LogoIcon();
        Width = 1080;
        Height = 720;
        MinWidth = 760;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // La barra de Windows se cambia por la propia; los bordes y las esquinas redondeadas siguen siendo los del sistema.
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = -1;
        _ink = Ink.For(IsDark());

        _scroll.Content = new Border { Child = _view, Padding = new Thickness(32, 28, 32, 32) };
        // La columna ocupa lo que haya, hasta 700 (las tarjetas no se encogen a lo que dicen sus filas).
        _scroll.SizeChanged += (_, e) => _view.Width = Math.Clamp(e.NewSize.Width - 64, 0, 700);
        _view.Action += (key, value) => _client.Send(new ConsoleMessage { Type = ConsoleLink.Action, Key = key, Value = value });
        _view.Navigate += Select;
        _chat.Request += OnChatRequest;
        _editor.FilesToggled += () =>
        {
            _editorFiles = !_editorFiles;
            Render(rebuild: true);
        };
        _editor.DocsChanged += () => { if (_mode == Mode.Editor && _editorFiles) RenderFileList(); };
        // Los atajos del editor (Ctrl+N, O, S, W) mientras se ve.
        KeyDown += (_, e) => { if (_mode == Mode.Editor) _editor.HandleKey(e); };
        Closing += (_, e) =>
        {
            if (_closing || !_editor.HasUnsaved) return;
            e.Cancel = true;
            SetMode(Mode.Editor);
            _editor.AskBeforeExit(() =>
            {
                _closing = true;
                Close();
            });
        };
        _client.ConnectedChanged += connected =>
        {
            _connected = connected;
            // La bandeja manda la lista de chats y la conversación al pedirla.
            if (connected) _client.Send(new ConsoleMessage { Type = ConsoleLink.ChatOpen, Key = _chatId });
            else Render(rebuild: true);
        };
        _client.Received += OnMessage;
        // El esqueleto se arma una vez; al cambiar el tema o el idioma se rehacen la barra y el contenido.
        _root.Children.Add(_header);
        Grid.SetRow(_body, 1);
        _body.Children.Add(_rail);
        _aside.Children.Add(_asideTop);
        Grid.SetRow(_asideScroll, 1);
        _aside.Children.Add(_asideScroll);
        Grid.SetColumn(_aside, 1);
        _body.Children.Add(_aside);
        _sceneBox.Child = _scene;
        Grid.SetColumn(_sceneBox, 2);
        _body.Children.Add(_sceneBox);
        Grid.SetColumn(_main, 2);
        _body.Children.Add(_main);
        _root.Children.Add(_body);
        Content = _root;
        Render(rebuild: true);
        foreach (var file in ConsoleApp.Files) _editor.Open(file);
        _client.Start();

        PropertyChanged += (_, e) =>
        {
            // Maximizada, Windows deja una parte fuera de la pantalla: se compensa para que no se corte.
            if (e.Property == WindowStateProperty) _root.Margin = WindowState == WindowState.Maximized ? OffScreenMargin : default;
            // El fondo animado no se mueve si no se ve.
            if (e.Property == WindowStateProperty || e.Property == IsVisibleProperty)
                _scene.Paused = WindowState == WindowState.Minimized || !IsVisible;
        };
        ActualThemeVariantChanged += (_, _) =>
        {
            _ink = Ink.For(IsDark());
            Render(rebuild: true);
        };
        Closed += (_, _) => _client.Dispose();
    }

    bool IsDark() => (Application.Current?.ActualThemeVariant ?? ThemeVariant.Light) == ThemeVariant.Dark;

    /// <summary>Abre esos archivos en el editor (Claupet.exe --console archivo…, con la Console ya abierta).</summary>
    public void OpenFiles(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return;
        SetMode(Mode.Editor);
        foreach (var file in files) _editor.Open(file);
    }

    void OnMessage(ConsoleMessage message)
    {
        switch (message.Type)
        {
            case ConsoleLink.State:
                var rebuild = false;
                if (message.Language is { } language && language != L.Language)
                {
                    L.Use(language);
                    rebuild = true;
                }
                if (message.Palette is { } palette && (palette != Claupet.Theme.SelectedPalette || message.CustomTheme != Claupet.Theme.SelectedCustom))
                {
                    Claupet.Theme.SelectedPalette = palette;
                    Claupet.Theme.SelectedCustom = message.CustomTheme;
                    _ink = Ink.For(IsDark());
                    rebuild = true;
                }
                rebuild |= _pages.Count == 0;
                _pages = message.Pages ?? [];
                Render(rebuild);
                break;
            case ConsoleLink.Options:
                var anchor = (_mode == Mode.Chat ? _chat.AnchorFor(message.Key) : _view.AnchorFor(message.Key)) ?? _main;
                Ui.ShowOptions(message, anchor, _ink, value => _client.Send(new ConsoleMessage { Type = ConsoleLink.Pick, Token = message.Token, Value = value }));
                break;
            case ConsoleLink.Show:
                if (message.Key is { Length: > 0 } section && _pages.Any(p => p.Id == section))
                {
                    var moved = section != _section;
                    _mode = Mode.Settings;
                    _section = section;
                    Render(rebuild: true);
                    if (moved) _scroll.Offset = default;
                }
                else if (message.Key == "editor" && _mode != Mode.Editor) SetMode(Mode.Editor);
                else if (message.Key is null or ConsoleLink.Chat && _mode != Mode.Chat)
                {
                    _mode = Mode.Chat;
                    Render(rebuild: true);
                }
                BringToFront();
                break;
            case ConsoleLink.Chats:
                _chats = message.Chats ?? [];
                // La que se mostraba ya no está (se borró): queda la rápida.
                if (_chats.All(c => c.Id != _chatId)) _chatId = ConsoleLink.QuickChat;
                if (_mode == Mode.Chat) RenderChatList();
                break;
            case ConsoleLink.Chat when message.Chat is { } chat && chat.Id == _chatId:
                _chat.Show(chat);
                break;
        }
    }

    void OnChatRequest(ConsoleMessage message)
    {
        // Al borrar se vuelve a la rápida; la bandeja hace lo mismo y manda esa.
        if (message.Type == ConsoleLink.ChatDelete) _chatId = ConsoleLink.QuickChat;
        _client.Send(message);
    }

    /// <summary>Trae la ventana al frente (si estaba minimizada, la restaura).</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true; // Windows a veces solo la hace parpadear; esto la sube de verdad
        Topmost = false;
    }

    void Render(bool rebuild)
    {
        Background = _ink.Window;
        if (rebuild)
        {
            Themes.Paint(Resources, _ink);
            _header.Child = Header();
            _rail.Child = Rail();
            _scene.Show(CustomThemes.Selected?.Scene);
            _sceneBox.Background = _ink.Surface;
            _main.Background = _scene.Active ? Brushes.Transparent : _ink.Surface;
            _main.BorderBrush = _ink.Hairline;
            _chat.SetInk(_ink);
            _editor.SetInk(_ink);
            _body.ColumnDefinitions[1].Width = new GridLength(_mode switch
            {
                Mode.Chat => 250,
                Mode.Editor => _editorFiles ? 250 : 0,
                _ => 210,
            });
            // Sin columna (el editor con los archivos ocultos), nada de ella asoma.
            _aside.IsVisible = _body.ColumnDefinitions[1].Width.Value > 0;
            _asideTop.Child = _mode switch { Mode.Chat => ChatsHeader(), Mode.Editor => FilesHeader(), _ => _heading };
            _asideScroll.Content = _mode switch { Mode.Chat => _chatList, Mode.Editor => _fileList, _ => _sections };
        }
        _heading.Text = L.T("Ajustes");
        _heading.Foreground = _ink.Text;
        _heading.Margin = new Thickness(10, 2, 10, 12);

        // El editor no necesita a la bandeja: trabaja con los archivos.
        if (_mode == Mode.Editor)
        {
            if (_editorFiles) RenderFileList();
            if (_main.Child != _editor) _main.Child = _editor;
            return;
        }
        if (!_connected || _pages.Count == 0)
        {
            _sections.Children.Clear();
            _chatList.Children.Clear();
            _main.Child = Waiting();
            return;
        }
        if (_mode == Mode.Chat)
        {
            RenderChatList();
            if (_main.Child != _chat) _main.Child = _chat;
            return;
        }
        // Una página de adentro que ya no está (la extensión se desinstaló, la revisión terminó) vuelve a su sección.
        if (_pages.All(p => p.Id != _section)) _section = _sectionParent is { } up && _pages.Any(p => p.Id == up) ? up : _pages[0].Id;
        var shown = _pages.First(p => p.Id == _section);
        _sectionParent = shown.Parent;
        _sections.Children.Clear();
        foreach (var page in _pages.Where(p => p.Parent is null))
        {
            var id = page.Id;
            var current = id == (shown.Parent ?? _section);
            var item = Ui.Press(Ui.Text(page.Title, 14.5, current ? _ink.Text : _ink.Secondary), () => Select(id),
                current ? null : (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, name: page.Title);
            item.MinHeight = 38;
            item.Padding = new Thickness(12, 0);
            item.CornerRadius = new CornerRadius(12);
            if (current) item.Background = _ink.Pill;
            _sections.Children.Add(item);
        }
        _view.Show(shown, _ink, rebuild, _pages.FirstOrDefault(p => p.Id == shown.Parent)?.Title);
        if (_main.Child != _scroll) _main.Child = _scroll;
    }

    void Select(string id)
    {
        if (id == _section || _pages.All(p => p.Id != id)) return;
        _section = id;
        Render(rebuild: true);
        _scroll.Offset = default;
    }

    void SetMode(Mode mode)
    {
        if (mode == _mode) return;
        _mode = mode;
        Render(rebuild: true);
        if (mode == Mode.Chat) _chat.FocusInput();
        if (mode == Mode.Editor) _editor.FocusText();
    }

    // ── Archivos del editor ─────────────────────────────────────────────────────────────────

    /// <summary>«Archivos» y el botón de uno nuevo, como «Chats».</summary>
    Control FilesHeader()
    {
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(8, 2, 6, 10) };
        bar.Children.Add(new TextBlock
        {
            Text = L.T("Archivos"),
            FontFamily = Ink.Display,
            FontSize = 19,
            FontWeight = FontWeight.SemiBold,
            Foreground = _ink.Text,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var plus = new Viewbox
        {
            Width = 15,
            Height = 15,
            Child = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M12,5 V19 M5,12 H19"),
                Stroke = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x13)),
                StrokeThickness = 2.2,
                StrokeLineCap = PenLineCap.Round,
                Width = 24,
                Height = 24,
            },
        };
        var add = Ui.Press(plus, _editor.New, (b, on) => b.Opacity = on ? 0.88 : 1, L.T("Documento nuevo"));
        add.Width = 34;
        add.Height = 34;
        add.CornerRadius = new CornerRadius(999);
        add.Background = _ink.Accent;
        ToolTip.SetTip(add, L.T("Documento nuevo"));
        Grid.SetColumn(add, 1);
        bar.Children.Add(add);
        return bar;
    }

    /// <summary>Los abiertos (el actual marcado, con su punto si tiene cambios) y, debajo, los recientes.</summary>
    void RenderFileList()
    {
        _fileList.Children.Clear();
        void Caption(string text) => _fileList.Children.Add(new TextBlock
        {
            Text = text,
            FontFamily = Ink.Sans,
            FontSize = 12,
            FontWeight = FontWeight.Medium,
            Foreground = _ink.Tertiary,
            Margin = new Thickness(10, 14, 10, 4),
        });
        if (_editor.Docs.Count > 0) Caption(L.T("Abiertos"));
        foreach (var doc in _editor.Docs)
        {
            var open = doc;
            _fileList.Children.Add(_editor.FileRow(doc.Path ?? doc.Title, () => _editor.Select(open), doc == _editor.Current, doc.Dirty));
        }
        var recents = _editor.Recents.Where(p => File.Exists(p) && _editor.Docs.All(d => !string.Equals(d.Path, p, StringComparison.OrdinalIgnoreCase))).Take(10).ToList();
        if (recents.Count > 0) Caption(L.T("Recientes"));
        foreach (var path in recents) _fileList.Children.Add(_editor.FileRow(path, () => _editor.Open(path)));
    }

    // ── Chats ───────────────────────────────────────────────────────────────────────────────

    /// <summary>«Chats» y el botón de chat nuevo, como en el diseño.</summary>
    Control ChatsHeader()
    {
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(8, 2, 6, 10) };
        bar.Children.Add(new TextBlock
        {
            Text = L.T("Chats"),
            FontFamily = Ink.Display,
            FontSize = 19,
            FontWeight = FontWeight.SemiBold,
            Foreground = _ink.Text,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var plus = new Viewbox
        {
            Width = 15,
            Height = 15,
            Child = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M12,5 V19 M5,12 H19"),
                Stroke = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x13)),
                StrokeThickness = 2.2,
                StrokeLineCap = PenLineCap.Round,
                Width = 24,
                Height = 24,
            },
        };
        var add = Ui.Press(plus, NewChat, (b, on) => b.Opacity = on ? 0.88 : 1, L.T("Chat nuevo"));
        add.Width = 34;
        add.Height = 34;
        add.CornerRadius = new CornerRadius(999);
        add.Background = _ink.Accent;
        ToolTip.SetTip(add, L.T("Chat nuevo"));
        Grid.SetColumn(add, 1);
        bar.Children.Add(add);
        return bar;
    }

    void NewChat()
    {
        // El id lo pone la Console: así sabe cuál mostrar sin esperar a la bandeja.
        _chatId = Guid.NewGuid().ToString("N")[..12];
        _client.Send(new ConsoleMessage { Type = ConsoleLink.ChatNew, Key = _chatId });
        if (_mode != Mode.Chat) SetMode(Mode.Chat);
        _chat.FocusInput();
    }

    void OpenChat(string id)
    {
        if (id == _chatId) return;
        _chatId = id;
        _client.Send(new ConsoleMessage { Type = ConsoleLink.ChatOpen, Key = id });
        RenderChatList();
        _chat.FocusInput();
    }

    /// <summary>La rápida arriba y las guardadas agrupadas por cuándo se usaron (Hoy, Ayer, Esta semana, Antes).</summary>
    void RenderChatList()
    {
        _chatList.Children.Clear();
        string? group = null;
        foreach (var chat in _chats)
        {
            if (!chat.Quick && Conversations.GroupOf(chat.Updated, DateTime.Now) is var g && g != group)
            {
                group = g;
                _chatList.Children.Add(new TextBlock
                {
                    Text = g,
                    FontFamily = Ink.Sans,
                    FontSize = 12,
                    FontWeight = FontWeight.Medium,
                    Foreground = _ink.Tertiary,
                    Margin = new Thickness(10, 14, 10, 4),
                });
            }
            _chatList.Children.Add(ChatItem(chat));
        }
    }

    Control ChatItem(ChatSummary chat)
    {
        var current = chat.Id == _chatId;
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (chat.Quick) title.Children.Add(new AppLogo { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(new TextBlock
        {
            Text = chat.Title,
            FontFamily = Ink.Sans,
            FontSize = 14.5,
            Foreground = _ink.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(title);
        if (chat.Preview.Length > 0)
            stack.Children.Add(new TextBlock
            {
                Text = chat.Preview,
                FontFamily = Ink.Sans,
                FontSize = 12.5,
                Foreground = _ink.Tertiary,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        var id = chat.Id;
        var item = Ui.Press(stack, () => OpenChat(id), current ? null : (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, chat.Title);
        item.MinHeight = 46;
        item.Padding = new Thickness(12, 8);
        item.CornerRadius = new CornerRadius(12);
        if (current) item.Background = _ink.Pill;
        return item;
    }

    // ── Barra y riel ────────────────────────────────────────────────────────────────────────

    Control Header()
    {
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), Background = Brushes.Transparent };
        var logo = new AppLogo { Width = 18, Height = 18, Margin = new Thickness(18, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(logo);
        var title = Ui.Text("Claupet Console", 13.5, _ink.Text, FontWeight.SemiBold);
        Grid.SetColumn(title, 1);
        bar.Children.Add(title);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(Caption("M2,6 L10,6", L.T("Minimizar"), () => WindowState = WindowState.Minimized));
        buttons.Children.Add(Caption("M3,2 L9,2 Q10,2 10,3 L10,9 Q10,10 9,10 L3,10 Q2,10 2,9 L2,3 Q2,2 3,2 Z", L.T("Maximizar"), ToggleMaximized));
        buttons.Children.Add(Caption("M2.5,2.5 L9.5,9.5 M9.5,2.5 L2.5,9.5", L.T("Cerrar"), Close, danger: true));
        Grid.SetColumn(buttons, 3);
        bar.Children.Add(buttons);
        bar.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(bar).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) ToggleMaximized();
            else BeginMoveDrag(e);
        };
        return bar;
    }

    // Los íconos del riel, en un cuadro de 24 (los del diseño).
    const string ChatIcon = "M5,5 H19 A1,1 0 0 1 20,6 V15 A1,1 0 0 1 19,16 H10 L5,20 V6 A1,1 0 0 1 6,5 Z";
    const string EditorIcon = "M14,3 H7 A1,1 0 0 0 6,4 V20 A1,1 0 0 0 7,21 H17 A1,1 0 0 0 18,20 V7 L14,3 Z M14,3 V7 H18 M9.5,12 H14.5 M9.5,16 H12.5";
    const string SettingsIcon = "M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12 Z M12,2.5 V5 M12,19 V21.5 M5.3,5.3 L7.1,7.1 M16.9,16.9 L18.7,18.7 M2.5,12 H5 "
        + "M19,12 H21.5 M5.3,18.7 L7.1,16.9 M16.9,7.1 L18.7,5.3";

    /// <summary>El riel de la izquierda: Chat, Editor y Ajustes. El editor llega en una próxima etapa.</summary>
    Control Rail()
    {
        var rail = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        rail.Children.Add(RailButton(ChatIcon, L.T("Chat"), _mode == Mode.Chat, () => SetMode(Mode.Chat)));
        rail.Children.Add(RailButton(EditorIcon, L.T("Editor"), _mode == Mode.Editor, () => SetMode(Mode.Editor)));
        rail.Children.Add(RailButton(SettingsIcon, L.T("Ajustes"), _mode == Mode.Settings, () => SetMode(Mode.Settings)));
        return rail;
    }

    /// <summary>Un botón del riel; sin onClick está apagado y dice «próximamente».</summary>
    Control RailButton(string path, string name, bool current, Action? onClick)
    {
        var shape = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(path),
            Stroke = current ? _ink.Text : _ink.Secondary,
            StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 24,
            Height = 24,
        };
        var icon = new Viewbox
        {
            Width = 19,
            Height = 19,
            Child = shape,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var button = onClick is null
            ? new Border { Child = icon, Opacity = 0.45 }
            : Ui.Press(icon, onClick, current ? null : (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, name);
        button.Width = 42;
        button.Height = 42;
        button.CornerRadius = new CornerRadius(12);
        button.Background = current ? _ink.Pill : Brushes.Transparent;
        ToolTip.SetTip(button, onClick is null ? L.T("{0}: próximamente", name) : name);
        Ui.AutomationName(button, name);
        return button;
    }

    void ToggleMaximized() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    Border Caption(string path, string name, Action onClick, bool danger = false)
    {
        var icon = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(path),
            Stroke = _ink.Secondary,
            StrokeThickness = 1,
            StrokeLineCap = PenLineCap.Round,
            Width = 12,
            Height = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var hoverBrush = danger ? new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C)) : _ink.Pill;
        var button = Ui.Press(icon, onClick, (b, on) =>
        {
            b.Background = on ? hoverBrush : Brushes.Transparent;
            icon.Stroke = on && danger ? Brushes.White : _ink.Secondary;
        }, name);
        button.Width = 38;
        button.Height = 32;
        button.CornerRadius = new CornerRadius(8);
        button.Focusable = false;
        return button;
    }

    /// <summary>Mientras no hay conexión con la bandeja: qué pasa y cómo abrir Claupet.</summary>
    Control Waiting()
    {
        var panel = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 380 };
        panel.Children.Add(new AppLogo { Width = 48, Height = 48 });
        // La Console es el mismo Claupet.exe: la bandeja es otro proceso con ese nombre.
        var claupet = Environment.ProcessPath ?? "";
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(claupet));
        var running = processes.Any(p => p.Id != Environment.ProcessId);
        foreach (var process in processes) process.Dispose();
        panel.Children.Add(new TextBlock
        {
            Text = running ? L.T("Conectando con Claupet…") : L.T("Claupet no está abierto. El chat y los ajustes los atiende Claupet: ábralo para verlos aquí."),
            FontSize = 14.5,
            FontFamily = Ink.Sans,
            Foreground = _ink.Secondary,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
        });
        if (!running && File.Exists(claupet))
        {
            var open = Ui.Press(Ui.Text(L.T("Abrir Claupet"), 14, Brushes.White, FontWeight.SemiBold), () =>
            {
                try { Process.Start(new ProcessStartInfo(claupet) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory })?.Dispose(); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { ErrorLog.Write(ex); }
            });
            open.Background = _ink.Accent;
            open.CornerRadius = new CornerRadius(999);
            open.Height = 38;
            open.Padding = new Thickness(18, 0);
            open.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(open);
        }
        return panel;
    }
}
