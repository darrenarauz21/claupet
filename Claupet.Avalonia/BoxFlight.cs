using System.Drawing;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Claupet;

/// <summary>
/// La caja que vuela de las manos de la mascota a la boca de la isla, como en la app WinForms: en su propia ventanita sin
/// fondo, da vueltas en el aire (cerrada y de costado, alternando) y en Windows pasa por detrás de la isla al entrar.
/// </summary>
sealed class BoxFlightWindow : Window
{
    readonly double _unit;
    readonly BoxView _view;

    /// <summary>unit: pixeles reales por casilla (los de la mascota); scale: la escala de esa pantalla.</summary>
    public BoxFlightWindow(int unit, double scale)
    {
        _unit = unit / scale;
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Width = ChibiSprite.BoxColumns * _unit;
        Height = ChibiSprite.BoxRows * _unit;
        Content = _view = new BoxView(this);
        WinStyles.Float(this);
    }

    /// <summary>
    /// Vuela del punto from (las manos) al punto to (la boca de la isla) en ms milisegundos; las coordenadas son del
    /// centro de la caja, en pixeles de pantalla. Termina cerrada.
    /// </summary>
    public async Task FlyAsync(PixelPoint from, PixelPoint to, int ms, Window? behind, float scale)
    {
        Place(from, scale);
        Show();
        if (OperatingSystem.IsWindows() && behind?.TryGetPlatformHandle()?.Handle is { } island && TryGetPlatformHandle()?.Handle is { } box)
            SetWindowPos(box, island, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // debajo de la isla, sin activar
        var start = Environment.TickCount64;
        while (true)
        {
            var k = Math.Clamp((Environment.TickCount64 - start) / (double)Math.Max(1, ms), 0, 1);
            var at = ThrowPlan.Flight(new PointF(from.X, from.Y), new PointF(to.X, to.Y), k, scale);
            Place(new PixelPoint((int)Math.Round(at.X), (int)Math.Round(at.Y)), scale);
            var tumbling = (Environment.TickCount64 - start) / 90 % 2 == 1;
            if (tumbling != _view.Tumbling)
            {
                _view.Tumbling = tumbling;
                _view.InvalidateVisual();
            }
            if (k >= 1) break;
            await Task.Delay(15);
        }
        Close();
    }

    void Place(PixelPoint center, float scale) =>
        Position = new PixelPoint(center.X - (int)(Width * scale / 2), center.Y - (int)(Height * scale / 2));

    sealed class BoxView(BoxFlightWindow owner) : Control
    {
        public bool Tumbling { get; set; }

        public override void Render(DrawingContext dc) =>
            Gfx.Pixels(dc, 0, 0, owner._unit, fill => ChibiSprite.PaintBox(fill, Tumbling ? ChibiSprite.BoxFrame.Tumbling : ChibiSprite.BoxFrame.Closed));
    }

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, int flags);
}
