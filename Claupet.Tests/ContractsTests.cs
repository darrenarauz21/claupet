using System.Text.Json.Nodes;

namespace Claupet.Tests;

/// <summary>Contratos Claupet: el formato, las capas, init, el índice y el comando cpt.</summary>
public class ContractsTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "claupet-contratos-" + Guid.NewGuid().ToString("N"));
    readonly string? _profileBefore = Environment.GetEnvironmentVariable(ContractStore.ProfileVariable);

    public ContractsTests()
    {
        Directory.CreateDirectory(_root);
        // Un perfil vacío y propio, para no leer el de quien corre las pruebas.
        Environment.SetEnvironmentVariable(ContractStore.ProfileVariable, Path.Combine(_root, "perfil"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ContractStore.ProfileVariable, _profileBefore);
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    static ContractDoc Doc(string text, ContractLayer layer = ContractLayer.Proyecto, string path = "x.md") => ContractParser.Parse(text, path, layer);

    const string Api = """
        ---
        id: calidad.api
        tipo: calidad
        titulo: API
        resumen: Cómo se hace una API correcta
        aplica: ["**/api/**", "src/*.cs"]
        etiquetas:
          - api
          - back # comentario
        prioridad: obligatoria
        actualizado: 2026-11-10
        ---

        ## Reglas

        - [errores] Los errores devuelven un cuerpo con código y mensaje. `check: api-errores`
        - [verbos] GET no cambia nada.

        ```md
        - [no-es-regla] dentro de un bloque de código
        ```
        """;

    [Fact]
    public void Parser_lee_cabecera_y_reglas()
    {
        var d = Doc(Api);
        Assert.Empty(d.Issues);
        Assert.Equal("calidad.api", d.Id);
        Assert.Equal(["**/api/**", "src/*.cs"], d.AppliesTo);
        Assert.Equal(["api", "back"], d.Tags);
        Assert.True(d.Required);
        Assert.Equal(2, d.Rules.Count);
        Assert.Equal("Los errores devuelven un cuerpo con código y mensaje.", d.Rules[0].Text);
        Assert.Equal(["api-errores"], d.Rules[0].Checks);
    }

    [Fact]
    public void Parser_avisa_lo_que_falta()
    {
        Assert.Contains(Doc("sin cabecera").Issues, i => i.Severity == ContractSeverity.Error);
        var d = Doc("---\nid: Mal Id\ntipo: raro\n---\n- [a] x\n- [a] y\n");
        var messages = string.Join("\n", d.Issues.Select(i => i.Message));
        Assert.Contains("minúsculas", messages);
        Assert.Contains("«tipo» no es válido", messages);
        Assert.Contains("Falta «titulo»", messages);
        Assert.Contains("repetida", messages);
        // Uno que solo amplía no necesita más que el id.
        Assert.DoesNotContain(Doc("---\nid: calidad.api\nmodo: amplia\n---\n").Issues, i => i.Severity == ContractSeverity.Error);
    }

    [Theory]
    [InlineData("**/*", "a/b/c.cs", true)]
    [InlineData("**/api/**", "src/api/x/y.ts", true)]
    [InlineData("**/api/**", "api/y.ts", true)]
    [InlineData("src/*.cs", "src/a.cs", true)]
    [InlineData("src/*.cs", "src/x/a.cs", false)]
    [InlineData("**/*.tsx", "./app/Page.TSX", true)]
    [InlineData("a?.md", "ab.md", true)]
    [InlineData("a?.md", "a/.md", false)]
    public void Glob(string pattern, string path, bool match) => Assert.Equal(match, ContractGlob.IsMatch(pattern, path));

    [Fact]
    public void Capas_amplian_reemplazan_y_apagan()
    {
        var builtIn = new[] { Doc(Api, ContractLayer.Claupet), Doc(Api.Replace("calidad.api", "calidad.otro"), ContractLayer.Claupet) };
        var profile = new[] { Doc("---\nid: calidad.api\nmodo: amplia\netiquetas: [rest]\napaga_reglas: [verbos]\n---\n- [errores] Cambiada.\n- [nueva] Otra.\n", ContractLayer.Perfil) };
        var project = new[] { Doc("---\nid: calidad.otro\nmodo: apaga\n---\n", ContractLayer.Proyecto) };
        var set = ContractStore.Resolve(builtIn, profile, project, null, null);

        var api = set.Find("calidad.api")!;
        Assert.Equal(["errores", "verbos", "nueva"], api.Rules.Select(r => r.Id));
        Assert.Equal("Cambiada.", api.Rules[0].Text);
        Assert.Equal(ContractLayer.Perfil, api.Rules[0].Layer);
        Assert.True(api.Rules[1].Off);
        Assert.Equal(["errores", "nueva"], api.ActiveRules.Select(r => r.Id));
        Assert.Contains("rest", api.Tags);
        Assert.Equal(ContractLayer.Claupet, api.Layer);

        var otro = set.Find("calidad.otro")!;
        Assert.False(otro.Enabled);
        Assert.Equal(ContractLayer.Proyecto, otro.OffBy);
        Assert.Empty(set.Issues);
    }

    [Fact]
    public void Capas_avisan_choques()
    {
        var set = ContractStore.Resolve([Doc(Api, ContractLayer.Claupet)], [], [Doc(Api), Doc("---\nid: calidad.nada\nmodo: amplia\n---\n")], null, null);
        Assert.Contains(set.Issues, i => i.Severity == ContractSeverity.Error && i.Message.Contains("ya existe"));
        Assert.Contains(set.Issues, i => i.Severity == ContractSeverity.Aviso && i.Message.Contains("no hace nada"));

        var replaced = ContractStore.Resolve([Doc(Api, ContractLayer.Claupet)], [], [Doc(Api.Replace("tipo: calidad", "tipo: calidad\nmodo: reemplaza").Replace("- [verbos] GET no cambia nada.", ""))], null, null);
        var api = replaced.Find("calidad.api")!;
        Assert.Equal(ContractLayer.Proyecto, api.Layer);
        Assert.Single(api.Rules);
        Assert.Equal(2, api.Sources.Count);
    }

    [Fact]
    public void Paquetes_de_Claupet_vienen_dentro_y_son_validos()
    {
        var docs = ContractStore.LoadBuiltIn();
        Assert.Contains(docs, d => d.Id == "estilo.escritura");
        Assert.Contains(docs, d => d.Id == "mapa.navegacion");
        Assert.All(docs, d => Assert.Empty(d.Issues));
        // Los paquetes cumplen sus propias reglas: sin emojis ni guion largo.
        Assert.All(docs, d => Assert.DoesNotContain('—', d.Body));
    }

    [Fact]
    public void Init_crea_sin_pisar_y_es_idempotente()
    {
        var project = Path.Combine(_root, "p");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "CLAUDE.md"), "# Mío\n");
        File.WriteAllText(Path.Combine(project, "package.json"), "{}");

        var dry = ContractProject.Init(project, true, true, new DateOnly(2026, 11, 10));
        Assert.Equal(4, dry.Count);
        Assert.False(Directory.Exists(Path.Combine(project, ".claupet")));

        ContractProject.Init(project, true, false, new DateOnly(2026, 11, 10));
        var base_ = File.ReadAllText(Path.Combine(project, ".claupet", "contratos", "base.md"));
        Assert.Contains("Node.js", base_);
        Assert.Contains("`npm test`", base_);
        Assert.Empty(Doc(base_).Issues);
        Assert.False(File.Exists(Path.Combine(project, "AGENTS.md")));
        var claude = File.ReadAllText(Path.Combine(project, "CLAUDE.md"));
        Assert.StartsWith("# Mío\n\n" + ContractProject.PointerStart, claude);

        var again = ContractProject.Init(project, true, false, new DateOnly(2026, 11, 10));
        Assert.Equal(claude, File.ReadAllText(Path.Combine(project, "CLAUDE.md")));
        Assert.Contains(again, s => s.What.Contains("ya tiene el puntero"));
        Assert.Contains(again, s => s.What.Contains("ya existe"));
    }

    [Fact]
    public void Load_encuentra_el_proyecto_subiendo_y_salta_lo_que_no_son_contratos()
    {
        var folder = Path.Combine(_root, "p", ".claupet", "contratos");
        Directory.CreateDirectory(Path.Combine(folder, ".generado"));
        Directory.CreateDirectory(Path.Combine(folder, "cambios"));
        Directory.CreateDirectory(Path.Combine(_root, "p", "src", "hondo"));
        File.WriteAllText(Path.Combine(folder, "api.md"), Api);
        File.WriteAllText(Path.Combine(folder, "api.en.md"), "traducción");
        File.WriteAllText(Path.Combine(folder, "README.md"), "léeme");
        File.WriteAllText(Path.Combine(folder, "cambios", "x.md"), "propuesta");

        var set = ContractStore.Load(Path.Combine(_root, "p", "src", "hondo"));
        Assert.Equal(folder, set.ProjectFolder);
        Assert.NotNull(set.Find("calidad.api"));
        Assert.Empty(set.Issues);

        var path = ContractProject.WriteIndex(set)!;
        var index = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal(1, (int)index["formato"]!);
        Assert.Contains(index["contratos"]!.AsArray(), c => (string)c!["id"]! == "calidad.api" && (string)c["prioridad"]! == "obligatoria");
    }

    [Fact]
    public void Busqueda_sin_tildes_ni_mayusculas()
    {
        var set = ContractStore.Resolve([Doc(Api)], [], [], null, null);
        var hits = ContractSearch.Find(set, "CODIGO");
        Assert.Single(hits);
        Assert.Equal(["errores"], hits[0].Rules.Select(r => r.Id));
        Assert.Empty(ContractSearch.Find(set, "zzz"));
    }

    (int Code, string Out, string Err) Cpt(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = CptCli.Run(args, output, error, _root, "es");
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void Cpt_en_espanol_e_ingles()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        Assert.Equal(0, Cpt("contratos", "iniciar").Code);
        Assert.True(File.Exists(Path.Combine(_root, "AGENTS.md")));

        var list = Cpt("c", "listar");
        Assert.Equal(0, list.Code);
        Assert.Contains("estilo.escritura", list.Out);
        Assert.Equal(list.Out, Cpt("contracts", "list").Out);
        Assert.Contains("Contracts", Cpt("--lang", "en", "contracts", "list").Out);

        Assert.Contains("[sin-emojis]", Cpt("c", "leer", "estilo.escritura").Out);
        Assert.Contains("estilo.escritura", Cpt("c", "buscar", "emojis").Out);
        Assert.Equal(0, Cpt("c", "validar").Code);

        var json = JsonNode.Parse(Cpt("c", "read", "base", "--json").Out)!;
        Assert.Equal("proyecto", (string)json["capa"]!);
    }

    [Fact]
    public void Cpt_errores_y_sugerencias()
    {
        var unknown = Cpt("contrcts");
        Assert.Equal(2, unknown.Code);
        Assert.Contains("cpt contracts", unknown.Err);

        var missing = Cpt("c", "read", "estilo.escritur");
        Assert.Equal(1, missing.Code);
        Assert.Contains("estilo.escritura", missing.Err);

        Assert.Equal(2, Cpt("c", "lisst").Code);
        Assert.Equal(2, Cpt("c", "aprobar").Code);
        Assert.Equal(2, Cpt(".").Code);
        Assert.Equal(0, Cpt().Code);
        Assert.StartsWith("cpt ", Cpt("--version").Out);
    }

    [Fact]
    public void Validar_falla_con_errores_de_formato()
    {
        var folder = Path.Combine(_root, ".claupet", "contratos");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "roto.md"), "sin cabecera");
        var result = Cpt("c", "validate");
        Assert.Equal(1, result.Code);
        Assert.Contains("roto.md", result.Out);
    }

    [Fact]
    public void Mcp_lista_busca_y_lee()
    {
        var server = new ContractMcp.Server(_root, true);
        Assert.Null(server.Handle("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
        var init = JsonNode.Parse(server.Handle("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}""")!)!;
        Assert.Equal("claupet-contratos", (string)init["result"]!["serverInfo"]!["name"]!);

        var tools = JsonNode.Parse(server.Handle("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""")!)!["result"]!["tools"]!.AsArray();
        Assert.Equal(["contract_list", "contract_search", "contract_read"], tools.Select(t => (string)t!["name"]!));

        var read = JsonNode.Parse(server.Handle("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"contract_read","arguments":{"id":"estilo.escritura"}}}""")!)!;
        Assert.Contains("[sin-emojis]", (string)read["result"]!["content"]![0]!["text"]!);
        Assert.False((bool)read["result"]!["isError"]!);

        var bad = JsonNode.Parse(server.Handle("""{"jsonrpc":"2.0","id":4,"method":"nada"}""")!)!;
        Assert.Equal(-32601, (int)bad["error"]!["code"]!);
        Assert.NotNull(JsonNode.Parse(server.Handle("{roto")!)!["error"]);
    }
}
