using System.Diagnostics;

namespace Claupet;

/// <summary>
/// Claupet Console, la ventana grande, corre aparte (Claupet.exe --console) y habla con la bandeja por una
/// tubería: la bandeja le manda las secciones de Ajustes ya armadas y la Console le pide las acciones de sus
/// filas, las mismas de los ajustes rápidos. Las listas para elegir se muestran allá; solo la bandeja guarda.
/// </summary>
sealed partial class TrayApp
{
    readonly ConsoleServer _console = new();
    /// <summary>La acción que se está atendiendo la pidió la Console (y esta es su fila): sus listas se eligen allá.</summary>
    string? _consoleKey;
    int _consoleToken;
    readonly Dictionary<int, (Action<string> Pick, Action? Closed)> _consolePicks = new();

    void StartConsoleServer()
    {
        _console.Connected += () => PostUi(PushSettings);
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
        }
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

    /// <summary>Abre Claupet Console o, si ya está abierta, la trae al frente (en una sección, si se indica).</summary>
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
