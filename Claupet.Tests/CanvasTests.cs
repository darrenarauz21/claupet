using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet.Tests;

/// <summary>Claupet Canvas: las operaciones, el guardado con versiones, la exportación y el servidor MCP.</summary>
public sealed class CanvasTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "claupet-canvas-" + Guid.NewGuid().ToString("N"));
    readonly CanvasStore _store;

    public CanvasTests() => _store = new CanvasStore(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    static CanvasOp Insert(string kind, string content, string? after = null, string? language = null) =>
        new("insert", Kind: kind, Content: content, After: after, Language: language);

    [Fact]
    public void Las_secciones_nuevas_llevan_ids_cortos_y_van_donde_se_pide()
    {
        var doc = _store.Create("Plan", "Claude Code", [Insert("markdown", "# Hola"), Insert("code", "print(1)", language: "python")]);
        Assert.Equal(["s1", "s2"], doc.Sections.Select(s => s.Id));
        Assert.Equal(1, doc.Version);

        var applied = _store.Apply(doc.Id, [Insert("markdown", "Arriba", after: "start"), Insert("diagram", "graph TD; A-->B", after: "s1")], "Codex");
        Assert.Equal(["s3", "s1", "s4", "s2"], applied.Doc.Sections.Select(s => s.Id));
        Assert.Equal(2, applied.Doc.Version);
        Assert.Equal("Codex", applied.Doc.Section("s4")!.Author);
    }

    [Fact]
    public void Edit_cambia_el_texto_exacto_y_pide_contexto_si_se_repite()
    {
        var doc = _store.Create("Notas", null, [Insert("markdown", "uno dos uno\r\ntres")]);
        var error = Assert.Throws<CanvasException>(() => _store.Apply(doc.Id, [new("edit", Id: "s1", Find: "uno", Replace: "1")], "x"));
        Assert.Contains("2", error.Message);

        _store.Apply(doc.Id, [new("edit", Id: "s1", Find: "dos uno\ntres", Replace: "DOS")], "x");
        Assert.Equal("uno DOS", _store.Load(doc.Id)!.Section("s1")!.Content);

        _store.Apply(doc.Id, [new("edit", Id: "s1", Find: "o", Replace: "0", All: true)], "x");
        Assert.Equal("un0 DOS", _store.Load(doc.Id)!.Section("s1")!.Content);
    }

    [Fact]
    public void Si_una_operacion_falla_no_se_aplica_ninguna()
    {
        var doc = _store.Create("Atómico", null, [Insert("markdown", "a")]);
        var error = Assert.Throws<CanvasException>(() => _store.Apply(doc.Id, [new("replace", Id: "s1", Content: "b"), new("delete", Id: "s9")], "x"));
        Assert.Contains("Operación 2", error.Message);
        var now = _store.Load(doc.Id)!;
        Assert.Equal("a", now.Section("s1")!.Content);
        Assert.Equal(1, now.Version);
    }

    [Fact]
    public void Cada_cambio_deja_la_version_anterior_y_se_puede_volver()
    {
        var doc = _store.Create("Historia", null, [Insert("markdown", "v1")]);
        _store.Apply(doc.Id, [new("replace", Id: "s1", Content: "v2")], "Gemini CLI");
        _store.Apply(doc.Id, [new("replace", Id: "s1", Content: "v3")], CanvasJson.User);
        Assert.Equal([2, 1], _store.Versions(doc.Id));
        Assert.Equal("v1", _store.LoadVersion(doc.Id, 1)!.Section("s1")!.Content);

        var restored = _store.Restore(doc.Id, 1, CanvasJson.User);
        Assert.Equal(4, restored.Version);
        Assert.Equal("v1", _store.Load(doc.Id)!.Section("s1")!.Content);
        Assert.Equal(["creó el canvas", "reescribió s1", "reescribió s1", "volvió a la versión 1"], _store.Load(doc.Id)!.Changes.Select(c => c.Summary));
    }

    [Fact]
    public void La_version_base_vieja_se_rechaza()
    {
        var doc = _store.Create("Concurrente", null, [Insert("markdown", "a")]);
        _store.Apply(doc.Id, [new("append", Id: "s1", Content: "b")], "uno");
        var error = Assert.Throws<CanvasException>(() => _store.Apply(doc.Id, [new("append", Id: "s1", Content: "c")], "otro", baseVersion: 1));
        Assert.Contains("versión 2", error.Message);
    }

    [Fact]
    public void Los_comentarios_se_responden_y_se_resuelven_y_el_modelo_ve_los_abiertos()
    {
        var doc = _store.Create("Revisión", null, [Insert("markdown", "Texto")]);
        _store.Apply(doc.Id, [new("comment", Section: "s1", Text: "¿Más corto?", Quote: "Texto")], CanvasJson.User);
        var forAgent = CanvasExport.ForAgent(_store.Load(doc.Id)!);
        Assert.Contains("<comment id=\"c1\" section=\"s1\"", forAgent);

        _store.Apply(doc.Id, [new("comment", Section: "s1", Text: "Hecho", Parent: "c1"), new("resolve", Id: "c1")], "Claude Code");
        var after = _store.Load(doc.Id)!;
        Assert.All(after.Comments, c => Assert.True(c.Resolved));
        Assert.DoesNotContain("<comments>", CanvasExport.ForAgent(after));
    }

    [Fact]
    public void Los_ids_raros_no_salen_de_la_carpeta()
    {
        Assert.Throws<CanvasException>(() => _store.Load("..\\..\\secreto"));
        Assert.Throws<CanvasException>(() => _store.Load("a/b"));
    }

    [Fact]
    public void La_exportacion_aisla_el_html_y_no_deja_pasar_html_en_el_markdown()
    {
        var doc = _store.Create("Exportar", null,
        [
            Insert("markdown", "Hola <script>alert(1)</script>"),
            Insert("html", "<h1>App</h1><script>1</script>"),
            Insert("diagram", "graph TD; A-->B", language: "mermaid"),
            Insert("code", "x = ```", language: "python"),
        ]);
        var html = CanvasExport.Html(doc);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("<iframe sandbox=\"allow-scripts", html);
        Assert.DoesNotContain("allow-same-origin", html);
        Assert.Contains("class=\"mermaid\"", html);

        var md = CanvasExport.Markdown(doc);
        Assert.StartsWith("# Exportar", md);
        Assert.Contains("````python\nx = ```\n````", md);
        Assert.Contains("```mermaid", md);
    }

    // 1×1 PNG transparente.
    const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";

    CanvasDoc Rich() => _store.Create("Informe & <prueba>", null,
    [
        Insert("markdown", "# Uno\n\nTexto **negrita**, *cursiva*, `código` y [enlace](https://claupet.dev).\n\n- a\n  - b\n1. c\n\n> cita\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n---\n\n```js\nlet x = 1;\n```"),
        Insert("code", "print('hola')\n\nfin", language: "python"),
        Insert("data", "nombre,valor\n\"a, b\",1\nc,2", language: "csv"),
        Insert("image", Png),
        Insert("html", "<b>x</b>"),
    ]);

    [Fact]
    public void Exporta_a_word_un_docx_valido_con_todo()
    {
        var path = Path.Combine(_root, "salida.docx");
        CanvasExport.Save("docx", Rich(), path);
        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        foreach (var entry in zip.Entries.Where(e => e.Name.EndsWith(".xml") || e.Name.EndsWith(".rels")))
        {
            using var stream = entry.Open();
            System.Xml.Linq.XDocument.Load(stream); // XML bien formado en todas las partes
        }
        string Read(string name) { using var r = new StreamReader(zip.GetEntry(name)!.Open()); return r.ReadToEnd(); }
        var body = Read("word/document.xml");
        Assert.Contains("Informe &amp; &lt;prueba&gt;", body);
        Assert.Contains("w:val=\"Heading1\"", body);
        Assert.Contains("<w:b/>", body);
        Assert.Contains("<w:hyperlink", body);
        Assert.Contains("<w:numId w:val=\"1\"/>", body);
        Assert.Contains("<w:numId w:val=\"2\"/>", body);
        Assert.Contains("w:val=\"Quote\"", body);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(body, "<w:tbl>").Count);
        Assert.Contains("a, b", body);
        Assert.Contains("print(&apos;hola&apos;)".Replace("&apos;", "'"), body);
        Assert.Contains("<a:blip r:embed=", body);
        Assert.NotNull(zip.GetEntry("word/media/imagen1.png"));
        Assert.Contains("https://claupet.dev", Read("word/_rels/document.xml.rels"));
        Assert.Contains("Extension=\"png\"", Read("[Content_Types].xml"));
        if (Environment.GetEnvironmentVariable("CLAUPET_EXPORT_DIR") is { Length: > 0 } dir) File.Copy(path, Path.Combine(dir, "Canvas.docx"), true);
    }

    [Fact]
    public void Exporta_a_pdf_con_el_navegador()
    {
        if (CanvasPdf.FindBrowser() is null) return; // sin Edge ni Chrome no hay con qué imprimir
        var path = Path.Combine(_root, "salida.pdf");
        CanvasExport.Save("pdf", Rich(), path);
        var head = new byte[5];
        using (var file = File.OpenRead(path)) file.ReadExactly(head);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(head));
        Assert.True(new FileInfo(path).Length > 2000);
        if (Environment.GetEnvironmentVariable("CLAUPET_EXPORT_DIR") is { Length: > 0 } dir) File.Copy(path, Path.Combine(dir, "Canvas.pdf"), true);
    }

    [Fact]
    public void Pdf_y_docx_sin_ruta_piden_una()
    {
        var doc = Rich();
        Assert.Throws<CanvasException>(() => CanvasExport.To("pdf", doc));
        var server = new CanvasMcp.Server(_store, null, null);
        var text = ToolText(Ask(server, 1, "tools/call", new { name = "canvas_export", arguments = new { id = doc.Id, format = "docx" } }), error: true);
        Assert.Contains("DOCX", text);
        var path = Path.Combine(_root, "mcp.docx");
        ToolText(Ask(server, 2, "tools/call", new { name = "canvas_export", arguments = new { id = doc.Id, format = "docx", path } }));
        Assert.True(File.Exists(path));
    }

    // ── MCP ─────────────────────────────────────────────────────────────────────────────

    static JsonElement Ask(CanvasMcp.Server server, int id, string method, object? parameters = null)
    {
        var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
        if (parameters is not null) request["params"] = JsonSerializer.SerializeToNode(parameters);
        var reply = server.Handle(request.ToJsonString());
        Assert.NotNull(reply);
        return JsonDocument.Parse(reply).RootElement.Clone();
    }

    static string ToolText(JsonElement reply, bool error = false)
    {
        var result = reply.GetProperty("result");
        Assert.Equal(error, result.GetProperty("isError").GetBoolean());
        return result.GetProperty("content")[0].GetProperty("text").GetString()!;
    }

    [Fact]
    public void El_servidor_mcp_se_presenta_y_lista_sus_herramientas()
    {
        var server = new CanvasMcp.Server(_store, null, null);
        var init = Ask(server, 1, "initialize", new { protocolVersion = "2025-03-26", clientInfo = new { name = "codex-mcp-client", version = "1" } });
        Assert.Equal("2025-03-26", init.GetProperty("result").GetProperty("protocolVersion").GetString());
        Assert.Equal("Codex", server.Author);
        Assert.Null(server.Handle("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));

        var tools = Ask(server, 2, "tools/list").GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Equal(["canvas_list", "canvas_create", "canvas_read", "canvas_edit", "canvas_export", "canvas_open", "plan_list", "plan_read", "plan_create", "plan_update"], tools);
        Assert.Equal(JsonRpcException.MethodNotFound, Ask(server, 3, "nada").GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void Un_agente_crea_lee_y_edita_por_mcp_y_sus_errores_vuelven_como_resultado()
    {
        var server = new CanvasMcp.Server(_store, "Gemini CLI", "C:\\proyecto");
        var created = ToolText(Ask(server, 1, "tools/call", new
        {
            name = "canvas_create",
            arguments = new { title = "Informe", sections = new object[] { new { kind = "markdown", content = "# Informe" }, new { kind = "data", language = "json", content = "{\"a\":1}" } } },
        }));
        Assert.Contains("<section id=\"s2\" kind=\"data\" language=\"json\"", created);
        var id = _store.List().Single().Id;
        Assert.Equal(id, _store.TakeOpenRequest());
        Assert.Equal("C:\\proyecto", _store.Load(id)!.Folder);

        var edited = ToolText(Ask(server, 2, "tools/call", new
        {
            name = "canvas_edit",
            arguments = new { id, ops = new object[] { new { op = "edit", id = "s1", find = "# Informe", replace = "# Informe final" } } },
        }));
        Assert.Contains("version 2", edited);
        Assert.Equal("Gemini CLI", _store.Load(id)!.Section("s1")!.Author);

        var wrong = ToolText(Ask(server, 3, "tools/call", new { name = "canvas_edit", arguments = new { id, ops = new object[] { new { op = "edit", id = "s1", find = "no está", replace = "x" } } } }), error: true);
        Assert.Contains("No encontré", wrong);

        var read = ToolText(Ask(server, 4, "tools/call", new { name = "canvas_read", arguments = new { id } }));
        Assert.Contains("# Informe final", read);
    }

    [Fact]
    public void Los_agentes_reciben_el_servidor_al_abrirse()
    {
        var acp = CanvasMcp.AcpServers("Gemini CLI", "C:\\p").Single()!;
        Assert.Equal(CanvasMcp.Name, acp["name"]!.GetValue<string>());
        Assert.Equal(CanvasMcp.Flag, acp["args"]![0]!.GetValue<string>());

        var codex = CanvasMcp.CodexArguments("C:\\p").ToList();
        Assert.Contains(codex, a => a.StartsWith("mcp_servers.claupet_canvas.command='", StringComparison.Ordinal));
        Assert.DoesNotContain(codex, a => a.Contains('"'));
    }
}
