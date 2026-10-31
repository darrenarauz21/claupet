namespace Claupet;

using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using XTerm.Buffer;
using XKey = XTerm.Input.Key;
using XMods = XTerm.Input.KeyModifiers;
using XMouse = XTerm.Input.MouseButton;
using XMouseEvent = XTerm.Input.MouseEventType;
using XTracking = XTerm.Input.MouseTrackingMode;

/// <summary>
/// La pantalla completa de un shell de Claupet Terminal: lo que pinta un programa en la pantalla alterna (vim, less,
/// claude, htop), con sus colores y del tamaño del panel. La interpreta XTerm.NET (ShellSession.Screen) y aquí se dibuja
/// celda por celda; las teclas, el texto, la rueda y los clics le llegan al programa como en cualquier terminal.
/// </summary>
sealed class TerminalScreen : Control
{
    readonly ShellSession _shell;
    readonly Ink _ink;
    /// <summary>Los 16 colores de la paleta elegida en Ajustes › Terminal, y la forma del cursor.</summary>
    readonly int[] _palette;
    readonly string _cursor;
    readonly double _size;
    readonly Typeface _regular, _bold, _italic, _boldItalic;
    readonly Dictionary<int, IBrush> _brushes = [];
    readonly double _cellWidth, _cellHeight;
    readonly StringBuilder _run = new();
    /// <summary>El botón apretado con el seguimiento del mouse encendido (para mandar también cuándo se suelta).</summary>
    XMouse? _pressed;

    public TerminalScreen(ShellSession shell, Ink ink, FontFamily mono, double size, TerminalLook look)
    {
        _shell = shell;
        _ink = ink;
        _palette = look.Colors16(ink.Dark);
        _cursor = look.Cursor;
        _size = size;
        _regular = new Typeface(mono);
        _bold = new Typeface(mono, FontStyle.Normal, FontWeight.Bold);
        _italic = new Typeface(mono, FontStyle.Italic);
        _boldItalic = new Typeface(mono, FontStyle.Italic, FontWeight.Bold);
        (_cellWidth, _cellHeight) = Cell(mono, size);
        Focusable = true;
        ClipToBounds = true;
        FocusAdorner = null;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        AttachedToVisualTree += (_, _) => _shell.ScreenChanged += InvalidateVisual;
        DetachedFromVisualTree += (_, _) => _shell.ScreenChanged -= InvalidateVisual;
        GotFocus += (_, _) => InvalidateVisual();
        LostFocus += (_, _) => InvalidateVisual();
    }

    /// <summary>El ancho y el alto de una celda con esa letra y ese tamaño (el panel calcula con lo mismo).</summary>
    public static (double Width, double Height) Cell(FontFamily mono, double size)
    {
        var probe = new FormattedText("MMMMMMMMMM", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(mono), size, Brushes.White);
        return (probe.WidthIncludingTrailingWhitespace / 10, Math.Ceiling(probe.Height));
    }

    /// <summary>Cuántas columnas y filas caben (la consola toma ese tamaño).</summary>
    public (int Cols, int Rows) Fit(Size size) =>
        ((int)Math.Floor(size.Width / _cellWidth), (int)Math.Floor(size.Height / _cellHeight));

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        // Al aparecer el tamaño cambia varias veces en el mismo acomodo: la consola cambia una sola vez, cuando termina
        // (cada cambio hace que el programa se vuelva a pintar entero).
        if (_resizePending) return;
        _resizePending = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _resizePending = false;
            var (cols, rows) = Fit(Bounds.Size);
            if (cols > 0 && rows > 0 && TopLevel.GetTopLevel(this) is not null) _shell.Resize(cols, rows);
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    bool _resizePending;

    FormattedText Text(string text, Typeface face, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, _size, brush);

    // ── Dibujo ──────────────────────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        var screen = _shell.Screen;
        var buffer = screen.Buffer;
        for (var y = 0; y < screen.Rows; y++)
        {
            var index = buffer.YDisp + y;
            if (index >= buffer.Lines.Length) break;
            if (buffer.Lines[index] is not { } line) continue;
            var cols = Math.Min(line.Length, screen.Cols);
            var x = 0;
            while (x < cols)
            {
                var cell = line[x];
                if (cell.Width == 0)
                {
                    x++;
                    continue;
                }
                // Un tramo con los mismos colores: lo de una sola columna y en ASCII va junto; lo ancho (un emoji, CJK)
                // y lo de otras letras, de a uno y en su celda, para que no corra a los que siguen.
                var start = x;
                var attributes = cell.Attributes;
                _run.Clear();
                _run.Append(Content(cell));
                x += Math.Max(1, cell.Width);
                if (cell.Width == 1 && IsAscii(cell))
                    while (x < cols && line[x] is var next && next.Width == 1 && IsAscii(next) && next.Attributes.Equals(attributes))
                    {
                        _run.Append(Content(next));
                        x++;
                    }
                Draw(context, _run.ToString(), start, y, x - start, attributes);
            }
        }
        var row = buffer.Y + buffer.YBase - buffer.YDisp;
        if (screen.CursorVisible && IsFocused && row >= 0 && row < screen.Rows)
        {
            var x = buffer.X * _cellWidth;
            var y = row * _cellHeight;
            var cursor = _cursor switch
            {
                "barra" => new Rect(x, y, 2, _cellHeight),
                "subrayado" => new Rect(x, y + _cellHeight - 2, _cellWidth, 2),
                _ => new Rect(x, y, _cellWidth, _cellHeight),
            };
            context.FillRectangle(_cursor == "bloque" ? Translucent(_ink.Accent, 0.6) : _ink.Accent, cursor);
        }
    }

    static string Content(BufferCell cell) => string.IsNullOrEmpty(cell.Content) ? " " : cell.Content;

    static bool IsAscii(BufferCell cell) => cell.Content is not { Length: > 0 } text || text.Length == 1 && text[0] < 0x7F;

    void Draw(DrawingContext context, string text, int col, int row, int width, AttributeData attributes)
    {
        var foreground = Color(attributes.GetFgColorMode(), attributes.GetFgColor());
        var background = Color(attributes.GetBgColorMode(), attributes.GetBgColor());
        if (attributes.IsInverse()) (foreground, background) = (background ?? _ink.Surface, foreground ?? _ink.Text);
        var rect = new Rect(col * _cellWidth, row * _cellHeight, width * _cellWidth, _cellHeight);
        if (background is not null) context.FillRectangle(background, rect);
        if (attributes.IsInvisible() || string.IsNullOrWhiteSpace(text)) return;
        var face = attributes.IsBold() ? attributes.IsItalic() ? _boldItalic : _bold : attributes.IsItalic() ? _italic : _regular;
        var formatted = Text(text, face, foreground ?? _ink.Text);
        if (attributes.IsUnderline()) formatted.SetTextDecorations(TextDecorations.Underline);
        else if (attributes.IsStrikethrough()) formatted.SetTextDecorations(TextDecorations.Strikethrough);
        using (attributes.IsDim() ? context.PushOpacity(0.6) : default(DrawingContext.PushedState?))
            context.DrawText(formatted, new Point(rect.X, rect.Y + (_cellHeight - formatted.Height) / 2));
    }

    /// <summary>El color de una celda: el de la paleta (los 16 con los tonos de la Terminal), RGB o null (el de siempre).</summary>
    IBrush? Color(int mode, int color)
    {
        if (mode == 0 && color is < 0 or >= 256) return null;
        var rgb = mode != 0 ? color & 0xFFFFFF : color < 16 ? _palette[color] : _shell.Screen.Colors[color] & 0xFFFFFF;
        if (!_brushes.TryGetValue(rgb, out var brush))
            _brushes[rgb] = brush = new SolidColorBrush(Avalonia.Media.Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        return brush;
    }

    static IBrush Translucent(IBrush brush, double opacity) =>
        brush is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, opacity) : brush;

    // ── Teclado ─────────────────────────────────────────────────────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (ctrl && !alt && e.Key == Key.V)
        {
            // Pegar, como Windows Terminal (Ctrl+V y Ctrl+Mayús+V).
            e.Handled = true;
            _ = PasteAsync();
            return;
        }
        if (Sequence(e.Key, ctrl, alt, shift) is not { } sequence) return;
        e.Handled = true;
        _shell.Input(sequence);
    }

    /// <summary>Lo que manda una tecla que no escribe texto (Intro, flechas, F1…, Ctrl+letra, Alt+letra); null si escribe.</summary>
    string? Sequence(Key key, bool ctrl, bool alt, bool shift)
    {
        var screen = _shell.Screen;
        var mods = (shift ? XMods.Shift : XMods.None) | (alt ? XMods.Alt : XMods.None) | (ctrl ? XMods.Control : XMods.None);
        XKey? special = key switch
        {
            Key.Up => XKey.UpArrow,
            Key.Down => XKey.DownArrow,
            Key.Left => XKey.LeftArrow,
            Key.Right => XKey.RightArrow,
            Key.Home => XKey.Home,
            Key.End => XKey.End,
            Key.PageUp => XKey.PageUp,
            Key.PageDown => XKey.PageDown,
            Key.Insert => XKey.Insert,
            Key.Delete => XKey.Delete,
            >= Key.F1 and <= Key.F12 => (XKey)((int)XKey.F1 + (key - Key.F1)),
            _ => null,
        };
        if (special is { } k) return screen.GenerateKeyInput(k, mods);
        switch (key)
        {
            case Key.Enter: return alt ? "\x1b\r" : "\r";
            case Key.Tab: return shift ? "\x1b[Z" : "\t";
            case Key.Back: return ctrl ? "\b" : alt ? "\x1b\x7f" : "\x7f";
            case Key.Escape: return "\x1b";
        }
        if (ctrl && !alt)
        {
            if (key is >= Key.A and <= Key.Z) return ((char)(key - Key.A + 1)).ToString();
            return key switch
            {
                Key.Space or Key.D2 => "\0",
                Key.OemOpenBrackets => "\x1b",
                Key.OemPipe or Key.OemBackslash => "\x1c",
                Key.OemCloseBrackets => "\x1d",
                Key.D6 => "\x1e",
                Key.OemMinus => "\x1f",
                _ => null,
            };
        }
        if (alt && !ctrl && key is >= Key.A and <= Key.Z) return "\x1b" + (char)((shift ? 'A' : 'a') + (key - Key.A));
        return null;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        // Las letras, los signos y lo que arma AltGr; los caracteres de control ya salieron por OnKeyDown.
        if (string.IsNullOrEmpty(e.Text) || e.Text.All(char.IsControl)) return;
        e.Handled = true;
        _shell.Input(e.Text);
    }

    async Task PasteAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        var text = await clipboard.TryGetTextAsync();
        if (string.IsNullOrEmpty(text)) return;
        text = text.ReplaceLineEndings("\r");
        _shell.Input(_shell.Screen.BracketedPasteMode ? "\x1b[200~" + text + "\x1b[201~" : text);
    }

    // ── Mouse ───────────────────────────────────────────────────────────────────────────────

    (int Col, int Row) Cell(Point point) =>
        (Math.Clamp((int)(point.X / _cellWidth), 0, _shell.Screen.Cols - 1), Math.Clamp((int)(point.Y / _cellHeight), 0, _shell.Screen.Rows - 1));

    static XMods Mods(KeyModifiers keys) =>
        (keys.HasFlag(KeyModifiers.Shift) ? XMods.Shift : XMods.None) | (keys.HasFlag(KeyModifiers.Alt) ? XMods.Alt : XMods.None)
        | (keys.HasFlag(KeyModifiers.Control) ? XMods.Control : XMods.None);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        e.Handled = true;
        var screen = _shell.Screen;
        if (screen.MouseTrackingMode == XTracking.None) return;
        var point = e.GetCurrentPoint(this).Properties;
        XMouse button = point.IsRightButtonPressed ? XMouse.Right : point.IsMiddleButtonPressed ? XMouse.Middle : XMouse.Left;
        var (col, row) = Cell(e.GetPosition(this));
        _pressed = button;
        _shell.Input(screen.GenerateMouseEvent(button, col, row, XMouseEvent.Down, Mods(e.KeyModifiers)));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pressed is not { } button) return;
        _pressed = null;
        var (col, row) = Cell(e.GetPosition(this));
        _shell.Input(_shell.Screen.GenerateMouseEvent(button, col, row, XMouseEvent.Up, Mods(e.KeyModifiers)));
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        e.Handled = true;
        var screen = _shell.Screen;
        var up = e.Delta.Y > 0;
        var steps = Math.Clamp((int)Math.Round(Math.Abs(e.Delta.Y)), 1, 10);
        var (col, row) = Cell(e.GetPosition(this));
        for (var i = 0; i < steps; i++)
            // Con el seguimiento del mouse, la rueda; si no, flechas (como Windows Terminal en la pantalla alterna).
            _shell.Input(screen.MouseTrackingMode != XTracking.None
                ? screen.GenerateMouseEvent(up ? XMouse.WheelUp : XMouse.WheelDown, col, row, up ? XMouseEvent.WheelUp : XMouseEvent.WheelDown, Mods(e.KeyModifiers))
                : string.Concat(Enumerable.Repeat(screen.GenerateKeyInput(up ? XKey.UpArrow : XKey.DownArrow), 3)));
    }
}
