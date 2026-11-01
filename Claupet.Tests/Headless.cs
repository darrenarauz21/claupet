using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
// El proyecto de pruebas también trae WinForms (nombres iguales a los de Avalonia).
using Application = Avalonia.Application;
using Window = Avalonia.Controls.Window;

namespace Claupet.Tests;

/// <summary>
/// Avalonia sin pantalla, para las pruebas que acomodan y dibujan controles: una sola sesión para todas, con Skia (el
/// mismo dibujo que la app, así lo que recibe el mouse sale de lo dibujado, como en la ventana).
/// </summary>
static class Headless
{
    sealed class App : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    static readonly Lazy<HeadlessUnitTestSession> Session = new(() => HeadlessUnitTestSession.StartNew(typeof(Headless)));

    /// <summary>Corre la prueba en el hilo de Avalonia y espera a que termine (sus fallas salen como en cualquier prueba).</summary>
    public static void Run(Action test) => Session.Value.Dispatch(test, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Acomoda y dibuja la ventana: después de esto, sus tamaños y lo que recibe el mouse son los de la pantalla.</summary>
    public static void Frame(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }
}
