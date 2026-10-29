using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>Cuánto esfuerzo pide una pregunta, en el modo «Automático» del modelo.</summary>
enum AutoLevel { Light, Normal, Deep }

/// <summary>Lo que eligió el modo automático: el nivel, el modelo (alias de Claude Code) y el esfuerzo (--effort).</summary>
sealed record AutoChoice(AutoLevel Level, string Model, string Effort);

/// <summary>
/// El modo «Automático» del modelo de las conversaciones: cada pregunta va al modelo y al esfuerzo que pide. Lo decide
/// la app con reglas sobre el texto, sin preguntarle a nadie (no gasta tokens): lo corto y suelto va a Haiku con poco
/// esfuerzo; explicar, escribir, traducir o un ejemplo de código, a Sonnet; diseñar, analizar, comparar a fondo o
/// código que falla, a Opus con esfuerzo alto.
/// </summary>
static class AutoModel
{
    public const string Id = "auto";

    // Sobre el texto en minúsculas y sin tildes.
    static readonly Regex Deep = new(
        @"\b(disen(a|ar|ame|o)\b|arquitectura|depur|debug|por que (falla|no funciona|da error|truena)|no funciona|optimiz|refactoriz|"
        + @"analiz|compar(a|ar|ame|acion)\b|evalu|ventajas y desventajas|pros y contras|estrategia|plan (de|para)|demuestr|paso a paso|"
        + @"a fondo|en detalle|detallad|profund|investig|revis(a|ar|ame) (este|mi|el|la) (codigo|script|consulta|funcion)|causa raiz|"
        + @"design|architecture|refactor|analy[sz]|trade-?offs?|in depth|step by step|prove|root cause|doesn'?t work)",
        RegexOptions.CultureInvariant);

    static readonly Regex Normal = new(
        @"\b(explica|como (hago|se|funciona|puedo|hacer)|por que|ejemplo|codigo|script|funcion|escrib|redact|traduc|resum|"
        + @"sql|regex|consulta|python|c#|c sharp|javascript|typescript|powershell|bash|json|html|css|correo|carta|"
        + @"explain|how (do|to|does|can)|why|example|write|translate|summari[sz]e|code|function|query)",
        RegexOptions.CultureInvariant);

    /// <summary>Código pegado o un error con su traza (sobre el texto tal cual).</summary>
    static readonly Regex Code = new(@"```|\bException\b|Traceback|\berror [A-Z]{1,4}\d{2,}|^\s+at \S+|[{};]\s*$", RegexOptions.Multiline);

    public static AutoChoice Pick(string prompt, IReadOnlyList<ChatTurn> history, bool file = false)
    {
        var score = Score(prompt);
        // Analizar un archivo, o repreguntar corto sobre una respuesta con código, no baja de lo normal.
        if (file || history.LastOrDefault(t => !t.FromPerson) is { } last && last.Text.Contains("```")) score = Math.Max(score, 1);
        var level = score >= 3 ? AutoLevel.Deep : score >= 1 ? AutoLevel.Normal : AutoLevel.Light;
        return level switch
        {
            AutoLevel.Light => new(level, "haiku", "low"),
            AutoLevel.Normal => new(level, "sonnet", "medium"),
            _ => new(level, "opus", "high"),
        };
    }

    /// <summary>Cuánto pesa la pregunta: 0 es suelta; de 1 a 2, normal; desde 3, a fondo.</summary>
    static int Score(string prompt)
    {
        var text = Plain(prompt);
        var score = 0;
        if (prompt.Length > 160) score++;
        if (prompt.Length > 700) score++;
        if (Code.IsMatch(prompt)) score += 2;
        if (Deep.IsMatch(text)) score += 3;
        else if (Normal.IsMatch(text)) score++;
        if (prompt.Count(c => c == '?') >= 3) score++;
        return score;
    }

    static string Plain(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c);
        return builder.ToString();
    }

    /// <summary>Cómo se dice el nivel en el chip de quién respondió.</summary>
    public static string EffortLabel(AutoLevel level) => level switch
    {
        AutoLevel.Light => L.T("esfuerzo bajo"),
        AutoLevel.Normal => L.T("esfuerzo medio"),
        _ => L.T("esfuerzo alto"),
    };
}
