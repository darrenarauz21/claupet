using Avalonia;
using Avalonia.Threading;

namespace Claupet;

/// <summary>
/// Un archivo soltado sobre la mascota, como en la app WinForms: lo atrapa en una caja y, con la isla, camina según dónde
/// esté (ThrowPlan), la lanza a la isla, mira mientras Claude lee, cuenta lo esencial y vuelve a su lugar. Sin la isla,
/// no camina: piensa con la caja en las manos y cuenta el resumen. Se apaga con «Analizar los archivos que le suelte».
/// </summary>
sealed partial class TrayController
{
    bool _carrying;

    void WireDrops(MascotWindow mascot)
    {
        mascot.FileHover += over =>
        {
            if (_carrying) return;
            _director.SetMood(over ? ChibiMove.Catch : null);
        };
        mascot.FilesDropped += files =>
        {
            // Un paquete de extensión no se analiza; las extensiones todavía no están en esta app.
            if (Path.GetExtension(files[0]).Equals(".claupet", StringComparison.OrdinalIgnoreCase))
            {
                _director.SetMood(null);
                ChatBubbleFor().Say(mascot.ScreenBounds, L.T("Las extensiones todavía no están en esta app. (._.)"));
                return;
            }
            _ = CarryAsync(files[0]);
        };
    }

    /// <summary>--soltar: como si soltaran ese archivo sobre la mascota (para probar la coreografía).</summary>
    async Task DemoDropAsync(string file)
    {
        await Task.Delay(2500);
        if (_mascotWindow is null) return;
        _director.SetMood(ChibiMove.Catch);
        await Task.Delay(900);
        await CarryAsync(Path.GetFullPath(file));
    }

    async Task CarryAsync(string file)
    {
        if (_mascotWindow is not { } mascot || _carrying) return;
        if (!_settings.AnalyzeDrops)
        {
            _director.SetMood(null);
            ChatBubbleFor().Say(mascot.ScreenBounds, L.T("Tengo apagado lo de mirar archivos (Ajustes › Asistente). (._.)"));
            return;
        }
        _carrying = true;
        mascot.Busy = true;
        _chatBubble?.Dismiss(notify: false);
        _bubble.Hide();
        var home = mascot.Position;
        try
        {
            _director.SetMood(ChibiMove.Carry);
            await Task.Delay(650);
            if (_island is not { } island || mascot.Screens.ScreenFromWindow(mascot) is not { } screen)
            {
                await AnalyzeAsync(file);
                return;
            }

            var scale = (float)screen.Scaling;
            var bounds = mascot.ScreenBounds;
            var work = screen.WorkingArea;
            var mouth = IslandWindow.MouthArea(screen);
            var plan = ThrowPlan.Make(new System.Drawing.Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                new System.Drawing.Rectangle(work.X, work.Y, work.Width, work.Height),
                new System.Drawing.Rectangle(mouth.X, mouth.Y, mouth.Width, mouth.Height), mascot.PixelUnit, scale);
            island.Receive(screen);
            if (plan.WalkMs > 0)
            {
                _director.SetMood(plan.Left ? ChibiMove.CarryLeft : ChibiMove.CarryRight);
                await mascot.WalkToAsync(plan.WalkToX, plan.WalkMs);
            }

            // Toma impulso, salta y la lanza: la caja sale de sus manos cuando suelta.
            island.OpenMouth();
            _director.SetMood(ChibiMove.Throw);
            await Task.Delay(ChibiSprite.ThrowRelease);
            // La caja sale de las manos del chibi (a la altura del pecho) o de arriba de la cabeza de la clásica.
            var unit = mascot.PixelUnit;
            var (bx, by) = ChibiSprite.BoxInHands;
            var at = mascot.Position;
            var hands = mascot.Chibi
                ? new PixelPoint(at.X + (bx + 7) * unit, at.Y + (by + 2) * unit)
                : new PixelPoint(at.X + mascot.ScreenBounds.Width / 2, at.Y + 3 * unit);
            var box = new BoxFlightWindow(mascot.Chibi ? unit : Math.Max(1, unit * 2 / 3), scale);
            await box.FlyAsync(hands, island.Mouth, plan.FlightMs, island, scale);
            island.Swallow();
            var analysis = AnalyzeAsync(file);
            await Task.Delay(Math.Max(0, ChibiSprite.Length(ChibiMove.Throw) - ChibiSprite.ThrowRelease - plan.FlightMs));
            if (!analysis.IsCompleted) _director.SetMood(ChibiMove.Listen); // mira hacia arriba mientras lee
            await analysis;

            // Cuenta lo esencial y, cuando termina de decirlo, vuelve caminando a su lugar.
            var waited = 0;
            while (_chatBubble?.Talking == true && waited < 8000) { await Task.Delay(100); waited += 100; }
            if (_mascotWindow != mascot) return;
            if (mascot.Position.X != home.X)
            {
                _director.SetMood(home.X > mascot.Position.X ? ChibiMove.WalkRight : ChibiMove.WalkLeft);
                await mascot.WalkToAsync(home.X, Math.Max(400, plan.WalkMs));
            }
            _director.SetMood(null);
        }
        finally
        {
            _carrying = false;
            mascot.Busy = false;
        }
    }
}
