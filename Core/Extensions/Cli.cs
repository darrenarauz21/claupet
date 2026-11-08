namespace Claupet;

/// <summary>
/// Lo de las extensiones por línea de comandos, igual en las dos apps (como --dump, la salida se ve si se
/// redirige: «Claupet.exe --extension-verify x.claupet | more»):
///   --anfitrion-extension &lt;dll&gt; [--un-hilo]     el anfitrión de una DLL (lo arranca Claupet, no la persona)
///   --extension-keygen &lt;clave.pem&gt;               una clave de firma nueva (contraseña en CLAUPET_KEY_PASSWORD)
///   --extension-pack &lt;carpeta&gt; &lt;salida.claupet&gt; [--clave &lt;clave.pem&gt; --firmante &lt;nombre&gt;]
///   --extension-verify &lt;paquete.claupet&gt;         qué es, si sirve y quién lo firmó
///   --extension-revocations &lt;clave.pem&gt; &lt;secuencia&gt; &lt;salida.json&gt; [--clave-revocada &lt;keyId&gt;]… [--version-revocada &lt;id&gt;=&lt;versión&gt;]…
///   --extension-catalog &lt;clave.pem&gt; &lt;secuencia&gt; &lt;salida.json&gt; [&lt;paquete.claupet&gt;…] [--clave-revocada …]… [--version-revocada …]…
///                                                       el catálogo firmado de las extensiones oficiales (catalog.json)
///   --release-sign &lt;clave.pem&gt; &lt;versión&gt; &lt;archivo&gt;…    la firma de cada archivo de una Release (archivo.firma), con la
///                                                       clave de las Releases (la usa empaquetar.py --clave)
/// </summary>
static class ExtensionCli
{
    /// <summary>Si los argumentos son de extensiones, los atiende y devuelve el código de salida; si no, null.</summary>
    public static int? TryRun(string[] args)
    {
        if (Index(args, NativeExtensionHost.Flag) is { } h && h + 1 < args.Length)
            return NativeExtensionHost.Run(args[h + 1], args.Contains(NativeExtensionHost.SingleFlag));
        try
        {
            if (Index(args, "--extension-keygen") is { } k && k + 1 < args.Length) return KeyGen(args[k + 1]);
            if (Index(args, "--extension-pack") is { } p && p + 2 < args.Length) return Pack(args[p + 1], args[p + 2], Value(args, "--clave"), Value(args, "--firmante"));
            if (Index(args, "--extension-verify") is { } v && v + 1 < args.Length) return Verify(args[v + 1]);
            if (Index(args, "--extension-revocations") is { } r && r + 3 < args.Length) return Revocations(args, r);
            if (Index(args, "--extension-catalog") is { } c && c + 3 < args.Length) return Catalog(args, c);
            if (Index(args, "--release-sign") is { } s && s + 3 < args.Length) return SignRelease(args[s + 1], args[s + 2], args[(s + 3)..]);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException
            or System.Security.Cryptography.CryptographicException or FormatException)
        {
            Console.WriteLine(ex.Message);
            return 1;
        }
        return null;
    }

    static int? Index(string[] args, string flag) => Array.IndexOf(args, flag) is var i and >= 0 ? i : null;

    static string? Value(string[] args, string flag) => Index(args, flag) is { } i && i + 1 < args.Length ? args[i + 1] : null;

    static string? Password => Environment.GetEnvironmentVariable("CLAUPET_KEY_PASSWORD");

    static int KeyGen(string path)
    {
        var keyId = ExtensionKeys.Generate(path, Password);
        Console.WriteLine(L.T("Clave privada: {0} (guárdela fuera de cualquier repositorio)", Path.GetFullPath(path)));
        Console.WriteLine(L.T("Clave pública: {0}", ExtensionKeys.PublicPath(path)));
        Console.WriteLine($"keyId: {keyId} ({ExtensionKeys.Short(keyId)})");
        if (string.IsNullOrEmpty(Password)) Console.WriteLine(L.T("Aviso: la clave privada quedó sin contraseña (CLAUPET_KEY_PASSWORD)."));
        return 0;
    }

    static ExtensionSigner? Signer(string? key, string? name)
    {
        if (key is null) return null;
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(L.T("Falta --firmante <nombre>."));
        return new ExtensionSigner(File.ReadAllText(key), Password, name);
    }

    static int Pack(string folder, string output, string? key, string? name)
    {
        using var signer = Signer(key, name);
        var (digest, errors) = PackageBuilder.Build(folder, output, signer);
        if (digest is null)
        {
            foreach (var e in errors) Console.WriteLine("  " + e);
            return 1;
        }
        Console.WriteLine(L.T("Paquete: {0}", Path.GetFullPath(output)));
        Console.WriteLine($"digest: {digest}");
        Console.WriteLine(signer is null ? L.T("Sin firma.") : L.T("Firmado por {0} (clave {1}).", signer.Name, ExtensionKeys.Short(signer.KeyId)));
        return 0;
    }

    static int Verify(string package)
    {
        var staging = Path.Combine(Path.GetTempPath(), "claupet-verificar");
        using var staged = StagedPackage.Open(package, staging);
        if (!staged.Ok)
        {
            Console.WriteLine(L.T("No sirve:"));
            foreach (var e in staged.Errors) Console.WriteLine("  " + e);
            return 1;
        }
        var m = staged.Manifest!;
        Console.WriteLine($"{m.Name.Text} {m.Version} ({m.Id})");
        Console.WriteLine($"  {L.T("Trae")}: {string.Join(", ", m.Provides.Select(ExtensionSlots.Label))}");
        if (m.Runtime is { } runtime) Console.WriteLine($"  runtime: {runtime.Type}{(runtime.Format is { } f ? " · " + f : "")}{(m.RunsCode ? " · " + L.T("corre código en el equipo") : "")}");
        foreach (var p in m.Permissions) Console.WriteLine($"  {L.T("Permiso")}: {ExtensionPermissions.Label(p)}");
        Console.WriteLine($"  digest: {staged.Digest}");
        var trust = new TrustStore(Path.Combine(staging, "confianza.json")).Evaluate(m.Id, m.Version, staged.Signature, staged.Digest, false);
        Console.WriteLine(trust switch
        {
            PackageTrust.Official => L.T("Firma oficial de Claupet."),
            PackageTrust.NewPublisher or PackageTrust.Trusted => L.T("Firmado por {0} (clave {1}).", staged.Signature!.Signer, ExtensionKeys.Short(staged.Signature.KeyId)),
            PackageTrust.Unsigned => L.T("Sin firma."),
            _ => L.T("La firma no corresponde a los archivos."),
        });
        return trust is PackageTrust.Invalid ? 1 : 0;
    }

    static int Revocations(string[] args, int at)
    {
        using var signer = new ExtensionSigner(File.ReadAllText(args[at + 1]), Password, "Claupet");
        var sequence = long.Parse(args[at + 2]);
        var (keys, versions) = Revoked(args);
        File.WriteAllText(args[at + 3], TrustStore.SignRevocations(signer, sequence, keys, versions));
        Console.WriteLine(L.T("Lista de revocaciones {0}: {1} claves, {2} extensiones.", sequence, keys.Count, versions.Count));
        return 0;
    }

    /// <summary>Las claves y versiones revocadas que se dieron (--clave-revocada, --version-revocada id=versión).</summary>
    static (List<string> Keys, Dictionary<string, string> Versions) Revoked(string[] args)
    {
        var keys = new List<string>();
        var versions = new Dictionary<string, string>();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--clave-revocada") keys.Add(args[i + 1]);
            if (args[i] == "--version-revocada" && args[i + 1].Split('=') is [var id, var version] && ExtensionManifest.ValidId(id) && SemVersion.Parse(version) is not null)
                versions[id] = version;
        }
        return (keys, versions);
    }

    /// <summary>El catálogo firmado (catalog.json del repositorio de extensiones) con los paquetes dados.</summary>
    static int Catalog(string[] args, int at)
    {
        using var signer = new ExtensionSigner(File.ReadAllText(args[at + 1]), Password, "Claupet");
        var sequence = long.Parse(args[at + 2]);
        var output = args[at + 3];
        var packages = new List<string>();
        for (var i = at + 4; i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal); i++) packages.Add(args[i]);
        var (keys, versions) = Revoked(args);
        var staging = Path.Combine(Path.GetTempPath(), "claupet-catalogo-" + Guid.NewGuid().ToString("N")[..8]);
        try { File.WriteAllText(output, ExtensionCatalog.Build(signer, sequence, packages, staging, keys, versions)); }
        finally { StagedPackage.TryDelete(staging); }
        Console.WriteLine(L.T("Catálogo {0}: {1} extensiones, firmado con la clave {2}.", sequence, packages.Count, ExtensionKeys.Short(signer.KeyId)));
        return 0;
    }

    /// <summary>Firma los archivos de una Release: deja «archivo.firma» junto a cada uno (ReleaseSignature).</summary>
    static int SignRelease(string key, string versionText, string[] files)
    {
        var version = SemVersion.Parse(versionText) ?? throw new ArgumentException(L.T("Versión inválida: «{0}».", versionText));
        using var signer = new ExtensionSigner(File.ReadAllText(key), Password, "Claupet");
        foreach (var file in files)
        {
            var signature = ReleaseSignature.Sign(signer, file, version);
            File.WriteAllText(file + ReleaseSignature.Suffix, signature.ToJson());
            Console.WriteLine($"{Path.GetFileName(file)}{ReleaseSignature.Suffix}: {signature.Sha256}");
        }
        Console.WriteLine(L.T("Firmado con la clave {0}.", ExtensionKeys.Short(signer.KeyId)));
        return 0;
    }
}
