namespace Claupet;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

/// <summary>
/// Apariencia › Temas: una tarjeta por tema, con bordes redondeados y la vista previa de ese tema (sus colores, su letra
/// y su escena animada, en el modo claro u oscuro de la Console), su nombre y, en los propios, el menú para exportar y
/// eliminar (con confirmación en la misma tarjeta). La elegida lleva el borde del acento y su marca; tocar otra la elige.
/// La que se está creando brilla y titila hasta que llega el tema, y se puede cancelar. Las acciones van a la bandeja.
/// </summary>
sealed class ThemeGallery : UniformGrid
{
    const double Gap = 6, PreviewHeight = 116;
    readonly Action<string, string?> _act;
    string _shape = "";
    Ink? _ink;

    public ThemeGallery(Action<string, string?> act)
    {
        _act = act;
        Columns = 3;
        // Las tarjetas llevan su margen: el borde de afuera queda alineado con el resto de la página, y abajo quedan 12
        // hasta la tarjeta del cuadro para crear.
        Margin = new Thickness(-Gap, -Gap, -Gap, 12 - Gap);
        SizeChanged += (_, e) => Columns = e.NewSize.Width < 520 ? 2 : 3;
    }

    /// <summary>Las tarjetas; si no cambió nada (otro aviso de la bandeja), se quedan como están y no se cortan las animaciones.</summary>
    public void Show(IReadOnlyList<ThemeCard> cards, Ink ink)
    {
        var shape = string.Join("|", cards.Select(c => $"{c.Id}:{c.Name}:{c.Selected}:{c.Creating}"));
        if (shape == _shape && ReferenceEquals(ink, _ink)) return;
        _shape = shape;
        _ink = ink;
        Children.Clear();
        foreach (var card in cards) Children.Add(card.Creating ? Creating(card, ink) : Card(card, ink));
    }

    /// <summary>La tarjeta de un tema: la vista previa, el nombre con lo que trae y, si es propio, el menú.</summary>
    Control Card(ThemeCard card, Ink ink)
    {
        var theme = card.Id.Length > 0 ? CustomThemes.Load(card.Id) : null;
        var preview = Preview(theme, ink.Dark);

        // La elegida lleva su marca delante del nombre (la vista previa queda libre para la escena).
        var name = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var title = Ui.Text(card.Name, 13.5, ink.Text, FontWeight.SemiBold);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(title, 1);
        name.Children.Add(title);
        if (card.Selected) name.Children.Add(Check(ink));
        var about = theme is null
            ? L.T("Sin tema, con el color {0}", Claupet.Theme.PaletteLabel(Claupet.Theme.SelectedPalette))
            : theme.Description ?? string.Join(" · ", new[] { CustomThemes.SceneLabel(theme.Scene?.Kind), theme.Font is { } font && Ink.Installed(font) ? font : null }
                .OfType<string>().DefaultIfEmpty(L.T("Tema propio")));
        var detail = Ui.Text(about, 12, ink.Tertiary);
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(detail, about);
        var words = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(name);
        words.Children.Add(detail);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(6, 9, 2, 3) };
        footer.Children.Add(words);

        var body = new StackPanel();
        var frame = new Border { Child = preview, CornerRadius = new CornerRadius(11), ClipToBounds = true, Height = PreviewHeight };
        body.Children.Add(frame);
        body.Children.Add(footer);

        var root = Shell(body, ink, card.Selected, () => { if (!card.Selected) _act(SettingsPages.ThemeUseKey + card.Id, null); }, card.Name);
        if (theme is not null)
        {
            Border? more = null;
            more = Ui.Press(Dots(ink.Secondary), () => Menu(more!, card, theme, preview, ink),
                (b, on) => b.Background = on ? ink.Pill : Brushes.Transparent, L.T("Más opciones de «{0}»", card.Name));
            more.Width = 30;
            more.Height = 30;
            more.CornerRadius = new CornerRadius(999);
            more.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(more, 1);
            footer.Children.Add(more);
        }
        return root;
    }

    /// <summary>El marco de toda tarjeta: redondeada, con su borde (el del acento si es la elegida) y un leve ascenso al pasar el cursor.</summary>
    static Border Shell(Control body, Ink ink, bool selected, Action onClick, string name)
    {
        var lift = new TranslateTransform
        {
            Transitions = [new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(140) }],
        };
        var line = selected ? ink.Accent : ink.Hairline;
        var shell = Ui.Press(body, onClick, (b, on) =>
        {
            lift.Y = on ? -2 : 0;
            b.BorderBrush = selected ? ink.Accent : on ? ink.Tertiary : ink.Hairline;
            b.BoxShadow = on ? BoxShadows.Parse(ink.Dark ? "0 8 22 0 #40000000" : "0 8 22 0 #1C000000") : default;
        }, name);
        shell.Background = ink.Raised;
        shell.BorderBrush = line;
        shell.BorderThickness = new Thickness(selected ? 2 : 1);
        shell.CornerRadius = new CornerRadius(16);
        shell.Padding = new Thickness(selected ? 5 : 6);
        shell.Margin = new Thickness(Gap);
        shell.RenderTransform = lift;
        return shell;
    }

    /// <summary>
    /// La vista previa: el fondo y la escena del tema, «Aa» en su letra, los tres colores de estado y una tarjeta chica
    /// con una línea de texto, un interruptor encendido y una barra de uso, como se ve en la Console.
    /// </summary>
    static Grid Preview(CustomTheme? theme, bool dark)
    {
        var ink = Ink.Of(theme, dark);
        var grid = new Grid { Background = ink.Surface };
        if (theme?.Scene is { } scene)
        {
            var layer = new SceneLayer(pixel: 2, alpha: 0.6);
            layer.Show(scene);
            grid.Children.Add(layer);
        }
        // «Aa» y los colores juntos, a la izquierda: arriba a la derecha queda la escena (el sol, la luna).
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(12, 8, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        top.Children.Add(Ui.Text("Aa", 24, ink.Text, FontWeight.SemiBold, Ink.DisplayWith(theme?.Font)));
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        foreach (var brush in new[] { ink.Accent, ink.Warn, ink.Danger })
            dots.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = brush });
        top.Children.Add(dots);
        grid.Children.Add(top);

        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        line.Children.Add(new StackPanel
        {
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Border { Height = 4, Width = 54, CornerRadius = new CornerRadius(2), Background = ink.Text, HorizontalAlignment = HorizontalAlignment.Left },
                new Border { Height = 3, Width = 34, CornerRadius = new CornerRadius(2), Background = ink.Tertiary, HorizontalAlignment = HorizontalAlignment.Left },
            },
        });
        var toggle = new Border
        {
            Width = 18, Height = 10, CornerRadius = new CornerRadius(5), Background = ink.Accent,
            Child = new Ellipse { Width = 6, Height = 6, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 2, 0) },
        };
        Grid.SetColumn(toggle, 1);
        line.Children.Add(toggle);
        var bar = new Grid { Height = 5, ColumnDefinitions = new ColumnDefinitions("62*,38*") };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = ink.Track, [Grid.ColumnSpanProperty] = 2 });
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = ink.Accent });
        var mini = new Border
        {
            Background = ink.Raised,
            BorderBrush = ink.Hairline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(9, 7),
            Margin = new Thickness(12, 0, 0, 10),
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = new StackPanel { Spacing = 7, Children = { line, bar } },
        };
        // Ocupa dos tercios: a la derecha se sigue viendo la escena.
        var room = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,1*") };
        room.Children.Add(mini);
        grid.Children.Add(room);
        return grid;
    }

    /// <summary>La marca de la elegida, delante de su nombre.</summary>
    static Control Check(Ink ink) => new Border
    {
        Width = 16,
        Height = 16,
        CornerRadius = new CornerRadius(8),
        Background = ink.Accent,
        Margin = new Thickness(0, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M3.5,8 L6.5,11 L12,5"),
            Stroke = Ink.On(ink.Accent),
            StrokeThickness = 1.8,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 16,
            Height = 16,
        },
    };

    static Control Dots(IBrush brush)
    {
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < 3; i++) dots.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = brush });
        return dots;
    }

    /// <summary>El menú de un tema propio: usarlo (si no es el elegido), exportarlo y eliminarlo.</summary>
    void Menu(Control anchor, ThemeCard card, CustomTheme theme, Grid preview, Ink ink)
    {
        const string use = "usar", export = "exportar", delete = "eliminar";
        var entries = new List<DropList.Entry>();
        if (!card.Selected) entries.Add(new(use, L.T("Usar este tema")));
        entries.Add(new(export, L.T("Exportar…")));
        entries.Add(new(delete, L.T("Eliminar"), Danger: true));
        DropList.Show(anchor, ink, entries, choice =>
        {
            switch (choice)
            {
                case use: _act(SettingsPages.ThemeUseKey + card.Id, null); break;
                case export: _ = ExportAsync(anchor, theme); break;
                case delete: Confirm(preview, card, ink); break;
            }
        });
    }

    /// <summary>Primero dónde guardarlo (con el selector del sistema); la bandeja escribe el archivo.</summary>
    async Task ExportAsync(Control anchor, CustomTheme theme)
    {
        if (TopLevel.GetTopLevel(anchor)?.StorageProvider is not { CanSave: true } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L.T("Exportar el tema «{0}»", theme.Name),
            SuggestedStartLocation = await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Documents),
            SuggestedFileName = theme.Id + ".json",
            DefaultExtension = "json",
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(L.T("Tema de Claupet")) { Patterns = ["*.json"] }],
        });
        if (file?.TryGetLocalPath() is { } path) _act(SettingsPages.ThemeExportKey + theme.Id, path);
    }

    /// <summary>Antes de borrar, la misma tarjeta pregunta (no se puede deshacer, salvo que se haya exportado).</summary>
    void Confirm(Grid preview, ThemeCard card, Ink ink)
    {
        Border? sheet = null;
        Border Button(string text, IBrush back, IBrush fore, Action click)
        {
            var b = Ui.Press(Ui.Text(text, 12.5, fore, FontWeight.SemiBold), click, (x, on) => x.Opacity = on ? 0.85 : 1, text);
            b.Background = back;
            b.CornerRadius = new CornerRadius(999);
            b.Height = 28;
            b.Padding = new Thickness(12, 0);
            return b;
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        buttons.Children.Add(Button(L.T("Eliminar"), ink.Danger, Ink.On(ink.Danger), () => _act(SettingsPages.ThemeDeleteKey + card.Id, null)));
        var cancel = Button(L.T("Cancelar"), ink.Pill, ink.Text, () => preview.Children.Remove(sheet!));
        buttons.Children.Add(cancel);
        var question = Ui.Text(L.T("¿Eliminar «{0}»?", card.Name), 13, ink.Text, FontWeight.SemiBold);
        question.TextTrimming = TextTrimming.CharacterEllipsis;
        question.HorizontalAlignment = HorizontalAlignment.Center;
        sheet = new Border
        {
            Background = new ImmutableSolidColorBrush(((ISolidColorBrush)ink.Raised).Color, 0.94),
            Padding = new Thickness(10),
            Child = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { question, buttons } },
        };
        // Un clic fuera de los botones no elige la tarjeta de abajo.
        sheet.PointerPressed += (_, e) => e.Handled = true;
        sheet.PointerReleased += (_, e) => e.Handled = true;
        preview.Children.Add(sheet);
        Dispatcher.UIThread.Post(() => cancel.Focus(), DispatcherPriority.Loaded);
    }

    /// <summary>La tarjeta del tema que se está creando: la vista previa que brilla, lo pedido y «Cancelar».</summary>
    Control Creating(ThemeCard card, Ink ink)
    {
        var forge = new ForgePreview(ink);
        var title = Ui.Text(L.T("Creando el tema"), 13.5, ink.Text, FontWeight.SemiBold);
        forge.Tick += step => title.Text = L.T("Creando el tema") + new string('.', step % 4);
        var asked = Ui.Text(card.Name, 12, ink.Tertiary);
        asked.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(asked, card.Name);
        var words = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(title);
        words.Children.Add(asked);
        var stop = Ui.Press(Ui.Text(L.T("Cancelar"), 12.5, ink.Accent, FontWeight.SemiBold), () => _act(SettingsPages.ThemeCancelKey, null),
            (b, on) => b.Background = on ? ink.Pill : Brushes.Transparent, L.T("Cancelar"));
        stop.CornerRadius = new CornerRadius(999);
        stop.Height = 28;
        stop.Padding = new Thickness(10, 0);
        stop.VerticalAlignment = VerticalAlignment.Center;
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(6, 9, 2, 3) };
        footer.Children.Add(words);
        Grid.SetColumn(stop, 1);
        footer.Children.Add(stop);
        var body = new StackPanel();
        body.Children.Add(new Border { Child = forge, CornerRadius = new CornerRadius(11), ClipToBounds = true, Height = PreviewHeight });
        body.Children.Add(footer);
        var shell = Shell(body, ink, selected: false, () => { }, L.T("Creando el tema"));
        shell.Cursor = null;
        return shell;
    }
}

/// <summary>
/// La vista previa mientras Claude crea el tema: el esqueleto de una tarjeta, un brillo que la cruza en diagonal, los
/// tres puntos que se encienden por turno y chispas en pixel art, como la mascota, que titilan en el color del acento.
/// Tick avisa cada medio segundo (los puntos suspensivos del título). Se anima solo mientras está en pantalla.
/// </summary>
sealed class ForgePreview : Control
{
    const double Fps = 30, Sweep = 1.6;
    readonly Ink _ink;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1000 / Fps) };
    readonly (double X, double Y, double Phase)[] _sparks;
    readonly IBrush _skeleton, _shine;
    readonly IPen _line;
    double _time;
    int _step;

    public event Action<int>? Tick;

    public ForgePreview(Ink ink)
    {
        _ink = ink;
        IsHitTestVisible = false;
        var random = new Random(3);
        _sparks = Enumerable.Range(0, 9).Select(_ => (random.NextDouble(), random.NextDouble() * 0.6, random.NextDouble() * Math.PI * 2)).ToArray();
        _skeleton = ink.Pill;
        _line = new Pen(ink.Hairline, 1);
        var glow = ink.Dark ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(150, 255, 255, 255);
        _shine = new ImmutableSolidColorBrush(glow);
        _timer.Tick += (_, _) =>
        {
            _time += 1 / Fps;
            if ((int)(_time * 2) != _step) Tick?.Invoke(_step = (int)(_time * 2));
            InvalidateVisual();
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 1 || h < 1) return;
        context.FillRectangle(_ink.Surface, new Rect(0, 0, w, h));
        // El esqueleto: «Aa», los tres puntos (se encienden por turno) y la tarjeta chica con sus líneas.
        context.DrawRectangle(_skeleton, null, new RoundedRect(new Rect(12, 12, 34, 18), 5));
        for (var i = 0; i < 3; i++)
        {
            var on = (int)(_time * 3) % 3 == i;
            context.DrawEllipse(on ? _ink.Accent : _skeleton, null, new Point(w - 16 - (2 - i) * 12, 21), 4, 4);
        }
        var card = new Rect(12, h - 50, w - 24, 40);
        context.DrawRectangle(_ink.Raised, _line, new RoundedRect(card, 8));
        context.DrawRectangle(_skeleton, null, new RoundedRect(new Rect(card.X + 9, card.Y + 9, 54, 5), 2.5));
        context.DrawRectangle(_skeleton, null, new RoundedRect(new Rect(card.X + 9, card.Bottom - 14, card.Width - 18, 5), 2.5));
        // La barra se llena y vuelve a empezar, como un avance que no termina.
        var fill = (_time % 2.4) / 2.4;
        context.DrawRectangle(_ink.Accent, null, new RoundedRect(new Rect(card.X + 9, card.Bottom - 14, (card.Width - 18) * fill, 5), 2.5));

        // Las chispas: una cruz de píxeles que crece y se apaga.
        const int px = 3;
        foreach (var (x, y, phase) in _sparks)
        {
            var glow = Math.Sin(_time * 3.2 + phase);
            if (glow < 0.2) continue;
            double cx = Math.Floor(x * (w - 20) / px) * px + 10, cy = Math.Floor((y * (h - 60) + 34) / px) * px;
            context.FillRectangle(_ink.Accent, new Rect(cx, cy, px, px));
            if (glow < 0.7) continue;
            context.FillRectangle(_ink.Accent, new Rect(cx - px, cy, px, px));
            context.FillRectangle(_ink.Accent, new Rect(cx + px, cy, px, px));
            context.FillRectangle(_ink.Accent, new Rect(cx, cy - px, px, px));
            context.FillRectangle(_ink.Accent, new Rect(cx, cy + px, px, px));
        }

        // El brillo que cruza en diagonal.
        var band = w * 0.45;
        var at = (_time % Sweep) / Sweep * (w + band * 2) - band;
        var shine = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(at, 0, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(at + band, h * 0.35, RelativeUnit.Absolute),
            GradientStops =
            {
                new GradientStop(Colors.Transparent, 0),
                new GradientStop(((ISolidColorBrush)_shine).Color, 0.5),
                new GradientStop(Colors.Transparent, 1),
            },
        };
        context.FillRectangle(shine, new Rect(0, 0, w, h));
    }
}
