using System.Text.RegularExpressions;
using Avalonia.Threading;

namespace Claupet;

/// <summary>
/// La tecla para hablar (Ctrl derecho, por defecto), como en Windows: mantenerla un cuarto de segundo empieza (Began)
/// y soltarla termina (Ended); si mientras tanto se aprieta otra tecla, era un atajo y se cancela. Dos toques rápidos
/// dejan el micrófono abierto hasta el toque siguiente, y Esc cancela. Aquí no hay gancho de teclado: se mira el teclado
/// cada 30 ms (GlobalKeys). Si el sistema no deja mirarlo, solo queda el botón del micrófono. Una combinación grabada o
/// la tecla Copilot (en Windows las vigila KeyHook, que avisa con ComboDown y ComboUp) no se usa para otra cosa: con un
/// solo toque ya escucha sin mantenerla, y el toque siguiente envía.
/// </summary>
sealed class PushToTalk : IDisposable
{
    public event Action? Began, Ended, Canceled;

    const int HoldMs = 250, DoubleTapMs = 400;
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(30) };
    readonly DispatcherTimer _hold = new() { Interval = TimeSpan.FromMilliseconds(HoldMs) };
    bool _down, _other, _escape, _active, _handsFree, _comboDown, _comboSent;
    long _downAt, _lastTap;

    public PushToTalk()
    {
        _poll.Tick += (_, _) => Poll();
        _poll.Start();
        _hold.Tick += (_, _) =>
        {
            _hold.Stop();
            if (_comboDown && !_active) Start(handsFree: false);
        };
    }

    /// <summary>Bajó la combinación: si ya escuchaba sin mantener, este toque envía; si no, se ve si la mantiene.</summary>
    public void ComboDown()
    {
        if (_comboDown) return;
        _comboDown = true;
        _comboSent = _active && _handsFree;
        if (_comboSent) Finish();
        else if (!_active) _hold.Start();
    }

    /// <summary>Subió: si la mantuvo, envía; si fue un toque, escucha hasta el toque siguiente (o una pausa).</summary>
    public void ComboUp()
    {
        if (!_comboDown) return;
        _comboDown = false;
        _hold.Stop();
        if (_comboSent) return;
        if (_active && !_handsFree) Finish();
        else if (!_active) Start(handsFree: true);
    }

    /// <summary>Una de MicKeys.Choices.</summary>
    public string Key { get; set; } = MicKeys.Choices[0].Id;

    /// <summary>Mira la tecla (si no, solo se habla con el botón del micrófono).</summary>
    public bool Watch
    {
        get => _poll.IsEnabled;
        set => _poll.IsEnabled = value;
    }

    public bool Active => _active;

    /// <summary>Escucha sin mantener la tecla (dos toques o el botón): termina con el toque siguiente.</summary>
    public bool HandsFree => _active && _handsFree;

    /// <summary>Vuelve a esperar la tecla, sin avisar (no se pudo abrir el micrófono, por ejemplo).</summary>
    public void Reset() => _active = _handsFree = false;

    /// <summary>Empieza como si se mantuviera la tecla (para las pruebas; termina con Finish).</summary>
    public void Hold()
    {
        if (!_active) Start(handsFree: false);
    }

    void Start(bool handsFree)
    {
        _active = true;
        _handsFree = handsFree;
        Began?.Invoke();
    }

    /// <summary>Termina desde fuera (el botón del micrófono, por ejemplo).</summary>
    public void Finish()
    {
        if (!_active) return;
        _active = _handsFree = false;
        Ended?.Invoke();
    }

    /// <summary>Empieza a escuchar sin tecla (el botón del micrófono): hasta Finish o el próximo toque.</summary>
    public void StartHandsFree()
    {
        if (!_active) Start(handsFree: true);
    }

    void Poll()
    {
        if (GlobalKeys.Read(Key, all: _down || _active) is not { } keys)
        {
            _poll.Stop(); // no hay forma de mirar el teclado en este sistema
            return;
        }
        var now = Environment.TickCount64;
        if (keys.Talk && !_down)
        {
            _down = true;
            _other = keys.Other;
            _downAt = now;
        }
        else if (keys.Talk && keys.Other && !_other)
        {
            _other = true; // era un atajo (Ctrl derecho + otra tecla)
            if (_active && !_handsFree)
            {
                _active = false;
                Canceled?.Invoke();
            }
        }
        else if (!keys.Talk && _down)
        {
            _down = false;
            var tap = now - _downAt < HoldMs && !_other;
            if (_active && !_handsFree) Finish();
            else if (_active && _handsFree && tap) Finish();
            else if (!_active && tap)
            {
                if (now - _lastTap < DoubleTapMs)
                {
                    _lastTap = 0;
                    Start(handsFree: true);
                }
                else _lastTap = now;
            }
        }
        if (_down && !_other && !_active && now - _downAt >= HoldMs) Start(handsFree: false);
        if (keys.Escape && !_escape && _active)
        {
            _active = _handsFree = false;
            Canceled?.Invoke();
        }
        _escape = keys.Escape;
    }

    public void Dispose()
    {
        _poll.Stop();
        _hold.Stop();
    }
}

/// <summary>
/// Hablarle a la mascota o a la isla, como en Windows: la tecla (o el botón del micrófono) abre el micrófono; mientras
/// se habla, lo dicho se va pasando a texto (de a un trozo, si el equipo da abasto) y al terminar se pasa todo. Todo
/// avisa en el hilo de la interfaz. El modelo se carga al empezar a escuchar y se suelta a los 3 minutos sin uso.
/// </summary>
sealed class VoiceInput : IDisposable
{
    /// <summary>Empezó a escuchar.</summary>
    public event Action? Started;
    /// <summary>El nivel de la voz, de 0 a 1, cada 100 ms.</summary>
    public event Action<float>? Level;
    /// <summary>Lo entendido hasta ahora, mientras sigue escuchando.</summary>
    public event Action<string>? Partial;
    /// <summary>Terminó de escuchar: está pasando todo a texto.</summary>
    public event Action? Converting;
    /// <summary>El texto final; vacío si no se entendió nada.</summary>
    public event Action<string>? Heard;
    /// <summary>Se canceló (Esc, otra tecla con la de hablar, o un toque sin hablar).</summary>
    public event Action? Canceled;
    /// <summary>Se pidió escuchar pero falta el modelo de voz.</summary>
    public event Action? NeedsModel;
    /// <summary>Algo falló (el micrófono, Whisper): el mensaje para la persona.</summary>
    public event Action<string>? Failed;

    const int MinSamples = 16000 * 4 / 10;  // menos de 0,4 s: fue un toque, no se habló
    const int PartialStep = 16000 * 7 / 10; // otro trozo cuando hay 0,7 s nuevos
    // Sin mantener la tecla, también se envía al callar (PauseMs después de hablar) o si no se dice nada en WaitMs.
    const float SpeechLevel = 350f / 6000;  // el umbral de Transcriber, en la escala de Microphone.Level
    const int PauseMs = 1800, WaitMs = 8000;
    readonly PushToTalk _key = new();
    readonly Microphone _mic = new();
    readonly DispatcherTimer _partialTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromMinutes(3) };
    Transcriber? _transcriber;
    // La extensión que pasa a texto esta vez (si ocupa la ranura): sin trozos mientras se habla, solo al final.
    ISpeechToText? _external;
    CancellationTokenSource? _externalCancel;
    CancellationTokenSource? _partialCancel;
    Task? _partial;
    int _partialSamples, _take, _loud, _warmLoud;
    bool _buttonPending, _byButton, _spoke;
    long _listenAt, _voiceAt;

    public VoiceInput()
    {
        _key.Began += Begin;
        _key.Ended += () => _ = EndAsync();
        _key.Canceled += Cancel;
        _mic.Level += level =>
        {
            Level?.Invoke(level);
            // Whisper (unos 850 MB) se carga al oír voz, no al apretar la tecla: mantener Ctrl derecho para un atajo, sin
            // hablar, ya no lo carga. Dos trozos con voz: un pico suelto (el micrófono al abrirse) no cuenta.
            if (_mic.Recording && _external is null && level > SpeechLevel && ++_warmLoud == 2)
                _ = Transcriber.WarmAsync().ContinueWith(t => ErrorLog.Write(t.Exception!), TaskContinuationOptions.OnlyOnFaulted);
            if (!_key.HandsFree || !_mic.Recording) return;
            var now = Environment.TickCount64;
            if (level > SpeechLevel)
            {
                _voiceAt = now;
                _spoke |= ++_loud >= Transcriber.VoiceChunks; // un pico suelto (el micrófono al abrirse) no es que habló
            }
            else if (_spoke ? now - _voiceAt > PauseMs : now - _listenAt > WaitMs) _key.Finish();
        };
        _partialTimer.Tick += (_, _) => MaybePartial();
        _idle.Tick += (_, _) =>
        {
            _idle.Stop();
            if (_mic.Recording) return;
            _transcriber?.Dispose();
            _transcriber = null;
        };
    }

    /// <summary>La tecla que se mantiene para hablar: una de MicKeys.Choices.</summary>
    public string Key { get => _key.Key; set => _key.Key = value; }

    /// <summary>Escucha con la tecla; si no, solo con el botón del micrófono (o la combinación grabada).</summary>
    public bool WatchKey { get => _key.Watch; set => _key.Watch = value; }

    /// <summary>Una combinación grabada o la tecla Copilot (KeyHook): un toque escucha sin mantenerla; mantenerla, hasta soltarla.</summary>
    public void ComboDown() => _key.ComboDown();

    public void ComboUp() => _key.ComboUp();

    public bool Listening => _mic.Recording;

    /// <summary>La extensión que ocupa la ranura de voz a texto; null: Whisper. Lo pone la app.</summary>
    public static Func<ISpeechToText?>? External { get; set; }

    Transcriber Transcriber => _transcriber ??= new Transcriber();

    /// <summary>Cómo se termina, para mostrarlo mientras escucha.</summary>
    public string Hint(string keyLabel) =>
        _byButton ? L.T("Envía al callar o con el micrófono") : _key.HandsFree ? L.T("Envía al callar o con {0}", keyLabel) : L.T("Suelte {0}", keyLabel);

    /// <summary>El botón del micrófono: empieza a escuchar (sin mantener la tecla) o termina y envía.</summary>
    public void Toggle()
    {
        if (_key.Active) { _key.Finish(); return; }
        _buttonPending = true;
        _key.StartHandsFree();
    }

    /// <summary>Lo que se dice si no se puede abrir el micrófono, con dónde se da el permiso en cada sistema.</summary>
    static string NoMicrophone() =>
        OperatingSystem.IsWindows() ? L.T("No encuentro el micrófono, o Windows no deja usarlo (Configuración › Privacidad › Micrófono).")
        : OperatingSystem.IsMacOS() ? L.T("No encuentro el micrófono, o macOS no deja usarlo (Ajustes del Sistema › Privacidad y seguridad › Micrófono).")
        : L.T("No encuentro el micrófono, o el sistema no deja usarlo.");

    void Begin()
    {
        _byButton = _buttonPending;
        _buttonPending = false;
        _external = External?.Invoke();
        if (_external is null && !VoiceModel.Ready)
        {
            _key.Reset();
            NeedsModel?.Invoke();
            return;
        }
        if (!_mic.Start())
        {
            _key.Reset();
            Failed?.Invoke(NoMicrophone());
            return;
        }
        _take++;
        _partialSamples = 0;
        _spoke = false;
        _loud = _warmLoud = 0;
        _listenAt = Environment.TickCount64;
        _idle.Stop();
        if (_external is null) _partialTimer.Start(); // Whisper se carga al oír voz (ver Level)
        Started?.Invoke();
    }

    /// <summary>Si hay voz nueva y nada pasándose a texto, pasa lo grabado hasta ahora (para verlo mientras se habla).</summary>
    void MaybePartial()
    {
        if (!_mic.Recording || _partial is { IsCompleted: false }) return;
        var pcm = _mic.Snapshot();
        if (pcm.Length - _partialSamples < PartialStep) return;
        _partialSamples = pcm.Length;
        var take = _take;
        var cancel = _partialCancel = new CancellationTokenSource();
        _partial = PartialAsync(pcm, take, cancel.Token);
    }

    async Task PartialAsync(short[] pcm, int take, CancellationToken cancel)
    {
        try
        {
            var text = await Transcriber.TranscribeAsync(pcm, cancel);
            if (take == _take && _mic.Recording && text.Length > 0) Partial?.Invoke(text);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }

    async Task EndAsync()
    {
        if (!_mic.Recording) return;
        _partialTimer.Stop();
        var take = _take;
        var pcm = _mic.Stop();
        _partialCancel?.Cancel(); // lo de a trozos ya no hace falta: se pasa todo
        if (pcm.Length < MinSamples)
        {
            Canceled?.Invoke();
            _idle.Start();
            return;
        }
        Converting?.Invoke();
        try
        {
            string text;
            if (_external is { } external)
            {
                using var cancel = _externalCancel = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                text = (await external.TranscribeAsync(pcm, L.Language, cancel.Token)).Trim();
            }
            else
            {
                if (_partial is { } partial) await partial;
                text = await Transcriber.TranscribeAsync(pcm);
            }
            if (take == _take) Heard?.Invoke(text);
        }
        catch (OperationCanceledException) when (take != _take) { }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            if (take == _take)
                Failed?.Invoke(ex is ExtensionUnavailableException
                    ? ex.Message
                    : _external is { } e ? L.T("{0} no pudo pasar la voz a texto.", e.Name) : L.T("No pude pasar la voz a texto."));
        }
        finally { _externalCancel = null; }
        _idle.Start();
    }

    /// <summary>
    /// Para las pruebas (--hablar archivo.wav, de 16 kHz, mono y 16 bits): como si se mantuviera la tecla mientras suena
    /// ese audio, que reemplaza al micrófono esa vez.
    /// </summary>
    public async Task DemoAsync(string wav)
    {
        if (Transcriber.ReadWav(await File.ReadAllBytesAsync(wav)) is not { } pcm) return;
        _mic.Fake = pcm;
        _key.Hold();
        await Task.Delay(pcm.Length / 16 + 400);
        _key.Finish();
        _mic.Fake = null;
    }

    /// <summary>Deja de escuchar sin enviar nada.</summary>
    public void Cancel()
    {
        _key.Reset();
        _partialTimer.Stop();
        _partialCancel?.Cancel();
        _take++;
        try { _externalCancel?.Cancel(); } catch (ObjectDisposedException) { }
        if (!_mic.Recording) return;
        _mic.Stop();
        _idle.Start();
        Canceled?.Invoke();
    }

    public void Dispose()
    {
        _partialTimer.Stop();
        _idle.Stop();
        _partialCancel?.Cancel();
        _key.Dispose();
        _mic.Dispose();
        _transcriber?.Dispose();
    }
}

/// <summary>
/// Lee las respuestas en voz alta (opcional), como en Windows. Con la voz de Claupet (Supertonic, femenina o masculina)
/// si su modelo ya está en el equipo: frase por frase, sonando la primera mientras se arma la siguiente, y con las
/// risas o suspiros que haya puesto Claude. Si no, o si se eligió, con la voz del sistema (SystemVoice). El modelo
/// cargado ocupa unos 550 MB de memoria: se suelta a los 3 minutos sin hablar. Con una extensión en la ranura de texto
/// a voz, habla ella (y si falla antes de sonar, la del sistema); las voces de los paquetes («ext:paquete/voz») son
/// estilos para la voz de Claupet.
/// </summary>
sealed class ReadAloud : IDisposable
{
    /// <summary>La extensión que ocupa la ranura de texto a voz; null: la integrada. Lo pone la app.</summary>
    public static Func<ITextToSpeech?>? External { get; set; }

    /// <summary>El archivo de estilo de una voz de un paquete. Lo pone la app.</summary>
    public static Func<string, CancellationToken, Task<string?>>? PackVoice { get; set; }

    static readonly TimeSpan KeepLoaded = TimeSpan.FromMinutes(3);
    const float Pause = 0.15f; // segundos entre frases

    readonly object _engineLock = new();
    readonly Dictionary<string, Supertonic.Voice> _voices = new();
    readonly System.Threading.Timer _idle;
    readonly SystemVoice _system = new();
    Supertonic? _engine;
    CancellationTokenSource? _speaking;
    AudioOut? _output;

    public ReadAloud() => _idle = new System.Threading.Timer(_ => Unload());

    /// <summary>
    /// Lo dice. started corre una sola vez, justo antes de que empiece a sonar (para mostrar el texto a la vez que la
    /// voz); si no hay nada que decir o la voz falla, corre igual. Puede correr en otro hilo.
    /// </summary>
    public void Say(string text, string voice, Action? started = null)
    {
        Stop();
        var once = 0;
        void Started()
        {
            if (Interlocked.Exchange(ref once, 1) == 0) started?.Invoke();
        }
        // Sin las caritas de texto, que se leerían letra por letra.
        var clean = Regex.Replace(text, @"\([^\p{L}\d\s]{1,8}\)|[\\/]\(.{1,6}\)[\\/]|[♪♫]", " ").Trim();
        if (clean.Length == 0)
        {
            Started();
            return;
        }
        if (External?.Invoke() is { } tts)
        {
            var cancel = _speaking = new CancellationTokenSource();
            _ = Task.Run(() => SpeakExternalAsync(tts, clean, Started, cancel.Token));
        }
        else if (voice.StartsWith(ExtensionManager.PackPrefix, StringComparison.Ordinal) && ClaupetVoiceModel.Ready && PackVoice is { } find)
        {
            var cancel = _speaking = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                string? file = null;
                try { file = await find(voice, cancel.Token); }
                catch (OperationCanceledException) { Started(); return; }
                catch (Exception ex) { ErrorLog.Write(ex); }
                // El paquete ya no está (o no pasó la verificación): la voz femenina de Claupet.
                SpeakClaupet(clean, file is null ? ClaupetVoices.Female : voice, Started, cancel.Token, file);
            });
        }
        else if (ClaupetVoices.IsClaupet(voice) && ClaupetVoiceModel.Ready)
        {
            var cancel = _speaking = new CancellationTokenSource();
            _ = Task.Run(() => SpeakClaupet(clean, voice, Started, cancel.Token));
        }
        else
        {
            Started();
            _system.Say(Expressions.Strip(clean));
        }
    }

    /// <summary>styleFile: el estilo de una voz de un paquete; sin él, el de la voz de Claupet (voice).</summary>
    void SpeakClaupet(string text, string voice, Action started, CancellationToken cancel, string? styleFile = null)
    {
        try
        {
            _idle.Change(Timeout.Infinite, Timeout.Infinite);
            AudioOut? output = null;
            try
            {
                foreach (var chunk in Supertonic.Chunks(text))
                {
                    float[] samples;
                    int rate;
                    lock (_engineLock)
                    {
                        if (cancel.IsCancellationRequested) return;
                        var engine = _engine ??= new Supertonic(ClaupetVoiceModel.Folder);
                        if (!_voices.TryGetValue(voice, out var style))
                            _voices[voice] = style = Supertonic.LoadVoice(styleFile ?? ClaupetVoiceModel.VoiceFile(voice));
                        samples = engine.Speak(chunk, style);
                        rate = engine.SampleRate;
                    }
                    if (cancel.IsCancellationRequested) return;
                    if (output is null)
                    {
                        _output = output = new AudioOut(rate);
                        started(); // la primera frase ya está lista: el texto sale con ella
                    }
                    else output.Play(new float[(int)(Pause * rate)]);
                    output.Play(samples);
                }
                output?.Drain(cancel);
            }
            finally
            {
                if (output is not null)
                {
                    Interlocked.CompareExchange(ref _output, null, output);
                    output.Dispose();
                }
                _idle.Change(KeepLoaded, Timeout.InfiniteTimeSpan);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { ErrorLog.Write(ex); }
        finally { started(); } // si se canceló o falló antes de sonar, el texto igual se muestra
    }

    /// <summary>
    /// Con la extensión: el audio llega por partes y suena a medida que llega. Sin expresiones si no las entiende. Si
    /// falla antes de sonar nada, lo dice la voz del sistema.
    /// </summary>
    async Task SpeakExternalAsync(ITextToSpeech tts, string text, Action started, CancellationToken cancel)
    {
        AudioOut? output = null;
        var rate = 0;
        try
        {
            await tts.SpeakAsync(tts.Expressive ? text : Expressions.Strip(text), null, L.Language, (samples, r) =>
            {
                if (cancel.IsCancellationRequested || samples.Length == 0) return;
                if (output is not null && r != rate)
                {
                    // Otra frecuencia a mitad de camino: lo de antes termina de sonar y se abre otra salida.
                    output.Drain(cancel);
                    Interlocked.CompareExchange(ref _output, null, output);
                    output.Dispose();
                    output = null;
                }
                if (output is null)
                {
                    _output = output = new AudioOut(rate = r);
                    started();
                }
                output.Play(samples);
            }, cancel);
            output?.Drain(cancel);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorLog.Write(ex);
            if (output is null && !cancel.IsCancellationRequested)
            {
                started();
                _system.Say(Expressions.Strip(text));
            }
        }
        finally
        {
            if (output is not null)
            {
                Interlocked.CompareExchange(ref _output, null, output);
                output.Dispose();
            }
            started();
        }
    }

    public void Stop()
    {
        _speaking?.Cancel();
        _speaking = null;
        _output?.Stop();
        _system.Stop();
    }

    /// <summary>Suelta el modelo (y su memoria) si no está hablando.</summary>
    void Unload()
    {
        lock (_engineLock)
        {
            _engine?.Dispose();
            _engine = null;
            _voices.Clear();
        }
    }

    public void Dispose()
    {
        Stop();
        _idle.Dispose();
        Unload();
        _system.Dispose();
    }
}
