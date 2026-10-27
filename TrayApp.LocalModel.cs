namespace Claupet;

/// <summary>
/// El modelo local en la app: su descarga y su fila en Ajustes, y las listas de modelos de la mascota y la
/// isla, que lo ofrecen junto a los de Claude. Elegirlo sin tenerlo lo descarga.
/// </summary>
sealed partial class TrayApp
{
    readonly LocalModelServer _localServer = new();
    CancellationTokenSource? _localDownload;
    int? _localPercent;
    string? _localError;

    LocalModel.Choice LocalChoice => LocalModel.Find(_settings.LocalModel);

    /// <summary>La mascota o la isla responden con él.</summary>
    bool UsesLocal => _settings.MascotModel == AssistantModels.Local || _settings.IslandModel == AssistantModels.Local;

    /// <summary>El que responde cuando se elige «local»; null si falta descargarlo (Assistant lo dice).</summary>
    IAssistantBrain? LocalBrainIfReady() => LocalModel.Ready(LocalChoice) ? new LocalBrain(_localServer, LocalChoice) : null;

    string LocalLabel() => L.T("Local: {0}", LocalChoice.Name);

    /// <summary>El nombre de un modelo para mostrar: el local con su nombre («Local: Qwen3.5 2B»).</summary>
    string ModelLabel(string? id) => id == AssistantModels.Local ? LocalLabel() : AssistantModels.Label(id);

    IEnumerable<(string Id, string Label)> MascotModelChoices() => [.. AssistantModels.Choices, (AssistantModels.Local, LocalLabel())];

    IEnumerable<(string Id, string Label)> IslandModelChoices() => [.. AssistantModels.IslandChoices, (AssistantModels.Local, LocalLabel())];

    /// <summary>Después de elegir un modelo: si es el local y falta, se descarga.</summary>
    void ModelPicked(string id)
    {
        if (id == AssistantModels.Local) _ = DownloadLocalModelAsync();
    }

    /// <summary>La fila del modelo local en Ajustes: qué dice y qué enlace lleva.</summary>
    (string Status, string Action) LocalStatusText() =>
        _localDownload is not null ? (L.T("Descargando el modelo local… {0} %", _localPercent ?? 0), L.T("Cancelar"))
        : _localError is not null ? (L.T("No se pudo descargar ({0})", _localError), L.T("Reintentar"))
        : LocalModel.Ready(LocalChoice) ? (L.T("En este equipo; usa unos {0} de RAM al responder", LocalChoice.Ram), L.T("Borrar"))
        : (L.T("Sin descargar ({0} MB, una sola vez)", LocalModel.Missing(LocalChoice) / 1_000_000), L.T("Descargar"));

    void LocalModelAction()
    {
        if (_localDownload is { } download) download.Cancel();
        else if (LocalModel.Ready(LocalChoice)) RemoveLocalModel();
        else _ = DownloadLocalModelAsync();
    }

    void PickLocalModel() => ShowOptions(
        LocalModel.Choices.Select(c => (c.Id, L.T("{0} · {1} MB · unos {2} de RAM", c.Name, c.Size / 1_000_000, c.Ram))), LocalChoice.Id, id =>
        {
            if (id == LocalChoice.Id && _settings.LocalModel is not null) return;
            var downloading = _localDownload is not null;
            _localDownload?.Cancel();
            _localServer.Stop();
            Change(s => s.LocalModel = id);
            // Si ya se usa, se baja el nuevo (al terminar se borra el anterior); si se estaba bajando otro, se pulsa «Descargar».
            if (UsesLocal && !downloading) _ = DownloadLocalModelAsync();
        });

    /// <summary>Baja llama-server y el modelo elegido (retoma lo que haya quedado) y muestra el avance en Ajustes.</summary>
    async Task DownloadLocalModelAsync()
    {
        var choice = LocalChoice;
        if (_localDownload is not null || LocalModel.Ready(choice)) return;
        if (_settings.LocalModel is null) Change(s => s.LocalModel = choice.Id);
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
            await LocalModel.DownloadAsync(choice, progress, cancel.Token);
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
    void RemoveLocalModel()
    {
        if (MessageBox.Show(L.T("¿Borrar el modelo local de este equipo? La mascota y la isla vuelven a responder con Claude."),
                "Claupet", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        _localServer.Stop();
        try { LocalModel.Remove(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex);
            MessageBox.Show(L.T("No se pudo borrar: {0}", ex.Message), "Claupet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Change(s =>
        {
            if (s.MascotModel == AssistantModels.Local) s.MascotModel = "haiku";
            if (s.IslandModel == AssistantModels.Local) s.IslandModel = null;
        });
        PushIslandModel();
    }
}
