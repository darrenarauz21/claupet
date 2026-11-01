namespace Claupet;

using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

/// <summary>
/// Retomar y pegar en una pestaña de agente: el historial de Claude Code (la pestaña Historial del panel o /resume) abre
/// la sesión elegida con --resume y con lo de antes a la vista; /clear y /new abren una pestaña nueva como esta; Ctrl+V
/// con una imagen la suma al pedido («[Imagen #1]»), como Claude Code en una terminal.
/// </summary>
sealed partial class TerminalPane
{
    /// <summary>Los comandos de la caja que hace la Terminal (los demás le llegan al agente). True si lo hizo.</summary>
    bool Command(Tab tab, string text)
    {
        switch (text.ToLowerInvariant())
        {
            case "/resume":
                // El historial, en el panel de la derecha.
                _projects.PanelOpen = true;
                _projects.PanelTab = "historial";
                _projects.Save();
                Build();
                return true;
            case "/clear" or "/new":
                // Una pestaña nueva como esta (la de ahora sigue, con lo suyo).
                var o = tab.Agent.Options;
                var account = Accounts().FirstOrDefault(a => global::Claupet.Account.SamePath(a.ConfigDir, o.ConfigDir ?? ""));
                OpenAgent(o.Kind, o.Folder, account, o.Model, tab.Agent.Effort ?? o.Effort);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Retoma una sesión anterior de Claude Code en una pestaña nueva: primero se lee su conversación (para verla) y
    /// después se abre Claude Code con --resume, con la cuenta, el modelo y el esfuerzo de la pestaña desde la que se pidió.
    /// Si ya está abierta en otra pestaña, se va a esa.
    /// </summary>
    void Resume(Tab from, PastSession past)
    {
        if (_tabs.FirstOrDefault(t => t.Shell is null && (t.Agent.Transcript.SessionId == past.Id || t.Agent.Options.Resume == past.Id)) is { } open)
        {
            Select(open);
            return;
        }
        var o = from.Agent.Options;
        var configDir = o.ConfigDir ?? global::Claupet.Account.DefaultDir;
        var account = Accounts().FirstOrDefault(a => global::Claupet.Account.SamePath(a.ConfigDir, configDir));
        AgentOptions options;
        if (from.Shell is null)
            options = new AgentOptions(o.Folder, o.ConfigDir, o.Model, from.Agent.Effort ?? o.Effort,
                from.Agent.Transcript.PermissionMode ?? o.PermissionMode, AgentKinds.Claude, past.Id);
        else
        {
            // Desde una pestaña de shell (su historial es el de la cuenta de siempre): el modelo y el modo recordados.
            var last = TerminalRecents.Load();
            options = new AgentOptions(o.Folder, account?.ConfigDir, last.Model, last.Effort, last.Mode ?? "default", AgentKinds.Claude, past.Id);
        }
        var label = account?.Label ?? L.T("cuenta por defecto");
        var agent = new TerminalAgent(options, action => Dispatcher.UIThread.Post(action)) { AskIsland = !Program.Args.Contains("--preview") };
        _ = Task.Run(() =>
        {
            ClaudeHistory.Replay(past.Path, agent.Transcript);
            agent.Transcript.Notice(L.T("Sesión retomada: lo de arriba es lo de antes. Siga escribiendo abajo."));
            Dispatcher.UIThread.Post(() =>
            {
                var tab = new Tab(agent, label);
                agent.Changed += changed => OnChanged(tab, changed);
                Add(tab);
                agent.Start();
                _input.Focus();
            });
        });
    }

    /// <summary>
    /// Ctrl+V en la caja de un agente: una imagen copiada, o la ruta o la URL de una imagen, se suma al pedido como
    /// «[Imagen #N]» (como en Rider o en otras consolas); cualquier otro texto se pega como siempre.
    /// </summary>
    async Task PasteIntoComposerAsync(Tab tab)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        string? text = null;
        try { text = await clipboard.TryGetTextAsync(); }
        catch (Exception) { }
        var image = string.IsNullOrEmpty(text) ? await ClipboardImageAsync(clipboard)
            : PastedImages.Reference(text, tab.Agent.Options.Folder) is { } reference ? await PastedImages.LoadAsync(reference) : null;
        if (image is null)
        {
            _input.Paste();
            return;
        }
        tab.Images.Add(image);
        var token = $"[Imagen #{tab.Images.Count}] ";
        var at = Math.Clamp(_input.CaretIndex, 0, _input.Text?.Length ?? 0);
        _input.Text = (_input.Text ?? "").Insert(at, token);
        _input.CaretIndex = at + token.Length;
        _input.Focus();
    }

    /// <summary>La imagen del portapapeles (una captura, «Copiar imagen») o un archivo de imagen copiado; null si no hay.</summary>
    static async Task<AgentImage?> ClipboardImageAsync(IClipboard clipboard)
    {
        try
        {
            if (await clipboard.TryGetBitmapAsync() is { } bitmap)
            {
                using var png = new MemoryStream();
                bitmap.Save(png);
                bitmap.Dispose();
                return new AgentImage("image/png", Convert.ToBase64String(png.ToArray()));
            }
            if (await clipboard.TryGetFilesAsync() is { } files)
                foreach (var file in files)
                    if (file.TryGetLocalPath() is { } path && ImageType(path) is { } type && new FileInfo(path) is { Exists: true, Length: < 20_000_000 })
                        return new AgentImage(type, Convert.ToBase64String(await File.ReadAllBytesAsync(path)));
        }
        catch (Exception) { }
        return null;
    }

    static string? ImageType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => null,
    };
}
