namespace Claupet;

using System.Text.RegularExpressions;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Rendering;

/// <summary>Los colores del editor, los del diseño (Documento y Código), en claro u oscuro.</summary>
sealed record EditorInk(IBrush Keyword, IBrush String, IBrush Function, IBrush Number, IBrush Comment, IBrush LineNumber,
    IBrush Marks, IBrush Ghost, IBrush SelectionDoc, IBrush SelectionCode, IBrush Suggest)
{
    public static EditorInk For(Ink ink) => ink.Dark
        ? new(B("#E8987C"), B("#A8C7A0"), B("#9DBBE0"), B("#E0C58A"), ink.Tertiary, B("#6E6C66"), B("#77756E"), B("#8C8A82"),
            B("#477896C8"), B("#247896C8"), B("#1AD97757"))
        : new(B("#B4532F"), B("#3F6B3A"), B("#2F5B8C"), B("#8A5A12"), ink.Tertiary, B("#9C9A92"), B("#A3A097"), B("#7A7871"),
            B("#335A82C8"), B("#1F5A82C8"), B("#1AD97757"));

    static ImmutableSolidColorBrush B(string hex) => new(Color.Parse(hex));
}

/// <summary>
/// El resaltado de AvaloniaEdit con los colores del tema, iguales en todos los lenguajes: cada color con nombre del
/// lenguaje (Keywords, String, Comment, MethodCall…) va a la familia que le toca; lo demás queda con el color del texto.
/// </summary>
sealed class ThemedColorizer(IHighlightingDefinition definition, EditorInk ink) : HighlightingColorizer(definition)
{
    protected override void ApplyColorToElement(VisualLineElement element, HighlightingColor color)
    {
        var name = color.Name ?? "";
        IBrush? brush =
            name.Contains("Comment", StringComparison.OrdinalIgnoreCase) || name.Contains("Preprocessor", StringComparison.OrdinalIgnoreCase) ? ink.Comment
            : name.Contains("String", StringComparison.OrdinalIgnoreCase) || name.Contains("Char", StringComparison.OrdinalIgnoreCase) ? ink.String
            : name.Contains("Number", StringComparison.OrdinalIgnoreCase) || name.Contains("Digit", StringComparison.OrdinalIgnoreCase) ? ink.Number
            : name.Contains("Method", StringComparison.OrdinalIgnoreCase) || name.Contains("Function", StringComparison.OrdinalIgnoreCase) ? ink.Function
            : name.Contains("Keyword", StringComparison.OrdinalIgnoreCase) || name.Contains("Modifier", StringComparison.OrdinalIgnoreCase)
              || name.Contains("Visibility", StringComparison.OrdinalIgnoreCase) || name.Contains("TrueFalse", StringComparison.OrdinalIgnoreCase)
              || name.Contains("Null", StringComparison.OrdinalIgnoreCase) || name.Contains("This", StringComparison.OrdinalIgnoreCase)
              || name.Contains("Tag", StringComparison.OrdinalIgnoreCase) ? ink.Keyword
            : name.Contains("Attribute", StringComparison.OrdinalIgnoreCase) || name.Contains("Type", StringComparison.OrdinalIgnoreCase) ? ink.Function
            : null;
        if (brush is not null) element.TextRunProperties.SetForegroundBrush(brush);
    }
}

/// <summary>Las marcas de Markdown de una línea: dónde están y qué hacen con el texto que encierran.</summary>
static class MarkdownMarks
{
    public enum Style { Bold, Italic, Code, Link, Heading, Quote }

    /// <summary>Un tramo con estilo (Start..End del texto que se ve) y las marcas que lo rodean (para esconderlas).</summary>
    public readonly record struct Span(int Start, int End, Style Style, int Level, (int Start, int Length)[] Marks);

    static readonly Regex Heading = new(@"^(#{1,6})[ \t]+", RegexOptions.Compiled);
    static readonly Regex Quote = new(@"^>[ \t]?", RegexOptions.Compiled);
    static readonly Regex Bullet = new(@"^([ \t]*)([-*+])[ \t]", RegexOptions.Compiled);
    static readonly Regex Code = new(@"`([^`\n]+)`", RegexOptions.Compiled);
    static readonly Regex Bold = new(@"(\*\*|__)(?=\S)(.+?)(?<=\S)\1", RegexOptions.Compiled);
    static readonly Regex Italic = new(@"(?<![*_\w])([*_])(?=\S)(.+?)(?<=\S)\1(?![*_\w])", RegexOptions.Compiled);
    static readonly Regex Link = new(@"\[([^\]\n]+)\]\(([^)\n]+)\)", RegexOptions.Compiled);

    /// <summary>Los tramos con estilo de una línea, en posiciones de la línea.</summary>
    public static List<Span> Spans(string line)
    {
        var spans = new List<Span>();
        if (Heading.Match(line) is { Success: true } h)
            spans.Add(new(h.Length, line.Length, Style.Heading, h.Groups[1].Length, [(0, h.Length)]));
        else if (Quote.Match(line) is { Success: true } q)
            spans.Add(new(q.Length, line.Length, Style.Quote, 0, [(0, q.Length)]));
        // El código en línea primero: lo de adentro no se interpreta.
        var taken = new bool[line.Length];
        foreach (Match m in Code.Matches(line))
        {
            spans.Add(new(m.Index + 1, m.Index + m.Length - 1, Style.Code, 0, [(m.Index, 1), (m.Index + m.Length - 1, 1)]));
            Array.Fill(taken, true, m.Index, m.Length);
        }
        bool Free(Match m) => !taken.AsSpan(m.Index, m.Length).Contains(true);
        foreach (Match m in Link.Matches(line))
            if (Free(m))
            {
                var text = m.Groups[1];
                spans.Add(new(text.Index, text.Index + text.Length, Style.Link, 0, [(m.Index, 1), (text.Index + text.Length, m.Length - text.Length - 1)]));
                Array.Fill(taken, true, m.Index, m.Length);
            }
        foreach (Match m in Bold.Matches(line))
            if (Free(m))
            {
                var mark = m.Groups[1].Length;
                spans.Add(new(m.Index + mark, m.Index + m.Length - mark, Style.Bold, 0, [(m.Index, mark), (m.Index + m.Length - mark, mark)]));
                Array.Fill(taken, true, m.Index, mark);
                Array.Fill(taken, true, m.Index + m.Length - mark, mark);
            }
        foreach (Match m in Italic.Matches(line))
            if (Free(m)) spans.Add(new(m.Index + 1, m.Index + m.Length - 1, Style.Italic, 0, [(m.Index, 1), (m.Index + m.Length - 1, 1)]));
        return spans;
    }

    /// <summary>La viñeta de una lista («- », «* » o «+ »): dónde está el signo, o -1.</summary>
    public static int BulletAt(string line) => Bullet.Match(line) is { Success: true } b ? b.Groups[2].Index : -1;
}

/// <summary>
/// El aspecto de Markdown en el modo Documento: títulos grandes, negritas, cursivas, código en línea y enlaces, con
/// las marcas tenues en la línea donde está el cursor (en las demás las esconde MarkdownHider).
/// </summary>
sealed class MarkdownLook(TextArea area, Ink ink, EditorInk colors, double size) : DocumentColorizingTransformer
{
    static readonly double[] HeadingScale = [0, 1.75, 1.3, 1.15, 1.05, 1, 1];

    protected override void ColorizeLine(DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line);
        var caretLine = area.Caret.Line == line.LineNumber;
        foreach (var span in MarkdownMarks.Spans(text))
        {
            if (span.End > span.Start)
                ChangeLinePart(line.Offset + span.Start, line.Offset + span.End, element =>
                {
                    var p = element.TextRunProperties;
                    switch (span.Style)
                    {
                        case MarkdownMarks.Style.Heading:
                            p.SetFontRenderingEmSize(size * HeadingScale[span.Level]);
                            p.SetTypeface(new Typeface(Ink.Display, FontStyle.Normal, FontWeight.SemiBold));
                            break;
                        case MarkdownMarks.Style.Bold:
                            p.SetTypeface(new Typeface(p.Typeface.FontFamily, p.Typeface.Style, FontWeight.Bold));
                            break;
                        case MarkdownMarks.Style.Italic:
                            p.SetTypeface(new Typeface(p.Typeface.FontFamily, FontStyle.Italic, p.Typeface.Weight));
                            break;
                        case MarkdownMarks.Style.Code:
                            p.SetTypeface(new Typeface(Ink.Mono));
                            p.SetFontRenderingEmSize(size * 0.86);
                            p.SetBackgroundBrush(ink.Pill);
                            break;
                        case MarkdownMarks.Style.Link:
                            p.SetForegroundBrush(ink.Accent);
                            break;
                        case MarkdownMarks.Style.Quote:
                            p.SetForegroundBrush(ink.Secondary);
                            p.SetTypeface(new Typeface(p.Typeface.FontFamily, FontStyle.Italic, p.Typeface.Weight));
                            break;
                    }
                });
            // En la línea del cursor las marcas se ven, tenues; en las demás no están (MarkdownHider).
            if (caretLine)
                foreach (var (start, length) in span.Marks)
                    if (length > 0) ChangeLinePart(line.Offset + start, line.Offset + start + length, e => e.TextRunProperties.SetForegroundBrush(colors.Marks));
        }
        if (caretLine && MarkdownMarks.BulletAt(text) is var bullet and >= 0)
            ChangeLinePart(line.Offset + bullet, line.Offset + bullet + 1, e => e.TextRunProperties.SetForegroundBrush(colors.Marks));
    }
}

/// <summary>Esconde las marcas de Markdown fuera de la línea del cursor y cambia la viñeta de las listas por «•».</summary>
sealed class MarkdownHider(TextArea area) : VisualLineElementGenerator
{
    /// <summary>Las marcas (y viñetas) de la línea que se está armando: se calculan una vez por línea.</summary>
    int _line = -1;
    List<(int Start, int Length, bool Bullet)> _marks = [];

    List<(int Start, int Length, bool Bullet)> MarksOf(DocumentLine line)
    {
        if (_line == line.LineNumber) return _marks;
        _line = line.LineNumber;
        _marks = [];
        if (area.Caret.Line == line.LineNumber) return _marks;
        var text = CurrentContext.Document.GetText(line);
        foreach (var span in MarkdownMarks.Spans(text))
            foreach (var (start, length) in span.Marks)
                if (length > 0) _marks.Add((line.Offset + start, length, false));
        if (MarkdownMarks.BulletAt(text) is var bullet and >= 0) _marks.Add((line.Offset + bullet, 1, true));
        _marks.Sort((a, b) => a.Start.CompareTo(b.Start));
        return _marks;
    }

    public override void StartGeneration(ITextRunConstructionContext context)
    {
        base.StartGeneration(context);
        _line = -1;
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        var line = CurrentContext.VisualLine.LastDocumentLine;
        foreach (var mark in MarksOf(CurrentContext.Document.GetLineByOffset(startOffset)))
            if (mark.Start >= startOffset) return mark.Start;
        return -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        foreach (var mark in MarksOf(CurrentContext.Document.GetLineByOffset(offset)))
            if (mark.Start == offset)
                return mark.Bullet ? new FormattedTextElement("•", mark.Length) { } : new FormattedTextElement("", mark.Length);
        return null;
    }
}
