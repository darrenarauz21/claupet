namespace Claupet;

/// <summary>Cambiar el volumen: un porcentaje, de a pasos (Step: +10 o -10) o silenciar/activar (Mute).</summary>
sealed record VolumeChange(int? Percent, int Step, bool? Mute);

enum MediaCommand { Play, Pause, Next, Previous }

enum WindowVerb { Close, Minimize, Maximize, Focus }

enum ComputerCommand { Lock, Sleep, Restart, Shutdown }

/// <summary>Las carpetas conocidas que se abren por su nombre (además de las capturas y las notas).</summary>
enum KnownFolder { Downloads, Documents, Desktop, Pictures, Music, Videos }

/// <summary>
/// Lo que cada sistema hace a su manera: SystemActions entiende el pedido (los valores, los permisos, los mensajes
/// comunes) y llama aquí. Los métodos que devuelven string? dan null si salió bien o lo que falló, dicho en corto.
/// Kinds dice qué acciones sabe hacer este sistema: las demás no se le ofrecen a Claude ni a las reglas sin IA.
/// </summary>
interface IDesktop
{
    IReadOnlySet<string> Kinds { get; }

    string? SetWallpaper(string file);
    string? SetDarkMode(bool dark);
    string? Volume(VolumeChange change);
    Task<string?> MediaAsync(MediaCommand command);

    /// <summary>Los tipos de enlace que «abrir» acepta (https, http y los del sistema).</summary>
    IReadOnlyList<string> Schemes { get; }

    /// <summary>La app instalada con ese nombre (lo que se le pasa a Start), o null.</summary>
    string? FindApp(string name);

    /// <summary>Abre un enlace, una app que dio FindApp o un archivo con su app.</summary>
    void Start(string target);

    /// <summary>El evento (.ics) se abre con la app de calendario.</summary>
    string? OpenCalendarFile(string file);

    /// <summary>
    /// La pantalla bajo el cursor o un recorte que marque la persona, sin las ventanas de Claupet: se guarda (en
    /// ScreenshotsFolder, con CaptureFiles.NewFile) y se copia al portapapeles. El PNG, o lo que falló.
    /// </summary>
    Task<(string? File, string? Problem)> CaptureAsync(bool crop);

    string? Window(WindowVerb verb, string app);
    string? ShowDesktop();
    string? Computer(ComputerCommand command);
    string? Brightness(int? percent, int step);

    string Battery();
    string Memory();
    /// <summary>Los discos que importan (en Linux y macOS, sin los montajes del sistema).</summary>
    IEnumerable<DriveInfo> Disks();

    Task<string?> CopyTextAsync(string text);
    /// <summary>Lo copiado: el texto o, si es una imagen, un PNG con ella (para que Claude la mire).</summary>
    Task<(string? Text, string? ImageFile)> ReadClipboardAsync();

    string? Folder(KnownFolder folder);
    /// <summary>Donde se guardan las capturas en este sistema.</summary>
    string ScreenshotsFolder { get; }
    void OpenFolder(string path);

    /// <summary>Por si la app está cerrada a esa hora: algo del sistema la abre con «--recordatorio &lt;id&gt;».</summary>
    void ScheduleReminder(Reminder reminder);
    void CancelReminder(string id);
}

/// <summary>
/// Lo de las acciones de Windows que necesita la interfaz de cada app: capturar (con su recorte, que es una ventana, y
/// sus ventanas escondidas) y el portapapeles. La app WinForms pone lo suyo; la de Avalonia, lo de ella.
/// </summary>
sealed record DesktopUi(
    Func<bool, Task<(string? File, string? Problem)>> CaptureAsync,
    Func<string, Task<string?>> CopyTextAsync,
    Func<Task<(string? Text, string? ImageFile)>> ReadClipboardAsync);

/// <summary>Las capturas guardadas: el archivo nuevo y la copia que mira Claude, sola en …/Claupet/capturas.</summary>
static class CaptureFiles
{
    static string LookDir => DataFolder.In("capturas");

    /// <summary>El archivo para una captura nueva, en la carpeta de capturas del sistema.</summary>
    public static string NewFile(string folder)
    {
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, L.T("Captura de pantalla {0:yyyy-MM-dd HHmmss}.png", DateTime.Now));
    }

    /// <summary>Una copia de la captura, sola en LookDir, para que Claude la lea sin ver las demás.</summary>
    public static string ForLook(string file)
    {
        var copy = Fresh(Path.GetFileName(file));
        File.Copy(file, copy, overwrite: true);
        return copy;
    }

    /// <summary>Un nombre libre en LookDir, después de borrar lo que había (Claude solo ve la última).</summary>
    public static string Fresh(string name)
    {
        Directory.CreateDirectory(LookDir);
        foreach (var old in Directory.EnumerateFiles(LookDir)) try { File.Delete(old); } catch (IOException) { }
        return Path.Combine(LookDir, name);
    }

    /// <summary>El nombre para una imagen copiada que Claude va a mirar.</summary>
    public static string ClipboardImage() => Fresh(L.T("Imagen copiada {0:yyyy-MM-dd HHmmss}.png", DateTime.Now));
}
