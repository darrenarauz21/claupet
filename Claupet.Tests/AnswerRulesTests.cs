using System.Globalization;

namespace Claupet.Tests;

/// <summary>Las reglas sin IA: lo que entienden y, sobre todo, lo que no deben adivinar.</summary>
public sealed class AnswerRulesTests
{
    [Theory]
    [InlineData("¿Qué hora es?")]
    [InlineData("oye, ¿qué horas son ahora?")]
    [InlineData("What time is it?")]
    public void LaHora(string prompt)
    {
        var intent = AnswerRules.Match(prompt);
        Assert.Equal(AnswerTopics.Time, intent?.Topic);
        Assert.False(intent!.Date);
        Assert.Null(intent.Place);
    }

    [Fact]
    public void LaHoraEnOtraCiudad() => Assert.Equal("tokio", AnswerRules.Match("¿Qué hora es en Tokio?")?.Place);

    [Fact]
    public void LaFecha() => Assert.True(AnswerRules.Match("¿Qué día es hoy?")?.Date);

    [Fact]
    public void ElClimaDeAhora()
    {
        var intent = AnswerRules.Match("¿Cómo está el clima?");
        Assert.Equal(AnswerTopics.Weather, intent?.Topic);
        Assert.Equal(0, intent!.Day);
        Assert.Null(intent.Place);
    }

    [Fact]
    public void LaLluviaDeMananaEnUnLugar()
    {
        var intent = AnswerRules.Match("¿Va a llover mañana en Boquete?");
        Assert.Equal(AnswerTopics.Weather, intent?.Topic);
        Assert.Equal(1, intent!.Day);
        Assert.Equal("boquete", intent.Place);
        Assert.True(intent.Rain);
    }

    [Theory]
    [InlineData("¿Qué tal el clima?")]
    [InlineData("¿qué tal el tiempo en Madrid?")]
    [InlineData("¿Hace sol?")]
    [InlineData("¿Está haciendo calor?")]
    [InlineData("¿cómo está el día?")]
    public void OtrasFormasDelClima(string prompt) => Assert.Equal(AnswerTopics.Weather, AnswerRules.Match(prompt)?.Topic);

    [Fact]
    public void ElClimaDePasadoManana() => Assert.Equal(2, AnswerRules.Match("clima en Madrid pasado mañana")?.Day);

    [Theory]
    [InlineData("¿Qué es el clima tropical?")]
    [InlineData("¿Qué hora es en Tokio y cuál es su capital?")]
    [InlineData("sube el volumen y abre spotify")]
    [InlineData("abre el archivo de ventas")]
    [InlineData("2024")]
    [InlineData("¿Quién escribió Cien años de soledad?")]
    public void NoAdivina(string prompt) => Assert.Null(AnswerRules.Match(prompt));

    [Theory]
    [InlineData("¿Cuánta batería me queda?", "bateria")]
    [InlineData("espacio en el disco", "disco")]
    [InlineData("¿Cómo está el equipo?", "todo")]
    public void ElEstadoDelEquipo(string prompt, string value)
    {
        var intent = AnswerRules.Match(prompt);
        Assert.Equal(AnswerTopics.Status, intent?.Topic);
        Assert.Equal(new AssistantAction("estado", value), intent!.Action);
    }

    [Theory]
    [InlineData("sube el volumen", "volumen", "subir")]
    [InlineData("pon el volumen al 30", "volumen", "30")]
    [InlineData("silencia", "volumen", "silenciar")]
    [InlineData("modo oscuro", "tema", "oscuro")]
    [InlineData("siguiente canción", "musica", "siguiente")]
    [InlineData("pausa la música", "musica", "pausar")]
    [InlineData("muestra el escritorio", "ventana", "escritorio")]
    public void LasAcciones(string prompt, string kind, string value)
    {
        var intent = AnswerRules.Match(prompt);
        Assert.Equal(AnswerTopics.Actions, intent?.Topic);
        Assert.Equal(new AssistantAction(kind, value), intent!.Action);
    }

    [Fact]
    public void AbrirUnaApp() => Assert.Equal(new AssistantAction("abrir", "spotify"), AnswerRules.Match("abre Spotify")!.Action! with { Value = "spotify" });

    [Fact]
    public void UnRecordatorioConLaHoraCalculada()
    {
        var intent = AnswerRules.Match("recuérdame llamar a mamá en 20 minutos");
        Assert.Equal(AnswerTopics.Notes, intent?.Topic);
        Assert.Equal("recordar", intent!.Action!.Kind);
        using var doc = System.Text.Json.JsonDocument.Parse(intent.Action.Value);
        Assert.Equal("llamar a mamá", doc.RootElement.GetProperty("texto").GetString());
        var when = DateTime.ParseExact(doc.RootElement.GetProperty("cuando").GetString()!, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
        Assert.InRange((when - DateTime.Now).TotalMinutes, 18, 21);
    }

    [Fact]
    public void UnaNota()
    {
        var action = AnswerRules.Match("anota comprar leche")?.Action;
        Assert.Equal("nota", action?.Kind);
        Assert.Contains("comprar leche", action!.Value);
    }

    [Theory]
    [InlineData("hola")]
    [InlineData("gracias")]
    [InlineData("¿quién eres?")]
    public void LaCharla(string prompt)
    {
        var intent = AnswerRules.Match(prompt);
        Assert.Equal(AnswerTopics.Chat, intent?.Topic);
        Assert.NotNull(intent!.Reply);
    }

    [Theory]
    [InlineData("cuánto es 25 * 4 + 10", "110")]
    [InlineData("2 + 3 x 4", "14")]
    [InlineData("(2+3)*4", "20")]
    [InlineData("2^10", "1,024")]
    [InlineData("15% de 200", "30")]
    [InlineData("10 km a millas", "6.2137")]
    [InlineData("100 F a C", "37.7778")]
    [InlineData("dos más dos", null)]
    [InlineData("1/0", null)]
    public void LasCuentas(string prompt, string? expected)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var answer = Calculator.Answer(prompt);
            if (expected is null) Assert.Null(answer);
            else Assert.Contains(expected, answer);
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public void LosTiposConSuMotorDeSiempre()
    {
        var settings = new AppSettings();
        Assert.Equal(Engine.None, AnswerTopics.First(settings, AnswerTopics.Time));
        Assert.Equal(Engine.Cloud, AnswerTopics.First(settings, AnswerTopics.General));
        settings.AnswerEngines[AnswerTopics.General] = Engine.None; // no se puede: las generales no tienen «sin IA»
        Assert.Equal(Engine.Cloud, AnswerTopics.First(settings, AnswerTopics.General));
        settings.AnswerEngines[AnswerTopics.Internet] = Engine.Local; // internet va siempre a la nube
        Assert.Equal(Engine.Cloud, AnswerTopics.First(settings, AnswerTopics.Internet));
    }
}

/// <summary>El reparto con la nube de prueba (Assistant.External): sin escribir el ahorro, que es de la nube.</summary>
[Collection("Assistant.External")]
public sealed class AnswersRouterTests : IDisposable
{
    public void Dispose() => Assistant.External = null;

    [Fact]
    public async Task LoQueNoEntiendeNadieVaALaNube()
    {
        Assistant.External = () => new FakeBrain("Gabriel García Márquez.");
        var result = await Answers.AskAsync(new AssistantRequest { Prompt = "¿Quién escribió Cien años de soledad?" }, new AppSettings());
        Assert.Equal(Engine.Cloud, result.By?.Engine);
        Assert.Equal("Gabriel García Márquez.", result.Text);
    }

    [Fact]
    public async Task UnaAccionConElMotorDeSiempreVaALaNube()
    {
        Assistant.External = () => new FakeBrain("Listo.");
        var result = await Answers.AskAsync(new AssistantRequest { Prompt = "sube el volumen" }, new AppSettings());
        Assert.Equal(Engine.Cloud, result.By?.Engine);
    }

    [Fact]
    public async Task LosDatosDeUnaAccionSinIaSeDicenTalCual()
    {
        var result = await Answers.AskAsync(new AssistantRequest { Prompt = "¿Cuánta batería me queda?", Data = "Batería: 80 %.", Engine = Engine.None },
            new AppSettings());
        Assert.Equal("Batería: 80 %. (^_^)", result.Text);
        Assert.Equal(Engine.None, result.By?.Engine);
    }
}
