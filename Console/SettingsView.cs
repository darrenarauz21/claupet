namespace Claupet;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

/// <summary>
/// Una sección de Ajustes como la manda la bandeja: el título, y las filas en tarjetas separadas por sus
/// encabezados. Si llega el mismo armado con otros valores (un avance de descarga, un interruptor), se
/// actualiza en su lugar: no se pierde el desplazamiento ni la lista abierta.
/// </summary>
sealed class SettingsView : StackPanel
{
    /// <summary>Se tocó una fila: la acción que hay que pedirle a la bandeja.</summary>
    public event Action<string>? Action;

    readonly List<RowView> _rows = [];
    string _shape = "";
    string _page = "";

    public SettingsView() => Spacing = 0;

    public void Show(SettingsPage page, Ink ink, bool rebuild)
    {
        var rows = page.Rows.ToList();
        var shape = string.Join("|", rows.Select(r => $"{r.Kind}:{r.Key}:{r.Tone}:{r.Text.Length > 0}"));
        if (!rebuild && page.Id == _page && shape == _shape && _rows.Count > 0)
        {
            // Mismo armado: solo cambian los textos y los interruptores.
            for (var i = 0; i < _rows.Count; i++) _rows[i].Update(_rows[i].Index < rows.Count ? rows[_rows[i].Index] : _rows[i].Row, ink);
            ((TextBlock)Children[0]).Text = page.Title;
            ((TextBlock)Children[1]).Text = page.Description;
            return;
        }
        _page = page.Id;
        _shape = shape;
        _rows.Clear();
        Children.Clear();
        Children.Add(new TextBlock
        {
            Text = page.Title,
            FontFamily = Ink.Display,
            FontSize = 26,
            FontWeight = FontWeight.SemiBold,
            Foreground = ink.Text,
        });
        Children.Add(new TextBlock
        {
            Text = page.Description,
            FontSize = 13.5,
            FontFamily = Ink.Sans,
            Foreground = ink.Tertiary,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

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
                    Margin = new Thickness(0, Children.Count == 2 ? 24 : 0, 0, 0),
                });
            }
            // Una aclaración apagada bajo una lista, un dato o un enlace va debajo de su etiqueta, como en el diseño.
            if (row.Kind == RowKind.Note && row.Tone == RowTone.Muted && last is { Row.Kind: RowKind.Choice or RowKind.Info or RowKind.Link })
            {
                last.AddHint(i, row, ink);
                _rows.Add(last.Hints[^1]);
                continue;
            }
            var view = new RowView(i, row, ink, key => Action?.Invoke(key), divider: card.Children.Count > 0);
            card.Children.Add(view.Root);
            _rows.Add(view);
            last = view;
        }
    }

    /// <summary>La fila que pidió una lista, para mostrarla a su lado (su botón, si tiene); null si no está aquí.</summary>
    public Control? AnchorFor(string? key) =>
        _rows.FirstOrDefault(r => r.Row.Key == key && r.Pill is not null)?.Pill ?? _rows.FirstOrDefault(r => r.Row.Key == key)?.Root;
}

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
    readonly Action<string> _act;

    /// <summary>Una aclaración bajo la etiqueta de otra fila: solo un texto.</summary>
    RowView(int index, SettingsRow row, TextBlock label)
    {
        Index = index;
        Row = row;
        Root = label;
        _label = label;
        _act = _ => { };
    }

    public RowView(int index, SettingsRow row, Ink ink, Action<string> act, bool divider)
    {
        Index = index;
        Row = row;
        _act = act;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = row.Kind == RowKind.Note ? 0 : 52 };
        grid.Children.Add(_left);
        Root = new Border
        {
            Child = grid,
            BorderBrush = ink.Hairline,
            BorderThickness = new Thickness(0, divider ? 1 : 0, 0, 0),
        };

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
                    _act(Row.Key);
                }, name: row.Text);
                toggle.CornerRadius = new CornerRadius(13);
                right = toggle;
                break;
            case RowKind.Choice:
                _value = Ui.Text(row.Value, 13.5, ink.Text);
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
                if (row.Swatch) content.Children.Add(new Ellipse { Width = 12, Height = 12, Fill = ink.Accent, VerticalAlignment = VerticalAlignment.Center });
                content.Children.Add(_value);
                content.Children.Add(Ui.Chevron(ink.Secondary));
                Pill = Ui.Press(content, () => _act(Row.Key), (b, on) => b.Opacity = on ? 0.85 : 1, name: row.Text);
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
                var link = Ui.Press(_value, () => _act(Row.Key), (b, on) => b.Background = on ? ink.Pill : Brushes.Transparent, name: row.Value);
                link.CornerRadius = new CornerRadius(999);
                link.Height = 32;
                link.Padding = new Thickness(12, 0);
                link.IsVisible = row.Value.Length > 0;
                right = link;
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
            var press = Ui.Press(Ui.Text(link.Text, 13.5, ink.Accent, FontWeight.SemiBold), () => _act(key),
                (b, on) => b.Background = on ? ink.Pill : Brushes.Transparent, name: link.Text);
            press.CornerRadius = new CornerRadius(999);
            press.Height = 32;
            press.Padding = new Thickness(10, 0);
            _links.Children.Add(press);
        }
    }

    public void Update(SettingsRow row, Ink ink)
    {
        Row = row;
        if (_label is not null) _label.Text = row.Text;
        if (_value is not null)
        {
            _value.Text = row.Value;
            if (_value.Parent is Border link && row.Kind == RowKind.Link) link.IsVisible = row.Value.Length > 0;
        }
        if (row.Kind == RowKind.Switch) SetSwitch(row.On, ink);
        if (row.Kind == RowKind.Links) FillLinks(row, ink);
    }
}
