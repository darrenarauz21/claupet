using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeUsageTray.Tests;

public class RpcTests
{
    /// <summary>Dos conexiones unidas por pipes anónimos, como un proceso y Claupet.</summary>
    static (JsonRpcConnection A, JsonRpcConnection B) Pair(JsonRpcConnection.RequestHandler? a = null, JsonRpcConnection.RequestHandler? b = null)
    {
        var aToB = new AnonymousPipeServerStream(PipeDirection.Out);
        var bFromA = new AnonymousPipeClientStream(PipeDirection.In, aToB.ClientSafePipeHandle);
        var bToA = new AnonymousPipeServerStream(PipeDirection.Out);
        var aFromB = new AnonymousPipeClientStream(PipeDirection.In, bToA.ClientSafePipeHandle);
        var left = new JsonRpcConnection(aFromB, aToB) { Handler = a };
        var right = new JsonRpcConnection(bFromA, bToA) { Handler = b };
        left.Start();
        right.Start();
        return (left, right);
    }

    [Fact]
    public async Task CallsBothWaysWithProgress()
    {
        JsonRpcConnection? other = null;
        var (claupet, ext) = Pair(
            a: (method, p, _) => Task.FromResult<JsonNode?>(new JsonObject { ["echo"] = method }),
            b: async (method, p, _) =>
            {
                await other!.CallAsync("pet/say", new JsonObject { ["text"] = "hola" }); // la extensión llama de vuelta mientras atiende
                await other!.ProgressAsync(JsonRpcConnection.CurrentRequestId.Value!, new JsonObject { ["text"] = "par" });
                await other!.ProgressAsync(JsonRpcConnection.CurrentRequestId.Value!, new JsonObject { ["text"] = "tes" });
                return new JsonObject { ["text"] = "partes" };
            });
        other = ext;
        var pieces = new List<string>();
        var result = await claupet.CallAsync("assistant/respond", null, CancellationToken.None, v => { lock (pieces) pieces.Add(v.GetProperty("text").GetString()!); });
        Assert.Equal("partes", result.GetProperty("text").GetString());
        Assert.Equal(["par", "tes"], pieces);
        await claupet.DisposeAsync();
        await ext.DisposeAsync();
    }

    [Fact]
    public async Task ErrorsAndCancelTravel()
    {
        var cancelled = new TaskCompletionSource();
        var (claupet, ext) = Pair(b: async (method, p, cancel) =>
        {
            if (method == "falla") throw new JsonRpcException(JsonRpcException.InvalidParams, "mal");
            try { await Task.Delay(Timeout.Infinite, cancel); }
            catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
            return null;
        });
        var error = await Assert.ThrowsAsync<JsonRpcException>(() => claupet.CallAsync("falla"));
        Assert.Equal(JsonRpcException.InvalidParams, error.Code);
        Assert.Equal("mal", error.Message);

        using var cts = new CancellationTokenSource(200);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => claupet.CallAsync("espera", cancel: cts.Token));
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)); // el otro lado se enteró

        var timeout = await Assert.ThrowsAsync<JsonRpcException>(() => claupet.CallAsync("espera", timeout: TimeSpan.FromMilliseconds(200)));
        Assert.Equal(JsonRpcException.Timeout, timeout.Code);
        await claupet.DisposeAsync();
        await ext.DisposeAsync();
    }

    [Fact]
    public async Task GarbageClosesTheConnection()
    {
        var toClaupet = new AnonymousPipeServerStream(PipeDirection.Out);
        var input = new AnonymousPipeClientStream(PipeDirection.In, toClaupet.ClientSafePipeHandle);
        var output = new MemoryStream();
        var rpc = new JsonRpcConnection(input, output);
        var closed = new TaskCompletionSource<string>();
        rpc.Closed += r => closed.TrySetResult(r);
        rpc.Start();
        var pending = rpc.CallAsync("algo");
        toClaupet.Write(Encoding.ASCII.GetBytes("esto no es un encabezado\r\n\r\n"));
        toClaupet.Flush();
        var reason = await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("encabezado", reason);
        var error = await Assert.ThrowsAsync<JsonRpcException>(() => pending);
        Assert.Equal(JsonRpcException.ConnectionClosed, error.Code);
        await rpc.DisposeAsync();
    }

    [Fact]
    public async Task OversizedMessageIsRejected()
    {
        var toClaupet = new AnonymousPipeServerStream(PipeDirection.Out);
        var input = new AnonymousPipeClientStream(PipeDirection.In, toClaupet.ClientSafePipeHandle);
        var rpc = new JsonRpcConnection(input, new MemoryStream());
        var closed = new TaskCompletionSource<string>();
        rpc.Closed += r => closed.TrySetResult(r);
        rpc.Start();
        toClaupet.Write(Encoding.ASCII.GetBytes($"Content-Length: {JsonRpcConnection.MaxMessage + 1}\r\n\r\n"));
        toClaupet.Flush();
        Assert.Contains("grande", await closed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await rpc.DisposeAsync();
    }
}

public class ProcessExtensionTests
{
    static string Manifest(string id = "prueba.eco", string permissions = "\"pet\", \"notices\"") => $$"""
        {
          "schema": 1, "id": "{{id}}", "name": "Eco", "version": "1.0.0",
          "provides": ["skill", "speech-to-text", "text-to-speech", "assistant"],
          "runtime": { "type": "process", "program": "node", "args": ["{package}/main.js"] },
          "commands": [ { "name": "eco", "description": "Repite" } ],
          "permissions": [{{permissions}}]
        }
        """;

    static (string, byte[])[] Files => [("main.js", Repo.File("docs/extensiones/ejemplos/node/main.js"))];

    [Fact]
    public async Task SkillCallsHostWithPermission()
    {
        await using var f = new ManagerFixture();
        var ext = await f.InstallAsync(Manifest(), Files);
        Assert.Equal(ext.Id, f.Manager.SkillForCommand("eco")!.Id);
        var reply = await f.Manager.CallAsync(ext.Id, "skill/command", new JsonObject { ["command"] = "eco", ["text"] = "hola" });
        Assert.Equal("eco: hola", reply.GetProperty("reply").GetString());
        Assert.True(reply.GetProperty("petSaid").GetBoolean());
        Assert.Equal(["hola"], f.Api.Said);

        // Sin permiso para el portapapeles: Claupet se lo niega.
        var denied = await Assert.ThrowsAsync<JsonRpcException>(() => f.Manager.CallAsync(ext.Id, "test/forbidden", null));
        Assert.Contains("clipboard", denied.Message);
        var info = await f.Manager.CallAsync(ext.Id, "test/hostinfo", null);
        Assert.Equal(["pet", "notices"], info.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
    }

    [Fact]
    public async Task ProvidersReplaceBuiltInsOnlyWhenChosen()
    {
        await using var f = new ManagerFixture();
        var ext = await f.InstallAsync(Manifest(), Files); // sin ocupar ranuras
        Assert.Null(ExtensionProviders.SpeechToText(f.Manager));
        Assert.Null(ExtensionProviders.Assistant(f.Manager));

        await f.Manager.SetProviderAsync(ExtensionSlots.SpeechToText, ext.Id);
        await f.Manager.SetProviderAsync(ExtensionSlots.TextToSpeech, ext.Id);
        await f.Manager.SetProviderAsync(ExtensionSlots.Assistant, ext.Id);

        var stt = ExtensionProviders.SpeechToText(f.Manager)!;
        Assert.Equal("oí 16000 muestras en es", await stt.TranscribeAsync(new short[16000], "es", CancellationToken.None));

        var chunks = new List<(int Samples, int Rate)>();
        await ExtensionProviders.TextToSpeech(f.Manager)!.SpeakAsync("hola", null, "es", (s, r) => chunks.Add((s.Length, r)), CancellationToken.None);
        Assert.Equal([(2205, 22050), (2205, 22050)], chunks);

        var pieces = new StringBuilder();
        var answer = await ExtensionProviders.Assistant(f.Manager)!.RespondAsync(
            new BrainRequest("sistema", [new BrainMessage("user", "¿qué hora es?")], null), p => pieces.Append(p), CancellationToken.None);
        Assert.Equal("Respondo a: ¿qué hora es?", answer.Text);
        Assert.Equal(answer.Text, pieces.ToString());
        Assert.Equal("eco-js", answer.Model);

        await f.Manager.SetProviderAsync(ExtensionSlots.Assistant, null);
        Assert.Null(ExtensionProviders.Assistant(f.Manager));
    }

    [Fact]
    public async Task ThreeCrashesDisableAndFallBack()
    {
        await using var f = new ManagerFixture();
        var ext = await f.InstallAsync(Manifest(), Files, ExtensionSlots.SpeechToText);
        Assert.NotNull(f.Manager.ProviderFor(ExtensionSlots.SpeechToText));
        var disabled = new TaskCompletionSource<string>();
        f.Manager.Disabled += (_, reason) => disabled.TrySetResult(reason);

        for (var i = 0; i < 3 && !disabled.Task.IsCompleted; i++)
        {
            await Assert.ThrowsAnyAsync<Exception>(() => f.Manager.CallAsync(ext.Id, "test/crash", null));
            // Espera a que Claupet note que terminó (y lo cuente).
            for (var w = 0; w < 50 && f.Manager.IsRunning(ext.Id); w++) await Task.Delay(100);
            await Task.Delay(200);
        }
        var why = await disabled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("3", why);
        Assert.Null(f.Manager.ProviderFor(ExtensionSlots.SpeechToText)); // volvió Whisper
        Assert.False(f.Manager.IsEnabled(ext.Id));
        Assert.NotNull(f.Manager.StateOf(ext.Id).DisabledReason);
        await Assert.ThrowsAsync<ExtensionUnavailableException>(() => f.Manager.ChannelAsync(ext.Id));
    }

    [Fact]
    public async Task TamperedFilesAreNotRun()
    {
        await using var f = new ManagerFixture();
        var ext = await f.InstallAsync(Manifest(), Files);
        File.AppendAllText(ext.FileOf("main.js"), "\nrequire('child_process').exec('calc');");
        var error = await Assert.ThrowsAsync<ExtensionUnavailableException>(() => f.Manager.ChannelAsync(ext.Id));
        Assert.Contains("cambiaron", error.Message);
        Assert.False(f.Manager.IsEnabled(ext.Id));
    }

    [Fact]
    public async Task MissingProgramSaysSo()
    {
        await using var f = new ManagerFixture();
        var ext = await f.InstallAsync(Manifest().Replace("\"node\"", "\"programa-que-no-existe\""), Files);
        var error = await Assert.ThrowsAsync<ExtensionUnavailableException>(() => f.Manager.ChannelAsync(ext.Id));
        Assert.Contains("programa-que-no-existe", error.Message);
    }

    [Fact]
    public async Task UninstallStopsAndForgets()
    {
        await using var f = new ManagerFixture();
        var ext = await f.InstallAsync(Manifest(), Files, ExtensionSlots.Assistant);
        await f.Manager.ChannelAsync(ext.Id);
        Assert.True(f.Manager.IsRunning(ext.Id));
        await f.Manager.UninstallAsync(ext.Id, keepData: false);
        Assert.False(f.Manager.IsRunning(ext.Id));
        Assert.Empty(f.Manager.Installed);
        Assert.Empty(f.Settings.Extensions.Slots);
        Assert.Empty(f.Settings.Extensions.States);
    }
}

public class NativeExtensionTests
{
    static string Manifest(string library = "bin/eco.dll") => $$"""
        {
          "schema": 1, "id": "prueba.eco-c", "name": "Eco C", "version": "1.0.0",
          "provides": ["skill", "assistant"],
          "runtime": { "type": "native", "library": { "win-x64": "{{library}}", "win-arm64": "{{library}}" } },
          "commands": [ { "name": "eco", "description": "Repite" } ],
          "permissions": ["pet"]
        }
        """;

    [Fact]
    public async Task DllRunsInItsOwnProcess()
    {
        await using var f = new ManagerFixture();
        var ext = await f.InstallAsync(Manifest(), [("bin/eco.dll", Repo.EcoDll())]);
        var reply = await f.Manager.CallAsync(ext.Id, "skill/command", new JsonObject { ["command"] = "eco", ["text"] = "desde C" });
        Assert.Equal("eco: desde C", reply.GetProperty("reply").GetString());
        Assert.True(reply.GetProperty("petSaid").GetBoolean()); // la DLL llamó a Claupet y le respondió
        Assert.Equal(["desde C"], f.Api.Said);

        await f.Manager.SetProviderAsync(ExtensionSlots.Assistant, ext.Id);
        var pieces = new StringBuilder();
        var answer = await ExtensionProviders.Assistant(f.Manager)!.RespondAsync(new BrainRequest("s", [new BrainMessage("user", "hola")], null), p => pieces.Append(p), CancellationToken.None);
        Assert.Equal("Hola, soy una DLL.", answer.Text);
        Assert.Equal("Hola, soy una DLL.", pieces.ToString());

        var unknown = await Assert.ThrowsAsync<JsonRpcException>(() => f.Manager.CallAsync(ext.Id, "no/existe", null));
        Assert.Equal(JsonRpcException.MethodNotFound, unknown.Code);
    }

    [Fact]
    public async Task CrashingDllDoesNotTakeClaupetDown()
    {
        await using var f = new ManagerFixture();
        var ext = await f.InstallAsync(Manifest(), [("bin/eco.dll", Repo.EcoDll())]);
        await f.Manager.ChannelAsync(ext.Id);
        var error = await Assert.ThrowsAsync<JsonRpcException>(() => f.Manager.CallAsync(ext.Id, "test/crash", null));
        Assert.Equal(JsonRpcException.ConnectionClosed, error.Code);
        // Este proceso sigue, y la extensión vuelve a arrancar a la siguiente.
        var reply = await f.Manager.CallAsync(ext.Id, "skill/command", new JsonObject { ["text"] = "otra vez" });
        Assert.Equal("eco: otra vez", reply.GetProperty("reply").GetString());
    }

    [Fact]
    public async Task WrongArchitectureIsExplained()
    {
        await using var f = new ManagerFixture();
        var ext = await f.InstallAsync(Manifest(), [("bin/eco.dll", Repo.EcoDll32())]);
        var error = await Assert.ThrowsAsync<ExtensionUnavailableException>(() => f.Manager.ChannelAsync(ext.Id));
        Assert.Contains("x86", error.Message);
        Assert.Equal("x86", NativeExtensionHost.MachineOf(ext.FileOf("bin/eco.dll")));
    }
}

public class HttpProviderTests
{
    static string Brain(string url, string extra = "") => $$"""
        {
          "schema": 1, "id": "prueba.local", "name": "Modelo local", "version": "1.0.0",
          "provides": ["assistant"],
          "runtime": { "type": "http", "format": "openai-chat", "url": "{{url}}", "model": "llama3", "auth": "bearer", "authSetting": "apiKey" },
          "settings": [ { "key": "apiKey", "type": "secret", "label": "Clave" }{{extra}} ]
        }
        """;

    [Fact]
    public async Task OpenAiChatStreamsAndSendsKey()
    {
        using var server = new FakeHttpServer((method, path, headers, body) =>
        {
            if (path != "/v1/chat/completions") return (404, "application/json", "{\"error\":{\"message\":\"no\"}}"u8.ToArray());
            var sse = "data: {\"model\":\"llama3\",\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}\n\n"
                + "data: {\"choices\":[{\"delta\":{\"content\":\"Hola \"}}]}\n\n"
                + "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"pienso\"}}]}\n\n"
                + "data: {\"choices\":[{\"delta\":{\"content\":\"mundo\"}}]}\n\n"
                + "data: [DONE]\n\n";
            return (200, "text/event-stream", Encoding.UTF8.GetBytes(sse));
        });
        await using var f = new ManagerFixture();
        var ext = await f.InstallAsync(Brain(server.Url + "/v1"), [], ExtensionSlots.Assistant);
        var brain = ExtensionProviders.Assistant(f.Manager)!;

        // Sin clave, lo dice.
        var missing = await Assert.ThrowsAsync<ExtensionUnavailableException>(() => brain.RespondAsync(new BrainRequest("s", [new BrainMessage("user", "hola")], null), null, CancellationToken.None));
        Assert.Contains("clave", missing.Message);

        await f.Manager.SetValueAsync(ext, "apiKey", "sk-prueba");
        Assert.Null(f.Manager.StateOf(ext.Id).Values.GetValueOrDefault("apiKey")); // la clave no va en settings.json
        var pieces = new StringBuilder();
        var answer = await brain.RespondAsync(new BrainRequest("sistema", [new BrainMessage("user", "hola")], null), p => pieces.Append(p), CancellationToken.None);
        Assert.Equal("Hola mundo", answer.Text);
        Assert.Equal("Hola mundo", pieces.ToString());
        var request = server.Requests.Last();
        Assert.Equal("Bearer sk-prueba", request.Headers["Authorization"]);
        var sent = JsonNode.Parse(request.Body)!;
        Assert.Equal("llama3", sent["model"]!.GetValue<string>());
        Assert.Equal("system", sent["messages"]![0]!["role"]!.GetValue<string>());
        Assert.True(sent["stream"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ServerErrorsAreExplained()
    {
        using var server = new FakeHttpServer((_, _, _, _) => (401, "application/json", "{\"error\":{\"message\":\"Invalid API key\"}}"u8.ToArray()));
        await using var f = new ManagerFixture();
        var ext = await f.InstallAsync(Brain(server.Url), [], ExtensionSlots.Assistant);
        await f.Manager.SetValueAsync(ext, "apiKey", "mala");
        var error = await Assert.ThrowsAsync<ExtensionUnavailableException>(() =>
            ExtensionProviders.Assistant(f.Manager)!.RespondAsync(new BrainRequest("s", [new BrainMessage("user", "x")], null), null, CancellationToken.None));
        Assert.Contains("clave", error.Message);
        Assert.Contains("Invalid API key", error.Message);
    }

    [Fact]
    public async Task UrlFromSettingStillChecksTheHost()
    {
        await using var f = new ManagerFixture();
        var manifest = """
            {
              "schema": 1, "id": "prueba.servidor", "name": "Servidor", "version": "1.0.0", "provides": ["assistant"],
              "runtime": { "type": "http", "format": "openai-chat", "url": "{setting:baseUrl}", "model": "m" },
              "settings": [ { "key": "baseUrl", "type": "string", "label": "Dirección", "default": "http://localhost:11434/v1" } ],
              "permissions": ["network:api.permitida.com"]
            }
            """;
        var ext = await f.InstallAsync(manifest, [], ExtensionSlots.Assistant);
        Assert.Equal("http://localhost:11434/v1/chat/completions", ExtensionProviders.Endpoint(f.Manager, ext, "/chat/completions").ToString());
        await f.Manager.SetValueAsync(ext, "baseUrl", "https://otro.sitio.com/v1");
        var error = Assert.Throws<ExtensionUnavailableException>(() => ExtensionProviders.Endpoint(f.Manager, ext, "/chat/completions"));
        Assert.Contains("otro.sitio.com", error.Message);
        await f.Manager.SetValueAsync(ext, "baseUrl", "http://api.permitida.com/v1");
        Assert.Throws<ExtensionUnavailableException>(() => ExtensionProviders.Endpoint(f.Manager, ext, "/chat/completions")); // afuera, solo https
        await f.Manager.SetValueAsync(ext, "baseUrl", "https://api.permitida.com/v1");
        Assert.Equal("api.permitida.com", ExtensionProviders.Endpoint(f.Manager, ext, "").Host);
    }

    [Fact]
    public async Task TranscriptionAndSpeech()
    {
        using var server = new FakeHttpServer((_, path, headers, body) => path switch
        {
            "/audio/transcriptions" => (200, "application/json", "{\"text\":\" hola desde el servidor \"}"u8.ToArray()),
            "/audio/speech" => (200, "audio/wav", AudioCodec.ToWav(new short[2400], 24000)),
            _ => (404, "text/plain", []),
        });
        await using var f = new ManagerFixture();
        await f.InstallAsync($$"""
            { "schema": 1, "id": "prueba.oido", "name": "Oído", "version": "1.0.0", "provides": ["speech-to-text"],
              "runtime": { "type": "http", "format": "openai-transcription", "url": "{{server.Url}}", "model": "whisper-1" } }
            """, [], ExtensionSlots.SpeechToText);
        await f.InstallAsync($$"""
            { "schema": 1, "id": "prueba.voz", "name": "Voz", "version": "1.0.0", "provides": ["text-to-speech"],
              "runtime": { "type": "http", "format": "openai-speech", "url": "{{server.Url}}", "model": "tts-1" },
              "settings": [ { "key": "voice", "type": "choice", "label": "Voz", "choices": ["alloy", "nova"], "default": "nova" } ] }
            """, [], ExtensionSlots.TextToSpeech);

        Assert.Equal("hola desde el servidor", await ExtensionProviders.SpeechToText(f.Manager)!.TranscribeAsync(new short[16000], "es", CancellationToken.None));
        var upload = server.Requests.Single(r => r.Path == "/audio/transcriptions");
        Assert.StartsWith("multipart/form-data", upload.Headers["Content-Type"]);
        Assert.Contains("RIFF", Encoding.ASCII.GetString(upload.Body));

        var audio = new List<(int, int)>();
        await ExtensionProviders.TextToSpeech(f.Manager)!.SpeakAsync("hola", null, "es", (s, r) => audio.Add((s.Length, r)), CancellationToken.None);
        Assert.Equal([(2400, 24000)], audio);
        Assert.Equal("nova", JsonNode.Parse(server.Requests.Single(r => r.Path == "/audio/speech").Body)!["voice"]!.GetValue<string>());
    }

    [Fact]
    public async Task ClaupetRpcServerFillsTheExclusiveSlots()
    {
        // Un servidor «claupet-rpc» en una ranura habla JSON-RPC, no los formatos de OpenAI.
        using var server = new FakeHttpServer((_, path, _, body) =>
        {
            var request = JsonNode.Parse(body)!;
            JsonNode result = request["method"]!.GetValue<string>() switch
            {
                "initialize" => new JsonObject(),
                "assistant/respond" => new JsonObject { ["text"] = "respuesta del servidor", ["model"] = "rpc-1" },
                "speechToText/transcribe" => new JsonObject { ["text"] = "dicho al servidor" },
                "textToSpeech/synthesize" => new JsonObject { ["audio"] = Convert.ToBase64String(new byte[3200]), ["sampleRate"] = 16000 },
                _ => new JsonObject(),
            };
            var reply = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = request["id"]!.DeepClone(), ["result"] = result };
            return path == "/rpc" ? (200, "application/json", Encoding.UTF8.GetBytes(reply.ToJsonString())) : (404, "text/plain", []);
        });
        await using var f = new ManagerFixture();
        await f.InstallAsync($$"""
            { "schema": 1, "id": "prueba.rpc", "name": "Servidor RPC", "version": "1.0.0",
              "provides": ["assistant", "speech-to-text", "text-to-speech"],
              "runtime": { "type": "http", "format": "claupet-rpc", "url": "{{server.Url}}/rpc" } }
            """, [], ExtensionSlots.Assistant, ExtensionSlots.SpeechToText, ExtensionSlots.TextToSpeech);

        var answer = await ExtensionProviders.Assistant(f.Manager)!.RespondAsync(new BrainRequest("s", [new BrainMessage("user", "hola")], null), null, CancellationToken.None);
        Assert.Equal("respuesta del servidor", answer.Text);
        Assert.Equal("dicho al servidor", await ExtensionProviders.SpeechToText(f.Manager)!.TranscribeAsync(new short[1600], "es", CancellationToken.None));
        var audio = new List<(int, int)>();
        await ExtensionProviders.TextToSpeech(f.Manager)!.SpeakAsync("hola", null, "es", (s, r) => audio.Add((s.Length, r)), CancellationToken.None);
        Assert.Equal([(1600, 16000)], audio);
        Assert.All(server.Requests, r => Assert.Equal("/rpc", r.Path));
    }
}

public class TextCliTests
{
    const string Echo = """
        let input = '';
        process.stdin.on('data', d => input += d);
        process.stdin.on('end', () => {
          const out = process.argv[2];
          const answer = 'modelo=' + process.argv[3] + ' | ' + input.trim().split('\n').filter(l => l.startsWith('Persona:')).pop();
          if (out) require('fs').writeFileSync(out, answer); else process.stdout.write(answer);
        });
        """;

    static string Manifest(string args) => $$"""
        { "schema": 1, "id": "prueba.consola", "name": "Consola", "version": "1.0.0", "provides": ["assistant"],
          "runtime": { "type": "process", "protocol": "text", "program": "node", "args": {{args}}, "model": "m1" } }
        """;

    [Fact]
    public async Task ReadsStdout()
    {
        await using var f = new ManagerFixture();
        await f.InstallAsync(Manifest("[\"{package}/eco.js\", \"\", \"{model}\"]"), [("eco.js", Encoding.UTF8.GetBytes(Echo))], ExtensionSlots.Assistant);
        var pieces = new StringBuilder();
        var answer = await ExtensionProviders.Assistant(f.Manager)!.RespondAsync(new BrainRequest("sistema", [new BrainMessage("user", "hola")], null), p => pieces.Append(p), CancellationToken.None);
        Assert.Equal("modelo=m1 | Persona: hola", answer.Text);
        Assert.Equal(answer.Text, pieces.ToString());
    }

    [Fact]
    public async Task ReadsOutputFileLikeCodex()
    {
        await using var f = new ManagerFixture();
        var ext = await f.InstallAsync(Manifest("[\"{package}/eco.js\", \"{output}\", \"{model}\"]"), [("eco.js", Encoding.UTF8.GetBytes(Echo))], ExtensionSlots.Assistant);
        var answer = await ExtensionProviders.Assistant(f.Manager)!.RespondAsync(new BrainRequest("s", [new BrainMessage("user", "uno"), new BrainMessage("assistant", "dos"), new BrainMessage("user", "tres")], "m2"), null, CancellationToken.None);
        Assert.Equal("modelo=m2 | Persona: tres", answer.Text);
        Assert.Empty(Directory.EnumerateFiles(ext.DataFolder, "respuesta-*")); // el archivo temporal se borra
    }
}

public class LocalApiTests
{
    static async Task<(int Status, JsonNode Body)> Post(int port, string body, string? token, string host = "127.0.0.1", string? origin = null)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = $"POST /v1/rpc HTTP/1.1\r\nHost: {host}:{port}\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\n"
            + (token is null ? "" : $"Authorization: Bearer {token}\r\n") + (origin is null ? "" : $"Origin: {origin}\r\n") + "\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        await stream.WriteAsync(bytes);
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync();
        var status = int.Parse(text.Split(' ')[1]);
        return (status, JsonNode.Parse(text[(text.IndexOf("\r\n\r\n") + 4)..])!);
    }

    [Fact]
    public async Task TokensPermissionsAndBrowsers()
    {
        await using var f = new ManagerFixture();
        var (token, saved) = LocalApiServer.NewToken("Mi script", [ExtensionPermissions.Pet]);
        f.Settings.Extensions.ApiTokens.Add(saved);
        await using var api = new LocalApiServer(f.Manager, () => f.Settings.Extensions.ApiTokens);
        api.Start(0);
        const string say = """{"jsonrpc":"2.0","id":1,"method":"pet/say","params":{"text":"hola desde un script"}}""";

        Assert.Equal(401, (await Post(api.Port, say, null)).Status);
        Assert.Equal(401, (await Post(api.Port, say, "cpt_falso")).Status);
        Assert.Equal(403, (await Post(api.Port, say, token, origin: "https://pagina-mala.com")).Status);
        Assert.Equal(403, (await Post(api.Port, say, token, host: "pagina-mala.com")).Status);

        var ok = await Post(api.Port, say, token);
        Assert.Equal(200, ok.Status);
        Assert.Null(ok.Body["error"]);
        Assert.Equal(["hola desde un script"], f.Api.Said);

        var denied = await Post(api.Port, """{"jsonrpc":"2.0","id":2,"method":"clipboard/read"}""", token);
        Assert.Equal(JsonRpcException.PermissionDenied, denied.Body["error"]!["code"]!.GetValue<int>());
        var onlyExtensions = await Post(api.Port, """{"jsonrpc":"2.0","id":3,"method":"settings/get"}""", token);
        Assert.NotNull(onlyExtensions.Body["error"]);
        Assert.NotEqual(saved.Hash, token); // se guarda el hash, no el token
    }

    [Fact]
    public async Task RunningExtensionGetsItsOwnToken()
    {
        await using var f = new ManagerFixture();
        await using var api = new LocalApiServer(f.Manager, () => f.Settings.Extensions.ApiTokens);
        api.Start(0);
        var caller = new HostCaller("prueba.eco", "Eco", [ExtensionPermissions.Notices], null);
        var token = api.IssueFor(caller);
        var shown = await Post(api.Port, """{"jsonrpc":"2.0","id":1,"method":"notice/show","params":{"title":"Hola","text":"desde la extensión"}}""", token);
        Assert.Null(shown.Body["error"]);
        Assert.Equal([("Hola", "desde la extensión")], f.Api.Notices);
        api.Revoke("prueba.eco");
        Assert.Equal(401, (await Post(api.Port, """{"jsonrpc":"2.0","id":2,"method":"host/info"}""", token)).Status);
    }
}
