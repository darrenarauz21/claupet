namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
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
    void Resume(Tab from, PastSession past, bool replace = false, string draft = "")
    {
        if (past.Kind != AgentKinds.Claude)
        {
            ResumeOther(from.Agent.Options.Folder, past);
            return;
        }
        if (_tabs.FirstOrDefault(t => (!replace || t != from) && t.Shell is null && (t.Agent.Transcript.SessionId == past.Id || t.Agent.Options.Resume == past.Id)) is { } open)
        {
            Select(open);
            // La conversación ya estaba abierta en otra pestaña: sigue ahí, con lo escrito si se escribió algo.
            if (replace) ReplaceEnded(from, open, draft.Length > 0 ? draft : _input.Text ?? "");
            return;
        }
        var o = from.Agent.Options;
        var configDir = o.ConfigDir ?? global::Claupet.Account.DefaultDir;
        var account = Accounts().FirstOrDefault(a => global::Claupet.Account.SamePath(a.ConfigDir, configDir));
        AgentOptions options;
        if (from.Shell is null)
            options = new AgentOptions(o.Folder, o.ConfigDir, o.Model, from.Agent.Effort ?? o.Effort,
                from.FollowsSession ? null : from.Agent.Transcript.PermissionMode ?? o.PermissionMode, AgentKinds.Claude, past.Id);
        else
        {
            // Desde una pestaña de shell (su historial es el de la cuenta de siempre): el modelo y el modo recordados.
            var last = TerminalRecents.Load();
            options = new AgentOptions(o.Folder, account?.ConfigDir, last.Model, last.Effort, null, AgentKinds.Claude, past.Id);
        }
        StartResumed(options, account?.Label ?? L.T("cuenta por defecto"), past, replace ? tab => ReplaceEnded(from, tab, draft) : null);
    }

    /// <summary>
    /// Retoma la sesión de otro agente (Codex, Gemini CLI, Copilot CLI, OpenCode) en una pestaña nueva: el agente la abre
    /// con lo suyo (thread/resume, session/load) y repite lo de antes. Si ya está abierta en otra pestaña, se va a esa.
    /// </summary>
    void ResumeOther(string folder, PastSession past)
    {
        if (_tabs.FirstOrDefault(t => t.Shell is null && (t.Agent.Transcript.SessionId == past.Id || t.Agent.Options.Resume == past.Id)) is { } open)
        {
            Select(open);
            return;
        }
        if (!Directory.Exists(folder)) return;
        OtherHistory.Forget(past.Kind, folder);
        var agent = NewAgent(new AgentOptions(folder, Kind: past.Kind, Resume: past.Id));
        var tab = new Tab(agent, AgentKinds.Label(past.Kind));
        agent.Changed += changed => OnChanged(tab, changed);
        Add(tab);
        agent.Start();
        _input.Focus();
    }

    /// <summary>Lee la conversación de antes (aparte) y abre la pestaña que la sigue con --resume.</summary>
    void StartResumed(AgentOptions options, string label, PastSession past, Action<Tab>? placed = null)
    {
        var agent = NewAgent(options);
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
                placed?.Invoke(tab);
                _input.Focus();
            });
        });
    }

    /// <summary>
    /// Lo que pidió la isla («Abrir en la terminal»): la pestaña donde ya está esa sesión o, si no está en ninguna, la
    /// sesión retomada en una pestaña nueva, con la cuenta de la sesión y el modelo de la última pestaña. Mientras se
    /// restauran las pestañas de antes se espera (puede ser una de ellas).
    /// </summary>
    public void OpenSession(SessionTarget target)
    {
        if (_restoring)
        {
            DispatcherTimer.RunOnce(() => OpenSession(target), TimeSpan.FromMilliseconds(200));
            return;
        }
        if (_tabs.FirstOrDefault(t => t.Shell is null && (t.Agent.Transcript.SessionId == target.Id || t.Agent.Options.Resume == target.Id)) is { } open)
        {
            Select(open);
            return;
        }
        var configDir = target.ConfigDir ?? global::Claupet.Account.DefaultDir;
        _ = Task.Run(() =>
        {
            var past = ClaudeHistory.Find(configDir, target.Folder, target.Id);
            Dispatcher.UIThread.Post(() =>
            {
                if (past is null || !Directory.Exists(target.Folder))
                {
                    // No hay de dónde retomarla: una pestaña nueva en su carpeta, si sigue existiendo.
                    if (Directory.Exists(target.Folder))
                        OpenAgent(AgentKinds.Claude, target.Folder, Accounts().FirstOrDefault(a => global::Claupet.Account.SamePath(a.ConfigDir, configDir)), null, null);
                    return;
                }
                var account = Accounts().FirstOrDefault(a => global::Claupet.Account.SamePath(a.ConfigDir, configDir));
                var last = TerminalRecents.Load();
                var options = new AgentOptions(target.Folder, account?.ConfigDir ?? target.ConfigDir, last.Model, last.Effort, null, AgentKinds.Claude, past.Id);
                StartResumed(options, account?.Label ?? L.T("cuenta por defecto"), past);
            });
        });
    }

    // ── Una sesión que terminó ──────────────────────────────────────────────────────────────

    /// <summary>La franja sobre la caja de una sesión que terminó: qué pasó y cómo seguir (antes, Intro no hacía nada).</summary>
    Grid EndedBar(string text, string action, Action reopen)
    {
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16, 0, 14, 8) };
        var message = Ui.Text(text, 12.5, _ink.Secondary);
        message.TextWrapping = TextWrapping.Wrap;
        message.Margin = new Thickness(0, 0, 10, 0);
        bar.Children.Add(message);
        var button = Pill(action, _ink.Accent, Ink.On(_ink.Accent), reopen);
        Grid.SetColumn(button, 1);
        bar.Children.Add(button);
        return bar;
    }

    /// <summary>
    /// Sigue en una pestaña nueva lo que terminó, en la misma carpeta y con lo escrito en la caja (no se envía solo): Claude
    /// Code retoma su conversación si la tiene guardada; lo demás abre de nuevo.
    /// </summary>
    internal void Reopen(Tab tab)
    {
        if (tab.Reopening) return;
        var o = tab.Agent.Options;
        if (!Directory.Exists(o.Folder))
        {
            if (_ended.Children.OfType<TextBlock>().FirstOrDefault() is { } message)
                message.Text = L.T("La carpeta «{0}» ya no existe: no se puede abrir de nuevo aquí.", o.Folder);
            return;
        }
        tab.Reopening = true;
        var draft = _input.Text ?? "";
        if (tab.Shell is { } shell)
        {
            OpenShell(shell.Shell, o.Folder);
            ReplaceEnded(tab, _current, draft);
            return;
        }
        var configDir = o.ConfigDir ?? global::Claupet.Account.DefaultDir;
        if (o.Kind == AgentKinds.Claude && (tab.Agent.Transcript.SessionId ?? o.Resume) is { } id
            && ClaudeHistory.List(configDir, o.Folder).FirstOrDefault(p => p.Id == id) is { } past)
        {
            Resume(tab, past, replace: true, draft);
            return;
        }
        var account = Accounts().FirstOrDefault(a => global::Claupet.Account.SamePath(a.ConfigDir, configDir));
        OpenAgent(o.Kind, o.Folder, account, o.Model, tab.Agent.Effort ?? o.Effort);
        ReplaceEnded(tab, _current, draft);
    }

    internal void ReplaceEnded(Tab ended, Tab? opened, string draft)
    {
        opened?.Images.AddRange(ended.Images);
        // Queda en el lugar de la que reemplaza, no al final de la lista.
        if (opened is not null && opened != ended && _tabs.Remove(opened)) _tabs.Insert(_tabs.IndexOf(ended), opened);
        CloseTab(ended);
        _input.Text = draft;
        _input.CaretIndex = draft.Length;
    }

    /// <summary>
    /// Ctrl+V en la caja de un agente: una imagen copiada, o la ruta o la URL de una imagen, se suma al pedido como
    /// «[Imagen #N]» (como en Rider o en otras consolas), y los demás archivos copiados entran con su ruta; cualquier otro
    /// texto se pega como siempre.
    /// </summary>
    async Task PasteIntoComposerAsync(Tab tab)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        string? text = null;
        try { text = await clipboard.TryGetTextAsync(); }
        catch (Exception) { }
        string? added = null;
        if (string.IsNullOrEmpty(text))
        {
            if (await ClipboardImageAsync(clipboard) is { } copied) added = Attach(tab, copied);
            else if (await ClipboardPaste.PathsAsync(clipboard) is { Count: > 0 } paths) added = await FilesForComposerAsync(paths, image => Attach(tab, image));
        }
        else if (PastedImages.Reference(text, tab.Agent.Options.Folder) is { } reference && await PastedImages.LoadAsync(reference) is { } image)
            added = Attach(tab, image);
        if (added is null)
        {
            _input.Paste();
            return;
        }
        var at = Math.Clamp(_input.CaretIndex, 0, _input.Text?.Length ?? 0);
        _input.Text = (_input.Text ?? "").Insert(at, added);
        _input.CaretIndex = at + added.Length;
        _input.Focus();
    }

    /// <summary>La imagen va con el pedido; devuelve su marca para la caja.</summary>
    static string Attach(Tab tab, AgentImage image)
    {
        tab.Images.Add(image);
        return $"[Imagen #{tab.Images.Count}] ";
    }

    /// <summary>Lo que entra a la caja por unos archivos copiados: las imágenes, con su marca (attach); lo demás, con su ruta.</summary>
    internal static async Task<string> FilesForComposerAsync(IEnumerable<string> paths, Func<AgentImage, string> attach)
    {
        var text = "";
        foreach (var path in paths)
            text += await ImageFileAsync(path) is { } image ? attach(image) : ClipboardPaste.Quote(path) + " ";
        return text;
    }

    /// <summary>El archivo, si es una imagen que se pueda mandar; null si no.</summary>
    static async Task<AgentImage?> ImageFileAsync(string path)
    {
        try
        {
            return ImageType(path) is { } type && new FileInfo(path) is { Exists: true, Length: < 20_000_000 }
                ? new AgentImage(type, Convert.ToBase64String(await File.ReadAllBytesAsync(path)))
                : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>La imagen del portapapeles (una captura, «Copiar imagen»); null si no hay.</summary>
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
