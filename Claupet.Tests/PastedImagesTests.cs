namespace Claupet.Tests;

/// <summary>Qué texto pegado se adjunta como imagen («[Imagen #N]»): la ruta de un archivo de imagen o la URL de una imagen.</summary>
public sealed class PastedImagesTests
{
    const string Folder = @"C:\proyecto";
    static readonly HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase) { @"C:\proyecto\capturas\error.png", @"C:\Fotos\gato.JPG" };

    static (bool Url, string Target)? Reference(string text) => PastedImages.Reference(text, Folder, Files.Contains);

    [Fact]
    public void LaRutaDeUnaImagenQueExisteSeAdjunta()
    {
        Assert.Equal((false, @"C:\proyecto\capturas\error.png"), Reference(@"capturas\error.png"));
        Assert.Equal((false, @"C:\Fotos\gato.JPG"), Reference("  \"C:\\Fotos\\gato.JPG\"  ")); // con comillas, como la copia el Explorador
        Assert.Equal((false, @"C:\Fotos\gato.JPG"), Reference("file:///C:/Fotos/gato.JPG"));
    }

    [Fact]
    public void LaUrlDeUnaImagenSeBajaYSeAdjunta()
    {
        Assert.Equal((true, "https://ejemplo.com/img/diagrama.webp?v=2"), Reference("https://ejemplo.com/img/diagrama.webp?v=2"));
        Assert.Equal("image/webp", PastedImages.MediaTypeOf("diagrama.webp"));
    }

    [Fact]
    public void OtroTextoSePegaComoSiempre()
    {
        Assert.Null(Reference("https://ejemplo.com/docs")); // una página, no una imagen
        Assert.Null(Reference(@"capturas\no-existe.png"));
        Assert.Null(Reference(@"C:\Fotos\dibujo.svg")); // los agentes no reciben SVG
        Assert.Null(Reference("mire capturas\\error.png y dígame")); // una frase, no una ruta sola
        Assert.Null(Reference("C:\\Fotos\\gato.JPG\nC:\\Fotos\\otro.png")); // varias líneas
    }
}
