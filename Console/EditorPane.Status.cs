namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

/// <summary>
/// La barra de abajo: dónde está el cursor y lo seleccionado; la sangría, la codificación, el fin de línea, el lenguaje
/// y el zoom, cada uno se cambia con un clic. También el ajuste de líneas y mostrar los espacios.
/// </summary>
sealed partial class EditorPane
{
    Border _status = new();
    TextBlock _position = new(), _selection = new(), _indent = new(), _encoding = new(), _newLine = new(), _language = new(), _zoomLabel = new();
    Border _selectionItem = new();

    Control StatusBar()
    {
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        left.Children.Add(StatusItem(out _position, L.T("Ir a la línea · Ctrl+G"), _ => ShowGoTo()));
        _selectionItem = StatusItem(out _selection, null, null);
        left.Children.Add(_selectionItem);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(StatusItem(out _indent, L.T("Sangría"), PickIndent));
        right.Children.Add(StatusItem(out _encoding, L.T("Codificación con que se guarda"), PickEncoding));
        right.Children.Add(StatusItem(out _newLine, L.T("Fin de línea"), PickNewLine));
        right.Children.Add(StatusItem(out _language, L.T("Lenguaje"), PickLanguage));
        right.Children.Add(StatusItem(out _zoomLabel, L.T("Zoom · Ctrl+rueda; clic: 100 %"), _ => Zoom(0)));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        _status = new Border
        {
            Child = grid,
            Height = 30,
            Padding = new Thickness(10, 0),
            BorderBrush = _ink.Hairline,
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
        Grid.SetRow(_status, 2);
        UpdateStatus();
        return _status;
    }

    /// <summary>Un dato de la barra; con onClick, se puede tocar (y se marca al pasar el puntero).</summary>
    Border StatusItem(out TextBlock text, string? tip, Action<Control>? onClick)
    {
        text = Ui.Text("", 12, _ink.Tertiary);
        var label = text;
        Border? item = null;
        item = onClick is null
            ? new Border { Child = text }
            : Ui.Press(text, () => onClick(item!), (b, on) =>
            {
                b.Background = on ? _ink.Pill : Brushes.Transparent;
                label.Foreground = on ? _ink.Text : _ink.Tertiary;
            }, tip);
        item.Height = 22;
        item.Padding = new Thickness(8, 0);
        item.CornerRadius = new CornerRadius(6);
        item.VerticalAlignment = VerticalAlignment.Center;
        if (tip is not null) ToolTip.SetTip(item, tip);
        return item;
    }

    void UpdateStatus()
    {
        _status.IsVisible = _current is not null;
        if (_current is not { } doc) return;
        var caret = _editor.TextArea.Caret;
        _position.Text = L.T("Lín {0}, col {1}", caret.Line, caret.Column);
        var selection = _editor.TextArea.Selection;
        _selectionItem.IsVisible = !selection.IsEmpty;
        if (!selection.IsEmpty)
        {
            var length = selection.Length;
            var lines = Math.Abs(selection.EndPosition.Line - selection.StartPosition.Line) + 1;
            _selection.Text = lines > 1 ? L.T("{0} selec. en {1} líneas", length, lines) : L.T("{0} selec.", length);
        }
        _indent.Text = doc.Tabs ? L.T("Tabulaciones") : L.T("Espacios: {0}", doc.IndentSize);
        _encoding.Text = EditorFormat.EncodingLabel(doc.Encoding);
        _newLine.Text = EditorFormat.NewLineLabel(doc.NewLine);
        _language.Text = EditorLanguages.Label(doc.Language);
        _zoomLabel.Text = $"{_zoom} %";
    }

    void PickIndent(Control anchor)
    {
        if (_current is not { } doc) return;
        DropList.Show(anchor, _ink,
        [
            new("2", L.T("Espacios: {0}", 2), Selected: !doc.Tabs && doc.IndentSize == 2),
            new("4", L.T("Espacios: {0}", 4), Selected: !doc.Tabs && doc.IndentSize == 4),
            new("8", L.T("Espacios: {0}", 8), Selected: !doc.Tabs && doc.IndentSize == 8),
            new("tabs", L.T("Tabulaciones"), Selected: doc.Tabs),
        ], picked =>
        {
            if (picked is null) return;
            doc.Tabs = picked == "tabs";
            if (!doc.Tabs) doc.IndentSize = int.Parse(picked);
            Reshow();
        });
    }

    void PickEncoding(Control anchor)
    {
        if (_current is not { } doc) return;
        var choices = EditorFormat.Choices;
        var now = EditorFormat.EncodingLabel(doc.Encoding);
        DropList.Show(anchor, _ink,
            choices.Select((e, i) => new DropList.Entry(i.ToString(), EditorFormat.EncodingLabel(e), Selected: EditorFormat.EncodingLabel(e) == now)).ToList(),
            picked =>
            {
                if (picked is null || EditorFormat.EncodingLabel(choices[int.Parse(picked)]) == now) return;
                doc.Encoding = choices[int.Parse(picked)];
                doc.EncodingChanged = true;
                Refresh();
                Toast(L.T("Se guarda en {0}", EditorFormat.EncodingLabel(doc.Encoding)));
            });
    }

    void PickNewLine(Control anchor)
    {
        if (_current is not { } doc) return;
        DropList.Show(anchor, _ink,
        [
            new(EditorFormat.Crlf, L.T("CRLF (Windows)"), Selected: doc.NewLine == EditorFormat.Crlf),
            new(EditorFormat.Lf, L.T("LF (Linux y macOS)"), Selected: doc.NewLine == EditorFormat.Lf),
        ], picked =>
        {
            if (picked is null || picked == doc.NewLine) return;
            doc.NewLine = picked;
            EditorFormat.ConvertNewLines(doc.Document, picked);
            UpdateStatus();
        });
    }

    void PickLanguage(Control anchor)
    {
        if (_current is not { } doc) return;
        DropList.Show(anchor, _ink,
            EditorLanguages.All.Select(l => new DropList.Entry(l, EditorLanguages.Label(l), Selected: l == doc.Language)).ToList(),
            picked =>
            {
                if (picked is null || picked == doc.Language) return;
                doc.Language = picked;
                _sessionChanged = true;
                Reshow();
                Refresh();
            });
    }

    /// <summary>Más o menos zoom (de 10 en 10, entre 50 y 300); 0 vuelve a 100.</summary>
    void Zoom(int step)
    {
        var zoom = step == 0 ? 100 : Math.Clamp(_zoom + step, 50, 300);
        if (zoom == _zoom) return;
        _zoom = zoom;
        _sessionChanged = true;
        Reshow();
    }

    void ToggleWrap()
    {
        _wrap = !_wrap;
        _sessionChanged = true;
        if (_current is { Code: false }) Toast(L.T("El texto siempre ajusta las líneas; en código: {0}", _wrap ? L.T("sí") : L.T("no")));
        else Toast(_wrap ? L.T("Ajustar las líneas: sí") : L.T("Ajustar las líneas: no"));
        Reshow();
    }

    void ToggleSpaces()
    {
        _spaces = !_spaces;
        _sessionChanged = true;
        Reshow();
    }
}
