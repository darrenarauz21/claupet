using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>
/// Lee un minicontrato: la cabecera YAML (solo lo que usa el formato: «clave: valor», listas «[a, b]» o con guiones,
/// comillas y comentarios) y las reglas «- [id] texto `check: nombre`» del cuerpo.
/// </summary>
static partial class ContractParser
{
    [GeneratedRegex(@"^\s*[-*]\s+\[([a-z0-9][a-z0-9-]*)\]\s*(.*)$")]
    private static partial Regex RuleLine();

    [GeneratedRegex(@"`check:\s*([a-z0-9][a-z0-9-]*)`")]
    private static partial Regex CheckTag();

    [GeneratedRegex(@"^[a-z][a-z0-9-]*(\.[a-z0-9][a-z0-9-]*)*$")]
    private static partial Regex IdShape();

    public static ContractDoc Parse(string text, string path, ContractLayer layer)
    {
        var doc = new ContractDoc { Path = path, Layer = layer };
        text = text.Replace("\r\n", "\n").TrimStart('﻿');
        var lines = text.Split('\n');
        var start = 0;
        while (start < lines.Length && lines[start].Trim().Length == 0) start++;
        if (start >= lines.Length || lines[start].Trim() != "---")
        {
            doc.Issues.Add(new(ContractSeverity.Error, path, "Falta la cabecera entre «---»."));
            doc.Body = text;
            return doc;
        }
        var end = Array.FindIndex(lines, start + 1, l => l.Trim() == "---");
        if (end < 0)
        {
            doc.Issues.Add(new(ContractSeverity.Error, path, "La cabecera no se cierra con «---»."));
            doc.Body = text;
            return doc;
        }
        var header = ReadHeader(lines[(start + 1)..end], path, doc.Issues);
        Apply(doc, header);
        doc.Body = string.Join('\n', lines[(end + 1)..]).Trim('\n');
        ReadRules(doc);
        Check(doc);
        return doc;
    }

    /// <summary>La cabecera como diccionario: un valor de texto o una lista.</summary>
    internal static Dictionary<string, object> ReadHeader(string[] lines, string path, List<ContractIssue> issues)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        string? listKey = null;
        foreach (var raw in lines)
        {
            var line = StripComment(raw);
            if (line.Trim().Length == 0) continue;
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("- ") || trimmed == "-")
            {
                if (listKey is null || result[listKey] is not List<string> list)
                {
                    issues.Add(new(ContractSeverity.Error, path, $"Elemento de lista sin clave: «{raw.Trim()}»."));
                    continue;
                }
                list.Add(Unquote(trimmed[1..].Trim()));
                continue;
            }
            var colon = line.IndexOf(':');
            if (colon <= 0 || char.IsWhiteSpace(line[0]))
            {
                issues.Add(new(ContractSeverity.Error, path, $"Línea de la cabecera que no se entiende: «{raw.Trim()}»."));
                listKey = null;
                continue;
            }
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (value.Length == 0)
            {
                result[key] = new List<string>();
                listKey = key;
            }
            else if (value.StartsWith('['))
            {
                if (!value.EndsWith(']')) issues.Add(new(ContractSeverity.Error, path, $"La lista de «{key}» no se cierra con «]»."));
                result[key] = SplitList(value.TrimStart('[').TrimEnd(']'));
                listKey = null;
            }
            else
            {
                result[key] = Unquote(value);
                listKey = null;
            }
        }
        return result;
    }

    static string StripComment(string line)
    {
        var quote = '\0';
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            if (c is '"' or '\'') quote = c;
            else if (c == '#' && (i == 0 || char.IsWhiteSpace(line[i - 1]))) return line[..i];
        }
        return line;
    }

    static List<string> SplitList(string inner)
    {
        var items = new List<string>();
        var current = new System.Text.StringBuilder();
        var quote = '\0';
        foreach (var c in inner)
        {
            if (quote != '\0') { if (c == quote) quote = '\0'; else current.Append(c); continue; }
            if (c is '"' or '\'') { quote = c; continue; }
            if (c == ',') { Add(); continue; }
            current.Append(c);
        }
        Add();
        return items;

        void Add()
        {
            var item = current.ToString().Trim();
            if (item.Length > 0) items.Add(item);
            current.Clear();
        }
    }

    static string Unquote(string value) =>
        value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\'') ? value[1..^1] : value;

    static void Apply(ContractDoc doc, Dictionary<string, object> header)
    {
        foreach (var (key, value) in header)
        {
            var text = value as string ?? "";
            var list = value as List<string> ?? (text.Length > 0 ? [text] : []);
            switch (key)
            {
                case "id": doc.Id = text.Trim(); break;
                case "tipo": doc.Type = text.Trim().ToLowerInvariant(); break;
                case "titulo": doc.Title = text; break;
                case "resumen": doc.Summary = text; break;
                case "aplica": doc.AppliesTo = list; break;
                case "etiquetas": doc.Tags = list; break;
                case "apaga_reglas": doc.OffRules = list; break;
                case "idioma": doc.Language = text; break;
                case "actualizado": doc.Updated = text; break;
                case "prioridad":
                    doc.Required = text.ToLowerInvariant() switch
                    {
                        "obligatoria" or "required" => true,
                        "recomendada" or "recommended" => false,
                        _ => Bad(doc, $"«prioridad» debe ser obligatoria o recomendada, no «{text}»."),
                    };
                    break;
                case "modo":
                    doc.Mode = text.ToLowerInvariant() switch
                    {
                        "nuevo" or "new" => ContractMode.Nuevo,
                        "amplia" or "amplía" or "extend" => ContractMode.Amplia,
                        "reemplaza" or "replace" => ContractMode.Reemplaza,
                        "apaga" or "off" => ContractMode.Apaga,
                        _ => BadMode(doc, text),
                    };
                    break;
                case "formato":
                    if (int.TryParse(text, out var format)) doc.Format = format;
                    else doc.Issues.Add(new(ContractSeverity.Error, doc.Path, $"«formato» debe ser un número, no «{text}»."));
                    break;
                default: doc.Extra[key] = value is List<string> l ? string.Join(", ", l) : text; break;
            }
        }
    }

    static bool? Bad(ContractDoc doc, string message)
    {
        doc.Issues.Add(new(ContractSeverity.Error, doc.Path, message));
        return null;
    }

    static ContractMode BadMode(ContractDoc doc, string text)
    {
        doc.Issues.Add(new(ContractSeverity.Error, doc.Path, $"«modo» debe ser nuevo, amplia, reemplaza o apaga, no «{text}»."));
        return ContractMode.Nuevo;
    }

    static void ReadRules(ContractDoc doc)
    {
        var inFence = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in doc.Body.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```")) { inFence = !inFence; continue; }
            if (inFence) continue;
            var m = RuleLine().Match(line);
            if (!m.Success) continue;
            var id = m.Groups[1].Value;
            var text = m.Groups[2].Value;
            var checks = CheckTag().Matches(text).Select(c => c.Groups[1].Value).ToList();
            text = CheckTag().Replace(text, "").Trim();
            if (!seen.Add(id))
            {
                doc.Issues.Add(new(ContractSeverity.Error, doc.Path, $"La regla «{id}» está repetida."));
                continue;
            }
            doc.Rules.Add(new ContractRule(id, text, checks) { Layer = doc.Layer });
        }
    }

    /// <summary>Lo que exige el formato, sin mirar las otras capas.</summary>
    static void Check(ContractDoc doc)
    {
        void Error(string m) => doc.Issues.Add(new(ContractSeverity.Error, doc.Path, m));
        void Warn(string m) => doc.Issues.Add(new(ContractSeverity.Aviso, doc.Path, m));

        if (doc.Id.Length == 0) { Error("Falta «id»."); return; }
        if (!IdShape().IsMatch(doc.Id)) Error($"El id «{doc.Id}» debe ir en minúsculas, con puntos y guiones (tipo.nombre).");
        // Los que solo modifican a otro pueden traer nada más que lo que cambian.
        if (doc.Mode is ContractMode.Amplia or ContractMode.Apaga) return;
        if (doc.Type.Length == 0) Error("Falta «tipo».");
        else if (!ContractDoc.Types.Contains(doc.Type)) Error($"«tipo» no es válido: «{doc.Type}» (se esperaba {string.Join(", ", ContractDoc.Types)}).");
        else if (doc.Type != "base" && !doc.Id.StartsWith(doc.Type + ".", StringComparison.Ordinal))
            Warn($"El id «{doc.Id}» debería empezar por «{doc.Type}.».");
        if (doc.Title.Length == 0) Error("Falta «titulo».");
        if (doc.Summary.Length == 0) Error("Falta «resumen».");
        else if (doc.Summary.Contains('\n') || doc.Summary.Length > 140) Warn("El «resumen» debe caber en una línea: va en el índice.");
        if (doc.AppliesTo.Count == 0) Error("Falta «aplica» (las rutas a las que aplica, por ejemplo [\"**/*\"]).");
        if (doc.Updated is null) Warn("Falta «actualizado».");
        if (doc.Rules.Count > 40) Warn($"Tiene {doc.Rules.Count} reglas: con más de 40 conviene dividirlo.");
        if ((doc.Body.Length + doc.Summary.Length) / 4 > 1500) Warn("Pasa de unos 1.500 tokens: conviene dividirlo.");
    }
}
