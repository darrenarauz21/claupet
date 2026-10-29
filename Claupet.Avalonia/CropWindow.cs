using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SD = System.Drawing;

namespace Claupet;

/// <summary>
/// Lo que hace la interfaz en las acciones de Windows (DesktopUi): capturar la pantalla del cursor sin las ventanas de
/// Claupet (hideWhile) y, si se pidió, recortarla con CropWindow; el portapapeles, con la API de Windows (WinClipboard).
/// Sin ventanas (--accion) no hay recorte.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
static class WindowsUi
{
    public static DesktopUi Create(Func<Func<Task>, Task> hideWhile, bool canCrop) => new(
        crop => CaptureAsync(crop, hideWhile, canCrop),
        text => Task.Run(() =>
        {
            WinClipboard.SetText(text);
            return (string?)null;
        }),
        () => Task.Run<(string?, string?)>(() =>
        {
            if (WinClipboard.GetText() is { Length: > 0 } text) return (text, null);
            if (WinClipboard.GetImage() is not { } image) return (null, null);
            var file = CaptureFiles.ClipboardImage();
            image.Save(file);
            return (null, file);
        }));

    static async Task<(string? File, string? Problem)> CaptureAsync(bool crop, Func<Func<Task>, Task> hideWhile, bool canCrop)
    {
        if (crop && !canCrop) return (null, L.T("El recorte necesita la app abierta."));
        ScreenShot? shot = null;
        await hideWhile(() => Task.Run(() => shot = WinScreen.UnderCursor()));
        if (shot is null) return (null, L.T("No pude capturar la pantalla."));
        if (crop)
        {
            var whole = shot;
            shot = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => CropWindow.PickAsync(whole));
            if (shot is null) return (null, null); // se canceló el recorte
        }
        var file = CaptureFiles.NewFile(WindowsDesktop.Screenshots);
        var picked = shot;
        await Task.Run(() =>
        {
            picked.Save(file);
            WinClipboard.SetImage(picked);
        });
        return (file, null);
    }
}

/// <summary>
/// El recorte de «captura recorte» en Windows, como el de la app WinForms: la pantalla congelada, oscurecida, a pantalla
/// completa en el monitor de la captura; se arrastra para marcar la parte que se quiere (se ve con su brillo y su
/// tamaño) y al soltar queda recortada. Esc o el clic derecho cancelan.
/// </summary>
sealed class CropWindow : Window
{
    static readonly SD.Color Shade = SD.Color.FromArgb(120, 0, 0, 0);
    static readonly SD.Color Pill = SD.Color.FromArgb(220, 0x14, 0x14, 0x13);

    readonly ScreenShot _shot;
    readonly WriteableBitmap _image;
    readonly TaskCompletionSource<ScreenShot?> _done = new();
    readonly CropView _view;
    Point? _from;
    Point _to;

    /// <summary>La parte marcada, en los pixeles de la captura; null si se canceló.</summary>
    public static Task<ScreenShot?> PickAsync(ScreenShot shot)
    {
        var window = new CropWindow(shot);
        window.Show();
        window.Activate();
        if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle()?.Handle is { } handle) Claupet.Foreground.Take(handle); // para que Esc llegue aquí
        return window._done.Task;
    }

    CropWindow(ScreenShot shot)
    {
        _shot = shot;
        _image = new WriteableBitmap(new PixelSize(shot.Width, shot.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var buffer = _image.Lock())
            for (var y = 0; y < shot.Height; y++)
                Marshal.Copy(shot.Bgra, y * shot.Width * 4, buffer.Address + y * buffer.RowBytes, shot.Width * 4);
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(shot.Left, shot.Top);
        WindowState = WindowState.FullScreen;
        Background = Brushes.Black;
        Cursor = new Cursor(StandardCursorType.Cross);
        Content = _view = new CropView(this);
        Closed += (_, _) => _done.TrySetResult(null);
    }

    Rect Selection => _from is { } from
        ? new Rect(new Point(Math.Min(from.X, _to.X), Math.Min(from.Y, _to.Y)), new Point(Math.Max(from.X, _to.X), Math.Max(from.Y, _to.Y)))
        : default;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(_view);
        if (point.Properties.IsRightButtonPressed) { Close(); return; }
        if (!point.Properties.IsLeftButtonPressed) return;
        _from = _to = point.Position;
        _view.InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_from is null) return;
        _to = e.GetPosition(_view);
        _view.InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_from is null || e.InitialPressMouseButton != MouseButton.Left) return;
        _to = e.GetPosition(_view);
        var selection = Selection;
        if (selection.Width < 4 || selection.Height < 4) // un clic suelto no recorta
        {
            _from = null;
            _view.InvalidateVisual();
            return;
        }
        // La ventana mide en pixeles de la interfaz y la captura en los reales: se pasa de una a otra.
        var kx = _shot.Width / _view.Bounds.Width;
        var ky = _shot.Height / _view.Bounds.Height;
        _done.TrySetResult(_shot.Crop((int)Math.Round(selection.X * kx), (int)Math.Round(selection.Y * ky),
            (int)Math.Round(selection.Width * kx), (int)Math.Round(selection.Height * ky)));
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Close();
    }

    sealed class CropView(CropWindow owner) : Control
    {
        public override void Render(DrawingContext dc)
        {
            var all = new Rect(Bounds.Size);
            dc.DrawImage(owner._image, new Rect(0, 0, owner._shot.Width, owner._shot.Height), all);
            var box = owner.Selection;
            var shade = Gfx.B(Shade);
            if (box.Width > 0 && box.Height > 0)
            {
                // Oscuro alrededor de lo marcado: arriba, abajo, izquierda y derecha.
                dc.FillRectangle(shade, new Rect(0, 0, all.Width, box.Top));
                dc.FillRectangle(shade, new Rect(0, box.Bottom, all.Width, all.Height - box.Bottom));
                dc.FillRectangle(shade, new Rect(0, box.Top, box.Left, box.Height));
                dc.FillRectangle(shade, new Rect(box.Right, box.Top, all.Width - box.Right, box.Height));
                dc.DrawRectangle(null, Gfx.P(Claupet.Theme.ForDark(true).Accent, 1.5), box);
                var size = $"{(int)(box.Width * owner._shot.Width / all.Width)} × {(int)(box.Height * owner._shot.Height / all.Height)}";
                Gfx.Write(dc, size, Gfx.Body, SD.Color.White, new Rect(box.X, Math.Max(0, box.Y - Gfx.Body.LineHeight - 6), 200, Gfx.Body.LineHeight));
                return;
            }
            dc.FillRectangle(shade, all);
            var hint = L.T("Arrastre para marcar la parte que quiere · Esc cancela");
            var measure = Gfx.Measure(hint, Gfx.Body);
            var pill = new Rect((all.Width - measure.Width) / 2 - 16, 40, measure.Width + 32, measure.Height + 16);
            Gfx.Fill(dc, Pill, pill, pill.Height / 2);
            Gfx.Write(dc, hint, Gfx.Body, SD.Color.White, pill, TextAlignment.Center, center: true);
        }
    }
}
