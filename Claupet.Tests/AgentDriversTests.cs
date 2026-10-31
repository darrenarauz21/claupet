using System.Text.Json.Nodes;

namespace Claupet.Tests;

/// <summary>Lo que comparten los agentes de la Terminal: el driver de cada uno y la respuesta de la isla.</summary>
public sealed class AgentDriversTests
{
    [Fact]
    public void ClaudeCodeEscribeSusPedidosDeControl()
    {
        var driver = new ClaudeDriver(new AgentTranscript());

        var opening = JsonNode.Parse(Assert.Single(driver.Opening(new AgentOptions(@"C:\proyecto"))))!;
        Assert.Equal("initialize", (string?)opening["request"]!["subtype"]);
        Assert.Equal("user", (string?)JsonNode.Parse(Assert.Single(driver.Prompt("hola")))!["type"]);
        // Un modelo con caracteres raros no se escribe (uno que no existe sí: Claude Code contesta con el error, que queda como aviso).
        Assert.Empty(driver.SetModel("sonnet\" --x"));
        Assert.Empty(driver.SetEffort("extremo"));
        Assert.Empty(driver.SetMode("cualquiera"));
        Assert.Equal("high", (string?)JsonNode.Parse(Assert.Single(driver.SetEffort("high")))!["request"]!["settings"]!["effortLevel"]);
    }

    [Fact]
    public void LaIslaSeEntiendeIgualParaTodosLosAgentes()
    {
        Assert.Equal(PermissionAnswer.Allow, AgentDriver.IslandDecision("""{"hookSpecificOutput":{"decision":{"behavior":"allow"}}}"""));
        Assert.Equal(PermissionAnswer.Deny, AgentDriver.IslandDecision("""{"hookSpecificOutput":{"decision":{"behavior":"deny"}}}"""));
        Assert.Null(AgentDriver.IslandDecision("""{"hookSpecificOutput":{"decision":{"behavior":"ask"}}}"""));
        Assert.Null(AgentDriver.IslandDecision("no es json"));
    }

    [Fact]
    public void CadaAgenteTieneSuDriver()
    {
        var t = new AgentTranscript();
        Assert.IsType<ClaudeDriver>(AgentKinds.Driver(AgentKinds.Claude, t));
        Assert.IsType<CodexDriver>(AgentKinds.Driver(AgentKinds.Codex, t));
        Assert.IsType<AcpDriver>(AgentKinds.Driver(AgentKinds.Gemini, t));
        Assert.Equal("Copilot CLI", AgentKinds.Driver(AgentKinds.Copilot, t).Name);
        // Las opciones de antes (sin agente) siguen siendo de Claude Code.
        Assert.Equal(AgentKinds.Claude, new AgentOptions(@"C:\proyecto").Kind);
    }
}
