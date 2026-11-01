using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
// El proyecto de pruebas también trae WinForms (nombres iguales a los de Avalonia).
using Border = Avalonia.Controls.Border;
using Brushes = Avalonia.Media.Brushes;
using Control = Avalonia.Controls.Control;
using Point = Avalonia.Point;
using TextBox = Avalonia.Controls.TextBox;
using Window = Avalonia.Controls.Window;

namespace Claupet.Tests;

/// <summary>
/// Cómo se acomoda Claupet Terminal en pantalla (fuera de ella, con Avalonia.Headless): la caja de abajo al cambiar de
/// pestaña, quién recibe el mouse en la pantalla completa de un shell y el marco que le da su tamaño a la consola.
/// </summary>
public sealed class TerminalPaneLayoutTests
{
    const string Account = "cuenta-de-prueba";

    [Fact]
    public void LaCajaDeClaudeCodeVuelveASuLugarDespuesDeUnaTerminal() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var (pane, window) = Open(1400, 900);
        var claude = ClaudeTab(dir.Path);
        pane.Add(claude);
        Headless.Frame(window);
        var (input, _) = Composer(pane);
        var before = In(input, pane);

        pane.Add(ShellTab(dir.Path));
        Headless.Frame(window);
        pane.Select(claude);
        Headless.Frame(window);

        // La de un shell la dejaba en la columna 1, la de la cuenta y el modelo: quedaba encima de ellos.
        var (again, chip) = Composer(pane);
        Assert.Same(input, again);
        Assert.Equal(0, Grid.GetColumn(input));
        Assert.Equal(before, In(input, pane));
        Assert.False(In(input, pane).Intersects(In(chip, pane)));
        Close(pane, window);
    });

    [Fact]
    public void LaCajaDeUnaTerminalNoHeredaLoQueLeDejoLaDeClaudeCode() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        // Angosta: la cuenta y el modelo pasan debajo y la caja de Claude Code ocupa tres columnas.
        var (pane, window) = Open(520, 700);
        pane.Add(ClaudeTab(dir.Path));
        Headless.Frame(window);
        var (input, _) = Composer(pane);
        Assert.Equal(3, Grid.GetColumnSpan(input));

        pane.Add(ShellTab(dir.Path));
        Headless.Frame(window);

        Assert.Equal(0, Grid.GetRow(input));
        Assert.Equal(1, Grid.GetColumn(input));
        Assert.Equal(1, Grid.GetColumnSpan(input));
        Close(pane, window);
    });

    [Fact]
    public void LaPantallaCompletaRecibeElMouseTambienDondeNoHayTexto() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        using var shell = new ShellSession(new ShellChoice(ShellKind.PowerShell, "PowerShell", "pwsh.exe"), dir.Path, a => a());
        shell.Screen.Write("hola\r\n");
        var look = new TerminalLook();
        var screen = new TerminalScreen(shell, Ink.For(true), look.Mono, look.Size, look);
        var window = new Window { Width = 800, Height = 600, Content = new Border { Background = Brushes.Gray, Padding = new Thickness(10), Child = screen } };
        window.Show();
        Headless.Frame(window);

        Assert.Same(screen, window.InputHitTest(new Point(14, 14)));   // sobre «hola»
        Assert.Same(screen, window.InputHitTest(new Point(400, 300))); // en celdas vacías: antes, el marco
        window.Close();
    });

    [Theory]
    [InlineData(1000, 700)]
    [InlineData(1367, 769)]
    [InlineData(801, 603)]
    public void ElMarcoDeLaPantallaCompletaMideLoQueRestaFitShell(int width, int height) => Headless.Run(() =>
    {
        var inside = new Border();
        var frame = TerminalPane.ShellFrame(inside, Ink.For(true));
        Grid.SetRow(frame, 1);
        var grid = new Grid { RowDefinitions = new RowDefinitions { new RowDefinition(TerminalPane.ShellHeaderHeight, GridUnitType.Pixel), new RowDefinition(1, GridUnitType.Star) } };
        grid.Children.Add(frame);
        var window = new Window { Width = width, Height = height, Content = grid };
        window.Show();
        Headless.Frame(window);

        // Lo que FitShell le resta al panel es justo lo que el marco le quita a la pantalla (con el borde).
        Assert.Equal(width - TerminalPane.FrameX, inside.Bounds.Width, 3);
        Assert.Equal(height - TerminalPane.FrameY, inside.Bounds.Height, 3);
        window.Close();
    });

    [Fact]
    public void ConTransparenciaElMarcoDejaVerElFondo() => Headless.Run(() =>
    {
        var ink = Ink.For(true);
        Assert.Same(ink.Raised, TerminalPane.ShellFrame(new Border(), ink).Background); // sin transparencia, el de siempre

        var seen = Assert.IsAssignableFrom<ISolidColorBrush>(TerminalPane.ShellFrame(new Border(), ink, opacity: 0.6).Background);
        Assert.Equal(((ISolidColorBrush)ink.Raised).Color, seen.Color);
        Assert.Equal(0.6, seen.Opacity, 6);
    });

    [Fact]
    public void LosAjustesDeLaTerminalSonUnaPaginaDeAjustesYNoTapanLaSesion() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        var (pane, window) = Open(1400, 900);
        pane.Add(ClaudeTab(dir.Path));
        Headless.Frame(window);
        var (input, _) = Composer(pane);

        // La página va en otra ventana (en la Console, en Ajustes): la sesión sigue en la Terminal, con su caja.
        var settings = new Window { Width = 900, Height = 900, Content = new ScrollViewer { Content = pane.SettingsPage() } };
        settings.Show();
        Headless.Frame(settings);
        Headless.Frame(window);

        Assert.Same(input, Composer(pane).Input);
        var slider = Assert.Single(settings.GetVisualDescendants().OfType<Slider>());
        Assert.Equal(TerminalLook.MaxTransparency, slider.Maximum);
        settings.Close();
        Close(pane, window);
    });

    // ── Andamio ─────────────────────────────────────────────────────────────────────────────

    static (TerminalPane Pane, Window Window) Open(double width, double height)
    {
        var pane = new TerminalPane();
        var window = new Window { Width = width, Height = height, Content = pane };
        window.Show();
        Headless.Frame(window);
        return (pane, window);
    }

    static void Close(TerminalPane pane, Window window)
    {
        pane.CloseAll();
        window.Close();
    }

    static TerminalPane.Tab ClaudeTab(string folder) => new(new TerminalAgent(new AgentOptions(folder), a => a()), Account);

    static TerminalPane.Tab ShellTab(string folder) => new(new TerminalAgent(new AgentOptions(folder), _ => { }), "PowerShell")
    {
        Shell = new ShellSession(new ShellChoice(ShellKind.PowerShell, "PowerShell", "pwsh.exe"), folder, a => a()),
    };

    /// <summary>La caja de la pestaña de Claude Code y la etiqueta de su cuenta (en la misma fila de la caja de abajo).</summary>
    static (TextBox Input, Control Chip) Composer(TerminalPane pane)
    {
        var label = pane.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Text == Account && t.GetVisualAncestors().OfType<Grid>().First().Children.OfType<TextBox>().Any());
        var line = label.GetVisualAncestors().OfType<Grid>().First();
        return (line.Children.OfType<TextBox>().Single(), label.GetVisualAncestors().OfType<Border>().First());
    }

    /// <summary>Dónde está un control dentro de otro.</summary>
    static Rect In(Visual visual, Visual root) => new(visual.TranslatePoint(default, root) ?? default, visual.Bounds.Size);
}
