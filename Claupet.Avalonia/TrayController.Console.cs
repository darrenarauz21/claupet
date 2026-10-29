using Avalonia.Threading;

namespace Claupet;

/// <summary>
/// Claupet Console del lado de esta bandeja (Linux y macOS, y Windows para probar). Lo que es igual en todos los
/// sistemas lo hace ConsoleHost; aquí van los ajustes que esta app ya tiene (lo demás no aparece: PlatformFeatures), sus
/// acciones y el chat, que responde el mismo asistente de Respuestas. Con la isla, la conversación rápida es la suya.
/// </summary>
sealed partial class TrayController : IConsoleApp
{
    /// <summary>
    /// Lo que esta app ya sabe hacer; al traer algo de Windows, se agrega aquí y aparecen sus filas. Las acciones en el
    /// equipo, donde hay un escritorio que las haga (macOS y Linux).
    /// </summary>
    static PlatformFeatures Features => PlatformFeatures.MascotChat | PlatformFeatures.Island | PlatformFeatures.Mic | PlatformFeatures.ReadAloud
        | PlatformFeatures.MascotSize | PlatformFeatures.FileDrops | PlatformFeatures.Updates
        | (SystemActions.Desktop is null ? PlatformFeatures.None : PlatformFeatures.Actions)
        | (OperatingSystem.IsWindows() ? PlatformFeatures.Hotkeys | PlatformFeatures.LocalModel : PlatformFeatures.None)
        | (HasExtensions ? PlatformFeatures.Extensions : PlatformFeatures.None)
        | (OwnsShortcuts ? PlatformFeatures.Shortcuts : PlatformFeatures.None);

    /// <summary>Los accesos directos de Windows, salvo en una copia de prueba (los reapuntaría a ella).</summary>
    static bool OwnsShortcuts => OperatingSystem.IsWindows() && !Program.Args.Contains("--preview");

    ConsoleHost? _consoleHost;
    ConsoleHost ConsoleHost => _consoleHost ??= new ConsoleHost(this);
    readonly ChatThread _quick = new();
    ChatTarget? _quickChat;
    CancellationTokenSource? _quickAsking;

    ChatTarget QuickTarget => _quickChat ??= new ChatTarget(_quick, AssistantVoice.Console, island: false, asking => ShowQuick(QuickTarget, asking));

    /// <summary>La conversación rápida de la Console: con la isla encendida, la de la isla (como en Windows).</summary>
    ChatTarget QuickChat => _settings.Island ? IslandChat : QuickTarget;

    void StartConsole() => ConsoleHost.Start();

    async Task OpenConsoleAsync(string? section = null)
    {
        if (ConsoleHost.Open(section) is { } problem) await Dialogs.Info(L.T("No se pudo abrir Claupet Console: {0}", problem), _theme);
    }

    // ── Ajustes ─────────────────────────────────────────────────────────────────────────────

    SettingsState IConsoleApp.SettingsState()
    {
        var s = _settings;
        var preset = AnswerPresets.Current(s);
        return new SettingsState
        {
            Features = Features,
            Notifications = s.Notifications,
            Sound = s.Sound,
            SoundPack = Sounds.Packs.FirstOrDefault(p => p.Id == s.SoundPack).Label ?? Sounds.Packs[0].Label,
            Palette = Theme.PaletteLabel(s.Palette),
            ThemeStatus = _themeStatus, CustomThemesHere = true,
            Mascot = Mascots.Label(Mascots.ChoiceOf(s)),
            DesktopMascot = s.DesktopMascot,
            MascotSize = MascotSizes.Label(s.MascotSize),
            MascotAllDesktops = s.MascotAllDesktops,
            Speak = s.Speak,
            FlowGestures = s.FlowGestures,
            DanceToMusic = s.DanceToMusic,
            NoticeGestures = s.NoticeGestures,
            GreetOnAppear = s.GreetOnAppear,
            AngryOnClicks = s.AngryOnClicks,
            SleepWithoutSessions = s.SleepWithoutSessions,
            Chat = s.Chat,
            MascotModel = ModelLabel(MascotModel),
            AssistantAccount = AssistantAccounts.Label(s),
            TaskFolder = TaskFolders.Label(s),
            AnalyzeDrops = s.AnalyzeDrops,
            Mic = s.Mic,
            MicKey = TalkKeyLabel,
            ChatKey = KeyNames.Label(s.ChatKey),
            CopilotKey = s.CopilotKey,
            VoiceStatus = VoiceStatusText().Status,
            VoiceAction = VoiceStatusText().Action,
            ReadAloud = s.ReadAloud,
            ReadAloudVoice = VoiceLabel(s.ReadAloudVoice),
            SpeechStatus = SpeechStatusText().Status,
            SpeechAction = SpeechStatusText().Action,
            IslandModel = ModelLabel(s.IslandModel),
            LocalModel = L.T("{0} · unos {1} de RAM", LocalChoice.Name, LocalChoice.Ram),
            LocalStatus = LocalStatusText().Status,
            LocalAction = LocalStatusText().Action,
            LocalReady = LocalModel.Ready(LocalChoice),
            LocalIdle = L.T("{0} min", s.LocalIdleMinutes),
            Island = s.Island,
            IslandHover = s.IslandHover,
            IslandWhileBusy = s.IslandWhileBusy,
            WeeklyRecap = s.WeeklyRecap,
            Approvals = ApprovalsText(),
            Grants = Capabilities.Available.Select(c => new GrantState(c.Id, c.Label, Capabilities.Allowed(s, c.Id))).ToList(),
            StartWithWindows = StartupRegistration.IsEnabled,
            StartMenuShortcut = s.StartMenuShortcut,
            DesktopShortcut = s.DesktopShortcut,
            Accounts = s.Accounts.Count,
            Language = L.Label,
            CloudModel = "Claude · " + AssistantModels.Label(s.IslandModel),
            GeneralLocal = AnswerTopics.First(s, AnswerTopics.General) == Engine.Local,
            Fallback = FallbackLabel(s.AnswerFallback),
            FallbackHint = s.AnswerFallback switch
            {
                Engine.Cloud => L.T("Sin IA, luego el modelo local (si está descargado) y al final la nube."),
                Engine.Local => L.T("Nunca llega a la nube, salvo internet, archivos y tareas."),
                _ => L.T("Si el primero no entiende, Claupet pide decirlo de otra forma."),
            },
            ShowWho = s.ShowWhoAnswered,
            City = CityLabel(),
            Saved = AnswerStats.ThisWeek() is var (none, local) && none + local == 1 ? L.T("1 respuesta") : L.T("{0} respuestas", none + local),
            SavedDetail = AnswerStats.ThisWeek() is var (n, l) && n + l > 0 ? L.T("{0} sin IA · {1} con el modelo local", n, l) : "",
            AnswerPreset = AnswerPresets.Label(preset),
            AnswerPresetHint = AnswerPresets.Describe(preset),
            Extensions = ExtensionsSummary(),
            AnswerCustom = preset == AnswerPresets.Custom,
            TopicEngines = AnswerTopics.All.Select(t => new TopicState(t.Id, L.T(t.Label),
                t.Fixed ? L.T("Siempre la nube") : EngineLabel(AnswerTopics.First(s, t.Id)), t.Fixed)).ToList(),
            AccountList = Agents.AccountStates(s),
            AgentList = Agents.AgentStates(s, _agents),
            CheckUpdates = s.CheckUpdates,
            UpdateStatus = _updates?.Row().Status ?? "",
            UpdateAction = _updates?.Row().Action ?? "",
        };
    }

    static string FallbackLabel(Engine engine) => engine switch
    {
        Engine.Cloud => L.T("Hasta la nube"),
        Engine.Local => L.T("Hasta el modelo local"),
        _ => L.T("No pasar"),
    };

    string IConsoleApp.Palette => _settings.Palette;

    /// <summary>
    /// La acción de una fila de la Console. Las listas (estilo de sonido, color, mascota, idioma, modo…) se eligen allá;
    /// los interruptores y lo demás, como en la vista Ajustes de esta app.
    /// </summary>
    void IConsoleApp.Act(string key)
    {
        if (key.StartsWith("grant:", StringComparison.Ordinal))
        {
            ToggleGrant(key["grant:".Length..]);
            PushSettings();
            return;
        }
        if (key.StartsWith("agent:", StringComparison.Ordinal))
        {
            ToggleAgent(key["agent:".Length..]);
            PushSettings();
            return;
        }
        if (key.StartsWith("topic:", StringComparison.Ordinal))
        {
            var topic = AnswerTopics.Find(key["topic:".Length..]);
            Choose(AnswerTopics.Choices(topic).Select(e => (e.ToString(), EngineLabel(e))), AnswerTopics.First(_settings, topic.Id).ToString(), v =>
            {
                var engine = Enum.Parse<Engine>(v);
                if (engine == topic.Default) _settings.AnswerEngines.Remove(topic.Id);
                else _settings.AnswerEngines[topic.Id] = engine;
                SettingsStore.Save(_settings);
            });
            return;
        }
        switch (key)
        {
            case "pack": Choose(Sounds.Packs, _settings.SoundPack, id => _ = OnSettingAsync("pack:" + id)); break;
            case "palette": Choose(Theme.Choices(), _settings.Palette, id => _ = OnSettingAsync("palette:" + id)); break;
            case SettingsPages.ThemeDeleteKey: DeleteTheme(); break;
            case "mascot": Choose(Mascots.Choices, Mascots.ChoiceOf(_settings), id => _ = OnSettingAsync("mascot:" + id)); break;
            case "language": Choose(L.Choices, L.Language, id => _ = OnSettingAsync("language:" + id)); break;
            case "answerpreset":
                Choose(AnswerPresets.Choices(), AnswerPresets.Current(_settings), v =>
                {
                    AnswerPresets.Apply(_settings, v);
                    SettingsStore.Save(_settings);
                });
                break;
            case "fallback":
                Choose(new[] { Engine.Cloud, Engine.Local, Engine.None }.Select(e => (e.ToString(), FallbackLabel(e))), _settings.AnswerFallback.ToString(), v =>
                {
                    _settings.AnswerFallback = Enum.Parse<Engine>(v);
                    SettingsStore.Save(_settings);
                });
                break;
            case "who":
                _settings.ShowWhoAnswered = !_settings.ShowWhoAnswered;
                SettingsStore.Save(_settings);
                PushSettings();
                break;
            case "city": PickCity(); break;
            case "mmodel":
                Choose(MascotModelChoices(), MascotModel, m =>
                {
                    _settings.MascotModel = m;
                    SettingsStore.Save(_settings);
                    _chatBubble?.SetModel(m);
                    ModelPicked(m);
                });
                break;
            case "account":
                Choose(AssistantAccounts.Choices(_settings), _settings.AssistantAccount ?? "", a =>
                {
                    _settings.AssistantAccount = a.Length == 0 ? null : a;
                    SettingsStore.Save(_settings);
                });
                break;
            case "folder": Choose(TaskFolders.Choices(_settings, _lastSessions), _settings.TaskFolder ?? "", f => _ = PickTaskFolderAsync(f)); break;
            case "imodel":
                Choose(IslandModelChoices(), _settings.IslandModel ?? "", m =>
                {
                    _settings.IslandModel = m.Length == 0 ? null : m;
                    SettingsStore.Save(_settings);
                    ModelPicked(m);
                    PushIslandModel();
                });
                break;
            case "size": Choose(MascotSizes.Choices, _settings.MascotSize, SetMascotSize); break;
            case "localmodel": PickLocalModel(); break;
            case "localidle": PickLocalIdle(); break;
            case "local": LocalModelAction(); break;
            // En Windows se puede grabar otra tecla o combinación; en Linux y macOS, solo las que se mantienen.
            case "mickey":
                Choose(KeyChoices(MicKeys.Choices, OperatingSystem.IsWindows() ? _settings.MicKey : "", L.T("Grabar otra tecla…")),
                    MicKeys.IsHold(_settings.MicKey) || TalkIsCombo ? _settings.MicKey : MicKeys.Choices[0].Id, k =>
                    {
                        if (k != RecordChoice) SetMicKey(k);
                        else _ = RecordKeyAsync(talk: true, combo =>
                        {
                            SetMicKey(combo);
                            PushSettings();
                        });
                    });
                break;
            case "chatkey":
                Choose(KeyChoices([(KeyNames.DefaultChat, KeyNames.Label(KeyNames.DefaultChat))], _settings.ChatKey, L.T("Grabar otra combinación…")),
                    _settings.ChatKey, k =>
                    {
                        if (k != RecordChoice) SetChatKey(k);
                        else _ = RecordKeyAsync(talk: false, combo =>
                        {
                            SetChatKey(combo);
                            PushSettings();
                        });
                    });
                break;
            case "readvoice": Choose(VoiceChoices(), _settings.ReadAloudVoice, SetReadAloudVoice); break;
            default: _ = OnSettingAsync(key); break;
        }
    }

    /// <summary>Dónde sale la lista de una fila: null, en la Console; si no, en el popup (OnQuickSetting).</summary>
    Action<IEnumerable<(string Id, string Label)>, string, Action<string>>? _menu;

    /// <summary>Una lista para elegir: en la Console, junto a su fila (o en el popup); elegida, se guarda y se avisa a todos.</summary>
    void Choose(IEnumerable<(string Id, string Label)> options, string selected, Action<string> pick)
    {
        void Picked(string id)
        {
            pick(id);
            PushSettings();
        }
        if (_menu is { } menu) menu(options, selected, Picked);
        else ConsoleHost.Offer(options, selected, Picked, null);
    }

    string CityLabel() => _settings.WeatherPlace switch
    {
        WeatherService.ByCity when !string.IsNullOrWhiteSpace(_settings.WeatherCity) => _settings.WeatherCity,
        WeatherService.ByWindows => L.T("Ubicación de Windows"),
        _ => L.T("Automática (por la IP)"),
    };

    /// <summary>Mi ciudad: por la IP, la ubicación de Windows (solo ahí) o una escrita.</summary>
    void PickCity()
    {
        List<(string, string)> choices = [(WeatherService.ByIp, L.T("Automática (por la IP)"))];
        if (OperatingSystem.IsWindows()) choices.Add((WeatherService.ByWindows, L.T("Ubicación de Windows")));
        choices.Add((WeatherService.ByCity, L.T("Escribir una ciudad…")));
        Choose(choices, _settings.WeatherPlace, id => _ = SetCityAsync(id));
    }

    /// <summary>
    /// La escrita se busca en Open-Meteo (y queda con su nombre); la de Windows se prueba antes de elegirla, porque puede
    /// estar apagada en Configuración › Privacidad.
    /// </summary>
    async Task SetCityAsync(string id)
    {
        if (id == WeatherService.ByCity)
        {
            var name = await Dialogs.Ask(L.T("Mi ciudad"), L.T("Su ciudad, para el clima:"), _settings.WeatherCity ?? "", _theme);
            if (name is null) return;
            if (await WeatherService.GeocodeAsync(name, CancellationToken.None) is not { } place)
            {
                await Dialogs.Info(L.T("No encontré «{0}». Pruebe con otro nombre o sin abreviaturas.", name), _theme);
                return;
            }
            _settings.WeatherPlace = WeatherService.ByCity;
            _settings.WeatherCity = place.Name;
        }
        else if (id == WeatherService.ByWindows && OperatingSystem.IsWindows())
        {
            if (await WindowsLocation.GetAsync(CancellationToken.None) is null)
            {
                await Dialogs.Info(L.T("Windows no dio la ubicación. Revise Configuración › Privacidad y seguridad › Ubicación, y que las apps de escritorio puedan usarla."), _theme);
                return;
            }
            _settings.WeatherPlace = WeatherService.ByWindows;
        }
        else _settings.WeatherPlace = WeatherService.ByIp;
        SettingsStore.Save(_settings);
        PushSettings();
    }

    // ── Chat ────────────────────────────────────────────────────────────────────────────────

    ChatTarget IConsoleApp.QuickChat => QuickChat;
    // Con la isla, es la misma conversación de la isla (y de la nubecita); sin ella, con el chat de la mascota, la de la nubecita.
    string IConsoleApp.QuickHint => _settings.Island ? L.T("La de la isla") : _settings.Chat ? L.T("La de la nubecita") : L.T("Se olvida a los 10 minutos sin escribir");
    string IConsoleApp.QuickAbout => _settings.Island
        ? L.T("Es la misma conversación de la isla y de la nubecita; se olvida a los 10 minutos sin escribir.")
        : _settings.Chat
        ? L.T("Es la misma conversación de la nubecita; se olvida a los 10 minutos sin escribir.")
        : L.T("Para preguntas sueltas; se olvida a los 10 minutos sin escribir.");

    void IConsoleApp.AskQuick(string text)
    {
        _quickAsking?.Cancel();
        _quickAsking = new CancellationTokenSource();
        _ = AskChatAsync(QuickChat, text, _quickAsking.Token);
    }

    void IConsoleApp.StopQuick() => _quickAsking?.Cancel();

    void IConsoleApp.ClearQuick()
    {
        _quickAsking?.Cancel();
        _quick.Clear();
        QuickChat.Show(false);
    }

    Task IConsoleApp.AskAsync(ChatTarget chat, string text, CancellationToken cancel) => AskChatAsync(chat, text, cancel);

    /// <summary>
    /// Le pregunta al asistente (sin IA, el modelo local o la nube, según Respuestas) con el modelo de las conversaciones.
    /// Lo que va llegando se ve en la Console (y en la isla). Una tarea se ofrece abrir; las acciones en el equipo todavía no
    /// están en esta app: se dice.
    /// </summary>
    async Task AskChatAsync(ChatTarget chat, string text, CancellationToken cancel)
    {
        var history = chat.Log.Turns.TakeLast(40).ToList();
        chat.Log.Add(new ChatTurn(true, text));
        chat.Streaming = "";
        chat.Show(true);
        // Con la voz encendida, la respuesta no se muestra mientras llega: sale cuando empieza a sonar.
        var speaking = _settings.ReadAloud;
        void Stream(string piece)
        {
            if (cancel.IsCancellationRequested || speaking) return;
            Dispatcher.UIThread.Post(() =>
            {
                chat.Streaming = (chat.Streaming ?? "") + piece;
                chat.Show(true);
            });
        }
        var request = new AssistantRequest
        {
            Prompt = text,
            Voice = chat.Voice,
            Model = _settings.IslandModel,
            ConfigDir = AssistantAccounts.ConfigDir(_settings),
            Context = AssistantContext(),
            History = history,
            Chibi = Mascots.IsChibi,
            Expressive = Expressive,
        };
        AssistantResult result;
        try { result = await Answers.AskAsync(request, _settings, Stream, cancel: cancel); }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            result = new AssistantResult("", null, null, null, ex.Message);
        }
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            chat.Streaming = null;
            if (cancel.IsCancellationRequested)
            {
                chat.Show(false);
                return;
            }
            var reply = result.Failed ? $"{result.Error} (._.)"
                : result.Task is not null ? result.Text.Length > 0 ? result.Text : L.T("Eso es una tarea para Claude Code.")
                // Si pidió algo en el equipo, lo hace (con su permiso); si trajo datos, la respuesta empieza de nuevo con ellos.
                : await RunActionAsync(result, request, chat.Island, Stream, () =>
                {
                    chat.Streaming = "";
                    chat.Show(true);
                }, cancel);
            chat.Streaming = null;
            if (cancel.IsCancellationRequested)
            {
                chat.Show(false);
                return;
            }
            ReadAloudIfOn(result, reply, () =>
            {
                if (cancel.IsCancellationRequested) return;
                chat.Log.Add(new ChatTurn(false, reply, _settings.ShowWhoAnswered ? result.By?.Label : null));
                chat.Show(false);
                if (result is { Failed: false, Task: { } task }) _ = OfferTaskAsync(task);
            });
        });
    }

    string IConsoleApp.ChatModel => AssistantModels.Label(_settings.IslandModel);
    bool IConsoleApp.ReadAloud => _settings.ReadAloud;
    void IConsoleApp.Post(Action action) => Dispatcher.UIThread.Post(action);
}
