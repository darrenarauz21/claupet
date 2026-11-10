namespace Claupet;

using System.Text;
using System.Text.RegularExpressions;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;

/// <summary>
/// Los lenguajes del editor. Texto y Markdown se ven como documento (letra normal, columna al centro); los demás, como
/// código (letra fija, números de línea), con los colores de AvaloniaEdit si los tiene.
/// </summary>
static class EditorLanguages
{
    public const string Text = "text", Markdown = "markdown", Plain = "plain";

    /// <summary>Los de AvaloniaEdit que no se ofrecen: variantes internas o los que ya están como Markdown.</summary>
    static readonly string[] Hidden = ["XmlDoc", "MarkDown", "MarkDownWithFontSize"];

    public static bool IsDocument(string language) => language is Text or Markdown;

    /// <summary>El que toca por la extensión: .md, Markdown; .txt o sin extensión, Texto; otra conocida, su lenguaje.</summary>
    public static string ForPath(string? path)
    {
        var extension = Path.GetExtension(path ?? "").ToLowerInvariant();
        if (extension is ".md" or ".markdown") return Markdown;
        if (extension is "" or ".txt" or ".text") return Text;
        return HighlightingManager.Instance.GetDefinitionByExtension(extension) is { } definition && !Hidden.Contains(definition.Name)
            ? definition.Name
            : Plain;
    }

    public static string Label(string language) => language switch
    {
        Text => L.T("Texto"),
        Markdown => "Markdown",
        Plain => L.T("Código sin colores"),
        _ => language,
    };

    /// <summary>Todos, en el orden de la lista: los tres generales y después los de código por nombre.</summary>
    public static IReadOnlyList<string> All =>
    [
        Text, Markdown, Plain,
        .. HighlightingManager.Instance.HighlightingDefinitions.Select(d => d.Name).Where(n => !Hidden.Contains(n))
            .Order(StringComparer.OrdinalIgnoreCase),
    ];

    public static IHighlightingDefinition? Definition(string language) =>
        IsDocument(language) || language == Plain ? null : HighlightingManager.Instance.GetDefinition(language);

    /// <summary>Cómo se comenta: el comienzo y, si es de bloque (HTML, CSS), el final. null: no tiene comentarios.</summary>
    public static (string Open, string? Close)? Comment(string language) => language switch
    {
        "C#" or "C++" or "Java" or "JavaScript" or "TypeScript" or "PHP" or "Json" or "Coco" => ("//", null),
        "Python" or "PowerShell" or "Boo" or Plain => ("#", null),
        "VB" => ("'", null),
        "TSQL" => ("--", null),
        "TeX" => ("%", null),
        "CSS" => ("/*", "*/"),
        "XML" or "HTML" or "ASP/XHTML" or Markdown => ("<!--", "-->"),
        _ => null,
    };
}

/// <summary>Lo que se busca: el texto y las tres opciones (mayúsculas, palabra completa, expresión regular).</summary>
sealed record EditorQuery(string Text, bool MatchCase = false, bool WholeWord = false, bool Regex = false)
{
    /// <summary>El patrón; null si no hay nada que buscar o la expresión regular está a medio escribir.</summary>
    public Regex? Pattern()
    {
        if (Text.Length == 0) return null;
        var pattern = Regex ? Text : System.Text.RegularExpressions.Regex.Escape(Text);
        if (WholeWord) pattern = $@"(?<![\p{{L}}\p{{N}}_])(?:{pattern})(?![\p{{L}}\p{{N}}_])";
        var options = RegexOptions.Multiline | RegexOptions.CultureInvariant | (MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        try { return new Regex(pattern, options, TimeSpan.FromSeconds(1)); }
        catch (ArgumentException) { return null; }
    }

    /// <summary>Lo que va en lugar de una coincidencia: con expresión regular, $1 y compañía se reemplazan.</summary>
    public string Replacement(Match match, string with) => Regex ? match.Result(with) : with;
}

static class EditorSearch
{
    /// <summary>Más que esto no se marca (un archivo enorme con una letra suelta): se cuenta hasta aquí.</summary>
    public const int Max = 10000;

    /// <summary>Dónde aparece, en orden. Las coincidencias vacías (como «^») no cuentan: no hay nada que marcar.</summary>
    public static List<(int Start, int Length)> FindAll(string text, Regex pattern)
    {
        var found = new List<(int, int)>();
        try
        {
            for (var m = pattern.Match(text); m.Success && found.Count < Max; m = m.NextMatch())
                if (m.Length > 0) found.Add((m.Index, m.Length));
        }
        catch (RegexMatchTimeoutException) { }
        return found;
    }

    /// <summary>Reemplaza todas de una vez (un solo Ctrl+Z las devuelve). Cuántas cambió.</summary>
    public static int ReplaceAll(TextDocument document, EditorQuery query, string with)
    {
        if (query.Pattern() is not { } pattern) return 0;
        var text = document.Text;
        List<(int Start, int Length, string Text)> changes = [];
        try
        {
            for (var m = pattern.Match(text); m.Success && changes.Count < Max; m = m.NextMatch())
                if (m.Length > 0) changes.Add((m.Index, m.Length, query.Replacement(m, with)));
        }
        catch (RegexMatchTimeoutException) { }
        using (document.RunUpdate())
            for (var i = changes.Count - 1; i >= 0; i--)
                document.Replace(changes[i].Start, changes[i].Length, changes[i].Text);
        return changes.Count;
    }
}

/// <summary>Lo que se hace con líneas enteras: duplicar, subir y bajar, comentar y borrar. Devuelven la selección nueva.</summary>
static class EditorLines
{
    /// <summary>Las líneas que toca la selección (si termina al principio de una línea, esa no cuenta).</summary>
    static (DocumentLine First, DocumentLine Last) Span(TextDocument document, int start, int length)
    {
        var first = document.GetLineByOffset(start);
        var last = document.GetLineByOffset(start + length);
        if (length > 0 && last.Offset == start + length && last.LineNumber > first.LineNumber) last = last.PreviousLine;
        return (first, last);
    }

    /// <summary>El fin de línea del archivo: el de su primera línea, o el del sistema si tiene una sola.</summary>
    public static string NewLine(TextDocument document)
    {
        var first = document.GetLineByNumber(1);
        return first.DelimiterLength > 0 ? document.GetText(first.EndOffset, first.DelimiterLength) : Environment.NewLine;
    }

    /// <summary>Con algo seleccionado, lo copia justo después; si no, copia la línea debajo.</summary>
    public static (int Start, int Length) Duplicate(TextDocument document, int start, int length)
    {
        if (length > 0)
        {
            document.Insert(start + length, document.GetText(start, length));
            return (start + length, length);
        }
        var line = document.GetLineByOffset(start);
        var column = start - line.Offset;
        var end = line.EndOffset;
        var newLine = line.DelimiterLength > 0 ? document.GetText(line.EndOffset, line.DelimiterLength) : NewLine(document);
        document.Insert(end, newLine + document.GetText(line.Offset, line.Length));
        return (end + newLine.Length + column, 0);
    }

    /// <summary>Sube o baja las líneas de la selección, cambiándolas con la de al lado.</summary>
    public static (int Start, int Length) Move(TextDocument document, int start, int length, bool up)
    {
        var (first, last) = Span(document, start, length);
        var block = document.GetText(first.Offset, last.EndOffset - first.Offset);
        if (up)
        {
            if (first.PreviousLine is not { } previous) return (start, length);
            var text = document.GetText(previous.Offset, previous.Length);
            var newLine = document.GetText(previous.EndOffset, previous.DelimiterLength);
            var from = previous.Offset;
            document.Replace(from, last.EndOffset - from, block + newLine + text);
            return (start - text.Length - newLine.Length, length);
        }
        if (last.NextLine is not { } next) return (start, length);
        var nextText = document.GetText(next.Offset, next.Length);
        var lastNewLine = document.GetText(last.EndOffset, last.DelimiterLength);
        document.Replace(first.Offset, next.EndOffset - first.Offset, nextText + lastNewLine + block);
        return (start + nextText.Length + lastNewLine.Length, length);
    }

    /// <summary>
    /// Comenta o descomenta las líneas de la selección. Si todas las que tienen algo ya están comentadas, les quita el
    /// comentario; si no, se lo pone a todas, a la altura de la menos sangrada.
    /// </summary>
    public static (int Start, int Length) ToggleComment(TextDocument document, int start, int length, (string Open, string? Close) comment)
    {
        var (first, last) = Span(document, start, length);
        var lines = new List<DocumentLine>();
        for (var line = first; line is not null && line.LineNumber <= last.LineNumber; line = line.NextLine) lines.Add(line);
        var filled = lines.Where(l => !string.IsNullOrWhiteSpace(document.GetText(l))).ToList();
        if (filled.Count == 0) return (start, length);
        var (caretLine, caretColumn, caretLineLength) = (first.LineNumber, start - first.Offset, first.Length);
        var (firstNumber, lastNumber) = (first.LineNumber, last.LineNumber);
        string Indent(DocumentLine l) => TextUtilities.GetLeadingWhitespace(document, l) is var ws ? document.GetText(ws) : "";

        using (document.RunUpdate())
        {
            if (comment.Close is { } close)
            {
                // De bloque: envuelve el tramo entero (de la primera letra de la primera al final de la última).
                var from = filled[0].Offset + Indent(filled[0]).Length;
                var to = filled[^1].EndOffset;
                var inner = document.GetText(from, to - from);
                if (inner.StartsWith(comment.Open, StringComparison.Ordinal) && inner.EndsWith(close, StringComparison.Ordinal) && inner.Length >= comment.Open.Length + close.Length)
                {
                    var body = inner[comment.Open.Length..^close.Length];
                    if (body.StartsWith(' ')) body = body[1..];
                    if (body.EndsWith(' ')) body = body[..^1];
                    document.Replace(from, to - from, body);
                }
                else document.Replace(from, to - from, comment.Open + " " + inner + " " + close);
            }
            else
            {
                var open = comment.Open;
                var all = filled.All(l => document.GetText(l).TrimStart().StartsWith(open, StringComparison.Ordinal));
                var column = filled.Min(l => Indent(l).Length);
                foreach (var line in filled.AsEnumerable().Reverse())
                {
                    if (all)
                    {
                        var at = line.Offset + Indent(line).Length;
                        var cut = open.Length + (document.GetText(line).Length > at - line.Offset + open.Length && document.GetCharAt(at + open.Length) == ' ' ? 1 : 0);
                        document.Remove(at, cut);
                    }
                    else document.Insert(line.Offset + column, open + " ");
                }
            }
        }
        if (length == 0)
        {
            var line = document.GetLineByNumber(caretLine);
            return (line.Offset + Math.Clamp(caretColumn + line.Length - caretLineLength, 0, line.Length), 0);
        }
        var firstNow = document.GetLineByNumber(firstNumber);
        var lastNow = document.GetLineByNumber(lastNumber);
        return (firstNow.Offset, lastNow.EndOffset - firstNow.Offset);
    }

    /// <summary>Borra las líneas de la selección; el cursor queda al principio de la que sigue.</summary>
    public static (int Start, int Length) Delete(TextDocument document, int start, int length)
    {
        var (first, last) = Span(document, start, length);
        var lineStart = first.Offset;
        var from = lineStart;
        var to = last.Offset + last.TotalLength;
        // La última línea no tiene fin de línea: se lleva el de la anterior.
        if (last.DelimiterLength == 0 && first.PreviousLine is { } previous) from = previous.EndOffset;
        document.Remove(from, to - from);
        return (from == lineStart ? Math.Min(from, document.TextLength) : document.GetLineByOffset(from).Offset, 0);
    }
}

/// <summary>Fin de línea, sangría y codificación: lo que se muestra (y se cambia) en la barra de abajo.</summary>
static class EditorFormat
{
    public const string Crlf = "\r\n", Lf = "\n";

    /// <summary>El fin de línea que más usa el texto; sin ninguno, el del sistema.</summary>
    public static string DetectNewLine(string text)
    {
        int crlf = 0, lf = 0;
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n')
            {
                if (i > 0 && text[i - 1] == '\r') crlf++;
                else lf++;
            }
        return crlf == 0 && lf == 0 ? Environment.NewLine : crlf >= lf ? Crlf : Lf;
    }

    /// <summary>Pone ese fin de línea en todas (un solo Ctrl+Z lo deshace).</summary>
    public static void ConvertNewLines(TextDocument document, string newLine)
    {
        using (document.RunUpdate())
            for (var line = document.LineCount > 0 ? document.GetLineByNumber(document.LineCount) : null; line is not null; line = line.PreviousLine)
                if (line.DelimiterLength > 0 && document.GetText(line.EndOffset, line.DelimiterLength) != newLine)
                    document.Replace(line.EndOffset, line.DelimiterLength, newLine);
    }

    /// <summary>Si el archivo sangra con tabulaciones o con espacios, y de cuántos.</summary>
    public static (bool Tabs, int Size) DetectIndent(string text)
    {
        int tabs = 0, spaces = 0, twos = 0;
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith('\t')) tabs++;
            else if (line.StartsWith(' ') && line.TrimStart().Length > 0)
            {
                spaces++;
                var count = line.Length - line.TrimStart(' ').Length;
                if (count % 4 == 2) twos++;
            }
        }
        if (tabs > spaces) return (true, 4);
        return (false, spaces > 0 && twos * 5 >= spaces ? 2 : 4);
    }

    public static string NewLineLabel(string newLine) => newLine == Lf ? "LF" : "CRLF";

    public static string EncodingLabel(Encoding? encoding) => encoding switch
    {
        null => "UTF-8",
        UTF8Encoding utf8 => utf8.GetPreamble().Length > 0 ? L.T("UTF-8 con BOM") : "UTF-8",
        UnicodeEncoding => encoding.CodePage == 1201 ? "UTF-16 BE" : "UTF-16 LE",
        UTF32Encoding => encoding.CodePage == 12001 ? "UTF-32 BE" : "UTF-32 LE",
        _ => $"ANSI ({encoding.WebName})",
    };

    /// <summary>Las que se ofrecen para guardar.</summary>
    public static Encoding[] Choices => [new UTF8Encoding(false), new UTF8Encoding(true), TextFiles.Ansi, new UnicodeEncoding(false, true)];
}
