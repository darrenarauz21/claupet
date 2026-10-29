using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Claupet;

/// <summary>
/// Una tecla con sus modificadores, como se guarda en Ajustes (KeyNames: «Ctrl+Alt+Space»). Las teclas van por su
/// nombre de System.Windows.Forms.Keys, el de siempre en settings.json; VirtualKeys da su código en Windows.
/// </summary>
readonly record struct KeyCombo(string Key, bool Ctrl = false, bool Alt = false, bool Shift = false, bool Win = false)
{
    /// <summary>La tecla Copilot de los teclados nuevos: Windows la recibe como Win+Mayús+F23.</summary>
    public static readonly KeyCombo Copilot = new("F23", Shift: true, Win: true);

    public static KeyCombo CtrlAlt(string key) => new(key, Ctrl: true, Alt: true);

    public static bool TryParse(string? text, out KeyCombo combo)
    {
        combo = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        bool ctrl = false, alt = false, shift = false, win = false;
        string? key = null;
        foreach (var part in text.Split('+'))
            switch (part)
            {
                case "Ctrl": ctrl = true; break;
                case "Alt": alt = true; break;
                case "Shift": shift = true; break;
                case "Win": win = true; break;
                default:
                    if (key is not null || !VirtualKeys.Codes.ContainsKey(part)) return false;
                    key = part;
                    break;
            }
        if (key is null) return false;
        combo = new KeyCombo(key, ctrl, alt, shift, win);
        return true;
    }

    public override string ToString() =>
        (Win ? "Win+" : "") + (Ctrl ? "Ctrl+" : "") + (Alt ? "Alt+" : "") + (Shift ? "Shift+" : "") + Key;

    public string Label => KeyNames.Label(ToString());

    /// <summary>El código de la tecla en Windows (0 si no es una de VirtualKeys).</summary>
    public int Vk => VirtualKeys.Codes.GetValueOrDefault(Key);
}

/// <summary>Los nombres de System.Windows.Forms.Keys (los que se guardan en Ajustes) y su código de tecla de Windows.</summary>
static class VirtualKeys
{
    public static readonly Dictionary<string, int> Codes = new() {
        ["LButton"] = 0x01, ["RButton"] = 0x02, ["Cancel"] = 0x03, ["MButton"] = 0x04, ["XButton1"] = 0x05, ["XButton2"] = 0x06,
        ["Back"] = 0x08, ["Tab"] = 0x09, ["LineFeed"] = 0x0A, ["Clear"] = 0x0C, ["Enter"] = 0x0D, ["Return"] = 0x0D,
        ["ShiftKey"] = 0x10, ["ControlKey"] = 0x11, ["Menu"] = 0x12, ["Pause"] = 0x13, ["CapsLock"] = 0x14, ["Capital"] = 0x14,
        ["HangulMode"] = 0x15, ["HanguelMode"] = 0x15, ["KanaMode"] = 0x15, ["JunjaMode"] = 0x17, ["FinalMode"] = 0x18, ["KanjiMode"] = 0x19,
        ["HanjaMode"] = 0x19, ["Escape"] = 0x1B, ["IMEConvert"] = 0x1C, ["IMENonconvert"] = 0x1D, ["IMEAccept"] = 0x1E, ["IMEAceept"] = 0x1E,
        ["IMEModeChange"] = 0x1F, ["Space"] = 0x20, ["Prior"] = 0x21, ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["Next"] = 0x22,
        ["End"] = 0x23, ["Home"] = 0x24, ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        ["Select"] = 0x29, ["Print"] = 0x2A, ["Execute"] = 0x2B, ["Snapshot"] = 0x2C, ["PrintScreen"] = 0x2C, ["Insert"] = 0x2D,
        ["Delete"] = 0x2E, ["Help"] = 0x2F, ["D0"] = 0x30, ["D1"] = 0x31, ["D2"] = 0x32, ["D3"] = 0x33,
        ["D4"] = 0x34, ["D5"] = 0x35, ["D6"] = 0x36, ["D7"] = 0x37, ["D8"] = 0x38, ["D9"] = 0x39,
        ["A"] = 0x41, ["B"] = 0x42, ["C"] = 0x43, ["D"] = 0x44, ["E"] = 0x45, ["F"] = 0x46,
        ["G"] = 0x47, ["H"] = 0x48, ["I"] = 0x49, ["J"] = 0x4A, ["K"] = 0x4B, ["L"] = 0x4C,
        ["M"] = 0x4D, ["N"] = 0x4E, ["O"] = 0x4F, ["P"] = 0x50, ["Q"] = 0x51, ["R"] = 0x52,
        ["S"] = 0x53, ["T"] = 0x54, ["U"] = 0x55, ["V"] = 0x56, ["W"] = 0x57, ["X"] = 0x58,
        ["Y"] = 0x59, ["Z"] = 0x5A, ["LWin"] = 0x5B, ["RWin"] = 0x5C, ["Apps"] = 0x5D, ["Sleep"] = 0x5F,
        ["NumPad0"] = 0x60, ["NumPad1"] = 0x61, ["NumPad2"] = 0x62, ["NumPad3"] = 0x63, ["NumPad4"] = 0x64, ["NumPad5"] = 0x65,
        ["NumPad6"] = 0x66, ["NumPad7"] = 0x67, ["NumPad8"] = 0x68, ["NumPad9"] = 0x69, ["Multiply"] = 0x6A, ["Add"] = 0x6B,
        ["Separator"] = 0x6C, ["Subtract"] = 0x6D, ["Decimal"] = 0x6E, ["Divide"] = 0x6F, ["F1"] = 0x70, ["F2"] = 0x71,
        ["F3"] = 0x72, ["F4"] = 0x73, ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
        ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B, ["F13"] = 0x7C, ["F14"] = 0x7D,
        ["F15"] = 0x7E, ["F16"] = 0x7F, ["F17"] = 0x80, ["F18"] = 0x81, ["F19"] = 0x82, ["F20"] = 0x83,
        ["F21"] = 0x84, ["F22"] = 0x85, ["F23"] = 0x86, ["F24"] = 0x87, ["NumLock"] = 0x90, ["Scroll"] = 0x91,
        ["LShiftKey"] = 0xA0, ["RShiftKey"] = 0xA1, ["LControlKey"] = 0xA2, ["RControlKey"] = 0xA3, ["LMenu"] = 0xA4, ["RMenu"] = 0xA5,
        ["BrowserBack"] = 0xA6, ["BrowserForward"] = 0xA7, ["BrowserRefresh"] = 0xA8, ["BrowserStop"] = 0xA9, ["BrowserSearch"] = 0xAA, ["BrowserFavorites"] = 0xAB,
        ["BrowserHome"] = 0xAC, ["VolumeMute"] = 0xAD, ["VolumeDown"] = 0xAE, ["VolumeUp"] = 0xAF, ["MediaNextTrack"] = 0xB0, ["MediaPreviousTrack"] = 0xB1,
        ["MediaStop"] = 0xB2, ["MediaPlayPause"] = 0xB3, ["LaunchMail"] = 0xB4, ["SelectMedia"] = 0xB5, ["LaunchApplication1"] = 0xB6, ["LaunchApplication2"] = 0xB7,
        ["OemSemicolon"] = 0xBA, ["Oem1"] = 0xBA, ["Oemplus"] = 0xBB, ["Oemcomma"] = 0xBC, ["OemMinus"] = 0xBD, ["OemPeriod"] = 0xBE,
        ["Oem2"] = 0xBF, ["OemQuestion"] = 0xBF, ["Oem3"] = 0xC0, ["Oemtilde"] = 0xC0, ["Oem4"] = 0xDB, ["OemOpenBrackets"] = 0xDB,
        ["OemPipe"] = 0xDC, ["Oem5"] = 0xDC, ["OemCloseBrackets"] = 0xDD, ["Oem6"] = 0xDD, ["OemQuotes"] = 0xDE, ["Oem7"] = 0xDE,
        ["Oem8"] = 0xDF, ["Oem102"] = 0xE2, ["OemBackslash"] = 0xE2, ["ProcessKey"] = 0xE5, ["Packet"] = 0xE7, ["Attn"] = 0xF6,
        ["Crsel"] = 0xF7, ["Exsel"] = 0xF8, ["EraseEof"] = 0xF9, ["Play"] = 0xFA, ["Zoom"] = 0xFB, ["NoName"] = 0xFC,
        ["Pa1"] = 0xFD, ["OemClear"] = 0xFE,
    };

    /// <summary>El nombre de un código (el primero de la tabla, si hay dos, como Return y Enter: el de WinForms).</summary>
    public static readonly Dictionary<int, string> Names = Codes.GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.First().Key);
}

/// <summary>
/// Atajos de teclado globales en Windows (RegisterHotKey), como en la app WinForms: una ventana solo de mensajes recibe
/// WM_HOTKEY en el hilo de la interfaz. Si otra app ya tiene uno, ese queda «en uso» y no se registra.
/// </summary>
[SupportedOSPlatform("windows")]
sealed class Hotkeys : IDisposable
{
    public sealed record Shortcut(string Keys, string What, bool Registered);

    const int WmHotkey = 0x0312, ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8, ModNoRepeat = 0x4000;
    static readonly WndProc Procedure = Dispatch;
    static readonly Dictionary<IntPtr, Hotkeys> Windows = new();
    static ushort _class;

    readonly Dictionary<int, Action> _actions = new();
    readonly List<Shortcut> _list = new();
    readonly IntPtr _window;

    public IReadOnlyList<Shortcut> List => _list;

    public Hotkeys()
    {
        if (_class == 0)
        {
            var info = new WndClassEx
            {
                Size = Marshal.SizeOf<WndClassEx>(), Procedure = Marshal.GetFunctionPointerForDelegate(Procedure),
                Instance = GetModuleHandle(null), ClassName = "ClaupetHotkeys",
            };
            _class = RegisterClassEx(ref info);
        }
        _window = CreateWindowEx(0, "ClaupetHotkeys", "", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero); // HWND_MESSAGE
        Windows[_window] = this;
    }

    /// <summary>Ctrl+Alt+key.</summary>
    public void Add(string key, string what, Action action) => Add(KeyCombo.CtrlAlt(key), what, action);

    public void Add(KeyCombo combo, string what, Action action)
    {
        var id = _actions.Count + 1;
        var modifiers = (combo.Alt ? ModAlt : 0) | (combo.Ctrl ? ModControl : 0) | (combo.Shift ? ModShift : 0)
            | (combo.Win ? ModWin : 0) | ModNoRepeat;
        var ok = _window != IntPtr.Zero && combo.Vk != 0 && RegisterHotKey(_window, id, modifiers, combo.Vk);
        if (ok) _actions[id] = action;
        _list.Add(new Shortcut(combo.Label, what, ok));
    }

    static IntPtr Dispatch(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmHotkey && Windows.TryGetValue(window, out var hotkeys) && hotkeys._actions.TryGetValue((int)wParam, out var action))
        {
            try { action(); } catch (Exception ex) { ErrorLog.Write(ex); }
            return IntPtr.Zero;
        }
        return DefWindowProc(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_window, id);
        _actions.Clear();
        Windows.Remove(_window);
        if (_window != IntPtr.Zero) DestroyWindow(_window);
    }

    delegate IntPtr WndProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WndClassEx
    {
        public int Size, Style;
        public IntPtr Procedure;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassEx(ref WndClassEx info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowEx(int exStyle, string className, string title, int style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr window, int id, int modifiers, int key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
}

/// <summary>El gancho de teclado de bajo nivel (WH_KEYBOARD_LL), compartido por KeyHook y KeyRecorderWindow.</summary>
[SupportedOSPlatform("windows")]
static class LowLevelKeyboard
{
    public delegate IntPtr Proc(int code, IntPtr wParam, IntPtr lParam);

    public static IntPtr Install(Proc proc) => SetWindowsHookEx(13, proc, GetModuleHandle(null), 0);

    public static IntPtr Next(IntPtr hook, int code, IntPtr wParam, IntPtr lParam) => CallNextHookEx(hook, code, wParam, lParam);

    public static void Remove(IntPtr hook)
    {
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
    }

    /// <summary>Los modificadores pulsados ahora mismo son exactamente los de la combinación.</summary>
    public static bool ModifiersDown(KeyCombo combo) =>
        combo.Ctrl == Down(0x11) && combo.Alt == Down(0x12) && combo.Shift == Down(0x10) && combo.Win == (Down(0x5B) || Down(0x5C));

    static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, Proc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
}

/// <summary>
/// Teclas o combinaciones que la app se queda, como en la app WinForms: no llegan a la ventana activa ni a Windows (así
/// la tecla Copilot no abre Copilot). Avisa al bajar y al subir, sin repeticiones, en el hilo de la interfaz. Si la
/// combinación lleva Win, Windows vería Win pulsada y soltada sola y abriría Inicio: antes de soltarla se «toca» una
/// tecla sin uso (0xE8, como hace AutoHotkey).
/// </summary>
[SupportedOSPlatform("windows")]
sealed class KeyHook : IDisposable
{
    public event Action<KeyCombo, bool>? Changed;

    const int MaskKey = 0xE8, KeyUp = 0x2, Extended = 0x1;
    readonly LowLevelKeyboard.Proc _proc;
    IntPtr _hook;
    KeyCombo? _held;
    bool _maskWin;

    public IReadOnlyList<KeyCombo> Combos { get; set; } = [];

    public KeyHook()
    {
        _proc = Callback;
        _hook = LowLevelKeyboard.Install(_proc);
    }

    IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var vk = Marshal.ReadInt32(lParam);
            var down = (int)wParam is 0x100 or 0x104; // WM_KEYDOWN, WM_SYSKEYDOWN
            if (_held is { } held && vk == held.Vk)
            {
                if (!down) { _held = null; Raise(held, false); }
                return 1; // ni la repetición ni la subida llegan a nadie
            }
            if (down && _held is null && Combos.FirstOrDefault(c => c.Vk == vk && LowLevelKeyboard.ModifiersDown(c)) is { Key: not null } combo)
            {
                _held = combo;
                _maskWin |= combo.Win;
                Raise(combo, true);
                return 1;
            }
            if (!down && _maskWin && vk is 0x5B or 0x5C)
            {
                _maskWin = false;
                keybd_event(MaskKey, 0, 0, IntPtr.Zero);
                keybd_event(MaskKey, 0, KeyUp, IntPtr.Zero);
                keybd_event((byte)vk, 0, KeyUp | Extended, IntPtr.Zero); // y ahora sí, Win arriba
                return 1;
            }
        }
        return LowLevelKeyboard.Next(_hook, code, wParam, lParam);
    }

    /// <summary>El gancho tiene que volver enseguida: el aviso va después, en el hilo de la interfaz.</summary>
    void Raise(KeyCombo combo, bool down) => Dispatcher.UIThread.Post(() => Changed?.Invoke(combo, down));

    public void Dispose()
    {
        LowLevelKeyboard.Remove(_hook);
        _hook = IntPtr.Zero;
    }

    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, int flags, IntPtr extra);
}

/// <summary>
/// «Grabar…», como en la app WinForms: una ventanita que espera la tecla o la combinación. Mientras está abierta se queda
/// con todas las teclas (así Win o la tecla Copilot no hacen lo suyo) y se cierra cuando se sueltan. Esc cancela. Las
/// teclas que escriben solo valen con Ctrl, Alt o Win; para hablar valen también Ctrl, Alt o Mayús derechos, solos.
/// </summary>
[SupportedOSPlatform("windows")]
sealed class KeyRecorderWindow : Window
{
    readonly bool _talk;
    readonly Theme _theme;
    readonly string _title;
    readonly LowLevelKeyboard.Proc _proc;
    readonly HashSet<int> _down = new();
    readonly TaskCompletionSource<string?> _result = new();
    readonly RecorderView _view;
    IntPtr _hook;
    int _lone;        // un modificador que se pulsó solo (puede ser la tecla, si se suelta sin otra)
    string _shown = "", _note = "", _combo = "";
    bool _done;

    /// <summary>La combinación (KeyNames), o null si se canceló.</summary>
    public static Task<string?> RecordAsync(Theme theme, bool talk, string title)
    {
        var window = new KeyRecorderWindow(theme, talk, title);
        window.Show();
        window.Activate();
        if (window.TryGetPlatformHandle()?.Handle is { } handle) Claupet.Foreground.Take(handle);
        return window._result.Task;
    }

    KeyRecorderWindow(Theme theme, bool talk, string title)
    {
        _theme = theme;
        _talk = talk;
        _title = title;
        _proc = Callback;
        SystemDecorations = SystemDecorations.BorderOnly;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 360;
        Height = 132;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Gfx.B(theme.Background);
        Content = _view = new RecorderView(this);
        Opened += (_, _) => _hook = LowLevelKeyboard.Install(_proc);
        Deactivated += (_, _) =>
        {
            if (!_done) Close(); // clic en otro lado: cancela
        };
        Closed += (_, _) =>
        {
            LowLevelKeyboard.Remove(_hook);
            _hook = IntPtr.Zero;
            _result.TrySetResult(_done && _combo.Length > 0 ? _combo : null);
        };
    }

    IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return LowLevelKeyboard.Next(_hook, code, wParam, lParam);
        var vk = Marshal.ReadInt32(lParam);
        var down = (int)wParam is 0x100 or 0x104;
        if (down) { if (_down.Add(vk)) Dispatcher.UIThread.Post(() => Pressed(vk)); }
        else if (_down.Remove(vk)) Dispatcher.UIThread.Post(() => Released(vk));
        return 1; // nada llega a la ventana activa ni a Windows mientras se graba
    }

    static bool IsModifier(int vk) => vk is >= 0xA0 and <= 0xA5 or 0x5B or 0x5C or 0x10 or 0x11 or 0x12;

    static string KeyName(int vk) => VirtualKeys.Names.GetValueOrDefault(vk, "");

    void Pressed(int vk)
    {
        if (_done) return;
        if (IsModifier(vk))
        {
            _lone = _down.Count == 1 ? vk : 0;
            var modifiers = Current("None").ToString(); // «Win+Shift+None»: lo que va pulsado, mientras se arma
            _shown = KeyNames.Label(modifiers[..^"None".Length]) + "…";
            _note = "";
            _view.InvalidateVisual();
            return;
        }
        _lone = 0;
        var combo = Current(KeyName(vk));
        if (vk == 0x1B && !combo.Ctrl && !combo.Alt && !combo.Shift && !combo.Win) // Esc
        {
            Close();
            return;
        }
        if (combo.Key.Length == 0) return; // una tecla sin nombre en Keys: no se puede guardar
        Finish(combo.ToString(), Problem(combo));
    }

    void Released(int vk)
    {
        if (_done)
        {
            if (_down.Count == 0) Close();
            return;
        }
        if (vk != _lone) return;
        _lone = 0;
        var key = KeyName(vk);
        if (_talk && MicKeys.IsHold(key)) Finish(key, null);
        else Finish(key, _talk ? L.T("Sola, vale Ctrl, Alt o Mayús del lado derecho.") : L.T("Combínela con otra tecla."));
    }

    /// <summary>La tecla con los modificadores pulsados ahora.</summary>
    KeyCombo Current(string key) => new(key,
        Ctrl: _down.Overlaps([0xA2, 0xA3, 0x11]), Alt: _down.Overlaps([0xA4, 0xA5, 0x12]),
        Shift: _down.Overlaps([0xA0, 0xA1, 0x10]), Win: _down.Overlaps([0x5B, 0x5C]));

    void Finish(string combo, string? problem)
    {
        _shown = KeyNames.Label(combo);
        _note = problem ?? "";
        if (problem is null)
        {
            _combo = combo;
            _done = true;
            if (_down.Count == 0) Dispatcher.UIThread.Post(Close);
        }
        _view.InvalidateVisual();
    }

    /// <summary>Por qué no sirve esa combinación (null: sirve).</summary>
    string? Problem(KeyCombo c)
    {
        if (c == KeyCombo.Copilot && !_talk) return L.T("La tecla Copilot tiene su propio interruptor en Ajustes.");
        if (!_talk && c is { Ctrl: true, Alt: true, Shift: false, Win: false, Key: "A" or "T" or "N" or "S" })
            return L.T("Ese atajo ya lo usa la isla.");
        if (c.Ctrl || c.Alt || c.Win) return null;
        var vk = c.Vk;
        // F13 a F24, Pausa, Bloq Despl y las de navegador y aplicaciones de los teclados multimedia.
        var alone = vk is >= 0x7C and <= 0x87 or 0x13 or 0x91 or >= 0xAA and <= 0xAC or >= 0xB4 and <= 0xB7;
        return alone ? null : L.T("Esa tecla tiene otro uso: combínela con Ctrl, Alt o Win.");
    }

    sealed class RecorderView(KeyRecorderWindow owner) : Control
    {
        public override void Render(DrawingContext dc)
        {
            var t = owner._theme;
            const double pad = 18;
            var width = Bounds.Width - pad * 2;
            Gfx.Write(dc, owner._title, Gfx.BodyStrong, t.Text, new Rect(pad, pad, width, 22));
            var box = new Rect(pad, pad + 28, width, 36);
            Gfx.Fill(dc, t.Raised, box, 8);
            var shown = owner._shown.Length > 0 ? owner._shown : L.T("Pulse la tecla o la combinación…");
            Gfx.Write(dc, shown, Gfx.BodyStrong, owner._shown.Length > 0 ? t.Text : t.Tertiary, box, TextAlignment.Center, center: true);
            var note = owner._note.Length > 0 ? owner._note : L.T("Esc cancela");
            Gfx.Write(dc, note, Gfx.Small, owner._note.Length > 0 ? t.Danger : t.Secondary, new Rect(pad, box.Bottom + 10, width, 20));
        }
    }
}
