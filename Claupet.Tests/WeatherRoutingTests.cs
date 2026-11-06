namespace Claupet.Tests;

/// <summary>
/// El clima es de la API: el modelo local no debe llegar a decir que no puede consultarlo. Cambian Assistant.Local,
/// Assistant.External y Answers.Forecast, que son estáticos.
/// </summary>
[Collection("Assistant.External")]
public sealed class WeatherRoutingTests : IDisposable
{
    /// <summary>Un modelo local de prueba: cuenta cuántas veces se le pregunta y, al clasificar, dice lo que se le mande.</summary>
    sealed class SmallBrain(string? classified = null) : IAssistantBrain, ISmallBrain, IStructuredBrain
    {
        public int Chats, Classified;
        public string? System;
        public string Name => "Local";

        public Task<BrainReply> RespondAsync(BrainRequest request, Action<string>? onText, CancellationToken cancel)
        {
            Chats++;
            System = request.System;
            return Task.FromResult(new BrainReply("No puedo verificar el clima.", "local"));
        }

        public Task<string?> JsonAsync(string system, string prompt, string schema, CancellationToken cancel)
        {
            Classified++;
            return Task.FromResult(classified);
        }
    }

    readonly Func<AppSettings, string?, int, bool, CancellationToken, Task<string?>> _forecast = Answers.Forecast;
    readonly List<(string? Place, int Day, bool Rain)> _asked = [];

    public void Dispose()
    {
        Answers.Forecast = _forecast;
        Assistant.Local = null;
        Assistant.External = null;
    }

    void Api(string? answer) => Answers.Forecast = (_, place, day, rain, _) =>
    {
        _asked.Add((place, day, rain));
        return Task.FromResult(answer);
    };

    static AssistantRequest OnlyLocal(string prompt, params ChatTurn[] history) =>
        new() { Prompt = prompt, Model = AssistantModels.Local, History = history };

    [Theory]
    [InlineData("dime el clima", null, 0)]
    [InlineData("¿me puedes decir cómo está el clima hoy?", null, 0)]
    [InlineData("quiero saber si va a llover mañana en Madrid", "madrid", 1)]
    [InlineData("¿cuánto calor hace en Panamá ahora mismo?", "panama", 0)]
    [InlineData("¿necesito paraguas mañana?", null, 1)]
    [InlineData("el clima pasado mañana por favor", null, 2)]
    [InlineData("revisa el clima en mi ciudad", null, 0)]
    [InlineData("can you check the weather in Paris", "paris", 0)]
    public void SeReconoceAunqueNoSeaUnaRegla(string prompt, string? place, int day)
    {
        var intent = AnswerRules.WeatherAsked(prompt);
        Assert.Equal(AnswerTopics.Weather, intent?.Topic);
        Assert.Equal((place, day), (intent!.Place, intent.Day));
    }

    [Theory]
    [InlineData("¿Qué es el clima tropical?")]
    [InlineData("¿Por qué llueve?")]
    [InlineData("escribe un poema sobre la lluvia")]
    [InlineData("hagamos una lluvia de ideas")]
    [InlineData("¿cuál es la temperatura de ebullición del agua?")]
    [InlineData("¿qué clima tiene Japón en abril?")]
    [InlineData("¿cuánto tiempo en horno lleva un pollo?")]
    [InlineData("¿qué hay de cenar?")]
    public void NoSeConfundeConOtraCosa(string prompt) => Assert.Null(AnswerRules.WeatherAsked(prompt));

    [Fact]
    public async Task SoloConElModeloLocalElClimaLoDaLaApi()
    {
        var brain = new SmallBrain();
        Assistant.Local = () => brain;
        Api("En Madrid: 20 °C.");
        var result = await Answers.AskAsync(OnlyLocal("dime cómo está el clima en Madrid mañana"), new AppSettings());
        Assert.Equal("En Madrid: 20 °C.", result.Text);
        Assert.Equal(Engine.None, result.By?.Engine);
        Assert.Equal(("madrid", 1, false), Assert.Single(_asked));
        Assert.Equal((0, 0), (brain.Chats, brain.Classified));
    }

    [Theory]
    [InlineData("¿y mañana?", "madrid", 1)]
    [InlineData("¿y en Lima?", "lima", 0)]
    [InlineData("y pasado mañana en Lima", "lima", 2)]
    public async Task ElSeguimientoDelClimaTambien(string prompt, string place, int day)
    {
        var brain = new SmallBrain();
        Assistant.Local = () => brain;
        Api("Sol.");
        var result = await Answers.AskAsync(
            OnlyLocal(prompt, new ChatTurn(true, "¿Cómo está el clima en Madrid?"), new ChatTurn(false, "Sol.")), new AppSettings());
        Assert.Equal("Sol.", result.Text);
        Assert.Equal((place, day, false), Assert.Single(_asked));
        Assert.Equal(0, brain.Chats);
    }

    [Fact]
    public async Task UnSeguimientoSinClimaAntesNoSeInventa()
    {
        var brain = new SmallBrain();
        Assistant.Local = () => brain;
        Api("Sol.");
        await Answers.AskAsync(OnlyLocal("¿y mañana?", new ChatTurn(true, "¿Quién ganó ayer?")), new AppSettings());
        Assert.Empty(_asked);
        Assert.Equal(1, brain.Chats);
    }

    [Fact]
    public async Task LoQueElClasificadorEntiendeComoClimaVaALaApi()
    {
        var brain = new SmallBrain("""{"tema":"clima","accion":"ninguna","valor":"","lugar":"Quito","dia":"hoy"}""");
        Assistant.Local = () => brain;
        Api("Nublado.");
        var result = await Answers.AskAsync(OnlyLocal("¿saldrá el sol por Quito?"), new AppSettings());
        Assert.Equal("Nublado.", result.Text);
        Assert.Equal(0, brain.Chats);
        Assert.Equal("Quito", Assert.Single(_asked).Place);
    }

    [Fact]
    public async Task SiLaApiNoRespondeLoDiceLaAppYNoElModelo()
    {
        var brain = new SmallBrain();
        Assistant.Local = () => brain;
        Api(null);
        var result = await Answers.AskAsync(OnlyLocal("¿Cómo está el clima?"), new AppSettings());
        Assert.Equal(L.T("No pude consultar el clima ahora: revisa la conexión o inténtalo en un momento. (・_・;)"), result.Text);
        Assert.False(result.Failed);
        Assert.Equal(0, brain.Chats);
        Assert.Single(_asked);
    }

    [Fact]
    public async Task ConLaNubePermitidaSiLaApiNoRespondeVaALaNube()
    {
        var brain = new SmallBrain();
        Assistant.Local = () => brain;
        Assistant.External = () => new FakeBrain("Soleado, según wttr.");
        Api(null);
        var result = await Answers.AskAsync(new AssistantRequest { Prompt = "¿Cómo está el clima?" }, new AppSettings());
        Assert.Equal("Soleado, según wttr.", result.Text);
        Assert.Equal(Engine.Cloud, result.By?.Engine);
        Assert.Equal(0, brain.Chats);
    }

    [Fact]
    public async Task ConLaNubeDeSiempreLaFraseLibreSigueYendoALaNube()
    {
        Assistant.External = () => new FakeBrain("Soleado.");
        Api("no debería");
        var result = await Answers.AskAsync(new AssistantRequest { Prompt = "dime el clima" }, new AppSettings());
        Assert.Equal(Engine.Cloud, result.By?.Engine);
        Assert.Empty(_asked);
    }

    [Fact]
    public async Task AlModeloLocalYaNoSeLeDiceQueElClimaNoLoPuede()
    {
        var brain = new SmallBrain();
        Assistant.Local = () => brain;
        await Answers.AskAsync(OnlyLocal("cuéntame un chiste"), new AppSettings());
        Assert.Equal(1, brain.Chats);
        Assert.DoesNotContain(L.IsSpanish ? "el clima" : "the weather", brain.System);
    }
}
