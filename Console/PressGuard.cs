namespace Claupet;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

/// <summary>
/// Una lista de filas que se tocan y que a veces se rehace entera. Un clic necesita que la fila del press sea la del
/// release: si se rehace en medio, el clic se pierde (y el puntero parpadea). Con el botón apretado dentro de area, Run
/// deja el pedido pendiente y lo hace al soltar. Va por Tunnel: antes que el release de la fila que hace el clic.
/// </summary>
sealed class PressGuard
{
    readonly Action _render;
    bool _queued;

    public PressGuard(Control area, Action render)
    {
        _render = render;
        area.AddHandler(InputElement.PointerPressedEvent, (_, _) => Held = true, RoutingStrategies.Tunnel, handledEventsToo: true);
        area.AddHandler(InputElement.PointerReleasedEvent, (_, _) =>
        {
            if (!Held) return;
            Held = false;
            if (!_queued) return;
            _queued = false;
            Dispatcher.UIThread.Post(_render, DispatcherPriority.Background);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>El botón está apretado dentro del área.</summary>
    public bool Held { get; private set; }

    public void Run()
    {
        if (Held)
        {
            _queued = true;
            return;
        }
        _queued = false;
        _render();
    }
}
