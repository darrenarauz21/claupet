namespace Claupet;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

/// <summary>
/// Ajustes › Terminal (se abre desde Ajustes de la Console): las pestañas, la apariencia con su vista previa y los
/// proyectos vinculados. También los botones de la cabecera de la lista.
/// </summary>
sealed partial class TerminalPane
{
    const string ListIcon = "M9,6 H20 M9,12 H20 M9,18 H20 M4.5,6 H5 M4.5,12 H5 M4.5,18 H5";
    const string GridIcon = "M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H20 V20 H14 Z";

    /// <summary>Abre los ajustes de la Terminal en su panel (desde Ajustes de la Console o el engranaje).</summary>
    public void ShowSettings()
    {
        if (_current is { } previous && !_creating) previous.Draft = _input.Text ?? "";
        _settings = true;
        Build();
        RenderList();
    }

    void CloseSettings()
    {
        _settings = false;
        if (_current is not null && !_creating) _input.Text = _current.Draft;
        Build();
        RenderList();
        FocusInput();
    }

    /// <summary>Guarda lo elegido y lo aplica: los bloques, la lista y la página misma (su vista previa).</summary>
    void ChangeLook(Action<TerminalLook> change)
    {
        var listWidth = ListWidth;
        change(_look);
        _look.Save();
        ApplyLook();
        Build();
        RenderList();
        if (ListWidth != listWidth) ListChanged?.Invoke();
    }

    /// <summary>Los botones de la columna: plegar todos, lista o solo íconos, y la pestaña nueva.</summary>
    Control HeaderButtons(Border add)
    {
        var icons = _look.IconsOnly;
        var panel = new StackPanel
        {
            Orientation = icons ? Orientation.Vertical : Orientation.Horizontal,
            Spacing = icons ? 6 : 0,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = icons ? HorizontalAlignment.Center : HorizontalAlignment.Right,
        };
        if (!icons && Projects().Count > 1)
            panel.Children.Add(Small(Round(AllFolded ? UnfoldAllIcon : FoldAllIcon, AllFolded ? L.T("Desplegar todos los proyectos") : L.T("Plegar todos los proyectos"), FoldAll)));
        panel.Children.Add(Small(Round(icons ? ListIcon : PanelIcon, icons ? L.T("Lista con descripción") : L.T("Solo íconos"),
            () => ChangeLook(l => l.IconsOnly = !l.IconsOnly))));
        // Los ajustes de la Terminal están en Ajustes › Terminal, con los demás.
        panel.Children.Add(add);
        return panel;
    }

    /// <summary>Un botón de ícono de 28, como los de la cabecera de la lista.</summary>
    static Border Small(Border button)
    {
        button.Width = 28;
        button.Height = 28;
        button.CornerRadius = new CornerRadius(8);
        return button;
    }

    /// <summary>
    /// Ajustes › Terminal, como en el diseño: tarjetas con filas (el nombre y su explicación a la izquierda; a la derecha,
    /// botones segmentados, el tamaño con − y +, muestras de color o un interruptor). Arriba de la apariencia, una vista
    /// previa que cambia al elegir.
    /// </summary>
    Control LookPage()
    {
        var panel = new StackPanel { Spacing = 18, Margin = new Thickness(28, 22, 28, 28), HorizontalAlignment = HorizontalAlignment.Left };

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        head.Children.Add(new TextBlock
        {
            Text = L.T("Terminal"),
            FontFamily = Ink.Display,
            FontSize = 19,
            FontWeight = FontWeight.SemiBold,
            Foreground = _ink.Text,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var reset = TextButton(L.T("Lo de siempre"), () => ChangeLook(l =>
        {
            var fresh = new TerminalLook();
            (l.Font, l.Size, l.LineSpacing, l.Text, l.Background, l.Animate, l.IconsOnly, l.Palette, l.Cursor, l.Ligatures, l.DefaultShell) =
                (fresh.Font, fresh.Size, fresh.LineSpacing, fresh.Text, fresh.Background, fresh.Animate, fresh.IconsOnly, fresh.Palette, fresh.Cursor, fresh.Ligatures, fresh.DefaultShell);
        }));
        Grid.SetColumn(reset, 1);
        head.Children.Add(reset);
        var close = Round(CloseIcon, L.T("Cerrar los ajustes"), CloseSettings);
        close.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(close, 2);
        head.Children.Add(close);
        panel.Children.Add(head);

        // Pestañas
        var shells = Shells.Available();
        var tabs = new List<Control>
        {
            SettingRow(L.T("Lista de pestañas"), L.T("Con la descripción o solo los íconos"),
                Segmented([("lista", L.T("Con descripción")), ("iconos", L.T("Solo íconos"))], _look.IconsOnly ? "iconos" : "lista",
                    v => ChangeLook(l => l.IconsOnly = v == "iconos"))),
        };
        if (shells.Count > 0)
            tabs.Add(SettingRow(L.T("Terminal por defecto"), L.T("La de «Nueva terminal aquí», en el menú de cada proyecto"),
                ValuePick(DefaultShell?.Label ?? shells[0].Label, anchor => DropList.Show(anchor, _ink,
                    shells.Select(s => new DropList.Entry(s.Id, s.Label, s == DefaultShell)).ToList(), picked =>
                    {
                        if (picked is not null) ChangeLook(l => l.DefaultShell = picked);
                    }))));
        panel.Children.Add(Section(L.T("Pestañas"), Card(tabs)));

        // Apariencia
        var fonts = TerminalLook.InstalledFonts();
        var dark = _ink.Dark;
        var appearance = new List<Control>
        {
            LookPreview(),
            SettingRow(L.T("Fuente"), null, ValuePick(_look.Font ?? L.T("La de siempre"), anchor => DropList.Show(anchor, _ink,
                fonts.Select(f => new DropList.Entry(f, f, f == _look.Font)).Prepend(new DropList.Entry("", L.T("La de siempre"), _look.Font is null)).ToList(),
                picked =>
                {
                    if (picked is not null) ChangeLook(l => l.Font = picked.Length == 0 ? null : picked);
                }), Mono)),
            SettingRow(L.T("Tamaño"), null, Stepper()),
            SettingRow(L.T("Interlineado"), null,
                Segmented(TerminalLook.Spacings.Select(s => (s.ToString(System.Globalization.CultureInfo.InvariantCulture), TerminalLook.SpacingLabel(s))).ToList(),
                    _look.LineSpacing.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    v => ChangeLook(l => l.LineSpacing = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)))),
            SettingRow(L.T("Color del texto"), L.T("El resto de la paleta lo sigue"), Swatches()),
            SettingRow(L.T("Paleta de la terminal"), L.T("Los 16 colores que usan los programas"),
                Segmented(TerminalLook.Palettes.Select(p => (p, TerminalLook.PaletteLabel(p))).ToList(), _look.Palette, v => ChangeLook(l => l.Palette = v))),
            SettingRow(L.T("Cursor"), L.T("El de los programas de pantalla completa"),
                Segmented(TerminalLook.Cursors.Select(c => (c, TerminalLook.CursorLabel(c))).ToList(), _look.Cursor, v => ChangeLook(l => l.Cursor = v))),
            SettingRow(L.T("Fondo"), null,
                Segmented([("tema", L.T("Del tema")), ("profundo", L.T("Más oscuro")), ("negro", L.T("Negro"))], _look.Background, v => ChangeLook(l => l.Background = v))),
            SettingRow(L.T("Animaciones"), L.T("Pensando, cargando, enviando y al aparecer"),
                Segmented([("si", L.T("Completas")), ("no", L.T("Ninguna"))], _look.Animate ? "si" : "no", v => ChangeLook(l => l.Animate = v == "si"))),
            SettingRow(L.T("Ligaduras"), L.T("Une -> y != en un solo signo"), Switch(_look.Ligatures, on => ChangeLook(l => l.Ligatures = on))),
        };
        panel.Children.Add(Section(L.T("Apariencia"), Card(appearance)));

        // Proyectos vinculados: desvincular solo los quita de la lista, no borra la carpeta.
        var linked = new List<Control>();
        foreach (var folder in _projects.Folders.ToList())
        {
            var name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(folder)) is { Length: > 0 } n ? n : folder;
            var count = TabsOf(folder).Count;
            var detail = $"{ShortPath(folder)} · {(count == 0 ? L.T("sin pestañas") : count == 1 ? L.T("1 pestaña") : L.T("{0} pestañas", count))}";
            var unlink = TextButton(L.T("Desvincular"), () => Unlink(folder));
            if (count > 0) ToolTip.SetTip(unlink, L.T("Cierra sus {0} pestañas", count));
            linked.Add(SettingRow(name, detail, unlink, labelFamily: Mono));
        }
        if (linked.Count == 0)
        {
            var none = Ui.Text(L.T("Todavía no hay. Un proyecto se vincula al abrir una pestaña en su carpeta."), 12.5, _ink.Tertiary);
            none.TextWrapping = TextWrapping.Wrap;
            none.Margin = new Thickness(14, 12);
            linked.Add(none);
        }
        panel.Children.Add(Section(L.T("Proyectos vinculados"), Card(linked)));

        var scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroll.SizeChanged += (_, e) => panel.Width = Math.Clamp(e.NewSize.Width - 56, 0, 680);
        return scroll;
    }

    // ── Piezas de los ajustes ───────────────────────────────────────────────────────────────

    /// <summary>Un grupo: su nombre chico arriba y la tarjeta.</summary>
    Control Section(string title, Control card)
    {
        var group = new StackPanel { Spacing = 6 };
        group.Children.Add(Ui.Text(title, 12, _ink.Tertiary, FontWeight.SemiBold));
        group.Children.Add(card);
        return group;
    }

    /// <summary>La tarjeta, con sus filas separadas por una línea fina.</summary>
    Border Card(IReadOnlyList<Control> rows)
    {
        var stack = new StackPanel();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            stack.Children.Add(i == 0 ? row : new Border { BorderBrush = _ink.Hairline, BorderThickness = new Thickness(0, 1, 0, 0), Child = row });
        }
        return new Border
        {
            Background = _ink.Raised,
            BorderBrush = _ink.Hairline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Child = stack,
        };
    }

    /// <summary>Una fila: el nombre (y su explicación, más chica) a la izquierda y lo que se elige a la derecha.</summary>
    Control SettingRow(string label, string? detail, Control control, FontFamily? labelFamily = null)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 46, Margin = new Thickness(14, 0) };
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 12, 6) };
        var name = Ui.Text(label, 13, _ink.Text, family: labelFamily);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(name);
        if (detail is not null)
        {
            var small = Ui.Text(detail, 12, _ink.Tertiary);
            small.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(small);
        }
        row.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    /// <summary>Botones segmentados: el elegido, en relieve.</summary>
    Control Segmented(IReadOnlyList<(string Id, string Label)> options, string selected, Action<string> chosen)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (id, label) in options)
        {
            var on = id == selected;
            var text = Ui.Text(label, 12.5, on ? _ink.Text : _ink.Secondary, on ? FontWeight.Medium : FontWeight.Normal);
            text.VerticalAlignment = VerticalAlignment.Center;
            var button = Ui.Press(text, () => { if (!on) chosen(id); }, on ? null : (b, hover) => text.Foreground = hover ? _ink.Text : _ink.Secondary, label);
            button.Height = 26;
            button.Padding = new Thickness(10, 0);
            button.CornerRadius = new CornerRadius(7);
            if (on)
            {
                button.Background = _ink.Raised;
                button.BoxShadow = BoxShadows.Parse("0 1 2 0 #29000000");
            }
            row.Children.Add(button);
        }
        return new Border { Background = _ink.Pill, CornerRadius = new CornerRadius(9), Padding = new Thickness(2), Child = row };
    }

    /// <summary>El tamaño de la letra: − el número + (entre los tamaños que se ofrecen).</summary>
    Control Stepper()
    {
        var sizes = TerminalLook.Sizes;
        var at = Array.IndexOf(sizes, _look.Size);
        Border Step(string glyph, int delta, string name)
        {
            var label = Ui.Text(glyph, 14, _ink.Secondary);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            var target = Math.Clamp(at + delta, 0, sizes.Length - 1);
            var button = Ui.Press(label, () => { if (target != at) ChangeLook(l => l.Size = sizes[target]); },
                (b, on) => b.Background = on ? _ink.Raised : Brushes.Transparent, name);
            button.Width = 26;
            button.Height = 26;
            button.CornerRadius = new CornerRadius(7);
            button.Opacity = target == at ? 0.4 : 1;
            return button;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        row.Children.Add(Step("−", -1, L.T("Más chica")));
        var value = Ui.Text(_look.Size.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture), 12.5, _ink.Text, family: Mono);
        value.MinWidth = 34;
        value.TextAlignment = TextAlignment.Center;
        value.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(value);
        row.Children.Add(Step("+", 1, L.T("Más grande")));
        return new Border { Background = _ink.Pill, CornerRadius = new CornerRadius(9), Padding = new Thickness(2), Child = row };
    }

    /// <summary>Las muestras del color del texto: el del tema y los otros; la elegida, con un anillo de acento.</summary>
    Control Swatches()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var text in TerminalLook.Texts)
        {
            var on = _look.Text == text;
            var dot = new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(Color.Parse(TerminalLook.TextSwatch(text, _ink.Dark))) };
            var button = Ui.Press(dot, () => { if (!on) ChangeLook(l => l.Text = text); }, null, TerminalLook.TextLabel(text));
            button.Width = 26;
            button.Height = 26;
            button.CornerRadius = new CornerRadius(13);
            button.BorderThickness = new Thickness(on ? 2 : 1);
            button.BorderBrush = on ? _ink.Accent : _ink.Hairline;
            ToolTip.SetTip(button, TerminalLook.TextLabel(text));
            row.Children.Add(button);
        }
        return row;
    }

    /// <summary>Un interruptor chico, como el del diseño.</summary>
    Control Switch(bool on, Action<bool> changed)
    {
        var knob = new Ellipse { Width = 14, Height = 14, Fill = on ? Brushes.White : _ink.Raised, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(on ? 17 : 3, 0, 0, 0) };
        var track = Ui.Press(knob, () => changed(!on), null, on ? L.T("Encendido") : L.T("Apagado"));
        track.Width = 34;
        track.Height = 20;
        track.CornerRadius = new CornerRadius(10);
        track.Background = on ? _ink.Accent : _ink.Track;
        return track;
    }

    /// <summary>Lo elegido con su flecha: abre su lista al tocarlo.</summary>
    Control ValuePick(string value, Action<Control> pick, FontFamily? family = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var text = Ui.Text(value, 13, _ink.Secondary, family: family);
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        var chevron = Ui.Chevron(_ink.Tertiary);
        chevron.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(chevron);
        Border? button = null;
        button = Ui.Press(row, () => pick(button!), (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, value);
        button.Height = 30;
        button.Padding = new Thickness(10, 0, 8, 0);
        button.CornerRadius = new CornerRadius(8);
        return button;
    }

    /// <summary>La vista previa: un bloque de la terminal con lo elegido (letra, tamaño, interlineado, colores y cursor).</summary>
    Control LookPreview()
    {
        var palette = _look.Colors16(_ink.Dark);
        var size = _look.Size;
        var lines = new StackPanel { Spacing = 2 };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var sign = Icon(CheckIcon, Added, 12, 2.2);
        sign.Margin = new Thickness(0, 0, 10, 0);
        head.Children.Add(sign);
        var command = Ui.Text("git log --oneline -2", size, _ink.Text, FontWeight.SemiBold, Mono);
        Grid.SetColumn(command, 1);
        head.Children.Add(command);
        var meta = Ui.Text(L.T("{0:0.0} s", 0.1), 11.5, _ink.Tertiary);
        meta.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(meta, 2);
        head.Children.Add(meta);
        lines.Children.Add(head);
        var hash = new SolidColorBrush(Color.FromUInt32(0xFF000000 | (uint)palette[3]));
        var green = new SolidColorBrush(Color.FromUInt32(0xFF000000 | (uint)palette[2]));
        void Line(string id, string message, bool cursor)
        {
            var text = new SelectableTextBlock
            {
                FontFamily = Mono,
                FontSize = size - 0.5,
                LineHeight = (size - 0.5) * _look.LineSpacing,
                Foreground = _ink.Text,
                Margin = new Thickness(22, 0, 0, 0),
                Inlines = [new Avalonia.Controls.Documents.Run(id) { Foreground = hash }, new Avalonia.Controls.Documents.Run(" " + message)],
            };
            ApplyLigatures(text);
            if (!cursor)
            {
                lines.Children.Add(text);
                return;
            }
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(text);
            row.Children.Add(PreviewCursor(size));
            lines.Children.Add(row);
        }
        Line("c3216a2", "Merge pull request #18", false);
        Line("bf80d77", "Gemini CLI: lee el formato actual", true);
        var tag = Ui.Text("✓ " + L.T("2 correctas"), size - 1.5, green, family: Mono);
        tag.Margin = new Thickness(22, 2, 0, 0);
        lines.Children.Add(tag);
        return new Border
        {
            Background = _look.OwnBackground ? _ink.Surface : _ink.Window,
            Padding = new Thickness(14, 10, 14, 12),
            Child = lines,
        };
    }

    /// <summary>El cursor de la vista previa, con la forma elegida.</summary>
    Control PreviewCursor(double size) => _look.Cursor switch
    {
        "barra" => new Border { Width = 2, Height = size * 1.15, Background = _ink.Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 0, 0) },
        "subrayado" => new Border { Width = size * 0.6, Height = 2, Background = _ink.Text, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(1, 0, 0, 3) },
        _ => new Border { Width = size * 0.6, Height = size * 1.15, Background = _ink.Text, Opacity = 0.85, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 0, 0) },
    };

    /// <summary>Con las ligaduras apagadas, cada carácter se ve como se escribió (-> no se une en una flecha).</summary>
    void ApplyLigatures(TextBlock text)
    {
        if (_look.Ligatures) return;
        text.FontFeatures = [FontFeature.Parse("-calt"), FontFeature.Parse("-liga")];
    }
}
