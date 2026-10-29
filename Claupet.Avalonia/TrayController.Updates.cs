using Avalonia.Threading;

namespace Claupet;

/// <summary>
/// Las versiones nuevas en esta app (lo de buscar, bajar y comprobar está en Core: Updates). Aquí van el aviso, la fila de
/// Ajustes › Sistema, el permiso antes de bajarla y el cambio al reiniciar: con «Reiniciar ahora» o al salir.
/// </summary>
sealed partial class TrayController
{
    Updates? _updates;
    /// <summary>El aviso de la versión nueva: un clic lleva a Ajustes › Sistema de Claupet Console.</summary>
    Notice? _updateNotice;

    void StartUpdates()
    {
        var updates = _updates = new Updates(_settings, () => SettingsStore.Save(_settings), action => Dispatcher.UIThread.Post(action));
        updates.Changed += PushSettings;
        updates.Found += release =>
        {
            if (!_settings.Notifications) return;
            ShowNotice(_updateNotice = new Notice(NoticeKind.Extension, L.T("Claupet {0} está disponible", release.Version),
                L.T("Clic para verla en Ajustes › Sistema"), ""));
        };
        // Una copia de prueba (--preview) no busca ni cambia nada: lo hace la app de siempre.
        if (Program.Args.Contains("--preview")) return;
        updates.CleanUp(ExePath);
        updates.Start();
    }

    /// <summary>«Buscar actualizaciones»: al encenderlo, busca ya.</summary>
    void ToggleUpdates()
    {
        _settings.CheckUpdates = !_settings.CheckUpdates;
        SettingsStore.Save(_settings);
        if (_settings.CheckUpdates && _updates is { } updates) _ = updates.CheckAsync();
    }

    /// <summary>La fila de la versión nueva: verla, bajarla (con permiso), cancelar o reiniciar con ella.</summary>
    async Task OnUpdateAsync()
    {
        if (_updates is not { } updates) return;
        switch (updates.Act())
        {
            case UpdateRequest.OpenPage:
                Shell.Open(updates.Page);
                break;
            case UpdateRequest.AskToInstall:
                if (await Dialogs.Confirm(updates.ConfirmText, _theme)) _ = updates.DownloadAsync();
                break;
            case UpdateRequest.Restart:
                if (updates.Apply(ExePath) is { } problem) await Dialogs.Info(L.T("No se pudo instalar la versión nueva: {0}", problem), _theme);
                else Restart();
                break;
        }
    }

    /// <summary>Al salir, la versión nueva ya comprobada toma el lugar de esta: la próxima vez abre la nueva.</summary>
    void ApplyUpdateOnExit()
    {
        _updates?.Apply(ExePath);
        _updates?.Dispose();
    }
}
