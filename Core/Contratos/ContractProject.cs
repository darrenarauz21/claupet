using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet;

/// <summary>
/// Lo que el motor escribe en un proyecto: el índice (.generado/indice.json, fuera de git) y lo de «cpt contracts init»
/// (base.md, ajustes.json, el .gitignore de .generado y el puntero en AGENTS.md o CLAUDE.md).
/// </summary>
static class ContractProject
{
    public const string PointerStart = "<!-- claupet-contratos -->";
    public const string PointerEnd = "<!-- /claupet-contratos -->";

    /// <summary>El índice: una línea por contrato, lo que el agente lee para saber qué pedir.</summary>
    public static JsonObject Index(ContractSet set)
    {
        var list = new JsonArray();
        foreach (var c in set.Contracts)
            list.Add((JsonNode)new JsonObject
            {
                ["id"] = c.Id,
                ["tipo"] = c.Type,
                ["titulo"] = c.Title,
                ["resumen"] = c.Summary,
                ["aplica"] = Array(c.AppliesTo),
                ["etiquetas"] = Array(c.Tags),
                ["prioridad"] = c.Required ? "obligatoria" : "recomendada",
                ["capa"] = ContractStore.Name(c.Layer),
                ["activo"] = c.Enabled,
                ["reglas"] = c.ActiveRules.Count(),
                ["tokens"] = c.Tokens,
            });
        return new JsonObject { ["formato"] = 1, ["contratos"] = list };
    }

    static JsonArray Array(IEnumerable<string> items)
    {
        var a = new JsonArray();
        foreach (var i in items) a.Add((JsonNode?)i);
        return a;
    }

    /// <summary>Escribe .generado/indice.json; devuelve la ruta, o null si no hay proyecto.</summary>
    public static string? WriteIndex(ContractSet set)
    {
        if (set.ProjectFolder is null) return null;
        var folder = Path.Combine(set.ProjectFolder, ContractStore.Generated);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "indice.json");
        File.WriteAllText(path, ToJson(Index(set)) + "\n", new UTF8Encoding(false));
        return path;
    }

    /// <summary>JSON con sangría, sin escapar las tildes (Utf8JsonWriter: sin reflexión, sirve con NativeAOT).</summary>
    public static string ToJson(JsonNode node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            node.WriteTo(writer);
        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n");
    }

    /// <summary>Un paso de init: qué archivo y qué se hizo (o se haría, con --dry-run).</summary>
    public sealed record Step(string Path, string What);

    /// <summary>
    /// Prepara un proyecto: .claupet/contratos con base.md (con el stack que encuentre), ajustes.json y el .gitignore de
    /// .generado, y el puntero en AGENTS.md y CLAUDE.md (los que existan; si no hay ninguno, crea AGENTS.md). Lo que ya
    /// existe no se pisa.
    /// </summary>
    public static List<Step> Init(string projectRoot, bool spanish, bool dryRun, DateOnly today)
    {
        var steps = new List<Step>();
        var folder = Path.Combine(projectRoot, ".claupet", "contratos");
        void Write(string path, string content, string what)
        {
            if (File.Exists(path)) { steps.Add(new(path, spanish ? "ya existe, no se toca" : "already exists, left as is")); return; }
            steps.Add(new(path, what));
            if (dryRun) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
        Write(Path.Combine(folder, "base.md"), BaseTemplate(projectRoot, spanish, today), spanish ? "contrato base creado" : "base contract created");
        Write(Path.Combine(folder, "ajustes.json"), SettingsTemplate(), spanish ? "ajustes creados" : "settings created");
        Write(Path.Combine(folder, ".gitignore"), ContractStore.Generated + "/\n", spanish ? ".generado fuera de git" : ".generado kept out of git");

        var targets = new[] { "AGENTS.md", "CLAUDE.md" }.Select(n => Path.Combine(projectRoot, n)).Where(File.Exists).ToList();
        if (targets.Count == 0) targets.Add(Path.Combine(projectRoot, "AGENTS.md"));
        foreach (var target in targets)
        {
            var exists = File.Exists(target);
            var text = exists ? File.ReadAllText(target) : "";
            if (text.Contains(PointerStart, StringComparison.Ordinal))
            {
                steps.Add(new(target, spanish ? "ya tiene el puntero" : "pointer already there"));
                continue;
            }
            steps.Add(new(target, exists ? (spanish ? "puntero agregado" : "pointer added") : (spanish ? "creado con el puntero" : "created with the pointer")));
            if (dryRun) continue;
            var separator = text.Length == 0 ? "" : text.EndsWith('\n') ? "\n" : "\n\n";
            File.WriteAllText(target, text + separator + Pointer(spanish) + "\n", new UTF8Encoding(false));
        }
        return steps;
    }

    public static string Pointer(bool spanish) => PointerStart + (spanish
        ? "Este proyecto usa Contratos Claupet: lee .claupet/contratos/base.md antes de empezar."
        : "This project uses Claupet Contracts: read .claupet/contratos/base.md before starting.") + PointerEnd;

    /// <summary>Lo que se reconoce del proyecto por sus archivos: stack y comandos.</summary>
    internal static (List<string> Stack, List<(string What, string Command)> Commands) Detect(string root)
    {
        var stack = new List<string>();
        var commands = new List<(string, string)>();
        bool Has(string pattern) => SafeFiles(root, pattern).Any();
        if (Has("*.sln") || Has("*.slnx") || Has("*.csproj"))
        {
            stack.Add(".NET");
            commands.Add(("compilar", "dotnet build"));
            commands.Add(("probar", "dotnet test"));
        }
        if (File.Exists(Path.Combine(root, "package.json")))
        {
            stack.Add("Node.js");
            var pm = File.Exists(Path.Combine(root, "pnpm-lock.yaml")) ? "pnpm" : File.Exists(Path.Combine(root, "yarn.lock")) ? "yarn" : "npm";
            commands.Add(("instalar", pm + " install"));
            commands.Add(("probar", pm + " test"));
        }
        if (File.Exists(Path.Combine(root, "pyproject.toml")) || File.Exists(Path.Combine(root, "requirements.txt")))
        {
            stack.Add("Python");
            commands.Add(("probar", "pytest"));
        }
        if (File.Exists(Path.Combine(root, "go.mod"))) { stack.Add("Go"); commands.Add(("probar", "go test ./...")); }
        if (File.Exists(Path.Combine(root, "Cargo.toml"))) { stack.Add("Rust"); commands.Add(("probar", "cargo test")); }
        return (stack, commands);
    }

    /// <summary>Archivos de la raíz y del primer nivel, sin entrar a carpetas ocultas ni de dependencias.</summary>
    static IEnumerable<string> SafeFiles(string root, string pattern)
    {
        IEnumerable<string> Try(string dir)
        {
            try { return Directory.EnumerateFiles(dir, pattern); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
        }
        foreach (var f in Try(root)) yield return f;
        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(root).ToList(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
        foreach (var d in dirs)
        {
            var name = Path.GetFileName(d);
            if (name.StartsWith('.') || name is "node_modules" or "bin" or "obj") continue;
            foreach (var f in Try(d)) yield return f;
        }
    }

    static string BaseTemplate(string root, bool es, DateOnly today)
    {
        var (stack, commands) = Detect(root);
        var sb = new StringBuilder();
        sb.Append("---\nid: base\ntipo: base\n");
        sb.Append(es ? "titulo: Contrato base\nresumen: Propósito, stack y reglas mínimas de este proyecto\n"
                     : "titulo: Base contract\nresumen: Purpose, stack and minimum rules of this project\n");
        sb.Append("aplica: [\"**/*\"]\nprioridad: obligatoria\nformato: 1\n");
        sb.Append("actualizado: ").Append(today.ToString("yyyy-MM-dd")).Append("\n---\n\n");
        sb.Append(es ? "## Propósito\n\nQué hace este proyecto, en dos líneas.\n\n## Stack\n\n" : "## Purpose\n\nWhat this project does, in two lines.\n\n## Stack\n\n");
        sb.Append(stack.Count > 0 ? string.Join("\n", stack.Select(s => "- " + s)) : es ? "- (por completar)" : "- (to fill in)").Append("\n\n");
        sb.Append(es ? "## Comandos\n\n" : "## Commands\n\n");
        if (commands.Count == 0) sb.Append(es ? "- (por completar)\n" : "- (to fill in)\n");
        foreach (var (what, command) in commands)
            sb.Append("- ").Append(es ? Capitalize(what) : Translate(what)).Append(": `").Append(command).Append("`\n");
        sb.Append(es ? "\n## Reglas\n\n- [alcance] Nada fuera del alcance pedido, salvo que se pida. `check: alcance`\n- [archivos-verdad] Los archivos son la fuente de verdad; los índices se regeneran.\n"
                     : "\n## Rules\n\n- [alcance] Nothing outside the requested scope unless asked. `check: alcance`\n- [archivos-verdad] Files are the source of truth; indexes are regenerated.\n");
        sb.Append(es ? "\n## Cómo usar los contratos\n\nNo leas todo: busca con `cpt contracts search <tema>` y lee con `cpt contracts read <id>` (o las herramientas MCP `contract_search` y `contract_read`). El índice está en `.claupet/contratos/.generado/indice.json`.\n"
                     : "\n## Using the contracts\n\nDon't read everything: search with `cpt contracts search <topic>` and read with `cpt contracts read <id>` (or the MCP tools `contract_search` and `contract_read`). The index is in `.claupet/contratos/.generado/indice.json`.\n");
        return sb.ToString();
    }

    static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    static string Translate(string what) => what switch { "compilar" => "Build", "probar" => "Test", "instalar" => "Install", _ => what };

    static string SettingsTemplate() => ToJson(new JsonObject
    {
        ["formato"] = 1,
        ["selector"] = new JsonObject { ["limite"] = null },
        ["flujo"] = new JsonObject { ["propuestaDesde"] = "mediano" },
        ["pruebas"] = new JsonObject { ["esfuerzo"] = "auto" },
        ["checks"] = new JsonObject { ["alFallar"] = "avisar", ["comandos"] = new JsonArray() },
        ["feedback"] = new JsonObject { ["compartir"] = false },
        ["riesgo"] = new JsonObject
        {
            ["auth"] = new JsonArray("**/Auth/**", "**/*Login*", "**/*Password*"),
            ["bd"] = new JsonArray("**/*.sql", "**/Migrations/**"),
            ["secretos"] = new JsonArray("**/.env*", "**/*secret*"),
        },
    }) + "\n";
}
