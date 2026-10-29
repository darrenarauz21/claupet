using Avalonia.Controls;
using Avalonia.Threading;

namespace Claupet;

/// <summary>
/// Lo que hace en el equipo (captura, calendario, volumen, ventanas…), como en Windows: SystemActions entiende el pedido
/// y WindowsDesktop, MacDesktop o LinuxDesktop lo hacen. Cada capacidad pide permiso la primera vez, en la isla, la nubecita o un
/// cuadro; reiniciar y apagar se confirman siempre. Los recordatorios avisan a su hora con la app abierta, y si está
/// cerrada la abre el sistema (el Programador de tareas, launchd o systemd).
/// </summary>
sealed partial class TrayController
{
    /// <summary>Mira los recordatorios cada 15 s (ReminderStore).</summary>
    readonly DispatcherTimer _reminderTimer = new() { Interval = TimeSpan.FromSeconds(15) };

    void StartActions()
    {
        SystemActions.Desktop = OperatingSystem.IsWindows() ? new WindowsDesktop(WindowsUi.Create(WithoutClaupetAsync, canCrop: true))
            : OperatingSystem.IsMacOS() ? new MacDesktop(() => _popup.Clipboard, WithoutClaupetAsync)
            : OperatingSystem.IsLinux() ? new LinuxDesktop(() => _popup.Clipboard, WithoutClaupetAsync)
            : null;
        Answers.OpenTarget = SystemActions.OpenTarget;
        // Una copia de prueba no avisa los recordatorios: los avisa la app de siempre (y los daría por avisados).
        if (SystemActions.Desktop is null || Program.Args.Contains("--preview")) return;
        _reminderTimer.Tick += (_, _) => CheckReminders();
        _reminderTimer.Start();
        CheckReminders();
    }

    /// <summary>
    /// Si la respuesta pide una acción en el equipo, la hace (con su permiso: MayRunAsync) y devuelve lo que se dice:
    /// la frase de Claude, lo que falló o lo que la app sabe y Claude no. Si la acción averiguó algo (la batería, las
    /// notas, lo copiado), Claude responde otra vez con esos datos: restart avisa que empieza esa respuesta y onText
    /// recibe sus partes. Si trajo una imagen para mirar, la analiza. En el hilo de la interfaz.
    /// </summary>
    async Task<string> RunActionAsync(AssistantResult result, AssistantRequest request, bool island,
        Action<string>? onText, Action? restart, CancellationToken cancel)
    {
        if (result.Action is not { } action) return result.Text;
        if (!SystemActions.Supports(action.Kind)) return L.T("Eso todavía no lo puedo hacer en este sistema. (・_・;)");
        if (!await MayRunAsync(action, island, cancel)) return L.T("Está bien, no lo hago. (^_^)");
        if (cancel.IsCancellationRequested) return "";
        // Fuera del hilo de la interfaz: un programa del sistema que tarda (o un permiso que pregunta macOS) no la congela.
        var outcome = await Task.Run(() => SystemActions.RunAsync(action, palette => Dispatcher.UIThread.Post(() => SetPalette(palette))));
        if (outcome.Problem is { } problem) return $"{problem} (._.)";
        if (outcome.Look is { } look)
        {
            _ = AnalyzeAsync(look, outcome.Question ?? request.Prompt);
            return L.T("Déjame mirarla… (o_o)");
        }
        if (outcome.Data is { } data)
        {
            restart?.Invoke();
            var follow = await Answers.AskAsync(request with { Data = data, Engine = result.By?.Engine }, _settings, onText, cancel: cancel);
            return follow.Failed ? $"{follow.Error} (._.)" : follow.Text;
        }
        return outcome.Say ?? (result.Text.Length > 0 ? result.Text : L.T("¡Listo! (^_^)"));
    }

    /// <summary>
    /// El permiso de la capacidad (si todavía no tiene «Permitir siempre») y, para reiniciar o apagar, la confirmación
    /// de cada vez; se pregunta donde se conversa: la isla o la nubecita (si no, un cuadro).
    /// </summary>
    async Task<bool> MayRunAsync(AssistantAction action, bool island, CancellationToken cancel)
    {
        if (SystemActions.CapabilityOf(action) is { } capability && !Capabilities.Allowed(_settings, capability))
        {
            var choice = await AskChoiceAsync(island, L.T("¿Dejo que Claupet {0}?", Capabilities.Ask(capability)),
                L.T("Se lo pregunto la primera vez. Puede quitarlo en Claupet Console › Asistente › Permisos."),
                [("no", L.T("No")), ("always", L.T("Permitir siempre")), ("once", L.T("Solo esta vez"))], "always", cancel);
            if (choice == "always")
            {
                _settings.Permissions.Add(capability);
                SettingsStore.Save(_settings);
                PushSettings();
            }
            if (choice is not ("always" or "once")) return false;
        }
        if (SystemActions.Confirmation(action) is not { } confirm) return true;
        return await AskChoiceAsync(island, confirm.Question, "", [("no", L.T("No")), ("yes", confirm.Yes)], "yes", cancel) == "yes";
    }

    /// <summary>
    /// Pregunta algo de Claupet con botones; devuelve el Id elegido o null (se cerró sin elegir). En la isla espera hasta
    /// que se responda o se cancele el pedido.
    /// </summary>
    async Task<string?> AskChoiceAsync(bool island, string title, string detail, IReadOnlyList<(string Id, string Label)> options,
        string primary, CancellationToken cancel)
    {
        if (island && _island is { } isle)
        {
            var asked = isle.Choose(title, detail, options, primary);
            using (cancel.Register(() => Dispatcher.UIThread.Post(() => isle.CancelChoice(asked)))) return await asked;
        }
        if (_mascotWindow is { } mascot && _chatBubble is { IsVisible: true } bubble)
        {
            _director.SetMood(ChibiMove.Think);
            var choice = await bubble.Choose(mascot.ScreenBounds, title, options, primary);
            if (choice is not null) bubble.Think(mascot.ScreenBounds); // mientras hace lo que se pidió
            return choice;
        }
        return await Dialogs.Choose(detail.Length > 0 ? title + "\n\n" + detail : title, _theme, options, primary);
    }

    /// <summary>
    /// Esconde (sin cerrarlas) las ventanas de Claupet mientras corre work: la captura no las muestra. Se hacen
    /// transparentes en vez de ocultarse, para que no se muevan ni vuelvan a saludar.
    /// </summary>
    async Task WithoutClaupetAsync(Func<Task> work)
    {
        var shown = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var windows = new Window?[] { _mascotWindow, _chatBubble, _bubble, _island }.OfType<Window>().Where(w => w.IsVisible).ToList();
            foreach (var window in windows) window.Opacity = 0;
            return windows;
        });
        try
        {
            if (shown.Count > 0) await Task.Delay(250); // que el compositor ya las haya quitado
            await work();
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var window in shown) window.Opacity = 1;
            });
        }
    }

    /// <summary>
    /// Los recordatorios que llegaron (también los que pasaron con la app cerrada): un aviso, la mascota lo dice y, con
    /// la voz, lo lee. Lo que el sistema tenía programado para abrir la app ya no hace falta.
    /// </summary>
    void CheckReminders()
    {
        List<Reminder> due;
        try { due = ReminderStore.TakeDue(DateTime.Now); }
        catch (Exception ex) { ErrorLog.Write(ex); return; }
        foreach (var reminder in due)
        {
            var late = DateTime.Now - reminder.At > TimeSpan.FromMinutes(2);
            ShowNotice(new Notice(NoticeKind.Reminder, late ? L.T("Recordatorio (se le pasó)") : L.T("Recordatorio"), reminder.Text,
                late ? L.T("Era para el {0:dddd d, HH:mm}.", reminder.At) : reminder.At.ToString("t")));
            ReadAloudIfOn(L.T("Te recuerdo: {0}", reminder.Text));
            var id = reminder.Id;
            _ = Task.Run(() =>
            {
                try { SystemActions.Desktop?.CancelReminder(id); }
                catch (Exception ex) { ErrorLog.Write(ex); }
            });
        }
    }

    /// <summary>Da o quita el permiso de una capacidad (Ajustes › Asistente › Permisos).</summary>
    void ToggleGrant(string id)
    {
        if (!_settings.Permissions.Remove(id)) _settings.Permissions.Add(id);
        SettingsStore.Save(_settings);
    }
}
