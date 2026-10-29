using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using PortAudioSharp;
using PaStream = PortAudioSharp.Stream;

namespace Claupet;

/// <summary>
/// PortAudio, para el micrófono y los parlantes en los tres sistemas (sus bibliotecas vienen en el paquete). Se inicia
/// la primera vez que hace falta y se cierra al salir de la app.
/// </summary>
static class AudioHost
{
    static readonly object Gate = new();
    static bool _ready, _failed;

    static bool Start()
    {
        lock (Gate)
        {
            if (_ready || _failed) return _ready;
            try
            {
                PortAudio.Initialize();
                _ready = true;
            }
            catch (Exception ex) when (ex is PortAudioException or DllNotFoundException or EntryPointNotFoundException)
            {
                ErrorLog.Write(ex);
                _failed = true;
            }
            return _ready;
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            if (!_ready) return;
            _ready = false;
            try { PortAudio.Terminate(); }
            catch (PortAudioException) { }
        }
    }

    /// <summary>
    /// Abre el dispositivo de siempre (mono, en float) a la frecuencia pedida o, si no la acepta, a la suya: devuelve
    /// cuál quedó y su latencia en segundos. IOException si no hay dispositivo o PortAudio no está.
    /// </summary>
    public static (PaStream Stream, int Rate, double Latency) Open(bool input, int rate, PaStream.Callback callback)
    {
        if (!Start()) throw new IOException(input ? L.T("No encuentro el micrófono.") : L.T("No hay salida de audio."));
        var device = input ? PortAudio.DefaultInputDevice : PortAudio.DefaultOutputDevice;
        if (device == PortAudio.NoDevice) throw new IOException(input ? L.T("No encuentro el micrófono.") : L.T("No hay salida de audio."));
        var info = PortAudio.GetDeviceInfo(device);
        var latency = input ? info.defaultLowInputLatency : info.defaultHighOutputLatency;
        var parameters = new StreamParameters
        {
            device = device,
            channelCount = 1,
            sampleFormat = SampleFormat.Float32,
            suggestedLatency = latency,
            hostApiSpecificStreamInfo = IntPtr.Zero,
        };
        var own = (int)info.defaultSampleRate;
        try { return (Open(input, parameters, rate, callback), rate, latency); }
        catch (PortAudioException) when (own != rate && own > 0) { }
        try { return (Open(input, parameters, own, callback), own, latency); }
        catch (PortAudioException ex) { throw new IOException(ex.Message, ex); }
    }

    static PaStream Open(bool input, StreamParameters parameters, int rate, PaStream.Callback callback) =>
        new(input ? parameters : null, input ? null : parameters, rate, PortAudio.FramesPerBufferUnspecified, StreamFlags.ClipOff, callback, null!);
}

/// <summary>
/// El micrófono, por PortAudio: 16 kHz, 16 bits, mono, en trozos de 100 ms (si el dispositivo no graba a 16 kHz, se
/// convierte). Lo graba el hilo de audio, que avisa el nivel (para las barritas) en el de la interfaz. Nada sale del equipo.
/// </summary>
sealed class Microphone : IDisposable
{
    const int Rate = 16000, BufferMs = 100, ChunkSamples = Rate * BufferMs / 1000;

    /// <summary>El nivel de la voz en el último trozo, de 0 a 1 (en el hilo de la interfaz).</summary>
    public event Action<float>? Level;

    /// <summary>Cada trozo de 100 ms, apenas llega (en el hilo de audio).</summary>
    public event Action<short[]>? Chunk;

    /// <summary>Guarda lo grabado (para Stop y Snapshot). Sin guardar, solo avisa los trozos.</summary>
    public bool Keep { get; init; } = true;

    /// <summary>Para las pruebas: en vez del micrófono, este audio, a tiempo real (y después, silencio).</summary>
    public short[]? Fake { get; set; }

    readonly List<short> _samples = new();
    readonly List<short> _pending = new(ChunkSamples);
    readonly PaStream.Callback _callback;
    PaStream? _stream;
    PcmResampler? _resampler;
    System.Threading.Timer? _fakeTimer;
    int _fakeAt;
    volatile bool _recording;

    public Microphone() => _callback = OnAudio;

    public bool Recording => _recording;

    /// <summary>Empieza a grabar. False si no hay micrófono (o el sistema no lo deja usar).</summary>
    public bool Start()
    {
        if (_recording) return true;
        lock (_samples) _samples.Clear();
        lock (_pending) _pending.Clear();
        if (Fake is { } fake)
        {
            _recording = true;
            _fakeAt = 0;
            _fakeTimer = new System.Threading.Timer(_ =>
            {
                if (!_recording) return;
                var count = Math.Clamp(fake.Length - _fakeAt, 0, ChunkSamples);
                Store(count > 0 ? fake.AsSpan(_fakeAt, count).ToArray() : new short[ChunkSamples]);
                _fakeAt += count;
            }, null, BufferMs, BufferMs);
            return true;
        }
        try
        {
            var (stream, rate, _) = AudioHost.Open(input: true, Rate, _callback);
            _resampler = new PcmResampler(rate, Rate); // a 16 kHz, solo pasa a 16 bits
            _stream = stream;
            _recording = true;
            stream.Start();
            return true;
        }
        catch (Exception ex) when (ex is IOException or PortAudioException)
        {
            ErrorLog.Write(ex);
            _recording = false;
            _stream?.Dispose();
            _stream = null;
            return false;
        }
    }

    StreamCallbackResult OnAudio(IntPtr input, IntPtr output, uint frames, ref StreamCallbackTimeInfo time, StreamCallbackFlags flags, IntPtr user)
    {
        if (!_recording || input == IntPtr.Zero || frames == 0) return StreamCallbackResult.Continue;
        var raw = new float[frames];
        Marshal.Copy(input, raw, 0, raw.Length);
        lock (_pending)
            foreach (var s in _resampler!.Push(raw))
            {
                _pending.Add(s);
                if (_pending.Count < ChunkSamples) continue;
                Store(_pending.ToArray());
                _pending.Clear();
            }
        return StreamCallbackResult.Continue;
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
        Dispatcher.UIThread.Post(() => Level?.Invoke(rms));
    }

    /// <summary>Deja de grabar y devuelve lo grabado, con el último trozo aunque esté a medias.</summary>
    public short[] Stop()
    {
        if (!_recording) return [];
        if (_fakeTimer is { } timer)
        {
            _recording = false;
            timer.Dispose();
            _fakeTimer = null;
            lock (_samples) return _samples.ToArray();
        }
        if (_stream is { } stream)
        {
            try { stream.Stop(); } // espera a que termine lo que estaba grabando
            catch (PortAudioException ex) { ErrorLog.Write(ex); }
            _recording = false;
            stream.Dispose();
            _stream = null;
        }
        _recording = false;
        lock (_pending)
        {
            Store(_pending.ToArray());
            _pending.Clear();
        }
        lock (_samples) return _samples.ToArray();
    }

    /// <summary>Lo grabado hasta ahora, sin dejar de grabar (para ir pasando a texto mientras se habla).</summary>
    public short[] Snapshot()
    {
        lock (_samples) return _samples.ToArray();
    }

    public void Dispose() => Stop();
}

/// <summary>
/// La salida de audio, por PortAudio: mono. Los trozos se encolan a medida que se generan y suenan seguidos; si el
/// dispositivo no acepta la frecuencia, se convierte.
/// </summary>
sealed class AudioOut : IDisposable
{
    readonly Queue<float[]> _queue = new();
    readonly ManualResetEventSlim _empty = new(true);
    readonly PaStream.Callback _callback;
    readonly PaStream _stream;
    readonly PcmResampler? _resampler;
    readonly double _latency;
    int _at; // lo que ya sonó del primero de la cola

    public AudioOut(int rate)
    {
        _callback = OnAudio;
        (_stream, var actual, _latency) = AudioHost.Open(input: false, rate, _callback);
        _resampler = actual == rate ? null : new PcmResampler(rate, actual);
        try { _stream.Start(); }
        catch (PortAudioException ex)
        {
            _stream.Dispose();
            _empty.Dispose();
            throw new IOException(ex.Message, ex);
        }
    }

    /// <summary>Encola un trozo (muestras de -1 a 1); suena cuando termine el anterior.</summary>
    public void Play(float[] samples)
    {
        var converted = _resampler?.PushFloat(samples) ?? samples;
        lock (_queue)
        {
            _queue.Enqueue(converted);
            _empty.Reset();
        }
    }

    StreamCallbackResult OnAudio(IntPtr input, IntPtr output, uint frames, ref StreamCallbackTimeInfo time, StreamCallbackFlags flags, IntPtr user)
    {
        var buffer = new float[frames]; // lo que falte, silencio
        var filled = 0;
        lock (_queue)
        {
            while (filled < buffer.Length && _queue.TryPeek(out var head))
            {
                var count = Math.Min(head.Length - _at, buffer.Length - filled);
                Array.Copy(head, _at, buffer, filled, count);
                filled += count;
                _at += count;
                if (_at < head.Length) continue;
                _queue.Dequeue();
                _at = 0;
            }
            if (_queue.Count == 0) _empty.Set();
        }
        Marshal.Copy(buffer, 0, output, buffer.Length);
        return StreamCallbackResult.Continue;
    }

    /// <summary>Espera a que suene todo lo encolado; si se cancela, vuelve enseguida.</summary>
    public void Drain(CancellationToken cancel)
    {
        if (WaitHandle.WaitAny([_empty.WaitHandle, cancel.WaitHandle]) != 0) return;
        cancel.WaitHandle.WaitOne(TimeSpan.FromSeconds(_latency + 0.1)); // lo último ya salió de la cola, pero todavía suena
    }

    /// <summary>Corta lo que suena (se puede llamar desde otro hilo).</summary>
    public void Stop()
    {
        lock (_queue)
        {
            _queue.Clear();
            _at = 0;
            _empty.Set();
        }
    }

    public void Dispose()
    {
        Stop();
        try { _stream.Abort(); }
        catch (PortAudioException) { }
        _stream.Dispose();
        _empty.Dispose();
    }
}

/// <summary>
/// La voz del sistema, para leer sin la de Claupet: la de Windows (System.Speech), «say» en macOS y, en Linux, la de
/// speech-dispatcher o eSpeak si están instaladas. Sin ninguna, no dice nada.
/// </summary>
sealed class SystemVoice : IDisposable
{
    System.Speech.Synthesis.SpeechSynthesizer? _windows;
    Process? _process;

    public void Say(string text)
    {
        Stop();
        try
        {
            if (OperatingSystem.IsWindows()) SayWindows(text);
            else if (OperatingSystem.IsMacOS()) _process = Run("say", [text]);
            else _process = Run("spd-say", ["-w", "-l", L.Language, "--", text]) ?? Run("espeak-ng", ["-v", L.Language, "--", text])
                ?? Run("espeak", ["-v", L.Language, "--", text]);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { ErrorLog.Write(ex); }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    void SayWindows(string text)
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

    /// <summary>El programa, si está; null si no.</summary>
    static Process? Run(string program, string[] args)
    {
        var start = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        try { return Process.Start(start); }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    public void Stop()
    {
        try
        {
            if (OperatingSystem.IsWindows()) _windows?.SpeakAsyncCancelAll();
            if (_process is { HasExited: false } process) process.Kill();
            if (_process?.StartInfo.FileName == "spd-say") Run("spd-say", ["-C"])?.Dispose();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        _process?.Dispose();
        _process = null;
    }

    public void Dispose()
    {
        Stop();
        if (OperatingSystem.IsWindows()) _windows?.Dispose();
    }
}
