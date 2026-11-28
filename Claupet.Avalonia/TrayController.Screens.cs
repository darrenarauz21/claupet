using Avalonia.Threading;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Claupet;

/// <summary>
/// Las mascotas (la principal y las de los otros agentes) después de bloquear, suspender o apagar la pantalla, como en la app WinForms: en Windows, al volver (o al
/// cambiar las pantallas, que apagar un monitor lo desconecta) vuelve a su lugar y arriba de todo, enseguida y otra vez a
/// los 2 y 6 s, cuando los monitores terminan de reconectarse. Además, al volver de suspender o al desbloquear, sus
/// ventanas se rehacen (a los 3 s, una vez aunque lleguen varios avisos): la de antes podía quedar sin mostrar lo que se
/// le dibujaba (se veían pedazos de cuadros viejos y solo se arreglaba al pasarle otra ventana por encima), y una ventana
/// nueva trae su propia superficie de dibujo. Los cambios de pantalla en Linux y macOS ya los atiende MascotWindow.
/// Mientras la sesión está bloqueada o el equipo suspendido nada se ve: la mascota, la isla y la tecla para hablar dejan
/// de despertarse hasta que vuelva.
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
                RecoverMascotSoon(rebuild: true);
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
                RecoverMascotSoon(rebuild: true);
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

    void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => RecoverMascotSoon());

    /// <summary>Arriba de todo enseguida y a los 2 y 6 s; con rebuild, además se rehacen sus ventanas (RebuildMascots).</summary>
    void RecoverMascotSoon(bool rebuild = false)
    {
        RecoverMascots();
        foreach (var ms in new[] { 2000, 6000 })
            DispatcherTimer.RunOnce(RecoverMascots, TimeSpan.FromMilliseconds(ms));
        if (!rebuild) return;
        // Volver, desbloquear y el cambio de pantallas llegan casi juntos: se rehacen una sola vez, con las pantallas ya puestas.
        _rebuildMascots?.Dispose();
        _rebuildMascots = DispatcherTimer.RunOnce(RebuildMascots, TimeSpan.FromSeconds(3));
    }

    IDisposable? _rebuildMascots;

    /// <summary>
    /// Cierra y vuelve a abrir las ventanas de las mascotas, en el mismo lugar (el guardado o el de siempre). Con la sesión
    /// todavía bloqueada no: se rehacen al desbloquear.
    /// </summary>
    void RebuildMascots()
    {
        _rebuildMascots = null;
        if (_locked) return;
        if (_mascotWindow is { } main)
        {
            _bubble.Hide(); // el globo de avisos sigue a la ventana de antes
            main.Close();
            _mascotWindow = null;
        }
        foreach (var pet in _agentPets.Values) pet.Window.Close();
        _agentPets.Clear();
        UpdateMascotWindow(); // abre la principal (si está encendida) y las de los otros agentes
    }

    /// <summary>La principal y las de los otros agentes: Windows también deja a estas debajo de las demás ventanas.</summary>
    void RecoverMascots()
    {
        _mascotWindow?.Recover();
        foreach (var pet in _agentPets.Values.ToList()) pet.Window.Recover();
    }
}
