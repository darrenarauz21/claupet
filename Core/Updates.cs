using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet;

/// <summary>Un archivo adjunto de una Release de GitHub.</summary>
sealed record ReleaseAsset(string Name, string Url, long Size);

/// <summary>Una Release de GitHub: su versión (de la etiqueta vX.Y.Z), su página y sus archivos.</summary>
sealed record ReleaseInfo(SemVersion Version, string Page, IReadOnlyList<ReleaseAsset> Assets)
{
    public ReleaseAsset? Asset(string name) => Assets.FirstOrDefault(a => a.Name == name);

    /// <summary>Lee la respuesta de api.github.com/repos/…/releases/latest; null si no es una Release con versión.</summary>
    public static ReleaseInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            if (r.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
            if (r.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;
            var tag = Json.Str(r, "tag_name")?.Trim() ?? "";
            if (SemVersion.Parse(tag.StartsWith('v') || tag.StartsWith('V') ? tag[1..] : tag) is not { } version) return null;
            var page = Json.Str(r, "html_url") is { } html && Uri.TryCreate(html, UriKind.Absolute, out var uri) && uri.Scheme == "https"
                ? uri.AbsoluteUri : Updates.ReleasesPage;
            var assets = new List<ReleaseAsset>();
            if (r.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var a in list.EnumerateArray())
                    // Solo por https (o desde este mismo equipo, para las pruebas); igual se comprueba la firma.
                    if (Json.Str(a, "name") is { Length: > 0 } name && Json.Str(a, "browser_download_url") is { } url
                        && Uri.TryCreate(url, UriKind.Absolute, out var download) && (download.Scheme == "https" || download.IsLoopback))
                        assets.Add(new ReleaseAsset(name, download.AbsoluteUri, a.TryGetProperty("size", out var s) && s.TryGetInt64(out var size) ? size : 0));
            return new ReleaseInfo(version, page, assets);
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>
/// La firma de un archivo de una Release (va junto a él, con «.firma» al final): ES256 como la de los paquetes de
/// extensiones (ExtensionSigner, ExtensionKeys), sobre su nombre, la versión, su tamaño y su SHA-256. No sirve para otro
/// archivo ni para otra versión: una firma vieja no puede hacer pasar una versión vieja por nueva. La crea
/// «Claupet --release-sign» (empaquetar.py --clave); la app solo tiene la clave pública (Updates.ReleaseKey).
/// </summary>
sealed record ReleaseSignature(string File, SemVersion Version, long Size, string Sha256, string KeyId, string Signature)
{
    public const int Format = 1;
    public const string Suffix = ".firma";

    /// <summary>Lo que de verdad se firma.</summary>
    public static string Message(string file, SemVersion version, long size, string sha256) =>
        $"claupet-release-v1\n{file}\n{version}\n{size}\n{sha256.ToLowerInvariant()}\n";

    /// <summary>Firma ese archivo como parte de esa versión.</summary>
    public static ReleaseSignature Sign(ExtensionSigner signer, string path, SemVersion version)
    {
        var name = Path.GetFileName(path);
        long size;
        string sha256;
        using (var stream = System.IO.File.OpenRead(path))
        {
            size = stream.Length;
            sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        return new ReleaseSignature(name, version, size, sha256, signer.KeyId, signer.SignText(Message(name, version, size, sha256)));
    }

    public string ToJson() => new JsonObject
    {
        ["format"] = Format,
        ["algorithm"] = PackageSignature.Algorithm,
        ["file"] = File,
        ["version"] = Version.ToString(),
        ["size"] = Size,
        ["sha256"] = Sha256,
        ["keyId"] = KeyId,
        ["signature"] = Signature,
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    public static ReleaseSignature? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var r = doc.RootElement;
            if (!r.TryGetProperty("format", out var f) || !f.TryGetInt32(out var format) || format != Format || Json.Str(r, "algorithm") != PackageSignature.Algorithm)
                return null;
            var file = Json.Str(r, "file") ?? "";
            var version = SemVersion.Parse(Json.Str(r, "version"));
            var sha256 = Json.Str(r, "sha256")?.ToLowerInvariant() ?? "";
            var keyId = Json.Str(r, "keyId") ?? "";
            var signature = Json.Str(r, "signature");
            if (file.Length == 0 || version is null || sha256.Length != 64 || keyId.Length != 64 || signature is null
                || !r.TryGetProperty("size", out var s) || !s.TryGetInt64(out var size) || size <= 0)
                return null;
            Convert.FromBase64String(signature);
            return new ReleaseSignature(file, version.Value, size, sha256, keyId, signature);
        }
        catch (Exception ex) when (ex is JsonException or FormatException) { return null; }
    }

    /// <summary>
    /// Es la firma de ese archivo en esa versión, hecha con una de esas claves (SubjectPublicKeyInfo en base64). Lo
    /// descargado se compara después con Size y Sha256.
    /// </summary>
    public bool Verify(IReadOnlyList<string> publicKeys, string file, SemVersion version)
    {
        if (File != file || Version.CompareTo(version) != 0) return false;
        foreach (var key in publicKeys)
        {
            try
            {
                var spki = Convert.FromBase64String(key);
                if (ExtensionKeys.KeyId(spki) != KeyId) continue;
                using var ecdsa = ExtensionKeys.ImportPublic(spki);
                return ecdsa.VerifyData(Encoding.UTF8.GetBytes(Message(File, Version, Size, Sha256)), Convert.FromBase64String(Signature), HashAlgorithmName.SHA256);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException) { }
        }
        return false;
    }
}

/// <summary>En qué va la búsqueda de una versión nueva.</summary>
enum UpdateStep { None, Checking, Available, Downloading, Ready, Failed }

/// <summary>Lo que la bandeja tiene que hacer al tocar la fila de la versión nueva.</summary>
enum UpdateRequest { None, AskToInstall, OpenPage, Restart }

/// <summary>
/// Las versiones nuevas de Claupet, desde las Releases de GitHub: una vez al día (con «Buscar actualizaciones»
/// encendido) mira la última, sin sesión, y si es más nueva avisa. Con la clave de las Releases (ReleaseKey) y el
/// permiso de la persona, baja el archivo de este sistema, comprueba su firma y su SHA-256, lo deja listo y lo cambia al
/// reiniciar (UpdateInstaller). Sin clave, solo avisa y abre la página de la Release. Cada bandeja la crea, la arranca y
/// muestra sus filas en Ajustes › Sistema; los avisos y reiniciar son de la bandeja.
/// </summary>
sealed class Updates : IDisposable
{
    public const string LatestUrl = "https://api.github.com/repos/darrenarauz21/claupet/releases/latest";
    public const string ReleasesPage = "https://github.com/darrenarauz21/claupet/releases/latest";

    /// <summary>
    /// La clave pública con que se firman las Releases: SubjectPublicKeyInfo en base64, la del «.pub.pem» que crea
    /// «Claupet --extension-keygen», sin las líneas BEGIN y END. La privada nunca va en el repositorio: la usa
    /// empaquetar.py (--clave). Vacía: todavía no hay clave, y la app solo avisa y abre la página de la Release.
    /// </summary>
    public const string ReleaseKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE2oZ9yQnv2+jIp6/Ho8QK6SE9v6HSIVe2HOb+jlo7BC7CXYjRVR8iQfPQJpiTx4FWWzws9tI2M/fQ82s7gmnGIg==";

    static readonly TimeSpan Every = TimeSpan.FromDays(1);

    readonly AppSettings _settings;
    readonly Action _save;
    readonly Action<Action> _post;
    readonly string _folder;
    readonly IReadOnlyList<string> _keys;
    readonly string _latestUrl;
    readonly string? _assetName;
    System.Threading.Timer? _timer;
    CancellationTokenSource? _download;
    bool _checking;

    /// <summary>
    /// post: corre en el hilo de la interfaz de la bandeja. Lo demás, para las pruebas: la carpeta donde se baja, las
    /// claves, de dónde se lee la última Release, el archivo de este sistema y la versión que corre.
    /// </summary>
    public Updates(AppSettings settings, Action save, Action<Action> post, string? folder = null, IReadOnlyList<string>? keys = null,
        string? latestUrl = null, string? assetName = null, SemVersion? current = null)
    {
        _settings = settings;
        _save = save;
        _post = post;
        _folder = folder ?? DataFolder.In("actualizacion");
        _keys = keys ?? (ReleaseKey.Length > 0 ? [ReleaseKey] : []);
        _latestUrl = latestUrl ?? LatestUrl;
        _assetName = assetName ?? AssetFor(ExtensionPlatform.Current);
        Current = current ?? SemVersion.App;
    }

    /// <summary>La versión que corre.</summary>
    public SemVersion Current { get; }
    public UpdateStep Step { get; private set; }
    /// <summary>La Release más nueva que la que corre; null si no hay (o todavía no se miró).</summary>
    public ReleaseInfo? Latest { get; private set; }
    public int Percent { get; private set; }
    public string? Problem { get; private set; }
    /// <summary>Lo bajado y comprobado, listo para cambiarlo al reiniciar (un archivo o, en macOS, el .app).</summary>
    public string? Staged { get; private set; }

    /// <summary>Cambió lo que se ve (en el hilo de la interfaz).</summary>
    public event Action? Changed;
    /// <summary>Hay una versión nueva: una vez por versión, para el aviso (en el hilo de la interfaz).</summary>
    public event Action<ReleaseInfo>? Found;

    /// <summary>El archivo de cada sistema en una Release (los que arma empaquetar.py); null: este sistema no tiene.</summary>
    public static string? AssetFor(string platform) => platform switch
    {
        "win-x64" => "Claupet-windows-x64.exe",
        "linux-x64" => "Claupet-linux-x64.tar.gz",
        "osx-arm64" => "Claupet-macos-arm64.zip",
        "osx-x64" => "Claupet-macos-x64.zip",
        _ => null,
    };

    /// <summary>Se puede instalar sola: hay clave y la Release trae el archivo de este sistema con su firma.</summary>
    public bool CanInstall => _keys.Count > 0 && _assetName is not null && Latest is { } latest
        && latest.Asset(_assetName) is not null && latest.Asset(_assetName + ReleaseSignature.Suffix) is not null;

    /// <summary>La página de la Release nueva (o la de la última).</summary>
    public string Page => Latest?.Page ?? ReleasesPage;

    /// <summary>Toca buscar: nunca se buscó, pasó un día o el reloj volvió atrás.</summary>
    public static bool Due(DateTimeOffset? last, DateTimeOffset now) => last is null || now - last >= Every || last > now;

    /// <summary>Mira cada hora si toca buscar; la primera vez, al minuto de abrir la app.</summary>
    public void Start() => _timer ??= new System.Threading.Timer(_ => _post(Tick), null, TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));

    void Tick()
    {
        if (!_settings.CheckUpdates || Step is UpdateStep.Downloading or UpdateStep.Ready) return;
        // Si ya se avisó de una versión nueva, al abrir la app se vuelve a mirar (para tener sus archivos a mano).
        var known = Latest is null && SemVersion.Parse(_settings.UpdateNotified) is { } notified && notified > Current;
        if (known || Due(_settings.UpdatesChecked, DateTimeOffset.UtcNow)) _ = CheckAsync();
    }

    /// <summary>Lee la última Release (sin sesión). Sin conexión no dice nada: vuelve a intentarlo en la hora siguiente.</summary>
    public async Task CheckAsync(HttpClient? client = null, CancellationToken cancel = default)
    {
        if (_checking || Step is UpdateStep.Downloading or UpdateStep.Ready) return;
        _checking = true;
        var before = Step;
        Set(() => Step = UpdateStep.Checking);
        ReleaseInfo? release = null;
        var ok = false;
        try
        {
            var http = client ?? SystemProxy.Client(TimeSpan.FromSeconds(20));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _latestUrl);
                request.Headers.UserAgent.ParseAdd($"Claupet/{Current}");
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using var response = await http.SendAsync(request, cancel);
                // 404: el repositorio todavía no tiene Releases.
                if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    response.EnsureSuccessStatusCode();
                    release = ReleaseInfo.Parse(await response.Content.ReadAsStringAsync(cancel));
                }
                ok = true;
            }
            finally
            {
                if (client is null) http.Dispose();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException) { ErrorLog.Write(ex); }
        finally { _checking = false; }

        Set(() =>
        {
            if (!ok)
            {
                Step = before == UpdateStep.Checking ? UpdateStep.None : before;
                return;
            }
            _settings.UpdatesChecked = DateTimeOffset.UtcNow;
            if (release is not null && release.Version > Current)
            {
                Latest = release;
                Step = UpdateStep.Available;
                if (_settings.UpdateNotified != release.Version.ToString())
                {
                    _settings.UpdateNotified = release.Version.ToString();
                    Found?.Invoke(release);
                }
            }
            else
            {
                Latest = null;
                Step = UpdateStep.None;
            }
            _save();
        });
    }

    /// <summary>
    /// La fila de la versión nueva: qué dice y qué hace al tocarla. Sin la clave, o si este sistema no tiene archivo, lleva
    /// a la página de la Release.
    /// </summary>
    public UpdateRequest Act()
    {
        switch (Step)
        {
            case UpdateStep.Available: return CanInstall ? UpdateRequest.AskToInstall : UpdateRequest.OpenPage;
            case UpdateStep.Downloading:
                _download?.Cancel();
                return UpdateRequest.None;
            case UpdateStep.Ready: return UpdateRequest.Restart;
            case UpdateStep.Failed: return UpdateRequest.OpenPage;
            default:
                _ = CheckAsync();
                return UpdateRequest.None;
        }
    }

    /// <summary>Lo que se pregunta antes de bajarla.</summary>
    public string ConfirmText => Latest is { } latest && _assetName is not null && latest.Asset(_assetName) is { } asset
        ? L.T("¿Descargar Claupet {0} ({1} MB)? Se comprueba su firma y se instala al reiniciar Claupet.", latest.Version, Math.Max(1, asset.Size / 1_000_000))
        : "";

    /// <summary>La fila de Ajustes › Sistema: qué dice y su enlace (vacío: sin enlace). Sin nada que decir, vacía.</summary>
    public (string Status, string Action) Row() => Step switch
    {
        UpdateStep.Checking => (L.T("Buscando una versión nueva…"), ""),
        UpdateStep.Available => (L.T("Claupet {0} está disponible", Latest!.Version), CanInstall ? L.T("Descargar e instalar…") : L.T("Ver la versión nueva")),
        UpdateStep.Downloading => (L.T("Descargando Claupet {0}… {1} %", Latest!.Version, Percent), L.T("Cancelar")),
        UpdateStep.Ready => (L.T("Claupet {0} está lista: se instala al reiniciar", Latest!.Version), L.T("Reiniciar ahora")),
        UpdateStep.Failed => (L.T("No se pudo instalar ({0})", Problem ?? ""), L.T("Ver la versión nueva")),
        _ => ("", ""),
    };

    /// <summary>
    /// Con el permiso de la persona: baja la firma, la comprueba con la clave de las Releases, baja el archivo de este
    /// sistema (retomando si se corta, como los modelos) y comprueba su tamaño y su SHA-256; después lo deja listo para
    /// cambiarlo al reiniciar. Si algo no coincide, se borra y no se instala.
    /// </summary>
    public async Task DownloadAsync(HttpClient? client = null)
    {
        if (Step != UpdateStep.Available || !CanInstall || Latest is not { } latest || _assetName is not { } name) return;
        var asset = latest.Asset(name)!;
        var signatureAsset = latest.Asset(name + ReleaseSignature.Suffix)!;
        var cancel = _download = new CancellationTokenSource();
        Set(() =>
        {
            Step = UpdateStep.Downloading;
            Percent = 0;
            Problem = null;
        });
        string? staged = null, problem = null;
        try
        {
            var http = client ?? SystemProxy.Client(TimeSpan.FromSeconds(30));
            string text;
            try { text = await http.GetStringAsync(signatureAsset.Url, cancel.Token); }
            finally { if (client is null) http.Dispose(); }
            if (ReleaseSignature.Parse(text) is not { } signature || !signature.Verify(_keys, name, latest.Version))
                throw new InvalidDataException(L.T("la firma de la versión nueva no es válida"));
            var folder = Path.Combine(_folder, latest.Version.ToString());
            var file = Path.Combine(folder, name);
            await ModelDownload.FetchAsync(asset.Url, file, signature.Size, signature.Sha256,
                new Progress<long>(have => _post(() =>
                {
                    var percent = (int)(have * 100 / signature.Size);
                    if (percent == Percent || Step != UpdateStep.Downloading) return;
                    Percent = percent;
                    Changed?.Invoke();
                })), cancel.Token, L.T("lo descargado no coincide con su firma; se borró"));
            staged = await Task.Run(() => UpdateInstaller.Unpack(file, Path.Combine(folder, "nueva")), cancel.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex);
            problem = ex.Message.TrimEnd('.');
        }
        finally
        {
            if (_download == cancel) _download = null;
            cancel.Dispose();
        }
        Set(() =>
        {
            Staged = staged;
            Problem = problem;
            Step = staged is not null ? UpdateStep.Ready : problem is not null ? UpdateStep.Failed : UpdateStep.Available;
        });
    }

    /// <summary>
    /// Cambia la app que corre (exe) por la nueva, ya comprobada; la bandeja la abre después (o queda para la próxima vez).
    /// Devuelve por qué no se pudo, o null.
    /// </summary>
    public string? Apply(string exe)
    {
        if (Step != UpdateStep.Ready || Staged is not { } staged) return null;
        if (UpdateInstaller.Target(exe) is not { } target) return L.T("Esta copia de Claupet no se puede reemplazar sola.");
        try
        {
            UpdateInstaller.Swap(target, staged);
            UpdateInstaller.AfterSwap(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex);
            Set(() =>
            {
                Step = UpdateStep.Failed;
                Problem = ex.Message.TrimEnd('.');
            });
            return ex.Message;
        }
        Staged = null;
        Step = UpdateStep.None;
        return null;
    }

    /// <summary>Al abrir la app: lo que quedó de una actualización anterior (la versión de antes, lo bajado) se borra.</summary>
    public void CleanUp(string exe)
    {
        if (UpdateInstaller.Target(exe) is { } target) UpdateInstaller.Delete(target + UpdateInstaller.Previous);
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    void Set(Action change) => _post(() =>
    {
        change();
        Changed?.Invoke();
    });

    public void Dispose()
    {
        _timer?.Dispose();
        _download?.Cancel();
    }
}

/// <summary>
/// Cambiar la app por la versión nueva. Lo que corre puede renombrarse (Windows, Linux y macOS lo permiten): la de antes
/// queda al lado con «.anterior» (se borra al abrir la nueva) y la nueva toma su lugar. Si algo falla, vuelve la de antes.
/// </summary>
static class UpdateInstaller
{
    public const string Previous = ".anterior";

    /// <summary>
    /// Lo que se reemplaza: el ejecutable (Windows y Linux, un solo archivo) o, en macOS, el .app que lo contiene; null si
    /// esta copia no está en un .app en macOS (se compiló a mano).
    /// </summary>
    public static string? Target(string exe)
    {
        if (!OperatingSystem.IsMacOS()) return exe;
        var bundle = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(exe)));
        return bundle is not null && bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? bundle : null;
    }

    /// <summary>
    /// Saca lo nuevo del archivo bajado: el .exe tal cual; de un .tar.gz (Linux), el ejecutable «Claupet»; de un .zip
    /// (macOS), la carpeta «Claupet.app». Devuelve su ruta en folder.
    /// </summary>
    public static string Unpack(string file, string folder)
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        if (file.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = File.OpenRead(file);
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            while (tar.GetNextEntry() is { } entry)
            {
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.Name.Split('/') is not [_, "Claupet"]) continue;
                var target = Path.Combine(folder, "Claupet");
                entry.ExtractToFile(target, overwrite: true);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, (UnixFileMode)0b111_101_101);
                return target;
            }
            throw new InvalidDataException(L.T("el archivo no trae Claupet"));
        }
        if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
            using var zip = ZipFile.OpenRead(file);
            foreach (var entry in zip.Entries)
            {
                // «Claupet-macos-arm64/Claupet.app/…»: solo lo de adentro del .app, y nada que salga de la carpeta.
                var parts = entry.FullName.Split('/');
                if (parts.Length < 3 || parts[1] != "Claupet.app" || entry.FullName.EndsWith('/')) continue;
                var relative = string.Join('/', parts.Skip(1));
                if (PackagePaths.Normalize(relative) is null) throw new InvalidDataException(L.T("Ruta no permitida en el paquete: «{0}».", entry.FullName));
                var target = Path.GetFullPath(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(root, StringComparison.Ordinal)) throw new InvalidDataException(L.T("Ruta no permitida en el paquete: «{0}».", entry.FullName));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
                var mode = (entry.ExternalAttributes >> 16) & 0x1FF;
                if (!OperatingSystem.IsWindows() && mode != 0) File.SetUnixFileMode(target, (UnixFileMode)mode);
            }
            var app = Path.Combine(folder, "Claupet.app");
            return Directory.Exists(app) ? app : throw new InvalidDataException(L.T("el archivo no trae Claupet"));
        }
        var exe = Path.Combine(folder, Path.GetFileName(file));
        File.Copy(file, exe, overwrite: true);
        return exe;
    }

    /// <summary>La de antes queda al lado con «.anterior»; la nueva toma su lugar. Si no se puede, vuelve la de antes.</summary>
    public static void Swap(string target, string staged)
    {
        var previous = target + Previous;
        Delete(previous);
        Move(target, previous);
        try { Move(staged, target); }
        catch
        {
            Move(previous, target);
            throw;
        }
    }

    /// <summary>En macOS, el .app nuevo se firma solo para este equipo y se le quita la cuarentena, como en preparar-mac.sh.</summary>
    public static void AfterSwap(string target)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Run("xattr", "-dr", "com.apple.quarantine", target);
        Run("codesign", "--force", "--deep", "--sign", "-", target);
    }

    static void Run(string program, params string[] args)
    {
        try
        {
            var start = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var a in args) start.ArgumentList.Add(a);
            using var process = Process.Start(start);
            process?.WaitForExit(30_000);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { ErrorLog.Write(ex); }
    }

    static void Move(string from, string to)
    {
        if (Directory.Exists(from)) Directory.Move(from, to);
        else File.Move(from, to);
    }

    public static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        // La de antes puede seguir abierta (se cierra en un momento): se borra la próxima vez.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
