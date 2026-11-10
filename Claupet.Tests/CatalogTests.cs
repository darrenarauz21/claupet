using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Claupet.Tests;

public class CatalogTests
{
    static (string Catalog, string Package) Build(TempDir dir, ExtensionSigner official, long sequence, string id = "claupet.notas", string version = "1.0.0",
        IReadOnlyDictionary<string, string>? revoked = null)
    {
        var package = TestKit.Package(dir, TestKit.Folder(dir, TestKit.SkillManifest(id, version), ("bin/notas.cmd", "@echo off")), official);
        return (ExtensionCatalog.Build(official, sequence, [package], dir["staging"], null, revoked), package);
    }

    [Fact]
    public void The_app_trusts_the_official_extensions_key()
    {
        using var dir = new TempDir();
        var trust = new TrustStore(dir["confianza.json"]);
        Assert.Contains("2f0a01cf13797cf2a233c84187b864a39514c031cca91d87c85350133341c410", trust.OfficialKeyIds);
    }

    [Fact]
    public void Lists_what_the_official_key_signed()
    {
        using var dir = new TempDir();
        using var official = TestKit.NewSigner("Claupet");
        var trust = new TrustStore(dir["confianza.json"], [official.PublicKey]);
        var (json, package) = Build(dir, official, 1);

        var catalog = ExtensionCatalog.Read(json, trust);

        Assert.NotNull(catalog);
        Assert.Equal(1, catalog.Sequence);
        var entry = Assert.Single(catalog.Entries);
        Assert.Equal("claupet.notas", entry.Id);
        Assert.Equal("1.0.0", entry.Version.ToString());
        Assert.Equal("Notes", entry.Name.ByLanguage["en"]);
        Assert.Equal(new FileInfo(package).Length, entry.Size);
        using (var file = File.OpenRead(package)) Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(file)), entry.Sha256);
        Assert.Equal(official.KeyId, entry.KeyId);
        Assert.Equal(ExtensionCatalog.PackageUrl("claupet.notas", entry.Version), entry.Url);
        Assert.Same(entry, catalog.Find("claupet.notas"));
    }

    [Fact]
    public void Rejects_another_key_a_tampered_payload_and_an_older_sequence()
    {
        using var dir = new TempDir();
        using var official = TestKit.NewSigner("Claupet");
        using var stranger = TestKit.NewSigner("Otro");
        var trust = new TrustStore(dir["confianza.json"], [official.PublicKey]);

        Assert.Null(ExtensionCatalog.Read(Build(dir, stranger, 5).Catalog, trust));

        var good = Build(dir, official, 5).Catalog;
        Assert.Null(ExtensionCatalog.Read(good.Replace("\"payload\": \"", "\"payload\": \"e"), trust));

        Assert.NotNull(ExtensionCatalog.Read(good, trust));
        // El mismo vuelve a servir (el guardado se lee al abrir la app); uno más viejo, no.
        Assert.NotNull(ExtensionCatalog.Read(good, trust));
        Assert.Null(ExtensionCatalog.Read(Build(dir, official, 4).Catalog, trust));
    }

    [Fact]
    public void Applies_its_revocations()
    {
        using var dir = new TempDir();
        using var official = TestKit.NewSigner("Claupet");
        var trust = new TrustStore(dir["confianza.json"], [official.PublicKey]);
        var json = Build(dir, official, 2, revoked: new Dictionary<string, string> { ["claupet.viejo"] = "1.4.0" }).Catalog;

        Assert.NotNull(ExtensionCatalog.Read(json, trust));
        Assert.True(trust.IsRevoked("claupet.viejo", new SemVersion(1, 4, 0, "")));
        Assert.False(trust.IsRevoked("claupet.viejo", new SemVersion(1, 4, 1, "")));
    }

    [Fact]
    public void Skips_entries_that_do_not_serve()
    {
        var good = new JsonObject
        {
            ["id"] = "claupet.bien", ["version"] = "1.0.0", ["url"] = "https://example.com/a.claupet",
            ["sha256"] = new string('a', 64), ["size"] = 10, ["minClaupet"] = "9.0.0",
        };
        JsonObject With(string key, JsonNode? value) { var o = (JsonObject)good.DeepClone(); o[key] = value; return o; }
        var payload = new JsonObject
        {
            ["sequence"] = 1,
            ["packages"] = new JsonArray(
                good.DeepClone(),
                With("id", "Mal Id"),
                With("url", "http://example.com/a.claupet"),
                With("sha256", "xyz"),
                With("size", 0),
                With("version", "uno")),
        }.ToJsonString();

        var catalog = ExtensionCatalog.Parse(payload);

        var entry = Assert.Single(catalog!.Entries);
        Assert.Equal("claupet.bien", entry.Id);
        Assert.False(entry.Compatible(new SemVersion(2, 6, 0, "")));
        Assert.True(entry.Compatible(new SemVersion(9, 0, 0, "")));
        Assert.Null(ExtensionCatalog.Parse("""{"schema": 99, "sequence": 1}"""));
    }

    [Fact]
    public async Task Client_keeps_the_last_good_one_and_uses_the_etag()
    {
        using var dir = new TempDir();
        using var official = TestKit.NewSigner("Claupet");
        using var stranger = TestKit.NewSigner("Otro");
        var trust = new TrustStore(dir["confianza.json"], [official.PublicKey]);
        var good = Build(dir, official, 3).Catalog;
        var handler = new FakeHandler();
        using var http = new HttpClient(handler);
        var client = new CatalogClient(dir["extensiones"], trust, "https://example.com/catalog.json");

        Assert.True(client.Due(DateTimeOffset.UtcNow));
        handler.Next = () => Ok(good, "\"v1\"");
        var (first, problem) = await client.RefreshAsync(http);
        Assert.Null(problem);
        Assert.Equal(3, first!.Sequence);
        Assert.False(client.Due(DateTimeOffset.UtcNow));

        // 304: el guardado, y se pidió con su ETag.
        handler.Next = () => new HttpResponseMessage(HttpStatusCode.NotModified);
        var (again, _) = await client.RefreshAsync(http);
        Assert.Equal("\"v1\"", handler.LastEtag);
        Assert.Equal(3, again!.Sequence);

        // Uno firmado con otra clave no reemplaza al guardado.
        handler.Next = () => Ok(Build(dir, stranger, 9).Catalog, "\"v2\"");
        var (kept, why) = await client.RefreshAsync(http);
        Assert.NotNull(why);
        Assert.Equal(3, kept!.Sequence);

        // Sin red, también el guardado.
        handler.Next = () => throw new HttpRequestException("sin red");
        var (offline, reason) = await client.RefreshAsync(http);
        Assert.NotNull(reason);
        Assert.Equal(3, offline!.Sequence);
    }

    static HttpResponseMessage Ok(string body, string etag)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        response.Headers.ETag = System.Net.Http.Headers.EntityTagHeaderValue.Parse(etag);
        return response;
    }

    sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpResponseMessage> Next { get; set; } = () => new HttpResponseMessage(HttpStatusCode.NotFound);
        public string? LastEtag { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastEtag = request.Headers.TryGetValues("If-None-Match", out var v) ? v.FirstOrDefault() : null;
            return Task.FromResult(Next());
        }
    }
}
