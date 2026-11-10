namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

/// <summary>Buscar y reemplazar (flota arriba a la derecha) e ir a una línea (arriba, al centro).</summary>
sealed partial class EditorPane
{
    const string UpIcon = "M6,15 L12,9 L18,15";
    const string DownIcon = "M6,9 L12,15 L18,9";
    const string ReplaceIcon = "M4,9 H16 L13,6 M20,15 H8 L11,18";
    const string ReplaceOneIcon = "M5,12 H15 M11,8 L15,12 L11,16 M19,6 V18";
    const string ReplaceAllIcon = "M4,8 H14 M10,4.5 L14,8 L10,11.5 M4,16 H14 M10,12.5 L14,16 L10,19.5 M19,4 V20";

    Border _findBar = new() { IsVisible = false };
    Border _goToBar = new() { IsVisible = false };
    TextBox _findBox = new(), _replaceBox = new(), _goToBox = new();
    Control _replaceRow = new Border();
    TextBlock _count = new();
    Border _caseToggle = new(), _wordToggle = new(), _regexToggle = new();
    bool _matchCase, _wholeWord, _regex;
    /// <summary>Hay que volver a buscar (el texto cambió): se hace una vez, después de la tecla.</summary>
    bool _searchPending;

    EditorQuery Query => new(_findBox.Text ?? "", _matchCase, _wholeWord, _regex);

    /// <summary>Arma las dos barras (con los colores de ahora); se agregan sobre el texto.</summary>
    void BuildBars()
    {
        var wasFind = _findBar.IsVisible;
        var wasReplace = _replaceRow.IsVisible;
        var wasGoTo = _goToBar.IsVisible;
        var query = _findBox.Text;
        var replace = _replaceBox.Text;
        _stage.Children.Remove(_findBar);
        _stage.Children.Remove(_goToBar);

        var (findPill, findBox) = Field(L.T("Buscar"), 220);
        _findBox = findBox;
        _findBox.Text = query;
        _findBox.TextChanged += (_, _) => Search();
        _findBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            FindNext(!e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        };
        _caseToggle = Toggle("Aa", L.T("Mayúsculas y minúsculas"), () => _matchCase = !_matchCase, () => _matchCase);
        _wordToggle = Toggle("ab", L.T("Palabra completa"), () => _wholeWord = !_wholeWord, () => _wholeWord, underline: true);
        _regexToggle = Toggle(".*", L.T("Expresión regular"), () => _regex = !_regex, () => _regex);
        _count = Ui.Text("", 12, _ink.Tertiary);
        _count.MinWidth = 70;
        _count.TextAlignment = TextAlignment.Center;
        var first = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        findPill.Margin = new Thickness(0, 0, 4, 0);
        first.Children.Add(findPill);
        first.Children.Add(_caseToggle);
        first.Children.Add(_wordToggle);
        first.Children.Add(_regexToggle);
        first.Children.Add(_count);
        first.Children.Add(Small(UpIcon, L.T("Anterior · Mayús+F3"), () => FindNext(false)));
        first.Children.Add(Small(DownIcon, L.T("Siguiente · F3"), () => FindNext(true)));
        first.Children.Add(Small(ReplaceIcon, L.T("Reemplazar · Ctrl+H"), () =>
        {
            _replaceRow.IsVisible = !_replaceRow.IsVisible;
            if (_replaceRow.IsVisible) _replaceBox.Focus();
        }));
        first.Children.Add(Small(CloseIcon, L.T("Cerrar · Esc"), HideBars));

        var (replacePill, replaceBox) = Field(L.T("Reemplazar por"), 220);
        _replaceBox = replaceBox;
        _replaceBox.Text = replace;
        _replaceBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) ReplaceAll();
            else ReplaceOne();
            e.Handled = true;
        };
        var second = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, IsVisible = wasReplace };
        replacePill.Margin = new Thickness(0, 0, 4, 0);
        second.Children.Add(replacePill);
        second.Children.Add(Small(ReplaceOneIcon, L.T("Reemplazar · Enter"), ReplaceOne));
        second.Children.Add(Small(ReplaceAllIcon, L.T("Reemplazar todo · Ctrl+Enter"), ReplaceAll));
        _replaceRow = second;

        var rows = new StackPanel { Spacing = 6 };
        rows.Children.Add(first);
        rows.Children.Add(second);
        _findBar = Card(rows);
        _findBar.HorizontalAlignment = HorizontalAlignment.Right;
        _findBar.VerticalAlignment = VerticalAlignment.Top;
        _findBar.Margin = new Thickness(0, 10, 22, 0);
        _findBar.IsVisible = wasFind;
        _stage.Children.Add(_findBar);

        var (goToPill, goToBox) = Field("", 170);
        _goToBox = goToBox;
        _goToBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            GoTo(_goToBox.Text ?? "");
            e.Handled = true;
        };
        var goTo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        goTo.Children.Add(goToPill);
        goTo.Children.Add(Small(CloseIcon, L.T("Cerrar · Esc"), HideBars));
        _goToBar = Card(goTo, 999);
        _goToBar.HorizontalAlignment = HorizontalAlignment.Center;
        _goToBar.VerticalAlignment = VerticalAlignment.Top;
        _goToBar.Margin = new Thickness(0, 10, 0, 0);
        _goToBar.IsVisible = wasGoTo;
        _stage.Children.Add(_goToBar);
    }

    /// <summary>Una opción de buscar (Aa, ab, .*): se enciende con el color de Claupet.</summary>
    Border Toggle(string text, string name, Action flip, Func<bool> on, bool underline = false)
    {
        var label = Ui.Text(text, 12, _ink.Tertiary, FontWeight.SemiBold, Ink.Mono);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        if (underline) label.TextDecorations = TextDecorations.Underline;
        Border? toggle = null;
        void Paint(bool hover)
        {
            label.Foreground = on() ? _ink.Accent : _ink.Tertiary;
            toggle!.Background = on() ? new SolidColorBrush(((ISolidColorBrush)_ink.Accent).Color, 0.16) : hover ? _ink.Pill : Brushes.Transparent;
        }
        toggle = Ui.Press(label, () =>
        {
            flip();
            Paint(true);
            Search();
        }, (_, hover) => Paint(hover), name);
        toggle.Width = 28;
        toggle.Height = 28;
        toggle.CornerRadius = new CornerRadius(8);
        ToolTip.SetTip(toggle, name);
        Paint(false);
        return toggle;
    }

    void ShowFind(bool replace)
    {
        if (_current is null) return;
        _goToBar.IsVisible = false;
        // Lo seleccionado (si es una sola línea) es lo que se busca.
        var selected = _editor.SelectedText;
        if (!string.IsNullOrEmpty(selected) && !selected.Contains('\n')) _findBox.Text = selected;
        _findBar.IsVisible = true;
        if (replace) _replaceRow.IsVisible = true;
        Search();
        Dispatcher.UIThread.Post(() =>
        {
            _findBox.Focus();
            _findBox.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    void ShowGoTo()
    {
        if (_current is not { } doc) return;
        _findBar.IsVisible = false;
        ClearMatches();
        _goToBox.Watermark = L.T("Ir a la línea (1–{0})", doc.Document.LineCount);
        _goToBox.Text = "";
        _goToBar.IsVisible = true;
        Dispatcher.UIThread.Post(() => _goToBox.Focus(), DispatcherPriority.Loaded);
    }

    void HideBars()
    {
        var had = _findBar.IsVisible || _goToBar.IsVisible;
        _findBar.IsVisible = false;
        _goToBar.IsVisible = false;
        ClearMatches();
        if (had) FocusText();
    }

    void ClearMatches()
    {
        _marks.Matches = [];
        _marks.Current = -1;
        _editor.TextArea.TextView.InvalidateLayer(_marks.Layer);
    }

    /// <summary>«12» o «12:5» (línea y columna).</summary>
    void GoTo(string text)
    {
        if (_current is not { } doc) return;
        var parts = text.Split([':', ',', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0], out var line)) return;
        line = Math.Clamp(line, 1, doc.Document.LineCount);
        var column = parts.Length > 1 && int.TryParse(parts[1], out var c) ? Math.Max(1, c) : 1;
        var target = doc.Document.GetLineByNumber(line);
        _editor.CaretOffset = target.Offset + Math.Min(column - 1, target.Length);
        _editor.Select(_editor.CaretOffset, 0);
        HideBars();
        _editor.ScrollTo(line, column);
        _editor.TextArea.Caret.BringCaretToView();
    }

    /// <summary>Busca de nuevo cuando el texto cambió (una vez, después de que se termine de escribir la tecla).</summary>
    void SearchLater()
    {
        if (_searchPending) return;
        _searchPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _searchPending = false;
            if (_findBar.IsVisible) Search(keepPlace: true);
        }, DispatcherPriority.Background);
    }

    /// <summary>Marca todo lo encontrado y elige el primero desde el cursor (o se queda donde estaba, si cambió el texto).</summary>
    void Search(bool keepPlace = false)
    {
        if (_current is not { } doc || !_findBar.IsVisible) return;
        var pattern = Query.Pattern();
        var found = pattern is null ? [] : EditorSearch.FindAll(doc.Document.Text, pattern);
        _marks.Matches = found;
        if (found.Count == 0) _marks.Current = -1;
        else if (!keepPlace || _marks.Current < 0 || _marks.Current >= found.Count)
        {
            var from = _editor.SelectionStart;
            var at = found.FindIndex(m => m.Start >= from);
            _marks.Current = at < 0 ? 0 : at;
            if (!keepPlace) Reveal(found[_marks.Current], select: false);
        }
        UpdateCount();
        _editor.TextArea.TextView.InvalidateLayer(_marks.Layer);
    }

    void UpdateCount()
    {
        var query = _findBox.Text ?? "";
        var found = _marks.Matches;
        _count.Text = query.Length == 0 ? ""
            : Query.Pattern() is null ? L.T("Expresión incompleta")
            : found.Count == 0 ? L.T("Sin resultados")
            : found.Count >= EditorSearch.Max ? L.T("{0}+", EditorSearch.Max)
            : L.T("{0} de {1}", _marks.Current + 1, found.Count);
        _count.Foreground = query.Length > 0 && found.Count == 0 ? _ink.Danger : _ink.Tertiary;
    }

    /// <summary>F3 y Mayús+F3: la siguiente o la anterior, desde el cursor; da la vuelta al llegar al final.</summary>
    void FindNext(bool forward)
    {
        if (_current is null) return;
        if (!_findBar.IsVisible)
        {
            // F3 sin la barra abierta: busca lo último que se buscó (o lo seleccionado).
            if (string.IsNullOrEmpty(_findBox.Text) && string.IsNullOrEmpty(_editor.SelectedText))
            {
                ShowFind(replace: false);
                return;
            }
            _findBar.IsVisible = true;
            if (!string.IsNullOrEmpty(_editor.SelectedText) && !_editor.SelectedText.Contains('\n')) _findBox.Text = _editor.SelectedText;
            Search(keepPlace: true);
        }
        var found = _marks.Matches;
        if (found.Count == 0) return;
        var caret = _editor.SelectionStart;
        int at;
        if (forward)
        {
            at = found.FindIndex(m => m.Start > caret || (m.Start == caret && _editor.SelectionLength == 0));
            if (at < 0) at = 0;
        }
        else
        {
            at = found.FindLastIndex(m => m.Start < caret);
            if (at < 0) at = found.Count - 1;
        }
        _marks.Current = at;
        Reveal(found[at], select: true);
        UpdateCount();
        _editor.TextArea.TextView.InvalidateLayer(_marks.Layer);
    }

    void Reveal((int Start, int Length) match, bool select)
    {
        if (select) _editor.Select(match.Start, match.Length);
        var location = _editor.Document.GetLocation(match.Start);
        _editor.ScrollTo(location.Line, location.Column);
    }

    /// <summary>Reemplaza el actual (si está seleccionado) y pasa al siguiente.</summary>
    void ReplaceOne()
    {
        if (_current is not { } doc || Query.Pattern() is not { } pattern) return;
        var found = _marks.Matches;
        if (_marks.Current < 0 || _marks.Current >= found.Count) return;
        var (start, length) = found[_marks.Current];
        if (_editor.SelectionStart != start || _editor.SelectionLength != length)
        {
            Reveal(found[_marks.Current], select: true);
            return;
        }
        var match = pattern.Match(doc.Document.Text, start);
        if (!match.Success || match.Index != start) return;
        var with = Query.Replacement(match, _replaceBox.Text ?? "");
        doc.Document.Replace(start, length, with);
        _editor.Select(start + with.Length, 0);
        Search(keepPlace: true);
        FindNext(true);
    }

    void ReplaceAll()
    {
        if (_current is not { } doc) return;
        var count = EditorSearch.ReplaceAll(doc.Document, Query, _replaceBox.Text ?? "");
        Search(keepPlace: true);
        Toast(count == 0 ? L.T("Nada que reemplazar") : count == 1 ? L.T("1 reemplazo · Ctrl+Z lo deshace") : L.T("{0} reemplazos · Ctrl+Z los deshace", count));
    }
}
