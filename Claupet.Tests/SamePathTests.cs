namespace Claupet.Tests;

/// <summary>Account.SamePath: compara carpetas sin fallar con una ruta vacía.</summary>
public sealed class SamePathTests
{
    [Fact]
    public void UnaRutaVaciaNoEsNingunaCarpetaYNoFalla()
    {
        var home = Account.DefaultDir;
        Assert.False(Account.SamePath(home, ""));
        Assert.False(Account.SamePath("", home));
        Assert.False(Account.SamePath("", ""));
        Assert.False(Account.SamePath(home, "   "));
    }

    [Fact]
    public void LaMismaCarpetaConOtraBarraOMayusculasEsLaMisma()
    {
        var home = Account.DefaultDir;
        Assert.True(Account.SamePath(home, home + Path.DirectorySeparatorChar));
        if (OperatingSystem.IsWindows()) Assert.True(Account.SamePath(home, home.ToUpperInvariant()));
    }
}
