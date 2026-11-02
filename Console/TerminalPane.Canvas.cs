namespace Claupet;

/// <summary>Lo que Claupet Canvas le pide a la Terminal: que un agente haga un cambio en un canvas.</summary>
sealed partial class TerminalPane
{
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
