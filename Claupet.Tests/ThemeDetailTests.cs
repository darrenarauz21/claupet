using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using SkiaSharp;
using Color = System.Drawing.Color;
using Size = Avalonia.Size;

namespace Claupet.Tests;

/// <summary>
/// Lo que separa las calidades de un tema: lo que se le pide al modelo y lo que el tema puede expresar (motivos en pixel
/// art y una segunda escena). Cambian CustomThemes.Folder y CustomThemes.Ask, que son estáticos.
/// </summary>
[Collection("Assistant.External")]
public sealed class ThemeDetailTests
{
    const string Pumpkin = """["..33..", ".1111.", "111112", "141412", "111112", ".1122."]""";

    static ThemeScene? SceneOf(string scene) =>
        CustomThemes.Parse($$"""{ "name": "x", "light": { "accent": "#2F7FC1" }, "scene": {{scene}} }""")?.Scene;

    [Fact]
    public void CadaCalidadPideMasDetalle()
    {
        var (fast, medium, high) = (CustomThemes.Prompt("otoño", "rapida"), CustomThemes.Prompt("otoño", "media"), CustomThemes.Prompt("otoño", "alta"));

        Assert.DoesNotContain("sprites", fast);
        Assert.DoesNotContain("scene.over", fast);
        Assert.Equal(fast, CustomThemes.Prompt("otoño"));

        Assert.Contains("scene.sprites", medium);
        Assert.Contains("1 o 2 motivos", medium);
        Assert.DoesNotContain("scene.over", medium);

        Assert.Contains("de 2 a 4 motivos", high);
        Assert.Contains("scene.over", high);
    }

    [Fact]
    public async Task ElPedidoSaleDeLaCalidadElegida()
    {
        using var dir = new TempDir();
        var (folder, ask) = (CustomThemes.Folder, CustomThemes.Ask);
        CustomThemes.Folder = dir.Path;
        string? asked = null;
        CustomThemes.Ask = (request, _) =>
        {
            asked ??= request.Prompt;
            return Task.FromResult(new AssistantResult("""{ "name": "x", "light": { "accent": "#2F7FC1" } }""", null, request.Model, null, null));
        };
        try
        {
            await CustomThemes.CreateAsync("otoño", new AppSettings { ThemeQuality = "media" });
            Assert.Equal(CustomThemes.Prompt("otoño", "media"), asked);
        }
        finally
        {
            (CustomThemes.Folder, CustomThemes.Ask) = (folder, ask);
        }
    }

    const string Plain = """{ "name": "Sin motivos", "light": { "accent": "#2F7FC1" }, "scene": { "kind": "nieve" } }""";
    const string Drawn = """{ "name": "Con motivos", "light": { "accent": "#2F7FC1" }, "scene": { "kind": "nieve", "sprites": [{ "rows": ["111", "111"] }] } }""";

    /// <summary>Crea un tema con estas respuestas, una por llamada; devuelve el resultado, los pedidos y la carpeta.</summary>
    static async Task<(ThemeResult Result, List<AssistantRequest> Asked, string[] Files)> Create(string quality, params string[] answers)
    {
        using var dir = new TempDir();
        var (folder, ask) = (CustomThemes.Folder, CustomThemes.Ask);
        CustomThemes.Folder = dir.Path;
        var asked = new List<AssistantRequest>();
        CustomThemes.Ask = (request, _) =>
        {
            asked.Add(request);
            var answer = answers[Math.Min(asked.Count, answers.Length) - 1];
            return Task.FromResult(answer.Length == 0
                ? new AssistantResult("", null, null, null, "se cortó")
                : new AssistantResult(answer, null, request.Model, null, null));
        };
        try
        {
            var result = await CustomThemes.CreateAsync("nieve", new AppSettings { ThemeQuality = quality });
            return (result, asked, Directory.GetFiles(dir.Path).Select(Path.GetFileName).OfType<string>().ToArray());
        }
        finally { (CustomThemes.Folder, CustomThemes.Ask) = (folder, ask); }
    }

    [Theory]
    [InlineData("media")]
    [InlineData("alta")]
    public async Task SinMotivosSePideOtraVezConElMismoModelo(string quality)
    {
        var (result, asked, _) = await Create(quality, Plain, Drawn);
        Assert.Equal(2, asked.Count);
        Assert.Equal(asked[0].Model, asked[1].Model);
        Assert.Contains("Sin motivos", asked[1].Prompt);
        Assert.Equal("Con motivos", result.Theme?.Name);
        Assert.Single(result.Theme!.Scene!.Sprites!);
        Assert.Null(result.Note);
    }

    [Fact]
    public async Task RapidaNoPideMotivosNiOtraVez()
    {
        var (result, asked, _) = await Create("rapida", Plain);
        Assert.Single(asked);
        Assert.Null(result.Note);
    }

    [Fact]
    public async Task ConMotivosALaPrimeraBastaUnPedido()
    {
        var (result, asked, _) = await Create("media", Drawn);
        Assert.Single(asked);
        Assert.Null(result.Note);
    }

    [Theory]
    [InlineData(Plain)]
    [InlineData("")]
    public async Task SiNoLleganMotivosQuedaElTemaYSeAvisa(string second)
    {
        var (result, asked, files) = await Create("media", Plain, second);
        Assert.Equal(2, asked.Count);
        Assert.Equal("Sin motivos", result.Theme?.Name);
        Assert.Contains(L.T("Claude no dibujó motivos para este tema."), result.Note);
        Assert.Contains("ultima-respuesta-sin-motivos.txt", files);
    }

    [Fact]
    public void LosMotivosAlLadoDeLaEscenaTambienValen() =>
        Assert.Single(CustomThemes.Parse("""{ "light": { "accent": "#2F7FC1" }, "scene": { "kind": "nieve" }, "sprites": [{ "rows": ["111"] }] }""")!.Scene!.Sprites!);

    [Fact]
    public void ElEjemploDelPedidoSeLeeComoUnTema()
    {
        var prompt = CustomThemes.Prompt("otoño", "alta");
        var start = prompt.LastIndexOf("no lo copies", StringComparison.Ordinal);
        var scene = CustomThemes.Parse(prompt[prompt.IndexOf('{', start)..])?.Scene;
        Assert.NotNull(scene);
        Assert.Equal("suelo", Assert.Single(scene.Sprites!).Motion);
        Assert.Equal("estrellas", scene.Over?.Kind);
    }

    [Fact]
    public void UnTemaSinDetalleSeLeeComoSiempre()
    {
        var scene = SceneOf("""{ "kind": "nieve" }""");
        Assert.Null(scene?.Sprites);
        Assert.Null(scene?.Over);
    }

    [Fact]
    public void LosMotivosSeLeenYSeCorrigen()
    {
        var scene = SceneOf($$"""
            { "kind": "nieve", "sprites": [
                { "motion": "Suelo", "count": 40, "rows": {{Pumpkin}} },
                { "motion": "volar", "rows": ["11", "x1 2"] } ] }
            """);
        Assert.Equal(2, scene!.Sprites!.Length);
        Assert.Equal(("suelo", 6), (scene.Sprites[0].Motion, scene.Sprites[0].Count));
        // Lo desconocido deriva; las filas quedan parejas y lo que no es un color, transparente.
        Assert.Equal("flota", scene.Sprites[1].Motion);
        Assert.Equal(1, scene.Sprites[1].Count);
        Assert.Equal(["11..", ".1.2"], scene.Sprites[1].Rows);
    }

    [Theory]
    [InlineData("""{ "rows": [] }""")]
    [InlineData("""{ "rows": "111" }""")]
    [InlineData("""{ "rows": ["1", ".", "."] }""")]
    [InlineData("""{ "rows": ["11111111111111111"] }""")]
    [InlineData("""{ "rows": ["1", "1", "1", "1", "1", "1", "1", "1", "1", "1", "1", "1", "1", "1", "1", "1", "1"] }""")]
    [InlineData("""{ "rows": ["111", 5] }""")]
    public void UnMotivoQueNoSePuedeDibujarSePierde(string sprite) =>
        Assert.Null(SceneOf($$"""{ "kind": "nieve", "sprites": [{{sprite}}] }""")!.Sprites);

    [Fact]
    public void NoHayMasDeCuatroMotivosNiDeSeisColores()
    {
        var sprites = string.Join(",", Enumerable.Repeat($$"""{ "rows": {{Pumpkin}} }""", 9));
        var colors = string.Join(",", Enumerable.Repeat("\"#123456\"", 9));
        var scene = SceneOf($$"""{ "kind": "nieve", "colors": [{{colors}}], "sprites": [{{sprites}}] }""");
        Assert.Equal(4, scene!.Sprites!.Length);
        Assert.Equal(6, scene.Colors.Length);
    }

    [Fact]
    public void LaSegundaEscenaTomaLosColoresDeLaPrimeraYNoSeAnida()
    {
        var scene = SceneOf("""
            { "kind": "ciudad", "colors": ["#112233"],
              "over": { "kind": "nieve", "density": 9, "over": { "kind": "lluvia" }, "sprites": [{ "rows": ["111"] }] } }
            """);
        Assert.Equal("nieve", scene!.Over!.Kind);
        Assert.Equal(1, scene.Over.Density);
        Assert.Equal(Color.FromArgb(255, 0x11, 0x22, 0x33), Assert.Single(scene.Over.Colors));
        Assert.Null(scene.Over.Over);
        Assert.Null(scene.Over.Sprites);
    }

    [Theory]
    [InlineData("playa", "ciudad")] // un paisaje sobre otro no se lee
    [InlineData("nieve", "nieve")]
    [InlineData("nieve", "volcan")]
    public void NoVaEncimaCualquierEscena(string under, string over) =>
        Assert.Null(SceneOf($$"""{ "kind": "{{under}}", "over": { "kind": "{{over}}" } }""")!.Over);

    [Fact]
    public void ElDetalleSobreviveAlGuardarlo()
    {
        var theme = CustomThemes.Parse($$"""
            { "name": "x", "light": { "accent": "#2F7FC1" }, "scene": { "kind": "ciudad", "colors": ["#112233", "#445566"],
              "sprites": [{ "motion": "cruza", "count": 2, "rows": {{Pumpkin}} }], "over": { "kind": "nieve", "density": 0.2 } } }
            """)!;
        var again = CustomThemes.Parse(CustomThemes.ToJson(theme))!.Scene!;
        var sprite = Assert.Single(again.Sprites!);
        Assert.Equal(("cruza", 2), (sprite.Motion, sprite.Count));
        Assert.Equal(theme.Scene!.Sprites![0].Rows, sprite.Rows);
        Assert.Equal(("nieve", 0.2), (again.Over!.Kind, again.Over.Density));
        Assert.Equal(theme.Scene.Over!.Colors, again.Over.Colors);
    }

    /// <summary>Cuántos píxeles dejó de ser fondo la escena.</summary>
    static int Painted(ThemeScene scene)
    {
        var painted = 0;
        Headless.Run(() =>
        {
            var layer = new SceneLayer(pixel: 4, alpha: 1);
            layer.Show(scene);
            var window = new Window { Width = 400, Height = 300, Content = new Grid { Background = Avalonia.Media.Brushes.Black, Children = { layer } } };
            window.Show();
            try
            {
                using var png = new MemoryStream();
                window.CaptureRenderedFrame()!.Save(png);
                png.Position = 0;
                using var frame = SKBitmap.Decode(png);
                for (var y = 0; y < frame.Height; y += 2)
                    for (var x = 0; x < frame.Width; x += 2)
                        if (frame.GetPixel(x, y) != SKColors.Black) painted++;
            }
            finally { window.Close(); }
        });
        return painted;
    }

    [Fact]
    public void LosMotivosSeDibujan()
    {
        var plain = new ThemeScene("ciudad", [Color.White], 0.1, 1);
        var drawn = plain with { Sprites = [new ThemeSprite(["1111", "1111", "1111", "1111"], "cielo", 2)] };
        Assert.True(Painted(drawn) > Painted(plain));
    }

    [Fact]
    public void LaVistaPreviaNoLlevaTodo() => Headless.Run(() =>
    {
        var motif = new ThemeSprite(["111", "111"], "cruza", 6);
        var layer = new SceneLayer(pixel: 2, alpha: 1, sample: true);
        layer.Show(new ThemeScene("nieve", [Color.White], 0.1, 1, [motif, motif, motif, motif], new ThemeScene("lluvia", [Color.White], 0.1, 1)));
        Assert.Empty(layer.GetVisualChildren());
    });

    static ThemeCard[] Cards(int count, int selected = -1, bool creating = false) =>
        Enumerable.Range(0, count).Select(i => new ThemeCard($"sin-archivo-{i}", $"Tema {i}", i == selected, creating && i == count - 1)).ToArray();

    [Fact]
    public void LaGaleriaVaDeSeisEnSeis() => Headless.Run(() =>
    {
        var gallery = new ThemeGallery((_, _) => { });
        gallery.Show(Cards(6), Ink.Of(null, true));
        Assert.Equal((1, 6), (gallery.Pages, gallery.Shown));
        gallery.Show(Cards(8), Ink.Of(null, true));
        Assert.Equal((2, 0, 6), (gallery.Pages, gallery.Page, gallery.Shown));
        gallery.Go(-1);
        Assert.Equal(0, gallery.Page);
        gallery.Go(1);
        Assert.Equal((1, 2), (gallery.Page, gallery.Shown));
        gallery.Go(1);
        Assert.Equal(1, gallery.Page);
        // Al borrarse las de la última página, vuelve a la que queda.
        gallery.Show(Cards(5), Ink.Of(null, true));
        Assert.Equal((1, 0, 5), (gallery.Pages, gallery.Page, gallery.Shown));
    });

    [Fact]
    public void LaGaleriaAbreDondeEstaLoQueImporta() => Headless.Run(() =>
    {
        var gallery = new ThemeGallery((_, _) => { });
        gallery.Show(Cards(14, selected: 7), Ink.Of(null, true));
        Assert.Equal(1, gallery.Page);
        // Mirar otra página no se pierde con otro aviso de la bandeja, aunque cambie algo más.
        gallery.Go(1);
        gallery.Show(Cards(15, selected: 7), Ink.Of(null, true));
        Assert.Equal(2, gallery.Page);
        // Lo que se está creando y lo que se acaba de elegir se traen a la vista.
        gallery.Go(-2);
        gallery.Show(Cards(16, selected: 7, creating: true), Ink.Of(null, true));
        Assert.Equal(2, gallery.Page);
        gallery.Show(Cards(16, selected: 13), Ink.Of(null, true));
        Assert.Equal(2, gallery.Page);
    });

    [Fact]
    public void LasPaginasMidenLoMismo() => Headless.Run(() =>
    {
        var gallery = new ThemeGallery((_, _) => { });
        var window = new Window { Width = 800, Height = 900, Content = new ScrollViewer { Content = gallery } };
        window.Show();
        gallery.Show(Cards(7), Ink.Of(null, true));
        Headless.Frame(window);
        var full = gallery.Bounds.Height;
        gallery.Go(1);
        Headless.Frame(window);
        Assert.Equal(1, gallery.Shown);
        Assert.Equal(full, gallery.Bounds.Height);
        window.Close();
    });

    [Fact]
    public void CambiarDeEscenaQuitaLaDeEncima() => Headless.Run(() =>
    {
        var scene = new ThemeScene("nieve", [Color.White], 0.1, 1, [new ThemeSprite(["11", "11"], "suelo", 1)]);
        var layer = new SceneLayer(pixel: 4, alpha: 1);
        layer.Show(scene);
        Assert.Empty(layer.GetVisualChildren());
        layer.Show(scene with { Over = new ThemeScene("lluvia", [Color.White], 0.1, 1) });
        Assert.IsType<SceneLayer>(Assert.Single(layer.GetVisualChildren()));
        layer.Show(scene);
        Assert.Empty(layer.GetVisualChildren());
    });

    [Fact]
    public void LaSegundaEscenaSeDibujaYSePausaConLaPrimera() => Headless.Run(() =>
    {
        var layer = new SceneLayer(pixel: 4, alpha: 1) { Paused = true };
        layer.Show(new ThemeScene("ciudad", [Color.White], 0.1, 1, Over: new ThemeScene("nieve", [Color.White], 1, 1)));
        var over = Assert.IsType<SceneLayer>(Assert.Single(layer.GetVisualChildren()));
        Assert.True(over.Paused);
        layer.Paused = false;
        Assert.False(over.Paused);
        layer.Measure(new Size(200, 100));
        layer.Arrange(new Avalonia.Rect(0, 0, 200, 100));
        Assert.Equal(200, over.Bounds.Width);
    });
}
