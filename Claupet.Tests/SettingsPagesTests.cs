namespace Claupet.Tests;

/// <summary>Las secciones de Ajustes (las mismas en la bandeja y en Claupet Console) y el mensaje que las lleva.</summary>
public sealed class SettingsPagesTests
{
    static readonly SettingsState Full = new()
    {
        Chat = true, Island = true, Mic = true, ReadAloud = true, DesktopMascot = true, LocalReady = true,
        SpeechStatus = "Voz de Claupet (descargando…)", SpeechAction = "Cancelar", SavedDetail = "3 sin IA · 1 con el modelo local",
        TopicEngines = [new("hora", "Hora y fecha", "Sin IA", false), new("internet", "Internet, archivos y tareas", "Siempre la nube", true)],
        Grants = [new("captura", "Capturar la pantalla", true), new("calendario", "Agendar en el calendario", false)],
    };

    static readonly string[] QuickTabs = ["Avisos", "Apariencia", "Asistente", "Isla", "Sistema"];

    [Fact]
    public void LosRapidosLlevanPocasFilasPorPestana()
    {
        var pages = SettingsPages.Build(Full);
        foreach (var tab in QuickTabs)
        {
            var quick = pages.Where(p => p.Tab == tab).SelectMany(p => p.Rows).Where(r => r.Place != RowPlace.Console).ToList();
            Assert.InRange(quick.Count, 1, 6);
            Assert.DoesNotContain(quick, r => r.Kind == RowKind.Caption);
        }
        Assert.All(pages.Where(p => p.Tab != null), p => Assert.Contains(p.Tab, QuickTabs));
    }

    [Theory]
    [InlineData("respuestas")]
    [InlineData("extensiones")]
    [InlineData("cuentas")]
    public void AlgunasSeccionesSoloEstanEnLaConsole(string id) =>
        Assert.Null(SettingsPages.Build(Full).Single(p => p.Id == id).Tab);

    [Fact]
    public void UnaAccionPorFila()
    {
        var keys = SettingsPages.Build(Full).SelectMany(p => p.Rows)
            .SelectMany(r => r.Kind == RowKind.Links ? r.Links!.Select(l => l.Key) : [r.Key]).Where(k => k.Length > 0).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void LaConsoleEligeCadaTipoYCadaPermisoEnSuFila()
    {
        var pages = SettingsPages.Build(Full);
        var answers = pages.Single(p => p.Id == "respuestas").Rows;
        Assert.Contains(answers, r => r is { Key: "topic:hora", Kind: RowKind.Choice });
        Assert.Contains(answers, r => r is { Text: "Internet, archivos y tareas", Kind: RowKind.Info }); // fijo: no se elige
        Assert.Contains(pages.SelectMany(p => p.Rows), r => r is { Key: "grant:captura", On: true, Place: RowPlace.Console });
    }

    [Fact]
    public void SinChatNiIslaNoHayPermisos() =>
        Assert.DoesNotContain(SettingsPages.Build(new SettingsState()).SelectMany(p => p.Rows), r => r.Key.StartsWith("grant", StringComparison.Ordinal));

    [Fact]
    public void ElEstadoLlegaIgualALaConsole()
    {
        var pages = SettingsPages.Build(Full);
        var line = ConsoleLink.Encode(new ConsoleMessage { Type = ConsoleLink.State, Language = "es", Palette = "oceano", Pages = pages });
        Assert.DoesNotContain('\n', line); // un mensaje por línea
        var back = ConsoleLink.Decode(line)!;
        Assert.Equal(ConsoleLink.State, back.Type);
        Assert.Equal("oceano", back.Palette);
        Assert.Equal(pages.Count, back.Pages!.Count);
        for (var i = 0; i < pages.Count; i++)
        {
            Assert.Equal(pages[i] with { Rows = [] }, back.Pages[i] with { Rows = [] });
            Assert.Equal(pages[i].Rows.Select(r => r with { Links = null }), back.Pages[i].Rows.Select(r => r with { Links = null }));
            Assert.Equal(pages[i].Rows.SelectMany(r => r.Links ?? []), back.Pages[i].Rows.SelectMany(r => r.Links ?? []));
        }
    }

    [Fact]
    public void UnaListaYSuRespuesta()
    {
        var offer = ConsoleLink.Decode(ConsoleLink.Encode(new ConsoleMessage
        {
            Type = ConsoleLink.Options, Key = "palette", Token = 7, Value = "bosque", Options = [new("terracota", "Terracota"), new("bosque", "Bosque")],
        }))!;
        Assert.Equal(7, offer.Token);
        Assert.Equal(["terracota", "bosque"], offer.Options!.Select(o => o.Id));
        var cancel = ConsoleLink.Decode(ConsoleLink.Encode(new ConsoleMessage { Type = ConsoleLink.Pick, Token = 7 }))!;
        Assert.Null(cancel.Value); // se cerró sin elegir
    }

    [Theory]
    [InlineData("")]
    [InlineData("{no es json")]
    public void UnaLineaRotaNoCortaElCanal(string line) => Assert.Null(ConsoleLink.Decode(line));
}
