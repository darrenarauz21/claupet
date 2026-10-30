namespace Claupet.Tests;

/// <summary>Lo que el panel de la Terminal lee con git y de los procesos, sin correr git ni abrir procesos.</summary>
public sealed class GitRepoTests
{
    [Fact]
    public void LasRamasTraenSuSeguimientoYSuWorktree()
    {
        var refs = "main\tbehind 1\t \nfeat/terminal\tahead 2\t*\nfix/vieja\tgone\t \nsuelta\t\t \n";
        var worktrees = """
            worktree C:/proyecto
            HEAD 1111111
            branch refs/heads/main

            worktree C:/proyecto/.worktrees/terminal
            HEAD 2222222
            branch refs/heads/feat/terminal

            """;

        var branches = GitRepo.ParseBranches(refs, worktrees);

        Assert.Equal(["feat/terminal", "main", "fix/vieja", "suelta"], branches.Select(b => b.Name));
        var current = branches[0];
        Assert.True(current.Current);
        Assert.Equal(2, current.Ahead);
        Assert.Equal(@"C:\proyecto\.worktrees\terminal", current.Worktree); // las pruebas corren en Windows
        Assert.Equal(1, branches[1].Behind);
        Assert.True(branches[2].Gone);
        Assert.Null(branches[3].Worktree);
    }

    [Fact]
    public void LosCambiosTraenSusLineasLosNuevosYLosRenombrados()
    {
        var changes = GitRepo.ParseChanges("39\t1\tCore/ChatSupport.cs\n-\t-\tapp.ico\n3\t0\tCore/{Viejo => Nuevo}.cs\n",
            new Dictionary<string, int> { ["notas.txt"] = 12 });

        Assert.Equal(["Core/ChatSupport.cs", "app.ico", "Core/Nuevo.cs", "notas.txt"], changes.Select(c => c.Path));
        Assert.Equal((39, 1), (changes[0].Added, changes[0].Removed));
        Assert.True(changes[1].Binary);
        Assert.True(changes[3].New);
        Assert.Equal(12, changes[3].Added);
    }

    [Fact]
    public void ElDiffSeQuedaConLasLineasSinCabeceras()
    {
        var lines = GitRepo.ParseDiff("diff --git a/x b/x\nindex 1..2 100644\n--- a/x\n+++ b/x\n@@ -1,2 +1,2 @@\n uno\n-dos\n+tres\n\\ No newline at end of file\n", 10);
        Assert.Equal([" uno", "-dos", "+tres"], lines);
    }

    [Fact]
    public void LosProcesosDeUnaSesionSonSusHijosYNietos()
    {
        ProcInfo[] all = [new(10, 1, "claude"), new(11, 10, "node"), new(12, 11, "esbuild"), new(13, 2, "otro"), new(14, 10, "pwsh"), new(15, 15, "raro")];

        Assert.Equal([11, 14, 12], ProcessTree.Descendants(all, 10).Select(p => p.Pid));
        Assert.Empty(ProcessTree.Descendants(all, 13));
    }
}
