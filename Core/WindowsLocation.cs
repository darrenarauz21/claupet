using System.Diagnostics;
using System.Globalization;

namespace Claupet;

/// <summary>
/// La ubicación de Windows (Configuración › Privacidad › Ubicación), para el clima de «Mi ciudad». La lee
/// PowerShell con System.Device.Location: .NET no la trae sin el SDK de Windows, que pesa decenas de MB.
/// Tarda un par de segundos; WeatherService la guarda 6 horas. null si no hay permiso o no responde.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
static class WindowsLocation
{
    const string Script = """
        Add-Type -AssemblyName System.Device
        $w = New-Object System.Device.Location.GeoCoordinateWatcher
        if ($w.TryStart($false, [TimeSpan]::FromSeconds(8))) {
          for ($i = 0; $i -lt 40 -and $w.Status -ne 'Ready'; $i++) { Start-Sleep -Milliseconds 250 }
          $l = $w.Position.Location
          if (-not $l.IsUnknown) { [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0};{1}', $l.Latitude, $l.Longitude) }
        }
        $w.Dispose()
        """;

    public static async Task<(double Lat, double Lon)?> GetAsync(CancellationToken cancel)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", Script }) start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start);
            if (process is null) return null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var kill = timeout.Token.Register(() =>
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
            });
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var parts = output.Trim().Split(';');
            return parts.Length == 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
                ? (lat, lon) : null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
