namespace Claupet.Tests;

/// <summary>Ajustes › Extensiones en Claupet Console: las páginas que arma ExtensionsPanel y sus acciones.</summary>
public sealed class ExtensionsPanelTests
{
    /// <summary>La Console de mentira: anota lo que se le pide y elige en las listas lo que diga la prueba.</summary>
    sealed class FakeSurface : IConsoleSurface
    {
        public List<string> Shown { get; } = new();
        public int Pushes { get; private set; }
        public IReadOnlyList<ConsoleOption>? Offered { get; private set; }
        /// <summary>Lo que se elige en la próxima lista (null: se cierra sin elegir).</summary>
        public string? Pick { get; set; }

        public bool Offer(IReadOnlyList<ConsoleOption> options, string selected, Action<string> pick)
        {
            Offered = options;
            if (Pick is { } choice) pick(choice);
            return true;
        }

        public void Push() => Pushes++;
        public void Show(string section) => Shown.Add(section);
        public void Post(Action action) => action();
    }

    /// <summary>La bandeja de mentira: el gestor de la prueba, la API local en memoria y lo que hizo al instalar.</summary>
    sealed class FakeHost(ExtensionManager manager) : IExtensionsHost
    {
        public ExtensionManager Extensions => manager;
        public List<(string Id, bool Update, bool Pet)> Installed { get; } = new();
        public List<ApiToken> Tokens { get; } = new();

        public Task InstalledAsync(InstalledExtension installed, bool update, bool useAsPet)
        {
            Installed.Add((installed.Id, update, useAsPet));
            return Task.CompletedTask;
        }

        public bool LocalApiEnabled { get; private set; }
        public string LocalApiStatus => LocalApiEnabled ? "http://127.0.0.1:47615" : "Apagada";
        public void SetLocalApi(bool on) => LocalApiEnabled = on;
        public IReadOnlyList<ApiToken> ApiTokens => Tokens;

        public string CreateApiToken(string name, IReadOnlyList<string> permissions)
        {
            var (token, saved) = LocalApiServer.NewToken(name, permissions);
            Tokens.Add(saved);
            return token;
        }

        public void RevokeApiToken(string id) => Tokens.RemoveAll(t => t.Id == id);
    }

    const string Id = "prueba.ajustes";

    static string Manifest(string version = "1.0.0") => $$"""
        {
          "schema": 1, "id": "{{Id}}", "name": { "es": "Ajustes de prueba", "en": "Test settings" }, "version": "{{version}}",
          "author": "Autor de prueba", "description": "Prueba los ajustes.",
          "provides": ["assistant"],
          "runtime": { "type": "http", "format": "openai-chat", "url": "http://127.0.0.1:9/v1", "model": "m", "auth": "bearer", "authSetting": "apiKey" },
          "settings": [
            { "key": "apiKey", "type": "secret", "label": "Clave" },
            { "key": "temperatura", "type": "number", "label": "Temperatura", "default": "0.5" },
            { "key": "voz", "type": "choice", "label": "Voz", "choices": ["alloy", "nova"], "default": "nova" },
            { "key": "breve", "type": "bool", "label": "Breve", "default": "true" },
            { "key": "nombre", "type": "string", "label": "Nombre" }
          ],
          "permissions": ["claude"]
        }
        """;

    static (ExtensionsPanel Panel, FakeSurface Surface, FakeHost Host) Panel(ManagerFixture f)
    {
        var surface = new FakeSurface();
        var host = new FakeHost(f.Manager);
        return (new ExtensionsPanel(host, surface), surface, host);
    }

    static SettingsPage Page(ExtensionsPanel panel, string id) => panel.Pages().Single(p => p.Id == id);

    static string PackageFile(ManagerFixture f, string manifest) => TestKit.Package(f.Dir, TestKit.Folder(f.Dir, manifest));

    /// <summary>Espera a que termine lo que el panel dejó corriendo (instalar, guardar un ajuste).</summary>
    static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) await Task.Delay(25);
        Assert.True(done());
    }

    [Fact]
    public async Task LaListaLlevaACadaExtensionYSuDetalleTieneTodo()
    {
        await using var f = new ManagerFixture();
        await f.InstallAsync(Manifest(), [], ExtensionSlots.Assistant);
        var (panel, _, _) = Panel(f);

        var list = Page(panel, ExtensionsPanel.PageId);
        Assert.Null(list.Parent);
        Assert.Contains(list.Rows, r => r is { Kind: RowKind.Page, Key: "ext:" + Id, Text: "Ajustes de prueba" });
        Assert.Contains(list.Rows, r => r is { Kind: RowKind.Link, Key: "ext-install", File: "*.claupet" });
        Assert.Contains(list.Rows, r => r is { Kind: RowKind.Choice, Key: "ext-slot:assistant", Value: "Ajustes de prueba" });
        Assert.Contains(list.Rows, r => r is { Kind: RowKind.Switch, Key: "ext-api" });

        var detail = Page(panel, ExtensionsPanel.DetailPage(Id));
        Assert.Equal(ExtensionsPanel.PageId, detail.Parent);
        Assert.Equal("Prueba los ajustes.", detail.Description);
        Assert.Contains(detail.Rows, r => r is { Kind: RowKind.Switch, Key: "ext-on:" + Id, On: true });
        Assert.Contains(detail.Rows, r => r is { Kind: RowKind.Info, Text: "Autor", Value: "Autor de prueba" });
        Assert.Contains(detail.Rows, r => r is { Kind: RowKind.Info, Text: "Firma", Value: "Sin firma" });
        Assert.Contains(detail.Rows, r => r.Kind == RowKind.Note && r.Text.Contains(ExtensionPermissions.Label(ExtensionPermissions.Claude)));
        // Un ajuste por tipo: la clave sin mostrarse, el número y el texto para escribir, la lista y el interruptor.
        Assert.Contains(detail.Rows, r => r is { Kind: RowKind.Input, Key: "ext-set:" + Id + ":apiKey", Secret: true, Value: "" });
        Assert.Contains(detail.Rows, r => r is { Kind: RowKind.Input, Key: "ext-set:" + Id + ":temperatura", Value: "0.5" });
        Assert.Contains(detail.Rows, r => r is { Kind: RowKind.Choice, Key: "ext-set:" + Id + ":voz", Value: "nova" });
        Assert.Contains(detail.Rows, r => r is { Kind: RowKind.Switch, Key: "ext-set:" + Id + ":breve", On: true });
        Assert.Contains(detail.Rows, r => r is { Kind: RowKind.Link, Key: "ext-remove:" + Id, Tone: RowTone.Danger });
    }

    [Fact]
    public async Task CadaPaginaTieneUnaAccionPorFilaYTodasSonDelPanel()
    {
        await using var f = new ManagerFixture();
        await f.InstallAsync(Manifest(), [], ExtensionSlots.Assistant);
        var (panel, _, host) = Panel(f);
        host.CreateApiToken("Mi script", [ExtensionPermissions.Pet]);
        foreach (var page in panel.Pages())
        {
            var keys = page.Rows.SelectMany(r => r.Kind == RowKind.Links ? r.Links!.Select(l => l.Key) : [r.Key])
                .Where(k => k.Length > 0 && !k.StartsWith("ext:", StringComparison.Ordinal)).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
            Assert.All(keys, k => Assert.True(ExtensionsPanel.Handles(k), k));
        }
    }

    [Fact]
    public async Task EncenderApagarYLosAjustesSeGuardan()
    {
        await using var f = new ManagerFixture();
        var ext = await f.InstallAsync(Manifest(), [], ExtensionSlots.Assistant);
        var (panel, surface, _) = Panel(f);

        panel.Act("ext-on:" + Id, null);
        await Until(() => !f.Manager.IsEnabled(Id));

        // Con la coma de quien escribe en español; se guarda con punto.
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("es-ES");
        try { panel.Act($"ext-set:{Id}:temperatura", "0,8"); }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
        await Until(() => f.Manager.ValueOf(ext, "temperatura") == "0.8");
        panel.Act($"ext-set:{Id}:breve", null);
        await Until(() => f.Manager.ValueOf(ext, "breve") == "false");
        surface.Pick = "alloy";
        panel.Act($"ext-set:{Id}:voz", null);
        Assert.Equal(["alloy", "nova"], surface.Offered!.Select(o => o.Id));
        await Until(() => f.Manager.ValueOf(ext, "voz") == "alloy");

        // La clave se guarda aparte y no vuelve a la Console; vacía no la borra, «Borrar» sí.
        panel.Act($"ext-set:{Id}:apiKey", "sk-prueba");
        await Until(() => f.Secrets.Get(Id, "apiKey") == "sk-prueba");
        Assert.DoesNotContain(panel.Pages().SelectMany(p => p.Rows), r => r.Value.Contains("sk-prueba") || r.Text.Contains("sk-prueba"));
        panel.Act($"ext-set:{Id}:apiKey", "");
        Assert.Equal("sk-prueba", f.Secrets.Get(Id, "apiKey"));
        panel.Act($"ext-clear:{Id}:apiKey", null);
        await Until(() => f.Secrets.Get(Id, "apiKey") is null);
    }

    [Fact]
    public async Task UnNumeroInvalidoSeDiceBajoSuFila()
    {
        await using var f = new ManagerFixture();
        var ext = await f.InstallAsync(Manifest(), [], ExtensionSlots.Assistant);
        var (panel, _, _) = Panel(f);
        panel.Act($"ext-set:{Id}:temperatura", "mucho");
        Assert.Equal("0.5", f.Manager.ValueOf(ext, "temperatura"));
        var rows = Page(panel, ExtensionsPanel.DetailPage(Id)).Rows.ToList();
        var at = rows.FindIndex(r => r.Key == $"ext-set:{Id}:temperatura");
        Assert.Equal(RowTone.Warn, rows[at + 1].Tone);
        Assert.Contains("Temperatura", rows[at + 1].Text);
    }

    [Fact]
    public async Task UnPaqueteSeRevisaAntesDeInstalarse()
    {
        await using var f = new ManagerFixture();
        var (panel, surface, host) = Panel(f);
        await panel.ReviewAsync(PackageFile(f, Manifest()));

        // La Console se abre en la revisión; todavía no hay nada instalado.
        Assert.Equal(ExtensionsPanel.ReviewPage, surface.Shown[^1]);
        Assert.Empty(f.Manager.Installed);
        var review = Page(panel, ExtensionsPanel.ReviewPage);
        Assert.Equal(ExtensionsPanel.PageId, review.Parent);
        Assert.Contains(review.Rows, r => r.Kind == RowKind.Note && r.Tone == RowTone.Warn && r.Text.Contains("Sin firma"));
        Assert.Contains(review.Rows, r => r is { Kind: RowKind.Switch, Key: "ext-review-slot:assistant", On: true });
        Assert.Contains(review.Rows, r => r.Kind == RowKind.Links && r.Links!.Any(l => l.Key == "ext-review-ok"));
        Assert.Contains(Page(panel, ExtensionsPanel.PageId).Rows, r => r is { Kind: RowKind.Page, Key: ExtensionsPanel.ReviewPage });

        panel.Act("ext-review-ok", null);
        await Until(() => host.Installed.Count == 1);
        Assert.Equal((Id, false, false), host.Installed[0]);
        Assert.Equal(Id, f.Manager.ProviderFor(ExtensionSlots.Assistant)?.Id);
        await Until(() => !panel.Reviewing);
        Assert.Equal(ExtensionsPanel.DetailPage(Id), surface.Shown[^1]);
        Assert.DoesNotContain(panel.Pages(), p => p.Id == ExtensionsPanel.ReviewPage);
    }

    [Fact]
    public async Task SinLaRanuraElegidaQuedaElIntegrado()
    {
        await using var f = new ManagerFixture();
        var (panel, _, host) = Panel(f);
        await panel.ReviewAsync(PackageFile(f, Manifest()));
        panel.Act("ext-review-slot:assistant", null);
        Assert.Contains(Page(panel, ExtensionsPanel.ReviewPage).Rows, r => r is { Key: "ext-review-slot:assistant", On: false });
        panel.Act("ext-review-ok", null);
        await Until(() => host.Installed.Count == 1);
        Assert.Null(f.Manager.ProviderFor(ExtensionSlots.Assistant));
    }

    [Fact]
    public async Task UnPaqueteRotoSeExplicaYNoSeInstala()
    {
        await using var f = new ManagerFixture();
        var (panel, surface, _) = Panel(f);
        await panel.ReviewAsync(TestKit.RawZip(f.Dir, ("otra-cosa.txt", TestKit.Utf8("hola"))));
        var review = Page(panel, ExtensionsPanel.ReviewPage);
        Assert.Contains(review.Rows, r => r is { Kind: RowKind.Note, Tone: RowTone.Danger } && r.Text.Contains("manifest.json"));
        Assert.DoesNotContain(review.Rows, r => r.Kind == RowKind.Links && r.Links!.Any(l => l.Key == "ext-review-ok"));

        panel.Act("ext-review-cancel", null);
        Assert.False(panel.Reviewing);
        Assert.Equal(ExtensionsPanel.PageId, surface.Shown[^1]);
        Assert.Empty(f.Manager.Installed);
    }

    [Fact]
    public async Task DesinstalarPreguntaSiGuardaLosDatos()
    {
        await using var f = new ManagerFixture();
        var ext = await f.InstallAsync(Manifest(), [], ExtensionSlots.Assistant);
        File.WriteAllText(Path.Combine(ext.DataFolder, "notas.txt"), "datos");
        var (panel, surface, _) = Panel(f);

        // Cerrar la lista sin elegir no desinstala.
        panel.Act("ext-remove:" + Id, null);
        Assert.Equal(2, surface.Offered!.Count);
        Assert.True(surface.Offered[0].Danger);
        Assert.Single(f.Manager.Installed);

        surface.Pick = "guardar";
        panel.Act("ext-remove:" + Id, null);
        await Until(() => f.Manager.Installed.Count == 0);
        Assert.True(File.Exists(Path.Combine(ext.DataFolder, "notas.txt")));
        await Until(() => surface.Shown.LastOrDefault() == ExtensionsPanel.PageId);
        Assert.DoesNotContain(panel.Pages(), p => p.Id == ExtensionsPanel.DetailPage(Id));
    }

    [Fact]
    public async Task UnTokenNuevoSeMuestraUnaSolaVez()
    {
        await using var f = new ManagerFixture();
        var (panel, surface, host) = Panel(f);
        panel.Act("ext-token-new", null);
        Assert.Equal(ExtensionsPanel.TokenPage, surface.Shown[^1]);

        // Sin nombre no se crea.
        panel.Act("ext-token-create", null);
        Assert.Empty(host.Tokens);
        Assert.Contains(Page(panel, ExtensionsPanel.TokenPage).Rows, r => r.Tone == RowTone.Warn);

        panel.Act("ext-token-name", "  Mi script  ");
        panel.Act("ext-token-perm:" + ExtensionPermissions.Clipboard, null);
        panel.Act("ext-token-perm:" + ExtensionPermissions.Pet, null);
        panel.Act("ext-token-create", null);
        var saved = Assert.Single(host.Tokens);
        Assert.Equal("Mi script", saved.Name);
        Assert.Equal([ExtensionPermissions.Notices, ExtensionPermissions.Clipboard], saved.Permissions);

        var copy = Page(panel, ExtensionsPanel.TokenPage).Rows.Single(r => r.Copy is not null);
        Assert.StartsWith("cpt_", copy.Copy);
        panel.Act("ext-token-done", null);
        Assert.DoesNotContain(panel.Pages(), p => p.Id == ExtensionsPanel.TokenPage);
        Assert.DoesNotContain(panel.Pages().SelectMany(p => p.Rows), r => r.Copy is not null);

        // Revocar pide confirmarlo en la lista.
        surface.Pick = "revocar";
        panel.Act("ext-revoke:" + saved.Id, null);
        Assert.Empty(host.Tokens);
    }

    [Fact]
    public async Task LaConsoleRecibeLasPaginasEnLugarDelResumen()
    {
        await using var f = new ManagerFixture();
        await f.InstallAsync(Manifest(), [], ExtensionSlots.Assistant);
        var (panel, _, _) = Panel(f);
        var pages = panel.Merge(SettingsPages.Build(new SettingsState()));
        var at = pages.ToList().FindIndex(p => p.Id == ExtensionsPanel.PageId);
        Assert.Equal(ExtensionsPanel.DetailPage(Id), pages[at + 1].Id);
        Assert.Single(pages, p => p.Id == ExtensionsPanel.PageId);
        // Lo que viaja por la tubería vuelve igual (las filas nuevas también).
        var back = ConsoleLink.Decode(ConsoleLink.Encode(new ConsoleMessage { Type = ConsoleLink.State, Pages = pages }))!;
        Assert.Equal(pages.Select(p => p with { Rows = [] }), back.Pages!.Select(p => p with { Rows = [] }));
        Assert.Equal(pages.SelectMany(p => p.Rows).Select(r => r with { Links = null }), back.Pages!.SelectMany(p => p.Rows).Select(r => r with { Links = null }));
    }
}
