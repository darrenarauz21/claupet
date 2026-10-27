using System.Security.Cryptography;

namespace Claupet.Tests;

public class SigningTests
{
    static readonly string Digest = new('d', 64);

    [Fact]
    public void SignatureRoundTrips()
    {
        using var signer = TestKit.NewSigner();
        var signature = signer.Sign(Digest);
        var parsed = PackageSignature.Parse(signature.ToJson(), out var problem);
        Assert.Null(problem);
        Assert.True(parsed!.Verify(Digest));
        Assert.Equal(signer.KeyId, parsed.KeyId);
        Assert.False(parsed.Verify(new string('e', 64)));
    }

    [Fact]
    public void ChangingSignerOrDateBreaksIt()
    {
        using var signer = TestKit.NewSigner();
        var signature = signer.Sign(Digest);
        Assert.False((signature with { Signer = "Otra persona" }).Verify(Digest));
        Assert.False((signature with { SignedAt = signature.SignedAt.AddSeconds(1) }).Verify(Digest));
    }

    [Fact]
    public void OtherCurvesAreRejected()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.ThrowsAny<CryptographicException>(() => ExtensionKeys.ImportPublic(p384.ExportSubjectPublicKeyInfo()));
        using var rsa = RSA.Create(2048);
        Assert.ThrowsAny<CryptographicException>(() => ExtensionKeys.ImportPublic(rsa.ExportSubjectPublicKeyInfo()));
    }

    [Fact]
    public void GeneratedKeyWithPasswordSigns()
    {
        using var dir = new TempDir();
        var path = dir["clave.pem"];
        var keyId = ExtensionKeys.Generate(path, "contraseña de prueba");
        Assert.True(File.Exists(ExtensionKeys.PublicPath(path)));
        Assert.Throws<IOException>(() => ExtensionKeys.Generate(path, null)); // no se sobrescribe
        Assert.ThrowsAny<CryptographicException>(() => new ExtensionSigner(File.ReadAllText(path), "otra", "Autor"));
        using var signer = new ExtensionSigner(File.ReadAllText(path), "contraseña de prueba", "Autor");
        Assert.Equal(keyId, signer.KeyId);
        Assert.True(signer.Sign(Digest).Verify(Digest));
    }

    [Fact]
    public void TrustLevels()
    {
        using var dir = new TempDir();
        using var official = TestKit.NewSigner("Claupet");
        using var author = TestKit.NewSigner();
        using var other = TestKit.NewSigner("Otro");
        var trust = new TrustStore(dir["confianza.json"], [official.PublicKey]);
        var v = new SemVersion(1, 0, 0, "");

        Assert.Equal(PackageTrust.Official, trust.Evaluate("claupet.notas", v, official.Sign(Digest), Digest, false));
        Assert.Equal(PackageTrust.Unsigned, trust.Evaluate("prueba.notas", v, null, Digest, false));
        Assert.Equal(PackageTrust.NewPublisher, trust.Evaluate("prueba.notas", v, author.Sign(Digest), Digest, false));
        Assert.Equal(PackageTrust.Invalid, trust.Evaluate("prueba.notas", v, author.Sign(Digest), new string('e', 64), false));

        trust.Accept("prueba.notas", author.Sign(Digest));
        Assert.Equal(PackageTrust.Trusted, trust.Evaluate("prueba.notas", v, author.Sign(Digest), Digest, true));
        // La misma extensión con otra clave, o sin firma, ya no.
        Assert.Equal(PackageTrust.KeyChanged, trust.Evaluate("prueba.notas", v, other.Sign(Digest), Digest, true));
        Assert.Equal(PackageTrust.KeyChanged, trust.Evaluate("prueba.notas", v, null, Digest, true));
        // Otra extensión del mismo autor: ya es de confianza.
        Assert.Equal(PackageTrust.Trusted, trust.Evaluate("prueba.otra", v, author.Sign(Digest), Digest, false));

        // Persiste.
        var again = new TrustStore(dir["confianza.json"], [official.PublicKey]);
        Assert.Equal(author.KeyId, again.PinnedKey("prueba.notas"));

        again.Revoke(author.KeyId);
        Assert.Equal(PackageTrust.Revoked, again.Evaluate("prueba.otra", v, author.Sign(Digest), Digest, false));
        again.Forget("prueba.notas");
        Assert.Null(again.PinnedKey("prueba.notas"));
    }

    [Fact]
    public void RevocationListMustBeOfficialAndNewer()
    {
        using var dir = new TempDir();
        using var official = TestKit.NewSigner("Claupet");
        using var stranger = TestKit.NewSigner("Desconocido");
        using var author = TestKit.NewSigner();
        var trust = new TrustStore(dir["confianza.json"], [official.PublicKey]);
        var exts = new Dictionary<string, string> { ["prueba.notas"] = "1.2.0" };

        Assert.False(trust.ApplyRevocations(TrustStore.SignRevocations(stranger, 1, [author.KeyId], exts)));
        Assert.True(trust.ApplyRevocations(TrustStore.SignRevocations(official, 2, [author.KeyId], exts)));
        Assert.True(trust.IsRevoked(author.KeyId));
        Assert.True(trust.IsRevoked("prueba.notas", new SemVersion(1, 1, 9, "")));
        Assert.True(trust.IsRevoked("prueba.notas", new SemVersion(1, 2, 0, "")));
        Assert.False(trust.IsRevoked("prueba.notas", new SemVersion(1, 2, 1, "")));
        // Una lista más vieja (secuencia menor) no deshace nada.
        Assert.False(trust.ApplyRevocations(TrustStore.SignRevocations(official, 1, [], new Dictionary<string, string>())));

        // Alterar el payload rompe la firma.
        var tampered = TrustStore.SignRevocations(official, 3, [], new Dictionary<string, string>()).Replace("\"payload\": \"", "\"payload\": \"e");
        Assert.False(trust.ApplyRevocations(tampered));
    }
}
