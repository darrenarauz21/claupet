namespace Claupet;

/// <summary>
/// Claupet Console del lado de la bandeja de Windows. Lo que es igual en todos los sistemas (la tubería, las
/// conversaciones, las listas para elegir) lo hace ConsoleHost; aquí va lo de esta app: sus ajustes, sus acciones y
/// el asistente de la isla, que también responde el chat.
/// </summary>
sealed partial class TrayApp : IConsoleApp
{
    ConsoleHost? _consoleHost;
    ConsoleHost ConsoleHost => _consoleHost ??= new ConsoleHost(this);

    void StartConsoleServer() => ConsoleHost.Start();

    SettingsState IConsoleApp.SettingsState() => BuildSettingsState();
    string IConsoleApp.Palette => _settings.Palette;
    void IConsoleApp.Act(string key) => OnSetting(key);
    ChatTarget IConsoleApp.QuickChat => _islandChat;
    string IConsoleApp.QuickHint => L.T("La de la isla y la nubecita");
    string IConsoleApp.QuickAbout => L.T("Es la misma conversación de la isla y la nubecita; se olvida a los 10 minutos sin escribir.");
    void IConsoleApp.AskQuick(string text) => _ = AskIslandAsync(text);
    void IConsoleApp.StopQuick() => _islandAsking?.Cancel();

    void IConsoleApp.ClearQuick()
    {
        _islandAsking?.Cancel();
        _chat.Clear();
        _islandChat.Show(false);
    }

    Task IConsoleApp.AskAsync(ChatTarget chat, string text, CancellationToken cancel) => AskChatAsync(chat, text, cancel);
    string IConsoleApp.ChatModel => _settings.IslandModel is { } m ? ModelLabel(m) : _defaultModel ?? AssistantModels.Label(null);
    bool IConsoleApp.ReadAloud => _settings.ReadAloud;
    void IConsoleApp.Post(Action action) => PostUi(action);

    /// <summary>
    /// Si la acción vino de la Console, la lista se le manda para que la muestre junto a su fila y la respuesta
    /// llega como «elegir». false: se muestra el menú de siempre.
    /// </summary>
    bool OfferToConsole(IEnumerable<(string Id, string Label)> options, string selected, Action<string> pick, Action? onClosed) =>
        ConsoleHost.Offer(options, selected, pick, onClosed);

    /// <summary>Abre Claupet Console o, si ya está abierta, la trae al frente: en una sección de Ajustes, o en el chat si no se indica.</summary>
    void OpenConsole(string? section = null)
    {
        if (ConsoleHost.Open(section) is { } problem) MessageBox.Show(L.T("No se pudo abrir Claupet Console: {0}", problem), "Claupet");
    }
}
