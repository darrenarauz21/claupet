namespace Claupet;

using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;

/// <summary>
/// La vista de un archivo que nombra un agente (un plan, un .md, un .cs, una imagen): se abre en una tarjeta flotante
/// sobre la sesión; arrastrada hacia la derecha (o con «Fijar») queda en el panel de la derecha con su ícono, junto a
/// Ramas, Cambios y Procesos, y se quita con su ×. Markdown con el aspecto del modo Documento, código con sus colores y
/// sus números de línea, imágenes; lo que no se puede ver aquí (binario, muy grande) se abre con el programa del sistema.
/// </summary>
sealed partial class TerminalPane
{
    const string FileIcon = "M7,3 H14 L19,8 V20 A1,1 0 0 1 18,21 H7 A1,1 0 0 1 6,20 V4 A1,1 0 0 1 7,3 Z M14,3 V8 H19";
    const string ImageFileIcon = "M5,4 H19 A1,1 0 0 1 20,5 V19 A1,1 0 0 1 19,20 H5 A1,1 0 0 1 4,19 V5 A1,1 0 0 1 5,4 Z M4,16 L9,11 L14,16 M13,15 L16,12 L20,16 M15.5,8.5 A1,1 0 1 0 15.51,8.5 Z";
    const string PinIcon = "M9,4 H15 M10,4 V10 L7,14 H17 L14,10 V4 M12,14 V20";
    const string ExternalIcon = "M14,4 H20 V10 M20,4 L11,13 M18,14 V19 A1,1 0 0 1 17,20 H5 A1,1 0 0 1 4,19 V7 A1,1 0 0 1 5,6 H10";
    /// <summary>Hasta cuánto se lee un archivo de texto para verlo; más grande, se abre con el programa del sistema.</summary>
    const long ViewLimit = 2_000_000;
    /// <summary>A cuántos píxeles del borde derecho de la sesión se suelta la tarjeta para fijarla en el panel.</summary>
    const double DockZone = 110;
    static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico"];
    static readonly string[] MarkdownExtensions = [".md", ".markdown", ".mdx"];

    static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Abre un archivo: si ya está fijado, se muestra en el panel; si no, en la tarjeta flotante.</summary>
    internal void OpenFile(string path, int? line = null)
    {
        if (_current is not { } tab) return;
        if (tab.Shell is null && tab.Docked.FirstOrDefault(d => SamePath(d, path)) is { } docked)
        {
            tab.SideFile = docked;
            tab.Preview = null;
            OpenPanel();
        }
        else tab.Preview = (path, line);
        Build();
    }

    void ClosePreview()
    {
        if (_current is not { } tab) return;
        tab.Preview = null;
        Build();
    }

    /// <summary>Fija el archivo en el panel de la derecha (lo abre si estaba cerrado) y cierra la tarjeta.</summary>
    internal void Dock(string path)
    {
        if (_current is not { Shell: null } tab) return;
        if (!tab.Docked.Any(d => SamePath(d, path))) tab.Docked.Add(path);
        tab.SideFile = tab.Docked.First(d => SamePath(d, path));
        tab.Preview = null;
        OpenPanel();
        Build();
    }

    /// <summary>Quita un archivo fijado: el panel vuelve a la vista de antes (Ramas, Cambios, Procesos o Historial).</summary>
    internal void Undock(Tab tab, string path)
    {
        tab.Docked.RemoveAll(d => SamePath(d, path));
        if (tab.SideFile is { } shown && SamePath(shown, path)) tab.SideFile = null;
        Build();
    }

    void OpenPanel()
    {
        if (_projects.PanelOpen) return;
        _projects.PanelOpen = true;
        _projects.Save();
    }

    /// <summary>
    /// La tarjeta flotante, si la pestaña tiene un archivo abierto: del lado derecho de la sesión, debajo de su franja.
    /// Se arrastra por su cabecera; soltada cerca del borde derecho (o sobre el panel), queda fijada en el panel.
    /// </summary>
    void AddPreview(Tab tab)
    {
        if (tab.Preview is not { } preview) return;
        var canDock = tab.Shell is null;
        var layer = new Panel { ClipToBounds = false };
        Grid.SetRowSpan(layer, Math.Max(1, RowDefinitions.Count));
        var hint = new Border
        {
            Width = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 54, 4, 16),
            CornerRadius = new CornerRadius(3),
            Background = _ink.Accent,
            Opacity = 0.7,
            IsVisible = false,
        };
        layer.Children.Add(hint);

        var name = Ui.Text(Path.GetFileName(preview.Path), 13, _ink.Text, FontWeight.SemiBold);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var where = Ui.Text(ShortPath(Path.GetDirectoryName(preview.Path) ?? ""), 11.5, _ink.Tertiary, family: Mono);
        where.TextTrimming = TextTrimming.PrefixCharacterEllipsis;
        var titles = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) };
        titles.Children.Add(name);
        titles.Children.Add(where);
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"), Height = 46, Background = Brushes.Transparent, Margin = new Thickness(12, 0, 8, 0) };
        head.Children.Add(Icon(IsImage(preview.Path) ? ImageFileIcon : FileIcon, LinkBrush, 15, 1.7));
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);
        if (canDock)
        {
            var pin = Round(PinIcon, L.T("Fijar a la derecha (o arrástrela hacia allá)"), () => Dock(preview.Path));
            Grid.SetColumn(pin, 2);
            head.Children.Add(pin);
        }
        var external = Round(ExternalIcon, L.T("Abrir con el programa del sistema"), () => Shell.Open(preview.Path));
        Grid.SetColumn(external, 3);
        head.Children.Add(external);
        var close = Round(CloseIcon, L.T("Cerrar (Esc)"), ClosePreview);
        Grid.SetColumn(close, 4);
        head.Children.Add(close);
        ToolTip.SetTip(titles, preview.Path);

        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        body.Children.Add(head);
        var view = new Border { BorderBrush = _ink.Hairline, BorderThickness = new Thickness(0, 1, 0, 0), Child = FileView(preview.Path, preview.Line) };
        Grid.SetRow(view, 1);
        body.Children.Add(view);
        var move = new TranslateTransform();
        var card = new Border
        {
            Width = Math.Clamp(SessionWidth() * 0.5, 320, 680),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 54, 16, 16),
            CornerRadius = new CornerRadius(12),
            Background = _ink.Raised,
            BorderBrush = _ink.Hairline,
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 8 28 0 #40000000"),
            ClipToBounds = true,
            RenderTransform = move,
            Child = body,
            Focusable = true,
        };
        card.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            ClosePreview();
        };
        layer.Children.Add(card);

        // Arrastrar por la cabecera; los botones no arrastran (Ui.Press se queda con su clic).
        head.Cursor = new Cursor(StandardCursorType.SizeAll);
        Point? from = null;
        Vector start = default;
        bool Near(PointerEventArgs e) => canDock && e.GetPosition(this).X > SessionWidth() - DockZone;
        head.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(head).Properties.IsLeftButtonPressed) return;
            from = e.GetPosition(this);
            start = new Vector(move.X, move.Y);
            e.Pointer.Capture(head);
            e.Handled = true;
        };
        head.PointerMoved += (_, e) =>
        {
            if (from is not { } origin) return;
            var delta = e.GetPosition(this) - origin;
            move.X = start.X + delta.X;
            move.Y = start.Y + delta.Y;
            hint.IsVisible = Near(e);
        };
        head.PointerReleased += (_, e) =>
        {
            if (from is null) return;
            from = null;
            e.Pointer.Capture(null);
            hint.IsVisible = false;
            if (Near(e)) Dock(preview.Path);
        };
        Children.Add(layer);
        Dispatcher.UIThread.Post(() => card.Focus(), DispatcherPriority.Input);
    }

    /// <summary>El ancho de la sesión (sin el panel de la derecha).</summary>
    double SessionWidth() => Bounds.Width - (_current is { Shell: null } && _projects.PanelOpen ? _projects.PanelWidth : 0);

    static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>
    /// Lo que se ve de un archivo: una imagen; Markdown y texto con el aspecto del modo Documento; código con sus colores y
    /// sus números de línea (y en esa línea, si vino con «:42»). Solo para mirar: no se edita aquí.
    /// </summary>
    Control FileView(string path, int? line)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return ViewNote(L.T("Este archivo ya no está."), path, openable: false);
            if (IsImage(path))
            {
                using var stream = File.OpenRead(path);
                return new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = new Image { Source = new Bitmap(stream), Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(14) },
                };
            }
            if (info.Length > ViewLimit) return ViewNote(L.T("Es muy grande para verlo aquí ({0:N0} KB).", info.Length / 1024), path, openable: true);
            var bytes = File.ReadAllBytes(path);
            if (Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, 8000)) >= 0) return ViewNote(L.T("No es un archivo de texto."), path, openable: true);
            string text;
            using (var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true))
                text = reader.ReadToEnd();
            // Un .md se lee como documento (títulos, listas, tablas…), no con sus # y sus **.
            if (MarkdownExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                return MarkdownView(text, Path.GetDirectoryName(path) ?? "");
            return TextView(path, text, line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return ViewNote(L.T("No se pudo leer: {0}", ex.Message), path, openable: true);
        }
    }

    Control TextView(string path, string text, int? line)
    {
        var colors = EditorInk.For(_ink);
        var editor = new TextEditor
        {
            Document = new TextDocument(text),
            IsReadOnly = true,
            Background = Brushes.Transparent,
            Foreground = _ink.Text,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        editor.TextArea.SelectionBrush = Translucent(_ink.Accent, 0.3);
        editor.TextArea.SelectionForeground = null;
        editor.TextArea.SelectionBorder = null;
        editor.TextArea.Caret.CaretBrush = Brushes.Transparent; // solo para mirar
        var view = editor.TextArea.TextView;
        if (Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            // Texto común: con la letra de leer y las líneas largas partidas.
            editor.WordWrap = true;
            editor.FontFamily = Ink.Sans;
            editor.FontSize = 13.5;
            editor.Padding = new Thickness(18, 14, 18, 14);
        }
        else
        {
            editor.ShowLineNumbers = true;
            editor.LineNumbersForeground = colors.LineNumber;
            editor.FontFamily = Mono;
            editor.FontSize = 12.5;
            editor.Padding = new Thickness(4, 10, 14, 10);
            if (HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(path)) is { } definition)
                view.LineTransformers.Add(new ThemedColorizer(definition, colors));
        }
        if (line is { } number && number > 0)
            Dispatcher.UIThread.Post(() =>
            {
                var target = Math.Min(number, editor.Document.LineCount);
                editor.TextArea.Caret.Line = target;
                editor.ScrollTo(target, 1);
            }, DispatcherPriority.Background);
        return editor;
    }

    /// <summary>Lo que no se puede ver aquí: por qué y, si se puede, el botón para abrirlo con el programa del sistema.</summary>
    Control ViewNote(string why, string path, bool openable)
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(20), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var text = Ui.Text(why, 13, _ink.Secondary);
        text.TextWrapping = TextWrapping.Wrap;
        text.TextAlignment = TextAlignment.Center;
        panel.Children.Add(text);
        if (openable)
        {
            var open = TextButton(L.T("Abrir con el programa del sistema"), () => Shell.Open(path), accent: true);
            open.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(open);
        }
        return panel;
    }
}
