using System.Runtime.InteropServices;
using System.Security;
using System.Text.RegularExpressions;
using Avalonia.Input.Platform;

namespace Claupet;

/// <summary>
/// Las acciones en macOS: AppleScript (osascript) para el fondo, el modo oscuro, el volumen, la música, las ventanas y
/// apagar; open para abrir; screencapture para capturar; pmset para la batería, bloquear y suspender; y un LaunchAgent
/// para los recordatorios. macOS pregunta una vez por cada permiso (Automatización para «System Events», Música y
/// Spotify; Accesibilidad para mover ventanas; Grabación de pantalla para capturar). El brillo no: macOS no trae una
/// forma de cambiarlo desde un programa sin bibliotecas privadas.
/// </summary>
sealed class MacDesktop(Func<IClipboard?>? clipboard, Func<Func<Task>, Task> hideWhile) : UnixDesktop(clipboard, hideWhile)
{
    const string Osa = "/usr/bin/osascript";
    static readonly HashSet<string> MacKinds = [.. SystemActions.All.Where(k => k != "brillo")];

    public override IReadOnlySet<string> Kinds => MacKinds;

    /// <summary>
    /// Corre un AppleScript con argumentos (item 1 of argv…): lo que diga la persona va como dato, nunca dentro del
    /// texto del script.
    /// </summary>
    static (int Code, string Output) Script(string script, params string[] argv)
    {
        var args = new List<string>();
        foreach (var line in script.Split('\n')) args.AddRange(["-e", line]);
        args.AddRange(argv);
        return Run(Osa, args, 30000);
    }

    /// <summary>null si salió bien; si no, lo que falló (con el permiso de Automatización, que es lo habitual).</summary>
    static string? Done((int Code, string Output) result) =>
        result.Code == 0 ? null : L.T("macOS no lo dejó hacer (revise Privacidad y seguridad › Automatización).");

    public override string? SetWallpaper(string file) => Done(Script("""
        on run argv
        tell application "System Events" to tell every desktop to set picture to (item 1 of argv)
        end run
        """, file));

    public override string? SetDarkMode(bool dark) => Done(Script("""
        on run argv
        tell application "System Events" to tell appearance preferences to set dark mode to ((item 1 of argv) is "1")
        end run
        """, dark ? "1" : "0"));

    public override string? Volume(VolumeChange change)
    {
        if (change.Mute is { } mute) return Done(Script(mute ? "set volume output muted true" : "set volume output muted false"));
        int target;
        if (change.Percent is { } percent) target = percent;
        else
        {
            var (code, output) = Script("output volume of (get volume settings)");
            if (code != 0 || !int.TryParse(output, out var level)) return L.T("No pude leer el volumen.");
            target = Math.Clamp(level + change.Step, 0, 100);
        }
        return Done(Script($"""
            set volume output volume {target}
            {(target > 0 ? "set volume output muted false" : "")}
            """));
    }

    /// <summary>Spotify o Música, el que esté abierto (los navegadores no responden a AppleScript).</summary>
    public override Task<string?> MediaAsync(MediaCommand command)
    {
        var verb = command switch { MediaCommand.Play => "play", MediaCommand.Pause => "pause", MediaCommand.Next => "next track", _ => "previous track" };
        foreach (var app in new[] { "Spotify", "Music" })
        {
            var (code, output) = Script("""
                on run argv
                if application (item 1 of argv) is running then return "abierta"
                return "cerrada"
                end run
                """, app);
            if (code != 0 || output != "abierta") continue;
            return Task.FromResult(Done(Script($"tell application \"{app}\" to {verb}")));
        }
        return Task.FromResult<string?>(L.T("No hay nada abierto en Música ni en Spotify."));
    }

    // ── Abrir ───────────────────────────────────────────────────────────────────────────────

    static readonly string[] AppFolders = ["/Applications", "/Applications/Utilities", "/System/Applications", "/System/Applications/Utilities"];

    public override string? FindApp(string name)
    {
        var apps = AppFolders.Append(Path.Combine(Home, "Applications"))
            .Where(Directory.Exists)
            .SelectMany(dir =>
            {
                try { return Directory.EnumerateDirectories(dir, "*.app"); }
                catch (Exception) { return []; }
            });
        return Best(apps, path => Path.GetFileNameWithoutExtension(path), name);
    }

    public override void Start(string target) => Launch("/usr/bin/open", target);

    // ── Captura ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Donde macOS guarda las capturas (lo que se eligió en Captura de pantalla; si no, el escritorio).</summary>
    public override string ScreenshotsFolder
    {
        get
        {
            var (code, output) = Run("/usr/bin/defaults", "read", "com.apple.screencapture", "location");
            var folder = code == 0 && output.Length > 0 ? output.Replace("~", Home) : "";
            return Directory.Exists(folder) ? folder : Path.Combine(Home, "Desktop");
        }
    }

    /// <summary>
    /// La pantalla principal o el recorte que se marque (con la herramienta de macOS: Esc cancela). Sin el permiso de
    /// Grabación de pantalla, macOS da solo el fondo: se dice dónde se da.
    /// </summary>
    public override async Task<(string? File, string? Problem)> CaptureAsync(bool crop)
    {
        var file = CaptureFiles.NewFile(ScreenshotsFolder);
        var code = 0;
        await WithoutClaupetAsync(() => Task.Run(() =>
        {
            code = crop ? Run("/usr/sbin/screencapture", ["-i", "-x", file], 120000).Code : Run("/usr/sbin/screencapture", "-x", "-m", file).Code;
        }));
        if (!File.Exists(file))
            return crop && code == 0 ? (null, null)
                : (null, L.T("macOS no dejó capturar la pantalla (Privacidad y seguridad › Grabación de pantalla)."));
        Script("""
            on run argv
            set the clipboard to (read (POSIX file (item 1 of argv)) as «class PNGf»)
            end run
            """, file);
        return (file, null);
    }

    protected override bool ClipboardImage(string file)
    {
        var (code, output) = Script("""
            on run argv
            try
            set png to the clipboard as «class PNGf»
            on error
            return "no"
            end try
            set handle to open for access (POSIX file (item 1 of argv)) with write permission
            write png to handle
            close access handle
            return "si"
            end run
            """, file);
        return code == 0 && output == "si" && File.Exists(file);
    }

    // ── Ventanas ────────────────────────────────────────────────────────────────────────────

    /// <summary>El nombre de la app abierta (con ventanas) que se llama así; null si no hay.</summary>
    static string? RunningApp(string name)
    {
        var (code, output) = Script("""tell application "System Events" to get name of every process whose background only is false""");
        if (code != 0) return null;
        var apps = output.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(a => a != "Claupet");
        return Best(apps, a => a, name);
    }

    public override string? Window(WindowVerb verb, string app)
    {
        if (RunningApp(app) is not { } name) return L.T("No encontré una ventana de «{0}».", app);
        return Done(verb switch
        {
            // Como Cmd+Q: la app puede preguntar si guardar.
            WindowVerb.Close => Script("on run argv\ntell application (item 1 of argv) to quit\nend run", name),
            WindowVerb.Minimize => Script("on run argv\ntell application \"System Events\" to set visible of process (item 1 of argv) to false\nend run", name),
            // Del tamaño del escritorio, debajo de la barra de menús (pide el permiso de Accesibilidad).
            WindowVerb.Maximize => Script("""
                on run argv
                tell application "Finder" to set screen to bounds of window of desktop
                tell application "System Events" to tell process (item 1 of argv)
                set position of front window to {0, 25}
                set size of front window to {item 3 of screen, (item 4 of screen) - 25}
                end tell
                end run
                """, name),
            _ => Script("on run argv\ntell application (item 1 of argv) to activate\nend run", name),
        });
    }

    /// <summary>Oculta las demás apps, como Cmd+Opción+H desde el Finder.</summary>
    public override string? ShowDesktop() => Done(Script("""
        tell application "System Events" to set visible of every process whose visible is true and name is not "Finder" and name is not "Claupet" to false
        """));

    // ── El equipo ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bloquear apaga la pantalla (con «Pedir contraseña: inmediatamente», que es lo de siempre, queda bloqueado).
    /// Reiniciar y apagar van por System Events: cada app puede pedir guardar, como desde el menú Apple.
    /// </summary>
    public override string? Computer(ComputerCommand command) => command switch
    {
        ComputerCommand.Lock => Run("/usr/bin/pmset", "displaysleepnow").Code == 0 ? null : L.T("macOS no dejó bloquear el equipo."),
        ComputerCommand.Sleep => Run("/usr/bin/pmset", "sleepnow").Code == 0 ? null : L.T("macOS no dejó suspender el equipo."),
        ComputerCommand.Restart => Done(Script("tell application \"System Events\" to restart")),
        _ => Done(Script("tell application \"System Events\" to shut down")),
    };

    public override string? Brightness(int? percent, int step) => L.T("Eso todavía no lo sé hacer en este sistema.");

    // ── Batería, disco y memoria ────────────────────────────────────────────────────────────

    /// <summary>De pmset -g batt: «-InternalBattery-0 (id=…)	85%; charging; 1:23 remaining».</summary>
    public override string Battery()
    {
        var (code, output) = Run("/usr/bin/pmset", "-g", "batt");
        if (code != 0 || !output.Contains("InternalBattery")) return L.T("Batería: este equipo no tiene.");
        var percent = Regex.Match(output, @"(\d+)%") is { Success: true } p ? int.Parse(p.Groups[1].Value) : (int?)null;
        var plugged = output.Contains("'AC Power'");
        var charging = Regex.IsMatch(output, @";\s*charging;");
        TimeSpan? left = Regex.Match(output, @"(\d+):(\d\d) remaining") is { Success: true } t
            ? new TimeSpan(int.Parse(t.Groups[1].Value), int.Parse(t.Groups[2].Value), 0) : null;
        return BatteryLine(percent, plugged, charging, left);
    }

    /// <summary>Libre: las páginas libres, inactivas y especulativas de vm_stat (lo que macOS puede dar enseguida).</summary>
    public override string Memory()
    {
        var total = long.TryParse(Run("/usr/sbin/sysctl", "-n", "hw.memsize").Output, out var bytes) ? bytes : 0;
        var stat = Run("/usr/bin/vm_stat").Output;
        var page = Regex.Match(stat, @"page size of (\d+) bytes") is { Success: true } size ? long.Parse(size.Groups[1].Value) : 4096;
        long Pages(string name) => Regex.Match(stat, $@"{name}:\s+(\d+)") is { Success: true } m ? long.Parse(m.Groups[1].Value) : 0;
        return MemoryLine((Pages("Pages free") + Pages("Pages inactive") + Pages("Pages speculative")) * page, total);
    }

    /// <summary>El disco del sistema y los externos (en /Volumes); no los volúmenes internos de macOS.</summary>
    public override IEnumerable<DriveInfo> Disks() =>
        DriveInfo.GetDrives().Where(d => d.IsReady && (d.Name == "/" || d.Name.StartsWith("/Volumes/", StringComparison.Ordinal)));

    // ── Carpetas ────────────────────────────────────────────────────────────────────────────

    public override string? Folder(KnownFolder folder) => Path.Combine(Home, folder switch
    {
        KnownFolder.Downloads => "Downloads",
        KnownFolder.Documents => "Documents",
        KnownFolder.Desktop => "Desktop",
        KnownFolder.Pictures => "Pictures",
        KnownFolder.Music => "Music",
        _ => "Movies",
    });

    // ── Recordatorios ───────────────────────────────────────────────────────────────────────

    static string Label(string id) => $"local.claupet.recordatorio.{id}";

    static string PlistFile(string id) => Path.Combine(Home, "Library", "LaunchAgents", Label(id) + ".plist");

    static string Domain => $"gui/{getuid()}";

    /// <summary>
    /// Un LaunchAgent que abre Claupet a esa hora con «--recordatorio &lt;id&gt;» (si ya está abierta, la nueva se
    /// cierra enseguida y avisa la que estaba). StartCalendarInterval se repetiría al año siguiente: la app lo quita al
    /// avisar o al cancelar el recordatorio.
    /// </summary>
    public override void ScheduleReminder(Reminder reminder)
    {
        var at = reminder.At;
        var plist = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
            + "<plist version=\"1.0\">\n<dict>\n"
            + $"  <key>Label</key><string>{Label(reminder.Id)}</string>\n"
            + $"  <key>ProgramArguments</key><array><string>{SecurityElement.Escape(Environment.ProcessPath ?? "")}</string>"
            + $"<string>--recordatorio</string><string>{SecurityElement.Escape(reminder.Id)}</string></array>\n"
            + $"  <key>StartCalendarInterval</key><dict><key>Month</key><integer>{at.Month}</integer><key>Day</key><integer>{at.Day}</integer>"
            + $"<key>Hour</key><integer>{at.Hour}</integer><key>Minute</key><integer>{at.Minute}</integer></dict>\n"
            + "</dict>\n</plist>\n";
        var file = PlistFile(reminder.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, plist);
        if (Run("/bin/launchctl", "bootstrap", Domain, file).Code != 0) throw new InvalidOperationException("launchctl bootstrap");
    }

    public override void CancelReminder(string id)
    {
        Run("/bin/launchctl", "bootout", $"{Domain}/{Label(id)}");
        var file = PlistFile(id);
        if (File.Exists(file)) File.Delete(file);
    }

    [DllImport("libc")] static extern uint getuid();
}
