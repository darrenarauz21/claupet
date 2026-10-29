using System.Runtime.InteropServices;

namespace Claupet;

/// <summary>
/// La tecla para hablar y Esc, leídas del sistema aunque Claupet no tenga el foco: solo mira, no se queda con ninguna
/// tecla. Windows lo da directo; en Linux lo pregunta a X11 (con Wayland, solo mientras el foco está en una app de X11);
/// en macOS, a CGEventSourceKeyState (que puede pedir el permiso de «Monitoreo de entrada»). Sin forma de saberlo,
/// Read devuelve null.
/// </summary>
static class GlobalKeys
{
    /// <summary>Talk: la de hablar; Other: alguna otra apretada a la vez (era un atajo).</summary>
    public readonly record struct State(bool Talk, bool Escape, bool Other);

    static bool _failed;

    /// <summary>talk: una de MicKeys.Choices. all: también Esc y las demás (si no, solo la de hablar, que es más barato).</summary>
    public static State? Read(string talk, bool all)
    {
        if (_failed) return null;
        try
        {
            if (OperatingSystem.IsWindows()) return ReadWindows(talk, all);
            if (OperatingSystem.IsLinux()) return ReadX11(talk, all);
            if (OperatingSystem.IsMacOS()) return ReadMac(talk, all);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            _failed = true; // no hay forma en este sistema: no se vuelve a intentar
        }
        return null;
    }

    // ── Windows ─────────────────────────────────────────────────────────────────────────────

    static State ReadWindows(string talk, bool all)
    {
        var key = talk switch { "RMenu" => 0xA5, "RShiftKey" => 0xA1, _ => 0xA3 }; // VK_RMENU, VK_RSHIFT, VK_RCONTROL
        static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
        if (!all) return new State(Down(key), false, false);
        var other = false;
        // Desde Retroceso: las de antes son los botones del ratón. Mayús, Ctrl y Alt sin lado repiten la de hablar, y
        // con distribuciones como la española Alt derecho es AltGr, que Windows acompaña de un Ctrl izquierdo falso.
        var altGr = key == 0xA5 ? 0xA2 : -1;
        for (var vk = 0x08; vk <= 0xFE && !other; vk++)
            if (vk != key && vk != altGr && vk is not (0x10 or 0x11 or 0x12) && Down(vk)) other = true;
        return new State(Down(key), Down(0x1B), other);
    }

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);

    // ── Linux (X11) ─────────────────────────────────────────────────────────────────────────

    static IntPtr _display;
    static string? _talkFor;
    static int _talkCode, _escapeCode;

    static State? ReadX11(string talk, bool all)
    {
        if (_display == IntPtr.Zero)
        {
            _display = XOpenDisplay(IntPtr.Zero);
            if (_display == IntPtr.Zero)
            {
                _failed = true;
                return null;
            }
            _escapeCode = XKeysymToKeycode(_display, 0xff1b);
        }
        if (_talkFor != talk)
        {
            _talkFor = talk;
            _talkCode = talk switch
            {
                // Con distribuciones como la española, Alt derecho es AltGr (ISO_Level3_Shift).
                "RMenu" => XKeysymToKeycode(_display, 0xffea) is var alt and not 0 ? alt : XKeysymToKeycode(_display, 0xfe03),
                "RShiftKey" => XKeysymToKeycode(_display, 0xffe2),
                _ => XKeysymToKeycode(_display, 0xffe4),
            };
        }
        var keys = new byte[32];
        XQueryKeymap(_display, keys);
        bool Down(int code) => code > 0 && (keys[code >> 3] & (1 << (code & 7))) != 0;
        if (!all) return new State(Down(_talkCode), false, false);
        var other = false;
        for (var code = 8; code < 256 && !other; code++)
            if (code != _talkCode && Down(code)) other = true;
        return new State(Down(_talkCode), Down(_escapeCode), other);
    }

    [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] static extern int XQueryKeymap(IntPtr display, byte[] keys);
    [DllImport("libX11.so.6")] static extern byte XKeysymToKeycode(IntPtr display, nuint keysym);

    // ── macOS ───────────────────────────────────────────────────────────────────────────────

    static State ReadMac(string talk, bool all)
    {
        ushort key = talk switch { "RMenu" => 0x3D, "RShiftKey" => 0x3C, _ => 0x3E }; // kVK_RightOption, RightShift, RightControl
        const int hid = 1; // kCGEventSourceStateHIDSystemState: el teclado, aunque la tecla vaya a otra app
        if (!all) return new State(CGEventSourceKeyState(hid, key), false, false);
        var other = false;
        for (ushort code = 0; code < 0x80 && !other; code++)
            if (code != key && CGEventSourceKeyState(hid, code)) other = true;
        return new State(CGEventSourceKeyState(hid, key), CGEventSourceKeyState(hid, 0x35), other);
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    [return: MarshalAs(UnmanagedType.I1)]
    static extern bool CGEventSourceKeyState(int state, ushort key);
}
