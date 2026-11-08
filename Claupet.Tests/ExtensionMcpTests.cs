using System.Text.Json.Nodes;

namespace Claupet.Tests;

public class ExtensionMcpTests
{
    static readonly HashSet<string> Files = ["manifest.json", "server.js", "bin/srv.exe"];

    static string Manifest(string mcp, string extra = "", string provides = "\"mcp\"") => $$"""
        {
          "schema": 1,
          "id": "prueba.herramientas",
          "name": "Herramientas",
          "version": "1.0.0",
          "provides": [{{provides}}],
          "settings": [
            { "key": "token", "type": "secret", "label": "Token" },
            { "key": "carpeta", "label": "Carpeta", "default": "C:\\notas" }
          ],
          "mcp": {{mcp}}{{extra}}
        }
        """;

    static (ExtensionManifest? M, IReadOnlyList<string> Errors) Parse(string json) => ExtensionManifest.Parse(json, Files);

    [Fact]
    public void Un_servidor_stdio_y_uno_http_se_leen()
    {
        var (m, errors) = Parse(Manifest("""
            {
              "notas": { "program": "node", "args": ["{package}/server.js", "{setting:carpeta}"], "env": { "TOKEN": "{setting:token}" } },
              "github": { "type": "http", "url": "https://api.example.com/mcp/", "headers": { "Authorization": "Bearer {setting:token}" } }
            }
            """, ", \"permissions\": [\"network:api.example.com\"]"));
        Assert.Empty(errors);
        Assert.Equal(2, m!.Mcp.Count);
        Assert.True(m.RunsCode);
        Assert.Equal(McpServerSpec.Http, m.Mcp.Single(s => s.Name == "github").Type);
    }

    [Fact]
    public void Solo_http_no_corre_codigo()
    {
        var (m, errors) = Parse(Manifest("""{ "web": { "url": "https://api.example.com/mcp" } }""", ", \"permissions\": [\"network:api.example.com\"]"));
        Assert.Empty(errors);
        Assert.False(m!.RunsCode);
    }

    [Theory]
    [InlineData("""{ "web": { "url": "https://otro.example.com/mcp" } }""", "network:")]
    [InlineData("""{ "web": { "url": "http://example.com/mcp" } }""", "https")]
    [InlineData("""{ "web": { "url": "https://x.example.com/{setting:token}" } }""", "«env» o «headers»")]
    [InlineData("""{ "s": { "program": "node", "args": ["{setting:token}"] } }""", "«env» o «headers»")]
    [InlineData("""{ "s": { "program": "node", "env": { "A": "{setting:nada}" } } }""", "«nada»")]
    [InlineData("""{ "s": { "command": "falta.exe" } }""", "no está en el paquete")]
    [InlineData("""{ "s": { "command": "../fuera.exe" } }""", "dentro del paquete")]
    [InlineData("""{ "s": { "program": "C:\\bin\\node.exe" } }""", "sin ruta")]
    [InlineData("""{ "s": { "program": "node", "command": "server.js" } }""", "uno de los dos")]
    [InlineData("""{ "claupet-canvas": { "program": "node" } }""", "nombre inválido")]
    [InlineData("""{ "Mayus": { "program": "node" } }""", "nombre inválido")]
    [InlineData("""{ "s": { "type": "sse", "url": "https://x.example.com" } }""", "«stdio» o «http»")]
    public void Lo_que_no_sirve_se_rechaza(string mcp, string problem)
    {
        var (m, errors) = Parse(Manifest(mcp, ", \"permissions\": [\"network:x.example.com\", \"network:api.example.com\"]"));
        Assert.Null(m);
        Assert.Contains(errors, e => e.Contains(problem));
    }

    [Fact]
    public void La_ranura_va_con_sus_servidores()
    {
        var (m, errors) = Parse(Manifest("{}"));
        Assert.Null(m);
        Assert.Contains(errors, e => e.Contains("«mcp» en «provides»"));
    }

    static InstalledExtension Installed(ExtensionManifest m) =>
        new(m, @"C:\ext\paquetes\prueba.herramientas", @"C:\ext\datos\prueba.herramientas", new InstallRecord());

    [Fact]
    public void El_ejemplo_de_la_documentacion_sirve()
    {
        var folder = Path.Combine(Repo.Root, "docs", "extensiones", "ejemplos", "mcp");
        var (m, errors) = ExtensionManifest.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")), new HashSet<string> { "manifest.json", "server.js" });
        Assert.Empty(errors);
        Assert.Equal("notas", Assert.Single(m!.Mcp).Name);
    }

    [Fact]
    public void Al_armarlo_se_ponen_rutas_y_ajustes()
    {
        var (m, _) = Parse(Manifest("""
            {
              "notas": { "program": "node", "args": ["{package}/server.js", "{setting:carpeta}"], "env": { "TOKEN": "{setting:token}" } },
              "web": { "url": "https://api.example.com/mcp", "headers": { "Authorization": "Bearer {setting:token}" } }
            }
            """, ", \"permissions\": [\"network:api.example.com\"]"));
        var ext = Installed(m!);
        string? Value(string key) => key == "token" ? "abc" : key == "carpeta" ? @"C:\notas" : null;

        var notas = ExtensionMcp.Resolve(ext, m!.Mcp[0], Value, p => @"C:\node\" + p + ".exe");
        Assert.Equal(@"C:\node\node.exe", notas.Command);
        Assert.Equal([@"C:\ext\paquetes\prueba.herramientas/server.js", @"C:\notas"], notas.Args);
        Assert.Equal("abc", notas.Env["TOKEN"]);
        Assert.Equal("prueba.herramientas", notas.Env["CLAUPET_EXTENSION_ID"]);

        var web = ExtensionMcp.Resolve(ext, m.Mcp[1], Value, _ => null);
        Assert.Equal("Bearer abc", web.Headers["Authorization"]);

        var falta = Assert.Throws<ExtensionUnavailableException>(() => ExtensionMcp.Resolve(ext, m.Mcp[0], Value, _ => null));
        Assert.Contains("node", falta.Message);
    }

    static readonly McpServer Stdio = new("notas", McpServerSpec.Stdio, @"C:\node\node.exe", ["server.js"],
        new Dictionary<string, string> { ["TOKEN"] = "abc" }, null, new Dictionary<string, string>(), "prueba.herramientas");

    static readonly McpServer Http = new("web", McpServerSpec.Http, null, [], new Dictionary<string, string>(),
        "https://api.example.com/mcp", new Dictionary<string, string> { ["Authorization"] = "Bearer abc" }, "prueba.herramientas");

    [Fact]
    public void Claude_Code_los_recibe_junto_al_canvas()
    {
        var file = CanvasMcp.ClaudeConfigFile("Prueba", null, [Stdio, Http])!;
        var servers = JsonNode.Parse(File.ReadAllText(file))!["mcpServers"]!;
        Assert.NotNull(servers[CanvasMcp.Name]);
        Assert.Equal(@"C:\node\node.exe", servers["notas"]!["command"]!.GetValue<string>());
        Assert.Equal("abc", servers["notas"]!["env"]!["TOKEN"]!.GetValue<string>());
        Assert.Equal("http", servers["web"]!["type"]!.GetValue<string>());
        Assert.Equal("Bearer abc", servers["web"]!["headers"]!["Authorization"]!.GetValue<string>());
    }

    [Fact]
    public void Codex_y_ACP_los_reciben()
    {
        var codex = CanvasMcp.CodexArguments(null, [Stdio, Http]).ToList();
        Assert.Contains(@"mcp_servers.notas.command='C:\node\node.exe'", codex);
        Assert.Contains("mcp_servers.notas.env={'TOKEN'='abc'}", codex);
        Assert.Contains("mcp_servers.web.http_headers={'Authorization'='Bearer abc'}", codex);

        var raro = Stdio with { Name = "raro", Args = ["a&b"] };
        Assert.DoesNotContain(CanvasMcp.CodexArguments(null, [raro]), a => a.Contains("raro"));

        var sinHttp = CanvasMcp.AcpServers("Gemini CLI", null, false, [Stdio, Http]);
        Assert.Equal([CanvasMcp.Name, "notas"], sinHttp.Select(s => s!["name"]!.GetValue<string>()));
        var conHttp = CanvasMcp.AcpServers("Gemini CLI", null, true, [Stdio, Http]);
        Assert.Equal("http", conHttp.Single(s => s!["name"]!.GetValue<string>() == "web")!["type"]!.GetValue<string>());
        Assert.Equal("TOKEN", conHttp.Single(s => s!["name"]!.GetValue<string>() == "notas")!["env"]![0]!["name"]!.GetValue<string>());
    }
}
