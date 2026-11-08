namespace Claupet;

/// <summary>
/// Ajustes › Skills en esta app: las skills de Claude Code de todas las cuentas (ClaudeSkills). Las de Claupet se
/// instalan o se quitan con su interruptor; las demás se agregan desde un archivo, se abren o se quitan (con permiso).
/// </summary>
sealed partial class TrayController
{
    string? _skillStatus;
    bool _skillProblem;

    IReadOnlyList<string> SkillDirs => ClaudeSkills.ConfigDirs(_settings.Accounts);

    /// <summary>Al abrir la app, las de Claupet ya instaladas se ponen al día (otra versión puede traer otro texto).</summary>
    void StartSkills() => _ = Task.Run(() => ClaudeSkills.RefreshBundled(SkillDirs));

    IReadOnlyList<SkillState> SkillList() => ClaudeSkills.List(SkillDirs);

    void SkillDone(string? text, bool problem)
    {
        (_skillStatus, _skillProblem) = (text, problem);
        PushSettings();
    }

    /// <summary>Lo escrito o elegido de las skills (la ruta del archivo a agregar); false si no es de ellas.</summary>
    bool SkillTyped(string key, string value)
    {
        if (key != SettingsPages.SkillAddKey) return false;
        var (name, problem) = ClaudeSkills.Add(value, SkillDirs);
        SkillDone(problem ?? L.T("Skill «{0}» agregada: la usan las sesiones nuevas de Claude Code.", name!), problem is not null);
        return true;
    }

    /// <summary>Las acciones de Ajustes › Skills; false si la clave no es de ellas.</summary>
    bool SkillAction(string key)
    {
        if (key.StartsWith(SettingsPages.SkillBundledKey, StringComparison.Ordinal))
        {
            var name = key[SettingsPages.SkillBundledKey.Length..];
            var installed = SkillList().Any(s => s.Name == name);
            var problem = installed ? ClaudeSkills.Remove(name, SkillDirs) : ClaudeSkills.InstallBundled(name, SkillDirs);
            SkillDone(problem, problem is not null);
            return true;
        }
        if (key.StartsWith(SettingsPages.SkillOpenKey, StringComparison.Ordinal))
        {
            if (ClaudeSkills.Folder(key[SettingsPages.SkillOpenKey.Length..], SkillDirs) is { } folder) Gfx.OpenFolder(folder);
            return true;
        }
        if (key.StartsWith(SettingsPages.SkillRemoveKey, StringComparison.Ordinal))
        {
            _ = RemoveSkillAsync(key[SettingsPages.SkillRemoveKey.Length..]);
            return true;
        }
        return false;
    }

    async Task RemoveSkillAsync(string name)
    {
        if (!await Dialogs.Confirm(L.T("¿Quitar la skill «{0}» de todas las cuentas de Claude Code? Se borra su carpeta.", name), _theme)) return;
        var problem = ClaudeSkills.Remove(name, SkillDirs);
        SkillDone(problem ?? L.T("Skill «{0}» quitada.", name), problem is not null);
    }
}
