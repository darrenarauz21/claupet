using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeUsageTray;

/// <summary>Lo de las extensiones que va en settings.json.</summary>
sealed class ExtensionSettings
{
    /// <summary>Por extensión: si está encendida, sus permisos y sus ajustes (las claves no van aquí: ISecretStore).</summary>
    public Dictionary<string, ExtensionState> States { get; set; } = new();
    /// <summary>Ranura exclusiva → la extensión que la ocupa. Sin entrada: el integrado.</summary>
    public Dictionary<string, string> Slots { get; set; } = new();
    /// <summary>La API local (127.0.0.1) para que otros programas le hablen a Claupet; apagada por defecto.</summary>
    public bool LocalApi { get; set; }
    public int LocalApiPort { get; set; } = LocalApiServer.DefaultPort;
    /// <summary>Los tokens de la API local creados en Ajustes (solo su hash).</summary>
    public List<ApiToken> ApiTokens { get; set; } = new();
}

sealed class ExtensionState
{
    public bool Enabled { get; set; } = true;
    public List<string> Granted { get; set; } = new();
    public Dictionary<string, string> Values { get; set; } = new();
    /// <summary>Por qué Claupet la apagó (falló varias veces, sus archivos cambiaron…); null si la apagó la persona o está bien.</summary>
    public string? DisabledReason { get; set; }
}

/// <summary>Las claves (de tipo «secret») de cada extensión, cifradas para esta cuenta de usuario.</summary>
interface ISecretStore
{
    string? Get(string extensionId, string key);
    void Set(string extensionId, string key, string? value);
    void Remove(string extensionId);
}

/// <summary>Para las pruebas y donde no hay dónde cifrar: en memoria, se pierden al cerrar.</summary>
sealed class MemorySecretStore : ISecretStore
{
    readonly ConcurrentDictionary<(string, string), string> _values = new();
    public string? Get(string extensionId, string key) => _values.TryGetValue((extensionId, key), out var v) ? v : null;
    public void Set(string extensionId, string key, string? value)
    {
        if (value is null) _values.TryRemove((extensionId, key), out _);
        else _values[(extensionId, key)] = value;
    }
    public void Remove(string extensionId)
    {
        foreach (var k in _values.Keys.Where(k => k.Item1 == extensionId)) _values.TryRemove(k, out _);
    }
}

/// <summary>
/// Lo que Claupet le deja hacer a una extensión (la API del anfitrión). Lo implementa cada app (la de
/// Windows con la mascota, la isla, la voz…). El administrador ya comprobó el permiso antes de llamar.
/// </summary>
interface IExtensionHostApi
{
    Task PetAnimateAsync(string extensionId, string move, int? ms);
    Task PetSayAsync(string extensionId, string text);
    Task NoticeAsync(string extensionId, string title, string text);
    Task IslandCardAsync(string extensionId, string title, string text);
    Task SpeakAsync(string extensionId, string text, string? voice, CancellationToken cancel);
    Task<string> AskClaudeAsync(string extensionId, string prompt, string? model, CancellationToken cancel);
    JsonNode Sessions();
    Task<string?> ClipboardReadAsync();
    Task ClipboardWriteAsync(string text);
    /// <summary>Audio de 16 kHz, mono, 16 bits, en trozos de 100 ms: del micrófono o de lo que suena en el equipo.</summary>
    IDisposable SubscribeAudio(string source, Action<short[]> chunk);
}

/// <summary>
/// Las extensiones de la app: cuáles están, cuáles ocupan cada ranura, sus permisos y sus ajustes, y las
/// que corren. Su código arranca la primera vez que hace falta (no al abrir la app, salvo las habilidades
/// que escuchan eventos), se verifica antes de cada arranque (los archivos firmados no cambiaron) y, si
/// falla 3 veces en 10 minutos, se apaga: la ranura vuelve al integrado y se avisa por qué.
/// </summary>
sealed class ExtensionManager : IAsyncDisposable
{
    const int MaxFailures = 3;
    static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(10);
    static readonly TimeSpan InitializeTimeout = TimeSpan.FromSeconds(20);

    readonly ExtensionStore _store;
    readonly Func<AppSettings> _settings;
    readonly Action<Action<AppSettings>> _change;
    readonly ISecretStore _secrets;
    readonly ConcurrentDictionary<string, Running> _running = new();
    readonly ConcurrentDictionary<string, List<DateTimeOffset>> _failures = new();
    readonly SemaphoreSlim _starting = new(1, 1);
    List<InstalledExtension> _installed = new();
    List<(string Id, string Problem)> _broken = new();

    /// <summary>Cómo se arranca el anfitrión de las DLL: esta app con --anfitrion-extension (las pruebas ponen otro).</summary>
    public Func<string, bool, (string File, IReadOnlyList<string> Args)> NativeHostCommand { get; init; } = (dll, single) =>
        (Environment.ProcessPath ?? throw new InvalidOperationException("ProcessPath"),
         single ? [NativeExtensionHost.Flag, dll, NativeExtensionHost.SingleFlag] : [NativeExtensionHost.Flag, dll]);

    public IExtensionHostApi? HostApi { get; set; }

    /// <summary>La API local, si está encendida: cada extensión que arranca recibe su token (CLAUPET_API_TOKEN).</summary>
    public LocalApiServer? LocalApi { get; set; }

    /// <summary>Una extensión se apagó sola (falló varias veces o sus archivos cambiaron): el nombre y el motivo.</summary>
    public event Action<string, string>? Disabled;
    /// <summary>Cambió la lista o el estado (para refrescar Ajustes y los proveedores).</summary>
    public event Action? Changed;

    sealed class Running(InstalledExtension extension, IExtensionChannel channel)
    {
        public InstalledExtension Extension { get; } = extension;
        public IExtensionChannel Channel { get; } = channel;
        public JsonElement Info { get; set; }
        public ConcurrentDictionary<string, IDisposable> Audio { get; } = new();
    }

    public ExtensionManager(ExtensionStore store, Func<AppSettings> settings, Action<Action<AppSettings>> change, ISecretStore secrets)
    {
        _store = store;
        _settings = settings;
        _change = change;
        _secrets = secrets;
        Reload();
    }

    public ExtensionStore Store => _store;

    public IReadOnlyList<InstalledExtension> Installed => _installed;

    public IReadOnlyList<(string Id, string Problem)> Broken => _broken;

    /// <summary>Lee otra vez el disco; los estados de las que ya no están se olvidan.</summary>
    public void Reload()
    {
        var broken = new List<(string, string)>();
        _installed = _store.List(broken);
        _broken = broken;
        var ids = _installed.Select(e => e.Id).ToHashSet();
        var settings = _settings();
        if (settings.Extensions.States.Keys.Any(k => !ids.Contains(k)) || settings.Extensions.Slots.Values.Any(v => !ids.Contains(v)))
            _change(s =>
            {
                foreach (var k in s.Extensions.States.Keys.Where(k => !ids.Contains(k)).ToList()) s.Extensions.States.Remove(k);
                foreach (var k in s.Extensions.Slots.Where(p => !ids.Contains(p.Value)).Select(p => p.Key).ToList()) s.Extensions.Slots.Remove(k);
            });
        Changed?.Invoke();
    }

    public InstalledExtension? Find(string id) => _installed.FirstOrDefault(e => e.Id == id);

    public ExtensionState StateOf(string id) => _settings().Extensions.States.TryGetValue(id, out var s) ? s : new ExtensionState { Enabled = false };

    public bool IsEnabled(string id) => _settings().Extensions.States.TryGetValue(id, out var s) && s.Enabled;

    // ── Instalar y quitar ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Instala lo revisado (la persona aceptó lo que muestra InstallReview). Si es una actualización, la
    /// detiene antes. Los permisos declarados quedan dados (los vio al aceptar). Si trae una ranura
    /// exclusiva, la ocupa solo si useForSlots lo dice: el integrado no se apaga sin preguntar.
    /// </summary>
    public async Task<InstalledExtension> InstallAsync(InstallReview review, IReadOnlyCollection<string> useForSlots)
    {
        await StopAsync(review.Manifest.Id);
        var installed = _store.Commit(review);
        _change(s =>
        {
            var state = s.Extensions.States.TryGetValue(installed.Id, out var had) ? had : new ExtensionState();
            state.Enabled = true;
            state.DisabledReason = null;
            state.Granted = installed.Manifest.Permissions.ToList();
            foreach (var setting in installed.Manifest.Settings.Where(x => x.Type != SettingSpec.Secret && x.Default is not null))
                state.Values.TryAdd(setting.Key, setting.Default!);
            // Ajustes que la nueva versión ya no tiene.
            foreach (var k in state.Values.Keys.Where(k => installed.Manifest.Settings.All(x => x.Key != k)).ToList()) state.Values.Remove(k);
            s.Extensions.States[installed.Id] = state;
            foreach (var slot in useForSlots.Where(x => ExtensionSlots.IsExclusive(x) && installed.Manifest.Provides.Contains(x)))
                s.Extensions.Slots[slot] = installed.Id;
            // Una ranura que esta versión ya no trae vuelve al integrado.
            foreach (var slot in s.Extensions.Slots.Where(p => p.Value == installed.Id && !installed.Manifest.Provides.Contains(p.Key)).Select(p => p.Key).ToList())
                s.Extensions.Slots.Remove(slot);
        });
        _failures.TryRemove(installed.Id, out _);
        Reload();
        return installed;
    }

    public async Task UninstallAsync(string id, bool keepData)
    {
        await StopAsync(id);
        _store.Uninstall(id, keepData);
        if (!keepData) _secrets.Remove(id);
        _change(s =>
        {
            s.Extensions.States.Remove(id);
            foreach (var k in s.Extensions.Slots.Where(p => p.Value == id).Select(p => p.Key).ToList()) s.Extensions.Slots.Remove(k);
        });
        Reload();
    }

    public async Task SetEnabledAsync(string id, bool enabled)
    {
        if (!enabled) await StopAsync(id);
        _failures.TryRemove(id, out _);
        _change(s =>
        {
            if (!s.Extensions.States.TryGetValue(id, out var state)) s.Extensions.States[id] = state = new ExtensionState();
            state.Enabled = enabled;
            state.DisabledReason = null;
            if (!enabled) foreach (var k in s.Extensions.Slots.Where(p => p.Value == id).Select(p => p.Key).ToList()) s.Extensions.Slots.Remove(k);
        });
        Changed?.Invoke();
    }

    // ── Ranuras ─────────────────────────────────────────────────────────────────────────────

    /// <summary>La extensión que ocupa la ranura, si está encendida e instalada; null: el integrado.</summary>
    public InstalledExtension? ProviderFor(string slot)
    {
        var settings = _settings().Extensions;
        if (!settings.Slots.TryGetValue(slot, out var id) || !IsEnabled(id)) return null;
        var ext = Find(id);
        return ext is not null && ext.Manifest.Provides.Contains(slot) ? ext : null;
    }

    /// <summary>Quién ocupa la ranura (null: el integrado). Las otras dos ranuras exclusivas no cambian.</summary>
    public async Task SetProviderAsync(string slot, string? id)
    {
        if (!ExtensionSlots.IsExclusive(slot)) throw new ArgumentException(slot);
        if (id is not null && Find(id)?.Manifest.Provides.Contains(slot) != true) throw new ArgumentException(id);
        var before = ProviderFor(slot)?.Id;
        _change(s =>
        {
            if (id is null) s.Extensions.Slots.Remove(slot);
            else s.Extensions.Slots[slot] = id;
        });
        // La que deja la ranura y ya no se usa para nada más, se detiene (libera su memoria).
        if (before is not null && before != id && !_settings().Extensions.Slots.ContainsValue(before)
            && Find(before)?.Manifest.Provides.Contains(ExtensionSlots.Skill) != true)
            await StopAsync(before);
        Changed?.Invoke();
    }

    /// <summary>Las habilidades encendidas.</summary>
    public IEnumerable<InstalledExtension> Skills => _installed.Where(e => IsEnabled(e.Id) && e.Manifest.Provides.Contains(ExtensionSlots.Skill));

    /// <summary>Los paquetes de datos encendidos de un tipo (pet, voice-style, sounds).</summary>
    public IEnumerable<InstalledExtension> DataPacks(string slot) => _installed.Where(e => IsEnabled(e.Id) && e.Manifest.Provides.Contains(slot));

    /// <summary>Un comando de habilidad (/notas) repetido entre dos encendidas: el primero gana y se avisa en Ajustes.</summary>
    public IReadOnlyList<(string Command, string Winner, string Loser)> CommandConflicts()
    {
        var seen = new Dictionary<string, string>();
        var conflicts = new List<(string, string, string)>();
        foreach (var e in Skills.OrderBy(e => e.Record.InstalledAt))
            foreach (var c in e.Manifest.Commands)
                if (!seen.TryAdd(c.Name, e.Id)) conflicts.Add((c.Name, seen[c.Name], e.Id));
        return conflicts;
    }

    /// <summary>La habilidad que atiende «/comando»; null si ninguna.</summary>
    public InstalledExtension? SkillForCommand(string command) =>
        Skills.OrderBy(e => e.Record.InstalledAt).FirstOrDefault(e => e.Manifest.Commands.Any(c => c.Name == command));

    // ── Paquetes de datos ───────────────────────────────────────────────────────────────────

    /// <summary>Lo elegido de un paquete en los ajustes de la app (una voz, un estilo de sonido, una mascota) empieza así.</summary>
    public const string PackPrefix = "ext:";

    /// <summary>Las voces de los paquetes encendidos (estilos de Supertonic): «ext:paquete/voz» y su nombre.</summary>
    public IReadOnlyList<(string Id, string Label)> PackVoices() =>
        DataPacks(ExtensionSlots.VoiceStyle)
            .SelectMany(e => e.Manifest.Voices.Select(v => ($"{PackPrefix}{e.Id}/{v.Id}", $"{v.Name.Text} · {e.Manifest.Name.Text}")))
            .ToList();

    /// <summary>El archivo de estilo de una voz «ext:paquete/voz», con el paquete verificado; null si ya no está o no pasa.</summary>
    public async Task<string?> VoiceFileAsync(string voice, CancellationToken cancel = default)
    {
        if (!voice.StartsWith(PackPrefix, StringComparison.Ordinal) || voice[PackPrefix.Length..].Split('/') is not [var id, var name]) return null;
        var ext = DataPacks(ExtensionSlots.VoiceStyle).FirstOrDefault(e => e.Id == id);
        return ext?.Manifest.Voices.FirstOrDefault(v => v.Id == name) is { } spec ? await PackFileAsync(ext, spec.File, cancel) : null;
    }

    /// <summary>Los estilos de sonido de los paquetes encendidos: «ext:paquete» y su nombre.</summary>
    public IReadOnlyList<(string Id, string Label)> SoundPacks() =>
        DataPacks(ExtensionSlots.Sounds).Select(e => ($"{PackPrefix}{e.Id}", e.Manifest.Name.Text)).ToList();

    /// <summary>
    /// El .wav de un aviso («terminado», «pregunta»…) en el estilo «ext:paquete»; null si el paquete no lo
    /// trae, no está encendido o todavía no se verificó (VerifyPacksAsync): entonces suena el clásico.
    /// </summary>
    public string? SoundFile(string pack, string name)
    {
        if (!pack.StartsWith(PackPrefix, StringComparison.Ordinal)) return null;
        var ext = DataPacks(ExtensionSlots.Sounds).FirstOrDefault(e => e.Id == pack[PackPrefix.Length..]);
        return ext is not null && ext.Manifest.Sounds.TryGetValue(name, out var file) && _checkedPacks.ContainsKey(PackKey(ext)) ? ext.FileOf(file) : null;
    }

    /// <summary>Las mascotas de los paquetes encendidos: el id del paquete y el nombre de la mascota.</summary>
    public IReadOnlyList<(string Id, string Label)> Pets() =>
        DataPacks(ExtensionSlots.Pet).Where(e => e.Manifest.Pet is not null).Select(e => (e.Id, e.Manifest.Pet!.Name.Text)).ToList();

    /// <summary>La mascota de un paquete, leída y verificada; null si no se puede (y, si sus imágenes no sirven, se apaga con el motivo).</summary>
    public async Task<PetSheet?> PetAsync(string id, CancellationToken cancel = default)
    {
        var ext = DataPacks(ExtensionSlots.Pet).FirstOrDefault(e => e.Id == id);
        if (ext?.Manifest.Pet is not { } spec || await PackFileAsync(ext, PackagePaths.Manifest, cancel) is null) return null;
        try { return await Task.Run(() => PetSheet.Load(spec, ext.FileOf), cancel); }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            await DisableAsync(ext, ex.Message);
            return null;
        }
    }

    /// <summary>Verifica los paquetes de datos encendidos (al abrir la app y al instalar): los que alguien tocó se apagan.</summary>
    public async Task VerifyPacksAsync(CancellationToken cancel = default)
    {
        foreach (var ext in _installed.Where(e => IsEnabled(e.Id) && e.Manifest.Provides.Any(p => p is ExtensionSlots.Pet or ExtensionSlots.VoiceStyle or ExtensionSlots.Sounds)).ToList())
            await PackFileAsync(ext, PackagePaths.Manifest, cancel);
    }

    static string PackKey(InstalledExtension ext) => $"{ext.Id}|{ext.Record.Digest}";

    readonly ConcurrentDictionary<string, bool> _checkedPacks = new();

    /// <summary>
    /// Un archivo de un paquete de datos encendido. El paquete se verifica la primera vez en la sesión (y
    /// de nuevo si se reinstala): si alguien tocó sus archivos, se apaga y devuelve null.
    /// </summary>
    public async Task<string?> PackFileAsync(InstalledExtension ext, string packagePath, CancellationToken cancel = default)
    {
        if (!IsEnabled(ext.Id)) return null;
        var key = PackKey(ext);
        if (!_checkedPacks.ContainsKey(key))
        {
            var problem = await Task.Run(() => _store.Verify(ext, cancel), cancel);
            if (problem is not null)
            {
                await DisableAsync(ext, problem);
                return null;
            }
            _checkedPacks[key] = true;
        }
        return ext.FileOf(packagePath);
    }

    // ── Ajustes y claves ────────────────────────────────────────────────────────────────────

    public string? ValueOf(InstalledExtension ext, string key)
    {
        var spec = ext.Manifest.Settings.FirstOrDefault(s => s.Key == key);
        if (spec is null) return null;
        if (spec.Type == SettingSpec.Secret) return _secrets.Get(ext.Id, key);
        return StateOf(ext.Id).Values.TryGetValue(key, out var v) ? v : spec.Default;
    }

    /// <summary>Cambia un ajuste (validado según su tipo). La extensión que corre lo recibe con «settings/changed».</summary>
    public async Task SetValueAsync(InstalledExtension ext, string key, string? value)
    {
        var spec = ext.Manifest.Settings.FirstOrDefault(s => s.Key == key) ?? throw new ArgumentException(key);
        if (value is not null)
        {
            var ok = spec.Type switch
            {
                SettingSpec.Bool => value is "true" or "false",
                SettingSpec.Number => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _),
                SettingSpec.Choice => spec.Choices.Contains(value),
                _ => value.Length <= 4000,
            };
            if (!ok) throw new ArgumentException(L.T("Valor inválido para «{0}».", spec.Label.Text));
        }
        if (spec.Type == SettingSpec.Secret) _secrets.Set(ext.Id, key, value);
        else _change(s =>
        {
            if (!s.Extensions.States.TryGetValue(ext.Id, out var state)) s.Extensions.States[ext.Id] = state = new ExtensionState();
            if (value is null) state.Values.Remove(key);
            else state.Values[key] = value;
        });
        if (_running.TryGetValue(ext.Id, out var running) && running.Channel.Alive)
            try { await running.Channel.NotifyAsync("settings/changed", SettingsJson(ext)); } catch (JsonRpcException) { }
    }

    /// <summary>Los ajustes con sus valores; las claves, solo si includeSecrets (al iniciar la extensión).</summary>
    JsonObject SettingsJson(InstalledExtension ext, bool includeSecrets = true)
    {
        var o = new JsonObject();
        foreach (var spec in ext.Manifest.Settings)
            if (includeSecrets || spec.Type != SettingSpec.Secret)
                o[spec.Key] = ValueOf(ext, spec.Key);
        return o;
    }

    // ── Correr ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// El canal con la extensión, arrancándola si no corre: verifica sus archivos, la lanza y la inicia
    /// («initialize»). Lanza ExtensionUnavailableException si no se puede (con el motivo para la persona).
    /// </summary>
    public async Task<IExtensionChannel> ChannelAsync(string id, CancellationToken cancel = default)
    {
        if (_running.TryGetValue(id, out var running) && running.Channel.Alive) return running.Channel;
        await _starting.WaitAsync(cancel);
        try
        {
            if (_running.TryGetValue(id, out running) && running.Channel.Alive) return running.Channel;
            if (running is not null) await DropAsync(id, running);
            var ext = Find(id) ?? throw new ExtensionUnavailableException(L.T("La extensión ya no está instalada."));
            if (!IsEnabled(id)) throw new ExtensionUnavailableException(L.T("La extensión «{0}» está apagada.", ext.Manifest.Name.Text));
            var kind = ext.Manifest.Runtime;
            if (kind is null || kind.Protocol == RuntimeSpec.Text || (kind.Type == RuntimeSpec.Http && kind.Format != RuntimeSpec.ClaupetRpc))
                throw new ExtensionUnavailableException(L.T("Esa extensión no habla JSON-RPC."));

            var problem = await Task.Run(() => _store.Verify(ext, cancel), cancel);
            if (problem is not null)
            {
                await DisableAsync(ext, problem);
                throw new ExtensionUnavailableException(problem);
            }
            IExtensionChannel channel;
            try { channel = Launch(ext); }
            catch (Exception ex) when (ex is not ExtensionUnavailableException)
            {
                await FailedAsync(ext, ex.Message);
                throw new ExtensionUnavailableException(L.T("No se pudo arrancar «{0}»: {1}", ext.Manifest.Name.Text, ex.Message));
            }
            running = new Running(ext, channel);
            _running[id] = running;
            channel.Stopped += reason => _ = OnStoppedAsync(ext, running, reason);
            try
            {
                running.Info = await channel.CallAsync("initialize", InitializeParams(ext), cancel, timeout: InitializeTimeout);
            }
            catch (Exception ex) when (ex is JsonRpcException or IOException)
            {
                await DropAsync(id, running);
                await FailedAsync(ext, ex.Message);
                throw new ExtensionUnavailableException(L.T("«{0}» no se pudo iniciar: {1}", ext.Manifest.Name.Text, ex.Message));
            }
            return channel;
        }
        finally { _starting.Release(); }
    }

    JsonObject InitializeParams(InstalledExtension ext) => new()
    {
        ["claupetApi"] = ExtensionManifest.ApiVersion,
        ["claupet"] = SemVersion.App.ToString(),
        ["language"] = L.Language,
        ["platform"] = ExtensionPlatform.Current,
        ["extension"] = new JsonObject
        {
            ["id"] = ext.Id,
            ["version"] = ext.Manifest.Version.ToString(),
            ["packageFolder"] = ext.Folder,
            ["dataFolder"] = ext.DataFolder,
        },
        ["permissions"] = new JsonArray(StateOf(ext.Id).Granted.Select(p => (JsonNode)p).ToArray()),
        ["settings"] = SettingsJson(ext),
    };

    IExtensionChannel Launch(InstalledExtension ext)
    {
        var runtime = ext.Manifest.Runtime!;
        if (runtime.Type == RuntimeSpec.Http) return new HttpRpcChannel(this, ext);
        string file;
        IEnumerable<string> args;
        if (runtime.Type == RuntimeSpec.Native)
        {
            var library = runtime.TargetFor(ExtensionPlatform.Current)
                ?? throw new ExtensionUnavailableException(L.T("«{0}» no trae su DLL para {1}.", ext.Manifest.Name.Text, ExtensionPlatform.Current));
            var dll = ext.FileOf(library);
            if (NativeExtensionHost.ArchitectureProblem(dll) is { } arch) throw new ExtensionUnavailableException(arch);
            (file, var hostArgs) = NativeHostCommand(dll, runtime.SingleThreaded);
            args = hostArgs;
        }
        else
        {
            if (runtime.Program is { } program)
                file = ProgramResolver.Find(program) ?? throw new ExtensionUnavailableException(L.T("«{0}» necesita «{1}», que no está instalado (o no está en el PATH).", ext.Manifest.Name.Text, program));
            else
                file = ext.FileOf(runtime.TargetFor(ExtensionPlatform.Current)
                    ?? throw new ExtensionUnavailableException(L.T("«{0}» no trae su programa para {1}.", ext.Manifest.Name.Text, ExtensionPlatform.Current)));
            args = runtime.Args.Select(a => a.Replace("{package}", ext.Folder).Replace("{data}", ext.DataFolder));
        }
        var environment = new Dictionary<string, string>
        {
            ["CLAUPET_API"] = ExtensionManifest.ApiVersion.ToString(),
            ["CLAUPET_EXTENSION_ID"] = ext.Id,
            ["CLAUPET_PACKAGE"] = ext.Folder,
            ["CLAUPET_DATA"] = ext.DataFolder,
            ["CLAUPET_LANGUAGE"] = L.Language,
        };
        if (LocalApi is { Running: true } api)
        {
            environment["CLAUPET_API_URL"] = api.Url;
            environment["CLAUPET_API_TOKEN"] = api.IssueFor(new HostCaller(ext.Id, ext.Manifest.Name.Text, StateOf(ext.Id).Granted, ext));
        }
        var log = new ExtensionLog(ext.DataFolder);
        return ExtensionProcess.Start(file, args, ext.DataFolder, log, environment,
            (method, parameters, cancel) => HandleAsync(ext, method, parameters, cancel),
            (method, parameters) => _ = HandleNotificationAsync(ext, method, parameters));
    }

    async Task OnStoppedAsync(InstalledExtension ext, Running running, string reason)
    {
        if (!_running.TryGetValue(ext.Id, out var current) || current != running) return; // ya se detuvo a propósito
        await DropAsync(ext.Id, running);
        await FailedAsync(ext, reason);
    }

    async Task DropAsync(string id, Running running)
    {
        _running.TryRemove(new KeyValuePair<string, Running>(id, running));
        LocalApi?.Revoke(id);
        foreach (var a in running.Audio.Values) a.Dispose();
        running.Audio.Clear();
        try { await running.Channel.DisposeAsync(); } catch (Exception ex) { ErrorLog.Write(ex); }
    }

    /// <summary>Anota un fallo; al tercero en 10 minutos, la apaga y su ranura vuelve al integrado.</summary>
    async Task FailedAsync(InstalledExtension ext, string reason)
    {
        var now = DateTimeOffset.UtcNow;
        var list = _failures.GetOrAdd(ext.Id, _ => new List<DateTimeOffset>());
        int count;
        lock (list)
        {
            list.Add(now);
            list.RemoveAll(t => now - t > FailureWindow);
            count = list.Count;
        }
        new ExtensionLog(ext.DataFolder).Write("[claupet] " + L.T("Falló ({0} de {1}): {2}", count, MaxFailures, reason));
        if (count >= MaxFailures) await DisableAsync(ext, L.T("Falló {0} veces seguidas: {1}", count, reason));
    }

    async Task DisableAsync(InstalledExtension ext, string reason)
    {
        await StopAsync(ext.Id);
        _change(s =>
        {
            if (!s.Extensions.States.TryGetValue(ext.Id, out var state)) s.Extensions.States[ext.Id] = state = new ExtensionState();
            state.Enabled = false;
            state.DisabledReason = reason;
            foreach (var k in s.Extensions.Slots.Where(p => p.Value == ext.Id).Select(p => p.Key).ToList()) s.Extensions.Slots.Remove(k);
        });
        Disabled?.Invoke(ext.Manifest.Name.Text, reason);
        Changed?.Invoke();
    }

    public async Task StopAsync(string id)
    {
        if (_running.TryGetValue(id, out var running)) await DropAsync(id, running);
    }

    public async Task StopAllAsync()
    {
        foreach (var (id, running) in _running.ToArray()) await DropAsync(id, running);
    }

    public bool IsRunning(string id) => _running.TryGetValue(id, out var r) && r.Channel.Alive;

    public ValueTask DisposeAsync() => new(StopAllAsync());

    /// <summary>Llama a un método de la extensión (arrancándola si hace falta).</summary>
    public async Task<JsonElement> CallAsync(string id, string method, JsonNode? parameters, CancellationToken cancel = default,
        Action<JsonElement>? onProgress = null, TimeSpan? timeout = null)
    {
        var channel = await ChannelAsync(id, cancel);
        return await channel.CallAsync(method, parameters, cancel, onProgress, timeout);
    }

    /// <summary>Avisa un evento a las habilidades encendidas que lo escuchan (y las arranca si hace falta).</summary>
    public async Task BroadcastAsync(string eventName, JsonNode data)
    {
        foreach (var skill in Skills.Where(s => s.Manifest.Events.Contains(eventName)).ToList())
        {
            try
            {
                var channel = await ChannelAsync(skill.Id);
                await channel.NotifyAsync("skill/event", new JsonObject { ["event"] = eventName, ["data"] = data.DeepClone() });
            }
            catch (Exception ex) when (ex is ExtensionUnavailableException or JsonRpcException) { ErrorLog.Write(ex); }
        }
    }

    /// <summary>Arranca las habilidades que escuchan eventos (al abrir la app).</summary>
    public async Task StartListenersAsync()
    {
        foreach (var skill in Skills.Where(s => s.Manifest.Events.Count > 0).ToList())
        {
            try { await ChannelAsync(skill.Id); }
            catch (ExtensionUnavailableException ex) { ErrorLog.Write(ex); }
        }
    }

    // ── La API del anfitrión ────────────────────────────────────────────────────────────────

    /// <summary>El permiso que pide cada método de la API; los que no están en la lista no piden ninguno.</summary>
    public static readonly IReadOnlyDictionary<string, string> MethodPermissions = new Dictionary<string, string>
    {
        ["pet/animate"] = ExtensionPermissions.Pet,
        ["pet/say"] = ExtensionPermissions.Pet,
        ["notice/show"] = ExtensionPermissions.Notices,
        ["island/card"] = ExtensionPermissions.Island,
        ["voice/speak"] = ExtensionPermissions.Voice,
        ["claude/ask"] = ExtensionPermissions.Claude,
        ["sessions/list"] = ExtensionPermissions.Sessions,
        ["clipboard/read"] = ExtensionPermissions.Clipboard,
        ["clipboard/write"] = ExtensionPermissions.Clipboard,
    };

    /// <summary>Lo que la extensión le pide a Claupet: se comprueba su permiso y se atiende.</summary>
    internal Task<JsonNode?> HandleAsync(InstalledExtension ext, string method, JsonElement p, CancellationToken cancel) =>
        HandleAsync(new HostCaller(ext.Id, ext.Manifest.Name.Text, StateOf(ext.Id).Granted, ext), method, p, cancel);

    /// <summary>
    /// Lo que le piden a Claupet una extensión o un programa por la API local (sin extensión: no tiene
    /// ajustes, registro ni audio, que necesitan el canal de vuelta).
    /// </summary>
    internal async Task<JsonNode?> HandleAsync(HostCaller caller, string method, JsonElement p, CancellationToken cancel)
    {
        var granted = caller.Granted;
        if (caller.Extension is null && method is "settings/get" or "settings/set" or "log" or "audio/subscribe" or "audio/unsubscribe")
            throw new JsonRpcException(JsonRpcException.MethodNotFound, L.T("«{0}» es solo para extensiones.", method));
        var ext = caller.Extension!;
        var needed = method == "audio/subscribe"
            ? Str(p, "source") == "system" ? ExtensionPermissions.SystemAudio : ExtensionPermissions.Microphone
            : MethodPermissions.TryGetValue(method, out var perm) ? perm : null;
        if (needed is not null && !granted.Contains(needed))
            throw new JsonRpcException(JsonRpcException.PermissionDenied, L.T("Sin permiso para «{0}» ({1}).", method, needed));

        var api = HostApi;
        switch (method)
        {
            case "host/info":
                return new JsonObject
                {
                    ["claupet"] = SemVersion.App.ToString(), ["language"] = L.Language, ["platform"] = ExtensionPlatform.Current,
                    ["permissions"] = new JsonArray(granted.Select(g => (JsonNode)g).ToArray()),
                };
            case "settings/get":
                return SettingsJson(ext);
            case "settings/set":
                {
                    var key = Required(p, "key");
                    var spec = ext.Manifest.Settings.FirstOrDefault(s => s.Key == key)
                        ?? throw new JsonRpcException(JsonRpcException.InvalidParams, L.T("Ajuste desconocido: «{0}».", key));
                    var value = p.TryGetProperty("value", out var v) ? v.ValueKind switch
                    {
                        JsonValueKind.Null => null,
                        JsonValueKind.String => v.GetString(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Number => v.GetRawText(),
                        _ => throw new JsonRpcException(JsonRpcException.InvalidParams, L.T("Valor inválido para «{0}».", key)),
                    } : null;
                    try { await SetValueAsync(ext, spec.Key, value); }
                    catch (ArgumentException ex) { throw new JsonRpcException(JsonRpcException.InvalidParams, ex.Message); }
                    return null;
                }
            case "log":
                new ExtensionLog(ext.DataFolder).Write(Clip(Str(p, "message") ?? "", 2000));
                return null;
        }
        if (api is null) throw new JsonRpcException(JsonRpcException.InternalError, L.T("Claupet todavía no está lista."));
        switch (method)
        {
            case "pet/animate":
                await api.PetAnimateAsync(caller.Id, Required(p, "move"), p.TryGetProperty("ms", out var ms) && ms.TryGetInt32(out var m) ? Math.Clamp(m, 100, 60_000) : null);
                return null;
            case "pet/say":
                await api.PetSayAsync(caller.Id, Clip(Required(p, "text"), 300));
                return null;
            case "notice/show":
                await api.NoticeAsync(caller.Id, Clip(Required(p, "title"), 80), Clip(Str(p, "text") ?? "", 300));
                return null;
            case "island/card":
                await api.IslandCardAsync(caller.Id, Clip(Required(p, "title"), 80), Clip(Str(p, "text") ?? "", 4000));
                return null;
            case "voice/speak":
                await api.SpeakAsync(caller.Id, Clip(Required(p, "text"), 4000), Str(p, "voice"), cancel);
                return null;
            case "claude/ask":
                return new JsonObject { ["text"] = await api.AskClaudeAsync(caller.Id, Clip(Required(p, "prompt"), 20_000), Str(p, "model"), cancel) };
            case "sessions/list":
                return api.Sessions();
            case "clipboard/read":
                return new JsonObject { ["text"] = await api.ClipboardReadAsync() };
            case "clipboard/write":
                await api.ClipboardWriteAsync(Clip(Required(p, "text"), 100_000));
                return null;
            case "audio/subscribe":
                {
                    var source = Str(p, "source") == "system" ? "system" : "microphone";
                    if (!_running.TryGetValue(ext.Id, out var running)) throw new JsonRpcException(JsonRpcException.InternalError, L.T("La extensión no está corriendo."));
                    var subscription = Guid.NewGuid().ToString("N");
                    var channel = running.Channel;
                    running.Audio[subscription] = api.SubscribeAudio(source, pcm =>
                    {
                        if (!channel.Alive) return;
                        var bytes = new byte[pcm.Length * 2];
                        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
                        _ = channel.NotifyAsync("audio/chunk", new JsonObject
                        {
                            ["subscription"] = subscription, ["source"] = source, ["sampleRate"] = 16000, ["audio"] = Convert.ToBase64String(bytes),
                        }).ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                    });
                    return new JsonObject { ["subscription"] = subscription };
                }
            case "audio/unsubscribe":
                {
                    if (_running.TryGetValue(ext.Id, out var running) && running.Audio.TryRemove(Required(p, "subscription"), out var sub)) sub.Dispose();
                    return null;
                }
        }
        throw new JsonRpcException(JsonRpcException.MethodNotFound, L.T("Método desconocido: {0}", method));
    }

    Task HandleNotificationAsync(InstalledExtension ext, string method, JsonElement p)
    {
        if (method == "log") new ExtensionLog(ext.DataFolder).Write(Clip(Str(p, "message") ?? "", 2000));
        return Task.CompletedTask;
    }

    static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object ? Json.Str(e, name) : null;

    static string Required(JsonElement e, string name) =>
        Str(e, name) is { Length: > 0 } v ? v : throw new JsonRpcException(JsonRpcException.InvalidParams, L.T("Falta «{0}».", name));

    static string Clip(string text, int max) => text.Length <= max ? text : text[..max];
}

/// <summary>Quién llama a la API del anfitrión: una extensión (Extension) o un programa con un token de la API local.</summary>
sealed record HostCaller(string Id, string Name, IReadOnlyList<string> Granted, InstalledExtension? Extension);

/// <summary>No se pudo usar la extensión (apagada, sin instalar, no arranca…): el mensaje es para la persona.</summary>
sealed class ExtensionUnavailableException(string message) : Exception(message);
