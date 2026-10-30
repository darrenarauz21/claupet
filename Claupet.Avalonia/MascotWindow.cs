using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Claupet;

/// <summary>
/// La mascota sobre el escritorio (un personaje o la clásica), sin marco ni fondo y siempre encima (igual
/// que en Windows). Hace lo que diga ChibiDirector; se arrastra para cambiarla de lugar (sin salirse
/// de la pantalla), un clic la hace saludar (y muchos, enojarse) y el clic derecho abre su menú. Se le pueden soltar
/// archivos encima: los atrapa (FileHover) y los lleva a la isla (FilesDropped, WalkToAsync).
/// </summary>
sealed class MascotWindow : Window
{
    /// <summary>Un archivo pasa por encima (true) o se va (false): la mascota lo espera con los brazos abiertos.</summary>
    public event Action<bool>? FileHover;
    public event Action<string[]>? FilesDropped;

    /// <summary>Lleva un archivo: no se arrastra ni atrapa otro.</summary>
    public bool Busy { get; set; }

    public event Action? OpenRequested;
    public event Action? HideRequested;
    /// <summary>Un clic encima (después del saludo o el enojo que corresponda).</summary>
    public event Action? Poked;
    /// <summary>Se soltó en otro lugar (esquina de arriba a la izquierda, en pixeles de pantalla).</summary>
    public event Action<PixelPoint>? Moved;

    readonly ChibiDirector _director;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly MascotView _view;
    /// <summary>Su lugar: el guardado o el último adonde se la arrastró (a él vuelve si cambian las pantallas).</summary>
    PixelPoint? _saved;
    PixelPoint? _pressAt;
    PixelPoint _startPosition;
    bool _dragging;

    /// <summary>Un personaje (el chibi u otro de ChibiSprite.Skins; true) o la clásica, que se pinta con el acento de la paleta.</summary>
    public bool Chibi => Skin is not null;

    /// <summary>El personaje que se pinta; null con la clásica o con la mascota de un paquete.</summary>
    public ChibiSkin? Skin { get; }

    /// <summary>La mascota de un paquete de extensión, si se eligió una: se pinta en vez del chibi o la clásica.</summary>
    public PetSheet? Pet { get; }

    /// <summary>Se ve en todos los Espacios de macOS (si no, solo en el que se abrió).</summary>
    public bool AllDesktops { get; }

    /// <summary>Dónde está en la pantalla, en pixeles (para la nubecita).</summary>
    public PixelRect ScreenBounds => new(Position, PixelSize.FromSize(Bounds.Size, RenderScaling));

    /// <summary>Su tamaño (MascotSizes).</summary>
    string _size;

    public MascotWindow(ChibiDirector director, PixelPoint? saved, ChibiSkin? skin, Func<System.Drawing.Color> accent, bool allDesktops = true,
        string size = MascotSizes.Normal, PetSheet? pet = null)
    {
        _director = director;
        _size = size;
        Skin = pet is null ? skin : null;
        Pet = pet;
        AllDesktops = allDesktops;
        _saved = saved;
        Title = "Claupet";
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        _view = new MascotView(director, Skin, pet, Skin is not null ? 3 : 4, accent) { Cursor = new Cursor(StandardCursorType.Hand) };
        Content = _view;
        WinStyles.Float(this);
        Resize(Screens.Primary?.Scaling ?? 1);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, (_, e) =>
        {
            if (Busy || FilesOf(e).Length == 0) { e.DragEffects = DragDropEffects.None; return; }
            e.DragEffects = DragDropEffects.Copy;
            FileHover?.Invoke(true);
        });
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => FileHover?.Invoke(false));
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var files = FilesOf(e);
            if (!Busy && files.Length > 0) FilesDropped?.Invoke(files);
            else FileHover?.Invoke(false);
        });

        var menu = new ContextMenu();
        var open = new MenuItem { Header = L.T("Abrir Claupet") };
        open.Click += (_, _) => OpenRequested?.Invoke();
        var hide = new MenuItem { Header = L.T("Ocultar del escritorio") };
        // Después del clic: ocultar cierra esta ventana, y con ella el menú que está avisando.
        hide.Click += (_, _) => Dispatcher.UIThread.Post(() => HideRequested?.Invoke());
        menu.Items.Add(open);
        menu.Items.Add(hide);
        ContextMenu = menu;

        _timer.Tick += (_, _) => _view.Tick();
        Opened += (_, _) =>
        {
            Resize(RenderScaling); // la escala de la pantalla en que quedó
            Position = Fit(_saved ?? DefaultPosition());
            if (AllDesktops) JoinAllSpaces();
            // Al apagarse o desconectarse un monitor, vuelve a su lugar (o al más cercano que se vea).
            Screens.Changed += (_, _) => Dispatcher.UIThread.Post(() => Position = Fit(_saved ?? Position));
            _director.Appear(); // saluda al aparecer
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>Las rutas de lo que se arrastra (solo archivos del equipo), como en la isla.</summary>
    static string[] FilesOf(DragEventArgs e) =>
#pragma warning disable CS0618 // DataTransfer recién llega en 11.3 y todavía no la usa el resto de la app
        e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
#pragma warning restore CS0618

    /// <summary>Lo que mide cada casilla en pixeles reales (para planear el lanzamiento de la caja).</summary>
    public int PixelUnit => UnitFor(RenderScaling);

    /// <summary>Pixeles reales por casilla con esa escala; la de un paquete mide en pantalla lo mismo que el chibi.</summary>
    int UnitFor(double scale) =>
        Pet is { } pet ? MascotSizes.PetUnit(_size, pet.Height, (float)scale) : MascotSizes.Unit(_size, Chibi, (float)scale);

    /// <summary>
    /// Después de bloquear, suspender o apagar la pantalla, Windows puede haberla movido (al desconectarse un monitor) o
    /// dejado debajo de otras: vuelve a su lugar (o al más cercano que se vea), arriba de todo y repintada.
    /// </summary>
    public void Recover()
    {
        if (!IsVisible) Show();
        if (!_dragging) Position = Fit(_saved ?? Position);
        Topmost = false;
        Topmost = true;
        _view.InvalidateVisual();
    }

    /// <summary>Camina hasta x (a su misma altura, en pixeles) en ms milisegundos, con un arranque y una llegada suaves.</summary>
    public async Task WalkToAsync(int x, int ms)
    {
        var from = Position.X;
        var start = Environment.TickCount64;
        while (true)
        {
            var k = Math.Clamp((Environment.TickCount64 - start) / (double)Math.Max(1, ms), 0, 1);
            var ease = k < 0.1 ? k * k * 5 : k > 0.9 ? 1 - (1 - k) * (1 - k) * 5 : k - 0.05; // casi constante, sin tirones
            Position = new PixelPoint((int)Math.Round(from + (x - from) * Math.Clamp(ease, 0, 1)), Position.Y);
            if (k >= 1) break;
            await Task.Delay(15);
        }
        Position = new PixelPoint(x, Position.Y);
    }

    /// <summary>Otro tamaño: crece o se achica sin moverse de su esquina de arriba (salvo para no salirse de la pantalla).</summary>
    public void SetSize(string size)
    {
        _size = size;
        Resize(RenderScaling);
        Position = Fit(Position);
    }

    /// <summary>
    /// Cada casilla mide un número entero de pixeles reales (MascotSizes, con la escala de la pantalla), para que los
    /// pixeles no se deformen; en la interfaz, eso dividido por la escala.
    /// </summary>
    void Resize(double scale)
    {
        var unit = UnitFor(scale) / scale;
        _view.Unit = unit;
        Width = (Pet?.Width ?? (Chibi ? ChibiSprite.Columns : ToastSprite.Columns)) * unit;
        Height = (Pet?.Height ?? (Chibi ? ChibiSprite.Rows : ToastSprite.Rows)) * unit;
        _view.InvalidateVisual();
    }

    /// <summary>La posición más cercana con la ventana entera dentro de la pantalla en que cae: nunca se pierde por un borde.</summary>
    PixelPoint Fit(PixelPoint at)
    {
        var screen = Screens.ScreenFromPoint(at) ?? Placement.PrimaryScreen(this);
        var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1280, 800);
        var scale = screen?.Scaling ?? 1;
        var width = (int)Math.Ceiling(Width * scale);
        var height = (int)Math.Ceiling(Height * scale);
        return new PixelPoint(Math.Clamp(at.X, area.X, Math.Max(area.X, area.Right - width)),
            Math.Clamp(at.Y, area.Y, Math.Max(area.Y, area.Bottom - height)));
    }

    /// <summary>
    /// En macOS, que se vea en todos los Espacios y sobre las apps a pantalla completa (NSWindow.collectionBehavior:
    /// CanJoinAllSpaces | Stationary | FullScreenAuxiliary). Avalonia da la ventana nativa o su vista.
    /// </summary>
    void JoinAllSpaces()
    {
        if (!OperatingSystem.IsMacOS() || TryGetPlatformHandle() is not { } handle) return;
        var window = handle.HandleDescriptor switch
        {
            "NSWindow" => handle.Handle,
            "NSView" => ObjC.Send(handle.Handle, ObjC.Selector("window")),
            _ => IntPtr.Zero,
        };
        if (window != IntPtr.Zero) ObjC.Send(window, ObjC.Selector("setCollectionBehavior:"), (nuint)(1 | 16 | 256));
    }

    static class ObjC
    {
        const string Lib = "/usr/lib/libobjc.A.dylib";
        [DllImport(Lib, EntryPoint = "sel_registerName")] public static extern IntPtr Selector(string name);
        [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern IntPtr Send(IntPtr receiver, IntPtr selector);
        [DllImport(Lib, EntryPoint = "objc_msgSend")] public static extern void Send(IntPtr receiver, IntPtr selector, nuint value);
    }

    /// <summary>Abajo a la derecha, a la izquierda de donde salen los avisos.</summary>
    PixelPoint DefaultPosition()
    {
        var screen = Placement.PrimaryScreen(this);
        var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1280, 800);
        var scale = screen?.Scaling ?? 1;
        var width = (int)Math.Ceiling(Width * scale);
        var height = (int)Math.Ceiling(Height * scale);
        return new PixelPoint(area.Right - (int)((ToastWindow.ToastWidth + 24) * scale) - width, area.Bottom - height);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Busy) return;
        _pressAt = this.PointToScreen(e.GetPosition(this));
        _startPosition = Position;
        _dragging = false;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_pressAt is not { } press) return;
        var now = this.PointToScreen(e.GetPosition(this));
        var (dx, dy) = (now.X - press.X, now.Y - press.Y);
        if (!_dragging && Math.Abs(dx) + Math.Abs(dy) < 4 * RenderScaling) return;
        _dragging = true;
        Position = Fit(new PixelPoint(_startPosition.X + dx, _startPosition.Y + dy));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left || _pressAt is null) return;
        _pressAt = null;
        if (_dragging)
        {
            _saved = Position;
            Moved?.Invoke(Position);
        }
        else
        {
            _director.Poke(); // saluda; si lo siguen molestando, se enoja
            Poked?.Invoke();
        }
    }

    sealed class MascotView(ChibiDirector director, ChibiSkin? skin, PetSheet? pet, double unit, Func<System.Drawing.Color> accent) : Control
    {
        /// <summary>Lo que mide cada casilla, en unidades de la interfaz.</summary>
        public double Unit { get; set; } = unit;

        (ChibiMove Move, int Frame, long Beat) _shown = (ChibiMove.Sleep, -1, -1);

        /// <summary>Repinta solo cuando cambia el cuadro o, con música, el tiempo del compás (la clásica se anima con el tiempo: cada vuelta del reloj).</summary>
        public void Tick()
        {
            var (move, t, music) = director.Now();
            var shown = (move, skin is not null ? ChibiSprite.FrameIndex(move, t, skin) : (int)(t / 40), music ? t / ChibiSprite.Beat : -1);
            if (shown == _shown) return;
            _shown = shown;
            InvalidateVisual();
        }

        public override void Render(DrawingContext dc)
        {
            var (move, t, music) = director.Now();
            if (pet is not null) Gfx.Pixels(dc, 0, 0, Unit, fill => pet.Paint(fill, move, t));
            else if (skin is not null) Gfx.Pixels(dc, 0, 0, Unit, fill => ChibiSprite.Paint(fill, move, t, music, skin));
            else Gfx.Pixels(dc, 0, 0, Unit, fill => ToastSprite.PaintMove(fill, move, t, accent(), music));
        }
    }
}
