namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

/// <summary>
/// Una pregunta que no se puede pasar por alto, dentro de la ventana: oscurece lo de atrás y ofrece dos caminos. El foco
/// empieza en el que no pierde nada (no), para que un Intro suelto no detenga nada; Esc o tocar afuera también es no.
/// </summary>
static class ConfirmCard
{
    public static Task<bool> Ask(Control anchor, Ink ink, string title, string text, string yes, string no)
    {
        if (TopLevel.GetTopLevel(anchor)?.Content is not Panel host) return Task.FromResult(true);
        var done = new TaskCompletionSource<bool>();
        var scrim = new Border { Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), Focusable = true };
        // La ventana puede ser una grilla: la cubre entera, no solo su primera casilla.
        Grid.SetRowSpan(scrim, 99);
        Grid.SetColumnSpan(scrim, 99);

        void Finish(bool answer)
        {
            host.Children.Remove(scrim);
            done.TrySetResult(answer);
        }

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = title, FontFamily = Ink.Display, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = ink.Text, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = text, FontFamily = Ink.Sans, FontSize = 13, Foreground = ink.Secondary, TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var stay = Button(no, ink.Pill, ink.Text, () => Finish(false));
        // En oscuro el rojo es claro: la letra va oscura, como en los otros botones de peligro.
        var go = Button(yes, ink.Danger, ink.Dark ? new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x13)) : Brushes.White, () => Finish(true));
        buttons.Children.Add(go);
        buttons.Children.Add(stay);
        panel.Children.Add(buttons);
        var card = new Border
        {
            Child = panel,
            Width = 420,
            Padding = new Thickness(20, 18),
            Margin = new Thickness(16),
            CornerRadius = new CornerRadius(14),
            Background = ink.Raised,
            BorderBrush = ink.Hairline,
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse(ink.Dark ? "0 14 34 0 #40000000" : "0 14 34 0 #24000000"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Tab queda dentro de la tarjeta, y lo que se toca en ella no cuenta como tocar afuera.
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        card.PointerPressed += (_, e) => e.Handled = true;
        scrim.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            Finish(false);
        };
        scrim.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Finish(false);
        };
        scrim.Child = card;
        host.Children.Add(scrim);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => stay.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
        return done.Task;
    }

    static Border Button(string text, IBrush background, IBrush foreground, Action onClick)
    {
        var label = Ui.Text(text, 13, foreground, FontWeight.SemiBold);
        var button = Ui.Press(label, onClick, (b, on) => b.Opacity = on ? 0.88 : 1, text);
        button.Background = background;
        button.Height = 34;
        button.Padding = new Thickness(14, 0);
        button.CornerRadius = new CornerRadius(999);
        return button;
    }
}
