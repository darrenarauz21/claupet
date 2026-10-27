using System.Text;
using System.Text.Json;

namespace Claupet;

/// <summary>Cómo quedó instalada una extensión (extensiones/registro/&lt;id&gt;.json): lo que se instaló y quién lo firmó.</summary>
sealed class InstallRecord
{
    public string Version { get; set; } = "";
    public string Digest { get; set; } = "";
    public bool Signed { get; set; }
    public string? KeyId { get; set; }
    public string? Signer { get; set; }
    public PackageTrust Trust { get; set; }
    public DateTimeOffset InstalledAt { get; set; }
}

/// <summary>Una extensión instalada: su manifiesto, su carpeta (solo lectura) y la de sus datos.</summary>
sealed record InstalledExtension(ExtensionManifest Manifest, string Folder, string DataFolder, InstallRecord Record)
{
    public string Id => Manifest.Id;

    /// <summary>La ruta de un archivo del paquete («bin/x.dll») en el disco.</summary>
    public string FileOf(string packagePath) => Path.Combine(Folder, packagePath.Replace('/', Path.DirectorySeparatorChar));
}

/// <summary>Lo que se muestra antes de instalar: qué es, quién lo firmó y qué cambia.</summary>
sealed record InstallReview(StagedPackage Package, ExtensionManifest Manifest, PackageTrust Trust, InstalledExtension? Existing)
{
    public bool IsUpdate => Existing is not null && Manifest.Version > Existing.Manifest.Version;
    public bool IsDowngrade => Existing is not null && Manifest.Version < Existing.Manifest.Version;
    public bool IsReinstall => Existing is not null && Manifest.Version == Existing.Manifest.Version;
    /// <summary>Se puede instalar (con su aviso): no es una firma inválida, revocada ni de otra clave.</summary>
    public bool Allowed => Trust is not (PackageTrust.Invalid or PackageTrust.Revoked or PackageTrust.KeyChanged);
    /// <summary>Permisos que la versión instalada no pedía: se vuelven a preguntar.</summary>
    public IReadOnlyList<string> NewPermissions => Existing is null ? Manifest.Permissions
        : Manifest.Permissions.Except(Existing.Manifest.Permissions).ToList();

    public string TrustText => Trust switch
    {
        PackageTrust.Official => L.T("Extensión oficial de Claupet."),
        PackageTrust.Trusted => L.T("Firmada por {0}, en quien ya confía.", Package.Signature!.Signer),
        PackageTrust.NewPublisher => L.T("Firmada por {0} (clave {1}), un autor nuevo: al instalarla, confiará en sus próximas versiones.",
            Package.Signature!.Signer, ExtensionKeys.Short(Package.Signature.KeyId)),
        PackageTrust.Unsigned => L.T("Sin firma: no se puede saber quién la hizo ni si alguien la cambió. Instálela solo si sabe de dónde viene."),
        PackageTrust.Invalid => L.T("La firma no corresponde a los archivos: el paquete fue modificado o está dañado. No se instala."),
        PackageTrust.Revoked => L.T("Esta extensión, o la clave de su autor, fue revocada. No se instala."),
        PackageTrust.KeyChanged => L.T("La versión instalada es de otra firma (o venía firmada y esta no). Para cambiar de autor, desinstale primero la que tiene."),
        _ => "",
    };
}

/// <summary>
/// Las extensiones en el disco, bajo %APPDATA%\Claupet\extensiones:
/// paquetes\&lt;id&gt; (lo instalado, tal cual se firmó), datos\&lt;id&gt; (lo que la extensión guarda),
/// registro\&lt;id&gt;.json (InstallRecord), preparacion\ (paquetes a medio abrir) y confianza.json
/// (TrustStore). Instalar y actualizar es atómico: lo nuevo se extrae aparte y reemplaza a lo anterior de
/// una vez; si algo falla, queda lo que había.
/// </summary>
sealed class ExtensionStore
{
    public static string DefaultRoot => DataFolder.In("extensiones");

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Root { get; }
    public TrustStore Trust { get; }
    string Packages => Path.Combine(Root, "paquetes");
    string Data => Path.Combine(Root, "datos");
    string Records => Path.Combine(Root, "registro");
    string Staging => Path.Combine(Root, "preparacion");

    public ExtensionStore(string? root = null, TrustStore? trust = null)
    {
        Root = root ?? DefaultRoot;
        Trust = trust ?? new TrustStore(Path.Combine(Root, "confianza.json"));
    }

    /// <summary>Lo que quedó de una instalación cortada (preparacion\) se borra al abrir la app.</summary>
    public void CleanStaging()
    {
        if (!Directory.Exists(Staging)) return;
        foreach (var dir in Directory.EnumerateDirectories(Staging)) StagedPackage.TryDelete(dir);
    }

    /// <summary>Las instaladas que se pueden leer; las que no (manifiesto dañado o borrado), en broken con el motivo.</summary>
    public List<InstalledExtension> List(List<(string Id, string Problem)>? broken = null)
    {
        var list = new List<InstalledExtension>();
        if (!Directory.Exists(Records)) return list;
        foreach (var file in Directory.EnumerateFiles(Records, "*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            if (!ExtensionManifest.ValidId(id)) continue;
            try
            {
                var record = JsonSerializer.Deserialize<InstallRecord>(File.ReadAllText(file), Options) ?? throw new InvalidDataException();
                var folder = Path.Combine(Packages, id);
                var manifestPath = Path.Combine(folder, PackagePaths.Manifest);
                if (!File.Exists(manifestPath)) { broken?.Add((id, L.T("Faltan sus archivos."))); continue; }
                var (manifest, errors) = ExtensionManifest.Parse(File.ReadAllText(manifestPath, Encoding.UTF8));
                if (manifest is null || manifest.Id != id) { broken?.Add((id, errors.FirstOrDefault() ?? L.T("Su manifiesto no corresponde."))); continue; }
                list.Add(new InstalledExtension(manifest, folder, Path.Combine(Data, id), record));
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                broken?.Add((id, L.T("Su registro está dañado.")));
            }
        }
        return list.OrderBy(e => e.Manifest.Name.Text, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public InstalledExtension? Find(string id) => List().FirstOrDefault(e => e.Id == id);

    /// <summary>
    /// Abre el paquete y dice qué es y si se puede instalar. Lanza InvalidDataException si el paquete no
    /// sirve (la ruta de un archivo, el manifiesto…), con el motivo. El llamador desecha la revisión
    /// (Dispose del paquete) si no se instala.
    /// </summary>
    public InstallReview Prepare(string packagePath, CancellationToken cancel = default)
    {
        var staged = StagedPackage.Open(packagePath, Staging, cancel);
        if (!staged.Ok)
        {
            var problems = string.Join("\n", staged.Errors);
            staged.Dispose();
            throw new InvalidDataException(problems.Length > 0 ? problems : L.T("El paquete no sirve."));
        }
        var manifest = staged.Manifest!;
        var existing = Find(manifest.Id);
        var trust = Trust.Evaluate(manifest.Id, manifest.Version, staged.Signature, staged.Digest, existing?.Record.Signed == true);
        return new InstallReview(staged, manifest, trust, existing);
    }

    /// <summary>
    /// Instala lo revisado (la persona ya aceptó). Antes, quien la tenga corriendo debe detenerla: una DLL
    /// o un programa abiertos no se pueden reemplazar en Windows.
    /// </summary>
    public InstalledExtension Commit(InstallReview review)
    {
        if (!review.Allowed) throw new InvalidOperationException(review.TrustText);
        var manifest = review.Manifest;
        var target = Path.Combine(Packages, manifest.Id);
        var old = Path.Combine(Staging, Guid.NewGuid().ToString("N") + "-anterior");
        Directory.CreateDirectory(Packages);
        var moved = false;
        try
        {
            if (Directory.Exists(target)) { Directory.Move(target, old); moved = true; }
            Directory.Move(review.Package.Folder, target);
        }
        catch
        {
            if (moved && !Directory.Exists(target)) Directory.Move(old, target);
            throw;
        }
        var record = new InstallRecord
        {
            Version = manifest.Version.ToString(), Digest = review.Package.Digest, Signed = review.Package.Signature is not null,
            KeyId = review.Package.Signature?.KeyId, Signer = review.Package.Signature?.Signer, Trust = review.Trust,
            InstalledAt = DateTimeOffset.UtcNow,
        };
        WriteRecord(manifest.Id, record);
        Trust.Accept(manifest.Id, review.Package.Signature);
        StagedPackage.TryDelete(old);
        var data = Path.Combine(Data, manifest.Id);
        Directory.CreateDirectory(data);
        return new InstalledExtension(manifest, target, data, record);
    }

    void WriteRecord(string id, InstallRecord record)
    {
        Directory.CreateDirectory(Records);
        var path = Path.Combine(Records, id + ".json");
        File.WriteAllText(path + ".parte", JsonSerializer.Serialize(record, Options));
        File.Move(path + ".parte", path, overwrite: true);
    }

    /// <summary>La quita del disco (y sus datos, si keepData es false). Quien la tenga corriendo debe detenerla antes.</summary>
    public void Uninstall(string id, bool keepData)
    {
        if (!ExtensionManifest.ValidId(id)) throw new ArgumentException(id);
        var folder = Path.Combine(Packages, id);
        if (Directory.Exists(folder))
        {
            // Primero se aparta (si un archivo está en uso, falla aquí y no queda a medias).
            var trash = Path.Combine(Staging, Guid.NewGuid().ToString("N") + "-quitada");
            Directory.CreateDirectory(Staging);
            Directory.Move(folder, trash);
            StagedPackage.TryDelete(trash);
        }
        File.Delete(Path.Combine(Records, id + ".json"));
        if (!keepData) StagedPackage.TryDelete(Path.Combine(Data, id));
        Trust.Forget(id);
    }

    /// <summary>
    /// Los archivos instalados siguen siendo los que se firmaron (nadie los cambió en el disco). Se comprueba
    /// antes de arrancar su código. Devuelve el problema, o null si está bien.
    /// </summary>
    public string? Verify(InstalledExtension extension, CancellationToken cancel = default)
    {
        try
        {
            var files = PackagePaths.Scan(extension.Folder, cancel);
            var digest = PackagePaths.Digest(files);
            if (digest != extension.Record.Digest) return L.T("Sus archivos cambiaron desde que se instaló.");
            if (extension.Record.Signed)
            {
                var signature = PackageSignature.Parse(File.ReadAllText(extension.FileOf(PackagePaths.Signature), Encoding.UTF8), out var problem);
                if (signature is null) return problem;
                if (!signature.Verify(digest) || signature.KeyId != extension.Record.KeyId) return L.T("Su firma ya no corresponde.");
                if (Trust.IsRevoked(signature.KeyId)) return L.T("La clave de su autor fue revocada.");
            }
            if (Trust.IsRevoked(extension.Id, extension.Manifest.Version)) return L.T("Esta versión fue revocada.");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return L.T("No se pudieron leer sus archivos: {0}", ex.Message);
        }
    }
}
