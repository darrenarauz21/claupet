using System.Runtime.CompilerServices;

namespace Claupet.Tests;

/// <summary>Un escritorio que no toca el equipo: anota lo que se le pide y devuelve lo que se le diga.</summary>
sealed class FakeDesktop(IEnumerable<string> kinds, string? folders = null) : IDesktop
{
    /// <summary>Todas las acciones: es el de todas las pruebas (el prompt las ofrece como en Windows).</summary>
    public static readonly FakeDesktop Everything = new(SystemActions.All);

    public readonly List<string> Calls = new();
    public IReadOnlySet<string> Kinds { get; } = kinds.ToHashSet();

    string? Did(string call)
    {
        lock (Calls) Calls.Add(call);
        return null;
    }

    public string? SetWallpaper(string file) => Did($"fondo {file}");
    public string? SetDarkMode(bool dark) => Did($"oscuro {dark}");
    public string? Volume(VolumeChange change) => Did($"volumen {change.Percent} {change.Step} {change.Mute}");
    public Task<string?> MediaAsync(MediaCommand command) => Task.FromResult(Did($"musica {command}"));
    public IReadOnlyList<string> Schemes { get; } = ["https", "http"];
    public string? FindApp(string name) => name == "Calculadora" ? "app:calculadora" : null;
    public void Start(string target) => Did($"abrir {target}");
    public string? OpenCalendarFile(string file) => Did("calendario");
    public Task<(string? File, string? Problem)> CaptureAsync(bool crop) => Task.FromResult<(string?, string?)>((null, crop ? null : "sin pantalla"));
    public string? Window(WindowVerb verb, string app) => Did($"ventana {verb} {app}");
    public string? ShowDesktop() => Did("escritorio");
    public string? Computer(ComputerCommand command) => Did($"equipo {command}");
    public string? Brightness(int? percent, int step) => Did($"brillo {percent} {step}");
    public string Battery() => "Batería: 50 %.";
    public string Memory() => "Memoria: 1 GB.";
    public IEnumerable<DriveInfo> Disks() => [];
    public Task<string?> CopyTextAsync(string text) => Task.FromResult(Did($"copiar {text}"));
    public Task<(string? Text, string? ImageFile)> ReadClipboardAsync() => Task.FromResult<(string?, string?)>(("copiado", null));
    public string? Folder(KnownFolder folder) => folders is null ? null : Path.Combine(folders, folder.ToString());
    public string ScreenshotsFolder => Path.Combine(folders ?? Path.GetTempPath(), "Capturas");
    public void OpenFolder(string path) => Did($"carpeta {path}");
    public void ScheduleReminder(Reminder reminder) => Did("recordar");
    public void CancelReminder(string id) => Did("cancelar");
}

static class TestSetup
{
    /// <summary>Las pruebas corren como en Windows: con todas las acciones, pero sin tocar el equipo.</summary>
#pragma warning disable CA2255 // en las pruebas es justo lo que hace falta
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Init() => SystemActions.Desktop = FakeDesktop.Everything;
}

/// <summary>Lo que entiende SystemActions de cada pedido, igual en los tres sistemas; lo que hace, lo hace el escritorio.</summary>
public sealed class SystemActionsTests
{
    static async Task<(ActionOutcome Outcome, List<string> Calls)> Run(string kind, string value, IEnumerable<string>? kinds = null, string? folders = null)
    {
        var desktop = new FakeDesktop(kinds ?? SystemActions.All, folders);
        var outcome = await SystemActions.RunAsync(new AssistantAction(kind, value), _ => { }, desktop);
        return (outcome, desktop.Calls);
    }

    [Theory]
    [InlineData("subir", "volumen  10 ")]
    [InlineData("bajar", "volumen  -10 ")]
    [InlineData("150%", "volumen 100 0 ")]
    [InlineData("silenciar", "volumen  0 True")]
    [InlineData("unmute", "volumen  0 False")]
    public async Task ElVolumenSeEntiendeIgualEnTodosLosSistemas(string value, string call)
    {
        var (outcome, calls) = await Run("volumen", value);
        Assert.Null(outcome.Problem);
        Assert.Equal([call], calls);
    }

    [Theory]
    [InlineData("música", "pausar", "musica Pause")]
    [InlineData("musica", "next", "musica Next")]
    [InlineData("ventana", "cerrar «Word»", "ventana Close Word")]
    [InlineData("ventana", "traer Firefox", "ventana Focus Firefox")]
    [InlineData("escritorio", "", "escritorio")]
    [InlineData("brillo", "bajar", "brillo  -10")]
    [InlineData("brillo", "40", "brillo 40 0")]
    [InlineData("equipo", "bloquear", "equipo Lock")]
    [InlineData("tema", "oscuro", "oscuro True")]
    [InlineData("abrir", "Calculadora", "abrir app:calculadora")]
    [InlineData("abrir", "https://claude.ai", "abrir https://claude.ai")]
    public async Task CadaPedidoLlegaAlEscritorio(string kind, string value, string call)
    {
        var (outcome, calls) = await Run(kind, value);
        Assert.Null(outcome.Problem);
        Assert.Equal([call], calls);
    }

    [Fact]
    public async Task LoQueEsteSistemaNoHaceSeDiceSinTocarNada()
    {
        var (outcome, calls) = await Run("brillo", "subir", kinds: ["volumen"]);
        Assert.Equal(L.T("Eso todavía no lo sé hacer en este sistema."), outcome.Problem);
        Assert.Empty(calls);
        var (unknown, _) = await Run("bailar", "x");
        Assert.Equal(L.T("Eso todavía no lo sé hacer."), unknown.Problem);
    }

    [Fact]
    public async Task UnEnlaceDeOtroTipoNoSeAbre()
    {
        var (outcome, calls) = await Run("abrir", "ms-settings:display");
        Assert.Equal(L.T("Ese tipo de enlace no lo abro."), outcome.Problem);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task AbrirUnaCarpetaConocidaEsCarpeta()
    {
        using var dir = new TempDir();
        var (outcome, calls) = await Run("abrir", "descargas", folders: dir.Path);
        Assert.Null(outcome.Problem);
        Assert.Equal([$"carpeta {Path.Combine(dir.Path, "Downloads")}"], calls);
        Assert.True(Directory.Exists(Path.Combine(dir.Path, "Downloads")));
    }

    [Fact]
    public async Task ElEstadoYElPortapapelesVuelvenComoDatos()
    {
        var (status, _) = await Run("estado", "todo");
        Assert.Equal("Batería: 50 %.\nMemoria: 1 GB.", status.Data);
        var (clipboard, _) = await Run("portapapeles", "leer");
        Assert.Equal("copiado", clipboard.Data);
    }

    [Fact]
    public async Task UnaCapturaQueFallaDiceQue()
    {
        var (failed, _) = await Run("captura", "pantalla");
        Assert.Equal("sin pantalla", failed.Problem);
        var (canceled, _) = await Run("captura", "recorte");
        Assert.Equal(L.T("Recorte cancelado."), canceled.Problem);
    }

    [Fact]
    public void ReiniciarYApagarSeConfirmanSiempre()
    {
        Assert.NotNull(SystemActions.Confirmation(new AssistantAction("equipo", "reiniciar")));
        Assert.Null(SystemActions.Confirmation(new AssistantAction("equipo", "bloquear")));
        Assert.Equal(Capabilities.Windows, SystemActions.CapabilityOf(new AssistantAction("escritorio", "")));
    }

    [Fact]
    public void UnaImagenBajadaTieneQueEmpezarComoImagen()
    {
        using var dir = new TempDir();
        var png = dir["a.png"];
        File.WriteAllBytes(png, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0]);
        var html = dir["b.png"];
        File.WriteAllText(html, "<!doctype html><html>");
        Assert.True(SystemActions.LooksLikeImage(png, ".png"));
        Assert.False(SystemActions.LooksLikeImage(html, ".png"));
    }
}
