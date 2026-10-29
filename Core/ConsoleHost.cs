using System.Diagnostics;

namespace Claupet;

/// <summary>
/// Lo que cada bandeja (la de Windows o la de la app Avalonia) le da a Claupet Console: los ajustes de este sistema, sus
/// acciones y el asistente que responde el chat. El resto (la tubería, las conversaciones, las listas para elegir) es
/// igual en todos los sistemas y lo hace ConsoleHost.
/// </summary>
interface IConsoleApp
{
    /// <summary>Lo que muestra Ajustes, ya listo (sin las filas de lo que este sistema todavía no tiene).</summary>
    SettingsState SettingsState();
    string Palette { get; }
    /// <summary>Hace la acción de una fila; si muestra una lista para elegir, la pasa por ConsoleHost.Offer.</summary>
    void Act(string key);
    /// <summary>Lo escrito en un cuadro de Ajustes que no es de una extensión; false si esa clave no espera texto.</summary>
    bool Typed(string key, string value) => false;
    /// <summary>La conversación rápida: la de la isla y la nubecita donde las hay.</summary>
    ChatTarget QuickChat { get; }
    /// <summary>Lo que se ve debajo de «Conversación rápida» mientras está vacía.</summary>
    string QuickHint { get; }
    /// <summary>Lo que dice la conversación rápida abierta y vacía: qué es.</summary>
    string QuickAbout { get; }
    void AskQuick(string text);
    void StopQuick();
    void ClearQuick();
    /// <summary>Pregunta en una conversación guardada; lo que llega lo muestra chat.Show.</summary>
    Task AskAsync(ChatTarget chat, string text, CancellationToken cancel);
    /// <summary>El modelo que dice la cabecera del chat.</summary>
    string ChatModel { get; }
    bool ReadAloud { get; }
    /// <summary>Corre en el hilo de la interfaz de la bandeja.</summary>
    void Post(Action action);
}

/// <summary>
/// La bandeja del lado de Claupet Console, igual en Windows, Linux y macOS: atiende la tubería, le manda las secciones
/// de Ajustes ya armadas y le pide a la bandeja las acciones de sus filas (las listas se muestran allá; solo la bandeja
/// guarda). El chat también se atiende aquí: las conversaciones se guardan en el equipo y responde el asistente de la app.
/// La bandeja que carga extensiones llama a UseExtensions: Ajustes › Extensiones las muestra y las maneja (ExtensionsPanel).
/// </summary>
sealed class ConsoleHost(IConsoleApp app) : IDisposable, IConsoleSurface
{
    readonly ConsoleServer _server = new();
    readonly ConversationStore _conversations = new(ConversationStore.DefaultFolder);
    readonly Dictionary<int, (Action<string> Pick, Action? Closed)> _picks = new();
    /// <summary>Las conversaciones guardadas abiertas o respondiendo, por id.</summary>
    readonly Dictionary<string, ChatTarget> _chats = new();
    readonly Dictionary<string, CancellationTokenSource> _asking = new();
    /// <summary>La acción que se está atendiendo la pidió la Console (y esta es su fila): sus listas se eligen allá.</summary>
    string? _key;
    int _token;
    /// <summary>La conversación que muestra la Console (null: está en otra parte o cerrada).</summary>
    string? _chatId;
    ExtensionsPanel? _extensions;

    public bool IsConnected => _server.IsConnected;

    /// <summary>Ajustes › Extensiones de la Console, para la bandeja que tiene un ExtensionManager. Una vez, al arrancar.</summary>
    public ExtensionsPanel UseExtensions(IExtensionsHost host) => _extensions = new ExtensionsPanel(host, this);

    /// <summary>El panel de las extensiones; null si esta bandeja no las carga.</summary>
    public ExtensionsPanel? Extensions => _extensions;

    public void Start()
    {
        _server.Connected += () => app.Post(() =>
        {
            _chatId = null; // la Console dice cuál abre
            PushSettings();
        });
        _server.Received += message => app.Post(() => OnMessage(message));
        _server.Start();
    }

    public void Dispose() => _server.Dispose();

    void OnMessage(ConsoleMessage message)
    {
        switch (message.Type)
        {
            case ConsoleLink.Action when message.Key is { Length: > 0 } key:
                _key = key;
                try
                {
                    if (_extensions is { } extensions && ExtensionsPanel.Handles(key)) extensions.Act(key, message.Value);
                    else if (message.Value is not { } typed || !app.Typed(key, typed)) app.Act(key);
                }
                finally { _key = null; }
                break;
            case ConsoleLink.Pick when _picks.Remove(message.Token, out var picked):
                if (message.Value is { } value) picked.Pick(value);
                picked.Closed?.Invoke();
                PushSettings();
                break;
            case ConsoleLink.ChatOpen:
                _chatId = message.Key is { Length: > 0 } open && Chat(open) is not null ? open : ConsoleLink.QuickChat;
                PushChats();
                PushChat(_chatId);
                break;
            case ConsoleLink.ChatNew:
                // La Console pone el id, para saber cuál mostrar sin esperar la respuesta.
                var created = _conversations.Create(message.Key);
                _chats[created.Id] = SavedTarget(created);
                _chatId = created.Id;
                PushChats();
                PushChat(created.Id);
                break;
            case ConsoleLink.ChatSend when message.Key is { Length: > 0 } id && message.Value is { Length: > 0 } text:
                if (id == ConsoleLink.QuickChat) app.AskQuick(text);
                else if (Chat(id) is { } chat)
                {
                    if (_asking.Remove(id, out var previous)) previous.Cancel();
                    var cancel = _asking[id] = new CancellationTokenSource();
                    _ = app.AskAsync(chat, text, cancel.Token);
                }
                break;
            case ConsoleLink.ChatStop when message.Key is { Length: > 0 } id:
                if (id == ConsoleLink.QuickChat) app.StopQuick();
                else if (_asking.Remove(id, out var asking)) asking.Cancel();
                break;
            case ConsoleLink.ChatDelete when message.Key is { Length: > 0 } id && id != ConsoleLink.QuickChat:
                if (_asking.Remove(id, out var running)) running.Cancel();
                _chats.Remove(id);
                _conversations.Delete(id);
                if (_chatId == id) _chatId = ConsoleLink.QuickChat;
                PushChats();
                PushChat(_chatId);
                break;
            case ConsoleLink.ChatClear:
                app.ClearQuick();
                PushChats();
                break;
        }
    }

    /// <summary>
    /// Si la acción vino de la Console, la lista se le manda para que la muestre junto a su fila y la respuesta llega
    /// como «elegir». false: la bandeja muestra su menú de siempre.
    /// </summary>
    public bool Offer(IEnumerable<(string Id, string Label)> options, string selected, Action<string> pick, Action? onClosed) =>
        Offer(options.Select(o => new ConsoleOption(o.Id, o.Label)).ToList(), selected, pick, onClosed);

    /// <summary>Lo mismo, con opciones que pueden ir en rojo (desinstalar, revocar).</summary>
    public bool Offer(IReadOnlyList<ConsoleOption> options, string selected, Action<string> pick, Action? onClosed = null)
    {
        if (_key is null || !_server.IsConnected) return false;
        var token = ++_token;
        _picks[token] = (pick, onClosed);
        _server.Send(new ConsoleMessage
        {
            Type = ConsoleLink.Options,
            Key = _key,
            Token = token,
            Value = selected,
            Options = options,
        });
        return true;
    }

    /// <summary>Las secciones de Ajustes (con el estado ya armado, si se tiene a mano) y la conversación abierta.</summary>
    public void PushSettings(SettingsState? state = null)
    {
        if (!_server.IsConnected) return;
        var pages = SettingsPages.Build(state ?? app.SettingsState());
        _server.Send(new ConsoleMessage
        {
            Type = ConsoleLink.State,
            Language = L.Language,
            Palette = app.Palette,
            Pages = _extensions is { } extensions ? extensions.Merge(pages) : pages,
        });
        // El modelo y «leer en voz alta» también están en la cabecera del chat.
        PushChat(_chatId);
    }

    // Lo que el panel de las extensiones le pide a la Console.
    bool IConsoleSurface.Offer(IReadOnlyList<ConsoleOption> options, string selected, Action<string> pick) => Offer(options, selected, pick);
    void IConsoleSurface.Push() => PushSettings();
    void IConsoleSurface.Post(Action action) => app.Post(action);

    void IConsoleSurface.Show(string section)
    {
        if (Open(section) is { } problem) ErrorLog.Write(new InvalidOperationException(problem));
    }

    /// <summary>Cambió la conversación rápida (la isla respondió, se vació…): se ve en la Console si está abierta.</summary>
    public void QuickChanged(bool asking)
    {
        if (_chatId == ConsoleLink.QuickChat) PushChat(ConsoleLink.QuickChat);
        if (!asking && _chatId is not null) PushChats(); // lo que se ve debajo de «Conversación rápida»
    }

    /// <summary>La conversación con ese id: la rápida o una guardada; null si no existe.</summary>
    ChatTarget? Chat(string id)
    {
        if (id == ConsoleLink.QuickChat) return app.QuickChat;
        if (_chats.TryGetValue(id, out var open)) return open;
        if (_conversations.Load(id) is not { } conversation) return null;
        return _chats[id] = SavedTarget(conversation);
    }

    ChatTarget SavedTarget(Conversation conversation) =>
        new(new SavedChat(conversation, _conversations), AssistantVoice.Console, island: false, asking =>
        {
            PushChat(conversation.Id);
            // Al llegar un turno cambian el título, el orden y lo que se ve debajo en la lista.
            if (!asking) PushChats();
        });

    /// <summary>La lista de conversaciones: la rápida arriba y las guardadas de la más reciente a la más vieja.</summary>
    void PushChats()
    {
        if (!_server.IsConnected) return;
        var quick = app.QuickChat.Log.Turns;
        var chats = new List<ChatSummary>
        {
            new(ConsoleLink.QuickChat, L.T("Conversación rápida"), quick.Count > 0 ? Conversations.PreviewOf(quick) : app.QuickHint,
                DateTimeOffset.Now, Quick: true),
        };
        var saved = _conversations.List();
        // Una nueva sin mensajes todavía no está en disco: se muestra igual mientras está abierta.
        foreach (var open in _chats.Values.Select(c => ((SavedChat)c.Log).Conversation))
            if (open.Turns.Count == 0 && open.Id == _chatId) saved.Insert(0, open);
        chats.AddRange(saved.Select(c => new ChatSummary(c.Id, c.Title.Length > 0 ? c.Title : L.T("Conversación nueva"),
            Conversations.PreviewOf(c.Turns), c.Updated)));
        _server.Send(new ConsoleMessage { Type = ConsoleLink.Chats, Chats = chats });
    }

    /// <summary>La conversación abierta en la Console, con lo que va llegando; solo si es la que muestra.</summary>
    void PushChat(string? id)
    {
        if (id is null || id != _chatId || !_server.IsConnected || Chat(id) is not { } chat) return;
        var title = chat.Log is SavedChat { Conversation: var c } ? c.Title.Length > 0 ? c.Title : L.T("Conversación nueva") : L.T("Conversación rápida");
        _server.Send(new ConsoleMessage
        {
            Type = ConsoleLink.Chat,
            Chat = new ChatView(id, title, chat.Log.Turns.ToList(), chat.Streaming, chat.Asking, app.ChatModel, app.ReadAloud,
                id == ConsoleLink.QuickChat ? app.QuickAbout : null),
        });
    }

    /// <summary>
    /// Abre Claupet Console o, si ya está abierta, la trae al frente: en una sección de Ajustes, o en el chat si no se
    /// indica. null si se abrió; si no, por qué no se pudo.
    /// </summary>
    public string? Open(string? section = null)
    {
        if (_server.IsConnected)
        {
            _server.Send(new ConsoleMessage { Type = ConsoleLink.Show, Key = section });
            return null;
        }
        // Es el mismo ejecutable, en otro proceso: la bandeja no carga la ventana grande.
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
            start.ArgumentList.Add(ConsoleLink.Flag);
            if (section is not null) start.ArgumentList.Add("--seccion=" + section);
            Process.Start(start)?.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            ErrorLog.Write(ex);
            return ex.Message;
        }
    }
}
