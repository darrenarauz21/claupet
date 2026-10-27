using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>Voz a texto: el integrado (Whisper) o el de una extensión.</summary>
interface ISpeechToText
{
    string Name { get; }
    /// <summary>pcm: 16 kHz, mono, 16 bits. language: «es», «en»…</summary>
    Task<string> TranscribeAsync(short[] pcm, string language, CancellationToken cancel);
}

/// <summary>Texto a voz: el integrado (Supertonic o Windows) o el de una extensión.</summary>
interface ITextToSpeech
{
    string Name { get; }
    /// <summary>Entiende las etiquetas &lt;laugh&gt;, &lt;sigh&gt; y &lt;breath&gt; (si no, se quitan antes).</summary>
    bool Expressive { get; }
    /// <summary>Lo dice por partes: onAudio recibe cada trozo (muestras de -1 a 1) y su frecuencia, en orden.</summary>
    Task SpeakAsync(string text, string? voice, string language, Action<float[], int> onAudio, CancellationToken cancel);
}

/// <summary>Un mensaje de la conversación para un asistente: «user» o «assistant».</summary>
sealed record BrainMessage(string Role, string Text);

sealed record BrainRequest(string System, IReadOnlyList<BrainMessage> Messages, string? Model);

sealed record BrainReply(string Text, string? Model);

/// <summary>
/// El asistente del chat cuando no es Claude Code: un modelo local (Ollama, LM Studio) o externo (DeepSeek,
/// OpenRouter, Codex…). Recibe el mismo prompt de sistema que Claude (sin las herramientas que no tiene) y
/// su respuesta pasa por las mismas reglas: las marcas [ACCION] y [TAREA] y sus permisos.
/// </summary>
interface IAssistantBrain
{
    string Name { get; }
    Task<BrainReply> RespondAsync(BrainRequest request, Action<string>? onText, CancellationToken cancel);
}

/// <summary>Audio de un lado a otro: PCM de 16 bits en base64 (lo que viaja en JSON) y muestras float.</summary>
static class AudioCodec
{
    public static string ToBase64(short[] pcm)
    {
        var bytes = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    public static float[] FromBase64(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        var samples = new float[bytes.Length / 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
        return samples;
    }

    /// <summary>Un WAV en memoria (PCM de 16 bits): sus muestras de -1 a 1 (los canales se mezclan) y su frecuencia.</summary>
    public static (float[] Samples, int Rate) FromWav(byte[] wav)
    {
        if (wav.Length < 44 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
            throw new InvalidDataException(L.T("El audio no es un WAV."));
        int at = 12, rate = 0, channels = 1, bits = 16;
        while (at + 8 <= wav.Length)
        {
            var id = Encoding.ASCII.GetString(wav, at, 4);
            var size = BitConverter.ToInt32(wav, at + 4);
            if (size < 0) break;
            if (id == "fmt " && size >= 16)
            {
                if (BitConverter.ToInt16(wav, at + 8) != 1) throw new InvalidDataException(L.T("El WAV no es PCM."));
                channels = BitConverter.ToInt16(wav, at + 10);
                rate = BitConverter.ToInt32(wav, at + 12);
                bits = BitConverter.ToInt16(wav, at + 22);
            }
            else if (id == "data")
            {
                if (bits != 16 || channels < 1 || rate <= 0) throw new InvalidDataException(L.T("El WAV debe ser de 16 bits."));
                // Un servidor que transmite pone 0 o 0xFFFFFFFF como tamaño: hasta el final.
                var length = size <= 0 || at + 8 + size > wav.Length ? wav.Length - at - 8 : size;
                var frames = length / 2 / channels;
                var samples = new float[frames];
                for (var f = 0; f < frames; f++)
                {
                    float sum = 0;
                    for (var c = 0; c < channels; c++) sum += BitConverter.ToInt16(wav, at + 8 + (f * channels + c) * 2) / 32768f;
                    samples[f] = sum / channels;
                }
                return (samples, rate);
            }
            at += 8 + size + (size & 1);
        }
        throw new InvalidDataException(L.T("El WAV no tiene audio."));
    }

    public static byte[] ToWav(short[] pcm, int rate)
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            w.Write("RIFF"u8); w.Write(36 + pcm.Length * 2); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(pcm.Length * 2);
            foreach (var s in pcm) w.Write(s);
        }
        return stream.ToArray();
    }
}

/// <summary>Los proveedores que da una extensión según su runtime: por JSON-RPC, por HTTP o por consola.</summary>
static class ExtensionProviders
{
    /// <summary>Un servidor con un formato de OpenAI; uno «claupet-rpc» habla JSON-RPC como un programa.</summary>
    static bool OpenAiFormat(InstalledExtension ext) =>
        ext.Manifest.Runtime is { Type: RuntimeSpec.Http } runtime && runtime.Format != RuntimeSpec.ClaupetRpc;

    public static ISpeechToText? SpeechToText(ExtensionManager manager) =>
        manager.ProviderFor(ExtensionSlots.SpeechToText) is { } ext
            ? OpenAiFormat(ext) ? new OpenAiTranscription(manager, ext) : new RpcSpeechToText(manager, ext)
            : null;

    public static ITextToSpeech? TextToSpeech(ExtensionManager manager) =>
        manager.ProviderFor(ExtensionSlots.TextToSpeech) is { } ext
            ? OpenAiFormat(ext) ? new OpenAiSpeech(manager, ext) : new RpcTextToSpeech(manager, ext)
            : null;

    public static IAssistantBrain? Assistant(ExtensionManager manager) =>
        manager.ProviderFor(ExtensionSlots.Assistant) is { } ext
            ? ext.Manifest.Runtime! switch
            {
                _ when OpenAiFormat(ext) => new OpenAiChat(manager, ext),
                { Protocol: RuntimeSpec.Text } => new TextCliBrain(manager, ext),
                _ => new RpcBrain(manager, ext),
            }
            : null;

    /// <summary>
    /// La dirección del servidor con los {setting:clave} ya puestos (solo ajustes que no son claves) y el
    /// destino comprobado: https (o http en este equipo) y a un host que la extensión declaró.
    /// </summary>
    public static Uri Endpoint(ExtensionManager manager, InstalledExtension ext, string path)
    {
        var runtime = ext.Manifest.Runtime!;
        var url = Regex.Replace(runtime.Url ?? "", @"\{setting:([A-Za-z0-9_]+)\}", m =>
        {
            var spec = ext.Manifest.Settings.FirstOrDefault(s => s.Key == m.Groups[1].Value);
            return spec is null || spec.Type == SettingSpec.Secret ? "" : manager.ValueOf(ext, spec.Key) ?? "";
        });
        if (!Uri.TryCreate(url.TrimEnd('/') + path, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0)
            throw new ExtensionUnavailableException(L.T("La dirección del servidor de «{0}» no es válida: revise sus ajustes.", ext.Manifest.Name.Text));
        if (!uri.IsLoopback)
        {
            if (uri.Scheme != "https") throw new ExtensionUnavailableException(L.T("Por http solo se permite este equipo (localhost); afuera, https."));
            if (!ExtensionPermissions.AllowsHost(ext.Manifest.Permissions, uri.Host))
                throw new ExtensionUnavailableException(L.T("«{0}» no declaró conectarse a {1}.", ext.Manifest.Name.Text, uri.Host));
        }
        return uri;
    }

    /// <summary>La clave de la API en el encabezado que pide el manifiesto («bearer» o «header:Nombre»).</summary>
    public static void Authorize(HttpRequestMessage request, ExtensionManager manager, InstalledExtension ext)
    {
        var runtime = ext.Manifest.Runtime!;
        if (runtime.Auth == "none") return;
        var key = manager.ValueOf(ext, runtime.AuthSetting!);
        if (string.IsNullOrEmpty(key)) throw new ExtensionUnavailableException(L.T("Falta la clave de «{0}»: póngala en Ajustes › Extensiones.", ext.Manifest.Name.Text));
        if (runtime.Auth == "bearer") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        else request.Headers.TryAddWithoutValidation(runtime.Auth[7..], key);
    }

    /// <summary>El modelo: el ajuste «model» si la extensión lo tiene, o el del runtime.</summary>
    public static string? Model(ExtensionManager manager, InstalledExtension ext, string? requested = null) =>
        requested is { Length: > 0 } ? requested
        : ext.Manifest.Settings.Any(s => s.Key == "model") && manager.ValueOf(ext, "model") is { Length: > 0 } m ? m
        : ext.Manifest.Runtime?.Model;

    /// <summary>Un error del servidor dicho en corto: su mensaje (formato OpenAI) o el código.</summary>
    public static async Task<string> ErrorOf(HttpResponseMessage response, InstalledExtension ext)
    {
        var body = "";
        try { body = await response.Content.ReadAsStringAsync(); } catch (Exception) { }
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            message = doc.RootElement.TryGetProperty("error", out var e)
                ? e.ValueKind == JsonValueKind.String ? e.GetString() : Json.Str(e, "message")
                : Json.Str(doc.RootElement, "message");
        }
        catch (JsonException) { }
        var status = (int)response.StatusCode;
        var why = status switch
        {
            401 or 403 => L.T("la clave no es válida o no tiene acceso"),
            404 => L.T("no existe esa dirección o ese modelo"),
            429 => L.T("se alcanzó el límite de uso"),
            >= 500 => L.T("el servidor tuvo un problema"),
            _ => L.T("error {0}", status),
        };
        var detail = message is { Length: > 0 } ? $" ({(message.Length > 160 ? message[..160] + "…" : message)})" : "";
        return L.T("«{0}» no respondió: {1}{2}.", ext.Manifest.Name.Text, why, detail);
    }
}

// ── Por JSON-RPC (un programa o una DLL) ──────────────────────────────────────────────────────

sealed class RpcSpeechToText(ExtensionManager manager, InstalledExtension ext) : ISpeechToText
{
    public string Name => ext.Manifest.Name.Text;

    public async Task<string> TranscribeAsync(short[] pcm, string language, CancellationToken cancel)
    {
        var result = await manager.CallAsync(ext.Id, "speechToText/transcribe", new JsonObject
        {
            ["audio"] = AudioCodec.ToBase64(pcm), ["sampleRate"] = 16000, ["language"] = language,
        }, cancel, timeout: TimeSpan.FromMinutes(2));
        return result.ValueKind == JsonValueKind.Object ? Json.Str(result, "text")?.Trim() ?? "" : "";
    }
}

sealed class RpcTextToSpeech(ExtensionManager manager, InstalledExtension ext) : ITextToSpeech
{
    public string Name => ext.Manifest.Name.Text;
    public bool Expressive => false;

    public async Task SpeakAsync(string text, string? voice, string language, Action<float[], int> onAudio, CancellationToken cancel)
    {
        void Take(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object && Json.Str(value, "audio") is { } audio
                && value.TryGetProperty("sampleRate", out var r) && r.TryGetInt32(out var rate) && rate is >= 8000 and <= 192_000)
                onAudio(AudioCodec.FromBase64(audio), rate);
        }
        var result = await manager.CallAsync(ext.Id, "textToSpeech/synthesize", new JsonObject
        {
            ["text"] = text, ["voice"] = voice, ["language"] = language, ["stream"] = true,
        }, cancel, Take, TimeSpan.FromMinutes(5));
        Take(result); // lo que no mandó por partes, al final
    }
}

sealed class RpcBrain(ExtensionManager manager, InstalledExtension ext) : IAssistantBrain
{
    public string Name => ext.Manifest.Name.Text;

    public async Task<BrainReply> RespondAsync(BrainRequest request, Action<string>? onText, CancellationToken cancel)
    {
        var streamed = new StringBuilder();
        var result = await manager.CallAsync(ext.Id, "assistant/respond", new JsonObject
        {
            ["system"] = request.System,
            ["messages"] = new JsonArray(request.Messages.Select(m => (JsonNode)new JsonObject { ["role"] = m.Role, ["text"] = m.Text }).ToArray()),
            ["model"] = ExtensionProviders.Model(manager, ext, request.Model),
            ["language"] = L.Language,
            ["stream"] = true,
        }, cancel, value =>
        {
            if (value.ValueKind == JsonValueKind.Object && Json.Str(value, "text") is { Length: > 0 } piece)
            {
                streamed.Append(piece);
                onText?.Invoke(piece);
            }
        }, TimeSpan.FromMinutes(5));
        var text = result.ValueKind == JsonValueKind.Object ? Json.Str(result, "text") : null;
        return new BrainReply(text ?? streamed.ToString(), result.ValueKind == JsonValueKind.Object ? Json.Str(result, "model") : null);
    }
}

// ── Por HTTP, en los formatos de OpenAI ───────────────────────────────────────────────────────

/// <summary>/chat/completions con stream (SSE): DeepSeek, Ollama, LM Studio, OpenRouter, Groq, vLLM…</summary>
sealed class OpenAiChat(ExtensionManager manager, InstalledExtension ext) : IAssistantBrain
{
    public string Name => ext.Manifest.Name.Text;

    public async Task<BrainReply> RespondAsync(BrainRequest request, Action<string>? onText, CancellationToken cancel)
    {
        var uri = ExtensionProviders.Endpoint(manager, ext, "/chat/completions");
        var model = ExtensionProviders.Model(manager, ext, request.Model)
            ?? throw new ExtensionUnavailableException(L.T("«{0}» no dice qué modelo usar: elíjalo en sus ajustes.", Name));
        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = request.System } };
        foreach (var m in request.Messages) messages.Add(new JsonObject { ["role"] = m.Role, ["content"] = m.Text });
        using var http = SystemProxy.Client(TimeSpan.FromMinutes(5));
        using var message = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(new JsonObject { ["model"] = model, ["messages"] = messages, ["stream"] = true }.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        ExtensionProviders.Authorize(message, manager, ext);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (!response.IsSuccessStatusCode) throw new ExtensionUnavailableException(await ExtensionProviders.ErrorOf(response, ext));

        var text = new StringBuilder();
        string? answeredBy = null;
        // Algunos servidores ignoran stream y mandan la respuesta entera en JSON.
        if (response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
            var whole = Content(doc.RootElement, "message");
            onText?.Invoke(whole);
            return new BrainReply(whole, Json.Str(doc.RootElement, "model"));
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancel) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") break;
            try
            {
                using var doc = JsonDocument.Parse(data);
                answeredBy ??= Json.Str(doc.RootElement, "model");
                if (doc.RootElement.TryGetProperty("error", out var error))
                    throw new ExtensionUnavailableException(L.T("«{0}» no respondió: {1}.", Name, Json.Str(error, "message") ?? error.GetRawText()));
                var piece = Content(doc.RootElement, "delta");
                if (piece.Length == 0) continue;
                text.Append(piece);
                onText?.Invoke(piece);
            }
            catch (JsonException) { }
        }
        return new BrainReply(text.ToString(), answeredBy ?? model);
    }

    /// <summary>choices[0].delta.content (o message.content). Los modelos que «razonan» mandan reasoning_content aparte: no se muestra.</summary>
    static string Content(JsonElement root, string part) =>
        root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
        && choices[0].TryGetProperty(part, out var d) && d.ValueKind == JsonValueKind.Object
            ? Json.Str(d, "content") ?? "" : "";
}

/// <summary>/audio/transcriptions (multipart, con el WAV): servidores de Whisper, Groq, OpenAI…</summary>
sealed class OpenAiTranscription(ExtensionManager manager, InstalledExtension ext) : ISpeechToText
{
    public string Name => ext.Manifest.Name.Text;

    public async Task<string> TranscribeAsync(short[] pcm, string language, CancellationToken cancel)
    {
        var uri = ExtensionProviders.Endpoint(manager, ext, "/audio/transcriptions");
        using var http = SystemProxy.Client(TimeSpan.FromMinutes(2));
        using var form = new MultipartFormDataContent();
        var wav = new ByteArrayContent(AudioCodec.ToWav(pcm, 16000));
        wav.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(wav, "file", "audio.wav");
        if (ExtensionProviders.Model(manager, ext) is { } model) form.Add(new StringContent(model), "model");
        form.Add(new StringContent(language), "language");
        form.Add(new StringContent("json"), "response_format");
        using var message = new HttpRequestMessage(HttpMethod.Post, uri) { Content = form };
        ExtensionProviders.Authorize(message, manager, ext);
        using var response = await http.SendAsync(message, cancel);
        if (!response.IsSuccessStatusCode) throw new ExtensionUnavailableException(await ExtensionProviders.ErrorOf(response, ext));
        var body = await response.Content.ReadAsStringAsync(cancel);
        try
        {
            using var doc = JsonDocument.Parse(body);
            return Json.Str(doc.RootElement, "text")?.Trim() ?? "";
        }
        catch (JsonException) { return body.Trim(); }
    }
}

/// <summary>/audio/speech: pide WAV, para no depender del formato «pcm» de cada servidor.</summary>
sealed class OpenAiSpeech(ExtensionManager manager, InstalledExtension ext) : ITextToSpeech
{
    public string Name => ext.Manifest.Name.Text;
    public bool Expressive => false;

    public async Task SpeakAsync(string text, string? voice, string language, Action<float[], int> onAudio, CancellationToken cancel)
    {
        var uri = ExtensionProviders.Endpoint(manager, ext, "/audio/speech");
        var chosen = voice ?? (ext.Manifest.Settings.Any(s => s.Key == "voice") ? manager.ValueOf(ext, "voice") : null);
        var body = new JsonObject { ["input"] = text, ["response_format"] = "wav" };
        if (ExtensionProviders.Model(manager, ext) is { } model) body["model"] = model;
        if (chosen is { Length: > 0 }) body["voice"] = chosen;
        using var http = SystemProxy.Client(TimeSpan.FromMinutes(2));
        using var message = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        ExtensionProviders.Authorize(message, manager, ext);
        using var response = await http.SendAsync(message, cancel);
        if (!response.IsSuccessStatusCode) throw new ExtensionUnavailableException(await ExtensionProviders.ErrorOf(response, ext));
        var (samples, rate) = AudioCodec.FromWav(await response.Content.ReadAsByteArrayAsync(cancel));
        onAudio(samples, rate);
    }
}

// ── Por consola (protocol «text») ─────────────────────────────────────────────────────────────

/// <summary>
/// Un asistente que es un programa de consola: por cada pregunta se corre una vez, con la conversación por
/// stdin. La respuesta es lo que escribe en stdout (se muestra mientras llega) o, si los argumentos
/// nombran {output}, lo que deja en ese archivo (codex exec -o {output}). {model} es el modelo elegido.
/// </summary>
sealed class TextCliBrain(ExtensionManager manager, InstalledExtension ext) : IAssistantBrain
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(5);

    public string Name => ext.Manifest.Name.Text;

    public async Task<BrainReply> RespondAsync(BrainRequest request, Action<string>? onText, CancellationToken cancel)
    {
        var problem = await Task.Run(() => manager.Store.Verify(ext, cancel), cancel);
        if (problem is not null) throw new ExtensionUnavailableException(problem);
        var runtime = ext.Manifest.Runtime!;
        var file = runtime.Program is { } program
            ? ProgramResolver.Find(program) ?? throw new ExtensionUnavailableException(L.T("«{0}» necesita «{1}», que no está instalado (o no está en el PATH).", Name, program))
            : ext.FileOf(runtime.TargetFor(ExtensionPlatform.Current) ?? throw new ExtensionUnavailableException(L.T("«{0}» no trae su programa para {1}.", Name, ExtensionPlatform.Current)));
        Directory.CreateDirectory(ext.DataFolder);
        var output = Path.Combine(ext.DataFolder, $"respuesta-{Guid.NewGuid():N}.txt");
        var model = ExtensionProviders.Model(manager, ext, request.Model) ?? "";
        var usesOutput = runtime.Args.Any(a => a.Contains("{output}"));
        var args = runtime.Args.Select(a => a.Replace("{package}", ext.Folder).Replace("{data}", ext.DataFolder).Replace("{output}", output).Replace("{model}", model))
            .ToList();

        var start = new ProcessStartInfo
        {
            WorkingDirectory = ext.DataFolder, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false),
        };
        if (OperatingSystem.IsWindows() && (file.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            start.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            foreach (var a in new[] { "/d", "/s", "/c", file }) start.ArgumentList.Add(a);
        }
        else start.FileName = file;
        foreach (var a in args) start.ArgumentList.Add(a);
        foreach (var name in new[] { "ANTHROPIC_API_KEY", "CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CONFIG_DIR" }) start.Environment.Remove(name);
        if (SystemProxy.ForClaude() is { } proxy) start.Environment["HTTPS_PROXY"] = proxy;

        using var job = new KillOnCloseJob();
        using var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (Exception ex) { throw new ExtensionUnavailableException(L.T("No se pudo arrancar «{0}»: {1}", Name, ex.Message)); }
        job.Add(process);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(Limit);
        using var kill = limit.Token.Register(() => { try { process.Kill(entireProcessTree: true); } catch (Exception) { } });
        try
        {
            await process.StandardInput.WriteAsync(Conversation(request));
            process.StandardInput.Close();
            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
            var text = new StringBuilder();
            var buffer = new char[512];
            int read;
            while ((read = await process.StandardOutput.ReadAsync(buffer, CancellationToken.None)) > 0)
            {
                var piece = new string(buffer, 0, read);
                text.Append(piece);
                if (!usesOutput) onText?.Invoke(piece);
            }
            await process.WaitForExitAsync(CancellationToken.None);
            cancel.ThrowIfCancellationRequested();
            if (limit.IsCancellationRequested) throw new ExtensionUnavailableException(L.T("«{0}» no respondió a tiempo.", Name));
            var log = new ExtensionLog(ext.DataFolder);
            var errors = (await stderr).Trim();
            if (errors.Length > 0) log.Write(errors.Length > 4000 ? errors[..4000] : errors);
            if (process.ExitCode != 0)
                throw new ExtensionUnavailableException(L.T("«{0}» terminó con el código {1}: {2}", Name, process.ExitCode,
                    errors.Split('\n').LastOrDefault(l => l.Trim().Length > 0)?.Trim() ?? ""));
            var answer = usesOutput && File.Exists(output) ? await File.ReadAllTextAsync(output, CancellationToken.None) : text.ToString();
            if (usesOutput) onText?.Invoke(answer);
            return new BrainReply(answer.Trim(), model.Length > 0 ? model : null);
        }
        finally
        {
            try { File.Delete(output); } catch (Exception) { }
        }
    }

    /// <summary>La conversación como texto (con «\n», también en Windows): las instrucciones, lo anterior y la pregunta.</summary>
    static string Conversation(BrainRequest request)
    {
        var text = new StringBuilder(request.System.Trim()).Append("\n\n");
        foreach (var m in request.Messages)
            text.Append(m.Role == "user" ? L.T("Persona:") : L.T("Tú:")).Append(' ').Append(m.Text.Trim()).Append('\n');
        return text.Append(L.T("Tú:")).ToString();
    }
}

/// <summary>
/// Una habilidad en un servidor que habla JSON-RPC por HTTP (formato «claupet-rpc»): cada petición es un
/// POST con el mensaje. Solo en un sentido: el servidor no puede llamar a la API de Claupet.
/// </summary>
sealed class HttpRpcChannel(ExtensionManager manager, InstalledExtension ext) : IExtensionChannel
{
    long _next;
    public bool Alive => true;
    public event Action<string>? Stopped { add { } remove { } }

    public async Task<JsonElement> CallAsync(string method, JsonNode? parameters, CancellationToken cancel, Action<JsonElement>? onProgress = null, TimeSpan? timeout = null)
    {
        var uri = ExtensionProviders.Endpoint(manager, ext, "");
        using var http = SystemProxy.Client(timeout ?? TimeSpan.FromMinutes(2));
        var body = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = Interlocked.Increment(ref _next), ["method"] = method, ["params"] = parameters ?? new JsonObject() };
        using var message = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        ExtensionProviders.Authorize(message, manager, ext);
        using var response = await http.SendAsync(message, cancel);
        if (!response.IsSuccessStatusCode) throw new JsonRpcException(JsonRpcException.InternalError, await ExtensionProviders.ErrorOf(response, ext));
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            throw new JsonRpcException(error.TryGetProperty("code", out var c) && c.TryGetInt32(out var code) ? code : JsonRpcException.InternalError,
                Json.Str(error, "message") ?? L.T("Error de la extensión."));
        return doc.RootElement.TryGetProperty("result", out var result) ? result.Clone() : default;
    }

    public Task NotifyAsync(string method, JsonNode? parameters) => CallAsync(method, parameters, CancellationToken.None);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
