namespace Claupet;

using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

/// <summary>
/// Claupet Console es un modo de Claupet.exe (--console): otro proceso, que se abre solo cuando se pide y se
/// cierra con su ventana. La bandeja no carga Avalonia; solo este proceso.
/// </summary>
static class ConsoleApp
{
    public const string Flag = "--console";

    public static string? Section { get; private set; }

    public static int Run(string[] args)
    {
        // --seccion=<id>: con qué sección de Ajustes se abre (la manda la bandeja).
        Section = args.FirstOrDefault(a => a.StartsWith("--seccion=", StringComparison.Ordinal))?["--seccion=".Length..];
        var settings = SettingsStore.Load(); // solo se lee: los cambios los guarda la bandeja
        L.Use(settings.Language);
        Claupet.Theme.SelectedPalette = settings.Palette;

        // Una sola ventana: si ya hay una abierta, se le pide que venga al frente y esta se cierra.
        using var mutex = new Mutex(true, @"Local\Claupet-console", out var created);
        using var wake = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Claupet-console-mostrar");
        if (!created)
        {
            Native.AllowForeground();
            wake.Set();
            return 0;
        }
        new Thread(() =>
        {
            while (wake.WaitOne()) Dispatcher.UIThread.Post(() => ConsoleApplication.Window?.BringToFront());
        }) { IsBackground = true, Name = "Mostrar la Console" }.Start();
        StartTrayIfClosed();

        return AppBuilder.Configure<ConsoleApplication>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Desde un acceso directo, Claupet puede no estar abierto: se abre (el chat y los ajustes los atiende él) y la
    /// Console se conecta en cuanto esté listo.
    /// </summary>
    static void StartTrayIfClosed()
    {
        if (Mutex.TryOpenExisting(Program.InstanceMutex, out var running))
        {
            running.Dispose();
            return;
        }
        if (Environment.ProcessPath is not { } exe) return;
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
        // Las barras de desplazamiento y los globos de ayuda de toda la Console son los del diseño.
        Themes.Register(Resources);
        RequestedThemeVariant = ThemeVariant.Default; // sigue el modo claro u oscuro de Windows
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
