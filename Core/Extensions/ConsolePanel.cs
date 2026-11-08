using System.Globalization;

namespace Claupet;

/// <summary>
/// Lo que la bandeja que carga extensiones le da a Ajustes › Extensiones de Claupet Console: el gestor, lo que hace
/// después de instalar (la mascota del paquete, arrancar lo que escucha, el aviso) y la API local con sus tokens. Lo
/// demás (la lista, el detalle, los ajustes, la revisión antes de instalar) lo arma ExtensionsPanel, igual en todos los
/// sistemas.
/// </summary>
interface IExtensionsHost
{
    ExtensionManager Extensions { get; }
    /// <summary>Recién instalada (la persona aceptó la revisión): su mascota, si la eligió; lo que escucha; el aviso.</summary>
    Task InstalledAsync(InstalledExtension installed, bool update, bool useAsPet);
    bool LocalApiEnabled { get; }
    string LocalApiStatus { get; }
    void SetLocalApi(bool on);
    IReadOnlyList<ApiToken> ApiTokens { get; }
    /// <summary>Crea el token y devuelve su texto, que se muestra una sola vez.</summary>
    string CreateApiToken(string name, IReadOnlyList<string> permissions);
    void RevokeApiToken(string id);
}

/// <summary>Lo que el panel de las extensiones le pide a Claupet Console: lo hace ConsoleHost (las pruebas ponen otro).</summary>
interface IConsoleSurface
{
    /// <summary>Una lista para elegir junto a la fila que se tocó; false si la acción no vino de la Console.</summary>
    bool Offer(IReadOnlyList<ConsoleOption> options, string selected, Action<string> pick);
    /// <summary>Vuelve a mandar Ajustes: cambió algo que se ve.</summary>
    void Push();
    /// <summary>Trae la Console en esa sección o página; si estaba cerrada, la abre ahí.</summary>
    void Show(string section);
    /// <summary>Corre en el hilo de la interfaz de la bandeja.</summary>
    void Post(Action action);
}

/// <summary>
/// Ajustes › Extensiones en Claupet Console: el catálogo oficial y las instaladas en tarjetas (Explorar, Instaladas y
/// Actualizaciones, que filtra la Console), una página por extensión (encender o apagar, qué es, de quién, su firma, sus
/// permisos, sus ajustes, su registro y desinstalar; o, si no está instalada, lo que dice el catálogo), las ranuras y la
/// API local con sus tokens en su propia página y, antes de instalar un paquete (bajado del catálogo o elegido), la
/// página de revisión (firma, permisos y ranuras) con «Instalar». Las acciones empiezan con «ext-» y llegan aquí desde
/// ConsoleHost.
/// </summary>
sealed class ExtensionsPanel(IExtensionsHost host, IConsoleSurface console)
{
    /// <summary>La sección de la lista; las demás páginas cuelgan de ella.</summary>
    public const string PageId = "extensiones";
    public const string ReviewPage = "ext-revision", TokenPage = "ext-token", MorePage = "ext-mas";
    const string Prefix = "ext-";

    /// <summary>Los permisos que se le pueden dar a un token de la API local (los que Claupet controla).</summary>
    static readonly string[] TokenPermissions =
    [
        ExtensionPermissions.Pet, ExtensionPermissions.Notices, ExtensionPermissions.Island, ExtensionPermissions.Voice,
        ExtensionPermissions.Claude, ExtensionPermissions.Sessions, ExtensionPermissions.Clipboard,
    ];

    /// <summary>Un paquete que espera la revisión de la persona, o por qué no se puede instalar.</summary>
    sealed class Pending(string file, InstallReview? review, string? problem)
    {
        public string File { get; } = file;
        public InstallReview? Review { get; } = review;
        public string? Problem { get; set; } = problem;
        public HashSet<string> Slots { get; } = new();
        public bool Pet { get; set; } = true;
        public bool Installing { get; set; }
    }

    /// <summary>Un token que se está creando: para qué programa, qué puede pedir y, ya creado, su texto.</summary>
    sealed class TokenDraft
    {
        public string Name { get; set; } = "";
        public HashSet<string> Permissions { get; } = [ExtensionPermissions.Pet, ExtensionPermissions.Notices];
        public string? Created { get; set; }
        public bool NameMissing { get; set; }
    }

    Pending? _review;
    TokenDraft? _token;
    CatalogClient? _catalogClient;
    /// <summary>El catálogo oficial verificado (el guardado o el recién bajado); null si todavía no hay.</summary>
    ExtensionCatalog? _catalog;
    bool _catalogLoaded, _refreshing;
    string? _catalogProblem;
    DateTimeOffset? _catalogChecked;
    /// <summary>Las que se están bajando del catálogo, con su avance (0–100).</summary>
    readonly Dictionary<string, int> _downloads = new();
    /// <summary>Un aviso bajo una fila de una página (un ajuste que no se guardó, un registro vacío…): la página, la clave y el texto.</summary>
    (string Page, string Key, string Text)? _note;

    ExtensionManager Manager => host.Extensions;

    public static string DetailPage(string id) => "ext:" + id;

    /// <summary>La página de una del catálogo que no está instalada.</summary>
    public static string CatalogPage(string id) => "ext-cat:" + id;

    /// <summary>El cliente del catálogo; las pruebas ponen otro (otra dirección).</summary>
    internal CatalogClient Catalogs
    {
        get => _catalogClient ??= new CatalogClient(Manager.Store.Root, Manager.Store.Trust);
        set => _catalogClient = value;
    }

    /// <summary>Para las pruebas: el HttpClient con que se pide el catálogo; null, el de siempre.</summary>
    internal HttpClient? CatalogHttp { get; set; }

    /// <summary>El catálogo que se muestra.</summary>
    internal ExtensionCatalog? Catalog => _catalog;

    /// <summary>Esa acción es de este panel.</summary>
    public static bool Handles(string key) => key.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Hay un paquete esperando su revisión.</summary>
    public bool Reviewing => _review is not null;

    // ── Páginas ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Las secciones de Ajustes con las páginas de las extensiones en lugar de la sección de resumen.</summary>
    public IReadOnlyList<SettingsPage> Merge(IReadOnlyList<SettingsPage> pages) =>
        pages.SelectMany(p => p.Id == PageId ? Pages() : new[] { p }).ToList();

    /// <summary>La lista, las ranuras, el detalle de cada instalada y de cada una del catálogo y, si hacen falta, la revisión y el token nuevo.</summary>
    public IEnumerable<SettingsPage> Pages()
    {
        LoadCatalog();
        yield return ListPage();
        yield return More();
        foreach (var ext in Manager.Installed) yield return Detail(ext);
        if (_catalog is { } catalog)
            foreach (var entry in catalog.Entries.Where(e => Manager.Find(e.Id) is null)) yield return CatalogDetail(entry);
        if (_review is { } review) yield return Review(review);
        if (_token is { } token) yield return Token(token);
    }

    SettingsPage ListPage()
    {
        var rows = new List<SettingsRow>();
        if (_review is { } pending)
            rows.Add(new(RowKind.Page, pending.Review is { } r ? L.T("Instalar «{0}»", r.Manifest.Name.Text) : L.T("No se puede instalar «{0}»", pending.File),
                ReviewPage, L.T("Revisar")));
        foreach (var (id, problem) in Manager.Broken) rows.Add(SettingsPages.Note(L.T("«{0}» no se puede leer: {1}", id, problem), RowTone.Warn));
        foreach (var (command, winner, loser) in Manager.CommandConflicts())
            rows.Add(SettingsPages.Note(L.T("El comando /{0} está en «{1}» y en «{2}»: lo atiende la primera.", command, NameOf(winner), NameOf(loser)), RowTone.Warn));
        if (_note is { Page: PageId } note) rows.Add(SettingsPages.Note(note.Text, RowTone.Warn));

        rows.Add(new(RowKind.Market, L.T("Extensiones"), Prefix + "market", CatalogStatus, Place: RowPlace.Console, Market: Cards(),
            Icons:
            [
                new(RowIconKind.Refresh, L.T("Buscar actualizaciones"), Prefix + "catalog"),
                new(RowIconKind.Package, L.T("Instalar desde archivo .claupet"), Prefix + "install", File: "*.claupet"),
                new(RowIconKind.Sliders, L.T("Ranuras y API local"), Prefix + "more"),
            ]));
        return new(PageId, L.T("Extensiones"),
            L.T("Habilidades, voces, mascotas y modelos. Las oficiales vienen firmadas por Claupet; también puede soltar un .claupet sobre la mascota."),
            null, rows);
    }

    /// <summary>Las ranuras y la API local, en su propia página.</summary>
    SettingsPage More()
    {
        var rows = new List<SettingsRow>();
        var slots = ExtensionSlots.Exclusive.Where(s => Manager.Installed.Any(e => e.Manifest.Provides.Contains(s))).ToList();
        rows.Add(SettingsPages.Caption(L.T("Ranuras")));
        rows.Add(SettingsPages.Note(L.T("Una extensión en una ranura apaga lo que trae Claupet en ella, para que no choquen.")));
        if (slots.Count == 0) rows.Add(SettingsPages.Note(L.T("Ninguna extensión instalada puede ocupar una ranura."), RowTone.Muted));
        foreach (var slot in slots)
            rows.Add(SettingsPages.Choice(ExtensionSlots.Label(slot), Prefix + "slot:" + slot, SlotLabel(slot)));

        rows.Add(SettingsPages.Caption(L.T("API local")));
        rows.Add(SettingsPages.Switch(L.T("Encender la API local"), Prefix + "api", host.LocalApiEnabled));
        rows.Add(SettingsPages.Note(L.T("Otros programas de este equipo le piden cosas a Claupet por HTTP (127.0.0.1), con un token. Las páginas web no pueden usarla.")));
        rows.Add(SettingsPages.Info(L.T("Estado"), host.LocalApiStatus));
        foreach (var token in host.ApiTokens)
        {
            rows.Add(SettingsPages.Link(token.Name, L.T("Revocar"), Prefix + "revoke:" + token.Id, RowTone.Danger));
            var permissions = token.Permissions.Count == 0 ? L.T("sin permisos") : string.Join(", ", token.Permissions.Select(ExtensionPermissions.Label));
            rows.Add(SettingsPages.Note(L.T("{0} · creado el {1}", permissions, token.Created.ToLocalTime().ToString("d", CultureInfo.CurrentCulture))));
        }
        rows.Add(SettingsPages.Link(L.T("Para otro programa"), L.T("Nuevo token…"), Prefix + "token-new"));
        return new(MorePage, L.T("Ranuras y API local"), L.T("Qué extensión ocupa cada ranura, y los programas de este equipo que le piden cosas a Claupet."),
            null, rows, PageId);
    }

    /// <summary>Las tarjetas: las del catálogo (con lo instalado de cada una) y, después, las instaladas que no están en él.</summary>
    internal IReadOnlyList<ExtensionCard> Cards()
    {
        var cards = new List<ExtensionCard>();
        var app = SemVersion.App;
        foreach (var entry in _catalog?.Entries ?? [])
        {
            var installed = Manager.Find(entry.Id);
            var newer = installed is null || entry.Version > installed.Manifest.Version;
            cards.Add(new ExtensionCard(entry.Id, installed?.Manifest.Name.Text ?? entry.Name.Text,
                installed?.Manifest.Description.Text ?? entry.Description.Text, entry.Provides,
                installed is null ? CatalogPage(entry.Id) : DetailPage(entry.Id),
                Official: installed is null || installed.Record.Trust == PackageTrust.Official,
                Installed: installed?.Manifest.Version.ToString(), Latest: entry.Version.ToString(),
                Update: installed is not null && entry.Version > installed.Manifest.Version && entry.Compatible(app),
                NeedsClaupet: newer && !entry.Compatible(app) ? entry.MinClaupet.ToString() : null,
                Downloads: entry.Downloads, Percent: _downloads.TryGetValue(entry.Id, out var p) ? p : null,
                Enabled: installed is null || Manager.IsEnabled(entry.Id)));
        }
        foreach (var ext in Manager.Installed.Where(e => cards.All(c => c.Id != e.Id)))
            cards.Add(new ExtensionCard(ext.Id, ext.Manifest.Name.Text, ext.Manifest.Description.Text, ext.Manifest.Provides, DetailPage(ext.Id),
                Official: ext.Record.Trust == PackageTrust.Official, Installed: ext.Manifest.Version.ToString(), Enabled: Manager.IsEnabled(ext.Id)));
        return cards;
    }

    /// <summary>Lo que se dice del catálogo bajo las tarjetas.</summary>
    string CatalogStatus
    {
        get
        {
            if (_refreshing) return L.T("Buscando en el catálogo…");
            if (_catalogProblem is { } problem) return problem;
            if (_catalog is null) return L.T("Todavía no se leyó el catálogo oficial.");
            return _catalogChecked is { } at
                ? L.T("Catálogo oficial verificado · {0}", Ago(DateTimeOffset.UtcNow - at))
                : L.T("Catálogo oficial verificado");
        }
    }

    static string Ago(TimeSpan span) => span.TotalMinutes < 1 ? L.T("hace un momento")
        : span.TotalHours < 1 ? L.T("hace {0} min", (int)span.TotalMinutes)
        : span.TotalDays < 1 ? L.T("hace {0} h", (int)span.TotalHours)
        : L.T("hace {0} días", (int)span.TotalDays);

    /// <summary>Una del catálogo que no está instalada: lo que dice el catálogo y «Instalar».</summary>
    SettingsPage CatalogDetail(CatalogEntry entry)
    {
        var page = CatalogPage(entry.Id);
        var rows = new List<SettingsRow>();
        if (!entry.Compatible(SemVersion.App))
            rows.Add(SettingsPages.Note(L.T("Necesita Claupet {0} o más nueva. Actualice Claupet para instalarla.", entry.MinClaupet), RowTone.Warn));
        else if (_downloads.TryGetValue(entry.Id, out var percent))
            rows.Add(SettingsPages.Note(L.T("Descargando y comprobando la firma… {0} %", percent)));
        else
            rows.Add(new(RowKind.Link, L.T("Oficial, firmada por Claupet · {0}", Size(entry.Size)), Prefix + "get:" + entry.Id, L.T("Instalar"), Tone: RowTone.Accent));
        AddNote(rows, page, "get");
        rows.Add(SettingsPages.Caption(L.T("Detalle")));
        rows.Add(SettingsPages.Info(L.T("Versión"), entry.Version.ToString()));
        if (entry.Author is { Length: > 0 } author) rows.Add(SettingsPages.Info(L.T("Autor"), author));
        rows.Add(SettingsPages.Info(L.T("Qué trae"), string.Join(", ", entry.Provides.Select(ExtensionSlots.Label))));
        rows.Add(SettingsPages.Info(L.T("Tamaño"), Size(entry.Size)));
        if (entry.Downloads is { } downloads) rows.Add(SettingsPages.Info(L.T("Descargas"), downloads.ToString("N0", CultureInfo.CurrentCulture)));
        if (entry.License is { Length: > 0 } license) rows.Add(SettingsPages.Info(L.T("Licencia"), license));
        rows.Add(SettingsPages.Link(L.T("Su código"), L.T("Abrir en GitHub"), Prefix + "source:" + entry.Id));
        rows.Add(SettingsPages.Caption(L.T("Permisos")));
        rows.Add(entry.Permissions.Count == 0
            ? SettingsPages.Note(L.T("No pide permisos."))
            : SettingsPages.Note(string.Join("\n", entry.Permissions.Select(p => "• " + ExtensionPermissions.Label(p))), RowTone.Normal));
        rows.Add(SettingsPages.Note(L.T("Antes de instalarla verá su firma y sus permisos, tal como vienen en el paquete."), RowTone.Muted));
        return new(page, entry.Name.Text, entry.Description.Text, null, rows, PageId);
    }

    static string Size(long bytes) => bytes < 1024 * 1024
        ? L.T("{0} KB", Math.Max(1, bytes / 1024))
        : L.T("{0} MB", (bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.CurrentCulture));

    SettingsPage Detail(InstalledExtension ext)
    {
        var m = ext.Manifest;
        var state = Manager.StateOf(ext.Id);
        var page = DetailPage(ext.Id);
        var rows = new List<SettingsRow> { SettingsPages.Switch(L.T("Encendida"), Prefix + "on:" + ext.Id, state.Enabled) };
        if (state.DisabledReason is { } reason) rows.Add(SettingsPages.Note(L.T("Se apagó: {0}", reason), RowTone.Warn));
        if (_catalog?.Find(ext.Id) is { } entry && entry.Version > m.Version)
        {
            if (_downloads.TryGetValue(ext.Id, out var percent))
                rows.Add(SettingsPages.Note(L.T("Descargando la versión {0}… {1} %", entry.Version, percent)));
            else if (!entry.Compatible(SemVersion.App))
                rows.Add(SettingsPages.Note(L.T("La versión {0} necesita Claupet {1} o más nueva.", entry.Version, entry.MinClaupet), RowTone.Warn));
            else
                rows.Add(new(RowKind.Link, L.T("Versión {0} disponible", entry.Version), Prefix + "get:" + ext.Id, L.T("Actualizar"), Tone: RowTone.Accent));
            AddNote(rows, page, "get");
        }

        rows.Add(SettingsPages.Caption(L.T("Detalle")));
        rows.Add(SettingsPages.Info(L.T("Versión"), m.Version.ToString()));
        if (m.Author is { Length: > 0 } author) rows.Add(SettingsPages.Info(L.T("Autor"), author));
        rows.Add(SettingsPages.Info(L.T("Firma"), SignatureLabel(ext.Record)));
        rows.Add(SettingsPages.Info(L.T("Qué trae"), Provides(m)));
        rows.Add(SettingsPages.Info(L.T("Estado"), StatusOf(ext)));
        if (m.Commands.Count > 0) rows.Add(SettingsPages.Info(L.T("Comandos en el chat"), string.Join(", ", m.Commands.Select(c => "/" + c.Name))));
        if (m.Events.Count > 0) rows.Add(SettingsPages.Info(L.T("Se entera de"), string.Join(", ", m.Events.Select(EventLabel))));
        if (m.License is { Length: > 0 } license) rows.Add(SettingsPages.Info(L.T("Licencia"), license));
        if (WebAddress(m.Homepage) is { } home)
        {
            rows.Add(SettingsPages.Link(L.T("Sitio"), L.T("Abrir"), Prefix + "home:" + ext.Id));
            rows.Add(SettingsPages.Note(home));
        }

        rows.Add(SettingsPages.Caption(L.T("Permisos")));
        rows.Add(m.Permissions.Count == 0
            ? SettingsPages.Note(L.T("No pide permisos."))
            : SettingsPages.Note(string.Join("\n", m.Permissions.Select(p => "• " + ExtensionPermissions.Label(p))), RowTone.Normal));
        if (m.RunsCode) rows.Add(SettingsPages.Note(RunsCodeText));

        if (m.Settings.Count > 0)
        {
            rows.Add(SettingsPages.Caption(L.T("Sus ajustes")));
            foreach (var spec in m.Settings)
            {
                var key = $"{Prefix}set:{ext.Id}:{spec.Key}";
                var value = Manager.ValueOf(ext, spec.Key);
                switch (spec.Type)
                {
                    case SettingSpec.Bool:
                        rows.Add(SettingsPages.Switch(spec.Label.Text, key, value == "true"));
                        break;
                    case SettingSpec.Choice:
                        rows.Add(SettingsPages.Choice(spec.Label.Text, key, value ?? ""));
                        break;
                    case SettingSpec.Secret:
                        // La clave guardada no se muestra: lo que se escriba la cambia y «Borrar» la quita.
                        rows.Add(new(RowKind.Input, spec.Label.Text, key, Secret: true,
                            Placeholder: value is { Length: > 0 } ? L.T("Guardada; escriba otra para cambiarla") : L.T("Sin guardar")));
                        if (value is { Length: > 0 }) rows.Add(SettingsPages.Links(new SettingsLink(L.T("Borrar la clave guardada"), $"{Prefix}clear:{ext.Id}:{spec.Key}")));
                        break;
                    default:
                        rows.Add(new(RowKind.Input, spec.Label.Text, key, value ?? "", Placeholder: spec.Default));
                        break;
                }
                AddNote(rows, page, spec.Key);
            }
        }

        rows.Add(SettingsPages.Caption(L.T("Más")));
        rows.Add(SettingsPages.Links(new SettingsLink(L.T("Abrir su registro"), Prefix + "log:" + ext.Id),
            new SettingsLink(L.T("Abrir su carpeta de datos"), Prefix + "data:" + ext.Id)));
        AddNote(rows, page, "log");
        rows.Add(SettingsPages.Link(L.T("Quitarla de este equipo"), L.T("Desinstalar…"), Prefix + "remove:" + ext.Id, RowTone.Danger));
        AddNote(rows, page, "remove");
        return new(page, m.Name.Text, m.Description.Text, null, rows, PageId);
    }

    SettingsPage Review(Pending pending)
    {
        if (pending.Review is not { } review)
            return new(ReviewPage, L.T("No se puede instalar «{0}»", pending.File), "", null,
            [
                SettingsPages.Note(pending.Problem ?? "", RowTone.Danger),
                SettingsPages.Links(new SettingsLink(L.T("Cerrar"), Prefix + "review-cancel")),
            ], PageId);

        var m = review.Manifest;
        var rows = new List<SettingsRow> { SettingsPages.Info(L.T("Versión"), m.Version.ToString()) };
        if (review.IsUpdate) rows.Add(SettingsPages.Note(L.T("Actualiza la versión {0} instalada.", review.Existing!.Manifest.Version)));
        if (review.IsDowngrade) rows.Add(SettingsPages.Note(L.T("Es más vieja que la instalada ({0}): vuelve a una versión anterior.", review.Existing!.Manifest.Version), RowTone.Warn));
        if (review.IsReinstall) rows.Add(SettingsPages.Note(L.T("Ya está instalada esta versión: se vuelve a instalar.")));
        if (m.Author is { Length: > 0 } author) rows.Add(SettingsPages.Info(L.T("Autor"), author));
        rows.Add(SettingsPages.Info(L.T("Qué trae"), Provides(m)));
        if (m.Commands.Count > 0) rows.Add(SettingsPages.Info(L.T("Comandos en el chat"), string.Join(", ", m.Commands.Select(c => "/" + c.Name))));
        if (m.Events.Count > 0) rows.Add(SettingsPages.Info(L.T("Se entera de"), string.Join(", ", m.Events.Select(EventLabel))));

        rows.Add(SettingsPages.Caption(L.T("Firma")));
        rows.Add(SettingsPages.Note(review.TrustText, review.Trust switch
        {
            PackageTrust.Official or PackageTrust.Trusted => RowTone.Normal,
            PackageTrust.NewPublisher or PackageTrust.Unsigned => RowTone.Warn,
            _ => RowTone.Danger,
        }));
        if (m.RunsCode) rows.Add(SettingsPages.Note(RunsCodeText));

        var permissions = review.Existing is null ? m.Permissions : review.NewPermissions;
        if (permissions.Count > 0)
        {
            rows.Add(SettingsPages.Caption(review.Existing is null ? L.T("Pide permiso para") : L.T("Ahora también pide permiso para")));
            rows.Add(SettingsPages.Note(string.Join("\n", permissions.Select(p => "• " + ExtensionPermissions.Label(p))), RowTone.Normal));
        }

        var slots = m.Provides.Where(ExtensionSlots.IsExclusive).ToList();
        if (review.Allowed && (slots.Count > 0 || m.Pet is not null))
        {
            rows.Add(SettingsPages.Caption(L.T("Al instalarla")));
            foreach (var slot in slots)
                rows.Add(SettingsPages.Switch(L.T("Usarla para «{0}» (deja de usarse {1})", ExtensionSlots.Label(slot), SlotLabel(slot)),
                    Prefix + "review-slot:" + slot, pending.Slots.Contains(slot)));
            if (m.Pet is not null)
                rows.Add(SettingsPages.Switch(L.T("Usar «{0}» como la mascota del escritorio", m.Pet.Name.Text), Prefix + "review-pet", pending.Pet));
        }

        if (pending.Problem is { } problem) rows.Add(SettingsPages.Note(problem, RowTone.Danger));
        if (pending.Installing) rows.Add(SettingsPages.Note(L.T("Instalando…"), RowTone.Normal));
        else if (review.Allowed)
            rows.Add(SettingsPages.Links(new SettingsLink(review.IsUpdate ? L.T("Actualizar") : L.T("Instalar"), Prefix + "review-ok"),
                new SettingsLink(L.T("Cancelar"), Prefix + "review-cancel")));
        else rows.Add(SettingsPages.Links(new SettingsLink(L.T("Cerrar"), Prefix + "review-cancel")));
        return new(ReviewPage, L.T("Instalar «{0}»", m.Name.Text), m.Description.Text, null, rows, PageId);
    }

    SettingsPage Token(TokenDraft draft)
    {
        var rows = new List<SettingsRow>();
        if (draft.Created is { } token)
        {
            rows.Add(SettingsPages.Note(L.T("Cópielo ahora: no se vuelve a mostrar. Va en «Authorization: Bearer <token>».")));
            rows.Add(new(RowKind.Link, token, Value: L.T("Copiar"), Tone: RowTone.Accent, Copy: token));
            rows.Add(SettingsPages.Links(new SettingsLink(L.T("Listo"), Prefix + "token-done")));
        }
        else
        {
            rows.Add(new(RowKind.Input, L.T("Para qué programa es"), Prefix + "token-name", draft.Name, Placeholder: L.T("Por ejemplo, «Mi script»")));
            if (draft.NameMissing) rows.Add(SettingsPages.Note(L.T("Escriba para qué programa es."), RowTone.Warn));
            rows.Add(SettingsPages.Caption(L.T("Lo que puede pedir")));
            foreach (var permission in TokenPermissions)
                rows.Add(SettingsPages.Switch(ExtensionPermissions.Label(permission), Prefix + "token-perm:" + permission, draft.Permissions.Contains(permission)));
            rows.Add(SettingsPages.Links(new SettingsLink(L.T("Crear"), Prefix + "token-create"), new SettingsLink(L.T("Cancelar"), Prefix + "token-cancel")));
        }
        return new(TokenPage, L.T("Nuevo token de la API local"), L.T("Para que otro programa de este equipo le pida cosas a Claupet."), null, rows, PageId);
    }

    void AddNote(List<SettingsRow> rows, string page, string key)
    {
        if (_note is { } note && note.Page == page && note.Key == key) rows.Add(SettingsPages.Note(note.Text, RowTone.Warn));
    }

    // ── Textos ──────────────────────────────────────────────────────────────────────────────

    static string RunsCodeText => L.T("Corre un programa en este equipo, con sus permisos. Claupet controla lo que le pide a la app, no lo que el programa haga por su cuenta.");

    string NameOf(string id) => Manager.Find(id)?.Manifest.Name.Text ?? id;

    string StatusOf(InstalledExtension ext)
    {
        var state = Manager.StateOf(ext.Id);
        return !state.Enabled ? state.DisabledReason is null ? L.T("Apagada") : L.T("Apagada (falló)")
            : Manager.IsRunning(ext.Id) ? L.T("Corriendo") : L.T("Encendida");
    }

    static string Provides(ExtensionManifest m) => string.Join(", ", m.Provides.Select(ExtensionSlots.Label));

    /// <summary>Quién ocupa la ranura: la extensión o lo que trae Claupet.</summary>
    string SlotLabel(string slot) => Manager.ProviderFor(slot)?.Manifest.Name.Text ?? L.T("Integrado: {0}", ExtensionSlots.BuiltIn(slot));

    public static string TrustLabel(PackageTrust trust) => trust switch
    {
        PackageTrust.Official => L.T("Oficial"),
        PackageTrust.Trusted or PackageTrust.NewPublisher => L.T("Firmada"),
        PackageTrust.Unsigned => L.T("Sin firma"),
        _ => trust.ToString(),
    };

    static string SignatureLabel(InstallRecord record) => record.Trust switch
    {
        PackageTrust.Official => L.T("Oficial de Claupet"),
        _ when record.Signer is { } signer => L.T("Firmada por {0}", signer),
        _ => TrustLabel(record.Trust),
    };

    /// <summary>De qué se entera una habilidad, para la persona.</summary>
    public static string EventLabel(string name) => name switch
    {
        "notice" => L.T("los avisos"),
        "toolUse" => L.T("las herramientas que usan sus sesiones"),
        "session" => L.T("el estado de sus sesiones"),
        "drop" => L.T("los archivos que suelte en la mascota o la isla"),
        "chat" => L.T("lo que se dice en el chat"),
        _ => name,
    };

    /// <summary>El sitio de la extensión, solo si es una dirección web.</summary>
    static string? WebAddress(string? homepage) =>
        Uri.TryCreate(homepage, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? uri.AbsoluteUri : null;

    // ── Acciones ────────────────────────────────────────────────────────────────────────────

    /// <summary>Una acción de estas páginas (value: lo escrito en un cuadro o el archivo elegido). Corre en el hilo de la bandeja.</summary>
    public void Act(string key, string? value)
    {
        var (name, argument) = Split(key[Prefix.Length..]);
        _note = null;
        switch (name)
        {
            case "install":
                if (value is { Length: > 0 } file) _ = ReviewAsync(file);
                break;
            case "catalog":
                _ = RefreshCatalogAsync(force: true);
                break;
            case "more":
                console.Show(MorePage);
                return;
            case "get" when _catalog?.Find(argument) is { } entry && entry.Compatible(SemVersion.App) && !_downloads.ContainsKey(entry.Id):
                _ = DownloadAsync(entry);
                break;
            case "update-all" when _catalog is { } catalog:
                foreach (var entry in catalog.Entries)
                    if (Manager.Find(entry.Id) is { } ext && entry.Version > ext.Manifest.Version && entry.Compatible(SemVersion.App)
                        && !_downloads.ContainsKey(entry.Id))
                        _ = DownloadAsync(entry, quiet: true);
                break;
            case "source" when _catalog?.Find(argument) is { } entry:
                Open($"{ExtensionCatalog.Repository}/tree/main/extensions/{entry.Id}");
                break;

            case "on" when Manager.Find(argument) is { } ext:
                _ = RunAsync(() => Manager.SetEnabledAsync(ext.Id, !Manager.IsEnabled(ext.Id)));
                break;
            case "set":
                SetValue(argument, value);
                break;
            case "clear" when SplitSetting(argument) is { } setting:
                _ = SaveAsync(setting.Ext, setting.Spec, null);
                break;
            case "home" when Manager.Find(argument) is { } ext && WebAddress(ext.Manifest.Homepage) is { } home:
                Open(home);
                break;
            case "log" when Manager.Find(argument) is { } ext:
                var log = new ExtensionLog(ext.DataFolder).FilePath;
                if (System.IO.File.Exists(log)) Open(log);
                else _note = (DetailPage(ext.Id), "log", L.T("Todavía no escribió nada en su registro."));
                break;
            case "data" when Manager.Find(argument) is { } ext:
                Directory.CreateDirectory(ext.DataFolder);
                Open(ext.DataFolder);
                break;
            case "remove" when Manager.Find(argument) is { } ext:
                console.Offer(
                [
                    new("borrar", L.T("Desinstalar y borrar sus datos y claves"), Danger: true),
                    new("guardar", L.T("Desinstalar y guardar sus datos")),
                ], "", choice => _ = UninstallAsync(ext, keepData: choice == "guardar"));
                break;
            case "slot" when ExtensionSlots.IsExclusive(argument):
                var options = new List<ConsoleOption> { new("", L.T("Integrado: {0}", ExtensionSlots.BuiltIn(argument))) };
                options.AddRange(Manager.Installed.Where(e => e.Manifest.Provides.Contains(argument) && Manager.IsEnabled(e.Id))
                    .Select(e => new ConsoleOption(e.Id, e.Manifest.Name.Text)));
                console.Offer(options, Manager.ProviderFor(argument)?.Id ?? "",
                    id => _ = RunAsync(() => Manager.SetProviderAsync(argument, id.Length == 0 ? null : id)));
                break;
            case "api":
                host.SetLocalApi(!host.LocalApiEnabled);
                break;
            case "revoke" when host.ApiTokens.FirstOrDefault(t => t.Id == argument) is { } token:
                console.Offer([new("revocar", L.T("Revocar «{0}»: deja de poder pedirle cosas a Claupet", token.Name), Danger: true)], "", _ =>
                {
                    host.RevokeApiToken(token.Id);
                    console.Push();
                });
                break;
            case "token-new":
                _token = new TokenDraft();
                console.Push();
                console.Show(TokenPage);
                return;
            case "token-name" when _token is { } draft:
                draft.Name = (value ?? "").Trim();
                if (draft.Name.Length > 60) draft.Name = draft.Name[..60];
                draft.NameMissing = false;
                break;
            case "token-perm" when _token is { } draft && TokenPermissions.Contains(argument):
                if (!draft.Permissions.Remove(argument)) draft.Permissions.Add(argument);
                break;
            case "token-create" when _token is { Created: null } draft:
                if (draft.Name.Length == 0) draft.NameMissing = true;
                else draft.Created = host.CreateApiToken(draft.Name, TokenPermissions.Where(draft.Permissions.Contains).ToList());
                break;
            case "token-cancel" or "token-done":
                _token = null;
                console.Push();
                console.Show(PageId);
                return;
            case "review-slot" when _review is { } pending:
                if (!pending.Slots.Remove(argument)) pending.Slots.Add(argument);
                break;
            case "review-pet" when _review is { } pending:
                pending.Pet = !pending.Pet;
                break;
            case "review-ok" when _review is { Review: { Allowed: true } review, Installing: false } pending:
                _ = InstallAsync(pending, review);
                break;
            case "review-cancel":
                CloseReview();
                console.Push();
                console.Show(PageId);
                return;
        }
        console.Push();
    }

    static (string Name, string Argument) Split(string rest)
    {
        var colon = rest.IndexOf(':');
        return colon < 0 ? (rest, "") : (rest[..colon], rest[(colon + 1)..]);
    }

    /// <summary>«id:clave» de un ajuste: la extensión y lo que declara de él.</summary>
    (InstalledExtension Ext, SettingSpec Spec)? SplitSetting(string argument)
    {
        var colon = argument.LastIndexOf(':');
        if (colon < 0 || Manager.Find(argument[..colon]) is not { } ext) return null;
        var spec = ext.Manifest.Settings.FirstOrDefault(s => s.Key == argument[(colon + 1)..]);
        return spec is null ? null : (ext, spec);
    }

    /// <summary>Un ajuste de una extensión: los interruptores cambian; las listas se eligen; lo escrito se valida.</summary>
    void SetValue(string argument, string? value)
    {
        if (SplitSetting(argument) is not { } setting) return;
        var (ext, spec) = setting;
        switch (spec.Type)
        {
            case SettingSpec.Bool:
                _ = SaveAsync(ext, spec, Manager.ValueOf(ext, spec.Key) == "true" ? "false" : "true");
                break;
            case SettingSpec.Choice:
                console.Offer(spec.Choices.Select(c => new ConsoleOption(c, c)).ToList(), Manager.ValueOf(ext, spec.Key) ?? "",
                    choice => _ = SaveAsync(ext, spec, choice));
                break;
            case SettingSpec.Secret:
                // Vacío no borra la guardada (para eso está «Borrar»).
                if (value is { Length: > 0 } secret) _ = SaveAsync(ext, spec, secret);
                break;
            case SettingSpec.Number:
                var text = value?.Trim() ?? "";
                if (text.Length == 0) { _ = SaveAsync(ext, spec, null); break; }
                // Se escribe con la coma o el punto de la persona; se guarda con punto.
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number)
                    && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                {
                    _note = (DetailPage(ext.Id), spec.Key, L.T("Valor inválido para «{0}».", spec.Label.Text));
                    break;
                }
                _ = SaveAsync(ext, spec, number.ToString(CultureInfo.InvariantCulture));
                break;
            default:
                _ = SaveAsync(ext, spec, value?.Trim() is { Length: > 0 } written ? written : null);
                break;
        }
    }

    async Task SaveAsync(InstalledExtension ext, SettingSpec spec, string? value)
    {
        if (value == Manager.ValueOf(ext, spec.Key)) return;
        string? problem = null;
        try { await Manager.SetValueAsync(ext, spec.Key, value); }
        catch (ArgumentException ex) { problem = ex.Message; }
        console.Post(() =>
        {
            if (problem is not null) _note = (DetailPage(ext.Id), spec.Key, problem);
            console.Push();
        });
    }

    /// <summary>Algo del gestor que tarda (encender, cambiar de ranura): al terminar se ve lo nuevo.</summary>
    async Task RunAsync(Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
        console.Post(console.Push);
    }

    async Task UninstallAsync(InstalledExtension ext, bool keepData)
    {
        string? problem = null;
        try { await Manager.UninstallAsync(ext.Id, keepData); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problem = L.T("No se pudo desinstalar: {0}", ex.Message); }
        console.Post(() =>
        {
            if (problem is not null) _note = (DetailPage(ext.Id), "remove", problem);
            console.Push();
            if (problem is null) console.Show(PageId);
        });
    }

    // ── Instalar ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Abre el paquete y lo deja esperando en la página de revisión (qué es, su firma, sus permisos y sus ranuras), con la
    /// Console abierta ahí: se instala solo si la persona toca «Instalar». Lo usan «Instalar desde archivo…» y soltar un
    /// .claupet sobre la mascota o la isla.
    /// </summary>
    /// deleteAfter: el paquete se bajó del catálogo; ya abierto (o si no sirve), se borra.
    public async Task ReviewAsync(string path, bool deleteAfter = false)
    {
        InstallReview? review = null;
        string? problem = null;
        try { review = await Task.Run(() => Manager.Store.Prepare(path)); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { problem = ex.Message; }
        finally { if (deleteAfter) TryDelete(path); }
        console.Post(() =>
        {
            CloseReview();
            var pending = _review = new Pending(Path.GetFileName(path), review, problem);
            if (review is not null)
                foreach (var slot in review.Manifest.Provides.Where(ExtensionSlots.IsExclusive))
                    // Nueva: se usa en sus ranuras. Una actualización sigue donde estaba.
                    if (review.Existing is null || Manager.ProviderFor(slot)?.Id == review.Existing.Id) pending.Slots.Add(slot);
            console.Push();
            console.Show(ReviewPage);
        });
    }

    async Task InstallAsync(Pending pending, InstallReview review)
    {
        pending.Installing = true;
        pending.Problem = null;
        InstalledExtension? installed = null;
        string? problem = null;
        try
        {
            installed = await Manager.InstallAsync(review, pending.Slots.ToList());
            await host.InstalledAsync(installed, review.IsUpdate, review.Manifest.Pet is not null && pending.Pet);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ErrorLog.Write(ex);
            problem = L.T("No se pudo instalar: {0}", ex.Message);
        }
        console.Post(() =>
        {
            pending.Installing = false;
            if (installed is null)
            {
                pending.Problem = problem;
                console.Push();
                return;
            }
            if (_review == pending) CloseReview();
            console.Push();
            console.Show(DetailPage(installed.Id));
        });
    }

    // ── Catálogo ────────────────────────────────────────────────────────────────────────────

    /// <summary>La primera vez que se arman las páginas: el catálogo guardado y, si toca (una vez por día), el del repositorio.</summary>
    void LoadCatalog()
    {
        if (_catalogLoaded) return;
        _catalogLoaded = true;
        _catalog = Catalogs.Cached();
        _catalogChecked = Catalogs.LastChecked;
        if (Catalogs.Due(DateTimeOffset.UtcNow)) _ = RefreshCatalogAsync(force: false);
    }

    /// <summary>Pide el catálogo al repositorio (force: aunque no haya pasado un día) y muestra lo nuevo.</summary>
    internal async Task RefreshCatalogAsync(bool force)
    {
        if (_refreshing || (!force && !Catalogs.Due(DateTimeOffset.UtcNow))) return;
        _refreshing = true;
        console.Post(console.Push);
        ExtensionCatalog? catalog = null;
        string? problem = null;
        try { (catalog, problem) = await Catalogs.RefreshAsync(CatalogHttp); }
        catch (OperationCanceledException) { }
        console.Post(() =>
        {
            _refreshing = false;
            if (catalog is not null) _catalog = catalog;
            _catalogProblem = problem;
            _catalogChecked = Catalogs.LastChecked;
            console.Push();
        });
    }

    /// <summary>
    /// Baja el paquete de esa entrada (comprobando su tamaño y su SHA-256 contra el catálogo) y lo abre en la página de
    /// revisión, como uno elegido a mano. quiet («Actualizar todas»): una actualización oficial que no pide permisos nuevos
    /// se instala sin pasar por la revisión; si pide algo nuevo, va a la revisión igual.
    /// </summary>
    async Task DownloadAsync(CatalogEntry entry, bool quiet = false)
    {
        _downloads[entry.Id] = 0;
        var folder = Path.Combine(Manager.Store.Root, "descargas");
        string? path = null, problem = null;
        try
        {
            path = await CatalogClient.DownloadAsync(entry, folder, new Progress<long>(have => console.Post(() =>
            {
                var percent = (int)Math.Clamp(have * 100 / Math.Max(1, entry.Size), 0, 100);
                if (!_downloads.TryGetValue(entry.Id, out var before) || before == percent) return;
                _downloads[entry.Id] = percent;
                console.Push();
            })), http: CatalogHttp);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException or OperationCanceledException)
        {
            ErrorLog.Write(ex);
            problem = L.T("No se pudo bajar «{0}»: {1}", entry.Name.Text, ex.Message.TrimEnd('.'));
        }
        if (path is not null && quiet && await TryUpdateQuietlyAsync(path, entry)) return;
        console.Post(() =>
        {
            _downloads.Remove(entry.Id);
            if (problem is not null)
            {
                var page = Manager.Find(entry.Id) is null ? CatalogPage(entry.Id) : DetailPage(entry.Id);
                _note = (quiet ? PageId : page, quiet ? "market" : "get", problem);
                console.Push();
            }
        });
        if (path is not null) await ReviewAsync(path, deleteAfter: true);
    }

    /// <summary>
    /// Una actualización oficial sin permisos nuevos: se instala sin revisión y se borra el paquete. false si necesita la
    /// revisión (el paquete queda para ella).
    /// </summary>
    async Task<bool> TryUpdateQuietlyAsync(string path, CatalogEntry entry)
    {
        InstallReview? review = null;
        try
        {
            review = await Task.Run(() => Manager.Store.Prepare(path));
            if (review is not { IsUpdate: true, Allowed: true, Trust: PackageTrust.Official } || review.NewPermissions.Count > 0)
            {
                review.Package.Dispose();
                return false;
            }
            var slots = review.Manifest.Provides.Where(s => ExtensionSlots.IsExclusive(s) && Manager.ProviderFor(s)?.Id == entry.Id).ToList();
            var installed = await Manager.InstallAsync(review, slots);
            await host.InstalledAsync(installed, update: true, useAsPet: false);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Lo que falló se ve en la revisión (que también borra el paquete).
            ErrorLog.Write(ex);
            review?.Package.Dispose();
            return false;
        }
        TryDelete(path);
        console.Post(() =>
        {
            _downloads.Remove(entry.Id);
            console.Push();
        });
        return true;
    }

    static void TryDelete(string path)
    {
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Sin instalar: lo que se abrió del paquete se borra.</summary>
    void CloseReview()
    {
        if (_review is { Review: { } review, Installing: false }) review.Package.Dispose();
        _review = null;
    }

    static void Open(string target) => Shell.Open(target);
}
