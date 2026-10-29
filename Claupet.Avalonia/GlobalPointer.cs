using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Platform;

namespace Claupet;

/// <summary>
/// Dónde está el cursor en la pantalla (en pixeles, como las ventanas) y si hay un botón apretado, aunque el cursor no
/// esté sobre una ventana de Claupet: así la isla asoma al acercarse al borde de arriba y se pliega con un clic fuera.
/// Windows lo da directo; en Linux lo pregunta a X11 (la app corre en X11 o en XWayland: con Wayland, solo se sabe
/// mientras el cursor pasa sobre otra app de X11); en macOS, a NSEvent. Sin forma de saberlo, Read devuelve null.
/// </summary>
static class GlobalPointer
{
    public readonly record struct State(PixelPoint At, bool Down);

    static bool _failed;

    /// <summary>primary: la pantalla principal (en macOS, para pasar de puntos a pixeles).</summary>
    public static State? Read(Screen? primary)
    {
        if (_failed) return null;
        try
        {
            if (OperatingSystem.IsWindows()) return ReadWindows();
            if (OperatingSystem.IsLinux()) return ReadX11();
            if (OperatingSystem.IsMacOS()) return ReadMac(primary);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            _failed = true; // no hay forma en este sistema: no se vuelve a intentar
        }
        return null;
    }

    // ── Windows ─────────────────────────────────────────────────────────────────────────────

    static State? ReadWindows()
    {
        if (!GetCursorPos(out var p)) return null;
        // El bit alto: apretado ahora; el bajo: se apretó desde la consulta anterior (un clic corto entre dos lecturas).
        var down = new[] { 0x01, 0x02, 0x04 }.Any(vk => (GetAsyncKeyState(vk) & 0x8001) != 0);
        return new State(new PixelPoint(p.X, p.Y), down);
    }

    [StructLayout(LayoutKind.Sequential)] struct WinPoint { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out WinPoint point);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);

    // ── Linux (X11) ─────────────────────────────────────────────────────────────────────────

    static IntPtr _display;

    static State? ReadX11()
    {
        if (_display == IntPtr.Zero)
        {
            _display = XOpenDisplay(IntPtr.Zero);
            if (_display == IntPtr.Zero)
            {
                _failed = true;
                return null;
            }
        }
        var root = XDefaultRootWindow(_display);
        if (!XQueryPointer(_display, root, out _, out _, out var x, out var y, out _, out _, out var mask)) return null;
        const uint buttons = (1 << 8) | (1 << 9) | (1 << 10); // Button1Mask, Button2Mask, Button3Mask
        return new State(new PixelPoint(x, y), (mask & buttons) != 0);
    }

    [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")]
    static extern bool XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int rootX, out int rootY,
        out int winX, out int winY, out uint mask);

    // ── macOS ───────────────────────────────────────────────────────────────────────────────

    static IntPtr _nsEvent, _mouseLocation, _pressedButtons;

    /// <summary>NSEvent da puntos con el origen abajo a la izquierda de la pantalla principal; las ventanas, pixeles desde arriba.</summary>
    static State? ReadMac(Screen? primary)
    {
        if (primary is null) return null;
        if (_nsEvent == IntPtr.Zero)
        {
            _nsEvent = objc_getClass("NSEvent");
            _mouseLocation = sel_registerName("mouseLocation");
            _pressedButtons = sel_registerName("pressedMouseButtons");
        }
        var p = SendPoint(_nsEvent, _mouseLocation);
        var buttons = SendUInt(_nsEvent, _pressedButtons);
        var scale = primary.Scaling;
        var height = primary.Bounds.Height / scale;
        return new State(new PixelPoint((int)(p.X * scale), (int)((height - p.Y) * scale)), buttons != 0);
    }

    [StructLayout(LayoutKind.Sequential)] struct CGPoint { public double X, Y; }
    [DllImport("/usr/lib/libobjc.A.dylib")] static extern IntPtr objc_getClass(string name);
    [DllImport("/usr/lib/libobjc.A.dylib")] static extern IntPtr sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] static extern CGPoint SendPoint(IntPtr receiver, IntPtr selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] static extern nuint SendUInt(IntPtr receiver, IntPtr selector);
}
