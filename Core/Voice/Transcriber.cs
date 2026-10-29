using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Whisper.net;

namespace Claupet;

/// <summary>
/// El modelo de voz de Whisper (ggml «small», multilingüe, 488 MB): se descarga una vez, al encender el
/// micrófono, a la carpeta de la app. Después todo funciona sin internet.
/// </summary>
static class VoiceModel
{
    const string Url = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin";
    /// <summary>Lo que mide y su SHA-256, según Hugging Face: una descarga que no coincide no se usa.</summary>
    public const long Size = 487_601_967;
    const string Sha256 = "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b";

    public static string FilePath => DataFolder.In("voz", "ggml-small.bin");

    /// <summary>Ya está descargado (y no a medias).</summary>
    public static bool Ready => Length(FilePath) == Size;

    static long Length(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    /// <summary>Lo baja (ver ModelDownload) y avisa el avance de 0 a 1.</summary>
    public static Task DownloadAsync(IProgress<double> progress, CancellationToken cancel) =>
        ModelDownload.FetchAsync(Url, FilePath, Size, Sha256, new Progress<long>(have => progress.Report((double)have / Size)), cancel);
}

/// <summary>
/// La descarga de un archivo de modelo: a un .parte y, entero y con su SHA-256, a su lugar. Si la
/// conexión se corta (o se queda un minuto sin datos), retoma donde iba; también lo que quedó de una
/// vez anterior. Avisa cuántos bytes lleva.
/// </summary>
static class ModelDownload
{
    static long Length(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    /// <summary>mismatch: lo que se dice si lo bajado no es lo esperado (si no, que no es el modelo).</summary>
    public static async Task FetchAsync(string url, string path, long size, string sha256, IProgress<long> progress, CancellationToken cancel,
        string? mismatch = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var part = path + ".parte";
        using var http = SystemProxy.Client(Timeout.InfiniteTimeSpan);
        var failures = 0;
        progress.Report(Length(part));
        while (Length(part) < size)
        {
            var before = Length(part);
            try { await ResumeAsync(http, url, part, progress, cancel); }
            catch (Exception ex) when (!cancel.IsCancellationRequested && ex is HttpRequestException or IOException or OperationCanceledException)
            {
                failures = Length(part) > before ? 1 : failures + 1;
                if (failures >= 8) throw new IOException(L.T("Se cortó la conexión varias veces seguidas."), ex);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 3 * failures)), cancel);
            }
        }
        progress.Report(size);
        if (Length(part) != size || !await Task.Run(() => Matches(part, sha256, cancel), cancel))
        {
            File.Delete(part);
            throw new IOException(mismatch ?? L.T("Lo descargado no es el modelo esperado; se borró."));
        }
        File.Move(part, path, overwrite: true);
    }

    static async Task ResumeAsync(HttpClient http, string url, string part, IProgress<long> progress, CancellationToken cancel)
    {
        var have = Length(part);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (have > 0) request.Headers.Range = new RangeHeaderValue(have, null);
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        stall.CancelAfter(TimeSpan.FromSeconds(60));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Delete(part); // lo que había no sirve para retomar: desde cero
            return;
        }
        response.EnsureSuccessStatusCode();
        if (response.StatusCode != HttpStatusCode.PartialContent) have = 0; // el servidor no retoma: desde cero
        await using var source = await response.Content.ReadAsStreamAsync(stall.Token);
        await using var target = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
        var buffer = new byte[1 << 16];
        var reported = have;
        while (true)
        {
            stall.CancelAfter(TimeSpan.FromSeconds(60));
            var read = await source.ReadAsync(buffer, stall.Token);
            if (read == 0) break;
            await target.WriteAsync(buffer.AsMemory(0, read), cancel);
            have += read;
            if (have - reported >= 1_000_000) { reported = have; progress.Report(have); }
        }
    }

    static bool Matches(string path, string sha256, CancellationToken cancel)
    {
        using var file = File.OpenRead(path);
        using var sha = SHA256.Create();
        var buffer = new byte[1 << 20];
        int read;
        while ((read = file.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancel.ThrowIfCancellationRequested();
            sha.TransformBlock(buffer, 0, read, null, 0);
        }
        sha.TransformFinalBlock(buffer, 0, 0);
        return Convert.ToHexString(sha.Hash!).Equals(sha256, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Pasa la voz grabada a texto con Whisper, en el idioma de la app, en el equipo. De a una: el modelo cargado ocupa
/// unos 850 MB de memoria, y VoiceInput lo suelta cuando no se usa.
/// </summary>
sealed class Transcriber : IDisposable
{
    /// <summary>Por debajo de este nivel (RMS de 16 bits, en trozos de 100 ms) no hay voz.</summary>
    const double Quiet = 350;
    /// <summary>Con silencio o ruido, Whisper «oye» frases de los subtítulos con que se entrenó.</summary>
    static readonly string[] Phantoms = ["amara.org", "subtítulos realizados", "subtitulado por", "suscríbete", "gracias por ver",
        "thanks for watching", "thank you for watching", "subtitles by", "please subscribe"];
    /// <summary>El tono y las palabras que se esperan: ayuda a escribir bien «Claude» y los signos de pregunta.</summary>
    static string Prompt => L.T("Hola, Claupet. ¿Cómo van mis sesiones de Claude Code?");

    readonly string _model;
    readonly SemaphoreSlim _one = new(1, 1);
    readonly object _gate = new();
    WhisperFactory? _factory;
    bool _disposed;

    public Transcriber(string? model = null) => _model = model ?? VoiceModel.FilePath;

    static int Threads => Math.Clamp(Environment.ProcessorCount / 2, 1, 8);

    /// <summary>Las muestras de un WAV de 16 kHz, mono y 16 bits (--hablar y --transcribir); null si no tiene datos.</summary>
    public static short[]? ReadWav(byte[] bytes)
    {
        var at = 12; // después de «RIFF», el largo y «WAVE»: los trozos, hasta «data»
        while (at + 8 <= bytes.Length && Encoding.ASCII.GetString(bytes, at, 4) != "data") at += 8 + BitConverter.ToInt32(bytes, at + 4);
        if (at + 8 > bytes.Length) return null;
        var pcm = new short[Math.Min(BitConverter.ToInt32(bytes, at + 4), bytes.Length - at - 8) / 2];
        Buffer.BlockCopy(bytes, at + 8, pcm, 0, pcm.Length * 2);
        return pcm;
    }

    /// <summary>Carga el modelo (tarda un poco): se llama al empezar a escuchar, mientras la persona habla.</summary>
    public Task WarmAsync() => Task.Run(Factory);

    WhisperFactory Factory()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_factory is null) FindNativeLibraries();
            return _factory ??= WhisperFactory.FromPath(_model);
        }
    }

    /// <summary>
    /// En el paquete de un solo archivo (Windows y Linux), las bibliotecas de Whisper se extraen a una carpeta
    /// temporal (…\runtimes\win-x64, …/runtimes/linux-x64) y Whisper.net solo las busca junto al ejecutable. Se le
    /// indica la carpeta de extracción: toma la carpeta de LibraryPath y le suma runtimes y el sistema, así que basta
    /// un nombre de archivo dentro de ella.
    /// </summary>
    static void FindNativeLibraries()
    {
        if (Directory.Exists(Path.Combine(AppContext.BaseDirectory, "runtimes"))) return;
        var extracted = (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(d => Directory.Exists(Path.Combine(d, "runtimes")));
        if (extracted is not null) Whisper.net.LibraryLoader.RuntimeOptions.LibraryPath = Path.Combine(extracted, "whisper.dll");
    }

    /// <summary>El texto de lo grabado; vacío si no hubo voz (o solo se «oyó» una frase fantasma).</summary>
    public async Task<string> TranscribeAsync(short[] pcm, CancellationToken cancel = default)
    {
        if (!HasVoice(pcm)) return "";
        await _one.WaitAsync(cancel);
        try
        {
            return await Task.Run(async () =>
            {
                var padded = Padded(pcm);
                // await using: si se cancela a mitad (un trozo parcial, al soltar la tecla), Dispose fallaría
                // mientras Whisper todavía procesa; DisposeAsync espera a que termine.
                await using var processor = Factory().CreateBuilder()
                    .WithLanguage(L.Language)
                    .WithPrompt(Prompt)
                    .WithThreads(Threads)
                    .WithNoSpeechThreshold(0.6f)
                    .WithAudioContextSize(AudioContext(padded.Length))
                    .Build();
                using var wav = Wav(padded);
                var text = new StringBuilder();
                await foreach (var segment in processor.ProcessAsync(wav, cancel))
                    if (segment.NoSpeechProbability < 0.6f) text.Append(segment.Text);
                return Clean(text.ToString());
            }, cancel);
        }
        finally
        {
            lock (_gate)
                if (_disposed) { _factory?.Dispose(); _factory = null; }
            _one.Release();
        }
    }

    /// <summary>
    /// Trozos de 100 ms sobre Quiet que hacen falta para que haya voz: uno o dos sueltos son un golpe o el micrófono que
    /// se acomoda al abrirse (algunos dan un pico en su primer medio segundo), y con eso Whisper inventa frases.
    /// </summary>
    public const int VoiceChunks = 3;

    internal static bool HasVoice(short[] pcm)
    {
        var loud = 0;
        for (var start = 0; start < pcm.Length; start += 1600)
        {
            var end = Math.Min(pcm.Length, start + 1600);
            double sum = 0;
            for (var i = start; i < end; i++) sum += (double)pcm[i] * pcm[i];
            if (Math.Sqrt(sum / (end - start)) > Quiet && ++loud >= VoiceChunks) return true;
        }
        return false;
    }

    /// <summary>Con silencio al final (Whisper corta la última palabra si el audio termina de golpe) y al menos 1 s.</summary>
    static short[] Padded(short[] pcm)
    {
        var padded = new short[Math.Max(pcm.Length + 4000, 17600)];
        pcm.CopyTo(padded, 0);
        return padded;
    }

    /// <summary>
    /// La ventana del codificador, del largo de lo dicho (50 por segundo, más un margen) en vez de los 30 s de
    /// siempre: una frase de 3 s pasa de unos 5 s a menos de 1 s, y acierta igual (medido con 4 frases).
    /// </summary>
    static int AudioContext(int samples) => Math.Min(1500, samples * 50 / 16000 + 64);

    /// <summary>Sin [Música], (risas), frases fantasma ni espacios de más.</summary>
    static string Clean(string text)
    {
        text = Regex.Replace(text, @"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*", " ");
        var sentences = Regex.Split(text, @"(?<=[.!?…])\s+")
            .Where(s => !Phantoms.Any(p => s.Contains(p, StringComparison.OrdinalIgnoreCase)));
        return Regex.Replace(string.Join(" ", sentences), @"\s+", " ").Trim().TrimStart('-', ' ');
    }

    /// <summary>Lo grabado como un WAV en memoria (16 kHz, 16 bits, mono), que es lo que lee Whisper.</summary>
    static MemoryStream Wav(short[] pcm)
    {
        var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            w.Write("RIFF"u8); w.Write(36 + pcm.Length * 2); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(16000); w.Write(32000); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(pcm.Length * 2);
            foreach (var s in pcm) w.Write(s);
        }
        stream.Position = 0;
        return stream;
    }

    /// <summary>Suelta el modelo; si está pasando algo a texto, al terminar.</summary>
    public void Dispose()
    {
        lock (_gate) _disposed = true;
        if (!_one.Wait(0)) return;
        lock (_gate) { _factory?.Dispose(); _factory = null; }
        _one.Release();
    }
}
