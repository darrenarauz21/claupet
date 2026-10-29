using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Claupet;

/// <summary>
/// Los accesos directos «Claupet» del menú Inicio y del escritorio, como los de cualquier app: abren Claupet Console
/// (y Claupet, si no estaba abierto). Llevan la cabeza del chibi, la misma de la ventana. Al arrancar, los encendidos
/// se crean si faltan y se corrigen si apuntan a otro .exe (se movió la carpeta).
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
static class Shortcuts
{
    public static string StartMenu => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Claupet.lnk");
    public static string Desktop => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Claupet.lnk");
    static string IconFile => DataFolder.In("claupet.ico");
    static string Exe => Environment.ProcessPath ?? "";

    /// <summary>Crea o quita el acceso directo de ese lugar.</summary>
    public static void Set(string file, bool on)
    {
        try
        {
            if (on) Write(file);
            else if (File.Exists(file)) File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException) { ErrorLog.Write(ex); }
    }

    /// <summary>Si está encendido y falta o apunta a otro .exe, lo vuelve a escribir.</summary>
    public static void Ensure(string file)
    {
        try
        {
            if (!File.Exists(file) || !File.Exists(IconFile) || !string.Equals(Target(file), Exe, StringComparison.OrdinalIgnoreCase)) Write(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException) { ErrorLog.Write(ex); }
    }

    static void Write(string file)
    {
        WriteIcon();
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(Exe);
            link.SetArguments(ConsoleApp.Flag);
            link.SetWorkingDirectory(Path.GetDirectoryName(Exe) ?? "");
            link.SetIconLocation(IconFile, 0);
            link.SetDescription(L.T("Claupet Console: el chat y todos los ajustes"));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            ((IPersistFile)link).Save(file, true);
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    static string? Target(string file)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(file, 0);
            var path = new StringBuilder(1024);
            link.GetPath(path, path.Capacity, IntPtr.Zero, 0);
            return path.ToString();
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    /// <summary>El ícono: la cabeza del chibi en los tamaños que pide Windows, como PNG dentro de un .ico.</summary>
    static void WriteIcon()
    {
        int[] sizes = [16, 24, 32, 48, 64, 128, 256];
        var images = sizes.Select(HeadPng).ToList();
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((short)0);
            writer.Write((short)1); // ícono
            writer.Write((short)sizes.Length);
            var offset = 6 + 16 * sizes.Length;
            for (var i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] % 256)); // 256 se escribe como 0
                writer.Write((byte)(sizes[i] % 256));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((short)1);
                writer.Write((short)32);
                writer.Write(images[i].Length);
                writer.Write(offset);
                offset += images[i].Length;
            }
            foreach (var image in images) writer.Write(image);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(IconFile)!);
        File.WriteAllBytes(IconFile, stream.ToArray());
    }

    /// <summary>
    /// La cabeza del chibi, recortada a lo que pinta y centrada, con cuadritos enteros (nítida). Se pinta a mano sobre
    /// los pixeles (BGRA): los cuadritos caen en pixeles enteros y no hace falta una biblioteca de imágenes.
    /// </summary>
    internal static byte[] HeadPng(int size)
    {
        var pixels = new List<(System.Drawing.Color Color, float X, float Y, float W, float H)>();
        ChibiSprite.PaintHead((color, x, y, w, h) => pixels.Add((color, x, y, w, h)), MascotFrame.Open);
        var bgra = new byte[size * size * 4];
        if (pixels.Count > 0)
        {
            var left = pixels.Min(p => p.X);
            var top = pixels.Min(p => p.Y);
            var width = pixels.Max(p => p.X + p.W) - left;
            var height = pixels.Max(p => p.Y + p.H) - top;
            var unit = Math.Max(1, (int)(size / Math.Max(width, height)));
            var x0 = (size - width * unit) / 2;
            var y0 = (size - height * unit) / 2;
            foreach (var (color, x, y, w, h) in pixels)
            {
                var fromX = Math.Clamp((int)Math.Round(x0 + (x - left) * unit), 0, size);
                var fromY = Math.Clamp((int)Math.Round(y0 + (y - top) * unit), 0, size);
                var toX = Math.Clamp((int)Math.Round(x0 + (x - left + w) * unit), 0, size);
                var toY = Math.Clamp((int)Math.Round(y0 + (y - top + h) * unit), 0, size);
                for (var py = fromY; py < toY; py++)
                    for (var px = fromX; px < toX; px++)
                    {
                        var i = (py * size + px) * 4;
                        (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (color.B, color.G, color.R, color.A);
                    }
            }
        }
        using var png = new MemoryStream();
        PngImage.Save(png, size, size, bgra, alpha: true);
        return png.ToArray();
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int max);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int max);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int show);
        void SetShowCmd(int show);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int max, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
