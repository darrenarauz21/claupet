using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet;

/// <summary>Por dónde se le habla a una extensión que corre: un proceso por stdio (también el anfitrión de una DLL) o un servidor claupet-rpc.</summary>
interface IExtensionChannel : IAsyncDisposable
{
    Task<JsonElement> CallAsync(string method, JsonNode? parameters, CancellationToken cancel, Action<JsonElement>? onProgress = null, TimeSpan? timeout = null);
    Task NotifyAsync(string method, JsonNode? parameters);
    bool Alive { get; }
    /// <summary>Dejó de correr sin que se le pidiera, con el motivo.</summary>
    event Action<string>? Stopped;
}

/// <summary>
/// Un programa del sistema (python, node, codex…) por su nombre, como lo encontraría la consola: en el
/// PATH y, en Windows, con las extensiones de PATHEXT. Null si no está.
/// </summary>
static class ProgramResolver
{
    public static string? Find(string program)
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!OperatingSystem.IsWindows()) dirs.AddRange(["/opt/homebrew/bin", "/usr/local/bin"]);
        var names = OperatingSystem.IsWindows() && !Path.HasExtension(program)
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(e => program + e.ToLowerInvariant())
            : [program];
        foreach (var dir in dirs)
            foreach (var name in names)
            {
                try
                {
                    var path = Path.Combine(dir.Trim('"'), name);
                    if (File.Exists(path)) return path;
                }
                catch (ArgumentException) { }
            }
        return null;
    }
}

/// <summary>
/// En Windows, los procesos de una extensión van en un Job que los cierra a todos si Claupet termina (aunque
/// sea de golpe): un programa de una extensión nunca queda corriendo solo. En los demás sistemas se cierra
/// el árbol al detenerla.
/// </summary>
sealed class KillOnCloseJob : IDisposable
{
    IntPtr _handle;

    public KillOnCloseJob()
    {
        if (!OperatingSystem.IsWindows()) return;
        _handle = CreateJobObject(IntPtr.Zero, null);
        if (_handle == IntPtr.Zero) return;
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION { BasicLimitInformation = new() { LimitFlags = 0x2000 } }; // KILL_ON_JOB_CLOSE
        var size = Marshal.SizeOf(info);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(_handle, 9, ptr, (uint)size)) { CloseHandle(_handle); _handle = IntPtr.Zero; } // ExtendedLimitInformation
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    public void Add(Process process)
    {
        if (_handle != IntPtr.Zero) AssignProcessToJobObject(_handle, process.Handle);
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        CloseHandle(_handle);
        _handle = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IO_COUNTERS { public ulong a, b, c, d, e, f; }

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
}

/// <summary>Lo que la extensión escribe en stderr, en datos\&lt;id&gt;\registro.log (los últimos 512 KB), para el diagnóstico.</summary>
sealed class ExtensionLog
{
    const long Max = 512 * 1024;
    readonly string _path;
    readonly object _gate = new();

    public ExtensionLog(string dataFolder) => _path = Path.Combine(dataFolder, "registro.log");

    public string FilePath => _path;

    public void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                if (File.Exists(_path) && new FileInfo(_path).Length > Max) File.Move(_path, _path + ".1", overwrite: true);
                File.AppendAllText(_path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
            }
            catch (Exception) { }
        }
    }

    /// <summary>Las últimas líneas, para mostrar por qué falló.</summary>
    public string Tail(int lines = 20)
    {
        lock (_gate)
        {
            try { return File.Exists(_path) ? string.Join(Environment.NewLine, File.ReadLines(_path).TakeLast(lines)) : ""; }
            catch (Exception) { return ""; }
        }
    }
}

/// <summary>
/// Una extensión que corre como un proceso y habla JSON-RPC por stdin/stdout: un programa del paquete,
/// uno del sistema (python main.py) o el anfitrión de una DLL (esta misma app con --anfitrion-extension).
/// Corre en la carpeta de sus datos, sin heredar las variables de Claude Code ni la clave de la API de
/// Anthropic, y en un Job que la cierra si Claupet termina.
/// </summary>
sealed class ExtensionProcess : IExtensionChannel
{
    static readonly string[] Scrubbed = ["ANTHROPIC_API_KEY", "CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_SSE_PORT", "CLAUDE_CONFIG_DIR", "CLAUPET_API_TOKEN"];

    readonly Process _process;
    readonly JsonRpcConnection _rpc;
    readonly KillOnCloseJob _job = new();
    readonly ExtensionLog _log;
    int _stopping;

    public event Action<string>? Stopped;

    ExtensionProcess(Process process, JsonRpcConnection rpc, ExtensionLog log)
    {
        _process = process;
        _rpc = rpc;
        _log = log;
    }

    public bool Alive => !_rpc.IsClosed && !HasExited;

    bool HasExited
    {
        get { try { return _process.HasExited; } catch (InvalidOperationException) { return true; } }
    }

    public int ProcessId => _process.Id;

    /// <summary>
    /// Lo arranca. file: el ejecutable (o un .cmd/.bat, por cmd.exe); args ya con {package} y {data}
    /// reemplazados. handler y onNotification atienden lo que la extensión le pide a Claupet.
    /// </summary>
    public static ExtensionProcess Start(string file, IEnumerable<string> args, string workingDirectory, ExtensionLog log,
        IReadOnlyDictionary<string, string> environment, JsonRpcConnection.RequestHandler handler, Action<string, JsonElement> onNotification)
    {
        Directory.CreateDirectory(workingDirectory);
        var start = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (OperatingSystem.IsWindows() && (file.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            start.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            foreach (var a in new[] { "/d", "/s", "/c", file }) start.ArgumentList.Add(a);
        }
        else start.FileName = file;
        foreach (var a in args) start.ArgumentList.Add(a);
        foreach (var name in Scrubbed) start.Environment.Remove(name);
        foreach (var (k, v) in environment) start.Environment[k] = v;
        if (SystemProxy.ForClaude() is { } proxy && !start.Environment.ContainsKey("HTTPS_PROXY")) start.Environment["HTTPS_PROXY"] = proxy;

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start()) throw new IOException(L.T("No se pudo arrancar la extensión."));
        var rpc = new JsonRpcConnection(process.StandardOutput.BaseStream, process.StandardInput.BaseStream)
        {
            Handler = handler,
            OnNotification = onNotification,
        };
        var running = new ExtensionProcess(process, rpc, log);
        running._job.Add(process);
        rpc.Closed += reason => running.OnClosed(reason);
        process.Exited += (_, _) => running.OnClosed(L.T("La extensión terminó (código {0}).", SafeExitCode(process)));
        _ = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardError.ReadLineAsync() is { } line)
                    log.Write(line.Length > 2000 ? line[..2000] + "…" : line);
            }
            catch (Exception) { }
        });
        rpc.Start();
        return running;
    }

    static string SafeExitCode(Process p)
    {
        try { return p.ExitCode.ToString(); } catch (InvalidOperationException) { return "?"; }
    }

    void OnClosed(string reason)
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
        _log.Write("[claupet] " + reason);
        Stopped?.Invoke(reason);
    }

    public Task<JsonElement> CallAsync(string method, JsonNode? parameters, CancellationToken cancel, Action<JsonElement>? onProgress = null, TimeSpan? timeout = null) =>
        _rpc.CallAsync(method, parameters, cancel, onProgress, timeout);

    public Task NotifyAsync(string method, JsonNode? parameters) => _rpc.NotifyAsync(method, parameters);

    /// <summary>Le pide terminar («shutdown» y «exit», como LSP); si no termina en unos segundos, la cierra.</summary>
    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _stopping, 1); // detenerla a propósito no es un fallo
        if (!_rpc.IsClosed && !HasExited)
        {
            try
            {
                await _rpc.CallAsync("shutdown", null, CancellationToken.None, timeout: TimeSpan.FromSeconds(3));
                await _rpc.NotifyAsync("exit");
            }
            catch (Exception) { }
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await _process.WaitForExitAsync(wait.Token); } catch (OperationCanceledException) { }
        }
        try { if (!HasExited) _process.Kill(entireProcessTree: true); } catch (Exception) { }
        await _rpc.DisposeAsync();
        _job.Dispose();
        _process.Dispose();
    }
}
