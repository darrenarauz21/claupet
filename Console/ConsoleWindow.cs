namespace Claupet;

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

/// <summary>
/// La ventana de Claupet Console: bordes redondeados, la barra de arriba propia (con la cabeza del chibi) y,
/// por ahora, todos los ajustes por secciones. Lo que muestra lo manda la bandeja; aquí no se guarda nada.
/// </summary>
sealed class ConsoleWindow : Window
{
    readonly ConsoleClient _client = new();
    readonly Grid _root = new() { RowDefinitions = new RowDefinitions("48,*") };
    readonly Border _header = new();
    readonly StackPanel _sections = new() { Spacing = 2 };
    readonly SettingsView _view = new() { MaxWidth = 700, HorizontalAlignment = HorizontalAlignment.Left };
    readonly ScrollViewer _scroll = new();
    readonly Border _main = new() { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), ClipToBounds = true };
    readonly TextBlock _heading = new() { FontFamily = Ink.Display, FontSize = 19, FontWeight = FontWeight.SemiBold, Margin = new Thickness(10, 2, 10, 12) };
    Ink _ink;
    IReadOnlyList<SettingsPage> _pages = [];
    string _section;
    bool _connected;

    public ConsoleWindow(string? section)
    {
        _section = section ?? "general";
        Title = "Claupet Console";
        Icon = Ui.HeadIcon();
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
        _view.Action += key => _client.Send(new ConsoleMessage { Type = ConsoleLink.Action, Key = key });
        _client.ConnectedChanged += connected =>
        {
            _connected = connected;
            if (!connected) Render(rebuild: true);
        };
        _client.Received += OnMessage;
        // El esqueleto se arma una vez; al cambiar el tema o el idioma se rehacen la barra y el contenido.
        _root.Children.Add(_header);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*"), Margin = new Thickness(0, 0, 10, 10) };
        Grid.SetRow(body, 1);
        var aside = new StackPanel { Margin = new Thickness(10, 6, 10, 10) };
        aside.Children.Add(_heading);
        aside.Children.Add(_sections);
        body.Children.Add(aside);
        Grid.SetColumn(_main, 1);
        body.Children.Add(_main);
        _root.Children.Add(body);
        Content = _root;
        Render(rebuild: true);
        _client.Start();

        PropertyChanged += (_, e) =>
        {
            // Maximizada, Windows deja una parte fuera de la pantalla: se compensa para que no se corte.
            if (e.Property == WindowStateProperty) _root.Margin = WindowState == WindowState.Maximized ? OffScreenMargin : default;
        };
        ActualThemeVariantChanged += (_, _) =>
        {
            _ink = Ink.For(IsDark());
            Render(rebuild: true);
        };
        Closed += (_, _) => _client.Dispose();
    }

    bool IsDark() => (Application.Current?.ActualThemeVariant ?? ThemeVariant.Light) == ThemeVariant.Dark;

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
                if (message.Palette is { } palette && palette != Claupet.Theme.SelectedPalette)
                {
                    Claupet.Theme.SelectedPalette = palette;
                    _ink = Ink.For(IsDark());
                    rebuild = true;
                }
                rebuild |= _pages.Count == 0;
                _pages = message.Pages ?? [];
                Render(rebuild);
                break;
            case ConsoleLink.Options:
                _view.ShowOptions(message, _main, value => _client.Send(new ConsoleMessage { Type = ConsoleLink.Pick, Token = message.Token, Value = value }));
                break;
            case ConsoleLink.Show:
                if (message.Key is { Length: > 0 } section && _pages.Any(p => p.Id == section))
                {
                    _section = section;
                    Render(rebuild: true);
                }
                BringToFront();
                break;
        }
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
            _header.Child = Header();
            _main.Background = _ink.Surface;
            _main.BorderBrush = _ink.Hairline;
        }
        _heading.Text = L.T("Ajustes");
        _heading.Foreground = _ink.Text;

        if (!_connected || _pages.Count == 0)
        {
            _sections.Children.Clear();
            _main.Child = Waiting();
            return;
        }
        if (_pages.All(p => p.Id != _section)) _section = _pages[0].Id;
        _sections.Children.Clear();
        foreach (var page in _pages)
        {
            var id = page.Id;
            var current = id == _section;
            var item = Ui.Press(Ui.Text(page.Title, 14.5, current ? _ink.Text : _ink.Secondary), () => Select(id),
                current ? null : (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, name: page.Title);
            item.MinHeight = 38;
            item.Padding = new Thickness(12, 0);
            item.CornerRadius = new CornerRadius(12);
            if (current) item.Background = _ink.Pill;
            _sections.Children.Add(item);
        }
        _view.Show(_pages.First(p => p.Id == _section), _ink, rebuild);
        if (_main.Child != _scroll) _main.Child = _scroll;
    }

    void Select(string id)
    {
        if (id == _section) return;
        _section = id;
        Render(rebuild: true);
        _scroll.Offset = default;
    }

    Control Header()
    {
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), Background = Brushes.Transparent };
        var logo = new HeadLogo { Width = 18, Height = 18, Margin = new Thickness(18, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
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
        panel.Children.Add(new HeadLogo { Width = 48, Height = 48 });
        // La Console es el mismo Claupet.exe: la bandeja es otro proceso con ese nombre.
        var claupet = Environment.ProcessPath ?? "";
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(claupet));
        var running = processes.Any(p => p.Id != Environment.ProcessId);
        foreach (var process in processes) process.Dispose();
        panel.Children.Add(new TextBlock
        {
            Text = running ? L.T("Conectando con Claupet…") : L.T("Claupet no está abierto. Los ajustes los guarda Claupet: ábralo para verlos aquí."),
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
