namespace Claupet;

/// <summary>Lo que Claupet Canvas y Proyectos le piden a la Terminal: que un agente haga un cambio.</summary>
sealed partial class TerminalPane
{
    /// <summary>¿Hay una sesión trabajando en esa carpeta? (el punto de la lista de Proyectos)</summary>
    internal bool WorkingIn(string folder) =>
        _tabs.Any(t => t.Shell is null && t.Agent.Transcript.Busy && global::Claupet.Account.SamePath(t.Agent.Options.Folder, folder));

    /// <summary>Las carpetas vinculadas, para buscar en ellas los planes.</summary>
    internal IReadOnlyList<string> ProjectFolders() => Projects();

    /// <summary>
    /// Un pedido de la sección Proyectos: va a una sesión de esa misma carpeta (nunca a la de otro proyecto). Si no hay, se
    /// abre una que arranca con él ya enviado: devuelve false y hay que mostrar la Terminal.
    /// </summary>
    internal bool AskInFolder(string text, string folder)
    {
        var tab = _tabs.FirstOrDefault(t => t.Shell is null && t.Agent.Running && global::Claupet.Account.SamePath(t.Agent.Options.Folder, folder));
        if (tab is not null)
        {
            tab.Agent.Send(text);
            return true;
        }
        WorkOn(text, folder);
        return false;
    }

    /// <summary>
    /// «Trabajar en esto»: una sesión nueva de Claude Code en la carpeta del proyecto (con la cuenta, el modelo y el
    /// esfuerzo de la última vez) que arranca con el pedido ya enviado. Si no pudo abrir, el pedido queda escrito en la caja.
    /// </summary>
    internal void WorkOn(string text, string folder)
    {
        var last = TerminalRecents.Load();
        var account = Accounts().FirstOrDefault(a => global::Claupet.Account.SamePath(a.ConfigDir, last.Account ?? "")) ?? Accounts().FirstOrDefault();
        if (OpenAgent(AgentKinds.Claude, Directory.Exists(folder) ? folder : global::Claupet.Account.Home, account, last.Model, last.Effort) is { Agent.Running: true } tab)
        {
            tab.Agent.Send(text);
            return;
        }
        _input.Text = text;
        _input.CaretIndex = text.Length;
    }

    /// <summary>
    /// Manda el pedido a un agente que esté corriendo: el de la pestaña elegida, o el primero de esa carpeta, o el primero.
    /// Si no hay ninguno, abre uno de Claude Code en la carpeta y deja el pedido escrito en la caja (devuelve false: hay
    /// que mirar la Terminal para enviarlo cuando arranque).
    /// </summary>
    internal bool AskAgent(string text, string? folder)
    {
        var agents = _tabs.Where(t => t.Shell is null && t.Agent.Running).ToList();
        var tab = _current is { Shell: null } current && current.Agent.Running ? current
            : agents.FirstOrDefault(t => folder is not null && global::Claupet.Account.SamePath(t.Agent.Options.Folder, folder))
            ?? agents.FirstOrDefault();
        if (tab is not null)
        {
            tab.Agent.Send(text);
            return true;
        }
        var last = TerminalRecents.Load();
        var account = Accounts().FirstOrDefault(a => global::Claupet.Account.SamePath(a.ConfigDir, last.Account ?? "")) ?? Accounts().FirstOrDefault();
        var where = folder is not null && Directory.Exists(folder) ? folder : global::Claupet.Account.Home;
        OpenAgent(AgentKinds.Claude, where, account, last.Model, last.Effort);
        _input.Text = text;
        _input.CaretIndex = text.Length;
        return false;
    }

    /// <summary>Las sesiones de agente que corren, para el chat de la mesa de diseño de Canvas.</summary>
    internal IReadOnlyList<CanvasSession> CanvasSessions() => _tabs
        .Where(t => t.Shell is null && t.Agent.Running)
        .Select(t => new CanvasSession(t.Agent, t.Agent.Driver.Name, t.Agent.Options.Folder, Title(t)))
        .ToList();

    /// <summary>Abre una sesión de Claude Code en esa carpeta para la mesa (sin pasar a la Terminal). Null si no pudo.</summary>
    internal CanvasSession? OpenCanvasSession(string? folder)
    {
        var last = TerminalRecents.Load();
        var account = Accounts().FirstOrDefault(a => global::Claupet.Account.SamePath(a.ConfigDir, last.Account ?? "")) ?? Accounts().FirstOrDefault();
        var where = folder is not null && Directory.Exists(folder) ? folder : global::Claupet.Account.Home;
        return OpenAgent(AgentKinds.Claude, where, account, last.Model, last.Effort) is { Agent.Running: true } tab
            ? new CanvasSession(tab.Agent, tab.Agent.Driver.Name, tab.Agent.Options.Folder, Title(tab))
            : null;
    }

    /// <summary>Elige en la Terminal la pestaña de esa sesión (para contestar un permiso o seguir ahí).</summary>
    internal void Reveal(TerminalAgent agent)
    {
        if (_tabs.FirstOrDefault(t => t.Agent == agent) is { } tab) Select(tab);
    }
}

/// <summary>Una sesión que corre en la Terminal, vista desde Canvas: su agente, su nombre, su carpeta y su título.</summary>
sealed record CanvasSession(TerminalAgent Agent, string Name, string Folder, string Title);

/// <summary>Lo que el chat de la mesa de diseño necesita de la Terminal (null en las pruebas: no hay chat con sesiones).</summary>
sealed class CanvasSessions(Func<IReadOnlyList<CanvasSession>> list, Func<string?, CanvasSession?> open, Action<TerminalAgent> reveal)
{
    public IReadOnlyList<CanvasSession> List() => list();
    public CanvasSession? Open(string? folder) => open(folder);
    public void Reveal(TerminalAgent agent) => reveal(agent);
    /// <summary>Se abrió o se cerró una sesión.</summary>
    public event Action? Changed;
    public void Notify() => Changed?.Invoke();
}
