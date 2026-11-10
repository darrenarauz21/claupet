using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>
/// Los títulos cortos del historial («Terminal no abre pestañas») en vez del primer pedido tal cual. Si el agente ya
/// guardó uno, se usa ese; si no, lo escribe el asistente que esté elegido en Claupet (el de una extensión, el modelo
/// local o Claude), una sola vez por sesión: queda en historial-titulos.json. Mientras tanto se ve el pedido recortado.
/// </summary>
static partial class SessionTitles
{
    /// <summary>Hasta cuántos caracteres del pedido se mandan para titularlo.</summary>
    public const int PromptLimit = 600;
    const int MaxTitle = 60;

    static readonly Lock Gate = new();
    static Dictionary<string, string>? _titles;

    static string FilePath => DataFolder.In("historial-titulos.json");

    public const string System =
        "Escribe un título corto (de 3 a 6 palabras) para una sesión de programación a partir del primer pedido de la persona. " +
        "En el mismo idioma del pedido, como un asunto: sin comillas, sin punto final, sin emojis y sin explicar nada. Responde solo el título.";

    /// <summary>El título que ya se escribió para esa sesión; null si todavía no hay.</summary>
    public static string? Cached(string id)
    {
        lock (Gate) return Load().GetValueOrDefault(id);
    }

    /// <summary>Guarda el título de una sesión (lo de antes del archivo se respeta: otra ventana pudo agregar títulos).</summary>
    public static void Remember(string id, string title)
    {
        lock (Gate)
        {
            _titles = null;
            var titles = Load();
            titles[id] = title;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(titles));
                File.Move(temp, FilePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    static Dictionary<string, string> Load()
    {
        if (_titles is not null) return _titles;
        try
        {
            if (File.Exists(FilePath))
                return _titles = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return _titles = [];
    }

    /// <summary>
    /// Mientras no hay título: el pedido sin las marcas de imagen, hasta el final de la primera frase y como mucho
    /// MaxTitle caracteres (cortado en una palabra), con mayúscula al comienzo.
    /// </summary>
    public static string Short(string prompt)
    {
        var text = ImageMark().Replace(prompt, " ");
        text = Spaces().Replace(text, " ").Trim();
        if (text.Length == 0) return prompt.Trim();
        var end = SentenceEnd().Match(text);
        if (end.Success && end.Index >= 12) text = text[..(end.Index + 1)].TrimEnd('.', ' ');
        if (text.Length > MaxTitle)
        {
            var cut = text.LastIndexOf(' ', MaxTitle);
            text = text[..(cut > MaxTitle / 2 ? cut : MaxTitle)].TrimEnd(',', ';', ':', ' ') + "…";
        }
        return char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];
    }

    /// <summary>Lo que respondió el modelo, como título: su primera línea, sin comillas, «Título:», markdown ni punto final. null si no sirve.</summary>
    public static string? Clean(string? answer)
    {
        if (answer is null) return null;
        var line = answer.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (line is null) return null;
        line = TitleLabel().Replace(line, "");
        line = line.Trim(' ', '"', '\'', '«', '»', '“', '”', '*', '#', '`', '_').TrimEnd('.', ' ');
        line = Spaces().Replace(line, " ");
        if (line.Length < 2) return null;
        if (line.Length > MaxTitle)
        {
            var cut = line.LastIndexOf(' ', MaxTitle);
            line = line[..(cut > MaxTitle / 2 ? cut : MaxTitle)].TrimEnd(',', ';', ':', ' ') + "…";
        }
        return line;
    }

    /// <summary>
    /// Le pide el título al asistente elegido en Claupet: el de una extensión (Gemini, Codex, OpenRouter…), si no el
    /// modelo local (si está listo) y si no Claude (Haiku), salvo que las respuestas no deban salir del equipo
    /// (cloud = false). null si nadie pudo.
    /// </summary>
    public static async Task<string?> AskAsync(string prompt, IAssistantBrain? external, IAssistantBrain? local, bool cloud,
        Func<string, CancellationToken, Task<string?>> claude, CancellationToken cancel)
    {
        var text = prompt.Length > PromptLimit ? prompt[..PromptLimit] : prompt;
        var request = new BrainRequest(System, [new BrainMessage("user", text)], null);
        foreach (var brain in new[] { external, local })
        {
            if (brain is null) continue;
            try
            {
                if (Clean((await brain.RespondAsync(request, null, cancel)).Text) is { } title) return title;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }
        if (!cloud || external is not null) return null; // con una extensión elegida, Claude no se usa a escondidas
        try { return Clean(await claude(System + "\n\nPedido:\n" + text, cancel)); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return null; }
    }

    [GeneratedRegex(@"\[(?:Imagen|Image) #\d+\]", RegexOptions.IgnoreCase)]
    private static partial Regex ImageMark();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
    [GeneratedRegex(@"[.?!](?:\s|$)")]
    private static partial Regex SentenceEnd();
    [GeneratedRegex(@"^(?:t[ií]tulo|title)\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex TitleLabel();
}
