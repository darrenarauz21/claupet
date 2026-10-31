using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Claupet;

/// <summary>Un proceso visto desde la Terminal: su id, el de su padre y su nombre.</summary>
sealed record ProcInfo(int Pid, int ParentPid, string Name);

/// <summary>
/// Los procesos que abrió cada sesión de la Terminal (un servidor que dejó corriendo un agente, sus MCP…): los hijos y
/// nietos del proceso de la sesión. En Windows se leen con una instantánea de Toolhelp; en Linux, de /proc; en macOS
/// todavía no (el panel muestra solo el agente).
/// </summary>
static class ProcessTree
{
    public static IReadOnlyList<ProcInfo> Snapshot()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return WindowsSnapshot();
            if (OperatingSystem.IsLinux()) return LinuxSnapshot();
        }
        catch (Exception) { }
        return [];
    }

    /// <summary>Los descendientes de root, en orden (primero los hijos), sin repetir y sin el propio root.</summary>
    public static IReadOnlyList<ProcInfo> Descendants(IReadOnlyList<ProcInfo> all, int root)
    {
        var children = all.Where(p => p.Pid != p.ParentPid).ToLookup(p => p.ParentPid);
        var found = new List<ProcInfo>();
        var seen = new HashSet<int> { root };
        var queue = new Queue<int>([root]);
        while (queue.Count > 0)
            foreach (var child in children[queue.Dequeue()])
                if (seen.Add(child.Pid))
                {
                    found.Add(child);
                    queue.Enqueue(child.Pid);
                }
        return found;
    }

    /// <summary>La memoria en uso de un proceso (working set), o null si ya no está.</summary>
    public static long? Memory(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited ? null : process.WorkingSet64;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Detiene el proceso y lo que abrió.</summary>
    public static void Stop(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception) { }
    }

    static List<ProcInfo> LinuxSnapshot()
    {
        var list = new List<ProcInfo>();
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out var pid)) continue;
            try
            {
                // /proc/<pid>/stat: «pid (nombre) estado ppid …»; el nombre puede tener espacios y paréntesis.
                var stat = File.ReadAllText(Path.Combine(dir, "stat"));
                var close = stat.LastIndexOf(')');
                var name = stat[(stat.IndexOf('(') + 1)..close];
                var rest = stat[(close + 2)..].Split(' ');
                list.Add(new ProcInfo(pid, int.Parse(rest[1]), name));
            }
            catch (Exception) { }
        }
        return list;
    }

    // ── Windows ─────────────────────────────────────────────────────────────────────────────

    const uint TH32CS_SNAPPROCESS = 0x2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);

    static List<ProcInfo> WindowsSnapshot()
    {
        var list = new List<ProcInfo>();
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return list;
        try
        {
            var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
            for (var ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
                list.Add(new ProcInfo((int)entry.th32ProcessID, (int)entry.th32ParentProcessID, Path.GetFileNameWithoutExtension(entry.szExeFile)));
        }
        finally { CloseHandle(snapshot); }
        return list;
    }
}
