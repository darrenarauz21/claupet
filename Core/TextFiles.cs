namespace Claupet;

using System.Globalization;
using System.Text;

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
