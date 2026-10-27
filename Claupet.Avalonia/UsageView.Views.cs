using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SD = System.Drawing;

namespace Claupet;

/// <summary>Las pestañas de las vistas y la vista «Ajustes» (igual que en Windows).</summary>
sealed partial class UsageView
{
    /// <summary>Las pestañas de arriba, en orden: las tres caben, así que no hay que elegir.</summary>
    static readonly View[] Tabs = [View.Usage, View.Sessions, View.Activity];

    /// <summary>
    /// Lo que muestra «Ajustes»; lo manda TrayController cada vez que cambia algo. Speak y Behavior son
    /// null si la mascota no está en el escritorio.
    /// </summary>
    public sealed record SettingsState(bool Notifications, bool Sound, string SoundPack, string Palette,
        string Mascot, bool DesktopMascot, bool? Speak, MascotBehavior? Behavior, bool StartWithSystem, int Accounts, string Language);

    /// <summary>Un interruptor por comportamiento de la mascota del escritorio.</summary>
    public sealed record MascotBehavior(bool FlowGestures, bool DanceToMusic, bool NoticeGestures, bool GreetOnAppear,
        bool AngryOnClicks, bool SleepWithoutSessions);

    View _beforeSettings = View.Usage;
    SettingsState _settingsState = new(true, true, "", "", "", false, null, null, false, 0, "");
    string _soundPackId = "", _paletteId = "", _mascotId = "";

    static string ViewLabel(View view) => view switch
    {
        View.Usage => L.T("Uso"),
        View.Sessions => L.T("Sesiones"),
        View.Activity => L.T("Actividad"),
        _ => L.T("Ajustes"),
    };

    /// <summary>Los Id marcan la opción elegida en cada lista.</summary>
    public void SetSettingsState(SettingsState state, string soundPackId, string paletteId, string mascotId)
    {
        _settingsState = state;
        _soundPackId = soundPackId;
        _paletteId = paletteId;
        _mascotId = mascotId;
        if (_view == View.Settings) InvalidateVisual();
    }

    void OpenView(View view)
    {
        _view = view;
        Relayout();
    }

    // ── Vista «Ajustes» ─────────────────────────────────────────────────────────────────────

    double ComposeSettingsHeader(DrawingContext? dc, double y)
    {
        var height = Gfx.Title.LineHeight;
        var close = new Rect(ViewWidth - Pad - 24, y + (height - 30) / 2, 30, 30);
        AddHit("settings-close", close, HitKind.CloseSettings);
        if (dc is not null)
        {
            Gfx.Write(dc, L.T("Ajustes"), Gfx.Title, _t.Text, new Rect(Pad, y, Inner - 40, height));
            if (_hoverKey == "settings-close") Gfx.Fill(dc, _t.Hover, close, 8);
            Gfx.Icon(dc, Glyph.Close, _t.Secondary, close, 13);
        }
        return y + height + 16;
    }

    double ComposeSettings(DrawingContext? dc, double y)
    {
        var s = _settingsState;
        y = SettingsCaption(dc, L.T("Avisos"), y);
        y = SwitchRow(dc, L.T("Notificaciones"), "notify", s.Notifications, y);
        y = SwitchRow(dc, L.T("Sonido"), "sound", s.Sound, y);
        y = ChoiceRow(dc, L.T("Estilo de sonido"), "pack", s.SoundPack, y);
        y = LinksRow(dc, y, (L.T("Ver un aviso de ejemplo"), "sample"), (L.T("Abrir carpeta de sonidos"), "sounds"));

        y = SettingsCaption(dc, L.T("Apariencia"), y + 10);
        y = ChoiceRow(dc, L.T("Color"), "palette", s.Palette, y, swatch: _t.Accent);
        y = ChoiceRow(dc, L.T("Mascota"), "mascot", s.Mascot, y);
        y = SwitchRow(dc, L.T("Mascota en el escritorio"), "desk", s.DesktopMascot, y);
        if (s.Behavior is { } b)
        {
            y = SwitchRow(dc, L.T("Gestos del flujo (lo que hace la sesión)"), "flowgest", b.FlowGestures, y);
            // La música sale del control multimedia de Windows; en Linux y macOS todavía no se detecta.
            if (OperatingSystem.IsWindows()) y = SwitchRow(dc, L.T("Bailar con música"), "music", b.DanceToMusic, y);
            y = SwitchRow(dc, L.T("Gesto de los avisos"), "noticegest", b.NoticeGestures, y);
            y = SwitchRow(dc, L.T("Saludar al aparecer"), "greet", b.GreetOnAppear, y);
            y = SwitchRow(dc, L.T("Enojarse con muchos clics"), "angry", b.AngryOnClicks, y);
            y = SwitchRow(dc, L.T("Dormir sin sesiones"), "sleep", b.SleepWithoutSessions, y);
        }
        if (s.Speak is { } speak) y = SwitchRow(dc, L.T("Hablar (los avisos en una nubecita)"), "speak", speak, y);

        y = SettingsCaption(dc, L.T("Cuentas"), y + 10);
        y = LinkRow(dc, s.Accounts == 1 ? L.T("1 cuenta") : L.T("{0} cuentas", s.Accounts), L.T("Agregar cuenta…"), "add", _t.Accent, y);

        y = SettingsCaption(dc, L.T("Programa"), y + 10);
        y = ChoiceRow(dc, L.T("Idioma"), "language", s.Language, y);
        y = SwitchRow(dc, L.T("Iniciar con el sistema"), "startup", s.StartWithSystem, y);
        y = LinkRow(dc, L.T("Avisos de permisos (hook)"), L.T("Configurar…"), "setup", _t.Accent, y);
        y = LinkRow(dc, L.T("Si algo no funciona"), L.T("Copiar diagnóstico"), "diag", _t.Accent, y);
        y = LinkRow(dc, L.T("Quitar hook e inicio con el sistema"), L.T("Quitar…"), "unsetup", _t.Danger, y);
        return y + 14;
    }

    double SettingsCaption(DrawingContext? dc, string text, double y)
    {
        Gfx.Write(dc, text.ToUpperInvariant(), Gfx.Small, _t.Tertiary, new Rect(Pad, y, Inner, Gfx.Small.LineHeight));
        return y + Gfx.Small.LineHeight + 6;
    }

    double SwitchRow(DrawingContext? dc, string label, string key, bool on, double y)
    {
        const double height = 32;
        var row = new Rect(Pad - 8, y, Inner + 16, height);
        AddHit("set:" + key, row, HitKind.Setting);
        if (dc is not null)
        {
            if (_hoverKey == "set:" + key) Gfx.Fill(dc, _t.Hover, row, 8);
            Gfx.Write(dc, label, Gfx.Body, _t.Text, new Rect(Pad, y, Inner - 50, height), center: true);
            var track = new Rect(Pad + Inner - 34, y + (height - 18) / 2, 34, 18);
            Gfx.Fill(dc, on ? _t.Accent : _t.Track, track, 9);
            const double knob = 12;
            var kx = on ? track.Right - 3 - knob : track.X + 3;
            dc.DrawEllipse(on ? Brushes.White : Gfx.B(_t.Secondary), null, new Rect(kx, track.Y + (track.Height - knob) / 2, knob, knob));
        }
        return y + height;
    }

    /// <summary>Una opción con lista; swatch pone un punto de ese color antes del valor (la paleta).</summary>
    double ChoiceRow(DrawingContext? dc, string label, string key, string value, double y, SD.Color? swatch = null)
    {
        const double height = 32;
        var row = new Rect(Pad - 8, y, Inner + 16, height);
        AddHit("set:" + key, row, HitKind.Setting);
        if (dc is not null)
        {
            if (_hoverKey == "set:" + key) Gfx.Fill(dc, _t.Hover, row, 8);
            Gfx.Write(dc, label, Gfx.Body, _t.Text, new Rect(Pad, y, Inner / 2, height), center: true);
            var valueWidth = Gfx.Measure(value, Gfx.Body).Width;
            var valueX = Pad + Inner - 18 - valueWidth;
            Gfx.Write(dc, value, Gfx.Body, _t.Secondary, new Rect(valueX, y, valueWidth + 2, height), center: true);
            if (swatch is { } color)
                dc.DrawEllipse(Gfx.B(color), null, new Rect(valueX - 8 - 12, y + (height - 12) / 2, 12, 12));
            Gfx.Icon(dc, Glyph.ChevronDown, _t.Secondary, new Rect(Pad + Inner - 14, y, 14, height), 12);
        }
        return y + height;
    }

    double LinkRow(DrawingContext? dc, string label, string link, string key, SD.Color color, double y)
    {
        const double height = 32;
        var linkWidth = Gfx.Measure(link, Gfx.Body).Width;
        var target = new Rect(Pad + Inner - linkWidth - 8, y + 2, linkWidth + 16, height - 4);
        AddHit("set:" + key, target, HitKind.Setting);
        if (dc is not null)
        {
            Gfx.Write(dc, label, Gfx.Body, _t.Secondary, new Rect(Pad, y, Inner - linkWidth - 16, height), center: true);
            if (_hoverKey == "set:" + key) Gfx.Fill(dc, _t.Hover, target, 6);
            Gfx.Write(dc, link, Gfx.Body, color, new Rect(target.X + 8, y, linkWidth + 2, height), center: true);
        }
        return y + height;
    }

    double LinksRow(DrawingContext? dc, double y, params (string Text, string Key)[] links)
    {
        const double height = 32;
        var x = Pad - 8;
        foreach (var (text, key) in links)
        {
            var width = Gfx.Measure(text, Gfx.Small).Width + 16;
            var target = new Rect(x, y + 3, width, height - 6);
            AddHit("set:" + key, target, HitKind.Setting);
            if (dc is not null)
            {
                if (_hoverKey == "set:" + key) Gfx.Fill(dc, _t.Hover, target, 6);
                Gfx.Write(dc, text, Gfx.Small, _t.Accent, new Rect(target.X + 8, y, width - 14, height), center: true);
            }
            x += width + 4;
        }
        return y + height;
    }

    /// <summary>La lista de la opción de Ajustes con esa clave (pack, palette, mascot); elegir manda «clave:id».</summary>
    void ShowChoiceMenu(string key)
    {
        IEnumerable<(string Id, string Label)> choices = key switch
        {
            "palette" => Claupet.Theme.Palettes.Select(p => (p.Id, p.Label)),
            "mascot" => Mascots.Choices,
            "language" => L.Choices,
            _ => Sounds.Packs,
        };
        var selected = key switch { "palette" => _paletteId, "mascot" => _mascotId, "language" => L.Language, _ => _soundPackId };
        var menu = new ContextMenu();
        foreach (var (id, label) in choices)
        {
            var item = new MenuItem { Header = (id == selected ? "✓  " : "     ") + label };
            if (key == "palette") // cada paleta con un punto de su color en el modo actual
            {
                var palette = Claupet.Theme.Palettes.First(p => p.Id == id);
                item.Icon = new Avalonia.Controls.Shapes.Ellipse { Width = 12, Height = 12, Fill = Gfx.B(_t.Dark ? palette.Dark : palette.Light) };
            }
            item.Click += (_, _) => SettingRequested?.Invoke(key + ":" + id);
            menu.Items.Add(item);
        }
        menu.Open(this);
    }
}
