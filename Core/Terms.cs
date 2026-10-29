namespace Claupet;

/// <summary>
/// Los términos y condiciones que se aceptan en el asistente de configuración (Core/Terminos/&lt;idioma&gt;.txt, dentro
/// del ejecutable). Sin aceptarlos, Claupet se cierra. Si el texto cambia de forma que haya que aceptarlo de nuevo, se
/// sube Version: al abrirse, la app vuelve a mostrar el asistente.
/// </summary>
static class Terms
{
    public const string Version = "1";

    /// <summary>El texto en el idioma de la app (en español, si falta el suyo).</summary>
    public static string Text => Read(L.Language) ?? Read("es") ?? "";

    public static bool Accepted(AppSettings settings) => settings.TermsAccepted == Version;

    /// <summary>El texto en ese idioma; null si no está.</summary>
    internal static string? Read(string language)
    {
        try
        {
            using var stream = typeof(Terms).Assembly.GetManifestResourceStream($"terminos/{language}.txt");
            if (stream is null) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().ReplaceLineEndings().Trim();
        }
        catch (IOException) { return null; }
    }
}
