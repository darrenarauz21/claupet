namespace Claupet;

/// <summary>
/// Cómo está el repositorio de una carpeta, para la lista de proyectos de la Terminal: la rama, cuántos commits
/// lleva adelante o atrás de su rama remota y cuántos archivos tienen cambios sin commit. Sale de
/// «git status --porcelain=v2 --branch», que es estable entre versiones de git.
/// </summary>
sealed record GitStatus(string? Branch, int Ahead, int Behind, int Changed)
{
    /// <summary>Lo que dice la lista: «↑2 · 3 sin commit», «al día»…</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Ahead > 0) parts.Add($"↑{Ahead}");
            if (Behind > 0) parts.Add($"↓{Behind}");
            if (Changed > 0) parts.Add(L.T("{0} sin commit", Changed));
            return parts.Count > 0 ? string.Join(" · ", parts) : L.T("al día");
        }
    }

    public static GitStatus? Parse(string output)
    {
        string? branch = null, oid = null;
        int ahead = 0, behind = 0, changed = 0;
        var any = false;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            any = true;
            if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
                branch = line["# branch.head ".Length..] is var head && head != "(detached)" ? head : null;
            else if (line.StartsWith("# branch.oid ", StringComparison.Ordinal))
                oid = line["# branch.oid ".Length..];
            else if (line.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                foreach (var part in line["# branch.ab ".Length..].Split(' '))
                {
                    if (part.StartsWith('+') && int.TryParse(part[1..], out var a)) ahead = a;
                    else if (part.StartsWith('-') && int.TryParse(part[1..], out var b)) behind = b;
                }
            }
            else if (line[0] is '1' or '2' or 'u' or '?') changed++;
        }
        if (!any) return null;
        // Sin rama (detached): el comienzo del commit, como en git.
        branch ??= oid is { Length: >= 7 } && oid != "(initial)" ? oid[..7] : null;
        return new GitStatus(branch, ahead, behind, changed);
    }

    /// <summary>Lo lee con git (en segundo plano, hasta 5 s). Null si no es un repositorio o git no está.</summary>
    public static async Task<GitStatus?> ReadAsync(string folder) =>
        await GitRepo.RunAsync(folder, "status", "--porcelain=v2", "--branch", "--untracked-files=normal") is { } output ? Parse(output) : null;
}
