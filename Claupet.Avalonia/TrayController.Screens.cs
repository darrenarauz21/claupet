using Avalonia.Threading;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Claupet;

/// <summary>
/// La mascota después de bloquear, suspender o apagar la pantalla, como en la app WinForms: en Windows, al volver (o al
/// cambiar las pantallas, que apagar un monitor lo desconecta) vuelve a su lugar y arriba de todo, enseguida y otra vez a
/// los 2 y 6 s, cuando los monitores terminan de reconectarse. Los cambios de pantalla en Linux y macOS ya los atiende
/// MascotWindow. Mientras la sesión está bloqueada o el equipo suspendido nada se ve: la mascota, la isla y la tecla
/// para hablar dejan de despertarse hasta que vuelva.
/// </summary>
sealed partial class TrayController
{
    bool _watchingScreens;

    void WatchMascotScreens()
    {
        if (_watchingScreens || !OperatingSystem.IsWindows()) return;
        _watchingScreens = true;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    void UnwatchMascotScreens()
    {
        if (!_watchingScreens || !OperatingSystem.IsWindows()) return;
        _watchingScreens = false;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }

    [SupportedOSPlatform("windows")]
    void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            Dispatcher.UIThread.Post(() =>
            {
                SetLocked(false);
                RecoverMascotSoon();
            });
        else if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            Dispatcher.UIThread.Post(() => SetLocked(true));
    }

    [SupportedOSPlatform("windows")]
    void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            Dispatcher.UIThread.Post(() =>
            {
                SetLocked(false); // si vuelve bloqueada, el bloqueo llega aparte
                RecoverMascotSoon();
            });
        else if (e.Mode == PowerModes.Suspend) Dispatcher.UIThread.Post(() => SetLocked(true));
    }

    bool _locked;

    void SetLocked(bool locked)
    {
        if (_locked == locked) return;
        _locked = locked;
        _mascotWindow?.SetPaused(locked);
        foreach (var pet in _agentPets.Values) pet.Window.SetPaused(locked); // las de los otros agentes, igual
        _island?.SetPaused(locked);
        UpdateVoice(); // la tecla para hablar no se mira con la sesión bloqueada
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RecoverMascotSoon);

    void RecoverMascotSoon()
    {
        _mascotWindow?.Recover();
        foreach (var ms in new[] { 2000, 6000 })
            DispatcherTimer.RunOnce(() => _mascotWindow?.Recover(), TimeSpan.FromMilliseconds(ms));
    }
}
