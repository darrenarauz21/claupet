using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet;

/// <summary>
/// Una extensión del catálogo oficial: lo que se muestra antes de instalarla y de dónde se baja. Lo que se
/// instala es lo que dice el manifiesto firmado del paquete; Name, Description, Provides y Permissions son
/// solo para mostrar. Url, Size y Sha256 atan la descarga a lo que firmó el catálogo. Downloads: cuántas veces se
/// bajó, sumando todas sus Releases (al armar el catálogo); null si no se sabe.
/// </summary>
sealed record CatalogEntry(string Id, SemVersion Version, LocalizedText Name, LocalizedText Description, string? Icon,
    string? Author, string? License, SemVersion? MinClaupet, IReadOnlyList<string> Provides, IReadOnlyList<string> Permissions,
    string Url, string Sha256, long Size, string? KeyId, long? Downloads = null)
{
    /// <summary>Esta versión de Claupet la puede cargar.</summary>
    public bool Compatible(SemVersion app) => MinClaupet is not { } min || app >= min;

    /// <summary>El nombre del archivo al bajarla.</summary>
    public string FileName => $"{Id}-{Version}.claupet";
}

/// <summary>
/// El catálogo de las extensiones oficiales (github.com/darrenarauz21/claupet-extensions, catalog.json): un
/// documento firmado con la clave oficial, con la misma forma que la lista de revocaciones. Su payload lleva
/// «sequence», las revocaciones («keys» y «extensions», como la lista) y «packages», la lista de extensiones.
/// Leerlo también aplica sus revocaciones; uno con una secuencia menor a la última vista se rechaza (nadie
/// puede servir uno viejo que liste una versión vulnerable).
/// </summary>
sealed record ExtensionCatalog(long Sequence, DateTimeOffset? Generated, IReadOnlyList<CatalogEntry> Entries)
{
    public const int SchemaVersion = 1;
    public const string Repository = "https://github.com/darrenarauz21/claupet-extensions";
    public const string Url = "https://raw.githubusercontent.com/darrenarauz21/claupet-extensions/main/catalog.json";
    const string Raw = "https://raw.githubusercontent.com/darrenarauz21/claupet-extensions/main/extensions";

    /// <summary>Un catálogo de más de esto no se lee.</summary>
    public const int MaxBytes = 4 << 20;

    /// <summary>Se vuelve a pedir como mucho una vez por día (con ETag, así que casi siempre es un 304).</summary>
    public static readonly TimeSpan Every = TimeSpan.FromHours(24);

    /// <summary>De dónde se baja cada versión: el asset de la Release «&lt;id&gt;-v&lt;versión&gt;».</summary>
    public static string PackageUrl(string id, SemVersion version) => $"{Repository}/releases/download/{id}-v{version}/{id}-{version}.claupet";

    /// <summary>La extensión en el catálogo, si está.</summary>
    public CatalogEntry? Find(string id) => Entries.FirstOrDefault(e => e.Id == id);

    /// <summary>
    /// Verifica la firma con las claves oficiales de trust, aplica sus revocaciones y lo lee. null si la firma no
    /// sirve, la secuencia es más vieja que la última vista o el formato no es el que entiende esta app.
    /// </summary>
    public static ExtensionCatalog? Read(string json, TrustStore trust)
    {
        if (json.Length > MaxBytes || trust.VerifiedPayload(json) is not { } payload) return null;
        if (!trust.ApplyRevocations(json)) return null;
        return Parse(payload);
    }

    /// <summary>El payload ya verificado. Las entradas que no sirven se saltan (no tiran el catálogo entero).</summary>
    internal static ExtensionCatalog? Parse(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 8 });
            var r = doc.RootElement;
            if (!r.TryGetProperty("sequence", out var s) || !s.TryGetInt64(out var sequence)) return null;
            if (r.TryGetProperty("schema", out var schema) && (!schema.TryGetInt32(out var v) || v > SchemaVersion)) return null;
            var entries = new List<CatalogEntry>();
            if (r.TryGetProperty("packages", out var packages) && packages.ValueKind == JsonValueKind.Array)
                foreach (var p in packages.EnumerateArray())
                    if (Entry(p) is { } entry && entries.All(e => e.Id != entry.Id)) entries.Add(entry);
            return new ExtensionCatalog(sequence, Json.Date(r, "generated"), entries);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { return null; }
    }

    static CatalogEntry? Entry(JsonElement p)
    {
        if (p.ValueKind != JsonValueKind.Object) return null;
        var id = Json.Str(p, "id");
        var version = SemVersion.Parse(Json.Str(p, "version"));
        var url = Json.Str(p, "url");
        var sha = Json.Str(p, "sha256");
        if (id is null || !ExtensionManifest.ValidId(id) || version is null || !Https(url) || sha is not { Length: 64 } || !sha.All(Uri.IsHexDigit))
            return null;
        if (!p.TryGetProperty("size", out var sz) || !sz.TryGetInt64(out var size) || size <= 0 || size > StagedPackage.MaxTotal) return null;
        var icon = Json.Str(p, "icon");
        return new CatalogEntry(id, version.Value, Text(p, "name") ?? new LocalizedText(new Dictionary<string, string> { ["en"] = id }),
            Text(p, "description") ?? LocalizedText.Empty, Https(icon) ? icon : null, Json.Str(p, "author"), Json.Str(p, "license"),
            SemVersion.Parse(Json.Str(p, "minClaupet")), Strings(p, "provides"), Strings(p, "permissions"), url!, sha.ToLowerInvariant(),
            size, Json.Str(p, "keyId"), p.TryGetProperty("downloads", out var d) && d.TryGetInt64(out var n) && n >= 0 ? n : null);
    }

    static bool Https(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;

    static LocalizedText? Text(JsonElement p, string name)
    {
        if (!p.TryGetProperty(name, out var t)) return null;
        if (t.ValueKind == JsonValueKind.String) return new LocalizedText(new Dictionary<string, string> { ["en"] = t.GetString()! });
        if (t.ValueKind != JsonValueKind.Object) return null;
        var map = new Dictionary<string, string>();
        foreach (var lang in t.EnumerateObject())
            if (lang.Value.ValueKind == JsonValueKind.String) map[lang.Name] = lang.Value.GetString()!;
        return map.Count > 0 ? new LocalizedText(map) : null;
    }

    static IReadOnlyList<string> Strings(JsonElement p, string name) =>
        p.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : [];

    /// <summary>
    /// Arma el catálogo firmado desde los paquetes (--extension-catalog, para el responsable del repositorio). Cada
    /// paquete se abre y se valida como al instalar; su url es la de su Release (PackageUrl) y su ícono, el
    /// icon.png de su carpeta en el repositorio si el paquete trae uno. downloads: por id, cuántas veces se bajó (de
    /// ReleaseDownloadsAsync). Lanza InvalidDataException si uno no sirve.
    /// </summary>
    public static string Build(ExtensionSigner signer, long sequence, IEnumerable<string> packages, string staging,
        IEnumerable<string>? revokedKeys = null, IReadOnlyDictionary<string, string>? revokedVersions = null, DateTimeOffset? now = null,
        IReadOnlyDictionary<string, long>? downloads = null)
    {
        var list = new JsonArray();
        var seen = new HashSet<string>();
        foreach (var package in packages)
        {
            using var staged = StagedPackage.Open(package, staging);
            if (!staged.Ok) throw new InvalidDataException($"{Path.GetFileName(package)}: {string.Join("; ", staged.Errors)}");
            var m = staged.Manifest!;
            if (!seen.Add(m.Id)) throw new InvalidDataException(L.T("«{0}» está dos veces.", m.Id));
            string sha;
            using (var file = File.OpenRead(package)) sha = Convert.ToHexStringLower(SHA256.HashData(file));
            var entry = new JsonObject
            {
                ["id"] = m.Id,
                ["version"] = m.Version.ToString(),
                ["name"] = Localized(m.Name),
                ["description"] = Localized(m.Description),
                ["author"] = m.Author,
                ["license"] = m.License,
                ["minClaupet"] = m.MinClaupet?.ToString(),
                ["provides"] = new JsonArray(m.Provides.Select(x => (JsonNode)x).ToArray()),
                ["permissions"] = new JsonArray(m.Permissions.Select(x => (JsonNode)x).ToArray()),
                ["url"] = PackageUrl(m.Id, m.Version),
                ["sha256"] = sha,
                ["size"] = new FileInfo(package).Length,
                ["keyId"] = staged.Signature?.KeyId,
            };
            if (staged.Files.Any(f => f.Path == "icon.png")) entry["icon"] = $"{Raw}/{m.Id}/icon.png";
            if (downloads is not null && downloads.TryGetValue(m.Id, out var count)) entry["downloads"] = count;
            list.Add(entry);
        }
        var payload = new JsonObject
        {
            ["schema"] = SchemaVersion,
            ["sequence"] = sequence,
            ["generated"] = (now ?? DateTimeOffset.UtcNow).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["keys"] = new JsonArray((revokedKeys ?? []).Select(k => (JsonNode)k).ToArray()),
            ["extensions"] = new JsonObject((revokedVersions ?? new Dictionary<string, string>()).Select(e => KeyValuePair.Create(e.Key, (JsonNode?)e.Value))),
            ["packages"] = list,
        };
        return TrustStore.SignOfficial(signer, payload);
    }

    /// <summary>
    /// Cuántas veces se bajó cada extensión: la suma de los .claupet de todas sus Releases («&lt;id&gt;-v&lt;versión&gt;»), de la
    /// API de GitHub. Vacío si no se pudo leer (el catálogo sale igual, sin el número).
    /// </summary>
    public static async Task<Dictionary<string, long>> ReleaseDownloadsAsync(HttpClient http, string? api = null, CancellationToken cancel = default)
    {
        var counts = new Dictionary<string, long>();
        var url = api ?? "https://api.github.com/repos/darrenarauz21/claupet-extensions/releases?per_page=100";
        try
        {
            for (var page = 1; page <= 20; page++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{url}&page={page}");
                request.Headers.UserAgent.ParseAdd($"Claupet/{SemVersion.App}");
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using var response = await http.SendAsync(request, cancel);
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0) break;
                foreach (var release in doc.RootElement.EnumerateArray())
                {
                    if (Json.Str(release, "tag_name") is not { } tag) continue;
                    var at = tag.LastIndexOf("-v", StringComparison.Ordinal);
                    if (at < 1) continue;
                    var id = tag[..at];
                    if (!ExtensionManifest.ValidId(id) || !release.TryGetProperty("assets", out var assets)) continue;
                    foreach (var asset in assets.EnumerateArray())
                        if (Json.Str(asset, "name")?.EndsWith(".claupet", StringComparison.Ordinal) == true
                            && asset.TryGetProperty("download_count", out var c) && c.TryGetInt64(out var n))
                            counts[id] = counts.GetValueOrDefault(id) + n;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Console.WriteLine(L.T("No se pudieron leer las descargas de GitHub: {0}", ex.Message));
            return new();
        }
        return counts;
    }

    static JsonObject Localized(LocalizedText text) => new(text.ByLanguage.Select(t => KeyValuePair.Create(t.Key, (JsonNode?)t.Value)));
}

/// <summary>
/// El catálogo en el equipo: lo pide al repositorio (con ETag, como mucho una vez por día salvo que se pida a mano),
/// lo guarda en extensiones\catalogo.json y lo vuelve a verificar al leerlo. Sin red, queda el último bueno.
/// </summary>
sealed class CatalogClient
{
    readonly string _cache, _url;
    readonly TrustStore _trust;

    public CatalogClient(string root, TrustStore trust, string? url = null)
    {
        _cache = Path.Combine(root, "catalogo.json");
        _trust = trust;
        _url = url ?? ExtensionCatalog.Url;
    }

    string EtagPath => _cache + ".etag";

    /// <summary>El último catálogo guardado, verificado otra vez; null si no hay o ya no sirve.</summary>
    public ExtensionCatalog? Cached()
    {
        try { return File.Exists(_cache) ? ExtensionCatalog.Read(File.ReadAllText(_cache), _trust) : null; }
        catch (IOException) { return null; }
    }

    /// <summary>Cuándo se pidió por última vez con éxito (o se confirmó con un 304); null si nunca.</summary>
    public DateTimeOffset? LastChecked => File.Exists(_cache) ? new DateTimeOffset(File.GetLastWriteTimeUtc(_cache), TimeSpan.Zero) : null;

    /// <summary>Hace falta pedirlo: nunca se pidió, pasó un día o el reloj volvió atrás.</summary>
    public bool Due(DateTimeOffset now) => !File.Exists(_cache) || File.GetLastWriteTimeUtc(_cache) is var last
        && (now.UtcDateTime - last >= ExtensionCatalog.Every || last > now.UtcDateTime);

    /// <summary>
    /// Lo pide al repositorio y devuelve el catálogo verificado. Si no hay red, el servidor responde mal o lo que llega
    /// no sirve, devuelve el guardado (Problem dice por qué).
    /// </summary>
    public async Task<(ExtensionCatalog? Catalog, string? Problem)> RefreshAsync(HttpClient? client = null, CancellationToken cancel = default)
    {
        var http = client ?? SystemProxy.Client(TimeSpan.FromSeconds(20));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _url);
            request.Headers.UserAgent.ParseAdd($"Claupet/{SemVersion.App}");
            if (File.Exists(_cache) && File.Exists(EtagPath) && File.ReadAllText(EtagPath).Trim() is { Length: > 0 } etag)
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                File.SetLastWriteTimeUtc(_cache, DateTime.UtcNow);
                return (Cached(), null);
            }
            if (response.StatusCode == HttpStatusCode.NotFound) return (Cached(), null);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > ExtensionCatalog.MaxBytes)
                return (Cached(), L.T("El catálogo es demasiado grande."));
            var json = await response.Content.ReadAsStringAsync(cancel);
            if (ExtensionCatalog.Read(json, _trust) is not { } catalog)
                return (Cached(), L.T("El catálogo no tiene una firma válida de Claupet."));
            Directory.CreateDirectory(Path.GetDirectoryName(_cache)!);
            var temp = _cache + ".parte";
            File.WriteAllText(temp, json);
            File.Move(temp, _cache, overwrite: true);
            if (response.Headers.ETag?.ToString() is { } newTag) File.WriteAllText(EtagPath, newTag);
            else File.Delete(EtagPath);
            return (catalog, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            if (cancel.IsCancellationRequested) throw;
            ErrorLog.Write(ex);
            return (Cached(), L.T("No se pudo leer el catálogo: {0}", ex.Message.TrimEnd('.')));
        }
        finally
        {
            if (client is null) http.Dispose();
        }
    }

    /// <summary>
    /// Baja el paquete de esa entrada a folder y comprueba su tamaño y su SHA-256 contra el catálogo (si no
    /// coincide, se borra). Devuelve la ruta, lista para ExtensionStore.Prepare.
    /// http: para las pruebas (sin él, el de siempre, con reintentos y reanudando lo bajado).
    /// </summary>
    public static async Task<string> DownloadAsync(CatalogEntry entry, string folder, IProgress<long>? progress = null,
        CancellationToken cancel = default, HttpClient? http = null)
    {
        var path = Path.Combine(folder, entry.FileName);
        if (File.Exists(path)) File.Delete(path);
        var mismatch = L.T("lo descargado no coincide con el catálogo; se borró");
        if (http is null)
        {
            await ModelDownload.FetchAsync(entry.Url, path, entry.Size, entry.Sha256, progress ?? new Progress<long>(), cancel, mismatch);
            return path;
        }
        Directory.CreateDirectory(folder);
        var bytes = await http.GetByteArrayAsync(entry.Url, cancel);
        if (bytes.LongLength != entry.Size || Convert.ToHexStringLower(SHA256.HashData(bytes)) != entry.Sha256) throw new IOException(mismatch);
        await File.WriteAllBytesAsync(path, bytes, cancel);
        progress?.Report(bytes.LongLength);
        return path;
    }
}
