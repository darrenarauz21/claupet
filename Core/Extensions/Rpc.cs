using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claupet;

/// <summary>Un error de JSON-RPC: el código y el mensaje, como los manda el otro lado.</summary>
sealed class JsonRpcException(int code, string message) : Exception(message)
{
    public const int ParseError = -32700, InvalidRequest = -32600, MethodNotFound = -32601, InvalidParams = -32602,
        InternalError = -32603, RequestCancelled = -32800, PermissionDenied = -32001, ConnectionClosed = -32002, Timeout = -32003;

    public int Code { get; } = code;
}

/// <summary>
/// JSON-RPC 2.0 en los dos sentidos sobre un par de streams, con el encabezado «Content-Length» de LSP:
/// cada mensaje es «Content-Length: N\r\n\r\n» y N bytes de JSON en UTF-8. Claupet le pide cosas a la
/// extensión (CallAsync) y la extensión a Claupet (Handler, la API del anfitrión). Además:
/// - «$/progress» {"id", "value"}: un avance de la petición id (los trozos de una respuesta o de audio);
/// - «$/cancelRequest» {"id"}: que deje de hacer esa petición.
/// Un mensaje de más de MaxMessage, un encabezado malformado o un JSON inválido cierran la conexión:
/// del otro lado hay un programa que no cumple el protocolo y seguir sería adivinar.
/// </summary>
sealed class JsonRpcConnection : IAsyncDisposable
{
    public const int MaxMessage = 64 << 20;

    /// <summary>Atiende una petición del otro lado; lo que devuelve va como «result» (null también vale).</summary>
    public delegate Task<JsonNode?> RequestHandler(string method, JsonElement parameters, CancellationToken cancel);

    readonly Stream _input, _output;
    readonly SemaphoreSlim _write = new(1, 1);
    readonly ConcurrentDictionary<long, Pending> _pending = new();
    readonly ConcurrentDictionary<string, CancellationTokenSource> _incoming = new();
    readonly CancellationTokenSource _closed = new();
    long _nextId;
    Task? _reader;

    sealed record Pending(TaskCompletionSource<JsonElement> Result, Action<JsonElement>? Progress);

    /// <summary>Mientras corre el Handler: el id de la petición que atiende (para mandarle avances con ProgressAsync).</summary>
    public static readonly AsyncLocal<JsonNode?> CurrentRequestId = new();

    public RequestHandler? Handler { get; init; }
    /// <summary>Una notificación del otro lado (que no sea de las del protocolo).</summary>
    public Action<string, JsonElement>? OnNotification { get; init; }
    /// <summary>La conexión se cerró (el otro lado terminó o mandó algo que no se entiende), con el motivo.</summary>
    public event Action<string>? Closed;

    public JsonRpcConnection(Stream input, Stream output)
    {
        _input = input;
        _output = output;
    }

    public bool IsClosed => _closed.IsCancellationRequested;

    public void Start() => _reader ??= Task.Run(ReadLoopAsync);

    /// <summary>Pide algo y espera la respuesta. Al cancelar, le avisa al otro lado y lanza OperationCanceledException.</summary>
    public async Task<JsonElement> CallAsync(string method, JsonNode? parameters = null, CancellationToken cancel = default,
        Action<JsonElement>? onProgress = null, TimeSpan? timeout = null)
    {
        if (IsClosed) throw new JsonRpcException(JsonRpcException.ConnectionClosed, L.T("La extensión ya no está corriendo."));
        var id = Interlocked.Increment(ref _nextId);
        var pending = new Pending(new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously), onProgress);
        _pending[id] = pending;
        using var limit = timeout is { } t ? new CancellationTokenSource(t) : null;
        using var linked = limit is null ? null : CancellationTokenSource.CreateLinkedTokenSource(cancel, limit.Token);
        var token = linked?.Token ?? cancel;
        try
        {
            await SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters ?? new JsonObject() });
            using (token.Register(() => pending.Result.TrySetCanceled(token)))
            {
                try { return await pending.Result.Task; }
                catch (OperationCanceledException)
                {
                    if (!IsClosed) _ = SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "$/cancelRequest", ["params"] = new JsonObject { ["id"] = id } });
                    if (limit?.IsCancellationRequested == true && !cancel.IsCancellationRequested)
                        throw new JsonRpcException(JsonRpcException.Timeout, L.T("La extensión no respondió a tiempo ({0}).", method));
                    throw;
                }
            }
        }
        finally { _pending.TryRemove(id, out _); }
    }

    public Task NotifyAsync(string method, JsonNode? parameters = null) =>
        SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters ?? new JsonObject() });

    /// <summary>Un avance de una petición que nos hizo el otro lado.</summary>
    public Task ProgressAsync(JsonNode requestId, JsonNode? value) =>
        NotifyAsync("$/progress", new JsonObject { ["id"] = requestId.DeepClone(), ["value"] = value });

    async Task SendAsync(JsonObject message)
    {
        var body = Encoding.UTF8.GetBytes(message.ToJsonString());
        if (body.Length > MaxMessage) throw new JsonRpcException(JsonRpcException.InvalidRequest, L.T("El mensaje es demasiado grande."));
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        // Si el otro lado se cerró mientras tanto (el proceso terminó), _closed cancela la espera o la escritura: es lo
        // mismo que una conexión cortada, no una cancelación de quien llama.
        try { await _write.WaitAsync(_closed.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested)
        {
            throw new JsonRpcException(JsonRpcException.ConnectionClosed, L.T("La extensión ya no está corriendo."));
        }
        try
        {
            await _output.WriteAsync(header, _closed.Token).ConfigureAwait(false);
            await _output.WriteAsync(body, _closed.Token).ConfigureAwait(false);
            await _output.FlushAsync(_closed.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException || ex is OperationCanceledException && _closed.IsCancellationRequested)
        {
            Close(L.T("Se cortó la conexión con la extensión."));
            throw new JsonRpcException(JsonRpcException.ConnectionClosed, L.T("La extensión ya no está corriendo."));
        }
        finally { _write.Release(); }
    }

    async Task ReadLoopAsync()
    {
        var reason = L.T("La extensión terminó.");
        try
        {
            while (!_closed.IsCancellationRequested)
            {
                var body = await ReadMessageAsync().ConfigureAwait(false);
                if (body is null) break;
                JsonDocument doc;
                try { doc = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 64 }); }
                catch (JsonException) { reason = L.T("La extensión mandó un JSON inválido."); break; }
                using (doc) Dispatch(doc.RootElement);
            }
        }
        catch (InvalidDataException ex) { reason = ex.Message; }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException) { }
        Close(reason);
    }

    /// <summary>Un mensaje entero; null si el stream terminó limpio entre mensajes.</summary>
    async Task<byte[]?> ReadMessageAsync()
    {
        var length = -1;
        var line = new StringBuilder();
        var one = new byte[1];
        var headerBytes = 0;
        while (true)
        {
            var read = await _input.ReadAsync(one, _closed.Token).ConfigureAwait(false);
            if (read == 0)
            {
                if (line.Length == 0 && length < 0 && headerBytes == 0) return null;
                throw new InvalidDataException(L.T("La extensión cortó un mensaje a la mitad."));
            }
            if (++headerBytes > 4096) throw new InvalidDataException(L.T("La extensión mandó un encabezado inválido."));
            if (one[0] == '\n')
            {
                var text = line.ToString().TrimEnd('\r');
                line.Clear();
                if (text.Length == 0)
                {
                    if (length < 0) throw new InvalidDataException(L.T("La extensión mandó un mensaje sin Content-Length."));
                    break;
                }
                var colon = text.IndexOf(':');
                if (colon <= 0) throw new InvalidDataException(L.T("La extensión mandó un encabezado inválido."));
                if (text[..colon].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(text[(colon + 1)..].Trim(), out length) || length < 0)
                        throw new InvalidDataException(L.T("La extensión mandó un encabezado inválido."));
                    if (length > MaxMessage) throw new InvalidDataException(L.T("La extensión mandó un mensaje demasiado grande."));
                }
            }
            else line.Append((char)one[0]);
        }
        var body = new byte[length];
        var have = 0;
        while (have < length)
        {
            var read = await _input.ReadAsync(body.AsMemory(have), _closed.Token).ConfigureAwait(false);
            if (read == 0) throw new InvalidDataException(L.T("La extensión cortó un mensaje a la mitad."));
            have += read;
        }
        return body;
    }

    void Dispatch(JsonElement m)
    {
        if (m.ValueKind != JsonValueKind.Object) return;
        var hasId = m.TryGetProperty("id", out var id) && id.ValueKind is JsonValueKind.Number or JsonValueKind.String;
        var method = Json.Str(m, "method");
        if (method is null)
        {
            // Una respuesta a algo que pedimos.
            if (!hasId || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var n) || !_pending.TryGetValue(n, out var pending)) return;
            if (m.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                var code = error.TryGetProperty("code", out var c) && c.TryGetInt32(out var cv) ? cv : JsonRpcException.InternalError;
                pending.Result.TrySetException(new JsonRpcException(code, Json.Str(error, "message") ?? L.T("Error de la extensión.")));
            }
            else pending.Result.TrySetResult(m.TryGetProperty("result", out var result) ? result.Clone() : default);
            return;
        }
        var parameters = m.TryGetProperty("params", out var p) ? p.Clone() : default;
        switch (method)
        {
            case "$/progress":
                if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("id", out var pid) && pid.TryGetInt64(out var pn)
                    && _pending.TryGetValue(pn, out var target) && parameters.TryGetProperty("value", out var value))
                    target.Progress?.Invoke(value.Clone());
                return;
            case "$/cancelRequest":
                if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("id", out var cid) && _incoming.TryGetValue(cid.GetRawText(), out var cts))
                    cts.Cancel();
                return;
        }
        if (!hasId)
        {
            OnNotification?.Invoke(method, parameters);
            return;
        }
        var requestId = JsonNode.Parse(id.GetRawText())!;
        _ = AnswerAsync(requestId, method, parameters);
    }

    async Task AnswerAsync(JsonNode id, string method, JsonElement parameters)
    {
        var key = id.ToJsonString();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token);
        _incoming[key] = cancel;
        JsonObject reply;
        try
        {
            if (Handler is null) throw new JsonRpcException(JsonRpcException.MethodNotFound, L.T("Método desconocido: {0}", method));
            CurrentRequestId.Value = id;
            var result = await Handler(method, parameters, cancel.Token).ConfigureAwait(false);
            reply = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result };
        }
        catch (Exception ex)
        {
            var (code, message) = ex switch
            {
                JsonRpcException rpc => (rpc.Code, rpc.Message),
                OperationCanceledException => (JsonRpcException.RequestCancelled, L.T("Cancelado.")),
                _ => (JsonRpcException.InternalError, ex.Message),
            };
            if (ex is not (JsonRpcException or OperationCanceledException)) ErrorLog.Write(ex);
            reply = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };
        }
        finally { _incoming.TryRemove(key, out _); }
        try { if (!IsClosed) await SendAsync(reply).ConfigureAwait(false); }
        catch (JsonRpcException) { }
    }

    void Close(string reason)
    {
        if (_closed.IsCancellationRequested) return;
        _closed.Cancel();
        foreach (var p in _pending.Values) p.Result.TrySetException(new JsonRpcException(JsonRpcException.ConnectionClosed, reason));
        foreach (var c in _incoming.Values) c.Cancel();
        Closed?.Invoke(reason);
    }

    public async ValueTask DisposeAsync()
    {
        Close(L.T("Se detuvo la extensión."));
        try { _input.Dispose(); } catch (Exception) { }
        try { _output.Dispose(); } catch (Exception) { }
        if (_reader is not null) try { await _reader.ConfigureAwait(false); } catch (Exception) { }
    }
}
