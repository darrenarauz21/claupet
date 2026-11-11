namespace Claupet;

/// <summary>Dónde está un repositorio: su carpeta de trabajo, su .git (el de este worktree) y el .git común a todos.</summary>
sealed record GitPlace(string Root, string GitDir, string CommonDir)
{
    /// <summary>La carpeta del worktree principal (la del proyecto); null si el .git común no es una carpeta «.git».</summary>
    public string? MainRoot => Path.GetFileName(Path.TrimEndingDirectorySeparator(CommonDir)) == ".git"
        ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(CommonDir)) : null;
}

/// <summary>
/// El proyecto es el repositorio y se muestra con su rama principal (main), no con la rama en que quedó su carpeta. Cada
/// sesión nueva de un agente puede empezar en su propio worktree, desde main al día: una carpeta hermana
/// «&lt;proyecto&gt;-sesion-&lt;fecha&gt;» con la rama «sesion/&lt;fecha&gt;», que sigue contando como el mismo proyecto. Se lee
/// sin git (archivos de .git) lo que se pide al dibujar; lo que cambia algo corre git en segundo plano.
/// </summary>
static class SessionWorktrees
{
    public const string FolderMark = "-sesion-", BranchPrefix = "sesion/";

    /// <summary>El repositorio de esa carpeta (o de alguna de arriba); null si no hay.</summary>
    public static GitPlace? Locate(string? folder)
    {
        try
        {
            for (var dir = folder is null ? null : new DirectoryInfo(folder); dir is not null; dir = dir.Parent)
            {
                var git = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(git)) return new GitPlace(dir.FullName, git, git);
                if (!File.Exists(git) || File.ReadAllText(git).Trim() is not { } link || !link.StartsWith("gitdir:", StringComparison.Ordinal)) continue;
                var gitDir = Path.GetFullPath(link["gitdir:".Length..].Trim(), dir.FullName);
                var common = Path.Combine(gitDir, "commondir");
                var commonDir = File.Exists(common) ? Path.GetFullPath(File.ReadAllText(common).Trim(), gitDir) : gitDir;
                return new GitPlace(dir.FullName, gitDir, Path.TrimEndingDirectorySeparator(commonDir));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return null;
    }

    /// <summary>
    /// La rama principal: la que dice origin/HEAD y, si no hay remoto, main o master si existen. Null si no es un
    /// repositorio o no tiene ninguna de esas.
    /// </summary>
    public static string? Default(string? folder)
    {
        if (Locate(folder) is not { } place) return null;
        try
        {
            var originHead = Path.Combine(place.CommonDir, "refs", "remotes", "origin", "HEAD");
            const string Ref = "ref: refs/remotes/origin/";
            if (File.Exists(originHead) && File.ReadAllText(originHead).Trim() is var text && text.StartsWith(Ref, StringComparison.Ordinal))
                return text[Ref.Length..];
            var packed = Path.Combine(place.CommonDir, "packed-refs");
            var refs = File.Exists(packed) ? File.ReadAllText(packed) : "";
            foreach (var name in new[] { "main", "master" })
                if (File.Exists(Path.Combine(place.CommonDir, "refs", "heads", name)) || refs.Contains(" refs/heads/" + name + "\n", StringComparison.Ordinal)
                    || refs.EndsWith(" refs/heads/" + name, StringComparison.Ordinal))
                    return name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>Es un worktree de sesión que abrió Claupet: la carpeta «&lt;proyecto&gt;-sesion-…» de otro repositorio principal.</summary>
    public static bool IsSession(string? folder) =>
        Locate(folder) is { MainRoot: { } main } place && !SamePath(place.Root, main)
        && Path.GetFileName(Path.TrimEndingDirectorySeparator(place.Root)).StartsWith(Path.GetFileName(main) + FolderMark, StringComparison.OrdinalIgnoreCase);

    /// <summary>La carpeta del proyecto: la del repositorio principal si es un worktree de sesión; si no, la misma.</summary>
    public static string ProjectOf(string folder) =>
        IsSession(folder) && Locate(folder)?.MainRoot is { } main ? main : folder;

    /// <summary>La rama y la carpeta de un worktree de sesión nuevo, junto a la del proyecto (sin pisar una que ya esté).</summary>
    public static (string Branch, string Path) NewName(string projectRoot, DateTime now)
    {
        var stamp = now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var root = Path.TrimEndingDirectorySeparator(projectRoot);
        var parent = Path.GetDirectoryName(root) ?? root;
        var name = Path.GetFileName(root) + FolderMark + stamp;
        var path = Path.Combine(parent, name);
        for (var n = 2; Directory.Exists(path); n++) path = Path.Combine(parent, name + "-" + n);
        return (BranchPrefix + Path.GetFileName(path)[(Path.GetFileName(root) + FolderMark).Length..], path);
    }

    /// <summary>
    /// Crea el worktree de una sesión desde la rama principal al día (origin/main después de un fetch; sin remoto, main).
    /// Devuelve su carpeta, o el error para mostrarlo.
    /// </summary>
    public static async Task<(string? Path, string? Error)> CreateAsync(string folder, DateTime now)
    {
        if (Locate(folder) is not { } place) return (null, L.T("«{0}» no es un repositorio de git.", folder));
        var project = place.MainRoot ?? place.Root;
        var main = Default(project) ?? GitBranch.Of(project) ?? "main";
        await GitRepo.RunAsync(TimeSpan.FromSeconds(30), project, "fetch", "--quiet", "origin", main);
        var from = await GitRepo.RunAsync(project, "rev-parse", "--verify", "--quiet", "origin/" + main) is not null ? "origin/" + main : main;
        var (branch, path) = NewName(project, now);
        // --no-track: la rama de la sesión no sigue a main, así un push nunca va a parar ahí.
        return await GitRepo.RunAsync(TimeSpan.FromSeconds(60), project, "worktree", "add", "--quiet", "--no-track", "-b", branch, path, from) is null
            ? (null, L.T("No se pudo crear el worktree de la sesión desde {0}.", from))
            : (path, null);
    }

    /// <summary>Si la rama ya está entera en la principal (origin/main o main): nada de ella se perdería.</summary>
    public static async Task<bool> IsMergedAsync(string folder, string branch, string main) =>
        await GitRepo.RunAsync(folder, "merge-base", "--is-ancestor", branch,
            await GitRepo.RunAsync(folder, "rev-parse", "--verify", "--quiet", "origin/" + main) is not null ? "origin/" + main : main) is not null;

    /// <summary>
    /// Vuelve la carpeta del proyecto a la rama principal y la pone al día (solo avanzar). No descarta nada: con cambios
    /// sin commit, o si otra sesión trabaja ahí, no lo hace y devuelve por qué.
    /// </summary>
    public static async Task<string?> SwitchToDefaultAsync(string folder, IEnumerable<string> claudeDirs)
    {
        if (Default(folder) is not { } main) return L.T("No se encontró la rama principal.");
        if (await GitStatus.ReadAsync(folder) is not { } status) return L.T("No se pudo leer el repositorio.");
        if (status.Changed > 0) return L.T("La carpeta tiene {0} archivos con cambios sin commit: no se cambia de rama.", status.Changed);
        if ((await Workspace.ReadAsync(folder, null, claudeDirs)).Peers.Count > 0) return L.T("Otra sesión está trabajando en esta carpeta: no se cambia de rama.");
        if (await GitRepo.RunAsync(TimeSpan.FromSeconds(30), folder, "switch", "--quiet", main) is null) return L.T("No se pudo volver a {0}.", main);
        return await GitRepo.RunAsync(TimeSpan.FromSeconds(60), folder, "pull", "--quiet", "--ff-only") is null
            ? L.T("Volvió a {0}, pero no se pudo actualizar.", main) : null;
    }

    /// <summary>
    /// Al cerrar una sesión en su worktree: si no dejó nada (sin cambios ni commits propios), se borran la carpeta y su
    /// rama. Si dejó algo, queda como está. Devuelve si se borró.
    /// </summary>
    public static async Task<bool> RemoveIfUnusedAsync(string folder)
    {
        if (!IsSession(folder) || Locate(folder) is not { MainRoot: { } project } || GitBranch.Of(folder) is not { } branch) return false;
        if (await GitStatus.ReadAsync(folder) is not { Changed: 0 }) return false;
        var main = Default(project) ?? "main";
        // Sus commits que no están en la principal: ninguno, o no se borra.
        var from = await GitRepo.RunAsync(folder, "rev-parse", "--verify", "--quiet", "origin/" + main) is not null ? "origin/" + main : main;
        if ((await GitRepo.RunAsync(folder, "rev-list", "--count", from + ".." + branch))?.Trim() != "0") return false;
        if (await GitRepo.RunAsync(TimeSpan.FromSeconds(30), project, "worktree", "remove", folder) is null) return false;
        await GitRepo.RunAsync(project, "branch", "-D", branch);
        return true;
    }

    static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
}
