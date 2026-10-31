using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

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

    // llama-server de llama.cpp, versión b11913, compilado con Vulkan para x64: usa la tarjeta gráfica (dedicada o
    // integrada, de cualquier marca) y trae también el motor del procesador, que queda si no hay gráfica o no arranca.
    const string Build = "b11913";
    const string ServerUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b11913/llama-b11913-bin-win-vulkan-x64.zip";
    public const long ServerSize = 37_140_477;
    const string ServerSha256 = "d9b27dfe8addb0782c59c194604dbab69ccef8493c8964a435d762c4be45e176";

    /// <summary>La tarjeta gráfica (Ajustes › Respuestas): si arranca con ella, siempre con ella, o nunca.</summary>
    public const string GpuAuto = "auto", GpuOn = "on", GpuOff = "off";

    public static string Folder => DataFolder.In("modelo-local");
    static string ServerFolder => Path.Combine(Folder, "llama-" + Build + "-vulkan");
    /// <summary>La de antes, solo para el procesador: se sigue usando, si estaba, hasta que se baje la de Vulkan.</summary>
    static string CpuServerFolder => Path.Combine(Folder, "llama-" + Build);
    static string GpuServerExe => Path.Combine(ServerFolder, "llama-server.exe");
    public static string ServerExe => File.Exists(GpuServerExe) ? GpuServerExe : Path.Combine(CpuServerFolder, "llama-server.exe");
    public static string ModelFile(Choice choice) => Path.Combine(Folder, choice.File);

    /// <summary>Está la versión que puede usar la tarjeta gráfica (la de Vulkan).</summary>
    public static bool GpuServerReady => File.Exists(GpuServerExe);

    static Task<string?>? _gpuDevice;

    /// <summary>
    /// La tarjeta gráfica que usaría llama-server, como la nombra él (llama-server --list-devices; al arrancar no lo
    /// dice), con «integrada» si comparte la memoria; null si no hay ninguna o falta la versión de Vulkan. Se pregunta una
    /// vez por sesión.
    /// </summary>
    public static Task<string?> GpuDeviceAsync() => _gpuDevice ??= Task.Run(ListGpu);

    static string? ListGpu()
    {
        if (!GpuServerReady) return null;
        try
        {
            // -v antes de --list-devices (que sale al leerlo): así escribe también «ggml_vulkan: N = … | uma: …» por
            // cada gráfica, aunque la salida esté capturada.
            using var process = Process.Start(new ProcessStartInfo(GpuServerExe, "-v --list-devices")
            {
                WorkingDirectory = ServerFolder, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            });
            if (process is null) return null;
            var errors = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(10_000))
            {
                process.Kill();
                return null;
            }
            return ParseGpu(output + "\n" + errors.Result);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { return null; }
    }

    /// <summary>
    /// De lo que escribe -v --list-devices, la gráfica en que correría: con «ggml_vulkan: N = … | uma: 0|1 | …» (uma:
    /// comparte la memoria con el procesador), la primera dedicada y, si no hay ninguna, la primera integrada (con
    /// «integrada»), que es como elige llama.cpp: en una laptop con Intel y NVIDIA, Vulkan0 es la Intel pero corre en la
    /// NVIDIA. Sin esas líneas, el nombre de la primera de la lista («  Vulkan0: AMD Radeon(TM) Graphics (12978 MiB, …)»).
    /// </summary>
    internal static string? ParseGpu(string text)
    {
        const string Detail = "ggml_vulkan: ", Listed = "Vulkan0: ";
        var lines = text.Split('\n');
        string? integrated = null;
        foreach (var line in lines)
        {
            var at = line.IndexOf(Detail, StringComparison.Ordinal);
            if (at < 0) continue;
            var rest = line[(at + Detail.Length)..];
            var equals = rest.IndexOf(" = ", StringComparison.Ordinal);
            if (equals < 1 || !rest[..equals].All(char.IsAsciiDigit)) continue; // «Found 2 Vulkan devices:» y otras
            rest = rest[(equals + 3)..];
            var name = rest.Split([" (", " |"], 2, StringSplitOptions.None)[0].Trim();
            if (!rest.Contains("uma: 1", StringComparison.Ordinal)) return name;
            integrated ??= L.T("{0} (integrada)", name);
        }
        if (integrated is not null) return integrated;
        foreach (var line in lines)
            if (line.Trim() is var listed && listed.StartsWith(Listed, StringComparison.Ordinal))
                return listed[Listed.Length..].Split(" (", 2)[0].Trim();
        return null;
    }

    /// <summary>El elegido (o el propuesto, si no hay ninguno o ya no existe).</summary>
    public static Choice Find(string? id) => Choices.FirstOrDefault(c => c.Id == id) ?? Choices.First(c => c.Id == DefaultId);

    public static bool ServerReady => File.Exists(ServerExe);

    static bool ModelReady(Choice choice) => Length(ModelFile(choice)) == choice.Size;

    /// <summary>Están llama-server y ese modelo (entero).</summary>
    public static bool Ready(Choice choice) => ServerReady && ModelReady(choice);

    /// <summary>Lo que falta bajar para usarlo, en bytes; con gpu, también la versión de Vulkan si solo está la de antes.</summary>
    public static long Missing(Choice choice, bool gpu = true) =>
        ((gpu ? GpuServerReady : ServerReady) ? 0 : ServerSize) + (ModelReady(choice) ? 0 : choice.Size);

    static long Length(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    /// <summary>
    /// Baja lo que falte (llama-server y el modelo), retomando lo que haya quedado, y avisa el avance de 0 a 1.
    /// Al terminar borra los otros modelos, para no dejar gigas sin usar.
    /// </summary>
    public static async Task DownloadAsync(Choice choice, IProgress<double> progress, CancellationToken cancel, bool gpu = true)
    {
        var total = Math.Max(1, Missing(choice, gpu));
        long done = 0;
        if (gpu ? !GpuServerReady : !ServerReady)
        {
            var zip = Path.Combine(Folder, "llama-server.zip");
            await ModelDownload.FetchAsync(ServerUrl, zip, ServerSize, ServerSha256,
                new Progress<long>(have => progress.Report((double)have / total)), cancel);
            await Task.Run(() => Extract(zip), cancel);
            File.Delete(zip);
            done += ServerSize;
            _gpuDevice = null; // ya hay a quién preguntarle por la gráfica
            // La de antes queda (24 MB): otra copia de la app, de una versión anterior, puede estar usándola.
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
/// puede usarlo). Va en un Job que lo cierra si Claupet termina, y tras IdleMinutes sin uso suelta la memoria.
/// </summary>
sealed class LocalModelServer : IDisposable
{
    /// <summary>Minutos sin uso tras los que suelta la memoria (Ajustes › Respuestas); vale desde el próximo arranque.</summary>
    public int IdleMinutes { get; set; } = 5;

    /// <summary>La tarjeta gráfica (LocalModel.GpuAuto, GpuOn o GpuOff); vale desde el próximo arranque.</summary>
    public string Gpu { get; set; } = LocalModel.GpuAuto;

    /// <summary>
    /// La gráfica en que corre, como la nombra llama.cpp (con «integrada» si comparte la memoria); null si corre en el
    /// procesador o todavía no arrancó.
    /// </summary>
    public string? Device => _process is { HasExited: false } && _onGpu ? _device : null;

    bool _onGpu, _gpuFailed;
    string? _device;

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
            var gpu = LocalModel.GpuServerReady && Gpu != LocalModel.GpuOff && !(_gpuFailed && Gpu == LocalModel.GpuAuto);
            try { await StartAsync(file, gpu, cancel); }
            catch (ExtensionUnavailableException) when (gpu && Gpu == LocalModel.GpuAuto && !cancel.IsCancellationRequested)
            {
                // Con la tarjeta gráfica no arrancó (sin memoria para el modelo, un controlador viejo): sigue con el procesador.
                _gpuFailed = true;
                Stop();
                await StartAsync(file, gpu: false, cancel);
            }
            return (_base!, _key);
        }
        catch
        {
            Stop();
            throw;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Arranca llama-server con ese modelo: con gpu, todas sus capas en la tarjeta gráfica; sin, solo el procesador.</summary>
    async Task StartAsync(string file, bool gpu, CancellationToken cancel)
    {
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
            "-c", "4096", "-np", "1", "--reasoning-budget", "0", "--no-webui", "--sleep-idle-seconds", (IdleMinutes * 60).ToString(),
        })
            start.ArgumentList.Add(arg);
        // La de Vulkan: todo el modelo en la gráfica, o ninguna gráfica (si no, igual le pasaría parte del trabajo).
        if (LocalModel.GpuServerReady)
            foreach (var arg in gpu ? new[] { "-ngl", "99" } : ["--device", "none"])
                start.ArgumentList.Add(arg);
        _onGpu = gpu;
        _device = gpu ? await LocalModel.GpuDeviceAsync() : null;
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
sealed class LocalBrain(LocalModelServer server, LocalModel.Choice choice) : IAssistantBrain, IStructuredBrain, ISmallBrain
{
    public string Name => choice.Name;

    public async Task<BrainReply> RespondAsync(BrainRequest request, Action<string>? onText, CancellationToken cancel)
    {
        var (root, key) = await server.EnsureAsync(choice, cancel);
        using var http = LocalModelServer.Client(TimeSpan.FromMinutes(5));
        var thinking = new ThinkFilter(onText);
        var reply = await OpenAiChat.SendAsync(http, new Uri(root, "chat/completions"), AssistantModels.Local, request,
            message => message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key), Name,
            response => Task.FromResult(L.T("{0} no respondió (error {1}).", Name, (int)response.StatusCode)), thinking.Push, cancel);
        return reply with { Model = choice.Name, Text = ThinkFilter.Strip(reply.Text) };
    }

    /// <summary>Una respuesta que cumple el esquema (llama-server la restringe con una gramática), sin creatividad.</summary>
    public async Task<string?> JsonAsync(string system, string prompt, string schema, CancellationToken cancel)
    {
        var (root, key) = await server.EnsureAsync(choice, cancel);
        using var http = LocalModelServer.Client(TimeSpan.FromSeconds(60));
        var body = new Dictionary<string, object>
        {
            ["model"] = AssistantModels.Local,
            ["messages"] = new object[] { new { role = "system", content = system }, new { role = "user", content = prompt } },
            ["temperature"] = 0,
            ["max_tokens"] = 200,
            ["response_format"] = new { type = "json_schema", json_schema = new { name = "intencion", strict = true, schema = JsonDocument.Parse(schema).RootElement } },
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(root, "chat/completions"))
        {
            Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await http.SendAsync(message, cancel);
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        return doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var answer) ? Json.Str(answer, "content") : null;
    }
}

/// <summary>
/// Qwen3.5 a veces abre la respuesta con «&lt;think&gt;&lt;/think&gt;» aunque no razone: no se muestra. Mientras
/// lo que llega puede ser ese comienzo, se guarda; lo demás pasa tal cual.
/// </summary>
sealed class ThinkFilter(Action<string>? onText)
{
    const string Open = "<think>", Close = "</think>";
    readonly System.Text.StringBuilder _held = new();
    bool _passing;

    public void Push(string piece)
    {
        if (_passing) { onText?.Invoke(piece); return; }
        _held.Append(piece);
        var text = _held.ToString().TrimStart();
        if (text.Length < Open.Length ? Open.StartsWith(text, StringComparison.Ordinal) : text.StartsWith(Open, StringComparison.Ordinal))
        {
            var end = text.IndexOf(Close, StringComparison.Ordinal);
            if (end < 0) return;
            text = text[(end + Close.Length)..].TrimStart();
            if (text.Length == 0) { _held.Clear(); _held.Append(' '); return; }
        }
        _passing = true;
        _held.Clear();
        onText?.Invoke(text);
    }

    public static string Strip(string text)
    {
        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith(Open, StringComparison.Ordinal)) return text;
        var end = trimmed.IndexOf(Close, StringComparison.Ordinal);
        return end < 0 ? "" : trimmed[(end + Close.Length)..].TrimStart();
    }
}
