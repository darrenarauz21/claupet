namespace Claupet;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// <summary>Cómo estaba un archivo la última vez que se leyó o se guardó: para saber si alguien más lo cambió.</summary>
sealed record DiskState(long Length, DateTime Written, string Sha256);

enum DiskChange { Same, Changed, Missing, Unreadable }

/// <summary>Lo que se encontró en el disco. Changed trae el texto nuevo; State, cómo quedó si se pudo leer.</summary>
sealed record DiskCheck(DiskChange Change, DiskState? State = null, string? Text = null, Encoding? Encoding = null);

/// <summary>
/// Leer y guardar un archivo de texto con su codificación. File.ReadAllText supone UTF-8 y un archivo en ANSI (lo de
/// siempre en un Windows en español: el Bloc de notas viejo, Excel, un .txt antiguo) mostraba «Ã©» o «�» en vez de
/// tildes y eñes. Ahora: la marca del principio (BOM) si la hay; si no, UTF-8 si lo es de verdad; si no, ANSI.
/// </summary>
static class TextFiles
{
    static readonly UTF8Encoding Utf8 = new(false), Utf8Strict = new(false, throwOnInvalidBytes: true);

    /// <summary>ANSI del sistema (la página de códigos de Windows: 1252 en español e inglés); en Linux y macOS, 1252.</summary>
    public static Encoding Ansi
    {
        get
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var page = OperatingSystem.IsWindows() ? CultureInfo.CurrentCulture.TextInfo.ANSICodePage : 1252;
            try { return Encoding.GetEncoding(page is 0 or 65001 ? 1252 : page); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return Encoding.GetEncoding(1252); }
        }
    }

    /// <summary>El texto de esos bytes y la codificación en que venían (para guardarlo igual).</summary>
    public static (string Text, Encoding Encoding) Decode(byte[] bytes)
    {
        // Con BOM: lo dice el archivo (UTF-8, UTF-16 o UTF-32); se guarda con la misma marca.
        Encoding? marked =
            bytes is [0xEF, 0xBB, 0xBF, ..] ? new UTF8Encoding(true)
            : bytes is [0xFF, 0xFE, 0x00, 0x00, ..] ? new UTF32Encoding(false, true)
            : bytes is [0x00, 0x00, 0xFE, 0xFF, ..] ? new UTF32Encoding(true, true)
            : bytes is [0xFF, 0xFE, ..] ? new UnicodeEncoding(false, true)
            : bytes is [0xFE, 0xFF, ..] ? new UnicodeEncoding(true, true)
            : null;
        if (marked is not null)
        {
            var bom = marked.Preamble.Length;
            return (marked.GetString(bytes, bom, bytes.Length - bom), marked);
        }
        try { return (Utf8Strict.GetString(bytes), Utf8); }
        catch (DecoderFallbackException) { }
        var ansi = Ansi;
        return (ansi.GetString(bytes), ansi);
    }

    public static (string Text, Encoding Encoding) Read(string path) => Decode(File.ReadAllBytes(path));

    /// <summary>Si no es de texto: tiene un byte en cero al principio. Si no se puede leer, se deja que lo diga quien lo abra.</summary>
    public static bool LooksBinary(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            var head = new byte[8000];
            return Array.IndexOf(head, (byte)0, 0, file.Read(head)) >= 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    /// <summary>Lo más grande que abre el editor.</summary>
    public const long MaxEditable = 20 * 1024 * 1024;

    static DiskState StateOf(FileInfo info, byte[] bytes) =>
        new(bytes.Length, info.LastWriteTimeUtc, Convert.ToHexString(SHA256.HashData(bytes)));

    /// <summary>Cómo está ahora el archivo (después de guardarlo).</summary>
    public static DiskState StateOf(string path)
    {
        var info = new FileInfo(path);
        return StateOf(info, File.ReadAllBytes(path));
    }

    /// <summary>
    /// ¿Cambió el archivo desde known? Mira primero el tamaño y la fecha, que no cuestan nada; si cambiaron, compara el
    /// contenido: abrirlo y guardarlo igual, o un «git checkout» que lo deja igual, no es un cambio. Sin known, cualquier
    /// archivo que aparezca es un cambio. Unreadable: no se pudo leer ahora (otro programa escribiéndolo): se mira después.
    /// </summary>
    public static DiskCheck Compare(string path, DiskState? known)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return new(known is null ? DiskChange.Same : DiskChange.Missing);
            if (known is not null && info.Length == known.Length && info.LastWriteTimeUtc == known.Written) return new(DiskChange.Same, known);
            if (info.Length > MaxEditable) return new(DiskChange.Unreadable);
            var bytes = File.ReadAllBytes(path);
            var state = StateOf(info, bytes);
            if (known is not null && state.Sha256 == known.Sha256) return new(DiskChange.Same, state);
            var (text, encoding) = Decode(bytes);
            return new(DiskChange.Changed, state, text, encoding);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(DiskChange.Unreadable); }
    }

    /// <summary>
    /// La codificación con que se guarda ese texto: la del archivo si puede escribirlo entero; si no (un emoji o una
    /// letra que ANSI no tiene, pegados después), UTF-8, así no se pierde nada como «?».
    /// </summary>
    public static Encoding ForSaving(string text, Encoding? original)
    {
        if (original is null || original is UTF8Encoding or UnicodeEncoding or UTF32Encoding) return original ?? Utf8;
        var strict = Encoding.GetEncoding(original.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        try
        {
            strict.GetByteCount(text);
            return original;
        }
        catch (EncoderFallbackException) { return Utf8; }
    }
}
