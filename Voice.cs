using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>
/// El micrófono, por WinMM (waveIn): 16 kHz, 16 bits, mono, en trozos de 100 ms. Un hilo propio espera
/// cada trozo, lo guarda y avisa el nivel (para las barritas). Nada sale del equipo.
/// </summary>
sealed class Microphone : IDisposable
{
    const int Rate = 16000, BufferMs = 100, Buffers = 4;
    const int BufferBytes = Rate * 2 * BufferMs / 1000;

    /// <summary>El nivel de la voz en el último trozo, de 0 a 1 (en el hilo de la interfaz).</summary>
    public event Action<float>? Level;

    /// <summary>Cada trozo de 100 ms, apenas llega (en el hilo del micrófono): para las extensiones que escuchan.</summary>
    public event Action<short[]>? Chunk;

    /// <summary>Guarda lo grabado (para Stop y Snapshot). Sin guardar, solo avisa los trozos: para escuchar sin fin.</summary>
    public bool Keep { get; init; } = true;

    readonly List<short> _samples = new();
    readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    readonly AutoResetEvent _ready = new(false);
    IntPtr _device;
    IntPtr[] _headers = [];
    IntPtr[] _data = [];
    Thread? _thread;
    int _next;
    volatile bool _recording;

    public bool Recording => _recording;

    /// <summary>Para las pruebas (--hablar): en vez del micrófono, este audio, a tiempo real (y después, silencio).</summary>
    public short[]? Fake { get; set; }
    System.Threading.Timer? _fakeTimer;
    int _fakeAt;

    /// <summary>Empieza a grabar. False si no hay micrófono (o Windows no lo deja usar).</summary>
    public bool Start()
    {
        if (_recording) return true;
        _samples.Clear();
        _next = 0;
        if (Fake is { } fake)
        {
            _recording = true;
            _fakeAt = 0;
            _fakeTimer = new System.Threading.Timer(_ =>
            {
                if (!_recording) return;
                var count = Math.Clamp(fake.Length - _fakeAt, 0, BufferBytes / 2);
                Store(count > 0 ? fake.AsSpan(_fakeAt, count).ToArray() : new short[BufferBytes / 2]);
                _fakeAt += count;
            }, null, BufferMs, BufferMs);
            return true;
        }
        var format = new WaveFormat { Tag = 1, Channels = 1, SamplesPerSec = Rate, AvgBytesPerSec = Rate * 2, BlockAlign = 2, BitsPerSample = 16 };
        if (waveInOpen(out _device, -1, ref format, _ready.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, 0x50000) != 0) // CALLBACK_EVENT
            return false;
        _headers = new IntPtr[Buffers];
        _data = new IntPtr[Buffers];
        var size = Marshal.SizeOf<WaveHeader>();
        for (var i = 0; i < Buffers; i++)
        {
            _data[i] = Marshal.AllocHGlobal(BufferBytes);
            _headers[i] = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(new WaveHeader { Data = _data[i], BufferLength = BufferBytes }, _headers[i], false);
            waveInPrepareHeader(_device, _headers[i], size);
            waveInAddBuffer(_device, _headers[i], size);
        }
        _recording = true;
        _thread = new Thread(Pump) { IsBackground = true, Name = "Micrófono" };
        _thread.Start();
        waveInStart(_device);
        return true;
    }

    void Pump()
    {
        while (_recording)
        {
            _ready.WaitOne(200);
            while (_recording && Take(requeue: true)) { }
        }
    }

    /// <summary>
    /// Guarda el próximo trozo si ya está lleno. Se leen en el orden en que se llenan (_next), que es el
    /// de la cola, aunque el hilo se atrase y encuentre varios listos a la vez.
    /// </summary>
    bool Take(bool requeue)
    {
        var header = _headers[_next];
        var h = Marshal.PtrToStructure<WaveHeader>(header);
        if ((h.Flags & 1) == 0) return false; // WHDR_DONE
        var count = (int)h.BytesRecorded / 2;
        if (count > 0)
        {
            var chunk = new short[count];
            Marshal.Copy(h.Data, chunk, 0, count);
            Store(chunk);
        }
        h.BytesRecorded = 0;
        h.Flags &= ~1u;
        Marshal.StructureToPtr(h, header, false);
        if (requeue) waveInAddBuffer(_device, header, Marshal.SizeOf<WaveHeader>());
        _next = (_next + 1) % Buffers;
        return true;
    }

    /// <summary>Guarda un trozo y avisa su nivel.</summary>
    void Store(short[] chunk)
    {
        if (chunk.Length == 0) return;
        double sum = 0;
        foreach (var s in chunk) sum += (double)s * s;
        var rms = (float)Math.Min(1, Math.Sqrt(sum / chunk.Length) / 6000);
        if (Keep) lock (_samples) _samples.AddRange(chunk);
        Chunk?.Invoke(chunk);
        if (_ui is not null) _ui.Post(_ => Level?.Invoke(rms), null);
    }

    /// <summary>Deja de grabar y devuelve lo grabado, con el último trozo aunque esté a medias.</summary>
    public short[] Stop()
    {
        if (!_recording) return [];
        _recording = false;
        if (_fakeTimer is { } timer)
        {
            timer.Dispose();
            _fakeTimer = null;
            lock (_samples) return _samples.ToArray();
        }
        _ready.Set();
        _thread?.Join(500);
        waveInStop(_device);  // el trozo que se estaba llenando queda listo con lo que tenga
        waveInReset(_device); // y los vacíos vuelven también
        for (var i = 0; i < Buffers && Take(requeue: false); i++) { }
        var size = Marshal.SizeOf<WaveHeader>();
        foreach (var header in _headers) waveInUnprepareHeader(_device, header, size);
        waveInClose(_device);
        foreach (var p in _headers.Concat(_data)) Marshal.FreeHGlobal(p);
        _headers = _data = [];
        lock (_samples) return _samples.ToArray();
    }

    /// <summary>Lo grabado hasta ahora, sin dejar de grabar (para ir pasando a texto mientras se habla).</summary>
    public short[] Snapshot()
    {
        lock (_samples) return _samples.ToArray();
    }

    public void Dispose()
    {
        Stop();
        _ready.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WaveFormat
    {
        public ushort Tag, Channels;
        public int SamplesPerSec, AvgBytesPerSec;
        public ushort BlockAlign, BitsPerSample, Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength, BytesRecorded;
        public IntPtr User;
        public uint Flags, Loops;
        public IntPtr Next, Reserved;
    }

    [DllImport("winmm.dll")] static extern int waveInOpen(out IntPtr device, int id, ref WaveFormat format, IntPtr callback, IntPtr instance, int flags);
    [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr device, IntPtr header, int size);
    [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr device, IntPtr header, int size);
    [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr device, IntPtr header, int size);
    [DllImport("winmm.dll")] static extern int waveInStart(IntPtr device);
    [DllImport("winmm.dll")] static extern int waveInStop(IntPtr device);
    [DllImport("winmm.dll")] static extern int waveInReset(IntPtr device);
    [DllImport("winmm.dll")] static extern int waveInClose(IntPtr device);
}

/// <summary>
/// La salida de audio por WinMM (waveOut), aparte de PlaySound (que usan los sonidos de los avisos y
/// se cortarían entre sí): mono, 16 bits. Los trozos se encolan a medida que se generan y suenan seguidos.
/// </summary>
sealed class WaveOut : IDisposable
{
    readonly AutoResetEvent _done = new(false);
    readonly List<(IntPtr Header, IntPtr Data)> _queued = new();
    IntPtr _device;

    public WaveOut(int rate)
    {
        var format = new WaveFormat { Tag = 1, Channels = 1, SamplesPerSec = rate, AvgBytesPerSec = rate * 2, BlockAlign = 2, BitsPerSample = 16 };
        if (waveOutOpen(out _device, -1, ref format, _done.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, 0x50000) != 0) // CALLBACK_EVENT
            throw new IOException(L.T("No hay salida de audio."));
    }

    /// <summary>Encola un trozo (muestras de -1 a 1); suena cuando termine el anterior.</summary>
    public void Play(float[] samples)
    {
        var pcm = new short[samples.Length];
        for (var i = 0; i < samples.Length; i++) pcm[i] = (short)(Math.Clamp(samples[i], -1f, 1f) * 32767);
        var bytes = pcm.Length * 2;
        var data = Marshal.AllocHGlobal(bytes);
        Marshal.Copy(pcm, 0, data, pcm.Length);
        var size = Marshal.SizeOf<WaveHeader>();
        var header = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(new WaveHeader { Data = data, BufferLength = (uint)bytes }, header, false);
        waveOutPrepareHeader(_device, header, size);
        waveOutWrite(_device, header, size);
        lock (_queued) _queued.Add((header, data));
    }

    /// <summary>Espera a que suene todo lo encolado; si se cancela, corta en seco.</summary>
    public void Drain(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            lock (_queued)
                if (_queued.All(q => (Marshal.PtrToStructure<WaveHeader>(q.Header).Flags & 1) != 0)) return; // WHDR_DONE
            WaitHandle.WaitAny([_done, cancel.WaitHandle], 200);
        }
    }

    /// <summary>Corta lo que suena (se puede llamar desde otro hilo).</summary>
    public void Stop()
    {
        lock (_queued)
            if (_device != IntPtr.Zero) waveOutReset(_device);
    }

    public void Dispose()
    {
        var size = Marshal.SizeOf<WaveHeader>();
        lock (_queued)
        {
            if (_device == IntPtr.Zero) return;
            waveOutReset(_device); // lo que quedaba sin sonar vuelve como terminado
            foreach (var (header, data) in _queued)
            {
                waveOutUnprepareHeader(_device, header, size);
                Marshal.FreeHGlobal(header);
                Marshal.FreeHGlobal(data);
            }
            _queued.Clear();
            waveOutClose(_device);
            _device = IntPtr.Zero;
        }
        _done.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WaveFormat
    {
        public ushort Tag, Channels;
        public int SamplesPerSec, AvgBytesPerSec;
        public ushort BlockAlign, BitsPerSample, Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength, BytesRecorded;
        public IntPtr User;
        public uint Flags, Loops;
        public IntPtr Next, Reserved;
    }

    [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr device, int id, ref WaveFormat format, IntPtr callback, IntPtr instance, int flags);
    [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr device, IntPtr header, int size);
    [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr device, IntPtr header, int size);
    [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr device, IntPtr header, int size);
    [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr device);
    [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr device);
}

/// <summary>
/// La tecla para hablar (Ctrl derecho, por defecto), con un gancho de teclado de bajo nivel que no
/// se queda con ninguna tecla. Mantenerla un cuarto de segundo empieza (Began) y soltarla termina
/// (Ended). Si mientras tanto se aprieta otra tecla, era un atajo: se cancela. Dos toques rápidos dejan
/// el micrófono abierto hasta el toque siguiente. Esc cancela. Los avisos salen después, en el hilo de
/// la interfaz: el gancho tiene que volver enseguida o Windows lo quita. Una combinación grabada o la
/// tecla Copilot (las vigila KeyHook, que avisa con ComboDown y ComboUp) no se usa para otra cosa: con
/// un solo toque ya escucha sin mantenerla, y el toque siguiente envía.
/// </summary>
sealed class PushToTalk : IDisposable
{
    public event Action? Began, Ended, Canceled;

    const int HoldMs = 250, DoubleTapMs = 400;
    readonly HookProc _proc;
    readonly System.Windows.Forms.Timer _hold = new() { Interval = HoldMs };
    readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    IntPtr _hook;
    bool _down, _other, _active, _handsFree, _comboDown, _comboSent;
    long _downAt, _lastTap;

    public Keys Key { get; set; } = Keys.RControlKey;

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

    void Raise(Action? handler)
    {
        if (handler is null) return;
        if (_ui is null) handler();
        else _ui.Post(_ => handler(), null);
    }

    public PushToTalk()
    {
        _proc = Callback;
        _hook = SetWindowsHookEx(13, _proc, GetModuleHandle(null), 0); // WH_KEYBOARD_LL
        _hold.Tick += (_, _) =>
        {
            _hold.Stop();
            if ((_down && !_other || _comboDown) && !_active) Start(handsFree: false);
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
        if (_active && !_handsFree) { _active = false; Raise(Ended); }
        else if (!_active) Start(handsFree: true);
    }

    void Start(bool handsFree)
    {
        _active = true;
        _handsFree = handsFree;
        Raise(Began);
    }

    /// <summary>Termina desde fuera (el botón del micrófono, por ejemplo).</summary>
    public void Finish()
    {
        if (!_active) return;
        _active = _handsFree = false;
        Raise(Ended);
    }

    /// <summary>Empieza a escuchar sin tecla (el botón del micrófono): hasta Finish o el próximo toque.</summary>
    public void StartHandsFree()
    {
        if (!_active) Start(handsFree: true);
    }

    IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var vk = (Keys)Marshal.ReadInt32(lParam);
            var message = (int)wParam;
            var down = message is 0x100 or 0x104; // WM_KEYDOWN, WM_SYSKEYDOWN
            var up = message is 0x101 or 0x105;
            var now = Environment.TickCount64;
            if (vk == Key)
            {
                if (down && !_down)
                {
                    _down = true;
                    _other = false;
                    _downAt = now;
                    if (!_active) _hold.Start();
                }
                else if (up && _down)
                {
                    _down = false;
                    _hold.Stop();
                    var tap = now - _downAt < HoldMs && !_other;
                    if (_active && !_handsFree) { _active = false; Raise(Ended); }
                    else if (_active && _handsFree && tap) Finish();
                    else if (!_active && tap)
                    {
                        if (now - _lastTap < DoubleTapMs) { _lastTap = 0; Start(handsFree: true); }
                        else _lastTap = now;
                    }
                }
            }
            else if (down)
            {
                if (vk == Keys.Escape && _active)
                {
                    _active = _handsFree = false;
                    Raise(Canceled);
                }
                else if (_down)
                {
                    _other = true; // era un atajo (Ctrl derecho + otra tecla)
                    _hold.Stop();
                    if (_active && !_handsFree) { _active = false; Raise(Canceled); }
                }
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        _hold.Dispose();
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
}

/// <summary>
/// Hablarle a la mascota o a la isla: la tecla (o el botón del micrófono) abre el micrófono; mientras
/// se habla, lo dicho se va pasando a texto (de a un trozo, si el equipo da abasto) y al terminar se
/// pasa todo. Todo avisa en el hilo de la interfaz. El modelo se carga al empezar a escuchar y se suelta
/// a los 3 minutos sin uso.
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
    readonly System.Windows.Forms.Timer _partialTimer = new() { Interval = 400 };
    readonly System.Windows.Forms.Timer _idle = new() { Interval = 3 * 60_000 };
    Transcriber? _transcriber;
    // La extensión que pasa a texto esta vez (si ocupa la ranura): sin trozos mientras se habla, solo al final.
    ISpeechToText? _external;
    CancellationTokenSource? _externalCancel;
    CancellationTokenSource? _partialCancel;
    Task? _partial;
    int _partialSamples, _take;
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
            if (!_key.HandsFree || !_mic.Recording) return;
            var now = Environment.TickCount64;
            if (level > SpeechLevel) { _spoke = true; _voiceAt = now; }
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

    public Keys Key { get => _key.Key; set => _key.Key = value; }

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
            Failed?.Invoke(L.T("No encuentro el micrófono, o Windows no deja usarlo (Configuración › Privacidad › Micrófono)."));
            return;
        }
        _take++;
        _partialSamples = 0;
        _spoke = false;
        _listenAt = Environment.TickCount64;
        _idle.Stop();
        if (_external is null)
        {
            _partialTimer.Start();
            _ = Transcriber.WarmAsync().ContinueWith(t => ErrorLog.Write(t.Exception!), TaskContinuationOptions.OnlyOnFaulted);
        }
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
    /// Para las pruebas (--hablar archivo.wav, de 16 kHz, mono y 16 bits): como si se mantuviera la tecla
    /// mientras suena ese audio, que reemplaza al micrófono esa vez.
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
        _partialTimer.Dispose();
        _idle.Dispose();
        _partialCancel?.Cancel();
        _key.Dispose();
        _mic.Dispose();
        _transcriber?.Dispose();
    }
}

/// <summary>
/// Lee las respuestas en voz alta (opcional). Con la voz de Claupet (Supertonic, femenina o masculina)
/// si su modelo ya está en el equipo: frase por frase, sonando la primera mientras se arma la
/// siguiente, y con las risas o suspiros que haya puesto Claude. Si no, o si se eligió, con una voz de
/// Windows en el idioma de la app. El modelo cargado ocupa unos 550 MB de memoria: se suelta a los 3 minutos sin hablar.
/// Con una extensión en la ranura de texto a voz, habla ella (y si falla antes de sonar, la voz de Windows);
/// las voces de los paquetes («ext:paquete/voz») son estilos para la voz de Claupet.
/// </summary>
sealed class ReadAloud : IDisposable
{
    /// <summary>La extensión que ocupa la ranura de texto a voz; null: la integrada. Lo pone la app.</summary>
    public static Func<ITextToSpeech?>? External { get; set; }

    /// <summary>El archivo de estilo de una voz de un paquete (ExtensionManager.VoiceFileAsync). Lo pone la app.</summary>
    public static Func<string, CancellationToken, Task<string?>>? PackVoice { get; set; }

    static readonly TimeSpan KeepLoaded = TimeSpan.FromMinutes(3);
    const float Pause = 0.15f; // segundos entre frases

    readonly object _engineLock = new();
    readonly Dictionary<string, Supertonic.Voice> _voices = new();
    readonly System.Threading.Timer _idle;
    System.Speech.Synthesis.SpeechSynthesizer? _windows;
    Supertonic? _engine;
    CancellationTokenSource? _speaking;
    WaveOut? _output;

    public ReadAloud() => _idle = new System.Threading.Timer(_ => Unload());

    /// <summary>
    /// Lo dice. started corre una sola vez, justo antes de que empiece a sonar (para mostrar el texto a la
    /// vez que la voz); si no hay nada que decir o la voz falla, corre igual. Puede correr en otro hilo.
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
            SayWithWindows(Expressions.Strip(clean));
        }
    }

    /// <summary>styleFile: el estilo de una voz de un paquete; sin él, el de la voz de Claupet (voice).</summary>
    void SpeakClaupet(string text, string voice, Action started, CancellationToken cancel, string? styleFile = null)
    {
        try
        {
            _idle.Change(Timeout.Infinite, Timeout.Infinite);
            WaveOut? output = null;
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
                        _output = output = new WaveOut(rate);
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
    /// Con la extensión: el audio llega por partes y suena a medida que llega. Sin expresiones si no las
    /// entiende. Si falla antes de sonar nada, lo dice la voz de Windows (la extensión se apaga sola si
    /// sigue fallando, y vuelve la integrada).
    /// </summary>
    async Task SpeakExternalAsync(ITextToSpeech tts, string text, Action started, CancellationToken cancel)
    {
        WaveOut? output = null;
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
                    _output = output = new WaveOut(rate = r);
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
                SayWithWindows(Expressions.Strip(text));
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

    void SayWithWindows(string text)
    {
        try
        {
            if (_windows is null)
            {
                _windows = new System.Speech.Synthesis.SpeechSynthesizer();
                _windows.SetOutputToDefaultAudioDevice();
                var same = _windows.GetInstalledVoices().FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == L.Language);
                if (same is not null) _windows.SelectVoice(same.VoiceInfo.Name);
            }
            _windows.SpeakAsync(text);
        }
        catch (Exception) { }
    }

    public void Stop()
    {
        _speaking?.Cancel();
        _speaking = null;
        _output?.Stop();
        try { _windows?.SpeakAsyncCancelAll(); } catch (Exception) { }
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
        _windows?.Dispose();
    }
}
