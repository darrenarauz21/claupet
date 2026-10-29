using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Claupet;

/// <summary>
/// En Windows, lo que flota sobre el escritorio sin pedir texto (la mascota, la nubecita de los avisos, los avisos y la
/// caja que vuela) no le quita el foco a la app en uso, ni al mostrarse ni al hacerle clic (WS_EX_NOACTIVATE), y no sale en
/// la barra de tareas ni en Alt+Tab (WS_EX_TOOLWINDOW), como en la app WinForms.
/// </summary>
static class WinStyles
{
    const int ExStyle = -20, NoActivate = 0x08000000, ToolWindow = 0x80;

    public static void Float(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        window.Opened += (_, _) =>
        {
            if (window.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero) return;
            SetWindowLongPtr(hwnd, ExStyle, (IntPtr)(GetWindowLongPtr(hwnd, ExStyle).ToInt64() | NoActivate | ToolWindow));
        };
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
