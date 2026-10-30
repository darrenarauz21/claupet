namespace Claupet.Tests;

/// <summary>Los personajes sobre la base del chibi (ChibiSkin): se eligen como el chibi y se dibujan enteros.</summary>
public sealed class MascotsTests
{
    public static TheoryData<string> Ids => [.. ChibiSprite.Skins.Select(s => s.Id)];

    [Fact]
    public void LosPersonajesEstanEnLaListaDeAjustes()
    {
        var ids = Mascots.Choices.Select(c => c.Id).ToList();
        Assert.Equal([Mascots.Classic, "chibi", "dokki", "gemma", "ciel"], ids.Take(5));
        Assert.Contains("ninguna", ids); // y después los estilos al actualizar
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void UnPersonajeSeEligeComoElChibi(string id)
    {
        var settings = new AppSettings();
        Mascots.Choose(settings, id);
        Assert.Equal((RefreshSprite.Mascot, id), (settings.Animation, settings.Mascot));
        Assert.Equal(id, Mascots.ChoiceOf(settings));
        Assert.True(Mascots.IsCharacter(id));
        Assert.Equal("chibi", RefreshSprite.Resolve(settings.Animation, settings.Mascot)); // el ícono, con su cabeza
    }

    [Fact]
    public void LaClasicaYLosEstilosNoSonPersonajes()
    {
        Assert.False(Mascots.IsCharacter(Mascots.Classic));
        Assert.False(Mascots.IsCharacter("asterisco"));
        Assert.Null(ChibiSprite.SkinOf(null));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void SusRejillasTienenElTamanoDeLaBase(string id)
    {
        var skin = ChibiSprite.SkinOf(id)!;
        Assert.Equal(48, skin.Body.Length);
        Assert.All(skin.Body, row => Assert.Equal(16, row.Length));
        Assert.Equal(16, skin.Head.Length);
        Assert.All(skin.Head, row => Assert.Equal(8, row.Length));
        Assert.Equal(8, skin.Armless.Length);
        Assert.All(skin.Armless, row => Assert.Equal(16, row.Length));
        if (skin.Lid is { } lid) Assert.All(lid, row => Assert.Equal(16, row.Length));
    }

    /// <summary>
    /// Cada cuadro de cada movimiento (con y sin música) y las cabezas se pintan dentro del lienzo: una letra sin color
    /// en la paleta del personaje fallaría aquí.
    /// </summary>
    [Theory]
    [MemberData(nameof(Ids))]
    public void CadaCuadroSePintaDentroDelLienzo(string id)
    {
        var skin = ChibiSprite.SkinOf(id)!;
        var cells = 0;
        void Inside(int x, int y, int w, int h, int columns, int rows)
        {
            Assert.InRange(x, 0, columns - w);
            Assert.InRange(y, 0, rows - h);
            cells++;
        }
        foreach (var move in Enum.GetValues<ChibiMove>())
        {
            var length = ChibiSprite.Length(move, skin);
            var last = -1;
            for (long t = 0; t < length; t += 10)
            {
                var i = ChibiSprite.FrameIndex(move, t, skin);
                if (i == last) continue;
                last = i;
                ChibiSprite.Paint((_, x, y, w, h) => Inside(x, y, w, h, ChibiSprite.Columns, ChibiSprite.Rows), move, t, skin);
                ChibiSprite.Paint((_, x, y, w, h) => Inside(x, y, w, h, ChibiSprite.Columns, ChibiSprite.Rows), move, t, music: true, skin);
            }
        }
        foreach (var frame in Enum.GetValues<MascotFrame>())
        {
            ChibiSprite.PaintHead((_, x, y, w, h) => Inside(x, y, w, h, ChibiSprite.HeadSize, ChibiSprite.HeadSize), frame, skin: skin);
            ChibiSprite.PaintHead((_, x, y, w, h) => Inside(x, y, w, h, ChibiSprite.SmallHeadSize, ChibiSprite.SmallHeadSize), frame, small: true, skin: skin);
        }
        Assert.True(cells > 1000);
    }

    [Theory]
    [InlineData("dokki")]
    [InlineData("gemma")]
    [InlineData("ciel")]
    public void CadaPersonajeTieneSuVoz(string pack)
    {
        Assert.Contains(Sounds.Packs, p => p.Id == pack);
        Assert.True(Sounds.IsVoice(pack));
        foreach (var name in new[] { "terminado", "pregunta", "permiso", "fallo", "uso", "aviso" })
        {
            var wav = Sounds.Generate(pack, name);
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
            var seconds = (wav.Length - 44) / 2.0 / 44100;
            Assert.InRange(seconds, 0.2, 1.5);
            Assert.Equal(wav, Sounds.Generate(pack, name)); // siempre igual
            Assert.NotEqual(Sounds.Generate("chibi", name), wav); // y no es la del chibi
        }
    }

    [Fact]
    public void CadaAgenteTieneSuPersonajeYSePuedeCambiar()
    {
        var settings = new AppSettings();
        Assert.Equal("ciel", Agents.PetOf(settings, Agents.Codex));
        Assert.Equal("gemma", Agents.PetOf(settings, Agents.Gemini));
        Assert.Equal("", Agents.PetOf(settings, Agents.Copilot)); // sin personaje propio: ninguno, salvo que se elija

        settings.AgentPet[Agents.Copilot] = "dokki"; // cualquiera sirve para cualquiera
        settings.AgentPet[Agents.Codex] = "";        // y se puede dejar sin mascota
        settings.AgentPet[Agents.Gemini] = "no-existe";
        Assert.Equal("dokki", Agents.PetOf(settings, Agents.Copilot));
        Assert.Equal("", Agents.PetOf(settings, Agents.Codex));
        Assert.Equal("gemma", Agents.PetOf(settings, Agents.Gemini)); // un personaje que ya no está vuelve al de siempre
    }

    [Theory]
    [InlineData(10, true, "Run")]       // escribió hace un momento: teclea
    [InlineData(120, true, "Idle")]     // hace poco: quieta
    [InlineData(3600, true, "Sleep")]   // hace rato: duerme
    [InlineData(3600, false, "Idle")]   // sin «Dormir sin sesiones», quieta
    [InlineData(-1, true, "Sleep")]     // nunca escribió
    public void LaMascotaDeUnAgenteSigueCuandoEscribio(int secondsAgo, bool sleep, string expected)
    {
        var director = new ChibiDirector { Settings = new AppSettings { SleepWithoutSessions = sleep, DanceToMusic = false }, Skin = ChibiSprite.Ciel };
        var now = DateTimeOffset.UtcNow;
        director.SetActivity(secondsAgo < 0 ? null : now.AddSeconds(-secondsAgo), now);
        Assert.Equal(Enum.Parse<ChibiMove>(expected), director.Now().Move);
    }

    [Fact]
    public void LaFilaDeCadaAgenteSaleConElInterruptor()
    {
        var agents = new[] { new AgentState(Agents.Codex, "Codex CLI", true, "", "Ciel"), new AgentState(Agents.Copilot, "Copilot", false, "") };
        IEnumerable<string?> Keys(bool on) => SettingsPages.Build(new SettingsState { DesktopMascot = true, AgentPets = on, AgentList = agents })
            .SelectMany(p => p.Rows).Select(r => r.Key);
        Assert.Contains("agentpets", Keys(false));
        Assert.DoesNotContain("agentpet:codex", Keys(false));
        Assert.Contains("agentpet:codex", Keys(true));
        Assert.DoesNotContain("agentpet:copilot", Keys(true)); // el que no se sigue, no
    }

    [Fact]
    public void SoloElChibiLlevaElCangrejoEnLaLaptop()
    {
        Assert.Null(ChibiSprite.Chibi.Lid);
        Assert.All(ChibiSprite.Skins.Where(s => s != ChibiSprite.Chibi), s => Assert.NotNull(s.Lid));
    }
}
