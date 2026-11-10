namespace Claupet;

using System.Text;
using System.Text.Json;

/// <summary>Un grupo de pestañas: su nombre y su color (un número de la paleta, así se ve bien en claro y en oscuro).</summary>
sealed class EditorGroup
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public int Color { get; set; }
}

/// <summary>
/// Lo que el editor tenía abierto, como en Notepad++: al volver, están las mismas pestañas y grupos, con lo que no se
/// había guardado (también lo «Sin título»). Va en la carpeta «editor» junto a los ajustes: sesion.json y, en
/// «respaldos», el texto de cada pestaña con cambios. No es un ajuste: lo guarda la Console.
/// </summary>
static class EditorSession
{
    public sealed record Doc(string? Path, string? Backup, string Language, string? Group, int Caret, int Number);
    public sealed record Group(string Id, string Name, int Color);
    public sealed record State(List<Doc> Docs, List<Group> Groups, int Current, int Zoom, bool Wrap, bool Spaces);

    /// <summary>Otra carpeta en vez de la de la persona: las pruebas no tocan su sesión.</summary>
    internal static string? FolderForTests { get; set; }

    static string Folder => FolderForTests ?? DataFolder.In("editor");
    static string FilePath => System.IO.Path.Combine(Folder, "sesion.json");
    static string Backups => System.IO.Path.Combine(Folder, "respaldos");
    static readonly UTF8Encoding Utf8 = new(false);

    public static State? Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath)) : null; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }

    /// <summary>Guarda la sesión y los respaldos (texto por id); borra los respaldos que ya no hacen falta. true si se pudo.</summary>
    public static bool Save(State state, IReadOnlyDictionary<string, string> backups)
    {
        try
        {
            Directory.CreateDirectory(Backups);
            foreach (var (id, text) in backups) Write(System.IO.Path.Combine(Backups, id + ".txt"), text);
            Write(FilePath, JsonSerializer.Serialize(state));
            foreach (var file in Directory.EnumerateFiles(Backups, "*.txt"))
                if (!backups.ContainsKey(System.IO.Path.GetFileNameWithoutExtension(file))) File.Delete(file);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex);
            return false;
        }
    }

    public static string? ReadBackup(string id)
    {
        var path = System.IO.Path.Combine(Backups, id + ".txt");
        try { return File.Exists(path) ? File.ReadAllText(path, Utf8) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Primero a un archivo de al lado y después se cambia: un corte a mitad no deja la sesión a medias.</summary>
    static void Write(string path, string text)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, text, Utf8);
        File.Move(temp, path, overwrite: true);
    }
}
