namespace Claupet;

using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Path = System.IO.Path;

/// <summary>
/// El dibujo de un plan (Core/Plan.cs) para quien lo muestre: la tarjeta de archivos de la Terminal y las secciones
/// «plan» de Canvas. Resumen (avance y fichas), Tablero (tareas por estado), Cronograma (barras por día con dependencias,
/// hitos y la línea de hoy), Diagrama (PlanPanel.Graph.cs) y los avatares de las IAs que lo editaron. Usa los colores del tema (Ink); lo recién cambiado
/// (Fresh) lleva un halo hasta que se llama a Settle. La sección Proyectos (ProjectsPane) y el PDF (PlanPdf) lo usan con
/// más aire (Spacious), con lo que no coincide atenuado (Match) y con clic y arrastre.
/// </summary>
sealed partial class PlanPanel(Ink ink)
{
    readonly Ink _ink = ink;

    /// <summary>Para una vista grande (la sección Proyectos) en vez de una tarjeta.</summary>
    public bool Spacious { get; init; }
    /// <summary>Para el PDF: todo a su tamaño entero, sin barras de desplazamiento ni animaciones.</summary>
    /// <summary>Lo que se busca: lo que no coincide queda atenuado.</summary>
    public Func<PlanTask, bool>? Match { get; init; }
    /// <summary>Se tocó una tarea.</summary>
    public Action<PlanTask>? TaskClicked { get; init; }
    /// <summary>Se arrastró una tarea a otra columna del tablero.</summary>
    public Action<PlanTask, PlanState>? TaskMoved { get; init; }

    bool Moving => TerminalPane.Animate;
    double Shade(PlanTask task) => Match is null || Match(task) ? 1 : 0.22;

    /// <summary>Lo que se toca abre esa tarea (si alguien escucha).</summary>
    void Clickable(Control control, PlanTask task)
    {
        if (TaskClicked is not { } clicked) return;
        control.Cursor = Ui.Hand;
        control.Tapped += (_, e) =>
        {
            e.Handled = true;
            clicked(task);
        };
    }


    internal const string PlanFlagIcon = "M5,21 V4 M5,5 H17 L14,8.5 L17,12 H5";
    internal const string PlanAlertIcon = "M12,4 L21,19 H3 Z M12,10 V13.5 M12,16.5 V16.6";
    internal const string PlanPlayIcon = "M8,5.5 L18.5,12 L8,18.5 Z";
    internal const string PlanClockIcon = "M12,3 A9,9 0 1 0 12.01,3 Z M12,7 V12 L15,14";
    internal const string PlanCalendarIcon = "M5,6 H19 V20 H5 Z M5,10.5 H19 M9,4 V8 M15,4 V8";
    const double PlanRow = 30, PlanDay = 24, PlanHeader = 34;
    double PlanLabels => Spacious ? 236 : 168;

    // ── Resumen ─────────────────────────────────────────────────────────────────────────────

    internal Control Summary(Plan plan)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var page = new StackPanel { Spacing = 14, Margin = new Thickness(20, 10, 20, 24) };
        if (plan.Title.Length > 0)
        {
            var title = Ui.Text(plan.Title, 20, _ink.Text, FontWeight.SemiBold, Ink.Display);
            title.TextWrapping = TextWrapping.Wrap;
            page.Children.Add(title);
        }
        if (plan.Description is { } description)
        {
            var text = Ui.Text(description, 13, _ink.Secondary);
            text.TextWrapping = TextWrapping.Wrap;
            text.Margin = new Thickness(0, -8, 0, 0);
            page.Children.Add(text);
        }

        // El avance de todo: la barra, el porcentaje y cuántas tareas están hechas.
        var progress = Plan.Progress(plan.Tasks);
        var done = plan.Tasks.Count(t => t.State == PlanState.Done);
        var total = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        var meter = Meter(progress, Added, 10);
        meter.VerticalAlignment = VerticalAlignment.Center;
        total.Children.Add(meter);
        var percent = Ui.Text($"{progress:P0}", 15, _ink.Text, FontWeight.SemiBold);
        percent.Margin = new Thickness(14, 0, 0, 0);
        Grid.SetColumn(percent, 1);
        total.Children.Add(percent);
        var count = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        count.Children.Add(Icon(CheckIcon, _ink.Tertiary, 13, 2));
        count.Children.Add(Ui.Text($"{done}/{plan.Tasks.Count}", 12.5, _ink.Tertiary));
        ToolTip.SetTip(count, L.T("{0} de {1} tareas", done, plan.Tasks.Count));
        Grid.SetColumn(count, 2);
        total.Children.Add(count);
        page.Children.Add(total);

        // Lo que importa ahora, en fichas: un ícono, una cifra y, debajo, de qué se trata (el detalle, al pasar el mouse).
        var tiles = new WrapPanel { Margin = new Thickness(0, 2, -8, 0) };
        void Tile(string icon, IBrush tint, string value, string? sub, string tip)
        {
            var mark = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(999),
                Background = Translucent(tint, 0.15),
                Child = Icon(icon, tint, 15, 1.8),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var text = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            text.Children.Add(Ui.Text(value, 15, _ink.Text, FontWeight.SemiBold));
            if (sub is { Length: > 0 })
            {
                var small = Ui.Text(sub, 11.5, _ink.Tertiary);
                small.TextTrimming = TextTrimming.CharacterEllipsis;
                text.Children.Add(small);
            }
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            row.Children.Add(mark);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var tile = new Border
            {
                Width = 176,
                Height = 52,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(11, 0, 10, 0),
                CornerRadius = new CornerRadius(12),
                Background = _ink.Surface,
                BorderBrush = _ink.Hairline,
                BorderThickness = new Thickness(1),
                Child = row,
            };
            ToolTip.SetTip(tile, tip);
            tiles.Children.Add(tile);
        }

        if ((plan.Milestones.FirstOrDefault(m => m.Date >= today) ?? plan.Milestones.LastOrDefault()) is { } next)
            Tile(PlanFlagIcon, next.Date < today ? _ink.Danger : _ink.Accent, Until(next.Date, today), next.Title, $"{next.Title} · {Day(next.Date)}");
        var late = plan.Tasks.Where(t => t.LateOn(today)).ToList();
        if (late.Count == 0) Tile(CheckIcon, Added, "0", L.T("Atrasadas"), L.T("Atrasadas") + ": " + L.T("Ninguna"));
        else Tile(PlanAlertIcon, _ink.Danger, late.Count.ToString(CultureInfo.CurrentCulture), late[0].Title,
            Listed(L.T("Atrasadas"), late.Select(t => L.T("{0} ({1})", t.Title, Until(t.End.AddDays(-1), today)))));
        var doing = plan.Tasks.Where(t => t.State == PlanState.Doing).ToList();
        if (doing.Count > 0) Tile(PlanPlayIcon, _ink.Accent, doing.Count.ToString(CultureInfo.CurrentCulture), doing[0].Title, Listed(L.T("En curso"), doing.Select(t => t.Title)));
        var blocked = plan.Tasks.Where(t => t.State == PlanState.Blocked).ToList();
        if (blocked.Count > 0) Tile(BlockedIcon, _ink.Warn, blocked.Count.ToString(CultureInfo.CurrentCulture), blocked[0].Title, Listed(L.T("Bloqueadas"), blocked.Select(t => t.Title)));
        var estimated = plan.Tasks.Sum(t => t.Hours ?? 0);
        var spent = plan.Tasks.Sum(t => t.HoursSpent ?? 0);
        if (estimated > 0 || spent > 0)
            Tile(PlanClockIcon, estimated > 0 && spent > estimated ? _ink.Warn : _ink.Secondary,
                estimated > 0 ? $"{spent:0.#}/{estimated:0.#} h" : $"{spent:0.#} h", null,
                estimated > 0 ? L.T("{0:0.#} h de {1:0.#} h estimadas", spent, estimated) : L.T("{0:0.#} h", spent));
        var (from, to) = plan.Span();
        Tile(PlanCalendarIcon, _ink.Secondary, L.T("{0} días", to.DayNumber - from.DayNumber), $"{from.ToString("d MMM", CultureInfo.CurrentCulture)} → {to.AddDays(-1).ToString("d MMM", CultureInfo.CurrentCulture)}",
            $"{Day(from)} → {Day(to.AddDays(-1))}");
        page.Children.Add(tiles);

        // Cada etapa con su color y su avance; las tareas sin etapa, al final.
        var groups = plan.Stages.Select((s, i) => (Name: s.Name, Brush: StageBrush(plan, s.Id), Tasks: plan.Tasks.Where(t => t.Stage == s.Id).ToList())).ToList();
        var loose = plan.Tasks.Where(t => t.Stage is null || plan.Stages.All(s => s.Id != t.Stage)).ToList();
        if (loose.Count > 0 && groups.Count > 0) groups.Add((L.T("Sin etapa"), _ink.Tertiary, loose));
        if (groups.Count > 0)
        {
            var stages = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
            foreach (var (name, brush, tasks) in groups)
            {
                var row = stages.RowDefinitions.Count;
                stages.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var dot = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(999), Background = brush, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                var label = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 16, 6) };
                label.Children.Add(dot);
                label.Children.Add(Ui.Text(name, 12.5, _ink.Text));
                Grid.SetRow(label, row);
                stages.Children.Add(label);
                var stageProgress = Plan.Progress(tasks);
                var stageMeter = Meter(stageProgress, brush, 7);
                stageMeter.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(stageMeter, row);
                Grid.SetColumn(stageMeter, 1);
                stages.Children.Add(stageMeter);
                var value = Ui.Text($"{stageProgress:P0}", 12, _ink.Secondary);
                value.MinWidth = 40;
                value.TextAlignment = TextAlignment.Right;
                value.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(value, row);
                Grid.SetColumn(value, 2);
                stages.Children.Add(value);
            }
            page.Children.Add(stages);
        }

        if (plan.Warnings.Count > 0)
        {
            var warnings = new StackPanel { Spacing = 3, Margin = new Thickness(0, 6, 0, 0) };
            foreach (var warning in plan.Warnings)
            {
                var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                var mark = Icon(PlanAlertIcon, _ink.Warn, 13, 1.8);
                mark.Margin = new Thickness(0, 1, 7, 0);
                mark.VerticalAlignment = VerticalAlignment.Top;
                line.Children.Add(mark);
                var text = Ui.Text(warning, 12, _ink.Warn);
                text.TextWrapping = TextWrapping.Wrap;
                Grid.SetColumn(text, 1);
                line.Children.Add(text);
                warnings.Children.Add(line);
            }
            page.Children.Add(warnings);
        }
        return new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = page };
    }

    /// <summary>Un título y debajo una línea por cosa (lo que se ve al pasar el mouse por una ficha).</summary>
    static string Listed(string title, IEnumerable<string> lines) => title + Environment.NewLine + string.Join(Environment.NewLine, lines);

    /// <summary>Una barra de avance redondeada, de 0 a 1.</summary>
    Control Meter(double value, IBrush brush, double height)
    {
        // Con proporciones y no midiendo al dibujarse: también sale bien en el PDF, que nunca está en pantalla.
        var part = Math.Clamp(value, 0, 1);
        var fill = new Border { Background = brush, CornerRadius = new CornerRadius(999), MinWidth = part > 0 ? height : 0 };
        var parts = new Grid { Children = { fill } };
        parts.ColumnDefinitions.Add(new ColumnDefinition(part, GridUnitType.Star));
        parts.ColumnDefinitions.Add(new ColumnDefinition(1 - part, GridUnitType.Star));
        return new Border { Height = height, CornerRadius = new CornerRadius(999), Background = _ink.Track, Child = parts, ClipToBounds = true };
    }

    // ── Tablero ─────────────────────────────────────────────────────────────────────────────

    internal Control Board(Plan plan)
    {
        if (Spacious) return WideBoard(plan);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var columns = new List<(PlanState State, string Name)>
        {
            (PlanState.Pending, L.T("Por hacer")), (PlanState.Doing, L.T("En curso")), (PlanState.Review, L.T("Revisión")), (PlanState.Done, L.T("Hecho")),
        };
        if (plan.Tasks.Any(t => t.State == PlanState.Blocked)) columns.Insert(1, (PlanState.Blocked, L.T("Bloqueado")));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(14, 6, 14, 18) };
        foreach (var (state, name) in columns)
        {
            var tasks = plan.Tasks.Where(t => t.State == state).ToList();
            var column = new StackPanel { Spacing = 8, Width = 190 };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(4, 0, 0, 2) };
            head.Children.Add(Ui.Text(name, 12.5, _ink.Secondary, FontWeight.SemiBold));
            head.Children.Add(Ui.Text(tasks.Count.ToString(CultureInfo.CurrentCulture), 12, _ink.Tertiary));
            column.Children.Add(head);
            foreach (var task in tasks) column.Children.Add(PlanCard(plan, task, today));
            if (tasks.Count == 0)
                column.Children.Add(new Border
                {
                    Height = 54,
                    CornerRadius = new CornerRadius(10),
                    BorderBrush = _ink.Hairline,
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock { Text = "—", Foreground = _ink.Tertiary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                });
            row.Children.Add(column);
        }
        return new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = row };
    }

    /// <summary>Una tarea del tablero: el color de su etapa a la izquierda, su título, cuándo y cuánto.</summary>
    Control PlanCard(Plan plan, PlanTask task, DateOnly today)
    {
        var late = task.LateOn(today);
        var stack = new StackPanel { Spacing = 4 };
        var title = Ui.Text(task.Title, 13, task.State == PlanState.Done ? _ink.Secondary : _ink.Text, FontWeight.Medium);
        title.TextWrapping = TextWrapping.Wrap;
        if (task.State == PlanState.Done) title.TextDecorations = TextDecorations.Strikethrough;
        stack.Children.Add(title);
        var parts = new List<string> { task.Days == 1 ? L.T("1 día") : L.T("{0} días", task.Days), Day(task.Start) };
        if (task.State == PlanState.Done && task.HoursSpent is { } spent) parts.Add("✓ " + L.T("{0:0.#} h", spent));
        else if (task.Hours is { } hours) parts.Add(L.T("{0:0.#} h", hours));
        stack.Children.Add(Ui.Text(string.Join(" · ", parts), 11.5, _ink.Tertiary));
        if (late) stack.Children.Add(Ui.Text(L.T("Atrasada · {0}", Until(task.End.AddDays(-1), today)), 11.5, _ink.Danger));
        if (task.Owner is { } owner) stack.Children.Add(Ui.Text("@ " + owner, 11.5, _ink.Secondary));
        var card = new Border
        {
            Background = _ink.Surface,
            CornerRadius = new CornerRadius(10),
            BorderBrush = late ? Translucent(_ink.Danger, 0.7) : _ink.Hairline,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 9, task.Editor is null ? 10 : 32, 10),
            Child = stack,
        };
        var strip = new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = StageBrush(plan, task.Stage), Margin = new Thickness(0, 9, 0, 9), HorizontalAlignment = HorizontalAlignment.Left };
        var shell = new Panel();
        // Si una IA acaba de cambiarla: un halo del color de quien fue; y siempre su avatar, arriba a la derecha.
        if (Fresh.Contains(task.Id))
        {
            var halo = Halo(AgentTint(task.Editor), 13);
            halo.Margin = new Thickness(-4);
            shell.Children.Add(halo);
        }
        shell.Children.Add(card);
        shell.Children.Add(strip);
        if (task.Editor is { } editor)
        {
            var face = AgentFace(editor, 18);
            face.HorizontalAlignment = HorizontalAlignment.Right;
            face.VerticalAlignment = VerticalAlignment.Top;
            face.Margin = new Thickness(0, 9, 9, 0);
            shell.Children.Add(face);
        }
        ToolTip.SetTip(shell, TaskTip(plan, task));
        return shell;
    }

    // ── Cronograma ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Las tareas como barras sobre los días, agrupadas por etapa: los nombres a la izquierda (quietos) y los días a la
    /// derecha (se deslizan). Las dependencias van como flechas; los hitos, como rombos; hoy, una línea en acento.
    /// </summary>
    internal Control Timeline(Plan plan)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var (from, to) = plan.Span();
        // Un poco de aire a los lados, y desde el lunes de esa semana.
        from = from.AddDays(-(((int)from.DayOfWeek + 6) % 7));
        // Grande, también con lugar para la fecha del último hito (en el PDF no se puede deslizar para verla).
        to = to.AddDays(Spacious ? 5 : 2);
        var days = to.DayNumber - from.DayNumber;
        double X(DateOnly d) => (d.DayNumber - from.DayNumber) * PlanDay;

        // Las filas: el nombre de cada etapa y sus tareas; las sueltas al final; los hitos al último.
        var rows = new List<(string Label, bool Stage, PlanTask? Task, PlanMilestone? Milestone, IBrush Brush)>();
        foreach (var stage in plan.Stages)
        {
            var tasks = plan.Tasks.Where(t => t.Stage == stage.Id).ToList();
            if (tasks.Count == 0) continue;
            rows.Add((stage.Name, true, null, null, StageBrush(plan, stage.Id)));
            rows.AddRange(tasks.Select(t => (t.Title, false, (PlanTask?)t, (PlanMilestone?)null, StageBrush(plan, stage.Id))));
        }
        var loose = plan.Tasks.Where(t => t.Stage is null || plan.Stages.All(s => s.Id != t.Stage)).ToList();
        if (loose.Count > 0)
        {
            if (rows.Count > 0) rows.Add((L.T("Sin etapa"), true, null, null, _ink.Tertiary));
            rows.AddRange(loose.Select(t => (t.Title, false, (PlanTask?)t, (PlanMilestone?)null, StageBrush(plan, null))));
        }
        if (plan.Milestones.Count > 0)
        {
            rows.Add((L.T("Hitos"), true, null, null, _ink.Accent));
            rows.AddRange(plan.Milestones.Select(m => (m.Title, false, (PlanTask?)null, (PlanMilestone?)m, _ink.Accent)));
        }

        var height = PlanHeader + rows.Count * PlanRow + 10;
        var canvas = new Canvas { Width = days * PlanDay, Height = height, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
        var labels = new Canvas { Width = PlanLabels, Height = height, VerticalAlignment = VerticalAlignment.Top };

        // Los fines de semana más oscuros y, arriba, el día de cada lunes (y el mes cuando cambia).
        for (var i = 0; i < days; i++)
        {
            var day = from.AddDays(i);
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                Place(canvas, new Border { Width = PlanDay, Height = height - PlanHeader, Background = Translucent(_ink.Track, 0.45) }, X(day), PlanHeader);
            if (day.DayOfWeek == DayOfWeek.Monday || i == 0)
            {
                // La fecha no va donde está la marca de «hoy» (se taparían).
                if (Math.Abs(day.DayNumber - today.DayNumber) > 2 || today < from || today >= to)
                    Place(canvas, Ui.Text(day.ToString(i == 0 || day.Day <= 7 ? "d MMM" : "ddd d", CultureInfo.CurrentCulture), 11, _ink.Tertiary), X(day) + 3, 9);
                Place(canvas, new Border { Width = 1, Height = height - PlanHeader + 6, Background = _ink.Hairline }, X(day), PlanHeader - 6);
            }
        }
        Place(canvas, new Border { Width = days * PlanDay, Height = 1, Background = _ink.Hairline }, 0, PlanHeader - 1);
        Place(labels, new Border { Width = PlanLabels, Height = 1, Background = _ink.Hairline }, 0, PlanHeader - 1);

        // Las filas: el nombre a la izquierda y su barra (o su rombo) a la derecha.
        var bars = new Dictionary<string, (double Y, double Start, double End)>();
        for (var r = 0; r < rows.Count; r++)
        {
            var (label, stage, task, milestone, brush) = rows[r];
            var y = PlanHeader + r * PlanRow;
            var name = Ui.Text(label, stage ? 12 : 12.5, stage ? _ink.Secondary : _ink.Text, stage ? FontWeight.SemiBold : FontWeight.Normal);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.Width = PlanLabels - (stage ? 30 : 38) - (task?.Editor is null ? 0 : 20);
            if (stage)
            {
                Place(labels, new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(999), Background = brush }, 14, y + PlanRow / 2 - 4);
                Place(labels, name, 30, y + PlanRow / 2 - 8);
            }
            else Place(labels, name, 30, y + PlanRow / 2 - 9);
            if (task is not null)
            {
                ToolTip.SetTip(name, TaskTip(plan, task));
                name.Opacity = Shade(task);
                Clickable(name, task);
                // En grande, también de un vistazo cómo está cada una.
                if (Spacious)
                {
                    var (stateIcon, stateTint, stateName) = StateLook(plan, task);
                    var mark = Icon(stateIcon, stateTint, 14, 2);
                    mark.Opacity = name.Opacity;
                    ToolTip.SetTip(mark, stateName);
                    Place(labels, mark, 12, y + PlanRow / 2 - 7);
                }
                if (task.Editor is { } editor) Place(labels, AgentFace(editor, 16), PlanLabels - 26, y + PlanRow / 2 - 8);
                var start = X(task.Start);
                var width = Math.Max(PlanDay - 4, task.Days * PlanDay - 4);
                var late = task.LateOn(today);
                var progress = task.State switch { PlanState.Done => 1.0, PlanState.Review => 0.8, PlanState.Doing => 0.4, _ => 0.0 };
                // La barra: clara toda y llena hasta su avance; atrasada, con borde rojo.
                var tone = task.State == PlanState.Done ? Added : brush;
                var fill = new Border { Width = width * progress, Background = tone, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(6) };
                var bar = new Border
                {
                    Width = width,
                    Height = PlanRow - 12,
                    CornerRadius = new CornerRadius(6),
                    Background = Translucent(tone, task.State == PlanState.Blocked ? 0.15 : 0.24),
                    BorderBrush = late ? _ink.Danger : task.State == PlanState.Blocked ? _ink.Warn : Brushes.Transparent,
                    BorderThickness = new Thickness(late || task.State == PlanState.Blocked ? 1.5 : 0),
                    ClipToBounds = true,
                    Child = fill,
                };
                ToolTip.SetTip(bar, TaskTip(plan, task));
                bar.Opacity = Shade(task);
                Clickable(bar, task);
                if (Fresh.Contains(task.Id))
                {
                    var halo = Halo(AgentTint(task.Editor), 9);
                    halo.Width = width + 8;
                    halo.Height = PlanRow - 12 + 8;
                    Place(canvas, halo, start + 2 - 4, y + 6 - 4);
                }
                Place(canvas, bar, start + 2, y + 6);
                bars[task.Id] = (y + PlanRow / 2, start + 2, start + 2 + width);
            }
            else if (milestone is not null)
            {
                var x = X(milestone.Date) + PlanDay / 2;
                var diamond = new Border
                {
                    Width = 12,
                    Height = 12,
                    Background = milestone.Date < today ? _ink.Tertiary : _ink.Accent,
                    RenderTransform = new RotateTransform(45),
                };
                ToolTip.SetTip(diamond, $"{milestone.Title} · {Day(milestone.Date)}");
                Place(canvas, diamond, x - 6, y + PlanRow / 2 - 6);
                var when = Ui.Text(Day(milestone.Date), 11, _ink.Secondary);
                Place(canvas, when, x + 12, y + PlanRow / 2 - 8);
                bars["◆" + milestone.Id] = (y + PlanRow / 2, x - 7, x + 7);
            }
        }

        // Las dependencias: de donde termina la anterior a donde empieza esta, con un codo.
        void Arrow((double Y, double Start, double End) a, (double Y, double Start, double End) b)
        {
            var x1 = a.End;
            var x2 = b.Start;
            // El codo, justo antes de la que espera (así la línea baja por el hueco entre las dos).
            var elbow = Math.Max(x1 + 6, x2 - 9);
            var line = new Polyline
            {
                Points = [new Point(x1, a.Y), new Point(elbow, a.Y), new Point(elbow, b.Y), new Point(x2, b.Y)],
                Stroke = _ink.Accent,
                StrokeThickness = 1.4,
                StrokeDashArray = [3.5, 3],
                StrokeJoin = PenLineJoin.Round,
                Opacity = 0.75,
            };
            // Las líneas «corren» hacia la tarea que espera (como las del diagrama de Canvas).
            if (Moving)
                Loop(line, new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(1200),
                    IterationCount = IterationCount.Infinite,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Shape.StrokeDashOffsetProperty, 0d) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Shape.StrokeDashOffsetProperty, -6.5d) } },
                    },
                });
            canvas.Children.Add(line);
            canvas.Children.Add(new Polyline
            {
                Points = [new Point(x2 - 5, b.Y - 3.5), new Point(x2, b.Y), new Point(x2 - 5, b.Y + 3.5)],
                Stroke = _ink.Accent,
                StrokeThickness = 1.4,
                StrokeLineCap = PenLineCap.Round,
                Opacity = 0.75,
            });
        }
        foreach (var task in plan.Tasks)
            foreach (var d in task.Depends)
                if (bars.TryGetValue(d, out var a) && bars.TryGetValue(task.Id, out var b)) Arrow(a, b);
        foreach (var m in plan.Milestones)
            foreach (var d in m.Depends)
                if (bars.TryGetValue(d, out var a) && bars.TryGetValue("◆" + m.Id, out var b)) Arrow(a, b);

        // Hoy: una línea en acento (si cae dentro del plan).
        if (today >= from && today < to)
        {
            var x = X(today) + PlanDay / 2;
            Place(canvas, new Border { Width = 2, Height = height - PlanHeader + 8, Background = _ink.Accent, Opacity = 0.85 }, x - 1, PlanHeader - 8);
            var mark = new Border { Background = _ink.Accent, CornerRadius = new CornerRadius(999), Padding = new Thickness(6, 1), Child = Ui.Text(L.T("hoy"), 10.5, Ink.On(_ink.Accent), FontWeight.SemiBold) };
            Place(canvas, mark, x - 14, PlanHeader - 19);
        }

        // Arriba (si sobra alto, no se centra).
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{PlanLabels},*"), VerticalAlignment = VerticalAlignment.Top };
        grid.Children.Add(new Border { Child = labels, BorderBrush = _ink.Hairline, BorderThickness = new Thickness(0, 0, 1, 0) });
        var dayColumn = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = canvas };
        // Empieza mostrando hoy (o el principio, si hoy queda afuera).
        if (today >= from && today < to)
            dayColumn.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => dayColumn.Offset = new Vector(Math.Max(0, X(today) - PlanDay * 7), 0), DispatcherPriority.Background);
        Grid.SetColumn(dayColumn, 1);
        grid.Children.Add(dayColumn);
        // Lo que flota abajo en la sección Proyectos no tapa la última fila.
        if (Spacious) grid.Margin = new Thickness(0, 0, 0, 72);
        return new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = grid };
    }

    static void Place(Canvas canvas, Control control, double x, double y)
    {
        Canvas.SetLeft(control, x);
        Canvas.SetTop(control, y);
        canvas.Children.Add(control);
    }

    // ── Quién editó ─────────────────────────────────────────────────────────────────────────

    /// <summary>Las tareas que acaban de cambiar (llevan un halo un momento) y los halos que se apagan solos.</summary>
    public HashSet<string> Fresh { get; } = [];
    readonly List<Control> _fading = [];

    /// <summary>El color de quien editó, como en Canvas: el acento para Claude Code, azul para Codex, morado para Gemini.</summary>
    IBrush AgentTint(string? who) => who?.ToLowerInvariant() switch
    {
        null or "" => _ink.Accent,
        var n when n.Contains("claude") => _ink.Accent,
        var n when n.Contains("codex") => new SolidColorBrush(Color.FromRgb(0x3D, 0x7B, 0xD9)),
        var n when n.Contains("gemini") => new SolidColorBrush(Color.FromRgb(0x7A, 0x5A, 0xF8)),
        var n when n.Contains("copilot") => new SolidColorBrush(Color.FromRgb(0x2E, 0x9E, 0x9A)),
        _ => _ink.Secondary,
    };

    /// <summary>La inicial de quien editó (Codex y Claude Code no pueden ser las dos «C»).</summary>
    static string AgentInitial(string who) => who.ToLowerInvariant() switch
    {
        var n when n.Contains("codex") => "X",
        var n when n.Contains("copilot") => "P",
        _ => who.Trim().Length > 0 ? who.Trim()[..1].ToUpperInvariant() : "?",
    };

    /// <summary>Un avatar redondo con la inicial y el color de quien editó.</summary>
    internal Border AgentFace(string who, double size)
    {
        var face = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(999),
            Background = AgentTint(who),
            Child = new TextBlock
            {
                Text = AgentInitial(who),
                FontFamily = Ink.Sans,
                FontSize = Math.Round(size * 0.52),
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        ToolTip.SetTip(face, who);
        return face;
    }

    /// <summary>El anillo de lo que una IA acaba de cambiar: se apaga solo (lo apaga PlanView con _fading).</summary>
    Border Halo(IBrush tint, double radius)
    {
        var ring = new Border
        {
            CornerRadius = new CornerRadius(radius),
            BorderBrush = tint,
            BorderThickness = new Thickness(1.5),
            Background = Translucent(tint, 0.12),
            IsHitTestVisible = false,
        };
        if (Moving) ring.Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(700) }];
        _fading.Add(ring);
        return ring;
    }

    // ── Piezas ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Lo que se ve al pasar el mouse por una tarea: su título, su estado, sus fechas, de qué depende y sus notas.</summary>
    string TaskTip(Plan plan, PlanTask task)
    {
        var lines = new List<string> { task.Title, $"{StateName(task.State)} · {Day(task.Start)} → {Day(task.End.AddDays(-1))}" };
        if (task.Hours is not null || task.HoursSpent is not null)
            lines.Add(task.Hours is { } h ? L.T("{0:0.#} h de {1:0.#} h estimadas", task.HoursSpent ?? 0, h) : L.T("{0:0.#} h", task.HoursSpent ?? 0));
        if (task.Depends.Count > 0)
            lines.Add(L.T("Depende de: {0}", string.Join(", ", task.Depends.Select(d => plan.Tasks.FirstOrDefault(t => t.Id == d)?.Title ?? d))));
        if (task.Owner is { } owner) lines.Add(L.T("Responsable: {0}", owner));
        if (task.Notes is { } notes) lines.Add(notes);
        return string.Join("\n", lines);
    }

    internal static string StateName(PlanState state) => state switch
    {
        PlanState.Doing => L.T("En curso"),
        PlanState.Review => L.T("Revisión"),
        PlanState.Done => L.T("Hecho"),
        PlanState.Blocked => L.T("Bloqueado"),
        _ => L.T("Por hacer"),
    };

    /// <summary>Un día corto: «vie 21 nov».</summary>
    internal static string Day(DateOnly day) => day.ToString(day.Year == DateTime.Today.Year ? "ddd d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture);

    /// <summary>Cuánto falta (o cuánto pasó) hasta ese día: «hoy», «mañana», «en 12 días», «hace 2 días».</summary>
    internal static string Until(DateOnly day, DateOnly today)
    {
        var n = day.DayNumber - today.DayNumber;
        return n switch
        {
            0 => L.T("hoy"),
            1 => L.T("mañana"),
            -1 => L.T("ayer"),
            > 1 => L.T("en {0} días", n),
            _ => L.T("hace {0} días", -n),
        };
    }

    static readonly string[] Palette =
        ["azul", "verde", "naranja", "morado", "turquesa", "rosa", "amarillo", "rojo", "gris", "blue", "green", "orange", "purple", "teal", "pink", "yellow", "red", "gray", "grey"];

    /// <summary>
    /// El color de una etapa: el que el plan escribió (un nombre como «azul» o «#5B8DEF», aclarado en el tema oscuro) o,
    /// si no dice ninguno, el acento.
    /// </summary>
    internal IBrush StageBrush(Plan plan, string? stageId)
    {
        var index = plan.Stages.ToList().FindIndex(s => s.Id == stageId);
        if (index < 0) return _ink.Tertiary;
        var named = plan.Stages[index].Color?.Trim().ToLowerInvariant();
        if (named is { Length: > 0 } && named.StartsWith('#') && Color.TryParse(named, out var custom)) return new SolidColorBrush(custom);
        // Sin color (o uno que no se conoce), el acento de Claupet: el resto de los colores queda para decir quién editó.
        if (named is null or "" || !Palette.Contains(named)) return _ink.Accent;
        var name = named;
        var (dark, light) = name switch
        {
            "verde" or "green" => (0x6CC08BU, 0x2F8A57U),
            "naranja" or "orange" => (0xEBA061U, 0xC0681FU),
            "morado" or "purple" => (0xAE93F0U, 0x7656C9U),
            "turquesa" or "teal" => (0x5CC6C0U, 0x1E8C86U),
            "rosa" or "pink" => (0xEB8FC0U, 0xC04B8AU),
            "amarillo" or "yellow" => (0xE3C25CU, 0xA8841CU),
            "rojo" or "red" => (0xEB7F7FU, 0xC24444U),
            "gris" or "gray" or "grey" => (0x9EA3ABU, 0x6B7079U),
            _ => (0x7BA5F5U, 0x3D6FD6U),
        };
        return new SolidColorBrush(Color.FromUInt32(0xFF000000 | (_ink.Dark ? dark : light)));
    }

    /// <summary>Los avatares de quienes (IAs) editaron el plan; el de quien acaba de cambiar algo lleva su halo.</summary>
    internal Control Faces(Plan plan)
    {
        var faces = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        foreach (var who in plan.Tasks.Where(t => t.Editor is not null).GroupBy(t => t.Editor!).OrderByDescending(g => g.Count()).Select(g => g.Key))
        {
            var slot = new Panel { Width = 26, Height = 26, Margin = new Thickness(faces.Children.Count == 0 ? 0 : -7, 0, 0, 0) };
            if (plan.Tasks.Any(t => t.Editor == who && Fresh.Contains(t.Id))) slot.Children.Add(Halo(AgentTint(who), 999));
            var face = AgentFace(who, 20);
            face.HorizontalAlignment = HorizontalAlignment.Center;
            face.VerticalAlignment = VerticalAlignment.Center;
            slot.Children.Add(face);
            faces.Children.Add(slot);
        }
        return faces;
    }

    /// <summary>Apaga lo recién cambiado: los halos se desvanecen.</summary>
    internal void Settle()
    {
        Fresh.Clear();
        foreach (var c in _fading) c.Opacity = 0;
    }

    /// <summary>Lee un plan; null si no se puede (el motivo lo da Plan.Parse).</summary>
    internal static Plan? TryParse(string json)
    {
        try { return Plan.Parse(json); }
        catch (FormatException) { return null; }
    }

    /// <summary>Las tareas nuevas o distintas de una lectura a la siguiente.</summary>
    internal static HashSet<string> Changed(Plan? before, Plan? after) =>
        before is null || after is null ? [] : after.Tasks.Where(t => before.Tasks.FirstOrDefault(o => o.Id == t.Id) is not { } old || Key(old) != Key(t)).Select(t => t.Id).ToHashSet();

    static string Key(PlanTask t) =>
        $"{t.Title}|{t.Stage}|{t.State}|{t.Start}|{t.Days}|{string.Join(',', t.Depends)}|{t.Hours}|{t.HoursSpent}|{t.Owner}|{t.Notes}|{t.Editor}";

    // ── Piezas comunes ──────────────────────────────────────────────────────────────────────

    internal const string CheckIcon = "M5,12.5 L10,17.5 L19,7";
    internal const string BlockedIcon = "M12,3 A9,9 0 1 0 12.01,3 Z M5.6,5.6 L18.4,18.4";

    internal IBrush Added => _ink.Dark ? new SolidColorBrush(Color.FromRgb(0xA8, 0xC7, 0xA0)) : new SolidColorBrush(Color.FromRgb(0x3F, 0x6B, 0x3A));

    internal static IBrush Translucent(IBrush brush, double opacity) =>
        brush is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, opacity) : brush;

    internal static Viewbox Icon(string path, IBrush brush, double size, double thickness) => new()
    {
        Width = size,
        Height = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(path),
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 24,
            Height = 24,
        },
    };

    /// <summary>Repite una animación mientras el control está en pantalla.</summary>
    static void Loop(Control control, Animation animation)
    {
        CancellationTokenSource? stop = null;
        control.AttachedToVisualTree += (_, _) =>
        {
            stop?.Cancel();
            stop = new CancellationTokenSource();
            _ = animation.RunAsync(control, stop.Token);
        };
        control.DetachedFromVisualTree += (_, _) => stop?.Cancel();
    }
}
