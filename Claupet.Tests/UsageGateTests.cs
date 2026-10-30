namespace Claupet.Tests;

/// <summary>
/// Cuándo se vuelve a preguntar el uso (UsageGate): un 429 nunca llega como error, se sigue mostrando lo último bueno y
/// la espera queda en el archivo para los reinicios.
/// </summary>
public sealed class UsageGateTests : IDisposable
{
    const string Body = """{"limits":[{"kind":"session","percent":42,"resets_at":"2026-10-30T03:00:00Z"}]}""";
    static readonly DateTimeOffset Start = new(2026, 10, 29, 20, 0, 0, TimeSpan.FromHours(-5));

    readonly string _real = UsageGate.FilePath;
    readonly TempDir _dir = new();
    readonly string _account;

    public UsageGateTests()
    {
        UsageGate.FilePath = _dir["Claupet/uso.json"];
        _account = _dir[".claude"];
    }

    public void Dispose()
    {
        UsageGate.FilePath = _real;
        _dir.Dispose();
    }

    [Fact]
    public void SinNadaGuardadoSePuedePreguntar() => Assert.Null(UsageGate.Hold(_account, Start));

    [Fact]
    public void ConsultasSeguidasUsanLaRespuestaAnteriorDuranteUnMinuto()
    {
        UsageGate.Success(_account, Body, Start);

        var held = UsageGate.Hold(_account, Start.AddSeconds(50));
        Assert.NotNull(held);
        Assert.Null(held.Error);
        Assert.Null(held.WaitingUntil);
        Assert.Equal(42, held.Session?.Percent);
        Assert.Equal(Start, held.FetchedAt); // la hora de los datos, no la de ahora

        Assert.Null(UsageGate.Hold(_account, Start + UsageGate.MinInterval));
    }

    [Fact]
    public void UnLimiteConDatosDeAntesLosMuestraSinError()
    {
        UsageGate.Success(_account, Body, Start);

        var shown = UsageGate.Limited(_account, null, Start.AddMinutes(5));
        Assert.Null(shown.Error);
        Assert.False(shown.SessionExpired);
        Assert.Null(shown.WaitingUntil);
        Assert.Equal(42, shown.Session?.Percent);
        Assert.Equal(Start, shown.FetchedAt);

        // Durante la pausa no se pregunta; al terminar, sí.
        Assert.Equal(42, UsageGate.Hold(_account, Start.AddMinutes(6))?.Session?.Percent);
        Assert.Null(UsageGate.Hold(_account, Start.AddMinutes(7)));
    }

    [Fact]
    public void UnLimiteSinDatosQuedaEnEsperaYNoEsUnError()
    {
        var now = Start;
        var shown = UsageGate.Limited(_account, null, now);
        Assert.Null(shown.Error);
        Assert.Empty(shown.Limits);
        Assert.Equal(now.AddMinutes(2), shown.WaitingUntil);

        Assert.Equal(now.AddMinutes(2), UsageGate.Hold(_account, now.AddMinutes(1))?.WaitingUntil);
        Assert.Null(UsageGate.Hold(_account, now.AddMinutes(2)));
    }

    [Fact]
    public void CadaLimiteSeguidoDuplicaLaEsperaHastaMediaHora()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), UsageGate.Pause(1));
        Assert.Equal(TimeSpan.FromMinutes(4), UsageGate.Pause(2));
        Assert.Equal(TimeSpan.FromMinutes(8), UsageGate.Pause(3));
        Assert.Equal(TimeSpan.FromMinutes(16), UsageGate.Pause(4));
        Assert.Equal(TimeSpan.FromMinutes(30), UsageGate.Pause(5));
        Assert.Equal(TimeSpan.FromMinutes(30), UsageGate.Pause(10));

        var now = Start;
        foreach (var minutes in new[] { 2, 4, 8, 16, 30, 30 })
        {
            var waiting = UsageGate.Limited(_account, null, now).WaitingUntil;
            Assert.Equal(now.AddMinutes(minutes), waiting);
            now = waiting!.Value;
        }

        // Una respuesta buena vuelve a empezar: la pausa siguiente es otra vez de 2 min.
        UsageGate.Success(_account, Body, now);
        var after = now + UsageGate.MinInterval;
        UsageGate.Limited(_account, null, after);
        Assert.NotNull(UsageGate.Hold(_account, after.AddMinutes(2).AddSeconds(-1)));
        Assert.Null(UsageGate.Hold(_account, after.AddMinutes(2)));
    }

    [Fact]
    public void SeRespetaElRetryAfterSiEsMasLargoYConTope()
    {
        // «retry-after: 0» (lo que suele mandar) no acorta la espera.
        Assert.Equal(Start.AddMinutes(2), UsageGate.Limited(_dir[".claude-a"], TimeSpan.Zero, Start).WaitingUntil);
        Assert.Equal(Start.AddMinutes(20), UsageGate.Limited(_dir[".claude-b"], TimeSpan.FromMinutes(20), Start).WaitingUntil);
        Assert.Equal(Start.AddHours(1), UsageGate.Limited(_dir[".claude-c"], TimeSpan.FromHours(5), Start).WaitingUntil);
    }

    [Fact]
    public void LaEsperaSobreviveAlReinicioYNoDependeDeComoSeEscribaLaCarpeta()
    {
        UsageGate.Limited(_account, null, Start);
        Assert.True(File.Exists(UsageGate.FilePath));

        var otherSpelling = _account.ToUpperInvariant() + Path.DirectorySeparatorChar;
        Assert.NotNull(UsageGate.Hold(otherSpelling, Start.AddMinutes(1)));
    }

    [Fact]
    public void UnArchivoDanadoNoImpidePreguntar()
    {
        _dir.Write("Claupet/uso.json", "{ esto no es json");
        Assert.Null(UsageGate.Hold(_account, Start));

        UsageGate.Success(_account, Body, Start); // y se reescribe bien
        Assert.Equal(42, UsageGate.Hold(_account, Start.AddSeconds(1))?.Session?.Percent);
    }
}
