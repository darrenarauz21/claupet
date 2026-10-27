using System.IO.Compression;

namespace ClaudeUsageTray.Tests;

public class PackageTests
{
    [Theory]
    [InlineData("../x.txt")]
    [InlineData("a/../../x.txt")]
    [InlineData("/x.txt")]
    [InlineData("a\\b.txt")]
    [InlineData("C:x.txt")]
    [InlineData("con.txt")]
    [InlineData("bin/NUL")]
    [InlineData("a/./b.txt")]
    [InlineData("a.")]
    [InlineData("a /b")]
    [InlineData("a//b")]
    [InlineData("datos:ads")]
    public void BadPathsAreRejected(string path) => Assert.Null(PackagePaths.Normalize(path));

    [Theory]
    [InlineData("manifest.json")]
    [InlineData("bin/notas.cmd")]
    [InlineData("voces/lucía.json")]
    [InlineData("a/b/c/d.png")]
    public void GoodPathsAreKept(string path) => Assert.Equal(path, PackagePaths.Normalize(path));

    [Fact]
    public void ZipSlipIsRejected()
    {
        using var dir = new TempDir();
        var zip = TestKit.RawZip(dir, ("manifest.json", TestKit.Utf8(TestKit.SkillManifest())), ("../../evil.txt", TestKit.Utf8("x")));
        var ex = Assert.Throws<InvalidDataException>(() => StagedPackage.Open(zip, dir["preparacion"]));
        Assert.Contains("evil", ex.Message);
        Assert.False(File.Exists(Path.Combine(dir.Path, "evil.txt")));
        Assert.Empty(Directory.EnumerateDirectories(dir["preparacion"])); // no quedó nada a medias
    }

    [Fact]
    public void DuplicateIgnoringCaseIsRejected()
    {
        using var dir = new TempDir();
        var zip = TestKit.RawZip(dir, ("manifest.json", TestKit.Utf8(TestKit.SkillManifest())), ("bin/a.txt", [1]), ("BIN/A.txt", [2]));
        Assert.Throws<InvalidDataException>(() => StagedPackage.Open(zip, dir["preparacion"]));
    }

    [Fact]
    public void SymlinkEntryIsRejected()
    {
        using var dir = new TempDir();
        var zip = Path.Combine(dir.Path, "enlace.claupet");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var s = archive.CreateEntry("manifest.json").Open()) s.Write(TestKit.Utf8(TestKit.SkillManifest()));
            var link = archive.CreateEntry("bin/notas.cmd");
            link.ExternalAttributes = unchecked((int)(0xA1FFu << 16)); // lrwxrwxrwx
            using (var s = link.Open()) s.Write(TestKit.Utf8("/etc/passwd"));
        }
        var ex = Assert.Throws<InvalidDataException>(() => StagedPackage.Open(zip, dir["preparacion"]));
        Assert.Contains("enlace", ex.Message);
    }

    [Fact]
    public void ZipBombIsRejected()
    {
        using var dir = new TempDir();
        var zip = TestKit.RawZip(dir, ("manifest.json", TestKit.Utf8(TestKit.SkillManifest())), ("ceros.bin", new byte[20 << 20]));
        var ex = Assert.Throws<InvalidDataException>(() => StagedPackage.Open(zip, dir["preparacion"]));
        Assert.Contains("descomprime", ex.Message);
    }

    [Fact]
    public void ReservedFolderIsRejected()
    {
        using var dir = new TempDir();
        var zip = TestKit.RawZip(dir, ("manifest.json", TestKit.Utf8(TestKit.SkillManifest())), ("_claupet/otra.json", [1]));
        Assert.Throws<InvalidDataException>(() => StagedPackage.Open(zip, dir["preparacion"]));
    }

    [Fact]
    public void BuiltPackageOpensWithSameDigest()
    {
        using var dir = new TempDir();
        var folder = TestKit.Folder(dir, TestKit.SkillManifest(), ("bin/notas.cmd", "@echo off"));
        var zip = TestKit.Package(dir, folder);
        using var staged = StagedPackage.Open(zip, dir["preparacion"]);
        Assert.True(staged.Ok, string.Join("\n", staged.Errors));
        Assert.Equal(PackagePaths.Digest(PackagePaths.Scan(folder)), staged.Digest);
        Assert.Null(staged.Signature);
    }

    [Fact]
    public void DigestCoversEveryFile()
    {
        var a = new List<PackageFile> { new("manifest.json", 10, new string('a', 64)), new("x.txt", 1, new string('b', 64)) };
        var reordered = new List<PackageFile> { a[1], a[0] };
        var changed = new List<PackageFile> { a[0], a[1] with { Sha256 = new string('c', 64) } };
        var renamed = new List<PackageFile> { a[0], a[1] with { Path = "y.txt" } };
        Assert.Equal(PackagePaths.Digest(a), PackagePaths.Digest(reordered));
        Assert.NotEqual(PackagePaths.Digest(a), PackagePaths.Digest(changed));
        Assert.NotEqual(PackagePaths.Digest(a), PackagePaths.Digest(renamed));
        Assert.NotEqual(PackagePaths.Digest(a), PackagePaths.Digest(a.Take(1)));
    }
}
