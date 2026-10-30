namespace Claupet.Tests;

/// <summary>La lista de proyectos de la Terminal: el estado del repositorio y los proyectos vinculados y plegados.</summary>
public sealed class TerminalProjectsTests
{
    [Fact]
    public void LeeLaRamaLoQueLlevaAdelanteYLoSinCommit()
    {
        var status = GitStatus.Parse("""
            # branch.oid 3f2a9c1d4e5b6a7c8d9e0f1a2b3c4d5e6f7a8b9c
            # branch.head feat/terminal
            # branch.upstream origin/feat/terminal
            # branch.ab +2 -1
            1 .M N... 100644 100644 100644 aaa bbb Console/TerminalPane.cs
            2 R. N... 100644 100644 100644 aaa bbb R100 Core/Nuevo.cs	Core/Viejo.cs
            ? notas.txt
            """)!;

        Assert.Equal("feat/terminal", status.Branch);
        Assert.Equal(2, status.Ahead);
        Assert.Equal(1, status.Behind);
        Assert.Equal(3, status.Changed);
        Assert.Equal("↑2 · ↓1 · 3 sin commit", status.Summary);
    }

    [Fact]
    public void SinCambiosEstaAlDiaYSinRamaUsaElCommit()
    {
        var clean = GitStatus.Parse("# branch.oid 3f2a9c1d4e5b\n# branch.head main\n")!;
        Assert.Equal("al día", clean.Summary);

        var detached = GitStatus.Parse("# branch.oid 3f2a9c1d4e5b6a7c\n# branch.head (detached)\n")!;
        Assert.Equal("3f2a9c1", detached.Branch);

        Assert.Null(GitStatus.Parse(""));
    }

    [Fact]
    public void LosProyectosSeVinculanUnaVezYSeDesvinculanConSuPliegue()
    {
        var projects = new TerminalProjects();
        Assert.True(projects.Link(@"C:\proyecto"));
        Assert.False(projects.Link(@"c:\PROYECTO\"));
        Assert.True(projects.Link(@"C:\otro"));

        projects.SetFolded(@"C:\proyecto", true);
        Assert.True(projects.IsFolded(@"c:\proyecto"));
        projects.SetFolded(@"C:\proyecto", false);
        Assert.False(projects.IsFolded(@"C:\proyecto"));

        projects.SetFolded(@"C:\otro", true);
        projects.Unlink(@"C:\otro\");
        Assert.Equal([@"C:\proyecto"], projects.Folders);
        Assert.Empty(projects.Folded);
    }
}
