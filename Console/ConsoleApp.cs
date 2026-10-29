namespace Claupet;

using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

/// <summary>
/// Claupet Console es un modo de Claupet (--console): otro proceso, que se abre solo cuando se pide y se cierra con su
/// ventana. Es la misma en Windows, Linux y macOS: la atiende la bandeja de cada app por la tubería de ConsoleLink.
/// </summary>
static class ConsoleApp
{
    public const string Flag = ConsoleLink.Flag;

    public static string? Section { get; private set; }

    /// <summary>Los archivos que se pidió abrir en el editor (Claupet.exe --console archivo…).</summary>
    public static IReadOnlyList<string> Files { get; private set; } = [];

    /// <summary>
    /// Por aquí una Console que se abre con otra ya abierta le pide venir al frente (y le pasa sus archivos, uno por
    /// línea). Una tubería con nombre, porque los eventos con nombre solo existen en Windows.
    /// </summary>
    static string WakePipe => "Claupet.ConsoleWake." + Environment.UserName;

    public static int Run(string[] args)
    {
        // --seccion=<id>: con qué sección de Ajustes se abre (la manda la bandeja).
        Section = args.FirstOrDefault(a => a.StartsWith("--seccion=", StringComparison.Ordinal))?["--seccion=".Length..];
        Files = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal) && File.Exists(a)).Select(Path.GetFullPath).ToList();
        var settings = SettingsStore.Load(); // solo se lee: los cambios los guarda la bandeja
        L.Use(settings.Language);
        Claupet.Theme.SelectedPalette = settings.Palette;

        // Una sola ventana: si ya hay una abierta, se le pide que venga al frente (con los archivos) y esta se cierra.
        using var mutex = new Mutex(true, ConsoleLink.ConsoleMutex, out var created);
        if (!created)
        {
            Native.AllowForeground();
            Wake(Files);
            return 0;
        }
        _ = Task.Run(ListenAsync);
        StartTrayIfClosed();

        return AppBuilder.Configure<ConsoleApplication>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Le pide a la Console abierta que venga al frente, con estos archivos.</summary>
    static void Wake(IReadOnlyList<string> files)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", WakePipe, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(2000);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { NewLine = "\n" };
            foreach (var file in files) writer.WriteLine(file);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
    }

    /// <summary>Espera los pedidos de otras Console que se abren: viene al frente y abre sus archivos.</summary>
    static async Task ListenAsync()
    {
        while (true)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(WakePipe, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync();
                using var reader = new StreamReader(pipe, new UTF8Encoding(false));
                var files = new List<string>();
                while (await reader.ReadLineAsync() is { } line)
                    if (File.Exists(line)) files.Add(line);
                Dispatcher.UIThread.Post(() =>
                {
                    ConsoleApplication.Window?.OpenFiles(files);
                    ConsoleApplication.Window?.BringToFront();
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ErrorLog.Write(ex);
                await Task.Delay(1000);
            }
        }
    }

    /// <summary>
    /// Desde un acceso directo, Claupet puede no estar abierto: se abre (el chat y los ajustes los atiende él) y la
    /// Console se conecta en cuanto esté listo.
    /// </summary>
    static void StartTrayIfClosed()
    {
        if (ConsoleLink.TrayRunning() || Environment.ProcessPath is not { } exe) return;
        try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { ErrorLog.Write(ex); }
    }
}

sealed class ConsoleApplication : Application
{
    public static ConsoleWindow? Window { get; private set; }

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        // El tema del motor del editor (sus colores los pone EditorPane).
        Styles.Add(new StyleInclude(new Uri("avares://Claupet/")) { Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml") });
        // Las barras de desplazamiento y los globos de ayuda de toda la Console son los del diseño.
        Themes.Register(Resources);
        RequestedThemeVariant = ThemeVariant.Default; // sigue el modo claro u oscuro del sistema
        Name = "Claupet Console";
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Un error al dibujar una sección queda en el registro de errores y no cierra la ventana.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            ErrorLog.Write(e.Exception);
            e.Handled = true;
        };
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = Window = new ConsoleWindow(ConsoleApp.Section);
        base.OnFrameworkInitializationCompleted();
    }
}

static class Native
{
    /// <summary>
    /// Deja que Claupet traiga sus ventanas al frente (grabar una tecla, escribir la ciudad…): sin esto, Windows
    /// solo las hace parpadear en la barra de tareas porque la que está al frente es la Console.
    /// </summary>
    public static void AllowForeground()
    {
        if (OperatingSystem.IsWindows()) AllowSetForegroundWindow(-1);
    }

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);
}
