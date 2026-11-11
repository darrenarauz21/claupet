using System.Diagnostics;

namespace Claupet.Tests;

/// <summary>
/// El proyecto con su rama principal y las sesiones en su propio worktree (SessionWorktrees), con repositorios de verdad;
/// y pasar una conversación de Claude Code a otra cuenta (ClaudeHistory.CopySession).
/// </summary>
public sealed class SessionWorktreesTests
{
    [Fact]
    public void LaRamaPrincipalEsLaDeOriginHeadOMainSiNoHayRemoto()
    {
        using var dir = new TempDir();
        var repo = Repo(dir, "proyecto");
        Assert.Equal("main", SessionWorktrees.Default(repo));
        Git(repo, "switch", "-q", "-c", "fix/vieja");
        Assert.Equal("main", SessionWorktrees.Default(Path.Combine(repo, "sub"))); // desde una subcarpeta también
        Assert.Equal("fix/vieja", GitBranch.Of(repo));

        dir.Write("proyecto/.git/refs/remotes/origin/HEAD", "ref: refs/remotes/origin/desarrollo\n");
        Assert.Equal("desarrollo", SessionWorktrees.Default(repo));
        Assert.Null(SessionWorktrees.Default(dir["sin-git"]));
    }

    [Fact]
    public void ElNombreDelWorktreeVaJuntoAlProyectoYNoPisaUnoQueYaEste()
    {
        using var dir = new TempDir();
        var now = new DateTime(2026, 11, 10, 18, 12, 5);
        var (branch, path) = SessionWorktrees.NewName(dir["claupet"], now);
        Assert.Equal("sesion/20261110-181205", branch);
        Assert.Equal(dir["claupet-sesion-20261110-181205"], path);
        Directory.CreateDirectory(path);
        Assert.Equal((branch + "-2", path + "-2"), SessionWorktrees.NewName(dir["claupet"], now));
    }

    [Fact]
    public async Task LaSesionEmpiezaDesdeMainEnSuWorktreeYSeBorraSiNoDejoNada()
    {
        using var dir = new TempDir();
        var repo = Repo(dir, "proyecto");
        Git(repo, "switch", "-q", "-c", "fix/vieja");
        File.WriteAllText(Path.Combine(repo, "solo-en-la-vieja.txt"), "x");
        Commit(repo, "solo en la vieja");

        var (path, error) = await SessionWorktrees.CreateAsync(repo, new DateTime(2026, 11, 10, 18, 0, 0));
        Assert.Null(error);
        Assert.Equal(dir["proyecto-sesion-20261110-180000"], path);
        Assert.Equal("sesion/20261110-180000", GitBranch.Of(path));
        Assert.False(File.Exists(Path.Combine(path!, "solo-en-la-vieja.txt"))); // empezó desde main, no desde la rama de la carpeta
        Assert.Equal("fix/vieja", GitBranch.Of(repo)); // la carpeta del proyecto no se tocó
        Assert.True(SessionWorktrees.IsSession(path));
        Assert.False(SessionWorktrees.IsSession(repo));
        Assert.Equal(repo, SessionWorktrees.ProjectOf(path!), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(repo, SessionWorktrees.ProjectOf(Path.Combine(path!, "sub")), StringComparer.OrdinalIgnoreCase);

        // Sin cambios ni commits propios: al cerrar se borran la carpeta y la rama.
        Assert.True(await SessionWorktrees.RemoveIfUnusedAsync(path!));
        Assert.False(Directory.Exists(path));
        Assert.Null(await GitRepo.RunAsync(repo, "rev-parse", "--verify", "--quiet", "sesion/20261110-180000"));
    }

    [Fact]
    public async Task ElWorktreeConTrabajoSeQueda()
    {
        using var dir = new TempDir();
        var repo = Repo(dir, "proyecto");
        var (path, _) = await SessionWorktrees.CreateAsync(repo, new DateTime(2026, 11, 10, 9, 0, 0));
        File.WriteAllText(Path.Combine(path!, "nuevo.txt"), "sin commit");
        Assert.False(await SessionWorktrees.RemoveIfUnusedAsync(path!));
        Commit(path!, "trabajo de la sesión");
        Assert.False(await SessionWorktrees.RemoveIfUnusedAsync(path!)); // con un commit propio tampoco
        Assert.True(Directory.Exists(path));
        Assert.False(await SessionWorktrees.RemoveIfUnusedAsync(repo)); // la carpeta del proyecto nunca
        Git(repo, "worktree", "remove", "--force", path!);
    }

    [Fact]
    public async Task VolverAMainNoDescartaNada()
    {
        using var dir = new TempDir();
        var repo = Repo(dir, "proyecto");
        Git(repo, "switch", "-q", "-c", "fix/vieja");
        Assert.True(await SessionWorktrees.IsMergedAsync(repo, "fix/vieja", "main"));
        File.WriteAllText(Path.Combine(repo, "a.txt"), "cambio sin commit");
        Assert.Equal(L.T("La carpeta tiene {0} archivos con cambios sin commit: no se cambia de rama.", 1), await SessionWorktrees.SwitchToDefaultAsync(repo, []));
        Assert.Equal("fix/vieja", GitBranch.Of(repo));

        Commit(repo, "en la vieja");
        Assert.False(await SessionWorktrees.IsMergedAsync(repo, "fix/vieja", "main"));
        // Sin remoto no se puede poner al día, pero sí vuelve a main (y lo dice).
        Assert.Equal(L.T("Volvió a {0}, pero no se pudo actualizar.", "main"), await SessionWorktrees.SwitchToDefaultAsync(repo, []));
        Assert.Equal("main", GitBranch.Of(repo));
    }

    [Fact]
    public void LaConversacionPasaAOtraCuentaSinTocarLaDeAntes()
    {
        using var dir = new TempDir();
        var folder = dir["trabajo"];
        var id = Guid.NewGuid().ToString();
        var from = dir["cuenta-a"];
        var to = dir["cuenta-b"];
        var source = Path.Combine(ClaudeHistory.ProjectDir(from, folder), id + ".jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, """{"type":"user","sessionId":"x","message":{"role":"user","content":"Arregla el inicio"}}""" + "\n");
        dir.Write("cuenta-a/projects/" + Path.GetFileName(ClaudeHistory.ProjectDir(from, folder)) + "/" + id + "/subagents/a.jsonl", "{}");

        var past = ClaudeHistory.CopySession(from, to, folder, id);
        Assert.NotNull(past);
        Assert.Equal(id, past.Id);
        Assert.Equal(File.ReadAllText(source), File.ReadAllText(past.Path));
        Assert.True(File.Exists(Path.Combine(ClaudeHistory.ProjectDir(to, folder), id, "subagents", "a.jsonl")));
        Assert.True(File.Exists(source));

        // Si se vuelve a pasar, la copia de antes se reemplaza por lo último.
        File.AppendAllText(source, """{"type":"user","message":{"role":"user","content":"Sigue"}}""" + "\n");
        Assert.Equal(File.ReadAllText(source), File.ReadAllText(ClaudeHistory.CopySession(from, to, folder, id)!.Path));
        Assert.Null(ClaudeHistory.CopySession(from, to, folder, Guid.NewGuid().ToString()));
    }

    static string Repo(TempDir dir, string name)
    {
        var repo = dir[name];
        Directory.CreateDirectory(Path.Combine(repo, "sub"));
        Git(repo, "init", "-q", "-b", "main");
        File.WriteAllText(Path.Combine(repo, "a.txt"), "a");
        File.WriteAllText(Path.Combine(repo, "sub", "b.txt"), "b");
        Commit(repo, "inicio");
        return repo;
    }

    static void Commit(string folder, string message)
    {
        Git(folder, "add", ".");
        Git(folder, "-c", "user.email=p@p", "-c", "user.name=p", "commit", "-q", "-m", message);
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
