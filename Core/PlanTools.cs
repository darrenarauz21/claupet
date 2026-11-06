using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet;

/// <summary>
/// Las herramientas del plan para los agentes (plan_list, plan_read, plan_create, plan_update), servidas por el mismo
/// servidor MCP de Claupet (CanvasMcp): así cualquier sesión (Claude Code, Codex, Gemini CLI, Copilot CLI o una app con
/// MCP) ve que existen y escribe planes en el formato de Core/Plan.cs en vez de texto plano. Solo tocan archivos
/// *.plan.json dentro de la carpeta del proyecto. Los textos van en inglés: son para el modelo, no para la persona.
/// </summary>
static class PlanTools
{
    public static readonly string[] Names = ["plan_list", "plan_read", "plan_create", "plan_update"];

    /// <summary>Lo que se suma a las instrucciones del servidor.</summary>
    public const string Instructions =
        "Claupet Plans: when the user asks to plan, schedule or track a project (tasks, stages, a timeline, milestones, progress), " +
        "keep it as a plan file (*.plan.json) with these tools, never as loose text. Claupet draws it as a summary, board and " +
        "timeline, and refreshes it live. Start with plan_list, then plan_read; create with plan_create; change with small " +
        "plan_update ops (set a task's 'estado' when work starts or ends). Tell the user the file path in your reply so they can open it.";

    const string Format =
        "Plan format (keys in Spanish): {titulo, descripcion?, inicio?: 'YYYY-MM-DD', etapas?: [{id, nombre, color?}], " +
        "tareas?: [{id, titulo, etapa?, estado?, inicio?, dias?, depende?: [task ids], horas?, horasReales?, responsable?, notas?}], " +
        "hitos?: [{id, titulo, fecha?, depende?: [task ids]}]}. " +
        "estado: por-hacer | en-curso | revision | hecho | bloqueado. A task without 'inicio' starts when the tasks it depends on end; " +
        "'dias' is its length in days (default 1). A milestone without 'fecha' falls on the day its dependencies end. " +
        "Use short stable ids (e1, t1, h1).";

    static readonly string[] TaskKeys = ["titulo", "etapa", "estado", "inicio", "dias", "depende", "horas", "horasReales", "responsable", "notas"];
    static readonly string[] StageKeys = ["nombre", "color"];
    static readonly string[] MilestoneKeys = ["titulo", "fecha", "depende"];
    static readonly string[] PlanKeys = ["titulo", "descripcion", "inicio"];

    public static JsonObject[] Tools() =>
    [
        Tool("plan_list", "List the plan files (*.plan.json) in the project folder: path, title, progress.", new JsonObject()),
        Tool("plan_read",
            "Read a plan: its stages, tasks (with resolved dates, status and dependencies), milestones and any warnings.",
            new JsonObject { ["path"] = Str("Plan file path, relative to the project folder.") }, ["path"]),
        Tool("plan_create",
            "Create a plan file and show it to the user as a plan (summary, board, timeline). " + Format,
            new JsonObject
            {
                ["path"] = Str("File to create, relative to the project folder, ending in .plan.json (default: planes/<title>.plan.json)."),
                ["plan"] = new JsonObject { ["type"] = "object", ["description"] = "The plan, in the format above." },
            }, ["plan"]),
        Tool("plan_update",
            "Change a plan atomically (all or nothing). Ops: " +
            "plan {titulo?, descripcion?, inicio?} · task {id, titulo?, etapa?, estado?, inicio?, dias?, depende?, horas?, horasReales?, responsable?, notas?} " +
            "(creates the task if the id is new, otherwise changes only the given fields; null removes a field) · " +
            "stage {id, nombre?, color?} · milestone {id, titulo?, fecha?, depende?} · delete {kind: task|stage|milestone, id}. " + Format,
            new JsonObject
            {
                ["path"] = Str("Plan file path, relative to the project folder."),
                ["ops"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["op"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("plan", "task", "stage", "milestone", "delete") },
                            ["id"] = Str("Task, stage or milestone id."),
                            ["kind"] = Str("For delete: task, stage or milestone."),
                        },
                        ["required"] = new JsonArray("op"),
                        ["additionalProperties"] = true,
                    },
                },
            }, ["path", "ops"]),
    ];

    static JsonObject Tool(string name, string description, JsonObject properties, string[]? required = null)
    {
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required is not null) schema["required"] = new JsonArray([.. required.Select(r => (JsonNode)r)]);
        return new JsonObject { ["name"] = name, ["description"] = description, ["inputSchema"] = schema };
    }

    static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };

    /// <summary>Corre la herramienta; folder es la carpeta del proyecto. Los errores del modelo salen como CanvasException.</summary>
    public static string Run(string tool, JsonElement args, string folder, string? author = null) => tool switch
    {
        "plan_list" => List(folder),
        "plan_read" => Read(Resolve(folder, Arg(args, "path") ?? throw new CanvasException("Missing path.")), folder),
        "plan_create" => Create(args, folder),
        "plan_update" => Update(args, folder, author),
        _ => throw new JsonRpcException(JsonRpcException.InvalidParams, $"Unknown tool: {tool}"),
    };

    // ── plan_list ───────────────────────────────────────────────────────────────────────────

    static string List(string folder)
    {
        var lines = new List<string>();
        foreach (var file in PlanIndex.Find(folder))
        {
            var relative = Path.GetRelativePath(folder, file);
            try
            {
                var plan = Plan.Parse(File.ReadAllText(file));
                lines.Add($"{relative}  \"{plan.Title}\"  {Plan.Progress(plan.Tasks):P0}  {plan.Tasks.Count} tasks");
            }
            catch (Exception ex) when (ex is FormatException or IOException) { lines.Add($"{relative}  (unreadable: {ex.Message})"); }
        }
        return lines.Count == 0 ? "No plans yet. Create one with plan_create." : string.Join('\n', lines);
    }

    // ── plan_read ───────────────────────────────────────────────────────────────────────────

    static string Read(string path, string folder)
    {
        if (!File.Exists(path)) throw new CanvasException($"No plan at {Path.GetRelativePath(folder, path)}. Use plan_list.");
        return Describe(path, folder, Parse(File.ReadAllText(path)));
    }

    static Plan Parse(string json)
    {
        try { return Plan.Parse(json); }
        catch (FormatException ex) { throw new CanvasException(ex.Message); }
    }

    /// <summary>El plan como lo lee un agente: fechas ya calculadas, estados e ids para usar en plan_update.</summary>
    static string Describe(string path, string folder, Plan plan)
    {
        var text = new StringBuilder();
        var (from, to) = plan.Span();
        text.AppendLine($"{Path.GetRelativePath(folder, path)}  \"{plan.Title}\"  {Plan.Progress(plan.Tasks):P0}  {from:yyyy-MM-dd} → {to.AddDays(-1):yyyy-MM-dd}");
        if (plan.Description is { } description) text.AppendLine(description);
        foreach (var s in plan.Stages) text.AppendLine($"stage {s.Id}: {s.Name}" + (s.Color is { } c ? $" ({c})" : ""));
        foreach (var t in plan.Tasks)
            text.AppendLine($"task {t.Id} [{StateName(t.State)}] \"{t.Title}\"" + (t.Stage is { } st ? $" stage={st}" : "") +
                $" {t.Start:yyyy-MM-dd}→{t.End.AddDays(-1):yyyy-MM-dd} ({t.Days}d)" +
                (t.Depends.Count > 0 ? " after=" + string.Join(',', t.Depends) : "") +
                (t.Hours is { } h ? $" est={h:0.#}h" : "") + (t.HoursSpent is { } hs ? $" spent={hs:0.#}h" : "") +
                (t.Owner is { } o ? $" owner={o}" : "") + (t.Editor is { } by ? $" by={by}" : "") + (t.Notes is { } n ? $" notes=\"{n}\"" : ""));
        foreach (var m in plan.Milestones)
            text.AppendLine($"milestone {m.Id} \"{m.Title}\" {m.Date:yyyy-MM-dd}" + (m.Depends.Count > 0 ? " after=" + string.Join(',', m.Depends) : ""));
        foreach (var w in plan.Warnings) text.AppendLine("warning: " + w);
        return text.ToString().TrimEnd();
    }

    static string StateName(PlanState state) => state switch
    {
        PlanState.Doing => "en-curso",
        PlanState.Review => "revision",
        PlanState.Done => "hecho",
        PlanState.Blocked => "bloqueado",
        _ => "por-hacer",
    };

    // ── plan_create ─────────────────────────────────────────────────────────────────────────

    static string Create(JsonElement args, string folder)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("plan", out var element)) throw new CanvasException("Missing plan.");
        // Algunos clientes mandan el objeto como texto JSON.
        var node = (element.ValueKind == JsonValueKind.String ? ParseNode(element.GetString()!) : JsonNode.Parse(element.GetRawText())) as JsonObject
            ?? throw new CanvasException("plan must be a JSON object.");
        node["claupet"] = Plan.Format;
        var plan = Parse(node.ToJsonString());
        var path = Resolve(folder, Arg(args, "path") ?? Path.Combine("planes", Slug(plan.Title) + Plan.Extension));
        if (File.Exists(path)) throw new CanvasException($"{Path.GetRelativePath(folder, path)} already exists. Use plan_update to change it.");
        Write(path, node);
        return $"Created {Path.GetRelativePath(folder, path)}. Mention this path in your reply: Claupet shows it as a plan the user can open.\n\n" + Describe(path, folder, plan);
    }

    static string Slug(string title)
    {
        var slug = new string([.. title.ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '-')]);
        slug = System.Text.RegularExpressions.Regex.Replace(slug, "-+", "-").Trim('-');
        return slug.Length == 0 ? "plan" : slug.Length > 60 ? slug[..60].Trim('-') : slug;
    }

    // ── plan_update ─────────────────────────────────────────────────────────────────────────

    static string Update(JsonElement args, string folder, string? author)
    {
        var path = Resolve(folder, Arg(args, "path") ?? throw new CanvasException("Missing path."));
        if (!File.Exists(path)) throw new CanvasException($"No plan at {Path.GetRelativePath(folder, path)}. Use plan_create.");
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("ops", out var opsElement)) throw new CanvasException("Missing ops.");
        var ops = (opsElement.ValueKind == JsonValueKind.String ? ParseNode(opsElement.GetString()!) : JsonNode.Parse(opsElement.GetRawText())) as JsonArray
            ?? throw new CanvasException("ops must be an array.");
        var root = ParseNode(File.ReadAllText(path)) as JsonObject ?? throw new CanvasException("The plan file is not a JSON object.");
        var done = new List<string>();
        for (var i = 0; i < ops.Count; i++)
        {
            if (ops[i] is not JsonObject op) throw new CanvasException($"Operation {i + 1}: expected an object.");
            try { done.Add(Apply(root, op, author)); }
            catch (CanvasException ex) { throw new CanvasException($"Operation {i + 1}: {ex.Message} Nothing was changed."); }
        }
        // Se valida el resultado entero antes de escribir: un plan que no se lee no se guarda.
        var plan = Parse(root.ToJsonString());
        Write(path, root);
        return $"OK: {string.Join("; ", done)}.\n\n" + Describe(path, folder, plan);
    }

    static string Apply(JsonObject root, JsonObject op, string? author)
    {
        var name = op["op"]?.GetValue<string>() ?? throw new CanvasException("Missing op.");
        switch (name)
        {
            case "plan":
                Fill(root, op, PlanKeys);
                return "plan updated";
            case "task":
                return Upsert(root, "tareas", op, TaskKeys, "task", author);
            case "stage":
                return Upsert(root, "etapas", op, StageKeys, "stage", null);
            case "milestone":
                return Upsert(root, "hitos", op, MilestoneKeys, "milestone", null);
            case "delete":
            {
                var kind = op["kind"]?.GetValue<string>();
                var (list, label) = kind switch { "task" => ("tareas", "task"), "stage" => ("etapas", "stage"), "milestone" => ("hitos", "milestone"), _ => throw new CanvasException("delete needs kind: task, stage or milestone.") };
                var id = Id(op);
                var items = root[list] as JsonArray;
                var item = items?.OfType<JsonObject>().FirstOrDefault(x => Same(x, id)) ?? throw new CanvasException($"No {label} {id}.");
                items!.Remove(item);
                // Lo que dependía de una tarea borrada ya no la espera; las tareas de una etapa borrada quedan sin etapa.
                if (kind == "task")
                    foreach (var owner in (root["tareas"] as JsonArray ?? []).Concat(root["hitos"] as JsonArray ?? []).OfType<JsonObject>())
                        if (owner["depende"] is JsonArray depends)
                            foreach (var d in depends.Where(d => d?.GetValue<string>() == id).ToList()) depends.Remove(d);
                if (kind == "stage")
                    foreach (var task in (root["tareas"] as JsonArray ?? []).OfType<JsonObject>().Where(t => t["etapa"]?.GetValue<string>() == id)) task.Remove("etapa");
                return $"deleted {label} {id}";
            }
            default:
                throw new CanvasException($"Unknown op '{name}'.");
        }
    }

    /// <summary>Cambia o agrega un elemento; en una tarea queda quién fue («por»), para que Claupet lo muestre.</summary>
    static string Upsert(JsonObject root, string list, JsonObject op, string[] keys, string label, string? author)
    {
        var id = Id(op);
        var items = root[list] as JsonArray;
        if (items is null) root[list] = items = [];
        var item = items.OfType<JsonObject>().FirstOrDefault(x => Same(x, id));
        var created = item is null;
        if (item is null)
        {
            item = new JsonObject { ["id"] = id };
            items.Add(item);
        }
        Fill(item, op, keys);
        if (author is { Length: > 0 }) item["por"] = author;
        return $"{(created ? "added" : "updated")} {label} {id}";
    }

    /// <summary>Copia los campos que vinieron (null quita el campo); lo que no es de ese elemento se avisa en vez de perderse.</summary>
    static void Fill(JsonObject target, JsonObject op, string[] keys)
    {
        foreach (var (key, value) in op)
        {
            if (key is "op" or "id" or "kind") continue;
            if (!keys.Contains(key)) throw new CanvasException($"Unknown field '{key}' (valid: {string.Join(", ", keys)}).");
            if (value is null) target.Remove(key);
            else target[key] = value.DeepClone();
        }
    }

    static string Id(JsonObject op) => op["id"]?.GetValue<string>() is { Length: > 0 } id ? id : throw new CanvasException("Missing id.");

    static bool Same(JsonObject item, string id) => item["id"]?.GetValue<string>() == id;

    // ── Desde Claupet ───────────────────────────────────────────────────────────────────────

    /// <summary>Cambia el estado de una tarea desde la sección Proyectos: el mismo camino atómico que usan las IAs.</summary>
    internal static void SetState(string path, string taskId, PlanState state)
    {
        var args = new JsonObject
        {
            ["path"] = path,
            ["ops"] = new JsonArray(new JsonObject { ["op"] = "task", ["id"] = taskId, ["estado"] = StateName(state) }),
        };
        using var doc = JsonDocument.Parse(args.ToJsonString());
        Update(doc.RootElement, Path.GetDirectoryName(path)!, null);
    }

    /// <summary>Crea un plan vacío con ese título (un plan nuevo de la sección Proyectos).</summary>
    internal static void CreateEmpty(string path, string title) =>
        Write(path, new JsonObject { ["claupet"] = Plan.Format, ["titulo"] = title, ["inicio"] = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) });

    // ── Archivos ────────────────────────────────────────────────────────────────────────────

    static JsonNode? ParseNode(string json)
    {
        try { return JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
        catch (JsonException ex) { throw new CanvasException("Not valid JSON: " + ex.Message); }
    }

    /// <summary>Escribe en un archivo temporal y lo mueve: quien mira el archivo (la tarjeta) nunca lo ve a medias.</summary>
    static void Write(string path, JsonObject plan)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, plan.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n", new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>La ruta completa de un plan: solo un *.plan.json dentro de la carpeta del proyecto (estas herramientas no preguntan).</summary>
    internal static string Resolve(string folder, string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.IsPathFullyQualified(path) ? path : Path.Combine(root, path));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(root, comparison)) throw new CanvasException("The plan must be inside the project folder.");
        if (!Plan.IsPlanFile(full)) throw new CanvasException($"Plan files end in {Plan.Extension}.");
        return full;
    }

    static string? Arg(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
}
