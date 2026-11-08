using System.Diagnostics;
using Avalonia.LogicalTree;
using System.Text.Json.Nodes;

namespace Claupet.Tests;

/// <summary>
/// La coordinación entre sesiones de una carpeta (Workspace): quién más trabaja ahí, qué cambios no son de la sesión, la
/// nota para el agente (sea cual sea) y el freno de los git que pisarían trabajo ajeno.
/// </summary>
public sealed class WorkspaceTests
{
    [Fact]
    public void LeeLasRutasDeGitStatus()
    {
        var status = " M Core/Notices.cs\r\n?? Core/Db.cs\nR  Viejo.cs -> Core/Nuevo.cs\n?? \"con espacio.txt\"\n";
        Assert.Equal(["Core/Notices.cs", "Core/Db.cs", "Core/Nuevo.cs", "con espacio.txt"], Workspace.ParseStatus(status));
    }

    [Theory]
    [InlineData("git stash", true)]
    [InlineData("git stash push -m x", true)]
    [InlineData("git switch main", true)]
    [InlineData("git checkout -b nueva", true)]
    [InlineData("git checkout -- Core/Notices.cs", true)]
    [InlineData("git restore .", true)]
    [InlineData("git reset --hard HEAD", true)]
    [InlineData("git clean -fd", true)]
    [InlineData("git add -A && git commit -m x", true)]
    [InlineData("git add .", true)]
    [InlineData("git commit -am \"x\"", true)]
    [InlineData("cd repo && git -C repo stash", true)]
    [InlineData("git stash list", false)]
    [InlineData("git status --short", false)]
    [InlineData("git add Core/Workspace.cs", false)]
    [InlineData("git add ./Core/Workspace.cs", false)]
    [InlineData("git commit -m \"Arreglo\"", false)]
    [InlineData("git diff HEAD", false)]
    [InlineData("dotnet test", false)]
    public void ElFrenoReconoceLosGitQuePisanTrabajoAjeno(string command, bool risky) => Assert.Equal(risky, Workspace.IsRisky(command));

    [Fact]
    public void ElComandoSaleDelPermisoDeCualquierAgente()
    {
        Assert.Equal("git stash", Workspace.CommandOf(new JsonObject { ["command"] = "git stash" }));
        Assert.Equal("git switch main", Workspace.CommandOf(new JsonObject { ["command"] = new JsonArray("git", "switch", "main") }));
        Assert.Equal("git clean -fd", Workspace.CommandOf(new JsonObject { ["cmd"] = "git clean -fd" }));
        Assert.Null(Workspace.CommandOf(new JsonObject { ["file_path"] = "a.cs" }));
    }

    [Fact]
    public void LaNotaLlevaLasReglasYNoSeVeEnElHistorial()
    {
        var first = Workspace.Note(null, first: true)!;
        Assert.StartsWith("\n\n<" + Workspace.Tag + ">", first);
        Assert.Contains("git stash", first);
        Assert.Null(Workspace.Note(new WorkspaceState("C:/r", "main", [], []), first: false));

        var state = new WorkspaceState("C:/r", "fix/avisos", [new WorkspacePeer("Codex", "Arreglar avisos", null, ["C:/r/Core/Notices.cs"], true)], ["Core/Db.cs"]);
        var note = Workspace.Note(state, first: false)!;
        Assert.Contains("Codex («Arreglar avisos»): edited Core/Notices.cs", note);
        Assert.Contains("Core/Db.cs", note);
        Assert.Contains("branch fix/avisos", note);

        Assert.Equal("Arregla los avisos", Workspace.Strip("Arregla los avisos" + note));
        Assert.Equal("Sin nota", Workspace.Strip("Sin nota"));
    }

    [Fact]
    public void ElAvisoDelFrenoSoloSaleSiHayAlgoQueCuidar()
    {
        Assert.Null(Workspace.Warning(new WorkspaceState("C:/r", null, [], [])));
        var warning = Workspace.Warning(new WorkspaceState("C:/r", null, [new WorkspacePeer("Codex", null, null, [], true)], ["a.cs", "b.cs"]))!;
        Assert.Contains("Codex", warning);
        Assert.Contains(L.T("{0} archivos con cambios que esta sesión no hizo.", 2), warning);
    }

    [Fact]
    public async Task VeLasOtrasSesionesYLosCambiosQueNoSonSuyos()
    {
        using var dir = new TempDir();
        var repo = dir["repo"];
        Directory.CreateDirectory(repo);
        Git(repo, "init", "-q");
        File.WriteAllText(Path.Combine(repo, "a.cs"), "a");
        File.WriteAllText(Path.Combine(repo, "b.cs"), "b");
        Git(repo, "add", ".");
        Git(repo, "-c", "user.email=p@p", "-c", "user.name=p", "commit", "-q", "-m", "inicio");

        // Antes de que empiece, otra persona ya cambió a.cs; después, la sesión edita b.cs.
        File.WriteAllText(Path.Combine(repo, "a.cs"), "ajeno");
        var self = new WorkspaceSession("yo-" + Guid.NewGuid().ToString("N"), "Claude Code", Path.Combine(repo, "sub"), Environment.ProcessId,
            DateTimeOffset.Now, DirtyAtStart: await Workspace.DirtyAsync(repo));
        File.WriteAllText(Path.Combine(repo, "b.cs"), "mío");
        File.WriteAllText(Path.Combine(repo, "nuevo.cs"), "de otro");
        self = self with { Touched = [Path.Combine(repo, "b.cs")] };

        var other = new WorkspaceSession("otra-" + Guid.NewGuid().ToString("N"), "Codex", repo, Environment.ProcessId, DateTimeOffset.Now,
            Title: "Arreglar avisos", Touched: [Path.Combine(repo, "nuevo.cs")]);
        var elsewhere = new WorkspaceSession("lejos-" + Guid.NewGuid().ToString("N"), "Gemini CLI", dir["otro"], Environment.ProcessId, DateTimeOffset.Now);
        var dead = new WorkspaceSession("muerta-" + Guid.NewGuid().ToString("N"), "Codex", repo, int.MaxValue - 7, DateTimeOffset.Now);
        foreach (var s in new[] { self, other, elsewhere, dead }) Workspace.Save(s);
        try
        {
            var state = await Workspace.ReadAsync(self.Folder, self, []);
            Assert.True(Account.SamePath(repo, state.Root));
            var peer = Assert.Single(state.Peers);
            Assert.Equal("Codex", peer.Agent);
            Assert.Equal(["a.cs", "nuevo.cs"], state.Foreign.Order());
            Assert.DoesNotContain(Workspace.Registered(), s => s.Id == dead.Id); // su Claupet ya no está: se borra
        }
        finally
        {
            foreach (var s in new[] { self, other, elsewhere }) Workspace.Remove(s.Id);
        }
    }

    static void Git(string folder, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = folder, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}

/// <summary>El camino completo: lo que le llega por su entrada a un agente de la Terminal (aquí, un proceso que lo guarda).</summary>
public sealed class WorkspaceAgentTests
{
    const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

    [Fact]
    public async Task ElAgenteRecibeLaNotaUnaVezYSeBorraDelRegistroAlCerrar()
    {
        using var dir = new TempDir();
        var repo = dir["repo"];
        Directory.CreateDirectory(repo);
        Git(repo, "init", "-q");
        File.WriteAllText(Path.Combine(repo, "a.cs"), "ajeno");
        var other = new WorkspaceSession("otra-" + Guid.NewGuid().ToString("N"), "Codex", repo, Environment.ProcessId, DateTimeOffset.Now, Title: "Arreglar avisos");
        Workspace.Save(other);
        var output = dir["entrada.txt"];
        try
        {
            var agent = new TerminalAgent(new AgentOptions(repo), action => action()) { Coordinate = true };
            var start = new ProcessStartInfo("powershell", ["-NoProfile", "-Command", $"[Console]::In.ReadToEnd() | Set-Content -Encoding utf8 '{output}'"])
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            };
            var process = Process.Start(start)!;
            typeof(TerminalAgent).GetField("_process", Private)!.SetValue(agent, process);
            var register = (Task)typeof(TerminalAgent).GetMethod("RegisterAsync", Private)!.Invoke(agent, null)!;
            typeof(TerminalAgent).GetField("_registered", Private)!.SetValue(agent, register);
            await register;
            Assert.Contains(Workspace.Registered(), s => s.Agent == agent.Driver.Name && Account.SamePath(s.Folder, repo));

            Task Sent() => (Task)typeof(TerminalAgent).GetField("_sending", Private)!.GetValue(agent)!;
            agent.Send("Arregla el inicio");
            await Sent();
            agent.Send("Sigue"); // nada cambió: sin nota
            await Sent();
            Assert.Equal("Arregla el inicio", agent.Transcript.Blocks.First(b => b.Kind == AgentBlockKind.Ask).Text); // en el chat, sin la nota

            process.StandardInput.Close();
            await process.WaitForExitAsync();
            agent.Dispose();
            Assert.DoesNotContain(Workspace.Registered(), s => s.Agent == agent.Driver.Name && Account.SamePath(s.Folder, repo));
            // Cada línea es JSON (stream-json): «<» llega como <.
            var lines = File.ReadAllLines(output).Where(l => l.Length > 0).Select(System.Text.RegularExpressions.Regex.Unescape).ToList();
            Assert.Equal(2, lines.Count);
            Assert.Contains("<" + Workspace.Tag + ">", lines[0]);
            Assert.Contains("Codex", lines[0]);
            Assert.Contains("a.cs", lines[0]);
            Assert.Contains("git stash", lines[0]);
            Assert.DoesNotContain(Workspace.Tag, lines[1]);
        }
        finally { Workspace.Remove(other.Id); }
    }

    static void Git(string folder, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = folder, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}

/// <summary>El aviso del freno se ve en el bloque del permiso, y ese permiso no ofrece «Siempre aquí».</summary>
public sealed class WorkspaceWarningViewTests
{
    [Fact]
    public void ElPermisoConAvisoLoMuestraYNoOfreceSiempreAqui() => Headless.Run(() =>
    {
        var pane = new TerminalPane();
        pane.SetInk(Ink.For(true));
        var method = typeof(TerminalPane).GetMethod("PermissionView", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        AgentBlock Block(string? warning) => new()
        {
            Kind = AgentBlockKind.Permission, Tool = "Bash", Text = "git stash", Input = new JsonObject { ["command"] = "git stash" },
            Suggestions = new JsonArray(new JsonObject { ["type"] = "addRules" }), Warning = warning,
        };
        IEnumerable<string?> Texts(AgentBlock block) => ((Avalonia.Controls.Control)method.Invoke(pane, [block])!)
            .GetLogicalDescendants().OfType<Avalonia.Controls.TextBlock>().Select(t => t.Text);

        var warning = "Hay otra sesión en esta carpeta (Codex).";
        var shown = Texts(Block(warning)).ToList();
        Assert.Contains(warning, shown);
        Assert.DoesNotContain(L.T("Siempre aquí"), shown);
        Assert.Contains(L.T("Siempre aquí"), Texts(Block(null)));
    });
}
