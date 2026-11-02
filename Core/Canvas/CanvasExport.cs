using System.Net;
using System.Text;
using System.Text.Json;
using Markdig;

namespace Claupet;

/// <summary>
/// Un canvas hacia afuera: para un modelo (ForAgent, con los ids de cada sección), Markdown, una página HTML que se abre
/// sola en cualquier navegador (con su HTML aislado y los diagramas dibujados) y el JSON tal cual.
/// </summary>
static class CanvasExport
{
    public static readonly string[] Formats = ["markdown", "html", "json", "pdf", "docx"];

    public static string Extension(string format) => format switch { "html" => ".html", "json" => ".json", "pdf" => ".pdf", "docx" => ".docx", _ => ".md" };

    /// <summary>PDF y Word son archivos, no texto: solo se guardan (Save), no se devuelven.</summary>
    public static bool IsFile(string format) => format is "pdf" or "docx";

    /// <summary>Guarda el canvas en ese formato (PDF con Edge o Chrome, Word con Open XML; los demás, como texto UTF-8).</summary>
    public static void Save(string format, CanvasDoc doc, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        switch (format)
        {
            case "pdf": CanvasPdf.Write(doc, path); break;
            case "docx": CanvasDocx.Write(doc, path); break;
            default: File.WriteAllText(path, To(format, doc), new UTF8Encoding(false)); break;
        }
    }

    public static string To(string format, CanvasDoc doc) => format switch
    {
        "html" => Html(doc),
        "json" => JsonSerializer.Serialize(doc, CanvasJson.Options),
        "markdown" or "md" => Markdown(doc),
        "pdf" or "docx" => throw new CanvasException(L.T("{0} se guarda como archivo: diga dónde (path).", format.ToUpperInvariant())),
        _ => throw new CanvasException(L.T("Formato «{0}» no válido; puede ser: {1}.", format, string.Join(", ", Formats))),
    };

    /// <summary>
    /// Lo que lee un modelo: cada sección entre etiquetas con su id, tipo y lenguaje, el contenido sin tocar, y los
    /// comentarios abiertos. Así puede citar el texto exacto en un «edit» y decir dónde insertar.
    /// </summary>
    public static string ForAgent(CanvasDoc doc)
    {
        var sb = new StringBuilder();
        sb.Append($"<canvas id=\"{doc.Id}\" version=\"{doc.Version}\" title=\"{Attr(doc.Title)}\" updated=\"{doc.Updated:O}\">\n");
        if (doc.Sections.Count == 0) sb.Append("(empty: add sections with canvas_edit op \"insert\")\n");
        foreach (var s in doc.Sections)
        {
            sb.Append($"<section id=\"{s.Id}\" kind=\"{s.Kind}\"");
            if (s.Language is { } lang) sb.Append($" language=\"{Attr(lang)}\"");
            if (s.Title is { } title) sb.Append($" title=\"{Attr(title)}\"");
            if (s.Author is { } author) sb.Append($" author=\"{Attr(author)}\"");
            sb.Append(">\n").Append(s.Content);
            if (!s.Content.EndsWith('\n')) sb.Append('\n');
            sb.Append("</section>\n");
        }
        var open = doc.Comments.Where(c => !c.Resolved).ToList();
        if (open.Count > 0)
        {
            sb.Append("<comments>\n");
            foreach (var c in open)
            {
                sb.Append($"<comment id=\"{c.Id}\" section=\"{c.SectionId}\" author=\"{Attr(CanvasJson.AuthorLabel(c.Author))}\"");
                if (c.ParentId is { } parent) sb.Append($" parent=\"{parent}\"");
                if (c.Quote is { } quote) sb.Append($" quote=\"{Attr(quote)}\"");
                sb.Append('>').Append(c.Text).Append("</comment>\n");
            }
            sb.Append("</comments>\n");
        }
        return sb.Append("</canvas>").ToString();
    }

    static string Attr(string value) => value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace("\n", " ");

    public static string Markdown(CanvasDoc doc)
    {
        var sb = new StringBuilder($"# {doc.Title}\n\n");
        foreach (var s in doc.Sections)
        {
            if (s.Title is { } title && s.Kind != CanvasKinds.Markdown) sb.Append($"### {title}\n\n");
            sb.Append(s.Kind switch
            {
                CanvasKinds.Markdown => s.Content.TrimEnd(),
                CanvasKinds.Image => $"![{s.Title ?? ""}]({s.Content.Trim()})",
                _ => Fence(FenceLanguage(s), s.Content),
            }).Append("\n\n");
        }
        return sb.ToString().TrimEnd() + "\n";
    }

    static string FenceLanguage(CanvasSection s) => s.Kind switch
    {
        CanvasKinds.Diagram => s.Language ?? "mermaid",
        CanvasKinds.Html => "html",
        CanvasKinds.Svg => "svg",
        CanvasKinds.Data => s.Language ?? "json",
        CanvasKinds.Plan => "json",
        _ => s.Language ?? "",
    };

    /// <summary>Un bloque de código con una cerca más larga que cualquier racha de «`» de adentro.</summary>
    static string Fence(string language, string content)
    {
        var longest = 0;
        for (int i = 0, run = 0; i < content.Length; i++) longest = Math.Max(longest, run = content[i] == '`' ? run + 1 : 0);
        var fence = new string('`', Math.Max(3, longest + 1));
        return $"{fence}{language}\n{content.TrimEnd('\n')}\n{fence}";
    }

    static MarkdownPipeline? _pipeline;
    static MarkdownPipeline Pipeline => _pipeline ??= new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

    /// <summary>
    /// Una página que se abre sola. El HTML y el SVG de las secciones van en un iframe aislado (sandbox: sin acceso a la
    /// página ni a los archivos), los diagramas Mermaid se dibujan con su script (necesita internet), y el Markdown no
    /// deja pasar HTML crudo.
    /// </summary>
    public static string Html(CanvasDoc doc, string? only = null)
    {
        var sections = only is null ? doc.Sections : doc.Sections.Where(s => s.Id == only).ToList();
        var body = new StringBuilder();
        var mermaid = false;
        var frames = false;
        foreach (var s in sections)
        {
            body.Append($"<section id=\"{s.Id}\" class=\"{s.Kind}\">");
            if (s.Title is { } title && s.Kind != CanvasKinds.Markdown) body.Append($"<h3>{E(title)}</h3>");
            switch (s.Kind)
            {
                case CanvasKinds.Markdown:
                    body.Append(Markdig.Markdown.ToHtml(s.Content, Pipeline));
                    break;
                case CanvasKinds.Html:
                case CanvasKinds.Svg:
                    var page = s.Kind == CanvasKinds.Svg ? $"<!doctype html><body style=\"margin:0\">{s.Content}</body>" : s.Content;
                    body.Append($"<iframe sandbox=\"allow-scripts allow-forms allow-modals allow-popups\" srcdoc=\"{WebUtility.HtmlEncode(page + FrameHeight)}\"></iframe>");
                    frames = true;
                    break;
                case CanvasKinds.Diagram when (s.Language ?? "mermaid").Equals("mermaid", StringComparison.OrdinalIgnoreCase):
                    mermaid = true;
                    body.Append($"<pre class=\"mermaid\">{E(s.Content)}</pre>");
                    break;
                case CanvasKinds.Image:
                    var src = s.Content.Trim();
                    if (src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || src.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                        body.Append($"<img src=\"{WebUtility.HtmlEncode(src)}\" alt=\"{E(s.Title ?? "")}\">");
                    else if (File.Exists(src))
                        body.Append($"<img src=\"{WebUtility.HtmlEncode(new Uri(src).AbsoluteUri)}\" alt=\"{E(s.Title ?? "")}\">");
                    else body.Append($"<p class=\"missing\">{E(src)}</p>");
                    break;
                case CanvasKinds.Data when (s.Language ?? "").Equals("csv", StringComparison.OrdinalIgnoreCase):
                    body.Append(CsvTable(s.Content));
                    break;
                default:
                    body.Append($"<pre><code class=\"language-{E(FenceLanguage(s))}\">{E(s.Content)}</code></pre>");
                    break;
            }
            body.Append("</section>\n");
        }
        var script = mermaid
            ? "<script type=\"module\">import mermaid from 'https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs';"
              + "mermaid.initialize({startOnLoad:true,theme:matchMedia('(prefers-color-scheme: dark)').matches?'dark':'default'});</script>"
            : "";
        if (frames) script += FrameListener;
        return $$"""
            <!doctype html>
            <html lang="{{L.Language}}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="generator" content="Claupet Canvas">
            <title>{{E(doc.Title)}}</title>
            {{Style}}
            </head>
            <body>
            <h1>{{E(doc.Title)}}</h1>
            {{body}}{{script}}
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Dentro de cada iframe: avisa a la página su alto (el del contenido), para que el marco mida eso y no deje un hueco
    /// (ni en pantalla ni en el PDF). El iframe sigue aislado: solo manda un número.
    /// </summary>
    const string FrameHeight = "<script>(function(){function h(){var b=document.body;if(!b)return;var m=parseFloat(getComputedStyle(b).marginBottom)||0;"
        + "parent.postMessage({claupetHeight:Math.ceil(b.getBoundingClientRect().bottom+m)},'*')}addEventListener('load',h);"
        + "if(window.ResizeObserver)addEventListener('DOMContentLoaded',function(){new ResizeObserver(h).observe(document.body)})})()</script>";

    const string FrameListener = "<script>addEventListener('message',function(e){var n=e.data&&e.data.claupetHeight;if(typeof n!=='number')return;"
        + "for(var f of document.querySelectorAll('iframe'))if(f.contentWindow===e.source){f.style.height=Math.max(40,Math.min(n+2,4000))+'px';break}})</script>";

    const string Style = """
        <style>
        :root{color-scheme:light dark;--bg:#F8F6F1;--fg:#1F1E1D;--muted:#6B6963;--line:#14000000;--code:#ECE9E1}
        @media (prefers-color-scheme: dark){:root{--bg:#262624;--fg:#F5F4EF;--muted:#9A988F;--code:#2F2F2C}}
        body{background:var(--bg);color:var(--fg);font:16px/1.6 "Segoe UI",system-ui,-apple-system,sans-serif;max-width:860px;margin:0 auto;padding:40px 24px}
        h1{font-size:2em;margin:0 0 .6em}section{margin:0 0 28px}h3{font-size:1em;color:var(--muted);margin:0 0 8px}
        pre{background:var(--code);padding:14px 16px;border-radius:10px;overflow:auto;font:13.5px/1.5 "Cascadia Mono",Consolas,monospace}
        code{font-family:"Cascadia Mono",Consolas,monospace}p code{background:var(--code);padding:1px 5px;border-radius:5px}
        iframe{display:block;width:100%;height:420px;border:1px solid rgba(127,127,127,.25);border-radius:10px;background:#fff}
        img{max-width:100%;border-radius:10px}table{border-collapse:collapse}td,th{border:1px solid rgba(127,127,127,.3);padding:6px 10px}
        pre.mermaid{background:none;text-align:center}blockquote{margin:0 0 1em;padding:2px 0 2px 14px;border-left:3px solid #D97757;color:var(--muted)}.missing{color:var(--muted)}
        @page{size:A4;margin:18mm 16mm}
        @media print{:root{--bg:#fff;--fg:#1F1E1D;--muted:#6B6963;--code:#F1EEE6}body{max-width:none;padding:0;font-size:11.5pt}
        section{break-inside:avoid-page}pre{white-space:pre-wrap;overflow:visible}h1{font-size:22pt}}
        </style>
        """;

    static string CsvTable(string csv)
    {
        var sb = new StringBuilder("<table>");
        var first = true;
        foreach (var line in csv.ReplaceLineEndings("\n").Split('\n').Where(l => l.Length > 0).Take(2000))
        {
            var tag = first ? "th" : "td";
            sb.Append("<tr>");
            foreach (var cell in SplitCsv(line)) sb.Append($"<{tag}>{E(cell)}</{tag}>");
            sb.Append("</tr>");
            first = false;
        }
        return sb.Append("</table>").ToString();
    }

    /// <summary>Una línea de CSV en celdas (con comillas para las que llevan comas).</summary>
    public static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { cells.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(c);
        }
        cells.Add(cell.ToString());
        return cells;
    }

    static string E(string text) => WebUtility.HtmlEncode(text);
}
