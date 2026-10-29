using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Claupet;

/// <summary>Una captura: dónde estaba en la pantalla (en pixeles reales) y sus pixeles BGRA, de arriba abajo.</summary>
sealed record ScreenShot(int Left, int Top, int Width, int Height, byte[] Bgra)
{
    /// <summary>Una parte (en pixeles de la captura); null si queda fuera o es de menos de 2 × 2.</summary>
    public ScreenShot? Crop(int x, int y, int width, int height)
    {
        var left = Math.Clamp(x, 0, Width);
        var top = Math.Clamp(y, 0, Height);
        var right = Math.Clamp(x + width, 0, Width);
        var bottom = Math.Clamp(y + height, 0, Height);
        if (right - left < 2 || bottom - top < 2) return null;
        var w = right - left;
        var h = bottom - top;
        var part = new byte[w * h * 4];
        for (var row = 0; row < h; row++)
            Array.Copy(Bgra, ((top + row) * Width + left) * 4, part, row * w * 4, w * 4);
        return new ScreenShot(Left + left, Top + top, w, h, part);
    }

    public void Save(string path) => PngImage.Save(path, Width, Height, Bgra);
}

/// <summary>
/// La pantalla en Windows sin WinForms: la del cursor, en sus pixeles reales. La app puede no ser «PerMonitorV2» y, en
/// un monitor con otra escala, Windows le daría una imagen reescalada; por eso el hilo pasa un momento a ese modo.
/// </summary>
[SupportedOSPlatform("windows")]
static class WinScreen
{
    public static ScreenShot UnderCursor()
    {
        var before = SetThreadDpiAwarenessContext(new IntPtr(-4)); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        try
        {
            GetCursorPos(out var cursor);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            GetMonitorInfo(MonitorFromPoint(cursor, 2), ref info); // MONITOR_DEFAULTTONEAREST
            var r = info.Monitor;
            return Shoot(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }
        finally { if (before != IntPtr.Zero) SetThreadDpiAwarenessContext(before); }
    }

    /// <summary>BitBlt de la pantalla a un DIB de 32 bits de arriba abajo; las ventanas con transparencia también salen.</summary>
    static ScreenShot Shoot(int left, int top, int width, int height)
    {
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var header = new BitmapInfoHeader { Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
        var bitmap = CreateDIBSection(screen, ref header, 0, out var bits, IntPtr.Zero, 0);
        try
        {
            if (bitmap == IntPtr.Zero) throw new InvalidOperationException(L.T("No pude capturar la pantalla."));
            var old = SelectObject(memory, bitmap);
            const int SrcCopy = 0x00CC0020, CaptureBlt = 0x40000000;
            var copied = BitBlt(memory, 0, 0, width, height, screen, left, top, SrcCopy | CaptureBlt);
            SelectObject(memory, old);
            if (!copied) throw new InvalidOperationException(L.T("No pude capturar la pantalla."));
            var pixels = new byte[width * height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255; // BitBlt deja el alfa en 0
            return new ScreenShot(left, top, width, height, pixels);
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
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

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point32 point);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point32 point, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, int rop);
}

/// <summary>
/// El portapapeles de Windows sin WinForms: texto (CF_UNICODETEXT) e imágenes (CF_DIB de 32 o 24 bits). Lo puede usar
/// cualquier hilo; si otra app lo tiene abierto, se reintenta un momento.
/// </summary>
[SupportedOSPlatform("windows")]
static class WinClipboard
{
    const uint UnicodeText = 13, Dib = 8, Moveable = 0x0002;

    static void Open()
    {
        for (var i = 0; i < 20; i++)
        {
            if (OpenClipboard(IntPtr.Zero)) return;
            Thread.Sleep(25);
        }
        throw new InvalidOperationException(L.T("No encuentro el portapapeles."));
    }

    public static void SetText(string text)
    {
        var bytes = (text.Length + 1) * 2;
        Open();
        try
        {
            EmptyClipboard();
            var memory = GlobalAlloc(Moveable, (UIntPtr)bytes);
            var target = GlobalLock(memory);
            var chars = (text + "\0").ToCharArray();
            Marshal.Copy(chars, 0, target, chars.Length);
            GlobalUnlock(memory);
            if (SetClipboardData(UnicodeText, memory) == IntPtr.Zero) GlobalFree(memory); // si no lo tomó, sigue siendo nuestro
        }
        finally { CloseClipboard(); }
    }

    public static string? GetText()
    {
        if (!IsClipboardFormatAvailable(UnicodeText)) return null;
        Open();
        try
        {
            var memory = GetClipboardData(UnicodeText);
            if (memory == IntPtr.Zero) return null;
            var source = GlobalLock(memory);
            try { return Marshal.PtrToStringUni(source); }
            finally { GlobalUnlock(memory); }
        }
        finally { CloseClipboard(); }
    }

    /// <summary>La imagen como DIB de 32 bits (de abajo arriba, como lo espera Windows); el resto de los formatos los arma Windows.</summary>
    public static void SetImage(ScreenShot shot)
    {
        const int Header = 40;
        var stride = shot.Width * 4;
        var bytes = Header + stride * shot.Height;
        Open();
        try
        {
            EmptyClipboard();
            var memory = GlobalAlloc(Moveable, (UIntPtr)bytes);
            var target = GlobalLock(memory);
            var dib = new byte[bytes];
            BitConverter.TryWriteBytes(dib.AsSpan(0), Header);
            BitConverter.TryWriteBytes(dib.AsSpan(4), shot.Width);
            BitConverter.TryWriteBytes(dib.AsSpan(8), shot.Height);
            BitConverter.TryWriteBytes(dib.AsSpan(12), (short)1);
            BitConverter.TryWriteBytes(dib.AsSpan(14), (short)32);
            BitConverter.TryWriteBytes(dib.AsSpan(20), stride * shot.Height);
            for (var y = 0; y < shot.Height; y++)
                Array.Copy(shot.Bgra, y * stride, dib, Header + (shot.Height - 1 - y) * stride, stride);
            Marshal.Copy(dib, 0, target, bytes);
            GlobalUnlock(memory);
            if (SetClipboardData(Dib, memory) == IntPtr.Zero) GlobalFree(memory);
        }
        finally { CloseClipboard(); }
    }

    /// <summary>La imagen copiada (DIB sin comprimir de 24 o 32 bits); null si no hay o viene en otro formato.</summary>
    public static ScreenShot? GetImage()
    {
        if (!IsClipboardFormatAvailable(Dib)) return null;
        Open();
        try
        {
            var memory = GetClipboardData(Dib);
            if (memory == IntPtr.Zero) return null;
            var size = (int)GlobalSize(memory);
            var source = GlobalLock(memory);
            try
            {
                var dib = new byte[size];
                Marshal.Copy(source, dib, 0, size);
                return FromDib(dib);
            }
            finally { GlobalUnlock(memory); }
        }
        finally { CloseClipboard(); }
    }

    /// <summary>Un DIB de 24 o 32 bits sin comprimir (o con BI_BITFIELDS) a BGRA de arriba abajo; null si es otro.</summary>
    internal static ScreenShot? FromDib(byte[] dib)
    {
        if (dib.Length < 40) return null;
        var header = BitConverter.ToInt32(dib, 0);
        var width = BitConverter.ToInt32(dib, 4);
        var rawHeight = BitConverter.ToInt32(dib, 8);
        var bits = BitConverter.ToInt16(dib, 14);
        var compression = BitConverter.ToInt32(dib, 16);
        var colors = BitConverter.ToInt32(dib, 32);
        if (width <= 0 || rawHeight == 0 || bits is not (24 or 32) || compression is not (0 or 3)) return null;
        var height = Math.Abs(rawHeight);
        // Los datos empiezan después del encabezado, de las máscaras (BI_BITFIELDS con encabezado de 40) y de la paleta.
        var start = header + (compression == 3 && header == 40 ? 12 : 0) + colors * 4;
        var stride = (width * bits / 8 + 3) & ~3;
        if (start + (long)stride * height > dib.Length) return null;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var row = start + (rawHeight > 0 ? height - 1 - y : y) * stride;
            for (var x = 0; x < width; x++)
            {
                var s = row + x * (bits / 8);
                var d = (y * width + x) * 4;
                pixels[d] = dib[s];
                pixels[d + 1] = dib[s + 1];
                pixels[d + 2] = dib[s + 2];
                pixels[d + 3] = 255;
            }
        }
        return new ScreenShot(0, 0, width, height, pixels);
    }

    [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] static extern IntPtr SetClipboardData(uint format, IntPtr memory);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr memory);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] static extern UIntPtr GlobalSize(IntPtr memory);
}
