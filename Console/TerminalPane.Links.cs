namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;

/// <summary>
/// Los enlaces de Claupet Terminal: las URL y los archivos que nombra un agente se pintan en azul y subrayados; con un
/// clic (sin arrastrar), una URL se abre en el navegador y un archivo en su vista (TerminalPane.Viewer.cs). El texto se
/// sigue seleccionando y copiando como siempre.
/// </summary>
sealed partial class TerminalPane
{
    /// <summary>El azul de los enlaces, legible sobre el fondo de la Terminal (claro u oscuro).</summary>
    IBrush LinkBrush => LinkClicks.Brush(_ink.Dark);

    static Cursor HandCursor => LinkClicks.Hand;

    /// <summary>Un párrafo de una respuesta: su `código` con fondo y sus enlaces (URL y archivos que existen) en azul.</summary>
    internal SelectableTextBlock Paragraph(string text)
    {
        var folder = _current?.Agent.Options.Folder;
        var inlines = new InlineCollection();
        var links = new List<(int Start, int End, TextLink Link)>();
        var at = 0;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            // Los saltos van dentro del texto (y no como LineBreak): así cada carácter tiene su posición y el clic se ubica.
            if (i > 0) Add("\n", false, null);
            foreach (var (piece, code, link) in LinkClicks.Pieces(lines[i], TextLinks.Find(lines[i], folder))) Add(piece, code, link);
        }
        var block = Selectable(null, Ink.Sans, 13.5, _ink.Text);
        block.Inlines = inlines;
        if (links.Count > 0) LinkClicks.Attach(block, links, OpenLink);
        return block;

        void Add(string piece, bool code, TextLink? link)
        {
            if (piece.Length == 0) return;
            var run = new Run(piece);
            if (code)
            {
                run.FontFamily = Mono;
                run.FontSize = 12.5;
                run.Background = _ink.Pill;
            }
            if (link is not null)
            {
                run.Foreground = LinkBrush;
                run.TextDecorations = TextDecorations.Underline;
                links.Add((at, at + piece.Length, link));
            }
            inlines.Add(run);
            at += piece.Length;
        }
    }

    /// <summary>La salida de un comando, en letra fija: sus URL, archivos y carpetas en azul, relativos a donde corrió.</summary>
    internal SelectableTextBlock OutputText(IReadOnlyList<string> lines, string? folder, IBrush brush)
    {
        var text = string.Join("\n", lines);
        var output = Selectable(text, Mono, 12.5, brush);
        var links = new List<(int Start, int End, TextLink Link)>();
        var at = 0;
        foreach (var line in lines)
        {
            foreach (var link in TextLinks.Find(line, folder))
                links.Add((at + link.Start, at + link.Start + link.Length, link));
            at += line.Length + 1;
        }
        if (links.Count == 0) return output;

        var inlines = new InlineCollection();
        var cut = 0;
        foreach (var (start, end, _) in links)
        {
            if (start > cut) inlines.Add(new Run(text[cut..start]));
            inlines.Add(new Run(text[start..end]) { Foreground = LinkBrush, TextDecorations = TextDecorations.Underline });
            cut = end;
        }
        if (cut < text.Length) inlines.Add(new Run(text[cut..]));
        output.Text = null;
        output.Inlines = inlines;
        LinkClicks.Attach(output, links, OpenLink);
        return output;
    }

    static readonly string[] FileTools = ["Read", "Edit", "MultiEdit", "Write", "NotebookEdit", "NotebookRead"];

    /// <summary>
    /// El archivo de un paso (leyó, editó, creó) en azul: tocarlo abre su vista; el resto de la fila sigue abriendo el
    /// cambio o lo que devolvió.
    /// </summary>
    void LinkStepTarget(TextBlock target, AgentBlock block)
    {
        if (block.Tool is not { } tool || !FileTools.Contains(tool) || block.Text.Length == 0 || _current?.Agent.Options.Folder is not { } folder) return;
        string full;
        try { full = Path.GetFullPath(Path.IsPathFullyQualified(block.Text) ? block.Text : Path.Combine(folder, block.Text)); }
        catch (Exception) { return; }
        if (!File.Exists(full)) return;
        target.Foreground = LinkBrush;
        target.TextDecorations = TextDecorations.Underline;
        target.Cursor = HandCursor;
        ToolTip.SetTip(target, L.T("Ver {0}", full));
        var pressed = false;
        target.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(target).Properties.IsLeftButtonPressed) return;
            pressed = true;
            e.Handled = true; // la fila no se abre
        };
        target.PointerReleased += (_, e) =>
        {
            if (!pressed) return;
            pressed = false;
            e.Handled = true;
            OpenFile(full);
        };
    }

    /// <summary>
    /// «Ver el plan», en azul, cuando Claude Code propone un plan (ExitPlanMode): lo abre en la vista de archivos (que se
    /// puede fijar a la derecha). Su archivo, si lo trae (planFilePath); si no, el texto guardado en un .md temporal.
    /// </summary>
    Control? PlanLink(AgentBlock block)
    {
        if (block.Tool != "ExitPlanMode" || block.Input is not System.Text.Json.Nodes.JsonObject input) return null;
        var path = (string?)input["planFilePath"] is { Length: > 0 } file && File.Exists(file) ? file : null;
        if (path is null && (string?)input["plan"] is { Length: > 0 } text)
        {
            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "Claupet", "planes");
                Directory.CreateDirectory(dir);
                var name = string.Concat((block.Id.Length > 0 ? block.Id : Guid.NewGuid().ToString("N")).Where(char.IsLetterOrDigit));
                path = Path.Combine(dir, $"plan-{name}.md");
                if (!File.Exists(path) || File.ReadAllText(path) != text) File.WriteAllText(path, text);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ErrorLog.Write(ex);
                return null;
            }
        }
        if (path is null) return null;
        var label = Ui.Text(L.T("Ver el plan"), 12.5, LinkBrush, FontWeight.Medium);
        label.TextDecorations = TextDecorations.Underline;
        var open = Ui.Press(label, () => OpenFile(path), null, L.T("Ver el plan"));
        open.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        open.Margin = new Thickness(24, 0, 0, 2);
        ToolTip.SetTip(open, path);
        return open;
    }

    /// <summary>Cómo se abre una URL o una carpeta: con el sistema (las pruebas lo cambian para no abrirlo).</summary>
    internal static Action<string> OpenUrl { get; set; } = Shell.Open;

    /// <summary>Una URL, en el navegador; una carpeta, en el explorador del sistema; un archivo, en su vista.</summary>
    void OpenLink(TextLink link)
    {
        if (link.Kind is LinkKind.Url or LinkKind.Folder) OpenUrl(link.Target);
        else OpenFile(link.Target, link.Line);
    }
}
