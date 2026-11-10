namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdInline = Markdig.Syntax.Inlines.Inline;

/// <summary>
/// Un .md de la vista de archivos dibujado como documento, para leerlo bien: títulos, párrafos (negrita, cursiva, tachado,
/// `código`), listas (también de tareas y anidadas), citas, bloques de código, tablas y líneas. Los enlaces van en azul:
/// una URL se abre en el navegador; un archivo (relativo al .md), en su vista. Markdig (BSD-2-Clause) lo entiende; aquí
/// se arma con los colores y las letras de la Terminal, y el texto se selecciona y se copia.
/// </summary>
sealed partial class TerminalPane
{
    static MarkdownPipeline? _markdownPipeline;

    static MarkdownPipeline MarkdownPipeline => _markdownPipeline ??= new MarkdownPipelineBuilder()
        .UsePipeTables().UseGridTables().UseTaskLists().UseAutoLinks().UseEmphasisExtras().UseYamlFrontMatter().Build();

    /// <summary>
    /// Se está dibujando la respuesta de un agente (ReplyView), no un .md: títulos más chicos, el código en el bloque de
    /// la Terminal (con copiar) y los archivos y URL que nombra en el texto, en azul como en el resto de la Terminal.
    /// </summary>
    bool _mdReply;

    /// <summary>La respuesta de un agente con su Markdown (negrita, cursiva, listas, títulos, citas, tablas, código).</summary>
    internal StackPanel ReplyView(string text, string? folder)
    {
        _mdReply = true;
        try
        {
            var page = MarkdownPage(text, folder ?? "");
            page.Spacing = 8;
            return page;
        }
        finally { _mdReply = false; }
    }

    /// <summary>El estilo de un pedazo de texto mientras se recorre un párrafo.</summary>
    readonly record struct MdStyle(bool Bold = false, bool Italic = false, bool Strike = false, bool Code = false, bool Dim = false, TextLink? Link = null);

    /// <summary>El documento entero, con su desplazamiento. folder: la carpeta del .md (sus enlaces relativos se buscan ahí).</summary>
    internal Control MarkdownView(string text, string folder)
    {
        var page = MarkdownPage(text, folder);
        page.Margin = new Thickness(22, 16, 22, 26);
        page.MaxWidth = 800;
        page.HorizontalAlignment = HorizontalAlignment.Left;
        return new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = page };
    }

    /// <summary>Solo los bloques, sin desplazamiento ni márgenes (las secciones de Claupet Canvas).</summary>
    internal StackPanel MarkdownPage(string text, string folder)
    {
        var page = new StackPanel { Spacing = 10 };
        foreach (var block in Markdown.Parse(text, MarkdownPipeline))
            if (MdBlock(block, folder, 0) is { } control) page.Children.Add(control);
        return page;
    }

    Control? MdBlock(Block block, string folder, int depth) => block switch
    {
        HeadingBlock heading => MdHeading(heading, folder),
        ParagraphBlock paragraph => MdText(paragraph.Inline, folder, 13.5),
        ListBlock list => MdList(list, folder, depth),
        QuoteBlock quote => MdQuote(quote, folder, depth),
        Table table => MdTable(table, folder),
        YamlFrontMatterBlock => null, // los datos del principio (---): no son para leer
        CodeBlock code => _mdReply ? Code(code.Lines.ToString().TrimEnd('\n', '\r')) : MdCode(code.Lines.ToString(), dim: false),
        HtmlBlock html => MdCode(html.Lines.ToString(), dim: true),
        ThematicBreakBlock => new Border { Height = 1, Background = _ink.Hairline, Margin = new Thickness(0, 8) },
        LinkReferenceDefinitionGroup => null,
        ContainerBlock container => MdBlocks(container, folder, depth),
        LeafBlock { Inline: { } inline } => MdText(inline, folder, 13.5),
        _ => null,
    };

    StackPanel MdBlocks(IEnumerable<Block> blocks, string folder, int depth)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var block in blocks)
            if (MdBlock(block, folder, depth) is { } control) panel.Children.Add(control);
        return panel;
    }

    Control MdHeading(HeadingBlock heading, string folder)
    {
        var (size, display) = _mdReply
            ? heading.Level switch { 1 => (17.0, true), 2 => (15.5, true), _ => (14.0, false) }
            : heading.Level switch { 1 => (23.0, true), 2 => (19.0, true), 3 => (16.0, false), 4 => (14.5, false), _ => (13.5, false) };
        var text = MdText(heading.Inline, folder, size, FontWeight.SemiBold, display ? Ink.Display : null);
        text.LineHeight = size * 1.3;
        var margin = new Thickness(0, heading.Level <= 2 ? 12 : 6, 0, 0);
        if (heading.Level > 2 || _mdReply)
        {
            if (_mdReply) margin = new Thickness(0, 6, 0, 0);
            text.Margin = margin;
            return text;
        }
        // Los dos primeros, con una línea debajo (como en GitHub).
        return new Border { BorderBrush = _ink.Hairline, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 6), Margin = margin, Child = text };
    }

    Control MdList(ListBlock list, string folder, int depth)
    {
        var panel = new StackPanel { Spacing = 4 };
        var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            // Una tarea ya trae su casilla (☐ o ☑) al principio del texto: sin viñeta.
            var task = item.FirstOrDefault() is ParagraphBlock { Inline.FirstChild: TaskList };
            var mark = Ui.Text(task ? "" : list.IsOrdered ? $"{number}." : depth switch { 0 => "•", 1 => "◦", _ => "▪" }, 13.5, _ink.Tertiary);
            mark.MinWidth = task ? 0 : list.IsOrdered ? 24 : 16;
            mark.LineHeight = 13.5 * _look.LineSpacing;
            mark.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(mark);
            var content = MdBlocks(item, folder, depth + 1);
            content.Spacing = 4;
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            panel.Children.Add(row);
            number++;
        }
        return panel;
    }

    Control MdQuote(QuoteBlock quote, string folder, int depth) => new Border
    {
        BorderBrush = Translucent(_ink.Accent, 0.55),
        BorderThickness = new Thickness(3, 0, 0, 0),
        Padding = new Thickness(12, 2, 0, 2),
        Opacity = 0.85,
        Child = MdBlocks(quote, folder, depth),
    };

    Control MdCode(string code, bool dim)
    {
        var text = Selectable(code.TrimEnd('\n', '\r'), Mono, 12.5, dim ? _ink.Tertiary : _ink.Text);
        text.TextWrapping = TextWrapping.NoWrap;
        return new Border
        {
            Background = _ink.Window,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9),
            Child = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = text },
        };
    }

    Control MdTable(Table table, string folder)
    {
        var rows = table.OfType<TableRow>().ToList();
        var columns = Math.Max(1, rows.Count == 0 ? 1 : rows.Max(r => r.Count));
        var grid = new Grid();
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var row = rows[r];
            for (var c = 0; c < columns; c++)
            {
                var cell = c < row.Count ? row[c] as TableCell : null;
                Control content = cell is null ? new Panel() : MdBlocks(cell, folder, 0);
                if (content is StackPanel stack)
                    foreach (var text in stack.Children.OfType<SelectableTextBlock>())
                    {
                        text.MaxWidth = 340;
                        if (row.IsHeader) text.FontWeight = FontWeight.SemiBold;
                    }
                var align = c < table.ColumnDefinitions.Count ? table.ColumnDefinitions[c].Alignment : null;
                content.HorizontalAlignment = align switch
                {
                    TableColumnAlign.Center => HorizontalAlignment.Center,
                    TableColumnAlign.Right => HorizontalAlignment.Right,
                    _ => HorizontalAlignment.Left,
                };
                var border = new Border
                {
                    BorderBrush = _ink.Hairline,
                    BorderThickness = new Thickness(c == 0 ? 1 : 0, r == 0 ? 1 : 0, 1, 1),
                    Background = row.IsHeader ? _ink.Pill : Brushes.Transparent,
                    Padding = new Thickness(10, 5),
                    Child = content,
                };
                Grid.SetRow(border, r);
                Grid.SetColumn(border, c);
                grid.Children.Add(border);
            }
        }
        // Una tabla ancha se desliza hacia el costado (no aprieta las columnas).
        return new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = grid };
    }

    /// <summary>Un párrafo (o un título, o una celda): sus pedazos con su estilo y sus enlaces, que responden al clic.</summary>
    SelectableTextBlock MdText(ContainerInline? inline, string folder, double size, FontWeight weight = FontWeight.Normal, FontFamily? family = null)
    {
        var block = Selectable(null, family ?? Ink.Sans, size, _ink.Text);
        block.FontWeight = weight;
        var inlines = new InlineCollection();
        var links = new List<(int Start, int End, TextLink Link)>();
        var at = 0;
        Walk(inline, default);
        block.Inlines = inlines;
        if (links.Count > 0) LinkClicks.Attach(block, links, OpenLink);
        return block;

        void Walk(MdInline? node, MdStyle style)
        {
            if (node is not ContainerInline container) return;
            for (var child = container.FirstChild; child is not null; child = child.NextSibling)
            {
                // Markdig corta el texto en los _ y * que no son énfasis: se junta de nuevo para que una ruta
                // como «mi_archivo.cs» se reconozca entera como enlace.
                if (child is LiteralInline && child.NextSibling is LiteralInline)
                {
                    var joined = new System.Text.StringBuilder();
                    for (; child is LiteralInline piece; child = child.NextSibling!)
                    {
                        joined.Append(piece.Content.ToString());
                        if (child.NextSibling is not LiteralInline) break;
                    }
                    Linked(joined.ToString(), style);
                    continue;
                }
                Visit(child, style);
            }
        }

        void Visit(MdInline node, MdStyle style)
        {
            switch (node)
            {
                case LiteralInline literal:
                    Linked(literal.Content.ToString(), style);
                    break;
                case CodeInline code:
                    Linked(code.Content, style with { Code = true });
                    break;
                case EmphasisInline emphasis:
                    Walk(emphasis, emphasis.DelimiterChar == '~' ? style with { Strike = true }
                        : emphasis.DelimiterCount >= 2 ? style with { Bold = true } : style with { Italic = true });
                    break;
                case LinkInline link:
                    var target = MdLink(link.Url, folder);
                    var before = at;
                    Walk(link, style with { Link = target });
                    // Sin texto propio (una imagen sin descripción, <url>): se muestra la dirección.
                    if (at == before) Add((link.IsImage ? "🖼 " : "") + (link.Url ?? ""), style with { Link = target });
                    else if (link.IsImage) Add(" 🖼", style with { Link = target });
                    break;
                case AutolinkInline auto:
                    Add(auto.Url, style with { Link = MdLink(auto.IsEmail ? "mailto:" + auto.Url : auto.Url, folder) });
                    break;
                case LineBreakInline lineBreak:
                    // En una respuesta, cada salto de línea se respeta (como antes); en un .md, no.
                    Add(lineBreak.IsHard || _mdReply ? "\n" : " ", style);
                    break;
                case HtmlEntityInline entity:
                    Add(entity.Transcoded.ToString(), style);
                    break;
                case HtmlInline html:
                    Add(html.Tag is "<br>" or "<br/>" or "<br />" ? "\n" : html.Tag, html.Tag.StartsWith("<br", StringComparison.OrdinalIgnoreCase) ? style : style with { Dim = true });
                    break;
                case TaskList task:
                    Add(task.Checked ? "☑" : "☐", style); // el texto que sigue ya trae su espacio
                    break;
                case ContainerInline container:
                    Walk(container, style);
                    break;
            }
        }

        // En una respuesta, los archivos y URL que se nombran sin enlace de Markdown también se abren con un clic.
        void Linked(string text, MdStyle style)
        {
            if (!_mdReply || style.Link is not null)
            {
                Add(text, style);
                return;
            }
            var cut = 0;
            foreach (var link in TextLinks.Find(text, folder.Length > 0 ? folder : null).OrderBy(l => l.Start))
            {
                if (link.Start < cut) continue;
                Add(text[cut..link.Start], style);
                Add(text.Substring(link.Start, link.Length), style with { Link = link });
                cut = link.Start + link.Length;
            }
            Add(text[cut..], style);
        }

        void Add(string text, MdStyle style)
        {
            if (text.Length == 0) return;
            var run = new Run(text);
            if (style.Bold) run.FontWeight = FontWeight.Bold;
            if (style.Italic) run.FontStyle = FontStyle.Italic;
            if (style.Code)
            {
                run.FontFamily = Mono;
                run.FontSize = Math.Max(11, size - 1);
                run.Background = _ink.Pill;
            }
            if (style.Dim) run.Foreground = _ink.Tertiary;
            if (style.Link is { } link)
            {
                run.Foreground = LinkBrush;
                run.TextDecorations = TextDecorations.Underline;
                links.Add((at, at + text.Length, link));
            }
            else if (style.Strike) run.TextDecorations = TextDecorations.Strikethrough;
            inlines.Add(run);
            at += text.Length;
        }
    }

    /// <summary>
    /// A dónde lleva un enlace del .md: una URL (http, https, mailto) o un archivo que existe, relativo a la carpeta del .md
    /// (sin el «#sección»). Un ancla sola o un archivo que no está: null (se ve como texto).
    /// </summary>
    static TextLink? MdLink(string? url, string folder)
    {
        if (string.IsNullOrWhiteSpace(url) || url.StartsWith('#')) return null;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            return new TextLink(0, 0, LinkKind.Url, url);
        try
        {
            var path = Uri.UnescapeDataString(url.Split('#')[0]);
            if (path.Length == 0) return null;
            var full = Path.GetFullPath(Path.IsPathFullyQualified(path) ? path : Path.Combine(folder, path));
            if (File.Exists(full)) return new TextLink(0, 0, LinkKind.File, full);
            return Directory.Exists(full) ? new TextLink(0, 0, LinkKind.Folder, full) : null;
        }
        catch (Exception) { return null; }
    }
}
