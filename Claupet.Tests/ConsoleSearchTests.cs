using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
// El proyecto de pruebas también trae WinForms (nombres iguales a los de Avalonia).
using Border = Avalonia.Controls.Border;
using KeyEventArgs = Avalonia.Input.KeyEventArgs;
using TextBlock = Avalonia.Controls.TextBlock;
using Window = Avalonia.Controls.Window;

namespace Claupet.Tests;

/// <summary>La búsqueda de la Console: Ctrl+K en Ajustes y Ctrl+Shift+K en toda la Console.</summary>
public class ConsoleSearchTests
{
    static readonly SettingsPage Looks = new("apariencia", "Apariencia", "El color, la letra y la mascota.", null,
    [
        new(RowKind.Caption, "Mascota"),
        new(RowKind.Switch, "Mascota en el escritorio", "mascota", On: true),
        new(RowKind.Note, "Camina por la pantalla mientras trabaja.", Tone: RowTone.Muted),
        new(RowKind.Choice, "Tamaño", "tamano", "Grande"),
        new(RowKind.Input, "Clave de la API", "clave", "sk-secreta", Secret: true),
        new(RowKind.Links, "", Links: [new("Exportar el tema", "exportar"), new("Importar un tema", "importar")]),
        new(RowKind.Note, "Los temas propios se guardan en temas/."),
    ]);

    static readonly SettingsPage Extensions = new("extensiones", "Extensiones", "", null,
    [
        new(RowKind.Market, "", Market: [new("clima", "Clima", "El tiempo de su ciudad.", ["Habilidad"], "ext:clima")]),
    ]);

    static readonly SettingsPage Weather = new("ext:clima", "Clima", "", null, [new(RowKind.Switch, "Activa", "ext-on")], Parent: "extensiones");

    [Fact]
    public void Ajustes_da_cada_seccion_sus_encabezados_y_sus_filas_con_donde_estan()
    {
        var hits = ConsoleSearch.Settings([Looks, Extensions, Weather]);

        Assert.Contains(hits, h => h is { Page: "apariencia", Index: -1, Name: "Apariencia" } && h.Extra.Contains("letra") && h.Extra.Contains("temas/"));
        Assert.Contains(hits, h => h is { Name: "Mascota", Index: 0, Where: "Apariencia" });
        var mascot = Assert.Single(hits, h => h.Name == "Mascota en el escritorio");
        Assert.Equal((1, "Apariencia › Mascota"), (mascot.Index, mascot.Where));
        Assert.Contains("Camina por la pantalla", mascot.Extra); // la aclaración se busca en su fila
        Assert.DoesNotContain(hits, h => h.Name.StartsWith("Camina")); // y no sale sola
        Assert.Contains("Grande", Assert.Single(hits, h => h.Name == "Tamaño").Extra);
        Assert.DoesNotContain("sk-secreta", Assert.Single(hits, h => h.Name == "Clave de la API").Extra); // lo oculto no se busca
        Assert.Equal(5, Assert.Single(hits, h => h.Name == "Importar un tema").Index);

        // Una extensión lleva a su detalle, y el detalle dice de qué sección es.
        Assert.Contains(hits, h => h is { Name: "Clima", Page: "ext:clima", Index: -1, Where: "Extensiones" } && h.Extra.Contains("tiempo"));
        Assert.Contains(hits, h => h is { Name: "Activa", Where: "Extensiones › Clima" });
    }

    [Fact]
    public void Las_secciones_de_verdad_salen_todas()
    {
        var pages = SettingsPages.Build(new SettingsState());
        var hits = ConsoleSearch.Settings(pages);
        foreach (var page in pages) Assert.Contains(hits, h => h.Page == page.Id && h.Name == page.Title);
        Assert.True(hits.Count > pages.Count * 3, "también las filas, no solo las secciones");
    }

    static SearchPick Pick(string name, string where = "", string extra = "") => new("M0,0", name, where, () => { }, extra);

    [Fact]
    public void Busca_sin_tildes_ni_mayusculas_cada_palabra_y_primero_lo_que_esta_en_el_nombre()
    {
        SearchPick[] all = [Pick("Notificaciones", "Ajustes", "avisos de uso"), Pick("Avisos", "Ajustes"), Pick("Tamaño de la mascota", "Apariencia"), Pick("Mascota en el escritorio", "Apariencia")];

        Assert.Equal(["Tamaño de la mascota"], SearchPalette.Filter(all, "TAMANO").Select(p => p.Name));
        Assert.Equal(["Mascota en el escritorio"], SearchPalette.Filter(all, "escritorio mascota").Select(p => p.Name));
        Assert.Equal(["Tamaño de la mascota", "Mascota en el escritorio"], SearchPalette.Filter(all, "apariencia").Select(p => p.Name));
        // «avisos» está en el nombre de uno y en lo que no se ve de otro: primero el del nombre.
        Assert.Equal(["Avisos", "Notificaciones"], SearchPalette.Filter(all, "avisos").Select(p => p.Name));
        Assert.Equal(4, SearchPalette.Filter(all, "  ").Count);
    }

    [Fact]
    public void La_paleta_se_maneja_con_flechas_Intro_y_Esc_y_no_mezcla_busquedas() => Headless.Run(() =>
    {
        var chosen = new List<string>();
        SearchPick Go(string name) => new("M0,0", name, "", () => chosen.Add(name));
        var palette = new SearchPalette();
        var window = new Window { Width = 900, Height = 700, Content = palette };
        window.Show();
        void Press(Key key)
        {
            palette.Query.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = palette.Query });
            Headless.Frame(window);
        }
        try
        {
            var first = palette.Open(Ink.For(true), "Buscar", [Go("Uno"), Go("Dos"), Go("Tres")]);
            Headless.Frame(window);
            Assert.True(palette.IsOpen);

            palette.Query.Text = "d";
            Headless.Frame(window);
            Assert.Equal(["Dos"], palette.Shown.Select(p => p.Name));
            palette.Query.Text = "";
            Headless.Frame(window);
            Press(Key.Down);
            Press(Key.Enter);
            Assert.Equal(["Dos"], chosen);
            Assert.False(palette.IsOpen);

            // Lo que llega tarde de una búsqueda ya cerrada no se mezcla con la nueva.
            var second = palette.Open(Ink.For(true), "Buscar", [Go("Cuatro")]);
            Headless.Frame(window);
            palette.SetPicks(first, [Go("Viejo")]);
            Assert.Equal(["Cuatro"], palette.Shown.Select(p => p.Name));
            palette.SetPicks(second, [Go("Cuatro"), Go("Cinco")]);
            Assert.Equal(["Cuatro", "Cinco"], palette.Shown.Select(p => p.Name));

            Press(Key.Escape);
            Assert.False(palette.IsOpen);
            Assert.Equal(["Dos"], chosen);
        }
        finally
        {
            // Lo pendiente (el foco, el dibujo) se hace antes de cerrar: si no, la sesión de pruebas queda sin letras.
            Headless.Frame(window);
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public void Ir_a_una_fila_de_Ajustes_la_muestra_y_la_marca() => Headless.Run(() =>
    {
        var view = new SettingsView();
        var ink = Ink.For(false);
        view.Show(Looks, ink, rebuild: true);
        var window = new Window { Width = 900, Height = 300, Content = new ScrollViewer { Content = view } };
        window.Show();
        Headless.Frame(window);

        // La fila, la que lleva la aclaración (si se busca la aclaración) y el encabezado.
        var row = Assert.IsType<Border>(view.TargetOf(1));
        Assert.Same(row, view.TargetOf(2));
        Assert.IsType<TextBlock>(view.TargetOf(0));
        Assert.Null(view.TargetOf(99));

        var links = Assert.IsType<Border>(view.TargetOf(5));
        view.Reveal(5, ink);
        Assert.NotNull(links.Background); // marcada un momento con el acento
        window.Close();
        Dispatcher.UIThread.RunJobs();
    });
}
