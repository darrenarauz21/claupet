using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Claupet.Tests;

/// <summary>
/// Un texto largo en la Terminal: pegado entra a la caja como una marca («[Texto #1 · 240 líneas]») y vuelve entero al
/// enviar; en la conversación, el pedido largo se ve plegado y se despliega.
/// </summary>
public sealed class PastedTextTests : IDisposable
{
    readonly TempDir _dir = new();
    static readonly string Big = string.Join("\n", Enumerable.Range(1, 240).Select(i => $"línea {i} del registro"));

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void UnTextoLargoEntraComoMarcaYVuelveEnteroAlEnviar()
    {
        Assert.True(LongText.IsLongPaste(Big));
        Assert.True(LongText.IsLongPaste(new string('x', LongText.PasteChars + 1)));
        Assert.False(LongText.IsLongPaste("una línea\ny otra"));

        var marker = LongText.PasteMarker(1, Big);
        Assert.Equal("[Texto #1 · 240 líneas]", marker);
        Assert.Contains("caracteres", LongText.PasteMarker(2, new string('x', 2000))); // una sola línea: sus caracteres

        List<(string Marker, string Text)> pastes = [(marker, Big), (LongText.PasteMarker(2, "otro"), "otro")];
        Assert.Equal($"revisa esto:\n{Big}\ngracias", LongText.ExpandPastes($"revisa esto:\n{marker}\ngracias", pastes));
        // La marca que se borró de la caja no va.
        Assert.Equal("ya no está", LongText.ExpandPastes("ya no está", pastes));
    }

    [Fact]
    public void UnPedidoLargoSeVePlegado()
    {
        Assert.Null(LongText.Preview("un pedido corto", TerminalPane.AskPreviewLines, TerminalPane.AskPreviewChars));
        var preview = LongText.Preview(Big, TerminalPane.AskPreviewLines, TerminalPane.AskPreviewChars)!;
        Assert.Equal(TerminalPane.AskPreviewLines, preview.Split('\n').Length);
        Assert.EndsWith(" …", preview);
        // Una sola línea larguísima: se corta en AskPreviewChars.
        Assert.Equal(TerminalPane.AskPreviewChars + 2, LongText.Preview(new string('a', 50_000), TerminalPane.AskPreviewLines, TerminalPane.AskPreviewChars)!.Length);
        Assert.Equal(3, LongText.LineCount("a\nb\nc"));
        Assert.Equal(5, LongText.LineCount(Big, stop: 5)); // sin recorrer el resto
    }

    [Fact]
    public void EnLaConversacionSoloSeDibujaElComienzo() => Headless.Run(() =>
    {
        var pane = new TerminalPane();
        var window = new Window { Width = 1200, Height = 800, Content = pane };
        window.Show();
        try
        {
            var tab = new TerminalPane.Tab(new TerminalAgent(new AgentOptions(_dir.Path), a => a()), "cuenta");
            tab.Agent.Transcript.Ask(Big);
            pane.Add(tab);
            Headless.Frame(window);
            var texts = pane.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
            Assert.Contains("Mostrar todo · 240 líneas", texts);
            Assert.DoesNotContain(texts, t => t.Contains("línea 240 del registro")); // el final, plegado, no se dibuja
        }
        finally
        {
            Headless.Frame(window);
            pane.CloseAll();
            window.Close();
        }
    });
}
