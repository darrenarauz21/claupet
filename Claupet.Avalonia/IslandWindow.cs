using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using SD = System.Drawing;

namespace Claupet;

/// <summary>
/// La isla del borde de arriba de la pantalla, como en Windows. Oculta, acercar el cursor al centro del borde la hace
/// asomar compacta (cabeza de la mascota, estado y uso); con el cursor encima un momento, o con un clic, se abre con
/// sus pestañas: Actividad (las sesiones con sus pasos), Chat (con el modelo de sus sesiones) y «+ Sesión». También
/// recibe archivos soltados encima y los analiza con Claude, y muestra los permisos y las preguntas de Claude Code.
/// Es una ventana transparente, siempre oscura, con el acento de la paleta. Abierta, un clic fuera la pliega (salvo
/// que algo la retenga). Dónde está el cursor lo dice GlobalPointer; sin él (Wayland), solo asoma mientras algo trabaja.
/// </summary>
sealed partial class IslandWindow : Window
{
    public enum Shape { Hidden, Compact, Open }
    public enum Page { Home, Chat, Analysis, Permission, Question, Diff, Week, Choice }

    /// <summary>Cómo respondió la persona un pedido de permiso o una pregunta de Claude Code.</summary>
    public enum ApprovalChoice { Allow, Deny, Always, Answered }

    public sealed record ApprovalAnswer(ApprovalChoice Choice, IReadOnlyDictionary<string, IReadOnlyList<string>>? Picks = null);

    // ── Colores (siempre oscura) ────────────────────────────────────────────────────────────
    static readonly SD.Color Ground = Hex(0x141413), Card = Hex(0x1F1E1D), Raised = Hex(0x262624), Tab = Hex(0x2E2E2C),
        Line = Hex(0x33322F), Edge = Hex(0x3D3D3A), Ink = Hex(0xF5F4EF), Soft = Hex(0xD6D4CC), Dim = Hex(0xA8A69E),
        Faint = Hex(0x8C8A83), Good = Hex(0x8FA46C), Warn = Hex(0xE0A84B), Bad = Hex(0xE0695C);

    // Los mismos tamaños que en Windows (9,75, 8,75, 7,75 y 8,5 puntos).
    static readonly UiFont Body = Gfx.Body, BodyStrong = Gfx.BodyStrong, Small = Gfx.Small, Tiny = Gfx.Tiny;
    static readonly UiFont SmallStrong = new(Gfx.BodyStrong.Typeface, Gfx.Small.Size);
    static readonly UiFont Mono = new(new Typeface("Cascadia Mono, Consolas, Menlo, SF Mono, DejaVu Sans Mono, Liberation Mono, monospace"), 11.3);
    static readonly UiFont Spinner = new(Gfx.Spinner.Typeface, 14);

    // Medidas (en unidades de la interfaz, como a 96 ppp en Windows).
    const double CompactW = 300, CompactH = 36, MouthW = 336, MouthH = 44, OpenW = 560, ShadowMargin = 14, HotWidth = 320, HotHeight = 6;
    const int OpenDelay = 250, CloseDelay = 700, HideDelay = 1200;

    // ── Eventos ─────────────────────────────────────────────────────────────────────────────
    public event Action<SessionInfo>? SessionClicked;
    public event Action<SessionInfo>? TerminalClicked;
    public event Action? NewSessionClicked, SettingsClicked, NewChatClicked;
    /// <summary>Se eligió otro modelo entre los del chip del chat ("" es el de sus sesiones).</summary>
    public event Action<string>? ModelPicked;
    public event Action<string>? Asked;
    public event Action<string[]>? FilesDropped;
    /// <summary>«Abrir en VS Code» desde el diff de un archivo.</summary>
    public event Action<string>? OpenFileClicked;
    public event Action? CancelClicked;
    /// <summary>Una acción sobre el resultado de un análisis: ask, copy o session.</summary>
    public event Action<string>? ResultAction;
    /// <summary>Clic en el micrófono del campo: empezar a escuchar o terminar y enviar.</summary>
    public event Action? MicClicked;

    // ── Estado ──────────────────────────────────────────────────────────────────────────────
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(50) };
    readonly DispatcherTimer _frame = new() { Interval = TimeSpan.FromMilliseconds(16) };
    readonly IslandView _view;
    readonly TextBox _box;
    readonly List<(Rect Bounds, string Key, object? Tag)> _hits = new();
    readonly Flows.Arrivals _arrivals = new();
    SD.Color _accent = Hex(0xD97757);
    Shape _shape = Shape.Hidden;
    Page _page = Page.Home;
    Screen? _screen;

    // Tamaño de la isla, animado: de (from) a (to) en _animMs desde _animAt. Y: 0 a la vista; negativo, escondida.
    double _w = CompactW, _h = CompactH, _y = -CompactH - ShadowMargin;
    double _fromW, _fromH, _fromY, _toW = CompactW, _toH = CompactH, _toY = -CompactH - ShadowMargin;
    long _animAt;
    int _animMs = 1;
    // El tamaño de la ventana (con el margen de la sombra): durante una animación, el más grande de los dos.
    double _winW, _winH;
    long _pageAt;           // cuándo cambió el contenido (entra apareciendo)
    long _overSince, _outSince;
    bool _muted, _clickOpened, _wasDown, _pointerInside;
    /// <summary>Se plegó con un clic fuera mientras respondía: al llegar la respuesta, se abre para mostrarla.</summary>
    bool _reopenForAnswer;
    long _swallowAt = -10_000;
    /// <summary>La mascota viene con un archivo (la isla espera asomada) y la boca abierta para recibir la caja.</summary>
    bool _receiving, _mouth;
    string? _hoverKey;
    bool _focusPending;

    // Datos
    IReadOnlyList<SessionInfo> _sessions = [];
    double? _sessionPercent, _weekPercent;
    string _model = L.T("El de sus sesiones");
    string _account = "";
    IReadOnlyList<(string Id, string Label)> _models = [];
    string _modelId = "";
    bool _picking;
    IReadOnlyList<ChatTurn> _turns = [];
    string? _streaming;
    bool _asking;
    long _answerEnd;        // cuándo terminó la última respuesta del chat
    int _answerHold;        // y cuánto se queda abierta para leerla (ms)
    /// <summary>El personaje de la cabeza; null: la clásica.</summary>
    ChibiSkin? _skin = ChibiSprite.Chibi;

    // Análisis de un archivo
    string? _file;
    AssistantStage _stage;
    string _analysis = "";
    string? _analysisError;
    bool _analysisDone, _analysisSeen = true;
    long _analysisAt, _analysisEnd;
    string _analysisModel = "";

    // Voz: escuchando (nivel, lo entendido y cómo terminar), pasando a texto o un aviso corto en el campo.
    bool _listening, _converting;
    double _level;
    string _heard = "", _voiceHint = "";
    string? _voiceNote;
    long _voiceNoteAt;

    public IslandWindow()
    {
        Title = "Claupet";
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        RequestedThemeVariant = ThemeVariant.Dark; // la isla es oscura en los dos modos
        DragDrop.SetAllowDrop(this, true);

        _view = new IslandView(this);
        _box = new TextBox
        {
            FontFamily = Body.Typeface.FontFamily,
            FontSize = Body.Size,
            Foreground = Gfx.B(Ink),
            Background = Gfx.B(Card),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MinHeight = 0,
            IsVisible = false,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        // Sin los colores del tema al pasar el mouse o con el foco: es parte del campo dibujado.
        foreach (var state in new[] { "", "PointerOver", "Focused", "Disabled" })
        {
            _box.Resources["TextControlBackground" + state] = Gfx.B(Card);
            _box.Resources["TextControlBorderBrush" + state] = Brushes.Transparent;
            _box.Resources["TextControlForeground" + state] = Gfx.B(Ink);
            _box.Resources["TextControlPlaceholderForeground" + state] = Gfx.B(Faint);
        }
        _box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        _box.Resources["TextControlThemeMinHeight"] = 0.0;
        _box.AddHandler(KeyDownEvent, OnBoxKey, RoutingStrategies.Tunnel);
        _box.GotFocus += (_, _) => Redraw();
        _box.LostFocus += (_, _) => Redraw();

        var canvas = new Canvas();
        canvas.Children.Add(_view);
        canvas.Children.Add(_box);
        Content = canvas;

        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        // Sin GlobalPointer (Wayland), perder el foco hace lo de un clic fuera.
        Deactivated += (_, _) =>
        {
            if (!_pointerKnown && _shape == Shape.Open && !_pointerInside) OnOutsideClick(null);
        };

        _poll.Tick += (_, _) => Poll();
        _frame.Tick += (_, _) => Render();
        _poll.Start();
    }

    /// <summary>Se sabe dónde está el cursor fuera de la isla (GlobalPointer respondió alguna vez).</summary>
    bool _pointerKnown;

    Screen? Primary => Placement.PrimaryScreen(this);

    Screen? Here => _screen ??= Primary;

    /// <summary>La pantalla donde está el cursor (o la principal).</summary>
    Screen? ScreenAtCursor() =>
        GlobalPointer.Read(Primary) is { } p ? Screens.ScreenFromPoint(p.At) ?? Primary : Primary;

    // ── Lo que manda TrayController ─────────────────────────────────────────────────────────

    public void SetAccent(SD.Color accent)
    {
        _accent = accent;
        Redraw();
    }

    public void SetMascot(ChibiSkin? skin)
    {
        _skin = skin;
        Redraw();
    }

    public void SetSessions(IReadOnlyList<SessionInfo> sessions)
    {
        _sessions = sessions;
        _arrivals.Update(sessions);
        Redraw(relayout: _shape == Shape.Open && _page == Page.Home);
    }

    public void SetUsage(double? session, double? week)
    {
        _sessionPercent = session;
        _weekPercent = week;
        Redraw();
    }

    /// <summary>
    /// El modelo de la isla, ya con nombre («Opus 5.5 · el de sus sesiones»), la cuenta que responde y los que se pueden
    /// elegir en el chip (con el Id del elegido; "" es el de sus sesiones).
    /// </summary>
    public void SetModel(string model, string account, IReadOnlyList<(string Id, string Label)> choices, string selectedId)
    {
        _model = model;
        _account = account;
        _models = choices;
        _modelId = selectedId;
        Redraw(relayout: _picking);
    }

    /// <summary>La conversación (compartida con la mascota y la Console) y, si está llegando, la respuesta en curso.</summary>
    public void SetChat(IReadOnlyList<ChatTurn> turns, string? streaming, bool asking)
    {
        var now = Environment.TickCount64;
        // La conversación guarda las últimas 40: si se cayeron las primeras, los índices se corren otro tanto.
        var shift = 0;
        if (turns.Count > 0 && _turns.Count > 0 && !ReferenceEquals(turns[0], _turns[0]))
            for (var i = 1; i < _turns.Count; i++)
                if (ReferenceEquals(turns[0], _turns[i])) { shift = i; break; }
        if (shift > 0)
        {
            var moved = _turnAt.Where(p => p.Key >= shift).ToList();
            _turnAt.Clear();
            foreach (var (index, at) in moved) _turnAt[index - shift] = at;
            _typedIndex -= shift;
            _turns = _turns.Skip(shift).ToList();
        }
        if (turns.Count < _turns.Count || turns.Count == 0) // conversación nueva
        {
            _turnAt.Clear();
            _typedIndex = -1;
            _chatScroll = _chatContent = 0;
        }
        for (var i = _turns.Count; i < turns.Count; i++)
        {
            if (turns[i].FromPerson) _chatScroll = 0; // lo que se acaba de enviar, a la vista
            // La respuesta que se venía escribiendo pasa a la conversación sin volver a entrar: sigue escribiéndose.
            if (!turns[i].FromPerson && _streaming is not null && streaming is null)
            {
                _typedIndex = i;
                continue;
            }
            _turnAt[i] = now;
            if (!turns[i].FromPerson) { _typedIndex = i; _typedShown = 0; } // una respuesta sin partes (un error): igual se escribe
        }
        if (streaming is not null && _streaming is null) { _typedShown = 0; _thinkingAt = now; } // empieza otra respuesta
        var answered = _asking && !asking && turns.Count > 0 && !turns[^1].FromPerson;
        if (answered)
        {
            // Terminó de responder: se queda a la vista lo que se tarda en leerla, aunque el cursor no esté encima.
            _answerEnd = now;
            _answerHold = Math.Clamp(3000 + 50 * turns[^1].Text.Length, 5000, 15000);
        }
        _turns = turns;
        _streaming = streaming;
        _asking = asking;
        if (!asking && _reopenForAnswer)
        {
            _reopenForAnswer = false;
            if (answered && _shape != Shape.Open && _choiceTask is null) ShowPage(Page.Chat);
        }
        Redraw(relayout: _shape == Shape.Open && _page == Page.Chat);
    }

    // Animación del chat: cuándo entró cada mensaje (índice → TickCount64) y cuántas letras se ven de la
    // respuesta que se está escribiendo (la que llega por partes o, al terminar, la última).
    readonly Dictionary<int, long> _turnAt = new();
    int _typedIndex = -1;
    int _typedShown;
    long _thinkingAt;

    /// <summary>La respuesta que se está escribiendo todavía no se ve entera.</summary>
    bool Typing => _streaming is { Length: > 0 } s ? _typedShown < s.Length
        : _typedIndex >= 0 && _typedIndex < _turns.Count && _typedShown < _turns[_typedIndex].Text.Length;

    /// <summary>Avanza la escritura: unas 70 letras por segundo y, si se atrasa, más rápido.</summary>
    void AdvanceTyping()
    {
        var target = _streaming is { Length: > 0 } s ? s.Length
            : _typedIndex >= 0 && _typedIndex < _turns.Count ? _turns[_typedIndex].Text.Length : 0;
        if (_typedShown >= target) return;
        var behind = target - _typedShown;
        _typedShown = Math.Min(target, _typedShown + Math.Max(1, behind / 14));
        if (_shape == Shape.Open && _page == Page.Chat) Relayout(); // el globo crece con el texto
    }

    // ── Voz ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>El campo del chat lleva el botón del micrófono (Ajustes › Asistente › Micrófono).</summary>
    public bool MicEnabled
    {
        get => _micEnabled;
        set { _micEnabled = value; Redraw(); }
    }
    bool _micEnabled;

    public bool IsOpen => _shape == Shape.Open;

    /// <summary>Le están hablando: se abre en el chat, con el campo en rojo, el nivel y cómo se termina (hint).</summary>
    public void Listen(string hint, Screen? screen = null)
    {
        _listening = true;
        _converting = false;
        _heard = "";
        _voiceHint = hint;
        _level = 0;
        _voiceNote = null;
        ShowPage(Page.Chat, screen ?? (IsOpen ? null : ScreenAtCursor()));
    }

    public void SetVoiceLevel(float level) => _level = Math.Max(_level, Math.Min(1, level * 2.2));

    public void SetHeard(string text)
    {
        _heard = text;
        Redraw();
    }

    /// <summary>Soltó la tecla: el campo muestra lo entendido, con brillo, mientras se pasa todo a texto.</summary>
    public void ConvertingVoice()
    {
        _listening = false;
        _converting = true;
        Redraw();
    }

    /// <summary>Terminó de escuchar; note: un aviso corto en el campo (no se entendió, falta el modelo…).</summary>
    public void StopListening(string? note = null)
    {
        _listening = _converting = false;
        _voiceNote = note;
        _voiceNoteAt = Environment.TickCount64;
        Redraw();
    }

    bool VoiceNoteShown => _voiceNote is not null && Environment.TickCount64 - _voiceNoteAt < 4000;

    /// <summary>Mientras se le habla, o se pasa a texto, o hay un aviso, el campo lo dibuja la isla (sin el TextBox).</summary>
    bool VoiceInField => _listening || _converting || VoiceNoteShown;

    // ── Análisis de un archivo ──────────────────────────────────────────────────────────────

    /// <summary>Llegó un archivo: la isla rebota y se abre en «Analizando».</summary>
    public void Swallow() => _swallowAt = Environment.TickCount64;

    /// <summary>La mascota viene caminando con un archivo: la isla asoma y espera.</summary>
    public void Receive(Screen? screen)
    {
        if (screen is not null) _screen = screen;
        _receiving = true;
        if (_shape == Shape.Hidden) ShowShape(Shape.Compact);
        Redraw();
    }

    /// <summary>Abre la boca para recibir la caja (más grande y redonda).</summary>
    public void OpenMouth()
    {
        _mouth = true;
        if (_shape == Shape.Compact) Animate(MouthW, MouthH, 0, 200);
    }

    /// <summary>Dónde está la boca, en pixeles de la pantalla: hacia ahí vuela la caja.</summary>
    public PixelPoint Mouth
    {
        get
        {
            if (Here is not { } screen) return default;
            var area = screen.WorkingArea;
            return new PixelPoint(area.X + area.Width / 2, area.Y + (int)Math.Round(((_mouth ? MouthH : CompactH) - 4) * screen.Scaling));
        }
    }

    /// <summary>Dónde queda la isla compacta (con la boca abierta) en esa pantalla, en pixeles: para planear el lanzamiento.</summary>
    public static PixelRect MouthArea(Screen screen)
    {
        var area = screen.WorkingArea;
        var width = (int)Math.Round(MouthW * screen.Scaling);
        return new PixelRect(area.X + (area.Width - width) / 2, area.Y, width, (int)Math.Round(MouthH * screen.Scaling));
    }

    public void BeginAnalysis(string file)
    {
        _file = file;
        _stage = AssistantStage.Opening;
        _analysis = "";
        _analysisError = null;
        _analysisDone = false;
        _analysisSeen = false;
        _receiving = false;
        _mouth = false;
        _analysisAt = Environment.TickCount64;
        _analysisModel = _model;
        ShowPage(Page.Analysis);
    }

    public void SetAnalysisStage(AssistantStage stage)
    {
        _stage = stage;
        Redraw();
    }

    public void AppendAnalysis(string piece)
    {
        _analysis += piece;
        Redraw(relayout: _page == Page.Analysis);
    }

    public void FinishAnalysis(string text, string? error, string? model)
    {
        _analysis = text;
        _analysisError = error;
        _analysisDone = true;
        _analysisEnd = Environment.TickCount64;
        if (model is { Length: > 0 }) _analysisModel = model;
        Redraw(relayout: true);
    }

    public bool Analyzing => _file is not null && !_analysisDone;

    // ── Permisos y preguntas ────────────────────────────────────────────────────────────────

    ApprovalRequest? _approval;
    string _approvalTitle = "";
    TaskCompletionSource<ApprovalAnswer?>? _approvalTask;
    int _questionIndex;
    readonly Dictionary<string, List<string>> _picks = new();

    /// <summary>Hay un permiso o una pregunta esperando respuesta.</summary>
    public bool Pending => _approvalTask is not null;

    /// <summary>
    /// Muestra la tarjeta del pedido (sube sola, aunque la isla esté oculta) y espera la respuesta.
    /// Uno a la vez: si ya hay otro esperando, este va a la terminal (null).
    /// </summary>
    public Task<ApprovalAnswer?> AskApproval(ApprovalRequest request, string title)
    {
        if (_approvalTask is not null) return Task.FromResult<ApprovalAnswer?>(null);
        _approval = request;
        _approvalTitle = title;
        _questionIndex = 0;
        _picks.Clear();
        _approvalTask = new TaskCompletionSource<ApprovalAnswer?>();
        ShowPage(request.IsQuestion ? Page.Question : Page.Permission, ScreenAtCursor());
        return _approvalTask.Task;
    }

    /// <summary>Se venció el tiempo (o lo respondió en la terminal): la tarjeta se va sin respuesta.</summary>
    public void DropApproval() => ResolveApproval(null);

    void ResolveApproval(ApprovalAnswer? answer)
    {
        var task = _approvalTask;
        _approvalTask = null;
        _approval = null;
        if (_page is Page.Permission or Page.Question)
        {
            _clickOpened = false;
            ShowPage(Page.Home);
            _clickOpened = false;
        }
        task?.TrySetResult(answer);
    }

    /// <summary>La sesión del pedido, si está entre las que se leen (para su título y su terminal).</summary>
    SessionInfo? ApprovalSession => _approval is { } a ? _sessions.FirstOrDefault(s => s.SessionId == a.SessionId) : null;

    // ── Preguntas de Claupet (el permiso de una capacidad, una confirmación) ────────────────

    TaskCompletionSource<string?>? _choiceTask;
    string _choiceTitle = "", _choiceDetail = "", _choicePrimary = "";
    IReadOnlyList<(string Id, string Label)> _choiceOptions = [];
    Page _choiceReturn = Page.Chat;

    /// <summary>
    /// Pregunta algo de Claupet (¿dejo que capture la pantalla?) con una tarjeta y sus botones; primary es el
    /// Id del botón principal. Como un permiso de Claude Code, salir de la tarjeta (otra pestaña, un clic
    /// fuera, plegarla) no la responde: la compacta avisa y la vuelve a abrir.
    /// Devuelve el Id elegido, o null si se descartó (CancelChoice: se hizo otra pregunta).
    /// </summary>
    public Task<string?> Choose(string title, string detail, IReadOnlyList<(string Id, string Label)> options, string primary)
    {
        _choiceTask?.TrySetResult(null);
        if (_page != Page.Choice) _choiceReturn = _page is Page.Chat or Page.Home ? _page : Page.Chat;
        (_choiceTitle, _choiceDetail, _choiceOptions, _choicePrimary) = (title, detail, options, primary);
        var task = _choiceTask = new TaskCompletionSource<string?>();
        ShowPage(Page.Choice, _shape == Shape.Open ? null : ScreenAtCursor());
        return task.Task;
    }

    /// <summary>Descarta esa pregunta, si todavía espera (el pedido que la hizo ya no sigue): responde null.</summary>
    public void CancelChoice(Task<string?> which)
    {
        if (_choiceTask?.Task == which) ResolveChoice(null, reopen: _shape == Shape.Open);
    }

    /// <summary>Responde la pregunta y vuelve a la página de antes (reopen: false, con la isla plegada).</summary>
    void ResolveChoice(string? id, bool reopen = true)
    {
        var task = _choiceTask;
        _choiceTask = null;
        if (_page == Page.Choice)
        {
            if (reopen) ShowPage(_choiceReturn);
            else _page = _choiceReturn;
        }
        task?.TrySetResult(id);
    }

    /// <summary>Abre la isla en una página (el chat, por ejemplo, desde «Ver en la isla»).</summary>
    public void ShowPage(Page page, Screen? screen = null)
    {
        if (screen is not null) _screen = screen;
        if (page == Page.Analysis) _analysisSeen = true;
        if (page != Page.Chat) HideInput(); // y si iba a tomar el foco, ya no: si no, la isla no se plegaría
        var changed = page != _page || _shape != Shape.Open;
        _page = page;
        _clickOpened = true;
        ShowShape(Shape.Open);
        if (changed) _pageAt = Environment.TickCount64;
        Relayout();
    }

    // ── Atajos ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Abre el chat con el campo listo para escribir.</summary>
    public void FocusChat()
    {
        ShowPage(Page.Chat, ScreenAtCursor());
        FocusText();
    }

    /// <summary>Va al permiso o a la pregunta que espera (de Claude Code o de Claupet), si hay.</summary>
    public void ShowPending()
    {
        if (_approval is { } a) ShowPage(a.IsQuestion ? Page.Question : Page.Permission, ScreenAtCursor());
        else if (_choiceTask is not null) ShowPage(Page.Choice, ScreenAtCursor());
    }

    /// <summary>Abre la isla (en la página de antes) o la pliega.</summary>
    public void ToggleOpen()
    {
        if (_shape == Shape.Open) Collapse(force: true);
        else ShowPage(_page, ScreenAtCursor());
    }

    public void ToggleMute()
    {
        _muted = !_muted;
        Redraw();
    }

    /// <summary>Silenciada desde su botón (los avisos no suenan).</summary>
    public bool Muted => _muted;

    // ── Resumen de la semana y diff ─────────────────────────────────────────────────────────

    WeekRecap? _week;
    (SessionInfo Session, FileEdit Edit)? _diff;

    public void ShowWeek(WeekRecap recap)
    {
        _week = recap;
        ShowPage(Page.Week, ScreenAtCursor());
    }

    // ── Forma y animación ───────────────────────────────────────────────────────────────────

    void ShowShape(Shape shape)
    {
        if (shape == Shape.Open && _shape != Shape.Open) _pageAt = Environment.TickCount64;
        _shape = shape;
        if (shape != Shape.Open) _picking = false;
        switch (shape)
        {
            case Shape.Hidden:
                HideInput();
                Animate(_w, _h, -_h - ShadowMargin, 180);
                break;
            case Shape.Compact:
                HideInput();
                Animate(_mouth ? MouthW : CompactW, _mouth ? MouthH : CompactH, 0, _y < -1 ? 180 : 240);
                break;
            case Shape.Open:
                Animate(OpenW, MeasureOpen(), 0, 240);
                break;
        }
        if (!IsVisible && shape != Shape.Hidden)
        {
            Place();
            Show();
        }
    }

    void Animate(double w, double h, double y, int ms)
    {
        (_fromW, _fromH, _fromY) = (_w, _h, _y);
        (_toW, _toH, _toY) = (w, h, y);
        _animAt = Environment.TickCount64;
        _animMs = Math.Max(1, ms);
        // Mientras cambia, la ventana tiene el tamaño más grande de los dos; al terminar, el final (Render).
        Fit(Math.Max(_w, w), Math.Max(_h, h));
        _frame.Interval = TimeSpan.FromMilliseconds(16);
        _frame.Start();
    }

    double AnimProgress => Math.Clamp((Environment.TickCount64 - _animAt) / (double)_animMs, 0, 1);

    static double Ease(double p) => 1 - Math.Pow(1 - p, 3);

    void Step()
    {
        var k = Ease(AnimProgress);
        _w = _fromW + (_toW - _fromW) * k;
        _h = _fromH + (_toH - _fromH) * k;
        _y = _fromY + (_toY - _fromY) * k;
        // El rebote al tragar un archivo: crece un poco y vuelve.
        var swallow = (Environment.TickCount64 - _swallowAt) / 240.0;
        if (swallow is >= 0 and < 1)
        {
            var bump = Math.Sin(Math.PI * swallow);
            _w += 12 * bump;
            _h += 9 * bump;
        }
    }

    /// <summary>La ventana mide la isla más el margen de la sombra, y va centrada en el borde de arriba.</summary>
    void Fit(double w, double h)
    {
        var winW = Math.Ceiling(w + 12) + 2 * ShadowMargin; // + el rebote
        var winH = Math.Ceiling(h + 9) + ShadowMargin;
        if (Math.Abs(winW - _winW) < 0.5 && Math.Abs(winH - _winH) < 0.5) return;
        (_winW, _winH) = (winW, winH);
        Width = winW;
        Height = winH;
        _view.Width = winW;
        _view.Height = winH;
        Place();
    }

    void Place()
    {
        if (Here is not { } screen) return;
        var area = screen.WorkingArea;
        Position = new PixelPoint(area.X + (int)Math.Round((area.Width - _winW * screen.Scaling) / 2), area.Y);
    }

    /// <summary>Cambió el contenido de la página abierta: el alto se acomoda con suavidad.</summary>
    void Relayout()
    {
        if (_shape != Shape.Open) { Redraw(); return; }
        var height = MeasureOpen();
        if (Math.Abs(height - _toH) > 0.5) Animate(OpenW, height, 0, 200);
        else Redraw();
    }

    void Redraw(bool relayout = false)
    {
        if (relayout) { Relayout(); return; }
        if (!IsVisible) return;
        if (!_frame.IsEnabled)
        {
            _frame.Interval = TimeSpan.FromMilliseconds(16);
            _frame.Start();
        }
    }

    /// <summary>¿Hay algo que se mueva en la isla (además del tamaño)? Si no, deja de dibujar.</summary>
    bool Animating =>
        AnimProgress < 1 || Environment.TickCount64 - _swallowAt < 260 || Environment.TickCount64 - _pageAt < 220
        || _shape == Shape.Open && (_sessions.Any(s => s.State is SessionState.Working or SessionState.Waiting) || _asking || Analyzing || Typing
            || _turnAt.Values.Any(t => Environment.TickCount64 - t < 260) || VoiceInField || _focusPending)
        || _shape == Shape.Compact; // en compacta, la cabeza parpadea

    void Render()
    {
        Step();
        AdvanceTyping();
        _level *= 0.93; // el nivel de la voz sube enseguida y baja de a poco
        if (_shape == Shape.Hidden && AnimProgress >= 1)
        {
            _frame.Stop();
            if (IsVisible) Hide();
            return;
        }
        if (AnimProgress >= 1 && Environment.TickCount64 - _swallowAt > 260) Fit(_w, _h);
        _view.InvalidateVisual();
        PlaceInput();

        if (!Animating) _frame.Stop();
        else _frame.Interval = TimeSpan.FromMilliseconds(AnimProgress < 1 || Environment.TickCount64 - _pageAt < 220 || Environment.TickCount64 - _swallowAt < 260
            ? 16 : _listening ? 33 : 50);
    }

    (double Bar, double Body) ContentAlpha(bool open)
    {
        var k = AnimProgress;
        var sizing = Math.Abs(_toW - _fromW) > 1 || Math.Abs(_toH - _fromH) > 30;
        var size = sizing ? Math.Clamp((k - 0.45) / 0.55, 0, 1) : 1;
        var page = Math.Clamp((Environment.TickCount64 - _pageAt) / 180.0, 0, 1);
        return (size, open ? Math.Min(size, page) : size);
    }

    // ── Cursor: asomar, abrir, cerrar ───────────────────────────────────────────────────────

    /// <summary>Lo que debería verse sin el cursor: compacta si algo trabaja (y está pedido), si analiza o espera; si no, nada.</summary>
    Shape Resting
    {
        get
        {
            if (Analyzing || _receiving || Pending || _choiceTask is not null || _asking || (_file is not null && !_analysisSeen)) return Shape.Compact;
            if (WhileBusy && _sessions.Any(s => s.State is SessionState.Working or SessionState.Waiting)) return Shape.Compact;
            return Shape.Hidden;
        }
    }

    /// <summary>La isla queda compacta mientras algo trabaja (Ajustes › Isla).</summary>
    public bool WhileBusy { get; set; } = true;

    /// <summary>Se abre con el cursor encima (si no, con un clic).</summary>
    public bool OpenOnHover { get; set; } = true;

    /// <summary>Dónde está la isla en la pantalla, en pixeles.</summary>
    PixelRect IslandScreenBounds()
    {
        if (Here is not { } screen) return default;
        var area = screen.WorkingArea;
        var s = screen.Scaling;
        var w = (int)(_w * s);
        return new PixelRect(area.X + (area.Width - w) / 2, area.Y + (int)(_y * s), w, (int)(_h * s));
    }

    static bool Near(PixelRect r, PixelPoint p, int pad) =>
        p.X >= r.X - pad && p.X < r.Right + pad && p.Y >= r.Y - pad && p.Y < r.Bottom + pad;

    void Poll()
    {
        var now = Environment.TickCount64;
        var hot = false;
        var over = IsVisible && _pointerInside;
        if (GlobalPointer.Read(Primary) is { } pointer)
        {
            _pointerKnown = true;
            // La franja sensible: en el centro del borde de arriba de cualquier pantalla.
            if (Screens.ScreenFromPoint(pointer.At) is { } under)
            {
                var area = under.WorkingArea;
                hot = pointer.At.Y <= area.Y + HotHeight * under.Scaling
                    && Math.Abs(pointer.At.X - (area.X + area.Width / 2.0)) <= HotWidth * under.Scaling / 2;
                if (hot && _shape == Shape.Hidden) _screen = under;
            }
            var pad = (int)(8 * (Here?.Scaling ?? 1));
            over = IsVisible && _y > -2 && Near(IslandScreenBounds(), pointer.At, pad);
            // Un botón que se acaba de apretar fuera de la isla abierta: se pliega (OnOutsideClick decide).
            if (pointer.Down && !_wasDown && _shape == Shape.Open && IsVisible && !Near(IslandScreenBounds(), pointer.At, pad / 2))
                OnOutsideClick(pointer.At);
            _wasDown = pointer.Down;
        }

        if (hot && _shape == Shape.Hidden) ShowShape(Shape.Compact);
        else if (_shape == Shape.Hidden && Resting == Shape.Compact) ShowShape(Shape.Compact); // algo trabaja: queda a la vista

        if (over)
        {
            _outSince = 0;
            if (_overSince == 0) _overSince = now;
            if (_shape == Shape.Compact && OpenOnHover && now - _overSince >= OpenDelay && !_mouth)
            {
                _clickOpened = false;
                ShowShape(Shape.Open);
                Relayout();
            }
        }
        else
        {
            _overSince = 0;
            if (_outSince == 0) _outSince = now;
            var away = now - _outSince;
            if (_shape == Shape.Open && away >= CloseDelay && CanFold && !(_clickOpened && away < CloseDelay * 3))
                Collapse(force: false);
            else if (_shape == Shape.Compact && Resting == Shape.Hidden && !_mouth && away >= HideDelay && !hot)
                ShowShape(Shape.Hidden);
        }

        if (_hoverKey is not null && !over) { _hoverKey = null; Redraw(); }
    }

    /// <summary>Se puede plegar: nada la retiene abierta (escribiendo, analizando o con el resultado recién llegado).</summary>
    bool CanFold =>
        !InputFocused && (_box.Text ?? "").Length == 0
        && !Analyzing && !(_page == Page.Analysis && Environment.TickCount64 - _analysisEnd < 5000) && !_asking
        && !(_page == Page.Chat && (Typing || Environment.TickCount64 - _answerEnd < _answerHold))
        && !_listening && !_converting
        && !(Pending && _page is Page.Permission or Page.Question) // una tarjeta no se pliega sola: se responde o se deja con ⌃
        && !(_choiceTask is not null && _page == Page.Choice)
        && !(_page == Page.Week && Environment.TickCount64 - _pageAt < 15000); // el resumen de la semana se queda un rato

    void Collapse(bool force)
    {
        if (!force && !CanFold) return;
        _clickOpened = false;
        ShowShape(Resting == Shape.Hidden && !force ? Shape.Hidden : Shape.Compact);
        if (Resting == Shape.Hidden) _outSince = Environment.TickCount64; // ya compacta, se esconde después
    }

    /// <summary>
    /// Un clic en otra parte con la isla abierta: se pliega a lo que corresponda sin el cursor. Un permiso de Claude
    /// Code o una pregunta de Claupet siguen esperando en la compacta; si estaba respondiendo, se abre de nuevo al
    /// llegar la respuesta.
    /// </summary>
    void OnOutsideClick(PixelPoint? at)
    {
        if (_shape != Shape.Open || !IsVisible) return;
        if (_page is Page.Permission or Page.Question or Page.Choice) _page = Page.Home; // la compacta la vuelve a abrir
        _reopenForAnswer = _asking;
        _clickOpened = false;
        ShowShape(Resting);
    }

    // ── El campo de texto ───────────────────────────────────────────────────────────────────

    Rect _inputBounds;

    bool InputFocused => _focusPending || _box.IsFocused && IsActive;

    /// <summary>El campo va encima del dibujo, solo con el chat abierto y quieto.</summary>
    void PlaceInput()
    {
        var show = _shape == Shape.Open && _page == Page.Chat && AnimProgress >= 1 && _inputBounds.Width > 0 && !VoiceInField;
        if (!show)
        {
            if (_box.IsVisible) _box.IsVisible = false;
            return;
        }
        Canvas.SetLeft(_box, _inputBounds.X);
        Canvas.SetTop(_box, _inputBounds.Y);
        _box.Width = _inputBounds.Width;
        _box.Height = _inputBounds.Height;
        var cue = _asking ? L.T("Respondiendo… (clic en el botón para cancelar)") : L.T("Pregunte algo o suelte un archivo aquí…");
        if (_box.Watermark != cue) _box.Watermark = cue;
        _box.IsVisible = true;
        if (_focusPending)
        {
            _focusPending = false;
            Activate();
            _box.Focus();
        }
    }

    /// <summary>Toma el foco para escribir; si todavía no está a la vista (la isla se está abriendo), apenas aparezca.</summary>
    void FocusText()
    {
        if (!_box.IsVisible) { _focusPending = true; Redraw(); return; }
        Activate();
        _box.Focus();
    }

    void HideInput()
    {
        _focusPending = false;
        _box.IsVisible = false;
    }

    void SendInput()
    {
        var text = (_box.Text ?? "").Trim();
        if (text.Length == 0) return;
        _box.Text = "";
        Asked?.Invoke(text);
    }

    void OnBoxKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SendInput();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_picking)
            {
                _picking = false;
                Relayout();
            }
            else Collapse(force: true);
        }
    }

    // ── Mouse y archivos ────────────────────────────────────────────────────────────────────

    // La conversación desplazable: cuánto se subió desde el final (0: lo último a la vista), cuánto mide,
    // el marco a la vista, la barra y, si se arrastra, desde dónde.
    double _chatScroll, _chatContent, _chatMaxScroll;
    Rect _chatView, _chatTrack, _chatThumb;
    (double Y, double Scroll)? _chatDrag;

    /// <summary>Sube o baja la conversación (desde el final), sin pasarse.</summary>
    void ScrollChat(double scroll)
    {
        scroll = Math.Clamp(scroll, 0, _chatMaxScroll);
        if (Math.Abs(scroll - _chatScroll) < 0.5) return;
        _chatScroll = scroll;
        Redraw();
    }

    (Rect Bounds, string Key, object? Tag)? HitAt(Point p)
    {
        for (var i = _hits.Count - 1; i >= 0; i--)
            if (_hits[i].Bounds.Contains(p)) return _hits[i];
        return null;
    }

    void Hit(Rect bounds, string key, object? tag = null) => _hits.Add((bounds, key, tag));

    void ViewMoved(Point p)
    {
        if (_chatDrag is { } drag)
        {
            // Arrastrar la barra: bajarla es ir hacia lo último.
            var room = Math.Max(1, _chatTrack.Height - _chatThumb.Height);
            ScrollChat(drag.Scroll - (p.Y - drag.Y) * _chatMaxScroll / room);
            return;
        }
        var hit = HitAt(p);
        _view.Cursor = new Cursor(hit is null ? StandardCursorType.Arrow : StandardCursorType.Hand);
        if (hit?.Key == _hoverKey) return;
        _hoverKey = hit?.Key;
        Redraw();
    }

    void ViewPressed(Point p)
    {
        if (HitAt(p) is not { } hit) return;
        if (hit.Key == "chat:thumb") _chatDrag = (p.Y, _chatScroll);
        else if (hit.Key == "chat:track") ScrollChat(_chatScroll + (p.Y < _chatThumb.Y ? 1 : -1) * _chatView.Height * 4 / 5); // de a una página
    }

    void ViewWheel(Point p, double delta)
    {
        if (_shape == Shape.Open && _page == Page.Chat && _chatMaxScroll > 0 && _chatView.Contains(p))
            ScrollChat(_chatScroll + delta * 48);
    }

    void ViewReleased(Point p, bool left)
    {
        if (_chatDrag is not null)
        {
            _chatDrag = null;
            Redraw();
            return;
        }
        if (!left || HitAt(p) is not { } hit) return;
        switch (hit.Key)
        {
            case "compact":
                ShowPage(_approval is { } a ? (a.IsQuestion ? Page.Question : Page.Permission)
                    : _choiceTask is not null ? Page.Choice
                    : _file is not null && !_analysisSeen ? Page.Analysis : _page);
                break;
            case "ap:allow": ResolveApproval(new ApprovalAnswer(ApprovalChoice.Allow)); break;
            case "ap:deny": ResolveApproval(new ApprovalAnswer(ApprovalChoice.Deny)); break;
            case "ap:always": ResolveApproval(new ApprovalAnswer(ApprovalChoice.Always)); break;
            case "ap:terminal": ResolveApproval(null); break;
            case "ap:fold":
                _clickOpened = false;
                ShowPage(Page.Home);
                Collapse(force: true);
                break;
            case "ap:term":
                if (ApprovalSession is { } session) TerminalClicked?.Invoke(session);
                break;
            case "q:next": NextQuestion(); break;
            case "diff:back": ShowPage(Page.Home); break;
            case "week:copy":
                if (_week is { } week && Clipboard is { } clipboard) _ = clipboard.SetTextAsync(week.Text);
                break;
            case "week:close": ShowPage(Page.Home); break;
            case "diff:open":
                if (_diff is { } open) OpenFileClicked?.Invoke(open.Edit.File);
                break;
            case "tab:home": ShowPage(Page.Home); break;
            case "tab:chat": ShowPage(Page.Chat); FocusText(); break;
            case "tab:new": NewSessionClicked?.Invoke(); break;
            case "gear": SettingsClicked?.Invoke(); break;
            case "mute": ToggleMute(); break;
            case "model":
                _picking = !_picking;
                Relayout();
                break;
            case "mic": MicClicked?.Invoke(); break;
            case "newchat": NewChatClicked?.Invoke(); break;
            case "send":
                if (_asking) CancelClicked?.Invoke();
                else SendInput();
                break;
            case "cancel": CancelClicked?.Invoke(); break;
            case "res:ask": ResultAction?.Invoke("ask"); break;
            case "res:copy": ResultAction?.Invoke("copy"); break;
            case "res:session": ResultAction?.Invoke("session"); break;
            default:
                if (hit.Key.StartsWith("q:opt:", StringComparison.Ordinal) && hit.Tag is string label) PickOption(label);
                else if (hit.Key.StartsWith("ch:", StringComparison.Ordinal) && hit.Tag is string choice) ResolveChoice(choice);
                else if (hit.Key.StartsWith("mdl:", StringComparison.Ordinal) && hit.Tag is string model)
                {
                    _picking = false;
                    if (model != _modelId)
                    {
                        _modelId = model;
                        ModelPicked?.Invoke(model);
                    }
                    Relayout();
                }
                else if (hit.Key.StartsWith("diff:", StringComparison.Ordinal) && hit.Tag is ValueTuple<SessionInfo, FileEdit> shown)
                {
                    _diff = (shown.Item1, shown.Item2);
                    ShowPage(Page.Diff);
                }
                else if (hit.Key.StartsWith("term:", StringComparison.Ordinal) && hit.Tag is SessionInfo t) TerminalClicked?.Invoke(t);
                else if (hit.Key.StartsWith("sess:", StringComparison.Ordinal) && hit.Tag is SessionInfo s) SessionClicked?.Invoke(s);
                break;
        }
    }

    /// <summary>Elegir una opción: si se puede una sola, responde y pasa a la siguiente pregunta (o termina).</summary>
    void PickOption(string label)
    {
        if (_approval?.Questions is not { } questions || _questionIndex >= questions.Count) return;
        var q = questions[_questionIndex];
        if (!_picks.TryGetValue(q.Question, out var list)) _picks[q.Question] = list = new List<string>();
        if (q.MultiSelect)
        {
            if (!list.Remove(label)) list.Add(label);
            Redraw();
            return;
        }
        list.Clear();
        list.Add(label);
        Redraw();
        NextQuestion();
    }

    void NextQuestion()
    {
        if (_approval?.Questions is not { } questions) return;
        if (_questionIndex < questions.Count - 1)
        {
            _questionIndex++;
            _pageAt = Environment.TickCount64; // la pregunta siguiente entra apareciendo
            Relayout();
            return;
        }
        var picks = _picks.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value.ToList());
        ResolveApproval(new ApprovalAnswer(ApprovalChoice.Answered, picks));
    }

    static string[] FilesOf(DragEventArgs e) =>
#pragma warning disable CS0618 // DataTransfer recién llega en 11.3 y todavía no la usa el resto de la app
        e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
#pragma warning restore CS0618

    void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (FilesOf(e).Length == 0) { e.DragEffects = DragDropEffects.None; return; }
        e.DragEffects = DragDropEffects.Copy;
        if (_shape == Shape.Hidden) ShowShape(Shape.Compact);
    }

    void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = FilesOf(e).Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;

    void OnDrop(object? sender, DragEventArgs e)
    {
        var files = FilesOf(e);
        if (files.Length == 0) return;
        Swallow();
        FilesDropped?.Invoke(files);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape || e.Handled) return;
        if (Analyzing) CancelClicked?.Invoke();
        else Collapse(force: true);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _pointerInside = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointerInside = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        _poll.Stop();
        _frame.Stop();
        _choiceTask?.TrySetResult(null);
        _approvalTask?.TrySetResult(null);
        base.OnClosed(e);
    }

    static SD.Color Hex(int rgb) => SD.Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    sealed class IslandView(IslandWindow owner) : Control
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

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            owner.ViewWheel(e.GetPosition(this), e.Delta.Y);
        }
    }
}
