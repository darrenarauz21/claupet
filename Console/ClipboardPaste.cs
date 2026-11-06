namespace Claupet;

using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

/// <summary>
/// Pegar archivos copiados (del Explorador, por ejemplo) en cualquier caja de texto de la app: entran sus rutas. Una caja
/// de Avalonia solo pega texto, y sin esto Ctrl+V no hacía nada. Si el portapapeles trae texto, se pega como siempre.
/// </summary>
static class ClipboardPaste
{
    static bool _installed, _plain;

    /// <summary>Las rutas de los archivos y carpetas copiados, si no hay texto (el que manda). Las pruebas la cambian.</summary>
    internal static Func<TextBox, Task<IReadOnlyList<string>>> Copied { get; set; } = async box =>
        TopLevel.GetTopLevel(box)?.Clipboard is { } clipboard && string.IsNullOrEmpty(await clipboard.TryGetTextAsync())
            ? await PathsAsync(clipboard)
            : [];

    /// <summary>Una vez al arrancar: vale para Ctrl+V y para el menú de todas las cajas.</summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        TextBox.PastingFromClipboardEvent.AddClassHandler<TextBox>(OnPasting);
    }

    static void OnPasting(TextBox box, RoutedEventArgs e)
    {
        if (_plain) return;
        e.Handled = true;
        _ = PasteAsync(box);
    }

    static async Task PasteAsync(TextBox box)
    {
        IReadOnlyList<string> paths = [];
        try { paths = await Copied(box); }
        catch (Exception) { }
        if (paths.Count > 0)
        {
            box.SelectedText = Join(paths);
            return;
        }
        // El aviso sale antes de leer el portapapeles, así que la marca solo dura esta llamada.
        _plain = true;
        try { box.Paste(); }
        finally { _plain = false; }
    }

    /// <summary>Las rutas de lo copiado; ninguna si no se pudo leer o no son archivos del equipo.</summary>
    public static async Task<IReadOnlyList<string>> PathsAsync(IClipboard clipboard)
    {
        try
        {
            return await clipboard.TryGetFilesAsync() is { } files ? files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() : [];
        }
        catch (Exception) { return []; }
    }

    /// <summary>Varias rutas en una línea, para que un programa o una IA las lea sin confundirse.</summary>
    public static string Join(IEnumerable<string> paths) => string.Join(" ", paths.Select(Quote));

    /// <summary>Una ruta para pegarla: entre comillas si tiene espacios.</summary>
    public static string Quote(string path) =>
        !path.Any(char.IsWhiteSpace) ? path
        : OperatingSystem.IsWindows() ? $"\"{path}\"" : "'" + path.Replace("'", "'\\''") + "'";
}
