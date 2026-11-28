namespace Claupet.Tests;

/// <summary>
/// La luz de «trabajando» de una pestaña: también en los turnos que Claude Code empieza solo (al terminar una tarea en
/// segundo plano), que no pasan por un pedido de la persona.
/// </summary>
public sealed class AgentTurnLightTests
{
    const string Result = """{"type":"result","subtype":"success","is_error":false,"num_turns":1,"total_cost_usd":0.01}""";

    static string Answer(string id, string model = "claude-opus-5-5") =>
        $$$"""{"type":"assistant","message":{"id":"{{{id}}}","model":"{{{model}}}","content":[{"type":"text","text":"Listo."}]}}""";

    [Fact]
    public void UnTurnoQueEmpiezaClaudeCodeSoloEnciendeLaLuz()
    {
        var t = new AgentTranscript();
        t.Ask("corre la Puerta en segundo plano");
        Assert.True(t.Busy);
        t.Apply("""{"type":"system","subtype":"background_tasks_changed","tasks":[{"id":"bash_1"}]}""");
        t.Apply(Answer("m1"));
        t.Apply(Result);
        Assert.False(t.Busy);
        Assert.Equal(1, t.BackgroundTasks); // el comando sigue corriendo entre turnos

        // Terminó la tarea: Claude Code sigue solo, sin un pedido. Antes la luz quedaba apagada.
        t.Apply("""{"type":"stream_event","event":{"type":"message_start","message":{"id":"m2","model":"claude-opus-5-5","content":[]}}}""");
        Assert.True(t.Busy);
        t.Apply(Answer("m2"));
        t.Apply(Result);
        Assert.False(t.Busy);

        // Sin mensajes parciales: la respuesta entera también la enciende.
        t.Apply(Answer("m3"));
        Assert.True(t.Busy);
        t.Apply(Result);

        // Un mensaje local de Claude Code (el de /exit) no es un turno: no la enciende.
        t.Apply(Answer("m4", "<synthetic>"));
        Assert.False(t.Busy);
    }
}
