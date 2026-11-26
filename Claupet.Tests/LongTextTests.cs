using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using A = Avalonia.Controls;

namespace Claupet.Tests;

/// <summary>
/// Los textos largos sin cargar con ellos: la respuesta que llega se arma sin copiarse entera con cada pedazo y se ve por
/// su final, los bloques largos se pliegan y el chat no vuelve a recibir la conversación entera con cada pedazo.
/// </summary>
public sealed class LongTextTests
{
    [Fact]
    public void LaRespuestaSeArmaPorPedazosSinPerderNada()
    {
        var block = new AgentBlock { Kind = AgentBlockKind.Say, Text = "Hola" };
        var expected = new System.Text.StringBuilder("Hola");
        for (var i = 0; i < 2000; i++)
        {
            var piece = $" pedazo {i}";
            block.Append(piece);
            expected.Append(piece);
            if (i % 250 == 0) Assert.Equal(expected.ToString(), block.Text); // se puede leer mientras llega
        }
        Assert.Equal(expected.ToString(), block.Text);
        Assert.Same(block.Text, block.Text); // leerlo dos veces no lo vuelve a armar
        block.Text = "otra";
        block.Append(" cosa");
        Assert.Equal("otra cosa", block.Text);
    }

    [Fact]
    public void MientrasLlegaSeVeSuFinal()
    {
        Assert.Equal("corta", LongText.LiveText("corta"));
        var lines = string.Join("\n", Enumerable.Range(1, 2000).Select(i => $"línea {i}"));
        var live = LongText.LiveText(lines);
        Assert.StartsWith("…\nlínea ", live); // desde un comienzo de línea
        Assert.EndsWith("línea 2000", live);
        Assert.True(live.Length <= LongText.LiveTail + 2);
    }

    [Fact]
    public void UnBloqueLargoSePliegaYUnoCortoNo() => Headless.Run(() =>
    {
        var code = string.Join("\n", Enumerable.Range(1, 500).Select(i => $"var x{i} = {i};"));
        var made = new List<string>();
        A.Control Make(string shown)
        {
            made.Add(shown);
            return new A.TextBlock { Text = shown };
        }
        Assert.IsType<A.TextBlock>(LongText.Foldable("corto", 40, 4000, Avalonia.Media.Brushes.Blue, Make));
        var folded = LongText.Foldable(code, 40, 4000, Avalonia.Media.Brushes.Blue, Make);
        Assert.Equal(40, made[^1].Split('\n').Length); // solo el comienzo se dibuja
        var texts = folded.GetLogicalDescendants().OfType<A.TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Mostrar todo · 500 líneas", texts);
    });

    [Fact]
    public void ElChatConservaSusMensajesCuandoSoloLlegaLaRespuesta() => Headless.Run(() =>
    {
        var pane = new ChatPane();
        var window = new A.Window { Width = 900, Height = 700, Content = pane };
        window.Show();
        try
        {
            var asked = new List<ConsoleMessage>();
            pane.Request += asked.Add;
            ChatTurn[] turns = [new(true, "pregunta de antes"), new(false, "respuesta de antes")];
            pane.Show(new ChatView("c1", "Chat", turns, null, false, "", false));
            pane.Show(new ChatView("c1", "Chat", [], "va llegando", true, "", false) { StreamOnly = true });
            Headless.Frame(window);
            // Las respuestas se arman por partes (Inlines): se lee una u otra.
            var texts = pane.GetLogicalDescendants().OfType<A.TextBlock>()
                .Select(t => t.Text is { Length: > 0 } text ? text : string.Concat(t.Inlines?.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text) ?? [])).ToList();
            Assert.Contains(texts, t => t.Contains("pregunta de antes"));
            Assert.Contains(texts, t => t.Contains("va llegando"));
            Assert.Empty(asked);

            // De otra conversación que no tiene: la pide entera.
            pane.Show(new ChatView("c2", "Otro", [], "x", true, "", false) { StreamOnly = true });
            Assert.Equal(ConsoleLink.ChatOpen, Assert.Single(asked).Type);
        }
        finally { window.Close(); }
    });
}
