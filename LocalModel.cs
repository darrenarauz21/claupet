using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Claupet;

/// <summary>
/// El modelo local: llama-server (llama.cpp, MIT) y un modelo Qwen3.5 (Apache 2.0) en GGUF. Se bajan a
/// pedido, de versiones fijas y con su SHA-256, como Whisper; después responden en la mascota y la isla
/// sin internet cuando se elige «Modelo local» como modelo.
/// </summary>
static class LocalModel
{
    /// <summary>Un modelo para elegir: Ram es lo que ocupa respondiendo (estimado, con contexto de 4.096).</summary>
    public sealed record Choice(string Id, string Name, string File, string Url, long Size, string Sha256, string Ram);

    /// <summary>Las versiones cuantizadas Q4_K_M de Unsloth, fijadas a un commit de Hugging Face.</summary>
    public static readonly Choice[] Choices =
    [
        new("qwen3.5-0.8b", "Qwen3.5 0.8B", "Qwen3.5-0.8B-Q4_K_M.gguf",
            "https://huggingface.co/unsloth/Qwen3.5-0.8B-GGUF/resolve/6ab461498e2023f6e3c1baea90a8f0fe38ab64d0/Qwen3.5-0.8B-Q4_K_M.gguf",
            532_517_120, "bd258782e35f7f458f8aced1adc053e6e92e89bc735ba3be89d38a06121dc517", "1 GB"),
        new("qwen3.5-2b", "Qwen3.5 2B", "Qwen3.5-2B-Q4_K_M.gguf",
            "https://huggingface.co/unsloth/Qwen3.5-2B-GGUF/resolve/f6d5376be1edb4d416d56da11e5397a961aca8ae/Qwen3.5-2B-Q4_K_M.gguf",
            1_280_835_840, "aaf42c8b7c3cab2bf3d69c355048d4a0ee9973d48f16c731c0520ee914699223", "2 GB"),
        new("qwen3.5-4b", "Qwen3.5 4B", "Qwen3.5-4B-Q4_K_M.gguf",
            "https://huggingface.co/unsloth/Qwen3.5-4B-GGUF/resolve/e87f176479d0855a907a41277aca2f8ee7a09523/Qwen3.5-4B-Q4_K_M.gguf",
            2_740_937_888, "00fe7986ff5f6b463e62455821146049db6f9313603938a70800d1fb69ef11a4", "3,5 GB"),
    ];

    /// <summary>El que se propone: responde bien en español y cabe en casi cualquier equipo.</summary>
    public const string DefaultId = "qwen3.5-2b";

    // llama-server de llama.cpp, versión b11913, compilado para CPU x64.
    const string Build = "b11913";
    const string ServerUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b11913/llama-b11913-bin-win-cpu-x64.zip";
    public const long ServerSize = 24_218_562;
    const string ServerSha256 = "55b31ccaf398e90457cfe28087297f5f3fdf1e3bfa7a28e1108f13d0af8e4621";

    public static string Folder => DataFolder.In("modelo-local");
    static string ServerFolder => Path.Combine(Folder, "llama-" + Build);
    public static string ServerExe => Path.Combine(ServerFolder, "llama-server.exe");
    public static string ModelFile(Choice choice) => Path.Combine(Folder, choice.File);

    /// <summary>El elegido (o el propuesto, si no hay ninguno o ya no existe).</summary>
    public static Choice Find(string? id) => Choices.FirstOrDefault(c => c.Id == id) ?? Choices.First(c => c.Id == DefaultId);

    public static bool ServerReady => File.Exists(ServerExe);

    static bool ModelReady(Choice choice) => Length(ModelFile(choice)) == choice.Size;

    /// <summary>Están llama-server y ese modelo (entero).</summary>
    public static bool Ready(Choice choice) => ServerReady && ModelReady(choice);

    /// <summary>Lo que falta bajar para usarlo, en bytes.</summary>
    public static long Missing(Choice choice) => (ServerReady ? 0 : ServerSize) + (ModelReady(choice) ? 0 : choice.Size);

    static long Length(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    /// <summary>
    /// Baja lo que falte (llama-server y el modelo), retomando lo que haya quedado, y avisa el avance de 0 a 1.
    /// Al terminar borra los otros modelos, para no dejar gigas sin usar.
    /// </summary>
    public static async Task DownloadAsync(Choice choice, IProgress<double> progress, CancellationToken cancel)
    {
        var total = Math.Max(1, Missing(choice));
        long done = 0;
        if (!ServerReady)
        {
            var zip = Path.Combine(Folder, "llama-server.zip");
            await ModelDownload.FetchAsync(ServerUrl, zip, ServerSize, ServerSha256,
                new Progress<long>(have => progress.Report((double)have / total)), cancel);
            await Task.Run(() => Extract(zip), cancel);
            File.Delete(zip);
            done += ServerSize;
        }
        if (!ModelReady(choice))
        {
            var before = done;
            await ModelDownload.FetchAsync(choice.Url, ModelFile(choice), choice.Size, choice.Sha256,
                new Progress<long>(have => progress.Report((double)(before + have) / total)), cancel);
        }
        progress.Report(1);
        foreach (var other in Choices.Where(c => c != choice))
            foreach (var file in new[] { ModelFile(other), ModelFile(other) + ".parte" })
                try { File.Delete(file); } catch (IOException) { } // en uso: se borra la próxima vez
    }

    /// <summary>
    /// Saca del zip solo lo que usa llama-server (sus DLL, las de ggml para cada CPU, OpenMP y las licencias),
    /// a una carpeta aparte que después toma su lugar: nunca queda una a medias.
    /// </summary>
    static void Extract(string zip)
    {
        var temporary = ServerFolder + ".parte";
        if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        Directory.CreateDirectory(temporary);
        using (var archive = ZipFile.OpenRead(zip))
            foreach (var entry in archive.Entries)
            {
                var name = entry.Name;
                if (name.Length == 0 || entry.FullName.Contains('/') || entry.FullName.Contains('\\') || !Needed(name)) continue;
                entry.ExtractToFile(Path.Combine(temporary, name));
            }
        if (!File.Exists(Path.Combine(temporary, "llama-server.exe")))
            throw new IOException(L.T("El paquete de llama-server no trae llama-server.exe."));
        if (Directory.Exists(ServerFolder)) Directory.Delete(ServerFolder, recursive: true);
        Directory.Move(temporary, ServerFolder);
    }

    static bool Needed(string name) =>
        name is "llama-server.exe" or "llama-server-impl.dll" or "llama-common.dll" or "llama.dll" or "mtmd.dll" or "libomp.dll"
        || name.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("ggml", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    /// <summary>Borra todo lo descargado (llama-server y los modelos).</summary>
    public static void Remove()
    {
        if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
    }
}

/// <summary>
/// llama-server corriendo en este equipo: arranca la primera vez que se le pregunta algo, solo escucha en
/// 127.0.0.1 en un puerto libre y pide una clave que cambia en cada arranque (otro programa del equipo no
/// puede usarlo). Va en un Job que lo cierra si Claupet termina, y a los 10 minutos sin uso suelta la memoria.
/// </summary>
sealed class LocalModelServer : IDisposable
{
    readonly SemaphoreSlim _lock = new(1, 1);
    readonly KillOnCloseJob _job = new();
    readonly Queue<string> _log = new();
    Process? _process;
    string? _file;
    Uri? _base;
    string _key = "";

    /// <summary>Lo arranca con ese modelo (si no corre ya con él) y espera a que esté listo para responder.</summary>
    public async Task<(Uri Base, string Key)> EnsureAsync(LocalModel.Choice choice, CancellationToken cancel)
    {
        await _lock.WaitAsync(cancel);
        try
        {
            var file = LocalModel.ModelFile(choice);
            if (_process is { HasExited: false } && _file == file && _base is { } running) return (running, _key);
            Stop();
            var port = FreePort();
            _key = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            var start = new ProcessStartInfo(LocalModel.ServerExe)
            {
                WorkingDirectory = Path.GetDirectoryName(LocalModel.ServerExe)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            // Sin «pensar» en voz alta (responde enseguida), una sola conversación a la vez y sin la página web.
            foreach (var arg in new[]
            {
                "-m", file, "--host", "127.0.0.1", "--port", port.ToString(), "--api-key", _key, "-a", AssistantModels.Local,
                "-c", "4096", "-np", "1", "--reasoning-budget", "0", "--no-webui", "--sleep-idle-seconds", "600",
            })
                start.ArgumentList.Add(arg);
            // La clave va por argumentos y no por el entorno: no la hereda nada más.
            var process = new Process { StartInfo = start };
            process.OutputDataReceived += (_, e) => Remember(e.Data);
            process.ErrorDataReceived += (_, e) => Remember(e.Data);
            lock (_log) _log.Clear();
            process.Start();
            _job.Add(process);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
            _file = file;
            _base = new Uri($"http://127.0.0.1:{port}/v1/");
            await WaitReadyAsync(process, port, cancel);
            return (_base, _key);
        }
        catch
        {
            Stop();
            throw;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Espera a que /health responda 200 (cargar el modelo lleva unos segundos); si se cierra antes, dice por qué.</summary>
    async Task WaitReadyAsync(Process process, int port, CancellationToken cancel)
    {
        using var http = Client(TimeSpan.FromSeconds(5));
        var until = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < until)
        {
            if (process.HasExited)
                throw new ExtensionUnavailableException(L.T("El modelo local no pudo arrancar: {0}", LastLine() ?? L.T("se cerró")));
            try
            {
                using var response = await http.GetAsync($"http://127.0.0.1:{port}/health", cancel);
                if (response.StatusCode == HttpStatusCode.OK) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!cancel.IsCancellationRequested) { }
            await Task.Delay(300, cancel);
        }
        throw new ExtensionUnavailableException(L.T("El modelo local tardó demasiado en cargar."));
    }

    void Remember(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (_log)
        {
            _log.Enqueue(line);
            while (_log.Count > 20) _log.Dequeue();
        }
    }

    /// <summary>La última línea que parece un error (o la última, si ninguna).</summary>
    string? LastLine()
    {
        lock (_log)
            return _log.LastOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)) ?? _log.LastOrDefault();
    }

    static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Directo a 127.0.0.1, sin el proxy del sistema.</summary>
    public static HttpClient Client(TimeSpan timeout) => new(new SocketsHttpHandler { UseProxy = false }) { Timeout = timeout };

    public void Stop()
    {
        if (_process is { } process)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            process.Dispose();
        }
        _process = null;
        _file = null;
        _base = null;
    }

    public void Dispose()
    {
        Stop();
        _job.Dispose();
    }
}

/// <summary>El modelo local como asistente: llama-server habla el formato de OpenAI.</summary>
sealed class LocalBrain(LocalModelServer server, LocalModel.Choice choice) : IAssistantBrain
{
    public string Name => choice.Name;

    public async Task<BrainReply> RespondAsync(BrainRequest request, Action<string>? onText, CancellationToken cancel)
    {
        var (root, key) = await server.EnsureAsync(choice, cancel);
        using var http = LocalModelServer.Client(TimeSpan.FromMinutes(5));
        var reply = await OpenAiChat.SendAsync(http, new Uri(root, "chat/completions"), AssistantModels.Local, request,
            message => message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key), Name,
            response => Task.FromResult(L.T("{0} no respondió (error {1}).", Name, (int)response.StatusCode)), onText, cancel);
        return reply with { Model = choice.Name };
    }
}
