using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace Claupet;

/// <summary>
/// El comando cpt (también «claupet»), al estilo de Conda: «cpt &lt;grupo&gt; &lt;acción&gt; [opciones]», con grupos y
/// acciones en español e inglés. Vive aquí y no en Claupet.Cli para que las pruebas lo usen directo; el ejecutable solo
/// llama a Run. Por ahora trae el grupo contracts y el servidor MCP; los demás grupos llegan después (plan c20).
/// </summary>
sealed class CptCli
{
    const int Ok = 0, Failed = 1, Usage = 2;

    readonly TextWriter _out;
    readonly TextWriter _err;
    readonly string _cwd;
    bool _es = true, _json, _quiet, _dryRun;

    CptCli(TextWriter output, TextWriter error, string cwd)
    {
        _out = output;
        _err = error;
        _cwd = cwd;
    }

    /// <summary>Corre un comando y devuelve el código de salida (0 bien, 1 falló, 2 uso incorrecto).</summary>
    public static int Run(string[] args, TextWriter output, TextWriter error, string cwd, string? language = null) =>
        new CptCli(output, error, cwd).Execute(args, language);

    public static string Version => typeof(CptCli).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    static readonly string[] ContractGroup = ["contracts", "contratos", "c"];

    /// <summary>Las acciones del grupo contracts: el nombre principal (en inglés) y sus alias.</summary>
    static readonly (string Name, string[] Aliases)[] ContractActions =
    [
        ("init", ["init", "iniciar"]),
        ("list", ["list", "listar", "ls"]),
        ("read", ["read", "leer", "show", "ver"]),
        ("search", ["search", "buscar"]),
        ("index", ["index", "indice", "índice"]),
        ("validate", ["validate", "validar"]),
    ];

    /// <summary>Lo que está en la especificación pero todavía no en el motor.</summary>
    static readonly string[] Coming = ["select", "seleccionar", "check", "revisar", "change", "cambio", "lesson", "leccion", "feedback", "propose", "proponer", "approve", "aprobar", "reject", "descartar", "eject", "copiar"];

    static readonly string[] OtherGroups = ["plan", "canvas", "ext", "extension", "skill", "hooks", "open", "abrir", "info", "doctor", "update", "config"];

    int Execute(string[] args, string? language)
    {
        var rest = new List<string>();
        string? folder = null, lang = language;
        var help = false;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--json": _json = true; break;
                case "-q" or "--quiet": _quiet = true; break;
                case "--dry-run": _dryRun = true; break;
                case "-y" or "--yes": break; // nada pregunta todavía; se acepta para scripts
                case "-h" or "--help" or "--ayuda": help = true; break;
                case "-C" when i + 1 < args.Length: folder = args[++i]; break;
                case "--lang" when i + 1 < args.Length: lang = args[++i]; break;
                default:
                    if (a.StartsWith("--lang=", StringComparison.Ordinal)) lang = a[7..];
                    else rest.Add(a);
                    break;
            }
        }
        _es = (lang ?? DetectLanguage()).StartsWith("es", StringComparison.OrdinalIgnoreCase);
        var cwd = folder is null ? _cwd : Path.GetFullPath(Path.Combine(_cwd, folder));

        if (rest.Count == 0) return PrintHelp(null);
        var first = rest[0].ToLowerInvariant();
        if (first is "-v" or "--version" or "version") return PrintVersion();
        if (first is "help" or "ayuda") return PrintHelp(rest.Count > 1 ? rest[1] : null);
        if (first == "mcp") return ContractMcp.Run(cwd, _es);
        if (ContractGroup.Contains(first))
        {
            if (rest.Count == 1 || help) return PrintHelp("contracts");
            return Contracts(rest[1].ToLowerInvariant(), rest.Skip(2).ToList(), cwd);
        }
        if (OtherGroups.Contains(first))
            return Fail(S($"«{rest[0]}» todavía no está en cpt: llega en una próxima versión.", $"«{rest[0]}» is not in cpt yet: it comes in a later version."), Usage);
        if (LooksLikePath(rest[0], cwd))
            return Fail(S("Abrir carpetas y archivos con «cpt .» llega con la integración en Claupet.", "Opening folders and files with «cpt .» comes with the Claupet integration."), Usage);
        return Unknown(rest[0], ContractGroup.Concat(OtherGroups).Concat(["mcp", "help", "ayuda", "version"]), null);
    }

    string S(string es, string en) => _es ? es : en;

    // ── contracts ────────────────────────────────────────────────────────────────────────────

    int Contracts(string action, List<string> args, string cwd)
    {
        var name = ContractActions.FirstOrDefault(a => a.Aliases.Contains(action)).Name;
        if (name is null)
        {
            if (Coming.Contains(action))
                return Fail(S($"«{action}» está en la especificación pero todavía no en el motor.", $"«{action}» is in the specification but not in the engine yet."), Usage);
            return Unknown(action, ContractActions.SelectMany(a => a.Aliases), "contracts");
        }
        return name switch
        {
            "init" => Init(cwd),
            "list" => List(cwd),
            "read" => args.Count == 0 ? Fail(S("Falta el id: cpt contracts read <id>", "Missing id: cpt contracts read <id>"), Usage) : Read(cwd, args[0]),
            "search" => args.Count == 0 ? Fail(S("Falta qué buscar: cpt contracts search <texto>", "Missing query: cpt contracts search <text>"), Usage) : Search(cwd, string.Join(' ', args)),
            "index" => Index(cwd),
            _ => Validate(cwd),
        };
    }

    int Init(string cwd)
    {
        var root = ContractStore.FindProject(cwd) is { } existing ? ContractStore.ProjectRoot(existing) : GitRoot(cwd) ?? cwd;
        var steps = ContractProject.Init(root, _es, _dryRun, DateOnly.FromDateTime(DateTime.Now));
        string? index = null;
        if (!_dryRun) index = ContractProject.WriteIndex(ContractStore.Load(root));
        if (_json)
        {
            var list = new JsonArray();
            foreach (var s in steps) list.Add((JsonNode)new JsonObject { ["archivo"] = s.Path, ["que"] = s.What });
            return Json(new JsonObject { ["raiz"] = root, ["simulado"] = _dryRun, ["pasos"] = list, ["indice"] = index });
        }
        Say((_dryRun ? S("Simulación (no se escribió nada) en ", "Dry run (nothing written) in ") : S("Proyecto: ", "Project: ")) + root);
        foreach (var s in steps) Say($"  {Path.GetRelativePath(root, s.Path).Replace('\\', '/'),-34} {s.What}");
        if (!_dryRun) Say(S("Siguiente: completa el propósito en .claupet/contratos/base.md.", "Next: fill in the purpose in .claupet/contratos/base.md."));
        return Ok;
    }

    static string? GitRoot(string from)
    {
        for (var dir = new DirectoryInfo(from); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
        return null;
    }

    int List(string cwd)
    {
        var set = ContractStore.Load(cwd);
        if (_json) return Json(ContractProject.Index(set));
        Say(S("Contratos", "Contracts") + $" ({set.Contracts.Count})  ·  " + (set.ProjectFolder is null
            ? S("sin proyecto (cpt contracts init para crear uno)", "no project (cpt contracts init to create one)")
            : S("proyecto: ", "project: ") + set.ProjectFolder));
        var width = Math.Min(28, set.Contracts.Select(c => c.Id.Length).DefaultIfEmpty(10).Max());
        string? lastType = null;
        foreach (var c in set.Contracts)
        {
            if (c.Type != lastType && c.Type != "base") Say("\n" + TypeName(c.Type));
            lastType = c.Type;
            var flags = new List<string> { LayerName(c.Layer) };
            if (c.Required) flags.Add(S("obligatorio", "required"));
            if (!c.Enabled) flags.Add(S("apagado", "off"));
            Say($"  {c.Id.PadRight(width)}  {Trim(c.Title, 30),-30}  {string.Join(" · ", flags)}");
        }
        IssuesSummary(set);
        return Ok;
    }

    int Read(string cwd, string id)
    {
        var set = ContractStore.Load(cwd);
        var c = set.Find(id);
        if (c is null) return NotFound(set, id);
        if (_json) return Json(ToJson(c));
        Say($"{c.Title}  ·  {c.Id}");
        Say(c.Summary);
        var meta = new List<string> { S("capa ", "layer ") + LayerName(c.Layer), c.Required ? S("obligatorio", "required") : S("recomendado", "recommended") };
        if (!c.Enabled) meta.Add(S($"apagado por {LayerName(c.OffBy!.Value)}", $"turned off by {LayerName(c.OffBy!.Value)}"));
        Say(string.Join(" · ", meta) + "  ·  " + S("aplica a ", "applies to ") + string.Join(", ", c.AppliesTo));
        Say("");
        Say(S("Reglas", "Rules"));
        foreach (var r in c.Rules)
        {
            var notes = new List<string>();
            if (r.Layer != c.Layer) notes.Add(LayerName(r.Layer));
            if (r.Checks.Count > 0) notes.Add("check: " + string.Join(", ", r.Checks));
            if (r.Off) notes.Add(S("apagada", "off"));
            Say($"  [{r.Id}] {r.Text}" + (notes.Count > 0 ? "   (" + string.Join(" · ", notes) + ")" : ""));
        }
        var rest = BodyWithoutRules(c.Body);
        if (rest.Length > 0) { Say(""); Say(rest); }
        return Ok;
    }

    static JsonObject ToJson(ResolvedContract c)
    {
        var rules = new JsonArray();
        foreach (var r in c.Rules)
        {
            var checks = new JsonArray();
            foreach (var k in r.Checks) checks.Add((JsonNode?)k);
            rules.Add((JsonNode)new JsonObject { ["id"] = r.Id, ["texto"] = r.Text, ["capa"] = ContractStore.Name(r.Layer), ["checks"] = checks, ["apagada"] = r.Off });
        }
        var sources = new JsonArray();
        foreach (var s in c.Sources) sources.Add((JsonNode)new JsonObject { ["capa"] = ContractStore.Name(s.Layer), ["archivo"] = s.Path, ["modo"] = ContractStore.ModeName(s.Mode) });
        var applies = new JsonArray();
        foreach (var a in c.AppliesTo) applies.Add((JsonNode?)a);
        return new JsonObject
        {
            ["id"] = c.Id, ["tipo"] = c.Type, ["titulo"] = c.Title, ["resumen"] = c.Summary, ["aplica"] = applies,
            ["prioridad"] = c.Required ? "obligatoria" : "recomendada", ["capa"] = ContractStore.Name(c.Layer), ["activo"] = c.Enabled,
            ["reglas"] = rules, ["cuerpo"] = BodyWithoutRules(c.Body), ["fuentes"] = sources,
        };
    }

    /// <summary>El cuerpo sin las líneas de reglas (ya se muestran aparte) ni un «## Reglas» que quede vacío.</summary>
    internal static string BodyWithoutRules(string body)
    {
        var lines = body.Split('\n').Where(l => !System.Text.RegularExpressions.Regex.IsMatch(l, @"^\s*[-*]\s+\[[a-z0-9][a-z0-9-]*\]")).ToList();
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!lines[i].TrimStart().StartsWith("## Reglas", StringComparison.OrdinalIgnoreCase) && !lines[i].TrimStart().StartsWith("## Rules", StringComparison.OrdinalIgnoreCase)) continue;
            var next = lines.Skip(i + 1).FirstOrDefault(l => l.Trim().Length > 0);
            if (next is null || next.TrimStart().StartsWith("## ")) lines.RemoveAt(i);
        }
        var text = string.Join('\n', lines);
        while (text.Contains("\n\n\n")) text = text.Replace("\n\n\n", "\n\n");
        return text.Trim();
    }

    int Search(string cwd, string query)
    {
        var set = ContractStore.Load(cwd);
        var results = ContractSearch.Find(set, query);
        if (_json)
        {
            var list = new JsonArray();
            foreach (var r in results)
            {
                var rules = new JsonArray();
                foreach (var rule in r.Rules) rules.Add((JsonNode)new JsonObject { ["id"] = rule.Id, ["texto"] = rule.Text });
                list.Add((JsonNode)new JsonObject { ["id"] = r.Contract.Id, ["titulo"] = r.Contract.Title, ["resumen"] = r.Contract.Summary, ["puntos"] = r.Score, ["reglas"] = rules });
            }
            return Json(new JsonObject { ["consulta"] = query, ["resultados"] = list });
        }
        if (results.Count == 0) { Say(S($"Nada coincide con «{query}».", $"Nothing matches «{query}».")); return Ok; }
        var width = Math.Min(28, results.Max(r => r.Contract.Id.Length));
        foreach (var r in results)
        {
            Say($"{r.Contract.Id.PadRight(width)}  {r.Contract.Title}  ·  {r.Contract.Summary}");
            foreach (var rule in r.Rules.Take(3)) Say($"{"".PadRight(width)}    [{rule.Id}] {rule.Text}");
        }
        return Ok;
    }

    int Index(string cwd)
    {
        var set = ContractStore.Load(cwd);
        if (set.ProjectFolder is null) return Fail(S("No hay proyecto: usa cpt contracts init.", "No project: use cpt contracts init."), Failed);
        var path = _dryRun ? Path.Combine(set.ProjectFolder, ContractStore.Generated, "indice.json") : ContractProject.WriteIndex(set)!;
        if (_json) return Json(new JsonObject { ["indice"] = path, ["contratos"] = set.Contracts.Count, ["simulado"] = _dryRun });
        Say(S($"Índice con {set.Contracts.Count} contratos: ", $"Index with {set.Contracts.Count} contracts: ") + path);
        return Ok;
    }

    int Validate(string cwd)
    {
        var set = ContractStore.Load(cwd);
        var issues = new List<ContractIssue>(set.Issues);
        if (set.ProjectFolder is null)
            issues.Add(new(ContractSeverity.Aviso, cwd, S("No hay .claupet/contratos aquí ni más arriba: solo se revisan Claupet y el perfil.", "No .claupet/contratos here or above: only Claupet and the profile are checked.")));
        else if (set.Find("base") is not { } b || b.Layer != ContractLayer.Proyecto)
            issues.Add(new(ContractSeverity.Aviso, set.ProjectFolder, S("Falta base.md (cpt contracts init lo crea).", "base.md is missing (cpt contracts init creates it).")));
        var errors = issues.Count(i => i.Severity == ContractSeverity.Error);
        var warnings = issues.Count - errors;
        if (_json)
        {
            var list = new JsonArray();
            foreach (var i in issues) list.Add((JsonNode)new JsonObject { ["severidad"] = i.Severity == ContractSeverity.Error ? "error" : "aviso", ["archivo"] = i.File, ["mensaje"] = i.Message });
            Json(new JsonObject { ["contratos"] = set.Contracts.Count, ["errores"] = errors, ["avisos"] = warnings, ["problemas"] = list });
            return errors > 0 ? Failed : Ok;
        }
        foreach (var i in issues)
            Say($"{(i.Severity == ContractSeverity.Error ? "error" : S("aviso", "warning"))}  {Shorten(i.File, set)}  {i.Message}");
        Say(errors == 0 && warnings == 0
            ? S($"Todo bien: {set.Contracts.Count} contratos.", $"All good: {set.Contracts.Count} contracts.")
            : S($"{set.Contracts.Count} contratos, {errors} errores, {warnings} avisos.", $"{set.Contracts.Count} contracts, {errors} errors, {warnings} warnings."));
        return errors > 0 ? Failed : Ok;
    }

    static string Shorten(string file, ContractSet set)
    {
        if (set.ProjectFolder is { } p && file.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return Path.GetRelativePath(ContractStore.ProjectRoot(p), file).Replace('\\', '/');
        if (set.ProfileFolder is { } f && file.StartsWith(f, StringComparison.OrdinalIgnoreCase)) return "perfil:" + Path.GetRelativePath(f, file).Replace('\\', '/');
        return file;
    }

    void IssuesSummary(ContractSet set)
    {
        var errors = set.Issues.Count(i => i.Severity == ContractSeverity.Error);
        if (errors > 0) Err(S($"\n{errors} errores de formato: cpt contracts validate para verlos.", $"\n{errors} format errors: cpt contracts validate to see them."));
    }

    int NotFound(ContractSet set, string id)
    {
        var near = set.Contracts.Select(c => c.Id).OrderBy(c => Distance(c, id)).FirstOrDefault(c => Distance(c, id) <= Math.Max(2, id.Length / 3));
        return Fail(S($"No hay un contrato «{id}».", $"There is no contract «{id}».") + (near is null ? "" : S($" ¿Quisiste decir «{near}»?", $" Did you mean «{near}»?")), Failed);
    }

    // ── Ayuda y salida ───────────────────────────────────────────────────────────────────────

    int PrintHelp(string? topic)
    {
        if (topic is not null && ContractGroup.Contains(topic.ToLowerInvariant()))
        {
            Say(S("cpt contracts <acción>   (también: contratos, c)", "cpt contracts <action>   (also: contratos, c)"));
            Say("");
            Say(S("  init                  Prepara el proyecto: base.md, ajustes.json y el puntero en AGENTS.md", "  init                  Set up the project: base.md, ajustes.json and the pointer in AGENTS.md"));
            Say(S("  list    | listar      Los contratos resueltos por capas", "  list    | listar      The contracts, resolved by layer"));
            Say(S("  read    | leer <id>   Un contrato con sus reglas", "  read    | leer <id>   One contract with its rules"));
            Say(S("  search  | buscar <t>  Busca por id, etiqueta o texto", "  search  | buscar <t>  Search by id, tag or text"));
            Say(S("  index   | indice      Regenera .generado/indice.json", "  index   | indice      Rebuild .generado/indice.json"));
            Say(S("  validate| validar     Revisa el formato de todos los contratos", "  validate| validar     Check the format of every contract"));
            Say("");
            Say(S("Próximamente: select, check, change, lesson, feedback, propose, approve, eject.", "Coming soon: select, check, change, lesson, feedback, propose, approve, eject."));
            return Ok;
        }
        Say($"cpt {Version}  ·  " + S("el comando de Claupet (también: claupet)", "the Claupet command (also: claupet)"));
        Say("");
        Say(S("Uso: cpt <grupo> <acción> [opciones]", "Usage: cpt <group> <action> [options]"));
        Say("");
        Say(S("Grupos", "Groups"));
        Say(S("  contracts | contratos | c   Contratos Claupet (cpt contracts --help)", "  contracts | contratos | c   Claupet Contracts (cpt contracts --help)"));
        Say(S("  mcp                         Servidor MCP de los contratos, para los agentes", "  mcp                         MCP server of the contracts, for agents"));
        Say("");
        Say(S("Opciones", "Options"));
        Say(S("  --json        Salida para scripts", "  --json        Output for scripts"));
        Say(S("  --lang es|en  Idioma de los mensajes", "  --lang es|en  Message language"));
        Say(S("  -C <carpeta>  Trabajar desde otra carpeta", "  -C <folder>   Work from another folder"));
        Say(S("  --dry-run     Mostrar lo que haría, sin escribir", "  --dry-run     Show what it would do, without writing"));
        Say(S("  -q, --quiet   Sin mensajes, solo errores", "  -q, --quiet   No messages, only errors"));
        Say(S("  -v, --version La versión", "  -v, --version The version"));
        return Ok;
    }

    int PrintVersion()
    {
        if (_json) return Json(new JsonObject { ["version"] = Version });
        _out.WriteLine($"cpt {Version}");
        return Ok;
    }

    int Unknown(string word, IEnumerable<string> known, string? group)
    {
        var near = known.Distinct().OrderBy(k => Distance(k, word)).FirstOrDefault(k => Distance(k, word) <= 2);
        var prefix = group is null ? "cpt " : $"cpt {group} ";
        return Fail(S($"No se entiende «{word}».", $"Unknown «{word}».") + (near is null
            ? S(" Prueba cpt --help.", " Try cpt --help.")
            : S($" ¿Quisiste decir «{prefix}{near}»?", $" Did you mean «{prefix}{near}»?")), Usage);
    }

    static bool LooksLikePath(string arg, string cwd) =>
        arg is "." or ".." || arg.Contains('/') || arg.Contains('\\') || Directory.Exists(Path.Combine(cwd, arg)) || File.Exists(Path.Combine(cwd, arg.Split(':')[0]));

    void Say(string text) { if (!_quiet) _out.WriteLine(text); }

    void Err(string text) => _err.WriteLine(text);

    int Fail(string message, int code)
    {
        if (_json) _out.WriteLine(ContractProject.ToJson(new JsonObject { ["error"] = message }));
        else _err.WriteLine(message);
        return code;
    }

    int Json(JsonNode node)
    {
        _out.WriteLine(ContractProject.ToJson(node));
        return Ok;
    }

    string LayerName(ContractLayer layer) => layer switch
    {
        ContractLayer.Claupet => "Claupet",
        ContractLayer.Perfil => S("perfil", "profile"),
        _ => S("proyecto", "project"),
    };

    string TypeName(string type) => type switch
    {
        "calidad" => S("Calidad", "Quality"),
        "estilo" => S("Estilo", "Style"),
        "capacidad" => S("Capacidades", "Capabilities"),
        "mapa" => S("Mapa", "Map"),
        "leccion" => S("Lecciones", "Lessons"),
        "perfil" => S("Perfil", "Profile"),
        _ => type,
    };

    static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    internal static int Distance(string a, string b)
    {
        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();
        var d = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) d[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var prev = d[0];
            d[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var temp = d[j];
                d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                prev = temp;
            }
        }
        return d[b.Length];
    }

    /// <summary>El idioma de la persona: CLAUPET_LANG, el de Windows o el de LANG/LC_ALL; si no, la cultura.</summary>
    static string DetectLanguage()
    {
        if (Environment.GetEnvironmentVariable("CLAUPET_LANG") is { Length: > 0 } custom) return custom;
        if (OperatingSystem.IsWindows())
        {
            try { return (GetUserDefaultUILanguage() & 0x3FF) == 0x0A ? "es" : "en"; }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        }
        foreach (var name in new[] { "LC_ALL", "LC_MESSAGES", "LANG" })
            if (Environment.GetEnvironmentVariable(name) is { Length: > 1 } value && value != "C" && value != "POSIX") return value;
        var culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return culture is "iv" or "" ? "en" : culture;
    }

    [DllImport("kernel32.dll")]
    static extern ushort GetUserDefaultUILanguage();
}

/// <summary>Buscar contratos por id, título, resumen, etiquetas y reglas, sin distinguir tildes ni mayúsculas.</summary>
static class ContractSearch
{
    public sealed record Hit(ResolvedContract Contract, int Score, List<ContractRule> Rules);

    public static List<Hit> Find(ContractSet set, string query)
    {
        var words = Fold(query).Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [];
        var hits = new List<Hit>();
        foreach (var c in set.Contracts)
        {
            var score = 0;
            var rules = new List<ContractRule>();
            foreach (var w in words)
            {
                if (Fold(c.Id).Contains(w) || Fold(c.Title).Contains(w)) score += 3;
                if (c.Tags.Any(t => Fold(t).Contains(w))) score += 2;
                if (Fold(c.Summary).Contains(w)) score += 2;
                foreach (var r in c.ActiveRules.Where(r => Fold(r.Text).Contains(w) || r.Id.Contains(w)))
                {
                    score += 1;
                    if (!rules.Contains(r)) rules.Add(r);
                }
            }
            if (score > 0) hits.Add(new(c, score, rules));
        }
        return [.. hits.OrderByDescending(h => h.Score).ThenBy(h => h.Contract.Id, StringComparer.Ordinal)];
    }

    /// <summary>Minúsculas y sin tildes (sin Normalize, que depende de ICU).</summary>
    internal static string Fold(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.ToLowerInvariant())
            sb.Append(ch switch
            {
                'á' or 'à' or 'ä' or 'â' => 'a',
                'é' or 'è' or 'ë' or 'ê' => 'e',
                'í' or 'ì' or 'ï' or 'î' => 'i',
                'ó' or 'ò' or 'ö' or 'ô' => 'o',
                'ú' or 'ù' or 'ü' or 'û' => 'u',
                'ñ' => 'n',
                _ => ch,
            });
        return sb.ToString();
    }
}
