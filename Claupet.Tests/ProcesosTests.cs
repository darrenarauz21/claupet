namespace Claupet.Tests;

/// <summary>El panel de Procesos de la Terminal: cada proceso dice de qué sesión es y qué es.</summary>
public sealed class ProcesosTests
{
    [Fact]
    public void ElServidorDeClaupetDiceQueEsYLoDemasSuPid()
    {
        Assert.Equal("servidor de Claupet · pid 27644", TerminalPane.ChildDetail("Claupet", 27644));
        Assert.Equal("servidor de Claupet · pid 1", TerminalPane.ChildDetail("claupet", 1));
        Assert.Equal("pid 512", TerminalPane.ChildDetail("dotnet", 512));
    }
}
