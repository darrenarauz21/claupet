using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Claupet;

/// <summary>
/// Un canvas en Word (.docx), escrito directo en Open XML (sin bibliotecas): el título, el Markdown con su formato
/// (títulos, negrita, cursiva, código, enlaces, listas, citas, tablas, líneas), el código y los datos en letra de ancho
/// fijo, las tablas CSV como tablas de Word y las imágenes (archivo o data URI) incrustadas. Lo que solo se ve en un
/// navegador (HTML, SVG, diagramas) va como su código, con su tipo de título.
/// </summary>
static class CanvasDocx
{
    const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    /// <summary>El ancho útil de una página A4 con márgenes de 2 cm, en EMU (para achicar las imágenes grandes).</summary>
    const long MaxImageWidth = 6_120_000;

    public static void Write(CanvasDoc doc, string path)
    {
        var writer = new Writer();
        writer.Paragraph(writer.Runs(doc.Title), "Title");
        foreach (var section in doc.Sections) writer.Section(section);
        var temp = path + ".claupet-tmp";
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                Entry(zip, "[Content_Types].xml", ContentTypes(writer));
                Entry(zip, "_rels/.rels", $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/></Relationships>""");
                Entry(zip, "docProps/core.xml", $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><dc:title>{X(doc.Title)}</dc:title><dc:creator>Claupet Canvas</dc:creator><dcterms:created xsi:type="dcterms:W3CDTF">{doc.Created.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}</dcterms:created><dcterms:modified xsi:type="dcterms:W3CDTF">{doc.Updated.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}</dcterms:modified></cp:coreProperties>""");
                Entry(zip, "word/document.xml", writer.Document());
                Entry(zip, "word/styles.xml", Styles);
                Entry(zip, "word/numbering.xml", Numbering);
                Entry(zip, "word/_rels/document.xml.rels", writer.Relationships());
                foreach (var (name, data) in writer.Media)
                {
                    using var stream = zip.CreateEntry("word/media/" + name, CompressionLevel.NoCompression).Open();
                    stream.Write(data);
                }
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    static void Entry(ZipArchive zip, string name, string xml)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        using var text = new StreamWriter(stream, new UTF8Encoding(false));
        text.Write(xml);
    }

    static string ContentTypes(Writer writer)
    {
        var defaults = writer.Media.Select(m => Path.GetExtension(m.Name).TrimStart('.')).Distinct()
            .Select(ext => $"""<Default Extension="{ext}" ContentType="image/{(ext == "jpg" ? "jpeg" : ext)}"/>""");
        return $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>{string.Concat(defaults)}<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/><Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/><Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/></Types>""";
    }

    /// <summary>Texto para XML: escapado y sin los caracteres de control que Word no acepta.</summary>
    static string X(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c < 0x20 && c is not ('\t' or '\n' or '\r')) continue;
            sb.Append(c switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", '"' => "&quot;", _ => c.ToString() });
        }
        return sb.ToString();
    }

    /// <summary>Arma el cuerpo del documento, sus relaciones (enlaces e imágenes) y sus imágenes.</summary>
    sealed class Writer
    {
        readonly StringBuilder _body = new();
        readonly List<(string Id, string Xml)> _rels = [];
        public List<(string Name, byte[] Data)> Media { get; } = [];
        int _images;
        static MarkdownPipeline? _pipeline;
        static MarkdownPipeline Pipeline => _pipeline ??= new MarkdownPipelineBuilder().UsePipeTables().UseGridTables().UseTaskLists().UseAutoLinks().UseEmphasisExtras().Build();

        string Rel(string type, string target, bool external)
        {
            var id = "rId" + (_rels.Count + 10);
            _rels.Add((id, $"""<Relationship Id="{id}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/{type}" Target="{X(target)}"{(external ? " TargetMode=\"External\"" : "")}/>"""));
            return id;
        }

        public string Relationships() =>
            $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>{string.Concat(_rels.Select(r => r.Xml))}</Relationships>""";

        public string Document() =>
            $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="{W}" xmlns:r="{R}" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><w:body>{_body}<w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1134" w:right="1134" w:bottom="1134" w:left="1134" w:header="567" w:footer="567" w:gutter="0"/></w:sectPr></w:body></w:document>""";

        public void Paragraph(string runs, string? style = null, string? extra = null) =>
            _body.Append("<w:p>").Append(style is null && extra is null ? "" : $"<w:pPr>{(style is null ? "" : $"<w:pStyle w:val=\"{style}\"/>")}{extra}</w:pPr>").Append(runs).Append("</w:p>");

        public string Runs(string text, bool bold = false, bool italic = false, bool code = false, bool strike = false, string? style = null)
        {
            if (text.Length == 0) return "";
            var props = new StringBuilder();
            if (style is not null) props.Append($"<w:rStyle w:val=\"{style}\"/>");
            if (bold) props.Append("<w:b/>");
            if (italic) props.Append("<w:i/>");
            if (strike) props.Append("<w:strike/>");
            if (code) props.Append("<w:rStyle w:val=\"CodeChar\"/>");
            var rPr = props.Length > 0 ? $"<w:rPr>{props}</w:rPr>" : "";
            var lines = text.Split('\n');
            var sb = new StringBuilder();
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append($"<w:r>{rPr}<w:br/></w:r>");
                if (lines[i].Length > 0) sb.Append($"<w:r>{rPr}<w:t xml:space=\"preserve\">{X(lines[i].TrimEnd('\r'))}</w:t></w:r>");
            }
            return sb.ToString();
        }

        public void Section(CanvasSection s)
        {
            if (s.Title is { } title && s.Kind != CanvasKinds.Markdown) Paragraph(Runs(title), "Heading3");
            switch (s.Kind)
            {
                case CanvasKinds.Markdown:
                    foreach (var block in Markdown.Parse(s.Content, Pipeline)) Block(block, 0);
                    break;
                case CanvasKinds.Data when (s.Language ?? "").Equals("csv", StringComparison.OrdinalIgnoreCase):
                    var rows = s.Content.ReplaceLineEndings("\n").Split('\n').Where(l => l.Length > 0).Take(2000).Select(CanvasExport.SplitCsv).ToList();
                    if (rows.Count > 0) Table(rows.Select(r => r.Select(c => Runs(c)).ToList()).ToList());
                    break;
                case CanvasKinds.Image:
                    if (!Image(s.Content.Trim(), s.Title)) Paragraph(Runs(s.Content.Trim(), italic: true));
                    break;
                default:
                    Code(s.Content);
                    break;
            }
        }

        void Code(string text)
        {
            foreach (var line in text.TrimEnd('\n').ReplaceLineEndings("\n").Split('\n'))
                Paragraph(line.Length == 0 ? "" : Runs(line), "Code");
            Spacer();
        }

        /// <summary>Un renglón corto en blanco: separa un bloque con fondo (código, tabla) del que sigue.</summary>
        void Spacer() => Paragraph("", null, "<w:spacing w:after=\"0\" w:line=\"200\" w:lineRule=\"exact\"/>");

        // ── Markdown ────────────────────────────────────────────────────────────────────

        void Block(Block block, int depth, string? listStyle = null)
        {
            switch (block)
            {
                case HeadingBlock h:
                    Paragraph(Inlines(h.Inline), "Heading" + Math.Clamp(h.Level, 1, 4));
                    break;
                case ParagraphBlock p:
                    Paragraph(Inlines(p.Inline), listStyle);
                    break;
                case ListBlock list:
                    var number = list.IsOrdered ? 2 : 1;
                    foreach (var item in list.OfType<ListItemBlock>())
                    {
                        var first = true;
                        foreach (var child in item)
                        {
                            if (child is ListBlock) { Block(child, depth + 1); continue; }
                            if (first && child is ParagraphBlock para)
                            {
                                var task = para.Inline?.FirstChild is TaskList t ? (t.Checked ? "☑ " : "☐ ") : "";
                                Paragraph(Runs(task) + Inlines(para.Inline), "ListParagraph", $"<w:numPr><w:ilvl w:val=\"{Math.Min(depth, 2)}\"/><w:numId w:val=\"{number}\"/></w:numPr>");
                                first = false;
                            }
                            else Block(child, depth + 1, "ListParagraph");
                        }
                    }
                    break;
                case QuoteBlock quote:
                    foreach (var child in quote) Block(child, depth, "Quote");
                    break;
                case Table table:
                    Table(table.OfType<TableRow>().Select(row => row.OfType<TableCell>()
                        .Select(cell => string.Concat(cell.OfType<ParagraphBlock>().Select(p => Inlines(p.Inline, bold: row.IsHeader)))).ToList()).ToList());
                    break;
                case FencedCodeBlock or CodeBlock:
                    Code(((LeafBlock)block).Lines.ToString());
                    break;
                case HtmlBlock html:
                    Code(html.Lines.ToString());
                    break;
                case ThematicBreakBlock:
                    Paragraph("", null, "<w:pBdr><w:bottom w:val=\"single\" w:sz=\"6\" w:space=\"1\" w:color=\"D9D6CC\"/></w:pBdr>");
                    break;
                case ContainerBlock container:
                    foreach (var child in container) Block(child, depth, listStyle);
                    break;
                case LeafBlock { Inline: { } inline }:
                    Paragraph(Inlines(inline), listStyle);
                    break;
            }
        }

        string Inlines(ContainerInline? container, bool bold = false, bool italic = false, bool strike = false)
        {
            if (container is null) return "";
            var sb = new StringBuilder();
            foreach (var inline in container) sb.Append(Inline(inline, bold, italic, strike));
            return sb.ToString();
        }

        string Inline(Inline inline, bool bold, bool italic, bool strike) => inline switch
        {
            LiteralInline literal => Runs(literal.Content.ToString(), bold, italic, strike: strike),
            CodeInline code => Runs(code.Content, bold, italic, code: true),
            LineBreakInline => "<w:r><w:br/></w:r>",
            TaskList => "",
            EmphasisInline e when e.DelimiterChar is '~' => Inlines(e, bold, italic, strike: true),
            EmphasisInline e => e.DelimiterCount >= 2 ? Inlines(e, bold: true, italic, strike) : Inlines(e, bold, italic: true, strike),
            LinkInline { IsImage: true } image => Image(image.Url ?? "", null) ? "" : Runs(image.Url ?? "", italic: true),
            LinkInline link when Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto" =>
                $"<w:hyperlink r:id=\"{Rel("hyperlink", uri.AbsoluteUri, external: true)}\">{Restyle(Inlines(link, bold, italic, strike))}</w:hyperlink>",
            AutolinkInline auto when Uri.TryCreate(auto.Url, UriKind.Absolute, out var uri) =>
                $"<w:hyperlink r:id=\"{Rel("hyperlink", uri.AbsoluteUri, external: true)}\">{Restyle(Runs(auto.Url))}</w:hyperlink>",
            HtmlInline html => Runs(html.Tag, code: true),
            ContainerInline container => Inlines(container, bold, italic, strike),
            _ => "",
        };

        /// <summary>Los pedazos de un enlace llevan el estilo «Hyperlink» (azul y subrayado).</summary>
        static string Restyle(string runs) => Regex.Replace(runs, "<w:r>(<w:rPr>)?", m => m.Groups[1].Success ? "<w:r><w:rPr><w:rStyle w:val=\"Hyperlink\"/>" : "<w:r><w:rPr><w:rStyle w:val=\"Hyperlink\"/></w:rPr>");

        void Table(List<List<string>> rows)
        {
            var columns = rows.Max(r => r.Count);
            if (columns == 0) return;
            var width = 9638 / columns;
            var sb = new StringBuilder("<w:tbl><w:tblPr><w:tblStyle w:val=\"CanvasTable\"/><w:tblW w:w=\"0\" w:type=\"auto\"/><w:tblLook w:val=\"04A0\" w:firstRow=\"1\"/></w:tblPr><w:tblGrid>");
            for (var c = 0; c < columns; c++) sb.Append($"<w:gridCol w:w=\"{width}\"/>");
            sb.Append("</w:tblGrid>");
            for (var r = 0; r < rows.Count; r++)
            {
                sb.Append(r == 0 ? "<w:tr><w:trPr><w:tblHeader/></w:trPr>" : "<w:tr>");
                for (var c = 0; c < columns; c++)
                {
                    var content = c < rows[r].Count ? rows[r][c] : "";
                    var shade = r == 0 ? "<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"ECE9E1\"/>" : "";
                    var runs = r == 0 && !content.Contains("<w:b/>") ? content.Replace("<w:r>", "<w:r><w:rPr><w:b/></w:rPr>").Replace("<w:rPr><w:b/></w:rPr><w:rPr>", "<w:rPr><w:b/>") : content;
                    sb.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{width}\" w:type=\"dxa\"/>{shade}</w:tcPr><w:p><w:pPr><w:pStyle w:val=\"TableText\"/></w:pPr>{runs}</w:p></w:tc>");
                }
                sb.Append("</w:tr>");
            }
            sb.Append("</w:tbl>");
            _body.Append(sb);
            Spacer();
        }

        // ── Imágenes ────────────────────────────────────────────────────────────────────

        /// <summary>Incrusta una imagen PNG, JPEG o GIF (data URI o archivo); false si no se pudo (una URL web queda como texto).</summary>
        bool Image(string source, string? alt)
        {
            byte[]? data = null;
            try
            {
                if (source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) && source.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase) is var at and > 0)
                    data = Convert.FromBase64String(source[(at + 8)..]);
                else if (Path.IsPathFullyQualified(source) && File.Exists(source))
                    data = File.ReadAllBytes(source);
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException) { return false; }
            if (data is null || Size(data) is not { } size) return false;
            var ext = size.Ext;
            var name = $"imagen{++_images}.{ext}";
            Media.Add((name, data));
            var id = Rel("image", "media/" + name, external: false);
            // De píxeles a EMU (96 ppp), achicada al ancho de la página si hace falta.
            long cx = size.Width * 9525L, cy = size.Height * 9525L;
            if (cx > MaxImageWidth) { cy = cy * MaxImageWidth / cx; cx = MaxImageWidth; }
            var drawing = $"""<w:r><w:drawing><wp:inline distT="0" distB="0" distL="0" distR="0"><wp:extent cx="{cx}" cy="{cy}"/><wp:docPr id="{_images}" name="{X(alt ?? name)}"/><a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic><pic:nvPicPr><pic:cNvPr id="{_images}" name="{name}"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed="{id}"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="{cx}" cy="{cy}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r>""";
            Paragraph(drawing);
            return true;
        }

        /// <summary>El tamaño en píxeles leyendo la cabecera (PNG, GIF o JPEG); null si no es ninguno.</summary>
        static (int Width, int Height, string Ext)? Size(byte[] d)
        {
            if (d.Length > 24 && d[0] == 0x89 && d[1] == 'P' && d[2] == 'N' && d[3] == 'G')
                return ((d[16] << 24) | (d[17] << 16) | (d[18] << 8) | d[19], (d[20] << 24) | (d[21] << 16) | (d[22] << 8) | d[23], "png");
            if (d.Length > 10 && d[0] == 'G' && d[1] == 'I' && d[2] == 'F')
                return (d[6] | (d[7] << 8), d[8] | (d[9] << 8), "gif");
            if (d.Length > 4 && d[0] == 0xFF && d[1] == 0xD8)
            {
                var i = 2;
                while (i + 9 < d.Length)
                {
                    if (d[i] != 0xFF) { i++; continue; }
                    var marker = d[i + 1];
                    var length = (d[i + 2] << 8) | d[i + 3];
                    if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                        return ((d[i + 7] << 8) | d[i + 8], (d[i + 5] << 8) | d[i + 6], "jpg");
                    i += 2 + length;
                }
            }
            return null;
        }
    }

    // ── Estilos: los de Claupet (letra Segoe UI, código en Cascadia Mono, el acento en los títulos) ──

    const string Styles = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:docDefaults>
            <w:rPrDefault><w:rPr><w:rFonts w:ascii="Segoe UI" w:hAnsi="Segoe UI" w:eastAsia="Segoe UI" w:cs="Segoe UI"/><w:sz w:val="22"/><w:szCs w:val="22"/><w:color w:val="1F1E1D"/><w:lang w:val="es-ES"/></w:rPr></w:rPrDefault>
            <w:pPrDefault><w:pPr><w:spacing w:after="120" w:line="300" w:lineRule="auto"/></w:pPr></w:pPrDefault>
          </w:docDefaults>
          <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style>
          <w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/><w:pPr><w:spacing w:after="240"/></w:pPr><w:rPr><w:rFonts w:ascii="Segoe UI Semibold" w:hAnsi="Segoe UI Semibold"/><w:sz w:val="52"/><w:szCs w:val="52"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before="360" w:after="120"/><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:rFonts w:ascii="Segoe UI Semibold" w:hAnsi="Segoe UI Semibold"/><w:sz w:val="36"/><w:szCs w:val="36"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before="280" w:after="100"/><w:outlineLvl w:val="1"/></w:pPr><w:rPr><w:rFonts w:ascii="Segoe UI Semibold" w:hAnsi="Segoe UI Semibold"/><w:sz w:val="28"/><w:szCs w:val="28"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Heading3"><w:name w:val="heading 3"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before="220" w:after="80"/><w:outlineLvl w:val="2"/></w:pPr><w:rPr><w:rFonts w:ascii="Segoe UI Semibold" w:hAnsi="Segoe UI Semibold"/><w:color w:val="B4532F"/><w:sz w:val="24"/><w:szCs w:val="24"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Heading4"><w:name w:val="heading 4"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/><w:pPr><w:keepNext/><w:outlineLvl w:val="3"/></w:pPr><w:rPr><w:b/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="ListParagraph"><w:name w:val="List Paragraph"/><w:basedOn w:val="Normal"/><w:pPr><w:spacing w:after="60"/><w:ind w:left="720"/></w:pPr></w:style>
          <w:style w:type="paragraph" w:styleId="Quote"><w:name w:val="Quote"/><w:basedOn w:val="Normal"/><w:pPr><w:pBdr><w:left w:val="single" w:sz="18" w:space="10" w:color="D97757"/></w:pBdr><w:ind w:left="360"/></w:pPr><w:rPr><w:i/><w:color w:val="5E5D59"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Code"><w:name w:val="Code"/><w:basedOn w:val="Normal"/><w:pPr><w:shd w:val="clear" w:color="auto" w:fill="F1EEE6"/><w:spacing w:after="0" w:line="260" w:lineRule="auto"/><w:ind w:left="120" w:right="120"/></w:pPr><w:rPr><w:rFonts w:ascii="Cascadia Mono" w:hAnsi="Cascadia Mono" w:cs="Consolas"/><w:sz w:val="18"/><w:szCs w:val="18"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="TableText"><w:name w:val="Table Text"/><w:basedOn w:val="Normal"/><w:pPr><w:spacing w:after="0"/></w:pPr><w:rPr><w:sz w:val="20"/></w:rPr></w:style>
          <w:style w:type="character" w:styleId="CodeChar"><w:name w:val="Code Char"/><w:rPr><w:rFonts w:ascii="Cascadia Mono" w:hAnsi="Cascadia Mono" w:cs="Consolas"/><w:sz w:val="19"/><w:shd w:val="clear" w:color="auto" w:fill="ECE9E1"/></w:rPr></w:style>
          <w:style w:type="character" w:styleId="Hyperlink"><w:name w:val="Hyperlink"/><w:rPr><w:color w:val="2F6DB5"/><w:u w:val="single"/></w:rPr></w:style>
          <w:style w:type="table" w:styleId="CanvasTable"><w:name w:val="Canvas Table"/><w:tblPr><w:tblBorders><w:top w:val="single" w:sz="4" w:color="D9D6CC"/><w:left w:val="single" w:sz="4" w:color="D9D6CC"/><w:bottom w:val="single" w:sz="4" w:color="D9D6CC"/><w:right w:val="single" w:sz="4" w:color="D9D6CC"/><w:insideH w:val="single" w:sz="4" w:color="D9D6CC"/><w:insideV w:val="single" w:sz="4" w:color="D9D6CC"/></w:tblBorders><w:tblCellMar><w:top w:w="60" w:type="dxa"/><w:left w:w="120" w:type="dxa"/><w:bottom w:w="60" w:type="dxa"/><w:right w:w="120" w:type="dxa"/></w:tblCellMar></w:tblPr></w:style>
        </w:styles>
        """;

    /// <summary>Viñetas (numId 1) y números (numId 2), con tres niveles de sangría.</summary>
    const string Numbering = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:abstractNum w:abstractNumId="0">
            <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="•"/><w:lvlJc w:val="left"/><w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr><w:rPr><w:color w:val="D97757"/></w:rPr></w:lvl>
            <w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="◦"/><w:lvlJc w:val="left"/><w:pPr><w:ind w:left="1440" w:hanging="360"/></w:pPr><w:rPr><w:color w:val="D97757"/></w:rPr></w:lvl>
            <w:lvl w:ilvl="2"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="▪"/><w:lvlJc w:val="left"/><w:pPr><w:ind w:left="2160" w:hanging="360"/></w:pPr><w:rPr><w:color w:val="D97757"/></w:rPr></w:lvl>
          </w:abstractNum>
          <w:abstractNum w:abstractNumId="1">
            <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/><w:lvlJc w:val="left"/><w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr></w:lvl>
            <w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="lowerLetter"/><w:lvlText w:val="%2."/><w:lvlJc w:val="left"/><w:pPr><w:ind w:left="1440" w:hanging="360"/></w:pPr></w:lvl>
            <w:lvl w:ilvl="2"><w:start w:val="1"/><w:numFmt w:val="lowerRoman"/><w:lvlText w:val="%3."/><w:lvlJc w:val="left"/><w:pPr><w:ind w:left="2160" w:hanging="360"/></w:pPr></w:lvl>
          </w:abstractNum>
          <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
          <w:num w:numId="2"><w:abstractNumId w:val="1"/></w:num>
        </w:numbering>
        """;
}
