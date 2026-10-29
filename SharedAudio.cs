using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Claupet;

/// <summary>
/// El audio que escuchan las extensiones (audio/subscribe): el micrófono o lo que suena en el equipo, en
/// trozos de 100 ms a 16 kHz, mono, 16 bits. Una sola captura por fuente, compartida por todas las que
/// escuchan: empieza con la primera y se detiene con la última. InUse avisa cuando una fuente empieza o
/// deja de usarse (para mostrarlo), en el hilo de la captura.
/// </summary>
sealed class SharedAudio : IDisposable
{
    public const string MicrophoneSource = "microphone", SystemSource = "system";

    sealed class Source(IDisposable capture)
    {
        public IDisposable Capture { get; } = capture;
        public List<Action<short[]>> Listeners { get; } = new();
    }

    readonly object _lock = new();
    readonly Dictionary<string, Source> _sources = new();

    public event Action<string, bool>? InUse;

    /// <summary>Lanza InvalidOperationException (con el motivo para la persona) si la fuente no se puede abrir.</summary>
    public IDisposable Subscribe(string source, Action<short[]> chunk)
    {
        if (source is not (MicrophoneSource or SystemSource)) throw new ArgumentException(source);
        var started = false;
        lock (_lock)
        {
            if (!_sources.TryGetValue(source, out var shared))
            {
                _sources[source] = shared = new Source(Open(source, pcm => Deliver(source, pcm)));
                started = true;
            }
            shared.Listeners.Add(chunk);
        }
        if (started) InUse?.Invoke(source, true);
        return new Subscription(() => Unsubscribe(source, chunk));
    }

    static IDisposable Open(string source, Action<short[]> deliver)
    {
        if (source == MicrophoneSource)
        {
            var mic = new Microphone { Keep = false };
            mic.Chunk += deliver;
            if (!mic.Start())
            {
                mic.Dispose();
                throw new InvalidOperationException(L.T("No encuentro el micrófono, o Windows no deja usarlo (Configuración › Privacidad › Micrófono)."));
            }
            return mic;
        }
        var loopback = new LoopbackCapture();
        loopback.Chunk += deliver;
        loopback.Start();
        return loopback;
    }

    void Deliver(string source, short[] pcm)
    {
        Action<short[]>[] listeners;
        lock (_lock) listeners = _sources.TryGetValue(source, out var s) ? s.Listeners.ToArray() : [];
        foreach (var listener in listeners)
        {
            try { listener(pcm); }
            catch (Exception ex) { ErrorLog.Write(ex); }
        }
    }

    void Unsubscribe(string source, Action<short[]> chunk)
    {
        IDisposable? stop = null;
        lock (_lock)
        {
            if (!_sources.TryGetValue(source, out var shared) || !shared.Listeners.Remove(chunk) || shared.Listeners.Count > 0) return;
            _sources.Remove(source);
            stop = shared.Capture;
        }
        stop.Dispose();
        InUse?.Invoke(source, false);
    }

    public bool Active(string source)
    {
        lock (_lock) return _sources.ContainsKey(source);
    }

    public void Dispose()
    {
        List<Source> all;
        lock (_lock)
        {
            all = _sources.Values.ToList();
            _sources.Clear();
        }
        foreach (var s in all) s.Capture.Dispose();
    }

    sealed class Subscription(Action dispose) : IDisposable
    {
        int _done;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0) dispose();
        }
    }
}

/// <summary>
/// Lo que suena en el equipo (la salida predeterminada), con WASAPI en modo loopback: en un hilo propio
/// (MTA), en el formato de mezcla de Windows, pasado a mono y a 16 kHz. Cuando no suena nada, Windows no
/// entrega audio: se completa con silencio para que el tiempo no se corra.
/// </summary>
sealed class LoopbackCapture : IDisposable
{
    const int ChunkSamples = 1600; // 100 ms
    static readonly Guid FloatFormat = new("00000003-0000-0010-8000-00aa00389b71");
    static readonly Guid PcmFormat = new("00000001-0000-0010-8000-00aa00389b71");

    public event Action<short[]>? Chunk;
    Thread? _thread;
    volatile bool _running;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(Run) { IsBackground = true, Name = "Audio del sistema" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    void Run()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        var format = IntPtr.Zero;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(0, 0, out device); // eRender, eConsole
            var clientId = typeof(IAudioClient).GUID;
            device.Activate(ref clientId, 0x17, IntPtr.Zero, out var activated); // CLSCTX_ALL
            client = (IAudioClient)activated;
            client.GetMixFormat(out format);
            var (rate, channels, isFloat, bits) = ReadFormat(format);
            client.Initialize(0, 0x00020000, 10_000_000, 0, format, IntPtr.Zero); // compartido, AUDCLNT_STREAMFLAGS_LOOPBACK, 1 s
            var captureId = typeof(IAudioCaptureClient).GUID;
            client.GetService(ref captureId, out var service);
            capture = (IAudioCaptureClient)service;

            var resampler = new PcmResampler(rate);
            var ready = new List<short>();
            long sent = 0;
            var clock = Stopwatch.StartNew();
            var lastPacket = clock.ElapsedMilliseconds;
            client.Start();
            while (_running)
            {
                Thread.Sleep(30);
                capture.GetNextPacketSize(out var packet);
                while (packet > 0 && _running)
                {
                    capture.GetBuffer(out var data, out var frames, out var flags, out _, out _);
                    var mono = new float[frames];
                    if ((flags & 2) == 0) Downmix(data, (int)frames, channels, isFloat, bits, mono); // AUDCLNT_BUFFERFLAGS_SILENT: ceros
                    capture.ReleaseBuffer(frames);
                    ready.AddRange(resampler.Push(mono));
                    lastPacket = clock.ElapsedMilliseconds;
                    capture.GetNextPacketSize(out packet);
                }
                // Sin audio por más de 200 ms: no suena nada, se completa con silencio hasta ahora.
                if (clock.ElapsedMilliseconds - lastPacket > 200)
                {
                    var expected = clock.ElapsedMilliseconds * 16;
                    var missing = expected - sent - ready.Count;
                    if (missing > 0) ready.AddRange(new short[missing]);
                }
                while (ready.Count >= ChunkSamples)
                {
                    var chunk = ready.GetRange(0, ChunkSamples).ToArray();
                    ready.RemoveRange(0, ChunkSamples);
                    sent += ChunkSamples;
                    Chunk?.Invoke(chunk);
                }
            }
            client.Stop();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or NotSupportedException)
        {
            ErrorLog.Write(ex);
        }
        finally
        {
            if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format);
            foreach (var com in new object?[] { capture, client, device, enumerator })
                if (com is not null) Marshal.ReleaseComObject(com);
        }
    }

    /// <summary>El formato de mezcla: frecuencia, canales, si es float y bits por muestra. NotSupportedException si no se entiende.</summary>
    static (int Rate, int Channels, bool Float, int Bits) ReadFormat(IntPtr format)
    {
        var tag = (ushort)Marshal.ReadInt16(format, 0);
        var channels = Marshal.ReadInt16(format, 2);
        var rate = Marshal.ReadInt32(format, 4);
        var bits = Marshal.ReadInt16(format, 14);
        var extra = Marshal.ReadInt16(format, 16);
        var isFloat = tag == 3;
        var isPcm = tag == 1;
        if (tag == 0xFFFE && extra >= 22)
        {
            var sub = Marshal.PtrToStructure<Guid>(format + 24);
            isFloat = sub == FloatFormat;
            isPcm = sub == PcmFormat;
        }
        if (channels < 1 || rate < 8000 || !(isFloat && bits == 32 || isPcm && bits is 16 or 24 or 32))
            throw new NotSupportedException($"mix format {tag:X} {bits} bits");
        return (rate, channels, isFloat, bits);
    }

    static void Downmix(IntPtr data, int frames, int channels, bool isFloat, int bits, float[] mono)
    {
        var bytes = bits / 8;
        var frameBytes = bytes * channels;
        var buffer = new byte[frames * frameBytes];
        Marshal.Copy(data, buffer, 0, buffer.Length);
        for (var f = 0; f < frames; f++)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++)
            {
                var at = f * frameBytes + c * bytes;
                sum += isFloat ? BitConverter.ToSingle(buffer, at)
                    : bits == 16 ? BitConverter.ToInt16(buffer, at) / 32768f
                    : bits == 24 ? (buffer[at] | buffer[at + 1] << 8 | (sbyte)buffer[at + 2] << 16) / 8388608f
                    : BitConverter.ToInt32(buffer, at) / 2147483648f;
            }
            mono[f] = sum / channels;
        }
    }

    public void Dispose()
    {
        _running = false;
        _thread?.Join(1000);
    }

    // Core Audio: solo los métodos hasta el último que se usa, en su orden.

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        void Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        void GetBufferSize(out uint frames);
        void GetStreamLatency(out long latency);
        void GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        void GetMixFormat(out IntPtr format);
        void GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle(IntPtr handle);
        void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        void GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        void ReleaseBuffer(uint frames);
        void GetNextPacketSize(out uint frames);
    }
}
