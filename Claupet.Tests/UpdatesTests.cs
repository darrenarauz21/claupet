using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Claupet.Tests;

/// <summary>Las versiones nuevas desde GitHub: leer la Release, la firma de sus archivos, bajarla, sacarla y cambiarla.</summary>
public sealed class UpdatesTests
{
    static readonly SemVersion Running = new(2, 2, 1, "");
    const string Asset = "Claupet-windows-x64.exe";

    static string Release(string tag, string baseUrl, bool prerelease = false, params string[] assets) => $$"""
        {
          "tag_name": "{{tag}}", "html_url": "https://github.com/darrenarauz21/claupet/releases/tag/{{tag}}",
          "draft": false, "prerelease": {{(prerelease ? "true" : "false")}},
          "assets": [{{string.Join(",", assets.Select(a => $$"""{ "name": "{{a}}", "browser_download_url": "{{baseUrl}}/{{a}}", "size": 4 }"""))}}]
        }
        """;

    /// <summary>Unos ajustes en memoria y lo que hace falta para crear un Updates de prueba.</summary>
    sealed class Kit : IDisposable
    {
        public TempDir Dir { get; } = new();
        public AppSettings Settings { get; } = new();
        public int Saves { get; private set; }
        public List<ReleaseInfo> Found { get; } = new();

        public Updates Make(string latestUrl, IReadOnlyList<string>? keys = null)
        {
            var updates = new Updates(Settings, () => Saves++, action => action(), Dir["actualizacion"], keys ?? [], latestUrl, Asset, Running);
            updates.Found += Found.Add;
            return updates;
        }

        public void Dispose() => Dir.Dispose();
    }

    [Fact]
    public void LaReleaseSeLeeDeLaRespuestaDeGitHub()
    {
        var release = ReleaseInfo.Parse(Release("v2.3.0", "https://github.com/x/releases/download/v2.3.0", false, Asset, Asset + ".firma"))!;
        Assert.Equal(new SemVersion(2, 3, 0, ""), release.Version);
        Assert.Equal("https://github.com/darrenarauz21/claupet/releases/tag/v2.3.0", release.Page);
        Assert.Equal(4, release.Asset(Asset)!.Size);
        Assert.NotNull(release.Asset(Asset + ".firma"));

        // Una prueba previa, una etiqueta sin versión o un archivo por http (de otro equipo) no sirven.
        Assert.Null(ReleaseInfo.Parse(Release("v2.4.0-beta.1", "https://x", true)));
        Assert.Null(ReleaseInfo.Parse(Release("ultima", "https://x")));
        Assert.Null(ReleaseInfo.Parse("{no es json"));
        Assert.Empty(ReleaseInfo.Parse(Release("v2.3.0", "http://ejemplo.com", false, Asset))!.Assets);
    }

    [Theory]
    [InlineData("win-x64", "Claupet-windows-x64.exe")]
    [InlineData("linux-x64", "Claupet-linux-x64.tar.gz")]
    [InlineData("osx-arm64", "Claupet-macos-arm64.zip")]
    [InlineData("osx-x64", "Claupet-macos-x64.zip")]
    [InlineData("linux-arm64", null)]
    public void CadaSistemaTieneSuArchivo(string platform, string? asset) => Assert.Equal(asset, Updates.AssetFor(platform));

    [Fact]
    public void SeBuscaUnaVezAlDia()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(Updates.Due(null, now));
        Assert.False(Updates.Due(now.AddHours(-23), now));
        Assert.True(Updates.Due(now.AddHours(-25), now));
        Assert.True(Updates.Due(now.AddDays(2), now)); // el reloj volvió atrás
    }

    [Fact]
    public void LaFirmaEsDeEseArchivoEnEsaVersion()
    {
        using var dir = new TempDir();
        using var signer = TestKit.NewSigner("Claupet");
        var file = dir.Write(Asset, "nueva");
        var signature = ReleaseSignature.Sign(signer, file, new SemVersion(2, 3, 0, ""));
        var back = ReleaseSignature.Parse(signature.ToJson())!;
        Assert.Equal(signature, back);
        Assert.True(back.Verify([signer.PublicKey], Asset, new SemVersion(2, 3, 0, "")));

        // Otra clave, otro archivo u otra versión: no.
        using var other = TestKit.NewSigner("Otro");
        Assert.False(back.Verify([other.PublicKey], Asset, new SemVersion(2, 3, 0, "")));
        Assert.False(back.Verify([], Asset, new SemVersion(2, 3, 0, "")));
        Assert.False(back.Verify([signer.PublicKey], "Claupet-linux-x64.tar.gz", new SemVersion(2, 3, 0, "")));
        Assert.False(back.Verify([signer.PublicKey], Asset, new SemVersion(2, 4, 0, "")));
        Assert.False((back with { Size = back.Size + 1 }).Verify([signer.PublicKey], Asset, new SemVersion(2, 3, 0, "")));
        Assert.Null(ReleaseSignature.Parse("{\"format\": 2}"));
    }

    [Fact]
    public void LaHerramientaFirmaLosArchivosDeUnaRelease()
    {
        using var dir = new TempDir();
        var key = dir["clave.pem"];
        ExtensionKeys.Generate(key, null);
        var file = dir.Write(Asset, "nueva");
        Assert.Equal(0, ExtensionCli.TryRun(["--release-sign", key, "2.3.0", file]));
        var signature = ReleaseSignature.Parse(File.ReadAllText(file + ReleaseSignature.Suffix))!;
        var pem = File.ReadAllText(ExtensionKeys.PublicPath(key));
        var spki = string.Concat(pem.Split('\n').Where(l => !l.StartsWith("-----", StringComparison.Ordinal)).Select(l => l.Trim()));
        Assert.True(signature.Verify([spki], Asset, new SemVersion(2, 3, 0, "")));
    }

    [Fact]
    public async Task UnaVersionNuevaSeAvisaUnaSolaVez()
    {
        using var kit = new Kit();
        var tag = "v2.3.0";
        using var server = new FakeHttpServer((_, path, _, _) => path == "/latest"
            ? (200, "application/json", Encoding.UTF8.GetBytes(Release(tag, "https://x", false, Asset)))
            : (404, "text/plain", []));
        var updates = kit.Make(server.Url + "/latest");
        await updates.CheckAsync();
        Assert.Equal(UpdateStep.Available, updates.Step);
        Assert.Equal("2.3.0", Assert.Single(kit.Found).Version.ToString());
        Assert.NotNull(kit.Settings.UpdatesChecked);

        // Sin la clave de las Releases solo se ve la página.
        Assert.False(updates.CanInstall);
        Assert.Equal(UpdateRequest.OpenPage, updates.Act());
        Assert.Equal(L.T("Ver la versión nueva"), updates.Row().Action);

        // La misma versión otra vez no vuelve a avisar; la que corre (o una vieja) no es nueva.
        await updates.CheckAsync();
        Assert.Single(kit.Found);
        tag = "v2.2.1";
        await updates.CheckAsync();
        Assert.Equal(UpdateStep.None, updates.Step);
        Assert.Equal(("", ""), updates.Row());
    }

    [Fact]
    public async Task SinConexionNoDiceNadaYVuelveAIntentarlo()
    {
        using var kit = new Kit();
        using var closed = new FakeHttpServer((_, _, _, _) => (200, "text/plain", []));
        var url = closed.Url + "/latest";
        closed.Dispose(); // el puerto queda cerrado
        var updates = kit.Make(url);
        await updates.CheckAsync();
        Assert.Equal(UpdateStep.None, updates.Step);
        Assert.Null(kit.Settings.UpdatesChecked);
        Assert.Empty(kit.Found);
    }

    [Fact]
    public async Task ConPermisoSeBajaSeCompruebaYQuedaLista()
    {
        using var kit = new Kit();
        using var signer = TestKit.NewSigner("Claupet");
        var content = Encoding.UTF8.GetBytes("Claupet 2.3.0");
        var source = kit.Dir.Write("fuente/" + Asset, "Claupet 2.3.0");
        var signature = Encoding.UTF8.GetBytes(ReleaseSignature.Sign(signer, source, new SemVersion(2, 3, 0, "")).ToJson());
        var served = content;
        FakeHttpServer? server = null;
        server = new FakeHttpServer((_, path, _, _) => path switch
        {
            "/latest" => (200, "application/json", Encoding.UTF8.GetBytes(Release("v2.3.0", server!.Url, false, Asset, Asset + ReleaseSignature.Suffix))),
            "/" + Asset => (200, "application/octet-stream", served),
            "/" + Asset + ReleaseSignature.Suffix => (200, "application/json", signature),
            _ => (404, "text/plain", []),
        });
        using (server)
        {
            var updates = kit.Make(server.Url + "/latest", [signer.PublicKey]);
            await updates.CheckAsync();
            Assert.True(updates.CanInstall);
            Assert.Equal(UpdateRequest.AskToInstall, updates.Act());
            Assert.Contains("2.3.0", updates.ConfirmText);

            await updates.DownloadAsync();
            Assert.Equal(UpdateStep.Ready, updates.Step);
            Assert.Equal(content, File.ReadAllBytes(updates.Staged!));
            Assert.Equal(UpdateRequest.Restart, updates.Act());

            // Cambiarla: la nueva toma el lugar de la que corre y la de antes queda al lado.
            var exe = kit.Dir.Write("app/Claupet.exe", "Claupet 2.2.1");
            Assert.Null(updates.Apply(exe));
            Assert.Equal("Claupet 2.3.0", File.ReadAllText(exe));
            Assert.Equal("Claupet 2.2.1", File.ReadAllText(exe + UpdateInstaller.Previous));
            updates.CleanUp(exe);
            Assert.False(File.Exists(exe + UpdateInstaller.Previous));
        }
    }

    [Fact]
    public async Task LoQueNoCoincideConLaFirmaNoSeInstala()
    {
        using var kit = new Kit();
        using var signer = TestKit.NewSigner("Claupet");
        var source = kit.Dir.Write("fuente/" + Asset, "Claupet 2.3.0");
        var signature = Encoding.UTF8.GetBytes(ReleaseSignature.Sign(signer, source, new SemVersion(2, 3, 0, "")).ToJson());
        FakeHttpServer? server = null;
        server = new FakeHttpServer((_, path, _, _) => path switch
        {
            "/latest" => (200, "application/json", Encoding.UTF8.GetBytes(Release("v2.3.0", server!.Url, false, Asset, Asset + ReleaseSignature.Suffix))),
            // Del mismo tamaño, pero otro contenido: el SHA-256 no coincide.
            "/" + Asset => (200, "application/octet-stream", Encoding.UTF8.GetBytes("Claupet 6.6.6")),
            "/" + Asset + ReleaseSignature.Suffix => (200, "application/json", signature),
            _ => (404, "text/plain", []),
        });
        using (server)
        {
            var updates = kit.Make(server.Url + "/latest", [signer.PublicKey]);
            await updates.CheckAsync();
            await updates.DownloadAsync();
            Assert.Equal(UpdateStep.Failed, updates.Step);
            Assert.Null(updates.Staged);
            Assert.Equal(UpdateRequest.OpenPage, updates.Act());

            // Firmada con otra clave: tampoco.
            using var other = TestKit.NewSigner("Otro");
            var stranger = kit.Make(server.Url + "/latest", [other.PublicKey]);
            await stranger.CheckAsync();
            await stranger.DownloadAsync();
            Assert.Equal(UpdateStep.Failed, stranger.Step);
            Assert.Contains(L.T("la firma de la versión nueva no es válida"), stranger.Problem);
        }
    }

    [Fact]
    public void DeLinuxSeSacaElEjecutable()
    {
        using var dir = new TempDir();
        var file = dir["Claupet-linux-x64.tar.gz"];
        using (var stream = File.Create(file))
        using (var gzip = new GZipStream(stream, CompressionLevel.Fastest))
        using (var tar = new TarWriter(gzip))
        {
            foreach (var (name, text) in new[] { ("Claupet-linux-x64/LEEME.txt", "léame"), ("Claupet-linux-x64/Claupet", "binario") })
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)) };
                tar.WriteEntry(entry);
            }
        }
        var exe = UpdateInstaller.Unpack(file, dir["nueva"]);
        Assert.Equal("Claupet", Path.GetFileName(exe));
        Assert.Equal("binario", File.ReadAllText(exe));
    }

    [Fact]
    public void DeMacSeSacaElPunteroAppYNadaFueraDeLaCarpeta()
    {
        using var dir = new TempDir();
        var file = TestKit.RawZip(dir,
            ("Claupet-macos-arm64/Claupet.app/Contents/MacOS/Claupet", TestKit.Utf8("binario")),
            ("Claupet-macos-arm64/Claupet.app/Contents/Info.plist", TestKit.Utf8("plist")),
            ("Claupet-macos-arm64/preparar-mac.sh", TestKit.Utf8("script")));
        var zip = Path.ChangeExtension(file, ".zip");
        File.Move(file, zip);
        var app = UpdateInstaller.Unpack(zip, dir["nueva"]);
        Assert.Equal("Claupet.app", Path.GetFileName(app));
        Assert.Equal("binario", File.ReadAllText(Path.Combine(app, "Contents", "MacOS", "Claupet")));
        Assert.False(File.Exists(Path.Combine(dir["nueva"], "preparar-mac.sh")));

        var hostile = Path.ChangeExtension(TestKit.RawZip(dir, ("x/Claupet.app/../../fuera.txt", TestKit.Utf8("no"))), ".zip");
        File.Move(Path.ChangeExtension(hostile, ".claupet"), hostile);
        Assert.Throws<InvalidDataException>(() => UpdateInstaller.Unpack(hostile, dir["otra"]));
        Assert.False(File.Exists(dir["fuera.txt"]));
    }

    [Fact]
    public void CambiarUnaCarpetaYVolverSiFalla()
    {
        using var dir = new TempDir();
        var app = dir["Claupet.app"];
        Directory.CreateDirectory(app);
        dir.Write("Claupet.app/version.txt", "vieja");
        var staged = dir["nueva/Claupet.app"];
        Directory.CreateDirectory(staged);
        dir.Write("nueva/Claupet.app/version.txt", "nueva");
        UpdateInstaller.Swap(app, staged);
        Assert.Equal("nueva", File.ReadAllText(Path.Combine(app, "version.txt")));
        Assert.Equal("vieja", File.ReadAllText(Path.Combine(app + UpdateInstaller.Previous, "version.txt")));

        // Si lo nuevo ya no está, vuelve lo de antes.
        var exe = dir.Write("Claupet.exe", "vieja");
        Assert.ThrowsAny<IOException>(() => UpdateInstaller.Swap(exe, dir["no-existe.exe"]));
        Assert.Equal("vieja", File.ReadAllText(exe));
    }

    [Fact]
    public void AjustesSistemaTieneElInterruptorYLaVersionNueva()
    {
        var page = SettingsPages.Build(new SettingsState { UpdateStatus = "Claupet 2.3.0 está disponible", UpdateAction = "Ver la versión nueva" })
            .Single(p => p.Id == "sistema");
        Assert.Contains(page.Rows, r => r is { Kind: RowKind.Switch, Key: "updates", On: true, Place: RowPlace.Console });
        // Con algo que hacer, también en los rápidos.
        Assert.Contains(page.Rows, r => r is { Kind: RowKind.Link, Key: "update", Place: RowPlace.Both });

        var without = SettingsPages.Build(new SettingsState { Features = PlatformFeatures.All & ~PlatformFeatures.Updates }).Single(p => p.Id == "sistema");
        Assert.DoesNotContain(without.Rows, r => r.Key is "updates" or "update");
        Assert.True(new AppSettings().CheckUpdates); // encendido de entrada
    }

    [Fact]
    public void LaClaveDeLasReleasesEsUnaP256()
    {
        // La pública (SubjectPublicKeyInfo en base64) de una clave P-256, como las que crea --extension-keygen.
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(Updates.ReleaseKey), out var read);
        Assert.Equal(Convert.FromBase64String(Updates.ReleaseKey).Length, read);
        Assert.Equal(ECCurve.NamedCurves.nistP256.Oid.Value, key.ExportParameters(false).Curve.Oid.Value);
    }

    [Fact]
    public void Se_borran_las_extracciones_de_versiones_anteriores_salvo_la_actual_y_las_en_uso_enteras()
    {
        using var temp = new TempDir();
        var root = Directory.CreateDirectory(temp[".net/Claupet"]).FullName;
        var current = Directory.CreateDirectory(Path.Combine(root, "actual")).FullName;
        File.WriteAllText(Path.Combine(current, "libSkiaSharp.dll"), "x");
        foreach (var old in new[] { "vieja1", "vieja2" })
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(root, old)).FullName, "onnxruntime.dll"), "x");
        var busy = Directory.CreateDirectory(Path.Combine(root, "en-uso")).FullName;
        // Una que esa versión todavía no cargó: no se tiene que ir (la cargaría después y fallaría).
        File.WriteAllText(Path.Combine(busy, "System.Net.Primitives.dll"), "x");
        // Abierta sin dejar escribir, como una biblioteca cargada por otra versión.
        using (new FileStream(Path.Combine(busy, "libSkiaSharp.dll"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
            Assert.Equal(2, Updates.DeleteOldExtractions(current));
        Assert.True(Directory.Exists(current));
        Assert.True(File.Exists(Path.Combine(busy, "libSkiaSharp.dll")));
        Assert.True(File.Exists(Path.Combine(busy, "System.Net.Primitives.dll")));
        Assert.False(Directory.Exists(busy + ".borrar"));
        Assert.False(Directory.Exists(Path.Combine(root, "vieja1")));
        Assert.False(Directory.Exists(Path.Combine(root, "vieja2")));
        // Ya libre, la próxima vez se va.
        Assert.Equal(1, Updates.DeleteOldExtractions(current));
        Assert.Equal([current], Directory.GetDirectories(root));
    }
}
