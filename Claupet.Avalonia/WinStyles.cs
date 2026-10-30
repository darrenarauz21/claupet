using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Claupet;

/// <summary>
/// Cómo se ven y se comportan en Windows las ventanas sin marco, como en la app WinForms. Lo que flota sobre el escritorio
/// sin pedir texto (la mascota, la nubecita de los avisos, los avisos y la caja que vuela) no le quita el foco a la app en
/// uso, ni al mostrarse ni al hacerle clic (WS_EX_NOACTIVATE), y no sale en la barra de tareas ni en Alt+Tab
/// (WS_EX_TOOLWINDOW). Las que tienen fondo (la ventana de la bandeja y los avisos) son tarjetas: con sombra y, en
/// Windows 11, con las esquinas redondeadas y el borde del DWM.
/// </summary>
static class WinStyles
{
    const int ExStyle = -20, NoActivate = 0x08000000, ToolWindow = 0x80;
    const int ClassStyle = -26, DropShadow = 0x20000;
    const int UseImmersiveDarkMode = 20, WindowCornerPreference = 33, BorderColor = 34, Round = 2;

    /// <summary>Windows 11: el DWM redondea las esquinas y dibuja el borde (en Windows 10, el borde va en la ventana).</summary>
    public static bool Rounded => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    public static void Float(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        window.Opened += (_, _) =>
        {
            if (Handle(window) is not { } hwnd) return;
            SetWindowLongPtr(hwnd, ExStyle, (IntPtr)(GetWindowLongPtr(hwnd, ExStyle).ToInt64() | NoActivate | ToolWindow));
        };
    }

    /// <summary>
    /// Una tarjeta: la sombra del sistema (CS_DROPSHADOW, en la clase de esa ventana: Avalonia registra una por ventana) y,
    /// en Windows 11, las esquinas redondeadas, el borde del color del tema y el modo oscuro. Se vuelve a llamar al cambiar
    /// el tema; antes de mostrarse, para que la sombra salga desde la primera vez.
    /// </summary>
    public static void Card(Window window, Theme theme)
    {
        if (!OperatingSystem.IsWindows() || Handle(window) is not { } hwnd) return;
        SetClassLongPtr(hwnd, ClassStyle, (IntPtr)(GetClassLongPtr(hwnd, ClassStyle).ToInt64() | DropShadow));
        try
        {
            var dark = theme.Dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, UseImmersiveDarkMode, ref dark, sizeof(int));
            var corner = Round;
            DwmSetWindowAttribute(hwnd, WindowCornerPreference, ref corner, sizeof(int));
            var border = theme.Border.R | theme.Border.G << 8 | theme.Border.B << 16; // COLORREF
            DwmSetWindowAttribute(hwnd, BorderColor, ref border, sizeof(int));
        }
        catch (Exception) { } // Windows 10: sin esquinas redondeadas.
    }

    static IntPtr? Handle(Window window) => window.TryGetPlatformHandle()?.Handle is { } hwnd && hwnd != IntPtr.Zero ? hwnd : null;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    static extern IntPtr GetClassLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    static extern IntPtr SetClassLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
