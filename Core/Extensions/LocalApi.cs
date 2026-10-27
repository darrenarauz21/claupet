using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet;

/// <summary>Un token de la API local creado en Ajustes: se guarda solo su SHA-256; el token se muestra una vez.</summary>
sealed class ApiToken
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "";
    public string Hash { get; set; } = "";
    public List<string> Permissions { get; set; } = new();
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// La API local: otros programas del equipo le piden cosas a Claupet (que la mascota hable, un aviso, leer
/// en voz alta…) con JSON-RPC por HTTP en http://127.0.0.1:&lt;puerto&gt;/v1/rpc. Apagada por defecto. Cada
/// pedido lleva «Authorization: Bearer &lt;token&gt;»: un token creado en Ajustes (con sus permisos) o el de
/// una extensión que corre (CLAUPET_API_TOKEN, con los permisos de esa extensión; cambia en cada arranque).
/// Contra las páginas web: solo escucha en 127.0.0.1, rechaza todo pedido con «Origin» (los navegadores
/// siempre lo mandan), exige «Host» de este equipo (contra el DNS rebinding) y «Content-Type:
/// application/json» (que obliga a un preflight que nunca se aprueba). Es un servidor HTTP/1.1 mínimo
/// sobre TCP, sin http.sys: no pide permisos de administrador.
/// </summary>
sealed class LocalApiServer : IAsyncDisposable
{
    public const int DefaultPort = 47615;
    const int MaxHeader = 16 * 1024, MaxBody = 1 << 20, PerSecond = 20;

    readonly ExtensionManager _manager;
    readonly Func<IReadOnlyList<ApiToken>> _tokens;
    readonly ConcurrentDictionary<string, HostCaller> _sessionTokens = new();
    readonly ConcurrentDictionary<string, (long Second, int Count)> _rate = new();
    TcpListener? _listener;
    CancellationTokenSource? _stop;
    Task? _loop;

    public LocalApiServer(ExtensionManager manager, Func<IReadOnlyList<ApiToken>> tokens)
    {
        _manager = manager;
        _tokens = tokens;
    }

    public int Port { get; private set; }

    public bool Running => _listener is not null;

    public string Url => $"http://127.0.0.1:{Port}/v1/rpc";

    /// <summary>Un token nuevo: el texto (para mostrarlo una vez) y lo que se guarda.</summary>
    public static (string Token, ApiToken Saved) NewToken(string name, IEnumerable<string> permissions)
    {
        var token = "cpt_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (token, new ApiToken { Name = name.Trim(), Hash = Hash(token), Permissions = permissions.Where(ExtensionPermissions.Enforced).Distinct().ToList() });
    }

    static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>Un token para una extensión que arranca (con sus permisos); se olvida al detenerla.</summary>
    public string IssueFor(HostCaller caller)
    {
        var token = "cpe_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _sessionTokens[Hash(token)] = caller;
        return token;
    }

    public void Revoke(string extensionId)
    {
        foreach (var (hash, caller) in _sessionTokens.ToArray())
            if (caller.Id == extensionId) _sessionTokens.TryRemove(hash, out _);
    }

    /// <summary>Empieza a escuchar. Si el puerto está ocupado, lanza SocketException (se avisa en Ajustes).</summary>
    public void Start(int port)
    {
        if (_listener is not null) return;
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start(32);
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _stop = new CancellationTokenSource();
        _loop = Task.Run(() => AcceptLoopAsync(listener, _stop.Token));
    }

    async Task AcceptLoopAsync(TcpListener listener, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stop); }
            catch (Exception) when (stop.IsCancellationRequested) { return; }
            catch (SocketException) { continue; }
            _ = Task.Run(() => ServeAsync(client, stop));
        }
    }

    async Task ServeAsync(TcpClient client, CancellationToken stop)
    {
        using (client)
        {
            client.ReceiveTimeout = client.SendTimeout = 10_000;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            var stream = client.GetStream();
            try
            {
                var (status, body) = await HandleAsync(stream, timeout.Token);
                await WriteAsync(stream, status, body, timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException) { }
        }
    }

    async Task<(int Status, JsonNode Body)> HandleAsync(NetworkStream stream, CancellationToken cancel)
    {
        var head = await ReadHeadAsync(stream, cancel);
        if (head is null) return (400, Error(null, JsonRpcException.InvalidRequest, "bad request"));
        var lines = head.Split("\r\n");
        var request = lines[0].Split(' ');
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        // El cuerpo se lee antes de responder (aun si se va a rechazar): cerrar con datos sin leer corta la conexión
        // sin que el cliente alcance a leer la respuesta.
        if (!int.TryParse(headers.GetValueOrDefault("Content-Length"), out var length) || length is < 0 or > MaxBody)
            return (413, Error(null, JsonRpcException.InvalidRequest, "Content-Length"));
        var body = new byte[length];
        var have = 0;
        while (have < length)
        {
            var read = await stream.ReadAsync(body.AsMemory(have), cancel);
            if (read == 0) return (400, Error(null, JsonRpcException.InvalidRequest, "truncated body"));
            have += read;
        }
        if (headers.ContainsKey("Origin")) return (403, Error(null, JsonRpcException.PermissionDenied, "browser requests are not allowed"));
        var host = headers.GetValueOrDefault("Host") ?? "";
        if (host != $"127.0.0.1:{Port}" && host != $"localhost:{Port}") return (403, Error(null, JsonRpcException.PermissionDenied, "bad host"));
        if (request.Length != 3 || request[0] != "POST" || request[1] != "/v1/rpc") return (404, Error(null, JsonRpcException.MethodNotFound, "POST /v1/rpc"));
        if (!(headers.GetValueOrDefault("Content-Type") ?? "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            return (415, Error(null, JsonRpcException.InvalidRequest, "Content-Type: application/json"));
        var auth = headers.GetValueOrDefault("Authorization") ?? "";
        var caller = auth.StartsWith("Bearer ", StringComparison.Ordinal) ? CallerFor(auth[7..].Trim()) : null;
        if (caller is null) return (401, Error(null, JsonRpcException.PermissionDenied, "missing or invalid token"));
        if (!Allow(caller.Id)) return (429, Error(null, JsonRpcException.InvalidRequest, "too many requests"));
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 32 }); }
        catch (JsonException) { return (400, Error(null, JsonRpcException.ParseError, "invalid JSON")); }
        using (doc)
        {
            var r = doc.RootElement;
            var id = r.ValueKind == JsonValueKind.Object && r.TryGetProperty("id", out var i) ? JsonNode.Parse(i.GetRawText()) : null;
            var method = r.ValueKind == JsonValueKind.Object ? Json.Str(r, "method") : null;
            if (method is null) return (400, Error(id, JsonRpcException.InvalidRequest, "method"));
            var parameters = r.TryGetProperty("params", out var p) ? p.Clone() : default;
            try
            {
                var result = await _manager.HandleAsync(caller, method, parameters, cancel);
                return (200, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });
            }
            catch (JsonRpcException ex) { return (200, Error(id, ex.Code, ex.Message)); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ErrorLog.Write(ex);
                return (200, Error(id, JsonRpcException.InternalError, ex.Message));
            }
        }
    }

    HostCaller? CallerFor(string token)
    {
        var hash = Hash(token);
        if (_sessionTokens.TryGetValue(hash, out var caller)) return caller;
        var saved = _tokens().FirstOrDefault(t => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(t.Hash), Encoding.ASCII.GetBytes(hash)));
        return saved is null ? null : new HostCaller("api:" + saved.Id, saved.Name, saved.Permissions, null);
    }

    bool Allow(string caller)
    {
        var second = Environment.TickCount64 / 1000;
        var now = _rate.AddOrUpdate(caller, (second, 1), (_, v) => v.Second == second ? (second, v.Count + 1) : (second, 1));
        return now.Count <= PerSecond;
    }

    static JsonObject Error(JsonNode? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    static async Task<string?> ReadHeadAsync(NetworkStream stream, CancellationToken cancel)
    {
        var buffer = new List<byte>(1024);
        var one = new byte[1];
        while (buffer.Count < MaxHeader)
        {
            if (await stream.ReadAsync(one, cancel) == 0) return null;
            buffer.Add(one[0]);
            var n = buffer.Count;
            if (n >= 4 && buffer[n - 4] == '\r' && buffer[n - 3] == '\n' && buffer[n - 2] == '\r' && buffer[n - 1] == '\n')
                return Encoding.ASCII.GetString(buffer.ToArray(), 0, n - 4);
        }
        return null;
    }

    static async Task WriteAsync(NetworkStream stream, int status, JsonNode body, CancellationToken cancel)
    {
        var text = Encoding.UTF8.GetBytes(body.ToJsonString());
        var reason = status switch { 200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found", 413 => "Payload Too Large", 415 => "Unsupported Media Type", 429 => "Too Many Requests", _ => "Error" };
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {text.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
        await stream.WriteAsync(head, cancel);
        await stream.WriteAsync(text, cancel);
        await stream.FlushAsync(cancel);
    }

    public async ValueTask DisposeAsync()
    {
        _stop?.Cancel();
        _listener?.Stop();
        _listener = null;
        if (_loop is not null) try { await _loop; } catch (Exception) { }
        _sessionTokens.Clear();
    }
}
