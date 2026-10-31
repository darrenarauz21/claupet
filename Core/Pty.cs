namespace Claupet;

/// <summary>
/// La consola de mentira de un shell de la Terminal: el programa cree que corre en una terminal de verdad y lo que pinta
/// llega como texto con secuencias VT. En Windows es ConPTY (PseudoConsole); en Linux y macOS, una pty (UnixPty).
/// </summary>
interface IPty : IDisposable
{
    /// <summary>Lo que pinta el programa: texto UTF-8 con secuencias VT. Se termina cuando se cierra la consola.</summary>
    Stream Output { get; }
    int ProcessId { get; }
    /// <summary>Termina con el código de salida del programa.</summary>
    Task<int> Exited { get; }
    /// <summary>Lo que se teclea (Intro es «\r»; Ctrl+C, «\x03»).</summary>
    void Write(string text);
    void Resize(int cols, int rows);
}

/// <summary>Cómo se abre un shell: el programa con sus argumentos (Linux y macOS), la línea entera (Windows) y sus variables.</summary>
sealed record ShellLaunch(string Program, IReadOnlyList<string> Args, string CommandLine, IReadOnlyDictionary<string, string> Env);

static class Pty
{
    /// <summary>Se puede abrir un shell en este sistema: Windows 10 1809 o más nuevo, Linux o macOS.</summary>
    public static bool IsSupported =>
        OperatingSystem.IsWindows() ? PseudoConsole.IsSupported : OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    public static IPty Start(ShellLaunch launch, string cwd, int cols, int rows) =>
        OperatingSystem.IsWindows()
            ? PseudoConsole.Start(launch.CommandLine, cwd, cols, rows, launch.Env.ToDictionary())
            : UnixPty.Start(launch, cwd, cols, rows);
}

/// <summary>
/// Una pty de Linux o macOS, con Porta.Pty (MIT): su ayudante nativo abre la pty y el shell (forkpty), así .NET no hace
/// fork. La pty es la terminal de control del shell: Ctrl+C («\x03») le llega como SIGINT al programa que corre.
/// </summary>
sealed class UnixPty : IPty
{
    readonly Porta.Pty.IPtyConnection _connection;
    readonly object _write = new();
    bool _disposed;

    UnixPty(Porta.Pty.IPtyConnection connection)
    {
        _connection = connection;
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ProcessExited += (_, e) => exited.TrySetResult(e.ExitCode);
        Exited = exited.Task;
    }

    public Stream Output => _connection.ReaderStream;
    public int ProcessId => _connection.Pid;
    public Task<int> Exited { get; }

    public static UnixPty Start(ShellLaunch launch, string cwd, int cols, int rows)
    {
        var options = new Porta.Pty.PtyOptions
        {
            Name = "Claupet",
            Cols = cols,
            Rows = rows,
            Cwd = cwd,
            App = launch.Program,
            CommandLine = [.. launch.Args],
            Environment = launch.Env.ToDictionary(),
        };
        // Se abre fuera del hilo de la vista (lo pide async; abrir un shell tarda milisegundos).
        var connection = Task.Run(() => Porta.Pty.PtyProvider.SpawnAsync(options, CancellationToken.None)).GetAwaiter().GetResult();
        return new UnixPty(connection);
    }

    public void Write(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        lock (_write)
        {
            if (_disposed) return;
            try
            {
                _connection.WriterStream.Write(bytes);
                _connection.WriterStream.Flush();
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }

    public void Resize(int cols, int rows)
    {
        lock (_write)
        {
            if (_disposed || cols <= 0 || rows <= 0) return;
            try { _connection.Resize(cols, rows); }
            catch (Exception) { }
        }
    }

    public void Dispose()
    {
        lock (_write)
        {
            if (_disposed) return;
            _disposed = true;
        }
        try
        {
            if (!Exited.IsCompleted) _connection.Kill();
        }
        catch (Exception) { }
        try { _connection.Dispose(); }
        catch (Exception) { }
    }
}
