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
        // En «Personalizado», cada tipo de pregunta tiene su fila.
        var pages = SettingsPages.Build(Full with { AnswerCustom = true });
        var answers = pages.Single(p => p.Id == "respuestas").Rows;
        Assert.Contains(answers, r => r is { Key: "answerpreset", Kind: RowKind.Choice });
        Assert.Contains(answers, r => r is { Key: "topic:hora", Kind: RowKind.Choice });
        Assert.Contains(answers, r => r is { Text: "Internet, archivos y tareas", Kind: RowKind.Info }); // fijo: no se elige
        Assert.Contains(pages.SelectMany(p => p.Rows), r => r is { Key: "grant:captura", On: true, Place: RowPlace.Console });
    }

    [Fact]
    public void LoQueElSistemaNoTieneNoAparece()
    {
        var pages = SettingsPages.Build(Full with { Features = PlatformFeatures.None });
        var keys = pages.SelectMany(p => p.Rows).Select(r => r.Key).ToHashSet();
        foreach (var missing in new[] { "chat", "mic", "readaloud", "island", "extensions", "localmodel", "size", "startmenu", "chatkey" })
            Assert.DoesNotContain(missing, keys);
        Assert.DoesNotContain(pages.SelectMany(p => p.Rows), r => r.Key.StartsWith("grant:", StringComparison.Ordinal));
        // En vez de filas que no hacen nada, una nota.
        foreach (var id in new[] { "isla", "extensiones", "asistente" })
            Assert.Contains(pages.Single(p => p.Id == id).Rows, r => r.Kind == RowKind.Note);
        // Lo que sí tiene sigue ahí.
        Assert.Contains("notify", keys);
        Assert.Contains("answerpreset", keys);
        Assert.Contains("desk", keys);
    }

    [Fact]
    public void ConSoloElChatAparecenSusFilasYNoLasDeArchivos()
    {
        // La app Avalonia, con el chat de la mascota pero sin soltar archivos ni acciones todavía.
        var keys = SettingsPages.Build(Full with { Features = PlatformFeatures.MascotChat })
            .SelectMany(p => p.Rows).Select(r => r.Key).ToHashSet();
        foreach (var key in new[] { "chat", "mmodel", "account", "folder" })
            Assert.Contains(key, keys);
        Assert.DoesNotContain("drops", keys);
        Assert.DoesNotContain(keys, k => k.StartsWith("grant:", StringComparison.Ordinal));
    }

    [Fact]
    public void ConLaIslaAparecenSusFilasSinLasDeVozNiAtajos()
    {
        // La app Avalonia con el chat y la isla, todavía sin voz, atajos ni acciones.
        var pages = SettingsPages.Build(Full with { Features = PlatformFeatures.MascotChat | PlatformFeatures.Island });
        var keys = pages.SelectMany(p => p.Rows).Select(r => r.Key).ToHashSet();
        foreach (var key in new[] { "island", "imodel", "hover", "busy", "approvals", "recap", "recapnow" })
            Assert.Contains(key, keys);
        foreach (var key in new[] { "mic", "readaloud", "shortcuts" })
            Assert.DoesNotContain(key, keys);
    }

    [Fact]
    public void ConLaVozAparecenSusFilasSinLasDeAtajos()
    {
        // La app Avalonia con el chat, la isla y la voz, todavía sin atajos ni acciones.
        var keys = SettingsPages.Build(Full with { Features = PlatformFeatures.MascotChat | PlatformFeatures.Island | PlatformFeatures.Mic | PlatformFeatures.ReadAloud })
            .SelectMany(p => p.Rows).Select(r => r.Key).ToHashSet();
        foreach (var key in new[] { "mic", "mickey", "voice", "readaloud", "readvoice", "speech" })
            Assert.Contains(key, keys);
        foreach (var key in new[] { "chatkey", "copilot", "shortcuts" })
            Assert.DoesNotContain(key, keys);
    }

    [Fact]
    public void ConTodoComoEnWindowsNoFaltaNada()
    {
        var pages = SettingsPages.Build(Full);
        var keys = pages.SelectMany(p => p.Rows).Select(r => r.Key).ToHashSet();
        foreach (var key in new[] { "chat", "drops", "mic", "readaloud", "island", "localmodel", "size", "startmenu" })
            Assert.Contains(key, keys);
        // Las extensiones las arma ExtensionsPanel en la Console; aquí queda el resumen, no la nota de «todavía no».
        Assert.DoesNotContain(pages.Single(p => p.Id == "extensiones").Rows, r => r.Kind == RowKind.Note);
    }

    [Fact]
    public void ConUnModoArmadoNoSeEligeTipoPorTipo()
    {
        var answers = SettingsPages.Build(Full).Single(p => p.Id == "respuestas").Rows;
        Assert.Contains(answers, r => r is { Key: "answerpreset", Kind: RowKind.Choice });
        Assert.DoesNotContain(answers, r => r.Key.StartsWith("topic:", StringComparison.Ordinal));
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

    [Fact]
    public void LosMutexSonDeLaSesionDeWindows()
    {
        // En Windows siguen en «Local\» (la sesión de Windows); en Linux y macOS van en «Global\» con el usuario.
        string[] names = [ConsoleLink.TrayMutex, ConsoleLink.ConsoleMutex];
        Assert.Equal([@"Local\Claupet", @"Local\Claupet-console"], names);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{no es json")]
    public void UnaLineaRotaNoCortaElCanal(string line) => Assert.Null(ConsoleLink.Decode(line));
}
