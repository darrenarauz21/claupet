using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Claupet;

/// <summary>
/// La bienvenida de Claupet, por pasos: el idioma y las novedades, el hook de permisos en cada carpeta de
/// Claude Code, la mascota y la isla, los modelos de voz y las extensiones, y el inicio con Windows y las
/// notificaciones, con lo que viene. Ventana sin marco y redondeada, como Claupet Console. Nada cambia hasta
/// «Aplicar», salvo instalar una extensión, que pide su propia confirmación.
/// </summary>
sealed class SetupForm : Form
{
    sealed record HookRow(string Dir, HookInstaller.State State, string Name);

    /// <summary>
    /// Cómo están los modelos de voz: listos en el equipo, o dados por una extensión (su nombre). La voz de
    /// Claupet hace falta solo si la voz elegida para leer es la suya.
    /// </summary>
    public sealed record ModelState(bool VoiceReady, string? VoiceExtension, bool SpeechReady, bool SpeechNeeded, string? SpeechExtension);

    const int Steps = 5;

    readonly Theme _t;
    readonly Font _small, _strong, _title, _eyebrow;
    readonly List<HookRow> _rows = new();
    readonly HashSet<string> _hooksOn = new(StringComparer.OrdinalIgnoreCase);
    readonly string _startLanguage;
    readonly StepRail _rail;
    readonly Label _step, _heading, _lead;
    readonly FlowLayoutPanel _body;
    readonly PillButton _back, _next, _later, _close;
    int _current;

    public string Language { get; private set; }
    /// <summary>La mascota elegida (Mascots.Classic o Chibi), o null si no se tocó.</summary>
    public string? Mascot { get; private set; }
    public bool DesktopMascot { get; private set; }
    public bool Island { get; private set; }
    public bool Chat { get; private set; }
    public bool StartWithWindows { get; private set; }
    public bool NotificationsOn { get; private set; }
    /// <summary>Hablarle con el micrófono (baja el modelo de voz si falta).</summary>
    public bool Mic { get; private set; }
    /// <summary>Leer las respuestas en voz alta (baja la voz de Claupet si falta).</summary>
    public bool ReadAloud { get; private set; }

    /// <summary>Lo que la app sabe de los modelos de voz; sin esto, el paso los da por faltantes.</summary>
    public ModelState Models { get; init; } = new(false, null, false, true, null);
    /// <summary>Instala un paquete .claupet con la revisión de siempre (firma, permisos, ranuras).</summary>
    public Func<string, IWin32Window, Task>? InstallPackage { get; init; }

    readonly string _currentMascot;

    public SetupForm(AppSettings settings, string exePath, Theme theme, IEnumerable<string> configDirs)
    {
        _t = theme;
        _startLanguage = Language = L.Language;
        _currentMascot = settings.CustomPet is null ? Mascots.ChoiceOf(settings) : "";
        DesktopMascot = settings.DesktopMascot;
        Island = settings.Island;
        Chat = settings.Chat;
        StartWithWindows = StartupRegistration.IsEnabled || !settings.SetupDone;
        NotificationsOn = settings.Notifications;
        Mic = settings.Mic;
        ReadAloud = settings.ReadAloud;
        foreach (var dir in configDirs)
        {
            var row = new HookRow(dir, HookInstaller.Status(dir, exePath), dir.Replace(Account.Home, "~"));
            _rows.Add(row);
            if (row.State is HookInstaller.State.Missing or HookInstaller.State.Outdated) _hooksOn.Add(dir);
        }

        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        BackColor = theme.Background;
        ForeColor = theme.Text;
        Font = Fonts.Make("Segoe UI Variable Text", 9.75f, "Segoe UI");
        _small = Fonts.Make("Segoe UI Variable Text", 8.75f, "Segoe UI");
        _strong = Fonts.Make("Segoe UI Variable Text Semibold", 9.75f, "Segoe UI Semibold");
        _eyebrow = Fonts.Make("Segoe UI Variable Text Semibold", 8f, "Segoe UI Semibold");
        _title = Fonts.Make("Georgia", 17f, "Cambria");
        ClientSize = new Size(U(800), U(600));

        _rail = new StepRail(theme, Version()) { Dock = DockStyle.Left, Width = U(236) };
        _rail.StepClicked += GoTo;
        _rail.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) DragWindow(); };

        _step = new Label { AutoSize = false, Font = _eyebrow, ForeColor = theme.Tertiary };
        _heading = new Label { AutoSize = false, Font = _title, ForeColor = theme.Text };
        _lead = new Label { AutoSize = false, ForeColor = theme.Secondary };
        _body = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = theme.Background,
        };
        _close = new PillButton(theme, "", PillButton.Kind.Close) { AccessibleName = L.T("Cerrar") };
        _close.Click += (_, _) => Close();
        _later = new PillButton(theme, "", PillButton.Kind.Link);
        _later.Click += (_, _) => Close();
        _back = new PillButton(theme, "", PillButton.Kind.Quiet);
        _back.Click += (_, _) => GoTo(_current - 1);
        _next = new PillButton(theme, "", PillButton.Kind.Primary);
        _next.Click += (_, _) =>
        {
            if (_current < Steps - 1) GoTo(_current + 1);
            else
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };
        Controls.AddRange([_step, _heading, _lead, _body, _close, _later, _back, _next, _rail]);
        GoTo(0);
    }

    int U(int value) => LogicalToDeviceUnits(value);

    static string Version()
    {
        var v = typeof(SetupForm).Assembly.GetName().Version;
        return v is null ? "" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    string[] StepNames => [L.T("Bienvenida"), L.T("Avisos de permisos"), L.T("Su compañero"), L.T("Modelos y voz"), L.T("Inicio y avisos")];

    /// <summary>Muestra un paso: lo rehace entero, así también sirve al cambiar de idioma.</summary>
    void GoTo(int index)
    {
        _current = Math.Clamp(index, 0, Steps - 1);
        Text = L.T("Configurar Claupet");
        _rail.SetSteps(StepNames, _current, L.T("Versión {0}", Version()), L.T("Próximamente"));
        _close.AccessibleName = L.T("Cerrar");
        _later.Text = L.T("Ahora no");
        _back.Text = L.T("Atrás");
        _next.Text = _current == Steps - 1 ? L.T("Aplicar") : L.T("Siguiente");
        _back.Visible = _current > 0;

        _body.SuspendLayout();
        foreach (Control old in _body.Controls.Cast<Control>().ToList()) old.Dispose();
        _body.Controls.Clear();
        switch (_current)
        {
            case 0: Welcome(); break;
            case 1: Hooks(); break;
            case 2: Companion(); break;
            case 3: ModelsAndVoice(); break;
            default: Startup(); break;
        }
        _body.ResumeLayout();
        Arrange();
        ActiveControl = _next;
    }

    /// <summary>Coloca encabezado, cuerpo y botones según el alto de los textos del paso.</summary>
    void Arrange()
    {
        var left = _rail.Width + U(40);
        var width = ClientSize.Width - left - U(40);
        _close.Location = new Point(ClientSize.Width - _close.Width - U(14), U(14));
        _step.Bounds = new Rectangle(left, U(36), width, _step.GetPreferredSize(new Size(width, 0)).Height);
        _heading.Bounds = new Rectangle(left, _step.Bottom + U(6), width, _heading.GetPreferredSize(new Size(width, 0)).Height);
        _lead.Bounds = new Rectangle(left, _heading.Bottom + U(8), width, _lead.GetPreferredSize(new Size(width, 0)).Height);

        var footer = ClientSize.Height - U(30) - _next.Height;
        _next.Location = new Point(left + width - _next.Width, footer);
        _back.Location = new Point(_next.Left - U(10) - _back.Width, footer);
        _later.Location = new Point(left - U(8), footer + (_next.Height - _later.Height) / 2);
        var top = _lead.Bottom + U(20);
        _body.Bounds = new Rectangle(left, top, width + SystemInformation.VerticalScrollBarWidth + U(2), footer - U(16) - top);
    }

    int BodyWidth => ClientSize.Width - _rail.Width - U(80);

    void Header(string heading, string lead)
    {
        _step.Text = L.T("PASO {0} DE {1}", _current + 1, Steps);
        _heading.Text = heading;
        _lead.Text = lead;
    }

    // ── Pasos ────────────────────────────────────────────────────────────────────────────────

    void Welcome()
    {
        Header(L.T("Le damos la bienvenida a Claupet"),
            L.T("Le muestra el uso de Claude Code, le avisa de los permisos y le ayuda con su asistente, su voz y sus extensiones. Nada cambia hasta que pulse «Aplicar»."));
        var languages = new Segmented(_t, L.Choices.Select(c => c.Label).ToArray(), Array.FindIndex(L.Choices, c => c.Id == Language));
        languages.Picked += i =>
        {
            Language = L.Choices[i].Id;
            L.Use(Language); // el asistente ya en el idioma elegido; la app se reinicia al aplicar
            BeginInvoke(() => GoTo(0));
        };
        Add(Field(L.T("Idioma"), languages));
        Add(Eyebrow(L.T("NOVEDADES"), 18));
        Add(Cards(
            (L.T("Extensiones"), L.T("Mascotas, voces, sonidos y habilidades en paquetes firmados."), L.T("Nuevo")),
            (L.T("Acciones de Windows"), L.T("Capturas, calendario, notas y recordatorios, con su permiso."), L.T("Nuevo")),
            (L.T("Voz"), L.T("Háblele con el micrófono y escuche las respuestas."), null),
            (L.T("Isla"), L.T("Sesiones, permisos y un chat rápido en el borde de arriba."), null)));
    }

    void Hooks()
    {
        Header(L.T("Avisos de permisos"),
            L.T("Para avisarle cuando una sesión pide permiso, Claupet agrega un hook al settings.json de cada carpeta de Claude Code. Antes de cambiarlo guarda un respaldo."));
        if (_rows.Count == 0)
        {
            Add(Note(L.T("No se encontró ninguna carpeta de Claude Code.")));
            return;
        }
        foreach (var row in _rows)
        {
            var (detail, enabled) = row.State switch
            {
                HookInstaller.State.Installed => (L.T("Ya está configurado."), false),
                HookInstaller.State.Outdated => (L.T("Apunta a otra ruta: se actualizará."), true),
                HookInstaller.State.Unreadable => (L.T("Su settings.json tiene errores; no se toca."), false),
                _ => (L.T("Se agregará el hook."), true),
            };
            var on = row.State == HookInstaller.State.Installed || _hooksOn.Contains(row.Dir);
            Add(new SwitchRow(_t, row.Name, detail, on, enabled, BodyWidth, Font, _small, v =>
            {
                if (v) _hooksOn.Add(row.Dir);
                else _hooksOn.Remove(row.Dir);
            }));
        }
    }

    void Companion()
    {
        Header(L.T("Su compañero"), L.T("Elija cómo quiere ver a Claupet. Todo se puede cambiar después en Ajustes."));
        string[] ids = [Mascots.Chibi, Mascots.Classic];
        var picked = Array.IndexOf(ids, Mascot ?? _currentMascot);
        SwitchRow? desk = null;
        var mascots = new Segmented(_t, [L.T("Chibi"), L.T("Clásica")], picked);
        mascots.Picked += i =>
        {
            Mascot = ids[i];
            if (Mascot == Mascots.Chibi && desk is not null) desk.On = true; // el chibi sale al escritorio
        };
        Add(Field(L.T("Mascota"), mascots));
        desk = new SwitchRow(_t, L.T("Mascota en el escritorio"),
            L.T("Le acompaña sobre las ventanas, reacciona a sus sesiones y baila con la música."), DesktopMascot, true, BodyWidth, Font, _small, v => DesktopMascot = v);
        Add(desk);
        Add(new SwitchRow(_t, L.T("Chat con la mascota"),
            L.T("Escríbale a la mascota para pedirle cosas a Claude Code."), Chat, true, BodyWidth, Font, _small, v => Chat = v));
        Add(new SwitchRow(_t, L.T("Isla"),
            L.T("Asoma arriba de la pantalla con sus sesiones, los permisos y un chat rápido."), Island, true, BodyWidth, Font, _small, v => Island = v));
        Add(Note(L.T("Las acciones de Windows piden permiso la primera vez que se usan; se ajustan en Ajustes › Asistente.")));
    }

    void ModelsAndVoice()
    {
        Header(L.T("Modelos y voz"),
            L.T("Los modelos corren en su equipo: se descargan una vez, en segundo plano, y luego funcionan sin internet."));
        var m = Models;
        var mb = (VoiceModel.Size + 500_000) / 1_000_000;
        var (micDetail, micEnabled) = m.VoiceExtension is { } stt ? (L.T("Lo entiende la extensión {0}.", stt), true)
            : m.VoiceReady ? (L.T("El modelo de voz ya está en este equipo."), true)
            : (L.T("Descarga el modelo de voz Whisper ({0} MB).", mb), true);
        Add(new SwitchRow(_t, L.T("Hablarle con el micrófono"), micDetail, Mic, micEnabled, BodyWidth, Font, _small, v =>
        {
            Mic = v;
            if (v && !Chat && !Island) Chat = true; // el micrófono necesita a quién hablarle
        }));
        var speechMb = (ClaupetVoiceModel.Size + 500_000) / 1_000_000;
        var readDetail = m.SpeechExtension is { } tts ? L.T("Con la voz de la extensión {0}.", tts)
            : !m.SpeechNeeded ? L.T("Con la voz elegida en Ajustes › Asistente.")
            : m.SpeechReady ? L.T("La voz de Claupet ya está en este equipo.")
            : L.T("Descarga la voz de Claupet ({0} MB); mientras, lee la de Windows.", speechMb);
        Add(new SwitchRow(_t, L.T("Leer las respuestas en voz alta"), readDetail, ReadAloud, true, BodyWidth, Font, _small, v => ReadAloud = v));

        if (InstallPackage is { } install)
        {
            var button = new PillButton(_t, L.T("Instalar…"), PillButton.Kind.Quiet);
            button.Click += async (_, _) =>
            {
                using var dialog = new OpenFileDialog { Filter = L.T("Paquetes de Claupet") + " (*.claupet)|*.claupet", Title = L.T("Instalar una extensión") };
                if (dialog.ShowDialog(this) == DialogResult.OK) await install(dialog.FileName, this);
            };
            Add(ActionRow(L.T("Extensiones"), L.T("Voces, modelos de voz y asistentes de terceros en paquetes firmados."), button));
        }

        Add(Eyebrow(L.T("PRÓXIMAMENTE"), 14));
        Add(Cards((L.T("Modelo de apoyo local"),
            L.T("Un modelo pequeño en su equipo que responde lo simple sin gastar tokens y funciona sin internet. Se activa a pedido: usa más memoria."), null)));
    }

    void Startup()
    {
        Header(L.T("Inicio y avisos"), L.T("Último paso. Revise esto y pulse «Aplicar»."));
        Add(new SwitchRow(_t, L.T("Iniciar con Windows"),
            L.T("Claupet se abre en la bandeja al encender el equipo."), StartWithWindows, true, BodyWidth, Font, _small, v => StartWithWindows = v));
        Add(new SwitchRow(_t, L.T("Notificaciones"),
            L.T("Cuando una sesión termina o falla, y cuando el uso se acerca al límite."), NotificationsOn, true, BodyWidth, Font, _small, v => NotificationsOn = v));
        Add(Eyebrow(L.T("PRÓXIMAMENTE"), 18));
        Add(Cards(
            ("Claupet Console", L.T("Una ventana con chat, editor de documentos y de código con IA, y todos los ajustes. Vendrá incluida."), L.T("Incluida")),
            ("Claupet IDE", L.T("Una extensión que lleva el editor de código a nivel de IDE: vista previa de interfaces y webs, y artifacts."), L.T("Extensión"))));
    }

    // ── Piezas ───────────────────────────────────────────────────────────────────────────────

    void Add(Control control) => _body.Controls.Add(control);

    Label Eyebrow(string text, int top) => new()
    {
        Text = text, Font = _eyebrow, ForeColor = _t.Tertiary, AutoSize = true, Margin = new Padding(0, U(top), 0, U(8)),
    };

    Label Note(string text) => new()
    {
        Text = text, Font = _small, ForeColor = _t.Tertiary, AutoSize = true, MaximumSize = new Size(BodyWidth, 0),
        Margin = new Padding(0, U(14), 0, 0),
    };

    /// <summary>Una etiqueta a la izquierda y su control a la derecha.</summary>
    Control Field(string label, Control control)
    {
        var row = new Panel { Width = BodyWidth, Height = Math.Max(control.Height, U(32)), Margin = new Padding(0, 0, 0, U(6)) };
        var caption = new Label { Text = label, AutoSize = true, ForeColor = _t.Text };
        row.Controls.Add(caption);
        row.Controls.Add(control);
        caption.Location = new Point(0, (row.Height - caption.PreferredHeight) / 2);
        control.Location = new Point(row.Width - control.Width, (row.Height - control.Height) / 2);
        return row;
    }

    /// <summary>Como una fila con interruptor, pero con un botón a la derecha.</summary>
    Control ActionRow(string title, string detail, Control action)
    {
        var row = new Panel { Width = BodyWidth, Margin = new Padding(0, 0, 0, U(4)), BackColor = _t.Background };
        var textWidth = BodyWidth - action.Width - U(20);
        var name = new Label { Text = title, ForeColor = _t.Text, AutoSize = false };
        name.Size = name.GetPreferredSize(new Size(textWidth, 0)) with { Width = textWidth };
        var line = new Label { Text = detail, Font = _small, ForeColor = _t.Tertiary, AutoSize = false };
        line.Size = line.GetPreferredSize(new Size(textWidth, 0)) with { Width = textWidth };
        name.Location = new Point(0, U(8));
        line.Location = new Point(0, name.Bottom + U(1));
        row.Height = Math.Max(line.Bottom + U(8), action.Height + U(8));
        action.Location = new Point(BodyWidth - action.Width, (row.Height - action.Height) / 2);
        row.Controls.AddRange([name, line, action]);
        return row;
    }

    /// <summary>Tarjetas de dos en dos (una sola, a todo lo ancho): título, una etiqueta opcional y una línea.</summary>
    Control Cards(params (string Title, string Text, string? Badge)[] cards)
    {
        var gap = U(12);
        var width = cards.Length == 1 ? BodyWidth : (BodyWidth - gap) / 2;
        var grid = new Panel { Width = BodyWidth, Margin = Padding.Empty, BackColor = _t.Background };
        var made = cards.Select(c => new Card(_t, _t.Background, width, c.Title, c.Text, c.Badge, _strong, _small)).ToList();
        var height = made.Max(c => c.Height); // todas del mismo alto
        for (var i = 0; i < made.Count; i++)
        {
            made[i].Bounds = new Rectangle(i % 2 * (width + gap), i / 2 * (height + gap), width, height);
            grid.Controls.Add(made[i]);
        }
        grid.Height = (made.Count + 1) / 2 * (height + gap) - gap;
        return grid;
    }

    void DragWindow()
    {
        ReleaseCapture();
        SendMessage(Handle, WmNcLButtonDown, HtCaption, 0);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        // El fondo de la ventana (fuera de los controles) también sirve para moverla.
        if (m.Msg == WmNcHitTest && m.Result == HtClient) m.Result = HtCaption;
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Dwm.Apply(Handle, _t);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // Sin aplicar, el idioma vuelve a ser el de antes.
        if (DialogResult != DialogResult.OK && L.Language != _startLanguage) L.Use(_startLanguage);
        base.OnFormClosed(e);
    }

    /// <summary>Instala o actualiza el hook donde se marcó. Devuelve una línea por carpeta.</summary>
    public List<string> ApplyHooks(string exePath)
    {
        var lines = new List<string>();
        foreach (var row in _rows.Where(r => r.State is HookInstaller.State.Missing or HookInstaller.State.Outdated && _hooksOn.Contains(r.Dir)))
        {
            try
            {
                var backup = HookInstaller.Install(row.Dir, exePath);
                var verb = row.State == HookInstaller.State.Outdated ? L.T("hook actualizado") : L.T("hook agregado");
                lines.Add(backup is null ? $"{row.Name}: {verb}." : L.T("{0}: {1} (respaldo: {2}).", row.Name, verb, Path.GetFileName(backup)));
            }
            catch (Exception ex)
            {
                lines.Add(L.T("{0}: no se pudo cambiar ({1}).", row.Name, ex.Message));
            }
        }
        return lines;
    }

    const int WmNcHitTest = 0x84, WmNcLButtonDown = 0xA1;
    static readonly IntPtr HtClient = 1, HtCaption = 2;

    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, int lParam);
}

/// <summary>La columna de la izquierda: la cara del chibi, el nombre, los pasos y lo que viene.</summary>
sealed class StepRail : Control
{
    readonly Theme _t;
    readonly Bitmap _head = ChibiHead();
    readonly Font _name = Fonts.Make("Georgia", 15f, "Cambria");
    readonly Font _text = Fonts.Make("Segoe UI Variable Text", 9.5f, "Segoe UI");
    readonly Font _current = Fonts.Make("Segoe UI Variable Text Semibold", 9.5f, "Segoe UI Semibold");
    readonly Font _small = Fonts.Make("Segoe UI Variable Text", 8.5f, "Segoe UI");
    readonly Font _eyebrow = Fonts.Make("Segoe UI Variable Text Semibold", 7.75f, "Segoe UI Semibold");
    string[] _steps = [];
    int _index;
    string _version = "", _soon = "";
    int _hover = -1;

    public event Action<int>? StepClicked;

    public StepRail(Theme theme, string version)
    {
        _t = theme;
        _version = version;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = theme.Dark ? Color.FromArgb(0x2E, 0x2E, 0x2B) : Color.FromArgb(0xF0, 0xEE, 0xE6);
    }

    int U(int value) => LogicalToDeviceUnits(value);

    public void SetSteps(string[] steps, int index, string version, string soon)
    {
        (_steps, _index, _version, _soon) = (steps, index, version, soon);
        AccessibleName = string.Join(", ", steps);
        Invalidate();
    }

    Rectangle StepBounds(int i) => new(U(16), U(176) + i * U(44), Width - U(32), U(40));

    int HitStep(Point p)
    {
        for (var i = 0; i < _steps.Length; i++)
            if (StepBounds(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitStep(e.Location);
        if (hit == _hover) return;
        _hover = hit;
        Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var hit = HitStep(e.Location);
        if (hit >= 0) StepClicked?.Invoke(hit);
        else base.OnMouseDown(e); // fuera de los pasos, mueve la ventana
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // La cara del chibi, en pixeles nítidos, sobre un cuadro redondeado.
        var tile = new RectangleF(U(24), U(30), U(64), U(64));
        Draw.Fill(g, _t.Raised, tile, U(18));
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        var scale = Math.Max(1, U(48) / Math.Max(_head.Width, _head.Height));
        var w = _head.Width * scale;
        var h = _head.Height * scale;
        g.DrawImage(_head, tile.X + (tile.Width - w) / 2, tile.Y + (tile.Height - h) / 2, w, h);
        g.PixelOffsetMode = PixelOffsetMode.Default;

        TextRenderer.DrawText(g, "Claupet", _name, new Point(U(22), U(104)), _t.Text);
        TextRenderer.DrawText(g, _version, _small, new Point(U(24), U(134)), _t.Tertiary);

        for (var i = 0; i < _steps.Length; i++)
        {
            var r = StepBounds(i);
            if (i == _index) Draw.Fill(g, _t.Raised, r, U(12));
            else if (i == _hover) Draw.Fill(g, Blend(BackColor, _t.Raised, 0.5), r, U(12));
            var dot = new RectangleF(r.X + U(10), r.Y + (r.Height - U(22)) / 2f, U(22), U(22));
            if (i < _index)
            {
                using var fill = new SolidBrush(_t.Accent);
                g.FillEllipse(fill, dot);
                using var pen = new Pen(Color.FromArgb(0x14, 0x14, 0x13), U(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                g.DrawLines(pen, [
                    new PointF(dot.X + dot.Width * 0.28f, dot.Y + dot.Height * 0.52f),
                    new PointF(dot.X + dot.Width * 0.44f, dot.Y + dot.Height * 0.68f),
                    new PointF(dot.X + dot.Width * 0.72f, dot.Y + dot.Height * 0.36f)]);
            }
            else
            {
                if (i == _index)
                {
                    using var fill = new SolidBrush(_t.Accent);
                    g.FillEllipse(fill, dot);
                }
                else
                {
                    using var pen = new Pen(_t.Tertiary, U(1));
                    g.DrawEllipse(pen, dot);
                }
                TextRenderer.DrawText(g, (i + 1).ToString(), _small, Rectangle.Round(dot),
                    i == _index ? Color.FromArgb(0x14, 0x14, 0x13) : _t.Secondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            var label = new Rectangle((int)dot.Right + U(10), r.Y, r.Right - (int)dot.Right - U(12), r.Height);
            TextRenderer.DrawText(g, _steps[i], i == _index ? _current : _text, label, i == _index ? _t.Text : _t.Secondary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        // Lo que viene, al pie.
        var y = Height - U(92);
        TextRenderer.DrawText(g, _soon.ToUpper(), _eyebrow, new Point(U(24), y), _t.Tertiary);
        foreach (var name in new[] { "Claupet Console", "Claupet IDE" })
        {
            y += U(24);
            using var dot = new SolidBrush(_t.Accent);
            g.FillEllipse(dot, U(26), y + U(7), U(6), U(6));
            TextRenderer.DrawText(g, name, _text, new Point(U(40), y), _t.Secondary);
        }
    }

    static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    /// <summary>La cabeza grande del chibi, recortada a lo que pinta.</summary>
    static Bitmap ChibiHead()
    {
        var rects = new List<(Color Color, Rectangle Rect)>();
        ChibiSprite.PaintHead((color, x, y, w, h) => rects.Add((color, new Rectangle(x, y, w, h))), MascotFrame.Open);
        if (rects.Count == 0) return new Bitmap(1, 1);
        var left = rects.Min(r => r.Rect.Left);
        var top = rects.Min(r => r.Rect.Top);
        var bmp = new Bitmap(rects.Max(r => r.Rect.Right) - left, rects.Max(r => r.Rect.Bottom) - top);
        using var g = Graphics.FromImage(bmp);
        foreach (var (color, rect) in rects)
        {
            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, rect.X - left, rect.Y - top, rect.Width, rect.Height);
        }
        return bmp;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _head.Dispose();
            _name.Dispose();
            _text.Dispose();
            _current.Dispose();
            _small.Dispose();
            _eyebrow.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>Botón en píldora: el principal (acento), uno discreto, un enlace o la × de cerrar.</summary>
sealed class PillButton : Control
{
    public enum Kind { Primary, Quiet, Link, Close }

    readonly Theme _t;
    readonly Kind _kind;
    bool _hover, _down;

    public PillButton(Theme theme, string text, Kind kind)
    {
        _t = theme;
        _kind = kind;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
        BackColor = theme.Background;
        Font = Fonts.Make(kind == Kind.Primary ? "Segoe UI Variable Text Semibold" : "Segoe UI Variable Text", 9.75f,
            kind == Kind.Primary ? "Segoe UI Semibold" : "Segoe UI");
        Text = text;
        Fit();
    }

    int U(int value) => LogicalToDeviceUnits(value);

    void Fit()
    {
        if (_kind == Kind.Close)
        {
            Size = new Size(U(32), U(32));
            return;
        }
        var text = TextRenderer.MeasureText(Text.Length == 0 ? " " : Text, Font);
        Size = new Size(text.Width + U(_kind == Kind.Link ? 16 : 36), U(38));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (_kind != Kind.Close) AccessibleName = Text;
        Fit();
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = _down = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _down = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _down = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is not (Keys.Enter or Keys.Space)) return;
        e.Handled = true;
        OnClick(EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var ink = Color.FromArgb(0x14, 0x14, 0x13);
        switch (_kind)
        {
            case Kind.Primary:
                Draw.Fill(g, _down ? ControlPaint.Dark(_t.Accent, 0.05f) : _hover ? ControlPaint.Light(_t.Accent, 0.25f) : _t.Accent, r, r.Height / 2);
                break;
            case Kind.Quiet:
                Draw.Fill(g, _hover ? _t.Hover : _t.Raised, r, r.Height / 2);
                Draw.Outline(g, _t.Border, r, r.Height / 2);
                ink = _t.Text;
                break;
            case Kind.Link:
                ink = _hover ? _t.Text : _t.Secondary;
                break;
            case Kind.Close:
                if (_hover) Draw.Fill(g, _t.Hover, r, r.Height / 2);
                using (var pen = new Pen(_hover ? _t.Text : _t.Secondary, U(1) * 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    float c = Width / 2f, d = U(5);
                    g.DrawLine(pen, c - d, c - d, c + d, c + d);
                    g.DrawLine(pen, c + d, c - d, c - d, c + d);
                }
                break;
        }
        if (_kind != Kind.Close)
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(_t.Accent, U(2)) { DashStyle = DashStyle.Solid };
            using var path = Draw.Rounded(new RectangleF(U(1), U(1), Width - U(2) - 1, Height - U(2) - 1), (Height - U(2)) / 2f);
            g.DrawPath(pen, path);
        }
    }
}

/// <summary>Interruptor redondeado, como los de Ajustes.</summary>
sealed class ToggleSwitch : Control
{
    readonly Theme _t;
    bool _on;

    public event Action<bool>? Toggled;

    public ToggleSwitch(Theme theme, bool on)
    {
        _t = theme;
        _on = on;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.CheckButton;
        Size = new Size(LogicalToDeviceUnits(42), LogicalToDeviceUnits(24));
    }

    public bool On
    {
        get => _on;
        set
        {
            if (_on == value) return;
            _on = value;
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            Invalidate();
            Toggled?.Invoke(value);
        }
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new SwitchAccessible(this);

    sealed class SwitchAccessible(ToggleSwitch owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleStates State => base.State | (owner.On ? AccessibleStates.Checked : AccessibleStates.None);
        public override string DefaultAction => owner.On ? L.T("Apagar") : L.T("Encender");
        public override void DoDefaultAction() => owner.Flip();
    }

    public void Flip()
    {
        if (Enabled) On = !On;
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Focus();
        Flip();
    }

    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode != Keys.Space) return;
        e.Handled = true;
        Flip();
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var u = LogicalToDeviceUnits(1);
        var off = _t.Dark ? Color.FromArgb(0x4A, 0x49, 0x46) : Color.FromArgb(0xD9, 0xD6, 0xCC);
        var track = _on ? _t.Accent : off;
        if (!Enabled) track = Color.FromArgb(110, track);
        var r = new RectangleF(u, u, Width - 2 * u - 1, Height - 2 * u - 1);
        Draw.Fill(g, track, r, r.Height / 2);
        var d = r.Height - 6 * u;
        var x = _on ? r.Right - d - 3 * u : r.X + 3 * u;
        using (var knob = new SolidBrush(Enabled ? Color.White : Color.FromArgb(200, Color.White)))
            g.FillEllipse(knob, x, r.Y + 3 * u, d, d);
        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(_t.Text, u);
            using var path = Draw.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), (Height - 1) / 2f);
            g.DrawPath(pen, path);
        }
    }
}

/// <summary>Una opción con título y explicación a la izquierda y su interruptor a la derecha.</summary>
sealed class SwitchRow : Panel
{
    readonly ToggleSwitch _switch;

    public SwitchRow(Theme theme, string title, string detail, bool on, bool enabled, int width, Font font, Font small, Action<bool> changed)
    {
        Width = width;
        Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(4));
        BackColor = theme.Background;
        _switch = new ToggleSwitch(theme, on) { Enabled = enabled, AccessibleName = title, BackColor = theme.Background };
        _switch.Toggled += v => changed(v);
        var textWidth = width - _switch.Width - LogicalToDeviceUnits(20);
        var name = new Label { Text = title, Font = font, ForeColor = enabled ? theme.Text : theme.Secondary, AutoSize = false };
        name.Size = name.GetPreferredSize(new Size(textWidth, 0)) with { Width = textWidth };
        var line = new Label { Text = detail, Font = small, ForeColor = theme.Tertiary, AutoSize = false };
        line.Size = line.GetPreferredSize(new Size(textWidth, 0)) with { Width = textWidth };
        var pad = LogicalToDeviceUnits(8);
        name.Location = new Point(0, pad);
        line.Location = new Point(0, name.Bottom + LogicalToDeviceUnits(1));
        Height = line.Bottom + pad;
        _switch.Location = new Point(width - _switch.Width, (Height - _switch.Height) / 2);
        foreach (var label in new[] { name, line })
        {
            label.Cursor = enabled ? Cursors.Hand : Cursors.Default;
            label.Click += (_, _) => _switch.Flip();
        }
        Controls.AddRange([name, line, _switch]);
    }

    public bool On
    {
        get => _switch.On;
        set => _switch.On = value;
    }
}

/// <summary>Opciones en píldora, una elegida (o ninguna, con -1).</summary>
sealed class Segmented : Control
{
    readonly Theme _t;
    readonly string[] _options;
    readonly int[] _widths;
    int _picked, _hover = -1;

    public event Action<int>? Picked;

    public Segmented(Theme theme, string[] options, int picked)
    {
        _t = theme;
        _options = options;
        _picked = picked;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        BackColor = theme.Background;
        AccessibleRole = AccessibleRole.PageTabList;
        Font = Fonts.Make("Segoe UI Variable Text", 9.5f, "Segoe UI");
        _widths = options.Select(o => TextRenderer.MeasureText(o, Font).Width + LogicalToDeviceUnits(28)).ToArray();
        Size = new Size(_widths.Sum() + LogicalToDeviceUnits(8), LogicalToDeviceUnits(36));
        AccessibleName = string.Join(" / ", options);
    }

    Rectangle Segment(int i)
    {
        var x = LogicalToDeviceUnits(4) + _widths.Take(i).Sum();
        return new Rectangle(x, LogicalToDeviceUnits(4), _widths[i], Height - LogicalToDeviceUnits(8));
    }

    int Hit(Point p)
    {
        for (var i = 0; i < _options.Length; i++)
            if (Segment(i).Contains(p)) return i;
        return -1;
    }

    void Pick(int i)
    {
        if (i < 0 || i >= _options.Length || i == _picked) return;
        _picked = i;
        Invalidate();
        Picked?.Invoke(i);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        Pick(Hit(e.Location));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = Hit(e.Location);
        if (hit == _hover) return;
        _hover = hit;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) Pick(Math.Max(0, _picked - 1));
        else if (e.KeyCode == Keys.Right) Pick(Math.Min(_options.Length - 1, _picked + 1));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = _t.Dark ? Color.FromArgb(0x30, 0x30, 0x2E) : Color.FromArgb(0xF0, 0xEE, 0xE6);
        var all = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Draw.Fill(g, track, all, all.Height / 2);
        if (Focused && ShowFocusCues) Draw.Outline(g, _t.Accent, all, all.Height / 2);
        for (var i = 0; i < _options.Length; i++)
        {
            var r = Segment(i);
            if (i == _picked) Draw.Fill(g, _t.Dark ? Color.FromArgb(0x3D, 0x3D, 0x3A) : Color.White, r, r.Height / 2f);
            var ink = i == _picked || i == _hover ? _t.Text : _t.Secondary;
            TextRenderer.DrawText(g, _options[i], Font, r, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}

/// <summary>Tarjeta redondeada con título, una etiqueta opcional (Nuevo, Próximamente…) y una explicación.</summary>
sealed class Card : Panel
{
    readonly Theme _t;
    readonly Color _outside;

    public Card(Theme theme, Color outside, int width, string title, string text, string? badge, Font strong, Font small)
    {
        _t = theme;
        _outside = outside;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = theme.Raised;
        Width = width;
        var pad = LogicalToDeviceUnits(14);
        var inner = width - 2 * pad;
        var name = new Label { Text = title, Font = strong, ForeColor = theme.Text, AutoSize = true, Location = new Point(pad, pad) };
        Controls.Add(name);
        if (badge is not null)
        {
            var tag = new Badge(theme, badge) { BackColor = theme.Raised };
            tag.Location = new Point(width - pad - tag.Width, pad - LogicalToDeviceUnits(1));
            Controls.Add(tag);
        }
        var line = new Label { Text = text, Font = small, ForeColor = theme.Secondary, AutoSize = false };
        line.Size = line.GetPreferredSize(new Size(inner, 0)) with { Width = inner };
        line.Location = new Point(pad, name.Top + name.PreferredHeight + LogicalToDeviceUnits(6));
        Controls.Add(line);
        Height = line.Bottom + pad;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(_outside);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var radius = LogicalToDeviceUnits(14);
        Draw.Fill(g, _t.Raised, r, radius);
        Draw.Outline(g, _t.Border, r, radius);
    }
}

/// <summary>Etiqueta pequeña en píldora, con el color del acento.</summary>
sealed class Badge : Control
{
    readonly Theme _t;

    public Badge(Theme theme, string text)
    {
        _t = theme;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Font = Fonts.Make("Segoe UI Variable Text Semibold", 7.75f, "Segoe UI Semibold");
        Text = text;
        var size = TextRenderer.MeasureText(text, Font);
        Size = new Size(size.Width + LogicalToDeviceUnits(12), size.Height + LogicalToDeviceUnits(4));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        Draw.Fill(g, Color.FromArgb(_t.Dark ? 56 : 36, _t.Accent), r, r.Height / 2);
        var ink = _t.Dark ? ControlPaint.Light(_t.Accent, 0.3f) : ControlPaint.Dark(_t.Accent, 0.15f);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}
