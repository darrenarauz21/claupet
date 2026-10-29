using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Claupet;

/// <summary>
/// Trae al frente la ventana donde corre una sesión de Claude Code: sube por los procesos padres del
/// de claude hasta el primero que tiene ventana (Windows Terminal, PowerShell, VS Code…). Si la sesión
/// es una pestaña, aparece la ventana, no siempre esa pestaña. En Linux lo hace xdotool (X11), si está
/// instalado; en macOS, System Events (la primera vez, macOS pide permiso para controlarlo).
/// </summary>
static class Terminals
{
    const int MaxDepth = 8;

    public static bool Focus(int pid)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return FocusWindows(pid);
            if (OperatingSystem.IsMacOS()) return Ancestors(pid).Any(FocusMac);
            if (OperatingSystem.IsLinux()) return Ancestors(pid).Any(FocusLinux);
        }
        catch (Exception) { }
        return false;
    }

    static bool FocusWindows(int pid)
    {
        var current = pid;
        for (var depth = 0; depth < MaxDepth && current > 4; depth++)
        {
            using var process = Process.GetProcessById(current);
            var window = process.MainWindowHandle;
            if (window != IntPtr.Zero)
            {
                if (IsIconic(window)) ShowWindow(window, 9); // SW_RESTORE
                SetForegroundWindow(window);
                return true;
            }
            current = ParentOf(process.Handle);
        }
        return false;
    }

    /// <summary>El proceso y sus padres (Linux y macOS), hasta el de arranque.</summary>
    static IEnumerable<int> Ancestors(int pid)
    {
        for (var depth = 0; depth < MaxDepth && pid > 1; depth++)
        {
            yield return pid;
            pid = UnixParent(pid);
        }
    }

    static int UnixParent(int pid)
    {
        // Linux: el cuarto campo de /proc/<pid>/stat, después del nombre entre paréntesis (que puede tener espacios).
        if (OperatingSystem.IsLinux() && File.Exists($"/proc/{pid}/stat"))
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            return fields.Length > 1 && int.TryParse(fields[1], out var parent) ? parent : 0;
        }
        return Run("ps", "-o", "ppid=", "-p", pid.ToString()) is (0, var output) && int.TryParse(output.Trim(), out var ppid) ? ppid : 0;
    }

    /// <summary>Linux: la primera ventana de ese proceso, con xdotool (no hay en Wayland sin XWayland).</summary>
    static bool FocusLinux(int pid) =>
        Run("xdotool", "search", "--pid", pid.ToString()) is (0, var windows)
        && windows.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() is { } window
        && Run("xdotool", "windowactivate", window.Trim()).Exit == 0;

    /// <summary>macOS: si ese proceso es una app (Terminal, iTerm2, VS Code…), pasa al frente.</summary>
    static bool FocusMac(int pid) =>
        Run("osascript", "-e", $"tell application \"System Events\" to set frontmost of (first process whose unix id is {pid}) to true").Exit == 0;

    /// <summary>Corre un programa y devuelve su código de salida y lo que escribió; -1 si no está o tardó más de 3 s.</summary>
    static (int Exit, string Output) Run(string program, params string[] args)
    {
        try
        {
            var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            if (process is null) return (-1, "");
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(3000))
            {
                try { process.Kill(); } catch (Exception) { }
                return (-1, "");
            }
            return (process.ExitCode, output.Result);
        }
        catch (Exception) { return (-1, ""); }
    }

    static int ParentOf(IntPtr handle)
    {
        var info = new ProcessBasicInformation();
        return NtQueryInformationProcess(handle, 0, ref info, Marshal.SizeOf(info), out _) == 0
            ? (int)info.InheritedFromUniqueProcessId : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessBasicInformation
    {
        public IntPtr ExitStatus, PebBaseAddress, AffinityMask, BasePriority, UniqueProcessId, InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    static extern int NtQueryInformationProcess(IntPtr process, int infoClass, ref ProcessBasicInformation info, int size, out int returned);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
}
