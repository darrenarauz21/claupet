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
using Shape = Avalonia.Controls.Shapes.Shape;

/// <summary>
/// El diagrama de dependencias de un plan: qué tiene que terminar antes de qué. Una línea punteada que corre es una
/// espera; una fina y quieta, una espera ya cumplida; una roja, un círculo que no se puede cumplir. Al pasar el mouse por
/// una tarea se resalta su cadena (lo que necesita y lo que la espera).
/// </summary>
sealed partial class PlanPanel
{
    /// <summary>El ícono de la vista: tres tareas unidas.</summary>
    internal const string GraphIcon = "M4,10 H9 V14 H4 Z M15,4 H20 V8 H15 Z M15,16 H20 V20 H15 Z M9,12 H12 V6 H15 M12,12 V18 H15";

    internal const string ReviewIcon = "M2.5,12 C5,7.5 8.5,5.5 12,5.5 C15.5,5.5 19,7.5 21.5,12 C19,16.5 15.5,18.5 12,18.5 C8.5,18.5 5,16.5 2.5,12 Z M12,9.5 A2.5,2.5 0 1 0 12.01,9.5 Z";
    internal const string WaitIcon = "M12,4 A8,8 0 1 0 12.01,4 Z";
    const double GraphNodeWidth = 186, GraphNodeHeight = 52, GraphGapX = 58, GraphGapY = 14, GraphMargin = 18;

    internal Control Graph(Plan plan)
    {
        var graph = PlanGraph.Build(plan);
        if (graph.Nodes.Count == 0)
        {
            var empty = Ui.Text(L.T("Este plan todavía no tiene tareas."), 12.5, _ink.Tertiary);
            empty.Margin = new Thickness(20, 12);
            return empty;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        double X(int column) => GraphMargin + column * (GraphNodeWidth + GraphGapX);
        double Y(double row) => GraphMargin + row * (GraphNodeHeight + GraphGapY);
        var canvas = new Canvas
        {
            Width = X(graph.Columns) - GraphGapX + GraphMargin,
            Height = Y(graph.Rows) - GraphGapY + GraphMargin,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        // Primero las líneas, para que queden debajo de las tareas.
        var places = graph.Nodes.ToDictionary(n => n.Id);
        var lines = new List<(PlanGraphEdge Edge, Control Line)>();
        foreach (var edge in graph.Edges)
        {
            var (from, to) = (places[edge.From], places[edge.To]);
            var cleared = from.Task?.State == PlanState.Done;
            var brush = edge.Loop ? _ink.Danger : cleared ? Added : _ink.Accent;

            // Salta columnas por sus lugares: entra y sale de cada uno derecho, y entre columnas va con una curva.
            var points = new List<(double X, double Y)> { (X(from.Column) + GraphNodeWidth, Y(from.Row) + GraphNodeHeight / 2) };
            foreach (var (column, row) in edge.Through)
            {
                points.Add((X(column), Y(row) + GraphNodeHeight / 2));
                points.Add((X(column) + GraphNodeWidth, Y(row) + GraphNodeHeight / 2));
            }
            var end = edge.Loop ? (X: X(to.Column) + GraphNodeWidth / 2, Y: Y(to.Row) + GraphNodeHeight) : (X: X(to.Column), Y: Y(to.Row) + GraphNodeHeight / 2);
            points.Add(end);
            var line = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(Curve(points)),
                Stroke = brush,
                StrokeThickness = cleared ? 1.2 : 1.4,
                StrokeLineCap = PenLineCap.Round,
                Opacity = cleared ? 0.55 : 0.8,
                IsHitTestVisible = false,
            };
            if (!cleared)
            {
                line.StrokeDashArray = [3.5, 3];
                if (Moving) RunDashes(line);
            }
            var group = new Panel { IsHitTestVisible = false, Children = { line } };
            if (!edge.Loop)
                group.Children.Add(new Polyline
                {
                    Points = [new Point(end.X - 5, end.Y - 3.5), new Point(end.X, end.Y), new Point(end.X - 5, end.Y + 3.5)],
                    Stroke = brush,
                    StrokeThickness = 1.4,
                    StrokeLineCap = PenLineCap.Round,
                    Opacity = line.Opacity,
                });
            canvas.Children.Add(group);
            lines.Add((edge, group));
        }

        // Las tareas; al pasar el mouse, todo lo que no es de su cadena se apaga.
        var nodes = new Dictionary<string, Control>();
        double ShadeOf(string id) => graph.Nodes.First(n => n.Id == id).Task is { } t ? Shade(t) : Match is null ? 1 : 0.22;
        void Light(string? id)
        {
            var chain = id is null ? null : graph.Chain(id);
            foreach (var (key, node) in nodes) node.Opacity = chain is null ? ShadeOf(key) : chain.Contains(key) ? 1 : 0.28;
            foreach (var (edge, line) in lines) line.Opacity = chain is null || (chain.Contains(edge.From) && chain.Contains(edge.To)) ? 1 : 0.15;
        }
        foreach (var item in graph.Nodes)
        {
            var node = item.Task is { } task ? GraphTask(plan, task, today) : GraphMilestone(item.Milestone!, today);
            if (Moving) node.Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) }];
            node.Opacity = ShadeOf(item.Id);
            if (item.Task is { } clicked) Clickable(node, clicked);
            node.PointerEntered += (_, _) => Light(item.Id);
            node.PointerExited += (_, _) => Light(null);
            Place(canvas, node, X(item.Column), Y(item.Row));
            nodes[item.Id] = node;
        }
        foreach (var (_, line) in lines)
            if (Moving) line.Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) }];

        if (!Spacious) return new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = canvas };
        return ZoomableGraph(graph, canvas, X, Y);
    }

    /// <summary>Una línea por esos puntos: los pares de una misma altura quedan rectos y entre columnas va con una curva suave.</summary>
    static string Curve(IReadOnlyList<(double X, double Y)> points)
    {
        static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        var path = $"M{N(points[0].X)},{N(points[0].Y)}";
        for (var i = 1; i < points.Count; i++)
        {
            var (a, b) = (points[i - 1], points[i]);
            if (a.Y == b.Y)
            {
                path += $" L{N(b.X)},{N(b.Y)}";
                continue;
            }
            var bend = Math.Max(18, Math.Abs(b.X - a.X) / 2);
            path += $" C{N(a.X + bend)},{N(a.Y)} {N(b.X - bend)},{N(b.Y)} {N(b.X)},{N(b.Y)}";
        }
        return path;
    }

    /// <summary>El punteado corre hacia lo que espera, como en el cronograma.</summary>
    static void RunDashes(Shape line) => Loop(line, new Animation
    {
        Duration = TimeSpan.FromMilliseconds(1200),
        IterationCount = IterationCount.Infinite,
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Shape.StrokeDashOffsetProperty, 0d) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Shape.StrokeDashOffsetProperty, -6.5d) } },
        },
    });

    /// <summary>Una tarea: su estado con un ícono, su título, cuándo y cuánto; el borde dice si va tarde o está trabada.</summary>
    Control GraphTask(Plan plan, PlanTask task, DateOnly today)
    {
        var late = task.LateOn(today);
        var ready = PlanGraph.Ready(plan, task);
        var (icon, tint, tip) = task.State switch
        {
            PlanState.Done => (CheckIcon, Added, L.T("Hecho")),
            PlanState.Doing => (PlanPlayIcon, _ink.Accent, L.T("En curso")),
            PlanState.Review => (ReviewIcon, _ink.Accent, L.T("Revisión")),
            PlanState.Blocked => (BlockedIcon, _ink.Warn, L.T("Bloqueado")),
            _ when ready => (PlanPlayIcon, _ink.Secondary, L.T("Se puede empezar")),
            _ => (WaitIcon, _ink.Tertiary, L.T("Esperando lo anterior")),
        };
        var title = Ui.Text(task.Title, 12.5, task.State == PlanState.Done ? _ink.Secondary : _ink.Text, FontWeight.Medium);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        var when = Ui.Text($"{(task.Days == 1 ? L.T("1 día") : L.T("{0} días", task.Days))} · {Day(task.Start)}", 11, late ? _ink.Danger : _ink.Tertiary);
        when.TextTrimming = TextTrimming.CharacterEllipsis;
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Children = { title, when } };
        var state = Icon(icon, tint, 15, 2);
        ToolTip.SetTip(state, tip);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { state } };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var card = new Border
        {
            Background = _ink.Surface,
            CornerRadius = new CornerRadius(10),
            BorderBrush = late ? _ink.Danger : task.State == PlanState.Blocked ? _ink.Warn : task.State == PlanState.Doing ? _ink.Accent : _ink.Hairline,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 0, task.Editor is null ? 10 : 28, 0),
            Child = row,
        };
        var strip = new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = StageBrush(plan, task.Stage), Margin = new Thickness(0, 11, 0, 11), HorizontalAlignment = HorizontalAlignment.Left };
        var shell = new Panel { Width = GraphNodeWidth, Height = GraphNodeHeight };
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
            var face = AgentFace(editor, 16);
            face.HorizontalAlignment = HorizontalAlignment.Right;
            face.VerticalAlignment = VerticalAlignment.Top;
            face.Margin = new Thickness(0, 7, 7, 0);
            shell.Children.Add(face);
        }
        ToolTip.SetTip(shell, TaskTip(plan, task));
        return shell;
    }

    /// <summary>Un hito: una bandera y su día, con el borde en acento mientras no pasó.</summary>
    Control GraphMilestone(PlanMilestone milestone, DateOnly today)
    {
        var past = milestone.Date < today;
        var title = Ui.Text(milestone.Title, 12.5, _ink.Text, FontWeight.SemiBold);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Children = { title, Ui.Text(Day(milestone.Date), 11, _ink.Tertiary) } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { Icon(PlanFlagIcon, past ? _ink.Tertiary : _ink.Accent, 15, 2) } };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var card = new Border
        {
            Width = GraphNodeWidth,
            Height = GraphNodeHeight,
            Background = Translucent(past ? _ink.Tertiary : _ink.Accent, 0.1),
            CornerRadius = new CornerRadius(999),
            BorderBrush = past ? _ink.Hairline : Translucent(_ink.Accent, 0.6),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 0),
            Child = row,
        };
        ToolTip.SetTip(card, $"{milestone.Title} · {Day(milestone.Date)}");
        return card;
    }
}
