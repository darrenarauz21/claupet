namespace Claupet;

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Path = Avalonia.Controls.Shapes.Path;

/// <summary>Los íconos de Canvas, en un cuadro de 24 (como los del resto de la Console).</summary>
static class CanvasIcons
{
    public const string Canvas = "M4,4 H20 A1,1 0 0 1 21,5 V19 A1,1 0 0 1 20,20 H4 A1,1 0 0 1 3,19 V5 A1,1 0 0 1 4,4 Z M3,9 H21 M10,9 V20";
    public const string Plus = "M12,5 V19 M5,12 H19";
    public const string Side = "M6.5,4.5 H17.5 A3,3 0 0 1 20.5,7.5 V16.5 A3,3 0 0 1 17.5,19.5 H6.5 A3,3 0 0 1 3.5,16.5 V7.5 A3,3 0 0 1 6.5,4.5 Z M9,4.5 V19.5";
    public const string Search = "M17,11 A6,6 0 1 1 5,11 A6,6 0 1 1 17,11 Z M15.5,15.5 L20,20";
    public const string History = "M3.5,12 A8.5,8.5 0 1 0 6,6 M3.5,4 V8 H7.5 M12,7.5 V12 L15,14";
    public const string Export = "M12,4 V15 M7.5,8.5 L12,4 L16.5,8.5 M5,14 V19 A1,1 0 0 0 6,20 H18 A1,1 0 0 0 19,19 V14";
    public const string Play = "M8,5.5 V18.5 L18.5,12 Z";
    public const string More = "M3.4,12 A1.6,1.6 0 1 0 6.6,12 A1.6,1.6 0 1 0 3.4,12 Z M10.4,12 A1.6,1.6 0 1 0 13.6,12 A1.6,1.6 0 1 0 10.4,12 Z M17.4,12 A1.6,1.6 0 1 0 20.6,12 A1.6,1.6 0 1 0 17.4,12 Z";
    public const string Grip = "M9,6 H9.01 M15,6 H15.01 M9,12 H9.01 M15,12 H15.01 M9,18 H9.01 M15,18 H15.01";
    public const string Pen = "M4,20 H8 L19,9 L15,5 L4,16 Z M13,7 L17,11";
    public const string Comment = "M5,5 H19 A1,1 0 0 1 20,6 V15 A1,1 0 0 1 19,16 H10 L5,20 V6 A1,1 0 0 1 6,5 Z";
    public const string Spark = "M12,3 L13.8,9.2 L20,11 L13.8,12.8 L12,19 L10.2,12.8 L4,11 L10.2,9.2 Z M19,3 V6 M17.5,4.5 H20.5";
    public const string Copy = "M9,9 H19 A1,1 0 0 1 20,10 V19 A1,1 0 0 1 19,20 H10 A1,1 0 0 1 9,19 Z M15,9 V5 A1,1 0 0 0 14,4 H5 A1,1 0 0 0 4,5 V14 A1,1 0 0 0 5,15 H9";
    public const string Trash = "M4.5,7 H19.5 M9.5,7 V4.5 H14.5 V7 M6.5,7 L7.5,20 H16.5 L17.5,7 M10.5,11 V16 M13.5,11 V16";
    public const string Text = "M5,6 H19 M12,6 V19 M9,19 H15";
    public const string Code = "M8.5,7 L3.5,12 L8.5,17 M15.5,7 L20.5,12 L15.5,17 M13.5,5 L10.5,19";
    public const string Html = "M3.5,5 H20.5 V19 H3.5 Z M3.5,9 H20.5 M6,7 H6.01 M8,7 H8.01";
    public const string Diagram = "M4,4 H10 V9 H4 Z M14,15 H20 V20 H14 Z M7,9 V17.5 H14 M17,15 V7 H10";
    public const string Svg = "M5,19 C7,8 17,16 19,5 M3.5,17.5 H6.5 V20.5 H3.5 Z M17.5,3.5 H20.5 V6.5 H17.5 Z";
    public const string Table = "M3.5,5 H20.5 V19 H3.5 Z M3.5,10 H20.5 M3.5,14.5 H20.5 M10,5 V19";
    public const string Chart = "M4,20 H20 M7,16 V11 M12,16 V6 M17,16 V13";
    public const string Image = "M4,5 H20 V19 H4 Z M4,16 L9,11 L13,15 L15.5,12.5 L20,17 M15.5,9 H15.51";
    public const string PlanSummary = "M5,20 V12 M12,20 V5 M19,20 V15";
    public const string PlanBoard = "M4,5 H8 V19 H4 Z M10,5 H14 V14 H10 Z M16,5 H20 V10 H16 Z";
    public const string PlanTimeline = "M4,7 H11 M8,12 H16 M13,17 H20";
    public const string PlanGraph = PlanPanel.GraphIcon;
    public const string Eye = "M2.5,12 C5,7 8.5,5 12,5 C15.5,5 19,7 21.5,12 C19,17 15.5,19 12,19 C8.5,19 5,17 2.5,12 Z M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12 Z";
    public const string Undo = "M9,14 L4,9 L9,4 M4,9 H14 A6,6 0 0 1 14,21 H11";
    public const string Close = "M6,6 L18,18 M18,6 L6,18";
    public const string Send = "M12,19 V5 M6,11 L12,5 L18,11";
    public const string Check = "M5,12.5 L10,17.5 L19,7";
    public const string Up = "M12,19 V5 M6,11 L12,5 L18,11";
    public const string Down = "M12,5 V19 M6,13 L12,19 L18,13";
    public const string Link = "M10,14 A4,4 0 0 0 15.7,14 L18.5,11.2 A4,4 0 0 0 12.8,5.5 L11.5,6.8 M14,10 A4,4 0 0 0 8.3,10 L5.5,12.8 A4,4 0 0 0 11.2,18.5 L12.5,17.2";
    public const string Folder = "M3.5,7 A1.5,1.5 0 0 1 5,5.5 H9.5 L11.5,7.5 H19 A1.5,1.5 0 0 1 20.5,9 V17.5 A1.5,1.5 0 0 1 19,19 H5 A1.5,1.5 0 0 1 3.5,17.5 Z";
    public const string Warn = "M12,4 L21,19.5 H3 Z M12,10 V14 M12,17 H12.01";
    /// <summary>Una hoja con la esquina doblada y una línea gruesa (PDF).</summary>
    public const string Pdf = "M14,3 H6.5 V21 H17.5 V6.5 Z M14,3 V6.5 H17.5 M9,12 H15 M9,15.5 H13";
    /// <summary>Una hoja con renglones (Word).</summary>
    public const string Docx = "M14,3 H6.5 V21 H17.5 V6.5 Z M14,3 V6.5 H17.5 M9,11 H15 M9,14 H15 M9,17 H12.5";
    public const string Json = "M8,4 C6,4 6,6 6,8 C6,10 5,11 4,12 C5,13 6,14 6,16 C6,18 6,20 8,20 M16,4 C18,4 18,6 18,8 C18,10 19,11 20,12 C19,13 18,14 18,16 C18,18 18,20 16,20";

    public static string Of(string kind) => kind switch
    {
        CanvasKinds.Markdown => Text,
        CanvasKinds.Code => Code,
        CanvasKinds.Html => Html,
        CanvasKinds.Diagram => Diagram,
        CanvasKinds.Svg => Svg,
        CanvasKinds.Data => Table,
        CanvasKinds.Image => Image,
        CanvasKinds.Plan => PlanTimeline,
        _ => Text,
    };
}

sealed partial class CanvasPane
{
    const double Column = 760;
    /// <summary>A la izquierda, lugar para el tipo y el asa; a la derecha, para los avatares (y aire antes de la barra de desplazamiento).</summary>
    const double Gutter = 56, Aside = 66;
    /// <summary>El aire de adentro de cada sección.</summary>
    static readonly Thickness BlockPadding = new(14, 10);

    Control Document(CanvasDoc doc)
    {
        var column = new StackPanel { MaxWidth = Column, Margin = new Thickness(Gutter, 10, Aside, 140), HorizontalAlignment = HorizontalAlignment.Stretch };
        column.Width = double.NaN;
        for (var i = 0; i < doc.Sections.Count; i++)
        {
            var after = i == 0 ? "start" : doc.Sections[i - 1].Id;
            column.Children.Add(InsertLine(after));
            column.Children.Add(Block(doc, doc.Sections[i], i));
        }
        column.Children.Add(InsertLine(doc.Sections.Count == 0 ? "start" : doc.Sections[^1].Id, last: true));
        foreach (var s in doc.Sections) _seen.Add(s.Id);
        return column;
    }

    /// <summary>La línea entre secciones: al pasar el mouse se dibuja y aparece el «+», que abre los tipos.</summary>
    Control InsertLine(string after, bool last = false)
    {
        if (_past is not null) return new Border { Height = 14 };
        var line = new Border
        {
            Height = 1.5, Background = _ink.Accent, CornerRadius = new CornerRadius(1), Margin = new Thickness(14, 0),
            VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = RelativePoint.Center,
            RenderTransform = TransformOperations.Parse("scaleX(0)"),
            Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(260), Easing = new Avalonia.Animation.Easings.CubicEaseOut() }],
        };
        Border? plus = null;
        plus = IconButton(CanvasIcons.Plus, L.T("Insertar"), () => KindsPopup(plus!, after), size: 24, glyph: 13, accent: true);
        plus.Background = _ink.Raised;
        plus.Tag = "solid";
        plus.BorderBrush = _ink.Hairline;
        plus.BorderThickness = new Thickness(1);
        plus.BoxShadow = Shadow(soft: true);
        plus.HorizontalAlignment = HorizontalAlignment.Center;
        Fade(plus, 0, scale: true);
        var zone = new Grid { Height = last ? 36 : 18, Background = Brushes.Transparent };
        zone.Children.Add(line);
        zone.Children.Add(plus);
        if (last)
        {
            plus.VerticalAlignment = VerticalAlignment.Top;
            line.VerticalAlignment = VerticalAlignment.Top;
            line.Margin = new Thickness(14, 12, 14, 0);
        }
        zone.PointerEntered += (_, _) => { line.RenderTransform = TransformOperations.Parse("scaleX(1)"); Fade(plus, 1, scale: true); };
        zone.PointerExited += (_, _) => { line.RenderTransform = TransformOperations.Parse("scaleX(0)"); Fade(plus, 0, scale: true); };
        return zone;
    }

    Control Block(CanvasDoc doc, CanvasSection section, int index)
    {
        var editing = _editing == section.Id;
        var readOnly = _past is not null;
        var host = new Grid { Tag = "blk:" + section.Id };

        var body = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = BlockPadding,
            Background = editing ? _ink.Raised : Brushes.Transparent,
            BoxShadow = editing ? Shadow() : default,
            Transitions = [new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(200) }],
            Child = editing && _editor is not null ? Detach(_editor) : View(doc, section),
        };
        host.Children.Add(body);

        // El color de quien la acaba de cambiar, que se apaga solo.
        if (_flash.TryGetValue(section.Id, out var by))
        {
            var color = AuthorColor(by);
            var glow = new Border
            {
                Tag = "glow", CornerRadius = new CornerRadius(12), BorderBrush = color, BorderThickness = new Thickness(1.5), IsHitTestVisible = false,
                BoxShadow = new BoxShadows(new BoxShadow { Spread = 4, Color = WithAlpha(color, 0x2A).Color }),
                Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(900) }],
            };
            host.Children.Add(glow);
        }

        // A la izquierda: el tipo y el asa; arriba a la derecha: las herramientas. Aparecen al pasar el mouse.
        var gutter = new StackPanel { Spacing = 2, Margin = new Thickness(-42, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var kind = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = _ink.Pill, Child = Glyph(CanvasIcons.Of(section.Kind), _ink.Tertiary, 14) };
        ToolTip.SetTip(kind, CanvasKinds.Label(section.Kind) + (section.Language is { } lang && section.Kind != CanvasKinds.Markdown ? " · " + lang : ""));
        gutter.Children.Add(kind);
        if (!readOnly) gutter.Children.Add(GripHandle(section));
        host.Children.Add(gutter);

        Border? tools = null;
        if (!readOnly)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            if (section.Kind is CanvasKinds.Markdown or CanvasKinds.Code or CanvasKinds.Data or CanvasKinds.Image or CanvasKinds.Html or CanvasKinds.Svg or CanvasKinds.Diagram or CanvasKinds.Plan)
                row.Children.Add(editing
                    ? IconButton(CanvasIcons.Check, L.T("Guardar") + " · Ctrl+Enter", SaveEditing, size: 28, glyph: 14, accent: true)
                    : IconButton(CanvasIcons.Pen, L.T("Editar"), () => StartEditing(section), size: 28, glyph: 14));
            Border? ask = null, comment = null, menu = null;
            ask = IconButton(CanvasIcons.Spark, L.T("Pedir a la IA"), () => AskPopup(ask!, section), size: 28, glyph: 14);
            comment = IconButton(CanvasIcons.Comment, L.T("Comentar"), () => ThreadPopup(comment!, section), size: 28, glyph: 14);
            menu = IconButton(CanvasIcons.More, L.T("Más"), () => SectionMenu(menu!, doc, section), size: 28, glyph: 14, fill: true);
            row.Children.Add(ask);
            row.Children.Add(comment);
            row.Children.Add(menu);
            tools = Floating(row, new Thickness(3));
            tools.HorizontalAlignment = HorizontalAlignment.Right;
            tools.VerticalAlignment = VerticalAlignment.Top;
            tools.Margin = new Thickness(0, -16, 8, 0);
            tools.ZIndex = 3;
            host.Children.Add(tools);
        }

        // A la derecha: quién la escribió y sus comentarios abiertos.
        var who = new StackPanel { Spacing = 6, Margin = new Thickness(0, 10, -40, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        var avatar = Avatar(section.Author, 22);
        ToolTip.SetTip(avatar, $"{CanvasJson.AuthorLabel(section.Author)} · {Format.Ago(section.Updated)}");
        who.Children.Add(avatar);
        var open = doc.Comments.Count(c => c.SectionId == section.Id && !c.Resolved);
        if (open > 0)
        {
            Border? count = null;
            count = Ui.Press(new TextBlock { Text = open.ToString(), FontSize = 10.5, FontWeight = FontWeight.SemiBold, Foreground = _ink.Secondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                () => ThreadPopup(count!, section), (b, on) => b.Background = on ? _ink.Hairline : _ink.Pill, L.T("Comentarios"));
            count.Width = 22;
            count.Height = 22;
            count.CornerRadius = new CornerRadius(11);
            count.Background = _ink.Pill;
            ToolTip.SetTip(count, L.T("Comentarios"));
            who.Children.Add(count);
        }
        host.Children.Add(who);

        var shown = editing;
        Fade(gutter, shown ? 1 : 0, slide: true);
        if (tools is not null) Fade(tools, shown ? 1 : 0, scale: true);
        host.PointerEntered += (_, _) =>
        {
            if (_editing != section.Id) body.Background = WithAlpha(_ink.Pill, 0x70);
            Fade(gutter, 1, slide: true);
            if (tools is not null) Fade(tools, 1, scale: true);
        };
        host.PointerExited += (_, _) =>
        {
            if (_editing == section.Id) return;
            body.Background = Brushes.Transparent;
            Fade(gutter, 0, slide: true);
            if (tools is not null) Fade(tools, 0, scale: true);
        };
        if (!_seen.Contains(section.Id)) Enter(host, Math.Min(index, 10) * 45);
        return host;
    }

    static Control Detach(Control control)
    {
        if (control.Parent is Panel panel) panel.Children.Remove(control);
        else if (control.Parent is Decorator decorator) decorator.Child = null;
        return control;
    }

    // ── Cada tipo ───────────────────────────────────────────────────────────────────────

    /// <summary>Cómo se ve una sección según su tipo (y su vista elegida, si tiene varias).</summary>
    Control View(CanvasDoc doc, CanvasSection section)
    {
        if (section.Content.Length == 0)
            return new TextBlock { Text = L.T("Vacía"), FontSize = 14, Foreground = _ink.Tertiary, FontStyle = FontStyle.Italic };
        var views = Views(section);
        var view = views.Length == 0 ? "" : _views.TryGetValue(section.Id, out var v) && views.Contains(v) ? v : views[0];
        var content = (section.Kind, view) switch
        {
            (CanvasKinds.Markdown, _) => Markdown(doc, section.Content),
            (CanvasKinds.Plan, "code") => Code(section.Content, "json"),
            (_, "code") => Code(section.Content, section.Language ?? section.Kind),
            (CanvasKinds.Html or CanvasKinds.Svg, _) => Live(doc, section) ?? Stage(doc, section),
            (CanvasKinds.Diagram, _) => Diagram(section.Content, animate: !_seen.Contains(section.Id)) ?? Code(section.Content, "mermaid"),
            (CanvasKinds.Data, "chart") => Chart(section.Content, animate: !_seen.Contains(section.Id) || _flash.ContainsKey(section.Id)) ?? CsvGrid(section.Content),
            (CanvasKinds.Data, "table") => CsvGrid(section.Content),
            (CanvasKinds.Data, _) => Code(PrettyJson(section.Content), "json"),
            (CanvasKinds.Image, _) => ImageView(section.Content.Trim()),
            (CanvasKinds.Plan, _) => PlanSection(section.Content, view) ?? Code(section.Content, "json"),
            _ => Code(section.Content, section.Language ?? ""),
        };
        if (views.Length < 2) return content;
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(Segmented(section.Id, views, view));
        stack.Children.Add(content);
        return stack;
    }

    /// <summary>
    /// Un plan dibujado (resumen, tablero, cronograma o diagrama) por PlanPanel, el mismo de la tarjeta de la Terminal; null si el
    /// JSON no se lee (entonces se ve como código).
    /// </summary>
    Control? PlanSection(string json, string view)
    {
        if (PlanPanel.TryParse(json) is not { } plan) return null;
        var panel = new PlanPanel(_ink);
        return new Border
        {
            Background = _ink.Raised,
            BorderBrush = _ink.Hairline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            MaxHeight = 560,
            Child = view switch { "board" => panel.Board(plan), "timeline" => panel.Timeline(plan), "graph" => panel.Graph(plan), _ => panel.Summary(plan) },
        };
    }

    /// <summary>Las vistas de cada tipo, la primera es la de siempre.</summary>
    static string[] Views(CanvasSection s) => s.Kind switch
    {
        CanvasKinds.Html or CanvasKinds.Svg => ["stage", "code"],
        CanvasKinds.Diagram => ["render", "code"],
        CanvasKinds.Plan => ["summary", "board", "timeline", "graph", "code"],
        CanvasKinds.Data when (s.Language ?? "").Equals("csv", StringComparison.OrdinalIgnoreCase) => ["table", "chart", "code"],
        _ => [],
    };

    static string ViewIcon(string view) => view switch
    {
        "stage" or "render" => CanvasIcons.Eye,
        "table" => CanvasIcons.Table,
        "chart" => CanvasIcons.Chart,
        "summary" => CanvasIcons.PlanSummary,
        "board" => CanvasIcons.PlanBoard,
        "timeline" => CanvasIcons.PlanTimeline,
        "graph" => CanvasIcons.PlanGraph,
        _ => CanvasIcons.Code,
    };

    static string ViewName(string view) => view switch
    {
        "stage" or "render" => L.T("Ver"),
        "table" => L.T("Tabla"),
        "chart" => L.T("Gráfico"),
        "summary" => L.T("Resumen"),
        "board" => L.T("Tablero"),
        "timeline" => L.T("Cronograma"),
        "graph" => L.T("Diagrama"),
        _ => L.T("Código"),
    };

    /// <summary>Botones de vista en una píldora, con una perilla que se desliza a la elegida.</summary>
    Control Segmented(string sectionId, string[] views, string current)
    {
        const double W = 32;
        var host = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        var back = new Border { Background = _ink.Pill, CornerRadius = new CornerRadius(999) };
        host.Children.Add(back);
        var knob = new Border
        {
            Width = W, Height = 26, Margin = new Thickness(3), CornerRadius = new CornerRadius(999), Background = _ink.Raised,
            HorizontalAlignment = HorizontalAlignment.Left, BoxShadow = Shadow(soft: true),
            RenderTransform = TransformOperations.Parse($"translateX({Array.IndexOf(views, current) * W}px)"),
            Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(260), Easing = new Avalonia.Animation.Easings.CubicEaseOut() }],
        };
        host.Children.Add(knob);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3) };
        for (var i = 0; i < views.Length; i++)
        {
            var view = views[i];
            var index = i;
            var on = view == current;
            var button = Ui.Press(Glyph(ViewIcon(view), on ? _ink.Text : _ink.Tertiary, 14), () =>
            {
                knob.RenderTransform = TransformOperations.Parse($"translateX({index * W}px)");
                _views[sectionId] = view;
                DispatcherTimer.RunOnce(() => Render(keepScroll: true), TimeSpan.FromMilliseconds(140));
            }, null, ViewName(view));
            button.Width = W;
            button.Height = 26;
            button.Background = Brushes.Transparent;
            ToolTip.SetTip(button, ViewName(view));
            row.Children.Add(button);
        }
        host.Children.Add(row);
        return host;
    }

    Control Markdown(CanvasDoc doc, string text)
    {
        try { return _markdown(text, doc.Folder is { } f && Directory.Exists(f) ? f : Account.Home); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return Code(text, "markdown"); }
    }

    static readonly Regex Token = new(@"(//[^\n]*|#[^\n]*)|(""(?:[^""\\\n]|\\.)*""|'(?:[^'\\\n]|\\.)*')|\b(var|let|const|if|else|for|foreach|while|return|new|class|public|private|static|void|async|await|def|function|import|from|using|namespace|true|false|null|None|True|False)\b|\b(\d+(?:\.\d+)?)\b", RegexOptions.Compiled);

    /// <summary>Código con colores sencillos (comentarios, cadenas, palabras clave y números) que se selecciona y se copia.</summary>
    Control Code(string text, string language)
    {
        var block = new SelectableTextBlock { FontFamily = Ink.Mono, FontSize = 12.8, LineHeight = 20, Foreground = _ink.Text, TextWrapping = TextWrapping.NoWrap };
        var source = text.TrimEnd('\n');
        var hashComments = language is "python" or "py" or "bash" or "sh" or "powershell" or "ps1" or "yaml" or "toml" or "r";
        var inlines = block.Inlines ??= [];
        var last = 0;
        foreach (Match m in Token.Matches(source.Length > 60_000 ? source[..60_000] : source))
        {
            if (m.Groups[1].Success && m.Value[0] == '#' && !hashComments) continue;
            if (m.Index > last) inlines.Add(new Avalonia.Controls.Documents.Run(source[last..m.Index]));
            var brush = m.Groups[1].Success ? _ink.Tertiary : m.Groups[2].Success ? _ink.String : m.Groups[3].Success ? _ink.Keyword : _ink.Accent;
            inlines.Add(new Avalonia.Controls.Documents.Run(m.Value) { Foreground = brush, FontStyle = m.Groups[1].Success ? FontStyle.Italic : FontStyle.Normal });
            last = m.Index + m.Length;
        }
        if (last < source.Length) inlines.Add(new Avalonia.Controls.Documents.Run(source[last..]));
        return new Border
        {
            Background = WithAlpha(_ink.Pill, 0xB0),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 11),
            Child = new ScrollViewer { Content = block, MaxHeight = 420, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        };
    }

    static string PrettyJson(string text)
    {
        try
        {
            using var json = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }
        catch (JsonException) { return text; }
    }

    /// <summary>
    /// Una página (HTML o SVG) viva dentro del canvas, en Windows (CanvasLivePage). Si no cambió, es la misma de antes,
    /// sin volver a cargarla. Null donde no se puede (otro sistema, sin WebView2 o si falló): ahí va el escenario.
    /// </summary>
    Control? Live(CanvasDoc doc, CanvasSection section)
    {
        if (_liveOff || !CanvasLivePage.Available) return null;
        var html = CanvasExport.Live(section);
        _live.TryGetValue(section.Id, out var before);
        if (before?.Html == html)
        {
            _liveShown.Add(section.Id);
            before.SetBackground(PageBackground()); // el tema pudo cambiar
            return Detach(before);
        }
        string file;
        try
        {
            var dir = System.IO.Path.Combine(_store.Root, "vista");
            Directory.CreateDirectory(dir);
            file = System.IO.Path.Combine(dir, $"{doc.Id}-{section.Id}-viva.html");
            File.WriteAllText(file, html, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex);
            return null;
        }
        var page = new CanvasLivePage(file, html, before?.Height ?? 360, PageBackground(), () => _overlaysOn);
        page.Wheel += dy =>
        {
            // Lo mismo que avanza la rueda en el canvas (unos 50 por paso; el navegador manda 100).
            var max = Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height);
            _scroll.Offset = new Vector(_scroll.Offset.X, Math.Clamp(_scroll.Offset.Y + dy / 2, 0, max));
        };
        page.Failed += () =>
        {
            if (_liveOff) return;
            _liveOff = true;
            Render(keepScroll: true);
        };
        _live[section.Id] = page;
        _liveShown.Add(section.Id);
        return page;
    }

    /// <summary>El fondo del panel, detrás de la página viva.</summary>
    Color PageBackground() => (_ink.Surface as ISolidColorBrush)?.Color ?? Avalonia.Media.Colors.Transparent;

    /// <summary>Una página (HTML o SVG): un escenario con su botón grande; se abre en el navegador, donde corre aislada.</summary>
    Control Stage(CanvasDoc doc, CanvasSection section)
    {
        var grid = new Grid { Height = 132 };
        var back = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(WithAlpha(_ink.Accent, 0x1E).Color, 0), new GradientStop(WithAlpha(AuthorColor("Gemini CLI"), 0x1A).Color, 1) },
            },
            BorderBrush = _ink.Hairline,
            BorderThickness = new Thickness(1),
        };
        grid.Children.Add(back);
        var title = Regex.Match(section.Content, @"<title>(.*?)</title>|<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var name = section.Title ?? (title.Success ? Regex.Replace(title.Groups[1].Success && title.Groups[1].Length > 0 ? title.Groups[1].Value : title.Groups[2].Value, "<.*?>", "") : CanvasKinds.Label(section.Kind));
        var center = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var play = IconButton(CanvasIcons.Play, L.T("Ver en el navegador"), () => Preview(doc, section), size: 52, glyph: 22);
        play.Background = _ink.Accent;
        play.Tag = "solid";
        ((Path)((Viewbox)play.Child!).Child!).Stroke = Ink.On(_ink.Accent);
        ((Path)((Viewbox)play.Child!).Child!).Fill = Ink.On(_ink.Accent);
        play.BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 6, Blur = 18, Color = WithAlpha(_ink.Accent, 0x55).Color });
        play.HorizontalAlignment = HorizontalAlignment.Center;
        play.RenderTransformOrigin = RelativePoint.Center;
        play.Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(200) }];
        play.PointerEntered += (_, _) => play.RenderTransform = TransformOperations.Parse("scale(1.08)");
        play.PointerExited += (_, _) => play.RenderTransform = TransformOperations.Parse("scale(1)");
        center.Children.Add(play);
        center.Children.Add(new TextBlock { Text = name.Trim(), FontSize = 13, Foreground = _ink.Secondary, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420 });
        grid.Children.Add(center);
        return grid;
    }

    Control CsvGrid(string csv)
    {
        var rows = csv.ReplaceLineEndings("\n").Split('\n').Where(l => l.Length > 0).Take(300).Select(CanvasExport.SplitCsv).ToList();
        if (rows.Count == 0) return Code(csv, "csv");
        var columns = rows.Max(r => r.Count);
        var grid = new Grid();
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var c = 0; c < rows[r].Count; c++)
            {
                var value = rows[r][c];
                var number = r > 0 && double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
                var cell = new Border
                {
                    BorderBrush = _ink.Hairline,
                    BorderThickness = new Thickness(0, 0, 0, r == rows.Count - 1 ? 0 : 1),
                    Padding = new Thickness(14, 7),
                    Background = r == 0 ? _ink.Pill : Brushes.Transparent,
                    Child = new SelectableTextBlock
                    {
                        Text = value, FontSize = 13, Foreground = _ink.Text, FontWeight = r == 0 ? FontWeight.SemiBold : FontWeight.Normal,
                        HorizontalAlignment = number ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                    },
                };
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }
        var frame = new Border { BorderBrush = _ink.Hairline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = grid, HorizontalAlignment = HorizontalAlignment.Left };
        return new ScrollViewer { Content = frame, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>Un CSV de etiqueta y número como barras que crecen al aparecer. Null si no tiene esa forma.</summary>
    Control? Chart(string csv, bool animate = true)
    {
        var rows = csv.ReplaceLineEndings("\n").Split('\n').Where(l => l.Length > 0).Select(CanvasExport.SplitCsv).ToList();
        var data = rows.Skip(1).Where(r => r.Count >= 2)
            .Select(r => (Label: r[0], Ok: double.TryParse(r[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v), Value: v))
            .Where(p => p.Ok).Take(40).ToList();
        if (data.Count == 0) return null;
        var max = Math.Max(data.Max(d => d.Value), 1e-9);
        var grid = new Grid { Height = 170 };
        for (var i = 0; i < data.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var (label, _, value) = data[i];
            var cell = new Grid { RowDefinitions = new RowDefinitions("*,22"), Margin = new Thickness(5, 0) };
            var bar = new Border
            {
                Height = Math.Max(4, 140 * value / max),
                VerticalAlignment = VerticalAlignment.Bottom,
                CornerRadius = new CornerRadius(6, 6, 2, 2),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(((ISolidColorBrush)_ink.Accent).Color, 0), new GradientStop(WithAlpha(_ink.Accent, 0x88).Color, 1) },
                },
                RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                RenderTransform = TransformOperations.Parse(animate ? "scaleY(0)" : "scaleY(1)"),
                Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(700), Delay = TimeSpan.FromMilliseconds(i * 60), Easing = new Avalonia.Animation.Easings.CubicEaseOut() }],
            };
            ToolTip.SetTip(bar, $"{label}: {value:0.##}");
            if (animate) bar.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => bar.RenderTransform = TransformOperations.Parse("scaleY(1)"), DispatcherPriority.Background);
            cell.Children.Add(bar);
            var text = new TextBlock { Text = label, FontSize = 11, Foreground = _ink.Tertiary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetRow(text, 1);
            cell.Children.Add(text);
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        return grid;
    }

    Control ImageView(string source)
    {
        try
        {
            Stream? stream = null;
            if (source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) && source.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase) is var at and > 0)
                stream = new MemoryStream(Convert.FromBase64String(source[(at + 8)..]));
            else if (System.IO.Path.IsPathFullyQualified(source) && File.Exists(source))
                stream = File.OpenRead(source);
            if (stream is not null)
                using (stream)
                    return new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, Child = new Image { Source = new Bitmap(stream), MaxHeight = 520, Stretch = Stretch.Uniform } };
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { }
        // Una URL no se descarga sola: un marco con su botón para abrirla.
        var frame = new Grid { Height = 120 };
        frame.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = _ink.Pill });
        var center = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        center.Children.Add(Glyph(CanvasIcons.Image, _ink.Tertiary, 26));
        center.Children.Add(new TextBlock { Text = source, FontSize = 11.5, Foreground = _ink.Tertiary, MaxWidth = 460, TextTrimming = TextTrimming.CharacterEllipsis });
        frame.Children.Add(center);
        if (source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var open = IconButton(CanvasIcons.Link, L.T("Abrir"), () => Shell.Open(source), size: 30, glyph: 14);
            open.HorizontalAlignment = HorizontalAlignment.Right;
            open.VerticalAlignment = VerticalAlignment.Top;
            open.Margin = new Thickness(8);
            frame.Children.Add(open);
        }
        return frame;
    }

    // ── Diagramas Mermaid (flujos), dibujados aquí ──────────────────────────────────────

    static readonly Regex Node = new(@"^\s*([A-Za-z0-9_\-]+)\s*(?:\(\((.+?)\)\)|\[\[(.+?)\]\]|\[(.+?)\]|\((.+?)\)|\{(.+?)\}|>(.+?)\])?\s*$");
    static readonly Regex Arrow = new(@"\s*(?:-->|---|-\.->|==>|--\s*[^-]+?\s*-->)\s*(?:\|[^|]*\|\s*)?");

    /// <summary>
    /// Un «graph»/«flowchart» de Mermaid: nodos por capas (de izquierda a derecha, o de arriba abajo con TD/TB) y flechas
    /// curvas con un trazo que corre. Lo que no es un flujo (secuencias, gantt…) devuelve null y se muestra como código.
    /// </summary>
    Control? Diagram(string source, bool animate = true)
    {
        var lines = source.ReplaceLineEndings("\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("%%")).ToList();
        if (lines.Count == 0 || !Regex.IsMatch(lines[0], @"^(graph|flowchart)\b", RegexOptions.IgnoreCase)) return null;
        var vertical = Regex.IsMatch(lines[0], @"\b(TD|TB)\b", RegexOptions.IgnoreCase);
        var labels = new Dictionary<string, string>();
        var order = new List<string>();
        var edges = new List<(string From, string To)>();
        string? Add(string part)
        {
            var m = Node.Match(part);
            if (!m.Success) return null;
            var id = m.Groups[1].Value;
            var label = Enumerable.Range(2, 6).Select(g => m.Groups[g]).FirstOrDefault(g => g.Success)?.Value.Trim('"');
            if (!labels.ContainsKey(id)) { labels[id] = id; order.Add(id); }
            if (label is not null) labels[id] = label;
            return id;
        }
        foreach (var line in lines.Skip(1))
        {
            if (Regex.IsMatch(line, @"^(classDef|class|style|linkStyle|subgraph|end|click)\b")) continue;
            var parts = Arrow.Split(line.TrimEnd(';'));
            var ids = parts.Select(Add).ToList();
            for (var i = 1; i < ids.Count; i++)
                if (ids[i - 1] is { } a && ids[i] is { } b) edges.Add((a, b));
        }
        if (order.Count == 0 || order.Count > 60) return null;

        // Capas: el camino más largo desde un nodo sin entradas (con un tope, por si hay ciclos).
        var layer = order.ToDictionary(id => id, _ => 0);
        for (var pass = 0; pass < order.Count; pass++)
        {
            var moved = false;
            foreach (var (from, to) in edges)
                if (from != to && layer[to] < layer[from] + 1 && layer[from] + 1 < order.Count) { layer[to] = layer[from] + 1; moved = true; }
            if (!moved) break;
        }
        var layers = order.GroupBy(id => layer[id]).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
        const double H = 40, GapMain = 56, GapCross = 16;
        double Width(string id) => Math.Clamp(labels[id].Length * 7.4 + 30, 84, 220);
        var boxes = new Dictionary<string, Rect>();
        var main = 0.0;
        var crossSizes = layers.Select(l => vertical ? l.Sum(Width) + (l.Count - 1) * GapCross : l.Count * H + (l.Count - 1) * GapCross).ToList();
        var crossMax = crossSizes.Max();
        for (var li = 0; li < layers.Count; li++)
        {
            var l = layers[li];
            var thickness = vertical ? H : l.Max(Width);
            var cross = (crossMax - crossSizes[li]) / 2;
            foreach (var id in l)
            {
                var w = Width(id);
                boxes[id] = vertical ? new Rect(cross, main, w, H) : new Rect(main + (thickness - w) / 2, cross, w, H);
                cross += (vertical ? w : H) + GapCross;
            }
            main += thickness + GapMain;
        }
        var canvas = new Canvas
        {
            Width = boxes.Values.Max(r => r.Right) + 4,
            Height = boxes.Values.Max(r => r.Bottom) + 4,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        foreach (var (from, to) in edges.Distinct())
        {
            var a = boxes[from];
            var b = boxes[to];
            Point p1, p2, c1, c2;
            if (vertical) { p1 = new(a.Center.X, a.Bottom); p2 = new(b.Center.X, b.Top - 3); c1 = new(p1.X, p1.Y + 24); c2 = new(p2.X, p2.Y - 24); }
            else { p1 = new(a.Right, a.Center.Y); p2 = new(b.Left - 3, b.Center.Y); c1 = new(p1.X + 24, p1.Y); c2 = new(p2.X - 24, p2.Y); }
            var path = new Path
            {
                Data = Geometry.Parse(FormattableString.Invariant($"M{p1.X},{p1.Y} C{c1.X},{c1.Y} {c2.X},{c2.Y} {p2.X},{p2.Y}")),
                Stroke = _ink.Accent, StrokeThickness = 1.6, StrokeDashArray = new AvaloniaList<double> { 3, 3 },
            };
            Flow(path);
            canvas.Children.Add(path);
            var head = vertical
                ? FormattableString.Invariant($"M{p2.X - 4.5},{p2.Y - 6} L{p2.X},{p2.Y} L{p2.X + 4.5},{p2.Y - 6}")
                : FormattableString.Invariant($"M{p2.X - 6},{p2.Y - 4.5} L{p2.X},{p2.Y} L{p2.X - 6},{p2.Y + 4.5}");
            canvas.Children.Add(new Path { Data = Geometry.Parse(head), Stroke = _ink.Accent, StrokeThickness = 1.6, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round });
        }
        var n = 0;
        foreach (var id in order)
        {
            var r = boxes[id];
            var box = new Border
            {
                Width = r.Width, Height = r.Height, CornerRadius = new CornerRadius(12), Background = _ink.Raised, BorderBrush = _ink.Hairline,
                BorderThickness = new Thickness(1.5), BoxShadow = Shadow(soft: true),
                Child = new TextBlock { Text = labels[id], FontSize = 13, FontWeight = FontWeight.Medium, Foreground = _ink.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 0) },
            };
            Canvas.SetLeft(box, r.X);
            Canvas.SetTop(box, r.Y);
            if (animate) Enter(box, n++ * 70, scale: true);
            canvas.Children.Add(box);
        }
        return new ScrollViewer
        {
            Content = new Border { Padding = new Thickness(0, 10), Child = canvas },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    /// <summary>El trazo punteado corre por la flecha, sin fin, mientras se ve.</summary>
    static void Flow(Shape path)
    {
        var stop = new CancellationTokenSource();
        path.AttachedToVisualTree += (_, _) => _ = new Animation
        {
            Duration = TimeSpan.FromSeconds(0.9),
            IterationCount = IterationCount.Infinite,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Avalonia.Styling.Setter(Shape.StrokeDashOffsetProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Avalonia.Styling.Setter(Shape.StrokeDashOffsetProperty, -6d) } },
            },
        }.RunAsync(path, stop.Token);
        path.DetachedFromVisualTree += (_, _) => stop.Cancel();
    }

    // ── Mover con el asa ────────────────────────────────────────────────────────────────

    Control GripHandle(CanvasSection section)
    {
        var grip = new Border { Width = 28, Height = 22, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeAll), Child = Glyph(CanvasIcons.Grip, _ink.Tertiary, 14, 2.6) };
        ToolTip.SetTip(grip, L.T("Arrastrar para mover"));
        grip.PointerEntered += (_, _) => grip.Background = _ink.Pill;
        grip.PointerExited += (_, _) => grip.Background = Brushes.Transparent;
        Control? target = null;
        var dragging = false;
        grip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) return;
            dragging = true;
            e.Pointer.Capture(grip);
            if (FindBlock(section.Id) is { } me) me.Opacity = 0.45;
            e.Handled = true;
        };
        grip.PointerMoved += (_, e) =>
        {
            if (!dragging) return;
            var over = BlockAt(e);
            if (over == target) return;
            if (target?.Tag is "drop") { }
            Mark(target, false);
            target = over is { Tag: string tag } && tag != "blk:" + section.Id ? over : null;
            Mark(target, true);
        };
        grip.PointerReleased += (_, e) =>
        {
            if (!dragging) return;
            dragging = false;
            e.Pointer.Capture(null);
            Mark(target, false);
            if (FindBlock(section.Id) is { } me) me.Opacity = 1;
            if (target is { Tag: string tag } && _doc is { } doc)
            {
                var to = tag[4..];
                var from = doc.Sections.FindIndex(s => s.Id == section.Id);
                var at = doc.Sections.FindIndex(s => s.Id == to);
                // Hacia arriba queda antes del destino; hacia abajo, después.
                var after = at < from ? (at == 0 ? "start" : doc.Sections[at - 1].Id) : to;
                if (Apply(new CanvasOp("move", Id: section.Id, After: after))) Render(keepScroll: true);
            }
            target = null;
        };
        return grip;
    }

    Control? FindBlock(string id) => (_scroll.Content as Panel)?.Children.OfType<Control>().FirstOrDefault(c => c.Tag is string t && t == "blk:" + id);

    Control? BlockAt(PointerEventArgs e)
    {
        if (_scroll.Content is not Panel column) return null;
        var y = e.GetPosition(column).Y;
        return column.Children.OfType<Control>().Where(c => c.Tag is string t && t.StartsWith("blk:", StringComparison.Ordinal))
            .FirstOrDefault(c => y >= c.Bounds.Top - 7 && y <= c.Bounds.Bottom + 7);
    }

    void Mark(Control? block, bool on)
    {
        if (block is not Grid grid) return;
        var line = grid.Children.OfType<Border>().FirstOrDefault(b => b.Tag is "drop");
        if (on && line is null)
            grid.Children.Add(new Border { Tag = "drop", Height = 2, Background = _ink.Accent, CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14, -6, 14, 0), IsHitTestVisible = false });
        else if (!on && line is not null) grid.Children.Remove(line);
    }

    // ── Piezas ──────────────────────────────────────────────────────────────────────────

    static readonly Dictionary<string, Color> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Claude Code"] = Color.Parse("#D97757"),
        ["Claude"] = Color.Parse("#D97757"),
        ["Codex"] = Color.Parse("#3D7BD9"),
        ["Gemini CLI"] = Color.Parse("#7A5AF8"),
        ["Copilot"] = Color.Parse("#2DA44E"),
        ["Copilot CLI"] = Color.Parse("#2DA44E"),
        ["OpenCode"] = Color.Parse("#CA8A04"),
        ["Cursor"] = Color.Parse("#1F1E1D"),
    };

    static readonly Color[] Spare = [Color.Parse("#C2410C"), Color.Parse("#0E7490"), Color.Parse("#BE185D"), Color.Parse("#4D7C0F")];

    /// <summary>El color de cada autor: el de su marca, o uno fijo por su nombre; usted, el gris de la Console.</summary>
    IBrush AuthorColor(string? author)
    {
        if (author is null or CanvasJson.User) return _ink.Tertiary;
        if (Colors.TryGetValue(author, out var known)) return new SolidColorBrush(_ink.Dark && author == "Cursor" ? Colors["Claude Code"] : known);
        var hash = author.Aggregate(17, (h, c) => h * 31 + c);
        return new SolidColorBrush(Spare[Math.Abs(hash % Spare.Length)]);
    }

    Border Avatar(string? author, double size)
    {
        var user = author is null or CanvasJson.User;
        var color = AuthorColor(author);
        Control content = user
            ? Glyph(CanvasIcons.Pen, Brushes.White, size * 0.5)
            : new TextBlock { Text = Initial(author!), FontSize = size * 0.42, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = color, Child = content };
    }

    /// <summary>La letra del avatar: la de la marca («X» para Codex, que si no sería otra «C»), o la primera del nombre.</summary>
    static string Initial(string author) => author.ToLowerInvariant() switch
    {
        var a when a.Contains("codex") => "X",
        var a when a.Contains("copilot") => "P",
        _ => author.TrimStart()[..1].ToUpperInvariant(),
    };

    /// <summary>Un halo que late alrededor (quien edita ahora, un canvas que se está escribiendo).</summary>
    static void Pulse(Border target, IBrush color)
    {
        var c = (color as ISolidColorBrush)?.Color ?? Avalonia.Media.Colors.Gray;
        var stop = new CancellationTokenSource();
        void Run() => _ = new Animation
        {
            Duration = TimeSpan.FromSeconds(1.4),
            IterationCount = IterationCount.Infinite,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Avalonia.Styling.Setter(Border.BoxShadowProperty, new BoxShadows(new BoxShadow { Spread = 0, Color = Color.FromArgb(0x90, c.R, c.G, c.B) })) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Avalonia.Styling.Setter(Border.BoxShadowProperty, new BoxShadows(new BoxShadow { Spread = 7, Color = Color.FromArgb(0, c.R, c.G, c.B) })) } },
            },
        }.RunAsync(target, stop.Token);
        if (target.IsAttachedToVisualTree()) Run();
        target.AttachedToVisualTree += (_, _) => Run();
        target.DetachedFromVisualTree += (_, _) => { stop.Cancel(); stop = new CancellationTokenSource(); };
    }

    /// <summary>Entra subiendo un poco y apareciendo (o creciendo, con scale), después de delay ms.</summary>
    static void Enter(Control control, int delay, bool scale = false, double distance = 10)
    {
        if (!TerminalPane.Animate) return;
        control.Opacity = 0;
        control.RenderTransformOrigin = RelativePoint.Center;
        control.RenderTransform = TransformOperations.Parse(scale ? "scale(0.85)" : FormattableString.Invariant($"translateY({distance}px)"));
        control.Transitions =
        [
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(380), Delay = TimeSpan.FromMilliseconds(delay) },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(460), Delay = TimeSpan.FromMilliseconds(delay), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
        ];
        control.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            control.Opacity = 1;
            control.RenderTransform = TransformOperations.Parse(scale ? "scale(1)" : "translateY(0px)");
        }, DispatcherPriority.Background);
    }

    /// <summary>Muestra u oculta con transición (las herramientas de cada sección), deslizando o escalando un poco.</summary>
    static void Fade(Control control, double opacity, bool slide = false, bool scale = false)
    {
        control.Transitions ??=
        [
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(170) },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(240), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
        ];
        control.RenderTransformOrigin = RelativePoint.Center;
        control.Opacity = opacity;
        control.IsHitTestVisible = opacity > 0;
        var on = opacity > 0;
        control.RenderTransform = TransformOperations.Parse(slide ? (on ? "translateX(0px)" : "translateX(6px)") : scale ? (on ? "scale(1)" : "scale(0.94)") : "none");
    }

    /// <summary>Una píldora flotante (las herramientas, la línea de versiones, el aviso de versión anterior).</summary>
    Border Floating(Control child, Thickness padding) => new()
    {
        Background = _ink.Raised,
        BorderBrush = _ink.Hairline,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(999),
        Padding = padding,
        BoxShadow = Shadow(),
        Child = child,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    BoxShadows Shadow(bool soft = false) => soft
        ? BoxShadows.Parse(_ink.Dark ? "0 1 3 0 #40000000" : "0 1 3 0 #14000000")
        : BoxShadows.Parse(_ink.Dark ? "0 10 30 0 #59000000" : "0 8 28 0 #17000000");

    static SolidColorBrush WithAlpha(IBrush brush, byte alpha)
    {
        var c = (brush as ISolidColorBrush)?.Color ?? Avalonia.Media.Colors.Gray;
        return new SolidColorBrush(Color.FromArgb((byte)(c.A * alpha / 255), c.R, c.G, c.B));
    }

    static Viewbox Glyph(string path, IBrush brush, double size, double stroke = 1.7) => new()
    {
        Width = size,
        Height = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new Path
        {
            Data = Geometry.Parse(path),
            Stroke = brush,
            StrokeThickness = stroke,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 24,
            Height = 24,
        },
    };

    /// <summary>Un botón redondo de solo ícono, con su nombre al pasar el mouse.</summary>
    Border IconButton(string path, string name, Action onClick, bool fill = false, double size = 34, double glyph = 17, bool on = false, bool accent = false)
    {
        var brush = on || accent ? _ink.Accent : _ink.Secondary;
        var icon = Glyph(path, brush, glyph);
        var shape = (Path)icon.Child!;
        if (fill) { shape.Fill = brush; shape.Stroke = null; }
        var idle = on ? WithAlpha(_ink.Accent, 0x22) : (IBrush)Brushes.Transparent;
        // Con Tag «solid» (un fondo de color puesto después: el ▶, el ✦, el +) el fondo no cambia al pasar el mouse.
        var button = Ui.Press(icon, onClick, (b, hover) =>
        {
            if (b.Tag is "solid") return;
            b.Background = hover ? _ink.Pill : idle;
        }, name);
        button.Width = size;
        button.Height = size;
        button.CornerRadius = new CornerRadius(size / 2);
        button.Background = idle;
        button.RenderTransformOrigin = RelativePoint.Center;
        button.Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(120) }];
        button.AddHandler(PointerPressedEvent, (_, _) => button.RenderTransform = TransformOperations.Parse("scale(0.9)"), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        button.AddHandler(PointerReleasedEvent, (_, _) => button.RenderTransform = TransformOperations.Parse("scale(1)"), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        button.PointerExited += (_, _) => button.RenderTransform = TransformOperations.Parse("scale(1)");
        ToolTip.SetTip(button, name);
        return button;
    }
}
