using System.Diagnostics;
using System.Text;

namespace Claupet;

/// <summary>
/// Un canvas en PDF, impreso por Microsoft Edge o Google Chrome sin ventana (--headless --print-to-pdf): el Markdown con
/// su formato, el código, las tablas y los diagramas Mermaid dibujados, como en la página HTML de la exportación. Cada
/// sección HTML o SVG va aparte, como su propia página (así se reparte en hojas entera y con sus colores; en un iframe
/// salía cortada o vacía), y PdfJoin une las partes en orden. Edge viene con Windows; en macOS y Linux sirve cualquiera
/// de los dos, o Chromium.
/// </summary>
static class CanvasPdf
{
    const int TimeoutSeconds = 90;

    public static void Write(CanvasDoc doc, string path)
    {
        var browser = FindBrowser() ?? throw new CanvasException(L.T("Para exportar a PDF hace falta Microsoft Edge, Google Chrome o Chromium."));
        var work = Path.Combine(Path.GetTempPath(), "claupet-pdf-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(work);
        try
        {
            var parts = Parts(doc);
            if (parts.Count == 1)
            {
                File.Copy(Print(browser, work, parts[0], 0), path, overwrite: true);
                return;
            }
            // Cada parte es un navegador aparte: de a tres a la vez.
            var files = new string[parts.Count];
            try { Parallel.For(0, parts.Count, new ParallelOptions { MaxDegreeOfParallelism = 3 }, i => files[i] = Print(browser, work, parts[i], i)); }
            catch (AggregateException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.Flatten().InnerExceptions[0]).Throw(); }
            byte[] joined;
            try { joined = PdfJoin.Join(files.Select(File.ReadAllBytes).ToList()); }
            catch (PdfJoinException ex)
            {
                // Otra forma de PDF (otro navegador): todo en una página, con el HTML en iframes, como antes.
                ErrorLog.Write(ex);
                joined = File.ReadAllBytes(Print(browser, work, CanvasExport.Html(doc, print: true), parts.Count));
            }
            File.WriteAllBytes(path, joined);
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

    /// <summary>
    /// Las páginas que se imprimen, en orden: lo seguido que no es HTML ni SVG va junto (con el título del canvas si es lo
    /// primero), y cada sección HTML o SVG, sola, como su propia página.
    /// </summary>
    internal static List<string> Parts(CanvasDoc doc)
    {
        var parts = new List<string>();
        var run = new List<CanvasSection>();
        foreach (var s in doc.Sections)
        {
            if (s.Kind is not (CanvasKinds.Html or CanvasKinds.Svg))
            {
                run.Add(s);
                continue;
            }
            if (run.Count > 0) parts.Add(CanvasExport.Html(doc, run, print: true, withTitle: parts.Count == 0));
            run = [];
            parts.Add(CanvasExport.PrintPage(s));
        }
        if (run.Count > 0 || parts.Count == 0) parts.Add(CanvasExport.Html(doc, run, print: true, withTitle: parts.Count == 0));
        return parts;
    }

    /// <summary>Imprime una página a PDF con el navegador sin ventana; devuelve el archivo.</summary>
    static string Print(string browser, string work, string html, int index)
    {
        var page = Path.Combine(work, $"parte-{index}.html");
        var output = Path.Combine(work, $"parte-{index}.pdf");
        File.WriteAllText(page, html, new UTF8Encoding(false));
        var start = new ProcessStartInfo(browser)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        // Un perfil aparte para cada una: así no choca con el navegador que la persona tenga abierto ni toca el suyo.
        foreach (var arg in new[]
        {
            "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions",
            "--user-data-dir=" + Path.Combine(work, $"perfil-{index}"), "--no-pdf-header-footer", "--virtual-time-budget=8000",
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
        return output;
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
