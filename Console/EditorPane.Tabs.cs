namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Document;

/// <summary>
/// Las pestañas y sus grupos. Un grupo es una píldora de color en la barra (su nombre, el archivo activo si es suyo,
/// cuántos tiene y un punto si alguno está sin guardar); al darle clic se despliega hacia abajo como lista, no a lo
/// ancho como en Edge. Se agrupa con clic derecho en una pestaña o arrastrándola sobre la píldora.
/// </summary>
sealed partial class EditorPane
{
    const string ChevronIcon = "M7,10 L12,15 L17,10";
    const string RenameIcon = "M5,19 H9 L19,9 L15,5 L5,15 Z M13,7 L17,11";
    const string PaletteIcon = "M12,4.5 A7.5,7.5 0 1 0 12,19.5 C13.2,19.5 13.6,18.6 13.1,17.7 C12.5,16.6 13.2,15.5 14.4,15.5 H16.5 A3,3 0 0 0 19.5,12.5 "
        + "C19.5,8.1 16.1,4.5 12,4.5 Z M8.5,11 A0.6,0.6 0 1 0 8.5,11.01 M11,8 A0.6,0.6 0 1 0 11,8.01 M15,8.5 A0.6,0.6 0 1 0 15,8.51";
    const string UngroupIcon = "M6,4 H9 A2,2 0 0 1 11,6 V9 A2,2 0 0 1 9,11 H6 A2,2 0 0 1 4,9 V6 A2,2 0 0 1 6,4 Z "
        + "M15,13 H18 A2,2 0 0 1 20,15 V18 A2,2 0 0 1 18,20 H15 A2,2 0 0 1 13,18 V15 A2,2 0 0 1 15,13 Z";
    const string OutIcon = "M14,5 H19 V19 H14 M10,8 L6,12 L10,16 M6,12 H15";
    const string DragFormat = "claupet/editor-pestana";

    readonly List<EditorGroup> _groups = [];
    Popup? _groupPopup;
    string? _openGroup;
    /// <summary>La pestaña que se está arrastrando (para soltarla sobre un grupo).</summary>
    EditorDoc? _dragging;
    (string? Group, DateTime At) _lastClosed;

    public IReadOnlyList<EditorGroup> Groups => _groups;

    /// <summary>Las pestañas, los grupos y el punto de «sin guardar», según lo de ahora.</summary>
    void Refresh()
    {
        _tabs.Children.Clear();
        var shown = new HashSet<string>();
        foreach (var doc in _docs)
        {
            if (doc.Group is { } id && _groups.FirstOrDefault(g => g.Id == id) is { } group)
            {
                if (shown.Add(id)) _tabs.Children.Add(GroupPill(group));
            }
            else _tabs.Children.Add(Tab(doc));
        }
        _tools.IsVisible = _current is not null && !_confirm.IsVisible;
        if (_current is null && _body.Child == _stage) _body.Child = Start();
        UpdateStatus();
        if (_openGroup is { } open)
        {
            if (_groups.FirstOrDefault(g => g.Id == open) is { } group && PillOf(open) is { } pill && _groupPopup is not null)
            {
                _groupPopup.PlacementTarget = pill;
                FillGroup(group, rename: false);
            }
            else CloseGroupList();
        }
        _sessionChanged = true;
        DocsChanged?.Invoke();
    }

    Border? PillOf(string group) => _tabs.Children.OfType<Border>().FirstOrDefault(b => Equals(b.Tag, "grupo:" + group));

    Color GroupColor(EditorGroup group) => EditorInk.GroupColors(_ink.Dark)[group.Color % 7];

    Border Tab(EditorDoc doc)
    {
        var current = doc == _current;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var title = Ui.Text(doc.Title, 13, current ? _ink.Text : _ink.Tertiary);
        title.MaxWidth = 180;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        row.Children.Add(title);
        // El punto de «sin guardar» y la × comparten lugar: al pasar el puntero, la ×.
        var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = _ink.Accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(dot, L.T("Sin guardar"));
        var close = Ui.Press(Glyph(CloseIcon, _ink.Secondary, 12), () => Close(doc), (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, L.T("Cerrar · Ctrl+W"));
        close.Width = 18;
        close.Height = 18;
        close.CornerRadius = new CornerRadius(999);
        ToolTip.SetTip(close, L.T("Cerrar · Ctrl+W"));
        void Paint(bool hover)
        {
            close.Opacity = hover || current && !doc.Dirty ? 0.9 : 0;
            dot.IsVisible = doc.Dirty && !hover;
        }
        var slot = new Panel { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
        slot.Children.Add(dot);
        slot.Children.Add(close);
        row.Children.Add(slot);
        var tab = Ui.Press(row, () => Select(doc), (b, on) =>
        {
            if (!current) b.Background = on ? _ink.Pill : Brushes.Transparent;
            Paint(on);
        }, doc.Title);
        Paint(false);
        tab.Height = 32;
        tab.Padding = new Thickness(14, 0, 8, 0);
        tab.CornerRadius = new CornerRadius(999);
        if (current) tab.Background = _ink.Pill;
        ToolTip.SetTip(tab, (doc.Path ?? doc.Title) + "\n" + L.T("Clic derecho: agrupar · clic del medio: cerrar"));
        // Clic del medio: cerrar; derecho: los grupos. Arrastrar sobre un grupo: agregarla.
        Point? pressedAt = null;
        tab.AddHandler(PointerPressedEvent, (_, e) =>
        {
            var point = e.GetCurrentPoint(tab);
            if (point.Properties.IsMiddleButtonPressed)
            {
                Close(doc);
                e.Handled = true;
            }
            else if (point.Properties.IsRightButtonPressed)
            {
                TabMenu(tab, doc);
                e.Handled = true;
            }
            else if (point.Properties.IsLeftButtonPressed) pressedAt = point.Position;
        }, RoutingStrategies.Tunnel);
        tab.AddHandler(PointerReleasedEvent, (_, _) => pressedAt = null, RoutingStrategies.Tunnel);
        tab.PointerMoved += async (_, e) =>
        {
            if (pressedAt is not { } from || _groups.Count == 0 || !e.GetCurrentPoint(tab).Properties.IsLeftButtonPressed) return;
            var now = e.GetPosition(tab);
            if (Math.Abs(now.X - from.X) + Math.Abs(now.Y - from.Y) < 8) return;
            pressedAt = null;
            _dragging = doc;
#pragma warning disable CS0618 // DataTransfer recién llega en 11.3 y todavía no la usa el resto de la app
            var data = new DataObject();
            data.Set(DragFormat, doc.BackupId);
            try { await DragDrop.DoDragDrop(e, data, DragDropEffects.Move); }
            finally { _dragging = null; }
#pragma warning restore CS0618
        };
        return tab;
    }

    void TabMenu(Control anchor, EditorDoc doc)
    {
        List<DropList.Entry> entries = [new("new", L.T("Agregar a un grupo nuevo"))];
        foreach (var group in _groups.Where(g => g.Id != doc.Group))
            entries.Add(new("to:" + group.Id, L.T("Agregar a «{0}»", group.Name)));
        if (doc.Group is not null) entries.Add(new("out", L.T("Sacar del grupo")));
        entries.Add(new("close", L.T("Cerrar · Ctrl+W"), Action: true));
        DropList.Show(anchor, _ink, entries, picked =>
        {
            switch (picked)
            {
                case "new": NewGroup(doc); break;
                case "out": Ungroup(doc); break;
                case "close": Close(doc); break;
                case { } to when to.StartsWith("to:"): AddToGroup(doc, to[3..]); break;
            }
        });
    }

    Border GroupPill(EditorGroup group)
    {
        var members = _docs.Where(d => d.Group == group.Id).ToList();
        var active = members.FirstOrDefault(d => d == _current);
        var color = GroupColor(group);
        var brush = new SolidColorBrush(color);
        var open = _openGroup == group.Id;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        row.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = brush, VerticalAlignment = VerticalAlignment.Center });
        var name = Ui.Text(group.Name, 13, brush, FontWeight.SemiBold);
        name.MaxWidth = 140;
        row.Children.Add(name);
        if (active is not null)
        {
            var title = Ui.Text(active.Title, 13, _ink.Text);
            title.MaxWidth = 150;
            row.Children.Add(title);
        }
        var count = Ui.Text(members.Count.ToString(), 11, _ink.Text);
        count.HorizontalAlignment = HorizontalAlignment.Center;
        row.Children.Add(new Border
        {
            Child = count,
            MinWidth = 18,
            Height = 18,
            Padding = new Thickness(5, 0),
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(color, 0.26),
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (members.Any(d => d.Dirty))
        {
            var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = _ink.Accent, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(dot, L.T("Hay cambios sin guardar"));
            row.Children.Add(dot);
        }
        var chevron = Glyph(ChevronIcon, _ink.Secondary, 13);
        chevron.RenderTransform = open ? new RotateTransform(180) : null;
        row.Children.Add(chevron);
        Border? pill = null;
        pill = Ui.Press(row, () => ToggleGroupList(group, pill!), (b, on) => b.Background = new SolidColorBrush(color, on || _openGroup == group.Id ? 0.24 : 0.13),
            L.T("Grupo {0}", group.Name));
        pill.Background = new SolidColorBrush(color, open ? 0.24 : 0.13);
        pill.Height = 32;
        pill.Padding = new Thickness(12, 0, 8, 0);
        pill.CornerRadius = new CornerRadius(999);
        pill.Tag = "grupo:" + group.Id;
        ToolTip.SetTip(pill, members.Count == 1 ? L.T("{0} · 1 archivo", group.Name) : L.T("{0} · {1} archivos", group.Name, members.Count));
        // Soltar una pestaña encima la agrega al grupo.
        DragDrop.SetAllowDrop(pill, true);
        pill.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = _dragging is not null && _dragging.Group != group.Id ? DragDropEffects.Move : DragDropEffects.None;
            if (e.DragEffects != DragDropEffects.None)
            {
                pill.BorderBrush = brush;
                pill.BorderThickness = new Thickness(1.5);
            }
        });
        pill.AddHandler(DragDrop.DragLeaveEvent, (_, _) => pill.BorderThickness = new Thickness(0));
        pill.AddHandler(DragDrop.DropEvent, (_, _) =>
        {
            pill.BorderThickness = new Thickness(0);
            if (_dragging is not { } doc || doc.Group == group.Id) return;
            AddToGroup(doc, group.Id);
            Toast(L.T("Agregado a «{0}»", group.Name));
        });
        return pill;
    }

    // ── La lista del grupo ──────────────────────────────────────────────────────────────────

    void ToggleGroupList(EditorGroup group, Control pill)
    {
        if (_openGroup == group.Id)
        {
            CloseGroupList();
            return;
        }
        // El clic que cerró la lista (por tocar afuera) fue en su misma píldora: queda cerrada.
        if (_lastClosed.Group == group.Id && DateTime.UtcNow - _lastClosed.At < TimeSpan.FromMilliseconds(300)) return;
        ShowGroupList(group, pill, rename: false);
    }

    void ShowGroupList(EditorGroup group, Control pill, bool rename)
    {
        CloseGroupList();
        if (TopLevel.GetTopLevel(this)?.Content is not Panel host) return;
        var popup = new Popup
        {
            PlacementTarget = pill,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            VerticalOffset = 6,
            IsLightDismissEnabled = true,
            ShouldUseOverlayLayer = true,
        };
        popup.Closed += (_, _) =>
        {
            host.Children.Remove(popup);
            if (_groupPopup != popup) return;
            _lastClosed = (_openGroup, DateTime.UtcNow);
            _groupPopup = null;
            _openGroup = null;
            Refresh();
        };
        _groupPopup = popup;
        _openGroup = group.Id;
        host.Children.Add(popup);
        // Primero la barra (la píldora abierta, con la flecha hacia arriba) y después la lista, debajo de la píldora nueva.
        Refresh();
        popup.PlacementTarget = PillOf(group.Id) ?? pill;
        FillGroup(group, rename);
        popup.IsOpen = true;
    }

    void CloseGroupList()
    {
        if (_groupPopup is not { } popup) return;
        _groupPopup = null;
        _openGroup = null;
        popup.IsOpen = false;
    }

    /// <summary>
    /// Lo de adentro: arriba el nombre (o la caja para cambiarlo) y los íconos de cambiar el nombre, el color, desagrupar y
    /// cerrar el grupo; debajo, un archivo por fila (el activo con fondo), con sacar y cerrar al pasar el puntero.
    /// </summary>
    void FillGroup(EditorGroup group, bool rename)
    {
        if (_groupPopup is not { } popup) return;
        var color = GroupColor(group);
        var brush = new SolidColorBrush(color);
        var stack = new StackPanel { Spacing = 1, Width = 290 };

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(8, 2, 0, 6) };
        head.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = brush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        Control title;
        if (rename)
        {
            var (pill, box) = Field("", 160);
            pill.Height = 28;
            pill.Width = double.NaN;
            box.Text = group.Name;
            var done = false;
            void Finish(bool keep)
            {
                if (done) return;
                done = true;
                if (keep && !string.IsNullOrWhiteSpace(box.Text)) group.Name = box.Text.Trim();
                Refresh();
                FillGroup(group, rename: false);
            }
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) Finish(true);
                else if (e.Key == Key.Escape) Finish(false);
                else return;
                e.Handled = true;
            };
            box.LostFocus += (_, _) => Finish(true);
            Dispatcher.UIThread.Post(() =>
            {
                box.Focus();
                box.SelectAll();
            }, DispatcherPriority.Loaded);
            title = pill;
        }
        else title = Ui.Text(group.Name, 14, brush, FontWeight.SemiBold);
        Grid.SetColumn(title, 1);
        head.Children.Add(title);
        var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, Margin = new Thickness(6, 0, 0, 0) };
        var colors = new WrapPanel { IsVisible = false, Margin = new Thickness(10, 2, 8, 8) };
        icons.Children.Add(Small(RenameIcon, L.T("Cambiar el nombre"), () => FillGroup(group, rename: true), 14));
        icons.Children.Add(Small(PaletteIcon, L.T("Color"), () => colors.IsVisible = !colors.IsVisible, 14));
        icons.Children.Add(Small(UngroupIcon, L.T("Desagrupar"), () => Dissolve(group), 14));
        icons.Children.Add(Small(CloseIcon, L.T("Cerrar el grupo"), () => CloseGroup(group), 14));
        Grid.SetColumn(icons, 2);
        head.Children.Add(icons);
        stack.Children.Add(head);

        var palette = EditorInk.GroupColors(_ink.Dark);
        for (var i = 0; i < palette.Length; i++)
        {
            var index = i;
            var swatch = Ui.Press(new Border(), () =>
            {
                group.Color = index;
                Refresh();
            }, (b, on) => b.RenderTransform = on ? new ScaleTransform(1.2, 1.2) : null, L.T("Color"));
            swatch.Width = 18;
            swatch.Height = 18;
            swatch.Margin = new Thickness(0, 0, 8, 0);
            swatch.CornerRadius = new CornerRadius(9);
            swatch.Background = new SolidColorBrush(palette[i]);
            if (i == group.Color % palette.Length) swatch.BorderBrush = _ink.Text;
            swatch.BorderThickness = new Thickness(i == group.Color % palette.Length ? 2 : 0);
            colors.Children.Add(swatch);
        }
        stack.Children.Add(colors);
        stack.Children.Add(new Border { Height = 1, Background = _ink.Hairline, Margin = new Thickness(0, 0, 0, 4) });

        foreach (var doc in _docs.Where(d => d.Group == group.Id).ToList())
            stack.Children.Add(GroupRow(doc));
        popup.Child = Card(new ScrollViewer { Content = stack, MaxHeight = 420, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
    }

    Border GroupRow(EditorDoc doc)
    {
        var current = doc == _current;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), MinHeight = 34 };
        var name = Ui.Text(doc.Title, 13.5, current ? _ink.Text : _ink.Secondary, current ? FontWeight.SemiBold : FontWeight.Normal);
        grid.Children.Add(name);
        if (doc.Dirty)
        {
            var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = _ink.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
            ToolTip.SetTip(dot, L.T("Sin guardar"));
            Grid.SetColumn(dot, 1);
            grid.Children.Add(dot);
        }
        var out_ = Small(OutIcon, L.T("Sacar del grupo"), () => Ungroup(doc), 14);
        var close = Small(CloseIcon, L.T("Cerrar · Ctrl+W"), () => Close(doc), 13);
        out_.Opacity = close.Opacity = 0;
        Grid.SetColumn(out_, 2);
        Grid.SetColumn(close, 3);
        grid.Children.Add(out_);
        grid.Children.Add(close);
        var row = Ui.Press(grid, () =>
        {
            CloseGroupList();
            Select(doc);
            FocusText();
        }, (b, on) =>
        {
            if (!current) b.Background = on ? _ink.Pill : Brushes.Transparent;
            out_.Opacity = close.Opacity = on ? 1 : 0;
        }, doc.Title);
        row.Padding = new Thickness(12, 0, 4, 0);
        row.CornerRadius = new CornerRadius(10);
        if (current) row.Background = _ink.Pill;
        ToolTip.SetTip(row, doc.Path ?? doc.Title);
        return row;
    }

    // ── Agrupar ─────────────────────────────────────────────────────────────────────────────

    /// <summary>La pone en el grupo, junto a las demás del grupo (las pestañas de un grupo van seguidas).</summary>
    internal void AddToGroup(EditorDoc doc, string group)
    {
        var was = doc.Group;
        doc.Group = group;
        var last = _docs.FindLastIndex(d => d.Group == group && d != doc);
        if (last >= 0)
        {
            _docs.Remove(doc);
            _docs.Insert(_docs.FindLastIndex(d => d.Group == group) + 1, doc);
        }
        Forget(was);
        Refresh();
    }

    internal EditorGroup NewGroup(EditorDoc doc)
    {
        var used = _groups.Select(g => g.Color).ToHashSet();
        var color = Enumerable.Range(0, 7).FirstOrDefault(c => !used.Contains(c), _groups.Count % 7);
        var number = 1;
        while (_groups.Any(g => g.Name == L.T("Grupo {0}", number))) number++;
        var group = new EditorGroup { Id = Guid.NewGuid().ToString("N")[..8], Name = L.T("Grupo {0}", number), Color = color };
        _groups.Add(group);
        AddToGroup(doc, group.Id);
        // Recién hecho: se abre su lista con la caja del nombre lista para escribir.
        Dispatcher.UIThread.Post(() =>
        {
            if (PillOf(group.Id) is { } pill) ShowGroupList(group, pill, rename: true);
        }, DispatcherPriority.Loaded);
        return group;
    }

    internal void Ungroup(EditorDoc doc)
    {
        var was = doc.Group;
        doc.Group = null;
        Forget(was);
        Refresh();
    }

    /// <summary>Desagrupar: las pestañas quedan sueltas, en su lugar.</summary>
    void Dissolve(EditorGroup group)
    {
        CloseGroupList();
        foreach (var doc in _docs.Where(d => d.Group == group.Id)) doc.Group = null;
        _groups.Remove(group);
        Refresh();
    }

    /// <summary>Cierra las del grupo; si alguna tiene cambios, pregunta por ella (las demás ya se cerraron).</summary>
    void CloseGroup(EditorGroup group)
    {
        CloseGroupList();
        foreach (var doc in _docs.Where(d => d.Group == group.Id && !d.Dirty).ToList()) Remove(doc, select: false);
        if (_docs.FirstOrDefault(d => d.Group == group.Id) is { } dirty) Close(dirty);
        else
        {
            _groups.Remove(group);
            if (_current is null && _docs.Count > 0) Select(_docs[0]);
            Refresh();
        }
    }

    /// <summary>Un grupo que quedó vacío se va.</summary>
    void Forget(string? group)
    {
        if (group is not null && _docs.All(d => d.Group != group)) _groups.RemoveAll(g => g.Id == group);
    }
}
