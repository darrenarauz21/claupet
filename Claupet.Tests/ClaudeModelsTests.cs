using System.Text.Json;

namespace Claupet.Tests;

/// <summary>
/// Los modelos de Claude se leen de Claude Code (la respuesta a initialize): así los menús y los nombres siguen sus
/// alias sin tocar la app cuando sale un modelo nuevo.
/// </summary>
public sealed class ClaudeModelsTests
{
    /// <summary>
    /// Lo que respondió Claude Code 2.1.319 el 2026-11-19 (los campos que se leen): «fable» todavía iba a la 5.1 y la 5.2
    /// salía aparte, con su ID.
    /// </summary>
    const string Offered = """
        [
          {"value":"default","resolvedModel":"claude-sonnet-5-5","displayName":"Default (recommended)","description":"Sonnet 5.5 · Efficient for routine tasks"},
          {"value":"opus","resolvedModel":"claude-opus-5-5","displayName":"Opus 5.5"},
          {"value":"claude-fable-5-2","resolvedModel":"claude-fable-5-2","displayName":"Fable 5.2"},
          {"value":"sonnet","resolvedModel":"claude-sonnet-5-5","displayName":"Sonnet 5.5"},
          {"value":"haiku","resolvedModel":"claude-haiku-5-5","displayName":"Haiku 5.5"},
          {"value":"fable","resolvedModel":"claude-fable-5-1","displayName":"Fable 5.1"},
          {"value":"claude-haiku-4-5-20251001","resolvedModel":"claude-haiku-4-5-20251001","displayName":"Haiku 4.5"},
          {"value":"claude-sonnet-5","resolvedModel":"claude-sonnet-5","displayName":"Sonnet 5"},
          {"value":"claude-opus-5","resolvedModel":"claude-opus-5","displayName":"Opus 5"},
          {"value":"claude-fable-5","resolvedModel":"claude-fable-5","displayName":"Fable 5"},
          {"value":"claude-opus-4-8","resolvedModel":"claude-opus-4-8","displayName":"Opus 4.8"},
          {"value":"claude-sonnet-4-6","resolvedModel":"claude-sonnet-4-6","displayName":"Sonnet 4.6"}
        ]
        """;

    internal static IReadOnlyList<ClaudeModel> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ClaudeModels.Parse(doc.RootElement);
    }

    [Fact]
    public void SeLeeLoQueOfreceClaudeCodeSinElPorDefecto()
    {
        var models = Parse(Offered);

        Assert.DoesNotContain(models, m => m.Value == "default");
        Assert.Equal(11, models.Count);
        Assert.Equal(new ClaudeModel("fable", "claude-fable-5-1", "Fable 5.1"), models.Single(m => m.Value == "fable"));
        Assert.Equal(new ClaudeModel("claude-fable-5-2", "claude-fable-5-2", "Fable 5.2"), models.Single(m => m.Value == "claude-fable-5-2"));
    }

    [Fact]
    public void LoQueNoSePuedePasarAlComandoNoEntra()
    {
        var models = Parse("""[{"value":"opus; rm -rf /","resolvedModel":"x","displayName":"Mal"},{"value":"opus","resolvedModel":"claude-opus-5-5"},{"value":"opus","displayName":"Repetido"}]""");

        // Sin nombre, se arma del ID; el repetido no entra.
        Assert.Equal([new ClaudeModel("opus", "claude-opus-5-5", "Opus 5.5")], models);
    }

    [Fact]
    public void LosMenusCortosLlevanLosAliasYLoMasNuevoQueTodaviaNoTieneAlias()
    {
        // Del más rápido al más capaz; Fable 5.2 entra porque «fable» sigue en la 5.1.
        Assert.Equal(["haiku", "sonnet", "opus", "claude-fable-5-2", "fable"], ClaudeModels.Featured(Parse(Offered)));

        // Cuando el alias la alcanza, la 5.2 ya no sale aparte.
        var caughtUp = Parse(Offered.Replace("\"resolvedModel\":\"claude-fable-5-1\",\"displayName\":\"Fable 5.1\"", "\"resolvedModel\":\"claude-fable-5-2\",\"displayName\":\"Fable 5.2\""));
        Assert.Equal(["haiku", "sonnet", "opus", "fable"], ClaudeModels.Featured(caughtUp));
    }

    [Fact]
    public void EnLaSesionSeElijeTodoPorFamiliaDelMasNuevoAlMasViejoYOpusplan()
    {
        Assert.Equal(
            ["haiku", "claude-haiku-4-5-20251001",
             "sonnet", "claude-sonnet-5", "claude-sonnet-4-6",
             "opus", "claude-opus-5", "claude-opus-4-8",
             "claude-fable-5-2", "fable", "claude-fable-5",
             "opusplan"],
            ClaudeModels.Choices(Parse(Offered)));
    }

    [Fact]
    public void SinRespuestaDeClaudeCodeQuedanLosDeLaAppSinLasVariantesDe1M()
    {
        // Las pruebas usan otra carpeta de datos: ahí Claude Code nunca respondió.
        Assert.Equal(["haiku", "sonnet", "opus", "fable", "opusplan"], Launcher.Models);
        Assert.DoesNotContain(Launcher.Models, m => m.EndsWith("[1m]", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("claude-fable-5-1", null, "fable")]                      // vino por el alias
    [InlineData("claude-fable-5-2", "fable", "claude-fable-5-2")]        // eligió la 5.2 después de abrir con «fable»
    [InlineData("claude-sonnet-5-5", "sonnet", "sonnet")]
    [InlineData(null, "opus", "opus")]                                   // todavía no respondió
    [InlineData("claude-opus-4-1", "opus", "opus")]                      // uno que no se ofrece: lo elegido al abrir
    public void ElModeloQueRespondioSeMarcaEnElMenu(string? used, string? chosen, string? marked)
    {
        var offered = Parse(Offered);
        Assert.Equal(marked, ClaudeModels.ChoiceFor(used, ClaudeModels.Choices(offered), chosen, offered));
    }

    [Fact]
    public void LoQueDijoClaudeCodeSeGuardaParaLaProximaVez()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "modelos-claude.json");
        Assert.Null(ClaudeModels.Load(path));

        var models = Parse(Offered);
        ClaudeModels.Save(path, models);
        Assert.Equal(models, ClaudeModels.Load(path));

        File.WriteAllText(path, "no es json");
        Assert.Null(ClaudeModels.Load(path));
    }

    [Fact]
    public void LaSesionDeClaudeCodeOfreceLoQueDijoAlAbrir()
    {
        using var dir = new TempDir();
        using var agent = new TerminalAgent(new AgentOptions(dir.Path), a => a());
        Assert.Equal(Launcher.Models, agent.Driver.Models);

        // Por la transcripción (no por el driver), para no cambiar lo que ven las demás pruebas.
        agent.Transcript.Apply(InitializeResponse(Offered));
        Assert.Equal(11, agent.Transcript.Offered!.Count);
        Assert.Equal(ClaudeModels.Choices(agent.Transcript.Offered), agent.Driver.Models);
        Assert.Contains("claude-fable-5-2", agent.Driver.Models);
    }

    /// <summary>La respuesta de Claude Code al initialize del principio de la sesión, con sus modelos.</summary>
    internal static string InitializeResponse(string models) =>
        """{"type":"control_response","response":{"subtype":"success","request_id":"c-1","response":{"models":""" + models.ReplaceLineEndings("") + "}}}";
}
