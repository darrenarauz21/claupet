namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

/// <summary>
/// Lo que se puede elegir en una búsqueda: su ícono (un trazo), su nombre, dónde está y qué hacer al elegirlo. Extra es
/// texto que también se busca pero no se ve (la descripción de una sección, la ruta de un archivo).
/// </summary>
sealed record SearchPick(string Icon, string Name, string Where, Action Go, string Extra = "");

/// <summary>
/// La búsqueda de la Console: Ctrl+K busca en Ajustes y Ctrl+Shift+K en toda la Console. Una tarjeta arriba, sobre un
/// velo, con el cuadro y los resultados, como la de Proyectos: flechas, Intro y Esc, y un clic afuera la cierra. Busca sin
/// mayúsculas ni tildes, y cada palabra escrita tiene que estar en el nombre, en dónde está o en lo que no se ve.
/// </summary>
sealed class SearchPalette : Border
{
    const int Max = 14;
    const string Lens = "M17,11 A6,6 0 1 1 5,11 A6,6 0 1 1 17,11 Z M15.5,15.5 L20,20";

    readonly TextBox _query = new() { MinHeight = 0, BorderThickness = new Thickness(0), Padding = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center };
    readonly StackPanel _results = new() { Spacing = 1, Margin = new Thickness(6) };
    readonly ScrollViewer _scroll = new() { MaxHeight = 380 };
    readonly Border _card = new();
    readonly List<SearchPick> _shown = [];
    IReadOnlyList<SearchPick> _all = [];
    Ink _ink = Ink.For(false);
    int _selected;
    /// <summary>Para no confundir la búsqueda de ahora con otra que se abrió después (lo que llega tarde, de otra búsqueda).</summary>
    int _opened;

    public SearchPalette()
    {
        IsVisible = false;
        ZIndex = 100;
        // La letra de la Console para todo lo de adentro, también el texto de ayuda del cuadro (no la del tema Fluent).
        SetValue(Avalonia.Controls.Documents.TextElement.FontFamilyProperty, Ink.Sans);
        _query.FontFamily = Ink.Sans;
        _scroll.Content = _results;
        _query.TextChanged += (_, _) =>
        {
            _selected = 0;
            Draw();
        };
        _query.AddHandler(KeyDownEvent, (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Down when _shown.Count > 0: _selected = (_selected + 1) % _shown.Count; Draw(); break;
                case Key.Up when _shown.Count > 0: _selected = (_selected - 1 + _shown.Count) % _shown.Count; Draw(); break;
                case Key.Enter when _shown.Count > 0: Choose(_shown[_selected]); break;
                case Key.Escape: Close(); break;
                default: return;
            }
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _card.PointerPressed += (_, e) => e.Handled = true;
        PointerPressed += (_, e) => { if (e.Source == this) Close(); };
    }

    public bool IsOpen => IsVisible;

    /// <summary>Para las pruebas: el cuadro y lo que se ve (en orden).</summary>
    internal TextBox Query => _query;
    internal IReadOnlyList<SearchPick> Shown => _shown;

    /// <summary>La abre con lo que se puede elegir; devuelve su número, para SetPicks.</summary>
    public int Open(Ink ink, string placeholder, IReadOnlyList<SearchPick> picks)
    {
        _ink = ink;
        _all = picks;
        _selected = 0;
        _opened++;
        Paint(placeholder);
        _query.Text = "";
        Draw();
        IsVisible = true;
        Dispatcher.UIThread.Post(() => _query.Focus(), DispatcherPriority.Background);
        return _opened;
    }

    /// <summary>Lo que se puede elegir, otra vez (lo que se buscó aparte llegó). Si ya se cerró o se abrió otra, nada.</summary>
    public void SetPicks(int opened, IReadOnlyList<SearchPick> picks)
    {
        if (opened != _opened || !IsVisible) return;
        _all = picks;
        Draw();
    }

    public void Close()
    {
        IsVisible = false;
        _query.Text = "";
    }

    void Choose(SearchPick pick)
    {
        Close();
        pick.Go();
    }

    /// <summary>Las palabras de query, cada una en el nombre, en dónde está o en lo que no se ve (sin tildes ni mayúsculas).</summary>
    internal static bool Matches(SearchPick pick, string query)
    {
        var words = ProjectsPane.Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return true;
        var text = ProjectsPane.Fold(pick.Name + " " + pick.Where + " " + pick.Extra);
        return words.All(w => text.Contains(w, StringComparison.Ordinal));
    }

    /// <summary>Los que coinciden: primero los que tienen la búsqueda en el nombre, después el resto, sin cambiar su orden.</summary>
    internal static List<SearchPick> Filter(IReadOnlyList<SearchPick> all, string query)
    {
        var text = query.Trim();
        var hits = all.Where(p => Matches(p, text)).ToList();
        if (text.Length == 0) return hits;
        var folded = ProjectsPane.Fold(text);
        return [.. hits.Where(p => ProjectsPane.Fold(p.Name).Contains(folded, StringComparison.Ordinal)), .. hits.Where(p => !ProjectsPane.Fold(p.Name).Contains(folded, StringComparison.Ordinal))];
    }

    void Draw()
    {
        _shown.Clear();
        _shown.AddRange(Filter(_all, _query.Text ?? "").Take(Max));
        _selected = Math.Clamp(_selected, 0, Math.Max(0, _shown.Count - 1));
        _results.Children.Clear();
        for (var i = 0; i < _shown.Count; i++)
        {
            var pick = _shown[i];
            var mark = PlanPanel.Icon(pick.Icon, _ink.Secondary, 15, 1.8);
            var label = Ui.Text(pick.Name, 14, _ink.Text);
            label.Margin = new Thickness(12, 0);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.VerticalAlignment = VerticalAlignment.Center;
            var place = Ui.Text(pick.Where, 12, _ink.Tertiary);
            place.MaxWidth = 220;
            place.TextTrimming = TextTrimming.CharacterEllipsis;
            place.VerticalAlignment = VerticalAlignment.Center;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { mark } };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            Grid.SetColumn(place, 2);
            row.Children.Add(place);
            var item = Ui.Press(row, () => Choose(pick), null, pick.Name);
            item.Padding = new Thickness(12, 8);
            item.CornerRadius = new CornerRadius(10);
            item.Background = i == _selected ? _ink.Pill : Brushes.Transparent;
            _results.Children.Add(item);
            if (i == _selected) Dispatcher.UIThread.Post(() => item.BringIntoView(), DispatcherPriority.Loaded);
        }
        if (_shown.Count == 0)
        {
            var none = Ui.Text(L.T("Nada con ese nombre"), 13, _ink.Tertiary);
            none.Margin = new Thickness(12);
            none.HorizontalAlignment = HorizontalAlignment.Center;
            _results.Children.Add(none);
        }
    }

    void Paint(string placeholder)
    {
        _query.Watermark = placeholder;
        _query.FontSize = 15;
        _query.FontFamily = Ink.Sans;
        _query.Foreground = _ink.Text;
        _query.CaretBrush = _ink.Accent;
        _query.Background = Brushes.Transparent;
        _query.SelectionBrush = PlanPanel.Translucent(_ink.Accent, 0.35);
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
            _query.Resources[key] = Brushes.Transparent;
        _query.Resources["TextControlBorderThemeThickness"] = new Thickness(0);
        _query.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        foreach (var key in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" })
            _query.Resources[key] = _ink.Text;
        foreach (var key in new[] { "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused" })
            _query.Resources[key] = _ink.Tertiary;
        Ui.AutomationName(_query, placeholder);

        var lens = PlanPanel.Icon(Lens, _ink.Tertiary, 17, 1.9);
        lens.Margin = new Thickness(0, 0, 10, 0);
        if (_query.Parent is Panel old) old.Children.Remove(_query);
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(16, 13), Children = { lens } };
        Grid.SetColumn(_query, 1);
        head.Children.Add(_query);
        if (_scroll.Parent is Panel holder) holder.Children.Remove(_scroll);
        _card.Width = 600;
        _card.MaxWidth = 600;
        _card.CornerRadius = new CornerRadius(16);
        _card.Background = _ink.Raised;
        _card.BoxShadow = BoxShadows.Parse("0 20 60 0 #47000000");
        _card.VerticalAlignment = VerticalAlignment.Top;
        _card.HorizontalAlignment = HorizontalAlignment.Center;
        _card.Margin = new Thickness(20, 90, 20, 20);
        _card.ClipToBounds = true;
        _card.Child = new StackPanel { Children = { head, new Border { Height = 1, Background = _ink.Hairline }, _scroll } };
        Background = PlanPanel.Translucent(Brushes.Black, 0.2);
        Child = _card;
    }
}
