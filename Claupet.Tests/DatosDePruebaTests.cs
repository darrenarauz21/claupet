namespace Claupet.Tests;

/// <summary>Las pruebas no escriben en la carpeta de datos de verdad.</summary>
public sealed class DatosDePruebaTests
{
    [Fact]
    public void LosDatosVanAUnaCarpetaTemporal()
    {
        var real = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claupet");
        Assert.StartsWith(System.IO.Path.GetTempPath(), DataFolder.Root, StringComparison.OrdinalIgnoreCase);
        Assert.False(DataFolder.Root.StartsWith(real, StringComparison.OrdinalIgnoreCase));
    }
}
