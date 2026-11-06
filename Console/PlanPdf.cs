namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Skia.Helpers;
using SkiaSharp;

/// <summary>
/// Pasa a PDF el informe de un plan (PlanReport): dibuja cada hoja sobre una página de Skia, así que queda vectorial,
/// con su letra y el texto seleccionable, no una foto.
/// </summary>
static class PlanPdf
{
    /// <summary>Un punto del PDF son 96/72 píxeles de pantalla.</summary>
    const double PointsPerPixel = 72d / 96d;

    /// <summary>
    /// Dibuja una página ya acomodada, de píxeles a puntos. La escala va en la propia página: el dibujo reemplaza la matriz
    /// del lienzo, así que un canvas.Scale previo se perdería y la página saldría recortada.
    /// </summary>
    internal static Task DrawAsync(SKCanvas canvas, Control page, Size size)
    {
        page.RenderTransformOrigin = RelativePoint.TopLeft;
        page.RenderTransform = new ScaleTransform(PointsPerPixel, PointsPerPixel);
        return DrawingContextHelper.RenderAsync(canvas, page, new Rect(size), new Vector(96, 96));
    }

    /// <summary>Guarda el informe con esas vistas (ids de PlanPanel.Views). Va en el hilo de la interfaz: arma controles.</summary>
    public static async Task SaveAsync(string path, PlanEntry entry, IReadOnlyList<PlanEvent> events, IReadOnlyList<string> views)
    {
        if (entry.Plan is not { } plan) throw new InvalidOperationException(entry.Error);
        var temp = path + ".tmp";
        try
        {
            // Con FileStream y no SKFileWStream: el de Skia se hereda, y un proceso que arrancara mientras tanto (la Terminal) lo dejaría bloqueado.
            using (var file = File.Create(temp))
            using (var stream = new SKManagedWStream(file))
            using (var document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata { Title = entry.Name, Creator = "Claupet", Producer = "Claupet" }))
            {
                var sheets = PlanReport.Build(entry.Name, plan, events, views);
                var ink = PlanReport.Paper();
                for (var i = 0; i < sheets.Count; i++)
                {
                    var size = new Size(PlanReport.PortraitWidth, PlanReport.PortraitHeight);
                    var page = Lay(PlanReport.Compose(sheets[i], ink, entry.Name, i + 1, sheets.Count), size);
                    using var canvas = document.BeginPage((float)(size.Width * PointsPerPixel), (float)(size.Height * PointsPerPixel));
                    await DrawAsync(canvas, page, size);
                    document.EndPage();
                }
                document.Close();
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Acomoda la hoja a su tamaño (nunca llega a estar en pantalla).</summary>
    internal static Control Lay(Control page, Size size)
    {
        page.Measure(size);
        page.Arrange(new Rect(size));
        return page;
    }
}
