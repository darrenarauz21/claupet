using System.Diagnostics;
using System.Text;

namespace Claupet;

/// <summary>
/// Un canvas en PDF: la misma página HTML de la exportación (CanvasExport.Html), impresa por Microsoft Edge o Google
/// Chrome sin ventana (--headless --print-to-pdf). Así el PDF trae lo mismo que se ve en el navegador: el Markdown con
/// su formato, el código, las tablas, el HTML de las secciones y los diagramas Mermaid dibujados. Edge viene con
/// Windows; en macOS y Linux sirve cualquiera de los dos, o Chromium.
/// </summary>
static class CanvasPdf
{
    const int TimeoutSeconds = 90;

    public static void Write(CanvasDoc doc, string path)
    {
        var browser = FindBrowser() ?? throw new CanvasException(L.T("Para exportar a PDF hace falta Microsoft Edge, Google Chrome o Chromium."));
        var work = Path.Combine(Path.GetTempPath(), "claupet-pdf-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(work);
        var page = Path.Combine(work, "canvas.html");
        var output = Path.Combine(work, "canvas.pdf");
        try
        {
            File.WriteAllText(page, CanvasExport.Html(doc), new UTF8Encoding(false));
            var start = new ProcessStartInfo(browser)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            // Un perfil aparte: así no choca con el navegador que la persona tenga abierto ni toca el suyo.
            foreach (var arg in new[]
            {
                "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions",
                "--user-data-dir=" + Path.Combine(work, "perfil"), "--no-pdf-header-footer", "--virtual-time-budget=8000",
                "--print-to-pdf=" + output, new Uri(page).AbsoluteUri,
            })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new CanvasException(L.T("No se pudo abrir {0}.", Path.GetFileName(browser)));
            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeoutSeconds * 1000))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw new CanvasException(L.T("El PDF tardó demasiado."));
            }
            if (!File.Exists(output) || new FileInfo(output).Length == 0) throw new CanvasException(L.T("No se pudo crear el PDF."));
            File.Copy(output, path, overwrite: true);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            throw new CanvasException(L.T("No se pudo crear el PDF: {0}", ex.Message));
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Edge, Chrome o Chromium, donde suelen instalarse; null si no hay ninguno.</summary>
    public static string? FindBrowser()
    {
        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.LocalApplicationData })
            {
                var dir = Environment.GetFolderPath(root);
                if (dir.Length == 0) continue;
                candidates.Add(Path.Combine(dir, "Microsoft", "Edge", "Application", "msedge.exe"));
                candidates.Add(Path.Combine(dir, "Google", "Chrome", "Application", "chrome.exe"));
                candidates.Add(Path.Combine(dir, "Chromium", "Application", "chrome.exe"));
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            candidates.Add("/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge");
            candidates.Add("/Applications/Google Chrome.app/Contents/MacOS/Google Chrome");
            candidates.Add("/Applications/Chromium.app/Contents/MacOS/Chromium");
        }
        else
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var name in new[] { "microsoft-edge", "microsoft-edge-stable", "google-chrome", "google-chrome-stable", "chromium", "chromium-browser" })
                foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                    candidates.Add(Path.Combine(dir, name));
        }
        return candidates.FirstOrDefault(File.Exists);
    }
}
