namespace Claupet.Tests;

/// <summary>Una sesión nueva de Claude Code usa los permisos que tenga configurados, salvo que se elija otro modo.</summary>
public sealed class PermisosDeLaSesionTests
{
    [Fact]
    public void UnaPestanaNuevaNoLeImponeUnModo()
    {
        using var dir = new TempDir();
        var options = TerminalPane.NewClaudeOptions(dir.Path, null, null, null);
        Assert.Null(options.PermissionMode);
        Assert.DoesNotContain("--permission-mode", TerminalAgent.Arguments(options));
    }

    [Fact]
    public void ElModoElegidoSiLlegaAClaudeCode()
    {
        using var dir = new TempDir();
        var args = TerminalAgent.Arguments(new AgentOptions(dir.Path, PermissionMode: "plan"));
        Assert.Equal("plan", args[args.IndexOf("--permission-mode") + 1]);
    }

    [Fact]
    public void SeRecuerdaElModoConElQueAbrioLaSesion()
    {
        var transcript = new AgentTranscript();
        transcript.Apply("""{"type":"system","subtype":"init","permissionMode":"acceptEdits"}""");
        transcript.Apply("""{"type":"system","subtype":"status","permissionMode":"plan"}""");
        Assert.Equal("plan", transcript.PermissionMode);
        Assert.Equal("acceptEdits", transcript.SessionMode);
    }

    [Fact]
    public void LaPestanaSigueALaSesionSoloSiNoLeDieronModoYEsClaudeCode() => Headless.Run(() =>
    {
        using var dir = new TempDir();
        TerminalPane.Tab Tab(AgentOptions o) => new(new TerminalAgent(o, _ => { }), "cuenta");
        Assert.True(Tab(TerminalPane.NewClaudeOptions(dir.Path, null, null, null)).FollowsSession);
        Assert.False(Tab(new AgentOptions(dir.Path, PermissionMode: "plan")).FollowsSession);
        Assert.False(Tab(new AgentOptions(dir.Path, Kind: AgentKinds.Codex)).FollowsSession);
    });
}
