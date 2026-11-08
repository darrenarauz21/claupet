using System.Text;

namespace Claupet.Tests;

/// <summary>
/// El PDF de un canvas por partes (cada sección HTML o SVG, su propia página), la unión de los PDF de Edge o Chrome
/// (PdfJoin) y la página de una sección viva dentro del panel.
/// </summary>
public sealed class CanvasPdfTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "claupet-canvas-pdf-" + Guid.NewGuid().ToString("N"));
    readonly CanvasStore _store;

    public CanvasPdfTests() => _store = new CanvasStore(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    static CanvasOp Insert(string kind, string content) => new("insert", Kind: kind, Content: content);

    // ── Las partes ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void El_pdf_va_por_partes_y_cada_html_o_svg_es_su_propia_pagina()
    {
        var doc = _store.Create("Partes", null, [Insert("markdown", "uno"), Insert("html", "<h1>Diseño</h1>"), Insert("markdown", "dos"), Insert("svg", "<svg></svg>")]);
        var parts = CanvasPdf.Parts(doc);
        Assert.Equal(4, parts.Count);
        Assert.Contains("<h1>Partes</h1>", parts[0]);
        Assert.Contains("<p>uno</p>", parts[0]);
        // El HTML va tal cual, sin iframe, con sus fondos y en A4.
        Assert.StartsWith("<h1>Diseño</h1>", parts[1]);
        Assert.DoesNotContain("<iframe", parts[1]);
        Assert.Contains("print-color-adjust:exact", parts[1]);
        Assert.Contains("@page{size:A4}", parts[1]);
        // El título del canvas, solo una vez, al principio.
        Assert.DoesNotContain("<h1>Partes</h1>", parts[2]);
        Assert.Contains("<p>dos</p>", parts[2]);
        Assert.Contains("svg{max-width:100%", parts[3]);
    }

    [Fact]
    public void Sin_html_el_pdf_es_una_sola_pagina_como_antes()
    {
        var doc = _store.Create("Texto", null, [Insert("markdown", "a"), Insert("code", "b")]);
        var part = Assert.Single(CanvasPdf.Parts(doc));
        Assert.Contains("<h1>Texto</h1>", part);
        Assert.Single(CanvasPdf.Parts(_store.Create("Vacío", null, [])));
    }

    [Fact]
    public void Al_imprimir_las_secciones_no_saltan_enteras_de_hoja_y_los_diagramas_van_en_claro()
    {
        var doc = _store.Create("Diagrama", null, [Insert("diagram", "graph TD; A-->B")]);
        var print = CanvasExport.Html(doc, print: true);
        Assert.DoesNotContain("section{break-inside:avoid-page}", print);
        Assert.Contains("break-after:avoid-page", print);
        Assert.Contains("theme:'default'", print);
        Assert.Contains("prefers-color-scheme: dark", CanvasExport.Html(doc));
    }

    // ── La unión ────────────────────────────────────────────────────────────────────────

    const string Binary = "x\u0000ÿ endobj 9 0 R ( stream\n\u0001";

    /// <summary>Un PDF como los de Skia: un objeto tras otro, la tabla clásica y el trailer.</summary>
    static byte[] Pdf(string[] objects, int? info = null, bool streamXref = false)
    {
        var latin1 = Encoding.Latin1;
        using var output = new MemoryStream();
        void Write(string s) => output.Write(latin1.GetBytes(s));
        Write("%PDF-1.4\n%âãÏÓ\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(output.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = output.Position;
        if (streamXref) Write($"{objects.Length + 1} 0 obj\n<</Type /XRef>>\nstream\nendstream\nendobj\n");
        else
        {
            Write($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
            foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
            Write($"trailer\n<</Size {objects.Length + 1} /Root 1 0 R{(info is { } n ? $" /Info {n} 0 R" : "")}>>\n");
        }
        Write($"startxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    static byte[] First() => Pdf(
    [
        "<</Type /Catalog /Pages 2 0 R /StructTreeRoot 6 0 R>>",
        "<</Type /Pages /Count 1 /Kids [3 0 R]>>",
        "<</Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources <</Font <</F1 5 0 R>>>> /Contents 4 0 R /Annots [7 0 R] /StructParents 0>>",
        $"<</Length {Binary.Length}>>\nstream\n{Binary}\nendstream",
        "<</Type /Font /Subtype /Type1 /BaseFont /Helvetica>>",
        "<</Type /StructTreeRoot /K [3 0 R]>>",
        "<</Type /Annot /Subtype /Link /Rect [0 0 1 1] /A <</S /URI /URI (http://x.y/3 0 R \\) 4 0 R)>>>>",
        "<</Title (Uno) /Producer (Skia/PDF)>>",
    ], info: 8);

    /// <summary>Con un árbol de páginas de dos niveles.</summary>
    static byte[] Second() => Pdf(
    [
        "<</Type /Catalog /Pages 2 0 R>>",
        "<</Type /Pages /Count 2 /Kids [3 0 R]>>",
        "<</Type /Pages /Parent 2 0 R /Count 2 /Kids [4 0 R 5 0 R]>>",
        "<</Type /Page /Parent 3 0 R /MediaBox [0 0 595 842] /Resources <<>> /Contents 6 0 R>>",
        "<</Type /Page /Parent 3 0 R /MediaBox [0 0 595 842] /Resources <<>> /Contents 6 0 R>>",
        "<</Length 3>>\nstream\nabc\nendstream",
    ]);

    [Fact]
    public void Une_los_pdf_en_orden_con_las_paginas_de_todos()
    {
        var joined = PdfJoin.Join([First(), Second()]);
        Assert.Equal(3, PdfJoin.PageCount(joined)); // y se vuelve a leer: la tabla y los objetos están donde dicen
        var text = Encoding.Latin1.GetString(joined);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        // Los datos del flujo, byte a byte, y los textos sin tocar (aunque parezcan referencias).
        Assert.Contains(Binary, text);
        Assert.Contains("(http://x.y/3 0 R \\) 4 0 R)", text);
        // Las páginas cuelgan del árbol nuevo; el catálogo y las etiquetas de cada parte quedan afuera.
        Assert.Equal(3, text.Split("/Parent 2 0 R").Length - 1);
        Assert.DoesNotContain("StructTreeRoot", text);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, @"/Type /Catalog"));
        Assert.Contains("/Title (Uno)", text);
        Assert.Matches(@"trailer\n<</Size \d+ /Root 1 0 R /Info \d+ 0 R>>", text);
    }

    [Fact]
    public void Un_pdf_con_otra_forma_se_avisa_para_hacerlo_de_otra_manera()
    {
        Assert.Throws<PdfJoinException>(() => PdfJoin.Join([First(), Pdf(["<</Type /Catalog /Pages 2 0 R>>"], streamXref: true)]));
        Assert.Throws<PdfJoinException>(() => PdfJoin.Join([Encoding.ASCII.GetBytes("no es un pdf")]));
        // Una página que hereda su tamaño del árbol: no se une (se perdería).
        var inherits = Pdf(["<</Type /Catalog /Pages 2 0 R>>", "<</Type /Pages /Count 1 /Kids [3 0 R] /MediaBox [0 0 1 1]>>", "<</Type /Page /Parent 2 0 R /Resources <<>>>>"]);
        Assert.Throws<PdfJoinException>(() => PdfJoin.Join([First(), inherits]));
    }

    [Fact]
    public void Con_el_navegador_el_html_va_aparte_y_el_pdf_se_une()
    {
        if (CanvasPdf.FindBrowser() is null) return; // sin Edge ni Chrome no hay con qué imprimir
        var doc = _store.Create("Unido", null, [Insert("markdown", "# Antes"), Insert("html", "<!doctype html><body style=\"background:#123\"><h1 style=\"color:#fff\">Diseño</h1></body>"), Insert("markdown", "Después")]);
        var path = Path.Combine(_root, "unido.pdf");
        CanvasPdf.Write(doc, path);
        var bytes = File.ReadAllBytes(path);
        Assert.Contains("/Type /Catalog /Pages 2 0 R", Encoding.Latin1.GetString(bytes, 0, 200)); // lo unió PdfJoin
        Assert.Equal(3, PdfJoin.PageCount(bytes));
    }

    // ── La sección viva ─────────────────────────────────────────────────────────────────

    [Fact]
    public void La_pagina_viva_aisla_la_seccion_y_solo_pasa_su_alto_y_la_rueda()
    {
        var doc = _store.Create("Viva", null, [Insert("html", "<p>\"hola\" & <b>chau</b></p>")]);
        var page = CanvasExport.Live(doc.Sections[0]);
        Assert.Contains("sandbox=\"allow-scripts allow-forms allow-modals allow-popups\"", page);
        Assert.DoesNotContain("allow-same-origin", page);
        Assert.Contains("&lt;p&gt;&quot;hola&quot; &amp; &lt;b&gt;chau&lt;/b&gt;&lt;/p&gt;", page);
        Assert.Contains("claupetHeight", page);
        Assert.Contains("claupetWheel", page);
        Assert.Contains("chrome.webview", page);
    }
}
