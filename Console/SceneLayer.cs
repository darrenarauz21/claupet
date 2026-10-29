namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

/// <summary>
/// El fondo animado de un tema propio: una escena del catálogo (CustomThemes.Scenes) en pixel art, como la mascota,
/// a 12 cuadros por segundo y con poca opacidad, detrás del contenido de la Console (las tarjetas siguen opacas).
/// Se pausa con la ventana minimizada u oculta (Paused) y fuera del árbol visual.
/// </summary>
sealed class SceneLayer : Control
{
    const int Pixel = 4;
    const double Alpha = 0.16;

    sealed class Particle
    {
        public double X, Y, Speed, Phase;
        public int Size, Color;
    }

    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1000 / 12.0) };
    readonly Random _random = new(7);
    readonly List<Particle> _particles = [];
    ThemeScene? _scene;
    IBrush[] _brushes = [];
    double _time;
    bool _paused;

    public SceneLayer()
    {
        IsHitTestVisible = false;
        _timer.Tick += (_, _) => Step();
    }

    public bool Active => _scene is not null;

    /// <summary>Con la ventana minimizada u oculta no se anima.</summary>
    public bool Paused
    {
        get => _paused;
        set { _paused = value; Update(); }
    }

    /// <summary>La escena del tema elegido (null: ninguna, y no se dibuja nada).</summary>
    public void Show(ThemeScene? scene)
    {
        _scene = scene;
        _brushes = scene?.Colors.Select(c => (IBrush)new ImmutableSolidColorBrush(Color.FromRgb(c.R, c.G, c.B), Alpha)).ToArray() ?? [];
        _particles.Clear();
        Update();
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Update();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    void Update()
    {
        var run = _scene is not null && !_paused && VisualRoot is not null;
        if (run && !_timer.IsEnabled) _timer.Start();
        else if (!run && _timer.IsEnabled) _timer.Stop();
    }

    /// <summary>Tantas partículas como pida la densidad para este tamaño, repartidas al azar.</summary>
    void Seed(double width, double height)
    {
        var wanted = (int)Math.Clamp(_scene!.Density * width * height / 6000, 8, 220);
        if (_particles.Count == wanted) return;
        _particles.Clear();
        for (var i = 0; i < wanted; i++)
            _particles.Add(new Particle
            {
                X = _random.NextDouble() * width, Y = _random.NextDouble() * height, Speed = 0.6 + _random.NextDouble() * 0.8,
                Phase = _random.NextDouble() * Math.PI * 2, Size = 1 + _random.Next(2), Color = _random.Next(_brushes.Length),
            });
    }

    void Step()
    {
        if (_scene is not { } scene) return;
        double w = Bounds.Width, h = Bounds.Height, v = scene.Speed;
        _time += v / 12;
        foreach (var p in _particles)
        {
            switch (scene.Kind)
            {
                case "lluvia": p.Y += 16 * v * p.Speed; p.X -= 2 * v; break;
                case "nieve": p.Y += 2 * v * p.Speed; p.X += Math.Sin(_time * 2 + p.Phase) * 1.5; break;
                case "hojas": p.Y += 3 * v * p.Speed; p.X += Math.Sin(_time * 1.3 + p.Phase) * 3; break;
                case "burbujas": p.Y -= 2.5 * v * p.Speed; p.X += Math.Sin(_time * 3 + p.Phase); break;
                case "luciernagas":
                    p.X += Math.Cos(_time * 0.7 + p.Phase) * 1.5 * v;
                    p.Y += Math.Sin(_time * 0.9 + p.Phase) * 1.5 * v;
                    break;
            }
            if (p.Y > h + 8) p.Y = -8;
            else if (p.Y < -8) p.Y = h + 8;
            if (p.X > w + 8) p.X = -8;
            else if (p.X < -8) p.X = w + 8;
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (_scene is not { } scene || _brushes.Length == 0) return;
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 1 || h < 1) return;
        IBrush Brush(int i) => _brushes[i % _brushes.Length];
        void Dot(IBrush brush, double x, double y, int across = 1, int down = 1) =>
            context.FillRectangle(brush, new Rect(Math.Floor(x / Pixel) * Pixel, Math.Floor(y / Pixel) * Pixel, across * Pixel, down * Pixel));

        if (scene.Kind == "playa")
        {
            Beach(Brush, Dot, w, h);
            return;
        }
        Seed(w, h);
        foreach (var p in _particles)
        {
            var brush = Brush(p.Color);
            switch (scene.Kind)
            {
                case "lluvia": Dot(brush, p.X, p.Y, 1, 3); break;
                case "hojas": Dot(brush, p.X, p.Y, 2, 1); Dot(brush, p.X + (Math.Sin(_time + p.Phase) > 0 ? Pixel : 0), p.Y + Pixel); break;
                case "burbujas":
                    var s = p.Size + 1;
                    Dot(brush, p.X, p.Y, s, 1); Dot(brush, p.X, p.Y + s * Pixel - Pixel, s, 1);
                    Dot(brush, p.X, p.Y, 1, s); Dot(brush, p.X + s * Pixel - Pixel, p.Y, 1, s);
                    break;
                case "estrellas" when Math.Sin(_time * 2 + p.Phase) > -0.4:
                    Dot(brush, p.X, p.Y, p.Size, p.Size);
                    break;
                case "luciernagas" when Math.Sin(_time * 3 + p.Phase) > 0:
                    Dot(brush, p.X, p.Y, p.Size, p.Size);
                    break;
                case "nieve": Dot(brush, p.X, p.Y, p.Size, p.Size); break;
            }
        }
    }

    /// <summary>La playa: el sol arriba a la derecha, tres hileras de olas que avanzan y la arena abajo.</summary>
    void Beach(Func<int, IBrush> brush, Action<IBrush, double, double, int, int> dot, double w, double h)
    {
        const int radius = 7;
        double sunX = w - 120, sunY = 60;
        for (var y = -radius; y <= radius; y++)
        {
            var half = (int)Math.Sqrt(radius * radius - y * y);
            dot(brush(0), sunX - half * Pixel, sunY + y * Pixel, half * 2 + 1, 1);
        }
        var sea = h * 0.68;
        for (var row = 0; row < 3; row++)
        {
            var y = sea + row * Pixel * 5;
            for (double x = 0; x < w; x += Pixel)
                if (Math.Sin(x / 26 + _time * 2.2 + row * 1.7) > 0.55) dot(brush(1), x, y, 1, 1);
        }
        dot(brush(2), 0, h * 0.88, (int)(w / Pixel) + 1, (int)(h * 0.12 / Pixel) + 1);
    }
}
