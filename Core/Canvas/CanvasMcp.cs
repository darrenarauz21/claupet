using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet;

/// <summary>
/// Claupet Canvas como servidor MCP (Model Context Protocol) por stdio: «Claupet --canvas-mcp». Así cualquier agente o
/// app con MCP (Claude Code, Codex, Gemini CLI, Copilot CLI, Claude Desktop, Cursor…), con el modelo y el proveedor que
/// sea, crea, lee y edita los canvas que la persona ve en vivo en Claupet Console. Un mensaje JSON-RPC por línea.
/// Los textos de las herramientas van en inglés: son para el modelo, no para la persona.
/// </summary>
static class CanvasMcp
{
    public const string Flag = "--canvas-mcp";
    public const string Name = "claupet-canvas";
    /// <summary>Quién edita (lo pone Claupet al abrir el agente); si no está, el nombre que da el cliente MCP.</summary>
    public const string AuthorVariable = "CLAUPET_CANVAS_AUTHOR";
    public const string FolderVariable = "CLAUPET_CANVAS_FOLDER";
    const string Protocol = "2025-06-18";

    /// <summary>
    /// Las que se usan sin preguntar (no salen del canvas ni de los planes del proyecto). canvas_export escribe archivos: esa se pregunta.
    /// </summary>
    public static readonly string[] SafeTools = ["canvas_list", "canvas_read", "canvas_create", "canvas_edit", "canvas_open", .. PlanTools.Names];

    const string Instructions =
        "Claupet Canvas is a shared, live document the user sees in Claupet Console while you edit it. Use it when the user " +
        "asks for a canvas, document, artifact, report, page, app, diagram or anything worth keeping and iterating on. " +
        "A canvas is a list of typed sections (markdown, code, html, diagram, svg, data, image), each with a short id (s1, s2…). " +
        "Workflow: canvas_create (or canvas_list + canvas_read), then canvas_edit with small operations instead of rewriting " +
        "everything: 'edit' (find/replace exact text) for changes, 'insert' for new sections, 'replace' only for full rewrites. " +
        "Read the canvas again before editing if the user may have changed it. Answer the user's comments with op 'comment' " +
        "(parent = comment id) and mark them 'resolve' when done. " + PlanTools.Instructions;

    public static int Run()
    {
        var store = CanvasStore.Default;
        var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        var server = new Server(store, Environment.GetEnvironmentVariable(AuthorVariable), Environment.GetEnvironmentVariable(FolderVariable) ?? Environment.CurrentDirectory);
        while (input.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (server.Handle(line) is { } reply) output.WriteLine(reply);
        }
        return 0;
    }

    /// <summary>La lógica del servidor sin la entrada y la salida (las pruebas le hablan directo).</summary>
    internal sealed class Server(CanvasStore store, string? author, string? folder)
    {
        string? _author = author;

        public string? Author => _author;

        /// <summary>Atiende una línea; devuelve la respuesta, o null si era una notificación.</summary>
        public string? Handle(string line)
        {
            JsonNode? id = null;
            try
            {
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                if (root.TryGetProperty("id", out var idElement)) id = JsonNode.Parse(idElement.GetRawText());
                var method = root.TryGetProperty("method", out var m) ? m.GetString() ?? "" : "";
                var parameters = root.TryGetProperty("params", out var p) ? p : default;
                if (id is null) return null; // notificaciones: initialized, cancelled… nada que responder
                return Result(id, Dispatch(method, parameters));
            }
            catch (JsonException) { return Error(id, JsonRpcException.ParseError, "Parse error"); }
            catch (JsonRpcException ex) { return Error(id, ex.Code, ex.Message); }
            catch (Exception ex) { return Error(id, JsonRpcException.InternalError, ex.Message); }
        }

        JsonNode Dispatch(string method, JsonElement p) => method switch
        {
            "initialize" => Initialize(p),
            "ping" => new JsonObject(),
            "tools/list" => new JsonObject { ["tools"] = Tools() },
            "tools/call" => Call(p),
            "resources/list" => Resources(),
            "resources/read" => ReadResource(p),
            "resources/templates/list" => new JsonObject { ["resourceTemplates"] = new JsonArray() },
            "prompts/list" => new JsonObject { ["prompts"] = new JsonArray() },
            _ => throw new JsonRpcException(JsonRpcException.MethodNotFound, $"Method not found: {method}"),
        };

        JsonObject Initialize(JsonElement p)
        {
            if (_author is null && p.ValueKind == JsonValueKind.Object && p.TryGetProperty("clientInfo", out var client) && client.TryGetProperty("name", out var name))
                _author = ClientName(name.GetString());
            var version = p.ValueKind == JsonValueKind.Object && p.TryGetProperty("protocolVersion", out var v) && v.GetString() is { Length: > 0 } asked ? asked : Protocol;
            return new JsonObject
            {
                ["protocolVersion"] = version,
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject(), ["resources"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = Name, ["title"] = "Claupet Canvas", ["version"] = typeof(CanvasMcp).Assembly.GetName().Version?.ToString(3) ?? "1.0.0" },
                ["instructions"] = Instructions,
            };
        }

        /// <summary>El nombre del cliente MCP como se muestra («claude-code» → «Claude Code»).</summary>
        internal static string? ClientName(string? raw) => raw?.ToLowerInvariant() switch
        {
            null or "" => null,
            var n when n.Contains("claude-code") || n == "claude code" => "Claude Code",
            var n when n.Contains("claude") => "Claude",
            var n when n.Contains("codex") => "Codex",
            var n when n.Contains("gemini") => "Gemini CLI",
            var n when n.Contains("copilot") => "Copilot",
            var n when n.Contains("cursor") => "Cursor",
            _ => raw,
        };

        // ── Herramientas ────────────────────────────────────────────────────────────────────

        static JsonArray Tools() =>
        [
            Tool("canvas_list", "List the user's canvases (id, title, version, last editor), newest first.", new JsonObject()),
            Tool("canvas_create",
                "Create a new canvas, optionally with its first sections, and show it to the user. Returns the canvas as you would read it.",
                new JsonObject
                {
                    ["title"] = Str("Canvas title."),
                    ["sections"] = new JsonObject { ["type"] = "array", ["description"] = "Initial sections, in order.", ["items"] = SectionSchema() },
                    ["open"] = new JsonObject { ["type"] = "boolean", ["description"] = "Show it in Claupet Console (default true)." },
                    ["plan"] = Str("Path of a *.plan.json this canvas is about: links them, so the user can jump to the plan's details (optional)."),
                }, ["title"]),
            Tool("canvas_read",
                "Read a canvas: every section wrapped in <section id kind language title> tags with its exact content, plus open comments. " +
                "Use the ids and the exact text in canvas_edit.",
                new JsonObject { ["id"] = Str("Canvas id."), ["version"] = new JsonObject { ["type"] = "integer", ["description"] = "An older version (optional)." } }, ["id"]),
            Tool("canvas_edit",
                "Apply operations to a canvas atomically (all or nothing). Ops: " +
                "insert {kind, language?, title?, content, after?: section id | 'start'} · " +
                "edit {id, find, replace, all?} (find must be the exact current text and unique unless all) · " +
                "replace {id, content?, title?, language?, kind?} · append {id, content} · delete {id} · move {id, after} · " +
                "title {title} · comment {section, text, quote?, parent?} · resolve {id: comment id} · " +
                "plan {content: path of the *.plan.json this canvas is about, shown in Claupet's Projects; empty to unlink}. " +
                "Kinds: markdown, code (language), html (full page/app, runs sandboxed), diagram (language: mermaid), svg, data (language: json|csv), image (https URL, file path or data URI), plan (a plan JSON: same format as plan_create; drawn as summary, board and timeline).",
                new JsonObject
                {
                    ["id"] = Str("Canvas id."),
                    ["ops"] = new JsonObject { ["type"] = "array", ["minItems"] = 1, ["items"] = OpSchema() },
                    ["base_version"] = new JsonObject { ["type"] = "integer", ["description"] = "Version you read; rejects the edit if the canvas changed since (optional)." },
                }, ["id", "ops"]),
            Tool("canvas_export",
                "Export a canvas as markdown, html (standalone page), json, pdf or docx (Word). With an absolute path, writes the file there; without, returns the text (pdf and docx need a path).",
                new JsonObject
                {
                    ["id"] = Str("Canvas id."),
                    ["format"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. CanvasExport.Formats.Select(f => (JsonNode)f)]) },
                    ["path"] = Str("Absolute file path to write (optional)."),
                }, ["id", "format"]),
            Tool("canvas_open", "Show a canvas to the user in Claupet Console.", new JsonObject { ["id"] = Str("Canvas id.") }, ["id"]),
            .. PlanTools.Tools(),
        ];

        static JsonObject Tool(string name, string description, JsonObject properties, string[]? required = null)
        {
            var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
            if (required is not null) schema["required"] = new JsonArray([.. required.Select(r => (JsonNode)r)]);
            return new JsonObject { ["name"] = name, ["description"] = description, ["inputSchema"] = schema };
        }

        static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };

        static JsonObject KindSchema() => new() { ["type"] = "string", ["enum"] = new JsonArray([.. CanvasKinds.All.Select(k => (JsonNode)k)]) };

        static JsonObject SectionSchema() => new()
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["kind"] = KindSchema(),
                ["language"] = Str("code: python, csharp…; diagram: mermaid; data: json or csv."),
                ["title"] = Str("Optional section title."),
                ["content"] = Str("The section content."),
            },
            ["required"] = new JsonArray("kind", "content"),
        };

        static JsonObject OpSchema() => new()
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["op"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. CanvasOp.Names.Select(n => (JsonNode)n)]) },
                ["id"] = Str("Section id (comment id for resolve)."),
                ["after"] = Str("Section id to insert/move after, or 'start'. Omit to add at the end."),
                ["kind"] = KindSchema(),
                ["language"] = Str("Language for code, diagram or data."),
                ["title"] = Str("Section title, or canvas title for op 'title'."),
                ["content"] = Str("Content for insert, replace, append; the plan path for op 'plan'."),
                ["find"] = Str("Exact text to find (op edit)."),
                ["replace"] = Str("Replacement text (op edit)."),
                ["all"] = new JsonObject { ["type"] = "boolean", ["description"] = "Replace every occurrence (op edit)." },
                ["section"] = Str("Section id (op comment)."),
                ["text"] = Str("Comment text."),
                ["quote"] = Str("Exact text the comment is about (optional)."),
                ["parent"] = Str("Comment id this replies to (optional)."),
            },
            ["required"] = new JsonArray("op"),
        };

        JsonObject Call(JsonElement p)
        {
            var name = p.TryGetProperty("name", out var n) ? n.GetString() : null;
            var args = p.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object ? a : default;
            try { return Text(Run(name ?? "", args), error: false); }
            // Lo que el modelo hizo mal vuelve como resultado con error (para que lo corrija), no como error del protocolo.
            catch (CanvasException ex) { return Text(ex.Message, error: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return Text(ex.Message, error: true); }
        }

        string Run(string tool, JsonElement args)
        {
            switch (tool)
            {
                case "canvas_list":
                {
                    var list = store.List();
                    if (list.Count == 0) return "No canvases yet. Create one with canvas_create.";
                    return string.Join('\n', list.Select(c => $"{c.Id}  v{c.Version}  {c.Sections} sections  \"{c.Title}\"  updated {c.Updated:yyyy-MM-dd HH:mm}" + (c.LastAuthor is { } la ? $" by {CanvasJson.AuthorLabel(la)}" : "")));
                }
                case "canvas_create":
                {
                    var sections = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("sections", out var s) && s.ValueKind == JsonValueKind.Array
                        ? CanvasOp.ParseAll(s).Select(op => op with { Op = "insert" }).ToList() : null;
                    var doc = store.Create(Arg(args, "title"), _author, sections, folder);
                    if (Arg(args, "plan") is { } plan) doc = store.Apply(doc.Id, [new CanvasOp("plan", Content: plan)], _author).Doc;
                    if (!(args.ValueKind == JsonValueKind.Object && args.TryGetProperty("open", out var open) && open.ValueKind == JsonValueKind.False))
                        store.RequestOpen(doc.Id);
                    return $"Created canvas {doc.Id} (shown to the user in Claupet Console).\n\n" + CanvasExport.ForAgent(doc);
                }
                case "canvas_read":
                {
                    var id = Required(args, "id");
                    var doc = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("version", out var v) && v.TryGetInt32(out var version)
                        ? store.LoadVersion(id, version) ?? throw new CanvasException($"Version {version} of {id} is not available.")
                        : store.Load(id) ?? throw new CanvasException($"No canvas {id}. Use canvas_list.");
                    return CanvasExport.ForAgent(doc);
                }
                case "canvas_edit":
                {
                    var id = Required(args, "id");
                    if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("ops", out var opsElement))
                        throw new CanvasException("Missing ops.");
                    // Algunos clientes mandan el arreglo como texto JSON.
                    using var parsed = opsElement.ValueKind == JsonValueKind.String ? JsonDocument.Parse(opsElement.GetString()!) : null;
                    var ops = CanvasOp.ParseAll(parsed?.RootElement ?? opsElement);
                    int? baseVersion = args.TryGetProperty("base_version", out var b) && b.TryGetInt32(out var bv) ? bv : null;
                    var applied = store.Apply(id, ops, _author, baseVersion);
                    return $"OK: {applied.Summary}. Canvas {id} is now version {applied.Doc.Version}. Sections: " +
                        string.Join(", ", applied.Doc.Sections.Select(x => $"{x.Id} ({x.Kind}{(x.Title is { } t ? $": {t}" : "")})"));
                }
                case "canvas_export":
                {
                    var id = Required(args, "id");
                    var format = Required(args, "format").ToLowerInvariant();
                    var doc = store.Load(id) ?? throw new CanvasException($"No canvas {id}.");
                    if (Arg(args, "path") is not { } path) return CanvasExport.To(format, doc);
                    if (!Path.IsPathFullyQualified(path)) throw new CanvasException("path must be absolute.");
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    CanvasExport.Save(format, doc, path);
                    return $"Wrote {path}.";
                }
                case "canvas_open":
                {
                    var id = Required(args, "id");
                    if (store.Load(id) is null) throw new CanvasException($"No canvas {id}.");
                    store.RequestOpen(id);
                    return $"Canvas {id} is shown in Claupet Console.";
                }
                default:
                    // Los planes del proyecto (Core/PlanTools.cs): en la carpeta de la sesión.
                    return PlanTools.Names.Contains(tool)
                        ? PlanTools.Run(tool, args, folder ?? Environment.CurrentDirectory, _author)
                        : throw new JsonRpcException(JsonRpcException.InvalidParams, $"Unknown tool: {tool}");
            }
        }

        static string? Arg(JsonElement args, string name) =>
            args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

        static string Required(JsonElement args, string name) => Arg(args, name) ?? throw new CanvasException($"Missing {name}.");

        static JsonObject Text(string text, bool error) => new()
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = error,
        };

        // ── Recursos: cada canvas, para los clientes que los leen así ──────────────────────────

        JsonObject Resources() => new()
        {
            ["resources"] = new JsonArray([.. store.List().Select(c => (JsonNode)new JsonObject
            {
                ["uri"] = $"canvas://{c.Id}",
                ["name"] = c.Title,
                ["mimeType"] = "text/plain",
                ["description"] = $"Claupet canvas, version {c.Version}, {c.Sections} sections",
            })]),
        };

        JsonObject ReadResource(JsonElement p)
        {
            var uri = p.ValueKind == JsonValueKind.Object && p.TryGetProperty("uri", out var u) ? u.GetString() ?? "" : "";
            if (!uri.StartsWith("canvas://", StringComparison.Ordinal)) throw new JsonRpcException(JsonRpcException.InvalidParams, "Unknown resource");
            var doc = store.Load(uri["canvas://".Length..]) ?? throw new JsonRpcException(JsonRpcException.InvalidParams, "No such canvas");
            return new JsonObject { ["contents"] = new JsonArray(new JsonObject { ["uri"] = uri, ["mimeType"] = "text/plain", ["text"] = CanvasExport.ForAgent(doc) }) };
        }

        static string Result(JsonNode id, JsonNode result) =>
            new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToJsonString();

        static string Error(JsonNode? id, int code, string message) =>
            new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString();
    }

    // ── Para los agentes y las apps ─────────────────────────────────────────────────────────

    /// <summary>El ejecutable de Claupet que corre el servidor (el que está abierto ahora).</summary>
    public static string? Command => Environment.ProcessPath is { } path && !Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? path : null;

    /// <summary>
    /// La configuración para Claude Code (--mcp-config), en un archivo junto a los canvas: un JSON en la línea de comandos
    /// no sobrevive a cmd.exe (claude.cmd). Null si no se pudo escribir.
    /// </summary>
    public static string? ClaudeConfigFile(string author, string? folder)
    {
        if (Command is not { } command) return null;
        var config = new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                [Name] = new JsonObject
                {
                    ["type"] = "stdio",
                    ["command"] = command,
                    ["args"] = new JsonArray(Flag),
                    ["env"] = Env(author, folder),
                },
            },
        };
        try
        {
            var dir = DataFolder.In("Canvas", "mcp");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "claude-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(author + "|" + folder)))[..10].ToLowerInvariant() + ".json");
            File.WriteAllText(file, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            return file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); return null; }
    }

    /// <summary>Lo que se le agrega a Claude Code: el servidor y sus herramientas seguras sin preguntar.</summary>
    public static IEnumerable<string> ClaudeArguments(string? folder)
    {
        if (ClaudeConfigFile("Claude Code", folder) is not { } file) return [];
        return ["--mcp-config", file, "--allowedTools", string.Join(',', SafeTools.Select(t => $"mcp__{Name}__{t}"))];
    }

    /// <summary>
    /// Lo que va antes de «app-server» en Codex: «-c» con la tabla del servidor en TOML. Comillas simples (cadenas
    /// literales de TOML): la ruta de Windows no necesita escapes y no hay comillas dobles que cmd.exe rompa.
    /// </summary>
    public static IEnumerable<string> CodexArguments(string? folder)
    {
        if (Command is not { } command || command.Contains('\'') || (folder?.Contains('\'') ?? false)) return [];
        var key = "mcp_servers.claupet_canvas";
        var env = $"{{{AuthorVariable}='Codex'" + (folder is null ? "" : $",{FolderVariable}='{folder}'") + "}";
        return ["-c", $"{key}.command='{command}'", "-c", $"{key}.args=['{Flag}']", "-c", $"{key}.env={env}"];
    }

    /// <summary>El servidor para «session/new» de ACP (Gemini CLI, Copilot CLI…).</summary>
    public static JsonArray AcpServers(string author, string? folder)
    {
        if (Command is not { } command) return [];
        var env = new JsonArray(new JsonObject { ["name"] = AuthorVariable, ["value"] = author });
        if (folder is not null) env.Add(new JsonObject { ["name"] = FolderVariable, ["value"] = folder });
        return [new JsonObject { ["name"] = Name, ["command"] = command, ["args"] = new JsonArray(Flag), ["env"] = env }];
    }

    static JsonObject Env(string author, string? folder)
    {
        var env = new JsonObject { [AuthorVariable] = author };
        if (folder is not null) env[FolderVariable] = folder;
        return env;
    }

    /// <summary>Para otras apps (Claude Desktop, Cursor, VS Code…): el bloque que se pega en su configuración de MCP.</summary>
    public static string ConfigSnippet()
    {
        var server = new JsonObject { ["command"] = Command ?? "Claupet", ["args"] = new JsonArray(Flag) };
        return new JsonObject { ["mcpServers"] = new JsonObject { [Name] = server } }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
