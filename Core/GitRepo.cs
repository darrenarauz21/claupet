using System.Diagnostics;

namespace Claupet;

/// <summary>Una rama local: lo que lleva adelante o atrás de su remota, si su remota ya no está, su worktree y si es la actual.</summary>
sealed record GitBranchInfo(string Name, int Ahead, int Behind, bool Gone, string? Worktree, bool Current);

/// <summary>Un archivo con cambios sin commit: las líneas agregadas y quitadas, si es nuevo (sin seguimiento) o binario.</summary>
sealed record GitChange(string Path, int Added, int Removed, bool New, bool Binary);

/// <summary>
/// Lo que el panel de la Terminal lee de un repositorio con git (solo lectura y con GIT_OPTIONAL_LOCKS=0, para no tomar
/// el índice mientras un agente trabaja): las ramas con sus worktrees, los cambios y el diff de un archivo. Cada lectura
/// tiene su parte que se prueba sin git (Parse…).
/// </summary>
static class GitRepo
{
    const int MaxBranches = 30;

    /// <summary>Corre git en esa carpeta y devuelve lo que escribió; null si falló, tardó más de 5 s o git no está.</summary>
    public static async Task<string?> RunAsync(string folder, params string[] args)
    {
        try
        {
            var start = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            };
            start.ArgumentList.Add("-C");
            start.ArgumentList.Add(folder);
            // Rutas con acentos tal cual, sin comillas ni \303\241.
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("core.quotepath=false");
            foreach (var arg in args) start.ArgumentList.Add(arg);
            start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            using var process = Process.Start(start);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                Assistant.Kill(process);
                return null;
            }
            return process.ExitCode == 0 ? await output : null;
        }
        catch (Exception) { return null; }
    }

    // ── Ramas ───────────────────────────────────────────────────────────────────────────────

    /// <summary>El formato de for-each-ref que lee ParseBranches: nombre, seguimiento («ahead 2, behind 1», «gone») y «*» si es la actual.</summary>
    public const string BranchFormat = "%(refname:short)%09%(upstream:track,nobracket)%09%(HEAD)";

    public static async Task<IReadOnlyList<GitBranchInfo>> BranchesAsync(string folder)
    {
        var refs = await RunAsync(folder, "for-each-ref", "--sort=-committerdate", "--format=" + BranchFormat, "refs/heads");
        if (refs is null) return [];
        var worktrees = await RunAsync(folder, "worktree", "list", "--porcelain") ?? "";
        return ParseBranches(refs, worktrees);
    }

    /// <summary>Las ramas (la actual primero, después las más recientes) con el worktree de cada una, si tiene.</summary>
    public static IReadOnlyList<GitBranchInfo> ParseBranches(string refs, string worktrees)
    {
        var paths = ParseWorktrees(worktrees);
        var list = new List<GitBranchInfo>();
        foreach (var raw in refs.Split('\n'))
        {
            var parts = raw.TrimEnd('\r').Split('\t');
            if (parts[0].Length == 0) continue;
            int ahead = 0, behind = 0;
            var track = parts.Length > 1 ? parts[1] : "";
            foreach (var piece in track.Split(',', StringSplitOptions.TrimEntries))
            {
                if (piece.StartsWith("ahead ", StringComparison.Ordinal)) int.TryParse(piece[6..], out ahead);
                else if (piece.StartsWith("behind ", StringComparison.Ordinal)) int.TryParse(piece[7..], out behind);
            }
            list.Add(new GitBranchInfo(parts[0], ahead, behind, track == "gone", paths.GetValueOrDefault(parts[0]),
                parts.Length > 2 && parts[2].Trim() == "*"));
        }
        return list.OrderByDescending(b => b.Current).Take(MaxBranches).ToList();
    }

    /// <summary>De «git worktree list --porcelain»: la carpeta de cada rama que tiene worktree.</summary>
    public static Dictionary<string, string> ParseWorktrees(string porcelain)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        string? path = null;
        foreach (var raw in porcelain.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("worktree ", StringComparison.Ordinal)) path = line[9..].Replace('/', Path.DirectorySeparatorChar);
            else if (line.StartsWith("branch refs/heads/", StringComparison.Ordinal) && path is not null) map[line[18..]] = path;
            else if (line.Length == 0) path = null;
        }
        return map;
    }

    // ── Cambios ─────────────────────────────────────────────────────────────────────────────

    public static async Task<IReadOnlyList<GitChange>> ChangesAsync(string folder)
    {
        // Contra HEAD: lo preparado y lo que no. En un repositorio sin commits, contra el índice vacío.
        var numstat = await RunAsync(folder, "diff", "--numstat", "HEAD") ?? await RunAsync(folder, "diff", "--numstat", "--cached") ?? "";
        var untracked = await RunAsync(folder, "ls-files", "--others", "--exclude-standard") ?? "";
        var news = untracked.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(200).ToDictionary(f => f, f => CountLines(Path.Combine(folder, f)));
        return ParseChanges(numstat, news);
    }

    /// <summary>Los cambios de --numstat («39	1	ruta», «-	-	binario») y los archivos nuevos con sus líneas.</summary>
    public static IReadOnlyList<GitChange> ParseChanges(string numstat, IReadOnlyDictionary<string, int> untracked)
    {
        var list = new List<GitChange>();
        foreach (var raw in numstat.Split('\n'))
        {
            var parts = raw.TrimEnd('\r').Split('\t');
            if (parts.Length < 3) continue;
            var binary = parts[0] == "-";
            int.TryParse(parts[0], out var added);
            int.TryParse(parts[1], out var removed);
            list.Add(new GitChange(Renamed(parts[2]), added, removed, false, binary));
        }
        list.AddRange(untracked.Select(u => new GitChange(u.Key, u.Value, 0, true, false)));
        return list;
    }

    /// <summary>Un archivo renombrado («Core/{Viejo => Nuevo}.cs» o «a => b»): el nombre nuevo.</summary>
    static string Renamed(string path)
    {
        var open = path.IndexOf('{');
        var arrow = path.IndexOf(" => ", StringComparison.Ordinal);
        if (arrow < 0) return path;
        if (open >= 0 && path.IndexOf('}') is var close and > 0 && open < arrow && arrow < close)
            return (path[..open] + path[(arrow + 4)..close] + path[(close + 1)..]).Replace("//", "/");
        return path[(arrow + 4)..];
    }

    static int CountLines(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > 2_000_000) return 0;
            return File.ReadLines(file).Count();
        }
        catch (Exception) { return 0; }
    }

    /// <summary>Las primeras líneas del cambio de un archivo, con «+», «-» o « » adelante (sin las cabeceras de git).</summary>
    public static async Task<IReadOnlyList<string>> DiffAsync(string folder, GitChange change, int max = 40)
    {
        if (change.New)
        {
            try
            {
                return File.ReadLines(Path.Combine(folder, change.Path)).Take(max).Select(l => "+" + l).ToList();
            }
            catch (Exception) { return []; }
        }
        var diff = await RunAsync(folder, "diff", "--no-color", "--unified=1", "HEAD", "--", change.Path) ?? "";
        return ParseDiff(diff, max);
    }

    public static IReadOnlyList<string> ParseDiff(string diff, int max) =>
        diff.Split('\n').Select(l => l.TrimEnd('\r'))
            .SkipWhile(l => !l.StartsWith("@@", StringComparison.Ordinal))
            // Una línea vacía de verdad es el final (las del contexto traen un espacio adelante).
            .Where(l => l.Length > 0 && !l.StartsWith("@@", StringComparison.Ordinal) && !l.StartsWith('\\'))
            .Take(max).ToList();
}
