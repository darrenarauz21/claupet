using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using SD = System.Drawing;

namespace Claupet;

/// <summary>
/// El ícono de la bandeja (barra de menús en macOS) y todo lo que coordina: consultas, sesiones,
/// actividad, avisos, sonidos, configuración, el chat con la mascota, la isla y Claupet Console. Es la misma lógica que TrayApp en Windows.
/// </summary>
sealed partial class TrayController
{
    readonly App _app;
    readonly TrayIcon _tray = new();
    readonly PopupWindow _popup = new();
    readonly DispatcherTimer _timer = new();
    readonly DispatcherTimer _sessionTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly SessionMonitor _monitor = new();
    readonly ActivityTracker _activity = new();
    readonly RefreshAnimator _refresh = new();
    readonly Dictionary<RefreshFrame, WindowIcon> _refreshIcons = new();
    readonly AppSettings _settings;
    readonly Dictionary<string, UsageSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Profile> _profiles = new(StringComparer.OrdinalIgnoreCase);
    readonly NoticeDetector _detector = new();
    readonly ToastStack _toasts = new();
    readonly Sounds _sounds = new();
    readonly ChibiDirector _director = new();

    readonly BubbleWindow _bubble = new();
    MascotWindow? _mascotWindow;
    Theme _theme = Theme.Light;
    LocalTotals? _local;
    DateTime? _updated;
    bool _busy, _scanning, _counting;
    /// <summary>Las sesiones vencidas se renuevan al abrir la app; si no había conexión, en la próxima actualización.</summary>
    bool _renewAtStart = true;
    Action? _pendingApply;
    IReadOnlyList<SessionInfo>? _lastSessions;
    int _sampleIndex, _noticesShown;

    UsageView View => _popup.View;
    static string ExePath => Environment.ProcessPath ?? "";

    public TrayController(App app, bool showOnStart, bool sessionsView, bool activityView, bool demo, bool autoSetup)
    {
        _app = app;
        _settings = SettingsStore.Load();
        _director.Settings = _settings;
        if (SettingsStore.DetectNew(_settings)) SettingsStore.Save(_settings);
        _settings.Selected ??= _settings.Accounts.FirstOrDefault()?.ConfigDir;
        Sounds.EnsureDefaults();

        Theme.SelectedPalette = _settings.Palette;
        Theme.SelectedCustom = _settings.CustomTheme;
        Mascots.Choose(_settings, Mascots.ChoiceOf(_settings)); // deja en orden lo guardado por versiones anteriores
        Mascots.Selected = _settings.Mascot;
        // --mascota <id>: ese personaje solo en esta ejecución (para verlo sin tocar los ajustes).
        if (ArgAfter("--mascota") is { } shown && Mascots.IsCharacter(shown)) Mascots.Selected = shown;
        _director.Skin = Mascots.Skin;
        _refresh.Style = RefreshSprite.Resolve(Mascots.Selected == _settings.Mascot ? _settings.Animation : RefreshSprite.Mascot, Mascots.Selected);
        ApplyTheme();
        app.ActualThemeVariantChanged += (_, _) => ApplyTheme();

        // Menú del ícono: corto; lo demás está en la vista Ajustes (engranaje del pie).
        var menu = new NativeMenu();
        void Item(string header, Action action)
        {
            var item = new NativeMenuItem(header);
            item.Click += (_, _) => action();
            menu.Add(item);
        }
        Item(L.T("Abrir"), ShowForm);
        Item(L.T("Actualizar"), () => _ = RefreshAsync(renewSelected: true));
        Item(L.T("Nueva sesión…"), () => _ = NewSessionAsync(null));
        Item("Claupet Console", () => _ = OpenConsoleAsync());
        menu.Add(new NativeMenuItemSeparator());
        Item(L.T("Ajustes…"), () => { ShowForm(); View.ShowSettings(); });
        menu.Add(new NativeMenuItemSeparator());
        Item(L.T("Salir"), Quit);

        _tray.Menu = menu;
        _tray.ToolTipText = "Claupet";
        _tray.Icon = Gfx.PercentIcon(null, _theme.Tertiary);
        _tray.Clicked += (_, _) => Toggle();
        _tray.IsVisible = true;
        TrayIcon.SetIcons(app, new TrayIcons { _tray });

        View.RefreshRequested += () => _ = RefreshAsync(renewSelected: true);
        View.AddRequested += () => _ = AddAccountAsync();
        View.RemoveRequested += a => _ = RemoveAccountAsync(a);
        View.RenameRequested += a => _ = RenameAccountAsync(a);
        View.SelectedChanged += account =>
        {
            _settings.Selected = account.ConfigDir;
            SettingsStore.Save(_settings);
            UpdateTray();
        };
        View.SettingRequested += OnQuickSetting;
        if (Enum.TryParse<UsageView.SettingsTab>(_settings.SettingsTab, out var tab)) View.SetSettingsTab(tab);
        View.SettingsTabChanged += t =>
        {
            _settings.SettingsTab = t.ToString();
            SettingsStore.Save(_settings);
        };
        View.NewSessionRequested += session => _ = NewSessionAsync(session);
        SetupExtensions();
        WatchMascotScreens(); // también sin mascota: con la sesión bloqueada se pausa la isla
        StartConsole();
        StartUpdates();
        _ = StartExtensionsAsync();
        PushSettings();
        _toasts.Clicked += OpenNotice;
        _bubble.Clicked += OpenNotice;
        _toasts.Shown += () => _noticesShown++;

        // Sesiones: cada 1,5 s con la ventana abierta y cada 3 s cerrada (solo con notificaciones).
        _sessionTimer.Tick += (_, _) => _ = ScanSessionsAsync();
        _sessionTimer.Start();
        _popup.PropertyChanged += (_, e) =>
        {
            if (e.Property != Visual.IsVisibleProperty) return;
            _sessionTimer.Interval = TimeSpan.FromMilliseconds(_popup.IsVisible ? 1500 : 3000);
            if (_popup.IsVisible) _ = ScanSessionsAsync();
        };

        // La animación (la mascota u otro estilo) ocupa el ícono y la hora del pie mientras se actualiza.
        _refresh.FrameChanged += ShowRefreshFrame;
        _refresh.Completed += () =>
        {
            _pendingApply?.Invoke();
            _pendingApply = null;
            Render();
            var usage = _detector.Usage(_settings.Accounts, _snapshots);
            if (_settings.Notifications) usage.ForEach(ShowNotice);
        };

        // Sesiones y Actividad son una sola vista; --sessions sigue sirviendo.
        if (sessionsView || activityView) View.ShowActivity();

        _timer.Interval = TimeSpan.FromMinutes(Math.Max(1, _settings.RefreshMinutes));
        _timer.Tick += (_, _) => _ = RefreshAsync();
        _timer.Start();

        Dispatcher.UIThread.Post(async () =>
        {
            Render();
            if (showOnStart) ShowForm();
            if (demo) ShowSamples();
            // --settings: abre en Ajustes; --soltar <archivo>: como si se lo soltaran a la mascota; --hablar <wav>: la voz
            // entera con ese audio en vez del micrófono (con «Micrófono» encendido).
            if (Program.Args.Contains("--settings"))
            {
                View.ShowSettings();
                ShowForm();
            }
            if (ArgAfter("--soltar") is { } drop) _ = DemoDropAsync(drop);
            StartActions();
            StartLocalModel();
            if (OperatingSystem.IsWindows()) WeatherService.WindowsLocation = WindowsLocation.GetAsync;
            if (OwnsShortcuts && OperatingSystem.IsWindows())
            {
                // Los encendidos se crean si faltan y se corrigen si apuntan a otro .exe (se movió la carpeta); la entrada
                // de Configuración › Aplicaciones, al día si esta es la copia instalada (cambia con cada versión nueva).
                if (_settings.StartMenuShortcut) Shortcuts.Ensure(Shortcuts.StartMenu);
                if (_settings.DesktopShortcut) Shortcuts.Ensure(Shortcuts.Desktop);
                Installation.Refresh(ExePath, SemVersion.App.ToString());
            }
            UpdateMascotWindow();
            UpdateIsland();
            UpdateVoice();
            // --descargar-modelos: la abrió el asistente al reiniciar (idioma o instalación) con modelos por bajar.
            if (Program.Args.Contains(DownloadModelsFlag)) DownloadMissingModels();
            if (ArgAfter("--hablar") is { } wav && _voice is { } voice) _ = Task.Delay(1500).ContinueWith(_ => Dispatcher.UIThread.Post(() => _ = voice.DemoAsync(wav)));
            // Si se cerró (sin aceptar los términos) o se reinició (otro idioma, o se abre la instalada), no sigue.
            if (autoSetup && NeedsSetup() && !await ShowSetupAsync()) return;
            await RefreshAsync();
        });
    }

    /// <summary>renewSelected: «Actualizar» renueva la sesión vencida de la cuenta que se está viendo.</summary>
    async Task RefreshAsync(bool renewSelected = false)
    {
        if (_busy) return;
        _busy = true;
        View.SetBusy(true);
        _refresh.Start();
        try
        {
            if (SettingsStore.DetectNew(_settings)) SettingsStore.Save(_settings);
            var accounts = _settings.Accounts.ToList();
            var results = await Task.WhenAll(accounts.Select(a => UsageClient.FetchAsync(a)));
            var offline = await SessionRenewal.RenewExpiredAsync(accounts, results, a => _renewAtStart
                || (renewSelected && _settings.Selected is { } selected && Account.SamePath(a.ConfigDir, selected)));
            if (!offline) _renewAtStart = false;
            var profiles = await Task.Run(() => accounts.Select(UsageClient.ReadProfile).ToList());
            var updated = DateTime.Now;

            // Los datos nuevos se aplican cuando la animación termina, no antes.
            _pendingApply = () =>
            {
                for (var i = 0; i < accounts.Count; i++)
                {
                    var dir = accounts[i].ConfigDir;
                    var fresh = results[i];
                    if (fresh.Error is not null && _snapshots.TryGetValue(dir, out var old) && old.Limits.Count > 0)
                        fresh = old.WithError(fresh.Error, fresh.SessionExpired);
                    _snapshots[dir] = fresh;
                    _profiles[dir] = profiles[i];
                }
                _updated = updated;
            };
            _ = CountActivityAsync(accounts);
            _ = ReadAgentsAsync();
        }
        finally
        {
            _busy = false;
            View.SetBusy(false);
            _refresh.Finish();
        }
    }

    async Task CountActivityAsync(List<Account> accounts)
    {
        if (_counting) return;
        _counting = true;
        try
        {
            var dirs = accounts.Select(a => a.ConfigDir).ToList();
            // Los tokens de hoy (el pie de la ventana) salen del mismo recorrido, que solo lee lo nuevo.
            var (summary, today) = await Task.Run(() => (_activity.Scan(dirs), _activity.Today(dirs)));
            View.SetActivity(summary);
            _activitySummary = summary;
            _local = today;
            View.Render(_settings.Accounts, _settings.Selected, _snapshots, _profiles, _local, _updated);
            MaybeShowRecap(false);
        }
        finally { _counting = false; }
    }

    async Task ScanSessionsAsync()
    {
        if (_scanning || (!_popup.IsVisible && !_settings.Notifications && _mascotWindow is null && _island is null && !ExtensionsWatchSessions)) return;
        _scanning = true;
        try
        {
            var accounts = _settings.Accounts.ToList();
            var sessions = await Task.Run(() => _monitor.Scan(accounts));
            // Casi siempre nada cambió: entonces no se repinta (la ventana sí, si está abierta, por los tiempos que muestra).
            var key = SessionsKey(sessions);
            var changed = key != _sessionsKey;
            _sessionsKey = key;
            _lastSessions = sessions;
            if (changed || _popup.IsVisible) View.SetSessions(sessions);
            if (changed)
            {
                _director.SetSessions(sessions);
                BroadcastSessions(sessions);
                IslandSessions(sessions);
            }
            if (_settings.Notifications)
            {
                _detector.Sessions(sessions).ForEach(ShowNotice);
                _detector.HookEvents().ForEach(ShowNotice);
            }
        }
        finally { _scanning = false; }
    }

    int _sessionsKey;

    /// <summary>Lo que se ve de las sesiones, resumido: si es igual que en el tick anterior, nada cambió.</summary>
    static int SessionsKey(IReadOnlyList<SessionInfo> sessions)
    {
        var hash = new HashCode();
        foreach (var s in sessions)
        {
            hash.Add(s.SessionId);
            hash.Add(s.State);
            hash.Add(s.Steps);
            hash.Add(s.Turns);
            hash.Add(s.Action);
            hash.Add(s.Title);
            hash.Add(s.StatusAt);
            hash.Add(s.TurnStartedAt);
            hash.Add(s.TurnEndedAt);
            hash.Add(s.TodoDone);
            hash.Add(s.TodoTotal);
            hash.Add(s.ErrorAt);
            hash.Add(s.RetryAt);
            hash.Add(s.AwaitingAnswer);
            hash.Add(s.Flow.Count);
            hash.Add(s.Edits.Count);
            hash.Add(s.LastAnswer);
            hash.Add(s.Model);
        }
        return hash.ToHashCode();
    }

    void ShowNotice(Notice notice)
    {
        _toasts.Show(notice, _theme);
        _director.Notice(notice.Kind);
        // Con el chat abierto, la nubecita no lo repite encima.
        if (_mascotWindow is { } mascot && _settings.Speak && _chatBubble?.IsVisible != true) _bubble.Say(notice, mascot.Chibi, mascot.ScreenBounds);
        if (_settings.Sound && _island?.Muted != true) _sounds.Play(notice.Kind, _settings.SoundPack);
        _ = BroadcastAsync("notice", new System.Text.Json.Nodes.JsonObject
        {
            ["kind"] = notice.Kind.ToString().ToLowerInvariant(), ["title"] = notice.Title, ["text"] = notice.Subtitle, ["session"] = notice.SessionId,
        });
    }

    /// <summary>Lo que la vista Ajustes muestra: se manda de nuevo cada vez que algo cambia.</summary>
    void PushSettings()
    {
        View.SetSettingsState(((IConsoleApp)this).SettingsState()); // el mismo estado que Claupet Console
        _consoleHost?.PushSettings(); // Claupet Console, si está abierta
    }

    /// <summary>
    /// Una fila de «Ajustes rápidos»: hace lo mismo que en Claupet Console, pero las listas salen junto al cursor, en el
    /// popup (Choose las manda a _menu mientras tanto).
    /// </summary>
    void OnQuickSetting(string key)
    {
        _menu = (options, selected, pick) => View.ShowOptions(key, options, selected, pick);
        try { ((IConsoleApp)this).Act(key); }
        finally { _menu = null; }
    }

    async Task OnSettingAsync(string action)
    {
        if (action.StartsWith("pack:", StringComparison.Ordinal)) SetSoundPack(action[5..]);
        else if (action.StartsWith("palette:", StringComparison.Ordinal)) SetPalette(action[8..]);
        else if (action.StartsWith("mascot:", StringComparison.Ordinal)) PickMascot(action[7..]);
        else if (action.StartsWith("language:", StringComparison.Ordinal)) { SetLanguage(action[9..]); return; }
        else
        {
            switch (action)
            {
                case "notify": ToggleNotifications(); break;
                case "sound": ToggleSound(); break;
                case "startup": ToggleStartup(); break;
                case "desk": ToggleDesktopMascot(); break;
                case "speak": ToggleSpeak(); break;
                case "chat": ToggleChat(); break;
                case "drops": Flip(s => s.AnalyzeDrops = !s.AnalyzeDrops); break;
                case "copilot":
                    _settings.CopilotKey = !_settings.CopilotKey;
                    SettingsStore.Save(_settings);
                    UpdateKeys();
                    break;
                case "shortcuts": await ShowShortcutsAsync(); break;
                case "extensions": await OpenConsoleAsync(ExtensionsPanel.PageId); break;
                case "startmenu" when OwnsShortcuts && OperatingSystem.IsWindows():
                    _settings.StartMenuShortcut = !_settings.StartMenuShortcut;
                    SettingsStore.Save(_settings);
                    Shortcuts.Set(Shortcuts.StartMenu, _settings.StartMenuShortcut);
                    break;
                case "desktoplink" when OwnsShortcuts && OperatingSystem.IsWindows():
                    _settings.DesktopShortcut = !_settings.DesktopShortcut;
                    SettingsStore.Save(_settings);
                    Shortcuts.Set(Shortcuts.Desktop, _settings.DesktopShortcut);
                    break;
                case "island": FlipIsland(s => s.Island = !s.Island); break;
                case "hover": FlipIsland(s => s.IslandHover = !s.IslandHover); break;
                case "busy": FlipIsland(s => s.IslandWhileBusy = !s.IslandWhileBusy); break;
                case "recap": Flip(s => s.WeeklyRecap = !s.WeeklyRecap); break;
                case "recapnow": MaybeShowRecap(true); break;
                case "approvals": await SetupApprovalsAsync(); break;
                case "console": await OpenConsoleAsync("general"); break;
                case "mic": ToggleMic(); break;
                case "readaloud": ToggleReadAloud(); break;
                case "voice":
                    if (_modelDownload is { } download) download.Cancel();
                    else _ = DownloadVoiceModelAsync();
                    break;
                case "speech":
                    if (_speechDownload is { } speech) speech.Cancel();
                    else _ = DownloadSpeechAsync();
                    break;
                case "flowgest": Flip(s => s.FlowGestures = !s.FlowGestures); break;
                case "music": Flip(s => s.DanceToMusic = !s.DanceToMusic); break;
                case "noticegest": Flip(s => s.NoticeGestures = !s.NoticeGestures); break;
                case "greet": Flip(s => s.GreetOnAppear = !s.GreetOnAppear); break;
                case "angry": Flip(s => s.AngryOnClicks = !s.AngryOnClicks); break;
                case "alldesks":
                    Flip(s => s.MascotAllDesktops = !s.MascotAllDesktops);
                    UpdateMascotWindow(); // se rehace con el nuevo comportamiento
                    break;
                case "agentpets":
                    Flip(s => s.AgentPets = !s.AgentPets);
                    UpdateAgentPets();
                    break;
                case "sleep":
                    Flip(s => s.SleepWithoutSessions = !s.SleepWithoutSessions);
                    _ = ScanSessionsAsync(); // para que se duerma o se despierte ya
                    break;
                case "sample": ShowNextSample(); break;
                case "sounds": Sounds.EnsureDefaults(); Gfx.OpenFolder(Sounds.Folder); break;
                case "add": await AddAccountAsync(); break;
                case "setup": await ShowSetupAsync(); break;
                case "diag": await CopyDiagnosticsAsync(); break;
                case "updates": ToggleUpdates(); break;
                case "update": await OnUpdateAsync(); break;
                case "unsetup":
                    _popup.HoldOpen = true;
                    try { await RemoveSetupAsync(); }
                    finally { _popup.HoldOpen = false; }
                    break;
            }
        }
        PushSettings();
    }

    /// <summary>Desde una sesión, propone su cuenta y su carpeta; el resto, lo último elegido.</summary>
    async Task NewSessionAsync(SessionInfo? from)
    {
        var initial = _settings.Launch;
        if (from is not null)
            initial = new LaunchOptions
            {
                ConfigDir = from.Account.ConfigDir, Folder = from.Cwd,
                Model = initial.Model, Effort = initial.Effort, Advisor = initial.Advisor,
            };
        var recent = (_lastSessions ?? []).Select(s => s.Cwd).Prepend(initial.Folder ?? "");

        _popup.HoldOpen = true;
        try
        {
            var dialog = new NewSessionWindow(_settings.Accounts, initial, recent, _theme);
            dialog.Show();
            dialog.Activate();
            if (await dialog.Result is not { } options) return;
            _settings.Launch = options;
            SettingsStore.Save(_settings);
            Launcher.Start(options);
        }
        catch (Exception ex)
        {
            await Dialogs.Info(L.T("No se pudo abrir la sesión: {0}", ex.Message), _theme);
        }
        finally
        {
            _popup.HoldOpen = false;
        }
    }

    void ToggleSound()
    {
        _settings.Sound = !_settings.Sound;
        SettingsStore.Save(_settings);
        if (_settings.Sound) _sounds.Play(NoticeKind.Done, _settings.SoundPack, force: true);
    }

    void SetSoundPack(string pack)
    {
        _settings.SoundPack = pack;
        SettingsStore.Save(_settings);
        _sounds.Play(NoticeKind.Done, pack, force: true);
    }

    /// <summary>Elige la paleta y quita el tema propio; la mascota también nombra así los temas («tema:&lt;id&gt;»).</summary>
    void SetPalette(string palette)
    {
        if (CustomThemes.IdOf(palette) is { } theme)
        {
            SetCustomTheme(theme);
            return;
        }
        _settings.Palette = palette;
        _settings.CustomTheme = null;
        SettingsStore.Save(_settings);
        Theme.SelectedPalette = palette;
        Theme.SelectedCustom = null;
        ApplyTheme();
    }

    /// <summary>Lo elegido en «Mascota»: la mascota de los avisos y la animación al actualizar.</summary>
    void SetMascot(string choice)
    {
        var before = _settings.Mascot;
        Mascots.Choose(_settings, choice);
        // Un personaje nuevo (el chibi, Dokki, Gemma, Ciel) sale al escritorio y, si sonaba la voz de otro, suena la suya.
        if (_settings.Mascot != before && Mascots.IsCharacter(_settings.Mascot))
        {
            _settings.DesktopMascot = true;
            if (Sounds.IsVoice(_settings.SoundPack)) _settings.SoundPack = _settings.Mascot;
        }
        SettingsStore.Save(_settings);
        Mascots.Selected = _settings.Mascot; // los avisos la leen al dibujarse
        _director.Skin = Mascots.Skin;
        _refresh.Style = RefreshSprite.Resolve(_settings.Animation, _settings.Mascot);
        _refreshIcons.Clear();
        ShowRefreshFrame();
        UpdateMascotWindow();
        _island?.SetMascot(Mascots.Skin);
        _ = RefreshAsync(); // muestra de la animación elegida, como la del estilo de sonido
    }

    /// <summary>El argumento que sigue a ese (--soltar archivo); null si no está.</summary>
    static string? ArgAfter(string flag) => Array.IndexOf(Program.Args, flag) is var i and >= 0 && i + 1 < Program.Args.Length ? Program.Args[i + 1] : null;

    /// <summary>Otro tamaño para la mascota del escritorio (MascotSizes).</summary>
    void SetMascotSize(string size)
    {
        _settings.MascotSize = size;
        SettingsStore.Save(_settings);
        _mascotWindow?.SetSize(size);
        foreach (var pet in _agentPets.Values) pet.Window.SetSize(size);
        if (_chatBubble?.IsVisible == true && _mascotWindow is { } mascot) _chatBubble.Follow(mascot.ScreenBounds);
    }

    void ToggleDesktopMascot()
    {
        _settings.DesktopMascot = !_settings.DesktopMascot;
        SettingsStore.Save(_settings);
        UpdateMascotWindow();
    }

    void ToggleSpeak()
    {
        _settings.Speak = !_settings.Speak;
        SettingsStore.Save(_settings);
        if (!_settings.Speak) _bubble.Hide();
    }

    /// <summary>Un interruptor de la mascota: se guarda y la música se mira solo si hace falta.</summary>
    void Flip(Action<AppSettings> flip)
    {
        flip(_settings);
        SettingsStore.Save(_settings);
        Music.Watch(_settings.DesktopMascot && _settings.DanceToMusic);
    }

    /// <summary>
    /// La mascota del escritorio está si no se ocultó; si cambia de mascota (un personaje o la clásica), la
    /// ventana se rehace con la nueva.
    /// </summary>
    void UpdateMascotWindow()
    {
        var wanted = _settings.DesktopMascot;
        Music.Watch(wanted && _settings.DanceToMusic); // la música solo se mira si hay mascota que baile
        if (_mascotWindow is { } current && (!wanted || current.Pet != _pet || (_pet is null && current.Skin != Mascots.Skin)
            || current.AllDesktops != _settings.MascotAllDesktops))
        {
            _bubble.Hide();
            _chatBubble?.Dismiss(notify: true);
            current.Close();
            _mascotWindow = null;
        }
        if (!wanted || _mascotWindow is not null)
        {
            UpdateAgentPets();
            return;
        }
        PixelPoint? saved = _settings.DesktopMascotX is { } x && _settings.DesktopMascotY is { } y ? new PixelPoint(x, y) : null;
        _mascotWindow = new MascotWindow(_director, saved, Mascots.Skin, () => _theme.Accent, _settings.MascotAllDesktops, _settings.MascotSize, _pet);
        WatchMascotScreens();
        _mascotWindow.PositionChanged += (_, _) =>
        {
            if (_mascotWindow is not { } moved) return;
            if (_bubble.IsVisible) _bubble.Follow(moved.ScreenBounds);
            if (_chatBubble?.IsVisible == true) _chatBubble.Follow(moved.ScreenBounds);
        };
        _mascotWindow.Poked += OnMascotPoked;
        WireDrops(_mascotWindow);
        _mascotWindow.OpenRequested += ShowForm;
        _mascotWindow.HideRequested += () => { ToggleDesktopMascot(); PushSettings(); };
        _mascotWindow.Moved += at =>
        {
            (_settings.DesktopMascotX, _settings.DesktopMascotY) = (at.X, at.Y);
            SettingsStore.Save(_settings);
        };
        _mascotWindow.Show();
        _ = ScanSessionsAsync(); // para que empiece haciendo lo que corresponde
        UpdateAgentPets(); // las de los otros agentes, a su lado
    }

    /// <summary>Toma el tema del momento (modo del sistema y paleta elegida): ventana, ícono y animación.</summary>
    void ApplyTheme()
    {
        _theme = Theme.ForDark(_app.ActualThemeVariant == ThemeVariant.Dark);
        _popup.ApplyTheme(_theme);
        _island?.SetAccent(Theme.ForDark(true).Accent); // la isla es siempre oscura, con el acento de la paleta
        _refreshIcons.Clear(); // se vuelven a dibujar con el acento nuevo
        ShowRefreshFrame();
        UpdateTray();
    }

    /// <summary>El cuadro actual de la animación en el ícono y en el pie de la ventana.</summary>
    void ShowRefreshFrame()
    {
        if (_refresh.Frame is { } frame)
        {
            if (!_refreshIcons.TryGetValue(frame, out var icon))
                _refreshIcons[frame] = icon = Gfx.RefreshIcon(_refresh.Style, frame, _theme.Accent);
            _tray.Icon = icon;
        }
        View.SetRefreshFrame(_refresh.Frame, _refresh.Style);
    }

    void ToggleNotifications()
    {
        _settings.Notifications = !_settings.Notifications;
        SettingsStore.Save(_settings);
        PushSettings();
        if (_settings.Notifications) _detector.Reset();
        else _toasts.CloseAll();
    }

    void OpenNotice(Notice notice)
    {
        if (notice == _updateNotice)
        {
            _ = OpenConsoleAsync("sistema");
            return;
        }
        if (notice.SessionId is { } id) View.FocusSession(id);
        else if (notice.AccountDir is { } dir)
        {
            _settings.Selected = dir;
            View.FocusAccount(dir);
        }
        ShowForm();
    }

    void ShowNextSample()
    {
        var samples = NoticeDetector.Samples().ToList();
        ShowNotice(samples[_sampleIndex % samples.Count]);
        _sampleIndex++;
    }

    void ShowSamples()
    {
        var queue = new Queue<Notice>(NoticeDetector.Samples());
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        timer.Tick += (_, _) =>
        {
            if (queue.Count == 0) { timer.Stop(); return; }
            ShowNotice(queue.Dequeue());
        };
        timer.Start();
    }

    async Task CopyDiagnosticsAsync()
    {
        var report = Diagnostics.Report(_settings, _lastSessions, _noticesShown);
        try
        {
            if (_popup.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(report);
                await Dialogs.Info(L.T("Diagnóstico copiado al portapapeles."), _theme);
                return;
            }
        }
        catch (Exception) { }
        await Dialogs.Info(report, _theme);
    }

    void Render()
    {
        UpdateTray();
        PushIslandUsage();
        PushSettings(); // la cantidad de cuentas puede haber cambiado
        View.Render(_settings.Accounts, _settings.Selected, _snapshots, _profiles, _local, _updated);
    }

    void UpdateTray()
    {
        var selected = _settings.Accounts.FirstOrDefault(a => _settings.Selected is not null && Account.SamePath(a.ConfigDir, _settings.Selected))
            ?? _settings.Accounts.FirstOrDefault();
        UsageSnapshot? snap = null;
        if (selected is not null) _snapshots.TryGetValue(selected.ConfigDir, out snap);
        var session = snap?.Session;

        if (!_refresh.Running)
        {
            SD.Color color = session is null || snap!.Error is not null ? _theme.Tertiary : _theme.For(session.Percent, session.Severity);
            _tray.Icon = Gfx.PercentIcon(session?.Percent, color);
        }

        var lines = _settings.Accounts.Select(a =>
        {
            _snapshots.TryGetValue(a.ConfigDir, out var s);
            if (s?.Session is null) return $"{a.Label}  —";
            var weekly = s.Weekly is null ? "" : L.T(" · {0:0} % semana", s.Weekly.Percent);
            return L.T("{0}  {1:0} % sesión{2}", a.Label, s.Session.Percent, weekly);
        });
        _tray.ToolTipText = string.Join("\n", lines.Prepend("Claupet"));
    }

    void Toggle()
    {
        if (_popup.IsVisible) { _popup.HidePopup(); return; }
        if ((DateTime.Now - _popup.LastHidden).TotalMilliseconds < 300) return;
        ShowForm();
    }

    void ShowForm()
    {
        Render();
        _popup.ShowNear();
        if (_updated is null || DateTime.Now - _updated > TimeSpan.FromMinutes(1)) _ = RefreshAsync();
    }

    async Task AddAccountAsync()
    {
        if (!_popup.IsVisible) ShowForm();
        _popup.HoldOpen = true;
        try
        {
            var start = await _popup.StorageProvider.TryGetFolderFromPathAsync(Account.Home);
            var folders = await _popup.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = L.T("Carpeta de configuración de Claude Code de la cuenta (la de CLAUDE_CONFIG_DIR)"),
                AllowMultiple = false,
                SuggestedStartLocation = start,
            });
            if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } dir) return;

            if (_settings.Accounts.FirstOrDefault(a => Account.SamePath(a.ConfigDir, dir)) is { } existing)
                _settings.Selected = existing.ConfigDir;
            else
            {
                if (!Credentials.Exists(dir)
                    && !await Dialogs.Confirm(L.T("Esa carpeta todavía no tiene credenciales de Claude Code. ¿La agrego de todas formas?"), _theme))
                    return;
                _settings.Ignored.RemoveAll(i => Account.SamePath(i, dir));
                _settings.Accounts.Add(new Account { Label = Account.LabelFor(dir), ConfigDir = dir });
                await AskMemoryAsync(dir); // compartida con otra cuenta o separada
                _settings.Selected = dir;
            }
            SettingsStore.Save(_settings);
        }
        finally { _popup.HoldOpen = false; }
        ShowForm();
        _ = RefreshAsync();
    }

    async Task RemoveAccountAsync(Account account)
    {
        _popup.HoldOpen = true;
        try
        {
            if (!await Dialogs.Confirm(L.T("¿Quitar «{0}» de la lista? No se borra nada de la carpeta.", account.Label), _theme)) return;
        }
        finally { _popup.HoldOpen = false; }
        _settings.Accounts.Remove(account);
        _settings.Ignored.Add(account.ConfigDir);
        _snapshots.Remove(account.ConfigDir);
        _profiles.Remove(account.ConfigDir);
        if (_settings.Selected is not null && Account.SamePath(_settings.Selected, account.ConfigDir))
            _settings.Selected = _settings.Accounts.FirstOrDefault()?.ConfigDir;
        SettingsStore.Save(_settings);
        Render();
    }

    async Task RenameAccountAsync(Account account)
    {
        _popup.HoldOpen = true;
        string? name;
        try { name = await Dialogs.Ask(L.T("Renombrar cuenta"), L.T("Nombre de la cuenta:"), account.Label, _theme); }
        finally { _popup.HoldOpen = false; }
        if (name is null) return;
        account.Label = name;
        SettingsStore.Save(_settings);
        Render();
    }

    void ToggleStartup()
    {
        try { StartupRegistration.Set(!StartupRegistration.IsEnabled); } catch (Exception) { }
        PushSettings();
    }

    IEnumerable<string> SetupDirs() => HookInstaller.ConfigDirs(_settings.Accounts.Select(a => a.ConfigDir));

    bool NeedsSetup() =>
        !_settings.SetupDone || !Terms.Accepted(_settings)
        || SetupDirs().Any(d => HookInstaller.Status(d, ExePath) == HookInstaller.State.Outdated);

    /// <summary>
    /// El asistente por pasos; lo elegido se aplica al pulsar «Aplicar», como en Windows. Devuelve false si Claupet se
    /// cierra (o se reinicia).
    /// </summary>
    async Task<bool> ShowSetupAsync()
    {
        _popup.HidePopup();
        var language = L.Language; // el asistente puede cambiarlo mientras está abierto
        var setup = new SetupWindow(_settings, ExePath, _theme, SetupDirs(), Features)
        {
            InstallPackage = HasExtensions ? InstallPackageAsync : null,
            SpeechToTextExtension = _extensions?.ProviderFor(ExtensionSlots.SpeechToText)?.Manifest.Name.Text,
            TextToSpeechExtension = _extensions?.ProviderFor(ExtensionSlots.TextToSpeech)?.Manifest.Name.Text,
        };
        setup.Show();
        setup.Activate();
        var accepted = await setup.Result;
        // Los términos cuentan apenas se aceptan, aunque después se cierre con «Ahora no»; sin ellos, Claupet se cierra.
        if (setup.TermsAccepted) _settings.TermsAccepted = Terms.Version;
        _settings.SetupDone = true; // «Ahora no» tampoco vuelve a preguntar, salvo que la app cambie de lugar
        SettingsStore.Save(_settings);
        if (!Terms.Accepted(_settings))
        {
            Quit();
            return false;
        }
        if (!accepted) return true;

        // La instalación primero (Windows): los hooks, el inicio y los accesos directos apuntan al .exe instalado, que se
        // abre al final.
        var lines = new List<string>();
        var exe = ExePath;
        string? installed = null;
        if (OperatingSystem.IsWindows() && setup.InstallFolder is { } folder)
        {
            try { installed = exe = Installation.Install(ExePath, folder, SemVersion.App.ToString()); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                ErrorLog.Write(ex);
                _settings.SetupDone = false; // el asistente vuelve la próxima vez
                SettingsStore.Save(_settings);
                await Dialogs.Info(L.T("No se pudo instalar Claupet en {0}: {1}", folder, ex.Message.TrimEnd('.')) + Environment.NewLine
                    + L.T("No se cambió nada más: el asistente vuelve a abrirse la próxima vez que abra Claupet."), _theme);
                return true;
            }
            lines.Add(L.T("Claupet quedó instalada en {0}.", folder));
        }

        lines.AddRange(setup.ApplyHooks(exe));
        if (installed is not null) lines.AddRange(MoveApprovals(exe));
        var windows = OperatingSystem.IsWindows();
        if (windows && setup.DesktopShortcut != _settings.DesktopShortcut)
        {
            // La copia instalada crea los encendidos al abrirse (Shortcuts.Ensure).
            _settings.DesktopShortcut = setup.DesktopShortcut;
            SettingsStore.Save(_settings);
            if (installed is null || !setup.DesktopShortcut) Shortcuts.Set(Shortcuts.Desktop, setup.DesktopShortcut);
        }
        try
        {
            StartupRegistration.Set(setup.StartWithSystem, exe);
            lines.Add(setup.StartWithSystem
                ? windows ? L.T("Iniciará con Windows.") : L.T("Iniciará con el sistema.")
                : windows ? L.T("No iniciará con Windows.") : L.T("No iniciará con el sistema."));
        }
        catch (Exception ex)
        {
            lines.Add(windows ? L.T("Inicio con Windows: no se pudo cambiar ({0}).", ex.Message) : L.T("Iniciar con el sistema: no se pudo cambiar ({0}).", ex.Message));
        }
        if (setup.NotificationsOn != _settings.Notifications) ToggleNotifications();
        lines.Add(_settings.Notifications ? L.T("Notificaciones encendidas.") : L.T("Notificaciones apagadas."));

        // El compañero: la mascota primero, porque el chibi sale al escritorio solo.
        if (setup.Mascot is { } mascot && mascot != Mascots.ChoiceOf(_settings)) SetMascot(mascot);
        if (setup.DesktopMascot != _settings.DesktopMascot) ToggleDesktopMascot();
        if (setup.Chat != _settings.Chat) ToggleChat();
        if (setup.Island != _settings.Island) FlipIsland(s => s.Island = setup.Island);

        // Modelos y voz: como encenderlos en Ajustes, que baja lo que falte. Si la app se reinicia (otro idioma, o se abre
        // la instalada), los baja la que se abre.
        var relaunch = setup.Language != language || installed is not null;
        var voiceMissing = setup.Mic && !VoiceModel.Ready;
        // El modelo local: la mascota responde con él; al apagarlo vuelve a Haiku.
        if (HasLocal && (setup.LocalModelId != LocalChoice.Id || setup.LocalModelOn && _settings.LocalModel is null))
        {
            _localDownload?.Cancel();
            _localServer.Stop();
            _settings.LocalModel = setup.LocalModelId;
        }
        if (HasLocal && setup.LocalModelOn != (_settings.MascotModel == AssistantModels.Local))
            _settings.MascotModel = setup.LocalModelOn ? AssistantModels.Local : "haiku";
        var localMissing = HasLocal && setup.LocalModelOn && !LocalModel.Ready(LocalChoice);
        var speechMissing = setup.ReadAloud && NeedsSupertonic(_settings.ReadAloudVoice) && !ClaupetVoiceModel.Ready;
        _settings.Mic = setup.Mic;
        _settings.ReadAloud = setup.ReadAloud;
        SettingsStore.Save(_settings);
        UpdateVoice();
        if (!_settings.ReadAloud) _readAloud?.Stop();
        if (voiceMissing)
        {
            if (!relaunch) _ = DownloadVoiceModelAsync();
            lines.Add(L.T("El modelo de voz se descarga en segundo plano; el avance está en Ajustes › Asistente."));
        }
        if (speechMissing)
        {
            if (!relaunch) _ = DownloadSpeechAsync();
            lines.Add(L.T("La voz de Claupet se descarga en segundo plano; el avance está en Ajustes › Asistente."));
        }
        if (localMissing)
        {
            if (!relaunch) _ = DownloadLocalModelAsync();
            lines.Add(L.T("El modelo local se descarga en segundo plano; el avance está en Claupet Console › Respuestas."));
        }
        PushSettings();

        if (setup.HooksChanged > 0)
            lines.Add(L.T("Los avisos de permisos funcionan en las sesiones de Claude Code que se abran desde ahora."));
        if (installed is not null) lines.Add(L.T("Claupet se abre ahora desde la carpeta instalada; puede borrar el archivo que descargó."));
        else if (relaunch) lines.Add(L.T("Claupet se reinicia para cambiar el idioma."));
        await Dialogs.Info(string.Join(Environment.NewLine, lines), _theme);
        if (!relaunch) return true;
        // El asistente ya usa el idioma nuevo (L.Language): se guarda y se reinicia sin pasar por SetLanguage.
        _settings.Language = setup.Language;
        SettingsStore.Save(_settings);
        return !Restart(installed, voiceMissing || speechMissing || localMissing ? [DownloadModelsFlag] : []);
    }

    /// <summary>Lo pasa el asistente a la app que se abre al reiniciar, si quedaron modelos por bajar.</summary>
    const string DownloadModelsFlag = "--descargar-modelos";

    /// <summary>Lo que el asistente encendió y falta bajar, como si se pulsara «Descargar» en Ajustes.</summary>
    void DownloadMissingModels()
    {
        if (_settings.Mic && !VoiceModel.Ready) _ = DownloadVoiceModelAsync();
        if (_settings.ReadAloud && NeedsSupertonic(_settings.ReadAloudVoice) && !ClaupetVoiceModel.Ready) _ = DownloadSpeechAsync();
        if (HasLocal && _settings.MascotModel == AssistantModels.Local && !LocalModel.Ready(LocalChoice)) _ = DownloadLocalModelAsync();
    }

    /// <summary>
    /// Al instalar: el hook de permisos en la isla (si está) también pasa al .exe instalado; si no, seguiría llamando al
    /// descargado. Devuelve una línea por carpeta que no se pudo cambiar.
    /// </summary>
    List<string> MoveApprovals(string exe)
    {
        var lines = new List<string>();
        foreach (var dir in SetupDirs().Where(d => HookInstaller.ApprovalsStatus(d, exe) == HookInstaller.State.Outdated))
        {
            try { HookInstaller.InstallApprovals(dir, exe); }
            catch (Exception ex) { lines.Add(L.T("{0}: no se pudo cambiar ({1}).", dir.Replace(Account.Home, "~"), ex.Message)); }
        }
        return lines;
    }

    async Task RemoveSetupAsync()
    {
        if (!await Dialogs.Confirm(L.T("Se quitará el hook de avisos de permisos de cada settings.json (con respaldo) y el inicio con el sistema. "
                + "La app sigue funcionando hasta que la cierre. ¿Continuar?"), _theme))
            return;
        var lines = new List<string>();
        foreach (var dir in SetupDirs())
        {
            var state = HookInstaller.Status(dir, ExePath);
            if (state is not (HookInstaller.State.Installed or HookInstaller.State.Outdated)) continue;
            var name = dir.Replace(Account.Home, "~");
            try { HookInstaller.Uninstall(dir); lines.Add(L.T("{0}: hook quitado.", name)); }
            catch (Exception ex) { lines.Add(L.T("{0}: no se pudo quitar ({1}).", name, ex.Message)); }
        }
        try { StartupRegistration.Set(false); lines.Add(L.T("Ya no inicia con el sistema.")); }
        catch (Exception ex) { lines.Add(L.T("Iniciar con el sistema: no se pudo quitar ({0}).", ex.Message)); }
        PushSettings();
        await Dialogs.Info(string.Join(Environment.NewLine, lines), _theme);
    }

    /// <summary>El idioma se fija al abrir la app: se guarda y la app se reinicia.</summary>
    void SetLanguage(string language)
    {
        if (language == L.Language) return;
        _settings.Language = language;
        SettingsStore.Save(_settings);
        Restart();
    }

    /// <summary>
    /// Abre otra instancia, que espera a que esta termine de cerrarse (--reiniciar), y se cierra. exe: otra copia (la recién
    /// instalada); extra: más opciones para la que se abre. Devuelve false si no pudo abrirla (y esta sigue).
    /// </summary>
    bool Restart(string? exe = null, string[]? extra = null)
    {
        var start = new System.Diagnostics.ProcessStartInfo(exe ?? ExePath) { ArgumentList = { "--reiniciar" }, UseShellExecute = false };
        foreach (var arg in extra ?? []) start.ArgumentList.Add(arg);
        try { System.Diagnostics.Process.Start(start)?.Dispose(); }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            return false;
        }
        Dispatcher.UIThread.Post(Quit);
        return true;
    }

    void Quit()
    {
        _timer.Stop();
        _sessionTimer.Stop();
        _reminderTimer.Stop();
        _toasts.CloseAll();
        _mascotWindow?.Close();
        CloseAgentPets();
        _bubble.Close();
        _asking?.Cancel();
        _chatBubble?.Close();
        _quickAsking?.Cancel();
        _analyzing?.Cancel();
        _approvals?.Dispose();
        _island?.DropApproval();
        _island?.Close();
        StopKeys();
        StopExtensions();
        UnwatchMascotScreens();
        StopVoice();
        _localDownload?.Cancel();
        _localServer.Dispose();
        _sounds.Dispose();
        _refresh.Dispose();
        _consoleHost?.Dispose();
        ApplyUpdateOnExit();
        _tray.IsVisible = false;
        _tray.Dispose();
        if (_app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime) lifetime.Shutdown();
    }
}
