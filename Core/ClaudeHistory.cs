using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>
/// Una sesión anterior de Claude Code en una carpeta: su id, su título, cuándo se usó y lo primero que se pidió. Named:
/// el título es uno de verdad (el que guardó Claude Code o uno escrito por el asistente); si no, es el pedido recortado.
/// </summary>
sealed record PastSession(string Id, string Title, DateTime LastUsed, string Path, string Prompt = "", bool Named = true);

/// <summary>
/// El historial de Claude Code de una carpeta, para retomarlo desde Claupet Terminal: cada sesión es un .jsonl en
/// &lt;cuenta&gt;/projects/&lt;carpeta con lo que no es letra o número cambiado por «-»&gt;/. Sus líneas tienen la misma forma que
/// la salida stream-json, así que la conversación se vuelve a armar con AgentTranscript. Solo se escribe para borrar
/// sesiones (Delete, DeleteAll), cuando se pide desde el Historial.
/// </summary>
static partial class ClaudeHistory
{
    /// <summary>La carpeta de proyecto de Claude Code para esa carpeta de trabajo.</summary>
    public static string ProjectDir(string configDir, string folder) =>
        Path.Combine(configDir, "projects", NotAlphanumeric().Replace(Path.TrimEndingDirectorySeparator(folder), "-"));

    /// <summary>
    /// Las sesiones de esa carpeta, la más reciente primero (como mucho limit). Con query, solo las que la tienen en el
    /// título o en el primer pedido (sin importar mayúsculas ni tildes).
    /// </summary>
    public static IReadOnlyList<PastSession> List(string configDir, string folder, int limit = 40, string? query = null)
    {
        var dir = ProjectDir(configDir, folder);
        var list = new List<PastSession>();
        query = query?.Trim();
        try
        {
            if (!Directory.Exists(dir)) return list;
            foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*.jsonl").OrderByDescending(f => f.LastWriteTimeUtc))
            {
                var id = System.IO.Path.GetFileNameWithoutExtension(file.Name);
                if (!Guid.TryParse(id, out _)) continue;
                var (named, prompt) = Read(file);
                if (prompt is null && named is null) continue;
                var title = named ?? SessionTitles.Cached(id);
                var past = new PastSession(id, title ?? SessionTitles.Short(prompt!), file.LastWriteTime, file.FullName, prompt ?? "", title is not null);
                if (query is { Length: > 0 } && !Matches(past, query)) continue;
                list.Add(past);
                if (list.Count >= limit) break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return list;
    }

    /// <summary>Una sesión por su id (aunque no esté entre las más recientes); null si no está en esa carpeta.</summary>
    public static PastSession? Find(string configDir, string folder, string id)
    {
        if (!Guid.TryParse(id, out _)) return null;
        var file = new FileInfo(System.IO.Path.Combine(ProjectDir(configDir, folder), id + ".jsonl"));
        if (!file.Exists) return null;
        var (named, prompt) = Read(file);
        var title = named ?? SessionTitles.Cached(id) ?? (prompt is null ? id : SessionTitles.Short(prompt));
        return new PastSession(id, title, file.LastWriteTime, file.FullName, prompt ?? "", named is not null);
    }

    /// <summary>La búsqueda está en el título o en el pedido (sin mayúsculas ni tildes).</summary>
    internal static bool Matches(PastSession past, string query)
    {
        var wanted = Fold(query);
        return Fold(past.Title).Contains(wanted, StringComparison.Ordinal) || Fold(past.Prompt).Contains(wanted, StringComparison.Ordinal);
    }

    /// <summary>En minúsculas y sin tildes («Rediseño» → «rediseno»), sea cual sea el idioma del equipo.</summary>
    static string Fold(string text)
    {
        var plain = new System.Text.StringBuilder(text.Length);
        foreach (var c in text.Normalize(System.Text.NormalizationForm.FormD))
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                plain.Append(char.ToLowerInvariant(c));
        return plain.ToString();
    }

    /// <summary>Lo leído de cada archivo, mientras no cambie: «Cargar más» y la búsqueda no lo vuelven a leer.</summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Stamp, long Length, string? Named, string? Prompt)> Seen =
        new(StringComparer.OrdinalIgnoreCase);

    static (string? Named, string? Prompt) Read(FileInfo file)
    {
        if (Seen.TryGetValue(file.FullName, out var seen) && seen.Stamp == file.LastWriteTimeUtc && seen.Length == file.Length)
            return (seen.Named, seen.Prompt);
        var (named, prompt) = Title(file.FullName);
        Seen[file.FullName] = (file.LastWriteTimeUtc, file.Length, named, prompt);
        return (named, prompt);
    }

    /// <summary>
    /// Borra una sesión del historial: su .jsonl, la carpeta con su mismo id al lado (subagentes, resultados largos) y
    /// sus copias de archivos (file-history). false si no se pudo borrar el .jsonl.
    /// </summary>
    public static bool Delete(string configDir, string path)
    {
        var id = System.IO.Path.GetFileNameWithoutExtension(path);
        // Solo un .jsonl de sesión (su nombre es un id): nunca otra cosa.
        if (!Guid.TryParse(id, out _) || !path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        foreach (var dir in new[] { System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, id), System.IO.Path.Combine(configDir, "file-history", id) })
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return true;
    }

    /// <summary>Borra todas las sesiones de esa carpeta menos las de keep (las abiertas en pestañas). Cuántas borró.</summary>
    public static int DeleteAll(string configDir, string folder, IReadOnlySet<string> keep)
    {
        var dir = ProjectDir(configDir, folder);
        var deleted = 0;
        try
        {
            if (!Directory.Exists(dir)) return 0;
            foreach (var file in Directory.GetFiles(dir, "*.jsonl"))
            {
                var id = System.IO.Path.GetFileNameWithoutExtension(file);
                if (Guid.TryParse(id, out _) && !keep.Contains(id) && Delete(configDir, file)) deleted++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return deleted;
    }

    /// <summary>
    /// De qué trató: el título que guardó Claude Code (el que se le puso con /rename, si no el que escribió su IA, si no
    /// un resumen) y lo primero que se pidió (en una línea, hasta 600 caracteres). Ambos null si no hay ningún pedido.
    /// </summary>
    internal static (string? Named, string? Prompt) Title(string path)
    {
        string? first = null, custom = null, ai = null, summary = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            for (var i = 0; i < 400 && reader.ReadLine() is { } line; i++)
            {
                if (line.Length == 0 || line[0] != '{') continue;
                JsonDocument doc;
                try { doc = JsonDocument.Parse(line); }
                catch (JsonException) { continue; }
                using (doc)
                {
                    var r = doc.RootElement;
                    switch (Json.Str(r, "type"))
                    {
                        case "custom-title" when Json.Str(r, "customTitle") is { Length: > 0 } t: custom = t; break;
                        case "ai-title" when Json.Str(r, "aiTitle") is { Length: > 0 } t: ai = t; break;
                        case "summary" when Json.Str(r, "summary") is { Length: > 0 } t: summary ??= t; break;
                        default:
                            if (first is null && Prompt(r) is { } prompt) first = prompt;
                            break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var named = (custom ?? ai ?? summary)?.ReplaceLineEndings(" ").Trim();
        first = first?.ReplaceLineEndings(" ").Trim();
        if (first is { Length: > SessionTitles.PromptLimit }) first = first[..SessionTitles.PromptLimit];
        // Sin pedido no se muestra (aunque tenga un resumen): es la de otra sesión, como antes.
        return first is null ? (null, null) : (named is { Length: > 0 } ? named : null, first);
    }

    /// <summary>Lo que la persona escribió en esa línea; null si es otra cosa (un resultado, un comando, un aviso interno).</summary>
    static string? Prompt(JsonElement r)
    {
        if (Json.Str(r, "type") != "user" || !r.TryGetProperty("message", out var m)) return null;
        if (Flag(r, "isMeta") || Flag(r, "isSidechain") || Flag(r, "isCompactSummary")) return null;
        if (!m.TryGetProperty("content", out var content)) return null;
        string text;
        if (content.ValueKind == JsonValueKind.String) text = content.GetString() ?? "";
        else if (content.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            var images = 0;
            foreach (var part in content.EnumerateArray())
                switch (Json.Str(part, "type"))
                {
                    case "tool_result": return null;
                    case "text" when Json.Str(part, "text") is { } t: parts.Add(t); break;
                    case "image": images++; break;
                }
            text = string.Join("\n", parts);
            for (var i = 1; i <= images && !text.Contains("[Image", StringComparison.Ordinal); i++) text = $"[Image #{i}] " + text;
        }
        else return null;
        // La nota de Claupet al final del pedido (Workspace) no es de la persona.
        text = Workspace.Strip(text).Trim();
        // Los comandos (/model, /clear…), su salida y los recordatorios del sistema no son pedidos.
        if (text.Length == 0 || text.StartsWith('<') || text.StartsWith("[Request interrupted", StringComparison.Ordinal)) return null;
        return text;
    }

    static bool Flag(JsonElement r, string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>
    /// Vuelve a armar la conversación de una sesión en transcript: lo pedido, las respuestas, los pasos con sus
    /// resultados y las tareas. Lo de los subagentes queda afuera (como en la sesión).
    /// </summary>
    public static void Replay(string path, AgentTranscript transcript)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0 || line[0] != '{') continue;
                JsonDocument doc;
                try { doc = JsonDocument.Parse(line); }
                catch (JsonException) { continue; }
                using (doc)
                {
                    var r = doc.RootElement;
                    if (Flag(r, "isSidechain")) continue;
                    switch (Json.Str(r, "type"))
                    {
                        case "user" when Prompt(r) is { } prompt:
                            transcript.Past(prompt);
                            break;
                        case "user":
                            // Los resultados de los pasos; el detalle (el diff) viene como toolUseResult en el .jsonl.
                            if (JsonNode.Parse(line) is JsonObject node && node["toolUseResult"] is { } detail)
                            {
                                node["tool_use_result"] = detail.DeepClone();
                                transcript.Apply(node.ToJsonString());
                            }
                            else transcript.Apply(line);
                            break;
                        case "assistant":
                            transcript.Apply(line);
                            break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        transcript.EndReplay(System.IO.Path.GetFileNameWithoutExtension(path));
    }

    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex NotAlphanumeric();
}
