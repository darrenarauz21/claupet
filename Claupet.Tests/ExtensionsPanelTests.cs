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

    /// <summary>Lo que responde «internet» en las pruebas: el catálogo y los paquetes que se le den; lo demás, 404.</summary>
    sealed class FakeWeb : HttpMessageHandler
    {
        public string? Catalog { get; set; }
        public Dictionary<string, byte[]> Files { get; } = new();
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var url = request.RequestUri!.AbsoluteUri;
            if (url == ExtensionCatalog.Url && Catalog is { } catalog)
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(catalog) });
            if (Files.TryGetValue(url, out var bytes))
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }

    static (ExtensionsPanel Panel, FakeSurface Surface, FakeHost Host) Panel(ManagerFixture f, FakeWeb? web = null)
    {
        var surface = new FakeSurface();
        var host = new FakeHost(f.Manager);
        // Las pruebas nunca salen a la red: sin catálogo, el repositorio responde 404.
        return (new ExtensionsPanel(host, surface) { CatalogHttp = new HttpClient(web ?? new FakeWeb()) }, surface, host);
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
        var market = Assert.Single(list.Rows, r => r.Kind == RowKind.Market);
        // Sin catálogo, la instalada sale igual (sin el escudo de oficial: no viene firmada).
        var card = Assert.Single(market.Market!);
        Assert.Equal((Id, "Ajustes de prueba", "ext:" + Id, "1.0.0", false), (card.Id, card.Name, card.Page, card.Installed, card.Official));
        Assert.Contains(market.Icons!, i => i is { Key: "ext-install", File: "*.claupet" });
        Assert.Contains(market.Icons!, i => i.Key == "ext-more");

        // Las ranuras y la API local, en su página.
        var more = Page(panel, ExtensionsPanel.MorePage);
        Assert.Equal(ExtensionsPanel.PageId, more.Parent);
        Assert.Contains(more.Rows, r => r is { Kind: RowKind.Choice, Key: "ext-slot:assistant", Value: "Ajustes de prueba" });
        Assert.Contains(more.Rows, r => r is { Kind: RowKind.Switch, Key: "ext-api" });

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
        Assert.Equal(ExtensionsPanel.MorePage, pages[at + 1].Id);
        Assert.Equal(ExtensionsPanel.DetailPage(Id), pages[at + 2].Id);
        Assert.Single(pages, p => p.Id == ExtensionsPanel.PageId);
        // Lo que viaja por la tubería vuelve igual (las filas nuevas también).
        var back = ConsoleLink.Decode(ConsoleLink.Encode(new ConsoleMessage { Type = ConsoleLink.State, Pages = pages }))!;
        Assert.Equal(pages.Select(p => p with { Rows = [] }), back.Pages!.Select(p => p with { Rows = [] }));
        Assert.Equal(pages.SelectMany(p => p.Rows).Select(r => r with { Links = null, Icons = null, Market = null }), back.Pages!.SelectMany(p => p.Rows).Select(r => r with { Links = null, Icons = null, Market = null }));
        static IEnumerable<string> Cards(IEnumerable<SettingsPage> pages) => pages.SelectMany(p => p.Rows).SelectMany(r => r.Market ?? [])
            .Select(c => (c with { Provides = [] }) + string.Join(",", c.Provides));
        Assert.NotEmpty(Cards(pages));
        Assert.Equal(Cards(pages), Cards(back.Pages!));
        Assert.Equal(pages.SelectMany(p => p.Rows).SelectMany(r => r.Icons ?? []), back.Pages!.SelectMany(p => p.Rows).SelectMany(r => r.Icons ?? []));
    }

    /// <summary>Un catálogo firmado con la clave oficial de la prueba, con esos paquetes ya servidos por web.</summary>
    static void Publish(ManagerFixture f, FakeWeb web, ExtensionSigner official, long sequence, params string[] packages)
    {
        web.Catalog = ExtensionCatalog.Build(official, sequence, packages, f.Dir["catalogo-staging"]);
        foreach (var package in packages)
        {
            using var staged = StagedPackage.Open(package, f.Dir["catalogo-staging"]);
            web.Files[ExtensionCatalog.PackageUrl(staged.Manifest!.Id, staged.Manifest.Version)] = File.ReadAllBytes(package);
        }
    }

    /// <summary>Un paquete de «claupet.notas» firmado; morePermissions: pide además el portapapeles.</summary>
    static string Official(ManagerFixture f, ExtensionSigner official, string version, bool morePermissions = false)
    {
        var manifest = TestKit.SkillManifest("claupet.notas", version);
        if (morePermissions) manifest = manifest.Replace("[\"pet\", \"notices\"]", "[\"pet\", \"notices\", \"clipboard\"]");
        return TestKit.Package(f.Dir, TestKit.Folder(f.Dir, manifest, ("bin/notas.cmd", "@echo off")), official);
    }

    static ExtensionCard CardOf(ExtensionsPanel panel, string id) =>
        Page(panel, ExtensionsPanel.PageId).Rows.Single(r => r.Kind == RowKind.Market).Market!.Single(c => c.Id == id);

    [Fact]
    public async Task DelCatalogoSeInstalaConSuRevisionYQuedaComoOficial()
    {
        using var official = TestKit.NewSigner("Claupet");
        await using var f = new ManagerFixture([official.PublicKey]);
        var web = new FakeWeb();
        Publish(f, web, official, 1, Official(f, official, "1.0.0"));
        var (panel, surface, host) = Panel(f, web);

        panel.Pages().ToList();
        await Until(() => panel.Catalog is not null);
        var card = CardOf(panel, "claupet.notas");
        Assert.Equal((true, null, "1.0.0", false), (card.Official, card.Installed, card.Latest, card.Update));
        Assert.Equal(ExtensionsPanel.CatalogPage("claupet.notas"), card.Page);
        var info = Page(panel, card.Page);
        Assert.Contains(info.Rows, r => r is { Kind: RowKind.Link, Key: "ext-get:claupet.notas" });
        Assert.Contains(info.Rows, r => r.Kind == RowKind.Note && r.Text.Contains(ExtensionPermissions.Label(ExtensionPermissions.Pet)));

        // Se baja, se comprueba contra el catálogo y va a la misma revisión que un archivo elegido a mano.
        panel.Act("ext-get:claupet.notas", null);
        await Until(() => surface.Shown.LastOrDefault() == ExtensionsPanel.ReviewPage);
        Assert.Contains(Page(panel, ExtensionsPanel.ReviewPage).Rows, r => r.Text == "Extensión oficial de Claupet.");
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(f.Manager.Store.Root, "descargas")));
        panel.Act("ext-review-ok", null);
        await Until(() => host.Installed.Count == 1);
        await Until(() => CardOf(panel, "claupet.notas").Installed == "1.0.0");
        Assert.Equal(ExtensionsPanel.DetailPage("claupet.notas"), CardOf(panel, "claupet.notas").Page);
    }

    [Fact]
    public async Task ActualizarTodasInstalaSinRevisionSoloSiNoPideNada()
    {
        using var official = TestKit.NewSigner("Claupet");
        await using var f = new ManagerFixture([official.PublicKey]);
        var web = new FakeWeb();
        await f.Manager.InstallAsync(f.Manager.Store.Prepare(Official(f, official, "1.0.0")), []);
        Publish(f, web, official, 1, Official(f, official, "1.1.0"));
        var (panel, surface, host) = Panel(f, web);

        panel.Pages().ToList();
        await Until(() => panel.Catalog is not null);
        var card = CardOf(panel, "claupet.notas");
        Assert.Equal((true, "1.0.0", "1.1.0"), (card.Update, card.Installed, card.Latest));
        Assert.Contains(Page(panel, ExtensionsPanel.DetailPage("claupet.notas")).Rows, r => r is { Kind: RowKind.Link, Key: "ext-get:claupet.notas" });

        // Los mismos permisos: se actualiza sin pasar por la revisión.
        panel.Act("ext-update-all", null);
        await Until(() => host.Installed.Count == 1);
        Assert.Equal(("claupet.notas", true), (host.Installed[0].Id, host.Installed[0].Update));
        Assert.DoesNotContain(ExtensionsPanel.ReviewPage, surface.Shown);
        Assert.Equal("1.1.0", f.Manager.Find("claupet.notas")!.Manifest.Version.ToString());

        // Una que pide un permiso nuevo va a la revisión, aunque se pida «Actualizar todas».
        Publish(f, web, official, 2, Official(f, official, "1.2.0", morePermissions: true));
        await panel.RefreshCatalogAsync(force: true);
        Assert.True(CardOf(panel, "claupet.notas").Update);
        panel.Act("ext-update-all", null);
        await Until(() => surface.Shown.LastOrDefault() == ExtensionsPanel.ReviewPage);
        Assert.Contains(Page(panel, ExtensionsPanel.ReviewPage).Rows, r => r.Text == "Ahora también pide permiso para");
        Assert.Single(host.Installed);
    }

    [Fact]
    public async Task UnaDescargaQueNoCoincideConElCatalogoNoSeAbre()
    {
        using var official = TestKit.NewSigner("Claupet");
        await using var f = new ManagerFixture([official.PublicKey]);
        var web = new FakeWeb();
        Publish(f, web, official, 1, Official(f, official, "1.0.0"));
        // Alguien cambió el paquete en el servidor: ya no es lo que firmó el catálogo.
        var url = web.Files.Keys.Single();
        web.Files[url] = [.. web.Files[url], 0];
        var (panel, surface, _) = Panel(f, web);

        panel.Pages().ToList();
        await Until(() => panel.Catalog is not null);
        panel.Act("ext-get:claupet.notas", null);
        var page = ExtensionsPanel.CatalogPage("claupet.notas");
        await Until(() => Page(panel, page).Rows.Any(r => r.Text.Contains("no coincide con el catálogo")));
        Assert.DoesNotContain(ExtensionsPanel.ReviewPage, surface.Shown);
        Assert.Null(CardOf(panel, "claupet.notas").Percent);
    }

    [Fact]
    public async Task UnCatalogoDeOtraClaveNoMuestraNada()
    {
        using var official = TestKit.NewSigner("Claupet");
        using var stranger = TestKit.NewSigner("Otro");
        await using var f = new ManagerFixture([official.PublicKey]);
        var web = new FakeWeb();
        Publish(f, web, stranger, 1, Official(f, stranger, "1.0.0"));
        var (panel, _, _) = Panel(f, web);

        panel.Pages().ToList();
        await Until(() => web.Requests > 0 && Page(panel, ExtensionsPanel.PageId).Rows.Single(r => r.Kind == RowKind.Market).Value.Contains("firma"));
        Assert.Null(panel.Catalog);
        Assert.Empty(Page(panel, ExtensionsPanel.PageId).Rows.Single(r => r.Kind == RowKind.Market).Market!);
    }
}
