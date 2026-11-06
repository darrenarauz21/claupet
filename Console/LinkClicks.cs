namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;

/// <summary>Lo común a los enlaces de un texto (Terminal y Chat): su azul, cómo se parte la línea y cómo responden al mouse.</summary>
static class LinkClicks
{
    static readonly IBrush OnDark = new ImmutableSolidColorBrush(Color.FromRgb(0x7A, 0xB7, 0xFF));
    static readonly IBrush OnLight = new ImmutableSolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xEB));
    static Cursor? _hand;

    /// <summary>El azul de los enlaces, legible sobre ese fondo (claro u oscuro).</summary>
    public static IBrush Brush(bool dark) => dark ? OnDark : OnLight;

    public static Cursor Hand => _hand ??= new Cursor(StandardCursorType.Hand);

    /// <summary>Una línea en pedazos: su `código` (sin las comillas invertidas) y sus enlaces, en orden y sin perder texto.</summary>
    public static IEnumerable<(string Text, bool Code, TextLink? Link)> Pieces(string line, IReadOnlyList<TextLink> found)
    {
        var offset = 0;
        foreach (var span in Conversations.Spans(line))
        {
            // Spans le quita las comillas invertidas al código: dónde empieza cada pedazo se busca en la línea.
            var start = line.IndexOf(span.Text, offset, StringComparison.Ordinal);
            if (start < 0) start = offset;
            var end = start + span.Text.Length;
            var cut = start;
            foreach (var link in found.Where(l => l.Start < end && l.Start + l.Length > start))
            {
                var from = Math.Max(link.Start, cut);
                var to = Math.Min(link.Start + link.Length, end);
                if (to <= from) continue;
                if (from > cut) yield return (line[cut..from], span.Code, null);
                yield return (line[from..to], span.Code, link);
                cut = to;
            }
            if (end > cut) yield return (line[cut..end], span.Code, null);
            offset = end;
        }
    }

    /// <summary>Los enlaces de un texto responden al mouse: la mano encima (con su destino al costado) y, con un clic sin arrastrar, se abren.</summary>
    public static void Attach(SelectableTextBlock block, IReadOnlyList<(int Start, int End, TextLink Link)> links, Action<TextLink> open)
    {
        TextLink? shown = null;
        TextLink? At(PointerEventArgs e)
        {
            // Se mide cada enlace, con sus renglones: HitTestPoint solo da IsInside en el primer renglón del párrafo.
            var point = e.GetPosition(block) - new Point(block.Padding.Left, block.Padding.Top);
            foreach (var (start, end, link) in links)
                if (block.TextLayout.HitTestTextRange(start, end - start).Any(r => r.Contains(point))) return link;
            return null;
        }
        block.PointerMoved += (_, e) =>
        {
            var link = At(e);
            if (ReferenceEquals(link, shown)) return;
            shown = link;
            if (link is null) block.ClearValue(InputElement.CursorProperty);
            else block.Cursor = Hand;
            ToolTip.SetTip(block, link is null ? null : link.Kind == LinkKind.Url ? link.Target
                : link.Kind == LinkKind.Folder ? L.T("Abrir la carpeta {0}", link.Target) : L.T("Ver {0}", link.Target));
        };
        block.PointerExited += (_, _) =>
        {
            shown = null;
            block.ClearValue(InputElement.CursorProperty);
            ToolTip.SetTip(block, null);
        };
        block.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            // Un clic, no el final de una selección: si se arrastró, se está copiando.
            if (e.InitialPressMouseButton != MouseButton.Left || block.SelectionStart != block.SelectionEnd) return;
            if (At(e) is { } link) open(link);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }
}
