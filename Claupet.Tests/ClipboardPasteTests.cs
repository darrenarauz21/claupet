using Avalonia.Controls;
using Avalonia.Threading;
using TextBox = Avalonia.Controls.TextBox;
using TopLevel = Avalonia.Controls.TopLevel;
using Window = Avalonia.Controls.Window;

namespace Claupet.Tests;

/// <summary>Pegar archivos copiados en una caja de texto. Cambian ClipboardPaste.Copied, que es estático.</summary>
[Collection("Assistant.External")]
public sealed class ClipboardPasteTests
{
    static void WithBox(IReadOnlyList<string> copied, Action<TextBox> test) => Headless.Run(() =>
    {
        ClipboardPaste.Install();
        var previous = ClipboardPaste.Copied;
        ClipboardPaste.Copied = _ => Task.FromResult(copied);
        var box = new TextBox();
        var window = new Window { Content = box };
        window.Show();
        try { test(box); }
        finally
        {
            ClipboardPaste.Copied = previous;
            window.Close();
        }
    });

    [Fact]
    public void LosArchivosCopiadosEntranConSuRuta() => WithBox([@"C:\a.txt", @"C:\mis cosas\b.pdf"], box =>
    {
        box.Text = "ver  ahora";
        box.CaretIndex = 4;
        box.Paste();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(@"ver C:\a.txt ""C:\mis cosas\b.pdf"" ahora", box.Text);
    });

    [Fact]
    public void LaRutaReemplazaLoSeleccionado() => WithBox([@"C:\a.txt"], box =>
    {
        box.Text = "uno dos";
        box.SelectionStart = 4;
        box.SelectionEnd = 7;
        box.Paste();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(@"uno C:\a.txt", box.Text);
    });

    [Fact]
    public void UnaCajaDeSoloLecturaNoCambia() => WithBox([@"C:\a.txt"], box =>
    {
        box.Text = "fijo";
        box.IsReadOnly = true;
        box.Paste();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("fijo", box.Text);
    });

    [Fact]
    public void SinArchivosSePegaElTexto() => WithBox([], box =>
    {
        var clipboard = TopLevel.GetTopLevel(box)!.Clipboard!;
        clipboard.SetTextAsync("texto copiado").GetAwaiter().GetResult();
        box.Paste();
        for (var i = 0; i < 50 && box.Text != "texto copiado"; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        Assert.Equal("texto copiado", box.Text);
    });

    [Fact]
    public async Task EnLaCajaDeUnAgenteLasImagenesVanConElPedido()
    {
        using var dir = new TempDir();
        var png = Path.Combine(dir.Path, "foto.png");
        await File.WriteAllBytesAsync(png, [1, 2, 3]);
        var images = new List<AgentImage>();
        var text = await TerminalPane.FilesForComposerAsync([png, @"C:\mis cosas\b.pdf", @"C:\no-existe.png"], image =>
        {
            images.Add(image);
            return $"[Imagen #{images.Count}] ";
        });
        Assert.Equal(@"[Imagen #1] ""C:\mis cosas\b.pdf"" C:\no-existe.png ", text);
        Assert.Equal("image/png", Assert.Single(images).MediaType);
    }
}
