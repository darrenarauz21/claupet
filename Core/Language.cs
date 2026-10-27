using System.Globalization;
using System.Text.Json;

namespace Claupet;

/// <summary>
/// Los idiomas de la app. El texto en español del código es la clave: L.T("Actualizar") da su traducción
/// en el idioma elegido (Core/Idiomas/en.json, dentro del ejecutable) o el mismo texto si es español o si
/// falta. Para otro idioma basta otro archivo y una entrada en Choices. El idioma se fija al abrir la
/// app (Program llama a Use antes que nada); al cambiarlo en Ajustes, la app se reinicia.
/// </summary>
static class L
{
    public static readonly (string Id, string Label)[] Choices = [("es", "Español"), ("en", "English")];

    static Dictionary<string, string> _table = new();

    /// <summary>«es» o «en».</summary>
    public static string Language { get; private set; } = "es";

    public static bool IsSpanish => Language == "es";

    /// <summary>El nombre del idioma elegido, en su idioma.</summary>
    public static string Label => Choices.First(c => c.Id == Language).Label;

    /// <summary>El guardado en Ajustes o, sin elegir, el del sistema: español si está en español; si no, inglés.</summary>
    public static string Resolve(string? saved) =>
        Choices.Any(c => c.Id == saved) ? saved! : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en";

    public static void Use(string? saved)
    {
        Language = Resolve(saved);
        _table = IsSpanish ? new() : Load(Language);
        // Fechas, horas y números en el idioma elegido; si es el del sistema, con sus formatos (12 o 24 h).
        var culture = CultureInfo.CurrentCulture.TwoLetterISOLanguageName == Language
            ? CultureInfo.CurrentCulture
            : CultureInfo.GetCultureInfo(IsSpanish ? "es" : "en-US");
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    static Dictionary<string, string> Load(string language)
    {
        try
        {
            using var stream = typeof(L).Assembly.GetManifestResourceStream($"idiomas/{language}.json");
            return stream is null ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
        }
        catch (Exception) { return new(); }
    }

    /// <summary>El texto en el idioma elegido.</summary>
    public static string T(string text) => _table.TryGetValue(text, out var translated) ? translated : text;

    /// <summary>Con valores, como string.Format: L.T("Reinicia en {0} min", minutos).</summary>
    public static string T(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(format), args);
}
