using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Claupet;

/// <summary>
/// «captura»: la pantalla donde está el cursor o, con «recorte», la parte que se marque con el mouse sobre
/// esa pantalla congelada. Se guarda como PNG en la carpeta Capturas de pantalla de Windows y se copia al
/// portapapeles; la mascota, las nubecitas, la isla y los avisos se esconden mientras tanto. Si se pidió
/// mirarla, una copia queda sola en …\Claupet\capturas, la única carpeta que Claude puede leer.
/// </summary>
static class ScreenCapture
{
    public static string Folder => WindowsDesktop.Screenshots;

    /// <summary>Lo de las acciones que hace la interfaz de esta app: capturar con su recorte y el portapapeles de WinForms.</summary>
    public static DesktopUi Ui { get; } = new(
        crop => Task.FromResult<(string?, string?)>((Run(crop), null)),
        text =>
        {
            Sta(() => { Clipboard.SetText(text); return true; });
            return Task.FromResult<string?>(null);
        },
        () =>
        {
            var (text, image) = Sta(() => (Clipboard.ContainsText() ? Clipboard.GetText() : null, Clipboard.ContainsImage() ? Clipboard.GetImage() : null));
            if (text is { Length: > 0 })
            {
                image?.Dispose();
                return Task.FromResult<(string?, string?)>((text, null));
            }
            if (image is null) return Task.FromResult<(string?, string?)>((null, null));
            using (image) return Task.FromResult<(string?, string?)>((null, ForLook(image)));
        });

    /// <summary>El portapapeles pide un hilo STA: el de la app lo es; desde --accion, después de un await, quizá no.</summary>
    static T Sta<T>(Func<T> work)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) return work();
        T result = default!;
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { error = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
        return result;
    }

    /// <summary>La pantalla bajo el cursor o el recorte que se marque: el PNG guardado (y copiado), o null si se canceló.</summary>
    public static string? Run(bool crop)
    {
        using var screen = WithoutClaupet(Shoot);
        var image = crop ? CropForm.Pick(screen) : screen;
        if (image is null) return null;
        try
        {
            var file = CaptureFiles.NewFile(Folder);
            image.Save(file, ImageFormat.Png);
            Sta(() => { Clipboard.SetImage(image); return true; });
            return file;
        }
        finally { if (image != screen) image.Dispose(); }
    }

    /// <summary>Una imagen del portapapeles, sola en …\Claupet\capturas, para que Claude la mire.</summary>
    public static string ForLook(Image image)
    {
        var file = CaptureFiles.ClipboardImage();
        image.Save(file, ImageFormat.Png);
        return file;
    }

    /// <summary>
    /// La pantalla bajo el cursor, en sus pixeles reales: la app es «SystemAware» y, en un monitor con otra
    /// escala, Windows le daría una imagen reescalada; por eso el hilo pasa un momento a «PerMonitorV2».
    /// </summary>
    static Bitmap Shoot()
    {
        var before = SetThreadDpiAwarenessContext(new IntPtr(-4)); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        try
        {
            GetCursorPos(out var cursor);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            GetMonitorInfo(MonitorFromPoint(cursor, 2), ref info); // MONITOR_DEFAULTTONEAREST
            var r = info.Monitor;
            var shot = new Bitmap(r.Right - r.Left, r.Bottom - r.Top, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(r.Left, r.Top, 0, 0, shot.Size, CopyPixelOperation.SourceCopy);
            return shot;
        }
        finally { if (before != IntPtr.Zero) SetThreadDpiAwarenessContext(before); }
    }

    /// <summary>Esconde (sin cerrarlas) las ventanas de Claupet que están encima mientras corre work.</summary>
    static T WithoutClaupet<T>(Func<T> work)
    {
        var hidden = Application.OpenForms.Cast<Form>()
            .Where(f => f.Visible && f is MascotWindow or ChatBubble or BubbleForm or IslandForm or IslandInput or ToastForm)
            .Select(f => f.Handle).ToList();
        foreach (var handle in hidden) Cloak(handle, true);
        try
        {
            if (hidden.Count > 0)
            {
                DwmFlush(); // que el escritorio ya se haya dibujado sin ellas
                DwmFlush();
            }
            return work();
        }
        finally { foreach (var handle in hidden) Cloak(handle, false); }
    }

    static void Cloak(IntPtr handle, bool on)
    {
        var value = on ? 1 : 0;
        DwmSetWindowAttribute(handle, 13, ref value, sizeof(int)); // DWMWA_CLOAK
    }

    [StructLayout(LayoutKind.Sequential)] struct Point32 { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct Rect32 { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo
    {
        public int Size;
        public Rect32 Monitor, Work;
        public uint Flags;
    }

    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point32 point);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point32 point, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();
}

/// <summary>
/// El recorte: la pantalla congelada, oscurecida, a pantalla completa; se arrastra para marcar la parte que
/// se quiere (se ve con su brillo y su tamaño) y al soltar queda recortada. Esc o el clic derecho cancelan.
/// </summary>
sealed class CropForm : Form
{
    readonly Bitmap _screen;
    readonly Font _hint = Fonts.Make("Segoe UI Variable Text", 10f, "Segoe UI");
    Point? _from;
    Point _to;
    Rectangle? _picked;

    CropForm(Bitmap screen)
    {
        _screen = screen;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = Screen.FromPoint(Cursor.Position).Bounds;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        KeyPreview = true;
    }

    /// <summary>La parte marcada, en los pixeles de la captura; null si se canceló.</summary>
    public static Bitmap? Pick(Bitmap screen)
    {
        using var form = new CropForm(screen);
        form.Shown += (_, _) => Foreground.Take(form.Handle); // para que Esc llegue aquí
        if (form.ShowDialog() != DialogResult.OK || form._picked is not { } part) return null;
        // La ventana mide en pixeles de la app y la captura en los reales: se pasa de una a otra.
        var kx = screen.Width / (float)form.ClientSize.Width;
        var ky = screen.Height / (float)form.ClientSize.Height;
        var source = Rectangle.Intersect(new Rectangle(Point.Empty, screen.Size),
            Rectangle.Round(new RectangleF(part.X * kx, part.Y * ky, part.Width * kx, part.Height * ky)));
        if (source.Width < 2 || source.Height < 2) return null;
        return screen.Clone(source, screen.PixelFormat);
    }

    Rectangle Selection => _from is { } from
        ? Rectangle.FromLTRB(Math.Min(from.X, _to.X), Math.Min(from.Y, _to.Y), Math.Max(from.X, _to.X), Math.Max(from.Y, _to.Y))
        : Rectangle.Empty;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.DrawImage(_screen, ClientRectangle);
        using (var shade = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
        using (var region = new Region(ClientRectangle))
        {
            var selection = Selection;
            if (selection.Width > 0 && selection.Height > 0) region.Exclude(selection);
            g.FillRegion(shade, region);
        }
        var accent = Theme.ForDark(true).Accent;
        if (Selection is { Width: > 0, Height: > 0 } box)
        {
            using var pen = new Pen(accent, Math.Max(1, DeviceDpi / 96f * 1.5f));
            g.DrawRectangle(pen, box);
            var size = $"{(int)(box.Width * _screen.Width / (float)ClientSize.Width)} × {(int)(box.Height * _screen.Height / (float)ClientSize.Height)}";
            TextRenderer.DrawText(g, size, _hint, new Point(box.X, Math.Max(0, box.Y - _hint.Height - 6)), Color.White);
        }
        else
        {
            var hint = L.T("Arrastre para marcar la parte que quiere · Esc cancela");
            var measure = TextRenderer.MeasureText(hint, _hint);
            var pill = new Rectangle((ClientSize.Width - measure.Width) / 2 - 16, 40, measure.Width + 32, measure.Height + 16);
            using (var back = new SolidBrush(Color.FromArgb(220, 0x14, 0x14, 0x13)))
            using (var path = Draw.Rounded(pill, pill.Height / 2f))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(back, path);
            }
            TextRenderer.DrawText(g, hint, _hint, pill, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; return; }
        if (e.Button != MouseButtons.Left) return;
        _from = _to = e.Location;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_from is null) return;
        _to = e.Location;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || _from is null) return;
        _to = e.Location;
        var selection = Selection;
        if (selection.Width < 4 || selection.Height < 4) { _from = null; Invalidate(); return; } // un clic suelto no recorta
        _picked = selection;
        DialogResult = DialogResult.OK;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hint.Dispose();
        base.Dispose(disposing);
    }
}
