namespace Claupet;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

/// <summary>
/// Las sesiones de la Terminal comparten contexto y conversan. Un mensaje de otra sesión (escribir_a_sesion) llega al buzón
/// de esta: si su agente está quieto, se le manda como un turno marcado con su origen; si trabaja, espera a que termine. Para
/// que dos agentes no reboten sin fin, después de MaxAutoMessages seguidos sin que la persona escriba, los siguientes no se
/// mandan solos: quedan arriba de la caja, con lo que se trae con «Enviar a…», y van con el próximo pedido de la persona.
/// «#sesión» en un pedido le agrega el contexto de esa sesión.
/// </summary>
sealed partial class TerminalPane
{
    /// <summary>Cuántos mensajes de otras sesiones entran solos seguidos, sin que la persona escriba.</summary>
    internal const int MaxAutoMessages = 3;

    readonly Border _contextBar = new() { IsVisible = false, Margin = new Thickness(12, 0, 12, 6) };
    FileSystemWatcher? _inboxWatcher;

    /// <summary>Para las pruebas: lo que se ve arriba de la caja como contexto para el próximo pedido (de quién).</summary>
    internal IReadOnlyList<string> ContextShown => _contextBar.IsVisible && _current is { } tab ? [.. tab.Context.Select(c => c.From)] : [];

    /// <summary>Empieza a mirar el buzón (una vez) y entrega lo que ya esperaba.</summary>
    void WatchInbox()
    {
        if (_inboxWatcher is null)
            try
            {
                Directory.CreateDirectory(SessionShare.Inbox);
                _inboxWatcher = new FileSystemWatcher(SessionShare.Inbox, "*.json") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
                _inboxWatcher.Created += (_, _) => Dispatcher.UIThread.Post(DeliverAll);
                _inboxWatcher.Renamed += (_, _) => Dispatcher.UIThread.Post(DeliverAll);
                _inboxWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { ErrorLog.Write(ex); }
        DeliverAll();
    }

    /// <summary>Los mensajes que esperan a alguna de estas pestañas: a su fila, y adentro si se puede.</summary>
    internal void DeliverAll()
    {
        foreach (var tab in _tabs.Where(t => t.Shell is null).ToList())
        {
            foreach (var message in SessionShare.Take(tab.Agent.Driver.CanvasKey)) tab.Waiting.Enqueue(message);
            Pump(tab);
        }
    }

    /// <summary>
    /// Entrega el siguiente mensaje que espera, si el agente está quieto: como un turno («Mensaje de la sesión X: …», con la
    /// nota de que viene de otra sesión). Pasado el tope, ya no entran solos: quedan para el próximo pedido de la persona.
    /// </summary>
    void Pump(Tab tab)
    {
        if (tab.Waiting.Count == 0) return;
        if (tab.AutoMessages >= MaxAutoMessages)
        {
            while (tab.Waiting.TryDequeue(out var held)) tab.Context.Add((held.FromTitle, held.Text));
            if (tab == _current) DrawContext();
            return;
        }
        if (!tab.Agent.Running || tab.Agent.Transcript.Busy) return;
        var message = tab.Waiting.Dequeue();
        tab.AutoMessages++;
        tab.Agent.Send(L.T("Mensaje de la sesión «{0}»: {1}", message.FromTitle, message.Text), hidden: SessionShare.OriginNote(message));
    }

    /// <summary>
    /// La nota con el contexto de otras sesiones para un pedido: las que nombra con #, y lo que se le trajo con «Enviar a…»
    /// o quedó del buzón (que se vacía al mandarlo). Null si no hay nada.
    /// </summary>
    internal string? SessionContext(Tab tab, string text)
    {
        var pieces = new List<string>();
        if (text.Contains('#'))
            foreach (var other in _tabs.Where(t => t.Shell is null && t != tab))
                if (System.Text.RegularExpressions.Regex.IsMatch(text, $@"(?<![\w#])#{System.Text.RegularExpressions.Regex.Escape(TabSlug(other))}(?![\w-])"))
                    pieces.Add(SessionShare.Describe(SessionShare.CardOf(other.Agent.Driver.CanvasKey, other.Agent.Driver.Name, other.Agent.Options, other.Agent.Transcript, [])));
        foreach (var (from, piece) in tab.Context) pieces.Add($"From session «{from}»:\n{piece}");
        tab.Context.Clear();
        if (tab == _current) DrawContext();
        return SessionShare.ContextNote(pieces);
    }

    /// <summary>Lo traído de otras sesiones para el próximo pedido de la pestaña que se ve, arriba de la caja (cada uno con ×).</summary>
    void DrawContext()
    {
        if (_creating || _current is not { Shell: null } tab || tab.Context.Count == 0)
        {
            _contextBar.IsVisible = false;
            return;
        }
        var list = new StackPanel { Spacing = 3 };
        var head = Ui.Text(L.T("Va con su próximo pedido"), 11.5, _ink.Tertiary, FontWeight.Medium);
        head.Margin = new Thickness(4, 0, 0, 2);
        list.Children.Add(head);
        foreach (var (from, text) in tab.Context.ToList())
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var line = Ui.Text(L.T("De «{0}»: {1}", from, text.ReplaceLineEndings(" ")), 12, _ink.Secondary);
            line.TextTrimming = TextTrimming.CharacterEllipsis;
            line.VerticalAlignment = VerticalAlignment.Center;
            ToolTip.SetTip(line, text.Length > 1500 ? text[..1500] + "…" : text);
            row.Children.Add(line);
            var item = (from, text);
            var remove = RunButton("×", () =>
            {
                tab.Context.Remove(item);
                DrawContext();
            });
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            list.Children.Add(new Border { Background = _ink.Raised, BorderBrush = _ink.Hairline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 4), Child = row });
        }
        _contextBar.Child = list;
        _contextBar.IsVisible = true;
    }

    /// <summary>«Enviar a…» en una respuesta: la lista de las otras pestañas de agente; la elegida la recibe para su próximo pedido.</summary>
    void SendTo(Control anchor, string text)
    {
        if (_current is not { } from) return;
        var others = _tabs.Where(t => t.Shell is null && t != from).ToList();
        if (others.Count == 0)
        {
            DropList.Show(anchor, _ink, [new DropList.Entry("", L.T("No hay otra sesión abierta a la cual enviarlo."))], _ => { });
            return;
        }
        DropList.Show(anchor, _ink, [.. others.Select((t, i) => new DropList.Entry(i.ToString(), $"{Title(t)} · {t.Project}"))], picked =>
        {
            if (picked is null || !int.TryParse(picked, out var i) || i >= others.Count) return;
            SendContext(from, others[i], text);
        });
    }

    /// <summary>Le deja a target lo que se le manda desde from: va con su próximo pedido y se ve arriba de su caja.</summary>
    internal void SendContext(Tab from, Tab target, string text)
    {
        target.Context.Add((Title(from), text));
        if (target == _current) DrawContext();
    }
}
