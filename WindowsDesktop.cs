using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Claupet;

/// <summary>
/// Las acciones de la mascota en Windows (SystemActions entiende el pedido; aquí se hace): el registro, Core Audio,
/// las teclas multimedia, el menú Inicio, las ventanas, WMI para el brillo, el Programador de tareas para los
/// recordatorios y el portapapeles de WinForms.
/// </summary>
sealed class WindowsDesktop : IDesktop
{
    static readonly HashSet<string> AllKinds = [.. SystemActions.All];

    public IReadOnlySet<string> Kinds => AllKinds;

    /// <summary>El portapapeles pide un hilo STA: el de la app lo es; desde --accion, después de un await, quizá no.</summary>
    internal static T Sta<T>(Func<T> work)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) return work();
        T result = default!;
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
        return result;
    }

    // ── Fondo y modo claro u oscuro ─────────────────────────────────────────────────────────

    public string? SetWallpaper(string file)
    {
        const int SetDeskWallpaper = 0x0014, UpdateIniFile = 0x01, SendChange = 0x02;
        return SystemParametersInfo(SetDeskWallpaper, 0, file, UpdateIniFile | SendChange) ? null : L.T("Windows no aceptó esa imagen.");
    }

    public string? SetDarkMode(bool dark)
    {
        var light = dark ? 0 : 1;
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
        {
            key.SetValue("AppsUseLightTheme", light, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", light, RegistryValueKind.DWord);
        }
        // Así se enteran las ventanas abiertas, la barra de tareas incluida.
        const int WmSettingChange = 0x001A, AbortIfHung = 0x0002;
        SendMessageTimeout(new IntPtr(0xFFFF), WmSettingChange, IntPtr.Zero, "ImmersiveColorSet", AbortIfHung, 1000, out _);
        return null;
    }

    // ── Volumen y música ────────────────────────────────────────────────────────────────────

    /// <summary>En la salida de audio predeterminada.</summary>
    public string? Volume(VolumeChange change)
    {
        var endpoint = DefaultEndpoint();
        try
        {
            var context = Guid.Empty;
            if (change.Mute is { } mute)
            {
                endpoint.SetMute(mute, ref context);
                return null;
            }
            endpoint.GetMasterVolumeLevelScalar(out var level);
            var target = change.Percent is { } percent ? percent / 100f : Math.Clamp(level + change.Step / 100f, 0f, 1f);
            endpoint.SetMasterVolumeLevelScalar(target, ref context);
            if (target > 0) endpoint.SetMute(false, ref context);
            return null;
        }
        finally { Marshal.ReleaseComObject(endpoint); }
    }

    static IAudioEndpointVolume DefaultEndpoint()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            enumerator.GetDefaultAudioEndpoint(0, 1, out var device); // eRender, eMultimedia
            try
            {
                var iid = typeof(IAudioEndpointVolume).GUID;
                device.Activate(ref iid, 0x17, IntPtr.Zero, out var endpoint); // CLSCTX_ALL
                return (IAudioEndpointVolume)endpoint;
            }
            finally { Marshal.ReleaseComObject(device); }
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    /// <summary>Con las teclas multimedia: lo que suene en Windows (Spotify, YouTube en el navegador…).</summary>
    public async Task<string?> MediaAsync(MediaCommand command)
    {
        const byte Next = 0xB0, Previous = 0xB1, PlayPause = 0xB3;
        switch (command)
        {
            case MediaCommand.Next: Tap(Next); break;
            case MediaCommand.Previous: Tap(Previous); break;
            default:
                // La tecla solo alterna: se mira antes si algo suena, para no reanudar al pedir una pausa.
                var playing = await Task.Run(Music.PlayingNow);
                if (playing != (command == MediaCommand.Play)) Tap(PlayPause);
                break;
        }
        return null;
    }

    static void Tap(byte key)
    {
        const int Extended = 0x1, KeyUp = 0x2;
        keybd_event(key, 0, Extended, IntPtr.Zero);
        keybd_event(key, 0, Extended | KeyUp, IntPtr.Zero);
    }

    // ── Abrir ───────────────────────────────────────────────────────────────────────────────

    public IReadOnlyList<string> Schemes { get; } = ["https", "http", "spotify", "ms-settings"];

    public void Start(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();

    /// <summary>El acceso directo del menú Inicio con ese nombre: primero el exacto, después el que empieza así y por último el que lo contiene.</summary>
    public string? FindApp(string name)
    {
        var shortcuts = new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms }
            .Select(Environment.GetFolderPath)
            .Where(Directory.Exists)
            .SelectMany(dir =>
            {
                try { return Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories); }
                catch (Exception) { return []; }
            })
            .Select(path => (Path: path, Name: Path.GetFileNameWithoutExtension(path)))
            .Where(s => !s.Name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) && !s.Name.Contains("desinstalar", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        return shortcuts.FirstOrDefault(s => SystemActions.Same(s.Name, name)).Path
            ?? shortcuts.Where(s => compare.IsPrefix(s.Name, name.Trim(), Loose)).OrderBy(s => s.Name.Length).FirstOrDefault().Path
            ?? shortcuts.Where(s => compare.IndexOf(s.Name, name.Trim(), Loose) >= 0).OrderBy(s => s.Name.Length).FirstOrDefault().Path;
    }

    // ── Calendario, captura y recordatorios ─────────────────────────────────────────────────

    /// <summary>
    /// Se abre con la app de calendario de Windows de la persona: ella lo guarda. Si ninguna abre los .ics, Windows
    /// le muestra «Abrir con» para que elija una (y quede para la próxima).
    /// </summary>
    public string? OpenCalendarFile(string file)
    {
        try { Start(file); }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1155) // ERROR_NO_ASSOCIATION
        {
            const int AllowRegistration = 0x1, RegisterExtension = 0x2, Execute = 0x4;
            var info = new OpenAsInfo { File = file, Flags = AllowRegistration | RegisterExtension | Execute };
            if (SHOpenWithDialog(IntPtr.Zero, ref info) != 0)
                return L.T("Para agendar, elija una app de calendario que abra los eventos (.ics): Windows se lo pregunta al abrir uno.");
        }
        return null;
    }

    public Task<(string? File, string? Problem)> CaptureAsync(bool crop) => Task.FromResult<(string?, string?)>((ScreenCapture.Run(crop), null));

    public string ScreenshotsFolder => ScreenCapture.Folder;

    public void ScheduleReminder(Reminder reminder) => ReminderTasks.Create(reminder);

    public void CancelReminder(string id) => ReminderTasks.Delete(id);

    // ── Ventanas ────────────────────────────────────────────────────────────────────────────

    public string? ShowDesktop()
    {
        var type = Type.GetTypeFromProgID("Shell.Application") ?? throw new InvalidOperationException("Shell.Application");
        var shell = Activator.CreateInstance(type)!;
        try { type.InvokeMember("MinimizeAll", BindingFlags.InvokeMethod, null, shell, null); }
        finally { Marshal.ReleaseComObject(shell); }
        return null;
    }

    public string? Window(WindowVerb verb, string app)
    {
        if (FindWindowOf(app) is not { } window) return L.T("No encontré una ventana de «{0}».", app);
        const int WmClose = 0x0010, Minimize = 6, Maximize = 3, Restore = 9;
        switch (verb)
        {
            case WindowVerb.Close: PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero); break; // como la X: la app puede preguntar si guardar
            case WindowVerb.Minimize: ShowWindow(window, Minimize); break;
            case WindowVerb.Maximize: ShowWindow(window, Maximize); break;
            default:
                if (IsIconic(window)) ShowWindow(window, Restore);
                Foreground.Take(window);
                break;
        }
        return null;
    }

    /// <summary>
    /// La ventana de esa app más al frente (EnumWindows va de arriba hacia abajo): primero la que se llama
    /// así (proceso, descripción del programa o título), después la que lo contiene en el nombre del programa
    /// y por último en el título. Nunca las de Claupet ni las del escritorio y la barra de tareas.
    /// </summary>
    static IntPtr? FindWindowOf(string name)
    {
        var windows = new List<(IntPtr Handle, string Title, string Process, string Description)>();
        var programs = new Dictionary<uint, (string Process, string Description)>();
        var own = (uint)Environment.ProcessId;
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || GetWindow(handle, 4) != IntPtr.Zero) return true; // GW_OWNER: solo las principales
            if ((GetWindowLong(handle, -20) & 0x80) != 0) return true;                       // WS_EX_TOOLWINDOW
            if (DwmGetWindowAttribute(handle, 14, out int cloaked, 4) == 0 && cloaked != 0) return true; // DWMWA_CLOAKED
            var length = GetWindowTextLength(handle);
            if (length == 0) return true;
            var className = new StringBuilder(64);
            GetClassName(handle, className, className.Capacity);
            if (className.ToString() is "Progman" or "Shell_TrayWnd" or "WorkerW" or "Shell_SecondaryTrayWnd") return true;
            GetWindowThreadProcessId(handle, out var pid);
            if (pid == own) return true;
            var title = new StringBuilder(length + 1);
            GetWindowText(handle, title, title.Capacity);
            if (!programs.TryGetValue(pid, out var program)) programs[pid] = program = ProgramOf(pid);
            windows.Add((handle, title.ToString(), program.Process, program.Description));
            return true;
        }, IntPtr.Zero);

        var compare = CultureInfo.InvariantCulture.CompareInfo;
        const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        bool Has(string text) => text.Length > 0 && compare.IndexOf(text, name, Loose) >= 0;
        var found = windows.FirstOrDefault(w => SystemActions.Same(w.Process, name) || SystemActions.Same(w.Description, name) || SystemActions.Same(w.Title, name)).Handle;
        if (found == IntPtr.Zero) found = windows.FirstOrDefault(w => Has(w.Process) || Has(w.Description)).Handle;
        if (found == IntPtr.Zero) found = windows.FirstOrDefault(w => Has(w.Title)).Handle;
        return found == IntPtr.Zero ? null : found;
    }

    /// <summary>El nombre del ejecutable y la descripción del programa («Microsoft Word»), si se pueden leer.</summary>
    static (string Process, string Description) ProgramOf(uint pid)
    {
        var process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == IntPtr.Zero) return ("", "");
        try
        {
            var path = new StringBuilder(1024);
            var size = path.Capacity;
            if (!QueryFullProcessImageName(process, 0, path, ref size)) return ("", "");
            var file = path.ToString();
            string description;
            try { description = FileVersionInfo.GetVersionInfo(file).FileDescription ?? ""; }
            catch (Exception) { description = ""; }
            return (Path.GetFileNameWithoutExtension(file), description);
        }
        finally { CloseHandle(process); }
    }

    // ── El equipo y el brillo ───────────────────────────────────────────────────────────────

    public string? Computer(ComputerCommand command)
    {
        switch (command)
        {
            case ComputerCommand.Lock: return LockWorkStation() ? null : L.T("Windows no dejó bloquear el equipo.");
            case ComputerCommand.Sleep: SetSuspendState(false, false, false); break;
            case ComputerCommand.Restart: Shutdown("/r"); break;
            default: Shutdown("/s"); break;
        }
        return null;
    }

    static void Shutdown(string option) =>
        Process.Start(new ProcessStartInfo("shutdown.exe") { ArgumentList = { option, "/t", "0" }, CreateNoWindow = true, UseShellExecute = false })?.Dispose();

    /// <summary>
    /// Por WMI (WmiMonitorBrightnessMethods), que solo llega a la pantalla del portátil: los monitores externos se
    /// ajustan con sus botones.
    /// </summary>
    public string? Brightness(int? percent, int step)
    {
        var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
        if (type is null) return L.T("No pude hablar con Windows para cambiar el brillo.");
        dynamic locator = Activator.CreateInstance(type)!;
        dynamic wmi = locator.ConnectServer(".", @"root\wmi");
        dynamic levels = wmi.ExecQuery("SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE");
        if ((int)levels.Count == 0) return L.T("Esta pantalla no deja cambiar el brillo desde Windows: un monitor externo se ajusta con sus botones.");
        // Solo con los miembros fijos de SWbemObject (Properties_, Methods_, ExecMethod_). Los de la clase de WMI
        // (CurrentBrightness, WmiSetBrightness) tienen un DISPID propio de cada objeto, y dynamic guarda el del
        // primero que vio: desde el segundo pedido en la misma app fallaban con «Error no especificado».
        int current = Convert.ToInt32(levels.ItemIndex(0).Properties_.Item("CurrentBrightness").Value);
        var target = percent ?? Math.Clamp(current + step, 0, 100);
        dynamic methods = wmi.ExecQuery("SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE");
        for (var i = 0; i < (int)methods.Count; i++)
        {
            dynamic monitor = methods.ItemIndex(i);
            dynamic input = monitor.Methods_.Item("WmiSetBrightness").InParameters.SpawnInstance_();
            input.Properties_.Item("Timeout").Value = 1;
            input.Properties_.Item("Brightness").Value = (byte)target;
            monitor.ExecMethod_("WmiSetBrightness", input);
        }
        return null;
    }

    // ── Batería, disco y memoria ────────────────────────────────────────────────────────────

    public string Battery()
    {
        if (!GetSystemPowerStatus(out var power) || power.BatteryFlag == 128) return L.T("Batería: este equipo no tiene.");
        var percent = power.BatteryLifePercent == 255 ? L.T("desconocida") : $"{power.BatteryLifePercent} %";
        var state = power.ACLineStatus == 1 ? L.T("enchufado") + ((power.BatteryFlag & 8) != 0 ? ", " + L.T("cargando") : "")
            : power.BatteryLifeTime > 0 ? L.T("sin enchufar, le quedan unas {0}", Format.Duration(TimeSpan.FromSeconds(power.BatteryLifeTime)))
            : L.T("sin enchufar");
        return L.T("Batería: {0} ({1}).", percent, state);
    }

    public IEnumerable<DriveInfo> Disks() => DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady);

    public string Memory()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory)) return L.T("Memoria: no se pudo leer.");
        return L.T("Memoria: {0} libres de {1} ({2} % en uso).",
            SystemActions.Bytes((long)memory.Available), SystemActions.Bytes((long)memory.Total), memory.Load);
    }

    // ── Portapapeles y carpetas ─────────────────────────────────────────────────────────────

    public Task<string?> CopyTextAsync(string text)
    {
        Sta(() => { Clipboard.SetText(text); return true; });
        return Task.FromResult<string?>(null);
    }

    public Task<(string? Text, string? ImageFile)> ReadClipboardAsync()
    {
        var (text, image) = Sta(() => (Clipboard.ContainsText() ? Clipboard.GetText() : null, Clipboard.ContainsImage() ? Clipboard.GetImage() : null));
        if (text is { Length: > 0 })
        {
            image?.Dispose();
            return Task.FromResult<(string?, string?)>((text, null));
        }
        if (image is null) return Task.FromResult<(string?, string?)>((null, null));
        using (image) return Task.FromResult<(string?, string?)>((null, ScreenCapture.ForLook(image)));
    }

    public string? Folder(KnownFolder folder) => folder switch
    {
        KnownFolder.Downloads => KnownFolders.Get(KnownFolders.Downloads),
        KnownFolder.Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        KnownFolder.Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        KnownFolder.Pictures => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        KnownFolder.Music => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        _ => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
    };

    public void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false })?.Dispose();

    // ── Windows ─────────────────────────────────────────────────────────────────────────────

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SystemParametersInfo(int action, int param, string value, int winIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessageTimeout(IntPtr hwnd, int msg, IntPtr wParam, string lParam, int flags, int timeout, out IntPtr result);

    [DllImport("user32.dll")]
    static extern void keybd_event(byte vk, byte scan, int flags, IntPtr extra);

    delegate bool EnumWindowsProc(IntPtr handle, IntPtr param);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr handle, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextLength(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr handle, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr handle, StringBuilder name, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr handle, out uint pid);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr handle, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool LockWorkStation();
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int size);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int size);
    [DllImport("powrprof.dll")] static extern bool SetSuspendState(bool hibernate, bool force, bool disableWake);
    [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out PowerStatus status);
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHOpenWithDialog(IntPtr parent, ref OpenAsInfo info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OpenAsInfo
    {
        public string File;
        public string? Class;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatus
    {
        public uint Length, Load;
        public ulong Total, Available, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    // Core Audio: la salida predeterminada y su volumen. Solo se declaran los métodos hasta el último que se usa, en su orden.

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

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr notify);
        void UnregisterControlChangeNotify(IntPtr notify);
        void GetChannelCount(out uint count);
        void SetMasterVolumeLevel(float level, ref Guid context);
        void SetMasterVolumeLevelScalar(float level, ref Guid context);
        void GetMasterVolumeLevel(out float level);
        void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        void GetChannelVolumeLevel(uint channel, out float level);
        void GetChannelVolumeLevelScalar(uint channel, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
    }
}

/// <summary>Las carpetas de Windows que no están en Environment.SpecialFolder (Descargas, Capturas de pantalla).</summary>
static class KnownFolders
{
    public static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");
    public static readonly Guid Screenshots = new("B7BEDE81-DF94-4682-A7D8-57A52620B86F");

    public static string? Get(Guid id)
    {
        if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var path) != 0) return null;
        try { return Marshal.PtrToStringUni(path); }
        finally { Marshal.FreeCoTaskMem(path); }
    }

    [DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
}
