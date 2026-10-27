namespace Claupet.Tests;

/// <summary>Un cerebro de prueba: responde lo que se le diga, en trozos, y guarda lo que recibió.</summary>
sealed class FakeBrain(params string[] pieces) : IAssistantBrain
{
    public BrainRequest? Received { get; private set; }
    public Exception? Throw { get; init; }
    public string Name => "Prueba";

    public Task<BrainReply> RespondAsync(BrainRequest request, Action<string>? onText, CancellationToken cancel)
    {
        Received = request;
        if (Throw is not null) throw Throw;
        foreach (var piece in pieces) onText?.Invoke(piece);
        return Task.FromResult(new BrainReply(string.Concat(pieces), "modelo-x"));
    }
}

/// <summary>Assistant.External es estático: estas pruebas no corren en paralelo entre sí.</summary>
[Collection("Assistant.External")]
public sealed class AssistantBrainTests : IDisposable
{
    public void Dispose() => Assistant.External = null;

    static async Task<(AssistantResult Result, string Shown)> AskAsync(FakeBrain brain, AssistantRequest request)
    {
        Assistant.External = () => brain;
        var shown = new System.Text.StringBuilder();
        var result = await Assistant.AskAsync(request, piece => shown.Append(piece));
        return (result, shown.ToString());
    }

    [Fact]
    public async Task Una_respuesta_simple_llega_sin_negritas_y_con_el_modelo()
    {
        var brain = new FakeBrain("Hola, **qué** tal", " (^_^)");
        var (result, shown) = await AskAsync(brain, new AssistantRequest { Prompt = "hola" });
        Assert.False(result.Failed);
        Assert.Equal("Hola, qué tal (^_^)", result.Text);
        Assert.Equal("Hola, qué tal (^_^)", shown);
        Assert.Equal("modelo-x", result.Model);
    }

    [Fact]
    public async Task La_accion_se_reconoce_y_su_linea_no_se_muestra()
    {
        var brain = new FakeBrain("[ACC", "ION] {\"accion\":\"volumen\",\"valor\":\"50\"}\n", "Listo, a la mitad.");
        var (result, shown) = await AskAsync(brain, new AssistantRequest { Prompt = "volumen a la mitad" });
        Assert.Equal("volumen", result.Action?.Kind);
        Assert.Equal("50", result.Action?.Value);
        Assert.Equal("Listo, a la mitad.", result.Text);
        Assert.Equal("Listo, a la mitad.", shown);
    }

    [Fact]
    public async Task Con_datos_no_se_acepta_otra_accion()
    {
        var brain = new FakeBrain("[ACCION] {\"accion\":\"equipo\",\"valor\":\"apagar\"}\nTienes 80 % de batería.");
        var (result, _) = await AskAsync(brain, new AssistantRequest { Prompt = "¿batería?", Data = "Batería: 80 %" });
        Assert.Null(result.Action);
        Assert.Null(result.Task);
        Assert.Equal("Tienes 80 % de batería.", result.Text);
    }

    [Fact]
    public async Task El_historial_va_como_mensajes_y_el_prompt_no_ofrece_internet()
    {
        var brain = new FakeBrain("Sí.");
        var history = new[] { new ChatTurn(true, "uno"), new ChatTurn(false, "dos") };
        await AskAsync(brain, new AssistantRequest { Prompt = "tres", History = history });
        var request = brain.Received!;
        Assert.Equal(["user", "assistant", "user"], request.Messages.Select(m => m.Role));
        Assert.Equal(["uno", "dos", "tres"], request.Messages.Select(m => m.Text));
        Assert.DoesNotContain("WebSearch", request.System);
        Assert.Contains("[ACCION]", request.System);
        Assert.Null(request.Model);
    }

    [Fact]
    public async Task Un_archivo_de_texto_va_en_el_mensaje_y_uno_binario_no()
    {
        using var dir = new TempDir();
        var text = dir.Write("nota.txt", "contenido de la nota");
        var brain = new FakeBrain("Una nota.\n\nDice algo.");
        var (result, _) = await AskAsync(brain, new AssistantRequest { Prompt = "", File = text });
        Assert.Contains("contenido de la nota", brain.Received!.Messages[^1].Text);
        Assert.Equal("Una nota.", result.Short);

        var binary = dir["foto.bin"];
        File.WriteAllBytes(binary, [1, 0, 2, 3]);
        var other = new FakeBrain("no debería llegar");
        var (failed, _) = await AskAsync(other, new AssistantRequest { Prompt = "", File = binary });
        Assert.True(failed.Failed);
        Assert.Null(other.Received);
    }

    [Fact]
    public async Task Si_la_extension_falla_el_error_se_dice_y_no_revienta()
    {
        var brain = new FakeBrain { Throw = new HttpRequestException("rechazada") };
        var (result, _) = await AskAsync(brain, new AssistantRequest { Prompt = "hola" });
        Assert.True(result.Failed);
        Assert.Contains("rechazada", result.Error);

        var gone = new FakeBrain { Throw = new ExtensionUnavailableException("La extensión está apagada.") };
        var (off, _) = await AskAsync(gone, new AssistantRequest { Prompt = "hola" });
        Assert.Equal("La extensión está apagada.", off.Error);
    }
}
