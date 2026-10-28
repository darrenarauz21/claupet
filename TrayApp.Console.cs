using System.Diagnostics;

namespace Claupet;

/// <summary>
/// Claupet Console, la ventana grande, corre aparte (Claupet.exe --console) y habla con la bandeja por una
/// tubería: la bandeja le manda las secciones de Ajustes ya armadas y la Console le pide las acciones de sus
/// filas, las mismas de los ajustes rápidos. Las listas para elegir se muestran allá; solo la bandeja guarda.
/// El chat también lo atiende la bandeja: las conversaciones se guardan aquí y responde el mismo asistente de la isla.
/// </summary>
sealed partial class TrayApp
{
    readonly ConsoleServer _console = new();
    /// <summary>La acción que se está atendiendo la pidió la Console (y esta es su fila): sus listas se eligen allá.</summary>
    string? _consoleKey;
    int _consoleToken;
    readonly Dictionary<int, (Action<string> Pick, Action? Closed)> _consolePicks = new();
    readonly ConversationStore _conversations = new(ConversationStore.DefaultFolder);
    /// <summary>Las conversaciones de la Console abiertas o respondiendo, por id.</summary>
    readonly Dictionary<string, ChatTarget> _consoleChats = new();
    readonly Dictionary<string, CancellationTokenSource> _consoleAsking = new();
    /// <summary>La conversación que muestra la Console (null: está en Ajustes o cerrada).</summary>
    string? _consoleChatId;

    void StartConsoleServer()
    {
        _console.Connected += () => PostUi(() =>
        {
            _consoleChatId = null; // la Console dice cuál abre
            PushSettings();
        });
        _console.Received += message => PostUi(() => OnConsoleMessage(message));
        _console.Start();
    }

    void OnConsoleMessage(ConsoleMessage message)
    {
        switch (message.Type)
        {
            case ConsoleLink.Action when message.Key is { Length: > 0 } key:
                _consoleKey = key;
                try { OnSetting(key); }
                finally { _consoleKey = null; }
                break;
            case ConsoleLink.Pick when _consolePicks.Remove(message.Token, out var picked):
                if (message.Value is { } value) picked.Pick(value);
                picked.Closed?.Invoke();
                PushSettings();
                break;
            case ConsoleLink.ChatOpen:
                _consoleChatId = message.Key is { Length: > 0 } open && ConsoleChat(open) is not null ? open : ConsoleLink.QuickChat;
                PushConsoleChats();
                PushConsoleChat(_consoleChatId);
                break;
            case ConsoleLink.ChatNew:
                // La Console pone el id, para saber cuál mostrar sin esperar la respuesta.
                var created = _conversations.Create(message.Key);
                _consoleChats[created.Id] = SavedTarget(created);
                _consoleChatId = created.Id;
                PushConsoleChats();
                PushConsoleChat(created.Id);
                break;
            case ConsoleLink.ChatSend when message.Key is { Length: > 0 } id && message.Value is { Length: > 0 } text:
                if (id == ConsoleLink.QuickChat) _ = AskIslandAsync(text);
                else if (ConsoleChat(id) is { } chat)
                {
                    if (_consoleAsking.Remove(id, out var previous)) previous.Cancel();
                    var cancel = _consoleAsking[id] = new CancellationTokenSource();
                    _ = AskChatAsync(chat, text, cancel.Token);
                }
                break;
            case ConsoleLink.ChatStop when message.Key is { Length: > 0 } id:
                if (id == ConsoleLink.QuickChat) _islandAsking?.Cancel();
                else if (_consoleAsking.Remove(id, out var asking)) asking.Cancel();
                break;
            case ConsoleLink.ChatDelete when message.Key is { Length: > 0 } id && id != ConsoleLink.QuickChat:
                if (_consoleAsking.Remove(id, out var running)) running.Cancel();
                _consoleChats.Remove(id);
                _conversations.Delete(id);
                if (_consoleChatId == id) _consoleChatId = ConsoleLink.QuickChat;
                PushConsoleChats();
                PushConsoleChat(_consoleChatId);
                break;
            case ConsoleLink.ChatClear:
                _islandAsking?.Cancel();
                _chat.Clear();
                _islandChat.Show(false);
                PushConsoleChats();
                break;
        }
    }

    /// <summary>La conversación con ese id: la rápida (la de la isla) o una guardada; null si no existe.</summary>
    ChatTarget? ConsoleChat(string id)
    {
        if (id == ConsoleLink.QuickChat) return _islandChat;
        if (_consoleChats.TryGetValue(id, out var open)) return open;
        if (_conversations.Load(id) is not { } conversation) return null;
        return _consoleChats[id] = SavedTarget(conversation);
    }

    ChatTarget SavedTarget(Conversation conversation) =>
        new(new SavedChat(conversation, _conversations), AssistantVoice.Console, island: false, asking =>
        {
            PushConsoleChat(conversation.Id);
            // Al llegar un turno cambian el título, el orden y lo que se ve debajo en la lista.
            if (!asking) PushConsoleChats();
        });

    /// <summary>La lista de conversaciones: la rápida arriba y las guardadas de la más reciente a la más vieja.</summary>
    void PushConsoleChats()
    {
        if (!_console.IsConnected) return;
        var quick = _chat.Turns;
        var chats = new List<ChatSummary>
        {
            new(ConsoleLink.QuickChat, L.T("Conversación rápida"),
                quick.Count > 0 ? Conversations.PreviewOf(quick) : L.T("La de la isla y la nubecita"), DateTimeOffset.Now, Quick: true),
        };
        var saved = _conversations.List();
        // Una nueva sin mensajes todavía no está en disco: se muestra igual mientras está abierta.
        foreach (var open in _consoleChats.Values.Select(c => ((SavedChat)c.Log).Conversation))
            if (open.Turns.Count == 0 && open.Id == _consoleChatId) saved.Insert(0, open);
        chats.AddRange(saved.Select(c => new ChatSummary(c.Id, c.Title.Length > 0 ? c.Title : L.T("Conversación nueva"),
            Conversations.PreviewOf(c.Turns), c.Updated)));
        _console.Send(new ConsoleMessage { Type = ConsoleLink.Chats, Chats = chats });
    }

    /// <summary>La conversación abierta en la Console, con lo que va llegando; solo si es la que muestra.</summary>
    void PushConsoleChat(string? id)
    {
        if (id is null || id != _consoleChatId || !_console.IsConnected || ConsoleChat(id) is not { } chat) return;
        var title = chat.Log is SavedChat { Conversation: var c } ? c.Title.Length > 0 ? c.Title : L.T("Conversación nueva") : L.T("Conversación rápida");
        var model = _settings.IslandModel is { } m ? ModelLabel(m) : _defaultModel ?? AssistantModels.Label(null);
        _console.Send(new ConsoleMessage
        {
            Type = ConsoleLink.Chat,
            Chat = new ChatView(id, title, chat.Log.Turns.ToList(), chat.Streaming, chat.Asking, model, _settings.ReadAloud),
        });
    }

    /// <summary>Las secciones de Ajustes para la Console, si hay una abierta.</summary>
    void SendConsoleState(SettingsState state)
    {
        if (!_console.IsConnected) return;
        _console.Send(new ConsoleMessage
        {
            Type = ConsoleLink.State,
            Language = L.Language,
            Palette = _settings.Palette,
            Pages = SettingsPages.Build(state),
        });
        // El modelo y «leer en voz alta» también están en la cabecera del chat.
        PushConsoleChat(_consoleChatId);
    }

    /// <summary>
    /// Si la acción vino de la Console, la lista se le manda para que la muestre junto a su fila y la respuesta
    /// llega como «elegir». false: se muestra el menú de siempre.
    /// </summary>
    bool OfferToConsole(IEnumerable<(string Id, string Label)> options, string selected, Action<string> pick, Action? onClosed)
    {
        if (_consoleKey is null || !_console.IsConnected) return false;
        var token = ++_consoleToken;
        _consolePicks[token] = (pick, onClosed);
        _console.Send(new ConsoleMessage
        {
            Type = ConsoleLink.Options,
            Key = _consoleKey,
            Token = token,
            Value = selected,
            Options = options.Select(o => new ConsoleOption(o.Id, o.Label)).ToList(),
        });
        return true;
    }

    /// <summary>Abre Claupet Console o, si ya está abierta, la trae al frente: en una sección de Ajustes, o en el chat si no se indica.</summary>
    void OpenConsole(string? section = null)
    {
        if (_console.IsConnected)
        {
            _console.Send(new ConsoleMessage { Type = ConsoleLink.Show, Key = section });
            return;
        }
        // Es el mismo ejecutable, en otro proceso: la bandeja no carga la ventana grande.
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
            start.ArgumentList.Add(ConsoleApp.Flag);
            if (section is not null) start.ArgumentList.Add("--seccion=" + section);
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            ErrorLog.Write(ex);
            MessageBox.Show(L.T("No se pudo abrir Claupet Console: {0}", ex.Message), "Claupet");
        }
    }
}
