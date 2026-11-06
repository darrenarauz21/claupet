using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace Claupet;

/// <summary>
/// Lo que las teclas multimedia no tienen: volver al inicio de la canción y repetirla. Lo hace el control multimedia de
/// Windows (GlobalSystemMediaTransportControls), con PowerShell porque .NET no lo trae sin el SDK de Windows, que pesa decenas
/// de MB (como WindowsLocation). Depende del reproductor: cada uno dice qué deja hacer desde fuera.
/// </summary>
[SupportedOSPlatform("windows")]
static class WinMedia
{
    /// <summary>Lo que hizo: lo pedido; no hay reproductor; el reproductor no lo deja, o no se pudo preguntar.</summary>
    public enum Outcome { Done, Nothing, Refused, Failed }

    const string Script = """
        $ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $as = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
        function Wait($op, $type) { $t = $as.MakeGenericMethod($type).Invoke($null, @($op)); [void]$t.Wait(4000); $t.Result }
        $M = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType=WindowsRuntime]
        $s = (Wait ($M::RequestAsync()) $M).GetCurrentSession()
        if (-not $s) { 'nada'; return }
        $c = $s.GetPlaybackInfo().Controls
        $done = switch ($accion) {
          'inicio' { $c.IsPlaybackPositionEnabled -and (Wait ($s.TryChangePlaybackPositionAsync(0)) ([bool])) }
          'repetir' { $c.IsRepeatEnabled -and (Wait ($s.TryChangeAutoRepeatModeAsync('Track')) ([bool])) }
          'sin-repetir' { $c.IsRepeatEnabled -and (Wait ($s.TryChangeAutoRepeatModeAsync('None')) ([bool])) }
        }
        if ($done) { 'hecho' } else { 'no' }
        """;

    /// <summary>La acción («inicio», «repetir» o «sin-repetir») en el reproductor actual.</summary>
    public static async Task<Outcome> RunAsync(string action)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var command = Convert.ToBase64String(Encoding.Unicode.GetBytes($"$accion = '{action}'\n{Script}"));
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", command }) start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start);
            if (process is null) return Outcome.Failed;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var kill = timeout.Token.Register(() =>
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
            });
            // Se lee también el error: si se llena sin leerlo, el proceso se queda esperando.
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await errors;
            await process.WaitForExitAsync(timeout.Token);
            return output.Trim() switch
            {
                "hecho" => Outcome.Done,
                "nada" => Outcome.Nothing,
                "no" => Outcome.Refused,
                _ => Outcome.Failed,
            };
        }
        catch (Exception ex) when (ex is OperationCanceledException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Outcome.Failed;
        }
    }
}
