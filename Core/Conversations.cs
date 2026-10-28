using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>Una conversación: la lista de turnos, o la de la isla y la nubecita, o una guardada de Claupet Console.</summary>
interface IChatLog
{
    IReadOnlyList<ChatTurn> Turns { get; }
    void Add(ChatTurn turn);
}

/// <summary>
/// Una conversación mientras Claupet responde, y dónde se ve. Streaming es lo que va llegando de la respuesta
/// (null si no está escribiendo); Show(asking) la muestra donde corresponda: la isla o la Console.
/// </summary>
sealed class ChatTarget(IChatLog log, AssistantVoice voice, bool island, Action<bool> show)
{
    public IChatLog Log => log;
    public AssistantVoice Voice => voice;
    /// <summary>Si pide permiso para una acción, lo pregunta en la isla (si no, en la nubecita o en un aviso).</summary>
    public bool Island => island;
    public string? Streaming { get; set; }
    public bool Asking { get; private set; }

    public void Show(bool asking)
    {
        Asking = asking;
        show(asking);
    }
}

/// <summary>Una conversación guardada de Claupet Console.</summary>
sealed class Conversation
{
    public required string Id { get; init; }
    public string Title { get; set; } = "";
    public DateTimeOffset Updated { get; set; }
    public List<ChatTurn> Turns { get; init; } = [];
}

/// <summary>Lo que muestra la lista de chats de la Console.</summary>
sealed record ChatSummary(string Id, string Title, string Preview, DateTimeOffset Updated, bool Quick = false);

/// <summary>Una conversación abierta en la Console: sus turnos, lo que va llegando y el modelo de la cabecera.</summary>
sealed record ChatView(string Id, string Title, IReadOnlyList<ChatTurn> Turns, string? Streaming, bool Asking, string Model, bool ReadAloud);

/// <summary>Una conversación guardada como IChatLog: cada turno nuevo se guarda; la primera pregunta le da el título.</summary>
sealed class SavedChat(Conversation conversation, ConversationStore store) : IChatLog
{
    public Conversation Conversation => conversation;
    public IReadOnlyList<ChatTurn> Turns => conversation.Turns;

    public void Add(ChatTurn turn)
    {
        conversation.Turns.Add(turn);
        if (conversation.Title.Length == 0 && turn.FromPerson) conversation.Title = Conversations.TitleFor(turn.Text);
        conversation.Updated = DateTimeOffset.Now;
        store.Save(conversation);
    }
}

/// <summary>
/// Las conversaciones de Claupet Console, una por archivo en conversaciones\&lt;id&gt;.json, junto a los ajustes.
/// Una conversación nueva no se escribe hasta su primer mensaje.
/// </summary>
sealed class ConversationStore(string folder)
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultFolder => DataFolder.In("conversaciones");

    /// <summary>Una conversación nueva, con el id que eligió la Console (si sirve) o uno nuevo.</summary>
    public Conversation Create(string? id = null) =>
        new() { Id = id is not null && ValidId(id) ? id : Guid.NewGuid().ToString("N")[..12], Updated = DateTimeOffset.Now };

    public List<Conversation> List()
    {
        var list = new List<Conversation>();
        if (!Directory.Exists(folder)) return list;
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
            if (Read(file) is { } conversation) list.Add(conversation);
        return list.OrderByDescending(c => c.Updated).ToList();
    }

    public Conversation? Load(string id) => ValidId(id) ? Read(PathOf(id)) : null;

    public void Save(Conversation conversation)
    {
        if (!ValidId(conversation.Id) || conversation.Turns.Count == 0) return;
        try
        {
            Directory.CreateDirectory(folder);
            var path = PathOf(conversation.Id);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(conversation, Options), new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
    }

    public void Delete(string id)
    {
        if (!ValidId(id)) return;
        try { File.Delete(PathOf(id)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
    }

    string PathOf(string id) => Path.Combine(folder, id + ".json");

    /// <summary>Solo letras y números: el id va en el nombre del archivo.</summary>
    static bool ValidId(string id) => id.Length is > 0 and <= 40 && id.All(char.IsAsciiLetterOrDigit);

    static Conversation? Read(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Conversation>(File.ReadAllText(path), Options) : null; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ErrorLog.Write(ex);
            return null;
        }
    }
}

/// <summary>Un trozo de una respuesta: texto corrido o un bloque de código (con su lenguaje, si lo dice).</summary>
sealed record ChatSegment(string Text, bool Code = false, string Language = "");

/// <summary>Un trozo de una línea: texto o `código`.</summary>
sealed record ChatSpan(string Text, bool Code = false);

static class Conversations
{
    /// <summary>El título de una conversación: el comienzo de la primera pregunta, en una línea.</summary>
    public static string TitleFor(string text)
    {
        var line = Regex.Replace(text.Trim(), @"\s+", " ");
        return line.Length <= 48 ? line : line[..47].TrimEnd() + "…";
    }

    /// <summary>Lo que se ve debajo del título en la lista: el último turno, en una línea.</summary>
    public static string PreviewOf(IReadOnlyList<ChatTurn> turns) =>
        turns.Count == 0 ? "" : TitleFor(turns[^1].Text.Replace("```", ""));

    /// <summary>El grupo de la lista según cuándo se usó: Hoy, Ayer, Esta semana o Antes.</summary>
    public static string GroupOf(DateTimeOffset updated, DateTime now)
    {
        var day = updated.LocalDateTime.Date;
        var today = now.Date;
        if (day >= today) return L.T("Hoy");
        if (day == today.AddDays(-1)) return L.T("Ayer");
        return day > today.AddDays(-7) ? L.T("Esta semana") : L.T("Antes");
    }

    /// <summary>Separa los bloques de código (entre ```) del texto corrido. Un bloque sin cerrar (aún llegando) llega hasta el final.</summary>
    public static List<ChatSegment> Split(string text)
    {
        var segments = new List<ChatSegment>();
        var at = 0;
        while (at < text.Length)
        {
            var open = text.IndexOf("```", at, StringComparison.Ordinal);
            if (open < 0)
            {
                AddText(segments, text[at..]);
                break;
            }
            AddText(segments, text[at..open]);
            var lineEnd = text.IndexOf('\n', open + 3);
            var language = (lineEnd < 0 ? text[(open + 3)..] : text[(open + 3)..lineEnd]).Trim();
            if (lineEnd < 0)
            {
                segments.Add(new ChatSegment("", Code: true, language));
                break;
            }
            var close = text.IndexOf("```", lineEnd + 1, StringComparison.Ordinal);
            var code = close < 0 ? text[(lineEnd + 1)..] : text[(lineEnd + 1)..close];
            segments.Add(new ChatSegment(code.TrimEnd('\n', '\r'), Code: true, language));
            if (close < 0) break;
            at = close + 3;
        }
        return segments;
    }

    static void AddText(List<ChatSegment> segments, string text)
    {
        text = text.Trim('\n', '\r');
        if (text.Trim().Length > 0) segments.Add(new ChatSegment(text));
    }

    /// <summary>Separa el `código` en línea del texto.</summary>
    public static List<ChatSpan> Spans(string text)
    {
        var spans = new List<ChatSpan>();
        var parts = text.Split('`');
        for (var i = 0; i < parts.Length; i++)
        {
            // Una comilla sin pareja se deja como está.
            var code = i % 2 == 1 && i < parts.Length - 1;
            if (parts[i].Length == 0) continue;
            if (i % 2 == 1 && !code) spans.Add(new ChatSpan("`" + parts[i]));
            else spans.Add(new ChatSpan(parts[i], code));
        }
        return spans;
    }
}
