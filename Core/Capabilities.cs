namespace ClaudeUsageTray;

/// <summary>
/// Lo que Claupet hace en el equipo además de lo de siempre (fondo, tema, color, volumen, música y abrir):
/// cada capacidad pide permiso la primera vez que se usa. «Permitir siempre» la deja en Permissions del
/// settings.json de la app; en Ajustes › Asistente › Permisos se da o se quita. Los Id van en settings.json
/// y no cambian con el idioma. Por ahora solo en Windows, como el chat.
/// </summary>
static class Capabilities
{
    public const string Capture = "captura", Calendar = "calendario", Notes = "notas", Reminders = "recordatorios",
        Windows = "ventanas", Computer = "equipo", Brightness = "brillo", Status = "estado",
        Clipboard = "portapapeles", Folders = "carpetas";

    /// <summary>Cada una con su nombre (en Ajustes) y lo que se pregunta: «¿Dejo que Claupet {Ask}?».</summary>
    public static readonly (string Id, string Label, string Ask)[] All =
    [
        (Capture, L.T("Capturar la pantalla"), L.T("capture la pantalla")),
        (Calendar, L.T("Agendar en el calendario"), L.T("agende en su calendario")),
        (Notes, L.T("Guardar y leer sus notas"), L.T("guarde y lea sus notas")),
        (Reminders, L.T("Recordarle cosas"), L.T("le recuerde cosas (con una tarea programada de Windows)")),
        (Windows, L.T("Cerrar, minimizar y traer ventanas"), L.T("cierre, minimice y traiga ventanas")),
        (Computer, L.T("Bloquear, suspender, reiniciar o apagar"), L.T("bloquee, suspenda, reinicie o apague el equipo")),
        (Brightness, L.T("Cambiar el brillo"), L.T("cambie el brillo de la pantalla")),
        (Status, L.T("Ver la batería, el disco y la memoria"), L.T("vea la batería, el disco y la memoria")),
        (Clipboard, L.T("Usar el portapapeles"), L.T("lea y use el portapapeles")),
        (Folders, L.T("Abrir sus carpetas"), L.T("abra sus carpetas")),
    ];

    public static string Label(string id) => All.FirstOrDefault(c => c.Id == id).Label ?? id;

    public static string Ask(string id) => All.FirstOrDefault(c => c.Id == id).Ask ?? id;

    public static bool Allowed(AppSettings settings, string id) => settings.Permissions.Contains(id);
}
