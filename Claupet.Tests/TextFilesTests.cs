namespace Claupet.Tests;

using System.Text;

/// <summary>TextFiles: el editor abre cada archivo con su codificación y lo guarda igual (sin «Ã©» ni «�»).</summary>
public sealed class TextFilesTests
{
    const string Spanish = "Canción del año: ¿qué tal? ñandú, pingüino, “comillas” y €";

    [Fact]
    public void UnArchivoEnAnsiSeLeeConSusTildesYSeGuardaEnAnsi()
    {
        var bytes = TextFiles.Ansi.GetBytes(Spanish);
        var (text, encoding) = TextFiles.Decode(bytes);
        Assert.Equal(Spanish, text);
        Assert.Equal(TextFiles.Ansi.CodePage, encoding.CodePage);
        Assert.Same(encoding, TextFiles.ForSaving(text + " más", encoding));
    }

    [Fact]
    public void UnArchivoEnUtf8SinMarcaSigueSiendoUtf8SinMarca()
    {
        var (text, encoding) = TextFiles.Decode(new UTF8Encoding(false).GetBytes(Spanish + " 😀"));
        Assert.Equal(Spanish + " 😀", text);
        Assert.Equal(65001, encoding.CodePage);
        Assert.Empty(encoding.Preamble.ToArray());
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    public void ConMarcaSeUsaLaDeLaMarcaYSeGuardaConElla(string name)
    {
        var original = Encoding.GetEncoding(name);
        var bytes = original.GetPreamble().Concat(original.GetBytes(Spanish)).ToArray();
        var (text, encoding) = TextFiles.Decode(bytes);
        Assert.Equal(Spanish, text);
        Assert.Equal(original.CodePage, encoding.CodePage);
        Assert.Equal(original.GetPreamble(), encoding.GetPreamble());
    }

    [Fact]
    public void LoQueAnsiNoTienePasaAUtf8EnVezDePerderse()
    {
        var saving = TextFiles.ForSaving("hola 😀", TextFiles.Ansi);
        Assert.Equal(65001, saving.CodePage);
        Assert.Empty(saving.Preamble.ToArray());
    }

    [Fact]
    public void UnoNuevoSeGuardaEnUtf8SinMarca()
    {
        var saving = TextFiles.ForSaving("año", null);
        Assert.Equal(65001, saving.CodePage);
        Assert.Empty(saving.Preamble.ToArray());
    }
}
