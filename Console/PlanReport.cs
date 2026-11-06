namespace Claupet;

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;

/// <summary>Una hoja del informe y lo que lleva, ya repartido.</summary>
sealed class ReportSheet
{
    public List<Control> Blocks { get; } = [];
    public double Used { get; set; }
}

/// <summary>
/// Un plan contado como informe: hojas carta en vertical, fondo blanco, títulos, cifras y tablas que siguen en la hoja
/// siguiente con su encabezado. No es la pantalla impresa: cada vista tiene su versión de papel.
/// </summary>
sealed class PlanReport
{
    /// <summary>Carta en vertical, en píxeles de pantalla (96 por pulgada).</summary>
    internal const double PortraitWidth = 816, PortraitHeight = 1056;
    internal const double Top = 64, Bottom = 72;
    const double Side = 72;
    const double ContentWidth = PortraitWidth - 2 * Side;

    readonly Plan _plan;
    readonly string _title;
    readonly IReadOnlyList<PlanEvent> _events;
    readonly Ink _ink = Paper();
    readonly PlanPanel _panel;
    readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Today);
    readonly List<ReportSheet> _sheets = [];
    ReportSheet _sheet = new();

    PlanReport(string title, Plan plan, IReadOnlyList<PlanEvent> events)
    {
        _title = title;
        _plan = plan;
        _events = events;
        _panel = new PlanPanel(_ink);
        _sheets.Add(_sheet);
    }

    /// <summary>
    /// Los colores del papel: siempre blanco y tinta casi negra, sea cual sea el tema. Del tema solo se queda el acento,
    /// y solo si se lee sobre blanco (el de un tema propio puede ser muy claro).
    /// </summary>
    internal static Ink Paper()
    {
        static IBrush B(string hex) => new ImmutableSolidColorBrush(Color.Parse(hex));
        var plain = Ink.Of(null, false);
        var accent = Ink.On(Ink.For(false).Accent) == Brushes.White ? Ink.For(false).Accent : plain.Accent;
        return plain with
        {
            Window = Brushes.White, Surface = Brushes.White, Raised = Brushes.White, Pill = B("#F3F4F6"),
            Hairline = B("#E2E4E8"), Track = B("#ECEEF1"), Text = B("#1D2025"), Secondary = B("#4A4F58"), Tertiary = B("#6A707A"),
            Accent = accent, Warn = B("#8A5A12"), Danger = B("#B3261E"),
        };
    }

    /// <summary>Arma el informe con esas vistas (ids de PlanPanel.Views) y lo reparte en hojas.</summary>
    internal static IReadOnlyList<ReportSheet> Build(string title, Plan plan, IReadOnlyList<PlanEvent> events, IReadOnlyList<string> views)
    {
        var report = new PlanReport(title, plan, events);
        report.Opening();
        foreach (var view in views)
            switch (view)
            {
                case "resumen": report.Summary(); break;
                case "tablero": report.TasksByState(); break;
                case "cronograma": report.Schedule(); break;
                case "diagrama": report.Dependencies(); break;
                case "metricas": report.Numbers(); break;
                case "actividad": report.Happenings(); break;
            }
        return report._sheets;
    }

    /// <summary>Una hoja lista para dibujarse: lo repartido y, abajo, el nombre del plan y el número de hoja.</summary>
    internal static Control Compose(ReportSheet sheet, Ink ink, string title, int number, int total)
    {
        var body = new StackPanel { Margin = new Thickness(Side, Top, Side, 0), ClipToBounds = false };
        foreach (var block in sheet.Blocks) body.Children.Add(block);

        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(Side, 0, Side, 34), VerticalAlignment = VerticalAlignment.Bottom };
        var name = Ui.Text(title, 10.5, ink.Tertiary);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        foot.Children.Add(name);
        var page = Ui.Text(L.T("Página {0} de {1}", number, total), 10.5, ink.Tertiary);
        page.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(page, 1);
        foot.Children.Add(page);

        return new Border { Background = Brushes.White, Width = PortraitWidth, Height = PortraitHeight, Child = new Panel { Children = { body, foot } } };
    }

    // ── El reparto en hojas ─────────────────────────────────────────────────────────────────

    static double Capacity => PortraitHeight - Top - Bottom;
    double Room => Capacity - _sheet.Used;

    double Tall(Control control)
    {
        control.Measure(new Size(ContentWidth, double.PositiveInfinity));
        return control.DesiredSize.Height;
    }

    void Put(Control control)
    {
        // Lo primero de una hoja empieza en el margen, sin el aire que lo separa de lo anterior.
        if (_sheet.Blocks.Count == 0) control.Margin = new Thickness(control.Margin.Left, 0, control.Margin.Right, control.Margin.Bottom);
        _sheet.Used += Tall(control);
        _sheet.Blocks.Add(control);
    }

    void NextSheet()
    {
        _sheet = new ReportSheet();
        _sheets.Add(_sheet);
    }

    /// <summary>
    /// Lo que va junto (título y primer dato) y sus filas. Si todo cabe en una hoja no se parte: pasa entero a la siguiente.
    /// Si no cabe en ninguna, se parte entre filas y la hoja nueva repite el encabezado. Una fila «pegada» (el nombre de un
    /// grupo) nunca queda sola al final de la hoja.
    /// </summary>
    void Flow(IReadOnlyList<Control> lead, Func<Control>? header, IReadOnlyList<(Control Row, bool Sticky)> rows)
    {
        double Need(int i) => Tall(rows[i].Row) + (rows[i].Sticky && i + 1 < rows.Count ? Tall(rows[i + 1].Row) : 0);
        var top = header?.Invoke();
        var opening = lead.Sum(Tall) + (top is null ? 0 : Tall(top));
        var whole = opening + rows.Sum(r => Tall(r.Row));
        var start = opening + (rows.Count > 0 ? Need(0) : 0);
        if (_sheet.Blocks.Count > 0 && whole > Room && (whole <= Capacity || start > Room)) NextSheet();

        foreach (var control in lead) Put(control);
        if (top is not null) Put(top);
        for (var i = 0; i < rows.Count; i++)
        {
            if (Need(i) > Room && _sheet.Blocks.Count > 0)
            {
                NextSheet();
                if (header is not null) Put(header());
            }
            Put(rows[i].Row);
        }
    }

    // ── Piezas ──────────────────────────────────────────────────────────────────────────────

    TextBlock Words(string text, double size, IBrush brush, FontWeight weight = FontWeight.Normal, int lines = 0)
    {
        var block = Ui.Text(text, size, brush, weight);
        block.TextWrapping = TextWrapping.Wrap;
        if (lines > 0)
        {
            block.MaxLines = lines;
            block.TextTrimming = TextTrimming.CharacterEllipsis;
        }
        return block;
    }

    TextBlock Right(string text, double size = 12.5, IBrush? brush = null, FontWeight weight = FontWeight.Normal)
    {
        var block = Ui.Text(text, size, brush ?? _ink.Secondary, weight);
        block.TextAlignment = TextAlignment.Right;
        return block;
    }

    Control Section(string name)
    {
        var title = Ui.Text(name, 18, _ink.Text, FontWeight.SemiBold, Ink.Display);
        title.Margin = new Thickness(0, 30, 0, 12);
        return title;
    }

    Control Subsection(string name)
    {
        var title = Ui.Text(name, 13.5, _ink.Text, FontWeight.SemiBold);
        title.Margin = new Thickness(0, 22, 0, 8);
        return title;
    }

    /// <summary>Una línea de tabla; las columnas se escriben igual en cada fila, así quedan alineadas entre hojas.</summary>
    Control Line(string columns, Thickness padding, params Control?[] cells)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns) };
        for (var i = 0; i < cells.Length; i++)
        {
            if (cells[i] is not { } cell) continue;
            if (i < cells.Length - 1) cell.Margin = new Thickness(cell.Margin.Left, cell.Margin.Top, cell.Margin.Right + 12, cell.Margin.Bottom);
            // Todas arriba: si una celda ocupa dos líneas, las demás no se van al medio.
            cell.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        return new Border { Child = grid, Padding = padding };
    }

    Control Row(string columns, params Control?[] cells)
    {
        var line = (Border)Line(columns, new Thickness(0, 7), cells);
        line.BorderBrush = _ink.Hairline;
        line.BorderThickness = new Thickness(0, 0, 0, 1);
        return line;
    }

    /// <summary>El encabezado de una tabla: en pequeño y con una raya firme debajo.</summary>
    Func<Control> Head(string columns, params Control?[] cells) => () =>
    {
        // Cada hoja lleva el suyo: un control tiene un solo padre.
        var copies = cells.Select(c => c switch
        {
            null => null,
            TextBlock t => (Control)Label(t.Text ?? "", t.TextAlignment),
            _ => throw new InvalidOperationException(),
        }).ToArray();
        var line = (Border)Line(columns, new Thickness(0, 0, 0, 6), copies);
        line.BorderBrush = _ink.Tertiary;
        line.BorderThickness = new Thickness(0, 0, 0, 1);
        return line;
    };

    TextBlock Label(string text, TextAlignment align = TextAlignment.Left)
    {
        var block = Ui.Text(text, 11, _ink.Tertiary, FontWeight.SemiBold);
        block.TextAlignment = align;
        return block;
    }

    /// <summary>Una barra fina de avance; con proporciones, porque en el PDF nada llega a estar en pantalla.</summary>
    Control Bar(double value, IBrush brush, double height = 5)
    {
        var part = Math.Clamp(value, 0, 1);
        var grid = new Grid { Children = { new Border { Background = brush, CornerRadius = new CornerRadius(height / 2), MinWidth = part > 0 ? height : 0 } } };
        grid.ColumnDefinitions.Add(new ColumnDefinition(part, GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(1 - part, GridUnitType.Star));
        return new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Background = _ink.Track, Child = grid, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
    }

    Control Dot(IBrush brush) => new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(999), Background = brush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };

    /// <summary>Un dato con su color de identidad al lado (el texto siempre en tinta).</summary>
    Control Tagged(Control mark, string text, double size = 12.5, IBrush? brush = null, FontWeight weight = FontWeight.Normal)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(mark);
        var words = Words(text, size, brush ?? _ink.Text, weight);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        return grid;
    }

    Control StateCell(PlanTask task)
    {
        var (icon, tint, name) = _panel.StateLook(_plan, task);
        var mark = PlanPanel.Icon(icon, tint, 13, 2);
        mark.Margin = new Thickness(0, 1, 6, 0);
        mark.VerticalAlignment = VerticalAlignment.Top;
        return Tagged(mark, name, 12, _ink.Secondary);
    }

    /// <summary>Unas cifras grandes en fila, separadas por aire y sin recuadros.</summary>
    Control Figures(params (string Value, string Name, IBrush? Tint)[] figures)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        foreach (var (value, name, tint) in figures)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var cell = new StackPanel { Spacing = 1, Children = { Ui.Text(value, 22, tint ?? _ink.Text, FontWeight.SemiBold, Ink.Display), Ui.Text(name, 11.5, _ink.Tertiary) } };
            Grid.SetColumn(cell, grid.ColumnDefinitions.Count - 1);
            grid.Children.Add(cell);
        }
        return grid;
    }

    string Dates(PlanTask task) => task.Days <= 1 ? Short(task.Start) : $"{Short(task.Start)} – {Short(task.End.AddDays(-1))}";
    static string Short(DateOnly day) => day.ToString("d MMM", CultureInfo.CurrentCulture);
    string StageName(PlanTask task) => _plan.Stages.FirstOrDefault(s => s.Id == task.Stage)?.Name ?? "";
    static string Hours(double? hours) => hours is { } h ? $"{h:0.#} h" : "—";

    // ── Lo primero de la primera hoja ───────────────────────────────────────────────────────

    void Opening()
    {
        var (from, to) = _plan.Span();
        var last = to.AddDays(-1);
        var head = new StackPanel { Spacing = 4 };
        head.Children.Add(Ui.Text(L.T("Informe del proyecto"), 12, _ink.Tertiary, FontWeight.SemiBold));
        var title = Words(_title, 28, _ink.Text, FontWeight.SemiBold);
        title.FontFamily = Ink.Display;
        head.Children.Add(title);
        if (!string.IsNullOrWhiteSpace(_plan.Description))
        {
            var about = Words(_plan.Description, 13.5, _ink.Secondary);
            about.Margin = new Thickness(0, 4, 0, 0);
            about.LineHeight = 20;
            head.Children.Add(about);
        }
        var facts = Ui.Text($"{DateTime.Today.ToString("D", CultureInfo.CurrentCulture)}  ·  {from.ToString("d MMM", CultureInfo.CurrentCulture)} – {last.ToString("d MMM yyyy", CultureInfo.CurrentCulture)}  ·  {L.T("{0:P0} de avance", Plan.Progress(_plan.Tasks))}", 11.5, _ink.Tertiary);
        facts.Margin = new Thickness(0, 8, 0, 0);
        head.Children.Add(facts);
        Put(new Border { Child = head, BorderBrush = _ink.Hairline, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 16) });
    }

    // ── Resumen ─────────────────────────────────────────────────────────────────────────────

    void Summary()
    {
        var tasks = _plan.Tasks;
        var late = tasks.Where(t => t.LateOn(_today)).ToList();
        var blocked = tasks.Where(t => t.State == PlanState.Blocked).ToList();
        var progress = Plan.Progress(tasks);
        var spent = tasks.Sum(t => t.HoursSpent ?? 0);
        var estimated = tasks.Sum(t => t.Hours ?? 0);

        var figures = new List<(string, string, IBrush?)>
        {
            ($"{progress:P0}", L.T("Avance"), null),
            ($"{tasks.Count(t => t.State == PlanState.Done)}/{tasks.Count}", L.T("Tareas"), null),
            (late.Count.ToString(CultureInfo.CurrentCulture), L.T("Atrasadas"), late.Count > 0 ? _ink.Danger : null),
            (blocked.Count.ToString(CultureInfo.CurrentCulture), L.T("Bloqueadas"), blocked.Count > 0 ? _ink.Warn : null),
        };
        if (estimated > 0 || spent > 0) figures.Add((estimated > 0 ? $"{spent:0.#}/{estimated:0.#}" : $"{spent:0.#}", L.T("Horas"), null));
        var bar = Bar(progress, _ink.Accent, 6);
        bar.Margin = new Thickness(0, 14, 0, 0);
        var lead = new List<Control> { Section(L.T("Resumen")), Figures(figures.ToArray()), bar };
        if (_plan.Milestones.FirstOrDefault(m => m.Date >= _today) is { } next)
        {
            var mark = PlanPanel.Icon(PlanPanel.PlanFlagIcon, _ink.Accent, 14, 2);
            mark.Margin = new Thickness(0, 0, 7, 0);
            var line = Tagged(mark, $"{next.Title}  ·  {PlanPanel.Day(next.Date)}  ·  {PlanPanel.Until(next.Date, _today)}", 12.5, _ink.Secondary);
            line.Margin = new Thickness(0, 14, 0, 0);
            lead.Add(line);
        }
        Flow(lead, null, []);

        StagesTable();

        const string Columns = "*,132,96";
        var attention = late.Concat(blocked).Distinct().ToList();
        if (attention.Count > 0)
            Flow([Subsection(L.T("Requiere atención"))], Head(Columns, Label(L.T("Tarea")), Label(L.T("Estado")), Label(L.T("Termina"), TextAlignment.Right)),
                attention.Select(t => (Row(Columns, TitleCell(t), StateCell(t), Right(Short(t.End.AddDays(-1)), 12)), false)).ToList());

        var current = tasks.Where(t => !attention.Contains(t) && (t.State is PlanState.Doing or PlanState.Review || (t.State == PlanState.Pending && PlanGraph.Ready(_plan, t)))).ToList();
        if (current.Count > 0)
            Flow([Subsection(L.T("Ahora"))], Head(Columns, Label(L.T("Tarea")), Label(L.T("Estado")), Label(L.T("Termina"), TextAlignment.Right)),
                current.Select(t => (Row(Columns, TitleCell(t), StateCell(t), Right(Short(t.End.AddDays(-1)), 12)), false)).ToList());
    }

    void StagesTable()
    {
        var groups = _plan.Stages.Select(s => (Name: s.Name, Tint: _panel.StageBrush(_plan, s.Id), Tasks: _plan.Tasks.Where(t => t.Stage == s.Id).ToList())).Where(g => g.Tasks.Count > 0).ToList();
        if (groups.Count == 0) return;
        const string Columns = "*,64,86,132";
        Flow([Subsection(L.T("Etapas"))], Head(Columns, Label(L.T("Etapa")), Label(L.T("Tareas"), TextAlignment.Right), Label(L.T("Horas"), TextAlignment.Right), Label(L.T("Avance"), TextAlignment.Right)),
            groups.Select(g =>
            {
                var part = Plan.Progress(g.Tasks);
                var meter = new Grid { ColumnDefinitions = new ColumnDefinitions("*,42") };
                meter.Children.Add(Bar(part, g.Tint));
                var percent = Right($"{part:P0}", 12);
                Grid.SetColumn(percent, 1);
                meter.Children.Add(percent);
                var spent = g.Tasks.Sum(t => t.HoursSpent ?? 0);
                var estimated = g.Tasks.Sum(t => t.Hours ?? 0);
                return (Row(Columns, Tagged(Dot(g.Tint), g.Name, 12.5, null, FontWeight.Medium),
                    Right($"{g.Tasks.Count(t => t.State == PlanState.Done)}/{g.Tasks.Count}", 12),
                    Right(estimated > 0 || spent > 0 ? $"{spent:0.#}/{estimated:0.#} h" : "—", 12),
                    meter), false);
            }).ToList());
    }

    Control TitleCell(PlanTask task, bool notes = false)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(Words(task.Title, 12.5, _ink.Text, FontWeight.Medium));
        if (task.LateOn(_today)) stack.Children.Add(Words(L.T("Atrasada · {0}", PlanPanel.Until(task.End.AddDays(-1), _today)), 11, _ink.Danger));
        if (notes && !string.IsNullOrWhiteSpace(task.Notes)) stack.Children.Add(Words(task.Notes, 11, _ink.Tertiary, lines: 4));
        return stack;
    }

    // ── Tareas por estado (el Tablero, en papel) ────────────────────────────────────────────

    void TasksByState()
    {
        var owners = _plan.Tasks.Any(t => !string.IsNullOrWhiteSpace(t.Owner));
        var stages = _plan.Tasks.Any(t => StageName(t).Length > 0);
        var columns = $"*,{(stages ? 112 : 0)},116,{(owners ? 88 : 0)},58";

        Control? Optional(bool shown, Func<Control> make) => shown ? make() : null;
        var rows = new List<(Control, bool)>();
        foreach (var (state, name) in new[] { (PlanState.Pending, L.T("Por hacer")), (PlanState.Blocked, L.T("Bloqueado")), (PlanState.Doing, L.T("En curso")), (PlanState.Review, L.T("Revisión")), (PlanState.Done, L.T("Hecho")) })
        {
            var tasks = _plan.Tasks.Where(t => t.State == state).ToList();
            if (tasks.Count == 0) continue;
            var title = Ui.Text($"{name}  ·  {tasks.Count}", 12, _ink.Text, FontWeight.SemiBold);
            title.Margin = new Thickness(0, rows.Count == 0 ? 8 : 16, 0, 5);
            rows.Add((title, true));
            foreach (var task in tasks)
                rows.Add((Row(columns,
                    TitleCell(task, notes: true),
                    Optional(stages, () => Words(StageName(task), 11.5, _ink.Secondary)),
                    Ui.Text(Dates(task), 11.5, _ink.Secondary),
                    Optional(owners, () => Words(task.Owner ?? "", 11.5, _ink.Secondary)),
                    Right(Hours(task.Hours), 11.5)), false));
        }
        if (rows.Count == 0)
        {
            Flow([Section(L.T("Tareas por estado")), Words(L.T("Sin tareas"), 12.5, _ink.Tertiary)], null, []);
            return;
        }
        Flow([Section(L.T("Tareas por estado"))], Head(columns, Label(L.T("Tarea")), stages ? Label(L.T("Etapa")) : null, Label(L.T("Fechas")), owners ? Label(L.T("Responsable")) : null, Label(L.T("Horas"), TextAlignment.Right)), rows);
    }

    // ── Cronograma ──────────────────────────────────────────────────────────────────────────

    void Schedule()
    {
        if (_plan.Tasks.Count == 0 && _plan.Milestones.Count == 0)
        {
            Flow([Section(L.T("Cronograma")), Words(L.T("Sin tareas"), 12.5, _ink.Tertiary)], null, []);
            return;
        }
        var (from, to) = _plan.Span();
        var days = Math.Max(1, to.DayNumber - from.DayNumber);
        const double Names = 212, TaskRow = 23, GroupRow = 27;
        var area = ContentWidth - Names;
        var day = area / days;
        double X(DateOnly d) => (d.DayNumber - from.DayNumber) * day;
        var showToday = _today >= from && _today < to;
        var todayX = X(_today) + day / 2;

        // Las marcas del eje: los lunes si hay lugar, si no los meses (o de tres en tres, o los años).
        var ticks = new List<(DateOnly Day, string Text)>();
        var months = day * 30 >= 46 ? 1 : day * 91 >= 46 ? 3 : 12;
        for (var d = from; d < to; d = d.AddDays(1))
        {
            if (day * 7 >= 46)
            {
                if (d.DayOfWeek == DayOfWeek.Monday) ticks.Add((d, d.ToString("d MMM", CultureInfo.CurrentCulture)));
            }
            else if (d.Day == 1 && (d.Month - 1) % months == 0)
                ticks.Add((d, d.ToString(months == 12 ? "yyyy" : "MMM yy", CultureInfo.CurrentCulture)));
        }

        // Cada fila dibuja su tramo de fondo: uno debajo del otro se ven como una sola cuadrícula.
        Canvas Track(double height)
        {
            var canvas = new Canvas { Width = area, Height = height, ClipToBounds = true };
            if (day >= 5)
                for (var d = from; d < to; d = d.AddDays(1))
                    if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                        Place(canvas, new Border { Width = day, Height = height, Background = _ink.Pill }, X(d), 0);
            foreach (var (tick, _) in ticks) Place(canvas, new Border { Width = 1, Height = height, Background = _ink.Hairline }, X(tick), 0);
            if (showToday) Place(canvas, new Border { Width = 1.5, Height = height, Background = _ink.Accent }, todayX - 0.75, 0);
            return canvas;
        }

        Control Timed(Control name, Canvas track)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions($"{Names},*") };
            name.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);
            Grid.SetColumn(track, 1);
            row.Children.Add(track);
            return row;
        }

        Control Axis()
        {
            var canvas = new Canvas { Width = area, Height = 22 };
            foreach (var (tick, text) in ticks)
            {
                var x = X(tick);
                if (x > area - 34 || (showToday && Math.Abs(x - todayX) < 30)) continue;
                Place(canvas, Ui.Text(text, 10.5, _ink.Tertiary), x + 3, 3);
            }
            if (showToday)
            {
                var mark = Ui.Text(L.T("hoy"), 10.5, _ink.Accent, FontWeight.SemiBold);
                Place(canvas, mark, Math.Min(todayX + 3, area - 22), 3);
            }
            var line = new Border { BorderBrush = _ink.Tertiary, BorderThickness = new Thickness(0, 0, 0, 1) };
            line.Child = Timed(Ui.Text(L.T("Tarea"), 11, _ink.Tertiary, FontWeight.SemiBold), canvas);
            return line;
        }

        Control Group(string name, IBrush tint)
        {
            var label = Tagged(Dot(tint), name, 12, _ink.Text, FontWeight.SemiBold);
            return Timed(label, Track(GroupRow));
        }

        Control TaskLine(PlanTask task, IBrush tint)
        {
            var (icon, stateTint, _) = _panel.StateLook(_plan, task);
            var mark = PlanPanel.Icon(icon, stateTint, 12, 2);
            mark.Margin = new Thickness(0, 0, 6, 0);
            mark.VerticalAlignment = VerticalAlignment.Center;
            var label = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 0, 10, 0) };
            label.Children.Add(mark);
            var words = Ui.Text(task.Title, 11.5, _ink.Text);
            words.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(words, 1);
            label.Children.Add(words);

            var track = Track(TaskRow);
            var width = Math.Max(4, task.Days * day - Math.Min(2, day / 4));
            var part = task.State switch { PlanState.Done => 1.0, PlanState.Review => 0.8, PlanState.Doing => 0.4, _ => 0.0 };
            var late = task.LateOn(_today);
            var fill = new Border { Width = width * part, Background = tint, HorizontalAlignment = HorizontalAlignment.Left };
            var bar = new Border
            {
                Width = width,
                Height = 11,
                CornerRadius = new CornerRadius(3),
                ClipToBounds = true,
                Background = PlanPanel.Translucent(tint, 0.24),
                BorderBrush = late ? _ink.Danger : Brushes.Transparent,
                BorderThickness = new Thickness(late ? 1 : 0),
                Child = fill,
            };
            Place(track, bar, X(task.Start), (TaskRow - 11) / 2);
            return Timed(label, track);
        }

        Control MilestoneLine(PlanMilestone milestone)
        {
            var label = new TextBlock { Text = milestone.Title, FontFamily = Ink.Sans, FontSize = 11.5, Foreground = _ink.Text, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(18, 0, 10, 0) };
            var track = Track(TaskRow);
            var x = X(milestone.Date) + day / 2;
            Place(track, new Border { Width = 9, Height = 9, Background = milestone.Date < _today ? _ink.Tertiary : _ink.Accent, RenderTransform = new RotateTransform(45) }, x - 4.5, (TaskRow - 9) / 2);
            var when = Ui.Text(Short(milestone.Date), 10.5, _ink.Secondary);
            Place(track, when, x > area - 56 ? x - 50 : x + 9, (TaskRow - 14) / 2);
            return Timed(label, track);
        }

        var rows = new List<(Control, bool)>();
        foreach (var stage in _plan.Stages)
        {
            var tasks = _plan.Tasks.Where(t => t.Stage == stage.Id).ToList();
            if (tasks.Count == 0) continue;
            var tint = _panel.StageBrush(_plan, stage.Id);
            rows.Add((Group(stage.Name, tint), true));
            rows.AddRange(tasks.Select(t => (TaskLine(t, tint), false)));
        }
        var loose = _plan.Tasks.Where(t => t.Stage is null || _plan.Stages.All(s => s.Id != t.Stage)).ToList();
        if (loose.Count > 0)
        {
            var tint = _panel.StageBrush(_plan, null);
            if (rows.Count > 0) rows.Add((Group(L.T("Sin etapa"), tint), true));
            rows.AddRange(loose.Select(t => (TaskLine(t, tint), false)));
        }
        if (_plan.Milestones.Count > 0)
        {
            rows.Add((Group(L.T("Hitos"), _ink.Accent), true));
            rows.AddRange(_plan.Milestones.Select(m => (MilestoneLine(m), false)));
        }

        Flow([Section(L.T("Cronograma"))], Axis, rows);
        var legend = ScheduleLegend();
        legend.Margin = new Thickness(0, 12, 0, 0);
        Flow([legend], null, []);
    }

    static void Place(Canvas canvas, Control control, double x, double y)
    {
        Canvas.SetLeft(control, x);
        Canvas.SetTop(control, y);
        canvas.Children.Add(control);
    }

    Control ScheduleLegend()
    {
        Control Sample(IBrush? fill, IBrush? edge) => new Border { Width = 18, Height = 9, CornerRadius = new CornerRadius(2.5), Background = fill, BorderBrush = edge, BorderThickness = new Thickness(edge is null ? 0 : 1), VerticalAlignment = VerticalAlignment.Center };
        Control Entry(Control mark, string name) => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { mark, Ui.Text(name, 10.5, _ink.Tertiary) } };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Children =
            {
                Entry(Sample(_ink.Secondary, null), L.T("Avance")),
                Entry(Sample(PlanPanel.Translucent(_ink.Secondary, 0.24), null), L.T("Pendiente")),
                Entry(Sample(null, _ink.Danger), L.T("Atrasada")),
                Entry(new Border { Width = 8, Height = 8, Background = _ink.Accent, RenderTransform = new RotateTransform(45), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0) }, L.T("Hito")),
                Entry(new Border { Width = 1.5, Height = 11, Background = _ink.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0) }, L.T("Hoy")),
            },
        };
    }

    // ── Dependencias ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// El diagrama de arriba hacia abajo, que es como cabe a lo ancho de una hoja y con letra que se lee. Si el plan es
    /// demasiado grande para dibujarlo legible, lo mismo como tabla: encogerlo no serviría de nada en papel.
    /// </summary>
    void Dependencies()
    {
        var graph = PlanGraph.Build(_plan);
        if (graph.Edges.Count == 0)
        {
            Flow([Section(L.T("Dependencias")), Words(L.T("Sin dependencias"), 12.5, _ink.Tertiary)], null, []);
            return;
        }
        const double GapX = 16, GapY = 26, NodeHeight = 56, MaxNode = 190, MinNode = 120, Heading = 70;
        var node = Math.Min(MaxNode, (ContentWidth - (graph.Rows - 1) * GapX) / graph.Rows);
        var height = graph.Columns * (NodeHeight + GapY) - GapY;
        if (node < MinNode || height > PortraitHeight - Top - Bottom - Heading)
        {
            DependencyTable();
            return;
        }

        var offset = (ContentWidth - (graph.Rows * node + (graph.Rows - 1) * GapX)) / 2;
        double X(double lane) => offset + lane * (node + GapX);
        double Y(int level) => level * (NodeHeight + GapY);
        var canvas = new Canvas { Width = ContentWidth, Height = height };
        var places = graph.Nodes.ToDictionary(n => n.Id);

        foreach (var edge in graph.Edges)
        {
            var (from, to) = (places[edge.From], places[edge.To]);
            var points = new List<Point> { new(X(from.Row) + node / 2, Y(from.Column) + NodeHeight) };
            foreach (var (level, lane) in edge.Through)
            {
                points.Add(new Point(X(lane) + node / 2, Y(level)));
                points.Add(new Point(X(lane) + node / 2, Y(level) + NodeHeight));
            }
            var end = edge.Loop ? new Point(X(to.Row) + node, Y(to.Column) + NodeHeight / 2) : new Point(X(to.Row) + node / 2, Y(to.Column));
            points.Add(end);

            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(points[0], false);
                for (var i = 1; i < points.Count; i++)
                {
                    var (p, q) = (points[i - 1], points[i]);
                    if (Math.Abs(p.X - q.X) < 0.5) path.LineTo(q);
                    else path.CubicBezierTo(new Point(p.X, (p.Y + q.Y) / 2), new Point(q.X, (p.Y + q.Y) / 2), q);
                }
                path.EndFigure(false);
            }
            var brush = edge.Loop ? _ink.Danger : from.Task?.State == PlanState.Done ? _ink.Tertiary : _ink.Secondary;
            canvas.Children.Add(new Path { Data = geometry, Stroke = brush, StrokeThickness = 1 });
            if (!edge.Loop)
                canvas.Children.Add(new Polyline { Points = [new Point(end.X - 3.5, end.Y - 5), end, new Point(end.X + 3.5, end.Y - 5)], Stroke = brush, StrokeThickness = 1 });
        }
        foreach (var item in graph.Nodes)
            Place(canvas, item.Task is { } task ? TaskNode(task, node, NodeHeight) : MilestoneNode(item.Milestone!, node, NodeHeight), X(item.Row), Y(item.Column));

        Flow([Section(L.T("Dependencias")), canvas], null, []);
    }

    Control TaskNode(PlanTask task, double width, double height)
    {
        var (icon, tint, _) = _panel.StateLook(_plan, task);
        var late = task.LateOn(_today);
        var mark = PlanPanel.Icon(icon, tint, 13, 2);
        mark.Margin = new Thickness(0, 1, 6, 0);
        mark.VerticalAlignment = VerticalAlignment.Top;
        var words = new StackPanel { Spacing = 1, Children = { Words(task.Title, 11, _ink.Text, FontWeight.Medium, lines: 2), Ui.Text(Dates(task), 10, late ? _ink.Danger : _ink.Tertiary) } };
        Grid.SetColumn(words, 1);
        var edge = late ? _ink.Danger : task.State == PlanState.Blocked ? _ink.Warn : _ink.Tertiary;
        return new Border
        {
            Width = width,
            Height = height,
            Background = Brushes.White,
            BorderBrush = edge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 5),
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), VerticalAlignment = VerticalAlignment.Center, Children = { mark, words } },
        };
    }

    Control MilestoneNode(PlanMilestone milestone, double width, double height)
    {
        var mark = PlanPanel.Icon(PlanPanel.PlanFlagIcon, _ink.Accent, 13, 2);
        mark.Margin = new Thickness(0, 1, 6, 0);
        mark.VerticalAlignment = VerticalAlignment.Top;
        var words = new StackPanel { Spacing = 1, Children = { Words(milestone.Title, 11, _ink.Text, FontWeight.SemiBold, lines: 2), Ui.Text(Short(milestone.Date), 10, _ink.Tertiary) } };
        Grid.SetColumn(words, 1);
        return new Border
        {
            Width = width,
            Height = height,
            Background = _ink.Pill,
            CornerRadius = new CornerRadius(height / 2),
            Padding = new Thickness(14, 5),
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), VerticalAlignment = VerticalAlignment.Center, Children = { mark, words } },
        };
    }

    void DependencyTable()
    {
        string Names(IEnumerable<string> ids) => string.Join(", ", ids.Select(id => _plan.Tasks.FirstOrDefault(t => t.Id == id)?.Title ?? id));
        const string Columns = "5*,4*,4*";
        var rows = _plan.Tasks.Where(t => t.Depends.Count > 0 || _plan.Tasks.Any(o => o.Depends.Contains(t.Id))).Select(t =>
            (Row(Columns,
                Words(t.Title, 12, _ink.Text, FontWeight.Medium),
                Words(t.Depends.Count > 0 ? Names(t.Depends) : "—", 11.5, _ink.Secondary),
                Words(_plan.Tasks.Any(o => o.Depends.Contains(t.Id)) ? Names(_plan.Tasks.Where(o => o.Depends.Contains(t.Id)).Select(o => o.Id)) : "—", 11.5, _ink.Secondary)), false)).ToList();
        Flow([Section(L.T("Dependencias"))], Head(Columns, Label(L.T("Tarea")), Label(L.T("Necesita")), Label(L.T("La esperan"))), rows);
    }

    // ── Métricas ────────────────────────────────────────────────────────────────────────────

    void Numbers()
    {
        var tasks = _plan.Tasks;
        var spent = tasks.Sum(t => t.HoursSpent ?? 0);
        var estimated = tasks.Sum(t => t.Hours ?? 0);
        var figures = new List<(string, string, IBrush?)> { ($"{tasks.Count(t => t.State == PlanState.Done)}/{tasks.Count}", L.T("Tareas"), null) };
        var finished = tasks.Where(t => t.State == PlanState.Done && t.Hours > 0 && t.HoursSpent is not null).ToList();
        if (finished.Count > 0)
            figures.Add(($"{finished.Sum(t => t.HoursSpent!.Value) / finished.Sum(t => t.Hours!.Value):P0}", L.T("Real / estimado"), null));
        if (estimated > 0 || spent > 0) figures.Add(($"{spent:0.#} h", estimated > 0 ? L.T("de {0:0.#} h", estimated) : L.T("Horas"), null));
        var (from, to) = _plan.Span();
        figures.Add((L.T("{0} días", to.DayNumber - from.DayNumber), $"{Short(from)} – {Short(to.AddDays(-1))}", null));

        var lead = new List<Control> { Section(L.T("Métricas")), Figures(figures.ToArray()) };
        if (tasks.Count > 0)
        {
            lead.Add(Subsection(L.T("Avance")));
            var chart = new PlanBurnChart(new PlanBurn(_plan, _today, _events), _ink, _today) { Height = 190, Margin = new Thickness(0, 0, 0, 6) };
            lead.Add(chart);
            lead.Add(BurnLegend());
        }
        Flow(lead, null, []);

        var worked = tasks.Where(t => t.Hours is not null || t.HoursSpent is not null).ToList();
        if (worked.Count == 0) return;
        const string Columns = "*,76,76,76";
        var rows = worked.Select(t =>
        {
            var gap = t.Hours is { } h && t.HoursSpent is { } s ? s - h : (double?)null;
            return (Row(Columns, Words(t.Title, 12.5, _ink.Text), Right(Hours(t.Hours), 12), Right(Hours(t.HoursSpent), 12), Right(gap is { } g ? $"{g:+0.#;-0.#;0} h" : "—", 12)), false);
        }).ToList();
        rows.Add((Line(Columns, new Thickness(0, 8, 0, 0), Words(L.T("Total"), 12.5, _ink.Text, FontWeight.SemiBold), Right(Hours(estimated), 12, _ink.Text, FontWeight.SemiBold), Right(Hours(spent), 12, _ink.Text, FontWeight.SemiBold), null), false));
        Flow([Subsection(L.T("Horas por tarea"))], Head(Columns, Label(L.T("Tarea")), Label(L.T("Estimado"), TextAlignment.Right), Label(L.T("Real"), TextAlignment.Right), Label(L.T("Diferencia"), TextAlignment.Right)), rows);
    }

    Control BurnLegend()
    {
        Control Key(IBrush brush, string name, bool dashed)
        {
            var mark = new Avalonia.Controls.Shapes.Line { StartPoint = new Point(0, 1), EndPoint = new Point(16, 1), Stroke = brush, StrokeThickness = 2, Width = 16, Height = 2, VerticalAlignment = VerticalAlignment.Center };
            if (dashed) mark.StrokeDashArray = [2, 1.5];
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { mark, Ui.Text(name, 10.5, _ink.Tertiary) } };
        }
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { Key(_ink.Accent, L.T("Real"), false), Key(_ink.Secondary, L.T("Ideal"), true) } };
    }

    // ── Actividad ───────────────────────────────────────────────────────────────────────────

    void Happenings()
    {
        if (_events.Count == 0)
        {
            Flow([Section(L.T("Actividad")), Words(L.T("Sin actividad todavía"), 12.5, _ink.Tertiary)], null, []);
            return;
        }
        const string Columns = "116,104,*";
        Flow([Section(L.T("Actividad"))], Head(Columns, Label(L.T("Fecha")), Label(L.T("Quién")), Label(L.T("Qué"))),
            _events.Select(e => (Row(Columns,
                Words(e.At.ToString("d MMM, t", CultureInfo.CurrentCulture), 11.5, _ink.Secondary),
                Words(e.Who ?? L.T("Usted"), 11.5, _ink.Secondary),
                Words($"{PlanPanel.Happened(e)} {e.Title}", 12, _ink.Text)), false)).ToList());
    }
}
