using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet;

/// <summary>Un modelo que ofrece Claude Code: lo que se le pasa (un alias o el ID), el modelo al que va y su nombre.</summary>
sealed record ClaudeModel(string Value, string Resolved, string Label);

/// <summary>
/// Los modelos de Claude, leídos de Claude Code: cada sesión los trae en la respuesta a initialize (los mismos de /model,
/// con el modelo al que apunta hoy cada alias y su nombre). Se guardan para la próxima vez: así los menús y los nombres
/// («Fable 5.2») siguen a Claude Code y la app no se toca cuando sale un modelo nuevo o un alias cambia de versión.
/// </summary>
static class ClaudeModels
{
    /// <summary>Lo que se usa mientras Claude Code no haya respondido nunca (la primera vez que se abre la app).</summary>
    static readonly ClaudeModel[] Fallback =
    [
        new("haiku", "claude-haiku-5-5", "Haiku 5.5"),
        new("sonnet", "claude-sonnet-5-5", "Sonnet 5.5"),
        new("opus", "claude-opus-5-5", "Opus 5.5"),
        new("fable", "claude-fable-5-1", "Fable 5.1"),
    ];

    /// <summary>Opusplan no sale en /model, pero Claude Code lo sigue aceptando: Opus para planear, Sonnet para hacer.</summary>
    public const string OpusPlan = "opusplan";

    /// <summary>El orden de los menús: del más rápido al más capaz. Las familias que no están aquí van al final.</summary>
    static readonly string[] Families = ["haiku", "sonnet", "opus", "fable"];

    static readonly object Gate = new();
    static IReadOnlyList<ClaudeModel>? _known;

    static string FilePath => DataFolder.In("modelos-claude.json");

    /// <summary>Los últimos que dijo Claude Code (o los de la app, si todavía no dijo nada).</summary>
    public static IReadOnlyList<ClaudeModel> Known
    {
        get
        {
            lock (Gate) return _known ??= Load(FilePath) ?? Fallback;
        }
    }

    /// <summary>Lo que dijo Claude Code al abrir una sesión: si cambió, se usa desde ya y se guarda para la próxima vez.</summary>
    public static void Learn(IReadOnlyList<ClaudeModel> models)
    {
        if (models.Count == 0) return;
        lock (Gate)
        {
            if (_known is not null && _known.SequenceEqual(models)) return;
            _known = models;
            Save(FilePath, models);
        }
    }

    /// <summary>La lista «models» de la respuesta a initialize; sin «default», que en los menús es «por defecto».</summary>
    public static IReadOnlyList<ClaudeModel> Parse(JsonElement models)
    {
        if (models.ValueKind != JsonValueKind.Array) return [];
        var list = new List<ClaudeModel>();
        foreach (var m in models.EnumerateArray())
        {
            if (m.ValueKind != JsonValueKind.Object || Json.Str(m, "value") is not { Length: > 0 } value || value == "default") continue;
            if (!Launcher.IsValid(value) || list.Any(k => k.Value == value)) continue;
            var resolved = Json.Str(m, "resolvedModel") is { Length: > 0 } r ? r : value;
            var label = Json.Str(m, "displayName") is { Length: > 0 } d ? d : Format.Model(resolved);
            list.Add(new ClaudeModel(value, resolved, label));
        }
        return list;
    }

    /// <summary>Todo lo que se puede elegir en una sesión: lo que ofrece Claude Code, en el orden de los menús, y opusplan.</summary>
    public static IReadOnlyList<string> Choices(IReadOnlyList<ClaudeModel> models) =>
        [.. Sorted(models).Select(m => m.Value), OpusPlan];

    /// <summary>
    /// Los de los menús cortos (la pestaña nueva, los especialistas): los alias y los modelos más nuevos que el de su alias
    /// («claude-fable-5-2» mientras «fable» vaya a la 5.1). Los anteriores se eligen dentro de la sesión.
    /// </summary>
    public static IReadOnlyList<string> Featured(IReadOnlyList<ClaudeModel> models)
    {
        var aliases = models.Where(IsAlias).ToList();
        return [.. Sorted(models.Where(m => IsAlias(m) || NewerThanItsAlias(m, aliases))).Select(m => m.Value)];
    }

    /// <summary>El nombre que le da Claude Code («Fable 5.2»), o null si no está entre los que ofrece.</summary>
    public static string? Label(string value) =>
        Known.FirstOrDefault(m => string.Equals(m.Value, value, StringComparison.OrdinalIgnoreCase))?.Label;

    /// <summary>El modelo al que va un alias hoy («opus» → «claude-opus-5-5»), o null si Claude Code no lo ofrece.</summary>
    public static string? Resolve(string alias) =>
        Known.FirstOrDefault(m => string.Equals(m.Value, alias, StringComparison.OrdinalIgnoreCase))?.Resolved;

    /// <summary>
    /// Cuál de las opciones es el modelo que respondió (used): el mismo ID o, si vino por un alias, ese alias (el que se
    /// eligió al abrir, si es uno de los que van ahí). Null si ninguno: entonces vale lo que se eligió al abrir.
    /// </summary>
    public static string? ChoiceFor(string? used, IReadOnlyList<string> choices, string? chosen, IReadOnlyList<ClaudeModel> offered)
    {
        if (used is null) return chosen;
        if (choices.Contains(used)) return used;
        var aliases = offered.Where(m => m.Resolved == used && choices.Contains(m.Value)).Select(m => m.Value).ToList();
        return chosen is not null && aliases.Contains(chosen) ? chosen : aliases.FirstOrDefault() ?? chosen;
    }

    static bool IsAlias(ClaudeModel m) => !m.Value.StartsWith("claude-", StringComparison.OrdinalIgnoreCase);

    static bool NewerThanItsAlias(ClaudeModel m, List<ClaudeModel> aliases) =>
        Family(m.Resolved) is { } mine
        && aliases.Select(a => Family(a.Resolved)).FirstOrDefault(a => a?.Name == mine.Name) is { } current
        && mine.Version > current.Version;

    /// <summary>Por familia (Haiku, Sonnet, Opus, Fable, las demás) y, dentro de cada una, del más nuevo al más viejo.</summary>
    static IEnumerable<ClaudeModel> Sorted(IEnumerable<ClaudeModel> models) => models
        .Select((m, index) => (Model: m, Index: index, Family: Family(m.Resolved)))
        .OrderBy(x => x.Family is { } f && Array.IndexOf(Families, f.Name) is >= 0 and var rank ? rank : Families.Length)
        .ThenByDescending(x => x.Family?.Version)
        .ThenBy(x => IsAlias(x.Model) ? 0 : 1)
        .ThenBy(x => x.Index)
        .Select(x => x.Model);

    /// <summary>«claude-fable-5-2» → (fable, 5.2); «claude-haiku-4-5-20251001» → (haiku, 4.5); lo demás, null.</summary>
    static (string Name, Version Version)? Family(string id)
    {
        if (!id.StartsWith("claude-", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = id[7..].Split('-');
        if (parts[0].Length == 0 || !char.IsLetter(parts[0][0])) return null;
        var numbers = parts.Skip(1).TakeWhile(p => p.Length is > 0 and <= 2 && p.All(char.IsDigit)).Select(int.Parse).ToList();
        if (numbers.Count == 0) return null;
        return (parts[0].ToLowerInvariant(), new Version(numbers[0], numbers.Count > 1 ? numbers[1] : 0));
    }

    internal static IReadOnlyList<ClaudeModel>? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var list = new List<ClaudeModel>();
            foreach (var m in doc.RootElement.EnumerateArray())
                if (Json.Str(m, "value") is { Length: > 0 } value && Launcher.IsValid(value)
                    && Json.Str(m, "resolved") is { Length: > 0 } resolved && Json.Str(m, "label") is { Length: > 0 } label)
                    list.Add(new ClaudeModel(value, resolved, label));
            return list.Count > 0 ? list : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return null; }
    }

    internal static void Save(string path, IReadOnlyList<ClaudeModel> models)
    {
        try
        {
            var array = new JsonArray();
            foreach (var m in models)
                array.Add(new JsonObject { ["value"] = m.Value, ["resolved"] = m.Resolved, ["label"] = m.Label });
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, array.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
