using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ClaudeUsageTray;

/// <summary>Lo que las acciones de la agenda leen del JSON que manda Claude: textos y fechas de este equipo.</summary>
static class AgendaJson
{
    /// <summary>El valor de la acción como objeto; si vino un texto suelto, null (y el texto queda en plain).</summary>
    public static JsonElement? Object(string value)
    {
        if (!value.TrimStart().StartsWith('{')) return null;
        try
        {
            using var doc = JsonDocument.Parse(value);
            return doc.RootElement.Clone();
        }
        catch (JsonException) { return null; }
    }

    public static string? Text(JsonElement? obj, string name) =>
        obj is { } o && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { } s && s.Trim().Length > 0
            ? s.Trim() : null;

    public static bool Flag(JsonElement? obj, string name) =>
        obj is { } o && o.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True
            || v.ValueKind == JsonValueKind.String && v.GetString() is "true" or "sí" or "si");

    /// <summary>«AAAA-MM-DDTHH:MM» (o solo la fecha) en la hora de este equipo; con zona, se pasa a la de aquí.</summary>
    public static DateTime? Date(string? text) =>
        text is not null && DateTime.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces, out var date) ? date : null;

    /// <summary>Las palabras de más de dos letras, para buscar sin importar mayúsculas ni tildes.</summary>
    public static bool HasAll(string text, string words)
    {
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        return words.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 2)
            .All(w => compare.IndexOf(text, w, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
    }

    /// <summary>Un nombre de archivo con lo que se pueda del título.</summary>
    public static string FileName(string title, int max = 60)
    {
        var clean = new string(title.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim(' ', '.');
        return clean.Length <= max ? clean : clean[..max].TrimEnd(' ', '.');
    }
}

/// <summary>
/// Las notas: un Markdown por nota en Documentos\Claupet\Notas (con OneDrive, se sincroniza), con el título
/// arriba y la fecha. Se leen las últimas o las que tengan ciertas palabras, para que Claude responda.
/// </summary>
static class Notes
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Claupet", "Notas");

    /// <summary>Guarda la nota y devuelve su archivo.</summary>
    public static string Save(string title, string text, DateTime now)
    {
        Directory.CreateDirectory(Folder);
        var name = AgendaJson.FileName(title) is { Length: > 0 } t ? t : L.T("Nota");
        var file = Path.Combine(Folder, $"{now:yyyy-MM-dd HHmm} {name}.md");
        for (var i = 2; File.Exists(file); i++) file = Path.Combine(Folder, $"{now:yyyy-MM-dd HHmm} {name} ({i}).md");
        File.WriteAllText(file, $"# {title}\n\n{now:f}\n\n{text.Trim()}\n", new UTF8Encoding(false));
        return file;
    }

    /// <summary>Las últimas notas (o las que tengan esas palabras), en texto para Claude; vacío si no hay.</summary>
    public static string Read(string? words, int count = 8, int maxChars = 6000)
    {
        if (!Directory.Exists(Folder)) return "";
        var text = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(Folder, "*.md").OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal))
        {
            string content;
            try { content = File.ReadAllText(file); }
            catch (IOException) { continue; }
            if (words is not null && !AgendaJson.HasAll(Path.GetFileNameWithoutExtension(file) + " " + content, words)) continue;
            var body = string.Join(" ", content.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')))
                .Trim();
            if (body.Length > 500) body = body[..500] + "…";
            text.Append("• ").Append(Path.GetFileNameWithoutExtension(file)).Append(": ").AppendLine(body);
            if (--count == 0 || text.Length > maxChars) break;
        }
        return text.ToString().TrimEnd();
    }
}

/// <summary>Un evento para el calendario.</summary>
sealed record CalendarEvent(string Title, DateTime Start, DateTime End, bool AllDay, string? Place, string? Notes);

/// <summary>
/// El calendario: Claupet escribe el evento en un .ics (iCalendar) y lo abre con el calendario de Windows de
/// la persona (Outlook, el nuevo Outlook, Calendario…), que lo muestra para guardarlo. Quedan los últimos
/// KeepFiles en …\ClaudeUsageTray\calendario.
/// </summary>
static class CalendarFile
{
    static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeUsageTray", "calendario");

    const int KeepFiles = 10;

    /// <summary>{"titulo","inicio","fin","lugar","nota","todo_el_dia"}; si falta algo esencial, null y lo que falta.</summary>
    public static CalendarEvent? Parse(string value, out string? problem)
    {
        var obj = AgendaJson.Object(value);
        var title = AgendaJson.Text(obj, "titulo") ?? (obj is null && value.Trim().Length > 0 ? value.Trim() : null);
        var start = AgendaJson.Date(AgendaJson.Text(obj, "inicio"));
        problem = title is null ? L.T("Me falta el nombre del evento.")
            : start is null ? L.T("Me falta la fecha y la hora del evento.")
            : null;
        if (problem is not null) return null;
        var allDay = AgendaJson.Flag(obj, "todo_el_dia") || AgendaJson.Text(obj, "inicio")!.Length <= 10;
        var end = AgendaJson.Date(AgendaJson.Text(obj, "fin")) is { } e && e > start ? e
            : allDay ? start!.Value.Date.AddDays(1) : start!.Value.AddHours(1);
        if (allDay && end.TimeOfDay != TimeSpan.Zero) end = end.Date.AddDays(1);
        return new CalendarEvent(title!, allDay ? start!.Value.Date : start!.Value, end, allDay,
            AgendaJson.Text(obj, "lugar"), AgendaJson.Text(obj, "nota"));
    }

    /// <summary>Escribe el .ics y devuelve su ruta.</summary>
    public static string Write(CalendarEvent e)
    {
        Directory.CreateDirectory(Folder);
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//Claupet//Claupet//ES", "CALSCALE:GREGORIAN", "METHOD:PUBLISH",
            "BEGIN:VEVENT",
            $"UID:{Guid.NewGuid():N}@claupet",
            $"DTSTAMP:{Utc(DateTime.Now)}",
        };
        if (e.AllDay)
        {
            lines.Add($"DTSTART;VALUE=DATE:{e.Start:yyyyMMdd}");
            lines.Add($"DTEND;VALUE=DATE:{e.End:yyyyMMdd}");
        }
        else
        {
            lines.Add($"DTSTART:{Utc(e.Start)}");
            lines.Add($"DTEND:{Utc(e.End)}");
        }
        lines.Add("SUMMARY:" + Escape(e.Title));
        if (e.Place is { } place) lines.Add("LOCATION:" + Escape(place));
        if (e.Notes is { } notes) lines.Add("DESCRIPTION:" + Escape(notes));
        lines.AddRange(["BEGIN:VALARM", "ACTION:DISPLAY", "DESCRIPTION:" + Escape(e.Title), "TRIGGER:-PT15M", "END:VALARM",
            "END:VEVENT", "END:VCALENDAR"]);

        var file = Path.Combine(Folder, $"evento-{DateTime.Now:yyyyMMdd-HHmmss}.ics");
        File.WriteAllText(file, string.Concat(lines.Select(l => Fold(l) + "\r\n")), new UTF8Encoding(false));
        foreach (var old in new DirectoryInfo(Folder).GetFiles("evento-*.ics").OrderByDescending(f => f.Name).Skip(KeepFiles))
            try { old.Delete(); } catch (IOException) { }
        return file;
    }

    static string Utc(DateTime local) => local.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    static string Escape(string text) =>
        text.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").ReplaceLineEndings("\\n");

    /// <summary>Las líneas de más de 75 bytes siguen en la de abajo, con un espacio al principio (RFC 5545).</summary>
    static string Fold(string line)
    {
        var folded = new StringBuilder();
        var bytes = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (bytes + size > 75)
            {
                folded.Append("\r\n ");
                bytes = 1;
            }
            folded.Append(rune.ToString());
            bytes += size;
        }
        return folded.ToString();
    }
}

/// <summary>Un recordatorio: qué y cuándo (hora de este equipo). Done: ya se avisó o se canceló.</summary>
sealed class Reminder
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime At { get; set; }
    public bool Done { get; set; }
}

/// <summary>
/// Los recordatorios, en …\ClaudeUsageTray\recordatorios.json. La app los mira cada pocos segundos y avisa
/// los que llegaron (también los que pasaron con la app cerrada); los avisados se borran a la semana.
/// </summary>
static class ReminderStore
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeUsageTray", "recordatorios.json");

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    static List<Reminder> Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<List<Reminder>>(File.ReadAllText(FilePath), Options) ?? new();
        }
        catch (Exception) { }
        return new();
    }

    static void Save(List<Reminder> list)
    {
        list.RemoveAll(r => r.Done && r.At < DateTime.Now.AddDays(-7));
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(list, Options));
    }

    /// <summary>{"texto","cuando"}; si falta algo o la hora ya pasó, null y lo que pasa.</summary>
    public static Reminder? Parse(string value, DateTime now, out string? problem)
    {
        var obj = AgendaJson.Object(value);
        var text = AgendaJson.Text(obj, "texto");
        var at = AgendaJson.Date(AgendaJson.Text(obj, "cuando"));
        problem = text is null ? L.T("No me dijeron qué recordar.")
            : at is null ? L.T("Me falta para cuándo es el recordatorio.")
            : at < now.AddMinutes(-1) ? L.T("Esa hora ya pasó ({0:g}).", at)
            : null;
        return problem is null ? new Reminder { Id = Guid.NewGuid().ToString("N")[..8], Text = text!, At = at!.Value } : null;
    }

    public static void Add(Reminder reminder)
    {
        var list = Load();
        list.Add(reminder);
        Save(list);
    }

    /// <summary>Los que ya llegaron y no se avisaron: quedan avisados.</summary>
    public static List<Reminder> TakeDue(DateTime now)
    {
        var list = Load();
        var due = list.Where(r => !r.Done && r.At <= now).OrderBy(r => r.At).ToList();
        if (due.Count == 0) return due;
        due.ForEach(r => r.Done = true);
        Save(list);
        return due;
    }

    public static List<Reminder> Pending() => Load().Where(r => !r.Done).OrderBy(r => r.At).ToList();

    /// <summary>Cancela el próximo que tenga esas palabras (o el próximo, sin palabras); null si no hay.</summary>
    public static Reminder? Cancel(string words)
    {
        var list = Load();
        var found = list.Where(r => !r.Done && (words.Length == 0 || AgendaJson.HasAll(r.Text, words))).OrderBy(r => r.At).FirstOrDefault();
        if (found is null) return null;
        found.Done = true;
        Save(list);
        return found;
    }
}
