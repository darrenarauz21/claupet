namespace Claupet.Tests;

public class StoreTests
{
    static ExtensionStore NewStore(TempDir dir) => new(dir["extensiones"], new TrustStore(dir["extensiones/confianza.json"], []));

    [Fact]
    public void InstallListVerifyUninstall()
    {
        using var dir = new TempDir();
        var store = NewStore(dir);
        using var signer = TestKit.NewSigner();
        var zip = TestKit.Package(dir, TestKit.Folder(dir, TestKit.SkillManifest(), ("bin/notas.cmd", "@echo off")), signer);

        var review = store.Prepare(zip);
        Assert.Equal(PackageTrust.NewPublisher, review.Trust);
        Assert.True(review.Allowed);
        Assert.Null(review.Existing);
        var installed = store.Commit(review);

        var listed = Assert.Single(store.List());
        Assert.Equal("prueba.notas", listed.Id);
        Assert.True(listed.Record.Signed);
        Assert.Null(store.Verify(listed));
        Assert.True(Directory.Exists(installed.DataFolder));

        // Alguien cambia un archivo en el disco: ya no corre.
        File.AppendAllText(installed.FileOf("bin/notas.cmd"), "\ndel *");
        Assert.NotNull(store.Verify(listed));

        store.Uninstall("prueba.notas", keepData: false);
        Assert.Empty(store.List());
        Assert.False(Directory.Exists(installed.Folder));
        Assert.False(Directory.Exists(installed.DataFolder));
    }

    [Fact]
    public void UpdateReplacesAndKeepsData()
    {
        using var dir = new TempDir();
        var store = NewStore(dir);
        using var signer = TestKit.NewSigner();
        var v1 = store.Commit(store.Prepare(TestKit.Package(dir, TestKit.Folder(dir, TestKit.SkillManifest(), ("bin/notas.cmd", "v1"), ("viejo.txt", "x")), signer)));
        File.WriteAllText(Path.Combine(v1.DataFolder, "notas.txt"), "mis notas");

        var review = store.Prepare(TestKit.Package(dir, TestKit.Folder(dir, TestKit.SkillManifest(version: "1.1.0"), ("bin/notas.cmd", "v2")), signer));
        Assert.True(review.IsUpdate);
        Assert.Equal(PackageTrust.Trusted, review.Trust);
        var v2 = store.Commit(review);

        Assert.Equal("v2", File.ReadAllText(v2.FileOf("bin/notas.cmd")));
        Assert.False(File.Exists(v2.FileOf("viejo.txt"))); // lo de la versión anterior no queda mezclado
        Assert.Equal("mis notas", File.ReadAllText(Path.Combine(v2.DataFolder, "notas.txt")));
        Assert.Null(store.Verify(Assert.Single(store.List())));
    }

    [Fact]
    public void UpdateFromOtherKeyOrUnsignedIsRefused()
    {
        using var dir = new TempDir();
        var store = NewStore(dir);
        using var signer = TestKit.NewSigner();
        using var other = TestKit.NewSigner("Otro");
        store.Commit(store.Prepare(TestKit.Package(dir, TestKit.Folder(dir, TestKit.SkillManifest(), ("bin/notas.cmd", "v1")), signer)));

        var foreign = store.Prepare(TestKit.Package(dir, TestKit.Folder(dir, TestKit.SkillManifest(version: "2.0.0"), ("bin/notas.cmd", "malo")), other));
        Assert.Equal(PackageTrust.KeyChanged, foreign.Trust);
        Assert.False(foreign.Allowed);
        Assert.Throws<InvalidOperationException>(() => store.Commit(foreign));
        foreign.Package.Dispose();

        var unsigned = store.Prepare(TestKit.Package(dir, TestKit.Folder(dir, TestKit.SkillManifest(version: "2.0.0"), ("bin/notas.cmd", "malo"))));
        Assert.Equal(PackageTrust.KeyChanged, unsigned.Trust);
        unsigned.Package.Dispose();

        Assert.Equal("v1", File.ReadAllText(Assert.Single(store.List()).FileOf("bin/notas.cmd")));
    }

    [Fact]
    public void TamperedPackageIsInvalid()
    {
        using var dir = new TempDir();
        var store = NewStore(dir);
        using var signer = TestKit.NewSigner();
        var folder = TestKit.Folder(dir, TestKit.SkillManifest(), ("bin/notas.cmd", "bueno"));
        var zip = TestKit.Package(dir, folder, signer);
        // Se cambia un archivo dentro del zip firmado.
        using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Update))
        {
            archive.GetEntry("bin/notas.cmd")!.Delete();
            using var s = archive.CreateEntry("bin/notas.cmd").Open();
            s.Write(TestKit.Utf8("malo"));
        }
        var review = store.Prepare(zip);
        Assert.Equal(PackageTrust.Invalid, review.Trust);
        Assert.False(review.Allowed);
        review.Package.Dispose();
        Assert.Empty(store.List());
    }

    [Fact]
    public void UnsignedInstallsWithWarning()
    {
        using var dir = new TempDir();
        var store = NewStore(dir);
        var review = store.Prepare(TestKit.Package(dir, TestKit.Folder(dir, TestKit.SkillManifest(), ("bin/notas.cmd", "x"))));
        Assert.Equal(PackageTrust.Unsigned, review.Trust);
        Assert.True(review.Allowed);
        Assert.Contains("Sin firma", review.TrustText);
        var installed = store.Commit(review);
        Assert.False(installed.Record.Signed);
        Assert.Null(store.Verify(installed));
    }

    [Fact]
    public void InvalidManifestDoesNotLeaveStaging()
    {
        using var dir = new TempDir();
        var store = NewStore(dir);
        var zip = TestKit.RawZip(dir, ("manifest.json", TestKit.Utf8("{ \"schema\": 1, \"id\": \"MAL\" }")));
        Assert.Throws<InvalidDataException>(() => store.Prepare(zip));
        Assert.Empty(Directory.EnumerateDirectories(dir["extensiones/preparacion"]));
    }
}
