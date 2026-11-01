using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>Qué es un enlace de un texto: una página web o un archivo del equipo.</summary>
enum LinkKind { Url, File }

/// <summary>
/// Un enlace dentro de un texto: dónde empieza y cuánto mide (lo que se pinta en azul), qué es, a qué lleva (la URL o la
/// ruta completa del archivo) y, en un archivo, la línea si venía con «:42».
/// </summary>
sealed record TextLink(int Start, int Length, LinkKind Kind, string Target, int? Line = null);

/// <summary>
/// Los enlaces de lo que escribe un agente o un programa: las URL (http y https) y las rutas de archivos que existen
/// (absolutas, relativas a la carpeta de la sesión o con «~»). Una ruta que no existe no es enlace: así «2.5.3», «e.g.» o
/// un nombre de ejemplo no se pintan.
/// </summary>
static partial class TextLinks
{
    /// <summary>Lo que no puede ir dentro de una URL escrita en un texto (espacios, comillas, ángulos, «», `).</summary>
    [GeneratedRegex(@"https?://[^\s<>""'`«»\[\]{}|\\^]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    /// <summary>
    /// Una ruta: opcionalmente «C:\», «.\», «..\» o «~/», carpetas separadas por / o \, y un nombre con extensión; detrás,
    /// opcionalmente «:línea». No empieza pegada a una letra, una barra o un punto (así no corta otra palabra).
    /// </summary>
    [GeneratedRegex(@"(?<![\w/\\.~-])(?<path>(?:[A-Za-z]:[\\/]|\.{1,2}[\\/]|~[\\/]|[\\/])?(?:[\w.@-]*[\w@-][\\/])*[\w@-][\w.@-]*\.[A-Za-z][A-Za-z0-9]{0,7})(?::(?<line>\d{1,7}))?(?![\w/\\-])")]
    private static partial Regex PathPattern();

    /// <summary>Lo escrito entre comillas invertidas (`así`): ahí una ruta puede tener espacios.</summary>
    [GeneratedRegex(@"`(?<path>[^`\r\n]+?)(?::(?<line>\d{1,7}))?`")]
    private static partial Regex QuotedPattern();

    /// <summary>
    /// Los enlaces del texto, en orden. folder: la carpeta de la sesión (las rutas relativas se buscan ahí); exists: si un
    /// archivo existe (en las pruebas, sin tocar el disco).
    /// </summary>
    public static IReadOnlyList<TextLink> Find(string text, string? folder, Func<string, bool>? exists = null)
    {
        var links = new List<TextLink>();
        if (string.IsNullOrEmpty(text)) return links;
        exists ??= File.Exists;
        foreach (Match m in UrlPattern().Matches(text))
        {
            var url = TrimUrl(m.Value);
            if (url.Length > "https://".Length) links.Add(new TextLink(m.Index, url.Length, LinkKind.Url, url));
        }
        // Primero lo que va entre comillas invertidas (puede tener espacios); después las rutas sueltas.
        foreach (Match m in QuotedPattern().Matches(text)) AddFile(m);
        foreach (Match m in PathPattern().Matches(text)) AddFile(m);
        return links.OrderBy(l => l.Start).ToList();

        void AddFile(Match m)
        {
            var path = m.Groups["path"];
            var line = m.Groups["line"];
            var end = line.Success ? line.Index + line.Length : path.Index + path.Length;
            if (links.Any(l => path.Index < l.Start + l.Length && l.Start < end)) return; // ya es parte de otro enlace
            if (Resolve(path.Value.Trim(), folder) is not { } full) return;
            bool found;
            try { found = exists(full); }
            catch (Exception) { found = false; }
            if (!found) return;
            links.Add(new TextLink(path.Index, end - path.Index, LinkKind.File, full,
                line.Success && int.TryParse(line.Value, out var n) ? n : null));
        }
    }

    /// <summary>
    /// Sin lo que suele seguir a una URL en una frase: el punto, la coma, el cierre de un paréntesis que no abrió dentro
    /// de ella (en «(ver https://x.y/z)» el «)» no es de la URL; en «…/Foo_(bar)» sí).
    /// </summary>
    static string TrimUrl(string url)
    {
        while (url.Length > 0)
        {
            var last = url[^1];
            if (".,;:!?…".Contains(last)) url = url[..^1];
            else if (last == ')' && url.Count(c => c == ')') > url.Count(c => c == '(')) url = url[..^1];
            else break;
        }
        return url;
    }

    /// <summary>La ruta completa: absoluta tal cual, «~» la carpeta personal, relativa dentro de la carpeta de la sesión.</summary>
    static string? Resolve(string path, string? folder)
    {
        try
        {
            if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith(@"~\", StringComparison.Ordinal))
                return Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]));
            if (Path.IsPathFullyQualified(path)) return Path.GetFullPath(path);
            if (string.IsNullOrEmpty(folder)) return null;
            return Path.GetFullPath(Path.Combine(folder, path.TrimStart('/', '\\')));
        }
        catch (Exception) { return null; }
    }
}
