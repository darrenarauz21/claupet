namespace Claupet;

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

/// <summary>
/// Los textos largos en la Console (Terminal y chat), sin cargar con ellos enteros:
/// - Pegados en una caja entran como una marca («[Texto #1 · 240 líneas]») que vuelve a ser el texto al enviar.
/// - Un pedido, un mensaje o un bloque de código largo se ve plegado a su comienzo, con «Mostrar todo» y «Mostrar menos»;
///   desplegado se dibujan como mucho ShownChars (el resto se copia con Copiar).
/// - Una respuesta que todavía llega se ve por su final (LiveText).
/// Acomodar en pantalla un texto de megas costaba segundos y cientos de MB cada vez.
/// </summary>
static class LongText
{
    /// <summary>Desde cuántas líneas o caracteres un texto pegado entra como marca y no entero.</summary>
    public const int PasteLines = 15, PasteChars = 1500;
    /// <summary>Lo más que se dibuja de un texto desplegado.</summary>
    public const int ShownChars = 200_000;
    /// <summary>Mientras llega una respuesta, desde cuántos caracteres se ve solo su final, y cuánto.</summary>
    public const int LiveTailFrom = 8_000, LiveTail = 6_000;

    public static bool IsLongPaste(string text) => text.Length > PasteChars || LineCount(text, PasteLines + 1) > PasteLines;

    /// <summary>Cuántas líneas tiene, contando como mucho hasta stop (para no recorrer un texto enorme).</summary>
    public static int LineCount(string text, int stop = int.MaxValue)
    {
        var lines = 1;
        for (var at = text.IndexOf('\n'); at >= 0 && lines < stop; at = text.IndexOf('\n', at + 1)) lines++;
        return lines;
    }

    public static string PasteMarker(int number, string text) => LineCount(text) is var lines and > 1
        ? L.T("[Texto #{0} · {1} líneas]", number, lines.ToString("N0"))
        : L.T("[Texto #{0} · {1} caracteres]", number, text.Length.ToString("N0"));

    /// <summary>Cambia cada marca que sigue en el texto por lo pegado; las que se borraron no van.</summary>
    public static string ExpandPastes(string text, IReadOnlyList<(string Marker, string Text)> pastes)
    {
        foreach (var (marker, pasted) in pastes)
            if (text.Contains(marker, StringComparison.Ordinal)) text = text.Replace(marker, pasted, StringComparison.Ordinal);
        return text;
    }

    /// <summary>El comienzo de un texto largo: sus primeras líneas, sin pasar de chars, con « …». null si es corto y se ve entero.</summary>
    public static string? Preview(string text, int lines, int chars)
    {
        if (text.Length <= chars && LineCount(text, lines + 1) <= lines) return null;
        var end = -1;
        for (var line = 0; line < lines; line++)
        {
            var next = text.IndexOf('\n', end + 1);
            if (next < 0 || next > chars) { end = -1; break; }
            end = next;
        }
        var cut = end >= 0 ? end : Math.Min(text.Length, chars);
        return text[..cut].TrimEnd() + " …";
    }

    /// <summary>Lo que se dibuja de un texto desplegado: entero, o hasta ShownChars con una nota de cuánto sigue.</summary>
    public static string Shown(string text) => text.Length <= ShownChars ? text
        : text[..ShownChars] + "\n…\n" + L.T("(Siguen {0} caracteres más: Copiar lleva el texto entero).", (text.Length - ShownChars).ToString("N0"));

    /// <summary>
    /// Una respuesta mientras llega: entera si es corta; si no, su final (LiveTail caracteres, desde un comienzo de línea),
    /// que es lo que se está leyendo. Al terminar se dibuja entera.
    /// </summary>
    public static string LiveText(string text)
    {
        if (text.Length <= LiveTailFrom) return text;
        var from = text.Length - LiveTail;
        var line = text.IndexOf('\n', from);
        if (line >= 0 && line < text.Length - 1) from = line + 1;
        return "…\n" + text[from..];
    }

    /// <summary>
    /// Un texto que se pliega si es largo: make dibuja lo que se ve (el comienzo, o entero hasta ShownChars) y abajo va
    /// «Mostrar todo · N líneas» o «Mostrar menos». open y toggled guardan si quedó desplegado (para cuando se vuelva a
    /// dibujar). Si es corto, solo make con el texto entero.
    /// </summary>
    public static Control Foldable(string full, int lines, int chars, IBrush accent, Func<string, Control> make, bool open = false, Action<bool>? toggled = null)
    {
        if (Preview(full, lines, chars) is not { } preview) return make(full);
        var column = new StackPanel { Spacing = 4 };
        void Fill()
        {
            column.Children.Clear();
            column.Children.Add(make(open ? Shown(full) : preview));
            var count = LineCount(full);
            var label = Ui.Text(open ? L.T("Mostrar menos")
                : count > 1 ? L.T("Mostrar todo · {0} líneas", count.ToString("N0")) : L.T("Mostrar todo · {0} caracteres", full.Length.ToString("N0")),
                12, accent);
            var toggle = Ui.Press(label, () =>
            {
                open = !open;
                toggled?.Invoke(open);
                Fill();
            }, (b, on) => label.TextDecorations = on ? TextDecorations.Underline : null, label.Text);
            toggle.HorizontalAlignment = HorizontalAlignment.Left;
            column.Children.Add(toggle);
        }
        Fill();
        return column;
    }
}
