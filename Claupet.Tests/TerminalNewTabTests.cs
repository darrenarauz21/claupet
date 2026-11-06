using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
// El proyecto de pruebas también trae WinForms (nombres iguales a los de Avalonia).
using Control = Avalonia.Controls.Control;
using Panel = Avalonia.Controls.Panel;
using Point = Avalonia.Point;
using Window = Avalonia.Controls.Window;

namespace Claupet.Tests;

/// <summary>El menú de la pestaña nueva: elegir el proyecto, con clics de verdad (fuera de la pantalla).</summary>
public sealed class TerminalNewTabTests
{
    static (TerminalPane Pane, Window Window) Open()
    {
        var pane = new TerminalPane();
        pane.SetInk(Ink.For(true));
        var window = new Window { Width = 1200, Height = 900, Content = new Panel { Children = { pane } } };
        window.Show();
        Headless.Frame(window);
        return (pane, window);
    }

    static TextBlock? Find(Window window, string text) => window.GetVisualDescendants().OfType<TextBlock>()
        .FirstOrDefault(t => t.Text == text && t.IsEffectivelyVisible);

    static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Headless.Frame(window);
    }

    static void ClickText(Window window, string text) => Click(window, Find(window, text) ?? throw new Xunit.Sdk.XunitException($"No se ve «{text}»"));

    /// <summary>Lo que hace Windows al abrir su diálogo: la ventana deja de ser la activa.</summary>
    static void LoseFocus(Window window)
    {
        var impl = window.PlatformImpl!;
        var deactivated = impl.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .First(p => p.Name.EndsWith("Deactivated", StringComparison.Ordinal));
        ((Action)deactivated.GetValue(impl)!).Invoke();
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void LoElegidoEnElDialogoQuedaEnElMenuAunqueLaVentanaPierdaElFoco() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var chosen = Directory.CreateDirectory(Path.Combine(dir.Path, "mi-proyecto")).FullName;
        var before = TerminalPane.PickPath;
        var (pane, window) = Open();
        Window? seen = null;
        TerminalPane.PickPath = (top, _, file) =>
        {
            seen = (Window)top;
            Assert.False(file);
            LoseFocus(window);
            return Task.FromResult<string?>(chosen);
        };
        try
        {
            ClickText(window, L.T("Nueva pestaña"));
            ClickText(window, L.T("Cambiar"));
            ClickText(window, L.T("Buscar una carpeta…"));
            Dispatcher.UIThread.RunJobs();
            Headless.Frame(window);

            Assert.Same(window, seen);
            // Antes el menú se cerraba con el diálogo: la carpeta elegida no iba a ninguna parte y nada se abría.
            Assert.NotNull(Find(window, "mi-proyecto"));
            Assert.NotNull(Find(window, chosen));
            Assert.NotNull(Find(window, L.T("Abrir")));

            // Y después del diálogo, tocar fuera vuelve a cerrar el menú.
            LoseFocus(window);
            Headless.Frame(window);
            Assert.Null(Find(window, "mi-proyecto"));
        }
        finally
        {
            TerminalPane.PickPath = before;
            pane.CloseAll();
            window.Close();
        }
    });

    [Fact]
    public void CancelarElDialogoDejaElMenuComoEstaba() => Headless.Run(() =>
    {
        var before = TerminalPane.PickPath;
        var (pane, window) = Open();
        TerminalPane.PickPath = (_, _, _) => Task.FromResult<string?>(null);
        try
        {
            ClickText(window, L.T("Nueva pestaña"));
            ClickText(window, L.T("Cambiar"));
            ClickText(window, L.T("Buscar una carpeta…"));
            Dispatcher.UIThread.RunJobs();
            Headless.Frame(window);
            Assert.NotNull(Find(window, L.T("Elegir proyecto")));

            ClickText(window, L.T("Elegir proyecto"));
            Assert.NotNull(Find(window, L.T("Abrir con")));
        }
        finally
        {
            TerminalPane.PickPath = before;
            pane.CloseAll();
            window.Close();
        }
    });

    [Fact]
    public void EnUnaVentanaBajaElMenuNoSeSaleYLasFlechasSiguenCambiandoLaOpcion() => Headless.Run(() =>
    {
        var pane = new TerminalPane();
        pane.SetInk(Ink.For(true));
        var window = new Window { Width = 1000, Height = 420, Content = new Panel { Children = { pane } } };
        window.Show();
        Headless.Frame(window);
        try
        {
            ClickText(window, L.T("Nueva pestaña"));
            var card = Find(window, L.T("Abrir con"))!.GetVisualAncestors().OfType<Avalonia.Controls.Border>().Last(b => b.Width == 318);
            Assert.True(card.Bounds.Height <= window.Bounds.Height, $"El menú mide {card.Bounds.Height} en una ventana de {window.Bounds.Height}");

            // Las filas de «Abrir con» son de 32 de alto; la elegida es la que lleva fondo.
            List<bool> Rows() => card.GetVisualDescendants().OfType<Avalonia.Controls.Border>()
                .Where(r => r.Height == 32).Select(r => r.Background is not null && !Equals(r.Background, Avalonia.Media.Brushes.Transparent)).ToList();
            var before = Rows();
            Assert.True(before.Count >= 2);
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Headless.Frame(window);
            Assert.NotEqual(before, Rows());
        }
        finally
        {
            pane.CloseAll();
            window.Close();
        }
    });

    [Fact]
    public void ElProyectoDeUnArchivoEsLaRaizDeSuRepositorio()
    {
        using var dir = new TempDir();
        var root = Directory.CreateDirectory(Path.Combine(dir.Path, "repo")).FullName;
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        var deep = Directory.CreateDirectory(Path.Combine(root, "src", "app")).FullName;
        Assert.Equal(root, TerminalPane.ProjectOfFile(Path.Combine(deep, "Program.cs")));
    }

    [Fact]
    public void UnArchivoSinRepositorioUsaSuCarpeta()
    {
        // Fuera de cualquier repositorio: la carpeta temporal no debe colgar de uno (no se toma el .git de un padre).
        using var dir = new TempDir();
        var folder = Directory.CreateDirectory(Path.Combine(dir.Path, "suelta")).FullName;
        var found = TerminalPane.ProjectOfFile(Path.Combine(folder, "notas.txt"));
        if (!Directory.Exists(Path.Combine(Path.GetTempPath(), ".git"))) Assert.Equal(folder, found);
    }
}
