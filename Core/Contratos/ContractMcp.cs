using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet;

/// <summary>
/// «cpt mcp»: los contratos para los agentes, por MCP sobre stdio (JSON-RPC, un mensaje por línea). Solo lectura por
/// ahora: listar, buscar y leer. Aprobar nunca tendrá herramienta: eso lo decide la persona.
/// </summary>
static class ContractMcp
{
    const string Protocol = "2025-06-18";
    const int ParseError = -32700, MethodNotFound = -32601, InvalidParams = -32602, InternalError = -32603;

    public static int Run(string cwd, bool spanish)
    {
        var server = new Server(cwd, spanish);
        using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        while (input.ReadLine() is { } line)
        {
            if (line.Trim().Length == 0) continue;
            if (server.Handle(line) is { } reply) output.WriteLine(reply);
        }
        return 0;
    }

    sealed class Failure(int code, string message) : Exception(message)
    {
        public int Code { get; } = code;
    }

    internal sealed class Server(string cwd, bool spanish)
    {
        string S(string es, string en) => spanish ? es : en;

        public string? Handle(string line)
        {
            JsonNode? id = null;
            try
            {
                var root = JsonNode.Parse(line) as JsonObject ?? throw new Failure(ParseError, "Parse error");
                id = root["id"]?.DeepClone();
                var method = root["method"]?.GetValue<string>() ?? "";
                if (id is null) return null; // notificaciones: nada que responder
                return Reply(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = Dispatch(method, root["params"] as JsonObject) });
            }
            catch (JsonException) { return ErrorReply(id, ParseError, "Parse error"); }
            catch (Failure ex) { return ErrorReply(id, ex.Code, ex.Message); }
            catch (Exception ex) { return ErrorReply(id, InternalError, ex.Message); }
        }

        static string Reply(JsonObject message)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                message.WriteTo(writer);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        static string ErrorReply(JsonNode? id, int code, string message) =>
            Reply(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message } });

        JsonNode Dispatch(string method, JsonObject? p) => method switch
        {
            "initialize" => new JsonObject
            {
                ["protocolVersion"] = p?["protocolVersion"]?.GetValue<string>() is { Length: > 0 } asked ? asked : Protocol,
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = "claupet-contratos", ["title"] = "Claupet Contracts", ["version"] = CptCli.Version },
                ["instructions"] = S(
                    "Contratos Claupet: las reglas de este proyecto. No leas todo: usa contract_search con el tema de la tarea y contract_read con los ids que necesites. Los obligatorios se cumplen siempre.",
                    "Claupet Contracts: the rules of this project. Don't read everything: use contract_search with the task's topic and contract_read with the ids you need. Required ones always apply."),
            },
            "ping" => new JsonObject(),
            "tools/list" => new JsonObject { ["tools"] = Tools() },
            "tools/call" => Call(p),
            _ => throw new Failure(MethodNotFound, "Method not found: " + method),
        };

        JsonArray Tools() =>
        new(
            Tool("contract_list", S("El índice de contratos: id, tipo, título, resumen, a qué rutas aplica y si es obligatorio.", "The contract index: id, type, title, summary, which paths it applies to and whether it is required."), new JsonObject()),
            Tool("contract_search", S("Busca contratos por id, etiqueta, título, resumen o texto de sus reglas.", "Search contracts by id, tag, title, summary or rule text."),
                new JsonObject { ["query"] = Str(S("Qué buscar, por ejemplo «api errores» o «login»", "What to search, e.g. «api errors» or «login»")) }, ["query"]),
            Tool("contract_read", S("Un contrato completo con sus reglas resueltas por capas.", "A full contract with its rules resolved by layer."),
                new JsonObject { ["id"] = Str(S("El id del contrato, por ejemplo «estilo.escritura»", "The contract id, e.g. «estilo.escritura»")) }, ["id"])
        );

        static JsonObject Tool(string name, string description, JsonObject properties, string[]? required = null)
        {
            var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
            if (required is not null)
            {
                var list = new JsonArray();
                foreach (var r in required) list.Add((JsonNode?)r);
                schema["required"] = list;
            }
            return new JsonObject { ["name"] = name, ["description"] = description, ["inputSchema"] = schema };
        }

        static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };

        JsonObject Call(JsonObject? p)
        {
            var name = p?["name"]?.GetValue<string>() ?? throw new Failure(InvalidParams, "Missing tool name");
            var args = p["arguments"] as JsonObject ?? new JsonObject();
            string Arg(string key) => args[key]?.GetValue<string>() is { Length: > 0 } v ? v : throw new Failure(InvalidParams, $"Missing argument: {key}");
            var set = ContractStore.Load(cwd);
            switch (name)
            {
                case "contract_list":
                    return Text(ContractProject.ToJson(ContractProject.Index(set)), false);
                case "contract_search":
                {
                    var query = Arg("query");
                    var hits = ContractSearch.Find(set, query);
                    if (hits.Count == 0) return Text(S($"Nada coincide con «{query}». Prueba contract_list.", $"Nothing matches «{query}». Try contract_list."), false);
                    var sb = new StringBuilder();
                    foreach (var h in hits.Take(10))
                    {
                        sb.Append(h.Contract.Id).Append(" · ").Append(h.Contract.Title).Append(h.Contract.Required ? S(" (obligatorio)", " (required)") : "").Append('\n');
                        sb.Append("  ").Append(h.Contract.Summary).Append('\n');
                        foreach (var r in h.Rules.Take(3)) sb.Append("  [").Append(r.Id).Append("] ").Append(r.Text).Append('\n');
                    }
                    return Text(sb.ToString().TrimEnd(), false);
                }
                case "contract_read":
                {
                    var id = Arg("id");
                    if (set.Find(id) is not { } c) return Text(S($"No hay un contrato «{id}».", $"There is no contract «{id}»."), true);
                    return Text(Render(c), false);
                }
                default:
                    throw new Failure(InvalidParams, "Unknown tool: " + name);
            }
        }

        string Render(ResolvedContract c)
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(c.Title).Append(" (").Append(c.Id).Append(")\n");
            sb.Append(c.Summary).Append('\n');
            sb.Append(c.Required ? S("Obligatorio", "Required") : S("Recomendado", "Recommended"));
            if (!c.Enabled) sb.Append(S(" · apagado", " · off"));
            sb.Append(" · ").Append(S("aplica a ", "applies to ")).Append(string.Join(", ", c.AppliesTo)).Append("\n\n");
            var rules = c.ActiveRules.ToList();
            if (rules.Count > 0)
            {
                sb.Append(S("## Reglas\n", "## Rules\n"));
                foreach (var r in rules) sb.Append("- [").Append(r.Id).Append("] ").Append(r.Text).Append('\n');
                sb.Append('\n');
            }
            var body = CptCli.BodyWithoutRules(c.Body);
            if (body.Length > 0) sb.Append(body).Append('\n');
            return sb.ToString().TrimEnd();
        }

        static JsonObject Text(string text, bool error) => new()
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = error,
        };
    }
}
