namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

/// <summary>
/// El fondo animado de un tema propio: una escena del catálogo (CustomThemes.SceneCatalog) en pixel art, como la mascota,
/// a 12 cuadros por segundo y con poca opacidad, detrás del contenido de la Console (las tarjetas siguen opacas). Las
/// partículas van en dos planos: las lejanas, más chicas, lentas y tenues, dan profundidad. Con un píxel más chico y más
/// opacidad es la vista previa de una tarjeta de tema. Se pausa con la ventana minimizada u oculta (Paused) y fuera del
/// árbol visual.
/// </summary>
sealed class SceneLayer : Control
{
    readonly int _pixel;
    readonly double _alpha;
    /// <summary>Cuánto se mueve todo respecto del tamaño de siempre (4 px por píxel): la vista previa va a escala.</summary>
    readonly double _scale;

    sealed class Particle
    {
        public double X, Y, Speed, Phase;
        public int Size, Color;
        /// <summary>Del plano de adelante; si no, del lejano.</summary>
        public bool Near;
    }

    /// <summary>Un edificio de la ciudad: dónde empieza, su ancho y su alto en píxeles, y sus ventanas.</summary>
    sealed record Building(int Left, int Width, int Height, double[] Windows);

    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1000 / 12.0) };
    readonly Random _random = new(7);
    readonly List<Particle> _particles = [];
    readonly List<Building> _city = [];
    ThemeScene? _scene;
    IBrush[] _near = [], _far = [];
    double _time, _cityWidth;
    bool _paused;

    /// <param name="pixel">El lado de un píxel del pixel art.</param>
    /// <param name="alpha">La opacidad del plano de adelante (el lejano, la mitad).</param>
    public SceneLayer(int pixel = 4, double alpha = 0.16)
    {
        _pixel = pixel;
        _alpha = alpha;
        _scale = pixel / 4.0;
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
        IBrush Brush(System.Drawing.Color c, double alpha) => new ImmutableSolidColorBrush(Color.FromRgb(c.R, c.G, c.B), alpha);
        _near = scene?.Colors.Select(c => Brush(c, _alpha)).ToArray() ?? [];
        _far = scene?.Colors.Select(c => Brush(c, _alpha * 0.5)).ToArray() ?? [];
        _particles.Clear();
        _city.Clear();
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

    /// <summary>Tantas partículas como pida la densidad para este tamaño, repartidas al azar; dos de cada cinco, adelante.</summary>
    void Seed(double width, double height)
    {
        var wanted = (int)Math.Clamp(_scene!.Density * width * height / (6000 * _scale * _scale), 8, 240);
        if (_particles.Count == wanted) return;
        _particles.Clear();
        for (var i = 0; i < wanted; i++)
            _particles.Add(new Particle
            {
                X = _random.NextDouble() * width, Y = _random.NextDouble() * height, Speed = 0.6 + _random.NextDouble() * 0.8,
                Phase = _random.NextDouble() * Math.PI * 2, Size = 1 + _random.Next(2), Color = _random.Next(_near.Length),
                Near = _random.NextDouble() < 0.4,
            });
    }

    void Step()
    {
        if (_scene is not { } scene) return;
        double w = Bounds.Width, h = Bounds.Height, v = scene.Speed * _scale;
        _time += scene.Speed / 12;
        foreach (var p in _particles)
        {
            // Lo lejano va más lento.
            var s = p.Speed * (p.Near ? 1 : 0.55);
            switch (scene.Kind)
            {
                case "lluvia": p.Y += 16 * v * s; p.X -= 2.5 * v * s; break;
                case "nieve": p.Y += 2 * v * s; p.X += Math.Sin(_time * 2 + p.Phase) * 1.5 * _scale; break;
                case "hojas": p.Y += 3 * v * s; p.X += Math.Sin(_time * 1.3 + p.Phase) * 3 * _scale; break;
                case "sakura": p.Y += 1.6 * v * s; p.X += (1.4 + Math.Sin(_time * 1.7 + p.Phase) * 2) * v * s; break;
                case "burbujas": p.Y -= 2.5 * v * s; p.X += Math.Sin(_time * 3 + p.Phase) * _scale; break;
                case "brasas": p.Y -= 3.5 * v * s; p.X += Math.Sin(_time * 4 + p.Phase) * 1.2 * _scale; break;
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
        if (_scene is not { } scene || _near.Length == 0) return;
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 1 || h < 1) return;
        var px = _pixel;
        // El color i en el plano de adelante (Front) o en el lejano (Back).
        IBrush Front(int i) => _near[i % _near.Length];
        IBrush Back(int i) => _far[i % _far.Length];
        void Dot(IBrush brush, double x, double y, int across = 1, int down = 1) =>
            context.FillRectangle(brush, new Rect(Math.Floor(x / px) * px, Math.Floor(y / px) * px, across * px, down * px));

        switch (scene.Kind)
        {
            case "playa": Beach(Front, Back, Dot, w, h); return;
            case "atardecer": Sunset(Front, Back, Dot, w, h); return;
            case "aurora": Aurora(Front, Back, Dot, w, h); return;
            case "ciudad": City(Front, Back, Dot, w, h); return;
        }
        Seed(w, h);
        if (scene.Kind == "brasas") Coals(Back, Dot, w, h);
        foreach (var p in _particles)
        {
            var brush = p.Near ? Front(p.Color) : Back(p.Color);
            var big = p.Near ? p.Size + 1 : p.Size;
            switch (scene.Kind)
            {
                case "lluvia": Dot(brush, p.X, p.Y, 1, p.Near ? 3 : 2); break;
                case "nieve": Dot(brush, p.X, p.Y, big, big); break;
                case "hojas":
                    // Gira: la hoja se ve de frente o de canto.
                    var flat = Math.Sin(_time * 2 + p.Phase) > 0;
                    Dot(brush, p.X, p.Y, flat ? 2 : 1, flat ? 1 : 2);
                    Dot(brush, p.X + (flat ? px : 0), p.Y + px);
                    break;
                case "sakura":
                    Dot(brush, p.X, p.Y, 2, 1);
                    Dot(brush, p.X + (Math.Sin(_time * 3 + p.Phase) > 0 ? px : 0), p.Y + px);
                    break;
                case "burbujas":
                    var s = big + 1;
                    Dot(brush, p.X + px, p.Y, s - 2, 1); Dot(brush, p.X + px, p.Y + (s - 1) * px, s - 2, 1);
                    Dot(brush, p.X, p.Y + px, 1, s - 2); Dot(brush, p.X + (s - 1) * px, p.Y + px, 1, s - 2);
                    if (p.Near) Dot(brush, p.X + px, p.Y + px); // el brillo
                    break;
                case "estrellas":
                    var shine = Math.Sin(_time * 2 + p.Phase);
                    if (shine < -0.4) break;
                    Dot(brush, p.X, p.Y);
                    // Las de adelante, en su momento más brillante, echan una cruz.
                    if (p.Near && shine > 0.85)
                    {
                        Dot(Back(p.Color), p.X - px, p.Y); Dot(Back(p.Color), p.X + px, p.Y);
                        Dot(Back(p.Color), p.X, p.Y - px); Dot(Back(p.Color), p.X, p.Y + px);
                    }
                    break;
                case "luciernagas" when Math.Sin(_time * 3 + p.Phase) > 0:
                    Dot(brush, p.X, p.Y, p.Size, p.Size);
                    if (p.Near)
                    {
                        // El halo, tenue alrededor.
                        var halo = Back(p.Color);
                        Dot(halo, p.X - px, p.Y, 1, p.Size); Dot(halo, p.X + p.Size * px, p.Y, 1, p.Size);
                        Dot(halo, p.X, p.Y - px, p.Size, 1); Dot(halo, p.X, p.Y + p.Size * px, p.Size, 1);
                    }
                    break;
                case "brasas" when Math.Sin(_time * 5 + p.Phase) > -0.3:
                    Dot(brush, p.X, p.Y, p.Near ? 2 : 1, p.Near ? 2 : 1);
                    break;
            }
        }
        if (scene.Kind == "estrellas") ShootingStar(Front, Back, Dot, w, h);
    }

    /// <summary>Cada tanto, una estrella fugaz cruza en diagonal con su estela.</summary>
    void ShootingStar(Func<int, IBrush> near, Func<int, IBrush> far, Action<IBrush, double, double, int, int> dot, double w, double h)
    {
        const double every = 7, lasts = 1.2;
        var at = _time % every;
        if (at > lasts) return;
        var run = (int)(_time / every);
        var x0 = w * (0.2 + 0.6 * ((run * 0.37) % 1));
        var y0 = h * 0.1;
        var progress = at / lasts;
        double x = x0 + progress * w * 0.35, y = y0 + progress * h * 0.25;
        for (var i = 0; i < 6; i++)
            dot(i < 2 ? near(0) : far(0), x - i * _pixel * 1.4, y - i * _pixel, 1, 1);
    }

    /// <summary>La playa: el sol con su brillo, nubes que pasan, olas que avanzan con destellos del sol y la arena abajo.</summary>
    void Beach(Func<int, IBrush> near, Func<int, IBrush> far, Action<IBrush, double, double, int, int> dot, double w, double h)
    {
        var px = _pixel;
        var radius = Math.Max(4, (int)(Math.Min(w, h) * 0.07 / px));
        double sunX = w * 0.82, sunY = h * 0.18;
        Disk(far(0), dot, sunX, sunY, radius + 2);
        Disk(near(0), dot, sunX, sunY, radius);
        // Dos nubes que cruzan despacio.
        for (var c = 0; c < 2; c++)
        {
            var cx = (_time * 6 * _scale * (c + 1) + c * w * 0.55) % (w + 120 * _scale) - 60 * _scale;
            var cy = h * (0.12 + c * 0.14);
            dot(far(2), cx, cy, 8, 1);
            dot(far(2), cx + 2 * px, cy - px, 4, 1);
        }
        var sea = h * 0.66;
        for (var row = 0; row < 4; row++)
        {
            var y = sea + row * px * 5;
            for (double x = 0; x < w; x += px)
            {
                var wave = Math.Sin(x / (26 * _scale) + _time * 2.2 + row * 1.7);
                if (wave > 0.55) dot(row < 2 ? near(1) : far(1), x, y, 1, 1);
                else if (wave < -0.97 && Math.Sin(_time * 5 + x) > 0.6) dot(near(0), x, y, 1, 1); // un destello
            }
        }
        dot(near(2), 0, h * 0.9, (int)(w / px) + 1, (int)(h * 0.1 / px) + 1);
        dot(far(2), 0, h * 0.88, (int)(w / px) + 1, 1);
    }

    /// <summary>
    /// El atardecer: el cielo en franjas que se desvanecen hacia arriba, el sol bajo con las rayas de los ochenta, su
    /// reflejo que tiembla en el agua y unos pájaros que cruzan aleteando.
    /// </summary>
    void Sunset(Func<int, IBrush> near, Func<int, IBrush> far, Action<IBrush, double, double, int, int> dot, double w, double h)
    {
        var px = _pixel;
        var horizon = h * 0.62;
        var across = (int)(w / px) + 1;
        // El cielo: franjas cada vez más ralas hacia arriba.
        for (var band = 0; band < 5; band++)
        {
            var y = horizon - (band + 1) * h * 0.08;
            for (var row = 0; row < 4 - band / 2; row++) dot(far(1), 0, y + row * px * 2, across, 1);
        }
        var radius = Math.Max(5, (int)(Math.Min(w, h) * 0.16 / px));
        double sunX = w * 0.5;
        for (var y = -radius; y <= 0; y++)
        {
            // Las rayas: en la mitad de abajo del disco, una fila sí y otra no, cada vez más anchas.
            var depth = radius + y;
            if (depth < radius * 0.45 && depth % 3 == 0) continue;
            var half = (int)Math.Sqrt(radius * radius - y * y);
            dot(near(0), sunX - half * px, horizon + y * px, half * 2 + 1, 1);
        }
        // El reflejo: rayas cortas que se mueven sobre el agua.
        for (var row = 1; row < 9; row++)
        {
            var y = horizon + row * px * 2;
            var width = (int)(radius * 1.6 * (1 - row / 10.0));
            var shift = Math.Sin(_time * 3 + row) * px * 2;
            dot(row < 4 ? near(2) : far(2), sunX - width * px / 2 + shift, y, width, 1);
        }
        dot(far(2), 0, horizon, across, 1);
        for (var b = 0; b < 3; b++)
        {
            var x = (_time * 10 * _scale * (1 + b * 0.3) + b * w * 0.33) % (w + 40) - 20;
            var y = h * (0.18 + b * 0.07) + Math.Sin(_time + b) * px;
            var up = Math.Sin(_time * 6 + b * 2) > 0;
            dot(near(1), x, y, 1, 1);
            dot(near(1), x - px, y - (up ? px : 0), 1, 1);
            dot(near(1), x + px, y - (up ? px : 0), 1, 1);
        }
    }

    /// <summary>La aurora: cortinas que ondulan (con la luz arriba y el velo que se desvanece abajo) sobre estrellas quietas.</summary>
    void Aurora(Func<int, IBrush> near, Func<int, IBrush> far, Action<IBrush, double, double, int, int> dot, double w, double h)
    {
        var px = _pixel;
        Seed(w, h);
        var stars = _near.Length > 2 ? 2 : 0;
        foreach (var p in _particles.Where(p => p.Y < h * 0.7))
            if (Math.Sin(_time * 1.5 + p.Phase) > -0.2) dot(far(stars), p.X, p.Y, 1, 1);
        for (var ribbon = 0; ribbon < 2; ribbon++)
        {
            var color = Math.Min(ribbon, _near.Length - 1);
            var baseY = h * (0.22 + ribbon * 0.16);
            for (double x = 0; x < w; x += px)
            {
                var y = baseY + Math.Sin(x / (70 * _scale) + _time * 0.8 + ribbon * 2) * 18 * _scale + Math.Sin(x / (23 * _scale) - _time * 1.3) * 5 * _scale;
                var tall = (int)((6 + 4 * Math.Sin(x / (40 * _scale) + _time + ribbon)) * _scale) + 2;
                dot(near(color), x, y, 1, 1);
                dot(far(color), x, y + px, 1, tall);
            }
        }
    }

    /// <summary>La ciudad: siluetas de edificios abajo, con ventanas que se encienden y se apagan, y estrellas encima.</summary>
    void City(Func<int, IBrush> near, Func<int, IBrush> far, Action<IBrush, double, double, int, int> dot, double w, double h)
    {
        var px = _pixel;
        if (_city.Count == 0 || Math.Abs(_cityWidth - w) > px)
        {
            _city.Clear();
            _cityWidth = w;
            for (var left = 0; left * px < w;)
            {
                var width = 6 + _random.Next(9);
                var height = (int)(h * (0.15 + _random.NextDouble() * 0.3) / px);
                var windows = Enumerable.Range(0, (width / 2) * (height / 3)).Select(_ => _random.NextDouble() * Math.PI * 2).ToArray();
                _city.Add(new Building(left, width, height, windows));
                left += width + _random.Next(2);
            }
        }
        Seed(w, h);
        var sky = _near.Length > 2 ? 2 : 0;
        foreach (var p in _particles.Where(p => p.Y < h * 0.45))
            if (Math.Sin(_time + p.Phase) > 0) dot(far(sky), p.X, p.Y, 1, 1);
        foreach (var b in _city)
        {
            var top = h - b.Height * px;
            dot(far(0), b.Left * px, top, b.Width, b.Height);
            var across = b.Width / 2;
            for (var i = 0; i < b.Windows.Length; i++)
            {
                // Cada ventana tiene su ritmo: muy de vez en cuando cambia.
                if (Math.Sin(_time * 0.25 + b.Windows[i]) < 0.3) continue;
                var col = i % across;
                var row = i / across;
                dot(near(Math.Min(1, _near.Length - 1)), (b.Left + 1 + col * 2) * px, top + (1 + row * 3) * px, 1, 2);
            }
        }
    }

    /// <summary>Las brasas: la base que brilla abajo, con alturas que cambian como una llama baja.</summary>
    void Coals(Func<int, IBrush> far, Action<IBrush, double, double, int, int> dot, double w, double h)
    {
        var px = _pixel;
        var color = Math.Min(1, _far.Length - 1);
        for (double x = 0; x < w; x += px * 2)
        {
            var tall = 1 + (int)(2.5 + 2.5 * Math.Sin(x / (17 * _scale) + _time * 4) * Math.Sin(x / (7 * _scale) - _time * 2.5));
            dot(far(color), x, h - tall * px, 2, tall);
        }
    }

    /// <summary>Un círculo de píxeles.</summary>
    static void Disk(IBrush brush, Action<IBrush, double, double, int, int> dot, double cx, double cy, int radius, int pixel)
    {
        for (var y = -radius; y <= radius; y++)
        {
            var half = (int)Math.Sqrt(radius * radius - y * y);
            dot(brush, cx - half * pixel, cy + y * pixel, half * 2 + 1, 1);
        }
    }

    void Disk(IBrush brush, Action<IBrush, double, double, int, int> dot, double cx, double cy, int radius) =>
        Disk(brush, dot, cx, cy, radius, _pixel);
}
