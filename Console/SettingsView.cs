namespace Claupet;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

/// <summary>
/// Una sección de Ajustes como la manda la bandeja: el título, y las filas en tarjetas separadas por sus
/// encabezados. Si llega el mismo armado con otros valores (un avance de descarga, un interruptor), se
/// actualiza en su lugar: no se pierde el desplazamiento, la lista abierta ni lo que se está escribiendo. Una página
/// dentro de una sección (el detalle de una extensión) lleva arriba el enlace para volver.
/// </summary>
sealed class SettingsView : StackPanel
{
    /// <summary>Se tocó una fila: la acción que hay que pedirle a la bandeja y, si la hay, lo escrito o el archivo elegido.</summary>
    public event Action<string, string?>? Action;

    /// <summary>Se pidió ir a otra página (una fila que lleva a otra, o volver): su id.</summary>
    public event Action<string>? Navigate;

    readonly List<RowView> _rows = [];
    /// <summary>Las tarjetas de los temas (Apariencia), si la página las tiene, y su fila.</summary>
    ThemeGallery? _gallery;
    int _galleryIndex;
    readonly TextBlock _title = new() { FontFamily = Ink.Display, FontSize = 26, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _description = new() { FontSize = 13.5, FontFamily = Ink.Sans, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
    string _shape = "";
    string _page = "";

    public SettingsView() => Spacing = 0;

    /// <summary>Muestra la página; back es el título de la sección a la que vuelve (solo en las páginas de adentro).</summary>
    public void Show(SettingsPage page, Ink ink, bool rebuild, string? back = null)
    {
        var rows = page.Rows.ToList();
        var shape = string.Join("|", rows.Select(r => $"{r.Kind}:{r.Key}:{r.Tone}:{r.Text.Length > 0}"));
        if (!rebuild && page.Id == _page && shape == _shape && _rows.Count > 0)
        {
            // Mismo armado: solo cambian los textos y los interruptores.
            for (var i = 0; i < _rows.Count; i++) _rows[i].Update(_rows[i].Index < rows.Count ? rows[_rows[i].Index] : _rows[i].Row, ink);
            _gallery?.Show(rows[_galleryIndex].Cards ?? [], ink);
            _title.Text = page.Title;
            _description.Text = page.Description;
            _description.IsVisible = page.Description.Length > 0;
            return;
        }
        _page = page.Id;
        _shape = shape;
        _rows.Clear();
        _gallery = null;
        Children.Clear();
        if (page.Parent is { } parent && back is not null) Children.Add(BackLink(parent, back, ink));
        _title.Text = page.Title;
        _title.Foreground = ink.Text;
        _description.Text = page.Description;
        _description.Foreground = ink.Tertiary;
        _description.IsVisible = page.Description.Length > 0;
        Children.Add(_title);
        Children.Add(_description);
        var head = Children.Count;
        var actions = new RowActions(Act, Send, id => Navigate?.Invoke(id));

        StackPanel? card = null;
        RowView? last = null;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Kind == RowKind.Caption)
            {
                Children.Add(new TextBlock
                {
                    Text = row.Text,
                    FontSize = 12.5,
                    FontWeight = FontWeight.Medium,
                    FontFamily = Ink.Sans,
                    Foreground = ink.Tertiary,
                    Margin = new Thickness(4, 26, 0, 8),
                });
                card = null;
                last = null;
                continue;
            }
            if (row.Kind == RowKind.Themes)
            {
                // Las tarjetas de los temas van sueltas, no dentro de una tarjeta de filas.
                _gallery = new ThemeGallery(Act);
                _galleryIndex = i;
                _gallery.Show(row.Cards ?? [], ink);
                Children.Add(_gallery);
                card = null;
                last = null;
                continue;
            }
            if (card is null)
            {
                card = new StackPanel();
                Children.Add(new Border
                {
                    Child = card,
                    CornerRadius = new CornerRadius(16),
                    Background = ink.Raised,
                    BorderBrush = ink.Hairline,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(16, 4),
                    Margin = new Thickness(0, Children.Count == head ? 24 : 0, 0, 0),
                });
            }
            // Una aclaración apagada bajo una fila con etiqueta va debajo de esa etiqueta, como en el diseño.
            if (row.Kind == RowKind.Note && row.Tone == RowTone.Muted
                && last is { Row.Kind: RowKind.Choice or RowKind.Info or RowKind.Link or RowKind.Switch or RowKind.Page or RowKind.Input })
            {
                last.AddHint(i, row, ink);
                _rows.Add(last.Hints[^1]);
                continue;
            }
            var view = new RowView(i, row, ink, actions, divider: card.Children.Count > 0);
            card.Children.Add(view.Root);
            _rows.Add(view);
            last = view;
        }
    }

    /// <summary>Lo escrito en un cuadro se manda tal cual: no espera a que se toque otra cosa.</summary>
    void Send(string key, string? value) => Action?.Invoke(key, value);

    /// <summary>Antes de otra acción («Crear», «Instalar»…), lo que quedó escrito en los cuadros llega primero.</summary>
    void Act(string key, string? value)
    {
        foreach (var row in _rows) row.Commit();
        Send(key, value);
    }

    Control BackLink(string parent, string title, Ink ink)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        content.Children.Add(RowView.Arrow(ink.Secondary, left: true));
        content.Children.Add(Ui.Text(title, 13.5, ink.Secondary, FontWeight.Medium));
        var back = Ui.Press(content, () => Navigate?.Invoke(parent), (b, on) => b.Background = on ? ink.Pill : Brushes.Transparent, L.T("Volver a {0}", title));
        back.CornerRadius = new CornerRadius(999);
        back.Height = 30;
        back.Padding = new Thickness(8, 0, 12, 0);
        back.Margin = new Thickness(-8, 0, 0, 10);
        back.HorizontalAlignment = HorizontalAlignment.Left;
        return back;
    }

    /// <summary>La fila que pidió una lista, para mostrarla a su lado (su botón, si tiene); null si no está aquí.</summary>
    public Control? AnchorFor(string? key) =>
        _rows.FirstOrDefault(r => r.Row.Key == key && r.Pill is not null)?.Pill ?? _rows.FirstOrDefault(r => r.Row.Key == key)?.Root;
}

/// <summary>Lo que puede hacer una fila: pedir una acción (Act), mandar lo escrito (Send) o ir a otra página.</summary>
sealed record RowActions(Action<string, string?> Act, Action<string, string?> Send, Action<string> Navigate);

/// <summary>Una fila: su etiqueta (con aclaraciones debajo) y, a la derecha, lo que corresponde a su tipo.</summary>
sealed class RowView
{
    public int Index { get; }
    public SettingsRow Row { get; private set; }
    public Control Root { get; }
    public Border? Pill { get; }
    public List<RowView> Hints { get; } = [];

    readonly StackPanel _left = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8) };
    readonly TextBlock? _label;
    readonly TextBlock? _value;
    readonly Border? _track;
    readonly TranslateTransform? _knob;
    readonly StackPanel? _links;
    readonly TextBox? _box;
    readonly RowActions _actions;

    /// <summary>Una aclaración bajo la etiqueta de otra fila: solo un texto.</summary>
    RowView(int index, SettingsRow row, TextBlock label)
    {
        Index = index;
        Row = row;
        Root = label;
        _label = label;
        _actions = new RowActions((_, _) => { }, (_, _) => { }, _ => { });
    }

    public RowView(int index, SettingsRow row, Ink ink, RowActions actions, bool divider)
    {
        Index = index;
        Row = row;
        _actions = actions;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = row.Kind == RowKind.Note ? 0 : 52 };
        grid.Children.Add(_left);
        var border = new Border
        {
            Child = grid,
            BorderBrush = ink.Hairline,
            BorderThickness = new Thickness(0, divider ? 1 : 0, 0, 0),
        };
        Root = border;

        switch (row.Kind)
        {
            case RowKind.Note:
                _left.Margin = new Thickness(0, 10);
                _label = Small(row.Text, ink.For(row.Tone));
                _left.Children.Add(_label);
                return;
            case RowKind.Links:
                _links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(-10, 0, 0, 0) };
                _left.Children.Add(_links);
                FillLinks(row, ink);
                return;
        }

        _label = new TextBlock
        {
            Text = row.Text,
            FontSize = 14.5,
            FontFamily = Ink.Sans,
            Foreground = row.Kind == RowKind.Link ? ink.Secondary : ink.Text,
            TextWrapping = TextWrapping.Wrap,
        };
        _left.Children.Add(_label);
        Control? right = null;
        switch (row.Kind)
        {
            case RowKind.Switch:
                _knob = new TranslateTransform(row.On ? 18 : 0, 0)
                {
                    Transitions = [new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(140) }],
                };
                _track = new Border
                {
                    Width = 44,
                    Height = 26,
                    CornerRadius = new CornerRadius(13),
                    Background = row.On ? ink.Accent : ink.Track,
                    Child = new Ellipse
                    {
                        Width = 20,
                        Height = 20,
                        Fill = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(3, 0),
                        RenderTransform = _knob,
                    },
                };
                var toggle = Ui.Press(_track, () =>
                {
                    // Se mueve ya; el estado que manda la bandeja lo confirma (o lo devuelve, si no se pudo).
                    SetSwitch(!Row.On, ink);
                    _actions.Act(Row.Key, null);
                }, name: row.Text);
                toggle.CornerRadius = new CornerRadius(13);
                right = toggle;
                break;
            case RowKind.Choice:
                _value = Ui.Text(row.Value, 13.5, ink.Text);
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
                // El punto es el de la paleta, aunque mande un tema propio.
                var swatch = Theme.PaletteAccent(ink.Dark);
                if (row.Swatch)
                    content.Children.Add(new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(Color.FromRgb(swatch.R, swatch.G, swatch.B)), VerticalAlignment = VerticalAlignment.Center });
                content.Children.Add(_value);
                content.Children.Add(Ui.Chevron(ink.Secondary));
                Pill = Ui.Press(content, () => _actions.Act(Row.Key, null), (b, on) => b.Opacity = on ? 0.85 : 1, name: row.Text);
                Pill.Background = ink.Pill;
                Pill.CornerRadius = new CornerRadius(999);
                Pill.Height = 32;
                Pill.Padding = new Thickness(12, 0);
                Pill.MaxWidth = 300;
                right = Pill;
                break;
            case RowKind.Info:
                _value = Ui.Text(row.Value, 13.5, ink.Secondary);
                _value.TextTrimming = TextTrimming.CharacterEllipsis;
                _value.MaxWidth = 320;
                right = _value;
                break;
            case RowKind.Link:
                _value = Ui.Text(row.Value, 14, ink.For(row.Tone), FontWeight.SemiBold);
                var link = Ui.Press(_value, OnLink, (b, on) => b.Background = on ? ink.Pill : Brushes.Transparent, name: row.Value);
                link.CornerRadius = new CornerRadius(999);
                link.Height = 32;
                link.Padding = new Thickness(12, 0);
                link.IsVisible = row.Value.Length > 0;
                right = link;
                break;
            case RowKind.Input:
                _box = Box(row, ink);
                var field = new Border
                {
                    Child = _box,
                    Background = ink.Pill,
                    CornerRadius = new CornerRadius(10),
                    Height = 34,
                    Width = row.Submit is null ? 260 : 300,
                    Padding = new Thickness(12, 0),
                };
                right = field;
                if (row.Submit is { } submit)
                {
                    // El botón del acento manda lo escrito; con el cuadro vacío no hace nada.
                    var send = Ui.Press(Ui.Text(submit, 13.5, Ink.On(ink.Accent), FontWeight.SemiBold), () => Commit(force: true),
                        (b, on) => b.Opacity = on ? 0.88 : 1, submit);
                    send.Background = ink.Accent;
                    send.CornerRadius = new CornerRadius(999);
                    send.Height = 34;
                    send.Padding = new Thickness(16, 0);
                    var both = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    both.Children.Add(field);
                    both.Children.Add(send);
                    right = both;
                }
                break;
            case RowKind.Page:
                // Toda la fila lleva a la página: a la derecha, su dato y la flecha.
                _value = Ui.Text(row.Value, 13.5, ink.Secondary);
                var go = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                go.Children.Add(_value);
                go.Children.Add(Arrow(ink.Tertiary, left: false));
                right = go;
                var label = _label;
                border.Child = null;
                border.Child = Ui.Press(grid, () => _actions.Navigate(Row.Key), (_, on) => label.Foreground = on ? ink.Accent : ink.Text, row.Text);
                break;
        }
        if (right is null) return;
        right.Margin = new Thickness(12, 0, 0, 0);
        right.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
    }

    static TextBlock Small(string text, IBrush brush) => new()
    {
        Text = text,
        FontSize = 12.5,
        FontFamily = Ink.Sans,
        Foreground = brush,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 0),
    };

    /// <summary>La flecha de las filas que llevan a otra página (o la de volver, hacia la izquierda).</summary>
    public static Avalonia.Controls.Shapes.Path Arrow(IBrush brush, bool left) => new()
    {
        Data = Geometry.Parse(left ? "M8.1,3.25 L4.9,6.5 L8.1,9.75" : "M4.9,3.25 L8.1,6.5 L4.9,9.75"),
        Stroke = brush,
        StrokeThickness = 1.6,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
        Width = 13,
        Height = 13,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>El cuadro para escribir, sin el marco de Fluent: el fondo es el de la píldora que lo rodea.</summary>
    TextBox Box(SettingsRow row, Ink ink)
    {
        var box = new TextBox
        {
            Text = row.Secret ? "" : row.Value,
            Watermark = row.Placeholder,
            PasswordChar = row.Secret ? '•' : default,
            FontFamily = Ink.Sans,
            FontSize = 13.5,
            Foreground = ink.Text,
            CaretBrush = ink.Text,
            Padding = new Thickness(0),
            MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            AcceptsReturn = false,
        };
        Ui.AutomationName(box, row.Text);
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
                     "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
            box.Resources[key] = Brushes.Transparent;
        box.Resources["TextControlBorderThemeThickness"] = new Thickness(0);
        box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        foreach (var key in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" })
            box.Resources[key] = ink.Text;
        foreach (var key in new[] { "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused" })
            box.Resources[key] = ink.Tertiary;
        // Intro guarda; Esc vuelve a lo que había. Al salir del cuadro también se guarda, salvo si tiene su botón.
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Commit(force: true);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                box.Text = Row.Secret || Row.Submit is not null ? "" : Row.Value;
            }
        };
        box.LostFocus += (_, _) => Commit();
        return box;
    }

    /// <summary>
    /// Si en el cuadro hay algo nuevo, se manda (una clave, solo si se escribió algo). Un cuadro con su botón solo se
    /// manda con él o con Intro (force), y se vacía: lo escrito es un pedido, no un valor que se guarda.
    /// </summary>
    public void Commit(bool force = false)
    {
        if (_box is null) return;
        var text = _box.Text ?? "";
        if (Row.Submit is not null)
        {
            if (!force || text.Trim().Length == 0) return;
            _box.Text = "";
            _actions.Send(Row.Key, text);
            return;
        }
        if (Row.Secret ? text.Length == 0 : text == Row.Value) return;
        if (Row.Secret) _box.Text = "";
        else Row = Row with { Value = text };
        _actions.Send(Row.Key, text);
    }

    void OnLink()
    {
        if (Row.Copy is { } copy) _ = CopyAsync(copy);
        else if (Row.File is { } pattern) _ = PickFileAsync(pattern);
        else _actions.Act(Row.Key, null);
    }

    /// <summary>Lo copia la Console (el token recién creado): un momento dice «Copiado».</summary>
    async Task CopyAsync(string text)
    {
        if (TopLevel.GetTopLevel(Root)?.Clipboard is not { } clipboard) return;
        await clipboard.SetTextAsync(text);
        if (_value is null) return;
        _value.Text = L.T("Copiado");
        await Task.Delay(1500);
        _value.Text = Row.Value;
    }

    /// <summary>Primero el archivo (en la Console, con el selector del sistema); la acción lleva su ruta.</summary>
    async Task PickFileAsync(string pattern)
    {
        if (TopLevel.GetTopLevel(Root)?.StorageProvider is not { CanOpen: true } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Row.Value.TrimEnd('…'),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Row.Text) { Patterns = [pattern] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) _actions.Act(Row.Key, path);
    }

    public void AddHint(int index, SettingsRow row, Ink ink)
    {
        var text = Small(row.Text, ink.For(row.Tone));
        _left.Children.Add(text);
        Hints.Add(new RowView(index, row, text));
    }

    void SetSwitch(bool on, Ink ink)
    {
        Row = Row with { On = on };
        if (_knob is not null) _knob.X = on ? 18 : 0;
        if (_track is not null) _track.Background = on ? ink.Accent : ink.Track;
    }

    void FillLinks(SettingsRow row, Ink ink)
    {
        _links!.Children.Clear();
        foreach (var link in row.Links ?? [])
        {
            var key = link.Key;
            var press = Ui.Press(Ui.Text(link.Text, 13.5, ink.Accent, FontWeight.SemiBold), () => _actions.Act(key, null),
                (b, on) => b.Background = on ? ink.Pill : Brushes.Transparent, name: link.Text);
            press.CornerRadius = new CornerRadius(999);
            press.Height = 32;
            press.Padding = new Thickness(10, 0);
            _links.Children.Add(press);
        }
    }

    public void Update(SettingsRow row, Ink ink)
    {
        var before = Row;
        Row = row;
        if (_label is not null) _label.Text = row.Text;
        if (_value is not null)
        {
            _value.Text = row.Value;
            if (_value.Parent is Border link && row.Kind == RowKind.Link) link.IsVisible = row.Value.Length > 0;
        }
        if (_box is not null)
        {
            _box.Watermark = row.Placeholder;
            // Lo que se está escribiendo no se pisa: solo cambia si llegó otro valor y el cuadro no tiene el foco.
            if (!row.Secret && !_box.IsFocused && row.Value != before.Value) _box.Text = row.Value;
        }
        if (row.Kind == RowKind.Switch) SetSwitch(row.On, ink);
        if (row.Kind == RowKind.Links) FillLinks(row, ink);
    }
}
