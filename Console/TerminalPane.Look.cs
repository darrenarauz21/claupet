namespace Claupet;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

/// <summary>
/// Ajustes › Terminal: la letra, el tamaño, el interlineado, el color del texto, el fondo, las animaciones y la lista de
/// sesiones, con una vista previa que cambia al elegir. Se abre con el engranaje de la columna o desde Ajustes.
/// </summary>
sealed partial class TerminalPane
{
    const string ListIcon = "M9,6 H20 M9,12 H20 M9,18 H20 M4.5,6 H5 M4.5,12 H5 M4.5,18 H5";
    const string GridIcon = "M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H20 V20 H14 Z";

    /// <summary>Abre los ajustes de la Terminal en su panel (desde Ajustes de la Console o el engranaje).</summary>
    public void ShowSettings()
    {
        if (_current is { } previous && !_creating) previous.Draft = _input.Text ?? "";
        _settings = true;
        Build();
        RenderList();
    }

    void CloseSettings()
    {
        _settings = false;
        if (_current is not null && !_creating) _input.Text = _current.Draft;
        Build();
        RenderList();
        FocusInput();
    }

    /// <summary>Guarda lo elegido y lo aplica: los bloques, la lista y la página misma (su vista previa).</summary>
    void ChangeLook(Action<TerminalLook> change)
    {
        var listWidth = ListWidth;
        change(_look);
        _look.Save();
        ApplyLook();
        Build();
        RenderList();
        if (ListWidth != listWidth) ListChanged?.Invoke();
    }

    /// <summary>Los botones de la columna: lista o íconos, ajustes y sesión nueva.</summary>
    Control HeaderButtons(Border add)
    {
        var icons = _look.IconsOnly;
        var panel = new StackPanel
        {
            Orientation = icons ? Orientation.Vertical : Orientation.Horizontal,
            Spacing = icons ? 6 : 2,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = icons ? HorizontalAlignment.Center : HorizontalAlignment.Right,
        };
        panel.Children.Add(Round(icons ? ListIcon : GridIcon, icons ? L.T("Lista con descripción") : L.T("Solo íconos"),
            () => ChangeLook(l => l.IconsOnly = !l.IconsOnly)));
        if (!icons && Projects().Count > 1)
            panel.Children.Add(Round(AllFolded ? UnfoldAllIcon : FoldAllIcon, AllFolded ? L.T("Desplegar todos los proyectos") : L.T("Plegar todos los proyectos"), FoldAll));
        // Los ajustes de la Terminal están en Ajustes › Terminal, con los demás.
        add.Margin = icons ? default : new Thickness(4, 0, 0, 0);
        panel.Children.Add(add);
        return panel;
    }

    Control LookPage()
    {
        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(32, 28), HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(new TextBlock
        {
            Text = L.T("Ajustes de la Terminal"),
            FontFamily = Ink.Display,
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            Foreground = _ink.Text,
        });
        var about = Ui.Text(L.T("Cómo se ven las sesiones. Se guarda en este equipo y vale para todas las pestañas."), 13, _ink.Secondary);
        about.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(about);

        var fonts = TerminalLook.InstalledFonts();
        panel.Children.Add(Field(L.T("Letra"), Ui.Text(_look.Font ?? L.T("La de siempre"), 13, _ink.Text, family: Mono), anchor =>
        {
            DropList.Show(anchor, _ink, fonts.Select(f => new DropList.Entry(f, f, f == _look.Font))
                .Prepend(new DropList.Entry("", L.T("La de siempre"), _look.Font is null)).ToList(), picked =>
            {
                if (picked is not null) ChangeLook(l => l.Font = picked.Length == 0 ? null : picked);
            });
            return Task.CompletedTask;
        }));
        panel.Children.Add(Field(L.T("Tamaño"), Ui.Text(Points(_look.Size), 13, _ink.Text), anchor =>
        {
            DropList.Show(anchor, _ink, TerminalLook.Sizes.Select(s => new DropList.Entry(s.ToString(System.Globalization.CultureInfo.InvariantCulture), Points(s), s == _look.Size)).ToList(),
                picked =>
                {
                    if (picked is not null) ChangeLook(l => l.Size = double.Parse(picked, System.Globalization.CultureInfo.InvariantCulture));
                });
            return Task.CompletedTask;
        }));
        panel.Children.Add(Field(L.T("Interlineado"), Ui.Text(TerminalLook.SpacingLabel(_look.LineSpacing), 13, _ink.Text), anchor =>
        {
            DropList.Show(anchor, _ink, TerminalLook.Spacings.Select(s => new DropList.Entry(s.ToString(System.Globalization.CultureInfo.InvariantCulture),
                TerminalLook.SpacingLabel(s), s == _look.LineSpacing)).ToList(), picked =>
            {
                if (picked is not null) ChangeLook(l => l.LineSpacing = double.Parse(picked, System.Globalization.CultureInfo.InvariantCulture));
            });
            return Task.CompletedTask;
        }));
        panel.Children.Add(Field(L.T("Color del texto"), Ui.Text(TerminalLook.TextLabel(_look.Text), 13, _ink.Text), anchor =>
        {
            DropList.Show(anchor, _ink, TerminalLook.Texts.Select(t => new DropList.Entry(t, TerminalLook.TextLabel(t), t == _look.Text)).ToList(), picked =>
            {
                if (picked is not null) ChangeLook(l => l.Text = picked);
            });
            return Task.CompletedTask;
        }));
        panel.Children.Add(Field(L.T("Fondo"), Ui.Text(TerminalLook.BackgroundLabel(_look.Background), 13, _ink.Text), anchor =>
        {
            DropList.Show(anchor, _ink, TerminalLook.Backgrounds.Select(b => new DropList.Entry(b, TerminalLook.BackgroundLabel(b), b == _look.Background)).ToList(), picked =>
            {
                if (picked is not null) ChangeLook(l => l.Background = picked);
            });
            return Task.CompletedTask;
        }));
        panel.Children.Add(Field(L.T("Lista de sesiones"), Ui.Text(_look.IconsOnly ? L.T("Solo íconos") : L.T("Con descripción"), 13, _ink.Text), anchor =>
        {
            DropList.Show(anchor, _ink, [new("lista", L.T("Con descripción"), !_look.IconsOnly), new("iconos", L.T("Solo íconos"), _look.IconsOnly)], picked =>
            {
                if (picked is not null) ChangeLook(l => l.IconsOnly = picked == "iconos");
            });
            return Task.CompletedTask;
        }));
        panel.Children.Add(Toggle(L.T("Animaciones"), _look.Animate, on => ChangeLook(l => l.Animate = on)));
        var hint = Ui.Text(L.T("Pensando, el giro de los pasos y los bloques que aparecen. Apagadas, gasta menos."), 12, _ink.Tertiary);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(4, -8, 0, 0);
        panel.Children.Add(hint);

        panel.Children.Add(Ui.Text(L.T("Vista previa"), 12.5, _ink.Tertiary, FontWeight.Medium));
        panel.Children.Add(Preview());

        // Los proyectos vinculados: desvincular solo los quita de la lista, no borra la carpeta.
        panel.Children.Add(Ui.Text(L.T("Proyectos vinculados"), 12.5, _ink.Tertiary, FontWeight.Medium));
        if (_projects.Folders.Count == 0)
        {
            var none = Ui.Text(L.T("Todavía no hay. Un proyecto se vincula al abrir una sesión en su carpeta."), 12.5, _ink.Tertiary);
            none.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(none);
        }
        foreach (var folder in _projects.Folders.ToList())
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 40 };
            var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Ui.Text(System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(folder)) is { Length: > 0 } name ? name : folder, 13, _ink.Text));
            var path = Ui.Text(ShortPath(folder), 11.5, _ink.Tertiary, family: Mono);
            path.TextTrimming = TextTrimming.PrefixCharacterEllipsis;
            text.Children.Add(path);
            row.Children.Add(text);
            var sessions = TabsOf(folder).Count;
            var unlink = Pill(L.T("Desvincular"), _ink.Pill, _ink.Danger, () => Unlink(folder));
            if (sessions > 0) ToolTip.SetTip(unlink, L.T("Cierra sus {0} sesiones", sessions));
            unlink.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(unlink, 1);
            row.Children.Add(unlink);
            panel.Children.Add(row);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(Pill(L.T("Listo"), _ink.Accent, Ink.On(_ink.Accent), CloseSettings));
        buttons.Children.Add(Pill(L.T("Lo de siempre"), _ink.Pill, _ink.Text, () => ChangeLook(l =>
        {
            var fresh = new TerminalLook();
            (l.Font, l.Size, l.LineSpacing, l.Text, l.Background, l.Animate, l.IconsOnly) =
                (fresh.Font, fresh.Size, fresh.LineSpacing, fresh.Text, fresh.Background, fresh.Animate, fresh.IconsOnly);
        })));
        panel.Children.Add(buttons);
        var scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroll.SizeChanged += (_, e) => panel.Width = Math.Clamp(e.NewSize.Width - 64, 0, 560);
        return scroll;
    }

    static string Points(double size) => L.T("{0} pt", size.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture));

    /// <summary>Unos bloques de ejemplo con lo elegido: el pedido, un paso con su cambio y la respuesta.</summary>
    Control Preview()
    {
        var blocks = new StackPanel { Spacing = 6 };
        blocks.Children.Add(PreviewView(new AgentBlock { Kind = AgentBlockKind.Ask, Text = L.T("Agrega una prueba para el inicio de sesión") }));
        var step = new AgentBlock
        {
            Kind = AgentBlockKind.Step,
            Tool = "Edit",
            Text = "src/login.test.ts",
            State = StepState.Done,
            Added = 12,
            Removed = 1,
        };
        blocks.Children.Add(PreviewView(step));
        blocks.Children.Add(PreviewView(new AgentBlock
        {
            Kind = AgentBlockKind.Say,
            Text = L.T("Listo: la prueba revisa la contraseña equivocada y pasa.") + "\n\n```\nnpm test  →  14 " + L.T("pruebas correctas") + "\n```",
        }));
        var scale = _look.Size / TerminalLook.DefaultSize;
        return new Border
        {
            Background = _ink.Surface,
            BorderBrush = _ink.Hairline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12),
            ClipToBounds = true,
            Child = new LayoutTransformControl { LayoutTransform = new ScaleTransform(scale, scale), Child = blocks },
        };
    }

    /// <summary>El bloque de ejemplo, sin quedar anotado entre los de la sesión.</summary>
    Control PreviewView(AgentBlock block)
    {
        var view = View(block, appear: false);
        _views.Remove(block);
        return view;
    }

    /// <summary>Una fila con interruptor, con el mismo dibujo que los de Ajustes.</summary>
    Control Toggle(string label, bool on, Action<bool> changed)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var text = Ui.Text(label, 13, _ink.Secondary);
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        var knob = new TranslateTransform(on ? 18 : 0, 0)
        {
            Transitions = [new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(140) }],
        };
        var track = new Border
        {
            Width = 44,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = on ? _ink.Accent : _ink.Track,
            Child = new Ellipse { Width = 20, Height = 20, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0), RenderTransform = knob },
        };
        Grid.SetColumn(track, 1);
        row.Children.Add(track);
        var press = Ui.Press(row, () => changed(!on), (b, hover) => b.Background = hover ? _ink.Track : _ink.Pill, label);
        press.Background = _ink.Pill;
        press.CornerRadius = new CornerRadius(10);
        press.Padding = new Thickness(14, 0, 8, 0);
        press.Height = 40;
        return press;
    }
}
