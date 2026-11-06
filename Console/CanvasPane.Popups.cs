namespace Claupet;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

sealed partial class CanvasPane
{
    // ── El plan ligado ──────────────────────────────────────────────────────────────────────

    (string Path, DateTime Modified, PlanEntry Entry)? _linked;
    TextBlock? _chipName;

    /// <summary>El plan ligado, leído solo si su archivo cambió. Null si ya no está.</summary>
    PlanEntry? LinkedEntry(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var modified = File.GetLastWriteTimeUtc(path);
            if (_linked is { } known && known.Path == path && known.Modified == modified) return known.Entry;
            var entry = PlanIndex.Read(path, System.IO.Path.GetDirectoryName(path)!);
            _linked = (path, modified, entry);
            return entry;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>En la barra: el avance y el nombre del plan ligado, que lleva a sus detalles en Proyectos. Si ya no se puede leer, el aviso y el menú para cambiarlo.</summary>
    Control? PlanChip(CanvasDoc doc)
    {
        _chipName = null;
        if (doc.Plan is not { } path) return null;
        var entry = LinkedEntry(path);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        Border chip;
        if (entry?.Plan is { } plan)
        {
            var progress = Plan.Progress(plan.Tasks);
            row.Children.Add(new PlanRing(progress, _ink.Accent, _ink.Track, 3) { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center });
            _chipName = Ui.Text(entry.Name, 12.5, _ink.Secondary, FontWeight.Medium);
            row.Children.Add(_chipName);
            chip = Ui.Press(row, () => PlanOpened?.Invoke(path), (b, on) => b.Background = on ? _ink.Track : _ink.Pill, L.T("Ver en Proyectos"));
            ToolTip.SetTip(chip, L.T("Ver en Proyectos") + $" · {progress:P0}");
        }
        else
        {
            row.Children.Add(Glyph(CanvasIcons.Warn, _ink.Warn, 15));
            _chipName = Ui.Text(System.IO.Path.GetFileName(path), 12.5, _ink.Tertiary);
            row.Children.Add(_chipName);
            Border? self = null;
            self = chip = Ui.Press(row, () => _ = LinkMenuAsync(self!), (b, on) => b.Background = on ? _ink.Track : _ink.Pill, L.T("Cambiar el plan ligado"));
            ToolTip.SetTip(chip, L.T("El plan ya no está: {0}", path));
        }
        _chipName.TextTrimming = TextTrimming.CharacterEllipsis;
        _chipName.MaxWidth = 150;
        chip.Background = _ink.Pill;
        chip.CornerRadius = new CornerRadius(999);
        chip.Padding = new Thickness(8, 5, 11, 5);
        chip.Margin = new Thickness(0, 0, 8, 0);
        chip.VerticalAlignment = VerticalAlignment.Center;
        FitPlanChip();
        return chip;
    }

    /// <summary>Sin lugar en la barra, el plan se queda solo con su avance.</summary>
    void FitPlanChip()
    {
        if (_chipName is not null && _bar.Bounds.Width > 0) _chipName.IsVisible = _bar.Bounds.Width >= 760;
    }

    /// <summary>Elegir el plan: primero los de la carpeta del canvas; también abrir uno de otro lado o quitarlo.</summary>
    async Task LinkMenuAsync(Control anchor)
    {
        if (_doc is not { } doc) return;
        var found = FindPlans is { } find ? await find() : [];
        var plans = found.Where(e => e.Plan is not null)
            .OrderByDescending(e => doc.Folder is { } folder && e.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            .ToList();
        // Dos planes con el mismo nombre se distinguen por su carpeta.
        var repeated = plans.GroupBy(e => e.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var entries = plans.Select(e => new DropList.Entry(e.Path,
            repeated.Contains(e.Name) ? $"{e.Name} · {System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(e.Path))}" : e.Name,
            Selected: string.Equals(e.Path, doc.Plan, StringComparison.OrdinalIgnoreCase))).ToList();
        entries.Add(new DropList.Entry("\0open", L.T("Abrir un plan…"), Action: true));
        if (doc.Plan is not null) entries.Add(new DropList.Entry("\0none", L.T("Quitar el plan"), Action: true, Danger: true));
        DropList.Show(anchor, _ink, entries, id =>
        {
            if (id == "\0open") _ = PickPlanAsync();
            else if (id == "\0none") LinkPlan(null);
            else if (id is not null) LinkPlan(id);
        });
    }

    async Task PickPlanAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions { Title = L.T("Abrir un plan"), FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType(L.T("Planes")) { Patterns = ["*" + Plan.Extension] }] });
        if (files.Count > 0 && Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(files[0]) is { } path) LinkPlan(path);
    }

    /// <summary>Liga el canvas a ese plan (null: lo desliga). Es un cambio más: queda en las versiones.</summary>
    internal void LinkPlan(string? path)
    {
        if (_doc is { } doc && !string.Equals(doc.Plan, path, StringComparison.OrdinalIgnoreCase) && Apply(new CanvasOp("plan", Content: path))) Render(keepScroll: true);
    }

    Popup? _popup;

    /// <summary>Una ventana emergente bajo anchor (o encima, si no cabe), que entra con una transición y se cierra al hacer clic afuera.</summary>
    Border Popover(Control anchor, Control content, Thickness? padding = null, PlacementMode placement = PlacementMode.Bottom)
    {
        ClosePopover();
        var card = new Border
        {
            Background = _ink.Raised,
            BorderBrush = _ink.Hairline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = padding ?? new Thickness(6),
            BoxShadow = BoxShadows.Parse(_ink.Dark ? "0 12 32 0 #59000000" : "0 12 32 0 #26000000"),
            Child = content,
            Focusable = true, // para que Esc llegue aunque el menú no tenga una caja donde escribir
            Opacity = 0,
            RenderTransform = TransformOperations.Parse("translateY(-6px) scale(0.97)"),
            Transitions =
            [
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(150) },
                new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
            ],
        };
        card.KeyDown += (_, e) => { if (e.Key == Key.Escape) { ClosePopover(); e.Handled = true; } };
        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = placement,
            VerticalOffset = placement == PlacementMode.Bottom ? 8 : 0,
            IsLightDismissEnabled = true,
            ShouldUseOverlayLayer = true,
            Child = new Border { Padding = new Thickness(12), Child = card },
        };
        if (TopLevel.GetTopLevel(this)?.Content is not Panel host) return card;
        popup.Closed += (_, _) => host.Children.Remove(popup);
        host.Children.Add(popup);
        _popup = popup;
        popup.IsOpen = true;
        Dispatcher.UIThread.Post(() =>
        {
            card.Opacity = 1;
            card.RenderTransform = TransformOperations.Parse("translateY(0px) scale(1)");
            card.Focus();
        }, DispatcherPriority.Background);
        return card;
    }

    void ClosePopover()
    {
        if (_popup is { } popup) popup.IsOpen = false;
        _popup = null;
    }

    /// <summary>Los tipos de sección en una cuadrícula de íconos («Gráfico» es una tabla CSV que se ve como barras).</summary>
    void KindsPopup(Control anchor, string? after)
    {
        var items = new (string Kind, string? View, string Icon, string Name)[]
        {
            (CanvasKinds.Markdown, null, CanvasIcons.Text, L.T("Texto")),
            (CanvasKinds.Code, null, CanvasIcons.Code, L.T("Código")),
            (CanvasKinds.Html, null, CanvasIcons.Html, "HTML"),
            (CanvasKinds.Diagram, null, CanvasIcons.Diagram, L.T("Diagrama")),
            (CanvasKinds.Svg, null, CanvasIcons.Svg, "SVG"),
            (CanvasKinds.Data, "table", CanvasIcons.Json, L.T("Datos")),
            (CanvasKinds.Data, "chart", CanvasIcons.Chart, L.T("Gráfico")),
            (CanvasKinds.Image, null, CanvasIcons.Image, L.T("Imagen")),
            (CanvasKinds.Plan, null, CanvasIcons.PlanTimeline, L.T("Plan")),
        };
        var grid = new UniformGrid { Columns = 4 };
        foreach (var (kind, view, icon, name) in items)
        {
            var button = Ui.Press(Glyph(icon, _ink.Secondary, 19), () => { ClosePopover(); AddSection(kind, after, view); }, null, name);
            button.Width = 46;
            button.Height = 46;
            button.Margin = new Thickness(2);
            button.CornerRadius = new CornerRadius(11);
            button.PointerEntered += (_, _) => { button.Background = WithAlpha(_ink.Accent, 0x1F); ((Avalonia.Controls.Shapes.Path)((Viewbox)button.Child!).Child!).Stroke = _ink.Accent; };
            button.PointerExited += (_, _) => { button.Background = Brushes.Transparent; ((Avalonia.Controls.Shapes.Path)((Viewbox)button.Child!).Child!).Stroke = _ink.Secondary; };
            ToolTip.SetTip(button, name);
            grid.Children.Add(button);
        }
        Popover(anchor, grid, new Thickness(4));
    }

    /// <summary>Pedirle a la IA un cambio: un campo, sugerencias rápidas y el ✦ para enviar al agente de la Terminal.</summary>
    void AskPopup(Control anchor, CanvasSection? section)
    {
        var stack = new StackPanel { Spacing = 10, Width = 320 };
        var box = new TextBox
        {
            Watermark = section is null ? L.T("¿Qué hago en este canvas?") : L.T("¿Qué cambio?"),
            BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 13.5, VerticalContentAlignment = VerticalAlignment.Center,
        };
        Plain(box);
        var go = IconButton(CanvasIcons.Spark, L.T("Pedir"), () => { ClosePopover(); Ask(section, box.Text ?? ""); }, size: 30, glyph: 15);
        go.Background = _ink.Accent;
        go.Tag = "solid";
        ((Avalonia.Controls.Shapes.Path)((Viewbox)go.Child!).Child!).Stroke = Ink.On(_ink.Accent);
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ClosePopover(); Ask(section, box.Text ?? ""); e.Handled = true; } };
        stack.Children.Add(Field(box, go));
        var chips = new WrapPanel();
        var quick = section is null
            ? new[] { L.T("Revísalo"), L.T("Agrega un resumen"), L.T("Hazlo más claro"), L.T("Tradúcelo al inglés") }
            : new[] { L.T("Más corto"), L.T("Más claro"), L.T("Amplíalo"), L.T("Tradúcelo") };
        foreach (var text in quick)
        {
            var chip = Ui.Press(Ui.Text(text, 12, _ink.Secondary), () => { ClosePopover(); Ask(section, text); }, (b, on) => b.Background = on ? _ink.Hairline : _ink.Pill, text);
            chip.Background = _ink.Pill;
            chip.CornerRadius = new CornerRadius(999);
            chip.Padding = new Thickness(10, 4);
            chip.Margin = new Thickness(0, 0, 6, 6);
            chips.Children.Add(chip);
        }
        stack.Children.Add(chips);
        Popover(anchor, stack, new Thickness(10));
        Dispatcher.UIThread.Post(() => box.Focus(), DispatcherPriority.Background);
    }

    /// <summary>Un campo en una píldora con su botón redondo a la derecha.</summary>
    Border Field(TextBox box, Control button)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(box);
        Grid.SetColumn(button, 1);
        row.Children.Add(button);
        return new Border { Background = _ink.Pill, CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 3, 3, 3), Child = row };
    }

    /// <summary>Los comentarios de una sección como conversación, con su respuesta y el ✓ para resolverlos.</summary>
    void ThreadPopup(Control anchor, CanvasSection section)
    {
        var stack = new StackPanel { Spacing = 10, Width = 320 };
        var messages = new StackPanel { Spacing = 10 };
        void Draw()
        {
            messages.Children.Clear();
            var open = _doc?.Comments.Where(c => c.SectionId == section.Id && !c.Resolved).ToList() ?? [];
            var n = 0;
            foreach (var c in open)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(c.ParentId is null ? 0 : 14, 0, 0, 0) };
                var face = Avatar(c.Author, 22);
                face.VerticalAlignment = VerticalAlignment.Top;
                ToolTip.SetTip(face, $"{CanvasJson.AuthorLabel(c.Author)} · {Format.Ago(c.Created)}");
                row.Children.Add(face);
                var bubble = new StackPanel { Spacing = 2 };
                if (c.Quote is { } quote) bubble.Children.Add(new TextBlock { Text = quote, FontSize = 11.5, FontStyle = FontStyle.Italic, Foreground = _ink.Tertiary, MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis });
                bubble.Children.Add(new Border
                {
                    Background = _ink.Pill,
                    CornerRadius = new CornerRadius(4, 12, 12, 12),
                    Padding = new Thickness(11, 7),
                    MaxWidth = 270,
                    Child = new SelectableTextBlock { Text = c.Text, FontSize = 13, Foreground = _ink.Text, TextWrapping = TextWrapping.Wrap },
                });
                row.Children.Add(bubble);
                Enter(row, n++ * 40, distance: 6);
                messages.Children.Add(row);
            }
        }
        Draw();
        stack.Children.Add(messages);
        var box = new TextBox { Watermark = L.T("Comentar…"), BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
        Plain(box);
        void Send()
        {
            if (string.IsNullOrWhiteSpace(box.Text)) return;
            var parent = _doc?.Comments.LastOrDefault(c => c.SectionId == section.Id && !c.Resolved && c.ParentId is null)?.Id;
            if (!Apply(new CanvasOp("comment", Section: section.Id, Text: box.Text, Parent: parent))) return;
            box.Text = "";
            Draw();
            Render(keepScroll: true);
        }
        var send = IconButton(CanvasIcons.Send, L.T("Enviar"), Send, size: 30, glyph: 15);
        send.Background = _ink.Accent;
        send.Tag = "solid";
        ((Avalonia.Controls.Shapes.Path)((Viewbox)send.Child!).Child!).Stroke = Ink.On(_ink.Accent);
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Send(); e.Handled = true; } };
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        actions.Children.Add(Field(box, send));
        if (_doc?.Comments.Any(c => c.SectionId == section.Id && !c.Resolved) == true)
        {
            var resolve = IconButton(CanvasIcons.Check, L.T("Resolver"), () =>
            {
                var ops = _doc!.Comments.Where(c => c.SectionId == section.Id && !c.Resolved && c.ParentId is null).Select(c => new CanvasOp("resolve", Id: c.Id)).ToArray();
                if (ops.Length > 0 && Apply(ops)) { ClosePopover(); Render(keepScroll: true); Toast(L.T("Resuelto"), CanvasIcons.Check); }
            }, size: 36, glyph: 16);
            resolve.Background = new SolidColorBrush(Color.Parse("#3F8F5A"));
            resolve.Tag = "solid";
            ((Avalonia.Controls.Shapes.Path)((Viewbox)resolve.Child!).Child!).Stroke = Brushes.White;
            resolve.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(resolve, 1);
            actions.Children.Add(resolve);
        }
        stack.Children.Add(actions);
        Popover(anchor, stack, new Thickness(10));
        Dispatcher.UIThread.Post(() => box.Focus(), DispatcherPriority.Background);
    }

    /// <summary>Un menú de filas con ícono (las peligrosas en rojo, separadas).</summary>
    void Menu(Control anchor, IReadOnlyList<(string Icon, string Name, Action Run, bool Danger)> items)
    {
        var list = new StackPanel { Spacing = 1, MinWidth = 200 };
        var danger = false;
        foreach (var (icon, name, run, isDanger) in items)
        {
            if (isDanger && !danger && list.Children.Count > 0) list.Children.Add(new Border { Height = 1, Background = _ink.Hairline, Margin = new Thickness(8, 4) });
            danger |= isDanger;
            var brush = isDanger ? _ink.Danger : _ink.Text;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(Glyph(icon, isDanger ? _ink.Danger : _ink.Secondary, 15));
            row.Children.Add(Ui.Text(name, 13.5, brush));
            var action = run;
            var item = Ui.Press(row, () => { ClosePopover(); action(); }, (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, name);
            item.Padding = new Thickness(10, 8);
            item.CornerRadius = new CornerRadius(9);
            list.Children.Add(item);
        }
        Popover(anchor, list, new Thickness(5));
    }

    void SectionMenu(Control anchor, CanvasDoc doc, CanvasSection section)
    {
        var index = doc.Sections.IndexOf(section);
        var items = new List<(string, string, Action, bool)>();
        if (index > 0) items.Add((CanvasIcons.Up, L.T("Subir"), () => { if (Apply(new CanvasOp("move", Id: section.Id, After: index >= 2 ? doc.Sections[index - 2].Id : "start"))) Render(keepScroll: true); }, false));
        if (index < doc.Sections.Count - 1) items.Add((CanvasIcons.Down, L.T("Bajar"), () => { if (Apply(new CanvasOp("move", Id: section.Id, After: doc.Sections[index + 1].Id))) Render(keepScroll: true); }, false));
        items.Add((CanvasIcons.Copy, L.T("Copiar"), () => { _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(section.Content); Toast(L.T("Copiado"), CanvasIcons.Copy); }, false));
        if (section.Kind is CanvasKinds.Html or CanvasKinds.Svg or CanvasKinds.Diagram)
            items.Add((CanvasIcons.Play, L.T("Ver en el navegador"), () => Preview(doc, section), false));
        items.Add((CanvasIcons.Trash, L.T("Borrar"), () =>
        {
            if (_editing == section.Id) CancelEditing();
            if (FindBlock(section.Id) is { } block)
            {
                block.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) }];
                block.Opacity = 0;
            }
            DispatcherTimer.RunOnce(() =>
            {
                if (Apply(new CanvasOp("delete", Id: section.Id))) { Render(keepScroll: true); Toast(L.T("Borrada"), CanvasIcons.Undo); }
            }, TimeSpan.FromMilliseconds(220));
        }, true));
        Menu(anchor, items);
    }

    void ExportPopup(Control anchor)
    {
        if (_doc is not { } doc) return;
        // Con su nombre: seis íconos de documento parecidos no se distinguen sin pasar el mouse por cada uno.
        Menu(anchor,
        [
            (CanvasIcons.Pdf, "PDF", () => _ = SaveAsAsync(doc, "pdf"), false),
            (CanvasIcons.Docx, "Word", () => _ = SaveAsAsync(doc, "docx"), false),
            (CanvasIcons.Text, "Markdown", () => _ = SaveAsAsync(doc, "markdown"), false),
            (CanvasIcons.Html, L.T("Página HTML"), () => _ = SaveAsAsync(doc, "html"), false),
            (CanvasIcons.Json, "JSON", () => _ = SaveAsAsync(doc, "json"), false),
            (CanvasIcons.Copy, L.T("Copiar como Markdown"), () => CopyMarkdown(doc), false),
        ]);
    }

    void CopyMarkdown(CanvasDoc doc)
    {
        SaveEditing();
        _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(CanvasExport.Markdown(_doc ?? doc));
        Toast(L.T("Copiado"), CanvasIcons.Copy);
    }

    void MoreMenu(Control anchor)
    {
        if (_doc is not { } doc) return;
        Menu(anchor,
        [
            (CanvasIcons.Plus, L.T("Canvas nuevo"), New, false),
            (CanvasIcons.Link, L.T("Usar desde otras apps"), CopyConfig, false),
            (CanvasIcons.Folder, L.T("Abrir la carpeta"), () => Shell.Open(_store.Root), false),
            (CanvasIcons.Trash, L.T("Borrar el canvas"), () => ConfirmDelete(anchor, doc), true),
        ]);
    }

    void ConfirmDelete(Control anchor, CanvasDoc doc) => Menu(anchor,
    [
        (CanvasIcons.Trash, L.T("Sí, borrar «{0}»", doc.Title), () =>
        {
            _store.Delete(doc.Id);
            CancelEditing();
            LeavePast();
            Reload();
            _doc = _list.Count > 0 ? _store.Load(_list[0].Id) : null;
            _seen.Clear();
            Render();
            ListChanged?.Invoke();
        }, true),
    ]);

    // ── Ctrl+K ──────────────────────────────────────────────────────────────────────────

    /// <summary>La paleta de comandos: escribir para filtrar, flechas y Enter.</summary>
    void Palette()
    {
        var commands = new List<(string Icon, string Name, string? Keys, Action Run)>
        {
            (CanvasIcons.Plus, L.T("Canvas nuevo"), "Ctrl+N", New),
        };
        if (_doc is { } doc)
        {
            var end = doc.Sections.Count == 0 ? "start" : doc.Sections[^1].Id;
            commands.Add((CanvasIcons.Spark, L.T("Pedir a la IA"), "Ctrl+J", () => AskPopup(_bar, null)));
            foreach (var (kind, view, icon, name) in new (string, string?, string, string)[]
            {
                (CanvasKinds.Markdown, null, CanvasIcons.Text, L.T("Texto")), (CanvasKinds.Code, null, CanvasIcons.Code, L.T("Código")),
                (CanvasKinds.Html, null, CanvasIcons.Html, "HTML"), (CanvasKinds.Diagram, null, CanvasIcons.Diagram, L.T("Diagrama")),
                (CanvasKinds.Data, "chart", CanvasIcons.Chart, L.T("Gráfico")), (CanvasKinds.Data, "table", CanvasIcons.Json, L.T("Datos")),
                (CanvasKinds.Svg, null, CanvasIcons.Svg, "SVG"), (CanvasKinds.Image, null, CanvasIcons.Image, L.T("Imagen")),
                (CanvasKinds.Plan, null, CanvasIcons.PlanTimeline, L.T("Plan")),
            })
                commands.Add((icon, "+ " + name, null, () => AddSection(kind, end, view)));
            commands.Add((CanvasIcons.History, L.T("Versiones"), null, ToggleHistory));
            commands.Add((CanvasIcons.Play, L.T("Ver en el navegador"), null, () => Preview(doc, null)));
            commands.Add((CanvasIcons.Pdf, L.T("Exportar a PDF"), null, () => _ = SaveAsAsync(doc, "pdf")));
            commands.Add((CanvasIcons.Docx, L.T("Exportar a Word"), null, () => _ = SaveAsAsync(doc, "docx")));
            commands.Add((CanvasIcons.Copy, L.T("Copiar como Markdown"), null, () => CopyMarkdown(doc)));
        }
        foreach (var summary in _list.Take(8))
        {
            var id = summary.Id;
            commands.Add((CanvasIcons.Canvas, summary.Title, null, () => Select(id)));
        }

        var stack = new StackPanel { Width = 480 };
        var query = new TextBox { Watermark = L.T("Buscar o insertar…"), BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 15, VerticalContentAlignment = VerticalAlignment.Center };
        Plain(query);
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(10, 6, 10, 8) };
        var lens = Glyph(CanvasIcons.Search, _ink.Tertiary, 17);
        lens.Margin = new Thickness(0, 0, 8, 0);
        head.Children.Add(lens);
        Grid.SetColumn(query, 1);
        head.Children.Add(query);
        stack.Children.Add(head);
        stack.Children.Add(new Border { Height = 1, Background = _ink.Hairline });
        var results = new StackPanel { Spacing = 1, Margin = new Thickness(0, 6, 0, 0) };
        stack.Children.Add(new ScrollViewer { Content = results, MaxHeight = 360 });
        var shown = commands;
        var selected = 0;
        void Draw()
        {
            var filter = query.Text?.Trim() ?? "";
            shown = commands.Where(c => filter.Length == 0 || c.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();
            selected = Math.Clamp(selected, 0, Math.Max(0, shown.Count - 1));
            results.Children.Clear();
            for (var i = 0; i < shown.Count; i++)
            {
                var (icon, name, keys, run) = shown[i];
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
                var glyph = Glyph(icon, _ink.Secondary, 16);
                glyph.Margin = new Thickness(0, 0, 12, 0);
                row.Children.Add(glyph);
                var label = Ui.Text(name, 14, _ink.Text);
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                if (keys is not null)
                {
                    var kbd = new Border { Background = _ink.Pill, CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1), Child = new TextBlock { Text = keys, FontFamily = Ink.Mono, FontSize = 11, Foreground = _ink.Tertiary } };
                    Grid.SetColumn(kbd, 2);
                    row.Children.Add(kbd);
                }
                var action = run;
                var item = Ui.Press(row, () => { ClosePopover(); action(); }, null, name);
                item.Padding = new Thickness(12, 9);
                item.CornerRadius = new CornerRadius(10);
                item.Background = i == selected ? _ink.Pill : Brushes.Transparent;
                var index = i;
                item.PointerEntered += (_, _) => { selected = index; foreach (var (child, j) in results.Children.OfType<Border>().Select((c, j) => (c, j))) child.Background = j == selected ? _ink.Pill : Brushes.Transparent; };
                results.Children.Add(item);
            }
        }
        query.TextChanged += (_, _) => { selected = 0; Draw(); };
        query.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Down when shown.Count > 0: selected = (selected + 1) % shown.Count; Draw(); break;
                case Key.Up when shown.Count > 0: selected = (selected - 1 + shown.Count) % shown.Count; Draw(); break;
                case Key.Enter when shown.Count > 0: var run = shown[selected].Run; ClosePopover(); run(); break;
                case Key.Escape: ClosePopover(); break;
                default: return;
            }
            e.Handled = true;
        };
        Draw();
        var card = Popover(_scroll, stack, new Thickness(6), PlacementMode.Center);
        card.VerticalAlignment = VerticalAlignment.Top;
        Dispatcher.UIThread.Post(() => query.Focus(), DispatcherPriority.Background);
    }
}
