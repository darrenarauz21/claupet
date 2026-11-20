namespace Claupet.Tests;

/// <summary>
/// Al actualizarse, Claupet se reinicia entero: la bandeja le dice a Claupet Console qué versión es y desde qué ejecutable
/// corre, y la Console que quedó con el código de antes se cierra y se vuelve a abrir (antes había que cerrarla a mano).
/// </summary>
public sealed class ConsoleRestartTests
{
    const string Exe = @"C:\Users\ana\AppData\Local\Programs\Claupet\Claupet.exe";

    [Fact]
    public void LaConsoleQuedaViejaSoloSiLaBandejaEsOtraVersionDelMismoEjecutable()
    {
        // Se actualizó: el mismo archivo, otra versión.
        Assert.True(ConsoleLink.Outdated("2.7.2+abc", Exe, "2.7.1+def", Exe));
        // La misma versión (cambió el idioma, se reinició la bandeja sin actualizar): nada.
        Assert.False(ConsoleLink.Outdated("2.7.1+def", Exe, "2.7.1+def", Exe));
        // Una copia de prueba (--preview) en otra carpeta, conectada a la bandeja instalada: no se reinicia (sería un bucle).
        Assert.False(ConsoleLink.Outdated("2.7.2+abc", Exe, "2.7.3+ghi", @"C:\repo\dist\Claupet.exe"));
        // Una bandeja de antes de este cambio no manda su versión: nada.
        Assert.False(ConsoleLink.Outdated(null, null, "2.7.1+def", Exe));
        Assert.False(ConsoleLink.Outdated("2.7.2+abc", Exe, "2.7.1+def", null));
        if (OperatingSystem.IsWindows()) Assert.True(ConsoleLink.Outdated("2.7.2+abc", Exe.ToUpperInvariant(), "2.7.1+def", Exe));
    }

    [Fact]
    public void ElEstadoLlevaLaVersionYElEjecutableDeLaBandeja()
    {
        var sent = new ConsoleMessage { Type = ConsoleLink.State, Language = "es", AppVersion = ConsoleLink.AppVersion, AppPath = Exe };
        var received = ConsoleLink.Decode(ConsoleLink.Encode(sent))!;

        Assert.Equal(ConsoleLink.AppVersion, received.AppVersion);
        Assert.Equal(Exe, received.AppPath);
        // La versión es la completa, con el commit si lo tiene: dos compilaciones distintas no se confunden.
        Assert.StartsWith(SemVersion.App.ToString(), ConsoleLink.AppVersion);
        // Un mensaje de una bandeja vieja, sin esos campos, sigue leyéndose.
        Assert.Null(ConsoleLink.Decode("""{"Type":"estado","Language":"es"}""")!.AppVersion);
    }
}
