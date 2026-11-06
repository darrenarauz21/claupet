namespace Claupet;

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

/// <summary>
/// Lo que solo se ve en grande (la sección Proyectos y su PDF): el Resumen con su anillo, lo que se puede hacer ahora y las
/// etapas; el Tablero a todo el ancho, donde las tareas se arrastran; el Diagrama con zoom; las Métricas y la Actividad.
/// </summary>
sealed partial class PlanPanel
{
    /// <summary>Se pidió trabajar en una tarea (el ▶ del Resumen).</summary>
    public Action<PlanTask>? TaskWork { get; init; }
    /// <summary>Se tocó una ficha del Resumen: el filtro que le corresponde.</summary>
    public Action<string>? FilterPicked { get; init; }
    /// <summary>El zoom con que abre el Diagrama (null: el que mejor le quede).</summary>
    public double? StartZoom { get; init; }
    public Action<double>? ZoomChanged { get; init; }

    const double PageBottom = 72;

    /// <summary>Una de las vistas por su id, la que ve la sección.</summary>
    internal Control View(string id, Plan plan, IReadOnlyList<PlanEvent> events) => id switch
    {
        "tablero" => Board(plan),
        "cronograma" => Timeline(plan),
        "diagrama" => Graph(plan),
        "metricas" => Metrics(plan, events),
        "actividad" => Activity(plan, events),
        _ => Overview(plan),
    };

    /// <summary>Las vistas de la sección, en orden.</summary>
    internal static (string Id, string Icon, string Name)[] Views() =>
    [
        ("resumen", OverviewIcon, L.T("Resumen")), ("tablero", BoardIcon, L.T("Tablero")), ("cronograma", TimelineIcon, L.T("Cronograma")),
        ("diagrama", GraphIcon, L.T("Diagrama")), ("metricas", MetricsIcon, L.T("Métricas")), ("actividad", ActivityIcon, L.T("Actividad")),
    ];

    internal const string OverviewIcon = "M12,3.5 A8.5,8.5 0 1 0 20.5,12 H12 Z M15,3.5 A6,6 0 0 1 20.5,9 H15 Z";
    internal const string BoardIcon = "M4,5 H8 V19 H4 Z M10,5 H14 V14 H10 Z M16,5 H20 V10 H16 Z";
    internal const string TimelineIcon = "M4,7 H11 M8,12 H16 M13,17 H20";
    internal const string MetricsIcon = "M4,20 H20 M4,16 L9,10 L13,14 L20,5 M15.5,5 H20 V9.5";
    internal const string ActivityIcon = "M3.5,12 A8.5,8.5 0 1 0 6,6 M3.5,4 V8 H7.5 M12,7.5 V12 L15,14";
    const string ArrowRightIcon = "M5,12 H19 M13,6 L19,12 L13,18";

    /// <summary>El ícono y el color de un estado; una tarea por hacer que ya se puede empezar lleva el ▶.</summary>
    internal (string Icon, IBrush Tint, string Name) StateLook(Plan plan, PlanTask task) => task.State switch
    {
        PlanState.Done => (CheckIcon, Added, L.T("Hecho")),
        PlanState.Doing => (PlanClockIcon, _ink.Accent, L.T("En curso")),
        PlanState.Review => (ReviewIcon, _ink.Secondary, L.T("Revisión")),
        PlanState.Blocked => (BlockedIcon, _ink.Warn, L.T("Bloqueado")),
        _ when PlanGraph.Ready(plan, task) => (PlanPlayIcon, Added, L.T("Se puede empezar")),
        _ => (WaitIcon, _ink.Tertiary, L.T("Por hacer")),
    };

    // ── Resumen ─────────────────────────────────────────────────────────────────────────────

    Control Overview(Plan plan)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var page = new StackPanel { Spacing = 22, MaxWidth = 1040, Margin = new Thickness(30, 26, 30, PageBottom + 20) };

        // El avance, grande, con el título y lo próximo que importa.
        var progress = Plan.Progress(plan.Tasks);
        var hero = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        hero.Children.Add(new Panel
        {
            Width = 112,
            Height = 112,
            Margin = new Thickness(0, 0, 26, 0),
            Children =
            {
                new PlanRing(progress, _ink.Accent, _ink.Pill, 11),
                new TextBlock { Text = $"{progress:P0}", FontFamily = Ink.Display, FontSize = 25, FontWeight = FontWeight.SemiBold, Foreground = _ink.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            },
        });
        var words = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var title = Ui.Text(plan.Title, 26, _ink.Text, FontWeight.SemiBold, Ink.Display);
        title.TextWrapping = TextWrapping.Wrap;
        words.Children.Add(title);
        if (plan.Description is { } description)
        {
            var text = Ui.Text(description, 14, _ink.Secondary);
            text.TextWrapping = TextWrapping.Wrap;
            words.Children.Add(text);
        }
        if ((plan.Milestones.FirstOrDefault(m => m.Date >= today) ?? plan.Milestones.LastOrDefault()) is { } next)
        {
            var tint = next.Date < today ? _ink.Danger : _ink.Accent;
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { Icon(PlanFlagIcon, tint, 13, 2), Ui.Text($"{next.Title} · {Until(next.Date, today)}", 12.5, tint, FontWeight.Medium) } };
            words.Children.Add(new Border { Background = Translucent(tint, 0.13), CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 4), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Child = line });
        }
        Grid.SetColumn(words, 1);
        hero.Children.Add(words);
        page.Children.Add(hero);

        // Lo que hay que mirar, en fichas; tocar una filtra por eso.
        var late = plan.Tasks.Count(t => t.LateOn(today));
        var blocked = plan.Tasks.Count(t => t.State == PlanState.Blocked);
        var doing = plan.Tasks.Count(t => t.State == PlanState.Doing);
        var ready = plan.Tasks.Count(t => t.State == PlanState.Pending && PlanGraph.Ready(plan, t));
        var spent = plan.Tasks.Sum(t => t.HoursSpent ?? 0);
        var estimated = plan.Tasks.Sum(t => t.Hours ?? 0);
        var tiles = new WrapPanel { Margin = new Thickness(0, 0, -10, -10) };
        tiles.Children.Add(StatTile(PlanAlertIcon, _ink.Danger, late.ToString(CultureInfo.CurrentCulture), L.T("Atrasadas"), "late"));
        tiles.Children.Add(StatTile(BlockedIcon, _ink.Warn, blocked.ToString(CultureInfo.CurrentCulture), L.T("Bloqueadas"), "blocked"));
        tiles.Children.Add(StatTile(PlanClockIcon, _ink.Accent, doing.ToString(CultureInfo.CurrentCulture), L.T("En curso"), "doing"));
        tiles.Children.Add(StatTile(PlanPlayIcon, Added, ready.ToString(CultureInfo.CurrentCulture), L.T("Listas"), "ready"));
        if (estimated > 0 || spent > 0)
            tiles.Children.Add(StatTile(PlanClockIcon, _ink.Tertiary, estimated > 0 ? $"{spent:0.#}/{estimated:0.#} h" : $"{spent:0.#} h", L.T("Horas"), null));
        page.Children.Add(tiles);

        // Lo de ahora y cómo va cada etapa, lado a lado.
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("5*,22,4*") };
        var now = new StackPanel { Spacing = 5 };
        now.Children.Add(SectionTitle(PlanPlayIcon, L.T("Ahora")));
        var current = plan.Tasks.Where(t => t.State is PlanState.Doing or PlanState.Review || (t.State == PlanState.Pending && PlanGraph.Ready(plan, t))).Take(8).ToList();
        foreach (var task in current) now.Children.Add(NowRow(plan, task));
        if (current.Count == 0) now.Children.Add(Ui.Text(L.T("Nada pendiente"), 13, _ink.Tertiary));
        columns.Children.Add(now);
        var stages = StageMeters(plan);
        Grid.SetColumn(stages, 2);
        columns.Children.Add(stages);
        page.Children.Add(columns);
        return new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
    }

    Control SectionTitle(string icon, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 4), Children = { Icon(icon, _ink.Tertiary, 13, 2), Ui.Text(text, 12, _ink.Tertiary, FontWeight.SemiBold) } };
        return row;
    }

    /// <summary>Una ficha: un ícono en su círculo, una cifra y de qué se trata. Con filter, tocarla filtra por eso.</summary>
    Control StatTile(string icon, IBrush tint, string value, string label, string? filter)
    {
        var mark = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(999), Background = Translucent(tint, 0.15), Child = Icon(icon, tint, 15, 1.8) };
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 0, 0), Children = { Ui.Text(value, 18, _ink.Text, FontWeight.SemiBold, Ink.Display), Ui.Text(label, 11.5, _ink.Tertiary) } };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { mark, words } };
        Border tile = filter is not null && FilterPicked is { } picked
            ? Ui.Press(row, () => picked(filter), (b, on) => b.Background = on ? _ink.Pill : _ink.Raised, label)
            : new Border { Child = row };
        tile.Width = 158;
        tile.Height = 60;
        tile.Margin = new Thickness(0, 0, 10, 10);
        tile.Padding = new Thickness(13, 0, 8, 0);
        tile.CornerRadius = new CornerRadius(14);
        tile.Background = _ink.Raised;
        tile.BorderBrush = _ink.Hairline;
        tile.BorderThickness = new Thickness(1);
        ToolTip.SetTip(tile, label);
        return tile;
    }

    Control NowRow(Plan plan, PlanTask task)
    {
        var (icon, tint, name) = StateLook(plan, task);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"), Height = 30 };
        var state = Icon(icon, tint, 15, 2);
        ToolTip.SetTip(state, name);
        row.Children.Add(state);
        var title = Ui.Text(task.Title, 13.5, _ink.Text, FontWeight.Medium);
        title.Margin = new Thickness(10, 0, 8, 0);
        Grid.SetColumn(title, 1);
        row.Children.Add(title);
        if (task.Editor is { } editor)
        {
            var face = AgentFace(editor, 18);
            face.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(face, 2);
            row.Children.Add(face);
        }
        var days = Ui.Text(task.Days == 1 ? L.T("1 día") : L.T("{0} días", task.Days), 12, _ink.Tertiary);
        Grid.SetColumn(days, 3);
        row.Children.Add(days);
        if (TaskWork is { } work && task.State != PlanState.Review)
        {
            var go = Ui.Press(Icon(PlanPlayIcon, Ink.On(_ink.Accent), 13, 2), () => work(task), (b, on) => b.Opacity = on ? 0.88 : 1, L.T("Trabajar en esto"));
            go.Width = 30;
            go.Height = 30;
            go.CornerRadius = new CornerRadius(999);
            go.Background = _ink.Accent;
            go.Margin = new Thickness(10, 0, 0, 0);
            ToolTip.SetTip(go, L.T("Trabajar en esto"));
            Grid.SetColumn(go, 4);
            row.Children.Add(go);
        }
        var card = new Border { Child = row, Background = _ink.Raised, BorderBrush = _ink.Hairline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 6, 7, 6), Opacity = Shade(task) };
        ToolTip.SetTip(card, TaskTip(plan, task));
        Clickable(card, task);
        return card;
    }

    Control StageMeters(Plan plan)
    {
        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(SectionTitle(BoardIcon, L.T("Etapas")));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        foreach (var stage in plan.Stages)
        {
            var tasks = plan.Tasks.Where(t => t.Stage == stage.Id).ToList();
            if (tasks.Count == 0) continue;
            var row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = Ui.Text(stage.Name, 13, _ink.Text);
            name.Margin = new Thickness(0, 5, 12, 5);
            name.MaxWidth = 150;
            Grid.SetRow(name, row);
            grid.Children.Add(name);
            var value = Plan.Progress(tasks);
            var meter = Meter(value, StageBrush(plan, stage.Id), 7);
            meter.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(meter, row);
            Grid.SetColumn(meter, 1);
            grid.Children.Add(meter);
            var percent = Ui.Text($"{value:P0}", 12, _ink.Tertiary);
            percent.MinWidth = 42;
            percent.TextAlignment = TextAlignment.Right;
            Grid.SetRow(percent, row);
            Grid.SetColumn(percent, 2);
            grid.Children.Add(percent);
        }
        stack.Children.Add(grid);
        return stack;
    }

    // ── Tablero ─────────────────────────────────────────────────────────────────────────────

    Control WideBoard(Plan plan)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var columns = new List<(PlanState State, string Icon, IBrush Tint, string Name)>
        {
            (PlanState.Pending, WaitIcon, _ink.Tertiary, L.T("Por hacer")), (PlanState.Doing, PlanClockIcon, _ink.Accent, L.T("En curso")),
            (PlanState.Review, ReviewIcon, _ink.Secondary, L.T("Revisión")), (PlanState.Done, CheckIcon, Added, L.T("Hecho")),
        };
        if (plan.Tasks.Any(t => t.State == PlanState.Blocked)) columns.Insert(1, (PlanState.Blocked, BlockedIcon, _ink.Warn, L.T("Bloqueado")));

        var grid = new Grid { Margin = new Thickness(16, 16, 16, PageBottom) };
        var boxes = new List<(Border Box, PlanState State)>();
        foreach (var (state, icon, tint, name) in columns)
        {
            var index = grid.ColumnDefinitions.Count;
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star) { MinWidth = 186 });
            var tasks = plan.Tasks.Where(t => t.State == state).ToList();
            var stack = new StackPanel { Spacing = 7 };
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(4, 2, 4, 4) };
            head.Children.Add(Icon(icon, tint, 14, 2));
            var label = Ui.Text(name, 12.5, _ink.Secondary, FontWeight.SemiBold);
            label.Margin = new Thickness(7, 0, 0, 0);
            Grid.SetColumn(label, 1);
            head.Children.Add(label);
            var count = Ui.Text(tasks.Count.ToString(CultureInfo.CurrentCulture), 12, _ink.Tertiary);
            Grid.SetColumn(count, 2);
            head.Children.Add(count);
            stack.Children.Add(head);
            var box = new Border { Background = Translucent(_ink.Pill, 0.38), CornerRadius = new CornerRadius(14), Padding = new Thickness(9), Margin = new Thickness(index == 0 ? 0 : 5, 0, index == columns.Count - 1 ? 0 : 5, 0), Child = stack };
            foreach (var task in tasks)
            {
                var card = PlanCard(plan, task, today);
                card.Opacity = Shade(task);
                Draggable(card, task, box, boxes);
                stack.Children.Add(card);
            }
            Grid.SetColumn(box, index);
            grid.Children.Add(box);
            boxes.Add((box, state));
        }
        // El ScrollViewer da alto y ancho sin fin: las columnas llenan lo que se ve, y si no caben se desliza.
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = grid };
        scroll.SizeChanged += (_, e) =>
        {
            grid.Width = Math.Max(e.NewSize.Width - 32, columns.Count * 196);
            grid.MinHeight = Math.Max(0, e.NewSize.Height - 16 - PageBottom);
        };
        return scroll;
    }

    /// <summary>Una tarjeta se toca (abre su detalle) o se arrastra a otra columna (cambia su estado).</summary>
    void Draggable(Control card, PlanTask task, Border home, List<(Border Box, PlanState State)> boxes)
    {
        if (TaskClicked is null && TaskMoved is null) return;
        card.Cursor = Ui.Hand;
        Point? start = null;
        var dragging = false;
        var shift = new TranslateTransform();
        card.RenderTransform = shift;
        Border? Over(PointerEventArgs e) => boxes.FirstOrDefault(b => new Rect(b.Box.Bounds.Size).Contains(e.GetPosition(b.Box))).Box;
        void Mark(Border? over)
        {
            foreach (var (box, _) in boxes)
            {
                box.Background = box == over && dragging ? Translucent(_ink.Accent, 0.13) : Translucent(_ink.Pill, 0.38);
                box.BorderBrush = box == over && dragging ? _ink.Accent : null;
                box.BorderThickness = new Thickness(box == over && dragging ? 1.5 : 0);
            }
        }
        void Reset()
        {
            start = null;
            dragging = false;
            shift.X = shift.Y = 0;
            card.Opacity = Shade(task);
            home.ZIndex = 0;
            Mark(null);
        }
        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
            start = e.GetPosition(home);
            e.Pointer.Capture(card);
            e.Handled = true;
        };
        card.PointerMoved += (_, e) =>
        {
            if (start is not { } from) return;
            var at = e.GetPosition(home);
            if (!dragging && TaskMoved is not null && Point.Distance(at, from) > 6)
            {
                dragging = true;
                home.ZIndex = 10;
                card.Opacity = 0.8;
            }
            if (!dragging) return;
            shift.X = at.X - from.X;
            shift.Y = at.Y - from.Y;
            Mark(Over(e));
        };
        card.PointerReleased += (_, e) =>
        {
            if (start is null) return;
            var moved = dragging;
            var over = moved ? Over(e) : null;
            e.Pointer.Capture(null);
            Reset();
            e.Handled = true;
            if (!moved) TaskClicked?.Invoke(task);
            else if (over is not null && boxes.First(b => b.Box == over).State is var state && state != task.State) TaskMoved?.Invoke(task, state);
        };
        card.PointerCaptureLost += (_, _) => { if (start is not null) Reset(); };
    }

    // ── Diagrama con zoom ───────────────────────────────────────────────────────────────────

    LayoutTransformControl? _zoomHost;
    ScrollViewer? _graphScroll;
    Canvas? _graphCanvas;

    public double Zoom { get; private set; } = 1;

    void SetZoom(double zoom)
    {
        Zoom = Math.Clamp(zoom, 0.35, 1.6);
        if (_zoomHost is not null) _zoomHost.LayoutTransform = new ScaleTransform(Zoom, Zoom);
        ZoomChanged?.Invoke(Zoom);
    }

    internal void ZoomBy(double delta) => SetZoom(Zoom + delta);

    /// <summary>Lo que hace falta para que se vea entero.</summary>
    double FitScale() =>
        _graphScroll is null || _graphCanvas is null ? 1 : Math.Min(_graphScroll.Bounds.Width / _graphCanvas.Width, Math.Max(1, _graphScroll.Bounds.Height - PageBottom) / _graphCanvas.Height);

    internal void ZoomFit() => SetZoom(Math.Min(1.25, FitScale()));

    Control ZoomableGraph(PlanGraph graph, Canvas canvas, Func<int, double> x, Func<double, double> y)
    {
        canvas.Background = Brushes.Transparent;
        _graphCanvas = canvas;
        Zoom = StartZoom ?? 1;
        _zoomHost = new LayoutTransformControl { LayoutTransform = new ScaleTransform(Zoom, Zoom), Child = canvas, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 0, PageBottom) };
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _zoomHost };
        _graphScroll = scroll;

        // Al abrir: legible aunque no quepa entero, y mostrando lo que está en curso.
        if (StartZoom is null)
            scroll.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                SetZoom(Math.Clamp(FitScale(), 0.85, 1));
                var now = graph.Nodes.FirstOrDefault(n => n.Task?.State == PlanState.Doing) ?? graph.Nodes.FirstOrDefault(n => n.Task is { State: not PlanState.Done });
                if (now is not null) scroll.Offset = new Vector(Math.Max(0, x(now.Column) * Zoom - scroll.Bounds.Width / 3), Math.Max(0, y(now.Row) * Zoom - scroll.Bounds.Height / 3));
            }, DispatcherPriority.Background);

        // Ctrl + rueda acerca; arrastrar el fondo lo mueve.
        scroll.AddHandler(InputElement.PointerWheelChangedEvent, (object? _, PointerWheelEventArgs e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            ZoomBy(Math.Sign(e.Delta.Y) * 0.08);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        Point? from = null;
        var origin = default(Vector);
        canvas.PointerPressed += (_, e) =>
        {
            if (e.Source != canvas || !e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
            from = e.GetPosition(scroll);
            origin = scroll.Offset;
            canvas.Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Pointer.Capture(canvas);
        };
        canvas.PointerMoved += (_, e) =>
        {
            if (from is not { } start) return;
            var at = e.GetPosition(scroll);
            scroll.Offset = new Vector(origin.X - at.X + start.X, origin.Y - at.Y + start.Y);
        };
        canvas.PointerReleased += (_, e) =>
        {
            from = null;
            canvas.Cursor = null;
            e.Pointer.Capture(null);
        };
        return scroll;
    }

    // ── Métricas ────────────────────────────────────────────────────────────────────────────

    Control Metrics(Plan plan, IReadOnlyList<PlanEvent> events)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var page = new StackPanel { Spacing = 18, MaxWidth = 1040, Margin = new Thickness(30, 22, 30, PageBottom + 20) };
        var spent = plan.Tasks.Sum(t => t.HoursSpent ?? 0);
        var estimated = plan.Tasks.Sum(t => t.Hours ?? 0);
        var tiles = new WrapPanel { Margin = new Thickness(0, 0, -10, -10) };
        tiles.Children.Add(StatTile(CheckIcon, Added, $"{plan.Tasks.Count(t => t.State == PlanState.Done)}/{plan.Tasks.Count}", L.T("Tareas"), null));
        var finished = plan.Tasks.Where(t => t.State == PlanState.Done && t.Hours > 0 && t.HoursSpent is not null).ToList();
        if (finished.Count > 0)
        {
            var ratio = finished.Sum(t => t.HoursSpent!.Value) / finished.Sum(t => t.Hours!.Value);
            tiles.Children.Add(StatTile(MetricsIcon, ratio > 1 ? _ink.Warn : _ink.Accent, $"{ratio:P0}", L.T("Real / estimado"), null));
        }
        if (estimated > 0 || spent > 0)
            tiles.Children.Add(StatTile(PlanClockIcon, _ink.Tertiary, $"{spent:0.#} h", estimated > 0 ? L.T("de {0:0.#} h", estimated) : L.T("Horas"), null));
        var (from, to) = plan.Span();
        tiles.Children.Add(StatTile(PlanCalendarIcon, _ink.Tertiary, L.T("{0} días", to.DayNumber - from.DayNumber), $"{from.ToString("d MMM", CultureInfo.CurrentCulture)} → {to.AddDays(-1).ToString("d MMM", CultureInfo.CurrentCulture)}", null));
        page.Children.Add(tiles);

        Border Card(Control child) => new() { Background = _ink.Raised, BorderBrush = _ink.Hairline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(17, 15), Child = child };
        Control Key(IBrush brush, string name, bool dashed = false)
        {
            var mark = new Avalonia.Controls.Shapes.Line { StartPoint = new Point(0, 1), EndPoint = new Point(14, 1), Stroke = brush, StrokeThickness = 2, Width = 14, Height = 2, VerticalAlignment = VerticalAlignment.Center };
            if (dashed) mark.StrokeDashArray = [2, 1.5];
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { mark, Ui.Text(name, 12, _ink.Tertiary) } };
        }
        Control Head(string icon, string name, params Control[] keys)
        {
            var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
            bar.Children.Add(SectionTitle(icon, name));
            var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
            foreach (var key in keys) legend.Children.Add(key);
            Grid.SetColumn(legend, 1);
            bar.Children.Add(legend);
            return bar;
        }

        if (plan.Tasks.Count > 0)
            page.Children.Add(Card(new StackPanel
            {
                Children =
                {
                    Head(MetricsIcon, L.T("Avance"), Key(_ink.Accent, L.T("Real")), Key(_ink.Secondary, L.T("Ideal"), dashed: true)),
                    new PlanBurnChart(new PlanBurn(plan, today, events), _ink, today) { Height = 196 },
                },
            }));

        var worked = plan.Tasks.Where(t => t.Hours is not null || t.HoursSpent is not null).ToList();
        if (worked.Count > 0)
        {
            var most = Math.Max(0.1, worked.Max(t => Math.Max(t.Hours ?? 0, t.HoursSpent ?? 0)));
            var rows = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            foreach (var task in worked)
            {
                var row = rows.RowDefinitions.Count;
                rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var name = Ui.Text(task.Title, 12.5, _ink.Text);
                name.MaxWidth = 190;
                name.Margin = new Thickness(0, 4, 12, 4);
                Grid.SetRow(name, row);
                rows.Children.Add(name);
                Clickable(name, task);
                var twin = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { Meter((task.HoursSpent ?? 0) / most, _ink.Accent, 6), Meter((task.Hours ?? 0) / most, _ink.Secondary, 6) } };
                ToolTip.SetTip(twin, L.T("{0}: {1:0.#} h reales, {2:0.#} h estimadas", task.Title, task.HoursSpent ?? 0, task.Hours ?? 0));
                Grid.SetRow(twin, row);
                Grid.SetColumn(twin, 1);
                rows.Children.Add(twin);
                var value = Ui.Text($"{task.HoursSpent ?? 0:0.#}/{task.Hours ?? 0:0.#} h", 11.5, _ink.Tertiary);
                value.Margin = new Thickness(12, 0, 0, 0);
                value.TextAlignment = TextAlignment.Right;
                value.MinWidth = 62;
                Grid.SetRow(value, row);
                Grid.SetColumn(value, 2);
                rows.Children.Add(value);
            }
            page.Children.Add(Card(new StackPanel { Children = { Head(PlanClockIcon, L.T("Horas por tarea"), Key(_ink.Accent, L.T("Real")), Key(_ink.Secondary, L.T("Estimado"))), rows } }));
        }
        return new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
    }

    // ── Actividad ───────────────────────────────────────────────────────────────────────────

    /// <summary>Lo que le pasó a una tarea, dicho en una frase corta.</summary>
    internal static string Happened(PlanEvent e) => e.Kind switch
    {
        "new" => L.T("agregó"),
        "removed" => L.T("quitó"),
        _ => e.State switch
        {
            PlanState.Doing => L.T("empezó"),
            PlanState.Review => L.T("pasó a revisión"),
            PlanState.Done => L.T("terminó"),
            PlanState.Blocked => L.T("bloqueó"),
            _ => L.T("devolvió a por hacer"),
        },
    };

    Control Activity(Plan plan, IReadOnlyList<PlanEvent> events)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var feed = new StackPanel { Spacing = 2, MaxWidth = 760, Margin = new Thickness(26, 18, 26, PageBottom + 20) };
        DateOnly? last = null;
        foreach (var e in events)
        {
            var task = plan.Tasks.FirstOrDefault(t => t.Id == e.Task);
            if (task is not null && Match is not null && !Match(task)) continue;
            var day = DateOnly.FromDateTime(e.At);
            if (day != last)
            {
                var heading = Ui.Text(day == today ? L.T("Hoy") : day == today.AddDays(-1) ? L.T("Ayer") : Day(day), 12, _ink.Tertiary, FontWeight.SemiBold);
                heading.Margin = new Thickness(44, last is null ? 0 : 14, 0, 6);
                feed.Children.Add(heading);
                last = day;
            }
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            row.Children.Add(AgentFace(e.Who ?? L.T("Usted"), 27));
            var words = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Children = { Ui.Text(Happened(e), 13.5, _ink.Secondary), Ui.Text(e.Title, 13.5, _ink.Text, FontWeight.Medium) } };
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            var time = Ui.Text(e.At.ToString("t", CultureInfo.CurrentCulture), 11.5, _ink.Tertiary);
            Grid.SetColumn(time, 2);
            row.Children.Add(time);
            var line = new Border { Child = row, Padding = new Thickness(8, 6), CornerRadius = new CornerRadius(11), Background = Brushes.Transparent };
            if (task is not null)
            {
                Clickable(line, task);
                line.PointerEntered += (_, _) => line.Background = _ink.Pill;
                line.PointerExited += (_, _) => line.Background = Brushes.Transparent;
            }
            feed.Children.Add(line);
        }
        if (feed.Children.Count == 0)
        {
            var none = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 90, 0, 0), Children = { Icon(ActivityIcon, _ink.Tertiary, 30, 1.4), Ui.Text(L.T("Sin actividad todavía"), 13.5, _ink.Tertiary) } };
            return new ScrollViewer { Content = none };
        }
        return new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = feed };
    }
}

/// <summary>Un anillo de avance, de 0 a 1, que empieza arriba y va en el sentido del reloj.</summary>
sealed class PlanRing(double value, IBrush tint, IBrush track, double thickness) : Control
{
    public override void Render(DrawingContext context)
    {
        var radius = (Math.Min(Bounds.Width, Bounds.Height) - thickness) / 2;
        if (radius <= 0) return;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        context.DrawEllipse(null, new Pen(track, thickness), center, radius, radius);
        var part = Math.Clamp(value, 0, 1);
        if (part <= 0) return;
        var pen = new Pen(tint, thickness, lineCap: PenLineCap.Round);
        if (part >= 0.999)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }
        var angle = part * Math.PI * 2;
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(new Point(center.X, center.Y - radius), false);
            path.ArcTo(new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle)), new Size(radius, radius), 0, part > 0.5, SweepDirection.Clockwise);
            path.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }
}

/// <summary>El avance real (escalones, hasta hoy) contra el ideal (recto, punteado). Al pasar el mouse dice los valores de ese día.</summary>
sealed class PlanBurnChart : Control
{
    const double Bottom = 26;

    readonly PlanBurn _burn;
    readonly Ink _ink;
    readonly DateOnly _today;
    DateOnly? _hover;

    public PlanBurnChart(PlanBurn burn, Ink ink, DateOnly today)
    {
        _burn = burn;
        _ink = ink;
        _today = today;
        ClipToBounds = false;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var length = _burn.To.DayNumber - _burn.From.DayNumber;
        var part = Bounds.Width <= 0 ? 0 : Math.Clamp(e.GetPosition(this).X / Bounds.Width, 0, 1);
        var day = _burn.From.AddDays((int)Math.Round(part * length));
        if (day == _hover) return;
        _hover = day;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    static FormattedText Label(string text, double size, IBrush brush, FontWeight weight = FontWeight.Normal) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Ink.Sans, FontStyle.Normal, weight), size, brush);

    public override void Render(DrawingContext context)
    {
        // Pinta todo su rectángulo: así recibe el mouse en cualquier punto.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var width = Bounds.Width;
        var height = Bounds.Height - Bottom;
        if (width <= 0 || height <= 0 || _burn.Total == 0) return;
        var length = (double)(_burn.To.DayNumber - _burn.From.DayNumber);
        double X(DateOnly d) => Math.Clamp((d.DayNumber - _burn.From.DayNumber) / length, 0, 1) * width;
        double Y(double v) => height - v / _burn.Total * (height - 10);

        context.DrawLine(new Pen(_ink.Hairline), new Point(0, height), new Point(width, height));

        // Lo real sube a escalones: cada uno, el día que algo se terminó.
        var now = _today >= _burn.To ? _burn.To : _today;
        var real = new StreamGeometry();
        var area = new StreamGeometry();
        using (var line = real.Open())
        using (var fill = area.Open())
        {
            line.BeginFigure(new Point(0, height), false);
            fill.BeginFigure(new Point(0, height), true);
            foreach (var day in _burn.Steps.Where(d => d <= now))
            {
                var before = _burn.DoneOn(day.AddDays(-1));
                var after = _burn.DoneOn(day);
                foreach (var point in new[] { new Point(X(day), Y(before)), new Point(X(day), Y(after)) })
                {
                    line.LineTo(point);
                    fill.LineTo(point);
                }
            }
            var end = new Point(X(now), Y(_burn.DoneOn(now)));
            line.LineTo(end);
            fill.LineTo(end);
            fill.LineTo(new Point(end.X, height));
            line.EndFigure(false);
            fill.EndFigure(true);
        }
        context.DrawGeometry(PlanPanel.Translucent(_ink.Accent, 0.14), null, area);
        context.DrawLine(new Pen(_ink.Secondary, 1.5, new DashStyle([3, 3], 0)), new Point(0, height), new Point(width, Y(_burn.Total)));
        context.DrawGeometry(null, new Pen(_ink.Accent, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), real);

        // Hoy: la línea, el punto y cuánto va.
        var today = new Point(X(now), Y(_burn.DoneOn(now)));
        context.DrawLine(new Pen(_ink.Hairline), new Point(today.X, 0), new Point(today.X, height));
        context.DrawEllipse(_ink.Raised, null, today, 7, 7);
        context.DrawEllipse(_ink.Accent, null, today, 5, 5);
        var note = Label(L.T("{0} de {1} días · hoy", _burn.DoneOn(now), _burn.Total), 11.5, _ink.Secondary);
        context.DrawText(note, new Point(today.X + 12 + note.Width > width ? today.X - 12 - note.Width : today.X + 12, Math.Max(0, today.Y - 20)));

        // Las fechas de los extremos.
        var first = Label(PlanPanel.Day(_burn.From), 11.5, _ink.Tertiary);
        context.DrawText(first, new Point(0, height + 7));
        var last = Label(PlanPanel.Day(_burn.To.AddDays(-1)), 11.5, _ink.Tertiary);
        context.DrawText(last, new Point(width - last.Width, height + 7));

        if (_hover is not { } hover) return;
        var x = X(hover);
        context.DrawLine(new Pen(_ink.Tertiary), new Point(x, 0), new Point(x, height));
        var text = PlanPanel.Day(hover) + " · " + (hover <= now ? L.T("real {0}", _burn.DoneOn(hover)) + " · " : "") + L.T("ideal {0:0} de {1} días", _burn.IdealOn(hover), _burn.Total);
        var tip = Label(text, 11.5, _ink.Surface, FontWeight.Medium);
        var box = new Rect(Math.Clamp(x + 8, 0, Math.Max(0, width - tip.Width - 16)), 2, tip.Width + 16, tip.Height + 8);
        context.DrawRectangle(_ink.Text, null, box, 7, 7);
        context.DrawText(tip, new Point(box.X + 8, box.Y + 4));
    }
}
