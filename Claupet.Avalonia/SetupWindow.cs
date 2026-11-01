using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SD = System.Drawing;

namespace Claupet;

/// <summary>
/// La bienvenida de Claupet, por pasos, como SetupForm en Windows: el idioma y las novedades, los términos y
/// condiciones, la carpeta donde se instala (solo en Windows: en Linux y macOS la instalan sus scripts), el hook de
/// permisos en cada carpeta de Claude Code, la mascota, los modelos y la voz, y el inicio con el sistema y las
/// notificaciones. No se pasa de los términos sin aceptarlos, ni de la instalación con una carpeta que no sirve. Lo que
/// esta app todavía no tiene (PlatformFeatures) no se ofrece: el paso lo muestra bajo «Próximamente». Nada cambia hasta
/// «Aplicar», que no es el botón por defecto y no responde a Enter: una tecla suelta no reescribe los hooks ni el inicio.
/// </summary>
sealed class SetupWindow : Window
{
    sealed record HookRow(string Dir, HookInstaller.State State, string Name);

    enum Step { Welcome, Terms, Install, Hooks, Companion, Models, Startup }

    readonly Step[] _order = Enum.GetValues<Step>().Where(s => s != Step.Install || OperatingSystem.IsWindows()).ToArray();
    int Steps => _order.Length;
    const PlatformFeatures Voice = PlatformFeatures.Mic | PlatformFeatures.ReadAloud;
    const PlatformFeatures Models = Voice | PlatformFeatures.LocalModel | PlatformFeatures.Extensions;
    static readonly SD.Color OnAccent = SD.Color.FromArgb(0x14, 0x14, 0x13);
    static FontFamily Sans => Gfx.Body.Typeface.FontFamily;
    static FontFamily Serif => Gfx.Title.Typeface.FontFamily;

    readonly Theme _t;
    readonly PlatformFeatures _features;
    readonly SD.Color _railColor;
    readonly List<HookRow> _rows = new();
    readonly HashSet<string> _hooksOn = new(StringComparer.OrdinalIgnoreCase);
    readonly string _startLanguage, _currentMascot, _readVoice;
    readonly TaskCompletionSource<bool> _done = new();
    readonly StackPanel _rail = new() { Margin = new Thickness(16, 26, 16, 0), Spacing = 4 };
    readonly TextBlock _version, _soon, _step, _heading, _lead;
    readonly StackPanel _body = new() { Margin = new Thickness(0, 0, 14, 0) };
    readonly ScrollViewer _scroll;
    readonly Border _back, _next, _apply, _later, _close;
    readonly TextBlock _backText, _nextText, _applyText, _laterText;
    int _current;
    bool _applied;

    public string Language { get; private set; }
    /// <summary>La mascota elegida (Mascots.Classic o un personaje), o null si no se tocó.</summary>
    public string? Mascot { get; private set; }
    public bool DesktopMascot { get; private set; }
    public bool Chat { get; private set; }
    public bool Island { get; private set; }
    public bool Mic { get; private set; }
    public bool ReadAloud { get; private set; }
    /// <summary>La mascota responde con el modelo local, y cuál (LocalModel.Choices).</summary>
    public bool LocalModelOn { get; private set; }
    public string LocalModelId { get; private set; }
    public bool StartWithSystem { get; private set; }
    public bool NotificationsOn { get; private set; }
    /// <summary>true con «Aplicar»; false con «Ahora no», la × o Escape.</summary>
    public Task<bool> Result => _done.Task;
    /// <summary>Cuántos hooks agregó o actualizó ApplyHooks.</summary>
    public int HooksChanged { get; private set; }
    /// <summary>Se aceptaron los términos (ahora o antes).</summary>
    public bool TermsAccepted { get; private set; }
    /// <summary>Dónde instalar Claupet al aplicar (Windows); null si esta copia ya es la instalada, o fuera de Windows.</summary>
    public string? InstallFolder { get; private set; }
    /// <summary>El acceso directo del escritorio (Windows; el del menú Inicio va siempre).</summary>
    public bool DesktopShortcut { get; private set; }

    /// <summary>Por qué no se puede instalar en InstallFolder; null si se puede.</summary>
    string? _installProblem;
    readonly string _exePath;

    /// <summary>Instala un paquete .claupet (lo revisa Claupet Console); null donde no hay extensiones.</summary>
    public Func<string, Task>? InstallPackage { get; init; }

    /// <summary>La extensión que pasa la voz a texto y la que lee en voz alta, si las hay (las dice en vez de los modelos).</summary>
    public string? SpeechToTextExtension { get; init; }
    public string? TextToSpeechExtension { get; init; }

    public SetupWindow(AppSettings settings, string exePath, Theme theme, IEnumerable<string> configDirs, PlatformFeatures features)
    {
        _t = theme;
        _features = features;
        _exePath = exePath;
        _railColor = theme.Dark ? SD.Color.FromArgb(0x2E, 0x2E, 0x2B) : SD.Color.FromArgb(0xF0, 0xEE, 0xE6);
        _startLanguage = Language = L.Language;
        TermsAccepted = Terms.Accepted(settings);
        // Una compilación de desarrollo no ofrece instalarse: copiaría solo su lanzador encima de la instalada.
        if (OperatingSystem.IsWindows() && !Installation.IsInstalled(exePath) && Installation.CanInstall(exePath))
        {
            InstallFolder = Installation.Folder ?? Installation.DefaultFolder;
            _installProblem = Installation.Problem(InstallFolder);
        }
        DesktopShortcut = settings.DesktopShortcut;
        _currentMascot = Mascots.ChoiceOf(settings);
        DesktopMascot = settings.DesktopMascot;
        Chat = settings.Chat;
        Island = settings.Island;
        Mic = settings.Mic;
        ReadAloud = settings.ReadAloud;
        LocalModelOn = settings.MascotModel == AssistantModels.Local;
        LocalModelId = LocalModel.Find(settings.LocalModel).Id;
        _readVoice = settings.ReadAloudVoice;
        StartWithSystem = StartupRegistration.IsEnabled || !settings.SetupDone;
        NotificationsOn = settings.Notifications;
        foreach (var dir in configDirs)
        {
            var row = new HookRow(dir, HookInstaller.Status(dir, exePath), dir.Replace(Account.Home, "~"));
            _rows.Add(row);
            if (row.State is HookInstaller.State.Missing or HookInstaller.State.Outdated) _hooksOn.Add(dir);
        }

        Icon = Ui.LogoIcon();
        Width = 800;
        Height = 600;
        CanResize = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Gfx.B(theme.Background);
        // Sin la barra del sistema, como Claupet Console: la × es propia y el fondo mueve la ventana.
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = -1;

        // La columna de la izquierda: el ícono de Claupet, el nombre, los pasos y lo que viene.
        _version = Label("", 11.3, theme.Tertiary, margin: new Thickness(24, 2, 0, 0));
        _soon = Label("", 10.3, theme.Tertiary, FontWeight.SemiBold);
        var top = new StackPanel();
        top.Children.Add(new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(18), Background = Gfx.B(theme.Raised),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(24, 30, 0, 0),
            Child = new AppLogo { Width = 48, Height = 48 },
        });
        top.Children.Add(Label("Claupet", 20, theme.Text, family: Serif, margin: new Thickness(22, 10, 0, 0)));
        top.Children.Add(_version);
        top.Children.Add(_rail);
        var bottom = new StackPanel { Margin = new Thickness(24, 0, 24, 28), Spacing = 8 };
        bottom.Children.Add(_soon);
        var ide = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        ide.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Gfx.B(theme.Accent), VerticalAlignment = VerticalAlignment.Center });
        ide.Children.Add(Label("Claupet IDE", 12.7, theme.Secondary));
        bottom.Children.Add(ide);
        var rail = new DockPanel { Background = Gfx.B(_railColor) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        rail.Children.Add(bottom);
        rail.Children.Add(top);

        // El paso: encabezado, cuerpo con desplazamiento y botones al pie.
        _step = Label("", 10.7, theme.Tertiary, FontWeight.SemiBold);
        _heading = Label("", 22.7, theme.Text, family: Serif, margin: new Thickness(0, 6, 0, 0));
        _lead = Label("", 13, theme.Secondary, margin: new Thickness(0, 8, 0, 0));
        _scroll = new ScrollViewer
        {
            Content = _body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 20, -14, 0),
        };
        (_later, _laterText) = Pill(PillKind.Link, () => Close());
        (_back, _backText) = Pill(PillKind.Quiet, () => GoTo(_current - 1));
        // «Siguiente» solo avanza; en el último paso se oculta y aparece «Aplicar», que es otro botón.
        (_next, _nextText) = Pill(PillKind.Primary, () => { if (_current < Gate) GoTo(_current + 1); });
        (_apply, _applyText) = Pill(PillKind.Primary, Apply);
        // Enter no pulsa «Aplicar» ni con el foco en él: se aplica con el clic (o, llegando con Tab, con Espacio).
        _apply.AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Enter) e.Handled = true; }, RoutingStrategies.Tunnel);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 10, Margin = new Thickness(0, 16, 0, 0) };
        _later.Margin = new Thickness(-8, 0, 0, 0);
        footer.Children.Add(_later);
        Grid.SetColumn(_back, 2);
        footer.Children.Add(_back);
        Grid.SetColumn(_next, 3);
        footer.Children.Add(_next);
        Grid.SetColumn(_apply, 3);
        footer.Children.Add(_apply);
        var main = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"), Margin = new Thickness(40, 36, 40, 30) };
        main.Children.Add(_step);
        Grid.SetRow(_heading, 1);
        main.Children.Add(_heading);
        Grid.SetRow(_lead, 2);
        main.Children.Add(_lead);
        Grid.SetRow(_scroll, 3);
        main.Children.Add(_scroll);
        Grid.SetRow(footer, 4);
        main.Children.Add(footer);

        _close = Ui.Press(new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M2.5,2.5 L9.5,9.5 M9.5,2.5 L2.5,9.5"), Stroke = Gfx.B(theme.Secondary), StrokeThickness = 1.6,
            StrokeLineCap = PenLineCap.Round, Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        }, Close, (b, on) => b.Background = on ? Gfx.B(theme.Hover) : Brushes.Transparent);
        _close.Width = _close.Height = 32;
        _close.CornerRadius = new CornerRadius(16);
        _close.HorizontalAlignment = HorizontalAlignment.Right;
        _close.VerticalAlignment = VerticalAlignment.Top;
        _close.Margin = new Thickness(0, 14, 14, 0);

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("236,*"), Background = Brushes.Transparent };
        root.Children.Add(rail);
        Grid.SetColumn(main, 1);
        root.Children.Add(main);
        Grid.SetColumn(_close, 1);
        root.Children.Add(_close);
        // El fondo (fuera de lo que se toca) mueve la ventana.
        root.PointerPressed += (_, e) => { if (e.GetCurrentPoint(root).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        Content = root;

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
        Opened += (_, _) => _next.Focus();
        Closed += (_, _) =>
        {
            // Sin aplicar, el idioma vuelve a ser el de antes.
            if (!_applied && L.Language != _startLanguage) L.Use(_startLanguage);
            _done.TrySetResult(false);
        };
        GoTo(0);
    }

    static string Version()
    {
        var v = typeof(SetupWindow).Assembly.GetName().Version;
        return v is null ? "" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    string[] StepNames => _order.Select(s => s switch
    {
        Step.Welcome => L.T("Bienvenida"),
        Step.Terms => L.T("Términos"),
        Step.Install => L.T("Instalación"),
        Step.Hooks => L.T("Avisos de permisos"),
        Step.Companion => L.T("Su compañero"),
        Step.Models => L.T("Modelos y voz"),
        _ => L.T("Inicio y avisos"),
    }).ToArray();

    /// <summary>El primer paso del que todavía no se puede pasar: los términos sin aceptar o una carpeta que no sirve.</summary>
    int Gate => !TermsAccepted ? Array.IndexOf(_order, Step.Terms) : _installProblem is not null ? Array.IndexOf(_order, Step.Install) : Steps - 1;

    bool Has(PlatformFeatures feature) => (_features & feature) == feature;

    void Apply()
    {
        if (_current != Steps - 1) return;
        _applied = true;
        _done.TrySetResult(true);
        Close();
    }

    /// <summary>Muestra un paso: lo rehace entero, así también sirve al cambiar de idioma.</summary>
    void GoTo(int index)
    {
        _current = Math.Clamp(index, 0, Gate);
        var last = _current == Steps - 1;
        var blocked = !last && _current >= Gate;
        Title = L.T("Configurar Claupet");
        _version.Text = L.T("Versión {0}", Version());
        _soon.Text = L.T("Próximamente").ToUpper();
        Ui.AutomationName(_close, L.T("Cerrar"));
        // Sin los términos aceptados, cerrar el asistente cierra Claupet.
        SetText(_later, _laterText, TermsAccepted ? L.T("Ahora no") : L.T("Salir"));
        SetText(_back, _backText, L.T("Atrás"));
        SetText(_next, _nextText, L.T("Siguiente"));
        SetText(_apply, _applyText, L.T("Aplicar"));
        _back.IsVisible = _current > 0;
        _next.IsVisible = !last;
        _next.IsEnabled = !blocked;
        _next.Opacity = blocked ? 0.5 : 1;
        _apply.IsVisible = last;
        Rail();

        _body.Children.Clear();
        switch (_order[_current])
        {
            case Step.Welcome: Welcome(); break;
            case Step.Terms: TermsStep(); break;
            case Step.Install: if (OperatingSystem.IsWindows()) Install(); break;
            case Step.Hooks: Hooks(); break;
            case Step.Companion: Companion(); break;
            case Step.Models: ModelsAndVoice(); break;
            default: Startup(); break;
        }
        _scroll.Offset = default;
        // Enter sigue con «Siguiente»; en el último paso (o con «Siguiente» apagado) nada tiene el foco, así que un
        // Enter suelto no hace nada.
        if (last || blocked) FocusManager?.ClearFocus();
        else _next.Focus();
    }

    void Header(string heading, string lead)
    {
        _step.Text = L.T("PASO {0} DE {1}", _current + 1, Steps);
        _heading.Text = heading;
        _lead.Text = lead;
    }

    /// <summary>Los pasos en la columna: los hechos con ✓, el actual resaltado; con un clic se va a cualquiera.</summary>
    void Rail()
    {
        _rail.Children.Clear();
        var names = StepNames;
        for (var i = 0; i < names.Length; i++)
        {
            var index = i;
            var current = i == _current;
            var dot = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(10, 0),
                Background = i <= _current ? Gfx.B(_t.Accent) : Brushes.Transparent,
                BorderBrush = Gfx.B(_t.Tertiary), BorderThickness = new Thickness(i <= _current ? 0 : 1),
                Child = i < _current
                    ? new Avalonia.Controls.Shapes.Path
                    {
                        Data = Geometry.Parse("M6.2,11.4 L9.7,14.9 L15.8,7.9"), Stroke = Gfx.B(OnAccent), StrokeThickness = 2,
                        StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, Width = 22, Height = 22,
                    }
                    : Label((i + 1).ToString(), 11.3, current ? OnAccent : _t.Secondary, align: TextAlignment.Center),
            };
            if (dot.Child is TextBlock number) number.VerticalAlignment = VerticalAlignment.Center;
            var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            line.Children.Add(dot);
            line.Children.Add(Label(names[i], 12.7, current ? _t.Text : _t.Secondary, current ? FontWeight.SemiBold : FontWeight.Normal));
            var item = Ui.Press(line, () => GoTo(index),
                (b, on) => { if (!current) b.Background = on ? Gfx.B(Gfx.Blend(_railColor, _t.Raised, 0.5)) : Brushes.Transparent; }, names[i]);
            item.Height = 40;
            item.CornerRadius = new CornerRadius(12);
            if (current) item.Background = Gfx.B(_t.Raised);
            _rail.Children.Add(item);
        }
    }

    // ── Pasos ────────────────────────────────────────────────────────────────────────────────

    void Welcome()
    {
        Header(L.T("Le damos la bienvenida a Claupet"),
            L.T("Le muestra el uso de Claude Code y le avisa cuando una sesión pide permiso. Nada cambia hasta que pulse «Aplicar»."));
        var languages = Segmented(L.Choices.Select(c => c.Label).ToArray(), Array.FindIndex(L.Choices, c => c.Id == Language), i =>
        {
            Language = L.Choices[i].Id;
            L.Use(Language); // el asistente ya en el idioma elegido; la app se reinicia al aplicar
            Dispatcher.UIThread.Post(() => GoTo(0));
        });
        Add(Field(L.T("Idioma"), languages));
        Add(Eyebrow(L.T("NOVEDADES"), 18));
        // Lo nuevo que ya está en este sistema; lo de Windows aparece cuando llega aquí.
        var news = new List<(string, string, string?)>
        {
            ("Claupet Console", L.T("El chat, el editor y todos los ajustes en una ventana; se abre desde el menú del ícono."), L.T("Nuevo")),
            (L.T("Respuestas"), L.T("Quién responde cada pregunta: sin IA, el modelo local o la nube."), null),
        };
        if (Has(PlatformFeatures.Extensions)) news.Add((L.T("Extensiones"), L.T("Mascotas, voces, sonidos y habilidades en paquetes firmados."), null));
        if (Has(Voice)) news.Add((L.T("Voz"), L.T("Háblele con el micrófono y escuche las respuestas."), null));
        if (Has(PlatformFeatures.Island)) news.Add((L.T("Isla"), L.T("Sesiones, permisos y un chat rápido en el borde de arriba."), null));
        Add(Cards(news.ToArray()));
    }

    void TermsStep()
    {
        Header(L.T("Términos y condiciones"), L.T("Léalos antes de seguir: para usar Claupet hay que aceptarlos."));
        Add(new Border
        {
            Height = 236, CornerRadius = new CornerRadius(14), Background = Gfx.B(_t.Raised), BorderBrush = Gfx.B(_t.Border),
            BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 10),
            Child = new ScrollViewer
            {
                Content = Label(Terms.Text, 11.7, _t.Text, margin: new Thickness(14, 12, 18, 12)),
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            },
        });
        Add(new SwitchRow(_t, L.T("Acepto los términos y condiciones"), L.T("Sin aceptarlos, Claupet se cierra."), TermsAccepted, true, v =>
        {
            TermsAccepted = v;
            Dispatcher.UIThread.Post(() => GoTo(_current)); // «Siguiente», «Salir» y los pasos siguientes
        }));
    }

    /// <summary>La carpeta donde se instala (solo en Windows: el paso no sale en los demás).</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    void Install()
    {
        if (InstallFolder is not { } folder)
        {
            Header(L.T("Instalación"), L.T("Claupet ya está instalada en este equipo."));
            var here = System.IO.Path.GetDirectoryName(_exePath) ?? "";
            Add(ActionRow(L.T("Carpeta"), here, L.T("Abrir"), () =>
            {
                try { System.Diagnostics.Process.Start("explorer.exe", $"\"{here}\"")?.Dispose(); }
                catch (Exception ex) { ErrorLog.Write(ex); }
            }));
            Add(DesktopRow());
            Add(Note(L.T("Para quitarla, use Configuración › Aplicaciones › Claupet › Desinstalar.")));
            return;
        }
        Header(L.T("Instalación"),
            L.T("Claupet se instala en una carpeta propia, como cualquier app: queda en el menú Inicio y en Configuración › Aplicaciones, desde donde se desinstala."));
        Add(ActionRow(L.T("Carpeta"), folder, L.T("Cambiar…"), async () =>
        {
            try
            {
                var start = await StorageProvider.TryGetFolderFromPathAsync(Directory.Exists(folder) ? folder : System.IO.Path.GetDirectoryName(folder) ?? Account.Home);
                var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = L.T("Elija dónde instalar Claupet"), AllowMultiple = false, SuggestedStartLocation = start,
                });
                if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } chosen) return;
                InstallFolder = Installation.FolderFor(chosen);
                _installProblem = Installation.Problem(InstallFolder);
                GoTo(_current);
            }
            catch (Exception ex) { ErrorLog.Write(ex); }
        }));
        if (_installProblem is { } problem) Add(Label(problem, 11.7, _t.Danger, margin: new Thickness(0, 2, 0, 8)));
        Add(DesktopRow());
        if (Installation.Folder is { } current && Installation.InstalledExe is not null && Account.SamePath(folder, current))
            Add(Note(L.T("Ya hay una Claupet instalada ahí: se reemplaza por esta versión ({0}).", Version())));
        Add(Note(L.T("Sus ajustes y modelos siguen en {0}. Después de instalarla puede borrar el archivo que descargó.", DataFolder.Root)));
    }

    SwitchRow DesktopRow() => new(_t, L.T("Acceso directo en el escritorio"), L.T("El del menú Inicio se crea siempre."), DesktopShortcut, true, v => DesktopShortcut = v);

    /// <summary>Como una fila con interruptor, pero con un botón discreto a la derecha.</summary>
    Control ActionRow(string title, string detail, string action, Action onClick)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 4) };
        var text = new StackPanel { Margin = new Thickness(0, 8, 20, 8), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Label(title, 13, _t.Text));
        text.Children.Add(Label(detail, 11.7, _t.Tertiary, margin: new Thickness(0, 1, 0, 0)));
        row.Children.Add(text);
        var (button, label) = Pill(PillKind.Quiet, onClick);
        SetText(button, label, action);
        button.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(button, 1);
        row.Children.Add(button);
        return row;
    }

    void Hooks()
    {
        Header(L.T("Avisos de permisos"),
            L.T("Para avisarle cuando una sesión pide permiso, Claupet agrega un hook al settings.json de cada carpeta de Claude Code. Antes de cambiarlo guarda un respaldo."));
        if (_rows.Count == 0)
        {
            Add(Note(L.T("No se encontró ninguna carpeta de Claude Code.")));
            return;
        }
        foreach (var row in _rows)
        {
            var (detail, enabled) = row.State switch
            {
                HookInstaller.State.Installed => (L.T("Ya está configurado."), false),
                HookInstaller.State.Outdated => (L.T("Apunta a otra ruta: se actualizará."), true),
                HookInstaller.State.Unreadable => (L.T("Su settings.json tiene errores; no se toca."), false),
                _ => (L.T("Se agregará el hook."), true),
            };
            var on = row.State == HookInstaller.State.Installed || _hooksOn.Contains(row.Dir);
            Add(new SwitchRow(_t, row.Name, detail, on, enabled, v =>
            {
                if (v) _hooksOn.Add(row.Dir);
                else _hooksOn.Remove(row.Dir);
            }));
        }
    }

    void Companion()
    {
        Header(L.T("Su compañero"), L.T("Elija cómo quiere ver a Claupet. Todo se puede cambiar después en Ajustes."));
        // Los personajes (el chibi, Dokki, Gemma, Ciel) y la clásica al final.
        string[] ids = [.. ChibiSprite.Skins.Select(s => s.Id), Mascots.Classic];
        SwitchRow? desk = null;
        var mascots = Segmented([.. ids.Select(Mascots.Label)], Array.IndexOf(ids, Mascot ?? _currentMascot), i =>
        {
            Mascot = ids[i];
            if (Mascots.IsCharacter(Mascot) && desk is not null) desk.On = true; // un personaje sale al escritorio
        });
        Add(Field(L.T("Mascota"), mascots));
        // Bailar con la música, por ahora, solo en Windows (Ajustes tampoco lo ofrece en los demás).
        desk = new SwitchRow(_t, L.T("Mascota en el escritorio"), OperatingSystem.IsWindows()
            ? L.T("Le acompaña sobre las ventanas, reacciona a sus sesiones y baila con la música.")
            : L.T("Le acompaña sobre las ventanas y reacciona a sus sesiones."), DesktopMascot, true, v => DesktopMascot = v);
        Add(desk);
        if (Has(PlatformFeatures.MascotChat))
            Add(new SwitchRow(_t, L.T("Chat con la mascota"), L.T("Escríbale a la mascota para pedirle cosas a Claude Code."), Chat, true, v => Chat = v));
        if (Has(PlatformFeatures.Island))
            Add(new SwitchRow(_t, L.T("Isla"), L.T("Asoma arriba de la pantalla con sus sesiones, los permisos y un chat rápido."), Island, true, v => Island = v));
        Soon((PlatformFeatures.MascotChat, L.T("Chat con la mascota"), L.T("Escríbale a la mascota para pedirle cosas a Claude Code.")),
            (PlatformFeatures.Island, L.T("Isla"), L.T("Asoma arriba de la pantalla con sus sesiones, los permisos y un chat rápido.")));
    }

    void ModelsAndVoice()
    {
        Header(L.T("Modelos y voz"), (_features & Models) == 0
            ? L.T("La voz, el modelo local y las extensiones todavía no están en este sistema: llegan en una etapa siguiente.")
            : L.T("Los modelos corren en su equipo: se descargan una vez, en segundo plano, y luego funcionan sin internet."));
        if (Has(PlatformFeatures.Mic))
            Add(new SwitchRow(_t, L.T("Hablarle con el micrófono"), SpeechToTextExtension is { } stt ? L.T("Lo entiende la extensión {0}.", stt)
                : VoiceModel.Ready ? L.T("El modelo de voz ya está en este equipo.")
                : L.T("Descarga el modelo de voz Whisper ({0} MB).", (VoiceModel.Size + 500_000) / 1_000_000), Mic, true, v =>
            {
                Mic = v;
                if (v && !Chat && !Island) Chat = true; // el micrófono necesita a quién hablarle
            }));
        if (Has(PlatformFeatures.ReadAloud))
            Add(new SwitchRow(_t, L.T("Leer las respuestas en voz alta"),
                TextToSpeechExtension is { } tts ? L.T("Con la voz de la extensión {0}.", tts)
                : !ClaupetVoices.IsClaupet(_readVoice) ? L.T("Con la voz elegida en Ajustes › Asistente.")
                : ClaupetVoiceModel.Ready ? L.T("La voz de Claupet ya está en este equipo.")
                : OperatingSystem.IsWindows() ? L.T("Descarga la voz de Claupet ({0} MB); mientras, lee la de Windows.", (ClaupetVoiceModel.Size + 500_000) / 1_000_000)
                : L.T("Descarga la voz de Claupet ({0} MB); mientras, lee la del sistema.", (ClaupetVoiceModel.Size + 500_000) / 1_000_000),
                ReadAloud, true, v => ReadAloud = v));
        if (Has(PlatformFeatures.Extensions) && InstallPackage is { } install)
        {
            var (button, text) = Pill(PillKind.Quiet, () => _ = PickPackageAsync(install));
            SetText(button, text, L.T("Instalar…"));
            Add(Field(L.T("Extensiones"), button));
            Add(Label(L.T("Voces, modelos de voz y asistentes de terceros en paquetes firmados."), 11.7, _t.Tertiary, margin: new Thickness(0, -2, 0, 8)));
        }
        if (Has(PlatformFeatures.LocalModel))
        {
            // El modelo local: la mascota responde con él, sin internet ni tokens; la isla sigue con Claude.
            var local = LocalModel.Find(LocalModelId);
            var detail = LocalModel.Ready(local)
                ? L.T("{0} ya está en este equipo; usa unos {1} de RAM al responder.", local.Name, local.Ram)
                : L.T("Descarga {0} ({1} MB) y usa unos {2} de RAM al responder.", local.Name, (LocalModel.Missing(local) + 500_000) / 1_000_000, local.Ram);
            Add(new SwitchRow(_t, L.T("La mascota responde con un modelo local"), detail, LocalModelOn, true, v => LocalModelOn = v));
            var sizes = Segmented(LocalModel.Choices.Select(c => c.Name.Replace("Qwen3.5 ", "")).ToArray(),
                Array.FindIndex(LocalModel.Choices, c => c.Id == LocalModelId), i =>
                {
                    LocalModelId = LocalModel.Choices[i].Id;
                    LocalModelOn = true;
                    Dispatcher.UIThread.Post(() => GoTo(_current)); // el detalle dice lo que baja y la RAM de este
                });
            Add(Field(L.T("Tamaño (Qwen3.5)"), sizes));
        }
        Soon((Voice, L.T("Voz"), L.T("Háblele con el micrófono y escuche las respuestas.")),
            (PlatformFeatures.LocalModel, L.T("Modelo de apoyo local"),
                L.T("Un modelo pequeño en su equipo que responde lo simple sin gastar tokens y funciona sin internet. Se activa a pedido: usa más memoria.")),
            (PlatformFeatures.Extensions, L.T("Extensiones"), L.T("Voces, modelos de voz y asistentes de terceros en paquetes firmados.")));
    }

    void Startup()
    {
        Header(L.T("Inicio y avisos"), L.T("Último paso. Revise esto y pulse «Aplicar»."));
        Add(new SwitchRow(_t, OperatingSystem.IsWindows() ? L.T("Iniciar con Windows") : L.T("Iniciar con el sistema"),
            OperatingSystem.IsMacOS() ? L.T("Claupet se abre en la barra de menús al encender el equipo.") : L.T("Claupet se abre en la bandeja al encender el equipo."),
            StartWithSystem, true, v => StartWithSystem = v));
        Add(new SwitchRow(_t, L.T("Notificaciones"),
            L.T("Cuando una sesión termina o falla, y cuando el uso se acerca al límite."), NotificationsOn, true, v => NotificationsOn = v));
        Add(Eyebrow(L.T("PRÓXIMAMENTE"), 18));
        Add(Cards(("Claupet IDE", L.T("Una extensión que lleva el editor de código a nivel de IDE: vista previa de interfaces y webs, y artifacts."), L.T("Extensión"))));
    }

    /// <summary>
    /// Lo que la app de Windows ofrece en este paso y esta todavía no (Features), en tarjetas bajo «Próximamente». Al traer
    /// una de esas funciones, su fila (la de SetupAssistant.cs) va en el paso y su tarjeta deja de salir.
    /// </summary>
    void Soon(params (PlatformFeatures Feature, string Title, string Text)[] items)
    {
        var missing = items.Where(i => !Has(i.Feature)).Select(i => (i.Title, i.Text, (string?)null)).ToArray();
        if (missing.Length == 0) return;
        Add(Eyebrow(L.T("PRÓXIMAMENTE"), 18));
        Add(Cards(missing));
    }

    // ── Piezas ───────────────────────────────────────────────────────────────────────────────

    void Add(Control control) => _body.Children.Add(control);

    static TextBlock Label(string text, double size, SD.Color color, FontWeight weight = FontWeight.Normal, FontFamily? family = null,
        Thickness margin = default, TextAlignment align = TextAlignment.Left) => new()
    {
        Text = text, FontSize = size, Foreground = Gfx.B(color), FontWeight = weight, FontFamily = family ?? Sans,
        TextWrapping = TextWrapping.Wrap, Margin = margin, TextAlignment = align,
    };

    TextBlock Eyebrow(string text, double top) => Label(text, 10.7, _t.Tertiary, FontWeight.SemiBold, margin: new Thickness(0, top, 0, 8));

    TextBlock Note(string text) => Label(text, 11.7, _t.Tertiary, margin: new Thickness(0, 14, 0, 0));

    /// <summary>Una etiqueta a la izquierda y su control a la derecha.</summary>
    Control Field(string label, Control control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 32, Margin = new Thickness(0, 0, 0, 6) };
        var caption = Label(label, 13, _t.Text);
        caption.VerticalAlignment = VerticalAlignment.Center;
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        row.Children.Add(caption);
        row.Children.Add(control);
        return row;
    }

    /// <summary>Opciones en píldora, una elegida (o ninguna, con -1); se eligen con el mouse o con Tab y Enter.</summary>
    Control Segmented(string[] options, int picked, Action<int> pick)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var pills = new List<(Border Pill, TextBlock Text)>();
        void Paint()
        {
            for (var i = 0; i < pills.Count; i++)
            {
                pills[i].Pill.Background = i == picked ? Gfx.B(_t.Dark ? SD.Color.FromArgb(0x3D, 0x3D, 0x3A) : SD.Color.White) : Brushes.Transparent;
                pills[i].Text.Foreground = Gfx.B(i == picked ? _t.Text : _t.Secondary);
            }
        }
        for (var i = 0; i < options.Length; i++)
        {
            var index = i;
            var text = Label(options[i], 12.7, _t.Secondary);
            text.VerticalAlignment = VerticalAlignment.Center;
            var pill = Ui.Press(text, () =>
            {
                if (index == picked) return;
                picked = index;
                Paint();
                pick(index);
            }, (b, on) => { if (index != picked) pills[index].Text.Foreground = Gfx.B(on ? _t.Text : _t.Secondary); }, options[i]);
            pill.Height = 28;
            pill.Padding = new Thickness(14, 0);
            pill.CornerRadius = new CornerRadius(14);
            pills.Add((pill, text));
            row.Children.Add(pill);
        }
        Paint();
        return new Border
        {
            Child = row, Padding = new Thickness(4), CornerRadius = new CornerRadius(18),
            Background = Gfx.B(_t.Dark ? SD.Color.FromArgb(0x30, 0x30, 0x2E) : SD.Color.FromArgb(0xF0, 0xEE, 0xE6)),
        };
    }

    /// <summary>Tarjetas de dos en dos (una sola, a todo lo ancho): título, una etiqueta opcional y una línea.</summary>
    Control Cards(params (string Title, string Text, string? Badge)[] cards)
    {
        var columns = cards.Length == 1 ? 1 : 2;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns == 1 ? "*" : "*,*"), ColumnSpacing = 12, RowSpacing = 12 };
        for (var i = 0; i < cards.Length; i++)
        {
            if (i % columns == 0) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var (title, text, badge) = cards[i];
            var inner = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto") };
            inner.Children.Add(Label(title, 13, _t.Text, FontWeight.SemiBold));
            if (badge is not null)
            {
                var tag = new Border
                {
                    CornerRadius = new CornerRadius(9), Padding = new Thickness(6, 1), VerticalAlignment = VerticalAlignment.Top,
                    Background = Gfx.B(SD.Color.FromArgb(_t.Dark ? 56 : 36, _t.Accent)),
                    Child = Label(badge, 10.3, _t.Dark ? Gfx.Blend(_t.Accent, SD.Color.White, 0.3) : Gfx.Blend(_t.Accent, SD.Color.Black, 0.15), FontWeight.SemiBold),
                };
                Grid.SetColumn(tag, 1);
                inner.Children.Add(tag);
            }
            var line = Label(text, 11.7, _t.Secondary, margin: new Thickness(0, 6, 0, 0));
            Grid.SetRow(line, 1);
            Grid.SetColumnSpan(line, 2);
            inner.Children.Add(line);
            var card = new Border
            {
                Child = inner, Padding = new Thickness(14), CornerRadius = new CornerRadius(14),
                Background = Gfx.B(_t.Raised), BorderBrush = Gfx.B(_t.Border), BorderThickness = new Thickness(1),
            };
            Grid.SetRow(card, i / columns);
            Grid.SetColumn(card, i % columns);
            grid.Children.Add(card);
        }
        return grid;
    }

    enum PillKind { Primary, Quiet, Link }

    /// <summary>Botón en píldora: el principal (acento), uno discreto o un enlace. Ninguno es el botón por defecto.</summary>
    (Border, TextBlock) Pill(PillKind kind, Action onClick)
    {
        var text = Label("", 13, kind switch { PillKind.Primary => OnAccent, PillKind.Quiet => _t.Text, _ => _t.Secondary },
            kind == PillKind.Primary ? FontWeight.SemiBold : FontWeight.Normal);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.TextWrapping = TextWrapping.NoWrap;
        var rest = kind switch { PillKind.Primary => Gfx.B(_t.Accent), PillKind.Quiet => Gfx.B(_t.Raised), _ => (IBrush)Brushes.Transparent };
        var pill = Ui.Press(text, onClick, (b, on) =>
        {
            if (kind == PillKind.Primary) b.Background = on ? Gfx.B(Gfx.Blend(_t.Accent, SD.Color.White, 0.25)) : rest;
            else if (kind == PillKind.Quiet) b.Background = on ? Gfx.B(_t.Hover) : rest;
            else text.Foreground = Gfx.B(on ? _t.Text : _t.Secondary);
        });
        pill.Background = rest;
        pill.Height = 38;
        pill.CornerRadius = new CornerRadius(19);
        pill.Padding = new Thickness(kind == PillKind.Link ? 8 : 18, 0);
        if (kind == PillKind.Quiet)
        {
            pill.BorderBrush = Gfx.B(_t.Border);
            pill.BorderThickness = new Thickness(1);
        }
        return (pill, text);
    }

    /// <summary>Elige un paquete .claupet con el selector del sistema y lo manda a revisar.</summary>
    async Task PickPackageAsync(Func<string, Task> install)
    {
        if (!StorageProvider.CanOpen) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("Instalar una extensión"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(L.T("Paquetes de Claupet")) { Patterns = ["*.claupet"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) await install(path);
    }

    static void SetText(Border pill, TextBlock text, string value)
    {
        text.Text = value;
        Ui.AutomationName(pill, value);
    }

    /// <summary>
    /// Deja el hook donde se marcó (o ya estaba) apuntando a exePath: esta copia o, al instalar, la de la carpeta instalada.
    /// Devuelve una línea por carpeta que cambió.
    /// </summary>
    public List<string> ApplyHooks(string exePath)
    {
        var lines = new List<string>();
        foreach (var row in _rows.Where(r => r.State == HookInstaller.State.Installed || r.State is HookInstaller.State.Missing or HookInstaller.State.Outdated && _hooksOn.Contains(r.Dir)))
        {
            var state = HookInstaller.Status(row.Dir, exePath);
            if (state is HookInstaller.State.Installed or HookInstaller.State.Unreadable) continue;
            try
            {
                var backup = HookInstaller.Install(row.Dir, exePath);
                HooksChanged++;
                var verb = state == HookInstaller.State.Outdated ? L.T("hook actualizado") : L.T("hook agregado");
                lines.Add(backup is null ? $"{row.Name}: {verb}." : L.T("{0}: {1} (respaldo: {2}).", row.Name, verb, System.IO.Path.GetFileName(backup)));
            }
            catch (Exception ex)
            {
                lines.Add(L.T("{0}: no se pudo cambiar ({1}).", row.Name, ex.Message));
            }
        }
        return lines;
    }

    /// <summary>Una opción con título y explicación a la izquierda y su interruptor a la derecha; el texto también lo cambia.</summary>
    sealed class SwitchRow : Grid
    {
        readonly Theme _t;
        readonly Border _track;
        readonly TranslateTransform _knob;
        readonly Action<bool> _changed;
        bool _on;

        public SwitchRow(Theme theme, string title, string detail, bool on, bool enabled, Action<bool> changed)
        {
            _t = theme;
            _on = on;
            _changed = changed;
            ColumnDefinitions = new ColumnDefinitions("*,Auto");
            Margin = new Thickness(0, 0, 0, 4);
            var text = new StackPanel { Margin = new Thickness(0, 8, 20, 8), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
            text.Children.Add(Label(title, 13, enabled ? theme.Text : theme.Secondary));
            text.Children.Add(Label(detail, 11.7, theme.Tertiary, margin: new Thickness(0, 1, 0, 0)));
            if (enabled)
            {
                text.Cursor = Ui.Hand;
                text.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(text).Properties.IsLeftButtonPressed) return;
                    e.Handled = true;
                    On = !On;
                };
            }
            Children.Add(text);

            _knob = new TranslateTransform(on ? 18 : 0, 0)
            {
                Transitions = [new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(140) }],
            };
            _track = new Border
            {
                Width = 42, Height = 24, CornerRadius = new CornerRadius(12),
                Child = new Ellipse
                {
                    Width = 18, Height = 18, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(3, 0), RenderTransform = _knob,
                },
            };
            var toggle = Ui.Press(_track, () => On = !On, name: title);
            toggle.CornerRadius = new CornerRadius(12);
            toggle.VerticalAlignment = VerticalAlignment.Center;
            toggle.IsEnabled = enabled;
            toggle.Opacity = enabled ? 1 : 0.45;
            Grid.SetColumn(toggle, 1);
            Children.Add(toggle);
            Paint();
        }

        public bool On
        {
            get => _on;
            set
            {
                if (_on == value) return;
                _on = value;
                Paint();
                _changed(value);
            }
        }

        void Paint()
        {
            _knob.X = _on ? 18 : 0;
            _track.Background = Gfx.B(_on ? _t.Accent : _t.Track);
        }
    }
}
