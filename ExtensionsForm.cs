using System.Diagnostics;
using System.Globalization;

namespace Claupet;

/// <summary>Lo que la ventana de extensiones le pide a la app.</summary>
interface IExtensionsPanelHost
{
    ExtensionManager Extensions { get; }
    Task InstallPackageAsync(string path, IWin32Window? owner);
    bool LocalApiEnabled { get; }
    string LocalApiStatus { get; }
    void SetLocalApi(bool on);
    IReadOnlyList<ApiToken> ApiTokens { get; }
    /// <summary>Crea el token y devuelve su texto, que se muestra una sola vez.</summary>
    string CreateApiToken(string name, IReadOnlyList<string> permissions);
    void RevokeApiToken(string id);
}

/// <summary>Lo común de las ventanas de extensiones: los colores del tema, las letras y los controles con su estilo.</summary>
abstract class ExtensionDialog : Form
{
    protected readonly Theme T;
    protected readonly Font Small;

    protected ExtensionDialog(Theme theme, string title)
    {
        T = theme;
        Text = title;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = theme.Background;
        ForeColor = theme.Text;
        Font = Fonts.Make("Segoe UI Variable Text", 9.5f, "Segoe UI");
        Small = Fonts.Make("Segoe UI Variable Text", 8.75f, "Segoe UI");
    }

    protected int U(int value) => LogicalToDeviceUnits(value);

    protected Label Caption(string text, Font? font = null, Color? color = null, int width = 560, int top = 0) => new()
    {
        Text = text, Font = font ?? Font, ForeColor = color ?? T.Text, AutoSize = true, MaximumSize = new Size(U(width), 0),
        Margin = new Padding(0, U(top), 0, U(4)),
    };

    protected Button Action(string text, EventHandler click, bool primary = false)
    {
        var button = new Button
        {
            Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Padding = new Padding(U(8), U(2), U(8), U(2)),
            BackColor = primary ? T.Accent : T.Raised, ForeColor = primary ? Color.White : T.Text,
            FlatAppearance = { BorderColor = primary ? T.Accent : T.Border }, Margin = new Padding(0, U(4), U(8), U(4)),
        };
        button.Click += click;
        return button;
    }

    protected static FlowLayoutPanel Row() => new() { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
}

/// <summary>
/// Ajustes › Extensiones: las instaladas (encender, ajustes, registro, quitar), quién ocupa cada ranura
/// exclusiva (o el integrado), instalar un paquete y la API local con sus tokens.
/// </summary>
sealed class ExtensionsForm : ExtensionDialog
{
    readonly IExtensionsPanelHost _host;
    readonly ExtensionManager _manager;
    readonly ListView _list;
    readonly Label _detail, _problems, _apiStatus;
    readonly Button _toggle, _settings, _log, _remove;
    readonly Dictionary<string, ComboBox> _slots = new();
    readonly CheckBox _api;
    readonly ListBox _tokens;
    bool _loading;

    public ExtensionsForm(IExtensionsPanelHost host, Theme theme) : base(theme, L.T("Extensiones de Claupet"))
    {
        _host = host;
        _manager = host.Extensions;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(U(640), U(560));
        Size = new Size(U(720), U(760));
        AllowDrop = true;
        DragEnter += (_, e) => e.Effect = e.Data?.GetData(DataFormats.FileDrop) is string[] f && f.Any(x => x.EndsWith(".claupet", StringComparison.OrdinalIgnoreCase)) ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += async (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.FirstOrDefault(x => x.EndsWith(".claupet", StringComparison.OrdinalIgnoreCase)) is { } file)
                await _host.InstallPackageAsync(file, this);
        };

        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Dock = DockStyle.Fill,
            Padding = new Padding(U(22)),
        };
        Controls.Add(layout);
        layout.Controls.Add(Caption(L.T("Extensiones"), Fonts.Make("Georgia", 14f, "Cambria")));
        layout.Controls.Add(Caption(L.T("Sin extensiones, Claupet funciona como siempre. Instale solo paquetes (.claupet) de quien confíe: un programa o una DLL corre con sus permisos."), Small, T.Secondary));

        var top = Row();
        top.Controls.Add(Action(L.T("Instalar un paquete…"), async (_, _) => await PickAsync(), primary: true));
        top.Controls.Add(Action(L.T("Abrir la carpeta"), (_, _) => Open(_manager.Store.Root)));
        layout.Controls.Add(top);

        _list = new ListView
        {
            View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Width = U(640), Height = U(170),
            BackColor = T.Raised, ForeColor = T.Text, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, U(8), 0, U(4)),
        };
        _list.Columns.Add(L.T("Nombre"), U(190));
        _list.Columns.Add(L.T("Versión"), U(64));
        _list.Columns.Add(L.T("Qué trae"), U(170));
        _list.Columns.Add(L.T("Firma"), U(90));
        _list.Columns.Add(L.T("Estado"), U(110));
        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        layout.Controls.Add(_list);

        var actions = Row();
        _toggle = Action(L.T("Apagar"), async (_, _) => await ToggleAsync());
        _settings = Action(L.T("Ajustes…"), (_, _) => EditSettings());
        _log = Action(L.T("Registro"), (_, _) => OpenLog());
        _remove = Action(L.T("Desinstalar…"), async (_, _) => await RemoveAsync());
        _remove.ForeColor = T.Danger;
        actions.Controls.AddRange([_toggle, _settings, _log, _remove]);
        layout.Controls.Add(actions);
        _detail = Caption("", Small, T.Secondary, 640);
        layout.Controls.Add(_detail);
        _problems = Caption("", Small, T.Danger, 640);
        layout.Controls.Add(_problems);

        layout.Controls.Add(Caption(L.T("Ranuras"), Fonts.Make("Segoe UI Variable Text Semibold", 10.5f, "Segoe UI Semibold"), top: 12));
        layout.Controls.Add(Caption(L.T("Una extensión en una ranura apaga lo que trae Claupet en ella, para que no choquen."), Small, T.Secondary));
        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, U(4), 0, 0) };
        foreach (var slot in ExtensionSlots.Exclusive)
        {
            grid.Controls.Add(new Label
            {
                Text = ExtensionSlots.Label(slot), AutoSize = true, ForeColor = T.Secondary, Anchor = AnchorStyles.Left,
                Margin = new Padding(0, U(6), U(12), U(6)),
            });
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = U(360), FlatStyle = FlatStyle.Flat, BackColor = T.Raised, ForeColor = T.Text };
            var chosen = slot;
            combo.SelectedIndexChanged += async (_, _) => await PickProviderAsync(chosen, combo);
            _slots[slot] = combo;
            grid.Controls.Add(combo);
        }
        layout.Controls.Add(grid);

        layout.Controls.Add(Caption(L.T("API local"), Fonts.Make("Segoe UI Variable Text Semibold", 10.5f, "Segoe UI Semibold"), top: 12));
        layout.Controls.Add(Caption(L.T("Otros programas de este equipo le piden cosas a Claupet por HTTP (127.0.0.1), con un token. Las páginas web no pueden usarla."), Small, T.Secondary));
        _api = new CheckBox { Text = L.T("Encender la API local"), AutoSize = true, ForeColor = T.Text, Margin = new Padding(0, U(4), 0, U(2)) };
        _api.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _host.SetLocalApi(_api.Checked);
            Reload();
        };
        layout.Controls.Add(_api);
        _apiStatus = Caption("", Small, T.Tertiary);
        layout.Controls.Add(_apiStatus);
        _tokens = new ListBox { Width = U(640), Height = U(80), BackColor = T.Raised, ForeColor = T.Text, BorderStyle = BorderStyle.FixedSingle };
        layout.Controls.Add(_tokens);
        var tokenActions = Row();
        tokenActions.Controls.Add(Action(L.T("Nuevo token…"), (_, _) => NewToken()));
        tokenActions.Controls.Add(Action(L.T("Revocar"), (_, _) => RevokeToken()));
        layout.Controls.Add(tokenActions);

        Reload();
    }

    string? SelectedId => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as string : null;

    /// <summary>Vuelve a leer todo (otra parte de la app cambió algo).</summary>
    public void Reload()
    {
        if (IsDisposed) return;
        _loading = true;
        try
        {
            var selected = SelectedId;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var ext in _manager.Installed)
            {
                var state = _manager.StateOf(ext.Id);
                var status = !state.Enabled ? state.DisabledReason is null ? L.T("Apagada") : L.T("Apagada (falló)")
                    : _manager.IsRunning(ext.Id) ? L.T("Corriendo") : L.T("Encendida");
                var item = new ListViewItem([ext.Manifest.Name.Text, ext.Manifest.Version.ToString(),
                    string.Join(", ", ext.Manifest.Provides.Select(ExtensionSlots.Label)), TrustLabel(ext.Record.Trust), status]) { Tag = ext.Id };
                if (!state.Enabled) item.ForeColor = T.Tertiary;
                _list.Items.Add(item);
                if (ext.Id == selected) item.Selected = true;
            }
            _list.EndUpdate();
            if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0) _list.Items[0].Selected = true;
            ShowSelected();

            var problems = _manager.Broken.Select(b => L.T("«{0}» no se puede leer: {1}", b.Id, b.Problem))
                .Concat(_manager.CommandConflicts().Select(c => L.T("El comando /{0} está en «{1}» y en «{2}»: lo atiende la primera.", c.Command,
                    _manager.Find(c.Winner)?.Manifest.Name.Text ?? c.Winner, _manager.Find(c.Loser)?.Manifest.Name.Text ?? c.Loser)));
            _problems.Text = string.Join(Environment.NewLine, problems);

            foreach (var (slot, combo) in _slots)
            {
                combo.Items.Clear();
                combo.Items.Add(new Choice(null, L.T("Integrado: {0}", ExtensionSlots.BuiltIn(slot))));
                foreach (var ext in _manager.Installed.Where(e => e.Manifest.Provides.Contains(slot) && _manager.IsEnabled(e.Id)))
                    combo.Items.Add(new Choice(ext.Id, ext.Manifest.Name.Text));
                var current = _manager.ProviderFor(slot)?.Id;
                combo.SelectedItem = combo.Items.Cast<Choice>().FirstOrDefault(c => c.Id == current) ?? combo.Items[0];
            }

            _api.Checked = _host.LocalApiEnabled;
            _apiStatus.Text = _host.LocalApiStatus;
            _tokens.Items.Clear();
            foreach (var token in _host.ApiTokens)
                _tokens.Items.Add(new Choice(token.Id, $"{token.Name} · {string.Join(", ", token.Permissions)} · {token.Created.ToLocalTime():d}"));
        }
        finally { _loading = false; }
    }

    sealed record Choice(string? Id, string Label)
    {
        public override string ToString() => Label;
    }

    static string TrustLabel(PackageTrust trust) => trust switch
    {
        PackageTrust.Official => L.T("Oficial"),
        PackageTrust.Trusted or PackageTrust.NewPublisher => L.T("Firmada"),
        PackageTrust.Unsigned => L.T("Sin firma"),
        _ => trust.ToString(),
    };

    void ShowSelected()
    {
        var ext = SelectedId is { } id ? _manager.Find(id) : null;
        _toggle.Enabled = _remove.Enabled = _log.Enabled = ext is not null;
        _settings.Enabled = ext?.Manifest.Settings.Count > 0;
        if (ext is null)
        {
            _detail.Text = _manager.Installed.Count == 0 ? L.T("No hay extensiones instaladas. Suelte un .claupet aquí o sobre la mascota para instalarlo.") : "";
            return;
        }
        var m = ext.Manifest;
        var state = _manager.StateOf(ext.Id);
        _toggle.Text = state.Enabled ? L.T("Apagar") : L.T("Encender");
        var lines = new List<string> { m.Description.Text };
        if (m.Author is not null) lines.Add(L.T("De {0}", m.Author) + (ext.Record.Signer is { } signer ? " · " + L.T("firmada por {0}", signer) : ""));
        if (state.DisabledReason is { } reason) lines.Add(L.T("Se apagó: {0}", reason));
        if (m.Permissions.Count > 0) lines.Add(L.T("Permisos: {0}", string.Join("; ", m.Permissions.Select(ExtensionPermissions.Label))));
        if (m.Commands.Count > 0) lines.Add(L.T("Comandos: {0}", string.Join(", ", m.Commands.Select(c => "/" + c.Name))));
        if (m.Events.Count > 0) lines.Add(L.T("Escucha: {0}", string.Join(", ", m.Events.Select(InstallReviewForm.EventLabel))));
        if (m.Homepage is not null) lines.Add(m.Homepage);
        _detail.Text = string.Join(Environment.NewLine, lines.Where(l => l.Length > 0));
    }

    async Task PickAsync()
    {
        using var dialog = new OpenFileDialog { Filter = L.T("Paquetes de Claupet") + " (*.claupet)|*.claupet", Title = L.T("Instalar una extensión") };
        if (dialog.ShowDialog(this) == DialogResult.OK) await _host.InstallPackageAsync(dialog.FileName, this);
        Reload();
    }

    async Task ToggleAsync()
    {
        if (SelectedId is not { } id) return;
        await _manager.SetEnabledAsync(id, !_manager.IsEnabled(id));
        Reload();
    }

    async Task PickProviderAsync(string slot, ComboBox combo)
    {
        if (_loading || combo.SelectedItem is not Choice choice || choice.Id == _manager.ProviderFor(slot)?.Id) return;
        await _manager.SetProviderAsync(slot, choice.Id);
        Reload();
    }

    void EditSettings()
    {
        if (SelectedId is not { } id || _manager.Find(id) is not { } ext) return;
        using var dialog = new ExtensionSettingsForm(_manager, ext, T);
        dialog.ShowDialog(this);
    }

    void OpenLog()
    {
        if (SelectedId is not { } id || _manager.Find(id) is not { } ext) return;
        var log = new ExtensionLog(ext.DataFolder).FilePath;
        if (File.Exists(log)) Open(log);
        else MessageBox.Show(this, L.T("Todavía no escribió nada en su registro."), "Claupet", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    async Task RemoveAsync()
    {
        if (SelectedId is not { } id || _manager.Find(id) is not { } ext) return;
        var answer = MessageBox.Show(this, L.T("¿Desinstalar «{0}»? Sus datos y claves también se borran (No: se desinstala y se guardan sus datos).", ext.Manifest.Name.Text),
            "Claupet", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) return;
        try { await _manager.UninstallAsync(id, keepData: answer == DialogResult.No); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, L.T("No se pudo desinstalar: {0}", ex.Message), "Claupet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Reload();
    }

    void NewToken()
    {
        using var dialog = new NewTokenForm(T);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var token = _host.CreateApiToken(dialog.TokenName, dialog.Permissions);
        Reload();
        using var shown = new TokenShownForm(token, T);
        shown.ShowDialog(this);
    }

    void RevokeToken()
    {
        if (_tokens.SelectedItem is not Choice { Id: { } id } choice) return;
        if (MessageBox.Show(this, L.T("¿Revocar «{0}»? Los programas que lo usan dejan de poder pedirle cosas a Claupet.", choice.Label.Split(" · ")[0]),
                "Claupet", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _host.RevokeApiToken(id);
        Reload();
    }

    static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }
}

/// <summary>
/// Antes de instalar: qué es, de quién (la firma y lo que significa), qué permisos pide (los nuevos, si
/// es una actualización), qué escucha y, si trae una ranura exclusiva, si se usa ya (apagando el integrado).
/// Una firma inválida, revocada o de otra clave no se instala: solo se explica.
/// </summary>
sealed class InstallReviewForm : ExtensionDialog
{
    readonly Dictionary<string, CheckBox> _slotChecks = new();
    readonly CheckBox? _pet;

    public IReadOnlyCollection<string> UseForSlots => _slotChecks.Where(p => p.Value.Checked).Select(p => p.Key).ToList();

    public bool UseAsPet => _pet?.Checked == true;

    public static string EventLabel(string name) => name switch
    {
        "notice" => L.T("los avisos"),
        "toolUse" => L.T("las herramientas que usan sus sesiones"),
        "session" => L.T("el estado de sus sesiones"),
        "drop" => L.T("los archivos que suelte en la mascota o la isla"),
        "chat" => L.T("lo que se dice en el chat"),
        _ => name,
    };

    public InstallReviewForm(InstallReview review, IReadOnlyDictionary<string, string?> occupied, Theme theme)
        : base(theme, L.T("Instalar una extensión"))
    {
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(0, 0, U(22), U(22));
        var m = review.Manifest;
        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Location = new Point(U(22), U(22)) };
        Controls.Add(layout);

        layout.Controls.Add(Caption($"{m.Name.Text} {m.Version}", Fonts.Make("Georgia", 14f, "Cambria")));
        if (m.Author is not null) layout.Controls.Add(Caption(L.T("De {0}", m.Author), Small, T.Secondary));
        if (m.Description.Text.Length > 0) layout.Controls.Add(Caption(m.Description.Text, Small, T.Secondary));
        if (review.IsUpdate) layout.Controls.Add(Caption(L.T("Actualiza la versión {0} instalada.", review.Existing!.Manifest.Version), Small, T.Secondary, top: 4));
        if (review.IsDowngrade) layout.Controls.Add(Caption(L.T("Es más vieja que la instalada ({0}): vuelve a una versión anterior.", review.Existing!.Manifest.Version), Small, T.Warn, top: 4));
        if (review.IsReinstall) layout.Controls.Add(Caption(L.T("Ya está instalada esta versión: se vuelve a instalar."), Small, T.Secondary, top: 4));

        var trustColor = review.Trust switch
        {
            PackageTrust.Official or PackageTrust.Trusted => T.Success,
            PackageTrust.NewPublisher or PackageTrust.Unsigned => T.Warn,
            _ => T.Danger,
        };
        layout.Controls.Add(Caption(review.TrustText, null, trustColor, top: 10));
        if (m.RunsCode)
            layout.Controls.Add(Caption(L.T("Corre un programa en este equipo, con sus permisos de Windows. Claupet controla lo que le pide a la app, no lo que el programa haga por su cuenta."), Small, T.Secondary));

        var permissions = review.Existing is null ? m.Permissions : review.NewPermissions;
        if (permissions.Count > 0)
        {
            layout.Controls.Add(Caption(review.Existing is null ? L.T("Pide permiso para:") : L.T("Ahora también pide permiso para:"), top: 10));
            foreach (var p in permissions) layout.Controls.Add(Caption("• " + ExtensionPermissions.Label(p), Small, T.Text));
        }
        if (m.Events.Count > 0)
            layout.Controls.Add(Caption(L.T("Se entera de: {0}.", string.Join(", ", m.Events.Select(EventLabel))), Small, T.Text, top: 6));
        if (m.Commands.Count > 0)
            layout.Controls.Add(Caption(L.T("Comandos en el chat: {0}", string.Join(", ", m.Commands.Select(c => $"/{c.Name}"))), Small, T.Text, top: 6));

        foreach (var slot in m.Provides.Where(ExtensionSlots.IsExclusive))
        {
            var current = occupied.TryGetValue(slot, out var name) && name is not null ? name : ExtensionSlots.BuiltIn(slot);
            var check = new CheckBox
            {
                Text = L.T("Usarla para «{0}» (deja de usarse {1})", ExtensionSlots.Label(slot), current), AutoSize = true, ForeColor = T.Text,
                Checked = review.Existing is null || review.Existing.Manifest.Provides.Contains(slot) && occupied.GetValueOrDefault(slot) == review.Existing.Manifest.Name.Text,
                Margin = new Padding(0, U(6), 0, 0), MaximumSize = new Size(U(560), 0),
            };
            _slotChecks[slot] = check;
            layout.Controls.Add(check);
        }
        if (m.Pet is not null)
        {
            _pet = new CheckBox { Text = L.T("Usar «{0}» como la mascota del escritorio", m.Pet.Name.Text), AutoSize = true, ForeColor = T.Text, Checked = true, Margin = new Padding(0, U(6), 0, 0) };
            layout.Controls.Add(_pet);
        }

        var buttons = Row();
        buttons.Margin = new Padding(0, U(16), 0, 0);
        if (review.Allowed)
        {
            var install = Action(review.IsUpdate ? L.T("Actualizar") : L.T("Instalar"), (_, _) => { DialogResult = DialogResult.OK; Close(); }, primary: true);
            buttons.Controls.Add(install);
            AcceptButton = install;
        }
        var cancel = Action(review.Allowed ? L.T("Cancelar") : L.T("Cerrar"), (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        buttons.Controls.Add(cancel);
        CancelButton = cancel;
        layout.Controls.Add(buttons);
    }
}

/// <summary>Los ajustes de una extensión, según su manifiesto. Las claves (secret) se guardan cifradas y no se muestran.</summary>
sealed class ExtensionSettingsForm : ExtensionDialog
{
    readonly ExtensionManager _manager;
    readonly InstalledExtension _ext;
    readonly Dictionary<string, Func<string?>> _read = new();

    public ExtensionSettingsForm(ExtensionManager manager, InstalledExtension ext, Theme theme) : base(theme, L.T("Ajustes de «{0}»", ext.Manifest.Name.Text))
    {
        _manager = manager;
        _ext = ext;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(0, 0, U(22), U(22));
        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Location = new Point(U(22), U(22)) };
        Controls.Add(layout);
        layout.Controls.Add(Caption(ext.Manifest.Name.Text, Fonts.Make("Georgia", 14f, "Cambria")));
        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, U(10), 0, 0) };
        foreach (var spec in ext.Manifest.Settings)
        {
            grid.Controls.Add(new Label
            {
                Text = spec.Label.Text, AutoSize = true, ForeColor = T.Secondary, Anchor = AnchorStyles.Left, MaximumSize = new Size(U(220), 0),
                Margin = new Padding(0, U(6), U(12), U(6)),
            });
            var value = manager.ValueOf(ext, spec.Key);
            Control field;
            switch (spec.Type)
            {
                case SettingSpec.Bool:
                    var check = new CheckBox { Checked = value == "true", AutoSize = true };
                    _read[spec.Key] = () => check.Checked ? "true" : "false";
                    field = check;
                    break;
                case SettingSpec.Choice:
                    var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = U(300), FlatStyle = FlatStyle.Flat, BackColor = T.Raised, ForeColor = T.Text };
                    combo.Items.AddRange(spec.Choices.Cast<object>().ToArray());
                    combo.SelectedItem = value is not null && spec.Choices.Contains(value) ? value : spec.Choices.FirstOrDefault();
                    _read[spec.Key] = () => combo.SelectedItem as string;
                    field = combo;
                    break;
                case SettingSpec.Secret:
                    // La clave guardada no se muestra: vacío la deja como está; «Borrar» la quita.
                    var secret = new TextBox { Width = U(300), UseSystemPasswordChar = true, BackColor = T.Raised, ForeColor = T.Text, BorderStyle = BorderStyle.FixedSingle,
                        PlaceholderText = value is { Length: > 0 } ? L.T("(guardada; escriba otra para cambiarla)") : L.T("(sin guardar)") };
                    var clear = false;
                    var row = Row();
                    row.Controls.Add(secret);
                    if (value is { Length: > 0 })
                        row.Controls.Add(Action(L.T("Borrar"), (_, _) => { clear = true; secret.Text = ""; secret.PlaceholderText = L.T("(se borrará)"); }));
                    _read[spec.Key] = () => secret.Text.Length > 0 ? secret.Text : clear ? null : value;
                    field = row;
                    break;
                default:
                    var text = new TextBox { Width = U(300), Text = value ?? "", BackColor = T.Raised, ForeColor = T.Text, BorderStyle = BorderStyle.FixedSingle };
                    _read[spec.Key] = () => text.Text.Trim().Length > 0 ? text.Text.Trim() : null;
                    field = text;
                    break;
            }
            field.Margin = new Padding(0, U(4), 0, U(4));
            grid.Controls.Add(field);
        }
        layout.Controls.Add(grid);
        var buttons = Row();
        buttons.Margin = new Padding(0, U(14), 0, 0);
        var save = Action(L.T("Guardar"), async (_, _) => await SaveAsync(), primary: true);
        var cancel = Action(L.T("Cancelar"), (_, _) => Close());
        buttons.Controls.AddRange([save, cancel]);
        AcceptButton = save;
        CancelButton = cancel;
        layout.Controls.Add(buttons);
    }

    async Task SaveAsync()
    {
        foreach (var spec in _ext.Manifest.Settings)
        {
            var value = _read[spec.Key]();
            if (spec.Type == SettingSpec.Number && value is not null)
            {
                // Se escribe con la coma o el punto de la persona; se guarda con punto.
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var number)
                    && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                {
                    MessageBox.Show(this, L.T("Valor inválido para «{0}».", spec.Label.Text), "Claupet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                value = number.ToString(CultureInfo.InvariantCulture);
            }
            if (value == _manager.ValueOf(_ext, spec.Key)) continue;
            try { await _manager.SetValueAsync(_ext, spec.Key, value); }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, ex.Message, "Claupet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>Un token nuevo de la API local: para qué programa es y qué puede pedir.</summary>
sealed class NewTokenForm : ExtensionDialog
{
    static readonly string[] Offered =
    [
        ExtensionPermissions.Pet, ExtensionPermissions.Notices, ExtensionPermissions.Island, ExtensionPermissions.Voice,
        ExtensionPermissions.Claude, ExtensionPermissions.Sessions, ExtensionPermissions.Clipboard,
    ];

    readonly TextBox _name;
    readonly Dictionary<string, CheckBox> _checks = new();

    public string TokenName => _name.Text.Trim();
    public IReadOnlyList<string> Permissions => _checks.Where(c => c.Value.Checked).Select(c => c.Key).ToList();

    public NewTokenForm(Theme theme) : base(theme, L.T("Nuevo token de la API local"))
    {
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(0, 0, U(22), U(22));
        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Location = new Point(U(22), U(22)) };
        Controls.Add(layout);
        layout.Controls.Add(Caption(L.T("¿Para qué programa es?"), top: 0));
        _name = new TextBox { Width = U(320), BackColor = T.Raised, ForeColor = T.Text, BorderStyle = BorderStyle.FixedSingle, MaxLength = 60 };
        layout.Controls.Add(_name);
        layout.Controls.Add(Caption(L.T("Lo que puede pedir:"), top: 10));
        foreach (var p in Offered)
        {
            var check = new CheckBox { Text = ExtensionPermissions.Label(p), AutoSize = true, ForeColor = T.Text, Checked = p is ExtensionPermissions.Pet or ExtensionPermissions.Notices };
            _checks[p] = check;
            layout.Controls.Add(check);
        }
        var buttons = Row();
        buttons.Margin = new Padding(0, U(14), 0, 0);
        var create = Action(L.T("Crear"), (_, _) =>
        {
            if (TokenName.Length == 0) { _name.Focus(); return; }
            DialogResult = DialogResult.OK;
            Close();
        }, primary: true);
        var cancel = Action(L.T("Cancelar"), (_, _) => Close());
        buttons.Controls.AddRange([create, cancel]);
        AcceptButton = create;
        CancelButton = cancel;
        layout.Controls.Add(buttons);
    }
}

/// <summary>El token recién creado, una sola vez: Claupet guarda solo su huella y no lo puede volver a mostrar.</summary>
sealed class TokenShownForm : ExtensionDialog
{
    public TokenShownForm(string token, Theme theme) : base(theme, L.T("Token creado"))
    {
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(0, 0, U(22), U(22));
        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Location = new Point(U(22), U(22)) };
        Controls.Add(layout);
        layout.Controls.Add(Caption(L.T("Cópielo ahora: no se vuelve a mostrar. Va en «Authorization: Bearer <token>»."), width: 460));
        var box = new TextBox { Text = token, ReadOnly = true, Width = U(460), BackColor = T.Raised, ForeColor = T.Text, BorderStyle = BorderStyle.FixedSingle };
        layout.Controls.Add(box);
        var buttons = Row();
        buttons.Margin = new Padding(0, U(14), 0, 0);
        buttons.Controls.Add(Action(L.T("Copiar"), (_, _) => Clipboard.SetText(token), primary: true));
        var close = Action(L.T("Cerrar"), (_, _) => Close());
        buttons.Controls.Add(close);
        CancelButton = close;
        layout.Controls.Add(buttons);
    }
}
