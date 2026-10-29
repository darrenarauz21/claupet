using System.Diagnostics;

namespace Claupet;

/// <summary>Abre un archivo, una carpeta o una dirección web con el programa del sistema (Windows, macOS o Linux).</summary>
static class Shell
{
    public static void Open(string target)
    {
        try
        {
            var start = OperatingSystem.IsWindows() ? new ProcessStartInfo(target) { UseShellExecute = true }
                : new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { ArgumentList = { target }, UseShellExecute = false };
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException) { ErrorLog.Write(ex); }
    }
}
