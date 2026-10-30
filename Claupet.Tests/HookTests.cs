using System.Text;

namespace Claupet.Tests;

/// <summary>Lo que Claude Code le pasa a los hooks: llega en UTF-8 y las preguntas de la isla se leen y se responden tal cual.</summary>
public sealed class HookTests
{
    const string Question = """
        { "hook_event_name": "PermissionRequest", "session_id": "s1", "cwd": "C:\\repo", "tool_name": "AskUserQuestion",
          "tool_input": { "questions": [ { "question": "¿Qué hago con el menú del ícono?", "multiSelect": false,
            "options": [ { "label": "A · Borrar ya", "description": "Más rápido; quedan tareas pendientes." },
                         { "label": "B · Aspecto primero", "description": "Toma más tiempo." } ] } ] } }
        """;

    [Fact]
    public void ElEventoSeLeeEnUtf8()
    {
        var read = HookInstaller.ReadEvent(new MemoryStream(new UTF8Encoding(false).GetBytes(Question)));
        Assert.Equal(Question, read);
        Assert.DoesNotContain("Ã", read);
    }

    [Fact]
    public void LaPreguntaLlegaYSeRespondeConLaEtiquetaExacta()
    {
        var line = System.Text.Json.Nodes.JsonNode.Parse(HookInstaller.ReadEvent(new MemoryStream(Encoding.UTF8.GetBytes(Question))))!.ToJsonString();
        var request = Approvals.Parse(line)!;
        var q = Assert.Single(request.Questions!);
        Assert.Equal("¿Qué hago con el menú del ícono?", q.Question);
        Assert.Equal(["A · Borrar ya", "B · Aspecto primero"], q.Options.Select(o => o.Label));
        Assert.Equal("Más rápido; quedan tareas pendientes.", q.Options[0].Description);

        // Claude Code compara la respuesta con la etiqueta: tiene que volver idéntica.
        var answer = Approvals.Answer(request, new Dictionary<string, IReadOnlyList<string>> { [q.Question] = ["A · Borrar ya"] });
        using var doc = System.Text.Json.JsonDocument.Parse(answer);
        var answers = doc.RootElement.GetProperty("hookSpecificOutput").GetProperty("decision").GetProperty("updatedInput").GetProperty("answers");
        Assert.Equal("A · Borrar ya", answers.GetProperty("¿Qué hago con el menú del ícono?").GetString());
    }
}
