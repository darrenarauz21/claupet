using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet;

/// <summary>
/// Las extensiones en la app de Windows: el gestor, lo que Claupet les ofrece (IExtensionHostApi), los
/// eventos que escuchan las habilidades, los comandos «/…» del chat y la API local. Se ven y se manejan en
/// Claupet Console (Ajustes › Extensiones, ExtensionsPanel); aquí va lo que es de esta app (IExtensionsHost).
/// Sin extensiones instaladas nada de esto cambia cómo se comporta la app.
/// </summary>
sealed partial class TrayApp : IExtensionsHost
{
    ExtensionManager _extensions = null!;
    readonly SharedAudio _audio = new();
    LocalApiServer? _localApi;
    string? _localApiError;
    SynchronizationContext? _ui;
    PetSheet? _pet;
    readonly Dictionary<string, (SessionState State, string? Action)> _seenSessions = new();

    public ExtensionManager Extensions => _extensions;

    void SetupExtensions()
    {
        _ui = SynchronizationContext.Current;
        var store = new ExtensionStore();
        try { store.CleanStaging(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
        _extensions = new ExtensionManager(store, () => _settings, change => OnUi(() =>
        {
            change(_settings);
            SettingsStore.Save(_settings);
        }), new DpapiSecretStore(DpapiSecretStore.DefaultPath)) { HostApi = new HostApi(this) };
        _extensions.Changed += () => PostUi(OnExtensionsChanged);
        _extensions.Disabled += (name, reason) => PostUi(() => ShowExtensionNotice(name, L.T("Se apagó: {0}", reason)));
        // Ajustes › Extensiones de Claupet Console las muestra y las maneja.
        ConsoleHost.UseExtensions(this);

        // Las ranuras exclusivas: si una extensión las ocupa, el integrado (Claude Code, Whisper, la voz de
        // Claupet o la de Windows) no corre; si no, todo es como siempre.
        Assistant.External = () => ExtensionProviders.Assistant(_extensions);
        Assistant.Local = LocalBrainIfReady;
        Answers.Usage = UsageAnswer;
        Answers.OpenTarget = SystemActions.OpenTarget;
        WeatherService.WindowsLocation = WindowsLocation.GetAsync;
        _localServer.IdleMinutes = _settings.LocalIdleMinutes;
        VoiceInput.External = () => ExtensionProviders.SpeechToText(_extensions);
        ReadAloud.External = () => ExtensionProviders.TextToSpeech(_extensions);
        ReadAloud.PackVoice = (voice, cancel) => _extensions.VoiceFileAsync(voice, cancel);
        Sounds.PackFile = (pack, name) => _extensions.SoundFile(pack, name);
        _chat.Added += turn => _ = BroadcastAsync("chat", new JsonObject { ["from"] = turn.FromPerson ? "person" : "claupet", ["text"] = turn.Text });
    }

    /// <summary>Al abrir la app: verifica los paquetes de datos, la mascota, la API local y las habilidades que escuchan eventos.</summary>
    async Task StartExtensionsAsync()
    {
        try
        {
            await _extensions.VerifyPacksAsync();
            if (_settings.CustomPet is not null) await LoadPetAsync();
            UpdateLocalApi();
            await _extensions.StartListenersAsync();
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }

    /// <summary>En el hilo de la interfaz, esperando a que termine (el gestor cambia los ajustes desde otros hilos).</summary>
    void OnUi(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui) action();
        else _ui.Send(_ => action(), null);
    }

    void PostUi(Action action)
    {
        if (_ui is null) action();
        else _ui.Post(_ => action(), null);
    }

    Task<T> OnUiAsync<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        PostUi(() =>
        {
            try { done.SetResult(work()); }
            catch (Exception ex) { done.SetException(ex); }
        });
        return done.Task;
    }

    void OnExtensionsChanged()
    {
        PushSettings();
        _ = _extensions.VerifyPacksAsync();
        // La mascota de un paquete que se apagó o se quitó: vuelve la de siempre.
        var petId = _settings.CustomPet is { } id && _extensions.Pets().Any(p => p.Id == id) ? id : null;
        if (petId is null && _pet is not null)
        {
            _pet = null;
            UpdateMascotWindow();
        }
        else if (petId is not null && _pet is null) _ = LoadPetAsync();
        UpdateVoice();
    }

    async Task LoadPetAsync()
    {
        var pet = _settings.CustomPet is { } id ? await _extensions.PetAsync(id) : null;
        if (pet is null && _settings.CustomPet is not null && !_extensions.Pets().Any(p => p.Id == _settings.CustomPet)) _settings.CustomPet = null;
        _pet = pet;
        UpdateMascotWindow();
        PushSettings();
    }

    /// <summary>Un aviso con el nombre de la extensión: así nadie se hace pasar por Claupet o por Claude Code.</summary>
    void ShowExtensionNotice(string extension, string text, string? title = null)
    {
        var notice = new Notice(NoticeKind.Extension, title ?? extension, text, title is null ? "" : L.T("De la extensión «{0}»", extension));
        _toasts.Show(notice);
        _director.Notice(notice.Kind);
    }

    Task BroadcastAsync(string name, JsonNode data)
    {
        if (!_extensions.Skills.Any(s => s.Manifest.Events.Contains(name))) return Task.CompletedTask;
        return Task.Run(async () =>
        {
            try { await _extensions.BroadcastAsync(name, data); }
            catch (Exception ex) { ErrorLog.Write(ex); }
        });
    }

    /// <summary>Los cambios de estado y de herramienta de las sesiones, para las habilidades que los escuchan.</summary>
    void BroadcastSessions(IReadOnlyList<SessionInfo> sessions)
    {
        var listening = _extensions.Skills.Any(s => s.Manifest.Events.Contains("session") || s.Manifest.Events.Contains("toolUse"));
        foreach (var s in sessions)
        {
            var had = _seenSessions.TryGetValue(s.SessionId, out var before);
            _seenSessions[s.SessionId] = (s.State, s.Action);
            if (!listening || !had) continue;
            if (before.State != s.State) _ = BroadcastAsync("session", SessionJson(s));
            if (s.Action is { Length: > 0 } action && action != before.Action)
                _ = BroadcastAsync("toolUse", new JsonObject { ["session"] = s.SessionId, ["title"] = s.DisplayTitle, ["action"] = action });
        }
        foreach (var gone in _seenSessions.Keys.Where(k => sessions.All(s => s.SessionId != k)).ToList()) _seenSessions.Remove(gone);
    }

    static JsonObject SessionJson(SessionInfo s) => new()
    {
        ["id"] = s.SessionId, ["title"] = s.DisplayTitle, ["state"] = s.State.ToString().ToLowerInvariant(), ["folder"] = s.Cwd,
        ["account"] = s.Account.Label, ["model"] = s.Model, ["action"] = s.Action, ["awaitingAnswer"] = s.AwaitingAnswer,
        ["startedAt"] = s.StartedAt?.ToString("O"),
    };

    // ── Comandos de las habilidades ─────────────────────────────────────────────────────────

    /// <summary>«/comando lo demás»: la habilidad que lo atiende; null si no es el comando de ninguna (va al asistente).</summary>
    (InstalledExtension Skill, string Command, string Text)? SkillCommand(string text)
    {
        text = text.Trim();
        if (text.Length < 2 || text[0] != '/') return null;
        var space = text.IndexOfAny([' ', '\n']);
        var name = (space < 0 ? text[1..] : text[1..space]).ToLowerInvariant();
        return _extensions.SkillForCommand(name) is { } skill ? (skill, name, space < 0 ? "" : text[(space + 1)..].Trim()) : null;
    }

    /// <summary>Le pasa el comando a su habilidad; lo que va escribiendo (avances con «text») llega a onText en este hilo.</summary>
    async Task<string> RunSkillCommandAsync((InstalledExtension Skill, string Command, string Text) command, Action<string>? onText, CancellationToken cancel)
    {
        var (skill, name, text) = command;
        try
        {
            var reply = await _extensions.CallAsync(skill.Id, "skill/command",
                new JsonObject { ["command"] = name, ["text"] = text, ["language"] = L.Language }, cancel,
                progress => { if (Json.Str(progress, "text") is { } piece && onText is not null) PostUi(() => onText(piece)); },
                TimeSpan.FromMinutes(5));
            return reply.ValueKind == JsonValueKind.Object && Json.Str(reply, "reply") is { } r ? r.Trim() : "";
        }
        catch (OperationCanceledException) { return ""; }
        catch (ExtensionUnavailableException ex) { return ex.Message + " (._.)"; }
        catch (JsonRpcException ex) { return L.T("«{0}» falló: {1}", skill.Manifest.Name.Text, ex.Message) + " (._.)"; }
    }

    // ── Instalar ────────────────────────────────────────────────────────────────────────────

    static bool IsPackage(string file) => file.EndsWith(".claupet", StringComparison.OrdinalIgnoreCase) && File.Exists(file);

    /// <summary>
    /// Un paquete soltado (o elegido en el asistente de configuración): Claupet Console se abre en la revisión (qué es,
    /// la firma, los permisos, las ranuras) y se instala solo si la persona lo acepta allí.
    /// </summary>
    public Task InstallPackageAsync(string path) => ConsoleHost.Extensions?.ReviewAsync(path) ?? Task.CompletedTask;

    /// <summary>Ajustes › Extensiones en Claupet Console; con un id, la página de esa extensión.</summary>
    void ShowExtensions(string? id = null) => OpenConsole(id is null ? ExtensionsPanel.PageId : ExtensionsPanel.DetailPage(id));

    async Task IExtensionsHost.InstalledAsync(InstalledExtension installed, bool update, bool useAsPet)
    {
        await _extensions.VerifyPacksAsync();
        await OnUiAsync(async () =>
        {
            if (useAsPet)
            {
                _settings.CustomPet = installed.Id;
                SettingsStore.Save(_settings);
                await LoadPetAsync();
            }
            if (installed.Manifest.Events.Count > 0)
                try { await _extensions.ChannelAsync(installed.Id); }
                catch (ExtensionUnavailableException ex) { ShowExtensionNotice(installed.Manifest.Name.Text, ex.Message); }
            ShowExtensionNotice(installed.Manifest.Name.Text, update ? L.T("Actualizada a la versión {0}.", installed.Manifest.Version) : L.T("Instalada."));
            OnExtensionsChanged();
            return true;
        }).Unwrap();
    }

    // ── La API local ────────────────────────────────────────────────────────────────────────

    public bool LocalApiEnabled => _settings.Extensions.LocalApi;

    public string LocalApiStatus =>
        _localApi is { Running: true } api ? api.Url
        : _localApiError is { } error ? L.T("No se pudo abrir el puerto {0}: {1}", _settings.Extensions.LocalApiPort, error)
        : L.T("Apagada");

    public void SetLocalApi(bool on)
    {
        _settings.Extensions.LocalApi = on;
        SettingsStore.Save(_settings);
        UpdateLocalApi();
    }

    void UpdateLocalApi()
    {
        if (_settings.Extensions.LocalApi && _localApi is null)
        {
            var api = new LocalApiServer(_extensions, () => _settings.Extensions.ApiTokens);
            try
            {
                api.Start(_settings.Extensions.LocalApiPort);
                _localApi = api;
                _extensions.LocalApi = api;
                _localApiError = null;
            }
            catch (SocketException ex)
            {
                _localApiError = ex.Message;
                _ = api.DisposeAsync();
            }
        }
        else if (!_settings.Extensions.LocalApi && _localApi is { } running)
        {
            _extensions.LocalApi = null;
            _localApi = null;
            _ = running.DisposeAsync();
        }
        PushSettings();
    }

    public IReadOnlyList<ApiToken> ApiTokens => _settings.Extensions.ApiTokens;

    public string CreateApiToken(string name, IReadOnlyList<string> permissions)
    {
        var (token, saved) = LocalApiServer.NewToken(name, permissions);
        _settings.Extensions.ApiTokens.Add(saved);
        SettingsStore.Save(_settings);
        return token;
    }

    public void RevokeApiToken(string id)
    {
        _settings.Extensions.ApiTokens.RemoveAll(t => t.Id == id);
        SettingsStore.Save(_settings);
    }

    async Task StopExtensionsAsync()
    {
        try
        {
            if (_localApi is { } api) await api.DisposeAsync();
            await _extensions.StopAllAsync();
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
        _audio.Dispose();
    }

    // ── Lo que Claupet les ofrece ──────────────────────────────────────────────────────────

    /// <summary>
    /// La API del anfitrión del lado de la app. El gestor ya comprobó el permiso de cada método; aquí se
    /// atiende en el hilo de la interfaz. Lo que se muestra lleva el nombre de quien lo pidió.
    /// </summary>
    sealed class HostApi(TrayApp app) : IExtensionHostApi
    {
        /// <summary>Los gestos que una extensión puede pedir: no los de la coreografía de un archivo (atrapar, cargar, lanzar, caminar).</summary>
        static readonly ChibiMove[] Gestures =
        [
            ChibiMove.Idle, ChibiMove.Dance, ChibiMove.Wave, ChibiMove.Cheer, ChibiMove.Think, ChibiMove.Dizzy, ChibiMove.Sleep,
            ChibiMove.Maracas, ChibiMove.Pout, ChibiMove.Sulk, ChibiMove.Fury, ChibiMove.Calm, ChibiMove.Listen, ChibiMove.Talk,
            ChibiMove.Read, ChibiMove.Search, ChibiMove.Edit, ChibiMove.Run, ChibiMove.Web, ChibiMove.Agent, ChibiMove.Ponder,
            ChibiMove.Tool, ChibiMove.Built,
        ];

        string NameOf(string id) => app._extensions.Find(id)?.Manifest.Name.Text
            ?? app._settings.Extensions.ApiTokens.FirstOrDefault(t => "api:" + t.Id == id)?.Name ?? id;

        public Task PetAnimateAsync(string extensionId, string move, int? ms)
        {
            var gesture = Gestures.FirstOrDefault(g => PetSheet.AnimationOf(g).Equals(move, StringComparison.OrdinalIgnoreCase), (ChibiMove)(-1));
            if ((int)gesture < 0)
                throw new JsonRpcException(JsonRpcException.InvalidParams, L.T("Gesto desconocido: «{0}» (son: {1}).", move, string.Join(", ", Gestures.Select(PetSheet.AnimationOf))));
            return app.OnUiAsync(() =>
            {
                if (app._mascotWindow is not null && !app._carrying) app._director.Gesture(gesture, ms ?? 2500);
                return true;
            });
        }

        public Task PetSayAsync(string extensionId, string text) => app.OnUiAsync(() =>
        {
            var name = NameOf(extensionId);
            if (app._mascotWindow is { } mascot && app._chatBubble?.Visible != true)
                app._bubble.Say(new Notice(NoticeKind.Extension, name, text, ""), mascot.Chibi, mascot.Bounds);
            else app.ShowExtensionNotice(name, text);
            return true;
        });

        public Task NoticeAsync(string extensionId, string title, string text) => app.OnUiAsync(() =>
        {
            if (!app._settings.Notifications) return false;
            app.ShowExtensionNotice(NameOf(extensionId), text, title);
            if (app._settings.Sound && app._island?.Muted != true) app._sounds.Play(NoticeKind.Extension, app._settings.SoundPack);
            return true;
        });

        public Task IslandCardAsync(string extensionId, string title, string text) => app.OnUiAsync(() =>
        {
            var name = NameOf(extensionId);
            if (app._island is { } island)
                _ = island.Choose(title, $"{text}\n\n{L.T("De la extensión «{0}»", name)}", [("ok", L.T("Listo"))], "ok");
            else app.ShowExtensionNotice(name, text, title);
            return true;
        });

        public Task SpeakAsync(string extensionId, string text, string? voice, CancellationToken cancel) => app.OnUiAsync(() =>
        {
            var known = voice is not null && (ClaupetVoices.Choices.Any(c => c.Id == voice) || app._extensions.PackVoices().Any(v => v.Id == voice));
            (app._readAloud ??= new ReadAloud()).Say(text, known ? voice! : app._settings.ReadAloudVoice);
            return true;
        });

        public async Task<string> AskClaudeAsync(string extensionId, string prompt, string? model, CancellationToken cancel)
        {
            var (text, error) = await Assistant.AskPlainAsync(prompt, model, app.AssistantConfigDir, cancel);
            return text ?? throw new JsonRpcException(JsonRpcException.InternalError, error ?? L.T("Claude Code no respondió."));
        }

        public JsonNode Sessions() =>
            new JsonArray((app._lastSessions ?? []).Select(s => (JsonNode)SessionJson(s)).ToArray());

        public Task<string?> ClipboardReadAsync() =>
            app.OnUiAsync(() => Clipboard.ContainsText() ? Clipboard.GetText() : null);

        public Task ClipboardWriteAsync(string text) => app.OnUiAsync(() =>
        {
            Clipboard.SetText(text.Length == 0 ? " " : text);
            return true;
        });

        public IDisposable SubscribeAudio(string extensionId, string source, Action<short[]> chunk)
        {
            IDisposable subscription;
            try { subscription = app._audio.Subscribe(source, chunk); }
            catch (InvalidOperationException ex) { throw new JsonRpcException(JsonRpcException.InternalError, ex.Message); }
            // Escuchar no pasa desapercibido: se avisa quién y qué.
            var name = NameOf(extensionId);
            app.PostUi(() => app.ShowExtensionNotice(name, source == SharedAudio.SystemSource
                ? L.T("Está escuchando lo que suena en el equipo.") : L.T("Está escuchando el micrófono.")));
            return subscription;
        }
    }
}
