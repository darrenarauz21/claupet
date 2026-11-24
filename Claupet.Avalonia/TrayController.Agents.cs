namespace Claupet;

/// <summary>
/// Los otros agentes (Codex CLI, Gemini CLI, GitHub Copilot CLI) en esta app: se leen al actualizar (Agents, en Core) y
/// salen en Uso y Actividad; en Ajustes › Cuentas se elige cuáles seguir. Y la memoria de una cuenta de Claude nueva.
/// </summary>
sealed partial class TrayController
{
    IReadOnlyList<AgentSnapshot> _agents = [];
    bool _readingAgents;
    /// <summary>
    /// Lo de hoy de los otros agentes por modelo (para Uso), en memoria: cada vez se lee solo lo nuevo de sus archivos. La
    /// caché del disco es de Ajustes › Consumo, en la Console; la bandeja no la toca. Lo de Claude ya lo cuenta Activity.
    /// </summary>
    readonly UsageLedger _today = UsageLedger.InMemory();

    /// <summary>Busca los que están en su carpeta de siempre y lee los que se siguen, fuera del hilo de la interfaz.</summary>
    async Task ReadAgentsAsync()
    {
        if (_readingAgents) return;
        _readingAgents = true;
        try
        {
            if (Agents.Detect(_settings)) SettingsStore.Save(_settings);
            var list = _settings.Agents.ToList();
            var sources = new UsageSources([], list.Where(a => a.Follow).ToList());
            (_agents, var today) = await Task.Run(() =>
            {
                var now = DateTimeOffset.Now;
                var agents = Agents.ReadAll(list, now);
                UsageReport? report = null;
                try { report = UsageReport.Build(_today.Refresh(sources, UsageReport.Range(UsagePeriod.Today, now).From), UsagePeriod.Today, now); }
                catch (Exception ex) { ErrorLog.Write(ex); }
                return (agents, report);
            });
            View.SetAgents(_agents, today);
            UpdateAgentPets(); // las mascotas de los agentes que están en el equipo
            PushSettings();
        }
        finally { _readingAgents = false; }
    }

    /// <summary>Seguir o dejar de seguir un agente (Ajustes › Cuentas).</summary>
    void ToggleAgent(string kind)
    {
        if (_settings.Agents.FirstOrDefault(a => a.Kind == kind) is not { } agent) return;
        agent.Follow = !agent.Follow;
        SettingsStore.Save(_settings);
        _ = ReadAgentsAsync();
    }

    /// <summary>
    /// Una cuenta de Claude nueva: si todavía no tiene memoria propia y hay otra cuenta, se elige si la comparte con ella
    /// (su carpeta projects se enlaza con la de la otra) o la tiene separada.
    /// </summary>
    async Task AskMemoryAsync(string dir)
    {
        var other = _settings.Accounts.FirstOrDefault(a => _settings.Selected is { } s && Account.SamePath(a.ConfigDir, s) && !Account.SamePath(a.ConfigDir, dir))
            ?? _settings.Accounts.FirstOrDefault(a => !Account.SamePath(a.ConfigDir, dir));
        if (other is null || !AccountMemory.CanShare(dir)) return;
        var choice = await Dialogs.Choose(
            L.T("¿La cuenta nueva comparte la memoria con «{0}»? Compartida: su carpeta projects (las conversaciones y la memoria de cada proyecto) se enlaza con la de «{0}»; CLAUDE.md, skills, agentes, ajustes y credenciales siguen siendo propios.", other.Label),
            _theme, [("compartida", L.T("Compartida con «{0}»", other.Label)), ("separada", L.T("Separada"))], "separada");
        if (choice != "compartida") return;
        try { AccountMemory.Share(dir, other.ConfigDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Info(L.T("No se pudo compartir la memoria: {0}", ex.Message), _theme);
        }
    }
}
