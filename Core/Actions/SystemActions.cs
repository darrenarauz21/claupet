using System.Globalization;
using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>
/// Cómo le fue a una acción. Problem: lo que falló, dicho en corto para la nubecita. Say: lo que se dice en
/// vez de la frase de Claude (cuando la app sabe algo que Claude no). Data: lo que la app averiguó para que
/// Claude responda la pregunta (la batería, las notas, lo copiado). Look: una imagen para que Claude la mire,
/// con Question.
/// </summary>
sealed record ActionOutcome(string? Problem = null, string? Say = null, string? Data = null, string? Look = null, string? Question = null);

/// <summary>
/// Lo que la mascota hace en el equipo cuando se lo piden (la respuesta de Claude empieza con [ACCION]): una lista
/// cerrada, para que una frase mal entendida no pueda hacer otra cosa. Lo que no está aquí se le encarga a una sesión
/// de Claude Code. Las de siempre (fondo, tema, color, volumen, música y abrir) se hacen sin preguntar; las demás
/// piden permiso por capacidad (Capabilities), y reiniciar o apagar se confirman cada vez. El permiso lo pide la
/// bandeja antes de llamar a RunAsync. Aquí se entiende el pedido; lo propio de cada sistema lo hace Desktop.
/// </summary>
static class SystemActions
{
    /// <summary>Todas las acciones del protocolo con Claude (los nombres van igual en todos los idiomas).</summary>
    public static readonly string[] All =
        ["fondo", "tema", "color", "volumen", "musica", "abrir", "captura", "agendar", "nota", "notas", "recordar", "recordatorios",
         "ventana", "equipo", "brillo", "estado", "copiar", "portapapeles", "carpeta"];

    /// <summary>Lo que hace cada acción en este sistema; lo pone la app al arrancar. Sin él, no hay acciones.</summary>
    public static IDesktop? Desktop { get; set; }

    /// <summary>Este sistema sabe hacer esa acción (las demás no se le ofrecen a Claude ni a las reglas sin IA).</summary>
    public static bool Supports(string kind) => Desktop is { } desktop && desktop.Kinds.Contains(Normalize(new AssistantAction(kind, ""), desktop).Kind);

    public static Task<ActionOutcome> RunAsync(AssistantAction action, Action<string> setPalette) => RunAsync(action, setPalette, Desktop);

    /// <summary>Con ese escritorio (las pruebas pasan uno que solo anota lo que se le pide).</summary>
    internal static async Task<ActionOutcome> RunAsync(AssistantAction action, Action<string> setPalette, IDesktop? desktop)
    {
        action = Normalize(action, desktop);
        var value = action.Value;
        if (desktop is null || !desktop.Kinds.Contains(action.Kind))
            return Fail(All.Contains(action.Kind) ? L.T("Eso todavía no lo sé hacer en este sistema.") : L.T("Eso todavía no lo sé hacer."));
        try
        {
            return action.Kind switch
            {
                "fondo" => Fail(await WallpaperAsync(desktop, value)),
                "tema" => Fail(Mode(desktop, value)),
                "color" => Fail(Palette(value, setPalette)),
                "volumen" => Fail(Volume(desktop, value)),
                "musica" => Fail(await MediaAsync(desktop, value)),
                "abrir" => Fail(Open(desktop, value)),
                "captura" => await CaptureAsync(desktop, value, action.Question),
                "agendar" => Fail(Schedule(desktop, value)),
                "nota" => Fail(Note(value)),
                "notas" => ReadNotes(desktop, value),
                "recordar" => Fail(Remind(desktop, value)),
                "recordatorios" => ListReminders(desktop, value),
                "ventana" => Fail(Window(desktop, value)),
                "equipo" => Fail(Computer(desktop, value)),
                "brillo" => Fail(Brightness(desktop, value)),
                "estado" => Status(desktop, value),
                "copiar" => Fail(await CopyAsync(desktop, value)),
                "portapapeles" => await ReadClipboardAsync(desktop, value, action.Question),
                "carpeta" => Fail(Folder(desktop, value)),
                _ => Fail(L.T("Eso todavía no lo sé hacer.")),
            };
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex); // el mensaje del sistema suele ser poco («Error no especificado»): el detalle queda en errores.log
            return Fail(L.T("No pude: {0}", ex.Message));
        }
    }

    /// <summary>
    /// Lo que Claude a veces pide con otro nombre: «escritorio» como acción es «ventana escritorio», «abrir» una
    /// carpeta conocida (descargas, documentos…) es «carpeta» y «música» va sin tilde.
    /// </summary>
    static AssistantAction Normalize(AssistantAction action, IDesktop? desktop) => action.Kind switch
    {
        "escritorio" => action with { Kind = "ventana", Value = "escritorio" },
        "música" => action with { Kind = "musica" },
        "abrir" when KnownFolderPath(desktop, action.Value.Trim().Trim('"')) is not null => action with { Kind = "carpeta" },
        _ => action,
    };

    /// <summary>La capacidad que pide permiso para esta acción; null, las de siempre (y las que no existen).</summary>
    public static string? CapabilityOf(AssistantAction action) => Normalize(action, Desktop).Kind switch
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
        action = Normalize(action, Desktop);
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
    internal static bool Is(string value, params string[] words) => words.Any(w => Same(value, w));

    // ── Fondo de pantalla ───────────────────────────────────────────────────────────────────

    /// <summary>Los fondos bajados de internet; quedan los últimos KeepWallpapers.</summary>
    static string WallpaperDir => DataFolder.In("fondos");

    const int KeepWallpapers = 5;
    const long MaxImage = 25 * 1024 * 1024;
    static readonly string[] ImageTypes = [".jpg", ".jpeg", ".png", ".bmp"];
    static readonly HttpClient Http = SystemProxy.Client(TimeSpan.FromSeconds(30));

    /// <summary>Una imagen del equipo o una URL https de una imagen (se baja a WallpaperDir).</summary>
    static async Task<string?> WallpaperAsync(IDesktop desktop, string value)
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
        return desktop.SetWallpaper(file);
    }

    /// <summary>La baja si es JPEG, PNG o BMP de hasta MaxImage y empieza como una imagen de ese tipo; si no, null.</summary>
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
            ok = LooksLikeImage(file, extension); // que de verdad sea una imagen, no una página con otro tipo
        }
        catch (Exception) { return null; }
        finally
        {
            if (!ok) try { File.Delete(file); } catch (Exception) { }
        }
        if (!ok) return null;

        foreach (var old in new DirectoryInfo(WallpaperDir).GetFiles("fondo-*").OrderByDescending(f => f.Name).Skip(KeepWallpapers))
            try { old.Delete(); } catch (Exception) { }
        return file;
    }

    /// <summary>Los primeros bytes son los de ese tipo de imagen.</summary>
    internal static bool LooksLikeImage(string file, string extension)
    {
        var head = new byte[8];
        using (var stream = File.OpenRead(file))
            if (stream.Read(head, 0, head.Length) < head.Length) return false;
        return extension switch
        {
            ".jpg" => head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF,
            ".png" => head.AsSpan().SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            ".bmp" => head[0] == (byte)'B' && head[1] == (byte)'M',
            _ => false,
        };
    }

    // ── Modo claro u oscuro y color de la app ───────────────────────────────────────────────

    static string? Mode(IDesktop desktop, string value)
    {
        // Los valores del protocolo van en español; en inglés, Claude a veces los traduce.
        if (Is(value, "claro", "light")) return desktop.SetDarkMode(false);
        if (Is(value, "oscuro", "dark")) return desktop.SetDarkMode(true);
        return L.T("Solo sé poner el modo claro o el oscuro.");
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
    static string? Volume(IDesktop desktop, string value)
    {
        VolumeChange? change = int.TryParse(value.TrimEnd('%', ' '), out var percent) ? new(Math.Clamp(percent, 0, 100), 0, null)
            : Is(value, "subir", "up") ? new(null, 10, null)
            : Is(value, "bajar", "down") ? new(null, -10, null)
            : Is(value, "silenciar", "mute") ? new(null, 0, true)
            : Is(value, "activar", "unmute") ? new(null, 0, false)
            : null;
        return change is null ? L.T("No entendí cuánto volumen.") : desktop.Volume(change);
    }

    /// <summary>Lo que suene en el equipo (Spotify, YouTube en el navegador…).</summary>
    static async Task<string?> MediaAsync(IDesktop desktop, string value)
    {
        MediaCommand? command = Is(value, "siguiente", "next") ? MediaCommand.Next
            : Is(value, "anterior", "previous") ? MediaCommand.Previous
            : Is(value, "reproducir", "play") ? MediaCommand.Play
            : Is(value, "pausar", "pause") ? MediaCommand.Pause
            : null;
        return command is { } c ? await desktop.MediaAsync(c) : L.T("No entendí qué hacer con la música.");
    }

    // ── Abrir ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Una URL de los tipos que acepta el sistema o una app instalada, por su nombre.</summary>
    static string? Open(IDesktop desktop, string value)
    {
        value = value.Trim().Trim('"');
        if (value.Length == 0) return L.T("No me dijeron qué abrir.");
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            if (!desktop.Schemes.Contains(uri.Scheme)) return L.T("Ese tipo de enlace no lo abro.");
            desktop.Start(value);
            return null;
        }
        if (desktop.FindApp(value) is not { } app)
            return OperatingSystem.IsWindows() ? L.T("No encontré «{0}» en el menú Inicio.", value) : L.T("No encontré la app «{0}».", value);
        desktop.Start(app);
        return null;
    }

    /// <summary>
    /// Lo que abriría «abrir» con ese valor, sin abrirlo: la URL, la carpeta conocida o la app instalada (un dominio
    /// como «youtube.com» pasa a https). null si no hay nada que abrir: así responde otro.
    /// </summary>
    public static string? OpenTarget(string value)
    {
        if (Desktop is not { } desktop || !desktop.Kinds.Contains("abrir")) return null;
        value = value.Trim().Trim('"');
        if (value.Length == 0) return null;
        if (KnownFolderPath(desktop, value) is not null) return value;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile) return desktop.Schemes.Contains(uri.Scheme) ? value : null;
        if (Regex.IsMatch(value, @"^[\w-]+(\.[\w-]+)*\.[a-z]{2,}(/\S*)?$", RegexOptions.IgnoreCase))
            return "https://" + value;
        return desktop.FindApp(value) is not null ? value : null;
    }

    // ── Captura ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// «pantalla» (la del cursor) o «recorte». Se guarda en la carpeta de capturas y se copia al portapapeles; si se
    /// pidió mirarla, una copia queda sola en …/Claupet/capturas, la única carpeta que Claude puede leer.
    /// </summary>
    static async Task<ActionOutcome> CaptureAsync(IDesktop desktop, string value, string? question)
    {
        var crop = Is(value, "recorte", "recortar", "crop");
        if (!crop && !(value.Trim().Length == 0 || Is(value, "pantalla", "screen")))
            return Fail(L.T("Sé capturar la pantalla entera o un recorte."));
        var (file, problem) = await desktop.CaptureAsync(crop);
        if (file is null) return Fail(problem ?? L.T("Recorte cancelado."));
        return question is null ? new ActionOutcome() : new ActionOutcome(Look: CaptureFiles.ForLook(file), Question: question);
    }

    // ── Calendario, notas y recordatorios ───────────────────────────────────────────────────

    /// <summary>El evento va a un .ics que se abre con la app de calendario de la persona: ella lo guarda.</summary>
    static string? Schedule(IDesktop desktop, string value)
    {
        if (CalendarFile.Parse(value, out var problem) is not { } calendarEvent) return problem;
        return desktop.OpenCalendarFile(CalendarFile.Write(calendarEvent));
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
    static ActionOutcome ReadNotes(IDesktop desktop, string value)
    {
        if (Is(value, "abrir", "open")) return Fail(Folder(desktop, "notas"));
        var words = Is(value, "leer", "read", "") ? null : value;
        var found = Notes.Read(words);
        return new ActionOutcome(Data: found.Length > 0 ? found
            : words is null ? L.T("Todavía no hay notas.") : L.T("No hay notas con «{0}».", words));
    }

    /// <summary>
    /// {"texto","cuando"}: queda en ReminderStore (la app lo avisa a su hora) y, por si la app está cerrada a esa hora,
    /// algo del sistema la abre (ScheduleReminder).
    /// </summary>
    static string? Remind(IDesktop desktop, string value)
    {
        if (ReminderStore.Parse(value, DateTime.Now, out var problem) is not { } reminder) return problem;
        ReminderStore.Add(reminder);
        try { desktop.ScheduleReminder(reminder); }
        catch (Exception ex) { ErrorLog.Write(ex); } // sin eso, avisa igual con la app abierta (o al abrirla)
        return null;
    }

    /// <summary>«leer» (los pendientes, para Claude) o «cancelar &lt;palabras&gt;».</summary>
    static ActionOutcome ListReminders(IDesktop desktop, string value)
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
        try { desktop.CancelReminder(done.Id); } catch (Exception ex) { ErrorLog.Write(ex); }
        return new ActionOutcome(Say: L.T("Listo, ya no te recuerdo «{0}». (^_^)", done.Text));
    }

    // ── Ventanas y el equipo ────────────────────────────────────────────────────────────────

    /// <summary>«cerrar &lt;app&gt;», «minimizar …», «maximizar …», «traer …» (al frente) o «escritorio» (minimiza todo).</summary>
    static string? Window(IDesktop desktop, string value)
    {
        value = value.Trim();
        if (Is(value, "escritorio", "desktop", "mostrar escritorio", "show desktop")) return desktop.ShowDesktop();
        var parts = value.Split(' ', 2, StringSplitOptions.TrimEntries);
        var app = parts.Length > 1 ? parts[1].Trim('"', '«', '»', ' ') : "";
        var verb = parts[0];
        WindowVerb? action = Is(verb, "cerrar", "close") ? WindowVerb.Close
            : Is(verb, "minimizar", "minimize") ? WindowVerb.Minimize
            : Is(verb, "maximizar", "maximize") ? WindowVerb.Maximize
            : Is(verb, "traer", "bring", "restaurar", "restore", "mostrar", "show", "focus") ? WindowVerb.Focus
            : null;
        if (action is not { } a) return L.T("Con las ventanas sé cerrar, minimizar, maximizar o traer al frente.");
        if (app.Length == 0) return L.T("¿Qué ventana?");
        return desktop.Window(a, app);
    }

    /// <summary>
    /// Bloquear, suspender, reiniciar o apagar. Los tres últimos esperan un par de segundos, para que se vea (y se
    /// oiga) la respuesta. Reiniciar y apagar no fuerzan el cierre: una app con algo sin guardar puede detenerlo.
    /// </summary>
    static string? Computer(IDesktop desktop, string value)
    {
        if (Is(value, "bloquear", "lock")) return desktop.Computer(ComputerCommand.Lock);
        ComputerCommand? later = Is(value, "suspender", "sleep", "suspend") ? ComputerCommand.Sleep
            : Is(value, "reiniciar", "restart") ? ComputerCommand.Restart
            : Is(value, "apagar", "shut down", "shutdown") ? ComputerCommand.Shutdown
            : null;
        if (later is not { } command) return L.T("Con el equipo sé bloquear, suspender, reiniciar o apagar.");
        _ = Task.Delay(2000).ContinueWith(_ =>
        {
            if (desktop.Computer(command) is { } problem) ErrorLog.Write(new InvalidOperationException(problem));
        }, TaskScheduler.Default);
        return null;
    }

    /// <summary>0 a 100, subir o bajar (de a 10).</summary>
    static string? Brightness(IDesktop desktop, string value)
    {
        int? percent = int.TryParse(value.TrimEnd('%', ' '), out var p) ? Math.Clamp(p, 0, 100) : null;
        var up = Is(value, "subir", "up");
        if (percent is null && !up && !Is(value, "bajar", "down")) return L.T("No entendí cuánto brillo.");
        return desktop.Brightness(percent, percent is null ? (up ? 10 : -10) : 0);
    }

    // ── Batería, disco y memoria ────────────────────────────────────────────────────────────

    /// <summary>«bateria», «disco», «memoria» o «todo»: los datos vuelven a Claude para que responda.</summary>
    static ActionOutcome Status(IDesktop desktop, string value)
    {
        var all = Is(value, "todo", "all", "");
        var lines = new List<string>();
        if (all || Is(value, "bateria", "battery")) lines.Add(desktop.Battery());
        if (all || Is(value, "disco", "disk", "discos", "disks"))
            lines.AddRange(desktop.Disks().Select(d =>
                L.T("Disco {0} {1} libres de {2}.", d.Name.TrimEnd('\\'), Bytes(d.AvailableFreeSpace), Bytes(d.TotalSize))));
        if (all || Is(value, "memoria", "memory", "ram")) lines.Add(desktop.Memory());
        return lines.Count == 0 ? Fail(L.T("Sé ver la batería, el disco o la memoria.")) : new ActionOutcome(Data: string.Join("\n", lines));
    }

    internal static string Bytes(long bytes) => string.Format(CultureInfo.CurrentCulture, "{0:0.#} GB", bytes / 1e9);

    // ── Portapapeles y carpetas ─────────────────────────────────────────────────────────────

    const int MaxClipboard = 8000;

    static async Task<string?> CopyAsync(IDesktop desktop, string value) =>
        value.Length == 0 ? L.T("No me dijeron qué copiar.") : await desktop.CopyTextAsync(value);

    /// <summary>«leer»: el texto copiado vuelve a Claude; si es una imagen, Claude la mira.</summary>
    static async Task<ActionOutcome> ReadClipboardAsync(IDesktop desktop, string value, string? question)
    {
        if (!Is(value, "leer", "read", "")) return Fail(L.T("Del portapapeles sé leer lo copiado o copiar un texto."));
        var (text, image) = await desktop.ReadClipboardAsync();
        if (text is { Length: > 0 })
            return new ActionOutcome(Data: text.Length > MaxClipboard ? text[..MaxClipboard] + L.T("… (recortado)") : text);
        if (image is null) return Fail(L.T("El portapapeles está vacío."));
        return new ActionOutcome(Look: image, Question: question);
    }

    /// <summary>Donde está una carpeta conocida (Descargas, Documentos…, las capturas o las notas); null, si no es una.</summary>
    static string? KnownFolderPath(IDesktop? desktop, string name)
    {
        if (desktop is null) return null;
        KnownFolder? folder = Is(name, "descargas", "downloads") ? KnownFolder.Downloads
            : Is(name, "documentos", "documents") ? KnownFolder.Documents
            : Is(name, "escritorio", "desktop") ? KnownFolder.Desktop
            : Is(name, "imagenes", "pictures", "fotos", "photos") ? KnownFolder.Pictures
            : Is(name, "musica", "music") ? KnownFolder.Music
            : Is(name, "videos") ? KnownFolder.Videos
            : null;
        return folder is { } f ? desktop.Folder(f)
            : Is(name, "capturas", "screenshots", "capturas de pantalla") ? desktop.ScreenshotsFolder
            : Is(name, "notas", "notes") ? Notes.Folder
            : null;
    }

    /// <summary>Una carpeta conocida (KnownFolderPath) o una que exista: se abre en el explorador de archivos.</summary>
    static string? Folder(IDesktop desktop, string value)
    {
        var name = value.Trim().Trim('"');
        var path = KnownFolderPath(desktop, name);
        if (path is not null) Directory.CreateDirectory(path); // las capturas o las notas, si todavía no hay
        else
        {
            var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(name));
            if (!Directory.Exists(full)) return L.T("No encontré la carpeta «{0}».", name);
            path = full;
        }
        desktop.OpenFolder(path);
        return null;
    }
}
