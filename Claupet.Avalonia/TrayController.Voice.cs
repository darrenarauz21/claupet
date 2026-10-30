using Avalonia.Threading;

namespace Claupet;

/// <summary>
/// La voz, como en Windows: hablarle a la mascota o a la isla con la tecla (o el botón del micrófono) y que lea las
/// respuestas en voz alta. Los modelos (Whisper y la voz de Claupet) se descargan una vez, al encender cada cosa, y el
/// avance se ve en Claupet Console › Asistente.
/// </summary>
sealed partial class TrayController
{
    VoiceInput? _voice;
    ReadAloud? _readAloud;
    /// <summary>A quién se le habla esta vez, si se usó el micrófono de la isla y si está escuchando.</summary>
    bool _voiceToIsland, _micFromIsland, _voiceActive;
    CancellationTokenSource? _modelDownload, _speechDownload;
    int? _modelPercent, _speechPercent;
    string? _modelError, _speechError;

    /// <summary>El micrófono: con «Micrófono» encendido y alguien a quien hablarle (el chat de la mascota o la isla).</summary>
    void UpdateVoice()
    {
        if (!_settings.Mic || !(_settings.Chat || _settings.Island))
        {
            _voice?.Dispose();
            _voice = null;
            if (_voiceActive) OnVoiceCanceled(); // se apagó mientras escuchaba
        }
        else if (_voice is null)
        {
            var voice = _voice = new VoiceInput();
            voice.Started += OnVoiceStarted;
            voice.Level += level =>
            {
                if (_voiceToIsland) _island?.SetVoiceLevel(level);
                else _chatBubble?.SetLevel(level);
            };
            voice.Partial += text =>
            {
                if (_voiceToIsland) _island?.SetHeard(text);
                else _chatBubble?.SetHeard(text);
            };
            voice.Converting += () =>
            {
                if (_voiceToIsland) _island?.ConvertingVoice();
                else _chatBubble?.Converting();
            };
            voice.Heard += OnVoiceHeard;
            voice.Canceled += OnVoiceCanceled;
            voice.NeedsModel += () => VoiceNote(_modelDownload is not null
                ? L.T("El modelo de voz se está descargando ({0} %).", _modelPercent ?? 0)
                : L.T("Falta el modelo de voz: se descarga en Ajustes › Asistente."));
            voice.Failed += VoiceNote;
        }
        if (_voice is { } current)
        {
            // Fuera de Windows no se graban combinaciones: una grabada vuelve a Ctrl derecho.
            current.Key = MicKeys.IsHold(_settings.MicKey) ? _settings.MicKey : MicKeys.Choices[0].Id;
            // Una combinación grabada la vigila KeyHook; una copia de prueba (--preview) en Windows no mira la tecla: la
            // mira la app de siempre, con los mismos ajustes.
            current.WatchKey = !(OperatingSystem.IsWindows() && Program.Args.Contains("--preview")) && !TalkIsCombo && !_locked;
        }
        if (_island is { } island) island.MicEnabled = _voice is not null;
        UpdateKeys();
    }

    /// <summary>La tecla para hablar, como se dice en Ajustes.</summary>
    string TalkKeyLabel => MicKeys.Label(MicKeys.IsHold(_settings.MicKey) || TalkIsCombo ? _settings.MicKey : MicKeys.Choices[0].Id);

    /// <summary>A quién se le habla: a la isla si está abierta, si se usó su micrófono o si no hay mascota con chat; si no, a la mascota.</summary>
    bool VoiceToIsland() =>
        _island is { } island && (_micFromIsland || island.IsOpen || !(_settings.Chat && _mascotWindow is not null));

    void OnVoiceStarted()
    {
        if (_voice is not { } voice) return;
        _readAloud?.Stop();
        _voiceToIsland = VoiceToIsland();
        _micFromIsland = false;
        var hint = voice.Hint(_voiceTrigger ?? TalkKeyLabel);
        _voiceTrigger = null;
        if (_voiceToIsland && _island is { } island)
        {
            island.Listen(hint);
            if (_mascotWindow is not null) _director.SetMood(ChibiMove.Listen); // mira a la isla mientras le hablan
        }
        else if (_settings.Chat && _mascotWindow is { } mascot)
        {
            _bubble.Hide();
            _asking?.Cancel();
            _pendingTask = null;
            ChatBubbleFor().Listen(mascot.ScreenBounds, hint);
            _director.SetMood(ChibiMove.Hear);
        }
        else
        {
            voice.Cancel();
            return;
        }
        _voiceActive = true;
    }

    void OnVoiceHeard(string text)
    {
        _voiceActive = false;
        if (_voiceToIsland)
        {
            if (_island is not { } island) return;
            if (_mascotWindow is not null) _director.SetMood(null);
            island.StopListening(text.Length == 0 ? L.T("No se entendió nada. Pruebe otra vez, más cerca del micrófono.") : null);
            if (text.Length > 0) AskIsland(text);
            return;
        }
        if (_mascotWindow is not { } mascot || _chatBubble is not { } bubble) return;
        if (text.Length > 0)
        {
            _ = AskMascotAsync(text);
            return;
        }
        bubble.Say(mascot.ScreenBounds, L.T("No te entendí… ¿me lo repites? (._.)"));
        _director.SetMood(null);
    }

    void OnVoiceCanceled()
    {
        _voiceActive = false;
        if (_voiceToIsland)
        {
            _island?.StopListening();
            if (_mascotWindow is not null) _director.SetMood(null);
        }
        else if (_chatBubble is { Current: ChatBubbleWindow.Mode.Listening } bubble) bubble.Dismiss(notify: true);
    }

    /// <summary>Un aviso corto de la voz (falta el modelo, no hay micrófono…) donde se le iba a hablar.</summary>
    void VoiceNote(string message)
    {
        var toIsland = _voiceActive ? _voiceToIsland : VoiceToIsland();
        _voiceActive = _micFromIsland = false;
        if (toIsland && _island is { } island)
        {
            island.ShowPage(IslandWindow.Page.Chat);
            island.StopListening(message);
        }
        else if (_mascotWindow is { } mascot)
        {
            ChatBubbleFor().Say(mascot.ScreenBounds, message + " (._.)");
            _director.SetMood(null);
        }
    }

    // ── Leer en voz alta ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lee la respuesta en voz alta, si se pidió (Ajustes › Asistente › Voz). show corre en el hilo de la interfaz
    /// cuando la voz empieza a sonar, para mostrar el texto a la vez; sin voz, enseguida.
    /// </summary>
    void ReadAloudIfOn(string text, Action? show = null)
    {
        if (!_settings.ReadAloud || text.Length == 0)
        {
            show?.Invoke();
            return;
        }
        Action? started = show is null ? null : () => Dispatcher.UIThread.Post(show);
        (_readAloud ??= new ReadAloud()).Say(text, _settings.ReadAloudVoice, started);
    }

    /// <summary>Lee lo que se mostró: si es la respuesta de Claude, con sus risas o suspiros (Spoken); si no, tal cual.</summary>
    void ReadAloudIfOn(AssistantResult result, string shown, Action? show = null) =>
        ReadAloudIfOn(shown == result.Text && result.Spoken is { } spoken ? spoken : shown, show);

    /// <summary>La respuesta se leerá con la voz de Claupet: Claude puede ponerle risas o suspiros.</summary>
    bool Expressive => _settings.ReadAloud && NeedsSupertonic(_settings.ReadAloudVoice);

    /// <summary>Una voz que necesita el modelo de Supertonic: la de Claupet o la de un paquete.</summary>
    static bool NeedsSupertonic(string voice) => ClaupetVoices.IsClaupet(voice) || voice.StartsWith(ExtensionManager.PackPrefix, StringComparison.Ordinal);

    /// <summary>Las voces para elegir: en Linux y macOS, la tercera es la del sistema; detrás, las de los paquetes.</summary>
    IEnumerable<(string Id, string Label)> VoiceChoices() => ClaupetVoices.Choices
        .Select(c => c.Id == ClaupetVoices.Windows && !OperatingSystem.IsWindows() ? (c.Id, L.T("La del sistema")) : c)
        .Concat(_extensions?.PackVoices() ?? []);

    string VoiceLabel(string id) => VoiceChoices().FirstOrDefault(v => v.Id == id).Label ?? ClaupetVoices.Label(id);

    // ── Los modelos ─────────────────────────────────────────────────────────────────────────

    /// <summary>La fila del modelo de voz en Ajustes: qué dice y qué enlace lleva.</summary>
    (string Status, string Action) VoiceStatusText() =>
        _extensions?.ProviderFor(ExtensionSlots.SpeechToText) is { } stt ? (L.T("Voz a texto: {0} (extensión)", stt.Manifest.Name.Text), "")
        : VoiceModel.Ready ? (L.T("Modelo de voz: listo, en este equipo"), "")
        : _modelDownload is not null ? (L.T("Descargando el modelo de voz… {0} %", _modelPercent ?? 0), L.T("Cancelar"))
        : _modelError is not null ? (L.T("No se pudo descargar ({0})", _modelError), L.T("Reintentar"))
        : (L.T("Modelo de voz (488 MB, una sola vez)"), L.T("Descargar"));

    /// <summary>La fila de la voz de Claupet en Ajustes; vacía si no hace falta (ya está, o se eligió la del sistema).</summary>
    (string Status, string Action) SpeechStatusText() =>
        !NeedsSupertonic(_settings.ReadAloudVoice) || ClaupetVoiceModel.Ready || _extensions?.ProviderFor(ExtensionSlots.TextToSpeech) is not null ? ("", "")
        : _speechDownload is not null ? (L.T("Descargando la voz… {0} %", _speechPercent ?? 0), L.T("Cancelar"))
        : _speechError is not null ? (L.T("No se pudo descargar ({0})", _speechError), L.T("Reintentar"))
        : (L.T("Voz de Claupet ({0} MB, una sola vez)", ClaupetVoiceModel.Size / 1_000_000), L.T("Descargar"));

    /// <summary>Baja el modelo de voz (retoma lo que haya quedado de antes) y muestra el avance en Ajustes.</summary>
    async Task DownloadVoiceModelAsync()
    {
        if (_modelDownload is not null || VoiceModel.Ready) return;
        var cancel = _modelDownload = new CancellationTokenSource();
        _modelError = null;
        _modelPercent = 0;
        PushSettings();
        var progress = new Progress<double>(p =>
        {
            var percent = (int)(p * 100);
            if (_modelDownload != cancel || percent == _modelPercent) return;
            _modelPercent = percent;
            PushSettings();
        });
        try { await VoiceModel.DownloadAsync(progress, cancel.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            _modelError = ex is IOException ? ex.Message.TrimEnd('.') : L.T("el servidor no respondió");
        }
        finally
        {
            _modelDownload = null;
            _modelPercent = null;
            cancel.Dispose();
            PushSettings();
        }
    }

    /// <summary>Baja la voz de Claupet (retoma lo que haya quedado de antes) y muestra el avance en Ajustes. Mientras tanto lee la del sistema.</summary>
    async Task DownloadSpeechAsync()
    {
        if (_speechDownload is not null || ClaupetVoiceModel.Ready) return;
        var cancel = _speechDownload = new CancellationTokenSource();
        _speechError = null;
        _speechPercent = 0;
        PushSettings();
        var progress = new Progress<double>(p =>
        {
            var percent = (int)(p * 100);
            if (_speechDownload != cancel || percent == _speechPercent) return;
            _speechPercent = percent;
            PushSettings();
        });
        try { await ClaupetVoiceModel.DownloadAsync(progress, cancel.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            _speechError = ex is IOException ? ex.Message.TrimEnd('.') : L.T("el servidor no respondió");
        }
        finally
        {
            _speechDownload = null;
            _speechPercent = null;
            cancel.Dispose();
            PushSettings();
        }
    }

    // ── Ajustes ─────────────────────────────────────────────────────────────────────────────

    void ToggleMic()
    {
        _settings.Mic = !_settings.Mic;
        SettingsStore.Save(_settings);
        UpdateVoice();
        // Se descarga una vez, al encenderlo; con una extensión de voz a texto no hace falta.
        if (_settings.Mic && _extensions?.ProviderFor(ExtensionSlots.SpeechToText) is null) _ = DownloadVoiceModelAsync();
    }

    void ToggleReadAloud()
    {
        _settings.ReadAloud = !_settings.ReadAloud;
        SettingsStore.Save(_settings);
        if (!_settings.ReadAloud) _readAloud?.Stop();
        else if (NeedsSupertonic(_settings.ReadAloudVoice)) _ = DownloadSpeechAsync(); // se descarga una vez
    }

    void SetReadAloudVoice(string voice)
    {
        _settings.ReadAloudVoice = voice;
        SettingsStore.Save(_settings);
        if (NeedsSupertonic(voice)) _ = DownloadSpeechAsync();
    }

    void SetMicKey(string key)
    {
        _settings.MicKey = key;
        SettingsStore.Save(_settings);
        UpdateVoice();
    }

    /// <summary>Al salir: suelta el micrófono, la voz y PortAudio, y corta las descargas (se retoman la próxima vez).</summary>
    void StopVoice()
    {
        _modelDownload?.Cancel();
        _speechDownload?.Cancel();
        _voice?.Dispose();
        _voice = null;
        _readAloud?.Dispose();
        _readAloud = null;
        AudioHost.Stop();
    }
}
