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
/// Un plan de proyecto (*.plan.json, Core/Plan.cs) en la tarjeta de archivos: en vez del JSON, su Resumen, su Tablero, su
/// Cronograma y su Diagrama (los dibuja PlanPanel, el mismo de Canvas), con las vistas como íconos. «Ver JSON» muestra el texto. Si una
/// IA cambia el archivo mientras se ve, se vuelve a dibujar solo y lo que cambió lleva un halo.
/// </summary>
sealed partial class TerminalPane
{
    /// <summary>La vista elegida de cada plan abierto (por archivo), para que no vuelva al Resumen al redibujarse.</summary>
    readonly Dictionary<string, string> _planViews = new(StringComparer.OrdinalIgnoreCase);

    // Íconos de 24×24 (trazo), como los del resto de la Terminal.
    const string PlanSummaryIcon = "M5,20 V12 M12,20 V5 M19,20 V15";
    const string PlanBoardIcon = "M4,5 H8 V19 H4 Z M10,5 H14 V14 H10 Z M16,5 H20 V10 H16 Z";
    const string PlanTimelineIcon = "M4,7 H11 M8,12 H16 M13,17 H20";
    const string PlanJsonIcon = "M9,4 C7,4 7,6 7,8 C7,10 6,12 4,12 C6,12 7,14 7,16 C7,18 7,20 9,20 M15,4 C17,4 17,6 17,8 C17,10 18,12 20,12 C18,12 17,14 17,16 C17,18 17,20 15,20";
    const double SegWidth = 34, SegHeight = 26;

    /// <summary>La vista del plan de ese archivo, que se vuelve a leer cuando el archivo cambia.</summary>
    Control PlanFileView(string path, string text)
    {
        var host = new ContentControl();
        var last = PlanPanel.TryParse(text);
        void Show(string json)
        {
            // Lo que cambió desde la última lectura (lo escribió una IA): se resalta un momento.
            var now = PlanPanel.TryParse(json);
            var fresh = PlanPanel.Changed(last, now);
            last = now ?? last;
            host.Content = PlanView(path, json, fresh);
        }
        Show(text);

        // Al cambiar el archivo (lo escribe una IA), se vuelve a leer; varios avisos seguidos se juntan en uno.
        FileSystemWatcher? watcher = null;
        IDisposable? settle = null;
        host.AttachedToVisualTree += (_, _) =>
        {
            try
            {
                watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    EnableRaisingEvents = true,
                };
                void Changed() => Dispatcher.UIThread.Post(() =>
                {
                    settle?.Dispose();
                    settle = DispatcherTimer.RunOnce(() =>
                    {
                        try { Show(File.ReadAllText(path)); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    }, TimeSpan.FromMilliseconds(250));
                });
                watcher.Changed += (_, _) => Changed();
                watcher.Created += (_, _) => Changed();
                watcher.Renamed += (_, _) => Changed();
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
        };
        host.DetachedFromVisualTree += (_, _) =>
        {
            settle?.Dispose();
            watcher?.Dispose();
            watcher = null;
        };
        return host;
    }

    /// <summary>Las pestañas del plan y la vista elegida (o, si no se puede leer, por qué y el JSON). changed: las tareas que acaban de cambiar.</summary>
    internal Control PlanView(string path, string json, IReadOnlySet<string>? changed = null)
    {
        Plan plan;
        try { plan = Plan.Parse(json); }
        catch (FormatException ex)
        {
            var broken = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            var why = Ui.Text(L.T("No se pudo leer el plan: {0}", ex.Message), 12.5, _ink.Danger);
            why.TextWrapping = TextWrapping.Wrap;
            why.Margin = new Thickness(18, 12, 18, 8);
            broken.Children.Add(why);
            var raw = TextView(path, json, null);
            Grid.SetRow(raw, 1);
            broken.Children.Add(raw);
            return broken;
        }

        var panel = new PlanPanel(_ink);
        if (changed is not null) panel.Fresh.UnionWith(changed);
        var views = new (string Id, string Icon, string Name)[]
        {
            ("resumen", PlanSummaryIcon, L.T("Resumen")), ("tablero", PlanBoardIcon, L.T("Tablero")), ("cronograma", PlanTimelineIcon, L.T("Cronograma")),
            ("diagrama", PlanPanel.GraphIcon, L.T("Diagrama")),
        };
        var selected = _planViews.TryGetValue(path, out var saved) ? saved : "resumen";
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(12, 8, 10, 6) };
        root.Children.Add(bar);
        var body = new ContentControl();
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        // Las vistas, como íconos en una pastilla con una perilla que se desliza hacia la elegida.
        var knob = new Border
        {
            Width = SegWidth,
            Height = SegHeight,
            CornerRadius = new CornerRadius(999),
            Background = _ink.Raised,
            BoxShadow = BoxShadows.Parse("0 1 3 0 #1F000000"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        if (Animate)
            knob.Transitions = [new ThicknessTransition { Property = Layoutable.MarginProperty, Duration = TimeSpan.FromMilliseconds(280), Easing = new Avalonia.Animation.Easings.CubicEaseOut() }, new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) }];
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var seg = new Border { Background = _ink.Pill, CornerRadius = new CornerRadius(999), Padding = new Thickness(3), Child = new Panel { Children = { knob, buttons } } };
        bar.Children.Add(seg);

        var progress = Plan.Progress(plan.Tasks);
        var chip = new Border
        {
            Background = _ink.Pill,
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(9, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Child = Ui.Text($"{progress:P0}", 12, _ink.Secondary, FontWeight.SemiBold),
        };
        ToolTip.SetTip(chip, L.T("{0} de {1} tareas", plan.Tasks.Count(t => t.State == PlanState.Done), plan.Tasks.Count));
        Grid.SetColumn(chip, 2);
        bar.Children.Add(chip);
        // Quiénes (IAs) editaron el plan, como los avatares de Canvas; el de quien acaba de cambiar algo lleva su halo.
        var faces = panel.Faces(plan);
        faces.Margin = new Thickness(0, 0, 10, 0);
        Grid.SetColumn(faces, 1);
        bar.Children.Add(faces);
        if (panel.Fresh.Count > 0)
        {
            var settle = DispatcherTimer.RunOnce(panel.Settle, TimeSpan.FromSeconds(6));
            root.DetachedFromVisualTree += (_, _) => settle.Dispose();
        }
        var jsonButton = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(999), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(jsonButton, 3);
        bar.Children.Add(jsonButton);
        ToolTip.SetTip(jsonButton, L.T("Ver JSON"));
        var buttonBorders = new List<Border>();
        foreach (var (viewId, icon, name) in views)
        {
            var button = Ui.Press(Icon(icon, _ink.Tertiary, 15, 1.8), () => Select(viewId), null, name);
            button.Width = SegWidth;
            button.Height = SegHeight;
            button.CornerRadius = new CornerRadius(999);
            ToolTip.SetTip(button, name);
            buttons.Children.Add(button);
            buttonBorders.Add(button);
        }

        void Select(string id)
        {
            selected = id;
            _planViews[path] = id;
            var index = views.ToList().FindIndex(v => v.Id == id);
            knob.Margin = new Thickness(Math.Max(0, index) * SegWidth, 0, 0, 0);
            knob.Opacity = index < 0 ? 0 : 1;
            for (var i = 0; i < views.Length; i++) buttonBorders[i].Child = Icon(views[i].Icon, i == index ? _ink.Text : _ink.Tertiary, 15, 1.8);
            var onJson = id == "json";
            var rawButton = Ui.Press(Icon(PlanJsonIcon, onJson ? _ink.Accent : _ink.Secondary, 15, 1.8), () => Select(onJson ? "resumen" : "json"),
                (b, hover) => b.Background = onJson ? Translucent(_ink.Accent, 0.14) : hover ? _ink.Pill : Brushes.Transparent, L.T("Ver JSON"));
            rawButton.Background = onJson ? Translucent(_ink.Accent, 0.14) : Brushes.Transparent;
            rawButton.CornerRadius = new CornerRadius(999);
            rawButton.Width = 30;
            rawButton.Height = 30;
            jsonButton.Child = rawButton;
            body.Content = id switch
            {
                "tablero" => panel.Board(plan),
                "cronograma" => panel.Timeline(plan),
                "diagrama" => panel.Graph(plan),
                "json" => TextView(path, json, null),
                _ => panel.Summary(plan),
            };
        }
        // La perilla aparece ya en su lugar (sin deslizarse desde el principio).
        var first = knob.Transitions;
        knob.Transitions = null;
        Select(selected);
        knob.Transitions = first;
        return root;
    }
}
