using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Control = Avalonia.Controls.Control;
using Window = Avalonia.Controls.Window;
using TextBox = Avalonia.Controls.TextBox;
using Border = Avalonia.Controls.Border;
using TextBlock = Avalonia.Controls.TextBlock;
using ScrollViewer = Avalonia.Controls.ScrollViewer;

namespace Claupet.Tests;

/// <summary>Ajustes › Extensiones en la Console: las tarjetas, sus pestañas, la búsqueda, los filtros y sus botones.</summary>
public sealed class ExtensionMarketTests
{
    static readonly IReadOnlyList<ExtensionCard> Sample =
    [
        new("claupet.piper", "Voz Piper", "Voces naturales en 30 idiomas que corren en el equipo, sin internet.", [ExtensionSlots.TextToSpeech],
            "ext:claupet.piper", Official: true, Installed: "1.2.0", Latest: "1.3.0", Update: true, Downloads: 4120),
        new("claupet.ollama", "Ollama local", "Usa un modelo de Ollama como asistente de la mascota y la isla.", [ExtensionSlots.Assistant],
            "ext:claupet.ollama", Official: true, Installed: "2.0.1", Latest: "2.0.1", Downloads: 6800),
        new("claupet.pomodoro", "Pomodoro", "/foco 25 empieza un pomodoro; la mascota avisa al terminar.", [ExtensionSlots.Skill],
            "ext-cat:claupet.pomodoro", Official: true, Latest: "1.0.4", Downloads: 2300),
        new("claupet.deepgram", "Deepgram", "Dictado en la nube, rápido y con puntuación.", [ExtensionSlots.SpeechToText],
            "ext-cat:claupet.deepgram", Official: true, Latest: "0.9.2", Downloads: 1100, Percent: 42),
        new("claupet.gato-pixel", "Gato pixel", "Una mascota de 16 bits con 12 animaciones.", [ExtensionSlots.Pet],
            "ext-cat:claupet.gato-pixel", Official: true, Latest: "1.1.0", Downloads: 9500),
        new("claupet.realtime", "Voz en tiempo real", "Conversación por voz continua con el asistente.", [ExtensionSlots.SpeechToText, ExtensionSlots.TextToSpeech],
            "ext-cat:claupet.realtime", Official: true, Latest: "0.4.0", NeedsClaupet: "2.8.0", Downloads: 640),
        new("autor.notas", "Notas", "Una de otro autor, instalada desde un archivo.", [ExtensionSlots.Skill], "ext:autor.notas", Installed: "1.0.0", Enabled: false),
    ];

    static SettingsPage PageWith(IReadOnlyList<ExtensionCard> cards) => new(ExtensionsPanel.PageId, "Extensiones",
        "Habilidades, voces, mascotas y modelos. Las oficiales vienen firmadas por Claupet.", null,
        [
            new(RowKind.Market, "Extensiones", "ext-market", "Catálogo oficial verificado · hace 3 h", Place: RowPlace.Console, Market: cards,
                Icons:
                [
                    new(RowIconKind.Refresh, "Buscar actualizaciones", "ext-catalog"),
                    new(RowIconKind.Package, "Instalar desde archivo .claupet", "ext-install", File: "*.claupet"),
                    new(RowIconKind.Sliders, "Ranuras y API local", "ext-more"),
                ]),
        ]);

    static List<string> Texts(Control root) => root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible)
        .Select(t => t.Text ?? "").ToList();

    static Border Pressable(Control root, string name) => root.GetVisualDescendants().OfType<Border>()
        .First(b => Avalonia.Automation.AutomationProperties.GetName(b) == name && b.IsEffectivelyVisible);

    static void Click(Window window, Border target)
    {
        var center = target.TranslatePoint(new Avalonia.Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, Avalonia.Input.MouseButton.Left);
        window.MouseUp(center, Avalonia.Input.MouseButton.Left);
        Headless.Frame(window);
    }

    [Fact]
    public void LasPestanasLaBusquedaYLosBotonesHacenLoQueDicen() => Headless.Run(() =>
    {
        foreach (var dark in new[] { false, true })
        {
            var actions = new List<string>();
            var pages = new List<string>();
            var view = new SettingsView();
            view.Action += (key, _) => actions.Add(key);
            view.Navigate += pages.Add;
            var ink = Ink.For(dark);
            view.Show(PageWith(Sample), ink, rebuild: true);
            var window = new Window
            {
                Width = 980, Height = 1000, Background = ink.Surface,
                Content = new ScrollViewer { Content = new Border { Padding = new Avalonia.Thickness(32, 28), Child = view } },
            };
            window.Show();
            Headless.Frame(window);
            if (Environment.GetEnvironmentVariable("CLAUPET_MARKET_SHOT") is { Length: > 0 } shot)
                window.CaptureRenderedFrame()?.Save(shot.Replace(".png", dark ? "-oscuro.png" : "-claro.png"));

            // Explorar: todas, con el número de actualizaciones en su pestaña.
            var texts = Texts(window);
            foreach (var card in Sample) Assert.Contains(card.Name, texts);
            Assert.Contains("1", texts);
            Assert.Contains(texts, t => t.StartsWith("4") && t.EndsWith(" mil"));
            Assert.Contains("1.2.0 → 1.3.0", texts);
            Assert.Contains("2.8.0+", texts);
            Assert.Contains("42 %", texts);
            Assert.Contains("Apagada", texts);

            // Los botones de la tarjeta: instalar y actualizar piden a la bandeja; ajustes y la tarjeta llevan a su página.
            Click(window, Pressable(window, "Instalar"));
            Assert.Equal("ext-get:claupet.pomodoro", actions[^1]);
            Click(window, Pressable(window, "Actualizar a 1.3.0"));
            Assert.Equal("ext-get:claupet.piper", actions[^1]);
            Click(window, Pressable(window, "Ajustes de la extensión"));
            Assert.Equal("ext:claupet.ollama", pages[^1]);
            Click(window, Pressable(window, "Gato pixel"));
            Assert.Equal("ext-cat:claupet.gato-pixel", pages[^1]);
            Click(window, Pressable(window, "Ranuras y API local"));
            Assert.Equal("ext-more", actions[^1]);

            // Instaladas: solo las tres instaladas (también la de otro autor).
            Click(window, Pressable(window, "Instaladas"));
            texts = Texts(window);
            Assert.Contains("Notas", texts);
            Assert.Contains("Ollama local", texts);
            Assert.DoesNotContain("Pomodoro", texts);

            // Actualizaciones: la que tiene una nueva.
            Click(window, Pressable(window, "Actualizaciones"));
            texts = Texts(window);
            Assert.Contains("Voz Piper", texts);
            Assert.DoesNotContain("Ollama local", texts);

            // La búsqueda y el filtro por tipo, en Explorar.
            Click(window, Pressable(window, "Explorar"));
            var search = window.GetVisualDescendants().OfType<TextBox>().Single();
            search.Text = "mascota";
            Headless.Frame(window);
            texts = Texts(window);
            Assert.Contains("Gato pixel", texts);
            Assert.Contains("Ollama local", texts);
            Assert.DoesNotContain("Deepgram", texts);
            search.Text = "";
            Headless.Frame(window);
            Click(window, Pressable(window, ExtensionSlots.Label(ExtensionSlots.SpeechToText)));
            texts = Texts(window);
            Assert.Contains("Deepgram", texts);
            Assert.Contains("Voz en tiempo real", texts);
            Assert.DoesNotContain("Pomodoro", texts);

            // Lo nuevo de la bandeja (un avance de descarga) no le quita la pestaña ni el filtro.
            view.Show(PageWith([.. Sample.Select(c => c.Id == "claupet.deepgram" ? c with { Percent = 80 } : c)]), ink, rebuild: false);
            Headless.Frame(window);
            texts = Texts(window);
            Assert.Contains("80 %", texts);
            Assert.DoesNotContain("Pomodoro", texts);

            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public void SinNadaDiceQueNoHayYNoMuestraFiltros() => Headless.Run(() =>
    {
        var view = new SettingsView();
        var ink = Ink.For(false);
        view.Show(PageWith([]), ink, rebuild: true);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Headless.Frame(window);
        Assert.Contains("El catálogo todavía no tiene extensiones.", Texts(window));
        Assert.DoesNotContain("Todas", Texts(window));
        Click(window, Pressable(window, "Actualizaciones"));
        Assert.Contains("Todo está al día.", Texts(window));
        window.Close();
        Dispatcher.UIThread.RunJobs();
    });
}
