using Avalonia.Threading;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Claupet;

/// <summary>
/// La mascota después de bloquear, suspender o apagar la pantalla, como en la app WinForms: en Windows, al volver (o al
/// cambiar las pantallas, que apagar un monitor lo desconecta) vuelve a su lugar y arriba de todo, enseguida y otra vez a
/// los 2 y 6 s, cuando los monitores terminan de reconectarse. Los cambios de pantalla en Linux y macOS ya los atiende
/// MascotWindow.
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
            Dispatcher.UIThread.Post(RecoverMascotSoon);
    }

    [SupportedOSPlatform("windows")]
    void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Dispatcher.UIThread.Post(RecoverMascotSoon);
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RecoverMascotSoon);

    void RecoverMascotSoon()
    {
        _mascotWindow?.Recover();
        foreach (var ms in new[] { 2000, 6000 })
            DispatcherTimer.RunOnce(() => _mascotWindow?.Recover(), TimeSpan.FromMilliseconds(ms));
    }
}
