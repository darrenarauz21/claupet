namespace Claupet;

using System.Text.Json;
using Avalonia.Threading;

/// <summary>
/// Las pestañas abiertas sobreviven a cerrar Claupet Console: al cerrarla se anotan (carpeta, cuenta, modelo, esfuerzo,
/// la sesión de Claude Code y lo escrito en la caja) y al abrirla vuelven en el mismo orden. Claude Code retoma su
/// conversación con --resume y con lo de antes a la vista; las terminales y los otros agentes abren de nuevo en su carpeta.
/// </summary>
sealed partial class TerminalPane
{
    sealed record OpenTab(string Folder, string Kind, string? ConfigDir, string? Model, string? Effort, string? Mode,
        string? Session, string? Shell, string? Distro, string Draft);

    sealed record OpenTabs(List<OpenTab> Tabs, int Current);

    bool _restoring;

    static string OpenTabsPath => DataFolder.In("terminal-pestanas.json");

    /// <summary>Una copia de prueba (--preview) no guarda ni abre las pestañas de la app de siempre.</summary>
    static bool PreviewCopy => Program.Args.Contains("--preview");

    /// <summary>Anota las pestañas abiertas (antes de cerrarlas al salir).</summary>
    void SaveOpen()
    {
        // Si se cierra antes de terminar de abrirlas, quedan las de la última vez (no una lista a medias).
        if (PreviewCopy || _restoring) return;
        if (_current is { } current && !_creating) current.Draft = _input.Text ?? "";
        var tabs = new List<OpenTab>();
        var selected = 0;
        foreach (var tab in _tabs)
        {
            if (tab == _current) selected = tabs.Count;
            var o = tab.Agent.Options;
            if (tab.Shell is { } shell)
            {
                tabs.Add(new OpenTab(o.Folder, "", null, null, null, null, null, shell.Shell.Id, shell.Shell.Distro, ""));
                continue;
            }
            tabs.Add(new OpenTab(o.Folder, o.Kind, o.ConfigDir, o.Model, tab.Agent.Effort ?? o.Effort,
                tab.FollowsSession ? null : tab.Agent.Transcript.PermissionMode ?? o.PermissionMode,
                tab.Agent.Transcript.SessionId ?? o.Resume, null, null, tab.Draft));
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OpenTabsPath)!);
            File.WriteAllText(OpenTabsPath, JsonSerializer.Serialize(new OpenTabs(tabs, selected)));
        }
        catch (Exception) { }
    }

    /// <summary>Vuelve a abrir las pestañas de la última vez (una vez, al abrir la Console).</summary>
    public void RestoreOpen()
    {
        if (PreviewCopy) return;
        OpenTabs? saved = null;
        try
        {
            if (File.Exists(OpenTabsPath)) saved = JsonSerializer.Deserialize<OpenTabs>(File.ReadAllText(OpenTabsPath));
        }
        catch (Exception) { }
        if (saved is not { Tabs.Count: > 0 }) return;
        _restoring = true;
        // Leer las conversaciones puede tardar: se hace aparte y las pestañas se arman después, todas juntas y en orden.
        _ = Task.Run(() =>
        {
            var agents = saved.Tabs.Select(Prepare).ToList();
            Dispatcher.UIThread.Post(() =>
            {
                Tab? selected = null;
                for (var i = 0; i < saved.Tabs.Count; i++)
                {
                    var tab = Reopen(saved.Tabs[i], agents[i]);
                    if (tab is not null && (i <= saved.Current || selected is null)) selected = tab;
                }
                if (selected is not null) Select(selected);
                _restoring = false;
            });
        });
    }

    /// <summary>El agente de una pestaña de Claude Code o de otro agente, con su conversación leída si la tiene (sin abrirlo).</summary>
    TerminalAgent? Prepare(OpenTab saved)
    {
        if (saved.Shell is not null || !Directory.Exists(saved.Folder)) return null;
        var claude = saved.Kind == AgentKinds.Claude;
        var configDir = saved.ConfigDir ?? global::Claupet.Account.DefaultDir;
        var path = claude && saved.Session is { } id ? Path.Combine(ClaudeHistory.ProjectDir(configDir, saved.Folder), id + ".jsonl") : null;
        var resume = path is not null && File.Exists(path);
        var options = claude
            ? new AgentOptions(saved.Folder, saved.ConfigDir, saved.Model, saved.Effort, saved.Mode, AgentKinds.Claude, resume ? saved.Session : null)
            : new AgentOptions(saved.Folder, Kind: saved.Kind);
        var agent = NewAgent(options, askIsland: true);
        if (resume)
        {
            try { ClaudeHistory.Replay(path!, agent.Transcript); }
            catch (Exception) { }
            agent.Transcript.Notice(L.T("Sesión retomada al abrir Claupet: lo de arriba es lo de antes. Siga escribiendo abajo."));
        }
        return agent;
    }

    Tab? Reopen(OpenTab saved, TerminalAgent? agent)
    {
        if (!Directory.Exists(saved.Folder)) return null;
        if (saved.Shell is { } shellId)
        {
            var choices = Shells.Available();
            var choice = choices.FirstOrDefault(c => c.Id == shellId && c.Distro == saved.Distro) ?? choices.FirstOrDefault(c => c.Id == shellId);
            if (choice is null) return null;
            OpenShell(choice, saved.Folder);
            return _current;
        }
        if (agent is null) return null;
        var claude = saved.Kind == AgentKinds.Claude;
        var configDir = saved.ConfigDir ?? global::Claupet.Account.DefaultDir;
        var account = claude ? Accounts().FirstOrDefault(a => global::Claupet.Account.SamePath(a.ConfigDir, configDir)) : null;
        var tab = new Tab(agent, claude ? account?.Label ?? L.T("cuenta por defecto") : AgentKinds.Label(saved.Kind)) { Draft = saved.Draft };
        agent.Changed += changed => OnChanged(tab, changed);
        Add(tab);
        // Add deja la caja vacía: lo escrito de esta pestaña vuelve a ella (y se guarda bien al abrir la siguiente).
        _input.Text = tab.Draft;
        agent.Start();
        return tab;
    }
}
