namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

/// <summary>
/// Ajustes › Extensiones: el catálogo oficial y las instaladas en tarjetas. Arriba, las pestañas (Explorar, Instaladas y
/// Actualizaciones, con cuántas hay), la búsqueda y los botones de ícono de la fila (buscar actualizaciones, instalar
/// desde archivo, ranuras); debajo, los filtros por tipo y las tarjetas en dos columnas; al pie, el estado del catálogo.
/// La pestaña, el filtro y lo buscado son de la Console: no viajan a la bandeja. Tocar una tarjeta lleva a su página;
/// su botón instala, actualiza o lleva a sus ajustes. Lo demás son acciones de la bandeja («ext-…»).
/// </summary>
sealed class ExtensionMarket : StackPanel
{
    enum Tab { Explore, Installed, Updates }

    /// <summary>Los colores de los íconos de las tarjetas: uno fijo por extensión (por su id), que se lee en claro y en oscuro.</summary>
    static readonly Color[] Tints =
    [
        Color.Parse("#5B8DEF"), Color.Parse("#D97757"), Color.Parse("#13A87B"), Color.Parse("#A67DE0"),
        Color.Parse("#D9962B"), Color.Parse("#2F7FC1"), Color.Parse("#C4507F"), Color.Parse("#6E8B3D"),
    ];

    readonly Action<string, string?> _act;
    readonly Action<string> _navigate;
    readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    readonly Border _tabsBox = new() { CornerRadius = new CornerRadius(12), Padding = new Thickness(3), BorderThickness = new Thickness(1) };
    readonly TextBox _search = new()
    {
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        FontFamily = Ink.Sans,
        FontSize = 13.5,
        VerticalContentAlignment = VerticalAlignment.Center,
        MinHeight = 34,
        Padding = new Thickness(6, 0),
    };
    readonly Border _searchBox = new() { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(10, 0, 4, 0) };
    readonly StackPanel _icons = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    readonly WrapPanel _chips = new() { Margin = new Thickness(0, 14, 0, 0) };
    readonly StackPanel _body = new() { Margin = new Thickness(0, 14, 0, 0) };
    readonly StackPanel _status = new() { Orientation = Orientation.Horizontal, Spacing = 7, Margin = new Thickness(2, 16, 0, 0) };
    Tab _tab;
    string? _kind;
    int _columns = 2;
    SettingsRow? _row;
    Ink? _ink;
    string _shape = "";

    public ExtensionMarket(Action<string, string?> act, Action<string> navigate)
    {
        _act = act;
        _navigate = navigate;
        Margin = new Thickness(0, 24, 0, 0);
        _search.Watermark = L.T("Buscar extensiones");
        AutomationName(_search, L.T("Buscar extensiones"));
        _search.TextChanged += (_, _) => Fill();
        _tabsBox.Child = _tabs;

        var searchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        searchRow.Children.Add(Glyph(Icons.Search, null, 15));
        Grid.SetColumn(_search, 1);
        searchRow.Children.Add(_search);
        _searchBox.Child = searchRow;

        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        bar.Children.Add(_tabsBox);
        _searchBox.Margin = new Thickness(10, 0);
        Grid.SetColumn(_searchBox, 1);
        bar.Children.Add(_searchBox);
        Grid.SetColumn(_icons, 2);
        bar.Children.Add(_icons);
        Children.Add(bar);
        Children.Add(_chips);
        Children.Add(_body);
        Children.Add(_status);
        SizeChanged += (_, e) =>
        {
            var columns = e.NewSize.Width < 600 ? 1 : 2;
            if (columns == _columns) return;
            _columns = columns;
            Fill();
        };
    }

    IReadOnlyList<ExtensionCard> Cards => _row?.Market ?? [];

    /// <summary>La fila como llegó de la bandeja; si no cambió nada que se vea, no se rearma (no se pierde el foco de la búsqueda).</summary>
    public void Show(SettingsRow row, Ink ink)
    {
        var shape = string.Join("|", (row.Market ?? []).Select(c => c.ToString())) + "§" + row.Value;
        if (shape == _shape && ReferenceEquals(ink, _ink)) return;
        var repaint = !ReferenceEquals(ink, _ink);
        _shape = shape;
        _row = row;
        _ink = ink;
        if (repaint)
        {
            _tabsBox.Background = ink.Raised;
            _tabsBox.BorderBrush = ink.Hairline;
            _searchBox.Background = ink.Raised;
            _searchBox.BorderBrush = ink.Hairline;
            _search.Foreground = ink.Text;
            ((Avalonia.Controls.Shapes.Path)((Viewbox)((Grid)_searchBox.Child!).Children[0]).Child!).Stroke = ink.Tertiary;
            _icons.Children.Clear();
            foreach (var icon in row.Icons ?? []) _icons.Children.Add(HeadIcon(icon, ink));
        }
        Fill();
    }

    /// <summary>Las pestañas, los filtros, las tarjetas y el estado, con lo elegido aquí.</summary>
    void Fill()
    {
        if (_ink is not { } ink || _row is null) return;
        var cards = Cards;
        var updates = cards.Count(c => c.Update);

        _tabs.Children.Clear();
        _tabs.Children.Add(TabButton(Tab.Explore, L.T("Explorar"), Icons.Grid, 0, ink));
        _tabs.Children.Add(TabButton(Tab.Installed, L.T("Instaladas"), Icons.Box, 0, ink));
        _tabs.Children.Add(TabButton(Tab.Updates, L.T("Actualizaciones"), Icons.Up, updates, ink));

        // Solo los tipos que hay en la pestaña.
        var inTab = cards.Where(InTab).ToList();
        var kinds = inTab.SelectMany(c => c.Provides).Distinct().OrderBy(k => Array.IndexOf(KindOrder, k) is var i and >= 0 ? i : 99).ToList();
        if (_kind is not null && !kinds.Contains(_kind)) _kind = null;
        _chips.Children.Clear();
        _chips.IsVisible = kinds.Count > 1;
        _chips.Children.Add(Chip(null, L.T("Todas"), null, ink));
        foreach (var kind in kinds) _chips.Children.Add(Chip(kind, ExtensionSlots.Label(kind), KindIcon(kind), ink));

        var query = _search.Text?.Trim() ?? "";
        var shown = inTab.Where(c => (_kind is null || c.Provides.Contains(_kind))
            && (query.Length == 0 || c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || c.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToList();

        _body.Children.Clear();
        if (_tab == Tab.Updates && updates > 1)
        {
            var all = Pill(L.T("Actualizar todas"), ink, accent: true, () => _act("ext-update-all", null));
            all.HorizontalAlignment = HorizontalAlignment.Right;
            all.Margin = new Thickness(0, 0, 0, 12);
            _body.Children.Add(all);
        }
        if (shown.Count == 0)
        {
            var empty = Ui.Text(EmptyText(query), 13.5, ink.Tertiary);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Margin = new Thickness(0, 40);
            _body.Children.Add(empty);
        }
        else
        {
            var grid = new UniformGrid { Columns = _columns, Margin = new Thickness(-6) };
            foreach (var card in shown) grid.Children.Add(Card(card, ink));
            _body.Children.Add(grid);
        }

        _status.Children.Clear();
        _status.Children.Add(Glyph(Icons.Shield, ink.Tertiary, 14));
        _status.Children.Add(Ui.Text(_row.Value, 12.5, ink.Tertiary));
    }

    static readonly string[] KindOrder =
    [
        ExtensionSlots.SpeechToText, ExtensionSlots.TextToSpeech, ExtensionSlots.Assistant, ExtensionSlots.Skill,
        ExtensionSlots.Pet, ExtensionSlots.VoiceStyle, ExtensionSlots.Sounds,
    ];

    bool InTab(ExtensionCard card) => _tab switch
    {
        Tab.Installed => card.Installed is not null,
        Tab.Updates => card.Update || (card.Installed is not null && card.Percent is not null),
        _ => true,
    };

    string EmptyText(string query) => query.Length > 0 || _kind is not null ? L.T("No hay extensiones que coincidan.") : _tab switch
    {
        Tab.Installed => L.T("No hay extensiones instaladas."),
        Tab.Updates => L.T("Todo está al día."),
        _ => L.T("El catálogo todavía no tiene extensiones."),
    };

    Border TabButton(Tab tab, string text, string icon, int badge, Ink ink)
    {
        var on = _tab == tab;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        content.Children.Add(Glyph(icon, on ? ink.Text : ink.Secondary, 15));
        content.Children.Add(Ui.Text(text, 13.5, on ? ink.Text : ink.Secondary));
        if (badge > 0)
            content.Children.Add(new Border
            {
                Background = ink.Accent,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(6, 0),
                MinWidth = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Child = Ui.Text(badge.ToString(), 11, Ink.On(ink.Accent), FontWeight.SemiBold),
            });
        var button = Ui.Press(content, () =>
        {
            _tab = tab;
            Fill();
        }, (b, hover) => b.Background = on ? ink.Pill : hover ? ink.Pill : Brushes.Transparent, text);
        button.Padding = new Thickness(12, 6);
        button.CornerRadius = new CornerRadius(9);
        button.Background = on ? ink.Pill : Brushes.Transparent;
        return button;
    }

    Border Chip(string? kind, string text, string? icon, Ink ink)
    {
        var on = _kind == kind;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (icon is not null) content.Children.Add(Glyph(icon, on ? ink.Text : ink.Secondary, 13));
        content.Children.Add(Ui.Text(text, 12.5, on ? ink.Text : ink.Secondary));
        var chip = Ui.Press(content, () =>
        {
            _kind = kind;
            Fill();
        }, (b, hover) => b.BorderBrush = on ? ink.Accent : hover ? ink.Tertiary : ink.Hairline, text);
        chip.Padding = new Thickness(11, 5);
        chip.Margin = new Thickness(0, 0, 6, 6);
        chip.CornerRadius = new CornerRadius(999);
        chip.BorderThickness = new Thickness(1);
        chip.BorderBrush = on ? ink.Accent : ink.Hairline;
        chip.Background = on ? Tint(ink.Accent, 0.14) : Brushes.Transparent;
        return chip;
    }

    Control Card(ExtensionCard card, Ink ink)
    {
        var tint = Tints[card.Id.Aggregate(0, (h, c) => h * 31 + c & 0x7fffffff) % Tints.Length];
        var icon = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(tint),
            VerticalAlignment = VerticalAlignment.Top,
            Child = Glyph(KindIcon(card.Provides.FirstOrDefault() ?? ""), Brushes.White, 22),
            Opacity = card.Installed is not null && !card.Enabled ? 0.5 : 1,
        };

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        title.Children.Add(Ui.Text(card.Name, 14, ink.Text, FontWeight.SemiBold));
        if (card.Official)
        {
            var shield = Glyph(Icons.Shield, ink.Accent, 15);
            ToolTip.SetTip(shield, L.T("Oficial: firmada por Claupet"));
            title.Children.Add(shield);
        }
        var description = new TextBlock
        {
            Text = card.Description,
            FontSize = 12.5,
            FontFamily = Ink.Sans,
            Foreground = ink.Secondary,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 3, 0, 8),
        };
        var meta = new WrapPanel();
        foreach (var kind in card.Provides.Take(2)) meta.Children.Add(Meta(KindIcon(kind), ExtensionSlots.Label(kind), ink));
        if (card.Downloads is { } downloads) meta.Children.Add(Meta(Icons.Down, Count(downloads), ink));
        if (card.Installed is { } installed)
            meta.Children.Add(Tag(!card.Enabled ? L.T("Apagada") : card.Update ? $"{installed} → {card.Latest}" : L.T("Instalada"),
                card.Enabled ? ink.Accent : ink.Tertiary));

        var body = new StackPanel { Margin = new Thickness(12, 0, 8, 0) };
        body.Children.Add(title);
        body.Children.Add(description);
        body.Children.Add(meta);
        if (card.Percent is { } percent) body.Children.Add(Progress(percent, ink));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(icon);
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        var action = Action(card, ink);
        Grid.SetColumn(action, 2);
        grid.Children.Add(action);

        var root = Ui.Press(grid, () => _navigate(card.Page), (b, hover) => b.BorderBrush = hover ? ink.Tertiary : ink.Hairline, card.Name);
        root.Background = ink.Raised;
        root.BorderBrush = ink.Hairline;
        root.BorderThickness = new Thickness(1);
        root.CornerRadius = new CornerRadius(14);
        root.Padding = new Thickness(14);
        root.Margin = new Thickness(6);
        return root;
    }

    /// <summary>A la derecha de la tarjeta: lo que se puede hacer con ella, o por qué no.</summary>
    Control Action(ExtensionCard card, Ink ink)
    {
        if (card.Percent is { } percent)
            return Top(Ui.Text(percent + " %", 12.5, ink.Tertiary));
        if (card.NeedsClaupet is { } needs)
        {
            var tag = Tag(needs + "+", ink.Warn);
            ToolTip.SetTip(tag, L.T("Necesita Claupet {0} o más nueva", needs));
            return Top(tag);
        }
        if (card.Update) return Top(RoundIcon(Icons.Up, L.T("Actualizar a {0}", card.Latest!), ink, accent: true, () => _act("ext-get:" + card.Id, null)));
        if (card.Installed is not null) return Top(RoundIcon(Icons.Gear, L.T("Ajustes de la extensión"), ink, accent: false, () => _navigate(card.Page)));
        return Top(RoundIcon(Icons.Down, L.T("Instalar"), ink, accent: true, () => _act("ext-get:" + card.Id, null)));
    }

    static Control Top(Control control)
    {
        control.VerticalAlignment = VerticalAlignment.Top;
        return control;
    }

    static Control Progress(int percent, Ink ink)
    {
        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{Math.Max(percent, 1)}*,{Math.Max(100 - percent, 0.001)}*"),
            Height = 4,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var track = new Border { Background = ink.Track, CornerRadius = new CornerRadius(2) };
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        bar.Children.Add(new Border { Background = ink.Accent, CornerRadius = new CornerRadius(2) });
        return bar;
    }

    static Control Meta(string icon, string text, Ink ink)
    {
        var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 0, 10, 2) };
        item.Children.Add(Glyph(icon, ink.Tertiary, 12));
        item.Children.Add(Ui.Text(text, 12, ink.Tertiary));
        return item;
    }

    static Border Tag(string text, IBrush color) => new()
    {
        Background = Tint(color, 0.18),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(7, 1),
        Margin = new Thickness(0, 0, 0, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = Ui.Text(text, 11.5, color),
    };

    /// <summary>1.234 → «1,2 mil»: cuántas veces se bajó, en corto.</summary>
    static string Count(long n) => n switch
    {
        < 1000 => n.ToString(System.Globalization.CultureInfo.CurrentCulture),
        < 1_000_000 => L.T("{0} mil", (n / 1000.0).ToString(n < 10_000 ? "0.#" : "0", System.Globalization.CultureInfo.CurrentCulture)),
        _ => L.T("{0} M", (n / 1_000_000.0).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture)),
    };

    static IBrush Tint(IBrush brush, double alpha) => brush is ISolidColorBrush solid
        ? new SolidColorBrush(solid.Color, alpha)
        : brush;

    Border RoundIcon(string icon, string name, Ink ink, bool accent, Action onClick)
    {
        var button = Ui.Press(Glyph(icon, accent ? Ink.On(ink.Accent) : ink.Secondary, 17), onClick,
            (b, hover) => b.Opacity = hover ? 0.85 : 1, name);
        button.Width = 34;
        button.Height = 34;
        button.CornerRadius = new CornerRadius(10);
        button.Background = accent ? ink.Accent : ink.Pill;
        ToolTip.SetTip(button, name);
        return button;
    }

    static Border Pill(string text, Ink ink, bool accent, Action onClick)
    {
        var button = Ui.Press(Ui.Text(text, 13.5, accent ? Ink.On(ink.Accent) : ink.Text, FontWeight.SemiBold), onClick,
            (b, hover) => b.Opacity = hover ? 0.88 : 1, text);
        button.Background = accent ? ink.Accent : ink.Pill;
        button.CornerRadius = new CornerRadius(999);
        button.Height = 34;
        button.Padding = new Thickness(16, 0);
        return button;
    }

    /// <summary>Los botones de la fila (buscar actualizaciones, instalar desde archivo, ranuras); con File, piden antes el archivo.</summary>
    Border HeadIcon(RowIcon icon, Ink ink)
    {
        var path = icon.Icon switch
        {
            RowIconKind.Refresh => Icons.Refresh,
            RowIconKind.Package => Icons.Package,
            RowIconKind.Sliders => Icons.Sliders,
            RowIconKind.Add => Icons.Add,
            _ => Icons.Box,
        };
        var key = icon.Key;
        var button = Ui.Press(Glyph(path, ink.Secondary, 17), () =>
        {
            if (icon.File is { } pattern) _ = PickFileAsync(pattern, key, icon.Name);
            else _act(key, null);
        }, (b, hover) => b.Background = hover ? ink.Pill : Brushes.Transparent, icon.Name);
        button.Width = 34;
        button.Height = 34;
        button.CornerRadius = new CornerRadius(17);
        ToolTip.SetTip(button, icon.Name);
        return button;
    }

    async Task PickFileAsync(string pattern, string key, string title)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(title) { Patterns = pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) _act(key, path);
    }

    static void AutomationName(Control control, string name) => Avalonia.Automation.AutomationProperties.SetName(control, name);

    static Viewbox Glyph(string data, IBrush? brush, double size) => new()
    {
        Width = size,
        Height = size,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(data),
            Stroke = brush,
            StrokeThickness = 1.8,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 24,
            Height = 24,
        },
    };

    static string KindIcon(string kind) => kind switch
    {
        ExtensionSlots.SpeechToText => Icons.Mic,
        ExtensionSlots.TextToSpeech => Icons.Speaker,
        ExtensionSlots.Assistant => Icons.Bot,
        ExtensionSlots.Pet => Icons.Paw,
        ExtensionSlots.VoiceStyle => Icons.Wave,
        ExtensionSlots.Sounds => Icons.Music,
        _ => Icons.Bolt,
    };

    /// <summary>Los íconos de la vista, en un cuadro de 24 y de trazo (como los del resto de la Console).</summary>
    static class Icons
    {
        static string Circle(double x, double y, double r) =>
            FormattableString.Invariant($"M{x - r},{y} A{r},{r} 0 1 0 {x + r},{y} A{r},{r} 0 1 0 {x - r},{y} Z ");

        public const string Grid = "M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H20 V20 H14 Z";
        public const string Box = "M21,8 L12,3 L3,8 L12,13 Z M3,8 V16 L12,21 L21,16 V8 M12,13 V21";
        public const string Up = "M12,20 V9 M7,14 L12,9 L17,14 M5,4 H19";
        public const string Down = "M12,4 V15 M7,10 L12,15 L17,10 M5,20 H19";
        public const string Shield = "M12,3 L4.5,6 V12 C4.5,16.5 7.8,19.6 12,21 C16.2,19.6 19.5,16.5 19.5,12 V6 Z M9,12 L11,14 L15,10";
        public static readonly string Search = Circle(11, 11, 7) + "M20,20 L16.5,16.5";
        public static readonly string Gear = Circle(12, 12, 3)
            + "M12,3 V5.5 M12,18.5 V21 M3,12 H5.5 M18.5,12 H21 M5.6,5.6 L7.4,7.4 M16.6,16.6 L18.4,18.4 M5.6,18.4 L7.4,16.6 M16.6,7.4 L18.4,5.6";
        public const string Mic = "M9,6 A3,3 0 0 1 15,6 V11 A3,3 0 0 1 9,11 Z M5,11 A7,7 0 0 0 19,11 M12,18 V21";
        public const string Speaker = "M11,5 L6,9 H3 V15 H6 L11,19 Z M15.5,8.5 A5,5 0 0 1 15.5,15.5 M18.5,5.5 A9,9 0 0 1 18.5,18.5";
        public const string Bot = "M7,8 H17 A3,3 0 0 1 20,11 V17 A3,3 0 0 1 17,20 H7 A3,3 0 0 1 4,17 V11 A3,3 0 0 1 7,8 Z M12,4 V8 M9,14 V14.5 M15,14 V14.5";
        public const string Bolt = "M13,2 L4,14 H11 L10,22 L19,10 H12 Z";
        public static readonly string Paw = Circle(7, 10, 2) + Circle(17, 10, 2) + Circle(10, 5.5, 1.8) + Circle(14, 5.5, 1.8)
            + "M8,17 C8,14 10,12 12,12 C14,12 16,14 16,17 C16,20 14,20 12,20 C10,20 8,20 8,17 Z";
        public static readonly string Music = "M9,18 V5 L21,3 V16 " + Circle(6, 18, 3) + Circle(18, 16, 3);
        public const string Wave = "M3,12 H5 M7,8 V16 M11,5 V19 M15,8 V16 M19,11 V13";
        public const string Refresh = "M19.5,12 A7.5,7.5 0 1 1 17.3,6.7 M17.5,3 V7 H13.5";
        public const string Add = "M12,5 V19 M5,12 H19";
        public const string Package = "M14,3 H6 A2,2 0 0 0 4,5 V19 A2,2 0 0 0 6,21 H18 A2,2 0 0 0 20,19 V9 Z M14,3 V9 H20 M12,18 V12 M9,15 L12,18 L15,15";
        public const string Sliders = "M4,6 H13 M17,6 H20 M4,12 H7 M11,12 H20 M4,18 H15 M19,18 H20 M15,4 V8 M9,10 V14 M17,16 V20";
    }
}
