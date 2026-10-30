namespace Claupet.Tests;

/// <summary>
/// La terminal de comandos en bloques: cómo se lee la salida de un shell en ConPTY con las marcas OSC 133. Los textos
/// tienen la forma de lo que pinta ConPTY con PowerShell y bash (CR LF, colores, el cursor que se mueve).
/// </summary>
public sealed class ShellTranscriptTests
{
    const string E = "\x1b";

    static string Osc(string body) => $"{E}]{body}\a";

    /// <summary>Lo que pinta el prompt de PowerShell con la integración: D, la carpeta, A, el prompt y B.</summary>
    static string Prompt(int code = 0) =>
        Osc($"133;D;{code}") + Osc("7;file://localhost/C%3A%5Cproyecto") + Osc("133;A") + @"PS C:\proyecto> " + Osc("133;B");

    /// <summary>El eco del comando (con colores, como PSReadLine), el salto y la marca C.</summary>
    static string Echo(string command) => $"{E}[93m{command}{E}[0m\r\n" + Osc("133;C");

    static ShellTranscript Started()
    {
        var t = new ShellTranscript(80, 10);
        t.Apply($"{E}[?9001h{E}[?1004h{E}[2J{E}[H" + Osc("0;PowerShell") + Prompt());
        return t;
    }

    [Fact]
    public void ArmaUnBloquePorComandoConSuSalidaYSuCodigo()
    {
        var t = Started();
        Assert.True(t.Ready);
        Assert.Equal(@"C:\proyecto", t.Cwd);

        var block = t.Begin("echo hola");
        Assert.True(block.Running);
        Assert.True(t.Busy);
        var changed = t.Apply(Echo("echo hola") + "hola\r\n");
        Assert.Contains(block, changed);
        Assert.Equal(["hola"], block.Lines);
        Assert.True(block.Running);

        t.Apply(Prompt(0));
        Assert.False(block.Running);
        Assert.False(t.Busy);
        Assert.Equal(0, block.ExitCode);
        Assert.False(block.Failed);
        Assert.Equal(@"C:\proyecto", block.Cwd);
        Assert.Equal([ShellBlockKind.Command], t.Blocks.Select(b => b.Kind));
    }

    [Fact]
    public void LasMarcasYLasSecuenciasPuedenLlegarCortadasEnCualquierParte()
    {
        var stream = $"{E}[2J{E}[H" + Prompt() + Echo("dir") + $"{E}[32muno{E}[m\r\ndos\r\n" + Prompt(0);
        var t = new ShellTranscript(80, 10);
        var block = t.Begin("dir");
        // De a una letra: cada secuencia y cada marca queda partida entre dos pedazos.
        foreach (var c in stream) t.Apply(c.ToString());

        Assert.Equal(["uno", "dos"], block.Lines);
        Assert.Equal(0, block.ExitCode);
        Assert.False(block.Running);
    }

    [Fact]
    public void UnComandoQueFallaGuardaSuCodigo()
    {
        var t = Started();
        var block = t.Begin("Get-Item C:\\no-existe");
        t.Apply(Echo("Get-Item C:\\no-existe") + $"{E}[91mGet-Item : No se encuentra la ruta{E}[0m\r\n" + Prompt(1));

        Assert.Equal(1, block.ExitCode);
        Assert.True(block.Failed);
        Assert.Equal(["Get-Item : No se encuentra la ruta"], block.Lines);
    }

    [Fact]
    public void ElRetornoDeCarroYElRetrocesoReescribenLaLinea()
    {
        var t = Started();
        var block = t.Begin("npm install");
        t.Apply(Echo("npm install") + "[##        ] 20%\r[#####     ] 50%");
        Assert.Equal(["[#####     ] 50%"], block.Lines);

        t.Apply("\r[##########] 100%\r\nlisto.\b!\r\n" + Prompt());
        Assert.Equal(["[##########] 100%", "listo!"], block.Lines);
    }

    [Fact]
    public void BorrarLaLineaYMoverElCursorNoDuplicaLaSalida()
    {
        var t = Started();
        var block = t.Begin("progreso");
        t.Apply(Echo("progreso") + "descargando 1 de 3\r\n");
        // Lo que hace ConPTY al volver a pintar: sube una fila, borra la línea y escribe de nuevo.
        t.Apply($"{E}[1A{E}[2Kdescargando 3 de 3{E}[5Xfin\r\n" + Prompt());

        Assert.Equal(["descargando 3 de 3fin"], block.Lines);
    }

    [Fact]
    public void LaSalidaLargaGuardaSoloLasUltimasLineasYCuentaLasDemas()
    {
        var t = Started();
        var block = t.Begin("1..1000");
        t.Apply(Echo("1..1000"));
        for (var i = 1; i <= 1000; i++) t.Apply($"fila {i}\r\n");
        t.Apply(Prompt());

        Assert.Equal(ShellTranscript.MaxLines, block.Lines.Count);
        Assert.Equal(1000 - ShellTranscript.MaxLines, block.Hidden);
        Assert.Equal("fila 601", block.Lines[0]);
        Assert.Equal("fila 1000", block.Lines[^1]);
    }

    [Fact]
    public void UnaLineaQueNoCabeEnElAnchoSeJuntaEnUna()
    {
        var t = new ShellTranscript(20, 5);
        t.Apply(Prompt());
        var block = t.Begin("largo");
        var line = new string('x', 75);
        // Más que el alto de la pantalla: las filas que salen por arriba también se juntan.
        t.Apply(Echo("largo") + line + "\r\nfin\r\n" + Prompt());

        Assert.Equal([line, "fin"], block.Lines);
    }

    [Fact]
    public void LaDDelPrimerPromptNoCierraUnComandoEnviadoAntes()
    {
        var t = new ShellTranscript(80, 10);
        var block = t.Begin("echo pronto");
        // El shell todavía arranca: muestra el perfil, su primer prompt (con D) y recién ahí lee el comando.
        t.Apply("Cargando perfil...\r\n" + Prompt(0));
        Assert.True(block.Running);

        t.Apply(Echo("echo pronto") + "pronto\r\n" + Prompt(0));
        Assert.False(block.Running);
        Assert.Equal(["pronto"], block.Lines);
        // Lo que mostró antes del primer prompt queda en su propio bloque, sin comando.
        var preamble = t.Blocks[0];
        Assert.Equal(ShellBlockKind.Output, preamble.Kind);
        Assert.Equal(["Cargando perfil..."], preamble.Lines);
    }

    [Fact]
    public void SinLaMarcaCLaSalidaEmpiezaDespuesDelEco()
    {
        var t = Started();
        var block = t.Begin("ls");
        t.Apply("ls\r\na.txt\r\nb.txt\r\n" + Prompt(0));

        Assert.Equal(["a.txt", "b.txt"], block.Lines);
        Assert.Equal(0, block.ExitCode);
    }

    [Fact]
    public void UnComandoQueNoSeEnvioDesdeLaAppSeTomaDelEco()
    {
        var t = Started();
        var changed = t.Apply("git status\r\n" + Osc("133;C") + "limpio\r\n");

        var block = Assert.Single(changed);
        Assert.Equal("git status", block.Command);
        Assert.True(block.Running);
        t.Apply(Prompt(0));
        Assert.Equal(["limpio"], block.Lines);
    }

    [Fact]
    public void LaPantallaAlternativaDaUnAvisoYNoEnsuciaElBloque()
    {
        var t = Started();
        var block = t.Begin("vim");
        var changed = t.Apply(Echo("vim") + $"{E}[?1049h{E}[H~\r\n~\r\n~");

        Assert.True(t.AltScreen);
        var notice = Assert.Single(changed, b => b.Kind == ShellBlockKind.Notice);
        Assert.Contains("pantalla completa", notice.Command);
        Assert.Empty(block.Lines);

        t.Apply($"{E}[?1049l" + Prompt(0));
        Assert.False(t.AltScreen);
        Assert.False(block.Running);
    }

    [Fact]
    public void LasMarcasDeVsCodeTambienSirven()
    {
        var t = new ShellTranscript(80, 10);
        t.Apply(Osc("633;P;Cwd=C:\\otro;lado") + Osc("633;A") + "$ " + Osc("633;B"));
        Assert.Equal(@"C:\otro;lado", t.Cwd);
        var block = t.Begin("pwd");
        t.Apply("pwd\r\n" + Osc("633;C") + "/c/otro\r\n" + Osc("633;D;7") + Osc("633;A"));

        Assert.Equal(["/c/otro"], block.Lines);
        Assert.Equal(7, block.ExitCode);
    }

    [Fact]
    public void UnPromptNuevoSinDCierraElComandoSinCodigo()
    {
        var t = Started();
        var block = t.Begin("exit");
        t.Apply(Echo("exit") + "adiós\r\n" + Osc("133;A"));

        Assert.False(block.Running);
        Assert.Null(block.ExitCode);
        Assert.Equal(["adiós"], block.Lines);
    }

    [Fact]
    public void AlCerrarseElShellElComandoQuedaSinCodigo()
    {
        var t = Started();
        var block = t.Begin("sleep 100");
        t.Apply(Echo("sleep 100") + "esperando\r\n");

        Assert.Same(block, t.Stop());
        Assert.False(block.Running);
        Assert.Null(block.ExitCode);
        Assert.Equal(["esperando"], block.Lines);
        Assert.False(t.Busy);
    }

    [Theory]
    [InlineData("file://localhost/C%3A%5Cproyecto%20nuevo", @"C:\proyecto nuevo")]
    [InlineData("file://equipo/home/ana/src", "/home/ana/src")]
    [InlineData("file:///C:/proyecto", @"C:\proyecto")]
    [InlineData("http://x/y", null)]
    public void LaCarpetaSaleDelOsc7(string url, string? path) => Assert.Equal(path, ShellTranscript.PathOf(url));

    [Theory]
    [InlineData(@"C:\Program Files\Git\bin\bash.exe", "\"C:\\Program Files\\Git\\bin\\bash.exe\"")]
    [InlineData("simple", "simple")]
    [InlineData("con \"comillas\"", "\"con \\\"comillas\\\"\"")]
    [InlineData(@"C:\carpeta con espacio\", "\"C:\\carpeta con espacio\\\\\"")]
    public void LasComillasDeLaLineaDeComandos(string arg, string quoted) => Assert.Equal(quoted, Shells.Quote(arg));

    [Fact]
    public void LaRutaDeWindowsComoLaVeWsl() => Assert.Equal("/mnt/c/proyecto/x.sh", Shells.WslPath(@"C:\proyecto\x.sh"));

    /// <summary>De verdad: Windows PowerShell (está en todo Windows) en ConPTY, con la integración de la app.</summary>
    [Fact]
    public async Task PowerShellDeVerdadDaBloquesConSuCodigo()
    {
        if (!PseudoConsole.IsSupported || Shells.Available().FirstOrDefault(s => s.Kind == ShellKind.PowerShell) is not { } shell) return;
        var gate = new object();
        using var session = new ShellSession(shell, Path.GetTempPath(), action => { lock (gate) action(); });
        session.Start();
        await Until(() => session.Transcript.Ready, gate);

        lock (gate) session.Send("echo hola");
        await Until(() => !session.Transcript.Busy, gate);
        lock (gate) session.Send("cmd /c exit 3");
        await Until(() => !session.Transcript.Busy, gate);

        lock (gate)
        {
            var blocks = session.Transcript.Blocks.Where(b => b.Kind == ShellBlockKind.Command).ToList();
            Assert.Equal(["echo hola", "cmd /c exit 3"], blocks.Select(b => b.Command));
            Assert.Equal(["hola"], blocks[0].Lines);
            Assert.Equal(0, blocks[0].ExitCode);
            Assert.Equal(3, blocks[1].ExitCode);
        }
    }

    static async Task Until(Func<bool> done, object gate)
    {
        var limit = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < limit)
        {
            lock (gate)
                if (done()) return;
            await Task.Delay(100);
        }
        throw new TimeoutException();
    }
}
