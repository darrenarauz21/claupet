using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace ClaudeUsageTray.Tests;

/// <summary>La API del anfitrión de mentira: anota lo que le piden.</summary>
sealed class FakeHostApi : IExtensionHostApi
{
    public List<string> Said { get; } = new();
    public List<(string Title, string Text)> Notices { get; } = new();
    public string? Clipboard { get; set; } = "copiado";

    public Task PetAnimateAsync(string extensionId, string move, int? ms) => Task.CompletedTask;
    public Task PetSayAsync(string extensionId, string text) { lock (Said) Said.Add(text); return Task.CompletedTask; }
    public Task NoticeAsync(string extensionId, string title, string text) { lock (Notices) Notices.Add((title, text)); return Task.CompletedTask; }
    public Task IslandCardAsync(string extensionId, string title, string text) => Task.CompletedTask;
    public Task SpeakAsync(string extensionId, string text, string? voice, CancellationToken cancel) => Task.CompletedTask;
    public Task<string> AskClaudeAsync(string extensionId, string prompt, string? model, CancellationToken cancel) => Task.FromResult("respuesta de Claude");
    public JsonNode Sessions() => new JsonArray();
    public Task<string?> ClipboardReadAsync() => Task.FromResult(Clipboard);
    public Task ClipboardWriteAsync(string text) { Clipboard = text; return Task.CompletedTask; }
    public IDisposable SubscribeAudio(string source, Action<short[]> chunk)
    {
        chunk(new short[1600]);
        return new Unsubscribe();
    }

    sealed class Unsubscribe : IDisposable { public void Dispose() { } }
}

/// <summary>Un administrador de extensiones sobre una carpeta temporal, con sus ajustes en memoria.</summary>
sealed class ManagerFixture : IAsyncDisposable
{
    public TempDir Dir { get; } = new();
    public AppSettings Settings { get; } = new();
    public FakeHostApi Api { get; } = new();
    public MemorySecretStore Secrets { get; } = new();
    public ExtensionManager Manager { get; }

    public ManagerFixture()
    {
        var store = new ExtensionStore(Dir["extensiones"], new TrustStore(Dir["extensiones/confianza.json"], []));
        Manager = new ExtensionManager(store, () => Settings, change => change(Settings), Secrets)
        {
            HostApi = Api,
            NativeHostCommand = (dll, single) => (Path.Combine(AppContext.BaseDirectory, "ClaudeUsageTray.exe"),
                single ? [NativeExtensionHost.Flag, dll, NativeExtensionHost.SingleFlag] : [NativeExtensionHost.Flag, dll]),
        };
    }

    /// <summary>Arma, firma (o no) e instala un paquete con ese manifiesto y esos archivos; ocupa las ranuras dadas.</summary>
    public async Task<InstalledExtension> InstallAsync(string manifest, IEnumerable<(string Path, byte[] Data)> files, params string[] slots)
    {
        var folder = Path.Combine(Dir.Path, "fuente-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.json"), manifest);
        foreach (var (path, data) in files)
        {
            var file = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, data);
        }
        var zip = TestKit.Package(Dir, folder);
        var review = Manager.Store.Prepare(zip);
        return await Manager.InstallAsync(review, slots);
    }

    public async ValueTask DisposeAsync()
    {
        await Manager.DisposeAsync();
        Dir.Dispose();
    }
}

static class Repo
{
    /// <summary>La raíz del repositorio (donde está docs/extensiones), subiendo desde la salida de las pruebas.</summary>
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "docs", "extensiones"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("docs/extensiones");
        }
    }

    public static byte[] File(string relative) => System.IO.File.ReadAllBytes(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>La DLL de ejemplo para esta arquitectura.</summary>
    public static byte[] EcoDll() => CompileEco(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
        == System.Runtime.InteropServices.Architecture.Arm64 ? "aarch64-windows-gnu" : "x86_64-windows-gnu");

    /// <summary>Una de 32 bits, para probar que se rechaza con un motivo claro.</summary>
    public static byte[] EcoDll32() => CompileEco("x86-windows-gnu");

    /// <summary>
    /// Compila docs/extensiones/ejemplos/c/eco.c con Zig (pip install ziglang, en el Python que diga
    /// CLAUPET_ZIG_PYTHON o en el entorno conda claupet-tools). Sin Zig, la prueba falla diciendo cómo instalarlo.
    /// </summary>
    static byte[] CompileEco(string target)
    {
        var python = Environment.GetEnvironmentVariable("CLAUPET_ZIG_PYTHON")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "miniconda3", "envs", "claupet-tools", "python.exe");
        Assert.True(System.IO.File.Exists(python), $"Hace falta Zig para compilar la DLL de prueba: pip install ziglang y CLAUPET_ZIG_PYTHON=<python> (no está {python}).");
        var output = Path.Combine(Path.GetTempPath(), "claupet-pruebas", $"eco-{target}-{Guid.NewGuid():N}.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var start = new ProcessStartInfo(python) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in new[] { "-m", "ziglang", "cc", "-shared", "-O2", "-target", target, "-o", output, Path.Combine(Root, "docs", "extensiones", "ejemplos", "c", "eco.c") })
            start.ArgumentList.Add(a);
        using var zig = Process.Start(start)!;
        var errors = zig.StandardError.ReadToEnd();
        zig.WaitForExit();
        Assert.True(zig.ExitCode == 0, errors);
        return System.IO.File.ReadAllBytes(output);
    }
}

/// <summary>Un servidor HTTP mínimo de mentira (como los de OpenAI) para probar los adaptadores.</summary>
sealed class FakeHttpServer : IDisposable
{
    readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    readonly Func<string, string, Dictionary<string, string>, byte[], (int Status, string Type, byte[] Body)> _handler;
    public List<(string Path, Dictionary<string, string> Headers, byte[] Body)> Requests { get; } = new();

    public FakeHttpServer(Func<string, string, Dictionary<string, string>, byte[], (int, string, byte[])> handler)
    {
        _handler = handler;
        _listener.Start();
        _ = Task.Run(LoopAsync);
    }

    public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    async Task LoopAsync()
    {
        while (true)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(); } catch (Exception) { return; }
            _ = Task.Run(async () =>
            {
                using (client)
                {
                    var stream = client.GetStream();
                    var head = new List<byte>();
                    var one = new byte[1];
                    while (!(head.Count >= 4 && head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n'))
                    {
                        if (await stream.ReadAsync(one) == 0) return;
                        head.Add(one[0]);
                    }
                    var lines = Encoding.ASCII.GetString(head.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                    var parts = lines[0].Split(' ');
                    var headers = lines.Skip(1).Select(l => l.Split(':', 2)).ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);
                    var length = headers.TryGetValue("Content-Length", out var l) ? int.Parse(l) : 0;
                    var body = new byte[length];
                    var have = 0;
                    while (have < length) have += await stream.ReadAsync(body.AsMemory(have));
                    lock (Requests) Requests.Add((parts[1], headers, body));
                    var (status, type, reply) = _handler(parts[0], parts[1], headers, body);
                    var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} X\r\nContent-Type: {type}\r\nContent-Length: {reply.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(response);
                    await stream.WriteAsync(reply);
                }
            });
        }
    }

    public void Dispose() => _listener.Stop();
}
