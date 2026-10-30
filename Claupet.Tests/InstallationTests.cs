using Microsoft.Win32;

namespace Claupet.Tests;

/// <summary>
/// Instalar Claupet en su carpeta y su entrada en Configuración › Aplicaciones (en una clave de prueba de HKCU, no en la
/// real), y los términos y condiciones del asistente.
/// </summary>
public sealed class InstallationTests : IDisposable
{
    const string TestRoot = @"Software\Claupet-pruebas";
    readonly string _realKey = Installation.KeyPath;
    readonly string _testKey = TestRoot + @"\" + Guid.NewGuid().ToString("N");
    readonly TempDir _dir = new();

    public InstallationTests() => Installation.KeyPath = _testKey;

    public void Dispose()
    {
        Installation.KeyPath = _realKey;
        Registry.CurrentUser.DeleteSubKeyTree(_testKey, throwOnMissingSubKey: false);
        using (var root = Registry.CurrentUser.OpenSubKey(TestRoot))
            if (root is { SubKeyCount: 0, ValueCount: 0 }) Registry.CurrentUser.DeleteSubKey(TestRoot, throwOnMissingSubKey: false);
        _dir.Dispose();
    }

    string Download(string text = "versión 1")
    {
        var file = _dir.Write("Descargas/Claupet-windows-x64.exe", text);
        File.SetLastWriteTimeUtc(file, new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc));
        return file;
    }

    [Fact]
    public void LaCarpetaElegidaLlevaSuPropiaCarpetaClaupet()
    {
        Assert.Equal(@"D:\Apps\Claupet", Installation.FolderFor(@"D:\Apps"));
        Assert.Equal(@"D:\Apps\Claupet", Installation.FolderFor(@"D:\Apps\"));
        Assert.Equal(@"D:\Apps\claupet", Installation.FolderFor(@"D:\Apps\claupet"));
        Assert.Equal(@"D:\Claupet", Installation.FolderFor(@"D:\"));
        Assert.EndsWith(@"\Programs\Claupet", Installation.DefaultFolder);
    }

    [Fact]
    public void SoloSirveUnaCarpetaDeEsteEquipoDondeSePuedaEscribir()
    {
        var nueva = _dir["Programas/Claupet"];
        Assert.Null(Installation.Problem(nueva));
        Assert.False(Directory.Exists(nueva)); // probar no crea nada

        Assert.NotNull(Installation.Problem(@"\\servidor\apps\Claupet"));
        Assert.NotNull(Installation.Problem(_dir["100%/Claupet"]));
        Assert.NotNull(Installation.Problem(_dir.Write("archivo", "")));
    }

    [Fact]
    public void InstalarCopiaElExeYLoRegistra()
    {
        var download = Download();
        var folder = _dir["Programas/Claupet"];
        Assert.Null(Installation.Folder);

        var exe = Installation.Install(download, folder, "2.4.0");

        Assert.Equal(Path.Combine(folder, "Claupet.exe"), exe);
        Assert.Equal("versión 1", File.ReadAllText(exe));
        Assert.True(File.Exists(download)); // lo descargado queda donde estaba
        Assert.Equal(File.GetLastWriteTimeUtc(download), File.GetLastWriteTimeUtc(exe));
        Assert.Equal(folder, Installation.Folder);
        Assert.Equal(exe, Installation.InstalledExe);
        Assert.True(Installation.IsInstalled(exe));
        Assert.False(Installation.IsInstalled(download));

        using var key = Registry.CurrentUser.OpenSubKey(_testKey)!;
        Assert.Equal("Claupet", key.GetValue("DisplayName"));
        Assert.Equal("2.4.0", key.GetValue("DisplayVersion"));
        Assert.Equal(folder, key.GetValue("InstallLocation"));
        Assert.Equal($"\"{exe}\" --desinstalar", key.GetValue("UninstallString"));
        Assert.Equal(Path.Combine(folder, "claupet.ico"), key.GetValue("DisplayIcon"));
        Assert.True(File.Exists(Path.Combine(folder, "claupet.ico")));
        Assert.Equal(1, key.GetValue("NoModify"));
    }

    [Fact]
    public void OtraVersionReemplazaALaInstalada()
    {
        var folder = _dir["Programas/Claupet"];
        var exe = Installation.Install(Download("versión 1"), folder, "2.4.0");

        Installation.Install(_dir.Write("Descargas/nueva.exe", "versión 2"), folder, "2.5.0");

        Assert.Equal("versión 2", File.ReadAllText(exe));
        // La de antes queda al lado, como en una actualización: se borra al abrir la nueva (Updates.CleanUp).
        Assert.Equal("versión 1", File.ReadAllText(exe + UpdateInstaller.Previous));
        using var key = Registry.CurrentUser.OpenSubKey(_testKey)!;
        Assert.Equal("2.5.0", key.GetValue("DisplayVersion"));
    }

    [Fact]
    public void LaInstaladaPoneAlDiaSuVersionAlAbrirse()
    {
        var exe = Installation.Install(Download(), _dir["Programas/Claupet"], "2.4.0");

        Installation.Refresh(exe, "2.5.0");
        using (var key = Registry.CurrentUser.OpenSubKey(_testKey)!)
            Assert.Equal("2.5.0", key.GetValue("DisplayVersion"));

        // Otra copia (sin instalar) no toca la entrada.
        Installation.Refresh(_dir.Write("otra/Claupet.exe", "x"), "9.9.9");
        using (var key = Registry.CurrentUser.OpenSubKey(_testKey)!)
            Assert.Equal("2.5.0", key.GetValue("DisplayVersion"));
    }

    [Fact]
    public void LoDescargadoDistintoDeLaInstaladaNoLaAbre()
    {
        Installation.Install(Download("versión 1"), _dir["Programas/Claupet"], "2.4.0");
        // Otra compilación u otra versión (otro archivo): sigue la que se abrió, y el asistente ofrece reemplazar.
        Assert.False(Installation.OpenInstalled(_dir.Write("Descargas/otra.exe", "versión 2")));
        // Sin instalar, tampoco.
        Installation.KeyPath = _testKey + "-vacia";
        Assert.False(Installation.OpenInstalled(Download()));
    }

    [Fact]
    public async Task AlDesinstalarSeBorraSoloLoQuePusoLaInstalacion()
    {
        // Con espacios y «&», que cmd leería como otro comando si la ruta no fuera entre comillas.
        var folder = _dir["Mis apps & más/Claupet"];
        var exe = Installation.Install(Download(), folder, "2.4.0");
        File.WriteAllText(exe + UpdateInstaller.Previous, "antes");
        var otra = _dir.Write("Mis apps & más/Claupet-otra/Claupet.exe", "de otra carpeta");

        Installation.ScheduleRemoval(folder);
        for (var i = 0; i < 100 && Directory.Exists(folder); i++) await Task.Delay(100);

        Assert.False(Directory.Exists(folder));
        Assert.True(File.Exists(otra));
    }

    [Fact]
    public async Task UnaCarpetaConOtrosArchivosNoSeBorra()
    {
        var folder = _dir["Programas/Claupet"];
        var exe = Installation.Install(Download(), folder, "2.4.0");
        var mine = _dir.Write("Programas/Claupet/notas.txt", "de la persona");

        Installation.ScheduleRemoval(folder);
        for (var i = 0; i < 100 && File.Exists(exe); i++) await Task.Delay(100);

        Assert.False(File.Exists(exe));
        Assert.False(File.Exists(Path.Combine(folder, "claupet.ico")));
        Assert.True(File.Exists(mine));
    }

    [Fact]
    public void SinInstalarElDesinstaladorSoloAvisa()
    {
        var told = new List<string>();
        var code = Installation.RunUninstaller(_ => throw new InvalidOperationException("no pregunta"), told.Add);
        Assert.Equal(1, code);
        Assert.Single(told);
    }

    [Theory]
    [InlineData("es", "TÉRMINOS Y CONDICIONES")]
    [InlineData("en", "TERMS AND CONDITIONS")]
    public void LosTerminosVanEnCadaIdioma(string language, string title)
    {
        // Sin L.Use: otras pruebas, en paralelo, cuentan con el español.
        var text = Terms.Read(language);
        Assert.NotNull(text);
        Assert.Contains(title, text.Split('\n')[0]);
        Assert.Contains("MIT", text);
        Assert.Contains("Version " + Terms.Version, text.Replace("Versión", "Version"));
    }

    [Fact]
    public void LosTerminosSeAceptanPorVersion()
    {
        var settings = new AppSettings();
        Assert.False(Terms.Accepted(settings));
        settings.TermsAccepted = Terms.Version;
        Assert.True(Terms.Accepted(settings));
        settings.TermsAccepted = "0"; // otra versión: hay que aceptarlos de nuevo
        Assert.False(Terms.Accepted(settings));
    }
}
