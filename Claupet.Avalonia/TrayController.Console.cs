using Avalonia.Threading;

namespace Claupet;

/// <summary>
/// Claupet Console del lado de esta bandeja (Linux y macOS, y Windows para probar). Lo que es igual en todos los
/// sistemas lo hace ConsoleHost; aquí van los ajustes que esta app ya tiene (lo demás no aparece: PlatformFeatures), sus
/// acciones y el chat, que responde el mismo asistente de Respuestas (sin la isla, que todavía no está aquí).
/// </summary>
sealed partial class TrayController : IConsoleApp
{
    /// <summary>Lo que esta app ya sabe hacer; al traer algo de Windows, se agrega aquí y aparecen sus filas.</summary>
    const PlatformFeatures Features = PlatformFeatures.None;

    ConsoleHost? _consoleHost;
    ConsoleHost ConsoleHost => _consoleHost ??= new ConsoleHost(this);
    readonly ChatThread _quick = new();
    ChatTarget? _quickChat;
    CancellationTokenSource? _quickAsking;

    ChatTarget QuickTarget => _quickChat ??= new ChatTarget(_quick, AssistantVoice.Console, island: false, asking => ConsoleHost.QuickChanged(asking));

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
            Mascot = Mascots.Label(Mascots.ChoiceOf(s)),
            DesktopMascot = s.DesktopMascot,
            MascotAllDesktops = s.MascotAllDesktops,
            Speak = s.Speak,
            FlowGestures = s.FlowGestures,
            DanceToMusic = s.DanceToMusic,
            NoticeGestures = s.NoticeGestures,
            GreetOnAppear = s.GreetOnAppear,
            AngryOnClicks = s.AngryOnClicks,
            SleepWithoutSessions = s.SleepWithoutSessions,
            IslandModel = AssistantModels.Label(s.IslandModel),
            StartWithWindows = StartupRegistration.IsEnabled,
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
            City = string.IsNullOrWhiteSpace(s.WeatherCity) || s.WeatherPlace != WeatherService.ByCity ? L.T("Automática (por la IP)") : s.WeatherCity,
            Saved = AnswerStats.ThisWeek() is var (none, local) && none + local == 1 ? L.T("1 respuesta") : L.T("{0} respuestas", none + local),
            SavedDetail = AnswerStats.ThisWeek() is var (n, l) && n + l > 0 ? L.T("{0} sin IA · {1} con el modelo local", n, l) : "",
            AnswerPreset = AnswerPresets.Label(preset),
            AnswerPresetHint = AnswerPresets.Describe(preset),
            AnswerCustom = preset == AnswerPresets.Custom,
            TopicEngines = AnswerTopics.All.Select(t => new TopicState(t.Id, L.T(t.Label),
                t.Fixed ? L.T("Siempre la nube") : Engines.Label(AnswerTopics.First(s, t.Id)), t.Fixed)).ToList(),
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
        if (key.StartsWith("topic:", StringComparison.Ordinal))
        {
            var topic = AnswerTopics.Find(key["topic:".Length..]);
            Choose(AnswerTopics.Choices(topic).Select(e => (e.ToString(), Engines.Label(e))), AnswerTopics.First(_settings, topic.Id).ToString(), v =>
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
            case "palette": Choose(Theme.Palettes.Select(p => (p.Id, p.Label)), _settings.Palette, id => _ = OnSettingAsync("palette:" + id)); break;
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
            case "city": _ = AskCityAsync(); break;
            case "imodel":
                Choose(AssistantModels.IslandChoices, _settings.IslandModel ?? "", m =>
                {
                    _settings.IslandModel = m.Length == 0 ? null : m;
                    SettingsStore.Save(_settings);
                });
                break;
            case "readaloud": break; // todavía no hay voz en esta app
            default: _ = OnSettingAsync(key); break;
        }
    }

    /// <summary>Una lista para elegir: en la Console, junto a su fila; elegida, se guarda y se avisa a todos.</summary>
    void Choose(IEnumerable<(string Id, string Label)> options, string selected, Action<string> pick)
    {
        ConsoleHost.Offer(options, selected, id =>
        {
            pick(id);
            PushSettings();
        }, null);
    }

    async Task AskCityAsync()
    {
        var city = await Dialogs.Ask(L.T("Mi ciudad"), L.T("Ciudad para el clima (vacía: automática, por la IP):"), _settings.WeatherCity ?? "", _theme);
        if (city is null) return;
        _settings.WeatherCity = city.Trim().Length == 0 ? null : city.Trim();
        _settings.WeatherPlace = _settings.WeatherCity is null ? WeatherService.ByIp : WeatherService.ByCity;
        SettingsStore.Save(_settings);
        PushSettings();
    }

    // ── Chat ────────────────────────────────────────────────────────────────────────────────

    ChatTarget IConsoleApp.QuickChat => QuickTarget;
    string IConsoleApp.QuickHint => L.T("Se olvida a los 10 minutos sin escribir");
    string IConsoleApp.QuickAbout => L.T("Para preguntas sueltas; se olvida a los 10 minutos sin escribir.");

    void IConsoleApp.AskQuick(string text)
    {
        _quickAsking?.Cancel();
        _quickAsking = new CancellationTokenSource();
        _ = AskChatAsync(QuickTarget, text, _quickAsking.Token);
    }

    void IConsoleApp.StopQuick() => _quickAsking?.Cancel();

    void IConsoleApp.ClearQuick()
    {
        _quickAsking?.Cancel();
        _quick.Clear();
        QuickTarget.Show(false);
    }

    Task IConsoleApp.AskAsync(ChatTarget chat, string text, CancellationToken cancel) => AskChatAsync(chat, text, cancel);

    /// <summary>
    /// Le pregunta al asistente (sin IA, el modelo local o la nube, según Respuestas) con el modelo de las conversaciones.
    /// Lo que va llegando se ve en la Console. Las acciones en el equipo todavía no están en esta app: se dice.
    /// </summary>
    async Task AskChatAsync(ChatTarget chat, string text, CancellationToken cancel)
    {
        var history = chat.Log.Turns.TakeLast(40).ToList();
        chat.Log.Add(new ChatTurn(true, text));
        chat.Streaming = "";
        chat.Show(true);
        void Stream(string piece)
        {
            if (cancel.IsCancellationRequested) return;
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
            ConfigDir = _settings.AssistantAccount ?? _settings.Selected,
            History = history,
            Chibi = Mascots.IsChibi,
        };
        AssistantResult result;
        try { result = await Answers.AskAsync(request, _settings, Stream, cancel: cancel); }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            result = new AssistantResult("", null, null, null, ex.Message);
        }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            chat.Streaming = null;
            if (cancel.IsCancellationRequested)
            {
                chat.Show(false);
                return;
            }
            var reply = result.Failed ? $"{result.Error} (._.)"
                : result.Task is not null ? result.Text.Length > 0 ? result.Text : L.T("Eso es una tarea para Claude Code.")
                : result.Action is not null ? L.T("Eso todavía no lo puedo hacer en este sistema. (・_・;)")
                : result.Text;
            chat.Log.Add(new ChatTurn(false, reply, _settings.ShowWhoAnswered ? result.By?.Label : null));
            chat.Show(false);
        });
    }

    string IConsoleApp.ChatModel => AssistantModels.Label(_settings.IslandModel);
    bool IConsoleApp.ReadAloud => false;
    void IConsoleApp.Post(Action action) => Dispatcher.UIThread.Post(action);
}
