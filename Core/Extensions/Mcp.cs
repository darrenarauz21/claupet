using System.Text.Json;
using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>
/// Un servidor MCP que trae una extensión (la ranura «mcp»), tal como lo dice el manifiesto. Type: «stdio» (un
/// programa del paquete, Targets, o del sistema, Program) o «http» (Streamable HTTP en Url). En Args, Env, Url y
/// Headers van {package}, {data} y {setting:clave}; las claves (ajustes «secret») solo en Env y Headers.
/// </summary>
sealed record McpServerSpec
{
    public const string Stdio = "stdio", Http = "http";

    public required string Name { get; init; }
    public required string Type { get; init; }
    public IReadOnlyDictionary<string, string> Targets { get; init; } = new Dictionary<string, string>();
    public string? Program { get; init; }
    public IReadOnlyList<string> Args { get; init; } = [];
    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();
    public string? Url { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
}

/// <summary>Un servidor listo para dárselo a un agente: rutas y ajustes ya puestos.</summary>
sealed record McpServer(string Name, string Type, string? Command, IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string> Env, string? Url, IReadOnlyDictionary<string, string> Headers, string Extension);

/// <summary>
/// Los servidores MCP de las extensiones: se leen del manifiesto y se suman al de Claupet (el del canvas) en Claude
/// Code, Codex y los agentes ACP. Así cualquier servidor MCP que ya exista se instala soltando un .claupet.
/// </summary>
static class ExtensionMcp
{
    public const int MaxServers = 8;

    static readonly Regex NamePattern = new(@"^[a-z][a-z0-9_-]{0,31}$", RegexOptions.CultureInvariant);
    static readonly Regex ProgramPattern = new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);
    static readonly Regex EnvPattern = new(@"^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant);
    static readonly Regex HeaderPattern = new(@"^[A-Za-z0-9-]{1,64}$", RegexOptions.CultureInvariant);
    static readonly Regex Setting = new(@"\{setting:([A-Za-z0-9_]+)\}", RegexOptions.CultureInvariant);

    /// <summary>Los servidores de las extensiones encendidas: en la Console, leídos del disco; en la bandeja, los de su ExtensionManager.</summary>
    public static Func<IReadOnlyList<McpServer>> Source { get; set; } = ExtensionsOnDisk.McpServers;

    public static IReadOnlyList<McpServer> Servers()
    {
        try { return Source(); }
        catch (Exception ex) { ErrorLog.Write(ex); return []; }
    }

    // ── El manifiesto ───────────────────────────────────────────────────────────────────────

    internal static List<McpServerSpec> Parse(JsonElement r, List<string> errors, IReadOnlySet<string>? files,
        IReadOnlyList<SettingSpec> settings, IReadOnlyList<string> permissions)
    {
        var list = new List<McpServerSpec>();
        if (!r.TryGetProperty("mcp", out var v)) return list;
        if (v.ValueKind != JsonValueKind.Object) { errors.Add(L.T("«mcp» debe ser un objeto: un servidor por nombre.")); return list; }
        foreach (var p in v.EnumerateObject())
        {
            var name = p.Name;
            if (!NamePattern.IsMatch(name) || name.StartsWith("claupet", StringComparison.Ordinal))
            {
                errors.Add(L.T("Servidor MCP con nombre inválido: «{0}».", name));
                continue;
            }
            if (p.Value.ValueKind != JsonValueKind.Object) { errors.Add(L.T("El servidor MCP «{0}» debe ser un objeto.", name)); continue; }
            var s = p.Value;
            var type = Json.Str(s, "type") ?? (s.TryGetProperty("url", out _) ? McpServerSpec.Http : McpServerSpec.Stdio);
            var spec = new McpServerSpec
            {
                Name = name, Type = type, Program = Json.Str(s, "program"), Url = Json.Str(s, "url"),
                Args = Strings(s, "args", name, errors), Env = Map(s, "env", name, EnvPattern, errors),
                Headers = Map(s, "headers", name, HeaderPattern, errors), Targets = Targets(s, name, errors, files),
            };
            switch (type)
            {
                case McpServerSpec.Stdio:
                    if ((spec.Targets.Count > 0) == (spec.Program is not null))
                        errors.Add(L.T("El servidor MCP «{0}» lleva «command» (del paquete) o «program» (del sistema), uno de los dos.", name));
                    if (spec.Program is not null && !ProgramPattern.IsMatch(spec.Program))
                        errors.Add(L.T("«program» es solo el nombre de un programa del sistema, sin ruta."));
                    if (spec.Url is not null || spec.Headers.Count > 0) errors.Add(L.T("El servidor MCP «{0}» es «stdio»: no lleva «url» ni «headers».", name));
                    break;
                case McpServerSpec.Http:
                    if (spec.Targets.Count > 0 || spec.Program is not null || spec.Args.Count > 0 || spec.Env.Count > 0)
                        errors.Add(L.T("El servidor MCP «{0}» es «http»: no lleva «command», «program», «args» ni «env».", name));
                    CheckUrl(spec, permissions, errors);
                    break;
                default:
                    errors.Add(L.T("El servidor MCP «{0}»: «type» es «stdio» o «http».", name));
                    break;
            }
            CheckSettings(spec, settings, errors);
            list.Add(spec);
        }
        if (list.Count > MaxServers) errors.Add(L.T("«{0}» trae demasiados elementos.", "mcp"));
        return list;
    }

    static List<string> Strings(JsonElement s, string field, string name, List<string> errors)
    {
        var list = new List<string>();
        if (!s.TryGetProperty(field, out var v)) return list;
        if (v.ValueKind != JsonValueKind.Array) { errors.Add(L.T("«{0}» debe ser una lista.", $"mcp.{name}.{field}")); return list; }
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString()!.Length > 1000 || item.GetString()!.Contains('\0'))
                errors.Add(L.T("«args» trae un argumento inválido."));
            else list.Add(item.GetString()!);
        }
        if (list.Count > 64) errors.Add(L.T("«{0}» trae demasiados elementos.", $"mcp.{name}.{field}"));
        return list;
    }

    static Dictionary<string, string> Map(JsonElement s, string field, string name, Regex keys, List<string> errors)
    {
        var map = new Dictionary<string, string>();
        if (!s.TryGetProperty(field, out var v)) return map;
        if (v.ValueKind != JsonValueKind.Object) { errors.Add(L.T("«{0}» debe ser un objeto.", $"mcp.{name}.{field}")); return map; }
        foreach (var p in v.EnumerateObject())
        {
            var text = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : null;
            if (!keys.IsMatch(p.Name) || text is null || text.Length > 4000 || text.Any(char.IsControl))
                errors.Add(L.T("«{0}» trae un valor inválido: «{1}».", $"mcp.{name}.{field}", p.Name));
            else map[p.Name] = text;
        }
        if (map.Count > 32) errors.Add(L.T("«{0}» trae demasiados elementos.", $"mcp.{name}.{field}"));
        return map;
    }

    static Dictionary<string, string> Targets(JsonElement s, string name, List<string> errors, IReadOnlySet<string>? files)
    {
        var targets = new Dictionary<string, string>();
        if (!s.TryGetProperty("command", out var t)) return targets;
        if (t.ValueKind == JsonValueKind.String) targets["any"] = t.GetString()!;
        else if (t.ValueKind == JsonValueKind.Object)
            foreach (var p in t.EnumerateObject())
            {
                if (!ExtensionPlatform.Known.Contains(p.Name) && p.Name != "any") errors.Add(L.T("Plataforma desconocida en «{0}»: «{1}».", "command", p.Name));
                else if (p.Value.ValueKind == JsonValueKind.String) targets[p.Name] = p.Value.GetString()!;
            }
        else errors.Add(L.T("«{0}» es una ruta o un objeto por plataforma.", "command"));
        foreach (var path in targets.Values)
            if (path.Length == 0 || PackagePaths.Normalize(path) != path)
                errors.Add(L.T("«{0}» debe ser una ruta dentro del paquete, con «/».", $"mcp.{name}.command"));
            else if (files is not null && !files.Contains(path))
                errors.Add(L.T("«{0}» nombra «{1}», que no está en el paquete.", $"mcp.{name}.command", path));
        return targets;
    }

    /// <summary>https (o http en este equipo), a un host que la extensión declaró en «permissions».</summary>
    static void CheckUrl(McpServerSpec spec, IReadOnlyList<string> permissions, List<string> errors)
    {
        var url = spec.Url ?? "";
        var templated = Setting.IsMatch(url);
        var probe = Regex.Replace(url, @"^\{setting:[A-Za-z0-9_]+\}", "https://localhost");
        probe = Setting.Replace(probe, "localhost");
        if (!Uri.TryCreate(probe, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0)
        {
            errors.Add(L.T("«url» debe ser una dirección https."));
            return;
        }
        if (uri.Scheme == "http" && !templated && !uri.IsLoopback) errors.Add(L.T("Por http solo se permite este equipo (localhost); afuera, https."));
        if (templated ? !permissions.Any(p => p == ExtensionPermissions.Network || p.StartsWith("network:", StringComparison.Ordinal))
            : !uri.IsLoopback && !ExtensionPermissions.AllowsHost(permissions, uri.Host))
            errors.Add(L.T("«url» va a {0}: declare «network:{0}» en «permissions».", templated ? "…" : uri.Host));
    }

    /// <summary>Los {setting:clave} nombran ajustes que existen; una clave no va en la dirección ni en los argumentos.</summary>
    static void CheckSettings(McpServerSpec spec, IReadOnlyList<SettingSpec> settings, List<string> errors)
    {
        void Check(string text, bool secretsOk)
        {
            foreach (Match m in Setting.Matches(text))
            {
                var setting = settings.FirstOrDefault(x => x.Key == m.Groups[1].Value);
                if (setting is null) errors.Add(L.T("El servidor MCP «{0}» nombra el ajuste «{1}», que no está en «settings».", spec.Name, m.Groups[1].Value));
                else if (setting.Type == SettingSpec.Secret && !secretsOk)
                    errors.Add(L.T("El servidor MCP «{0}»: la clave «{1}» va solo en «env» o «headers».", spec.Name, setting.Key));
            }
        }
        Check(spec.Url ?? "", false);
        foreach (var a in spec.Args) Check(a, false);
        foreach (var e in spec.Env.Values) Check(e, true);
        foreach (var h in spec.Headers.Values) Check(h, true);
    }

    // ── Para los agentes ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// El servidor con rutas y ajustes puestos. value: el valor de un ajuste de la extensión; findProgram: la ruta de
    /// un programa del sistema. Lanza ExtensionUnavailableException si no se puede (con el motivo para la persona).
    /// </summary>
    public static McpServer Resolve(InstalledExtension ext, McpServerSpec spec, Func<string, string?> value, Func<string, string?> findProgram)
    {
        var label = ext.Manifest.Name.Text;
        string Fill(string text) => Setting.Replace(text.Replace("{package}", ext.Folder).Replace("{data}", ext.DataFolder), m => value(m.Groups[1].Value) ?? "");
        if (spec.Type == McpServerSpec.Http)
        {
            var url = Fill(spec.Url ?? "");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0)
                throw new ExtensionUnavailableException(L.T("La dirección del servidor de «{0}» no es válida: revise sus ajustes.", label));
            if (!uri.IsLoopback && (uri.Scheme != "https" || !ExtensionPermissions.AllowsHost(ext.Manifest.Permissions, uri.Host)))
                throw new ExtensionUnavailableException(L.T("«{0}» quiere conectarse a {1}, que no declaró.", label, uri.Host));
            var headers = spec.Headers.ToDictionary(h => h.Key, h => Fill(h.Value)).Where(h => h.Value.Length > 0).ToDictionary();
            return new McpServer(spec.Name, spec.Type, null, [], new Dictionary<string, string>(), uri.ToString(), headers, ext.Id);
        }
        string command;
        if (spec.Program is { } program)
            command = findProgram(program) ?? throw new ExtensionUnavailableException(L.T("«{0}» necesita «{1}», que no está instalado (o no está en el PATH).", label, program));
        else
            command = ext.FileOf((spec.Targets.TryGetValue(ExtensionPlatform.Current, out var t) ? t : spec.Targets.GetValueOrDefault("any"))
                ?? throw new ExtensionUnavailableException(L.T("«{0}» no trae su programa para {1}.", label, ExtensionPlatform.Current)));
        var env = new Dictionary<string, string>
        {
            ["CLAUPET_EXTENSION_ID"] = ext.Id,
            ["CLAUPET_PACKAGE"] = ext.Folder,
            ["CLAUPET_DATA"] = ext.DataFolder,
            ["CLAUPET_LANGUAGE"] = L.Language,
        };
        foreach (var (k, v) in spec.Env) env[k] = Fill(v);
        return new McpServer(spec.Name, spec.Type, command, [.. spec.Args.Select(Fill)], env, null, new Dictionary<string, string>(), ext.Id);
    }
}
