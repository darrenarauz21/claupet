namespace Claupet;

/// <summary>Las pestañas de las vistas y la vista «Ajustes».</summary>
sealed partial class UsageForm
{
    const string GlyphSettings = "", GlyphChevronRight = "", GlyphBack = "";

    /// <summary>Las pestañas de arriba, en orden.</summary>
    static readonly View[] Tabs = [View.Usage, View.Activity, View.History];

    /// <summary>Las pestañas de Ajustes, en orden.</summary>
    public enum SettingsTab { Avisos, Apariencia, Asistente, Isla, Sistema }

    /// <summary>Se cambió de pestaña en Ajustes (para recordarla).</summary>
    public event Action<SettingsTab>? SettingsTabChanged;

    View _beforeSettings = View.Usage;
    SettingsState _settingsState = new();
    /// <summary>Las secciones de Ajustes armadas con el estado (las mismas de Claupet Console).</summary>
    IReadOnlyList<SettingsPage> _settingsPages = SettingsPages.Build(new());
    SettingsTab _settingsTab;
    /// <summary>Desde qué pestaña se desliza el indicador y cuándo empezó.</summary>
    SettingsTab _tabFrom;
    long _tabAt;

    static string ViewLabel(View view) => view switch
    {
        View.Usage => L.T("Uso"),
        View.Activity => L.T("Actividad"),
        View.History => L.T("Historial"),
        _ => L.T("Ajustes"),
    };

    public void SetSettingsState(SettingsState state)
    {
        var old = _settingsState;
        _settingsState = state;
        _settingsPages = SettingsPages.Build(state);
        if (_view != View.Settings) return;
        // Las filas que aparecen o se van (al encender el chat, la isla, el micrófono…) entran con una transición.
        if (old.Chat != state.Chat || old.Mic != state.Mic || old.Island != state.Island || old.DesktopMascot != state.DesktopMascot || old.DanceToMusic != state.DanceToMusic
            || old.ReadAloud != state.ReadAloud || old.SpeechStatus.Length > 0 != state.SpeechStatus.Length > 0)
        {
            BeginTransition(Slide.Fade);
            Relayout();
        }
        else Invalidate();
    }

    /// <summary>La pestaña de Ajustes con la que se abre (la última usada).</summary>
    public void SetSettingsTab(SettingsTab tab) => _settingsTab = _tabFrom = tab;

    void SelectSettingsTab(SettingsTab tab)
    {
        if (tab == _settingsTab) return;
        BeginTransition(tab > _settingsTab ? Slide.FromRight : Slide.FromLeft);
        _tabFrom = _settingsTab;
        _tabAt = AnimationsEnabled ? Environment.TickCount64 : 0;
        _settingsTab = tab;
        StartMotion();
        Relayout();
        SettingsTabChanged?.Invoke(tab);
    }

    void OpenView(View view)
    {
        if (view != _view) BeginTransition(SlideFor(_view, view));
        _view = view;
        Relayout();
    }

    // ── Vista «Ajustes» ─────────────────────────────────────────────────────────────────────

    int ComposeSettingsHeader(Graphics g, bool draw, int y)
    {
        var height = TextRenderer.MeasureText(g, L.T("Ajustes rápidos"), _title, Size.Empty, Line).Height;
        var close = new Rectangle(ClientSize.Width - Pad - S(24), y + (height - S(30)) / 2, S(30), S(30));
        AddHit("settings-close", close, HitKind.CloseSettings);
        if (draw)
        {
            Write(g, L.T("Ajustes rápidos"), _title, _t.Text, new Rectangle(Pad, y, Inner - S(40), height));
            if (_hoverKey == "settings-close") Draw.Fill(g, _t.Hover, close, S(8));
            Write(g, GlyphClose, _glyphSmall, _t.Secondary, close, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        return y + height + S(16);
    }

    static readonly SettingsTab[] AllTabs = Enum.GetValues<SettingsTab>();

    static string TabLabel(SettingsTab tab) => tab switch
    {
        SettingsTab.Avisos => L.T("Avisos"),
        SettingsTab.Apariencia => L.T("Apariencia"),
        SettingsTab.Asistente => L.T("Asistente"),
        SettingsTab.Isla => L.T("Isla"),
        _ => L.T("Sistema"),
    };

    Font? _smallStrong;
    Font SmallStrong => _smallStrong ??= Fonts.Make("Segoe UI Variable Text Semibold", 8.75f, "Segoe UI Semibold");

    /// <summary>
    /// Las pestañas: una barra con el indicador que se desliza bajo la elegida. Cada pestaña mide lo
    /// que su nombre más una parte igual del espacio que sobra.
    /// </summary>
    int ComposeSettingsTabs(Graphics g, bool draw, int y)
    {
        var bar = new Rectangle(Pad, y, Inner, S(32));
        var widths = AllTabs.Select(t => TextRenderer.MeasureText(g, TabLabel(t), SmallStrong, Size.Empty, Line).Width).ToArray();
        var extra = (bar.Width - S(8) - widths.Sum()) / (float)AllTabs.Length;
        var cells = new RectangleF[AllTabs.Length];
        var x = (float)bar.X + S(4);
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = new RectangleF(x, bar.Y + S(4), widths[i] + extra, bar.Height - S(8));
            x += cells[i].Width;
        }
        if (draw)
        {
            Draw.Fill(g, _t.Hover, bar, S(9));
            var k = (float)Smooth(Math.Clamp((Environment.TickCount64 - _tabAt) / (double)IndicatorMs, 0, 1));
            var a = cells[(int)_tabFrom];
            var b = cells[(int)_settingsTab];
            var pill = new RectangleF(a.X + (b.X - a.X) * k, b.Y, a.Width + (b.Width - a.Width) * k, b.Height);
            Draw.Fill(g, _t.Raised, pill, S(6));
            Draw.Outline(g, _t.Border, pill, S(6));
        }
        for (var i = 0; i < cells.Length; i++)
        {
            var tab = AllTabs[i];
            var cell = Rectangle.Round(cells[i]);
            AddHit("stab:" + tab, cell, HitKind.SettingsTab);
            if (!draw) continue;
            var selected = tab == _settingsTab;
            var color = selected || _hoverKey == "stab:" + tab ? _t.Text : _t.Secondary;
            Write(g, TabLabel(tab), selected ? SmallStrong : _small, color, cell, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        return bar.Bottom + S(12);
    }

    /// <summary>
    /// Todas las pestañas ocupan lo que la más alta (con lo que esté encendido): al cambiar de pestaña
    /// solo se desliza el contenido y el cuadro no cambia de tamaño. Las demás se miden sin dejar hits.
    /// </summary>
    int ComposeSettings(Graphics g, bool draw, int y)
    {
        y = ComposeSettingsTabs(g, draw, y);
        var bottom = ComposeSettingsTab(g, draw, _settingsTab, y);
        var hits = _hits.Count;
        foreach (var tab in AllTabs)
            if (tab != _settingsTab) bottom = Math.Max(bottom, ComposeSettingsTab(g, false, tab, y));
        _hits.RemoveRange(hits, _hits.Count - hits);
        // Aquí solo lo de todos los días; lo demás, en la ventana grande.
        return ConsoleCard(g, draw, bottom + S(16)) + S(14);
    }

    /// <summary>«Todos los ajustes»: la tarjeta que abre Claupet Console, con la cabeza del chibi.</summary>
    int ConsoleCard(Graphics g, bool draw, int y)
    {
        var card = new Rectangle(Pad, y, Inner, S(68));
        AddHit("set:console", card, HitKind.Setting);
        if (!draw) return card.Bottom;
        Draw.Fill(g, _t.Raised, card, S(14));
        Draw.Outline(g, _t.Border, card, S(14));
        var tile = new Rectangle(card.X + S(14), card.Y + (card.Height - S(40)) / 2, S(40), S(40));
        Draw.Fill(g, _t.Hover, tile, S(10));
        var unit = Math.Max(1, (tile.Width - S(8)) / 16);
        ToastArt.Pixels(g, tile.X + (tile.Width - 16 * unit) / 2, tile.Y + (tile.Height - 16 * unit) / 2, unit,
            fill => ChibiSprite.PaintHead(fill, MascotFrame.Open, small: true));
        var open = L.T("Abrir");
        var pillWidth = TextRenderer.MeasureText(g, open, _bodyStrong, Size.Empty, Line).Width + S(36);
        var pill = new Rectangle(card.Right - S(14) - pillWidth, card.Y + (card.Height - S(36)) / 2, pillWidth, S(36));
        var hover = _hoverKey == "set:console";
        Draw.Fill(g, hover ? Blend(_t.Accent, Color.White, 0.12) : _t.Accent, pill, S(18));
        Write(g, open, _bodyStrong, Color.FromArgb(0x14, 0x14, 0x13), pill, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        var textX = tile.Right + S(14);
        var textWidth = pill.X - S(12) - textX;
        var lines = _bodyStrong.Height + S(2) + _small.Height;
        var top = card.Y + (card.Height - lines) / 2;
        Write(g, L.T("Todos los ajustes"), _bodyStrong, _t.Text, new Rectangle(textX, top, textWidth, _bodyStrong.Height), TextFormatFlags.EndEllipsis);
        Write(g, L.T("En Claupet Console"), _small, _t.Tertiary, new Rectangle(textX, top + _bodyStrong.Height + S(2), textWidth, _small.Height), TextFormatFlags.EndEllipsis);
        return card.Bottom;
    }

    int ComposeSettingsTab(Graphics g, bool draw, SettingsTab tab, int y)
    {
        var first = true;
        foreach (var page in _settingsPages)
        {
            if (page.Tab != tab.ToString()) continue;
            foreach (var row in page.Rows)
            {
                if (row.Place == RowPlace.Console) continue;
                y = row.Kind switch
                {
                    RowKind.Caption => SettingsCaption(g, draw, row.Text, first ? y : y + S(8)),
                    RowKind.Switch => SwitchRow(g, draw, row.Text, row.Key, row.On, y),
                    RowKind.Choice => ChoiceRow(g, draw, row.Text, row.Key, row.Value, y, swatch: row.Swatch ? _t.Accent : null),
                    RowKind.Info => InfoRow(g, draw, row.Text, row.Value, y),
                    RowKind.Note => NoteRow(g, draw, row.Text, ToneColor(row.Tone), y),
                    RowKind.Link => LinkRow(g, draw, row.Text, row.Value, row.Key, ToneColor(row.Tone), y),
                    _ => LinksRow(g, draw, y, (row.Links ?? []).Select(l => (l.Text, l.Key)).ToArray()),
                };
                first = false;
            }
        }
        return y;
    }

    Color ToneColor(RowTone tone) => tone switch
    {
        RowTone.Muted => _t.Tertiary,
        RowTone.Warn => _t.Warn,
        RowTone.Accent => _t.Accent,
        RowTone.Danger => _t.Danger,
        _ => _t.Text,
    };

    int SettingsCaption(Graphics g, bool draw, string text, int y)
    {
        if (draw) Write(g, text.ToUpperInvariant(), _small, _t.Tertiary, new Rectangle(Pad, y, Inner, _small.Height));
        return y + _small.Height + S(6);
    }

    /// <summary>Un interruptor: la perilla se desliza y el color cambia al encenderlo o apagarlo.</summary>
    int SwitchRow(Graphics g, bool draw, string label, string key, bool on, int y)
    {
        var height = S(32);
        var row = new Rectangle(Pad - S(8), y, Inner + S(16), height);
        AddHit("set:" + key, row, HitKind.Setting);
        var k = SwitchPosition(key, on);
        if (draw)
        {
            if (_hoverKey == "set:" + key) Draw.Fill(g, _t.Hover, row, S(8));
            Write(g, label, _body, _t.Text, new Rectangle(Pad, y, Inner - S(50), height), TextFormatFlags.VerticalCenter);
            var track = new RectangleF(Pad + Inner - S(34), y + (height - S(18)) / 2f, S(34), S(18));
            Draw.Fill(g, Mix(_t.Track, _t.Accent, k), track, track.Height / 2);
            var knob = S(12);
            var kx = track.X + S(3) + (track.Width - S(6) - knob) * k;
            using var brush = new SolidBrush(Mix(_t.Secondary, Color.White, k));
            g.FillEllipse(brush, kx, track.Y + (track.Height - knob) / 2, knob, knob);
        }
        return y + height;
    }

    /// <summary>Una opción con lista; swatch pone un punto de ese color antes del valor (la paleta).</summary>
    int ChoiceRow(Graphics g, bool draw, string label, string key, string value, int y, Color? swatch = null)
    {
        var height = S(32);
        var row = new Rectangle(Pad - S(8), y, Inner + S(16), height);
        AddHit("set:" + key, row, HitKind.Setting);
        if (draw)
        {
            if (_hoverKey == "set:" + key) Draw.Fill(g, _t.Hover, row, S(8));
            var labelWidth = TextRenderer.MeasureText(g, label, _body, Size.Empty, Line).Width;
            Write(g, label, _body, _t.Text, new Rectangle(Pad, y, labelWidth, height), TextFormatFlags.VerticalCenter);
            var space = Inner - labelWidth - S(18) - S(14) - (swatch is null ? 0 : S(20));
            var valueWidth = Math.Max(0, Math.Min(space, TextRenderer.MeasureText(g, value, _body, Size.Empty, Line).Width));
            var valueX = Pad + Inner - S(18) - valueWidth;
            Write(g, value, _body, _t.Secondary, new Rectangle(valueX, y, valueWidth, height), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (swatch is { } color)
            {
                var dot = S(12);
                Draw.Fill(g, color, new RectangleF(valueX - S(8) - dot, y + (height - dot) / 2f, dot, dot), dot / 2f);
            }
            Write(g, GlyphChevronDown, _glyphSmall, _t.Secondary, new Rectangle(Pad + Inner - S(14), y, S(14), height),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        return y + height;
    }

    /// <summary>Una fila que solo informa: el nombre y su valor a la derecha, sin lista.</summary>
    int InfoRow(Graphics g, bool draw, string label, string value, int y)
    {
        var height = S(32);
        if (draw)
        {
            var labelWidth = TextRenderer.MeasureText(g, label, _body, Size.Empty, Line).Width;
            Write(g, label, _body, _t.Text, new Rectangle(Pad, y, labelWidth, height), TextFormatFlags.VerticalCenter);
            var valueWidth = Math.Max(0, Inner - labelWidth - S(14));
            Write(g, value, _body, _t.Secondary, new Rectangle(Pad + Inner - valueWidth, y, valueWidth, height),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis);
        }
        return y + height;
    }

    /// <summary>Un texto chico, en varias líneas si hace falta (una aclaración bajo una fila).</summary>
    int NoteRow(Graphics g, bool draw, string text, Color color, int y)
    {
        if (text.Length == 0) return y;
        const TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        var height = TextRenderer.MeasureText(g, text, _small, new Size(Inner, 0), flags).Height;
        if (draw) TextRenderer.DrawText(g, text, _small, new Rectangle(Pad, y, Inner, height), color, flags);
        return y + height + S(6);
    }

    int LinkRow(Graphics g, bool draw, string label, string link, string key, Color color, int y)
    {
        var height = S(32);
        var linkWidth = TextRenderer.MeasureText(g, link, _body, Size.Empty, Line).Width;
        var target = new Rectangle(Pad + Inner - linkWidth - S(8), y + S(2), linkWidth + S(16), height - S(4));
        AddHit("set:" + key, target, HitKind.Setting);
        if (draw)
        {
            Write(g, label, _body, _t.Secondary, new Rectangle(Pad, y, Inner - linkWidth - S(16), height), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (_hoverKey == "set:" + key) Draw.Fill(g, _t.Hover, target, S(6));
            Write(g, link, _body, color, new Rectangle(target.X + S(8), y, linkWidth, height), TextFormatFlags.VerticalCenter);
        }
        return y + height;
    }

    int LinksRow(Graphics g, bool draw, int y, params (string Text, string Key)[] links)
    {
        var height = S(32);
        var x = Pad - S(8);
        foreach (var (text, key) in links)
        {
            var width = TextRenderer.MeasureText(g, text, _small, Size.Empty, Line).Width + S(16);
            var target = new Rectangle(x, y + S(3), width, height - S(6));
            AddHit("set:" + key, target, HitKind.Setting);
            if (draw)
            {
                if (_hoverKey == "set:" + key) Draw.Fill(g, _t.Hover, target, S(6));
                Write(g, text, _small, _t.Accent, new Rectangle(target.X + S(8), y, width - S(16), height), TextFormatFlags.VerticalCenter);
            }
            x += width + S(4);
        }
        return y + height;
    }
}
