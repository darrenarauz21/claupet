using System.Text;

namespace Claupet;

/// <summary>
/// El modelo con que trabaja ahora cada sesión de agente, para que el servidor MCP de Canvas (otro proceso, que abre el
/// agente) lo anote en cada cambio. Cada sesión tiene una clave que va al servidor en CLAUPET_CANVAS_SESSION; la Console
/// escribe «Canvas\sesiones\&lt;clave&gt;» con el modelo cada vez que cambia (también a mitad de sesión) y el servidor lo
/// lee al guardar. Sin clave o sin archivo (otras apps, una sesión que todavía no respondió), el cambio va sin modelo.
/// </summary>
static class CanvasAgentModel
{
    public const string Variable = "CLAUPET_CANVAS_SESSION";

    /// <summary>Lo que queda de sesiones que no se cerraron bien (la app se cerró de golpe) se borra pasado este tiempo.</summary>
    static readonly TimeSpan Stale = TimeSpan.FromDays(7);

    static string Folder => DataFolder.In("Canvas", "sesiones");

    public static string NewKey() => Guid.NewGuid().ToString("N")[..16];

    /// <summary>Una clave es lo que genera NewKey (hexadecimal): nada que salga de la carpeta.</summary>
    static bool Valid(string? key) => key is { Length: > 0 and <= 32 } && key.All(char.IsAsciiHexDigit);

    /// <summary>Anota el modelo de la sesión (null lo borra).</summary>
    public static void Write(string key, string? model)
    {
        if (!Valid(key)) return;
        try
        {
            var file = Path.Combine(Folder, key);
            if (model is not { Length: > 0 })
            {
                File.Delete(file);
                return;
            }
            Directory.CreateDirectory(Folder);
            File.WriteAllText(file, model.Trim(), new UTF8Encoding(false));
            foreach (var old in new DirectoryInfo(Folder).EnumerateFiles().Where(f => DateTime.UtcNow - f.LastWriteTimeUtc > Stale).ToList())
                old.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>El modelo que anotó la Console para esa sesión, o null.</summary>
    public static string? Read(string? key)
    {
        if (!Valid(key)) return null;
        try
        {
            var file = Path.Combine(Folder, key!);
            return File.Exists(file) && File.ReadAllText(file).Trim() is { Length: > 0 and <= 200 } model ? model : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Al cerrar la sesión: su archivo ya no hace falta.</summary>
    public static void Forget(string key) => Write(key, null);
}
