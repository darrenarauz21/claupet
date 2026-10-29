using System.Drawing;

namespace Claupet.Tests;

/// <summary>
/// Los temas propios: leer lo que responde el modelo, corregir el diseño y volver a la terracota si algo falla. Cambian
/// Theme.SelectedPalette, CustomThemes.Folder y Assistant.External, que son estáticos: no corren en paralelo con las del asistente.
/// </summary>
[Collection("Assistant.External")]
public sealed class CustomThemesTests
{
    const string Beach = """
        Aquí está su tema:
        ```json
        {
          "id": "playa-dorada",
          "name": "Playa dorada",
          "font": "Arial",
          "light": { "background": "#FBF7EE", "raised": "#FFFFFF", "hover": "#F1EBDD", "border": "#E2D9C6", "text": "#22201C",
                     "secondary": "#5F5A50", "tertiary": "#857E70", "track": "#EAE2D0", "accent": "#2B8FB3",
                     "danger": "#C6473B", "success": "#5E7A3A", "warn": "#B7791F" },
          "dark":  { "background": "#1F2426", "raised": "#2B3134", "hover": "#262B2E", "border": "#353C40", "text": "#F2EFE8",
                     "secondary": "#BDB8AD", "tertiary": "#8E897F", "track": "#363C3F", "accent": "#5BB8D9",
                     "danger": "#E0695C", "success": "#8FA46C", "warn": "#E0A84B" },
          "scene": { "kind": "playa", "colors": ["#F4D58D", "#5BB8D9"], "density": 0.6, "speed": 1.2 },
        }
        ```
        """;

    [Fact]
    public void LeeElJsonDentroDeUnBloqueDeCodigo()
    {
        var theme = CustomThemes.Parse(Beach);
        Assert.NotNull(theme);
        Assert.Equal("playa-dorada", theme.Id);
        Assert.Equal("Playa dorada", theme.Name);
        Assert.Equal("Arial", theme.Font);
        Assert.Equal(Color.FromArgb(255, 0xFB, 0xF7, 0xEE), theme.Light.Background);
        Assert.Equal("playa", theme.Scene?.Kind);
        Assert.Equal(2, theme.Scene?.Colors.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("No puedo hacer eso.")]
    [InlineData("{ \"name\": \"sin colores\" }")]
    [InlineData("{ \"light\": { \"background\": ")]
    [InlineData("[1, 2, 3]")]
    public void UnaRespuestaRotaNoDaTema(string text) => Assert.Null(CustomThemes.Parse(text));

    [Fact]
    public void LosCamposFueraDeRangoSeCorrigen()
    {
        var theme = CustomThemes.Parse("""
            { "name": "Chillón", "font": "Arial; rm -rf /",
              "light": { "background": "#FF00FF", "text": "#FFFF00", "accent": "#FFFFAA", "danger": "#00FF00", "secondary": "nada" },
              "scene": { "kind": "nieve", "density": 40, "speed": -3, "colors": ["#ZZZZZZ"] } }
            """);
        Assert.NotNull(theme);
        Assert.Null(theme.Font);
        var light = theme.Light;
        Assert.True(Chroma(light.Background) <= 0.11, $"fondo saturado: {CustomThemes.Hex(light.Background)}");
        Assert.True(CustomThemes.Ratio(light.Text, light.Background) >= 4.5);
        Assert.True(CustomThemes.Ratio(light.Text, light.Raised) >= 4.5);
        Assert.True(CustomThemes.Ratio(light.Accent, light.Background) >= 3);
        Assert.Equal(Theme.Light.Danger, light.Danger);
        Assert.Equal(Theme.Light.Secondary, light.Secondary);
        Assert.Equal(1, theme.Scene!.Density);
        Assert.Equal(0.25, theme.Scene.Speed);
        Assert.Equal(light.Accent, Assert.Single(theme.Scene.Colors));
    }

    [Theory]
    [InlineData(false, "#F0F0F0", "#E8E8E8")]
    [InlineData(true, "#202020", "#303030")]
    public void ElContrasteLlegaAlMinimo(bool dark, string background, string text)
    {
        var colors = ThemeColors.Of(dark ? Theme.DarkTheme : Theme.Light) with
        {
            Background = CustomThemes.ParseHex(background)!.Value, Text = CustomThemes.ParseHex(text)!.Value,
            Secondary = CustomThemes.ParseHex(text)!.Value, Accent = CustomThemes.ParseHex(text)!.Value,
        };
        var fixedColors = CustomThemes.Fix(colors, dark);
        foreach (var under in new[] { fixedColors.Background, fixedColors.Raised })
        {
            Assert.True(CustomThemes.Ratio(fixedColors.Text, under) >= 4.5);
            Assert.True(CustomThemes.Ratio(fixedColors.Secondary, under) >= 3);
            Assert.True(CustomThemes.Ratio(fixedColors.Accent, under) >= 3);
        }
    }

    /// <summary>Los de Claupet ya cumplen; solo la terracota del modo claro (2,97:1 sobre el fondo) se oscurece un poco.</summary>
    [Fact]
    public void LosColoresDeClaupetPasanSinCambios()
    {
        var light = ThemeColors.Of(Theme.Light);
        var fixedLight = CustomThemes.Fix(light, dark: false);
        Assert.Equal(light with { Accent = fixedLight.Accent }, fixedLight);
        Assert.InRange(CustomThemes.Ratio(fixedLight.Accent, fixedLight.Background), 3, 3.3);
        Assert.Equal(ThemeColors.Of(Theme.DarkTheme), CustomThemes.Fix(ThemeColors.Of(Theme.DarkTheme), dark: true));
    }

    [Theory]
    [InlineData("volcan")]
    [InlineData("<script>")]
    [InlineData("")]
    public void UnaEscenaDesconocidaNoDaFondo(string kind)
    {
        var theme = CustomThemes.Parse($$"""{ "name": "x", "light": { "accent": "#2F7FC1" }, "scene": { "kind": "{{kind}}" } }""");
        Assert.NotNull(theme);
        Assert.Null(theme.Scene);
    }

    [Fact]
    public void LaEscenaAceptaTildesYMayusculas() =>
        Assert.Equal("luciernagas", CustomThemes.Parse("""{ "light": { "accent": "#2F7FC1" }, "scene": { "kind": "Luciérnagas" } }""")?.Scene?.Kind);

    [Theory]
    [InlineData("../../settings", "settings")]
    [InlineData("..\\..\\Windows\\win", "windows-win")]
    [InlineData("C:/temp/x", "c-temp-x")]
    [InlineData("con", "tema-con")]
    [InlineData("...", "tema")]
    [InlineData("Otoño cálido", "otono-calido")]
    public void ElIdNoSaleDeLaCarpeta(string raw, string safe)
    {
        using var dir = new TempDir();
        var before = CustomThemes.Folder;
        CustomThemes.Folder = dir.Path;
        try
        {
            Assert.Equal(safe, CustomThemes.SafeId(raw));
            Assert.Equal(dir.Path, Path.GetDirectoryName(CustomThemes.PathFor(raw)));
        }
        finally { CustomThemes.Folder = before; }
    }

    [Fact]
    public void GuardaEligeYBorra()
    {
        using var dir = new TempDir();
        var (folder, palette) = (CustomThemes.Folder, Theme.SelectedPalette);
        CustomThemes.Folder = dir.Path;
        try
        {
            var saved = CustomThemes.Save(CustomThemes.Parse(Beach)!);
            Assert.Equal("playa-dorada-2", CustomThemes.Save(saved).Id);
            Theme.SelectedPalette = saved.PaletteId;
            Assert.Equal(saved.Light.Accent, Theme.ForDark(false).Accent);
            Assert.Equal(saved.Dark.Background, Theme.ForDark(true).Background);
            Assert.Equal("Playa dorada", Theme.PaletteLabel(saved.PaletteId));
            Assert.Contains(Theme.Choices(), c => c.Id == "tema:playa-dorada");
            Assert.True(CustomThemes.Delete(saved.Id));
            Assert.Equal(Theme.Light.Accent, Theme.ForDark(false).Accent);
        }
        finally { (CustomThemes.Folder, Theme.SelectedPalette) = (folder, palette); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{ esto no es json")]
    [InlineData("{ \"name\": \"sin colores\" }")]
    public void SiFaltaORompeElArchivoVuelveALaTerracota(string? content)
    {
        using var dir = new TempDir();
        var (folder, palette) = (CustomThemes.Folder, Theme.SelectedPalette);
        CustomThemes.Folder = dir.Path;
        try
        {
            if (content is not null) dir.Write("roto.json", content);
            Theme.SelectedPalette = "tema:roto";
            Assert.Null(CustomThemes.Selected);
            Assert.Equal(Theme.Light, Theme.ForDark(false));
            Assert.Equal(Theme.DarkTheme, Theme.ForDark(true));
            Assert.Equal(Theme.Palettes[0].Label, Theme.PaletteLabel("tema:roto"));
        }
        finally { (CustomThemes.Folder, Theme.SelectedPalette) = (folder, palette); }
    }

    [Fact]
    public async Task CreaElTemaConLaRespuestaDeClaudeYLoGuarda()
    {
        using var dir = new TempDir();
        var folder = CustomThemes.Folder;
        CustomThemes.Folder = dir.Path;
        var brain = new FakeBrain(Beach[..Beach.IndexOf('{')], Beach[Beach.IndexOf('{')..]);
        Assistant.External = () => brain;
        try
        {
            var (theme, error) = await CustomThemes.CreateAsync("algo de playa", new AppSettings());
            Assert.Null(error);
            Assert.Equal("playa-dorada", theme?.Id);
            Assert.True(File.Exists(dir["playa-dorada.json"]));
            var asked = string.Join("\n", brain.Received?.Messages.Select(m => m.Text) ?? []);
            Assert.Contains("algo de playa", asked);
            Assert.Contains("luciernagas", asked);

            Assistant.External = () => new FakeBrain("Hoy no tengo ganas de colores.");
            var (none, problem) = await CustomThemes.CreateAsync("otro", new AppSettings());
            Assert.Null(none);
            Assert.NotNull(problem);
        }
        finally { (CustomThemes.Folder, Assistant.External) = (folder, null); }
    }

    [Fact]
    public void AparienciaOfreceCrearYBorrarSoloEnLaConsoleYDondeSeSabe()
    {
        using var dir = new TempDir();
        var (folder, palette) = (CustomThemes.Folder, Theme.SelectedPalette);
        CustomThemes.Folder = dir.Path;
        try
        {
            Theme.SelectedPalette = CustomThemes.Save(CustomThemes.Parse(Beach)!).PaletteId;
            var rows = SettingsPages.Build(new SettingsState { CustomThemesHere = true }).Single(p => p.Id == "apariencia").Rows;
            Assert.Equal(RowPlace.Console, rows.Single(r => r.Key == SettingsPages.ThemeCreateKey).Place);
            Assert.Contains(rows, r => r.Links?.Any(l => l.Key == SettingsPages.ThemeDeleteKey) == true);
            var elsewhere = SettingsPages.Build(new SettingsState()).Single(p => p.Id == "apariencia").Rows;
            Assert.DoesNotContain(elsewhere, r => r.Key == SettingsPages.ThemeCreateKey || r.Links is not null);
        }
        finally { (CustomThemes.Folder, Theme.SelectedPalette) = (folder, palette); }
    }

    [Fact]
    public async Task LaMascotaEligeUnTemaPropioPorSuNombre()
    {
        using var dir = new TempDir();
        var folder = CustomThemes.Folder;
        CustomThemes.Folder = dir.Path;
        try
        {
            CustomThemes.Save(CustomThemes.Parse(Beach)!);
            string? chosen = null;
            var outcome = await SystemActions.RunAsync(new AssistantAction("color", "playa dorada"), id => chosen = id);
            Assert.Null(outcome.Problem);
            Assert.Equal("tema:playa-dorada", chosen);
        }
        finally { CustomThemes.Folder = folder; }
    }

    static double Chroma(Color c) => (Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B))) / 255.0;
}
