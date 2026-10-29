namespace Claupet;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

/// <summary>
/// La lista desplegable de la Console, como lo que flota en el diseño: una tarjeta con borde fino, esquinas de 14 y
/// sombra, con las opciones en filas redondeadas (la elegida, con su marca de acento) y, separadas abajo, las que hacen
/// algo en lugar de elegir («Elegir otra carpeta…»). Se maneja con el mouse o con las flechas, Intro y Esc. Va en la
/// capa de arriba de la ventana, no en otra ventana.
/// </summary>
static class DropList
{
    /// <summary>Una fila. Action: hace algo en vez de elegir (va separada, abajo); Danger: en rojo.</summary>
    public sealed record Entry(string Id, string Label, bool Selected = false, bool Action = false, bool Danger = false);

    const string CheckIcon = "M5,12.5 L10,17.5 L19,7";

    /// <summary>Muestra la lista bajo anchor. reply: el id elegido, o null si se cerró sin elegir.</summary>
    public static void Show(Control anchor, Ink ink, IReadOnlyList<Entry> entries, Action<string?> reply)
    {
        if (TopLevel.GetTopLevel(anchor)?.Content is not Panel host || entries.Count == 0)
        {
            reply(null);
            return;
        }
        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.BottomEdgeAlignedRight,
            VerticalOffset = 6,
            IsLightDismissEnabled = true,
            ShouldUseOverlayLayer = true,
        };
        var done = false;
        void Finish(string? id)
        {
            if (done) return;
            done = true;
            popup.IsOpen = false;
            reply(id);
        }

        var items = new List<Border>();
        var list = new StackPanel { Spacing = 1 };
        var actions = false;
        foreach (var entry in entries)
        {
            if (entry.Action && !actions && items.Count > 0)
                list.Children.Add(new Border { Height = 1, Background = ink.Hairline, Margin = new Thickness(8, 4) });
            actions |= entry.Action;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var text = Ui.Text(entry.Label, 14, entry.Danger ? ink.Danger : entry.Action ? ink.Secondary : ink.Text,
                entry.Selected ? FontWeight.SemiBold : FontWeight.Normal);
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Children.Add(text);
            if (entry.Selected)
            {
                var check = Icon(CheckIcon, ink.Accent, 15);
                check.Margin = new Thickness(14, 0, 0, 0);
                Grid.SetColumn(check, 1);
                row.Children.Add(check);
            }
            var id = entry.Id;
            var item = Ui.Press(row, () => Finish(id), (b, on) => b.Background = on ? ink.Pill : Brushes.Transparent, entry.Label);
            item.MinHeight = 36;
            item.Padding = new Thickness(12, 0);
            item.CornerRadius = new CornerRadius(10);
            // Con el teclado, la fila con el foco se marca como con el mouse encima.
            item.GotFocus += (_, e) => { if (e.NavigationMethod != NavigationMethod.Unspecified) item.Background = ink.Pill; };
            item.LostFocus += (_, _) => item.Background = Brushes.Transparent;
            list.Children.Add(item);
            items.Add(item);
        }

        var card = new Border
        {
            Child = new ScrollViewer { Content = list, MaxHeight = 360, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            Background = ink.Raised,
            BorderBrush = ink.Hairline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(5),
            BoxShadow = BoxShadows.Parse(ink.Dark ? "0 12 32 0 #47000000" : "0 12 32 0 #24000000"),
            MinWidth = Math.Max(anchor.Bounds.Width, 200),
            MaxWidth = 380,
            Opacity = 0,
            Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(110) }],
        };
        card.KeyDown += (_, e) =>
        {
            var at = items.FindIndex(i => i.IsFocused);
            switch (e.Key)
            {
                case Key.Down:
                    items[(at + 1) % items.Count].Focus(NavigationMethod.Directional);
                    break;
                case Key.Up:
                    items[at <= 0 ? items.Count - 1 : at - 1].Focus(NavigationMethod.Directional);
                    break;
                case Key.Escape:
                    Finish(null);
                    break;
                default:
                    return;
            }
            e.Handled = true;
        };
        popup.Child = card;
        popup.Closed += (_, _) =>
        {
            host.Children.Remove(popup);
            // El clic en una fila llega antes que el cierre: se espera una vuelta para saber si se eligió.
            Dispatcher.UIThread.Post(() => Finish(null));
        };
        host.Children.Add(popup);
        popup.IsOpen = true;
        card.Opacity = 1;
        // El foco entra a la lista (en la elegida) para que las flechas funcionen desde el principio.
        var selected = entries.Select((e, i) => (e, i)).FirstOrDefault(p => p.e.Selected);
        var start = items[selected.e is null ? 0 : selected.i];
        Dispatcher.UIThread.Post(() => start.Focus(), DispatcherPriority.Loaded);
    }

    static Viewbox Icon(string path, IBrush brush, double size) => new()
    {
        Width = size,
        Height = size,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(path),
            Stroke = brush,
            StrokeThickness = 2.2,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 24,
            Height = 24,
        },
    };
}
