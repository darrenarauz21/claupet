using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Claupet;

/// <summary>Un archivo del paquete: su ruta (con «/»), su tamaño y su SHA-256 en hexadecimal (minúsculas).</summary>
sealed record PackageFile(string Path, long Size, string Sha256);

/// <summary>
/// Las rutas dentro de un paquete: relativas, con «/», sin «..», sin unidades ni nombres reservados de
/// Windows, sin caracteres que Windows no acepte y sin espacios o puntos al final de una parte. Lo que
/// no cumple se rechaza (no se «arregla»), para que lo firmado sea exactamente lo que se instala.
/// </summary>
static class PackagePaths
{
    /// <summary>La carpeta del paquete reservada para Claupet: la firma.</summary>
    public const string Reserved = "_claupet/";
    public const string Signature = "_claupet/signature.json";
    public const string Manifest = "manifest.json";

    static readonly HashSet<string> WindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "CONIN$", "CONOUT$",
    };

    /// <summary>La ruta tal cual si es válida; null si no.</summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 240 || path[0] == '/' || path.EndsWith('/')) return null;
        var parts = path.Split('/');
        if (parts.Length > 12) return null;
        foreach (var part in parts)
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.StartsWith(' ')) return null;
            if (part.Any(c => c < 0x20 || c is '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' || c == 0x7F)) return null;
            if (WindowsNames.Contains(part.Split('.')[0])) return null;
        }
        return path;
    }

    /// <summary>
    /// Lo que se firma: una línea por archivo («sha256 tamaño ruta»), ordenadas por ruta, sin la firma.
    /// Cubre el manifiesto, porque es un archivo más.
    /// </summary>
    public static string Listing(IEnumerable<PackageFile> files)
    {
        var text = new StringBuilder("claupet-package-v1\n");
        foreach (var f in files.Where(f => f.Path != Signature).OrderBy(f => f.Path, StringComparer.Ordinal))
            text.Append(f.Sha256).Append(' ').Append(f.Size).Append(' ').Append(f.Path).Append('\n');
        return text.ToString();
    }

    public static string Digest(IEnumerable<PackageFile> files) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Listing(files)))).ToLowerInvariant();

    /// <summary>Lee la lista y los hashes de una carpeta ya instalada (para ver si alguien la tocó).</summary>
    public static List<PackageFile> Scan(string folder, CancellationToken cancel = default)
    {
        var list = new List<PackageFile>();
        var root = Path.GetFullPath(folder).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        foreach (var file in Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
        {
            cancel.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            if (info.LinkTarget is not null) throw new IOException(L.T("La extensión tiene un enlace simbólico: {0}", file));
            var relative = Path.GetFullPath(file)[root.Length..].Replace(System.IO.Path.DirectorySeparatorChar, '/');
            using var stream = File.OpenRead(file);
            list.Add(new PackageFile(relative, info.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()));
        }
        return list;
    }
}

/// <summary>
/// Un paquete .claupet (un zip) abierto en una carpeta de preparación: cada archivo se valida y se
/// extrae calculando su SHA-256 en la misma pasada. Límites contra paquetes hostiles: cantidad de
/// archivos, tamaño de cada uno y del total, y la proporción de compresión (las «bombas zip»). Los
/// enlaces simbólicos y las rutas repetidas (también solo en mayúsculas) se rechazan.
/// </summary>
sealed class StagedPackage : IDisposable
{
    public const int MaxEntries = 5000;
    public const long MaxFile = 2L << 30, MaxTotal = 4L << 30;
    /// <summary>Un archivo de más de 1 MB que se descomprime más de 200 veces es sospechoso.</summary>
    const int MaxRatio = 200;

    public string Folder { get; }
    public IReadOnlyList<PackageFile> Files { get; }
    public ExtensionManifest? Manifest { get; }
    public IReadOnlyList<string> Errors { get; }
    public PackageSignature? Signature { get; }
    public string Digest { get; }

    StagedPackage(string folder, List<PackageFile> files, ExtensionManifest? manifest, IReadOnlyList<string> errors, PackageSignature? signature)
    {
        Folder = folder;
        Files = files;
        Manifest = manifest;
        Errors = errors;
        Signature = signature;
        Digest = PackagePaths.Digest(files);
    }

    public bool Ok => Manifest is not null && Errors.Count == 0;

    /// <summary>Lo extrae en una carpeta nueva dentro de staging; si algo no sirve, la borra y lanza InvalidDataException.</summary>
    public static StagedPackage Open(string zipPath, string staging, CancellationToken cancel = default)
    {
        Directory.CreateDirectory(staging);
        var folder = Path.Combine(staging, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var files = Extract(zipPath, folder, cancel);
            var set = files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
            if (!set.Contains(PackagePaths.Manifest)) throw new InvalidDataException(L.T("El paquete no tiene manifest.json en la raíz."));
            var (manifest, errors) = ExtensionManifest.Parse(File.ReadAllText(Path.Combine(folder, PackagePaths.Manifest), Encoding.UTF8), set);
            PackageSignature? signature = null;
            var allErrors = errors.ToList();
            if (set.Contains(PackagePaths.Signature))
            {
                signature = PackageSignature.Parse(File.ReadAllText(Path.Combine(folder, PackagePaths.Signature.Replace('/', Path.DirectorySeparatorChar)), Encoding.UTF8), out var problem);
                if (signature is null) allErrors.Add(problem!);
            }
            if (manifest is not null && manifest.MinClaupet is { } min && SemVersion.App < min)
                allErrors.Add(L.T("Necesita Claupet {0} o más nuevo (esta es la {1}).", min, SemVersion.App));
            return new StagedPackage(folder, files, manifest, allErrors, signature);
        }
        catch
        {
            TryDelete(folder);
            throw;
        }
    }

    static List<PackageFile> Extract(string zipPath, string folder, CancellationToken cancel)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.Entries.Count > MaxEntries) throw new InvalidDataException(L.T("El paquete tiene demasiados archivos ({0}).", zip.Entries.Count));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<PackageFile>();
        long total = 0;
        var buffer = new byte[1 << 16];
        foreach (var entry in zip.Entries)
        {
            cancel.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/') && entry.Length == 0) continue; // una carpeta
            var path = PackagePaths.Normalize(entry.FullName)
                ?? throw new InvalidDataException(L.T("Ruta no permitida en el paquete: «{0}».", entry.FullName));
            if (path.StartsWith(PackagePaths.Reserved, StringComparison.OrdinalIgnoreCase) && path != PackagePaths.Signature)
                throw new InvalidDataException(L.T("«{0}» está reservado para la firma.", PackagePaths.Reserved));
            if (!seen.Add(path)) throw new InvalidDataException(L.T("Archivo repetido en el paquete: «{0}».", path));
            // Los permisos de Unix van en los 16 bits altos: 0xA000 es un enlace simbólico.
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException(L.T("El paquete trae un enlace simbólico: «{0}».", path));
            if (entry.Length > MaxFile) throw new InvalidDataException(L.T("«{0}» es demasiado grande.", path));
            if (entry.Length > 1 << 20 && entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > MaxRatio)
                throw new InvalidDataException(L.T("«{0}» se descomprime demasiado: parece un paquete dañino.", path));

            var target = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            long written = 0;
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (var source = entry.Open())
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
            {
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    written += read;
                    total += read;
                    // El tamaño declarado puede mentir: se cuenta lo que de verdad sale.
                    if (written > entry.Length || written > MaxFile) throw new InvalidDataException(L.T("«{0}» no coincide con su tamaño declarado.", path));
                    if (total > MaxTotal) throw new InvalidDataException(L.T("El paquete descomprimido pasa de {0} GB.", MaxTotal >> 30));
                    sha.AppendData(buffer, 0, read);
                    output.Write(buffer, 0, read);
                }
            }
            if (written != entry.Length) throw new InvalidDataException(L.T("«{0}» no coincide con su tamaño declarado.", path));
            files.Add(new PackageFile(path, written, Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant()));
        }
        return files;
    }

    public void Dispose() => TryDelete(Folder);

    internal static void TryDelete(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch (Exception) { }
    }
}

/// <summary>
/// Arma un .claupet desde una carpeta (para los autores: --extension-pack). Valida el manifiesto con
/// los archivos que de verdad van, con las mismas reglas que al instalar, y firma si se da la clave.
/// </summary>
static class PackageBuilder
{
    public static (string? Digest, IReadOnlyList<string> Errors) Build(string folder, string output, ExtensionSigner? signer)
    {
        var files = PackagePaths.Scan(folder).Where(f => f.Path != PackagePaths.Signature).ToList();
        var bad = files.Where(f => PackagePaths.Normalize(f.Path) is null).Select(f => L.T("Ruta no permitida en el paquete: «{0}».", f.Path)).ToList();
        if (bad.Count > 0) return (null, bad);
        var set = files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        if (!set.Contains(PackagePaths.Manifest)) return (null, [L.T("El paquete no tiene manifest.json en la raíz.")]);
        var (manifest, errors) = ExtensionManifest.Parse(File.ReadAllText(Path.Combine(folder, PackagePaths.Manifest), Encoding.UTF8), set);
        if (manifest is null) return (null, errors);

        var digest = PackagePaths.Digest(files);
        var temp = output + ".parte";
        if (File.Exists(temp)) File.Delete(temp);
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (var f in files.OrderBy(f => f.Path, StringComparer.Ordinal))
                zip.CreateEntryFromFile(Path.Combine(folder, f.Path.Replace('/', Path.DirectorySeparatorChar)), f.Path, CompressionLevel.Optimal);
            if (signer is not null)
            {
                var entry = zip.CreateEntry(PackagePaths.Signature, CompressionLevel.Optimal);
                using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                w.Write(signer.Sign(digest).ToJson());
            }
        }
        File.Move(temp, output, overwrite: true);
        return (digest, []);
    }
}
