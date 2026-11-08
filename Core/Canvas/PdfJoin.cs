using System.Text;
using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>
/// Une varios PDF en uno, en orden, sin bibliotecas: solo los que escribe Edge o Chrome al imprimir (Skia: PDF 1.4 con
/// su tabla de referencias clásica, sin objetos comprimidos ni cifrado). Con otra forma avisa (PdfJoinException) y el
/// PDF se hace de otra manera. Copia los objetos con otros números (los flujos, byte a byte), junta las páginas en un
/// árbol nuevo y deja afuera lo que nadie usa: el catálogo de cada parte y sus etiquetas de accesibilidad.
/// </summary>
static class PdfJoin
{
    static readonly Encoding Latin1 = Encoding.Latin1;
    /// <summary>Una referencia «N G R» fuera de un texto.</summary>
    static readonly Regex Reference = new(@"(?<![\w.+\-])(\d+)\s+(\d+)\s+R(?![\w])", RegexOptions.Compiled);
    static readonly Regex Parent = new(@"/Parent\s+\d+\s+\d+\s+R", RegexOptions.Compiled);

    /// <summary>Un PDF leído: su texto (Latin-1, un carácter por byte), dónde empieza y termina cada objeto, y su raíz.</summary>
    sealed record Part(byte[] Bytes, string Text, Dictionary<int, (int Start, int End)> Objects, int Root, int? Info);

    public static byte[] Join(IReadOnlyList<byte[]> pdfs)
    {
        if (pdfs.Count == 0) throw new PdfJoinException("No hay nada que unir.");
        try { return JoinCore(pdfs); }
        catch (Exception ex) when (ex is FormatException or OverflowException or IndexOutOfRangeException or ArgumentException or KeyNotFoundException)
        {
            throw new PdfJoinException("PDF con otra forma: " + ex.Message, ex);
        }
    }

    /// <summary>Cuántas páginas tiene un PDF de esta forma (para las pruebas y para revisar lo unido).</summary>
    public static int PageCount(byte[] pdf) => Pages(Read(pdf)).Count;

    static byte[] JoinCore(IReadOnlyList<byte[]> pdfs)
    {
        var parts = pdfs.Select(Read).ToList();
        var pages = parts.Select(Pages).ToList();

        // Los números nuevos: 1 el catálogo, 2 el árbol de páginas, y después lo que se alcanza desde las páginas (y los
        // datos del documento de la primera parte: título, quién lo hizo).
        var numbers = new Dictionary<(int Part, int Number), int>();
        var keep = new List<(int Part, int Number)>();
        var queue = new Queue<(int Part, int Number)>();
        var next = 3;
        void Visit(int part, int number)
        {
            if (!parts[part].Objects.ContainsKey(number) || !numbers.TryAdd((part, number), next)) return;
            next++;
            keep.Add((part, number));
            queue.Enqueue((part, number));
        }
        for (var i = 0; i < parts.Count; i++)
            foreach (var page in pages[i]) Visit(i, page);
        if (parts[0].Info is { } info) Visit(0, info);
        var isPage = pages.SelectMany((list, i) => list.Select(n => (i, n))).ToHashSet();
        while (queue.Count > 0)
        {
            var (part, number) = queue.Dequeue();
            var head = Head(parts[part], number, isPage.Contains((part, number)));
            foreach (var target in OutsideStrings(head).SelectMany(span => Reference.Matches(span).Select(m => int.Parse(m.Groups[1].Value))))
                Visit(part, target);
        }

        using var output = new MemoryStream();
        void Write(string text) => output.Write(Latin1.GetBytes(text));
        var offsets = new long[next];
        Write("%PDF-1.4\n%âãÏÓ\n");
        offsets[1] = output.Position;
        Write("1 0 obj\n<</Type /Catalog /Pages 2 0 R>>\nendobj\n");
        offsets[2] = output.Position;
        var kids = string.Join(" ", pages.SelectMany((list, i) => list.Select(n => numbers[(i, n)] + " 0 R")));
        Write($"2 0 obj\n<</Type /Pages /Count {pages.Sum(p => p.Count)} /Kids [{kids}]>>\nendobj\n");
        foreach (var (part, number) in keep)
        {
            var p = parts[part];
            var page = isPage.Contains((part, number));
            var head = Renumber(Head(p, number, page), old => numbers.TryGetValue((part, old), out var n) ? n : null);
            // La página cuelga del árbol nuevo.
            if (page) head = Inside(head, "/Parent 2 0 R ");
            offsets[numbers[(part, number)]] = output.Position;
            Write($"{numbers[(part, number)]} 0 obj");
            Write(head);
            var (bodyStart, end) = Body(p, number);
            var data = StreamData(p.Text, bodyStart, end);
            if (data >= 0) output.Write(p.Bytes, data, end - data); // el flujo y su «endstream endobj», tal cual
            if (output.Length > 0 && output.GetBuffer()[output.Length - 1] != (byte)'\n') Write("\n");
        }
        var xref = output.Position;
        var sb = new StringBuilder($"xref\n0 {next}\n0000000000 65535 f \n");
        for (var i = 1; i < next; i++) sb.Append($"{offsets[i]:D10} 00000 n \n");
        sb.Append($"trailer\n<</Size {next} /Root 1 0 R");
        if (parts[0].Info is { } first && numbers.TryGetValue((0, first), out var infoNumber)) sb.Append($" /Info {infoNumber} 0 R");
        sb.Append($">>\nstartxref\n{xref}\n%%EOF\n");
        Write(sb.ToString());
        return output.ToArray();
    }

    // ── Leer ────────────────────────────────────────────────────────────────────────────

    static Part Read(byte[] pdf)
    {
        var text = Latin1.GetString(pdf);
        if (!text.StartsWith("%PDF-", StringComparison.Ordinal)) throw new FormatException("no es un PDF");
        var startxref = text.LastIndexOf("startxref", StringComparison.Ordinal);
        if (startxref < 0) throw new FormatException("sin startxref");
        var xref = int.Parse(Regex.Match(text[(startxref + 9)..], @"^\s*(\d+)").Groups[1].Value);
        if (string.CompareOrdinal(text, xref, "xref", 0, 4) != 0) throw new FormatException("tabla de referencias comprimida");
        var trailerAt = text.IndexOf("trailer", xref, StringComparison.Ordinal);
        if (trailerAt < 0 || trailerAt > startxref) throw new FormatException("sin trailer");
        var trailer = text[trailerAt..startxref];
        if (trailer.Contains("/Prev") || trailer.Contains("/Encrypt")) throw new FormatException("con varias tablas o cifrado");

        var offsets = new Dictionary<int, int>();
        var tokens = text[(xref + 4)..trailerAt].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length;)
        {
            var first = int.Parse(tokens[i]);
            var count = int.Parse(tokens[i + 1]);
            i += 2;
            for (var k = 0; k < count; k++, i += 3)
            {
                if (tokens[i + 2] != "n") continue;
                if (int.Parse(tokens[i + 1]) != 0) throw new FormatException("objetos con otra generación");
                offsets[first + k] = int.Parse(tokens[i]);
            }
        }
        // Cada objeto llega hasta donde empieza el siguiente (o la tabla): así los flujos no hace falta leerlos.
        var sorted = offsets.OrderBy(o => o.Value).ToList();
        var objects = new Dictionary<int, (int Start, int End)>();
        for (var i = 0; i < sorted.Count; i++)
        {
            var (number, start) = (sorted[i].Key, sorted[i].Value);
            var end = i + 1 < sorted.Count ? sorted[i + 1].Value : xref;
            var header = Regex.Match(text[start..Math.Min(end, start + 32)], @"^(\d+)\s+0\s+obj");
            if (!header.Success || int.Parse(header.Groups[1].Value) != number) throw new FormatException($"el objeto {number} no está donde dice la tabla");
            objects[number] = (start, end);
        }
        var root = Ref(trailer, "Root") ?? throw new FormatException("sin catálogo");
        return new Part(pdf, text, objects, root, Ref(trailer, "Info"));
    }

    /// <summary>Las páginas, en orden, recorriendo el árbol desde el catálogo. Cada una trae su tamaño y sus recursos.</summary>
    static List<int> Pages(Part part)
    {
        var list = new List<int>();
        var catalog = Head(part, part.Root, false);
        Walk(Ref(catalog, "Pages") ?? throw new FormatException("sin páginas"), 0);
        return list;

        void Walk(int number, int depth)
        {
            if (depth > 32) throw new FormatException("árbol de páginas demasiado hondo");
            var node = Head(part, number, false);
            if (Regex.IsMatch(node, @"/Type\s*/Pages\b"))
            {
                var kids = Regex.Match(node, @"/Kids\s*\[([^\]]*)\]");
                if (!kids.Success) throw new FormatException("páginas sin hijas");
                foreach (Match kid in Reference.Matches(kids.Groups[1].Value)) Walk(int.Parse(kid.Groups[1].Value), depth + 1);
            }
            else if (Regex.IsMatch(node, @"/Type\s*/Page\b"))
            {
                // Lo heredado del árbol se perdería: solo páginas que traen lo suyo.
                if (!node.Contains("/MediaBox") || !node.Contains("/Resources")) throw new FormatException("página que hereda su tamaño o sus recursos");
                list.Add(number);
            }
            else throw new FormatException($"el objeto {number} no es una página");
        }
    }

    static int? Ref(string dict, string key)
    {
        var m = Regex.Match(dict, $@"/{key}\s+(\d+)\s+(\d+)\s+R");
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    /// <summary>Lo que va después de «N 0 obj» hasta el final del objeto.</summary>
    static (int Start, int End) Body(Part part, int number)
    {
        var (start, end) = part.Objects[number];
        var obj = part.Text.IndexOf("obj", start, StringComparison.Ordinal);
        return (obj + 3, end);
    }

    /// <summary>
    /// El objeto sin los datos de su flujo (si tiene): lo único donde puede haber referencias. En una página, sin su
    /// «/Parent» (va el árbol nuevo).
    /// </summary>
    static string Head(Part part, int number, bool page)
    {
        var (start, end) = Body(part, number);
        var data = StreamData(part.Text, start, end);
        var head = part.Text[start..(data < 0 ? end : data)];
        return page ? Parent.Replace(head, "", 1) : head;
    }

    /// <summary>Dónde empiezan los datos de un flujo (después de «stream» y su salto de línea), o -1 si no tiene.</summary>
    static int StreamData(string text, int from, int to)
    {
        for (var i = from; i < to; i++)
        {
            var c = text[i];
            if (c == '(') i = StringEnd(text, i, to);
            else if (c == '%') while (i + 1 < to && text[i + 1] is not ('\n' or '\r')) i++;
            else if (c == 's' && i + 6 <= to && string.CompareOrdinal(text, i, "stream", 0, 6) == 0 && !char.IsLetter(text[i - 1]))
            {
                var j = i + 6;
                if (j < to && text[j] == '\r') j++;
                if (j < to && text[j] == '\n') j++;
                if (j > i + 6) return j;
            }
        }
        return -1;
    }

    /// <summary>El «)» que cierra el texto que abre el «(» de esa posición (con paréntesis dentro y «\» que escapa).</summary>
    static int StringEnd(string text, int open, int to)
    {
        var depth = 0;
        for (var i = open; i < to; i++)
        {
            var c = text[i];
            if (c == '\\') i++;
            else if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return i;
        }
        return to;
    }

    /// <summary>Los tramos fuera de los textos «(…)»: ahí van las referencias.</summary>
    static IEnumerable<string> OutsideStrings(string text)
    {
        var last = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '(') continue;
            yield return text[last..i];
            i = StringEnd(text, i, text.Length);
            last = Math.Min(i + 1, text.Length);
        }
        yield return text[last..];
    }

    /// <summary>Cambia las referencias por los números nuevos, sin tocar los textos; las que no van, a «null».</summary>
    static string Renumber(string text, Func<int, int?> map)
    {
        var sb = new StringBuilder(text.Length + 16);
        var last = 0;
        void Outside(int to) => sb.Append(Reference.Replace(text[last..to], m => map(int.Parse(m.Groups[1].Value)) is { } n ? $"{n} 0 R" : "null"));
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '(') continue;
            Outside(i);
            var close = StringEnd(text, i, text.Length);
            sb.Append(text, i, Math.Min(close + 1, text.Length) - i);
            i = close;
            last = Math.Min(close + 1, text.Length);
        }
        Outside(text.Length);
        return sb.ToString();
    }

    /// <summary>Agrega algo al principio del diccionario del objeto (después de su primer «&lt;&lt;»).</summary>
    static string Inside(string head, string entry)
    {
        var at = head.IndexOf("<<", StringComparison.Ordinal);
        if (at < 0) throw new FormatException("página sin diccionario");
        return head.Insert(at + 2, entry);
    }
}

/// <summary>Un PDF que PdfJoin no sabe unir (otra forma que la de Edge o Chrome).</summary>
sealed class PdfJoinException(string message, Exception? inner = null) : Exception(message, inner);
