using Avalonia.Media;

namespace Claupet.Tests;

/// <summary>Ajustes › Terminal: el fondo y el color del texto sobre los colores del tema.</summary>
public sealed class TerminalLookTests
{
    static string Hex(IBrush brush) => ((ISolidColorBrush)brush).Color.ToString();

    [Fact]
    public void LoDeSiempreDejaLosColoresDelTema()
    {
        var ink = Ink.For(false);
        Assert.Same(ink, new TerminalLook().Apply(ink));
        Assert.False(new TerminalLook().OwnBackground);
    }

    [Fact]
    public void LaTransparenciaEsLaOpacidadQueFaltaDeLasTerminales()
    {
        Assert.Equal(0, new TerminalLook().Transparency); // como hasta ahora: sólidas
        Assert.Equal(1, new TerminalLook().ShellOpacity);
        Assert.Equal(0.6, new TerminalLook { Transparency = 40 }.ShellOpacity, 6);
        Assert.Equal(0.2, new TerminalLook { Transparency = 80 }.ShellOpacity, 6);
        Assert.Equal(0.55, new TerminalLook { Transparency = 45 }.ShellOpacity, 6); // la barra va de 5 en 5
        Assert.Equal(80, TerminalLook.MaxTransparency);
    }

    [Fact]
    public void ElFondoNegroEsSiempreOscuroYElTextoSigueSiendoLegible()
    {
        var look = new TerminalLook { Background = "negro", Text = "ambar" };
        var ink = look.Apply(Ink.For(false));

        Assert.True(ink.Dark);
        Assert.Equal("#ff0c0c0c", Hex(ink.Surface));
        Assert.Equal("#fff2b35b", Hex(ink.Text)); // el ámbar para fondo oscuro
        Assert.True(look.OwnBackground);
    }

    [Fact]
    public void ElFondoProfundoUsaElDeLaVentana()
    {
        var theme = Ink.For(true);
        var ink = new TerminalLook { Background = "profundo" }.Apply(theme);
        Assert.Same(theme.Window, ink.Surface);
        Assert.Same(theme.Text, ink.Text);
    }

    [Fact]
    public void LaLetraElegidaVaDelanteDeLaDeSiempre()
    {
        Assert.Same(Ink.Mono, new TerminalLook().Mono);
        var chosen = new TerminalLook { Font = "Consolas" }.Mono;
        Assert.Equal("Consolas", chosen.Name);
        Assert.Contains("Cascadia Mono", chosen.FamilyNames);
    }
}
