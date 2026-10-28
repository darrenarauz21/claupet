namespace Claupet.Tests;

/// <summary>El modo «Automático» del modelo: cada pregunta va al modelo y al esfuerzo que pide.</summary>
public class AutoModelTests
{
    static AutoLevel Level(string prompt, params ChatTurn[] history) => AutoModel.Pick(prompt, history).Level;

    [Theory]
    [InlineData("hola")]
    [InlineData("¿qué día es hoy?")]
    [InlineData("gracias, eso era todo")]
    public void Lo_corto_y_suelto_va_a_Haiku_con_poco_esfuerzo(string prompt)
    {
        Assert.Equal(AutoLevel.Light, Level(prompt));
        Assert.Equal(new AutoChoice(AutoLevel.Light, "haiku", "low"), AutoModel.Pick(prompt, []));
    }

    [Theory]
    [InlineData("Explícame cómo funciona async en C#")]
    [InlineData("dame un ejemplo corto de un ciclo for en Python")]
    [InlineData("tradúceme esto al inglés: nos vemos mañana")]
    [InlineData("How do I read a JSON file?")]
    public void Explicar_escribir_o_un_ejemplo_va_a_Sonnet(string prompt)
    {
        Assert.Equal(AutoLevel.Normal, Level(prompt));
        Assert.Equal("sonnet", AutoModel.Pick(prompt, []).Model);
    }

    [Theory]
    [InlineData("Diseña la arquitectura de un sistema de colas para los avisos")]
    [InlineData("Compara Avalonia con MAUI: ventajas y desventajas para una app de escritorio")]
    [InlineData("¿Por qué falla esto?\nSystem.NullReferenceException: Object reference not set\n   at Claupet.TrayApp.Render()")]
    [InlineData("Analyze this in depth and find the root cause")]
    public void Disenar_analizar_o_codigo_que_falla_va_a_Opus_con_esfuerzo_alto(string prompt)
    {
        Assert.Equal(new AutoChoice(AutoLevel.Deep, "opus", "high"), AutoModel.Pick(prompt, []));
    }

    [Fact]
    public void Repreguntar_corto_sobre_una_respuesta_con_codigo_no_baja_de_lo_normal()
    {
        var history = new[] { new ChatTurn(true, "un for en C#"), new ChatTurn(false, "Así:\n```csharp\nfor (var i = 0; i < 3; i++) { }\n```") };
        Assert.Equal(AutoLevel.Light, Level("¿y con while?"));
        Assert.Equal(AutoLevel.Normal, Level("¿y con while?", history));
    }

    [Fact]
    public void Analizar_un_archivo_no_baja_de_lo_normal()
    {
        Assert.Equal(AutoLevel.Normal, AutoModel.Pick("¿qué es esto?", [], file: true).Level);
    }

    [Fact]
    public void Automatico_se_ofrece_primero_y_tiene_su_nombre()
    {
        L.Use("es");
        Assert.Equal(AutoModel.Id, AssistantModels.IslandChoices[0].Id);
        Assert.Equal("Automático", AssistantModels.Label(AutoModel.Id));
    }
}
