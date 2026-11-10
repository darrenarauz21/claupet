using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet.Tests;

/// <summary>
/// El historial de los otros agentes: lo que dicen sus listas (session/list de ACP, como la de OpenCode 1.19.5, y
/// thread/list de Codex), y cómo se retoma una sesión (session/load o session/resume; thread/resume) con lo de antes.
/// </summary>
public sealed class HistorialAgentesTests
{
    const string Folder = @"C:\proyecto";

    static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    // La respuesta real de «opencode acp» (1.19.5) a initialize y a session/list, con las rutas cambiadas.
    const string OpenCodeInit = """{"protocolVersion":1,"agentCapabilities":{"loadSession":true,"mcpCapabilities":{"http":true,"sse":true},"promptCapabilities":{"embeddedContext":true,"image":true},"sessionCapabilities":{"close":{},"fork":{},"list":{},"resume":{}}},"authMethods":[],"agentInfo":{"name":"OpenCode","version":"1.19.5"}}""";
    const string OpenCodeList = """{"sessions":[{"sessionId":"ses_a","cwd":"C:\\proyecto","title":"New session - 2026-11-10T21:25:51.184Z","updatedAt":"2026-11-10T21:25:51.645Z"},{"sessionId":"ses_b","cwd":"C:\\proyecto\\","title":"Arreglar el login","updatedAt":"2026-11-10T22:00:00.000Z"},{"sessionId":"ses_c","cwd":"C:\\otro","title":"De otra carpeta","updatedAt":"2026-11-10T22:00:00.000Z"}]}""";

    [Fact]
    public void LeeLaListaDeOpenCode()
    {
        Assert.True(AgentHistory.CanList(Json(OpenCodeInit)));
        Assert.False(AgentHistory.CanList(Json("""{"agentCapabilities":{"loadSession":true}}""")));

        var list = AgentHistory.AcpSessions(Json(OpenCodeList), AgentKinds.OpenCode, Folder).ToList();
        Assert.Equal(["ses_a", "ses_b"], list.Select(p => p.Id));
        Assert.All(list, p => Assert.Equal(AgentKinds.OpenCode, p.Kind));
        // El título que pone solo mientras no tiene uno no sirve: queda «Sesión de OpenCode» hasta que el asistente escriba uno.
        Assert.Equal((L.T("Sesión de {0}", "OpenCode"), false), (list[0].Title, list[0].Named));
        Assert.Equal(("Arreglar el login", true), (list[1].Title, list[1].Named));
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 22, 0, 0, TimeSpan.Zero).LocalDateTime, list[1].LastUsed);
    }

    [Theory]
    [InlineData("New session - 2026-11-10T21:25:51.184Z", true)]
    [InlineData("New Session", true)]
    [InlineData("Untitled", true)]
    [InlineData("", true)]
    [InlineData("Nueva sesión del login", false)]
    [InlineData("Arreglar el login", false)]
    public void ReconoceLosTitulosDeRelleno(string title, bool placeholder) => Assert.Equal(placeholder, AgentHistory.IsPlaceholder(title));

    [Fact]
    public void LeeLosHilosDeCodexConLoQueSePidio()
    {
        var result = Json("""{"data":[{"id":"t1","preview":"arregla el\nlogin por favor","cwd":"C:\\proyecto","createdAt":1793458200,"updatedAt":1793458300},{"id":"t2","preview":"otra cosa","name":"Mi hilo","cwd":"c:\\PROYECTO","updatedAt":1793458100},{"id":"t3","preview":"de otro lado","cwd":"C:\\otro","updatedAt":1793458400}],"nextCursor":null}""");
        var list = AgentHistory.CodexThreads(result, Folder).ToList();
        Assert.Equal(["t1", "t2"], list.Select(p => p.Id));
        Assert.Equal(("Arregla el login por favor", false, "arregla el login por favor"), (list[0].Title, list[0].Named, list[0].Prompt));
        Assert.Equal(("Mi hilo", true), (list[1].Title, list[1].Named));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793458300).LocalDateTime, list[0].LastUsed);
    }

    [Fact]
    public void ElHistorialJuntaTodosLosAgentesConLaBusqueda()
    {
        var now = DateTime.Now;
        IReadOnlyList<PastSession> claude = [new("c1", "Isla abre la terminal", now.AddMinutes(-1), "x"), new("c2", "Canvas", now.AddMinutes(-30), "y")];
        IReadOnlyList<PastSession> codex = [new("t1", "Login con Codex", now.AddMinutes(-10), "", "arregla el login", false, AgentKinds.Codex)];
        IReadOnlyList<PastSession> opencode = [new("o1", "Rediseño", now, "", "", true, AgentKinds.OpenCode)];

        var all = TerminalPane.Merge(claude, [codex, opencode], 15, "");
        Assert.Equal(["o1", "c1", "t1", "c2"], all.Select(p => p.Id));
        Assert.Equal(["o1", "c1"], TerminalPane.Merge(claude, [codex, opencode], 1, "").Select(p => p.Id));
        // La búsqueda de Claude Code ya viene filtrada; la de los otros se aplica aquí.
        Assert.Equal(["t1"], TerminalPane.Merge([], [codex, opencode], 15, "LOGIN").Select(p => p.Id));
    }

    // ── Retomar por ACP ──────────────────────────────────────────────────────────────────

    sealed class Acp
    {
        public readonly AgentTranscript Transcript = new();
        public readonly AcpDriver Driver;
        public readonly List<JsonNode> Sent = [];

        public Acp(string? resume)
        {
            Driver = new AcpDriver(AgentKinds.OpenCode, Transcript);
            Sent.AddRange(Driver.Opening(new AgentOptions(Folder, Kind: AgentKinds.OpenCode, Resume: resume)).Select(l => JsonNode.Parse(l)!));
        }

        public void Feed(string line)
        {
            var replies = new List<string>();
            Driver.Apply(line, replies);
            Sent.AddRange(replies.Select(l => JsonNode.Parse(l)!));
        }
    }

    static string Result(int id, string result) => "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":" + result + "}";
    static string Update(string update) => "{\"jsonrpc\":\"2.0\",\"method\":\"session/update\",\"params\":{\"sessionId\":\"ses_b\",\"update\":" + update + "}}";

    [Fact]
    public void RetomaConSessionLoadYMuestraLoDeAntes()
    {
        var s = new Acp("ses_b");
        s.Feed(Result(1, OpenCodeInit));
        var load = s.Sent[^1];
        Assert.Equal("session/load", (string?)load["method"]);
        Assert.Equal("ses_b", (string?)load["params"]!["sessionId"]);
        Assert.Equal(Folder, (string?)load["params"]!["cwd"]);

        // El agente repite la conversación antes de responder.
        s.Feed(Update("""{"sessionUpdate":"user_message_chunk","content":{"type":"text","text":"arregla "}}"""));
        s.Feed(Update("""{"sessionUpdate":"user_message_chunk","content":{"type":"text","text":"el login"}}"""));
        s.Feed(Update("""{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"Listo."}}"""));
        s.Feed(Result(2, """{"modes":null,"models":null}"""));

        Assert.Equal("ses_b", s.Transcript.SessionId);
        Assert.False(s.Transcript.Busy);
        var blocks = s.Transcript.Blocks;
        Assert.Equal([AgentBlockKind.Ask, AgentBlockKind.Say, AgentBlockKind.Notice], blocks.Select(b => b.Kind));
        Assert.Equal("arregla el login", blocks[0].Text);
        Assert.Equal("Listo.", blocks[1].Text);
        Assert.All(blocks, b => Assert.False(b.Open));
    }

    [Fact]
    public void SinSessionLoadUsaSessionResume()
    {
        var s = new Acp("ses_b");
        s.Feed(Result(1, OpenCodeInit.Replace("\"loadSession\":true", "\"loadSession\":false")));
        Assert.Equal("session/resume", (string?)s.Sent[^1]["method"]);
    }

    [Fact]
    public void SiNoSePuedeRetomarAbreUnaNueva()
    {
        var s = new Acp("ses_b");
        s.Feed(Result(1, OpenCodeInit));
        s.Feed("""{"jsonrpc":"2.0","id":2,"error":{"code":-32602,"message":"Session not found"}}""");
        Assert.Equal("session/new", (string?)s.Sent[^1]["method"]);
        Assert.Contains(s.Transcript.Blocks, b => b.Kind == AgentBlockKind.Notice && b.Text.Contains("Session not found"));

        // Y un agente que no sabe retomar abre una nueva directamente.
        var old = new Acp("ses_b");
        old.Feed(Result(1, """{"protocolVersion":1,"agentCapabilities":{}}"""));
        Assert.Equal("session/new", (string?)old.Sent[^1]["method"]);
    }

    [Fact]
    public void DuranteUnTurnoElPedidoRepetidoNoSeDuplica()
    {
        var s = new Acp(null);
        s.Feed(Result(1, OpenCodeInit));
        Assert.Equal("session/new", (string?)s.Sent[^1]["method"]);
        s.Feed(Result(2, """{"sessionId":"ses_b"}"""));
        s.Transcript.Ask("hola");
        s.Driver.Prompt("hola").ToList();
        s.Feed(Update("""{"sessionUpdate":"user_message_chunk","content":{"type":"text","text":"hola"}}"""));
        Assert.Single(s.Transcript.Blocks, b => b.Kind == AgentBlockKind.Ask);
    }

    // ── Retomar en Codex ─────────────────────────────────────────────────────────────────

    [Fact]
    public void CodexRetomaElHiloConLoDeAntes()
    {
        var t = new AgentTranscript();
        var d = new CodexDriver(t);
        var sent = new List<string>(d.Opening(new AgentOptions(Folder, Kind: AgentKinds.Codex, Resume: "t1")));
        d.Apply("""{"id":1,"result":{"userAgent":"claupet"}}""", sent);
        d.Apply("""{"id":2,"result":{"data":[],"nextCursor":null}}""", sent);
        var resume = JsonNode.Parse(sent[^1])!;
        Assert.Equal("thread/resume", (string?)resume["method"]);
        Assert.Equal("t1", (string?)resume["params"]!["threadId"]);
        Assert.Equal(Folder, (string?)resume["params"]!["cwd"]);

        d.Apply("""{"id":3,"result":{"thread":{"id":"t1","cwd":"C:\\proyecto","turns":[{"id":"u","items":[{"type":"userMessage","id":"u1","content":[{"type":"text","text":"arregla el login","text_elements":[]}]},{"type":"agentMessage","id":"m1","text":"Hecho."}],"status":"completed"}]},"model":"gpt-5.6-luna","cwd":"C:\\proyecto"}}""", sent);
        Assert.Equal("t1", t.SessionId);
        Assert.False(t.Busy);
        Assert.Equal([AgentBlockKind.Ask, AgentBlockKind.Say, AgentBlockKind.Notice], t.Blocks.Select(b => b.Kind));
        Assert.Equal(("arregla el login", "Hecho."), (t.Blocks[0].Text, t.Blocks[1].Text));
    }

    [Fact]
    public void CodexSinElHiloAbreUnoNuevo()
    {
        var t = new AgentTranscript();
        var d = new CodexDriver(t);
        var sent = new List<string>(d.Opening(new AgentOptions(Folder, Kind: AgentKinds.Codex, Resume: "t1")));
        d.Apply("""{"id":1,"result":{}}""", sent);
        d.Apply("""{"id":2,"result":{"data":[]}}""", sent);
        d.Apply("""{"id":3,"error":{"code":-32600,"message":"no rollout found for thread id t1"}}""", sent);
        Assert.Equal("thread/start", (string?)JsonNode.Parse(sent[^1])!["method"]);
        Assert.Contains(t.Blocks, b => b.Kind == AgentBlockKind.Notice && b.Text.Contains("no rollout found"));
    }
}
