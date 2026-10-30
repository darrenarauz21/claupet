namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

/// <summary>
/// El chat de Claupet Console, como en el diseño: arriba el título, el modelo, leer en voz alta y más opciones; los
/// mensajes en una columna de hasta 720 (los suyos en burbuja; los de Claupet con su cabeza, los bloques de código con
/// «Copiar» y de dónde vino la respuesta) y abajo el cuadro para escribir. Lo que muestra lo manda la bandeja; lo que
/// se hace aquí se le pide a ella con Request.
/// </summary>
sealed class ChatPane : Grid
{
    /// <summary>Un mensaje para la bandeja: los del chat (con la conversación en Key) o «accion» (el modelo, leer en voz alta).</summary>
    public event Action<ConsoleMessage>? Request;

    const double Column = 720;

    // Los íconos, en un cuadro de 24 (los del diseño).
    const string CloudIcon = "M7,18 H17 A4,4 0 0 0 17.6,10.04 A6,6 0 0 0 6.1,9.2 A4.5,4.5 0 0 0 7,18 Z";
    const string ChevronIcon = "M6,9 L12,15 L18,9";
    const string SpeakerIcon = "M4,9 V15 H8 L13,19 V5 L8,9 H4 Z M16.5,8.5 A5,5 0 0 1 16.5,15.5";
    const string MoreIcon = "M3.4,12 A1.6,1.6 0 1 0 6.6,12 A1.6,1.6 0 1 0 3.4,12 Z M10.4,12 A1.6,1.6 0 1 0 13.6,12 A1.6,1.6 0 1 0 10.4,12 Z "
        + "M17.4,12 A1.6,1.6 0 1 0 20.6,12 A1.6,1.6 0 1 0 17.4,12 Z";
    const string AttachIcon = "M20,11.5 L12.5,19 A5,5 0 0 1 5.5,12 L13,4.5 A3.5,3.5 0 0 1 18,9.5 L10.5,17 A2,2 0 0 1 7.5,14 L14.5,7";
    const string MicIcon = "M9,6 A3,3 0 0 1 15,6 V11 A3,3 0 0 1 9,11 Z M5,11 A7,7 0 0 0 19,11 M12,18 V21";
    const string SendIcon = "M12,19 V5 M6,11 L12,5 L18,11";
    const string StopIcon = "M8.5,7 H15.5 A1.5,1.5 0 0 1 17,8.5 V15.5 A1.5,1.5 0 0 1 15.5,17 H8.5 A1.5,1.5 0 0 1 7,15.5 V8.5 A1.5,1.5 0 0 1 8.5,7 Z";

    readonly List<(ChatTurn Turn, Control View)> _turns = [];
    readonly StackPanel _messages = new() { Spacing = 22, MaxWidth = Column };
    readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    readonly TextBox _input = new();
    Ink _ink = Ink.For(false);
    ChatView? _chat;
    Control? _streaming;
    Control? _empty;
    TextBlock _title = new();
    TextBlock _model = new();
    Border _modelPill = new();
    Border _readAloud = new();
    Border _send = new();
    /// <summary>Los botones de la cabecera y, en su lugar mientras se pregunta, «¿Borrar esta conversación?».</summary>
    StackPanel _tools = new();
    StackPanel _confirm = new();
    /// <summary>Al enviar se baja hasta el final aunque se estuviera leyendo más arriba.</summary>
    bool _toEnd;

    public ChatPane()
    {
        RowDefinitions = new RowDefinitions("56,*,Auto");
        _scroll.Content = new Border { Child = _messages, Padding = new Thickness(24, 8, 24, 24) };
        // Intro envía y Mayús+Intro hace otra línea: se atiende antes de que el cuadro ponga el salto.
        _input.AddHandler(KeyDownEvent, OnInputKey, RoutingStrategies.Tunnel);
        Build();
    }

    /// <summary>El id de la conversación que se muestra (null: todavía ninguna).</summary>
    public string? ChatId => _chat?.Id;

    /// <summary>Con otro tema (claro u oscuro, otro color) se rehace todo; con el mismo no se toca nada.</summary>
    public void SetInk(Ink ink)
    {
        if (ReferenceEquals(ink, _ink)) return;
        _ink = ink;
        Build();
        if (_chat is { } chat)
        {
            _chat = null;
            Show(chat);
        }
    }

    /// <summary>Junto a qué se muestra la lista de esa fila (el modelo, en la cabecera); null si no está aquí.</summary>
    public Control? AnchorFor(string? key) => key == "imodel" ? _modelPill : null;

    public void FocusInput() => _input.Focus();

    /// <summary>Muestra la conversación; si es la misma, solo agrega o cambia lo que llegó (no se reconstruye todo).</summary>
    public void Show(ChatView chat)
    {
        var same = _chat?.Id == chat.Id;
        if (!same) AskDelete(false);
        var atEnd = !same || _toEnd || _scroll.Offset.Y >= _scroll.Extent.Height - _scroll.Viewport.Height - 48;
        _toEnd = false;
        _chat = chat;
        _title.Text = chat.Title;
        _model.Text = chat.Model;
        PaintReadAloud(chat.ReadAloud);
        PaintSend(chat.Asking);

        if (!same)
        {
            _turns.Clear();
            _messages.Children.Clear();
            _streaming = null;
            _empty = null;
        }
        if (_empty is not null)
        {
            _messages.Children.Remove(_empty);
            _empty = null;
        }
        var keep = 0;
        while (keep < _turns.Count && keep < chat.Turns.Count && _turns[keep].Turn == chat.Turns[keep]) keep++;
        for (var i = _turns.Count - 1; i >= keep; i--)
        {
            _messages.Children.Remove(_turns[i].View);
            _turns.RemoveAt(i);
        }
        if (_streaming is not null)
        {
            _messages.Children.Remove(_streaming);
            _streaming = null;
        }
        for (var i = keep; i < chat.Turns.Count; i++)
        {
            var turn = chat.Turns[i];
            var view = turn.FromPerson ? PersonBubble(turn.Text) : Answer(turn.Text, turn.Who, writing: false);
            _messages.Children.Add(view);
            _turns.Add((turn, view));
        }
        if (chat.Streaming is { } streaming)
        {
            _streaming = Answer(streaming, null, writing: true);
            _messages.Children.Add(_streaming);
        }
        if (chat.Turns.Count == 0 && chat.Streaming is null)
        {
            _empty = Empty(chat.About);
            _messages.Children.Add(_empty);
        }
        if (atEnd) Dispatcher.UIThread.Post(_scroll.ScrollToEnd, DispatcherPriority.Background);
    }

    void Build()
    {
        // El cuadro de texto es el mismo (no se pierde lo escrito): se suelta de la tarjeta anterior.
        (_input.Parent as Panel)?.Children.Remove(_input);
        Children.Clear();
        Children.Add(Header());
        Grid.SetRow(_scroll, 1);
        Children.Add(_scroll);
        var composer = Composer();
        Grid.SetRow(composer, 2);
        Children.Add(composer);
        _turns.Clear();
        _messages.Children.Clear();
        _streaming = null;
        _empty = null;
    }

    // ── Cabecera ────────────────────────────────────────────────────────────────────────────

    Control Header()
    {
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(24, 0, 16, 0) };
        _title = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            FontFamily = Ink.Sans,
            Foreground = _ink.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        bar.Children.Add(_title);

        var tools = _tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        _model = Ui.Text("", 13, _ink.Secondary);
        var pill = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        pill.Children.Add(Icon(CloudIcon, _ink.Secondary, 14, 1.8));
        pill.Children.Add(_model);
        pill.Children.Add(Icon(ChevronIcon, _ink.Secondary, 12, 2));
        _modelPill = Ui.Press(pill, () => Act("imodel"), (b, on) => b.Background = on ? _ink.Track : _ink.Pill, L.T("Cambiar de modelo"));
        _modelPill.Background = _ink.Pill;
        _modelPill.Height = 32;
        _modelPill.Padding = new Thickness(12, 0);
        _modelPill.CornerRadius = new CornerRadius(999);
        ToolTip.SetTip(_modelPill, L.T("Cambiar de modelo"));
        tools.Children.Add(_modelPill);

        _readAloud = Round(SpeakerIcon, L.T("Leer en voz alta las respuestas"), () => Act("readaloud"));
        tools.Children.Add(_readAloud);
        Border? more = null;
        more = Round(MoreIcon, L.T("Más opciones"), () => ShowMore(more!), fill: true);
        tools.Children.Add(more);
        Grid.SetColumn(tools, 1);
        bar.Children.Add(tools);

        // Borrar no se puede deshacer: se pregunta aquí mismo, en lugar de los botones.
        _confirm = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        var question = Ui.Text(L.T("¿Borrar esta conversación? No se puede deshacer."), 13, _ink.Secondary);
        question.Margin = new Thickness(0, 0, 4, 0);
        _confirm.Children.Add(question);
        _confirm.Children.Add(Pill(L.T("Cancelar"), _ink.Pill, _ink.Text, () => AskDelete(false)));
        // En oscuro el rojo es claro: la letra va oscura, como en los botones de acento.
        IBrush onDanger = _ink.Dark ? new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x13)) : Brushes.White;
        _confirm.Children.Add(Pill(L.T("Borrar"), _ink.Danger, onDanger, () =>
        {
            AskDelete(false);
            Ask(ConsoleLink.ChatDelete);
        }));
        Grid.SetColumn(_confirm, 1);
        bar.Children.Add(_confirm);
        return bar;
    }

    void AskDelete(bool asking)
    {
        _confirm.IsVisible = asking;
        _tools.IsVisible = !asking;
    }

    void PaintReadAloud(bool on)
    {
        _readAloud.Tag = on; // encendido, conserva el fondo aunque el cursor se vaya
        _readAloud.Background = on ? _ink.Pill : Brushes.Transparent;
        if (_readAloud.Child is Viewbox { Child: Avalonia.Controls.Shapes.Path path }) path.Stroke = on ? _ink.Accent : _ink.Secondary;
        ToolTip.SetTip(_readAloud, on ? L.T("Leer en voz alta las respuestas: encendido") : L.T("Leer en voz alta las respuestas"));
    }

    void ShowMore(Control anchor)
    {
        if (_chat is not { } chat) return;
        DropList.Entry entry = chat.Id == ConsoleLink.QuickChat
            ? new("clear", L.T("Vaciar la conversación"))
            : new("delete", L.T("Borrar la conversación…"), Danger: true);
        DropList.Show(anchor, _ink, [entry], picked =>
        {
            if (picked == "clear") Ask(ConsoleLink.ChatClear);
            else if (picked == "delete") AskDelete(true);
        });
    }

    // ── Mensajes ────────────────────────────────────────────────────────────────────────────

    Control PersonBubble(string text) => new Border
    {
        Background = _ink.Pill,
        CornerRadius = new CornerRadius(20),
        Padding = new Thickness(16, 10),
        HorizontalAlignment = HorizontalAlignment.Right,
        MaxWidth = Column * 0.78,
        Child = Selectable(text),
    };

    /// <summary>Una respuesta de Claupet: su cabeza y, al lado, el texto, los bloques de código y de dónde vino.</summary>
    Control Answer(string text, string? who, bool writing)
    {
        var column = new StackPanel { Spacing = 10, Margin = new Thickness(12, 0, 0, 0) };
        foreach (var segment in Conversations.Split(text))
            column.Children.Add(segment.Code ? CodeBlock(segment) : Paragraph(segment.Text));
        if (writing && text.Trim().Length == 0)
            column.Children.Add(Ui.Text(L.T("Pensando…"), 14.5, _ink.Tertiary));
        if (who is { Length: > 0 })
            column.Children.Add(new Border
            {
                Background = _ink.Pill,
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(10, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = Ui.Text(who, 12, _ink.Tertiary),
            });
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*") };
        row.Children.Add(new AppLogo { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) });
        Grid.SetColumn(column, 1);
        row.Children.Add(column);
        return row;
    }

    SelectableTextBlock Selectable(string? text = null) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = Ink.Sans,
        FontSize = 14.5,
        LineHeight = 23,
        Foreground = _ink.Text,
        SelectionBrush = Translucent(_ink.Accent, 0.35),
    };

    /// <summary>Texto corrido con el `código` en línea en letra fija y con fondo.</summary>
    SelectableTextBlock Paragraph(string text)
    {
        var inlines = new InlineCollection();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) inlines.Add(new LineBreak());
            foreach (var span in Conversations.Spans(lines[i]))
                inlines.Add(span.Code
                    ? new Run(span.Text) { FontFamily = Ink.Mono, FontSize = 13, Background = _ink.Pill }
                    : new Run(span.Text));
        }
        var block = Selectable();
        block.Inlines = inlines;
        return block;
    }

    /// <summary>Un bloque de código: el lenguaje y «Copiar» arriba; el código en letra fija, con los colores del tema.</summary>
    Control CodeBlock(ChatSegment segment)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 38, Margin = new Thickness(16, 0, 6, 0) };
        header.Children.Add(Ui.Text(LanguageName(segment.Language), 12.5, _ink.Tertiary));
        var label = Ui.Text(L.T("Copiar"), 12.5, _ink.Secondary);
        var copy = Ui.Press(label, () =>
        {
            _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(segment.Text);
            label.Text = L.T("Copiado");
            DispatcherTimer.RunOnce(() => label.Text = L.T("Copiar"), TimeSpan.FromMilliseconds(1500));
        }, (b, on) => b.Background = on ? _ink.Pill : Brushes.Transparent, L.T("Copiar"));
        copy.Height = 28;
        copy.Padding = new Thickness(10, 0);
        copy.CornerRadius = new CornerRadius(999);
        copy.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(copy, 1);
        header.Children.Add(copy);

        var code = new SelectableTextBlock
        {
            FontFamily = Ink.Mono,
            FontSize = 13,
            LineHeight = 21.5,
            Foreground = _ink.Text,
            TextWrapping = TextWrapping.NoWrap,
            SelectionBrush = Translucent(_ink.Accent, 0.35),
        };
        var runs = new InlineCollection();
        foreach (var (text, kind) in CodeColors.Tokens(segment.Text))
            runs.Add(new Run(text)
            {
                Foreground = kind switch
                {
                    CodeColors.Kind.Keyword => _ink.Keyword,
                    CodeColors.Kind.String => _ink.String,
                    CodeColors.Kind.Comment => _ink.Tertiary,
                    _ => _ink.Text,
                },
            });
        code.Inlines = runs;
        // Una línea larga se recorre de lado; la rueda sigue moviendo la conversación.
        var scroller = new ScrollViewer
        {
            Content = code,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(16, 4, 16, 16),
        };
        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(scroller);
        return new Border
        {
            Background = _ink.Window,
            CornerRadius = new CornerRadius(14),
            BorderBrush = _ink.Hairline,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = stack,
        };
    }

    static string LanguageName(string language) => language.ToLowerInvariant() switch
    {
        "" => L.T("Código"),
        "cs" or "csharp" or "c#" => "C#",
        "js" or "javascript" => "JavaScript",
        "ts" or "typescript" => "TypeScript",
        "py" or "python" => "Python",
        "json" => "JSON",
        "sh" or "bash" or "shell" => "Bash",
        "ps1" or "powershell" or "pwsh" => "PowerShell",
        "sql" => "SQL",
        "html" => "HTML",
        "css" => "CSS",
        "xml" or "xaml" => language.ToUpperInvariant(),
        "yaml" or "yml" => "YAML",
        _ => language,
    };

    /// <summary>Una conversación sin mensajes: qué es (la rápida lo dice la bandeja) y qué se puede hacer.</summary>
    Control Empty(string? about)
    {
        var panel = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 96, 0, 0), MaxWidth = 420 };
        panel.Children.Add(new AppLogo { Width = 44, Height = 44, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock
        {
            Text = L.T("¿En qué le ayudo?"),
            FontFamily = Ink.Display,
            FontSize = 21,
            FontWeight = FontWeight.SemiBold,
            Foreground = _ink.Text,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = about ?? L.T("Esta conversación se guarda en este equipo y queda en la lista de la izquierda."),
            FontFamily = Ink.Sans,
            FontSize = 13.5,
            Foreground = _ink.Tertiary,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
        });
        return panel;
    }

    // ── Cuadro para escribir ────────────────────────────────────────────────────────────────

    Control Composer()
    {
        _input.AcceptsReturn = true;
        _input.TextWrapping = TextWrapping.Wrap;
        _input.MinHeight = 46;
        _input.MaxHeight = 200;
        _input.Padding = new Thickness(0);
        _input.FontFamily = Ink.Sans;
        _input.FontSize = 14.5;
        _input.Foreground = _ink.Text;
        _input.CaretBrush = _ink.Text;
        _input.SelectionBrush = Translucent(_ink.Accent, 0.35);
        _input.Watermark = L.T("Escríbale a Claupet");
        Ui.AutomationName(_input, L.T("Mensaje para Claupet"));
        // Sin el marco ni los fondos de Fluent: el cuadro es la tarjeta de abajo.
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
                     "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
            _input.Resources[key] = Brushes.Transparent;
        _input.Resources["TextControlBorderThemeThickness"] = new Thickness(0);
        _input.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        foreach (var key in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" })
            _input.Resources[key] = _ink.Text;
        foreach (var key in new[] { "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused" })
            _input.Resources[key] = _ink.Tertiary;

        var tools = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto") };
        var attach = Round(AttachIcon, L.T("Adjuntar un archivo"), () => { }, enabled: false);
        tools.Children.Add(attach);
        var mic = Round(MicIcon, L.T("Hablar"), () => { }, enabled: false);
        Grid.SetColumn(mic, 1);
        tools.Children.Add(mic);
        var hint = Ui.Text(L.T("Intro envía · Mayús+Intro, otra línea"), 12, _ink.Tertiary);
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        hint.Margin = new Thickness(0, 0, 10, 0);
        Grid.SetColumn(hint, 2);
        tools.Children.Add(hint);
        _send = Ui.Press(Icon(SendIcon, Brushes.Black, 16, 2.2), Submit, null, L.T("Enviar"));
        _send.Width = 36;
        _send.Height = 36;
        _send.CornerRadius = new CornerRadius(999);
        _send.Background = _ink.Accent;
        Grid.SetColumn(_send, 3);
        tools.Children.Add(_send);

        var card = new StackPanel { Spacing = 6 };
        card.Children.Add(_input);
        card.Children.Add(tools);
        return new Border
        {
            Margin = new Thickness(24, 4, 24, 20),
            MaxWidth = Column,
            CornerRadius = new CornerRadius(22),
            Background = _ink.Raised,
            BorderBrush = _ink.Hairline,
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse(_ink.Dark ? "0 10 30 0 #4D000000" : "0 10 30 0 #24000000"),
            Padding = new Thickness(18, 12, 10, 8),
            Child = card,
        };
    }

    /// <summary>Mientras responde, el botón de enviar pasa a detener.</summary>
    void PaintSend(bool asking)
    {
        _send.Child = asking ? Icon(StopIcon, Brushes.Black, 16, 0, fill: true) : Icon(SendIcon, Brushes.Black, 16, 2.2);
        ToolTip.SetTip(_send, asking ? L.T("Detener") : L.T("Enviar"));
        Ui.AutomationName(_send, asking ? L.T("Detener") : L.T("Enviar"));
    }

    void OnInputKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        e.Handled = true;
        Submit();
    }

    void Submit()
    {
        if (_chat is not { } chat) return;
        if (chat.Asking)
        {
            Ask(ConsoleLink.ChatStop);
            return;
        }
        var text = _input.Text?.Trim() ?? "";
        if (text.Length == 0) return;
        _toEnd = true;
        Request?.Invoke(new ConsoleMessage { Type = ConsoleLink.ChatSend, Key = chat.Id, Value = text });
        _input.Text = "";
    }

    void Ask(string type)
    {
        if (_chat is { } chat) Request?.Invoke(new ConsoleMessage { Type = type, Key = chat.Id });
    }

    void Act(string key) => Request?.Invoke(new ConsoleMessage { Type = ConsoleLink.Action, Key = key });

    // ── Piezas ──────────────────────────────────────────────────────────────────────────────

    static Viewbox Icon(string path, IBrush brush, double size, double thickness, bool fill = false) => new()
    {
        Width = size,
        Height = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(path),
            Stroke = fill ? null : brush,
            Fill = fill ? brush : null,
            StrokeThickness = thickness,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 24,
            Height = 24,
        },
    };

    /// <summary>Un botón redondo de 34 con un ícono; apagado, dice «próximamente».</summary>
    Border Round(string path, string name, Action onClick, bool fill = false, bool enabled = true)
    {
        var button = Ui.Press(Icon(path, _ink.Secondary, 17, fill ? 0 : 1.7, fill), enabled ? onClick : () => { },
            enabled ? (b, on) => b.Background = on || b.Tag is true ? _ink.Pill : Brushes.Transparent : null, name);
        button.Width = 34;
        button.Height = 34;
        button.CornerRadius = new CornerRadius(999);
        ToolTip.SetTip(button, enabled ? name : L.T("{0}: próximamente", name));
        if (!enabled)
        {
            button.Opacity = 0.45;
            button.Cursor = Cursor.Default;
            button.Focusable = false;
        }
        return button;
    }

    Border Pill(string text, IBrush background, IBrush foreground, Action onClick)
    {
        var pill = Ui.Press(Ui.Text(text, 13, foreground, FontWeight.SemiBold), onClick, null, text);
        pill.Background = background;
        pill.Height = 32;
        pill.Padding = new Thickness(14, 0);
        pill.CornerRadius = new CornerRadius(999);
        return pill;
    }

    static IBrush Translucent(IBrush brush, double opacity) =>
        brush is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, opacity) : brush;
}

/// <summary>Colores sencillos para el código de las respuestas: palabras clave, textos entre comillas y comentarios.</summary>
static class CodeColors
{
    public enum Kind { Plain, Keyword, String, Comment }

    /// <summary>Las palabras clave más comunes de C#, JavaScript, TypeScript y Python.</summary>
    static readonly HashSet<string> Keywords =
    [
        "using", "var", "new", "return", "if", "else", "for", "foreach", "in", "while", "do", "class", "public", "private",
        "protected", "internal", "static", "void", "string", "int", "long", "bool", "double", "true", "false", "null",
        "async", "await", "const", "let", "function", "def", "import", "from", "as", "try", "catch", "finally", "throw",
        "switch", "case", "break", "continue", "this", "self", "None", "True", "False", "elif", "lambda", "yield",
        "interface", "struct", "enum", "namespace", "readonly", "sealed", "record", "export", "default", "typeof", "is",
        "not", "and", "or", "with", "pass", "raise", "except", "echo", "fi", "then", "SELECT", "FROM", "WHERE", "JOIN",
    ];

    /// <summary>Corta el código en trozos con su clase. Las comillas sin cerrar llegan hasta el final de la línea.</summary>
    public static List<(string Text, Kind Kind)> Tokens(string code)
    {
        var tokens = new List<(string, Kind)>();
        var plain = new System.Text.StringBuilder();
        void Flush()
        {
            if (plain.Length == 0) return;
            tokens.Add((plain.ToString(), Kind.Plain));
            plain.Clear();
        }
        var i = 0;
        while (i < code.Length)
        {
            var c = code[i];
            if (c == '/' && i + 1 < code.Length && code[i + 1] == '/' || c == '#' && (i == 0 || code[i - 1] is '\n' or ' ' or '\t'))
            {
                Flush();
                var end = code.IndexOf('\n', i);
                if (end < 0) end = code.Length;
                tokens.Add((code[i..end], Kind.Comment));
                i = end;
            }
            else if (c is '"' or '\'')
            {
                Flush();
                var end = i + 1;
                while (end < code.Length && code[end] != c && code[end] != '\n')
                    end += code[end] == '\\' && end + 1 < code.Length && code[end + 1] != '\n' ? 2 : 1;
                if (end < code.Length && code[end] == c) end++;
                tokens.Add((code[i..end], Kind.String));
                i = end;
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var end = i;
                while (end < code.Length && (char.IsLetterOrDigit(code[end]) || code[end] == '_')) end++;
                var word = code[i..end];
                if (Keywords.Contains(word))
                {
                    Flush();
                    tokens.Add((word, Kind.Keyword));
                }
                else plain.Append(word);
                i = end;
            }
            else
            {
                plain.Append(c);
                i++;
            }
        }
        Flush();
        return tokens;
    }
}
