using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace Claupet;

/// <summary>
/// Lo común de las acciones en macOS y Linux: se hacen con los programas del sistema (sin shell: cada argumento va
/// aparte, así un nombre con comillas no puede ejecutar otra cosa), el portapapeles de Avalonia y las ventanas de
/// Claupet escondidas mientras se captura la pantalla. La app llama a las acciones fuera del hilo de la interfaz: un
/// programa que tarda (o un permiso que pregunta el sistema) no la congela.
/// </summary>
abstract class UnixDesktop(Func<IClipboard?>? clipboard, Func<Func<Task>, Task> hideWhile) : IDesktop
{
    protected static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public abstract IReadOnlySet<string> Kinds { get; }
    public IReadOnlyList<string> Schemes { get; } = ["https", "http", "spotify"];

    public abstract string? SetWallpaper(string file);
    public abstract string? SetDarkMode(bool dark);
    public abstract string? Volume(VolumeChange change);
    public abstract Task<string?> MediaAsync(MediaCommand command);
    public abstract string? FindApp(string name);
    public abstract void Start(string target);
    public abstract Task<(string? File, string? Problem)> CaptureAsync(bool crop);
    public abstract string? Window(WindowVerb verb, string app);
    public abstract string? ShowDesktop();
    public abstract string? Computer(ComputerCommand command);
    public abstract string? Brightness(int? percent, int step);
    public abstract string Battery();
    public abstract string Memory();
    public abstract IEnumerable<DriveInfo> Disks();
    public abstract string? Folder(KnownFolder folder);
    public abstract string ScreenshotsFolder { get; }
    public abstract void ScheduleReminder(Reminder reminder);
    public abstract void CancelReminder(string id);

    /// <summary>La imagen copiada como PNG en file; false si lo copiado no es una imagen (o no hay cómo leerla).</summary>
    protected abstract bool ClipboardImage(string file);

    public string? OpenCalendarFile(string file)
    {
        Start(file);
        return null;
    }

    public void OpenFolder(string path) => Start(path);

    // ── Portapapeles ────────────────────────────────────────────────────────────────────────

    public async Task<string?> CopyTextAsync(string text)
    {
        if (clipboard is null) return L.T("No encuentro el portapapeles.");
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (clipboard() is not { } board) throw new InvalidOperationException(L.T("No encuentro el portapapeles."));
            await board.SetTextAsync(text);
        });
        return null;
    }

    public async Task<(string? Text, string? ImageFile)> ReadClipboardAsync()
    {
        // Sin ventanas (--accion) no hay portapapeles de Avalonia: solo la imagen, con los programas del sistema.
        var text = clipboard is null ? null : await Dispatcher.UIThread.InvokeAsync(async () => clipboard() is { } board ? await board.TryGetTextAsync() : null);
        if (text is { Length: > 0 }) return (text, null);
        var file = CaptureFiles.ClipboardImage();
        return ClipboardImage(file) ? (null, file) : (null, null);
    }

    /// <summary>Esconde las ventanas de Claupet mientras corre work (en el hilo de la interfaz, como pide Avalonia).</summary>
    protected Task WithoutClaupetAsync(Func<Task> work) => hideWhile(work);

    // ── Programas del sistema ───────────────────────────────────────────────────────────────

    /// <summary>Corre el programa y devuelve su código y lo que escribió; (-1, "") si no está, (-2, "") si no terminó a tiempo.</summary>
    protected static (int Code, string Output) Run(string program, params string[] args) => Run(program, args, 15000);

    /// <summary>outputFile: lo que escribe va a ese archivo (una imagen); inputFile: ese archivo es su entrada.</summary>
    protected static (int Code, string Output) Run(string program, IEnumerable<string> args, int timeoutMs, string? outputFile = null,
        string? inputFile = null)
    {
        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            RedirectStandardInput = inputFile is not null,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        Process? process;
        try { process = Process.Start(start); }
        catch (Win32Exception) { return (-1, ""); }
        if (process is null) return (-1, "");
        using (process)
        {
            _ = process.StandardError.ReadToEndAsync();
            Task<string> output;
            if (outputFile is null) output = process.StandardOutput.ReadToEndAsync();
            else
            {
                // Lo que escribe es un archivo (una imagen del portapapeles): va directo, sin pasar por texto.
                output = Task.Run(async () =>
                {
                    await using (var target = File.Create(outputFile)) await process.StandardOutput.BaseStream.CopyToAsync(target);
                    return "";
                });
            }
            if (inputFile is not null)
            {
                try
                {
                    using (var source = File.OpenRead(inputFile)) source.CopyTo(process.StandardInput.BaseStream);
                    process.StandardInput.Close();
                }
                catch (IOException) { } // el programa terminó antes de leerlo todo
            }
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception) { }
                return (-2, "");
            }
            output.Wait(2000);
            return (process.ExitCode, output.IsCompletedSuccessfully ? output.Result.Trim() : "");
        }
    }

    /// <summary>Lanza el programa sin esperarlo (abrir una app, un enlace o una carpeta).</summary>
    protected static bool Launch(string program, params string[] args)
    {
        var start = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        try
        {
            Process.Start(start)?.Dispose();
            return true;
        }
        catch (Win32Exception) { return false; }
    }

    /// <summary>El programa está en el PATH.</summary>
    protected static bool Has(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir, program)));

    /// <summary>El primero de los programas que esté instalado, o null.</summary>
    protected static string? First(params string[] programs) => programs.FirstOrDefault(Has);

    /// <summary>
    /// El que se llama así: primero el exacto, después el que empieza así y por último el que lo contiene (como las
    /// apps del menú Inicio en Windows). Sin tildes ni mayúsculas.
    /// </summary>
    protected static T? Best<T>(IEnumerable<T> items, Func<T, string> name, string wanted) where T : class
    {
        var list = items.ToList();
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        wanted = wanted.Trim();
        return list.FirstOrDefault(i => SystemActions.Same(name(i), wanted))
            ?? list.Where(i => compare.IsPrefix(name(i), wanted, Loose)).OrderBy(i => name(i).Length).FirstOrDefault()
            ?? list.Where(i => compare.IndexOf(name(i), wanted, Loose) >= 0).OrderBy(i => name(i).Length).FirstOrDefault();
    }

    /// <summary>«Batería: …» con lo que se sabe (porcentaje, enchufado o no, cargando, lo que le queda).</summary>
    protected static string BatteryLine(int? percent, bool plugged, bool charging, TimeSpan? left)
    {
        var amount = percent is { } p ? $"{p} %" : L.T("desconocida");
        var state = plugged ? L.T("enchufado") + (charging ? ", " + L.T("cargando") : "")
            : left is { } time ? L.T("sin enchufar, le quedan unas {0}", Format.Duration(time))
            : L.T("sin enchufar");
        return L.T("Batería: {0} ({1}).", amount, state);
    }

    protected static string MemoryLine(long available, long total) =>
        total <= 0 ? L.T("Memoria: no se pudo leer.")
            : L.T("Memoria: {0} libres de {1} ({2} % en uso).", SystemActions.Bytes(available), SystemActions.Bytes(total), (total - available) * 100 / total);

    /// <summary>Lo que se dice cuando falta un programa para esa acción.</summary>
    protected static string Missing(string what, string programs) => L.T("Para {0} hace falta {1}: instálelo con el gestor de paquetes.", what, programs);
}
