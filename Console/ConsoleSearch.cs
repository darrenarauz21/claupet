namespace Claupet;

/// <summary>
/// Una cosa de Ajustes que se puede buscar: la sección (Index -1) o una de sus filas (su número en la página). Name es lo
/// que se ve; Where, dónde está («Apariencia › Mascota»); Extra, lo que también se busca sin verse (la descripción de la
/// sección, el valor elegido o la aclaración de la fila). Page: adonde lleva una tarjeta de extensión (su detalle).
/// </summary>
sealed record SettingsHit(string Page, int Index, string Name, string Where, string Extra = "");

/// <summary>Lo que se busca en Ajustes (Ctrl+K) y en toda la Console (Ctrl+Shift+K), sin dibujar nada (para las pruebas).</summary>
static class ConsoleSearch
{
    /// <summary>
    /// Las secciones de Ajustes y sus filas, en el orden de la lista: cada sección, sus encabezados y las filas con
    /// etiqueta (interruptores, opciones, datos, enlaces, cuadros, páginas), los enlaces sueltos, los temas y las
    /// extensiones. Las aclaraciones y las notas no salen solas: lo que dicen se busca en la fila de arriba o en la sección.
    /// </summary>
    public static IReadOnlyList<SettingsHit> Settings(IReadOnlyList<SettingsPage> pages)
    {
        var hits = new List<SettingsHit>();
        var seen = new HashSet<(string, string)>();
        void Add(SettingsHit hit)
        {
            if (hit.Name.Trim().Length > 0 && seen.Add((hit.Name.Trim(), hit.Where))) hits.Add(hit with { Name = hit.Name.Trim() });
        }

        foreach (var page in pages)
        {
            var parent = page.Parent is { } up ? pages.FirstOrDefault(p => p.Id == up)?.Title : null;
            var place = parent is null ? page.Title : $"{parent} › {page.Title}";
            var notes = string.Join(" ", page.Rows.Where(r => r.Kind == RowKind.Note).Select(r => r.Text));
            Add(new SettingsHit(page.Id, -1, page.Title, parent ?? L.T("Ajustes"), $"{page.Description} {notes}"));
            string? caption = null;
            for (var i = 0; i < page.Rows.Count; i++)
            {
                var row = page.Rows[i];
                var where = caption is null ? place : $"{place} › {caption}";
                switch (row.Kind)
                {
                    case RowKind.Caption:
                        caption = row.Text;
                        Add(new SettingsHit(page.Id, i, row.Text, place));
                        break;
                    case RowKind.Switch or RowKind.Choice or RowKind.Info or RowKind.Link or RowKind.Input or RowKind.Page:
                        // La aclaración que va debajo (una nota apagada) también se busca, en su fila.
                        var hint = i + 1 < page.Rows.Count && page.Rows[i + 1] is { Kind: RowKind.Note, Tone: RowTone.Muted } next ? next.Text : "";
                        Add(new SettingsHit(page.Id, i, row.Text, where, $"{(row.Secret ? "" : row.Value)} {hint}"));
                        break;
                    case RowKind.Links:
                        foreach (var link in row.Links ?? [])
                            Add(new SettingsHit(page.Id, i, link.Text, where));
                        break;
                    case RowKind.Themes:
                        foreach (var card in row.Cards ?? [])
                            if (!card.Creating) Add(new SettingsHit(page.Id, i, card.Name, where.Length > 0 ? where : L.T("Temas")));
                        break;
                    case RowKind.Market:
                        // Una extensión lleva a su detalle.
                        foreach (var card in row.Market ?? [])
                            Add(card.Page.Length > 0 && pages.Any(p => p.Id == card.Page)
                                ? new SettingsHit(card.Page, -1, card.Name, where, $"{card.Description} {string.Join(" ", card.Provides)}")
                                : new SettingsHit(page.Id, i, card.Name, where, $"{card.Description} {string.Join(" ", card.Provides)}"));
                        break;
                }
            }
        }
        return hits;
    }
}
