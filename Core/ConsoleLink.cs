using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claupet;

/// <summary>Una opción de una lista que la bandeja le pide elegir a Claupet Console.</summary>
sealed record ConsoleOption(string Id, string Label);

/// <summary>
/// Un mensaje entre Claupet (la bandeja) y Claupet Console, una línea de JSON cada uno.
/// De la bandeja: «estado» (las secciones ya armadas, el idioma y los colores), «opciones» (una lista para
/// elegir, con su número), «mostrar» (traer la ventana al frente, en una sección o en «chat»), «chats» (la lista
/// de conversaciones) y «chat» (la conversación abierta, con lo que va llegando).
/// De la Console: «accion» (se tocó la fila Key), «elegir» (Value de la lista Token; sin Value, se cerró sin
/// elegir) y los del chat, con la conversación en Key: «chat-abrir», «chat-nuevo» (con el id que elige la
/// Console), «chat-enviar» (Value: el mensaje), «chat-parar», «chat-borrar» y «chat-limpiar» (vacía la
/// conversación rápida).
/// </summary>
sealed record ConsoleMessage
{
    public string Type { get; init; } = "";
    public string? Key { get; init; }
    public string? Value { get; init; }
    public int Token { get; init; }
    public string? Language { get; init; }
    public string? Palette { get; init; }
    public IReadOnlyList<SettingsPage>? Pages { get; init; }
    public IReadOnlyList<ConsoleOption>? Options { get; init; }
    public IReadOnlyList<ChatSummary>? Chats { get; init; }
    public ChatView? Chat { get; init; }
}

/// <summary>
/// El canal con Claupet Console: una tubería con nombre que solo abre el mismo usuario de Windows.
/// Solo la bandeja guarda los ajustes; la Console muestra lo que le manda y le pide los cambios.
/// </summary>
static class ConsoleLink
{
    public const string State = "estado", Options = "opciones", Show = "mostrar", Action = "accion", Pick = "elegir";
    public const string Chats = "chats", Chat = "chat", ChatOpen = "chat-abrir", ChatNew = "chat-nuevo", ChatSend = "chat-enviar",
        ChatStop = "chat-parar", ChatDelete = "chat-borrar", ChatClear = "chat-limpiar";

    /// <summary>La conversación de la isla y la nubecita, que la Console muestra arriba de las guardadas.</summary>
    public const string QuickChat = "rapida";

    /// <summary>Una tubería por usuario: con otra sesión abierta en el equipo, cada una tiene la suya.</summary>
    public static string PipeName => "Claupet.Console." + Environment.UserName;

    static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Encode(ConsoleMessage message) => JsonSerializer.Serialize(message, Json);

    /// <summary>null si la línea no es un mensaje (no se corta el canal por una línea rota).</summary>
    public static ConsoleMessage? Decode(string line)
    {
        try { return JsonSerializer.Deserialize<ConsoleMessage>(line, Json); }
        catch (JsonException) { return null; }
    }
}
