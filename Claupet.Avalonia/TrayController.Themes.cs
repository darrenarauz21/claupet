namespace Claupet;

/// <summary>
/// Los temas propios desde Claupet Console (Ajustes › Apariencia › Temas), aparte del color: el prompt del cuadro «Tema
/// nuevo» va a Claude con la calidad elegida, y mientras tanto la Console muestra su tarjeta animada (se puede cancelar);
/// el tema validado se guarda en temas/ y queda elegido. Cada tarjeta se usa, se exporta o se borra; un archivo exportado
/// se importa. Elegir un color o la tarjeta de Claupet quita el tema.
/// </summary>
sealed partial class TrayController
{
    /// <summary>Cómo terminó lo último (un error, o el modelo que se usó en lugar del pedido), para la nota bajo el cuadro.</summary>
    string? _themeStatus;
    bool _themeProblem;
    /// <summary>Lo pedido para el tema que se está creando; null, ninguno.</summary>
    string? _themeCreating;
    CancellationTokenSource? _themeCancel;

    bool IConsoleApp.Typed(string key, string value)
    {
        switch (key)
        {
            case SettingsPages.ThemeCreateKey: _ = CreateThemeAsync(value.Trim()); return true;
            case SettingsPages.ThemeImportKey: ImportTheme(value); return true;
        }
        if (!key.StartsWith(SettingsPages.ThemeExportKey, StringComparison.Ordinal)) return false;
        ThemeDone(CustomThemes.Export(key[SettingsPages.ThemeExportKey.Length..], value) is { } problem
            ? (problem, true) : (L.T("Tema exportado: {0}", Path.GetFileName(value)), false));
        return true;
    }

    /// <summary>Las acciones de las tarjetas y de la calidad; false si la clave no es de los temas.</summary>
    bool ThemeAction(string key)
    {
        if (key.StartsWith(SettingsPages.ThemeUseKey, StringComparison.Ordinal))
        {
            var id = key[SettingsPages.ThemeUseKey.Length..];
            SetCustomTheme(id.Length == 0 ? null : id);
            ThemeDone(null);
        }
        else if (key.StartsWith(SettingsPages.ThemeDeleteKey, StringComparison.Ordinal)) DeleteTheme(key[SettingsPages.ThemeDeleteKey.Length..]);
        else if (key == SettingsPages.ThemeCancelKey) _themeCancel?.Cancel();
        else if (key == SettingsPages.ThemeQualityKey)
            Choose(CustomThemes.Qualities.Select(q => (q.Id, QualityLabel(q))), CustomThemes.Quality(_settings.ThemeQuality).Id, id =>
            {
                _settings.ThemeQuality = id;
                SettingsStore.Save(_settings);
            });
        else return false;
        return true;
    }

    /// <summary>«Media · Opus»: la calidad y el primer modelo que prueba.</summary>
    static string QualityLabel((string Id, string Label, string[] Models) quality) =>
        $"{L.T(quality.Label)} · {AssistantModels.Label(quality.Models[0])}";

    string ThemeQualityText => QualityLabel(CustomThemes.Quality(_settings.ThemeQuality));

    async Task CreateThemeAsync(string prompt)
    {
        if (prompt.Length == 0 || _themeCreating is not null) return;
        _themeCreating = prompt;
        using var cancel = _themeCancel = new CancellationTokenSource();
        ThemeDone(null);
        ThemeResult result;
        try
        {
            CustomThemes.FontInstalled = Ink.Installed;
            CustomThemes.InstalledFonts = Ink.Families;
            result = await CustomThemes.CreateAsync(prompt, _settings, cancel.Token);
        }
        catch (Exception ex) { result = new(null, L.T("No se pudo crear el tema: {0}", ex.Message)); }
        finally
        {
            _themeCreating = null;
            _themeCancel = null;
        }
        if (result.Theme is not null) SetCustomTheme(result.Theme.Id);
        ThemeDone(result.Error is { } error ? (error, true) : result.Note is { } note ? (note, false) : null);
    }

    void ImportTheme(string path)
    {
        var result = CustomThemes.Import(path);
        if (result.Theme is not null) SetCustomTheme(result.Theme.Id);
        ThemeDone(result.Error is { } error ? (error, true) : null);
    }

    /// <summary>Borra el tema; si era el elegido, vuelve a la paleta.</summary>
    void DeleteTheme(string id)
    {
        if (_settings.CustomTheme == id) SetCustomTheme(null);
        ThemeDone(CustomThemes.Delete(id) ? null : (L.T("No se pudo borrar el tema."), true));
    }

    /// <summary>La nota bajo el cuadro (null: ninguna) y Ajustes al día, en la bandeja y en la Console.</summary>
    void ThemeDone((string Text, bool Problem)? status)
    {
        (_themeStatus, _themeProblem) = status is { } s ? (s.Text, s.Problem) : (null, false);
        PushSettings();
    }

    /// <summary>Elige el tema propio (null: ninguno, manda la paleta) y lo aplica en toda la app.</summary>
    void SetCustomTheme(string? id)
    {
        _settings.CustomTheme = id;
        SettingsStore.Save(_settings);
        Theme.SelectedCustom = id;
        ApplyTheme();
    }
}
