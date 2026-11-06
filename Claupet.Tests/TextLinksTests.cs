namespace Claupet.Tests;

/// <summary>Las URL y los archivos que se pintan en azul en lo que escribe un agente (sin tocar el disco).</summary>
public sealed class TextLinksTests
{
    const string Folder = @"C:\proyecto";

    static readonly HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\proyecto\docs\plan.md",
        @"C:\proyecto\Core\Foo.cs",
        @"C:\proyecto\hola.py",
        @"C:\proyecto\Mis notas\idea uno.md",
        @"C:\otra\Program.cs",
    };

    static IReadOnlyList<TextLink> Find(string text) => TextLinks.Find(text, Folder, Files.Contains);

    static string Cut(string text, TextLink link) => text.Substring(link.Start, link.Length);

    [Fact]
    public void UnaUrlSinElPuntoNiElParentesisDeLaFrase()
    {
        const string text = "Vea https://claupet.dev/docs. También (https://github.com/darrenarauz21/claupet) y https://es.wikipedia.org/wiki/Foo_(bar).";
        var links = Find(text);
        Assert.Equal(["https://claupet.dev/docs", "https://github.com/darrenarauz21/claupet", "https://es.wikipedia.org/wiki/Foo_(bar)"],
            links.Select(l => l.Target));
        Assert.All(links, l => Assert.Equal(LinkKind.Url, l.Kind));
        Assert.Equal("https://claupet.dev/docs", Cut(text, links[0]));
    }

    [Fact]
    public void LosArchivosQueExistenRelativosALaCarpetaDeLaSesion()
    {
        const string text = "Creé `docs/plan.md` y cambié Core/Foo.cs; para probar, ejecute hola.py.";
        var links = Find(text);
        Assert.Equal([@"C:\proyecto\docs\plan.md", @"C:\proyecto\Core\Foo.cs", @"C:\proyecto\hola.py"], links.Select(l => l.Target));
        Assert.All(links, l => Assert.Equal(LinkKind.File, l.Kind));
        Assert.Equal(["docs/plan.md", "Core/Foo.cs", "hola.py"], links.Select(l => Cut(text, l)));
    }

    [Fact]
    public void LoQueNoExisteNoEsEnlace()
    {
        Assert.Empty(Find("La versión 2.5.3 trae, e.g., arreglos; vea otro/archivo.md y `python`."));
    }

    [Fact]
    public void RutasAbsolutasConLineaYConEspaciosEntreComillas()
    {
        const string text = @"El error está en C:\otra\Program.cs:42 y la idea en `Mis notas\idea uno.md`.";
        var links = Find(text);
        Assert.Equal(2, links.Count);
        Assert.Equal(@"C:\otra\Program.cs", links[0].Target);
        Assert.Equal(42, links[0].Line);
        Assert.Equal(@"C:\otra\Program.cs:42", Cut(text, links[0]));
        Assert.Equal(@"C:\proyecto\Mis notas\idea uno.md", links[1].Target);
        Assert.Null(links[1].Line);
    }

    static readonly HashSet<string> Folders = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\proyecto\docs",
        @"C:\Program Files\GitHub CLI",
        @"C:\paquetes\2.6.0",
    };

    static readonly HashSet<string> Absolutes = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\Program Files\GitHub CLI\gh.exe",
        @"C:\otra\Qué es Claupet (1).pdf",
        @"C:\otra\Program.cs",
    };

    static IReadOnlyList<TextLink> FindAll(string text) => TextLinks.Find(text, Folder, Absolutes.Contains, Folders.Contains);

    [Theory]
    [InlineData(@"Está en C:\proyecto\docs y nada más.", @"C:\proyecto\docs", @"C:\proyecto\docs")]
    [InlineData(@"Está en C:\paquetes\2.6.0\", @"C:\paquetes\2.6.0", @"C:\paquetes\2.6.0\")]
    [InlineData(@"Abra C:/proyecto/docs.", @"C:\proyecto\docs", @"C:/proyecto/docs")]
    [InlineData(@"Abra C://proyecto//docs ahora", @"C:\proyecto\docs", @"C://proyecto//docs")]
    [InlineData(@"Con espacios: C:\Program Files\GitHub CLI, y sigue", @"C:\Program Files\GitHub CLI", @"C:\Program Files\GitHub CLI")]
    public void UnaCarpetaAbsolutaEsEnlace(string text, string target, string shown)
    {
        var link = Assert.Single(FindAll(text));
        Assert.Equal(LinkKind.Folder, link.Kind);
        Assert.Equal(target, link.Target, ignoreCase: true);
        Assert.Equal(shown, Cut(text, link));
    }

    [Theory]
    [InlineData(@"Instalado en C:\Program Files\GitHub CLI\gh.exe.", @"C:\Program Files\GitHub CLI\gh.exe", @"C:\Program Files\GitHub CLI\gh.exe")]
    [InlineData(@"Vea C:/otra/Qué es Claupet (1).pdf ya", @"C:\otra\Qué es Claupet (1).pdf", @"C:/otra/Qué es Claupet (1).pdf")]
    [InlineData(@"Vea C://otra//Program.cs por favor", @"C:\otra\Program.cs", @"C://otra//Program.cs")]
    [InlineData(@"Vea file:///C:/otra/Program.cs por favor", @"C:\otra\Program.cs", @"file:///C:/otra/Program.cs")]
    [InlineData(@"Vea file:///C:/otra/Qu%C3%A9%20es%20Claupet%20(1).pdf ya", @"C:\otra\Qué es Claupet (1).pdf", @"file:///C:/otra/Qu%C3%A9%20es%20Claupet%20(1).pdf")]
    [InlineData(@"(C:\otra\Program.cs)", @"C:\otra\Program.cs", @"C:\otra\Program.cs")]
    public void UnArchivoAbsolutoEsEnlaceSeaCualSeaSuForma(string text, string target, string shown)
    {
        var link = Assert.Single(FindAll(text));
        Assert.Equal(LinkKind.File, link.Kind);
        Assert.Equal(target, link.Target, ignoreCase: true);
        Assert.Equal(shown, Cut(text, link));
    }

    [Fact]
    public void DosRutasEnUnaLineaSonDosEnlaces()
    {
        const string text = @"De C:\otra\Program.cs:7 a C:\Program Files\GitHub CLI\gh.exe";
        var links = FindAll(text);
        Assert.Equal([@"C:\otra\Program.cs", @"C:\Program Files\GitHub CLI\gh.exe"], links.Select(l => l.Target));
        Assert.Equal(7, links[0].Line);
    }

    [Fact]
    public void UnaRutaAbsolutaQueNoExisteNoEsEnlaceNiSuPadre()
    {
        Assert.Empty(FindAll(@"Vea C:\proyecto\docs\no-existe.txt y Z:\nada\aqui."));
    }

    [Fact]
    public void UnaRutaDentroDeUnaUrlEsParteDeLaUrl()
    {
        var links = Find("Bájelo de https://ejemplo.com/hola.py ahora.");
        Assert.Equal(LinkKind.Url, Assert.Single(links).Kind);
    }
}
