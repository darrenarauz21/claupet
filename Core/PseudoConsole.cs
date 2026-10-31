using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Claupet;

/// <summary>
/// Una consola de mentira de Windows (ConPTY): el programa cree que corre en una consola de verdad (colores, PSReadLine,
/// programas que preguntan) y lo que pinta llega como texto con secuencias VT por Output. Lo que se escribe en Write le
/// llega como si se tecleara. Solo en Windows 10 1809 o más nuevo; en Linux y macOS (forkpty) todavía no.
/// </summary>
sealed class PseudoConsole : IPty
{
    IntPtr _console;
    readonly SafeFileHandle _process;
    readonly FileStream _input;
    readonly object _write = new();
    bool _disposed;

    /// <summary>Se puede abrir en este sistema.</summary>
    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);

    /// <summary>Lo que pinta el programa: texto UTF-8 con secuencias VT. Se termina cuando se cierra la consola.</summary>
    public Stream Output { get; }
    public int ProcessId { get; }
    /// <summary>Termina con el código de salida del programa.</summary>
    public Task<int> Exited { get; }

    PseudoConsole(IntPtr console, SafeFileHandle process, int pid, FileStream input, FileStream output)
    {
        _console = console;
        _process = process;
        ProcessId = pid;
        _input = input;
        Output = output;
        Exited = WaitAsync(process);
    }

    /// <summary>Abre commandLine (ya con sus comillas) en una consola de cols × rows, en la carpeta cwd.</summary>
    public static PseudoConsole Start(string commandLine, string cwd, int cols, int rows, IDictionary<string, string>? env = null)
    {
        if (!IsSupported) throw new PlatformNotSupportedException();
        // Dos tuberías: la entrada (escribimos nosotros, lee la consola) y la salida (escribe la consola, leemos nosotros).
        if (!CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0)) throw new Win32Exception();
        if (!CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0)) throw new Win32Exception();
        var hr = CreatePseudoConsole(new Coord(cols, rows), inputRead, outputWrite, 0, out var console);
        // La consola ya tiene sus copias: las de este lado no hacen falta.
        inputRead.Dispose();
        outputWrite.Dispose();
        if (hr != 0)
        {
            inputWrite.Dispose();
            outputRead.Dispose();
            throw new Win32Exception(hr);
        }

        var attributes = IntPtr.Zero;
        var environment = IntPtr.Zero;
        try
        {
            var size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attributes = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) throw new Win32Exception();
            if (!UpdateProcThreadAttribute(attributes, 0, PseudoConsoleAttribute, console, IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception();

            var info = new StartupInfoEx { Attributes = attributes };
            info.Info.Size = Marshal.SizeOf<StartupInfoEx>();
            // Sin esto, si la app tiene la salida redirigida (las pruebas), el programa la heredaría en vez de la consola.
            info.Info.Flags = UseStdHandles;
            environment = Marshal.StringToHGlobalUni(Block(env));
            // «No hacer caso de Ctrl+C» se hereda: si a la app la abrió un proceso que lo tenía (una consola, otro
            // programa), el shell y sus comandos tampoco atendían Ctrl+C e «Interrumpir» no cortaba nada.
            SetConsoleCtrlHandler(IntPtr.Zero, false);
            if (!CreateProcessW(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false,
                    ExtendedStartupInfoPresent | UnicodeEnvironment, environment, cwd, ref info, out var created))
                throw new Win32Exception();
            CloseHandle(created.Thread);
            var input = new FileStream(inputWrite, FileAccess.Write, 1);
            var output = new FileStream(outputRead, FileAccess.Read, 1);
            return new PseudoConsole(console, new SafeFileHandle(created.Process, true), created.ProcessId, input, output);
        }
        catch (Exception)
        {
            ClosePseudoConsole(console);
            inputWrite.Dispose();
            outputRead.Dispose();
            throw;
        }
        finally
        {
            if (attributes != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributes);
                Marshal.FreeHGlobal(attributes);
            }
            if (environment != IntPtr.Zero) Marshal.FreeHGlobal(environment);
        }
    }

    /// <summary>Lo que se teclea (Intro es «\r»; Ctrl+C, «\x03»).</summary>
    public void Write(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        lock (_write)
        {
            if (_disposed) return;
            try
            {
                _input.Write(bytes);
                _input.Flush();
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }

    public void Resize(int cols, int rows)
    {
        if (_console != IntPtr.Zero && cols > 0 && rows > 0) ResizePseudoConsole(_console, new Coord(cols, rows));
    }

    /// <summary>Cierra el programa y lo que abrió, la consola y las tuberías.</summary>
    public void Dispose()
    {
        lock (_write)
        {
            if (_disposed) return;
            _disposed = true;
        }
        // Si ya terminó, su número pudo pasar a otro proceso: solo se cierra mientras corre.
        if (!Exited.IsCompleted)
            try
            {
                using var process = Process.GetProcessById(ProcessId);
                process.Kill(entireProcessTree: true);
            }
            catch (Exception) { }
        _input.Dispose();
        // ClosePseudoConsole espera a que se lea lo último que pintó (en Windows 10): se cierra aparte, sin trabar la vista.
        var console = _console;
        _console = IntPtr.Zero;
        _ = Task.Run(() =>
        {
            ClosePseudoConsole(console);
            Output.Dispose();
        });
        _ = Exited.ContinueWith(_ => _process.Dispose(), TaskScheduler.Default);
    }

    /// <summary>El código de salida, cuando el proceso termina (sin un hilo esperando).</summary>
    static Task<int> WaitAsync(SafeFileHandle process)
    {
        var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = new ManualResetEvent(false) { SafeWaitHandle = new SafeWaitHandle(process.DangerousGetHandle(), false) };
        RegisteredWaitHandle? registered = null;
        registered = ThreadPool.RegisterWaitForSingleObject(wait, (_, _) =>
        {
            var code = GetExitCodeProcess(process, out var exit) ? (int)exit : -1;
            registered?.Unregister(null);
            wait.Dispose();
            done.TrySetResult(code);
        }, null, Timeout.Infinite, executeOnlyOnce: true);
        return done.Task;
    }

    /// <summary>Las variables del proceso: las de la app con las de env encima, en el formato de CreateProcess.</summary>
    static string Block(IDictionary<string, string>? env)
    {
        var all = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            all[(string)entry.Key] = (string?)entry.Value ?? "";
        if (env is not null)
            foreach (var (key, value) in env) all[key] = value;
        var block = new StringBuilder();
        foreach (var (key, value) in all) block.Append(key).Append('=').Append(value).Append('\0');
        return block.Append('\0').ToString();
    }

    // ── Windows ──────────────────────────────────────────────────────────────────────────────

    const int ExtendedStartupInfoPresent = 0x00080000, UnicodeEnvironment = 0x00000400, UseStdHandles = 0x00000100;
    static readonly IntPtr PseudoConsoleAttribute = 0x00020016;

    sealed class Win32Exception : System.ComponentModel.Win32Exception
    {
        public Win32Exception() : base(Marshal.GetLastWin32Error()) { }
        public Win32Exception(int hr) : base(hr) { }
    }

    [StructLayout(LayoutKind.Sequential)]
    readonly struct Coord(int x, int y)
    {
        public readonly short X = (short)Math.Clamp(x, 1, short.MaxValue), Y = (short)Math.Clamp(y, 1, short.MaxValue);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo
    {
        public int Size;
        public string? Reserved, Desktop, Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, Reserved2;
        public IntPtr Reserved2Pointer, StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct StartupInfoEx
    {
        public StartupInfo Info;
        public IntPtr Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr attributes, int size);

    [DllImport("kernel32.dll")]
    static extern int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr console);

    [DllImport("kernel32.dll")]
    static extern int ResizePseudoConsole(IntPtr console, Coord size);

    [DllImport("kernel32.dll")]
    static extern void ClosePseudoConsole(IntPtr console);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnSize);

    [DllImport("kernel32.dll")]
    static extern void DeleteProcThreadAttributeList(IntPtr list);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessW(string? application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, int flags, IntPtr environment, string? cwd, ref StartupInfoEx startup, out ProcessInformation info);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);

    [DllImport("kernel32.dll")]
    static extern bool GetExitCodeProcess(SafeFileHandle process, out uint code);
}
