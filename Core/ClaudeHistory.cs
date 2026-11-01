using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>Una sesión anterior de Claude Code en una carpeta: su id, de qué trató (lo primero que se pidió) y cuándo se usó.</summary>
sealed record PastSession(string Id, string Title, DateTime LastUsed, string Path);

/// <summary>
/// El historial de Claude Code de una carpeta, para retomarlo desde Claupet Terminal: cada sesión es un .jsonl en
/// &lt;cuenta&gt;/projects/&lt;carpeta con lo que no es letra o número cambiado por «-»&gt;/. Sus líneas tienen la misma forma que
/// la salida stream-json, así que la conversación se vuelve a armar con AgentTranscript. Solo lectura.
/// </summary>
static partial class ClaudeHistory
{
    /// <summary>La carpeta de proyecto de Claude Code para esa carpeta de trabajo.</summary>
    public static string ProjectDir(string configDir, string folder) =>
        Path.Combine(configDir, "projects", NotAlphanumeric().Replace(Path.TrimEndingDirectorySeparator(folder), "-"));

    /// <summary>Las sesiones de esa carpeta, la más reciente primero (como mucho limit).</summary>
    public static IReadOnlyList<PastSession> List(string configDir, string folder, int limit = 40)
    {
        var dir = ProjectDir(configDir, folder);
        var list = new List<PastSession>();
        try
        {
            if (!Directory.Exists(dir)) return list;
            foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*.jsonl").OrderByDescending(f => f.LastWriteTimeUtc).Take(limit * 2))
            {
                if (!Guid.TryParse(System.IO.Path.GetFileNameWithoutExtension(file.Name), out _)) continue;
                if (Title(file.FullName) is not { } title) continue;
                list.Add(new PastSession(System.IO.Path.GetFileNameWithoutExtension(file.Name), title, file.LastWriteTime, file.FullName));
                if (list.Count >= limit) break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return list;
    }

    /// <summary>De qué trató: el resumen que guardó Claude Code o lo primero que se pidió; null si no hay ningún pedido.</summary>
    static string? Title(string path)
    {
        string? first = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            for (var i = 0; i < 400 && reader.ReadLine() is { } line; i++)
            {
                if (line.Length == 0 || line[0] != '{') continue;
                using var doc = JsonDocument.Parse(line);
                var r = doc.RootElement;
                if (Json.Str(r, "type") == "summary" && Json.Str(r, "summary") is { Length: > 0 } summary) return summary;
                if (first is null && Prompt(r) is { } prompt) first = prompt;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return first is null ? null : first.ReplaceLineEndings(" ").Trim();
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
        text = text.Trim();
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
