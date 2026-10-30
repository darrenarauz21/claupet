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
        var (folder, custom) = (CustomThemes.Folder, Theme.SelectedCustom);
        CustomThemes.Folder = dir.Path;
        try
        {
            var saved = CustomThemes.Save(CustomThemes.Parse(Beach)!);
            Assert.Equal("playa-dorada-2", CustomThemes.Save(saved).Id);
            Theme.SelectedCustom = saved.Id;
            Assert.Equal(saved.Light.Accent, Theme.ForDark(false).Accent);
            Assert.Equal(saved.Dark.Background, Theme.ForDark(true).Background);
            Assert.Contains(Theme.Choices(), c => c.Id == "tema:playa-dorada");
            Assert.True(CustomThemes.Delete(saved.Id));
            Assert.Equal(Theme.Light.Accent, Theme.ForDark(false).Accent);
        }
        finally { (CustomThemes.Folder, Theme.SelectedCustom) = (folder, custom); }
    }

    [Fact]
    public void ElColorYaNoOfreceLosTemasPeroLaMascotaSi()
    {
        using var dir = new TempDir();
        var (folder, palette, custom) = (CustomThemes.Folder, Theme.SelectedPalette, Theme.SelectedCustom);
        CustomThemes.Folder = dir.Path;
        try
        {
            var saved = CustomThemes.Save(CustomThemes.Parse(Beach)!);
            Assert.Equal(Theme.Palettes.Select(p => p.Id), Theme.PaletteChoices().Select(c => c.Id));
            Assert.Contains(Theme.Choices(), c => c.Id == saved.PaletteId);
            // Con el tema elegido, la paleta sigue siendo la suya: su etiqueta y el punto de la fila «Color».
            (Theme.SelectedPalette, Theme.SelectedCustom) = ("oceano", saved.Id);
            Assert.Equal(Theme.Palettes.Single(p => p.Id == "oceano").Label, Theme.PaletteLabel(Theme.SelectedPalette));
            Assert.Equal(Theme.Palettes.Single(p => p.Id == "oceano").Light, Theme.PaletteAccent(dark: false));
            Assert.Equal(saved.Light.Accent, Theme.ForDark(false).Accent);
        }
        finally { (CustomThemes.Folder, Theme.SelectedPalette, Theme.SelectedCustom) = (folder, palette, custom); }
    }

    [Fact]
    public void ElTemaGuardadoComoPaletaPasaASuPropioAjuste()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("""{ "Palette": "tema:playa-dorada" }""")!;
        Assert.Equal("terracota", settings.Palette);
        Assert.Equal("playa-dorada", settings.CustomTheme);
        Assert.Equal("rapida", settings.ThemeQuality);
        var fresh = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("""{ "Palette": "bosque", "CustomTheme": "otono" }""")!;
        Assert.Equal(("bosque", "otono"), (fresh.Palette, fresh.CustomTheme));
    }

    [Fact]
    public void ExportaEImportaUnTema()
    {
        using var dir = new TempDir();
        using var elsewhere = new TempDir();
        var folder = CustomThemes.Folder;
        CustomThemes.Folder = dir.Path;
        try
        {
            var saved = CustomThemes.Save(CustomThemes.Parse(Beach)!);
            var file = elsewhere["playa.json"];
            Assert.Null(CustomThemes.Export(saved.Id, file));
            Assert.Contains(CustomThemes.Format, File.ReadAllText(file));
            Assert.NotNull(CustomThemes.Export("no-existe", elsewhere["otro.json"]));

            // Se importa con un id libre y un nombre que se distingue del que ya estaba, y por lo demás igual al exportado.
            var (imported, error, _) = CustomThemes.Import(file);
            Assert.Null(error);
            Assert.Equal("playa-dorada-2", imported?.Id);
            Assert.Equal("Playa dorada 2", imported?.Name);
            Assert.Equal((saved.Description, saved.Font, saved.Light, saved.Dark), (imported!.Description, imported.Font, imported.Light, imported.Dark));
            Assert.Equal(saved.Scene?.Colors, imported.Scene?.Colors);
        }
        finally { CustomThemes.Folder = folder; }
    }

    [Fact]
    public void ImportarAlgoQueNoEsUnTemaNoGuardaNada()
    {
        using var dir = new TempDir();
        using var elsewhere = new TempDir();
        var folder = CustomThemes.Folder;
        CustomThemes.Folder = dir.Path;
        try
        {
            Assert.NotNull(CustomThemes.Import(elsewhere.Write("notas.json", """{ "name": "sin colores" }""")).Error);
            Assert.NotNull(CustomThemes.Import(elsewhere.Write("grande.json", new string('x', 70_000))).Error);
            Assert.NotNull(CustomThemes.Import(elsewhere["no-existe.json"]).Error);
            Assert.Empty(CustomThemes.All());
            // Uno escrito a mano, sin nombre ni formato, vale: toma el nombre del archivo y pasa por las correcciones. La
            // letra se queda aunque aquí no esté instalada (se usa si se instala), y también al volver a leerlo.
            CustomThemes.FontInstalled = _ => false;
            var (theme, _, _) = CustomThemes.Import(elsewhere.Write("Mi tema.json",
                """{ "font": "Letra Rara", "light": { "background": "#FF00FF", "accent": "#2F7FC1" } }"""));
            Assert.Equal("Mi tema", theme?.Name);
            Assert.True(Chroma(theme!.Light.Background) <= 0.11);
            Assert.Equal("Letra Rara", CustomThemes.Load(theme.Id)?.Font);
            Assert.Null(CustomThemes.Parse("""{ "font": "Letra Rara", "light": { "accent": "#2F7FC1" } }""")?.Font);
        }
        finally { (CustomThemes.Folder, CustomThemes.FontInstalled) = (folder, null); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{ esto no es json")]
    [InlineData("{ \"name\": \"sin colores\" }")]
    public void SiFaltaORompeElArchivoVuelveALaTerracota(string? content)
    {
        using var dir = new TempDir();
        var (folder, palette, custom) = (CustomThemes.Folder, Theme.SelectedPalette, Theme.SelectedCustom);
        CustomThemes.Folder = dir.Path;
        try
        {
            if (content is not null) dir.Write("roto.json", content);
            (Theme.SelectedPalette, Theme.SelectedCustom) = ("terracota", "roto");
            Assert.Null(CustomThemes.Selected);
            Assert.Equal(Theme.Light, Theme.ForDark(false));
            Assert.Equal(Theme.DarkTheme, Theme.ForDark(true));
            Assert.Equal(Theme.Palettes[0].Label, Theme.PaletteLabel("tema:roto"));
        }
        finally { (CustomThemes.Folder, Theme.SelectedPalette, Theme.SelectedCustom) = (folder, palette, custom); }
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
            var (theme, error, _) = await CustomThemes.CreateAsync("algo de playa", new AppSettings());
            Assert.Null(error);
            Assert.Equal("playa-dorada", theme?.Id);
            Assert.True(File.Exists(dir["playa-dorada.json"]));
            var asked = string.Join("\n", brain.Received?.Messages.Select(m => m.Text) ?? []);
            Assert.Contains("algo de playa", asked);
            Assert.All(CustomThemes.Scenes, kind => Assert.Contains(kind, asked));

            Assistant.External = () => new FakeBrain("Hoy no tengo ganas de colores.");
            var (none, problem, _) = await CustomThemes.CreateAsync("otro", new AppSettings());
            Assert.Null(none);
            Assert.NotNull(problem);
        }
        finally { (CustomThemes.Folder, Assistant.External) = (folder, null); }
    }

    [Fact]
    public async Task LaCalidadEligeElModeloYBajaSiLaCuentaNoLoTiene()
    {
        using var dir = new TempDir();
        var (folder, ask) = (CustomThemes.Folder, CustomThemes.Ask);
        CustomThemes.Folder = dir.Path;
        var models = new List<string?>();
        CustomThemes.Ask = (request, _) =>
        {
            models.Add(request.Model);
            return Task.FromResult(request.Model == "fable"
                ? new AssistantResult("", null, null, null, "There's an issue with the selected model (fable). It may not exist or you may not have access to it.")
                : new AssistantResult(Beach, null, request.Model, null, null));
        };
        try
        {
            var (theme, error, note) = await CustomThemes.CreateAsync("playa", new AppSettings { ThemeQuality = "alta" });
            Assert.Null(error);
            Assert.NotNull(theme);
            Assert.Equal(["fable", "opus"], models);
            Assert.Contains("Opus", note);

            models.Clear();
            (_, _, note) = await CustomThemes.CreateAsync("playa", new AppSettings { ThemeQuality = "rapida" });
            Assert.Equal(["sonnet"], models);
            Assert.Null(note);

            // Un límite de uso no se arregla con otro modelo: no se prueba el siguiente.
            models.Clear();
            CustomThemes.Ask = (request, _) =>
            {
                models.Add(request.Model);
                return Task.FromResult(new AssistantResult("", null, null, null, "Claude AI usage limit reached for this model."));
            };
            (theme, error, _) = await CustomThemes.CreateAsync("playa", new AppSettings { ThemeQuality = "media" });
            Assert.Null(theme);
            Assert.NotNull(error);
            Assert.Equal(["opus"], models);

            // Cancelar a mitad de la respuesta no es un error: ni tema ni aviso.
            using var cancel = new CancellationTokenSource();
            CustomThemes.Ask = (_, token) =>
            {
                cancel.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new AssistantResult(Beach, null, null, null, null));
            };
            Assert.Equal(new ThemeResult(null, null), await CustomThemes.CreateAsync("playa", new AppSettings(), cancel.Token));
        }
        finally { (CustomThemes.Folder, CustomThemes.Ask) = (folder, ask); }
    }

    [Fact]
    public void AparienciaMuestraLosTemasAparteYSoloEnLaConsole()
    {
        using var dir = new TempDir();
        var (folder, custom) = (CustomThemes.Folder, Theme.SelectedCustom);
        CustomThemes.Folder = dir.Path;
        try
        {
            Theme.SelectedCustom = CustomThemes.Save(CustomThemes.Parse(Beach)!).Id;
            var rows = SettingsPages.Build(new SettingsState { CustomThemesHere = true, ThemeCreating = "un bosque" })
                .Single(p => p.Id == "apariencia").Rows;
            var cards = rows.Single(r => r.Kind == RowKind.Themes).Cards!;
            Assert.Equal(["", "playa-dorada", ""], cards.Select(c => c.Id));
            Assert.Equal([false, true, false], cards.Select(c => c.Selected));
            Assert.True(cards[^1].Creating);
            Assert.Equal("un bosque", cards[^1].Name);
            Assert.All(rows.SkipWhile(r => r.Kind != RowKind.Caption), r => Assert.Equal(RowPlace.Console, r.Place));
            Assert.Equal(L.T("Crear"), rows.Single(r => r.Key == SettingsPages.ThemeCreateKey).Submit);
            Assert.Equal("*.json", rows.Single(r => r.Key == SettingsPages.ThemeImportKey).File);
            var elsewhere = SettingsPages.Build(new SettingsState()).Single(p => p.Id == "apariencia").Rows;
            Assert.DoesNotContain(elsewhere, r => r.Kind == RowKind.Themes || r.Key.StartsWith(SettingsPages.ThemeKey, StringComparison.Ordinal));
        }
        finally { (CustomThemes.Folder, Theme.SelectedCustom) = (folder, custom); }
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
