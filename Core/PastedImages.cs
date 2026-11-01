namespace Claupet;

/// <summary>
/// Una imagen pegada como texto: la ruta de un archivo de imagen o la URL de una imagen. Se adjunta como «[Imagen #N]»,
/// igual que una imagen copiada (como en Rider o en otras consolas). Solo los formatos que reciben los agentes (PNG,
/// JPEG, GIF y WebP) y hasta 20 MB; cualquier otro texto se pega como siempre.
/// </summary>
static class PastedImages
{
    public const long MaxBytes = 20_000_000;
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>El tipo de una imagen por su extensión (image/png…); null si no es de las que se adjuntan.</summary>
    public static string? MediaTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => null,
    };

    /// <summary>
    /// Lo pegado, si es una sola ruta o URL de imagen: (Url: true, la URL) o (Url: false, la ruta completa de un archivo
    /// que existe; relativa, dentro de la carpeta de la sesión). null si es otro texto.
    /// </summary>
    public static (bool Url, string Target)? Reference(string text, string? folder, Func<string, bool>? exists = null)
    {
        var value = text.Trim().Trim('"', '\'');
        if (value.Length == 0 || value.IndexOfAny(['\r', '\n']) >= 0) return null;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                return MediaTypeOf(uri.AbsolutePath) is not null ? (true, uri.AbsoluteUri) : null;
            if (uri.IsFile) value = uri.LocalPath;
        }
        if (MediaTypeOf(value) is null) return null;
        string full;
        try
        {
            if (Path.IsPathFullyQualified(value)) full = Path.GetFullPath(value);
            else if (string.IsNullOrEmpty(folder)) return null;
            else full = Path.GetFullPath(Path.Combine(folder, value));
        }
        catch (Exception) { return null; }
        bool found;
        try { found = (exists ?? File.Exists)(full); }
        catch (Exception) { found = false; }
        return found ? (false, full) : null;
    }

    /// <summary>La imagen: leída del archivo o bajada de la URL (si el servidor dice que es PNG, JPEG, GIF o WebP); null si no se pudo.</summary>
    public static async Task<AgentImage?> LoadAsync((bool Url, string Target) reference, CancellationToken cancel = default)
    {
        try
        {
            if (!reference.Url)
            {
                var info = new FileInfo(reference.Target);
                if (!info.Exists || info.Length > MaxBytes || MediaTypeOf(reference.Target) is not { } type) return null;
                return new AgentImage(type, Convert.ToBase64String(await File.ReadAllBytesAsync(reference.Target, cancel)));
            }
            using var response = await Http.GetAsync(reference.Target, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (!response.IsSuccessStatusCode) return null;
            var media = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            if (media is not ("image/png" or "image/jpeg" or "image/gif" or "image/webp")) return null;
            if (response.Content.Headers.ContentLength > MaxBytes) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancel);
            using var memory = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancel)) > 0)
            {
                memory.Write(buffer, 0, read);
                if (memory.Length > MaxBytes) return null;
            }
            return new AgentImage(media, Convert.ToBase64String(memory.ToArray()));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ErrorLog.Write(ex);
            return null;
        }
    }
}
