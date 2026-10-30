namespace Claupet;

/// <summary>
/// El modelo local, como en la app WinForms (por ahora solo en Windows: llama-server se baja compilado para Windows): su
/// descarga y su fila en Ajustes, y las listas de modelos de la mascota y la isla, que lo ofrecen junto a los de Claude.
/// Elegirlo sin tenerlo lo descarga.
/// </summary>
sealed partial class TrayController
{
    readonly LocalModelServer _localServer = new();
    CancellationTokenSource? _localDownload;
    int? _localPercent;
    string? _localError;

    /// <summary>Esta app tiene el modelo local en este sistema.</summary>
    static bool HasLocal => OperatingSystem.IsWindows();

    LocalModel.Choice LocalChoice => LocalModel.Find(_settings.LocalModel);

    /// <summary>La mascota o la isla responden con él.</summary>
    bool UsesLocal => _settings.MascotModel == AssistantModels.Local || _settings.IslandModel == AssistantModels.Local;

    /// <summary>El que responde cuando se elige «local»; null si falta descargarlo (Assistant lo dice).</summary>
    IAssistantBrain? LocalBrainIfReady() => HasLocal && LocalModel.Ready(LocalChoice) ? new LocalBrain(_localServer, LocalChoice) : null;

    string LocalLabel() => L.T("Local: {0}", LocalChoice.Name);

    /// <summary>El nombre de un modelo para mostrar: el local con su nombre («Local: Qwen3.5 2B»).</summary>
    string ModelLabel(string? id) => id == AssistantModels.Local ? LocalLabel() : AssistantModels.Label(id);

    IReadOnlyList<(string Id, string Label)> MascotModelChoices() =>
        HasLocal ? [.. AssistantModels.Choices, (AssistantModels.Local, LocalLabel())] : AssistantModels.Choices;

    IReadOnlyList<(string Id, string Label)> IslandModelChoices() =>
        HasLocal ? [.. AssistantModels.IslandChoices, (AssistantModels.Local, LocalLabel())] : AssistantModels.IslandChoices;

    void StartLocalModel()
    {
        if (!HasLocal) return;
        Assistant.Local = LocalBrainIfReady;
        _localServer.IdleMinutes = _settings.LocalIdleMinutes;
        _localServer.Gpu = _settings.LocalGpu;
    }

    /// <summary>Con la tarjeta gráfica (si se puede o siempre): hace falta la versión de llama-server con Vulkan.</summary>
    bool WantsGpu => _settings.LocalGpu != LocalModel.GpuOff;

    /// <summary>Está el modelo, pero solo la versión de llama-server de antes (sin la tarjeta gráfica) y se la quiere usar.</summary>
    bool GpuMissing => LocalModel.Ready(LocalChoice) && WantsGpu && !LocalModel.GpuServerReady;

    static IReadOnlyList<(string Id, string Label)> GpuChoices() =>
    [
        (LocalModel.GpuAuto, L.T("Automático: la usa si arranca con ella")),
        (LocalModel.GpuOn, L.T("Siempre")),
        (LocalModel.GpuOff, L.T("Nunca: solo el procesador")),
    ];

    /// <summary>Lo que dice la fila: el modo y, si ya corre en una, su nombre.</summary>
    string LocalGpuLabel()
    {
        var mode = _settings.LocalGpu switch
        {
            LocalModel.GpuOn => L.T("Siempre"),
            LocalModel.GpuOff => L.T("Nunca"),
            _ => L.T("Automático"),
        };
        return _localServer.Device is { } device ? $"{mode} · {device}" : mode;
    }

    void PickLocalGpu() => Choose(GpuChoices(), _settings.LocalGpu, v =>
    {
        _settings.LocalGpu = v;
        SettingsStore.Save(_settings);
        _localServer.Gpu = v;
        _localServer.Stop(); // vuelve a arrancar así en la próxima pregunta
        if (GpuMissing) _ = DownloadLocalModelAsync(); // la versión de llama-server con Vulkan (unos 37 MB)
        PushSettings();
    });

    /// <summary>Después de elegir un modelo: si es el local y falta, se descarga.</summary>
    void ModelPicked(string id)
    {
        if (id == AssistantModels.Local && HasLocal) _ = DownloadLocalModelAsync();
    }

    /// <summary>La fila del modelo local en Ajustes: qué dice y qué enlace lleva.</summary>
    (string Status, string Action) LocalStatusText() =>
        _localDownload is not null ? (L.T("Descargando el modelo local… {0} %", _localPercent ?? 0), L.T("Cancelar"))
        : _localError is not null ? (L.T("No se pudo descargar ({0})", _localError), L.T("Reintentar"))
        : GpuMissing ? (L.T("En este equipo, con el procesador; con la tarjeta gráfica responde más rápido ({0} MB más)",
            LocalModel.ServerSize / 1_000_000), L.T("Descargar"))
        : LocalModel.Ready(LocalChoice) ? (L.T("En este equipo; usa unos {0} de RAM al responder", LocalChoice.Ram), L.T("Borrar"))
        : (L.T("Sin descargar ({0} MB, una sola vez)", LocalModel.Missing(LocalChoice, WantsGpu) / 1_000_000), L.T("Descargar"));

    void LocalModelAction()
    {
        if (_localDownload is { } download) download.Cancel();
        else if (GpuMissing) _ = DownloadLocalModelAsync();
        else if (LocalModel.Ready(LocalChoice)) _ = RemoveLocalModelAsync();
        else _ = DownloadLocalModelAsync();
    }

    void PickLocalModel() => Choose(
        LocalModel.Choices.Select(c => (c.Id, L.T("{0} · {1} MB · unos {2} de RAM", c.Name, c.Size / 1_000_000, c.Ram))), LocalChoice.Id, id =>
        {
            if (id == LocalChoice.Id && _settings.LocalModel is not null) return;
            var downloading = _localDownload is not null;
            _localDownload?.Cancel();
            _localServer.Stop();
            _settings.LocalModel = id;
            SettingsStore.Save(_settings);
            // Si ya se usa, se baja el nuevo (al terminar se borra el anterior); si se estaba bajando otro, se pulsa «Descargar».
            if (UsesLocal && !downloading) _ = DownloadLocalModelAsync();
        });

    void PickLocalIdle() => Choose(new[] { 2, 5, 10, 30 }.Select(m => (m.ToString(), L.T("{0} min", m))), _settings.LocalIdleMinutes.ToString(), v =>
    {
        _settings.LocalIdleMinutes = int.Parse(v);
        SettingsStore.Save(_settings);
        _localServer.IdleMinutes = _settings.LocalIdleMinutes;
        _localServer.Stop(); // vuelve a arrancar con el nuevo tiempo en la próxima pregunta
    });

    /// <summary>Baja llama-server y el modelo elegido (retoma lo que haya quedado) y muestra el avance en Ajustes.</summary>
    async Task DownloadLocalModelAsync()
    {
        var choice = LocalChoice;
        if (!HasLocal || _localDownload is not null || LocalModel.Ready(choice) && !GpuMissing) return;
        if (_settings.LocalModel is null)
        {
            _settings.LocalModel = choice.Id;
            SettingsStore.Save(_settings);
        }
        var cancel = _localDownload = new CancellationTokenSource();
        _localError = null;
        _localPercent = 0;
        PushSettings();
        var progress = new Progress<double>(p =>
        {
            var percent = (int)(p * 100);
            if (_localDownload != cancel || percent == _localPercent) return;
            _localPercent = percent;
            PushSettings();
        });
        try
        {
            _localServer.Stop(); // al terminar se borran los otros modelos: ninguno puede estar abierto
            await LocalModel.DownloadAsync(choice, progress, cancel.Token, gpu: WantsGpu);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            _localError = ex is IOException ? ex.Message.TrimEnd('.') : L.T("el servidor no respondió");
        }
        finally
        {
            _localDownload = null;
            _localPercent = null;
            cancel.Dispose();
            PushSettings();
        }
    }

    /// <summary>Borra llama-server y el modelo; la mascota y la isla vuelven a Claude.</summary>
    async Task RemoveLocalModelAsync()
    {
        if (!await Dialogs.Confirm(L.T("¿Borrar el modelo local de este equipo? La mascota y la isla vuelven a responder con Claude."), _theme)) return;
        _localServer.Stop();
        try { LocalModel.Remove(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex);
            await Dialogs.Info(L.T("No se pudo borrar: {0}", ex.Message), _theme);
        }
        if (_settings.MascotModel == AssistantModels.Local) _settings.MascotModel = "haiku";
        if (_settings.IslandModel == AssistantModels.Local) _settings.IslandModel = null;
        SettingsStore.Save(_settings);
        PushIslandModel();
        PushSettings();
    }

    /// <summary>Un motor dicho para Ajustes: el modelo local avisa si falta descargarlo.</summary>
    string EngineLabel(Engine engine) =>
        engine == Engine.Local && HasLocal && !LocalModel.Ready(LocalChoice) ? L.T("{0} (sin descargar)", Engines.Label(engine)) : Engines.Label(engine);
}
