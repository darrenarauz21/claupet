using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Input.Platform;

namespace Claupet;

/// <summary>
/// Las acciones en Linux, con lo que traen los escritorios habituales: gsettings (GNOME, Cinnamon, MATE) o las
/// herramientas de Plasma para el fondo y el tema; wpctl (PipeWire) o pactl para el volumen; MPRIS (playerctl o
/// dbus-send) para la música; los .desktop para abrir apps; la herramienta de capturas del escritorio; wmctrl para las
/// ventanas (solo en X11: Wayland no deja que una app mueva las de otras); brightnessctl o GNOME para el brillo;
/// /sys y /proc para el estado; y systemd-run para los recordatorios. Si falta un programa, se dice cuál instalar.
/// </summary>
sealed class LinuxDesktop : UnixDesktop
{
    readonly HashSet<string> _kinds;

    public LinuxDesktop(Func<IClipboard?>? clipboard, Func<Func<Task>, Task> hideWhile) : base(clipboard, hideWhile) =>
        _kinds = [.. SystemActions.All.Where(k => k != "ventana" || !Wayland)];

    public override IReadOnlySet<string> Kinds => _kinds;

    static bool Wayland => Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is { Length: > 0 }
        || string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase);

    /// <summary>El escritorio (XDG_CURRENT_DESKTOP, por ejemplo «ubuntu:GNOME» o «KDE»), en mayúsculas.</summary>
    static string Session => (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").ToUpperInvariant();

    static bool Is(string desktop) => Session.Split(':').Contains(desktop);

    /// <summary>null si el programa salió bien; si no, «No pude …» con lo que se quería hacer (en infinitivo).</summary>
    static string? Done(int code, string what) => code == 0 ? null : L.T("No pude {0}.", what);

    // ── Fondo y tema ────────────────────────────────────────────────────────────────────────

    public override string? SetWallpaper(string file)
    {
        var uri = new Uri(file).AbsoluteUri;
        if (Is("KDE")) return Run("plasma-apply-wallpaperimage", file).Code == 0 ? null : Missing(L.T("cambiar el fondo"), "plasma-workspace");
        if (Is("MATE")) return Run("gsettings", "set", "org.mate.background", "picture-filename", file).Code == 0 ? null : L.T("No pude cambiar el fondo.");
        if (Is("X-CINNAMON")) return Run("gsettings", "set", "org.cinnamon.desktop.background", "picture-uri", uri).Code == 0 ? null : L.T("No pude cambiar el fondo.");
        if (Is("GNOME") || Is("UNITY") || Is("BUDGIE"))
        {
            if (Run("gsettings", "set", "org.gnome.desktop.background", "picture-uri", uri).Code != 0) return L.T("No pude cambiar el fondo.");
            Run("gsettings", "set", "org.gnome.desktop.background", "picture-uri-dark", uri); // GNOME 42 o más: el del modo oscuro
            return null;
        }
        return L.T("No sé cambiar el fondo en este escritorio ({0}).", Session.Length > 0 ? Session : "?");
    }

    public override string? SetDarkMode(bool dark)
    {
        if (Is("KDE"))
            return Run("plasma-apply-colorscheme", dark ? "BreezeDark" : "BreezeLight").Code == 0 ? null : Missing(L.T("cambiar el tema"), "plasma-workspace");
        if (Is("GNOME") || Is("UNITY") || Is("BUDGIE") || Is("X-CINNAMON"))
            return Run("gsettings", "set", "org.gnome.desktop.interface", "color-scheme", dark ? "prefer-dark" : "default").Code == 0
                ? null : L.T("No pude cambiar el tema.");
        return L.T("No sé cambiar el tema en este escritorio ({0}).", Session.Length > 0 ? Session : "?");
    }

    // ── Volumen y música ────────────────────────────────────────────────────────────────────

    /// <summary>Con wpctl (PipeWire, lo de hoy) o pactl (PulseAudio, y PipeWire con su capa de PulseAudio).</summary>
    public override string? Volume(VolumeChange change)
    {
        if (Has("wpctl"))
        {
            const string Sink = "@DEFAULT_AUDIO_SINK@";
            if (change.Mute is { } mute) return Done(Run("wpctl", "set-mute", Sink, mute ? "1" : "0").Code, L.T("cambiar el volumen"));
            var target = change.Percent is { } percent ? $"{percent}%" : change.Step > 0 ? $"{change.Step}%+" : $"{-change.Step}%-";
            var code = Run("wpctl", "set-volume", "-l", "1.0", Sink, target).Code;
            if (code == 0 && (change.Percent ?? 1) > 0) Run("wpctl", "set-mute", Sink, "0");
            return Done(code, L.T("cambiar el volumen"));
        }
        if (Has("pactl"))
        {
            const string Sink = "@DEFAULT_SINK@";
            if (change.Mute is { } mute) return Done(Run("pactl", "set-sink-mute", Sink, mute ? "1" : "0").Code, L.T("cambiar el volumen"));
            var target = change.Percent is { } percent ? $"{percent}%" : change.Step > 0 ? $"+{change.Step}%" : $"-{-change.Step}%";
            var code = Run("pactl", "set-sink-volume", Sink, target).Code;
            if (code == 0 && (change.Percent ?? 1) > 0) Run("pactl", "set-sink-mute", Sink, "0");
            return Done(code, L.T("cambiar el volumen"));
        }
        return Missing(L.T("cambiar el volumen"), "wpctl (PipeWire) o pactl");
    }

    /// <summary>Lo que suene en un reproductor con MPRIS (Spotify, los navegadores, Rhythmbox…).</summary>
    public override Task<string?> MediaAsync(MediaCommand command)
    {
        var verb = command switch { MediaCommand.Play => "Play", MediaCommand.Pause => "Pause", MediaCommand.Next => "Next", _ => "Previous" };
        if (Has("playerctl"))
        {
            var code = Run("playerctl", verb.ToLowerInvariant()).Code;
            return Task.FromResult(code == 0 ? null : L.T("No hay nada sonando que pueda controlar."));
        }
        var (listed, names) = Run("dbus-send", "--session", "--print-reply", "--dest=org.freedesktop.DBus", "/org/freedesktop/DBus",
            "org.freedesktop.DBus.ListNames");
        if (listed == -1) return Task.FromResult<string?>(Missing(L.T("controlar la música"), "playerctl"));
        var player = Regex.Match(names, "\"(org\\.mpris\\.MediaPlayer2\\.[^\"]+)\"") is { Success: true } m ? m.Groups[1].Value : null;
        if (player is null) return Task.FromResult<string?>(L.T("No hay nada sonando que pueda controlar."));
        var sent = Run("dbus-send", "--session", "--type=method_call", $"--dest={player}", "/org/mpris/MediaPlayer2",
            $"org.mpris.MediaPlayer2.Player.{verb}").Code;
        return Task.FromResult(sent == 0 ? null : L.T("No hay nada sonando que pueda controlar."));
    }

    // ── Abrir ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Las carpetas con .desktop: las de XDG_DATA_DIRS (Flatpak y Snap incluidos) y la del usuario.</summary>
    static IEnumerable<string> ApplicationDirs()
    {
        var data = Environment.GetEnvironmentVariable("XDG_DATA_DIRS") is { Length: > 0 } dirs ? dirs : "/usr/local/share:/usr/share";
        var home = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } own ? own : Path.Combine(Home, ".local", "share");
        return data.Split(':', StringSplitOptions.RemoveEmptyEntries).Prepend(home)
            .Append("/var/lib/flatpak/exports/share").Append("/var/lib/snapd/desktop")
            .Select(d => Path.Combine(d, "applications")).Distinct().Where(Directory.Exists);
    }

    sealed record DesktopApp(string File, string Name);

    /// <summary>Las apps del menú: los .desktop de tipo Application que se muestran, con su nombre (también el traducido).</summary>
    static IEnumerable<DesktopApp> Applications()
    {
        foreach (var dir in ApplicationDirs())
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*.desktop", SearchOption.AllDirectories).ToList(); }
            catch (Exception) { continue; }
            foreach (var file in files)
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); }
                catch (Exception) { continue; }
                var entry = lines.SkipWhile(l => l.Trim() != "[Desktop Entry]").Skip(1).TakeWhile(l => !l.StartsWith('[')).ToList();
                string? Value(string key) => entry.FirstOrDefault(l => l.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..].Trim();
                if (Value("Type") != "Application" || Value("NoDisplay") == "true" || Value("Hidden") == "true") continue;
                foreach (var name in new[] { Value($"Name[{L.Language}]"), Value("Name") }.OfType<string>().Distinct())
                    yield return new DesktopApp(file, name);
            }
        }
    }

    static (DateTime At, List<DesktopApp> Apps)? _apps;

    /// <summary>La lista se lee de nuevo cada minuto: las reglas sin IA la consultan en cada «abre…».</summary>
    public override string? FindApp(string name)
    {
        if (_apps is not { } cached || DateTime.UtcNow - cached.At > TimeSpan.FromMinutes(1))
            _apps = cached = (DateTime.UtcNow, Applications().ToList());
        return Best(cached.Apps, a => a.Name, name)?.File;
    }

    /// <summary>Una app (su .desktop) con gio o gtk-launch; un enlace, un archivo o una carpeta con xdg-open.</summary>
    public override void Start(string target)
    {
        if (target.EndsWith(".desktop", StringComparison.Ordinal) && File.Exists(target))
        {
            if (Launch("gio", "launch", target)) return;
            if (Launch("gtk-launch", Path.GetFileNameWithoutExtension(target))) return;
            throw new InvalidOperationException(Missing(L.T("abrir apps"), "gio (glib2) o gtk-launch"));
        }
        if (!Launch("xdg-open", target)) throw new InvalidOperationException(Missing(L.T("abrir enlaces y archivos"), "xdg-utils"));
    }

    // ── Captura ─────────────────────────────────────────────────────────────────────────────

    public override string ScreenshotsFolder
    {
        get
        {
            var pictures = Folder(KnownFolder.Pictures)!;
            return new[] { "Screenshots", "Capturas de pantalla" }.Select(n => Path.Combine(pictures, n)).FirstOrDefault(Directory.Exists)
                ?? Path.Combine(pictures, "Screenshots");
        }
    }

    /// <summary>Con la herramienta del escritorio (GNOME, Plasma, wlroots) o una de X11; el recorte, con la suya.</summary>
    static (string Program, string[] Args)? CaptureTool(bool crop, string file)
    {
        if (Is("KDE") && Has("spectacle")) return ("spectacle", crop ? ["-b", "-n", "-r", "-o", file] : ["-b", "-n", "-m", "-o", file]);
        if ((Is("GNOME") || Is("UNITY") || Is("BUDGIE")) && Has("gnome-screenshot")) return ("gnome-screenshot", crop ? ["-a", "-f", file] : ["-f", file]);
        if (Wayland && Has("grim"))
        {
            if (!crop) return ("grim", [file]);
            if (crop && !Has("slurp")) return null;
            var area = Run("slurp", [], 120000).Output; // espera a que se marque el recorte
            return area.Length == 0 ? null : ("grim", ["-g", area, file]);
        }
        if (Has("maim")) return ("maim", crop ? ["-s", file] : [file]);
        if (Has("scrot")) return ("scrot", crop ? ["-s", file] : [file]);
        if (Has("import")) return ("import", crop ? [file] : ["-window", "root", file]);
        return null;
    }

    public override async Task<(string? File, string? Problem)> CaptureAsync(bool crop)
    {
        var file = CaptureFiles.NewFile(ScreenshotsFolder);
        string? problem = null;
        await WithoutClaupetAsync(() => Task.Run(() =>
        {
            if (CaptureTool(crop, file) is not { } tool)
            {
                problem = crop && Wayland && Has("grim") && Has("slurp") ? null // se canceló el recorte de slurp
                    : Missing(L.T("capturar la pantalla"), "gnome-screenshot, spectacle, grim o maim");
                return;
            }
            Run(tool.Program, tool.Args, crop ? 120000 : 15000);
        }));
        if (problem is not null) return (null, problem);
        if (!File.Exists(file)) return crop ? (null, null) : (null, L.T("No pude capturar la pantalla."));
        // Al portapapeles, si hay con qué (si no, queda guardada igual).
        if (Wayland && Has("wl-copy")) Run("wl-copy", ["--type", "image/png"], 15000, inputFile: file);
        else if (Has("xclip")) Run("xclip", "-selection", "clipboard", "-t", "image/png", "-i", file);
        return (file, null);
    }

    protected override bool ClipboardImage(string file)
    {
        var copied = Wayland && Has("wl-paste")
            ? Run("wl-paste", "--list-types").Output.Contains("image/png") && Run("wl-paste", ["--type", "image/png"], 15000, file).Code == 0
            : Has("xclip") && Run("xclip", "-selection", "clipboard", "-t", "TARGETS", "-o").Output.Contains("image/png")
                && Run("xclip", ["-selection", "clipboard", "-t", "image/png", "-o"], 15000, file).Code == 0;
        return copied && File.Exists(file) && new FileInfo(file).Length > 0;
    }

    // ── Ventanas (X11) ──────────────────────────────────────────────────────────────────────

    /// <summary>La ventana de esa app (por su clase o su título, como en Windows): el id de wmctrl, o null.</summary>
    static string? FindWindow(string app)
    {
        var windows = Run("wmctrl", "-lx").Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => Regex.Match(line, @"^(0x[0-9a-f]+)\s+-?\d+\s+(\S+)\s+\S+\s+(.*)$"))
            .Where(m => m.Success)
            .Select(m => new[] { m.Groups[1].Value, m.Groups[2].Value.Split('.').Last(), m.Groups[3].Value })
            .Where(w => !w[2].Contains("Claupet", StringComparison.Ordinal))
            .ToList();
        return (Best(windows, w => w[1], app) ?? Best(windows, w => w[2], app))?[0];
    }

    public override string? Window(WindowVerb verb, string app)
    {
        if (!Has("wmctrl")) return Missing(L.T("manejar las ventanas"), "wmctrl");
        if (FindWindow(app) is not { } id) return L.T("No encontré una ventana de «{0}».", app);
        var code = verb switch
        {
            WindowVerb.Close => Run("wmctrl", "-i", "-c", id).Code, // como la X: la app puede preguntar si guardar
            WindowVerb.Maximize => Run("wmctrl", "-i", "-r", id, "-b", "add,maximized_vert,maximized_horz").Code,
            WindowVerb.Minimize => Has("xdotool") ? Run("xdotool", "windowminimize", id).Code : Run("wmctrl", "-i", "-r", id, "-b", "add,hidden").Code,
            _ => Run("wmctrl", "-i", "-a", id).Code,
        };
        return Done(code, L.T("manejar las ventanas"));
    }

    public override string? ShowDesktop() =>
        Has("wmctrl") ? Done(Run("wmctrl", "-k", "on").Code, L.T("manejar las ventanas")) : Missing(L.T("manejar las ventanas"), "wmctrl");

    // ── El equipo y el brillo ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Reiniciar y apagar, por el escritorio cuando se puede (GNOME y Plasma dejan que cada app pida guardar); si no,
    /// con systemctl, que cierra las apps sin preguntar.
    /// </summary>
    public override string? Computer(ComputerCommand command)
    {
        switch (command)
        {
            case ComputerCommand.Lock:
                return Run("loginctl", "lock-session").Code == 0 || Run("xdg-screensaver", "lock").Code == 0 ? null : L.T("No pude bloquear el equipo.");
            case ComputerCommand.Sleep:
                return Done(Run("systemctl", "suspend").Code, L.T("suspender el equipo"));
        }
        var restart = command == ComputerCommand.Restart;
        if ((Is("GNOME") || Is("UNITY") || Is("BUDGIE")) && Launch("gnome-session-quit", restart ? "--reboot" : "--power-off")) return null;
        if (Is("KDE") && First("qdbus6", "qdbus") is { } qdbus
            && (Run(qdbus, "org.kde.Shutdown", "/Shutdown", restart ? "logoutAndReboot" : "logoutAndShutdown").Code == 0 // Plasma 6
                || Run(qdbus, "org.kde.ksmserver", "/KSMServer", "logout", "0", restart ? "1" : "2", "2").Code == 0)) // Plasma 5
            return null;
        return Done(Run("systemctl", restart ? "reboot" : "poweroff").Code, restart ? L.T("reiniciar el equipo") : L.T("apagar el equipo"));
    }

    /// <summary>Con brightnessctl o, en GNOME, con su servicio de energía; solo llega a la pantalla del portátil.</summary>
    public override string? Brightness(int? percent, int step)
    {
        if (Has("brightnessctl"))
        {
            var target = percent is { } p ? $"{p}%" : step > 0 ? $"+{step}%" : $"{-step}%-";
            return Run("brightnessctl", "set", target).Code == 0 ? null
                : L.T("Esta pantalla no deja cambiar el brillo desde el sistema: un monitor externo se ajusta con sus botones.");
        }
        if (Is("GNOME") && Has("gdbus"))
        {
            string[] Call(string method, params string[] rest) =>
                ["call", "--session", "--dest", "org.gnome.SettingsDaemon.Power", "--object-path", "/org/gnome/SettingsDaemon/Power",
                 "--method", $"org.freedesktop.DBus.Properties.{method}", "org.gnome.SettingsDaemon.Power.Screen", "Brightness", .. rest];
            var current = Regex.Match(Run("gdbus", Call("Get")).Output, @"(\d+)") is { Success: true } m ? int.Parse(m.Groups[1].Value) : -1;
            if (current < 0) return L.T("Esta pantalla no deja cambiar el brillo desde el sistema: un monitor externo se ajusta con sus botones.");
            var value = percent ?? Math.Clamp(current + step, 0, 100);
            return Done(Run("gdbus", Call("Set", $"<int32 {value}>")).Code, L.T("cambiar el brillo"));
        }
        return Missing(L.T("cambiar el brillo"), "brightnessctl");
    }

    // ── Batería, disco y memoria ────────────────────────────────────────────────────────────

    const string PowerSupply = "/sys/class/power_supply";

    static string Read(string file) => File.Exists(file) ? File.ReadAllText(file).Trim() : "";

    public override string Battery()
    {
        var supplies = Directory.Exists(PowerSupply) ? Directory.GetDirectories(PowerSupply) : [];
        var battery = supplies.FirstOrDefault(d => Read(Path.Combine(d, "type")) == "Battery" && Read(Path.Combine(d, "scope")) != "Device");
        if (battery is null) return L.T("Batería: este equipo no tiene.");
        int? percent = int.TryParse(Read(Path.Combine(battery, "capacity")), out var p) ? p : null;
        var status = Read(Path.Combine(battery, "status"));
        var plugged = supplies.Any(d => Read(Path.Combine(d, "type")) == "Mains" && Read(Path.Combine(d, "online")) == "1") || status is "Charging" or "Full";
        // Lo que le queda, con la energía y el consumo de ahora (si el equipo los da).
        TimeSpan? left = null;
        if (!plugged && long.TryParse(Read(Path.Combine(battery, "energy_now")), out var energy)
            && long.TryParse(Read(Path.Combine(battery, "power_now")), out var power) && power > 0)
            left = TimeSpan.FromHours((double)energy / power);
        return BatteryLine(percent, plugged, status == "Charging", left);
    }

    public override string Memory()
    {
        var info = File.Exists("/proc/meminfo") ? File.ReadAllLines("/proc/meminfo") : [];
        long Kb(string key) => info.Select(l => Regex.Match(l, $@"^{key}:\s+(\d+) kB")).FirstOrDefault(m => m.Success) is { } m
            ? long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 1024 : 0;
        return MemoryLine(Kb("MemAvailable"), Kb("MemTotal"));
    }

    static readonly string[] DiskFormats = ["ext2", "ext3", "ext4", "btrfs", "xfs", "zfs", "f2fs", "ntfs", "ntfs3", "fuseblk", "vfat", "exfat"];

    /// <summary>Los discos de verdad (no tmpfs, snap ni los del sistema), una vez cada uno.</summary>
    public override IEnumerable<DriveInfo> Disks() =>
        DriveInfo.GetDrives()
            .Where(d => d.IsReady && DiskFormats.Contains(d.DriveFormat)
                && (d.Name is "/" or "/home" || d.Name.StartsWith("/media/", StringComparison.Ordinal)
                    || d.Name.StartsWith("/mnt/", StringComparison.Ordinal) || d.Name.StartsWith("/run/media/", StringComparison.Ordinal)))
            .GroupBy(d => (d.TotalSize, d.AvailableFreeSpace)) // un mismo btrfs montado en / y en /home
            .Select(g => g.First());

    // ── Carpetas ────────────────────────────────────────────────────────────────────────────

    /// <summary>Las de xdg-user-dirs (traducidas: «Descargas», «Imágenes»…); si no está, las de siempre.</summary>
    public override string? Folder(KnownFolder folder)
    {
        var (key, fallback) = folder switch
        {
            KnownFolder.Downloads => ("DOWNLOAD", "Downloads"),
            KnownFolder.Documents => ("DOCUMENTS", "Documents"),
            KnownFolder.Desktop => ("DESKTOP", "Desktop"),
            KnownFolder.Pictures => ("PICTURES", "Pictures"),
            KnownFolder.Music => ("MUSIC", "Music"),
            _ => ("VIDEOS", "Videos"),
        };
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c : Path.Combine(Home, ".config");
        var dirs = Path.Combine(config, "user-dirs.dirs");
        if (File.Exists(dirs)
            && Regex.Match(File.ReadAllText(dirs), $"^XDG_{key}_DIR=\"([^\"]+)\"", RegexOptions.Multiline) is { Success: true } m)
        {
            var path = m.Groups[1].Value.Replace("$HOME", Home);
            if (path.TrimEnd('/') != Home.TrimEnd('/')) return path; // «$HOME» solo es que no tiene una propia
        }
        return Path.Combine(Home, fallback);
    }

    // ── Recordatorios ───────────────────────────────────────────────────────────────────────

    static string Unit(string id) => $"claupet-recordatorio-{id}";

    /// <summary>
    /// Un temporizador de systemd del usuario que abre Claupet a esa hora con «--recordatorio &lt;id&gt;» (si ya está
    /// abierta, la nueva se cierra enseguida y avisa la que estaba). Es transitorio: no queda si se reinicia antes; ahí
    /// avisa la app al abrirse.
    /// </summary>
    public override void ScheduleReminder(Reminder reminder)
    {
        var (code, _) = Run("systemd-run", "--user", $"--unit={Unit(reminder.Id)}",
            $"--on-calendar={reminder.At.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}",
            "--timer-property=AccuracySec=1s", Environment.ProcessPath ?? "", "--recordatorio", reminder.Id);
        if (code != 0) throw new InvalidOperationException("systemd-run");
    }

    public override void CancelReminder(string id) => Run("systemctl", "--user", "stop", Unit(id) + ".timer");
}
