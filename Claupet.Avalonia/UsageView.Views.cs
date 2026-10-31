using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SD = System.Drawing;

namespace Claupet;

/// <summary>
/// Las pestañas de las vistas y «Ajustes rápidos», como en Windows: las filas salen de SettingsPages (las mismas de
/// Claupet Console), en cinco pestañas, sin las que son solo de la Console; lo demás, en «Todos los ajustes».
/// </summary>
sealed partial class UsageView
{
    /// <summary>Las pestañas de arriba, en orden.</summary>
    static readonly View[] Tabs = [View.Usage, View.Activity, View.History];

    /// <summary>Las pestañas de Ajustes, en orden (los nombres son los de SettingsPage.Tab).</summary>
    public enum SettingsTab { Avisos, Apariencia, Asistente, Isla, Sistema }

    static readonly SettingsTab[] AllTabs = Enum.GetValues<SettingsTab>();

    /// <summary>Se cambió de pestaña en Ajustes (para recordarla).</summary>
    public event Action<SettingsTab>? SettingsTabChanged;

    static readonly SD.Color OnAccent = SD.Color.FromArgb(0x14, 0x14, 0x13);

    View _beforeSettings = View.Usage;
    IReadOnlyList<SettingsPage> _settingsPages = SettingsPages.Build(new());
    SettingsTab _settingsTab;

    static string ViewLabel(View view) => view switch
    {
        View.Usage => L.T("Uso"),
        View.Activity => L.T("Actividad"),
        View.History => L.T("Historial"),
        _ => L.T("Ajustes"),
    };

    static string TabLabel(SettingsTab tab) => tab switch
    {
        SettingsTab.Avisos => L.T("Avisos"),
        SettingsTab.Apariencia => L.T("Apariencia"),
        SettingsTab.Asistente => L.T("Asistente"),
        SettingsTab.Isla => L.T("Isla"),
        _ => L.T("Sistema"),
    };

    /// <summary>El estado de Ajustes (el mismo que recibe Claupet Console); las filas que aparecen o se van rehacen el tamaño.</summary>
    public void SetSettingsState(SettingsState state)
    {
        _settingsPages = SettingsPages.Build(state);
        if (_view == View.Settings) Relayout();
    }

    /// <summary>La pestaña de Ajustes con la que se abre (la última usada).</summary>
    public void SetSettingsTab(SettingsTab tab) => _settingsTab = tab;

    void SelectSettingsTab(SettingsTab tab)
    {
        if (tab == _settingsTab) return;
        _settingsTab = tab;
        Relayout();
        SettingsTabChanged?.Invoke(tab);
    }

    void OpenView(View view)
    {
        _view = view;
        Relayout();
    }

    /// <summary>
    /// La lista de una opción de Ajustes, junto al cursor: la arma TrayController (las mismas que en la Console) y
    /// elegir llama a pick. Las paletas llevan un punto de su color.
    /// </summary>
    public void ShowOptions(string key, IEnumerable<(string Id, string Label)> options, string selected, Action<string> pick)
    {
        var menu = new ContextMenu();
        foreach (var (id, label) in options)
        {
            var item = new MenuItem { Header = (id == selected ? "✓  " : "     ") + label };
            if (key == "palette" && Claupet.Theme.Palettes.Any(p => p.Id == id))
                if (Claupet.Theme.Palettes.First(p => p.Id == id) is var palette)
                    item.Icon = new Avalonia.Controls.Shapes.Ellipse { Width = 12, Height = 12, Fill = Gfx.B(_t.Dark ? palette.Dark : palette.Light) };
            item.Click += (_, _) => pick(id);
            menu.Items.Add(item);
        }
        menu.Open(this);
    }

    // ── Vista «Ajustes» ─────────────────────────────────────────────────────────────────────

    double ComposeSettingsHeader(DrawingContext? dc, double y)
    {
        var height = Gfx.Title.LineHeight;
        var close = new Rect(ViewWidth - Pad - 24, y + (height - 30) / 2, 30, 30);
        AddHit("settings-close", close, HitKind.CloseSettings);
        if (dc is not null)
        {
            Gfx.Write(dc, L.T("Ajustes rápidos"), Gfx.Title, _t.Text, new Rect(Pad, y, Inner - 40, height));
            if (_hoverKey == "settings-close") Gfx.Fill(dc, _t.Hover, close, 8);
            Gfx.Icon(dc, Glyph.Close, _t.Secondary, close, 13);
        }
        return y + height + 16;
    }

    /// <summary>
    /// Todas las pestañas ocupan lo que la más alta (con lo que esté encendido): al cambiar de pestaña no cambia el
    /// tamaño del cuadro. Las demás se miden sin dejar hits.
    /// </summary>
    double ComposeSettings(DrawingContext? dc, double y)
    {
        y = ComposeSettingsTabs(dc, y);
        var bottom = ComposeSettingsTab(dc, _settingsTab, y);
        var hits = _hits.Count;
        foreach (var tab in AllTabs)
            if (tab != _settingsTab) bottom = Math.Max(bottom, ComposeSettingsTab(null, tab, y));
        _hits.RemoveRange(hits, _hits.Count - hits);
        // Aquí solo lo de todos los días; lo demás, en la ventana grande.
        return ConsoleCard(dc, bottom + 16) + 14;
    }

    /// <summary>Las pestañas: una barra con la elegida resaltada; cada una mide su nombre más una parte igual de lo que sobra.</summary>
    double ComposeSettingsTabs(DrawingContext? dc, double y)
    {
        var bar = new Rect(Pad, y, Inner, 32);
        var widths = AllTabs.Select(t => Gfx.Measure(TabLabel(t), Gfx.Small).Width).ToArray();
        var extra = (bar.Width - 8 - widths.Sum()) / AllTabs.Length;
        if (dc is not null) Gfx.Fill(dc, _t.Hover, bar, 9);
        var x = bar.X + 4;
        for (var i = 0; i < AllTabs.Length; i++)
        {
            var tab = AllTabs[i];
            var cell = new Rect(x, bar.Y + 4, widths[i] + extra, bar.Height - 8);
            x += cell.Width;
            AddHit("stab:" + tab, cell, HitKind.SettingsTab);
            if (dc is null) continue;
            var selected = tab == _settingsTab;
            if (selected)
            {
                Gfx.Fill(dc, _t.Raised, cell, 6);
                Gfx.Outline(dc, _t.Border, cell, 6);
            }
            var color = selected || _hoverKey == "stab:" + tab ? _t.Text : _t.Secondary;
            Gfx.Write(dc, TabLabel(tab), selected ? Gfx.SmallStrong : Gfx.Small, color, cell, TextAlignment.Center, center: true);
        }
        return bar.Bottom + 12;
    }

    double ComposeSettingsTab(DrawingContext? dc, SettingsTab tab, double y)
    {
        foreach (var page in _settingsPages)
        {
            if (page.Tab != tab.ToString()) continue;
            foreach (var row in page.Rows)
            {
                if (row.Place == RowPlace.Console) continue;
                y = row.Kind switch
                {
                    RowKind.Caption => SettingsCaption(dc, row.Text, y),
                    RowKind.Switch => SwitchRow(dc, row.Text, row.Key, row.On, y),
                    RowKind.Choice => ChoiceRow(dc, row.Text, row.Key, row.Value, y, swatch: row.Swatch ? Claupet.Theme.PaletteAccent(_t.Dark) : null),
                    RowKind.Info => InfoRow(dc, row.Text, row.Value, y),
                    RowKind.Note => NoteRow(dc, row.Text, ToneColor(row.Tone), y),
                    RowKind.Link => LinkRow(dc, row.Text, row.Value, row.Key, ToneColor(row.Tone), y),
                    _ => LinksRow(dc, y, (row.Links ?? []).Select(l => (l.Text, l.Key)).ToArray()),
                };
            }
        }
        return y;
    }

    SD.Color ToneColor(RowTone tone) => tone switch
    {
        RowTone.Muted => _t.Tertiary,
        RowTone.Warn => _t.Warn,
        RowTone.Accent => _t.Accent,
        RowTone.Danger => _t.Danger,
        _ => _t.Text,
    };

    /// <summary>«Todos los ajustes»: la tarjeta que abre Claupet Console, con el ícono de Claupet.</summary>
    double ConsoleCard(DrawingContext? dc, double y)
    {
        var card = new Rect(Pad, y, Inner, 68);
        AddHit("set:console", card, HitKind.Setting);
        if (dc is null) return card.Bottom;
        Gfx.Fill(dc, _t.Raised, card, 14);
        Gfx.Outline(dc, _t.Border, card, 14);
        var tile = new Rect(card.X + 14, card.Y + (card.Height - 40) / 2, 40, 40);
        Gfx.Fill(dc, _t.Hover, tile, 10);
        var unit = Math.Max(1, Math.Floor((tile.Width - 8) / ClaupetLogo.Size));
        Gfx.Pixels(dc, tile.X + (tile.Width - ClaupetLogo.Size * unit) / 2, tile.Y + (tile.Height - ClaupetLogo.Size * unit) / 2, unit,
            ClaupetLogo.Paint);
        var open = L.T("Abrir");
        var pillWidth = Gfx.Measure(open, Gfx.BodyStrong).Width + 36;
        var pill = new Rect(card.Right - 14 - pillWidth, card.Y + (card.Height - 36) / 2, pillWidth, 36);
        Gfx.Fill(dc, _hoverKey == "set:console" ? Gfx.Blend(_t.Accent, SD.Color.White, 0.12) : _t.Accent, pill, 18);
        Gfx.Write(dc, open, Gfx.BodyStrong, OnAccent, pill, TextAlignment.Center, center: true);
        var textX = tile.Right + 14;
        var textWidth = pill.X - 12 - textX;
        var lines = Gfx.BodyStrong.LineHeight + 2 + Gfx.Small.LineHeight;
        var top = card.Y + (card.Height - lines) / 2;
        Gfx.Write(dc, L.T("Todos los ajustes"), Gfx.BodyStrong, _t.Text, new Rect(textX, top, textWidth, Gfx.BodyStrong.LineHeight));
        Gfx.Write(dc, L.T("En Claupet Console"), Gfx.Small, _t.Tertiary, new Rect(textX, top + Gfx.BodyStrong.LineHeight + 2, textWidth, Gfx.Small.LineHeight));
        return card.Bottom;
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
            var labelWidth = Gfx.Measure(label, Gfx.Body).Width;
            Gfx.Write(dc, label, Gfx.Body, _t.Text, new Rect(Pad, y, labelWidth + 2, height), center: true);
            var space = Inner - labelWidth - 18 - 14 - (swatch is null ? 0 : 20);
            var valueWidth = Math.Max(0, Math.Min(space, Gfx.Measure(value, Gfx.Body).Width));
            var valueX = Pad + Inner - 18 - valueWidth;
            Gfx.Write(dc, value, Gfx.Body, _t.Secondary, new Rect(valueX, y, valueWidth + 2, height), center: true);
            if (swatch is { } color)
                dc.DrawEllipse(Gfx.B(color), null, new Rect(valueX - 8 - 12, y + (height - 12) / 2, 12, 12));
            Gfx.Icon(dc, Glyph.ChevronDown, _t.Secondary, new Rect(Pad + Inner - 14, y, 14, height), 12);
        }
        return y + height;
    }

    /// <summary>Una fila que solo informa: el nombre y su valor a la derecha, sin lista.</summary>
    double InfoRow(DrawingContext? dc, string label, string value, double y)
    {
        const double height = 32;
        if (dc is not null)
        {
            var labelWidth = Gfx.Measure(label, Gfx.Body).Width;
            Gfx.Write(dc, label, Gfx.Body, _t.Text, new Rect(Pad, y, labelWidth + 2, height), center: true);
            var valueWidth = Math.Max(0, Inner - labelWidth - 14);
            Gfx.Write(dc, value, Gfx.Body, _t.Secondary, new Rect(Pad + Inner - valueWidth, y, valueWidth, height), TextAlignment.Right, center: true);
        }
        return y + height;
    }

    /// <summary>Un texto chico, en varias líneas si hace falta (una aclaración bajo una fila).</summary>
    double NoteRow(DrawingContext? dc, string text, SD.Color color, double y)
    {
        if (text.Length == 0) return y;
        var height = Gfx.MeasureWrapped(text, Gfx.Small, Inner, 6);
        Gfx.Write(dc, text, Gfx.Small, color, new Rect(Pad, y, Inner, height), maxLines: 6);
        return y + height + 6;
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
}
