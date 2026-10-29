using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using SD = System.Drawing;

namespace Claupet;

/// <summary>
/// La nubecita del chat con la mascota, de pixeles como la de «Hablar» (BubbleSprite), igual que en Windows. Tiene
/// cuatro momentos: el campo para escribir (con el chip del modelo, que despliega los otros dentro de la nube), el micrófono mientras se le habla, los tres
/// puntos mientras Claude piensa y la respuesta, que aparece de a poco. Si la respuesta es una tarea, ofrece abrir la
/// sesión; si Claupet tiene que preguntar algo, lo pregunta con botones (Choose). Va arriba de la mascota (o abajo, si
/// no cabe) y la sigue si la arrastran. Esc la cierra.
/// </summary>
sealed class ChatBubbleWindow : Window
{
    public enum Mode { Input, Thinking, Answer, Listening }

    const double Unit = 2, Pad = Unit * 5, TextWidth = 224;
    const int AnswerLifetime = 14000;
    static readonly SD.Color Ink = SD.Color.FromArgb(0x2A, 0x1F, 0x1A);
    static readonly SD.Color Muted = SD.Color.FromArgb(0x8F, 0x8D, 0x86);
    static readonly SD.Color Chip = SD.Color.FromArgb(0xD8, 0xD4, 0xCB);
    static readonly SD.Color Accent = SD.Color.FromArgb(0xC2, 0x5E, 0x3E);
    static readonly SD.Color Mic = SD.Color.FromArgb(0xD9, 0x4B, 0x3F);
    static readonly SD.Color Quote = SD.Color.FromArgb(0x5E, 0x5D, 0x59);
    // Los mismos tamaños que en Windows (9,5 y 8,25 puntos).
    static readonly UiFont Font = new(Gfx.Body.Typeface, 12.7);
    static readonly UiFont Small = new(Gfx.Body.Typeface, 11);
    static readonly UiFont ButtonFont = new(Gfx.BodyStrong.Typeface, 11);
    static readonly Geometry MicGlyph = Geometry.Parse("M8,2 A2,2 0 0 1 10,4 L10,7.5 A2,2 0 0 1 6,7.5 L6,4 A2,2 0 0 1 8,2 Z M4.5,7.5 A3.5,3.5 0 0 0 11.5,7.5 M8,11 L8,13.5");

    /// <summary>Se envió lo escrito.</summary>
    public event Action<string>? Sent;
    /// <summary>Se eligió otro modelo entre los del chip.</summary>
    public event Action<string>? ModelPicked;
    /// <summary>Clic en «Ver en la isla».</summary>
    public event Action? IslandClicked;
    /// <summary>La tarea: true abre la sesión en la carpeta propuesta; false elige otra carpeta.</summary>
    public event Action<bool>? TaskChosen;
    /// <summary>Se cerró (Esc, clic fuera o se acabó su tiempo).</summary>
    public event Action? Dismissed;
    /// <summary>La respuesta terminó de aparecer (la mascota deja de mover la boca).</summary>
    public event Action? DoneTalking;

    readonly BubbleView _view;
    readonly TextBox _box;
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(30) };
    Mode _mode = Mode.Input;
    // Los modelos para elegir, el elegido y si están desplegados bajo el chip.
    IReadOnlyList<(string Id, string Label)> _models = [];
    string _modelId = "";
    bool _picking;
    readonly List<(Rect Bounds, string Id)> _modelBounds = [];
    string _target = "";      // la respuesta completa que se conoce hasta ahora
    int _visible;             // cuántas letras se ven: avanza de a poco hacia _target
    bool _streaming, _island, _error, _hover, _wasTalking;
    /// <summary>Quién respondió («Sin IA · al instante»), en gris junto a «Ver en la isla»; null: no se dice.</summary>
    string? _who;
    string? _taskPrompt, _taskFolder;
    long _doneAt;
    int _cols, _rows, _tailX;
    bool _tailDown = true;
    PixelRect _mascot;
    Rect _chipBounds, _linkBounds, _openBounds, _otherBounds;
    string? _pressed;
    // Una pregunta de Claupet: sus botones (Id y texto), cuál es el principal y quién espera la respuesta.
    IReadOnlyList<(string Id, string Label)>? _choices;
    string _choicePrimary = "";
    TaskCompletionSource<string?>? _choiceTask;
    readonly List<(Rect Bounds, string Id)> _choiceBounds = [];
    string? _question;
    long _questionAt;
    // Escuchando: el nivel que se ve (sube enseguida y baja de a poco), lo entendido y cómo terminar.
    double _level;
    string _heard = "", _hint = "";
    bool _converting;

    /// <summary>Mientras hay un diálogo abierto (elegir carpeta), perder el foco no la cierra.</summary>
    public bool HoldOpen { get; set; }

    public Mode Current => _mode;

    /// <summary>La respuesta todavía está apareciendo (para que la mascota mueva la boca).</summary>
    public bool Talking => _mode == Mode.Answer && (_streaming || _visible < _target.Length);

    public ChatBubbleWindow()
    {
        Title = "Claupet";
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        RequestedThemeVariant = ThemeVariant.Light; // la nube es blanca en los dos modos

        _view = new BubbleView(this);
        _box = new TextBox
        {
            FontFamily = Font.Typeface.FontFamily,
            FontSize = Font.Size,
            Foreground = Gfx.B(Ink),
            Background = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MinHeight = 0,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true, // Mayús+Intro: otra línea; Intro solo envía (OnBoxKey)
            Watermark = L.T("Pregúntame algo o pídeme una tarea…"),
            Width = TextWidth,
        };
        // Sin los colores del tema al pasar el mouse o con el foco: es parte de la nube.
        foreach (var state in new[] { "", "PointerOver", "Focused", "Disabled" })
        {
            _box.Resources["TextControlBackground" + state] = Brushes.White;
            _box.Resources["TextControlBorderBrush" + state] = Brushes.Transparent;
            _box.Resources["TextControlForeground" + state] = Gfx.B(Ink);
            _box.Resources["TextControlPlaceholderForeground" + state] = Gfx.B(Muted);
        }
        _box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        _box.Resources["TextControlThemeMinHeight"] = 0.0;
        _box.AddHandler(KeyDownEvent, OnBoxKey, RoutingStrategies.Tunnel);
        _box.TextChanged += (_, _) => { if (_mode == Mode.Input) Relayout(); };

        var canvas = new Canvas();
        canvas.Children.Add(_view);
        canvas.Children.Add(_box);
        Content = canvas;

        _tick.Tick += (_, _) => Tick();
        // Perder el foco cierra el campo; un momento después, por si vuelve (al cerrarse un diálogo, por ejemplo).
        Deactivated += (_, _) => DispatcherTimer.RunOnce(() =>
        {
            if (!IsActive && IsVisible && _mode == Mode.Input && !HoldOpen) Dismiss(notify: true);
        }, TimeSpan.FromMilliseconds(150));
    }

    // ── Momentos ────────────────────────────────────────────────────────────────────────────

    /// <summary>El campo para escribir, junto a la mascota, con los modelos del chip. Se queda con el foco para escribir enseguida.</summary>
    public void AskInput(PixelRect mascot, IReadOnlyList<(string Id, string Label)> models, string modelId)
    {
        DropChoice(null);
        _mode = Mode.Input;
        _models = models;
        _modelId = modelId;
        _picking = false;
        _target = "";
        _visible = 0;
        _taskPrompt = null;
        _error = false;
        _box.IsVisible = true;
        Relayout(mascot);
        if (!IsVisible) Show();
        Activate();
        _box.Focus();
        _box.CaretIndex = _box.Text?.Length ?? 0;
        _tick.Start();
    }

    /// <summary>El modelo cambió desde Ajustes.</summary>
    public void SetModel(string modelId)
    {
        _modelId = modelId;
        _view.InvalidateVisual();
    }

    string ModelLabel => _models.FirstOrDefault(m => m.Id == _modelId).Label ?? _modelId;

    /// <summary>Los tres puntos mientras llega la respuesta, bajo la pregunta que se le hizo (en gris).</summary>
    public void Think(PixelRect mascot, string? question = null)
    {
        DropChoice(null);
        _question = question;
        _questionAt = Environment.TickCount64;
        _mode = Mode.Thinking;
        _box.IsVisible = false;
        _target = "";
        _visible = 0;
        _streaming = true;
        Relayout(mascot);
        if (!IsVisible) Show();
        _tick.Start();
    }

    /// <summary>El micrófono abierto: el nivel de la voz, lo que va entendiendo y cómo se termina (hint).</summary>
    public void Listen(PixelRect mascot, string hint)
    {
        DropChoice(null);
        _mode = Mode.Listening;
        _box.IsVisible = false;
        _heard = "";
        _hint = hint;
        _converting = false;
        _level = 0;
        _taskPrompt = null;
        _error = false;
        Relayout(mascot);
        if (!IsVisible) Show();
        _tick.Start();
    }

    public void SetLevel(float level) => _level = Math.Max(_level, Math.Min(1, level * 2.2));

    public void SetHeard(string text)
    {
        if (_mode != Mode.Listening) return;
        _heard = text;
        Relayout();
    }

    /// <summary>Soltó la tecla: queda lo entendido mientras se pasa todo a texto.</summary>
    public void Converting()
    {
        if (_mode != Mode.Listening) return;
        _converting = true;
        _view.InvalidateVisual();
    }

    /// <summary>Llega otro trozo de la respuesta: se agrega y aparece de a poco.</summary>
    public void Append(string piece)
    {
        if (_mode != Mode.Answer)
        {
            _mode = Mode.Answer;
            _box.IsVisible = false;
        }
        _target += piece;
        Relayout();
        _tick.Start();
    }

    /// <summary>
    /// La respuesta terminó: el texto final (puede corregir lo que llegó por partes), si ofrecer «Ver en la isla» y,
    /// si es una tarea, la tarea y el nombre de la carpeta donde se abriría.
    /// </summary>
    public void Finish(string text, bool island, string? taskPrompt = null, string? taskFolder = null, bool error = false, string? who = null)
    {
        DropChoice(null);
        ShowAnswer(text, island, taskPrompt, taskFolder, error);
        _who = error || taskPrompt is not null ? null : who;
        Relayout();
    }

    /// <summary>
    /// Una pregunta de Claupet (¿dejo que capture la pantalla?) con sus botones; primary es el Id del principal.
    /// Devuelve el Id elegido, o null si la cerraron o llegó otra cosa.
    /// </summary>
    public Task<string?> Choose(PixelRect mascot, string text, IReadOnlyList<(string Id, string Label)> options, string primary)
    {
        DropChoice(null);
        _mascot = mascot;
        _choices = options;
        _choicePrimary = primary;
        var task = _choiceTask = new TaskCompletionSource<string?>();
        ShowAnswer(text, island: false, null, null, error: false);
        return task.Task;
    }

    /// <summary>Un mensaje corto de la mascota (al abrir una tarea, por ejemplo), con su tiempo para irse.</summary>
    public void Say(PixelRect mascot, string text)
    {
        _mascot = mascot;
        Finish(text, island: false);
    }

    void DropChoice(string? id)
    {
        var task = _choiceTask;
        _choiceTask = null;
        _choices = null;
        _choiceBounds.Clear();
        task?.TrySetResult(id);
    }

    void ShowAnswer(string text, bool island, string? taskPrompt, string? taskFolder, bool error)
    {
        _mode = Mode.Answer;
        _box.IsVisible = false;
        _streaming = false;
        _error = error;
        _island = island && !error;
        _who = null;
        _taskPrompt = taskPrompt;
        _taskFolder = taskFolder;
        if (taskPrompt is not null)
            text = L.T("Eso es una tarea. ¿Abro Claude Code en «{0}» con esto?", taskFolder);
        if (!text.StartsWith(_target[..Math.Min(_visible, _target.Length)], StringComparison.Ordinal)) _visible = 0;
        _target = text;
        _doneAt = Environment.TickCount64;
        Relayout();
        if (!IsVisible) Show();
        _tick.Start();
    }

    void Tick()
    {
        var now = Environment.TickCount64;
        if (_mode == Mode.Answer && _visible < _target.Length)
        {
            // Unas 70 letras por segundo; si se atrasa mucho, se pone al día más rápido.
            var behind = _target.Length - _visible;
            _visible = Math.Min(_target.Length, _visible + Math.Max(2, behind / 12));
            _view.InvalidateVisual();
        }
        else if (_mode == Mode.Thinking) _view.InvalidateVisual();
        else if (_mode == Mode.Listening)
        {
            _level *= 0.9;
            _view.InvalidateVisual();
        }
        var talking = Talking;
        if (_wasTalking && !talking) DoneTalking?.Invoke();
        _wasTalking = talking;

        var waiting = _taskPrompt is not null || _choices is not null;
        if (_mode == Mode.Answer && !_streaming && _visible >= _target.Length && !waiting && !_hover && now - _doneAt > AnswerLifetime)
            Dismiss(notify: true);
    }

    /// <summary>Se esconde (no se destruye: se vuelve a usar); notify avisa que se cerró sin terminar.</summary>
    public void Dismiss(bool notify)
    {
        DropChoice(null);
        if (!IsVisible) return;
        _tick.Stop();
        Hide();
        if (notify) Dismissed?.Invoke();
    }

    // ── Medidas y lugar ─────────────────────────────────────────────────────────────────────

    static FormattedText Wrap(string text, UiFont font, IBrush brush, double width)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, font.Typeface, font.Size, brush)
        {
            MaxTextWidth = width,
        };
        return ft;
    }

    static double WrappedHeight(string text, UiFont font, double width) => Wrap(text.Length == 0 ? " " : text, font, Brushes.Black, width).Height;

    double ContentWidth => _mode != Mode.Thinking ? TextWidth
        : _question is null ? 56
        : Math.Clamp(Gfx.Measure(QuoteOf(_question), Small).Width + 4, 56, TextWidth);

    static string QuoteOf(string question) => L.T("«{0}»", question.ReplaceLineEndings(" "));

    double BodyTop => _tailDown ? 0 : BubbleSprite.Tail * Unit;

    void Relayout(PixelRect? mascot = null)
    {
        if (mascot is { } m) _mascot = m;
        var width = ContentWidth;
        double height;
        switch (_mode)
        {
            case Mode.Input:
                var text = _box.Text is { Length: > 0 } typed ? typed + (typed.EndsWith('\n') ? "W" : "") : "W";
                var lines = Math.Clamp((int)Math.Round(WrappedHeight(text, Font, width) / Font.LineHeight), 1, 5);
                var boxHeight = lines * Font.LineHeight;
                _box.Height = boxHeight;
                height = boxHeight + 8 + 20;
                if (_picking) height += 6 + ModelLayout(0, 0).Max(m => m.Bounds.Bottom);
                break;
            case Mode.Thinking:
                height = Font.LineHeight + (_question is null ? 0 : Small.LineHeight + 4);
                break;
            case Mode.Listening:
                height = WrappedHeight(HeardShown, Font, HeardWidth) + 6 + Small.LineHeight;
                break;
            default:
                height = WrappedHeight(_target, Font, width);
                if (_taskPrompt is not null) height += 4 + TaskQuote(width).Height + 10 + 26;
                else if (_choices is not null) height += 10 + ChoiceLayout(0, 0).Max(c => c.Bounds.Bottom);
                else if (_island || _who is not null) height += 6 + Small.LineHeight;
                break;
        }
        _cols = (int)((width + Pad * 2) / Unit) + 1;
        _rows = (int)((height + Pad * 2) / Unit) + 1 + BubbleSprite.Tail;
        Width = _cols * Unit;
        Height = _rows * Unit;
        _view.Width = Width;
        _view.Height = Height;
        Follow(_mascot);
    }

    /// <summary>Arriba de la mascota con la colita hacia su cabeza (o abajo, si arriba no cabe), sin salirse de la pantalla.</summary>
    public void Follow(PixelRect mascot)
    {
        _mascot = mascot;
        var screen = Screens.ScreenFromPoint(mascot.Center) ?? Placement.PrimaryScreen(this);
        var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1280, 800);
        var scale = screen?.Scaling ?? 1;
        var width = (int)Math.Ceiling(Width * scale);
        var height = (int)Math.Ceiling(Height * scale);
        var headX = mascot.X + mascot.Width / 2;
        var head = mascot.Y + mascot.Height / 20;
        _tailDown = head - height >= area.Y;
        var y = _tailDown ? head - height : mascot.Bottom - mascot.Height / 20;
        var x = Math.Clamp(headX - width * 2 / 3, area.X, Math.Max(area.X, area.Right - width));
        y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - height));
        _tailX = (int)((headX - x) / scale / Unit);
        Canvas.SetLeft(_box, Pad);
        Canvas.SetTop(_box, BodyTop + Pad);
        Position = new PixelPoint(x, y);
        _view.InvalidateVisual();
    }

    /// <summary>El ancho del texto al escuchar: lo que queda junto al micrófono y las barritas.</summary>
    double HeardWidth => ContentWidth - 58;

    /// <summary>Lo entendido, en tres líneas como mucho: si no cabe, el final (lo último que se dijo).</summary>
    string HeardShown
    {
        get
        {
            if (_heard.Length == 0) return L.T("Te escucho…");
            var text = _heard;
            var max = Font.LineHeight * 3 + 1;
            while (text.Length > 0 && WrappedHeight("…" + text, Font, HeardWidth) > max)
                text = text.IndexOf(' ') is var space and > 0 ? text[(space + 1)..] : text[1..];
            return text.Length == _heard.Length ? text : "…" + text;
        }
    }

    /// <summary>La tarea entre comillas, en dos líneas como mucho.</summary>
    FormattedText TaskQuote(double width)
    {
        var ft = Wrap(L.T("«{0}»", _taskPrompt), Small, Gfx.B(Quote), width);
        ft.MaxLineCount = 2;
        ft.Trimming = TextTrimming.CharacterEllipsis;
        return ft;
    }

    /// <summary>Dónde va cada modelo desplegado: chips en fila, y si no caben, en la de abajo.</summary>
    List<(Rect Bounds, string Id, string Label)> ModelLayout(double x, double y)
    {
        var list = new List<(Rect, string, string)>();
        var cx = x;
        const double height = 20;
        foreach (var (id, label) in _models)
        {
            var width = Gfx.Measure(label, Small).Width + 18;
            if (cx > x && cx + width > x + ContentWidth)
            {
                cx = x;
                y += height + 6;
            }
            list.Add((new Rect(cx, y, width, height), id, label));
            cx += width + 6;
        }
        return list;
    }

    /// <summary>Dónde va cada botón de la pregunta: en fila, y si no caben, en la de abajo.</summary>
    List<(Rect Bounds, string Id, string Label)> ChoiceLayout(double x, double y)
    {
        var list = new List<(Rect, string, string)>();
        var cx = x;
        const double height = 22 + Unit;
        foreach (var (id, label) in _choices ?? [])
        {
            var width = Gfx.Measure(label, ButtonFont).Width + 20 + Unit * 2;
            if (cx > x && cx + width > x + ContentWidth)
            {
                cx = x;
                y += height + 8;
            }
            list.Add((new Rect(cx, y, width, height), id, label));
            cx += width + 10;
        }
        return list;
    }

    // ── Dibujo ──────────────────────────────────────────────────────────────────────────────

    void Paint(DrawingContext dc)
    {
        Gfx.Pixels(dc, 0, 0, Unit, fill => BubbleSprite.Paint(fill, _cols, _rows, _tailX, _tailDown));
        var x = Pad;
        var y = BodyTop + Pad;
        var width = ContentWidth;
        _chipBounds = _linkBounds = _openBounds = _otherBounds = default;

        switch (_mode)
        {
            case Mode.Input:
            {
                var row = y + (_box.Height is var h && double.IsNaN(h) ? Font.LineHeight : h) + 8;
                var model = ModelLabel;
                var textWidth = Gfx.Measure(model, Small).Width;
                _chipBounds = new Rect(x, row, textWidth + 26, 18);
                dc.DrawRectangle(null, Gfx.P(Chip), _chipBounds.Deflate(0.5), 9, 9);
                Gfx.Write(dc, model, Small, Ink, new Rect(_chipBounds.X + 8, row, textWidth + 2, _chipBounds.Height), center: true);
                // El triángulo apunta abajo; con los modelos desplegados, arriba.
                var ax = _chipBounds.Right - 13;
                var ay = row + _chipBounds.Height / 2 - 1;
                Point[] arrow = _picking ? [new(ax, ay + 3), new(ax + 6, ay + 3), new(ax + 3, ay)] : [new(ax, ay), new(ax + 6, ay), new(ax + 3, ay + 3)];
                dc.DrawGeometry(Gfx.B(Ink), null, new PolylineGeometry(arrow, isFilled: true));
                _modelBounds.Clear();
                if (_picking)
                    foreach (var (bounds, id, label) in ModelLayout(x, _chipBounds.Bottom + 6))
                    {
                        var chosen = id == _modelId;
                        if (chosen) dc.DrawRectangle(Gfx.B(Chip), null, bounds, 10, 10);
                        else dc.DrawRectangle(null, Gfx.P(Chip), bounds.Deflate(0.5), 10, 10);
                        Gfx.Write(dc, label, Small, Ink, bounds, TextAlignment.Center, center: true);
                        _modelBounds.Add((bounds, id));
                    }
                var hint = L.T("Enter para enviar");
                var hintWidth = Gfx.Measure(hint, Small).Width;
                Gfx.Write(dc, hint, Small, Muted, new Rect(x + width - hintWidth, row, hintWidth + 2, _chipBounds.Height), center: true);
                break;
            }
            case Mode.Thinking:
            {
                var tick = Environment.TickCount64;
                if (_question is { } question)
                {
                    // La pregunta queda arriba, en gris, apareciendo (de blanco a gris) mientras sube un poco.
                    var k = Math.Clamp((tick - _questionAt) / 260.0, 0, 1);
                    var ease = 1 - (1 - k) * (1 - k) * (1 - k);
                    Gfx.Write(dc, QuoteOf(question), Small, Gfx.Blend(SD.Color.White, Muted, ease),
                        new Rect(x, y + (1 - ease) * 6, width + 2, Small.LineHeight));
                    y += Small.LineHeight + 4;
                }
                // Tres puntos que saltan uno detrás del otro (como «Pensando» en Actividad).
                for (var i = 0; i < 3; i++)
                {
                    var bounce = Flows.DotBounce(tick, i);
                    var r = 3 + bounce;
                    var cx = x + 8 + i * 14;
                    var cy = y + Font.LineHeight / 2 + 2 - bounce * 5;
                    dc.DrawEllipse(Gfx.B(Gfx.Blend(Muted, Ink, bounce)), null, new Point(cx, cy), r, r);
                }
                break;
            }
            case Mode.Listening:
                PaintListening(dc, x, y);
                break;
            default:
            {
                var shown = _target[..Math.Min(_visible, _target.Length)];
                var full = WrappedHeight(_target, Font, width);
                dc.DrawText(Wrap(shown, Font, Gfx.B(_error ? Muted : Ink), width), new Point(x, y));
                var below = y + full;
                if (_taskPrompt is not null && _visible >= _target.Length)
                {
                    var quote = TaskQuote(width);
                    dc.DrawText(quote, new Point(x, below + 4));
                    var by = below + 4 + quote.Height + 10;
                    _openBounds = PixelButton(dc, L.T("Abrir sesión"), x, by, primary: true, pressed: _pressed == "open");
                    _otherBounds = PixelButton(dc, L.T("Otra carpeta…"), _openBounds.Right + 10, by, primary: false, pressed: _pressed == "other");
                }
                else if (_choices is not null && _visible >= _target.Length)
                {
                    _choiceBounds.Clear();
                    foreach (var (bounds, id, text) in ChoiceLayout(x, below + 10))
                        _choiceBounds.Add((PixelButton(dc, text, bounds.X, bounds.Y, primary: id == _choicePrimary, pressed: _pressed == "ch:" + id), id));
                }
                else if ((_island || _who is not null) && _visible >= _target.Length)
                {
                    double lw = 0;
                    if (_island)
                    {
                        var link = L.T("Ver en la isla");
                        lw = Gfx.Measure(link, Small).Width;
                        _linkBounds = new Rect(x, below + 6, lw, Small.LineHeight);
                        Gfx.Write(dc, link, Small, Accent, _linkBounds.Inflate(new Thickness(0, 0, 2, 0)));
                    }
                    if (_who is { } who)
                    {
                        var left = _island ? x + lw + 12 : x;
                        Gfx.Write(dc, who, Small, Muted, new Rect(left, below + 6, x + width - left, Small.LineHeight),
                            _island ? TextAlignment.Right : TextAlignment.Left);
                    }
                }
                break;
            }
        }
    }

    /// <summary>El micrófono rojo, las barritas del nivel (o tres puntos mientras pasa a texto), lo entendido y, debajo, cómo se termina.</summary>
    void PaintListening(DrawingContext dc, double x, double y)
    {
        var tick = Environment.TickCount64;
        var mic = new Rect(x, y + (Font.LineHeight - 20) / 2, 20, 20);
        dc.FillRectangle(Gfx.B(Mic), mic); // cuadrado, como los pixeles de la nubecita
        using (dc.PushTransform(Matrix.CreateScale(1.1, 1.1) * Matrix.CreateTranslation(mic.X + 1.2, mic.Y + 1.2)))
            dc.DrawGeometry(null, Gfx.P(SD.Color.White, 1.4), MicGlyph);

        var barsX = mic.Right + 6;
        var middle = mic.Y + mic.Height / 2;
        var brush = Gfx.B(Mic);
        if (_converting)
            for (var i = 0; i < 3; i++)
            {
                var bounce = Flows.DotBounce(tick, i);
                var r = 2 + bounce;
                dc.DrawEllipse(brush, null, new Point(barsX + 4 + i * 8, middle - bounce * 3), r, r);
            }
        else
            for (var i = 0; i < Flows.BarPeriods.Length; i++)
            {
                var period = Flows.BarPeriods[i];
                var wave = 0.5 + 0.5 * Math.Sin(tick % period / (double)period * 2 * Math.PI + i);
                var h = Math.Max(3, 16 * (0.35 + 0.65 * wave) * (0.2 + 0.8 * _level));
                dc.FillRectangle(brush, new Rect(barsX + i * 4, middle - h / 2, 2, h));
            }

        var shown = HeardShown;
        var text = Wrap(shown, Font, Gfx.B(_heard.Length == 0 ? Muted : Ink), HeardWidth);
        dc.DrawText(text, new Point(x + 58, y));
        var hint = _converting ? L.T("Pasando a texto…") : L.T("{0} · Esc cancela", _hint);
        Gfx.Write(dc, hint, Small, _converting ? Muted : Mic, new Rect(x, y + text.Height + 6, ContentWidth, Small.LineHeight));
    }

    /// <summary>Un botón de pixeles: contorno oscuro y una sombra de una casilla que desaparece al pulsarlo.</summary>
    static Rect PixelButton(DrawingContext dc, string text, double x, double y, bool primary, bool pressed)
    {
        var tw = Gfx.Measure(text, ButtonFont).Width;
        var rect = new Rect(x, y + (pressed ? Unit : 0), tw + 20, 22);
        dc.FillRectangle(primary ? Gfx.B(Accent) : Brushes.White, rect);
        var ink = Gfx.B(Ink);
        dc.FillRectangle(ink, new Rect(rect.X, rect.Y, rect.Width, Unit));
        dc.FillRectangle(ink, new Rect(rect.X, rect.Bottom - Unit, rect.Width, Unit));
        dc.FillRectangle(ink, new Rect(rect.X, rect.Y, Unit, rect.Height));
        dc.FillRectangle(ink, new Rect(rect.Right - Unit, rect.Y, Unit, rect.Height));
        if (!pressed) dc.FillRectangle(ink, new Rect(rect.X + Unit * 2, rect.Bottom, rect.Width, Unit));
        Gfx.Write(dc, text, ButtonFont, primary ? SD.Color.White : Ink, rect, TextAlignment.Center, center: true);
        return new Rect(x, y, rect.Width, rect.Height + Unit);
    }

    // ── Teclado y mouse ─────────────────────────────────────────────────────────────────────

    void OnBoxKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        e.Handled = true;
        var text = (_box.Text ?? "").Trim();
        if (text.Length == 0) return;
        _box.Text = "";
        Sent?.Invoke(text);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape) return;
        if (_picking) // primero se pliegan los modelos
        {
            _picking = false;
            Relayout();
        }
        else Dismiss(notify: true);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hover = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = false;
        _doneAt = Math.Max(_doneAt, Environment.TickCount64 - AnswerLifetime + 2500); // al irse el mouse, le quedan unos segundos
    }

    bool OnSomething(Point p) => _chipBounds.Contains(p) || _modelBounds.Any(m => m.Bounds.Contains(p)) || _linkBounds.Contains(p) || _openBounds.Contains(p) || _otherBounds.Contains(p)
        || _choiceBounds.Any(c => c.Bounds.Contains(p));

    void ViewMoved(Point p) =>
        _view.Cursor = new Cursor(OnSomething(p) ? StandardCursorType.Hand : StandardCursorType.Arrow);

    void ViewPressed(Point p)
    {
        _pressed = _openBounds.Contains(p) ? "open" : _otherBounds.Contains(p) ? "other"
            : _choiceBounds.FirstOrDefault(c => c.Bounds.Contains(p)).Id is { } choice ? "ch:" + choice : null;
        if (_pressed is not null) _view.InvalidateVisual();
    }

    void ViewReleased(Point p, bool left)
    {
        var pressed = _pressed;
        _pressed = null;
        if (!left) return;
        if (_chipBounds.Contains(p))
        {
            _picking = !_picking;
            Relayout();
            _box.Focus();
            return;
        }
        if (_modelBounds.FirstOrDefault(m => m.Bounds.Contains(p)).Id is { } picked)
        {
            _picking = false;
            if (picked != _modelId)
            {
                _modelId = picked;
                ModelPicked?.Invoke(picked);
            }
            Relayout();
            _box.Focus();
            return;
        }
        if (_linkBounds.Contains(p)) { Dismiss(notify: false); IslandClicked?.Invoke(); return; }
        if (pressed == "open" && _openBounds.Contains(p)) { _taskPrompt = null; TaskChosen?.Invoke(true); return; }
        if (pressed == "other" && _otherBounds.Contains(p)) { TaskChosen?.Invoke(false); return; }
        if (pressed?.StartsWith("ch:", StringComparison.Ordinal) == true && _choiceBounds.Any(c => "ch:" + c.Id == pressed && c.Bounds.Contains(p)))
        {
            DropChoice(pressed[3..]);
            return;
        }
        if (_mode == Mode.Answer && _taskPrompt is null && _choices is null && !Talking) Dismiss(notify: true);
        else _view.InvalidateVisual();
    }

    sealed class BubbleView(ChatBubbleWindow owner) : Control
    {
        public override void Render(DrawingContext dc) => owner.Paint(dc);

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            owner.ViewMoved(e.GetPosition(this));
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) owner.ViewPressed(e.GetPosition(this));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            owner.ViewReleased(e.GetPosition(this), e.InitialPressMouseButton == MouseButton.Left);
        }
    }
}
