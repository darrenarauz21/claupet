using System.Security;
using Microsoft.Win32;

namespace Claupet;

/// <summary>
/// Iniciar con el sistema, sin permisos de administrador: en Windows una entrada en
/// HKCU\...\Run, en Linux un .desktop en ~/.config/autostart y en macOS un LaunchAgent. Las entradas
/// con el nombre de antes de Claupet (ClaudeUsageTray, uso-de-claude) cuentan como encendidas y se
/// quitan al cambiarlo.
/// </summary>
static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "Claupet";
    const string MacLabel = "local.claupet";
    const string OldRunValue = "ClaudeUsageTray";
    const string OldMacLabel = "local.usodeclaude";

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static string LinuxFile => Path.Combine(Home, ".config", "autostart", "claupet.desktop");
    static string MacFile => Path.Combine(Home, "Library", "LaunchAgents", MacLabel + ".plist");
    static string OldLinuxFile => Path.Combine(Home, ".config", "autostart", "uso-de-claude.desktop");
    static string OldMacFile => Path.Combine(Home, "Library", "LaunchAgents", OldMacLabel + ".plist");
    static string Exe => Environment.ProcessPath ?? "";

    public static bool IsEnabled
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(RunValue) is string || key?.GetValue(OldRunValue) is string;
            }
            return OperatingSystem.IsMacOS() ? File.Exists(MacFile) || File.Exists(OldMacFile)
                : File.Exists(LinuxFile) || File.Exists(OldLinuxFile);
        }
    }

    /// <summary>Si quedó la entrada con el nombre de antes (apunta al .exe viejo), la cambia por la de este.</summary>
    public static void Migrate()
    {
        bool old;
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            old = key?.GetValue(OldRunValue) is string;
        }
        else old = File.Exists(OperatingSystem.IsMacOS() ? OldMacFile : OldLinuxFile);
        if (old) Set(true);
    }

    public static void Set(bool enabled)
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.DeleteValue(OldRunValue, throwOnMissingValue: false);
            if (enabled) key.SetValue(RunValue, $"\"{Exe}\"");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
            return;
        }

        var old = OperatingSystem.IsMacOS() ? OldMacFile : OldLinuxFile;
        if (File.Exists(old)) File.Delete(old);
        var file = OperatingSystem.IsMacOS() ? MacFile : LinuxFile;
        if (!enabled)
        {
            if (File.Exists(file)) File.Delete(file);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, OperatingSystem.IsMacOS() ? LaunchAgent() : DesktopEntry());
    }

    static string DesktopEntry() =>
        "[Desktop Entry]\n"
        + "Type=Application\n"
        + "Name=Claupet\n"
        + $"Exec=\"{Exe}\"\n"
        + "Icon=claupet\n"
        + "X-GNOME-Autostart-enabled=true\n";

    static string LaunchAgent() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
        + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
        + "<plist version=\"1.0\">\n<dict>\n"
        + $"  <key>Label</key><string>{MacLabel}</string>\n"
        + $"  <key>ProgramArguments</key><array><string>{SecurityElement.Escape(Exe)}</string></array>\n"
        + "  <key>RunAtLoad</key><true/>\n"
        + "  <key>ProcessType</key><string>Interactive</string>\n"
        + "</dict>\n</plist>\n";
}
