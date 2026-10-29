namespace Claupet;

/// <summary>
/// Las teclas, como en la app WinForms (por ahora solo en Windows): los atajos globales (Ctrl+Alt+Espacio abre el chat
/// y se puede grabar otro; Ctrl+Alt+A, T, N y S van al permiso pendiente, traen la terminal, abren la isla y silencian),
/// la tecla Copilot y la combinación grabada para hablar, que la app se queda con un gancho de teclado. Una copia de
/// prueba (--preview) no los toma: los tiene la app de siempre, con los mismos ajustes.
/// </summary>
sealed partial class TrayController
{
    Hotkeys? _hotkeys;
    KeyHook? _keyHook;
    /// <summary>Con qué se empezó a hablar (la tecla Copilot o la combinación grabada), para decir cómo se termina.</summary>
    string? _voiceTrigger;

    /// <summary>Esta app toma las teclas: en Windows y sin --preview.</summary>
    static bool OwnsKeys => OperatingSystem.IsWindows() && !Program.Args.Contains("--preview");

    /// <summary>La tecla para hablar es una combinación grabada (la vigila KeyHook, no el sondeo de PushToTalk).</summary>
    bool TalkIsCombo => OperatingSystem.IsWindows() && !MicKeys.IsHold(_settings.MicKey) && KeyCombo.TryParse(_settings.MicKey, out _);

    /// <summary>Los atajos y el gancho, al día con el chat, la isla, el micrófono y la tecla Copilot.</summary>
    void UpdateKeys()
    {
        if (!OperatingSystem.IsWindows()) return;
        UpdateHotkeys();
        UpdateKeyHook();
    }

    /// <summary>Los atajos de teclado: con la isla o el chat encendidos.</summary>
    void UpdateHotkeys()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!OwnsKeys || !_settings.Island && !_settings.Chat)
        {
            _hotkeys?.Dispose();
            _hotkeys = null;
            return;
        }
        if (_hotkeys is not null) return;
        _hotkeys = new Hotkeys();
        _hotkeys.Add(KeyCombo.TryParse(_settings.ChatKey, out var chat) ? chat : KeyCombo.CtrlAlt("Space"), L.T("Abrir el chat"), FocusChat);
        _hotkeys.Add("A", L.T("Ir al permiso o la pregunta pendiente"), () => _island?.ShowPending());
        _hotkeys.Add("T", L.T("Traer la terminal de la sesión"), () =>
        {
            var session = (_lastSessions ?? []).Where(s => s.State is SessionState.Waiting or SessionState.Working).FirstOrDefault()
                ?? (_lastSessions ?? []).OrderByDescending(s => s.TurnEndedAt ?? s.StatusAt).FirstOrDefault();
            if (session is not null) Terminals.Focus(session.Pid);
        });
        _hotkeys.Add("N", L.T("Abrir o plegar la isla"), () => _island?.ToggleOpen());
        _hotkeys.Add("S", L.T("Silenciar o volver a sonar"), () => _island?.ToggleMute());
    }

    /// <summary>
    /// Las teclas que la app se queda: la Copilot (con el chat o la isla) y la de hablar, si se grabó una combinación. La
    /// Copilot habla si hay micrófono; si no, abre el chat.
    /// </summary>
    void UpdateKeyHook()
    {
        if (!OperatingSystem.IsWindows()) return;
        var combos = new List<KeyCombo>();
        if (OwnsKeys && _settings.CopilotKey && (_settings.Chat || _settings.Island)) combos.Add(KeyCombo.Copilot);
        if (OwnsKeys && _voice is not null && TalkIsCombo && KeyCombo.TryParse(_settings.MicKey, out var talk) && !combos.Contains(talk))
            combos.Add(talk);
        if (combos.Count == 0)
        {
            _keyHook?.Dispose();
            _keyHook = null;
            return;
        }
        if (_keyHook is null)
        {
            _keyHook = new KeyHook();
            _keyHook.Changed += OnHookKey;
        }
        _keyHook.Combos = combos;
    }

    void OnHookKey(KeyCombo combo, bool down)
    {
        if (_voice is { } voice)
        {
            _voiceTrigger = combo == KeyCombo.Copilot ? "Copilot" : combo.Label;
            if (down) voice.ComboDown();
            else voice.ComboUp();
        }
        else if (down) FocusChat(); // la Copilot, sin micrófono
    }

    /// <summary>Otro atajo del chat: los atajos se vuelven a registrar.</summary>
    void SetChatKey(string combo)
    {
        if (OperatingSystem.IsWindows()) _hotkeys?.Dispose();
        _hotkeys = null;
        _settings.ChatKey = combo;
        SettingsStore.Save(_settings);
        UpdateKeys();
    }

    /// <summary>El atajo del chat (y la tecla Copilot sin micrófono): el campo de la isla o la nubecita de la mascota.</summary>
    void FocusChat()
    {
        // Para escribir hay que estar al frente, y Windows no siempre deja a una app de fondo.
        if (OperatingSystem.IsWindows() && ((Avalonia.Controls.Window?)_island ?? _mascotWindow)?.TryGetPlatformHandle()?.Handle is { } handle)
            Foreground.Take(handle);
        if (_island is { } island) island.FocusChat();
        else if (_settings.Chat && _mascotWindow is not null) OpenChat();
    }

    const string RecordChoice = "•grabar";

    /// <summary>Las opciones de una tecla: las de siempre, la grabada (si es otra) y, donde se puede grabar, «Grabar…».</summary>
    static IEnumerable<(string Id, string Label)> KeyChoices(IEnumerable<(string Id, string Label)> usual, string current, string record)
    {
        var list = usual.ToList();
        if (!list.Any(c => c.Id == current) && current.Length > 0) list.Add((current, KeyNames.Label(current)));
        if (OperatingSystem.IsWindows()) list.Add((RecordChoice, record));
        return list;
    }

    /// <summary>«Grabar…»: espera la tecla o la combinación y, si se eligió una, la guarda.</summary>
    async Task RecordKeyAsync(bool talk, Action<string> save)
    {
        if (!OperatingSystem.IsWindows()) return;
        _popup.HoldOpen = true;
        try
        {
            if (await KeyRecorderWindow.RecordAsync(_theme, talk, talk ? L.T("Tecla para hablar") : L.T("Atajo del chat")) is { } combo) save(combo);
        }
        finally { _popup.HoldOpen = false; }
    }

    async Task ShowShortcutsAsync()
    {
        if (!OperatingSystem.IsWindows()) return;
        var lines = new List<string>();
        foreach (var s in _hotkeys?.List ?? [])
            lines.Add(s.Registered ? $"{s.Keys}: {s.What}" : L.T("{0}: {1} (en uso por otra app)", s.Keys, s.What));
        if (_settings.Mic)
            lines.Add(MicKeys.IsHold(_settings.MicKey) || !TalkIsCombo
                ? L.T("Mantener {0}: hablarle a la mascota o a la isla", TalkKeyLabel)
                : L.T("{0}: hablarle (un toque, o mantenerla)", MicKeys.Label(_settings.MicKey)));
        if (_keyHook?.Combos.Contains(KeyCombo.Copilot) == true && !(_settings.Mic && _settings.MicKey == KeyNames.Copilot))
            lines.Add(_voice is not null ? L.T("Tecla Copilot: hablarle (un toque, o mantenerla)") : L.T("Tecla Copilot: abrir el chat"));
        if (lines.Count == 0) lines.Add(L.T("Los atajos funcionan con la isla o el chat encendidos."));
        _popup.HoldOpen = true;
        try { await Dialogs.Info(string.Join(Environment.NewLine, lines), _theme); }
        finally { _popup.HoldOpen = false; }
    }

    void StopKeys()
    {
        if (!OperatingSystem.IsWindows()) return;
        _hotkeys?.Dispose();
        _hotkeys = null;
        _keyHook?.Dispose();
        _keyHook = null;
    }
}
