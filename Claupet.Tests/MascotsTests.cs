namespace Claupet.Tests;

/// <summary>Los personajes sobre la base del chibi (ChibiSkin): se eligen como el chibi y se dibujan enteros.</summary>
public sealed class MascotsTests
{
    public static TheoryData<string> Ids => [.. ChibiSprite.Skins.Select(s => s.Id)];

    [Fact]
    public void LosPersonajesEstanEnLaListaDeAjustes()
    {
        var ids = Mascots.Choices.Select(c => c.Id).ToList();
        Assert.Equal([Mascots.Classic, "chibi", "dokki", "gemma", "ciel"], ids.Take(5));
        Assert.Contains("ninguna", ids); // y después los estilos al actualizar
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void UnPersonajeSeEligeComoElChibi(string id)
    {
        var settings = new AppSettings();
        Mascots.Choose(settings, id);
        Assert.Equal((RefreshSprite.Mascot, id), (settings.Animation, settings.Mascot));
        Assert.Equal(id, Mascots.ChoiceOf(settings));
        Assert.True(Mascots.IsCharacter(id));
        Assert.Equal("chibi", RefreshSprite.Resolve(settings.Animation, settings.Mascot)); // el ícono, con su cabeza
    }

    [Fact]
    public void LaClasicaYLosEstilosNoSonPersonajes()
    {
        Assert.False(Mascots.IsCharacter(Mascots.Classic));
        Assert.False(Mascots.IsCharacter("asterisco"));
        Assert.Null(ChibiSprite.SkinOf(null));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void SusRejillasTienenElTamanoDeLaBase(string id)
    {
        var skin = ChibiSprite.SkinOf(id)!;
        Assert.Equal(48, skin.Body.Length);
        Assert.All(skin.Body, row => Assert.Equal(16, row.Length));
        Assert.Equal(16, skin.Head.Length);
        Assert.All(skin.Head, row => Assert.Equal(8, row.Length));
        Assert.Equal(8, skin.Armless.Length);
        Assert.All(skin.Armless, row => Assert.Equal(16, row.Length));
        if (skin.Lid is { } lid) Assert.All(lid, row => Assert.Equal(16, row.Length));
    }

    /// <summary>
    /// Cada cuadro de cada movimiento (con y sin música) y las cabezas se pintan dentro del lienzo: una letra sin color
    /// en la paleta del personaje fallaría aquí.
    /// </summary>
    [Theory]
    [MemberData(nameof(Ids))]
    public void CadaCuadroSePintaDentroDelLienzo(string id)
    {
        var skin = ChibiSprite.SkinOf(id)!;
        var cells = 0;
        void Inside(int x, int y, int w, int h, int columns, int rows)
        {
            Assert.InRange(x, 0, columns - w);
            Assert.InRange(y, 0, rows - h);
            cells++;
        }
        foreach (var move in Enum.GetValues<ChibiMove>())
        {
            var length = ChibiSprite.Length(move, skin);
            var last = -1;
            for (long t = 0; t < length; t += 10)
            {
                var i = ChibiSprite.FrameIndex(move, t, skin);
                if (i == last) continue;
                last = i;
                ChibiSprite.Paint((_, x, y, w, h) => Inside(x, y, w, h, ChibiSprite.Columns, ChibiSprite.Rows), move, t, skin);
                ChibiSprite.Paint((_, x, y, w, h) => Inside(x, y, w, h, ChibiSprite.Columns, ChibiSprite.Rows), move, t, music: true, skin);
            }
        }
        foreach (var frame in Enum.GetValues<MascotFrame>())
        {
            ChibiSprite.PaintHead((_, x, y, w, h) => Inside(x, y, w, h, ChibiSprite.HeadSize, ChibiSprite.HeadSize), frame, skin: skin);
            ChibiSprite.PaintHead((_, x, y, w, h) => Inside(x, y, w, h, ChibiSprite.SmallHeadSize, ChibiSprite.SmallHeadSize), frame, small: true, skin: skin);
        }
        Assert.True(cells > 1000);
    }

    [Fact]
    public void SoloElChibiLlevaElCangrejoEnLaLaptop()
    {
        Assert.Null(ChibiSprite.Chibi.Lid);
        Assert.All(ChibiSprite.Skins.Where(s => s != ChibiSprite.Chibi), s => Assert.NotNull(s.Lid));
    }
}
