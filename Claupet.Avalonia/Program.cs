using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Claupet;

static class Program
{
    public static string[] Args { get; private set; } = [];

    [STAThread]
    static int Main(string[] args)
    {
        Args = args;
        // --idioma <es|en>: ese idioma solo en esta ejecución (para probar las traducciones sin tocar los ajustes).
        L.Use(Array.IndexOf(args, "--idioma") is var li and >= 0 && li + 1 < args.Length ? args[li + 1] : SettingsStore.Load().Language);

        // --desinstalar (Windows): lo abre Configuración › Aplicaciones › Claupet › Desinstalar (Installation). Todavía sin
        // Avalonia: los mensajes son los de Windows.
        if (OperatingSystem.IsWindows() && args.Contains(Installation.UninstallFlag))
            return Installation.RunUninstaller(
                text => NativeMessage(text, MbYesNo | MbIconQuestion | MbDefButton2) == IdYes,
                text => NativeMessage(text, MbOk | MbIconInformation));

        // Las extensiones: el anfitrión de una DLL y las herramientas para autores (ExtensionCli).
        if (ExtensionCli.TryRun(args) is { } extensionCode) return extensionCode;
        // Mismos modos de línea de comandos que la app de Windows.
        if (args.Contains("--dump")) return Dump.Run();
        if (args.Contains("--hook")) return args.Contains("--permiso") ? Approvals.RunHook() : NoticeDetector.RunHook();
        if (args.Contains("--diag"))
        {
            var settings = SettingsStore.Load();
            SettingsStore.DetectNew(settings);
            Console.Write(Diagnostics.Report(settings, new SessionMonitor().Scan(settings.Accounts), 0));
            return 0;
        }
        // Los de prueba sin ventanas (--accion, --preguntar, --responder, --decir, --transcribir…): DevCommands.
        if (DevCommands.TryRun(args) is { } devCode) return devCode;

        if (OperatingSystem.IsLinux() && MissingLinuxLibraries() is { Length: > 0 } missing)
        {
            Console.Error.WriteLine(L.T("Claupet no puede abrir su ventana: faltan bibliotecas del sistema ({0}).", missing));
            Console.Error.WriteLine(L.T("Instálelas y vuelva a abrir la app:"));
            Console.Error.WriteLine("  Ubuntu, Debian, Mint:  sudo apt install libice6 libsm6 libfontconfig1");
            Console.Error.WriteLine("  Fedora:                sudo dnf install libICE libSM fontconfig");
            Console.Error.WriteLine("  Arch:                  sudo pacman -S libice libsm fontconfig");
            return 1;
        }

        // --console: Claupet Console, la ventana grande, en su propio proceso (la misma de Windows).
        if (args.Contains(ConsoleApp.Flag)) return ConsoleApp.Run(args);

        // El .exe descargado otra vez, con esta misma versión ya instalada (Windows): se abre la instalada.
        if (OperatingSystem.IsWindows() && args.Length == 0 && Installation.OpenInstalled(Environment.ProcessPath ?? "")) return 0;

        // --preview: abre la ventana aunque ya haya otra instancia (para probar cambios).
        var preview = args.Contains("--preview");
        using var mutex = new Mutex(true, ConsoleLink.TrayMutex, out var created);
        // --reiniciar: la abrió la anterior al cambiar el idioma; espera a que termine de cerrarse.
        if (!created && args.Contains("--reiniciar")) created = WaitFor(mutex);
        if (!created && !preview)
        {
            if (args.Length > 0) return 0;
            // Abrir Claupet otra vez, sin argumentos, trae Claupet Console, como cualquier app (como en Windows).
            // El mutex se suelta antes: si no, una bandeja que arranque después creería que ya hay otra.
            mutex.Dispose();
            return ConsoleApp.Run(args);
        }
        // Lo que quedó con el nombre de antes de Claupet (la carpeta de datos, la entrada de inicio) pasa a este .exe.
        if (!preview)
        {
            DataFolder.Migrate();
            try { StartupRegistration.Migrate(); } catch (Exception) { }
        }

        // Un error inesperado queda anotado: sirve para el diagnóstico (los de la interfaz, además, no cierran la app: App).
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ErrorLog.Write(e.ExceptionObject as Exception);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    /// <summary>Espera el mutex de la instancia que se cierra; si se cerró sin soltarlo, igual queda tomado.</summary>
    static bool WaitFor(Mutex mutex)
    {
        try { return mutex.WaitOne(TimeSpan.FromSeconds(15)); }
        catch (AbandonedMutexException) { return true; }
    }

    /// <summary>
    /// En Linux, Avalonia 11 carga siempre libICE y libSM (gestión de sesión de X11) y usa fontconfig.
    /// Vienen en los escritorios habituales, pero no en instalaciones mínimas.
    /// </summary>
    static string MissingLinuxLibraries()
    {
        var missing = new List<string>();
        foreach (var library in new[] { "libICE.so.6", "libSM.so.6", "libfontconfig.so.1" })
        {
            if (System.Runtime.InteropServices.NativeLibrary.TryLoad(library, out var handle))
                System.Runtime.InteropServices.NativeLibrary.Free(handle);
            else missing.Add(library);
        }
        return string.Join(", ", missing);
    }

    const uint MbOk = 0x0, MbYesNo = 0x4, MbIconQuestion = 0x20, MbIconInformation = 0x40, MbDefButton2 = 0x100, MbTopMost = 0x40000;
    const int IdYes = 6;

    /// <summary>Un mensaje de Windows, para el desinstalador (corre sin Avalonia).</summary>
    static int NativeMessage(string text, uint flags) => MessageBoxW(IntPtr.Zero, text, "Claupet", flags | MbTopMost);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .LogToTrace();
}

sealed class App : Application
{
    TrayController? _controller;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Default; // sigue el tema del sistema
        Name = "Claupet";
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Un error en la interfaz queda en el registro de errores y la app sigue, como en Claupet Console.
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            ErrorLog.Write(e.Exception);
            e.Handled = true;
        };
        var args = Program.Args;
        var preview = args.Contains("--preview");
        _controller = new TrayController(this,
            showOnStart: preview || args.Contains("--show"),
            sessionsView: args.Contains("--sessions"),
            activityView: args.Contains("--activity"),
            demo: args.Contains("--demo"),
            autoSetup: !preview || args.Contains("--setup"));
        base.OnFrameworkInitializationCompleted();
    }
}
