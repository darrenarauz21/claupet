namespace Claupet;

/// <summary>
/// Las versiones nuevas en la app de Windows, con lo de Core (Updates): el aviso, la fila de Ajustes › Sistema, el permiso
/// antes de bajarla y el cambio al reiniciar (con «Reiniciar ahora» o al salir).
/// </summary>
sealed partial class TrayApp
{
    Updates? _updates;

    void SetupUpdates(bool start)
    {
        var updates = _updates = new Updates(_settings, () => SettingsStore.Save(_settings), PostUi);
        updates.Changed += PushSettings;
        updates.Found += release =>
        {
            if (_settings.Notifications)
                ShowNotice(new Notice(NoticeKind.Extension, L.T("Claupet {0} está disponible", release.Version), L.T("En Ajustes › Sistema"), ""));
        };
        // Una copia de prueba (--preview) no busca ni cambia nada: lo hace la app de siempre.
        if (!start) return;
        updates.CleanUp(ExePath);
        updates.Start();
    }

    void ToggleUpdates()
    {
        Toggle(s => s.CheckUpdates = !s.CheckUpdates);
        if (_settings.CheckUpdates && _updates is { } updates) _ = updates.CheckAsync();
    }

    void OnUpdate()
    {
        if (_updates is not { } updates) return;
        switch (updates.Act())
        {
            case UpdateRequest.OpenPage:
                Shell.Open(updates.Page);
                break;
            case UpdateRequest.AskToInstall:
                if (MessageBox.Show(updates.ConfirmText, "Claupet", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    _ = updates.DownloadAsync();
                break;
            case UpdateRequest.Restart:
                if (updates.Apply(ExePath) is { } problem)
                    MessageBox.Show(L.T("No se pudo instalar la versión nueva: {0}", problem), "Claupet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else Restart();
                break;
        }
    }
}
