using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ClaudeUsageTray;

/// <summary>
/// Cómo le fue a una acción. Problem: lo que falló, dicho en corto para la nubecita. Say: lo que se dice en
/// vez de la frase de Claude (cuando la app sabe algo que Claude no). Data: lo que la app averiguó para que
/// Claude responda la pregunta (la batería, las notas, lo copiado). Look: una imagen para que Claude la mire,
/// con Question.
/// </summary>
sealed record ActionOutcome(string? Problem = null, string? Say = null, string? Data = null, string? Look = null, string? Question = null);

/// <summary>
/// Lo que la mascota hace en el equipo cuando se lo piden (la respuesta de Claude empieza con
/// [ACCION]): una lista cerrada, para que una frase mal entendida no pueda hacer otra cosa. Lo que no
/// está aquí se le encarga a una sesión de Claude Code. Las de siempre (fondo, tema, color, volumen,
/// música y abrir) se hacen sin preguntar; las demás piden permiso por capacidad (Capabilities), y
/// reiniciar o apagar se confirman cada vez. El permiso lo pide TrayApp antes de llamar a RunAsync.
/// </summary>
static class SystemActions
{
    public static async Task<ActionOutcome> RunAsync(AssistantAction action, Action<string> setPalette)
    {
        action = Normalize(action);
        var value = action.Value;
        try
        {
            return action.Kind switch
            {
                "fondo" => Fail(await WallpaperAsync(value)),
                "tema" => Fail(Mode(value)),
                "color" => Fail(Palette(value, setPalette)),
                "volumen" => Fail(Volume(value)),
                "musica" or "música" => Fail(await MediaAsync(value)),
                "abrir" => Fail(Open(value)),
                "captura" => ScreenCapture.Run(value, action.Question),
                "agendar" => Fail(Schedule(value)),
                "nota" => Fail(Note(value)),
                "notas" => ReadNotes(value),
                "recordar" => Fail(Remind(value)),
                "recordatorios" => ListReminders(value),
                "ventana" => Fail(Window(value)),
                "equipo" => Fail(Computer(value)),
                "brillo" => Fail(Brightness(value)),
                "estado" => Status(value),
                "copiar" => Fail(Copy(value)),
                "portapapeles" => ReadClipboard(value, action.Question),
                "carpeta" => Fail(Folder(value)),
                _ => Fail(L.T("Eso todavía no lo sé hacer.")),
            };
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex); // el mensaje de Windows suele ser poco («Error no especificado»): el detalle queda en errores.log
            return Fail(L.T("No pude: {0}", ex.Message));
        }
    }

    /// <summary>
    /// Lo que Claude a veces pide con otro nombre: «escritorio» como acción es «ventana escritorio», y
    /// «abrir» una carpeta conocida (descargas, documentos…) es «carpeta».
    /// </summary>
    static AssistantAction Normalize(AssistantAction action) => action.Kind switch
    {
        "escritorio" => action with { Kind = "ventana", Value = "escritorio" },
        "abrir" when KnownFolder(action.Value.Trim().Trim('"')) is not null => action with { Kind = "carpeta" },
        _ => action,
    };

    /// <summary>La capacidad que pide permiso para esta acción; null, las de siempre (y las que no existen).</summary>
    public static string? CapabilityOf(AssistantAction action) => Normalize(action).Kind switch
    {
        "captura" => Capabilities.Capture,
        "agendar" => Capabilities.Calendar,
        "nota" or "notas" => Capabilities.Notes,
        "recordar" or "recordatorios" => Capabilities.Reminders,
        "ventana" => Capabilities.Windows,
        "equipo" => Capabilities.Computer,
        "brillo" => Capabilities.Brightness,
        "estado" => Capabilities.Status,
        "copiar" or "portapapeles" => Capabilities.Clipboard,
        "carpeta" => Capabilities.Folders,
        _ => null,
    };

    /// <summary>Lo que se confirma cada vez, aunque haya permiso (reiniciar y apagar): la pregunta y el botón de sí.</summary>
    public static (string Question, string Yes)? Confirmation(AssistantAction action)
    {
        action = Normalize(action);
        if (action.Kind != "equipo") return null;
        if (Same(action.Value, "reiniciar") || Same(action.Value, "restart"))
            return (L.T("¿Reinicio el equipo ahora? Lo que no esté guardado puede perderse."), L.T("Reiniciar ahora"));
        if (Same(action.Value, "apagar") || Same(action.Value, "shut down") || Same(action.Value, "shutdown"))
            return (L.T("¿Apago el equipo ahora? Lo que no esté guardado puede perderse."), L.T("Apagar ahora"));
        return null;
    }

    static ActionOutcome Fail(string? problem) => new(problem);

    internal static bool Same(string a, string b) =>
        string.Compare(a.Trim(), b.Trim(), CultureInfo.InvariantCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0;

    /// <summary>Si el valor es una de esas palabras (en español, o en inglés si Claude lo tradujo).</summary>
    static bool Is(string value, params string[] words) => words.Any(w => Same(value, w));

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

    // ── Fondo de pantalla ───────────────────────────────────────────────────────────────────

    /// <summary>Los fondos bajados de internet; quedan los últimos KeepWallpapers.</summary>
    static readonly string WallpaperDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeUsageTray", "fondos");

    const int KeepWallpapers = 5;
    const long MaxImage = 25 * 1024 * 1024;
    static readonly string[] ImageTypes = [".jpg", ".jpeg", ".png", ".bmp"];
    static readonly HttpClient Http = SystemProxy.Client(TimeSpan.FromSeconds(30));

    /// <summary>Una imagen del equipo o una URL https de una imagen (se baja a WallpaperDir).</summary>
    static async Task<string?> WallpaperAsync(string value)
    {
        string file;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            if (uri.Scheme != Uri.UriSchemeHttps) return L.T("Solo bajo imágenes de enlaces https.");
            var downloaded = await DownloadImageAsync(uri);
            if (downloaded is null) return L.T("Ese enlace no es una imagen que pueda poner de fondo.");
            file = downloaded;
        }
        else
        {
            file = Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim('"', ' ')));
            if (!File.Exists(file)) return L.T("No encontré esa imagen.");
            if (!ImageTypes.Contains(Path.GetExtension(file).ToLowerInvariant())) return L.T("Eso no es una imagen (.jpg, .png o .bmp).");
        }
        const int SetDeskWallpaper = 0x0014, UpdateIniFile = 0x01, SendChange = 0x02;
        return SystemParametersInfo(SetDeskWallpaper, 0, file, UpdateIniFile | SendChange) ? null : L.T("Windows no aceptó esa imagen.");
    }

    /// <summary>La baja si es JPEG, PNG o BMP de hasta MaxImage y se puede abrir como imagen; si no, null.</summary>
    static async Task<string?> DownloadImageAsync(Uri uri)
    {
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) return null;
        var extension = response.Content.Headers.ContentType?.MediaType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/bmp" => ".bmp",
            _ => null,
        };
        if (extension is null || response.Content.Headers.ContentLength > MaxImage) return null;

        Directory.CreateDirectory(WallpaperDir);
        var file = Path.Combine(WallpaperDir, $"fondo-{DateTime.Now:yyyyMMdd-HHmmss}{extension}");
        var ok = false;
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var target = File.Create(file))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    total += read;
                    if (total > MaxImage) return null;
                    await target.WriteAsync(buffer.AsMemory(0, read));
                }
            }
            using (Image.FromFile(file)) { } // que de verdad sea una imagen
            ok = true;
        }
        catch (Exception) { return null; }
        finally
        {
            if (!ok) try { File.Delete(file); } catch (Exception) { }
        }

        foreach (var old in new DirectoryInfo(WallpaperDir).GetFiles("fondo-*").OrderByDescending(f => f.Name).Skip(KeepWallpapers))
            try { old.Delete(); } catch (Exception) { }
        return file;
    }

    // ── Modo claro u oscuro y color de la app ───────────────────────────────────────────────

    static string? Mode(string value)
    {
        int light;
        // Los valores del protocolo van en español; en inglés, Claude a veces los traduce.
        if (Same(value, "claro") || Same(value, "light")) light = 1;
        else if (Same(value, "oscuro") || Same(value, "dark")) light = 0;
        else return L.T("Solo sé poner el modo claro o el oscuro.");
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

    static string? Palette(string value, Action<string> setPalette)
    {
        var palette = Theme.Palettes.FirstOrDefault(p => Same(p.Label, value) || Same(p.Id, value));
        if (palette.Id is null)
            return L.T("Ese color no lo tengo: hay {0}.", string.Join(", ", Theme.Palettes.Select(p => p.Label)));
        setPalette(palette.Id);
        return null;
    }

    // ── Volumen y música ────────────────────────────────────────────────────────────────────

    /// <summary>Un número de 0 a 100, subir o bajar (de a 10), silenciar o activar; en la salida de audio predeterminada.</summary>
    static string? Volume(string value)
    {
        var endpoint = DefaultEndpoint();
        try
        {
            var context = Guid.Empty;
            endpoint.GetMasterVolumeLevelScalar(out var level);
            float? target = null;
            if (int.TryParse(value.TrimEnd('%', ' '), out var percent)) target = Math.Clamp(percent, 0, 100) / 100f;
            else if (Same(value, "subir") || Same(value, "up")) target = Math.Min(1f, level + 0.1f);
            else if (Same(value, "bajar") || Same(value, "down")) target = Math.Max(0f, level - 0.1f);
            else if (Same(value, "silenciar") || Same(value, "mute")) endpoint.SetMute(true, ref context);
            else if (Same(value, "activar") || Same(value, "unmute")) endpoint.SetMute(false, ref context);
            else return L.T("No entendí cuánto volumen.");
            if (target is { } t)
            {
                endpoint.SetMasterVolumeLevelScalar(t, ref context);
                if (t > 0) endpoint.SetMute(false, ref context);
            }
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
    static async Task<string?> MediaAsync(string value)
    {
        const byte Next = 0xB0, Previous = 0xB1, PlayPause = 0xB3;
        if (Same(value, "siguiente") || Same(value, "next")) Tap(Next);
        else if (Same(value, "anterior") || Same(value, "previous")) Tap(Previous);
        else if (Same(value, "reproducir") || Same(value, "pausar") || Same(value, "play") || Same(value, "pause"))
        {
            // La tecla solo alterna: se mira antes si algo suena, para no reanudar al pedir una pausa.
            var play = Same(value, "reproducir") || Same(value, "play");
            var playing = await Task.Run(Music.PlayingNow);
            if (playing != play) Tap(PlayPause);
        }
        else return L.T("No entendí qué hacer con la música.");
        return null;
    }

    static void Tap(byte key)
    {
        const int Extended = 0x1, KeyUp = 0x2;
        keybd_event(key, 0, Extended, IntPtr.Zero);
        keybd_event(key, 0, Extended | KeyUp, IntPtr.Zero);
    }

    // ── Abrir ───────────────────────────────────────────────────────────────────────────────

    static readonly string[] Schemes = ["https", "http", "spotify", "ms-settings"];

    /// <summary>Una URL de los tipos de Schemes o una app del menú Inicio, por su nombre.</summary>
    static string? Open(string value)
    {
        value = value.Trim().Trim('"');
        if (value.Length == 0) return L.T("No me dijeron qué abrir.");
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            if (!Schemes.Contains(uri.Scheme)) return L.T("Ese tipo de enlace no lo abro.");
            Start(value);
            return null;
        }
        if (FindShortcut(value) is not { } shortcut) return L.T("No encontré «{0}» en el menú Inicio.", value);
        Start(shortcut);
        return null;
    }

    static void Start(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();

    /// <summary>El acceso directo del menú Inicio con ese nombre: primero el exacto, después el que empieza así y por último el que lo contiene.</summary>
    static string? FindShortcut(string name)
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
        return shortcuts.FirstOrDefault(s => Same(s.Name, name)).Path
            ?? shortcuts.Where(s => compare.IsPrefix(s.Name, name.Trim(), Loose)).OrderBy(s => s.Name.Length).FirstOrDefault().Path
            ?? shortcuts.Where(s => compare.IndexOf(s.Name, name.Trim(), Loose) >= 0).OrderBy(s => s.Name.Length).FirstOrDefault().Path;
    }

    // ── Calendario, notas y recordatorios ───────────────────────────────────────────────────

    /// <summary>
    /// El evento va a un .ics que se abre con la app de calendario de Windows de la persona: ella lo guarda.
    /// Si ninguna abre los .ics, Windows le muestra «Abrir con» para que elija una (y quede para la próxima).
    /// </summary>
    static string? Schedule(string value)
    {
        if (CalendarFile.Parse(value, out var problem) is not { } calendarEvent) return problem;
        var file = CalendarFile.Write(calendarEvent);
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

    /// <summary>{"titulo","texto"}, o el texto solo (el título son sus primeras palabras).</summary>
    static string? Note(string value)
    {
        var obj = AgendaJson.Object(value);
        var text = AgendaJson.Text(obj, "texto") ?? (obj is null ? value.Trim() : null);
        if (string.IsNullOrEmpty(text)) return L.T("No me dijeron qué anotar.");
        var title = AgendaJson.Text(obj, "titulo") ?? string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6));
        Notes.Save(title, text, DateTime.Now);
        return null;
    }

    /// <summary>«leer» (las últimas), «abrir» (la carpeta) o palabras para buscar: lo encontrado vuelve a Claude.</summary>
    static ActionOutcome ReadNotes(string value)
    {
        if (Is(value, "abrir", "open")) return Fail(Folder("notas"));
        var words = Is(value, "leer", "read", "") ? null : value;
        var found = Notes.Read(words);
        return new ActionOutcome(Data: found.Length > 0 ? found
            : words is null ? L.T("Todavía no hay notas.") : L.T("No hay notas con «{0}».", words));
    }

    /// <summary>
    /// {"texto","cuando"}: queda en ReminderStore (la app lo avisa a su hora) y, por si la app está cerrada a
    /// esa hora, en una tarea programada de Windows que la abre.
    /// </summary>
    static string? Remind(string value)
    {
        if (ReminderStore.Parse(value, DateTime.Now, out var problem) is not { } reminder) return problem;
        ReminderStore.Add(reminder);
        try { ReminderTasks.Create(reminder); }
        catch (Exception ex) { ErrorLog.Write(ex); } // sin la tarea, avisa igual con la app abierta (o al abrirla)
        return null;
    }

    /// <summary>«leer» (los pendientes, para Claude) o «cancelar &lt;palabras&gt;».</summary>
    static ActionOutcome ListReminders(string value)
    {
        var cancel = value.Split(' ', 2) is [var verb, ..] && Is(verb, "cancelar", "cancel", "borrar", "delete");
        if (!cancel)
        {
            var pending = ReminderStore.Pending();
            return new ActionOutcome(Data: pending.Count == 0 ? L.T("No hay recordatorios pendientes.")
                : string.Join("\n", pending.Select(r => L.T("• {0:dddd d 'de' MMMM, HH:mm}: {1}", r.At, r.Text))));
        }
        var words = value.Split(' ', 2) is [_, var rest] ? rest.Trim() : "";
        if (ReminderStore.Cancel(words) is not { } done) return Fail(L.T("No encontré un recordatorio así."));
        try { ReminderTasks.Delete(done.Id); } catch (Exception ex) { ErrorLog.Write(ex); }
        return new ActionOutcome(Say: L.T("Listo, ya no te recuerdo «{0}». (^_^)", done.Text));
    }

    // ── Ventanas ────────────────────────────────────────────────────────────────────────────

    /// <summary>«cerrar &lt;app&gt;», «minimizar …», «maximizar …», «traer …» (al frente) o «escritorio» (minimiza todo).</summary>
    static string? Window(string value)
    {
        value = value.Trim();
        if (Is(value, "escritorio", "desktop", "mostrar escritorio", "show desktop"))
        {
            var type = Type.GetTypeFromProgID("Shell.Application") ?? throw new InvalidOperationException("Shell.Application");
            var shell = Activator.CreateInstance(type)!;
            try { type.InvokeMember("MinimizeAll", BindingFlags.InvokeMethod, null, shell, null); }
            finally { Marshal.ReleaseComObject(shell); }
            return null;
        }
        var parts = value.Split(' ', 2, StringSplitOptions.TrimEntries);
        var app = parts.Length > 1 ? parts[1].Trim('"', '«', '»', ' ') : "";
        var verb = parts[0];
        if (!Is(verb, "cerrar", "close", "minimizar", "minimize", "maximizar", "maximize", "traer", "bring", "restaurar", "restore", "mostrar", "show", "focus"))
            return L.T("Con las ventanas sé cerrar, minimizar, maximizar o traer al frente.");
        if (app.Length == 0) return L.T("¿Qué ventana?");
        if (FindWindowOf(app) is not { } window) return L.T("No encontré una ventana de «{0}».", app);
        const int WmClose = 0x0010, Minimize = 6, Maximize = 3, Restore = 9;
        if (Is(verb, "cerrar", "close")) PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero); // como la X: la app puede preguntar si guardar
        else if (Is(verb, "minimizar", "minimize")) ShowWindow(window, Minimize);
        else if (Is(verb, "maximizar", "maximize")) ShowWindow(window, Maximize);
        else
        {
            if (IsIconic(window)) ShowWindow(window, Restore);
            Foreground.Take(window);
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
        var found = windows.FirstOrDefault(w => Same(w.Process, name) || Same(w.Description, name) || Same(w.Title, name)).Handle;
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

    // ── El equipo ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bloquear, suspender, reiniciar o apagar. Los tres últimos esperan un par de segundos, para que se vea
    /// (y se oiga) la respuesta. Reiniciar y apagar no fuerzan el cierre: una app con algo sin guardar puede
    /// detenerlo, como al hacerlo desde Inicio.
    /// </summary>
    static string? Computer(string value)
    {
        if (Is(value, "bloquear", "lock")) return LockWorkStation() ? null : L.T("Windows no dejó bloquear el equipo.");
        Action? later = Is(value, "suspender", "sleep", "suspend") ? () => SetSuspendState(false, false, false)
            : Is(value, "reiniciar", "restart") ? () => Shutdown("/r")
            : Is(value, "apagar", "shut down", "shutdown") ? () => Shutdown("/s")
            : null;
        if (later is not { } run) return L.T("Con el equipo sé bloquear, suspender, reiniciar o apagar.");
        _ = Task.Delay(2000).ContinueWith(_ => run(), TaskScheduler.Default);
        return null;
    }

    static void Shutdown(string option) =>
        Process.Start(new ProcessStartInfo("shutdown.exe") { ArgumentList = { option, "/t", "0" }, CreateNoWindow = true, UseShellExecute = false })?.Dispose();

    // ── Brillo ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 0 a 100, subir o bajar (de a 10). Por WMI (WmiMonitorBrightnessMethods), que solo llega a la pantalla
    /// del portátil: los monitores externos se ajustan con sus botones.
    /// </summary>
    static string? Brightness(string value)
    {
        int? percent = int.TryParse(value.TrimEnd('%', ' '), out var p) ? Math.Clamp(p, 0, 100) : null;
        var up = Is(value, "subir", "up");
        if (percent is null && !up && !Is(value, "bajar", "down")) return L.T("No entendí cuánto brillo.");
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
        var target = percent ?? Math.Clamp(current + (up ? 10 : -10), 0, 100);
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

    /// <summary>«bateria», «disco», «memoria» o «todo»: los datos vuelven a Claude para que responda.</summary>
    static ActionOutcome Status(string value)
    {
        var all = Is(value, "todo", "all", "");
        var lines = new List<string>();
        if (all || Is(value, "bateria", "battery")) lines.Add(Battery());
        if (all || Is(value, "disco", "disk", "discos", "disks")) lines.AddRange(Disks());
        if (all || Is(value, "memoria", "memory", "ram")) lines.Add(Memory());
        return lines.Count == 0 ? Fail(L.T("Sé ver la batería, el disco o la memoria.")) : new ActionOutcome(Data: string.Join("\n", lines));
    }

    static string Battery()
    {
        if (!GetSystemPowerStatus(out var power) || power.BatteryFlag == 128) return L.T("Batería: este equipo no tiene.");
        var percent = power.BatteryLifePercent == 255 ? L.T("desconocida") : $"{power.BatteryLifePercent} %";
        var state = power.ACLineStatus == 1 ? L.T("enchufado") + ((power.BatteryFlag & 8) != 0 ? ", " + L.T("cargando") : "")
            : power.BatteryLifeTime > 0 ? L.T("sin enchufar, le quedan unas {0}", Format.Duration(TimeSpan.FromSeconds(power.BatteryLifeTime)))
            : L.T("sin enchufar");
        return L.T("Batería: {0} ({1}).", percent, state);
    }

    static IEnumerable<string> Disks() =>
        DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => L.T("Disco {0} {1} libres de {2}.", d.Name.TrimEnd('\\'), Bytes(d.AvailableFreeSpace), Bytes(d.TotalSize)));

    static string Memory()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory)) return L.T("Memoria: no se pudo leer.");
        return L.T("Memoria: {0} libres de {1} ({2} % en uso).", Bytes((long)memory.Available), Bytes((long)memory.Total), memory.Load);
    }

    static string Bytes(long bytes) => string.Format(CultureInfo.CurrentCulture, "{0:0.#} GB", bytes / 1e9);

    // ── Portapapeles y carpetas ─────────────────────────────────────────────────────────────

    const int MaxClipboard = 8000;

    static string? Copy(string value)
    {
        if (value.Length == 0) return L.T("No me dijeron qué copiar.");
        Sta(() => { Clipboard.SetText(value); return true; });
        return null;
    }

    /// <summary>«leer»: el texto copiado vuelve a Claude; si es una imagen, Claude la mira.</summary>
    static ActionOutcome ReadClipboard(string value, string? question)
    {
        if (!Is(value, "leer", "read", "")) return Fail(L.T("Del portapapeles sé leer lo copiado o copiar un texto."));
        var (text, image) = Sta(() => (Clipboard.ContainsText() ? Clipboard.GetText() : null, Clipboard.ContainsImage() ? Clipboard.GetImage() : null));
        if (text is { Length: > 0 })
        {
            image?.Dispose();
            return new ActionOutcome(Data: text.Length > MaxClipboard ? text[..MaxClipboard] + L.T("… (recortado)") : text);
        }
        if (image is null) return Fail(L.T("El portapapeles está vacío."));
        using (image) return new ActionOutcome(Look: ScreenCapture.ForLook(image), Question: question);
    }

    /// <summary>Donde está una carpeta conocida (Descargas, Documentos…, las capturas o las notas); null, si no es una.</summary>
    static string? KnownFolder(string name) =>
        Is(name, "descargas", "downloads") ? KnownFolders.Get(KnownFolders.Downloads)
        : Is(name, "documentos", "documents") ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        : Is(name, "escritorio", "desktop") ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        : Is(name, "imagenes", "pictures", "fotos", "photos") ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        : Is(name, "musica", "music") ? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
        : Is(name, "videos") ? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        : Is(name, "capturas", "screenshots", "capturas de pantalla") ? ScreenCapture.Folder
        : Is(name, "notas", "notes") ? Notes.Folder
        : null;

    /// <summary>Una carpeta conocida (KnownFolder) o una que exista: se abre en el Explorador.</summary>
    static string? Folder(string value)
    {
        var name = value.Trim().Trim('"');
        var path = KnownFolder(name);
        if (path is not null) Directory.CreateDirectory(path); // las capturas o las notas, si todavía no hay
        else
        {
            var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(name));
            if (!Directory.Exists(full)) return L.T("No encontré la carpeta «{0}».", name);
            path = full;
        }
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false })?.Dispose();
        return null;
    }

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
