namespace Claupet;

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Web.WebView2.Core;

/// <summary>
/// Una sección HTML o SVG de Claupet Canvas, viva dentro del panel (solo Windows): el motor de Edge (WebView2) en una
/// ventana nativa hija. La página (CanvasExport.Live) lleva la sección en un iframe aislado, como la exportación: no ve
/// los archivos ni a Claupet, y solo le pasa su alto y la rueda que no usó. Los enlaces se abren en el navegador, y las
/// descargas y los permisos (cámara, micrófono, ubicación…) se niegan.
/// Avalonia no recorta las ventanas nativas: esta se recorta sola a lo visible del canvas y deja ver lo que flota
/// encima (el aviso, la línea de las versiones…), con una región de Windows.
/// </summary>
sealed class CanvasLivePage : NativeControlHost
{
    /// <summary>El alto de la sección: entre esto y Tallest, según su contenido; más alta, se desplaza por dentro.</summary>
    const double Shortest = 60, Tallest = 2400;

    static Task<CoreWebView2Environment>? _engine;
    static bool? _available;

    readonly Uri _page;
    readonly Func<IEnumerable<Control>> _above;
    Color _background;
    /// <summary>La ventana de Avalonia que sostiene la nativa (la que se recorta) y la nativa, donde va WebView2.</summary>
    IntPtr _holder, _hwnd;
    CoreWebView2Controller? _controller;
    Rect _viewport;
    bool _hasViewport, _fitQueued;

    /// <summary>La rueda que la sección no usó, en píxeles: el canvas se desplaza eso.</summary>
    public event Action<double>? Wheel;
    /// <summary>WebView2 no arrancó: el canvas vuelve al botón del navegador.</summary>
    public event Action? Failed;

    /// <summary>La página que muestra: si no cambió, el canvas la reusa al redibujarse (sin volver a cargarla).</summary>
    public string Html { get; }

    /// <param name="file">La página ya escrita (CanvasExport.Live), que se carga desde el disco.</param>
    /// <param name="above">Lo que flota encima en ese momento: esa parte no se tapa.</param>
    public CanvasLivePage(string file, string html, double height, Color background, Func<IEnumerable<Control>> above)
    {
        _page = new Uri(file);
        Html = html;
        Height = Math.Clamp(height, Shortest, Tallest);
        _background = background;
        _above = above;
        EffectiveViewportChanged += (_, e) =>
        {
            _viewport = e.EffectiveViewport;
            _hasViewport = true;
            Refit();
        };
        SizeChanged += (_, _) => Refit();
    }

    /// <summary>Si se puede: Windows con WebView2 (viene con Windows 11 y con Edge en Windows 10).</summary>
    public static bool Available => OperatingSystem.IsWindows() && (_available ??= Probe());

    static bool Probe()
    {
        try { return CoreWebView2Environment.GetAvailableBrowserVersionString() is { Length: > 0 }; }
        catch (WebView2RuntimeNotFoundException) { return false; }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or COMException or EntryPointNotFoundException)
        {
            ErrorLog.Write(ex);
            return false;
        }
    }

    /// <summary>Un solo motor para todas las secciones, con su perfil en la carpeta de datos (no al lado del .exe, que puede no dejar escribir).</summary>
    static Task<CoreWebView2Environment> Engine()
    {
        if (_engine is { IsFaulted: false, IsCanceled: false } ready) return ready;
        return _engine = CoreWebView2Environment.CreateAsync(null, DataFolder.In("WebView2"), new CoreWebView2EnvironmentOptions { Language = L.Language });
    }

    /// <summary>El color de detrás (el del panel): se ve en las esquinas redondeadas y mientras la página carga.</summary>
    public void SetBackground(Color color)
    {
        _background = color;
        if (_controller is { } controller) controller.DefaultBackgroundColor = Drawing(color);
    }

    /// <summary>Vuelve a medir y a recortar (después del acomodo): lo que flota encima cambió, o la sección se movió.</summary>
    public void Refit()
    {
        if (_fitQueued) return;
        _fitQueued = true;
        Dispatcher.UIThread.Post(() => { _fitQueued = false; Fit(); }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Avalonia crea la ventana nativa al entrar al árbol, en medio del acomodo: si Windows no la deja crear, el error
    /// cortaba el acomodo y la Console quedaba vacía. Aquí se anota y el canvas vuelve al botón del navegador.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        try { base.OnAttachedToVisualTree(e); }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or Win32Exception)
        {
            ErrorLog.Write(ex);
            Dispatcher.UIThread.Post(() => Failed?.Invoke());
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = base.CreateNativeControlCore(parent);
        if (OperatingSystem.IsWindows() && handle.HandleDescriptor == "HWND")
        {
            _holder = parent.Handle;
            _hwnd = handle.Handle;
            _ = StartAsync(_hwnd);
        }
        return handle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _hwnd = _holder = IntPtr.Zero;
        var controller = _controller;
        _controller = null;
        try { controller?.Close(); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { ErrorLog.Write(ex); }
        base.DestroyNativeControlCore(control);
    }

    async Task StartAsync(IntPtr hwnd)
    {
        try
        {
            var engine = await Engine();
            if (hwnd != _hwnd) return;
            var controller = await engine.CreateCoreWebView2ControllerAsync(hwnd);
            if (hwnd != _hwnd)
            {
                controller.Close();
                return;
            }
            _controller = controller;
            controller.DefaultBackgroundColor = Drawing(_background);
            var web = controller.CoreWebView2;
            var settings = web.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.AreHostObjectsAllowed = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            // Solo carga su página; lo demás (un enlace, una ventana nueva) va al navegador.
            web.NavigationStarting += (_, e) =>
            {
                if (Ours(e.Uri)) return;
                e.Cancel = true;
                Outside(e.Uri);
            };
            web.FrameNavigationStarting += (_, e) =>
            {
                if (e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) || e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                    || e.Uri.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)) return;
                e.Cancel = true;
                Outside(e.Uri);
            };
            web.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                Outside(e.Uri);
            };
            web.DownloadStarting += (_, e) => e.Cancel = true;
            web.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            web.WebMessageReceived += OnMessage;
            Fit();
            web.Navigate(_page.AbsoluteUri);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorLog.Write(ex);
            if (hwnd == _hwnd) Failed?.Invoke();
        }
    }

    bool Ours(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var other)
        && Uri.Compare(other, _page, UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;

    /// <summary>Un enlace de la sección: solo direcciones web y de correo, en el programa del sistema.</summary>
    static void Outside(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var target) && target.Scheme is "http" or "https" or "mailto")
            Shell.Open(target.AbsoluteUri);
    }

    void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!Ours(e.Source)) return;
        try
        {
            using var json = JsonDocument.Parse(e.WebMessageAsJson);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (root.TryGetProperty("h", out var h) && h.ValueKind == JsonValueKind.Number && h.GetDouble() is var height && double.IsFinite(height))
            {
                // El borde del marco (1 px arriba y abajo) va aparte del alto del contenido.
                var next = Math.Clamp(Math.Ceiling(height) + 2, Shortest, Tallest);
                if (Math.Abs(next - Height) > 1) Height = next;
            }
            else if (root.TryGetProperty("w", out var w) && w.ValueKind == JsonValueKind.Number && w.GetDouble() is var dy && double.IsFinite(dy))
                Wheel?.Invoke(Math.Clamp(dy, -4000, 4000));
        }
        catch (JsonException) { }
    }

    void Fit()
    {
        if (!OperatingSystem.IsWindows() || _hwnd == IntPtr.Zero) return;
        if (_controller is { } controller && GetClientRect(_hwnd, out var size))
        {
            controller.Bounds = new System.Drawing.Rectangle(0, 0, size.Right - size.Left, size.Bottom - size.Top);
            controller.NotifyParentWindowPositionChanged();
        }
        Cut();
    }

    /// <summary>
    /// Deja ver solo lo que está dentro del canvas (no lo que se desplazó arriba o abajo) y no tapa lo que flota encima.
    /// La región va en la ventana de Avalonia que sostiene la nativa: así tampoco se ve ese marco vacío.
    /// </summary>
    void Cut()
    {
        if (_holder == IntPtr.Zero) return;
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var full = new Rect(Bounds.Size);
        var visible = _hasViewport ? full.Intersect(_viewport) : full;
        var holes = new List<Rect>();
        if (visible.Width > 0 && visible.Height > 0)
            foreach (var cover in _above())
            {
                if (!cover.IsEffectivelyVisible || cover.GetVisualParent() is not { } parent || parent.TransformToVisual(this) is not { } toHere) continue;
                // Bounds y no la transformación: lo que entra subiendo se mide donde va a quedar.
                var area = cover.Bounds.TransformToAABB(toHere).Inflate(2).Intersect(visible);
                if (area.Width > 0 && area.Height > 0) holes.Add(area);
            }
        if (holes.Count == 0 && visible == full)
        {
            SetWindowRgn(_holder, IntPtr.Zero, true);
            return;
        }
        var region = visible.Width > 0 && visible.Height > 0 ? Region(visible, scale) : CreateRectRgn(0, 0, 0, 0);
        foreach (var hole in holes)
        {
            var cut = Region(hole, scale);
            CombineRgn(region, region, cut, RgnDiff);
            DeleteObject(cut);
        }
        // Si Windows la acepta, la región pasa a ser suya; si no, se libera acá.
        if (SetWindowRgn(_holder, region, true) == 0) DeleteObject(region);
    }

    static IntPtr Region(Rect r, double scale) => CreateRectRgn(
        (int)Math.Floor(r.X * scale), (int)Math.Floor(r.Y * scale), (int)Math.Ceiling(r.Right * scale), (int)Math.Ceiling(r.Bottom * scale));

    static System.Drawing.Color Drawing(Color c) => System.Drawing.Color.FromArgb(255, c.R, c.G, c.B);

    const int RgnDiff = 4;

    [StructLayout(LayoutKind.Sequential)]
    struct Native32Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out Native32Rect rect);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] static extern int CombineRgn(IntPtr destination, IntPtr first, IntPtr second, int mode);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
}
