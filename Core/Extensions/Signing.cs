using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet;

/// <summary>
/// La firma de un paquete (_claupet/signature.json): ECDSA P-256 con SHA-256 (ES256), que .NET trae en
/// Windows, macOS y Linux. Se firma el resumen de la lista de archivos (PackagePaths.Digest) junto con
/// quién firma y cuándo, así que cambiar un byte de cualquier archivo, agregar uno o quitarlo la rompe.
/// KeyId: el SHA-256 de la clave pública (SubjectPublicKeyInfo), en hexadecimal.
/// </summary>
sealed record PackageSignature(string PublicKey, string Signer, DateTimeOffset SignedAt, string Digest, string Signature)
{
    public const int Format = 1;
    public const string Algorithm = "ES256";

    public string KeyId => ExtensionKeys.KeyId(Convert.FromBase64String(PublicKey));

    /// <summary>Lo que de verdad se firma: el resumen con el firmante y la fecha.</summary>
    public static byte[] Message(string digest, string signer, DateTimeOffset signedAt) =>
        Encoding.UTF8.GetBytes($"claupet-signature-v1\n{digest}\n{signer}\n{signedAt.ToUniversalTime():yyyy-MM-dd'T'HH:mm:ss'Z'}\n");

    public static PackageSignature? Parse(string json, out string? problem)
    {
        problem = null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var r = doc.RootElement;
            if (!r.TryGetProperty("format", out var f) || !f.TryGetInt32(out var format) || format != Format || Json.Str(r, "algorithm") != Algorithm)
            {
                problem = L.T("La firma tiene un formato que esta app no conoce.");
                return null;
            }
            var key = Json.Str(r, "publicKey");
            var signer = Json.Str(r, "signer")?.Trim() ?? "";
            var digest = Json.Str(r, "digest") ?? "";
            var signature = Json.Str(r, "signature");
            var when = Json.Date(r, "signedAt");
            if (key is null || signature is null || when is null || digest.Length != 64 || signer.Length is 0 or > 80 || signer.Any(char.IsControl))
            {
                problem = L.T("La firma está incompleta.");
                return null;
            }
            Convert.FromBase64String(key);
            Convert.FromBase64String(signature);
            return new PackageSignature(key, signer, when.Value, digest.ToLowerInvariant(), signature);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            problem = L.T("La firma está dañada.");
            return null;
        }
    }

    public string ToJson() => new JsonObject
    {
        ["format"] = Format,
        ["algorithm"] = Algorithm,
        ["signer"] = Signer,
        ["signedAt"] = SignedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
        ["digest"] = Digest,
        ["publicKey"] = PublicKey,
        ["signature"] = Signature,
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <summary>La firma corresponde a esos archivos (digest) y a su clave, y la clave es P-256.</summary>
    public bool Verify(string digest)
    {
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(digest), Encoding.ASCII.GetBytes(Digest))) return false;
        try
        {
            using var key = ExtensionKeys.ImportPublic(Convert.FromBase64String(PublicKey));
            return key.VerifyData(Message(Digest, Signer, SignedAt), Convert.FromBase64String(Signature), HashAlgorithmName.SHA256);
        }
        catch (CryptographicException) { return false; }
    }
}

/// <summary>Las claves de firma: crearlas, leerlas y su KeyId.</summary>
static class ExtensionKeys
{
    public static string KeyId(byte[] subjectPublicKeyInfo) => Convert.ToHexString(SHA256.HashData(subjectPublicKeyInfo)).ToLowerInvariant();

    /// <summary>El KeyId en corto, para mostrarlo: 4 grupos de 4.</summary>
    public static string Short(string keyId) => string.Join(" ", Enumerable.Range(0, 4).Select(i => keyId.Substring(i * 4, 4).ToUpperInvariant()));

    /// <summary>Una clave pública P-256; otra curva u otro tipo se rechaza.</summary>
    public static ECDsa ImportPublic(byte[] spki)
    {
        var key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(spki, out _);
            if (key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
                throw new CryptographicException("not P-256");
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Una clave nueva en dos archivos PEM: la privada (PKCS#8, cifrada si se da contraseña) y la pública
    /// (.pub.pem). Devuelve el KeyId. La privada no se sobrescribe si ya existe.
    /// </summary>
    public static string Generate(string privatePath, string? password)
    {
        if (File.Exists(privatePath)) throw new IOException(L.T("Ya existe {0}: no se sobrescribe una clave.", privatePath));
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = string.IsNullOrEmpty(password)
            ? key.ExportPkcs8PrivateKeyPem()
            : key.ExportEncryptedPkcs8PrivateKeyPem(password, new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(privatePath))!);
        using (var stream = new FileStream(privatePath, FileMode.CreateNew, FileAccess.Write))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            writer.Write(pem);
        File.WriteAllText(PublicPath(privatePath), key.ExportSubjectPublicKeyInfoPem());
        return KeyId(key.ExportSubjectPublicKeyInfo());
    }

    public static string PublicPath(string privatePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(privatePath))!, Path.GetFileNameWithoutExtension(privatePath) + ".pub.pem");
}

/// <summary>Quien firma: la clave privada (PEM) y el nombre que aparece al instalar.</summary>
sealed class ExtensionSigner : IDisposable
{
    readonly ECDsa _key;
    public string Name { get; }

    public ExtensionSigner(string privatePem, string? password, string name)
    {
        if (name.Trim().Length is 0 or > 80 || name.Any(char.IsControl)) throw new ArgumentException(L.T("El nombre de quien firma va de 1 a 80 caracteres."));
        Name = name.Trim();
        _key = ECDsa.Create();
        if (string.IsNullOrEmpty(password)) _key.ImportFromPem(privatePem);
        else _key.ImportFromEncryptedPem(privatePem, password);
        if (_key.KeySize != 256) throw new CryptographicException(L.T("La clave debe ser ECDSA P-256."));
    }

    public string KeyId => ExtensionKeys.KeyId(_key.ExportSubjectPublicKeyInfo());

    public PackageSignature Sign(string digest) => Sign(digest, DateTimeOffset.UtcNow);

    public PackageSignature Sign(string digest, DateTimeOffset when)
    {
        // Al segundo: así se escribe en el JSON, y lo firmado debe ser lo mismo que se lee.
        when = DateTimeOffset.FromUnixTimeSeconds(when.ToUnixTimeSeconds());
        var signature = _key.SignData(PackageSignature.Message(digest, Name, when), HashAlgorithmName.SHA256);
        return new PackageSignature(Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo()), Name, when, digest, Convert.ToBase64String(signature));
    }

    /// <summary>Firma un texto cualquiera (la lista de revocaciones): la firma en base64.</summary>
    public string SignText(string text) => Convert.ToBase64String(_key.SignData(Encoding.UTF8.GetBytes(text), HashAlgorithmName.SHA256));

    public string PublicKey => Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());

    public void Dispose() => _key.Dispose();
}

/// <summary>Qué tan confiable es un paquete, de mejor a peor. Los tres últimos no se instalan.</summary>
enum PackageTrust { Official, Trusted, NewPublisher, Unsigned, Invalid, Revoked, KeyChanged }

/// <summary>
/// En quién se confía (extensiones/confianza.json):
/// - las claves oficiales de Claupet, dentro de la app (OfficialKeys);
/// - las de los autores que la persona aceptó al instalar;
/// - la clave con que se instaló cada extensión, fijada: una actualización firmada con otra se
///   rechaza, y una sin firma de algo que se instaló firmado, también;
/// - las claves y versiones revocadas (por la persona o por la lista firmada de Claupet).
/// </summary>
sealed class TrustStore
{
    /// <summary>
    /// Las claves públicas oficiales (SubjectPublicKeyInfo en base64). Firman las extensiones de Claupet y la
    /// lista de revocaciones; se agregan con la clave que genera el responsable del repositorio
    /// (--extension-keygen) y nunca se guarda la privada en el repositorio.
    /// </summary>
    public static readonly string[] OfficialKeys = [];

    sealed class Data
    {
        public Dictionary<string, Publisher> Publishers { get; set; } = new();
        public Dictionary<string, string> Pins { get; set; } = new();
        public HashSet<string> RevokedKeys { get; set; } = new();
        /// <summary>Extensión → la versión más alta revocada (esa y las anteriores no se cargan).</summary>
        public Dictionary<string, string> RevokedVersions { get; set; } = new();
        public long RevocationSequence { get; set; }
        public DateTimeOffset? RevocationChecked { get; set; }
    }

    public sealed class Publisher
    {
        public string Name { get; set; } = "";
        public string PublicKey { get; set; } = "";
        public DateTimeOffset TrustedAt { get; set; }
    }

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    readonly string _path;
    /// <summary>Las oficiales en base64 (para verificar con ellas) y sus KeyId.</summary>
    readonly IReadOnlyList<string> _officialKeys, _official;
    readonly object _gate = new();
    Data _data;

    /// <summary>officialKeys: otras claves oficiales (las pruebas); null, las de la app.</summary>
    public TrustStore(string path, IReadOnlyList<string>? officialKeys = null)
    {
        _path = path;
        _officialKeys = officialKeys ?? OfficialKeys;
        _official = _officialKeys.Select(k => ExtensionKeys.KeyId(Convert.FromBase64String(k))).ToList();
        _data = Load(path);
    }

    static Data Load(string path)
    {
        try { if (File.Exists(path)) return JsonSerializer.Deserialize<Data>(File.ReadAllText(path), Options) ?? new(); }
        catch (Exception ex) when (ex is JsonException or IOException) { ErrorLog.Write(ex); }
        return new();
    }

    void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".parte";
        File.WriteAllText(temp, JsonSerializer.Serialize(_data, Options));
        File.Move(temp, _path, overwrite: true);
    }

    public bool IsOfficial(string keyId) => _official.Contains(keyId);

    public IReadOnlyList<string> OfficialKeyIds => _official;

    public string? PinnedKey(string extensionId)
    {
        lock (_gate) return _data.Pins.TryGetValue(extensionId, out var k) ? k : null;
    }

    /// <summary>¿Se puede instalar? installedSigned: la versión ya instalada vino firmada.</summary>
    public PackageTrust Evaluate(string extensionId, SemVersion version, PackageSignature? signature, string digest, bool installedSigned)
    {
        lock (_gate)
        {
            if (_data.RevokedVersions.TryGetValue(extensionId, out var upTo) && SemVersion.Parse(upTo) is { } max && version <= max)
                return PackageTrust.Revoked;
            if (signature is null)
                return installedSigned || _data.Pins.ContainsKey(extensionId) ? PackageTrust.KeyChanged : PackageTrust.Unsigned;
            if (!signature.Verify(digest)) return PackageTrust.Invalid;
            var keyId = signature.KeyId;
            if (_data.RevokedKeys.Contains(keyId)) return PackageTrust.Revoked;
            if (_data.Pins.TryGetValue(extensionId, out var pinned) && pinned != keyId) return PackageTrust.KeyChanged;
            if (_official.Contains(keyId)) return PackageTrust.Official;
            return _data.Publishers.ContainsKey(keyId) ? PackageTrust.Trusted : PackageTrust.NewPublisher;
        }
    }

    /// <summary>Al instalar: se acepta al autor (si es nuevo) y la extensión queda fijada a su clave.</summary>
    public void Accept(string extensionId, PackageSignature? signature)
    {
        lock (_gate)
        {
            if (signature is null) return;
            var keyId = signature.KeyId;
            if (!_official.Contains(keyId) && !_data.Publishers.ContainsKey(keyId))
                _data.Publishers[keyId] = new Publisher { Name = signature.Signer, PublicKey = signature.PublicKey, TrustedAt = DateTimeOffset.UtcNow };
            _data.Pins[extensionId] = keyId;
            Save();
        }
    }

    /// <summary>Al desinstalar: la extensión deja de estar fijada (otra con el mismo id podrá instalarse con su aviso).</summary>
    public void Forget(string extensionId)
    {
        lock (_gate)
        {
            if (_data.Pins.Remove(extensionId)) Save();
        }
    }

    public IReadOnlyDictionary<string, Publisher> Publishers
    {
        get { lock (_gate) return new Dictionary<string, Publisher>(_data.Publishers); }
    }

    public bool IsRevoked(string keyId) { lock (_gate) return _data.RevokedKeys.Contains(keyId); }

    public bool IsRevoked(string extensionId, SemVersion version)
    {
        lock (_gate) return _data.RevokedVersions.TryGetValue(extensionId, out var upTo) && SemVersion.Parse(upTo) is { } max && version <= max;
    }

    /// <summary>La persona deja de confiar en un autor: su clave queda revocada aquí.</summary>
    public void Revoke(string keyId)
    {
        lock (_gate)
        {
            _data.Publishers.Remove(keyId);
            _data.RevokedKeys.Add(keyId);
            Save();
        }
    }

    public DateTimeOffset? RevocationChecked { get { lock (_gate) return _data.RevocationChecked; } }

    /// <summary>
    /// Aplica una lista de revocaciones: {"payload": "&lt;JSON en base64&gt;", "keyId": "…", "signature": "…"},
    /// firmada con una clave oficial. El payload: {"sequence": n, "keys": ["keyId"…], "extensions":
    /// {"autor.nombre": "1.2.0"}}. Solo se acepta una secuencia mayor a la última (no se puede volver a
    /// una lista vieja). Devuelve false si la lista no sirve.
    /// </summary>
    public bool ApplyRevocations(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var r = doc.RootElement;
            var payloadB64 = Json.Str(r, "payload");
            var keyId = Json.Str(r, "keyId");
            var signature = Json.Str(r, "signature");
            if (payloadB64 is null || keyId is null || signature is null || !_official.Contains(keyId)) return false;
            var publicKey = OfficialKeysFor(keyId);
            if (publicKey is null) return false;
            var payload = Encoding.UTF8.GetString(Convert.FromBase64String(payloadB64));
            using (var key = ExtensionKeys.ImportPublic(Convert.FromBase64String(publicKey)))
                if (!key.VerifyData(Encoding.UTF8.GetBytes(payload), Convert.FromBase64String(signature), HashAlgorithmName.SHA256)) return false;

            using var list = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 4 });
            var p = list.RootElement;
            if (!p.TryGetProperty("sequence", out var s) || !s.TryGetInt64(out var sequence)) return false;
            lock (_gate)
            {
                _data.RevocationChecked = DateTimeOffset.UtcNow;
                if (sequence <= _data.RevocationSequence) { Save(); return sequence == _data.RevocationSequence; }
                if (p.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Array)
                    foreach (var k in keys.EnumerateArray())
                        if (k.GetString() is { Length: 64 } revoked && !_official.Contains(revoked))
                        {
                            _data.RevokedKeys.Add(revoked);
                            _data.Publishers.Remove(revoked);
                        }
                if (p.TryGetProperty("extensions", out var exts) && exts.ValueKind == JsonValueKind.Object)
                    foreach (var e in exts.EnumerateObject())
                        if (ExtensionManifest.ValidId(e.Name) && SemVersion.Parse(e.Value.GetString()) is { } upTo
                            && !(_data.RevokedVersions.TryGetValue(e.Name, out var had) && SemVersion.Parse(had) is { } before && before >= upTo))
                            _data.RevokedVersions[e.Name] = upTo.ToString();
                _data.RevocationSequence = sequence;
                Save();
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException or InvalidOperationException) { return false; }
    }

    string? OfficialKeysFor(string keyId) =>
        _officialKeys.FirstOrDefault(k => ExtensionKeys.KeyId(Convert.FromBase64String(k)) == keyId);

    /// <summary>Arma una lista de revocaciones firmada (--extension-revocations, para el responsable del repositorio).</summary>
    public static string SignRevocations(ExtensionSigner signer, long sequence, IEnumerable<string> keys, IReadOnlyDictionary<string, string> extensions)
    {
        var payload = new JsonObject
        {
            ["sequence"] = sequence,
            ["keys"] = new JsonArray(keys.Select(k => (JsonNode)k).ToArray()),
            ["extensions"] = new JsonObject(extensions.Select(e => KeyValuePair.Create(e.Key, (JsonNode?)e.Value))),
        }.ToJsonString();
        return new JsonObject
        {
            ["payload"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)),
            ["keyId"] = signer.KeyId,
            ["signature"] = signer.SignText(payload),
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
