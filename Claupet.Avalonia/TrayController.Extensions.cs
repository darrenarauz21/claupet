using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;

namespace Claupet;

/// <summary>
/// Las extensiones en la app Avalonia, como en la de WinForms (TrayApp.Extensions.cs): el gestor, lo que Claupet les
/// ofrece (IExtensionHostApi), los eventos que escuchan las habilidades, los comandos «/…» del chat y la API local. Se
/// ven y se manejan en Claupet Console (Ajustes › Extensiones, ExtensionsPanel). Por ahora, solo en Windows (las claves
/// van cifradas con DPAPI) y sin la mascota de un paquete ni el audio para las que escuchan.
/// </summary>
sealed partial class TrayController : IExtensionsHost
{
    static bool HasExtensions => OperatingSystem.IsWindows();

    /// <summary>
    /// Una copia de prueba (--preview) muestra las extensiones pero no limpia su carpeta ni arranca las que escuchan ni la
    /// API local: las corre la app de siempre, y así no corren dos veces.
    /// </summary>
    static bool RunsExtensions => !Program.Args.Contains("--preview");

    ExtensionManager? _extensions;
    LocalApiServer? _localApi;
    string? _localApiError;
    readonly Dictionary<string, (SessionState State, string? Action)> _seenSessions = new();

    public ExtensionManager Extensions => _extensions!;

    void SetupExtensions()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new ExtensionStore();
        try { if (RunsExtensions) store.CleanStaging(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
        var extensions = _extensions = new ExtensionManager(store, () => _settings, change => OnUi(() =>
        {
            change(_settings);
            SettingsStore.Save(_settings);
        }), new DpapiSecretStore(DpapiSecretStore.DefaultPath)) { HostApi = new HostApi(this) };
        extensions.Changed += () => Dispatcher.UIThread.Post(OnExtensionsChanged);
        extensions.Disabled += (name, reason) => Dispatcher.UIThread.Post(() => ShowExtensionNotice(name, L.T("Se apagó: {0}", reason)));
        // Ajustes › Extensiones de Claupet Console las muestra y las maneja.
        ConsoleHost.UseExtensions(this);

        // Las ranuras exclusivas: si una extensión las ocupa, el integrado no corre; si no, todo es como siempre.
        Assistant.External = () => ExtensionProviders.Assistant(extensions);
        VoiceInput.External = () => ExtensionProviders.SpeechToText(extensions);
        ReadAloud.External = () => ExtensionProviders.TextToSpeech(extensions);
        ReadAloud.PackVoice = (voice, cancel) => extensions.VoiceFileAsync(voice, cancel);
        Sounds.PackFile = (pack, name) => extensions.SoundFile(pack, name);
    }

    /// <summary>Al abrir la app: verifica los paquetes de datos, la API local y las habilidades que escuchan eventos.</summary>
    async Task StartExtensionsAsync()
    {
        if (_extensions is not { } extensions) return;
        try
        {
            await extensions.VerifyPacksAsync();
            if (!RunsExtensions) return;
            UpdateLocalApi();
            await extensions.StartListenersAsync();
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }

    /// <summary>En el hilo de la interfaz, esperando a que termine (el gestor cambia los ajustes desde otros hilos).</summary>
    static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Invoke(action);
    }

    static Task<T> OnUiAsync<T>(Func<T> work) => Dispatcher.UIThread.InvokeAsync(work).GetTask();

    void OnExtensionsChanged()
    {
        PushSettings();
        if (_extensions is { } extensions) _ = extensions.VerifyPacksAsync();
        UpdateVoice();
    }

    /// <summary>El resumen de Ajustes › Extensiones.</summary>
    string ExtensionsSummary() => _extensions is not { } extensions ? ""
        : extensions.Installed.Count == 0 ? L.T("Ninguna")
        : L.T("{0} de {1} encendidas", extensions.Installed.Count(e => extensions.IsEnabled(e.Id)), extensions.Installed.Count);

    /// <summary>Un aviso con el nombre de la extensión: así nadie se hace pasar por Claupet o por Claude Code.</summary>
    void ShowExtensionNotice(string extension, string text, string? title = null)
    {
        var notice = new Notice(NoticeKind.Extension, title ?? extension, text, title is null ? "" : L.T("De la extensión «{0}»", extension));
        _toasts.Show(notice, _theme);
        _director.Notice(notice.Kind);
    }

    Task BroadcastAsync(string name, JsonNode data)
    {
        if (_extensions is not { } extensions || !extensions.Skills.Any(s => s.Manifest.Events.Contains(name))) return Task.CompletedTask;
        return Task.Run(async () =>
        {
            try { await extensions.BroadcastAsync(name, data); }
            catch (Exception ex) { ErrorLog.Write(ex); }
        });
    }

    /// <summary>Los cambios de estado y de herramienta de las sesiones, para las habilidades que los escuchan.</summary>
    void BroadcastSessions(IReadOnlyList<SessionInfo> sessions)
    {
        if (_extensions is not { } extensions) return;
        var listening = extensions.Skills.Any(s => s.Manifest.Events.Contains("session") || s.Manifest.Events.Contains("toolUse"));
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
        if (_extensions is not { } extensions || text.Length < 2 || text[0] != '/') return null;
        var space = text.IndexOfAny([' ', '\n']);
        var name = (space < 0 ? text[1..] : text[1..space]).ToLowerInvariant();
        return extensions.SkillForCommand(name) is { } skill ? (skill, name, space < 0 ? "" : text[(space + 1)..].Trim()) : null;
    }

    /// <summary>Le pasa el comando a su habilidad; lo que va escribiendo (avances con «text») llega a onText en este hilo.</summary>
    async Task<string> RunSkillCommandAsync((InstalledExtension Skill, string Command, string Text) command, Action<string>? onText, CancellationToken cancel)
    {
        var (skill, name, text) = command;
        try
        {
            var reply = await Extensions.CallAsync(skill.Id, "skill/command",
                new JsonObject { ["command"] = name, ["text"] = text, ["language"] = L.Language }, cancel,
                progress => { if (Json.Str(progress, "text") is { } piece && onText is not null) Dispatcher.UIThread.Post(() => onText(piece)); },
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
    /// Un paquete soltado: Claupet Console se abre en la revisión (qué es, la firma, los permisos, las ranuras) y se
    /// instala solo si la persona lo acepta allí.
    /// </summary>
    Task InstallPackageAsync(string path) => ConsoleHost.Extensions?.ReviewAsync(path) ?? Task.CompletedTask;

    async Task IExtensionsHost.InstalledAsync(InstalledExtension installed, bool update, bool useAsPet)
    {
        await Extensions.VerifyPacksAsync();
        await OnUiAsync(async () =>
        {
            // La mascota de un paquete queda elegida (la dibuja la app de WinForms); esta app todavía no la dibuja.
            if (useAsPet)
            {
                _settings.CustomPet = installed.Id;
                SettingsStore.Save(_settings);
                ShowExtensionNotice(installed.Manifest.Name.Text, L.T("Su mascota todavía no se ve en esta app."));
            }
            if (installed.Manifest.Events.Count > 0)
                try { await Extensions.ChannelAsync(installed.Id); }
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
        if (_extensions is not { } extensions || !RunsExtensions) return;
        if (_settings.Extensions.LocalApi && _localApi is null)
        {
            var api = new LocalApiServer(extensions, () => _settings.Extensions.ApiTokens);
            try
            {
                api.Start(_settings.Extensions.LocalApiPort);
                _localApi = api;
                extensions.LocalApi = api;
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
            extensions.LocalApi = null;
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

    /// <summary>Al salir: la API local y las extensiones que corren, sin esperar más de 3 s.</summary>
    void StopExtensions()
    {
        if (_extensions is not { } extensions) return;
        var api = _localApi;
        Task.Run(async () =>
        {
            try
            {
                if (api is not null) await api.DisposeAsync();
                await extensions.StopAllAsync();
            }
            catch (Exception ex) { ErrorLog.Write(ex); }
        }).Wait(3000);
    }

    // ── Lo que Claupet les ofrece ──────────────────────────────────────────────────────────

    /// <summary>
    /// La API del anfitrión del lado de la app. El gestor ya comprobó el permiso de cada método; aquí se atiende en el
    /// hilo de la interfaz. Lo que se muestra lleva el nombre de quien lo pidió.
    /// </summary>
    sealed class HostApi(TrayController app) : IExtensionHostApi
    {
        /// <summary>Los gestos que una extensión puede pedir: no los de la coreografía de un archivo (atrapar, cargar, lanzar, caminar).</summary>
        static readonly ChibiMove[] Gestures =
        [
            ChibiMove.Idle, ChibiMove.Dance, ChibiMove.Wave, ChibiMove.Cheer, ChibiMove.Think, ChibiMove.Dizzy, ChibiMove.Sleep,
            ChibiMove.Maracas, ChibiMove.Pout, ChibiMove.Sulk, ChibiMove.Fury, ChibiMove.Calm, ChibiMove.Listen, ChibiMove.Talk,
            ChibiMove.Read, ChibiMove.Search, ChibiMove.Edit, ChibiMove.Run, ChibiMove.Web, ChibiMove.Agent, ChibiMove.Ponder,
            ChibiMove.Tool, ChibiMove.Built,
        ];

        string NameOf(string id) => app.Extensions.Find(id)?.Manifest.Name.Text
            ?? app._settings.Extensions.ApiTokens.FirstOrDefault(t => "api:" + t.Id == id)?.Name ?? id;

        public Task PetAnimateAsync(string extensionId, string move, int? ms)
        {
            var gesture = Gestures.FirstOrDefault(g => PetSheet.AnimationOf(g).Equals(move, StringComparison.OrdinalIgnoreCase), (ChibiMove)(-1));
            if ((int)gesture < 0)
                throw new JsonRpcException(JsonRpcException.InvalidParams, L.T("Gesto desconocido: «{0}» (son: {1}).", move, string.Join(", ", Gestures.Select(PetSheet.AnimationOf))));
            return OnUiAsync(() =>
            {
                if (app._mascotWindow is not null && !app._carrying) app._director.Gesture(gesture, ms ?? 2500);
                return true;
            });
        }

        public Task PetSayAsync(string extensionId, string text) => OnUiAsync(() =>
        {
            var name = NameOf(extensionId);
            if (app._mascotWindow is { } mascot && app._chatBubble?.IsVisible != true)
                app._bubble.Say(new Notice(NoticeKind.Extension, name, text, ""), mascot.Chibi, mascot.ScreenBounds);
            else app.ShowExtensionNotice(name, text);
            return true;
        });

        public Task NoticeAsync(string extensionId, string title, string text) => OnUiAsync(() =>
        {
            if (!app._settings.Notifications) return false;
            app.ShowExtensionNotice(NameOf(extensionId), text, title);
            if (app._settings.Sound && app._island?.Muted != true) app._sounds.Play(NoticeKind.Extension, app._settings.SoundPack);
            return true;
        });

        public Task IslandCardAsync(string extensionId, string title, string text) => OnUiAsync(() =>
        {
            var name = NameOf(extensionId);
            if (app._island is { } island)
                _ = island.Choose(title, $"{text}\n\n{L.T("De la extensión «{0}»", name)}", [("ok", L.T("Listo"))], "ok");
            else app.ShowExtensionNotice(name, text, title);
            return true;
        });

        public Task SpeakAsync(string extensionId, string text, string? voice, CancellationToken cancel) => OnUiAsync(() =>
        {
            var known = voice is not null && (ClaupetVoices.Choices.Any(c => c.Id == voice) || app.Extensions.PackVoices().Any(v => v.Id == voice));
            (app._readAloud ??= new ReadAloud()).Say(text, known ? voice! : app._settings.ReadAloudVoice);
            return true;
        });

        public async Task<string> AskClaudeAsync(string extensionId, string prompt, string? model, CancellationToken cancel)
        {
            var (text, error) = await Assistant.AskPlainAsync(prompt, model, AssistantAccounts.ConfigDir(app._settings), cancel);
            return text ?? throw new JsonRpcException(JsonRpcException.InternalError, error ?? L.T("Claude Code no respondió."));
        }

        public JsonNode Sessions() =>
            new JsonArray((app._lastSessions ?? []).Select(s => (JsonNode)SessionJson(s)).ToArray());

        public Task<string?> ClipboardReadAsync() =>
            OnUiAsync(() => OperatingSystem.IsWindows() ? WinClipboard.GetText() : null);

        public Task ClipboardWriteAsync(string text) => OnUiAsync(() =>
        {
            if (OperatingSystem.IsWindows()) WinClipboard.SetText(text.Length == 0 ? " " : text);
            return true;
        });

        public IDisposable SubscribeAudio(string extensionId, string source, Action<short[]> chunk) =>
            throw new JsonRpcException(JsonRpcException.InternalError, L.T("Escuchar el audio todavía no está en esta app."));
    }
}
