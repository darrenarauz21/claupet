using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>
/// El historial de los agentes que no son Claude Code, preguntándoselo a cada uno por su protocolo (así no depende de
/// dónde ni cómo guarda sus sesiones): Codex con thread/list (app-server) y Gemini CLI, Copilot CLI y OpenCode con
/// session/list (ACP), si lo anuncian. Se abre el agente, se pregunta, se cierra. Solo las de esa carpeta.
/// </summary>
static partial class AgentHistory
{
    static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);
    /// <summary>Hasta cuántas páginas se piden (un agente con muchísimas sesiones no tarda para siempre).</summary>
    const int MaxPages = 10;

    /// <summary>Las sesiones de ese agente en esa carpeta, la más reciente primero; vacío si no está o no sabe listarlas.</summary>
    public static async Task<IReadOnlyList<PastSession>> ListAsync(string kind, string folder, CancellationToken cancel = default)
    {
        if (kind == AgentKinds.Claude) return [];
        var driver = AgentKinds.Driver(kind, new AgentTranscript());
        var options = new AgentOptions(folder, Kind: kind);
        if (driver.Start(options) is not { } start) return [];
        start.WorkingDirectory = Directory.Exists(folder) ? folder : start.WorkingDirectory;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(Limit);
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) return [];
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return []; }
        _ = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            var rpc = new Rpc(process);
            // El initialize del driver (con sus capacidades y clientInfo).
            var init = await rpc.CallAsync(driver.Opening(options).First(), limit.Token);
            return kind == AgentKinds.Codex
                ? await CodexAsync(rpc, folder, limit.Token)
                : await AcpAsync(rpc, kind, folder, init, limit.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or JsonException or InvalidOperationException) { return []; }
        finally
        {
            try { process.StandardInput.Close(); } catch (Exception) { }
            Assistant.Kill(process);
        }
    }

    static async Task<IReadOnlyList<PastSession>> AcpAsync(Rpc rpc, string kind, string folder, JsonElement init, CancellationToken cancel)
    {
        if (!CanList(init)) return [];
        var list = new List<PastSession>();
        string? cursor = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var parameters = new JsonObject { ["cwd"] = folder };
            if (cursor is not null) parameters["cursor"] = cursor;
            var result = await rpc.CallAsync("session/list", parameters, cancel);
            list.AddRange(AcpSessions(result, kind, folder));
            cursor = Json.Str(result, "nextCursor");
            if (cursor is not { Length: > 0 }) break;
        }
        return list.OrderByDescending(p => p.LastUsed).ToList();
    }

    static async Task<IReadOnlyList<PastSession>> CodexAsync(Rpc rpc, string folder, CancellationToken cancel)
    {
        rpc.Notify("initialized");
        var list = new List<PastSession>();
        string? cursor = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var parameters = new JsonObject { ["limit"] = 100 };
            if (cursor is not null) parameters["cursor"] = cursor;
            JsonElement result;
            try { result = await rpc.CallAsync("thread/list", parameters, cancel); }
            catch (InvalidOperationException) when (page == 0)
            {
                // Una versión que no toma «limit»: con lo suyo.
                result = await rpc.CallAsync("thread/list", new JsonObject(), cancel);
            }
            list.AddRange(CodexThreads(result, folder));
            cursor = Json.Str(result, "nextCursor");
            if (cursor is not { Length: > 0 }) break;
        }
        return list.OrderByDescending(p => p.LastUsed).ToList();
    }

    /// <summary>El agente anunció session/list (agentCapabilities.sessionCapabilities.list) en su initialize.</summary>
    internal static bool CanList(JsonElement init) =>
        init.ValueKind == JsonValueKind.Object && init.TryGetProperty("agentCapabilities", out var caps) && caps.ValueKind == JsonValueKind.Object
        && caps.TryGetProperty("sessionCapabilities", out var sessions) && sessions.ValueKind == JsonValueKind.Object
        && sessions.TryGetProperty("list", out var list) && list.ValueKind is not (JsonValueKind.Null or JsonValueKind.False or JsonValueKind.Undefined);

    /// <summary>Las sesiones de una respuesta de session/list (ACP), las de esa carpeta.</summary>
    internal static IEnumerable<PastSession> AcpSessions(JsonElement result, string kind, string folder)
    {
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("sessions", out var sessions) || sessions.ValueKind != JsonValueKind.Array) yield break;
        foreach (var s in sessions.EnumerateArray())
        {
            if (Json.Str(s, "sessionId") is not { Length: > 0 } id) continue;
            if (Json.Str(s, "cwd") is { Length: > 0 } cwd && !Account.SamePath(cwd, folder)) continue;
            var title = Json.Str(s, "title") is { } t && !IsPlaceholder(t) ? t.ReplaceLineEndings(" ").Trim() : null;
            yield return Entry(id, kind, title, "", Date(s, "updatedAt"));
        }
    }

    /// <summary>
    /// Los hilos de una respuesta de thread/list (Codex), los de esa carpeta: su nombre si se lo pusieron y lo primero que
    /// se pidió (preview).
    /// </summary>
    internal static IEnumerable<PastSession> CodexThreads(JsonElement result, string folder)
    {
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) yield break;
        foreach (var t in data.EnumerateArray())
        {
            if (Json.Str(t, "id") is not { Length: > 0 } id) continue;
            if (Json.Str(t, "cwd") is not { Length: > 0 } cwd || !Account.SamePath(cwd, folder)) continue;
            var prompt = (Json.Str(t, "preview") ?? "").ReplaceLineEndings(" ").Trim();
            if (prompt.Length > SessionTitles.PromptLimit) prompt = prompt[..SessionTitles.PromptLimit];
            var name = Json.Str(t, "name") is { Length: > 0 } n ? n.Trim() : null;
            yield return Entry(id, AgentKinds.Codex, name, prompt, Date(t, "updatedAt") ?? Date(t, "createdAt"));
        }
    }

    /// <summary>
    /// Una sesión de la lista: el título del agente; si no tiene, el escrito por el asistente; si no, el pedido recortado;
    /// y si tampoco se sabe qué se pidió, «Sesión de OpenCode».
    /// </summary>
    static PastSession Entry(string id, string kind, string? title, string prompt, DateTime? when)
    {
        var named = title ?? SessionTitles.Cached(id);
        var shown = named ?? (prompt.Length > 0 ? SessionTitles.Short(prompt) : L.T("Sesión de {0}", AgentKinds.Label(kind)));
        return new PastSession(id, shown, when ?? DateTime.MinValue, "", prompt, named is not null, kind);
    }

    /// <summary>El título que el agente pone solo mientras no tiene uno («New session - 2026-11-10T21:25:51.184Z»).</summary>
    internal static bool IsPlaceholder(string title) => string.IsNullOrWhiteSpace(title) || Placeholder().IsMatch(title.Trim());

    /// <summary>Una fecha: ISO 8601 o segundos (o milisegundos) desde 1970, en hora local.</summary>
    static DateTime? Date(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            return date.LocalDateTime;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var number) && number > 0)
            return (number > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(number) : DateTimeOffset.FromUnixTimeSeconds(number)).LocalDateTime;
        return null;
    }

    [GeneratedRegex(@"^(?:new session|untitled|sesi[oó]n nueva|nueva sesi[oó]n)(?:\s*[-–:]\s*\S.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Placeholder();

    /// <summary>JSON-RPC por la entrada y la salida del agente: un pedido y su respuesta (lo demás que mande se salta).</summary>
    sealed class Rpc(Process process)
    {
        int _id = 9000;

        public void Notify(string method)
        {
            process.StandardInput.WriteLine(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method }.ToJsonString());
            process.StandardInput.Flush();
        }

        public Task<JsonElement> CallAsync(string method, JsonObject parameters, CancellationToken cancel)
        {
            var id = ++_id;
            return CallAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters }.ToJsonString(), id, cancel);
        }

        /// <summary>Una línea ya armada (el initialize del driver): su id se lee de ella.</summary>
        public Task<JsonElement> CallAsync(string line, CancellationToken cancel)
        {
            using var doc = JsonDocument.Parse(line);
            var id = doc.RootElement.GetProperty("id");
            return CallAsync(line, id.ValueKind == JsonValueKind.Number ? id.GetInt64() : long.Parse(id.GetString()!, CultureInfo.InvariantCulture), cancel);
        }

        async Task<JsonElement> CallAsync(string line, long id, CancellationToken cancel)
        {
            await process.StandardInput.WriteLineAsync(line.AsMemory(), cancel);
            await process.StandardInput.FlushAsync(cancel);
            while (await process.StandardOutput.ReadLineAsync(cancel) is { } reply)
            {
                if (reply.Length == 0 || reply[0] != '{') continue;
                using var doc = JsonDocument.Parse(reply);
                var r = doc.RootElement;
                if (!r.TryGetProperty("id", out var got) || r.TryGetProperty("method", out _)) continue; // un aviso o un pedido del agente
                var same = got.ValueKind == JsonValueKind.Number ? got.TryGetInt64(out var n) && n == id : got.GetString() == id.ToString(CultureInfo.InvariantCulture);
                if (!same) continue;
                if (r.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                    throw new InvalidOperationException(Json.Str(error, "message") ?? "error");
                return r.TryGetProperty("result", out var result) ? result.Clone() : default;
            }
            throw new IOException("El agente se cerró.");
        }
    }
}
