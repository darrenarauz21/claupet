namespace Claupet;

/// <summary>
/// Los temas propios desde Claupet Console (Ajustes › Apariencia): el prompt del cuadro «Crear un tema…» va a Claude, el
/// tema validado se guarda en temas/ y queda elegido como una paleta más; el enlace «Borrar el tema» vuelve a la terracota.
/// </summary>
sealed partial class TrayController
{
    /// <summary>Cómo va la creación («Creando…» o el error), para la nota bajo el cuadro.</summary>
    string? _themeStatus;
    bool _themeCreating;

    bool IConsoleApp.Typed(string key, string value)
    {
        if (key != SettingsPages.ThemeCreateKey) return false;
        _ = CreateThemeAsync(value.Trim());
        return true;
    }

    async Task CreateThemeAsync(string prompt)
    {
        if (prompt.Length == 0 || _themeCreating) return;
        _themeCreating = true;
        _themeStatus = L.T("Creando el tema con Claude…");
        PushSettings();
        try
        {
            CustomThemes.FontInstalled = Ink.Installed;
            var (theme, error) = await CustomThemes.CreateAsync(prompt, _settings);
            _themeStatus = error;
            if (theme is not null) SetPalette(theme.PaletteId);
        }
        catch (Exception ex) { _themeStatus = L.T("No se pudo crear el tema: {0}", ex.Message); }
        finally { _themeCreating = false; }
        PushSettings();
    }

    /// <summary>Borra el tema elegido y vuelve a la terracota.</summary>
    void DeleteTheme()
    {
        if (CustomThemes.IdOf(_settings.Palette) is not { } id) return;
        SetPalette(Theme.Palettes[0].Id);
        CustomThemes.Delete(id);
        _themeStatus = null;
        PushSettings();
    }
}
