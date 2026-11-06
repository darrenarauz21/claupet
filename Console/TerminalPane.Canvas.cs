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
    /// abre una con el texto ya escrito, para que la persona lo envíe: devuelve false y hay que mostrar la Terminal.
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

    /// <summary>«Trabajar en esto»: una sesión nueva de Claude Code en la carpeta del proyecto, con la tarea ya escrita.</summary>
    internal void WorkOn(string text, string folder)
    {
        var last = TerminalRecents.Load();
        var account = Accounts().FirstOrDefault(a => global::Claupet.Account.SamePath(a.ConfigDir, last.Account ?? "")) ?? Accounts().FirstOrDefault();
        OpenAgent(AgentKinds.Claude, Directory.Exists(folder) ? folder : global::Claupet.Account.Home, account, last.Model, last.Effort);
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
}
