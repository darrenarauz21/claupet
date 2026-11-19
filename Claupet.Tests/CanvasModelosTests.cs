using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet.Tests;

/// <summary>
/// Canvas más sólido: borrar va a una papelera que se puede deshacer, y cada cambio de un agente lleva el modelo con que
/// lo hizo (la sesión se lo anota al servidor MCP, que es otro proceso).
/// </summary>
public sealed class CanvasModelosTests
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    static CanvasOp Text(string content) => new("insert", Kind: "markdown", Content: content);

    [Fact]
    public void BorrarLoMandaALaPapeleraYDeshacerLoDevuelveConSuHistorial()
    {
        using var dir = new TempDir();
        var store = new CanvasStore(dir.Path);
        var doc = store.Create("Informe", "Claude Code", [Text("uno")]);
        store.Apply(doc.Id, [Text("dos")], "Claude Code");
        Assert.Single(store.Versions(doc.Id));

        store.Delete(doc.Id);
        Assert.Empty(store.List());
        Assert.Null(store.Load(doc.Id));

        Assert.True(store.Undelete(doc.Id));
        Assert.Equal(2, store.Load(doc.Id)!.Version);
        Assert.Single(store.Versions(doc.Id));
        Assert.Single(store.List());
        // Ya salió de la papelera: no hay nada que deshacer.
        Assert.False(store.Undelete(doc.Id));
    }

    [Fact]
    public void LaPapeleraSeVaciaPasadoUnMes()
    {
        using var dir = new TempDir();
        var store = new CanvasStore(dir.Path);
        var old = store.Create("Viejo", null);
        store.Delete(old.Id);
        File.SetLastWriteTimeUtc(Path.Combine(dir.Path, "papelera", old.Id + ".json"), DateTime.UtcNow - CanvasStore.TrashKeep - TimeSpan.FromDays(1));

        var recent = store.Create("Nuevo", null);
        store.Delete(recent.Id);

        Assert.False(store.Undelete(old.Id));
        Assert.True(store.Undelete(recent.Id));
    }

    [Fact]
    public void CadaCambioLlevaElModeloConQueLoHizoElAgente()
    {
        using var dir = new TempDir();
        var store = new CanvasStore(dir.Path);
        var doc = store.Create("Informe", "Claude Code", [Text("uno")], model: "claude-opus-5-5");
        store.Apply(doc.Id, [Text("dos")], "Claude Code", model: "claude-fable-5-2");
        store.Apply(doc.Id, [Text("tres")], "OpenCode", model: "opencode/gpt-5.5");
        store.Apply(doc.Id, [new CanvasOp("edit", Id: "s1", Find: "uno", Replace: "uno bis")], CanvasJson.User);

        var saved = store.Load(doc.Id)!;
        Assert.Equal(["claude-opus-5-5", "claude-fable-5-2", "opencode/gpt-5.5", null], saved.Changes.Select(c => c.Model));
        // La persona editó la primera después: ya no es del modelo.
        Assert.Null(saved.Section("s1")!.Model);
        Assert.Equal("claude-fable-5-2", saved.Section("s2")!.Model);

        // En la lista: los modelos de cada agente, juntos y del más reciente al más viejo.
        var summary = store.List().Single();
        Assert.Equal(["claude-fable-5-2", "claude-opus-5-5"], summary.ModelsOf("Claude Code"));
        Assert.Equal("Fable 5.2, Opus 5.5", CanvasPane.WhoLabel(summary, "Claude Code"));
        Assert.Equal("gpt-5.5", CanvasPane.WhoLabel(summary, "OpenCode"));
        Assert.Equal(CanvasJson.AuthorLabel(CanvasJson.User), CanvasPane.WhoLabel(summary, CanvasJson.User));
        Assert.Equal("Claude Code: Fable 5.2, Opus 5.5", CanvasPane.WhoDetail(summary, "Claude Code"));
        Assert.Equal("Claude Code · Opus 5.5", CanvasJson.AuthorLabel("Claude Code", "claude-opus-5-5"));
    }

    [Fact]
    public void LosCanvasDeAntesSinModeloSiguenMostrandoAlAgente()
    {
        using var dir = new TempDir();
        var store = new CanvasStore(dir.Path);
        store.Create("Viejo", "Claude Code", [Text("uno")]);

        var summary = store.List().Single();
        Assert.Empty(summary.ModelsOf("Claude Code"));
        Assert.Equal("Claude Code", CanvasPane.WhoLabel(summary, "Claude Code"));
    }

    [Fact]
    public void ElServidorMcpAnotaElModeloDeSuSesionAunqueCambieAMitad()
    {
        using var dir = new TempDir();
        var store = new CanvasStore(dir.Path);
        var key = CanvasAgentModel.NewKey();
        CanvasAgentModel.Write(key, "claude-opus-5-5");
        try
        {
            var server = new CanvasMcp.Server(store, "Claude Code", dir.Path, key);
            Call(server, 1, "canvas_create", new { title = "Informe", sections = new object[] { new { kind = "markdown", content = "uno" } }, open = false });
            var id = store.List().Single().Id;

            CanvasAgentModel.Write(key, "claude-fable-5-2");
            Call(server, 2, "canvas_edit", new { id, ops = new object[] { new { op = "edit", id = "s1", find = "uno", replace = "dos" } } });

            var saved = store.Load(id)!;
            Assert.Equal(["claude-opus-5-5", "claude-fable-5-2"], saved.Changes.Select(c => c.Model));
            Assert.Equal("claude-fable-5-2", saved.Section("s1")!.Model);

            // Sin clave (otras apps, como Claude Desktop) el cambio va sin modelo.
            Call(new CanvasMcp.Server(store, "Claude", dir.Path), 3, "canvas_edit", new { id, ops = new object[] { new { op = "edit", id = "s1", find = "dos", replace = "tres" } } });
            Assert.Null(store.Load(id)!.Changes[^1].Model);
        }
        finally
        {
            CanvasAgentModel.Forget(key);
        }
        Assert.Null(CanvasAgentModel.Read(key));
    }

    [Fact]
    public void LaClaveDeLaSesionLlegaAlServidorConCadaAgente()
    {
        var acp = CanvasMcp.AcpServers("Gemini CLI", "C:\\p", session: "abc123").Single()!;
        Assert.Contains(acp["env"]!.AsArray(), e => e!["name"]!.GetValue<string>() == CanvasAgentModel.Variable && e["value"]!.GetValue<string>() == "abc123");

        Assert.Contains(CanvasMcp.CodexArguments("C:\\p", session: "abc123"), a => a.Contains($"{CanvasAgentModel.Variable}='abc123'"));
        // Algo que no es una clave no entra en el TOML de Codex.
        Assert.DoesNotContain(CanvasMcp.CodexArguments("C:\\p", session: "x'; rm"), a => a.Contains(CanvasAgentModel.Variable));

        var first = CanvasMcp.ClaudeConfigFile("Claude Code", "C:\\p", [], session: "abc123")!;
        var second = CanvasMcp.ClaudeConfigFile("Claude Code", "C:\\p", [], session: "def456")!;
        Assert.NotEqual(first, second);
        var env = JsonNode.Parse(File.ReadAllText(first))!["mcpServers"]![CanvasMcp.Name]!["env"]!;
        Assert.Equal("abc123", env[CanvasAgentModel.Variable]!.GetValue<string>());
    }

    [Fact]
    public void LaSesionLeAnotaASuServidorElModeloConQueTrabaja()
    {
        using var dir = new TempDir();
        var agent = new TerminalAgent(new AgentOptions(dir.Path), a => a());
        var key = agent.Driver.CanvasKey;
        var lines = (ConcurrentQueue<string>)typeof(TerminalAgent).GetField("_lines", Private)!.GetValue(agent)!;
        void Feed(string line)
        {
            lines.Enqueue(line);
            typeof(TerminalAgent).GetMethod("Drain", Private)!.Invoke(agent, null);
        }

        Feed("""{"type":"system","subtype":"init","session_id":"s-1","model":"claude-opus-5-5"}""");
        Assert.Equal("claude-opus-5-5", CanvasAgentModel.Read(key));

        // Otro modelo a mitad de sesión (/model o el menú).
        Feed("""{"type":"control_response","response":{"subtype":"success","request_id":"c-1","response":{"model":"claude-fable-5-2"}}}""");
        Assert.Equal("claude-fable-5-2", CanvasAgentModel.Read(key));

        agent.Dispose();
        Assert.Null(CanvasAgentModel.Read(key));
    }

    [Fact]
    public void UnaCarpetaYSuVariantePorMayusculasOBarraSonElMismoProyecto()
    {
        using var dir = new TempDir();
        var project = Directory.CreateDirectory(Path.Combine(dir.Path, "proyecto")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(dir.Path, "otro")).FullName;
        Assert.True(TerminalPane.SameProject(project, project + Path.DirectorySeparatorChar));
        if (OperatingSystem.IsWindows()) Assert.True(TerminalPane.SameProject(project, project.ToUpperInvariant()));
        Assert.False(TerminalPane.SameProject(project, other));
    }

    // ── Andamio ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Llama una herramienta del servidor MCP y falla si respondió con error.</summary>
    static void Call(CanvasMcp.Server server, int id, string tool, object arguments)
    {
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = "tools/call",
            ["params"] = JsonSerializer.SerializeToNode(new { name = tool, arguments }),
        };
        var reply = JsonDocument.Parse(server.Handle(request.ToJsonString())!).RootElement;
        Assert.False(reply.GetProperty("result").GetProperty("isError").GetBoolean(), reply.ToString());
    }
}
