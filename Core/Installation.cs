using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Claupet;

/// <summary>
/// Claupet instalada como cualquier app de Windows, sin permisos de administrador: Claupet.exe en una carpeta propia
/// (DefaultFolder, o la que elija la persona en el asistente) y una entrada en Configuración › Aplicaciones
/// (HKCU\…\Uninstall\Claupet) con su desinstalador, «Claupet.exe --desinstalar». El .exe descargado se copia ahí y se
/// abre desde ahí; los hooks, el inicio con Windows, los accesos directos y los recordatorios apuntan a la copia
/// instalada, y los ajustes y los modelos siguen en la carpeta de datos (DataFolder). Las versiones nuevas (Updates)
/// cambian el .exe en esa carpeta: por eso tiene que poder escribirse sin administrador.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
static class Installation
{
    public const string ExeName = "Claupet.exe";
    public const string UninstallFlag = "--desinstalar";
    const string AppFolder = "Claupet";
    const string IconName = "claupet.ico";
    const string Staged = ".nuevo";
    const string Repository = "https://github.com/darrenarauz21/claupet";

    /// <summary>La entrada de Configuración › Aplicaciones, en HKCU (las pruebas usan otra).</summary>
    internal static string KeyPath { get; set; } = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Claupet";

    /// <summary>%LOCALAPPDATA%\Programs\Claupet: donde van las apps instaladas para un solo usuario.</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppFolder);

    /// <summary>La carpeta donde quedó instalada (InstallLocation de su entrada); null si no está instalada.</summary>
    public static string? Folder
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue("InstallLocation") is string { Length: > 0 } folder ? folder : null;
        }
    }

    /// <summary>El .exe instalado, si está.</summary>
    public static string? InstalledExe => Folder is { } folder && File.Exists(Path.Combine(folder, ExeName)) ? Path.Combine(folder, ExeName) : null;

    /// <summary>Esta copia es la instalada.</summary>
    public static bool IsInstalled(string exe) => InstalledExe is { } installed && Account.SamePath(installed, exe);

    /// <summary>
    /// Si este .exe ya se instaló (la copia instalada es el mismo archivo: mismo tamaño y misma fecha, que la copia
    /// conserva), abre la instalada y devuelve true: así abrir otra vez lo descargado no vuelve a instalar. Otro .exe
    /// (otra versión, o una compilación nueva) sigue: el asistente ofrece reemplazar la instalada.
    /// </summary>
    public static bool OpenInstalled(string exe)
    {
        try
        {
            if (InstalledExe is not { } installed || Account.SamePath(installed, exe)) return false;
            FileInfo mine = new(exe), theirs = new(installed);
            if (mine.Length != theirs.Length || mine.LastWriteTimeUtc != theirs.LastWriteTimeUtc) return false;
            Process.Start(new ProcessStartInfo(installed) { UseShellExecute = false })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            ErrorLog.Write(ex);
            return false;
        }
    }

    /// <summary>
    /// La carpeta para lo elegido: si no se llama Claupet, una «Claupet» dentro, como hace cualquier instalador; así la
    /// app no queda suelta entre otros archivos.
    /// </summary>
    public static string FolderFor(string chosen)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(chosen));
        return string.Equals(Path.GetFileName(full), AppFolder, StringComparison.OrdinalIgnoreCase) ? full : Path.Combine(full, AppFolder);
    }

    /// <summary>
    /// Por qué no se puede instalar ahí; null si se puede. Tiene que ser una carpeta de este equipo donde se pueda escribir
    /// sin administrador: se prueba en ella o, si todavía no existe, en la primera que exista hacia arriba (sin crear nada).
    /// Sin «%»: la limpieza al desinstalar pasa la ruta por cmd, que lo leería como una variable.
    /// </summary>
    public static string? Problem(string folder)
    {
        string full;
        try { full = Path.GetFullPath(folder); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return L.T("Esa ruta no es válida."); }
        if (full.StartsWith(@"\\", StringComparison.Ordinal)) return L.T("Elija una carpeta de este equipo, no una de la red.");
        if (full.Contains('%')) return L.T("Elija una ruta sin «%».");
        if (File.Exists(full)) return L.T("Ya hay un archivo con ese nombre.");
        var probe = full;
        while (!Directory.Exists(probe))
            if (Path.GetDirectoryName(probe) is { } parent) probe = parent;
            else return L.T("Esa ruta no es válida.");
        try
        {
            var file = Path.Combine(probe, $".claupet-{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(file, []);
            File.Delete(file);
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return L.T("Claupet no puede escribir ahí sin permisos de administrador. Elija otra carpeta: las versiones nuevas se instalan solas en ella.");
        }
    }

    /// <summary>
    /// Copia este .exe a la carpeta (si no está ya ahí), le quita la marca de «descargado de internet» (la persona ya
    /// aceptó abrirlo; cualquier instalador deja así lo que copia) y lo registra en Configuración › Aplicaciones. Si en la
    /// carpeta hay otra versión, se cambia como en una actualización: la de antes queda con «.anterior» y se borra al
    /// abrir la nueva. Devuelve el .exe instalado.
    /// </summary>
    public static string Install(string source, string folder, string version)
    {
        folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, ExeName);
        if (!Account.SamePath(source, target))
        {
            var staged = target + Staged;
            File.Copy(source, staged, overwrite: true);
            DeleteFile(staged + ":Zone.Identifier");
            File.SetLastWriteTimeUtc(staged, File.GetLastWriteTimeUtc(source)); // el mismo archivo (OpenInstalled)
            if (File.Exists(target)) UpdateInstaller.Swap(target, staged);
            else File.Move(staged, target);
        }
        Register(target, version);
        return target;
    }

    /// <summary>La entrada de Configuración › Aplicaciones: nombre, versión, ícono, carpeta, tamaño y cómo se desinstala.</summary>
    public static void Register(string exe, string version)
    {
        var folder = Path.GetDirectoryName(exe)!;
        var icon = Path.Combine(folder, IconName);
        try { Shortcuts.WriteIcon(icon); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue("DisplayName", "Claupet");
        key.SetValue("DisplayVersion", version);
        key.SetValue("DisplayIcon", File.Exists(icon) ? icon : exe);
        key.SetValue("InstallLocation", folder);
        key.SetValue("UninstallString", $"\"{exe}\" {UninstallFlag}");
        key.SetValue("URLInfoAbout", Repository);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
        if (key.GetValue("InstallDate") is null) key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
    }

    /// <summary>Al abrir la copia instalada: la versión y el tamaño de su entrada, al día (cambian con cada actualización).</summary>
    public static void Refresh(string exe, string version)
    {
        try
        {
            if (!IsInstalled(exe)) return;
            using (var key = Registry.CurrentUser.OpenSubKey(KeyPath))
                if (key?.GetValue("DisplayVersion") as string == version) return;
            Register(exe, version);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { ErrorLog.Write(ex); }
    }

    /// <summary>
    /// «Claupet.exe --desinstalar», desde Configuración › Aplicaciones: pregunta, cierra Claupet, quita los hooks de la app
    /// (con respaldo), el inicio con Windows, los accesos directos, los recordatorios pendientes y la entrada; si la
    /// persona quiere, también los ajustes y los modelos. El .exe lo borra cmd cuando este proceso termina (uno no puede
    /// borrarse a sí mismo): solo lo que puso la instalación, y la carpeta solo si queda vacía. ask y tell muestran los
    /// mensajes (sí/no y aceptar).
    /// </summary>
    public static int RunUninstaller(Func<string, bool> ask, Action<string> tell)
    {
        if (Folder is not { } folder)
        {
            tell(L.T("Claupet no está instalada en este equipo."));
            return 1;
        }
        if (!ask(L.T("¿Desinstalar Claupet de {0}?", folder) + "\n\n"
            + L.T("Se quitan los hooks de Claupet del settings.json de cada carpeta de Claude Code (con respaldo), el inicio con Windows, los accesos directos y los recordatorios pendientes.")))
            return 1;
        var data = DataFolder.Root;
        var removeData = Directory.Exists(data) && ask(L.T("¿Borrar también sus ajustes, su historial y los modelos descargados ({0} MB en {1})?", Megabytes(data), data)
            + "\n\n" + L.T("Si los conserva, Claupet los vuelve a usar si la instala de nuevo."));

        var exe = Path.Combine(folder, ExeName);
        CloseOthers(folder);
        var problems = new List<string>();
        var settings = SettingsStore.Load();
        foreach (var dir in HookInstaller.ConfigDirs(settings.Accounts.Select(a => a.ConfigDir)))
        {
            if (HookInstaller.Status(dir, exe) is not (HookInstaller.State.Installed or HookInstaller.State.Outdated)
                && HookInstaller.ApprovalsStatus(dir, exe) is not (HookInstaller.State.Installed or HookInstaller.State.Outdated))
                continue;
            try { HookInstaller.Uninstall(dir); }
            catch (Exception ex) { problems.Add(L.T("{0}: no se pudo quitar ({1}).", dir.Replace(Account.Home, "~"), ex.Message)); }
        }
        try { StartupRegistration.Set(false); }
        catch (Exception ex) { problems.Add(L.T("Inicio con Windows: no se pudo quitar ({0}).", ex.Message)); }
        Shortcuts.Set(Shortcuts.StartMenu, false);
        Shortcuts.Set(Shortcuts.Desktop, false);
        foreach (var id in ReminderStore.PendingIds()) ReminderTasks.Delete(id);
        Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
        if (removeData)
        {
            try { Directory.Delete(data, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problems.Add(L.T("No se pudo borrar todo {0} ({1}).", data, ex.Message)); }
        }

        var done = L.T("Claupet se desinstaló.");
        if (!removeData && Directory.Exists(data)) done += "\n\n" + L.T("Sus ajustes quedaron en {0}.", data);
        if (problems.Count > 0) done += "\n\n" + string.Join("\n", problems);
        tell(done);
        // Después del último mensaje: cmd espera unos segundos a que este proceso termine.
        ScheduleRemoval(folder);
        return 0;
    }

    /// <summary>Cierra los Claupet que corren desde la carpeta instalada (la bandeja y Claupet Console), salvo este.</summary>
    static void CloseOthers(string folder)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || process.MainModule?.FileName is not { } file
                        || !Account.SamePath(Path.GetDirectoryName(file)!, folder))
                        continue;
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { ErrorLog.Write(ex); }
            }
    }

    /// <summary>Borra, cuando este proceso ya terminó, lo que puso la instalación y la carpeta si quedó vacía.</summary>
    internal static void ScheduleRemoval(string folder)
    {
        var files = new[] { ExeName, ExeName + Staged, ExeName + UpdateInstaller.Previous, IconName }
            .Select(name => $"del /f /q \"{Path.Combine(folder, name)}\" 2>nul");
        var command = "ping -n 4 127.0.0.1 >nul & " + string.Join(" & ", files) + $" & rmdir \"{folder}\" 2>nul";
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/d /c " + command)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath(),
            })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { ErrorLog.Write(ex); }
    }

    static long Megabytes(string folder)
    {
        try { return (new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) + 500_000) / 1_000_000; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "DeleteFileW")]
    static extern bool DeleteFile(string path);
}
