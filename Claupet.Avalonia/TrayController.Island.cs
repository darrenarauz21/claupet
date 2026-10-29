using Avalonia.Threading;

namespace Claupet;

/// <summary>
/// La isla del borde de arriba, como en Windows: las sesiones con sus pasos, el chat (la misma conversación de la
/// nubecita y de la conversación rápida de Claupet Console), los archivos que se le sueltan, los permisos y las
/// preguntas de Claude Code (por el hook PermissionRequest) y el resumen de la semana.
/// </summary>
sealed partial class TrayController
{
    IslandWindow? _island;
    ApprovalServer? _approvals;
    CancellationTokenSource? _analyzing;
    ChatTarget? _islandChat;
    /// <summary>Los permisos que se respondieron «Siempre en esta sesión» (sesión, herramienta y destino).</summary>
    readonly HashSet<string> _alwaysAllow = new();
    /// <summary>El modelo de sus sesiones (el de la última que respondió), para el chip de la isla.</summary>
    string? _defaultModel;
    string? _lastAnalysis, _lastAnalysisFile;
    ActivitySummary? _activitySummary;

    /// <summary>La conversación de la isla: la rápida, con la voz de la isla y sus preguntas en la isla.</summary>
    ChatTarget IslandChat => _islandChat ??= new ChatTarget(_quick, AssistantVoice.Island, island: true, asking => ShowQuick(IslandChat, asking));

    /// <summary>La conversación rápida cambió: la isla y Claupet Console (si están) la vuelven a mostrar.</summary>
    void ShowQuick(ChatTarget chat, bool asking)
    {
        _island?.SetChat(_quick.Turns.ToList(), chat.Streaming, asking);
        ConsoleHost.QuickChanged(asking);
    }

    /// <summary>La isla está si se pidió en Ajustes; se le pasan el acento, la mascota, el modelo, el uso y las sesiones.</summary>
    void UpdateIsland()
    {
        if (!_settings.Island)
        {
            _approvals?.Dispose();
            _approvals = null;
            if (_island is { } old)
            {
                _island = null;
                old.DropApproval();
                old.Close();
            }
            return;
        }
        // Una copia de prueba (--preview) no responde los permisos: los sigue respondiendo la app de siempre.
        if (!Program.Args.Contains("--preview")) _approvals ??= new ApprovalServer(OnApprovalRequested);
        if (_island is null)
        {
            var island = _island = new IslandWindow();
            island.SessionClicked += s => { View.FocusSession(s.SessionId); ShowForm(); };
            island.TerminalClicked += s => Terminals.Focus(s.Pid);
            island.NewSessionClicked += () => _ = NewSessionAsync(null);
            island.SettingsClicked += () => _ = OpenConsoleAsync("isla");
            island.ModelPicked += m =>
            {
                _settings.IslandModel = m.Length == 0 ? null : m;
                SettingsStore.Save(_settings);
                ModelPicked(m);
                PushIslandModel();
                PushSettings();
            };
            island.NewChatClicked += () =>
            {
                _quickAsking?.Cancel();
                _quick.Clear();
                IslandChat.Show(false);
            };
            island.Asked += AskIsland;
            island.FilesDropped += files => _ = AnalyzeAsync(files[0]);
            island.CancelClicked += () =>
            {
                _quickAsking?.Cancel();
                _analyzing?.Cancel();
            };
            island.ResultAction += OnResultAction;
            island.OpenFileClicked += OpenInEditor;
            island.MicClicked += () =>
            {
                _micFromIsland = !(_voice?.Listening ?? false);
                _voice?.Toggle();
            };
            _ = ScanSessionsAsync();
        }
        _island.WhileBusy = _settings.IslandWhileBusy;
        _island.OpenOnHover = _settings.IslandHover;
        _island.MicEnabled = _voice is not null;
        _island.SetAccent(Theme.ForDark(true).Accent);
        _island.SetMascot(Mascots.IsChibi);
        PushIslandModel();
        PushIslandUsage();
        _island.SetChat(_quick.Turns.ToList(), IslandChat.Streaming, IslandChat.Asking);
        if (_lastSessions is { } sessions) _island.SetSessions(sessions);
    }

    /// <summary>Le pregunta en el chat de la isla (lo escrito o lo dicho con el micrófono).</summary>
    void AskIsland(string text)
    {
        _quickAsking?.Cancel();
        _quickAsking = new CancellationTokenSource();
        _ = AskChatAsync(IslandChat, text, _quickAsking.Token);
    }

    void PushIslandModel()
    {
        var label = _settings.IslandModel is { } model ? ModelLabel(model) : _defaultModel ?? AssistantModels.Label(null);
        var account = AssistantAccounts.ConfigDir(_settings) is { } dir
            ? _settings.Accounts.FirstOrDefault(a => Account.SamePath(a.ConfigDir, dir))?.Label ?? "" : "";
        _island?.SetModel(label, account, IslandModelChoices(), _settings.IslandModel ?? "");
    }

    void PushIslandUsage()
    {
        if (_island is null) return;
        var selected = _settings.Accounts.FirstOrDefault(a => _settings.Selected is not null && Account.SamePath(a.ConfigDir, _settings.Selected))
            ?? _settings.Accounts.FirstOrDefault();
        UsageSnapshot? snap = null;
        if (selected is not null) _snapshots.TryGetValue(selected.ConfigDir, out snap);
        _island.SetUsage(snap?.Session?.Percent, snap?.Weekly?.Percent);
    }

    /// <summary>Las sesiones nuevas: a la isla y, la primera vez, el modelo de la última que respondió (el de sus sesiones).</summary>
    void IslandSessions(IReadOnlyList<SessionInfo> sessions)
    {
        _island?.SetSessions(sessions);
        if (_defaultModel is null && sessions.Where(s => s.Model is not null).OrderByDescending(s => s.TurnEndedAt ?? s.StatusAt).FirstOrDefault() is { } recent)
        {
            _defaultModel = Format.Model(recent.Model!);
            PushIslandModel();
        }
    }

    /// <summary>
    /// Un permiso o una pregunta de Claude Code llegó por el hook: la isla muestra su tarjeta y espera.
    /// Lo que la persona elige vuelve a Claude Code; sin respuesta a tiempo, la tarjeta se va y pregunta la terminal.
    /// </summary>
    async Task<string?> OnApprovalRequested(ApprovalRequest request)
    {
        var key = $"{request.SessionId}|{request.Tool}|{request.Target}";
        if (!request.IsQuestion && _alwaysAllow.Contains(key)) return Approvals.Allow();
        if (_island is not { } island) return null;
        var title = (_lastSessions ?? []).FirstOrDefault(s => s.SessionId == request.SessionId)?.DisplayTitle
            ?? Path.GetFileName(request.Cwd.TrimEnd('\\', '/'));
        if (_mascotWindow is not null) _director.Gesture(ChibiMove.Think, 3000);
        var asked = island.AskApproval(request, title);
        var finished = await Task.WhenAny(asked, Task.Delay(TimeSpan.FromSeconds(Approvals.DecisionSeconds - 5)));
        if (finished != asked)
        {
            island.DropApproval();
            return null;
        }
        var answer = await asked;
        switch (answer?.Choice)
        {
            case IslandWindow.ApprovalChoice.Allow: return Approvals.Allow();
            case IslandWindow.ApprovalChoice.Always:
                _alwaysAllow.Add(key);
                return Approvals.Allow();
            case IslandWindow.ApprovalChoice.Deny: return Approvals.Deny();
            case IslandWindow.ApprovalChoice.Answered when answer.Picks is { } picks: return Approvals.Answer(request, picks);
            default: return null;
        }
    }

    /// <summary>
    /// Analiza un archivo (o una carpeta) con el modelo de la isla: la isla muestra los pasos reales y el resumen
    /// mientras se escribe; la mascota, si está, mira mientras lee y cuenta lo esencial en su nubecita.
    /// </summary>
    async Task AnalyzeAsync(string file, string? question = null)
    {
        _analyzing?.Cancel();
        var cancel = _analyzing = new CancellationTokenSource();
        var island = _island;
        island?.BeginAnalysis(file);
        if (_mascotWindow is not null) _director.SetMood(ChibiMove.Listen);
        AssistantResult result;
        try
        {
            result = await Assistant.AskAsync(new AssistantRequest
            {
                Prompt = question ?? "",
                Voice = AssistantVoice.Island,
                Model = _settings.IslandModel,
                ConfigDir = AssistantAccounts.ConfigDir(_settings),
                File = file,
                Chibi = Mascots.IsChibi,
            }, piece => Dispatcher.UIThread.Post(() => island?.AppendAnalysis(piece)),
                stage => Dispatcher.UIThread.Post(() => island?.SetAnalysisStage(stage)), cancel.Token);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            result = new AssistantResult("", null, null, null, ex.Message);
        }
        if (cancel.IsCancellationRequested)
        {
            island?.FinishAnalysis("", L.T("Cancelado."), null);
            _director.SetMood(null);
            return;
        }
        island?.FinishAnalysis(result.Text, result.Error, result.Model is { } m ? Format.Model(m) : null);
        if (result.Model is { } used && _settings.IslandModel is null)
        {
            _defaultModel = Format.Model(used);
            PushIslandModel();
        }
        _lastAnalysis = result.Failed ? null : result.Text;
        _lastAnalysisFile = file;
        // No lo pudo abrir (un Excel, por ejemplo): se lo encarga a una sesión en la carpeta del archivo.
        if (result.Task is { } task)
        {
            await OfferTaskAsync(task);
            return;
        }
        if (_mascotWindow is { } mascot)
        {
            var line = result.Failed ? $"{result.Error} (._.)" : island is null ? result.Text : result.Short ?? result.Text;
            ChatBubbleFor().Say(mascot.ScreenBounds, line);
            _director.SetMood(result.Failed ? null : ChibiMove.Talk);
            if (result.Failed) _director.Gesture(ChibiMove.Dizzy, 1500);
        }
    }

    /// <summary>Lo que se hace con el resultado de un análisis: preguntar más en el chat, copiarlo o seguir en una sesión.</summary>
    void OnResultAction(string action)
    {
        if (_lastAnalysis is not { } analysis || _lastAnalysisFile is not { } file) return;
        switch (action)
        {
            case "ask":
                _quick.Add(new ChatTurn(false, L.T("(Resumen de {0}) {1}", Path.GetFileName(file), analysis)));
                IslandChat.Show(false);
                _island?.ShowPage(IslandWindow.Page.Chat);
                break;
            case "copy":
                if (_island?.Clipboard is { } clipboard) _ = clipboard.SetTextAsync(analysis);
                break;
            case "session":
                try
                {
                    var folder = Directory.Exists(file) ? file : Path.GetDirectoryName(file);
                    Launcher.Start(new LaunchOptions
                    {
                        ConfigDir = AssistantAccounts.ConfigDir(_settings), Folder = folder,
                        Prompt = L.T("Revisa «{0}». Ya lo resumí así: {1} Dime qué ves y pregúntame qué hacer con él.", file, analysis.ReplaceLineEndings(" ")),
                        Model = _settings.Launch.Model, Effort = _settings.Launch.Effort, Advisor = _settings.Launch.Advisor,
                    });
                }
                catch (Exception ex) { _ = Dialogs.Info(L.T("No se pudo abrir la sesión: {0}", ex.Message), _theme); }
                break;
        }
    }

    /// <summary>
    /// Una tarea pedida en el chat (o un archivo que hay que encargar): la mascota la ofrece en su nubecita; sin
    /// mascota, la isla pregunta si se abre; sin isla, un cuadro.
    /// </summary>
    async Task OfferTaskAsync(TaskProposal task)
    {
        var folder = task.Folder ?? TaskFolders.For(_settings);
        if (_mascotWindow is { } mascot)
        {
            _pendingTask = task;
            var bubble = ChatBubbleFor();
            bubble.Say(mascot.ScreenBounds, "");
            bubble.Finish("", island: false, task.Prompt, TaskFolders.Name(folder));
            _director.SetMood(ChibiMove.Think);
            return;
        }
        var question = L.T("¿Abro Claude Code en {0} con esta tarea?\n\n«{1}»", L.T("«{0}»", TaskFolders.Name(folder)), task.Prompt);
        var open = _island is { } island
            ? await island.Choose(question, "", [("no", L.T("No")), ("yes", L.T("Sí"))], "yes") == "yes"
            : await Dialogs.Confirm(question, _theme);
        if (!open) return;
        try { LaunchTask(task, folder); }
        catch (Exception ex) { await Dialogs.Info(L.T("No se pudo abrir la sesión: {0}", ex.Message), _theme); }
    }

    /// <summary>Abre la tarea en una sesión de Claude Code en esa carpeta, con lo elegido la última vez.</summary>
    void LaunchTask(TaskProposal task, string folder)
    {
        TaskFolders.Prepare(folder);
        Launcher.Start(new LaunchOptions
        {
            ConfigDir = AssistantAccounts.ConfigDir(_settings), Folder = folder, Prompt = task.Prompt,
            Model = _settings.Launch.Model, Effort = _settings.Launch.Effort, Advisor = _settings.Launch.Advisor,
        });
    }

    /// <summary>Abre un archivo en VS Code (si está el comando code) o con el programa que el sistema tenga para él.</summary>
    static void OpenInEditor(string file)
    {
        try
        {
            var names = OperatingSystem.IsWindows() ? new[] { "code.cmd" } : ["code"];
            var code = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .SelectMany(d => names.Select(n => Path.Combine(d.Trim('"'), n))).FirstOrDefault(File.Exists);
            System.Diagnostics.ProcessStartInfo start;
            if (code is not null && OperatingSystem.IsWindows())
                start = new(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") { ArgumentList = { "/d", "/c", code, "-g", file }, CreateNoWindow = true };
            else if (code is not null) start = new(code) { ArgumentList = { "-g", file } };
            else if (OperatingSystem.IsWindows()) start = new(file) { UseShellExecute = true };
            else start = new(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { ArgumentList = { file } };
            if (!start.UseShellExecute) start.UseShellExecute = false;
            using var _ = System.Diagnostics.Process.Start(start);
        }
        catch (Exception) { }
    }

    /// <summary>
    /// Los lunes (o la primera vez que la app corre en la semana), el resumen de la semana pasada en la isla, una sola
    /// vez. now: mostrarlo ya (desde Ajustes), aunque ya se haya visto.
    /// </summary>
    void MaybeShowRecap(bool now)
    {
        if (_island is not { } island || _activitySummary is not { } summary) return;
        var thisMonday = WeekRecap.MondayOf(DateOnly.FromDateTime(DateTime.Today));
        if (!now && (!_settings.WeeklyRecap || _settings.RecapShown == thisMonday)) return;
        var recap = WeekRecap.For(summary, thisMonday.AddDays(-7)) ?? (now ? WeekRecap.For(summary, thisMonday) : null);
        if (recap is null) return;
        if (!now)
        {
            _settings.RecapShown = thisMonday;
            SettingsStore.Save(_settings);
        }
        island.ShowWeek(recap);
    }

    /// <summary>El hook de permisos y preguntas en la isla: listo si está en todas las carpetas de Claude Code.</summary>
    string ApprovalsText()
    {
        var dirs = SetupDirs().ToList();
        return dirs.Count > 0 && dirs.All(d => HookInstaller.ApprovalsStatus(d, ExePath) == HookInstaller.State.Installed) ? L.T("Listo") : L.T("Configurar…");
    }

    /// <summary>Agrega (o quita) el hook PermissionRequest en cada carpeta de Claude Code, con respaldo, después de preguntar.</summary>
    async Task SetupApprovalsAsync()
    {
        var dirs = SetupDirs().ToList();
        if (dirs.Count == 0) return;
        static string Name(string dir) => dir.Replace(Account.Home, "~");
        var lines = new List<string>();
        if (dirs.All(d => HookInstaller.ApprovalsStatus(d, ExePath) == HookInstaller.State.Installed))
        {
            if (!await Dialogs.Confirm(L.T("Los permisos y las preguntas ya se responden en la isla. ¿Quitar ese hook de Claude Code?"), _theme)) return;
            foreach (var dir in dirs)
            {
                try { HookInstaller.UninstallApprovals(dir); lines.Add(L.T("{0}: quitado.", Name(dir))); }
                catch (Exception ex) { lines.Add(L.T("{0}: no se pudo ({1}).", Name(dir), ex.Message)); }
            }
        }
        else
        {
            var list = string.Join(Environment.NewLine, dirs.Select(d => "• " + Name(d)));
            if (!await Dialogs.Confirm(
                    L.T("Se agrega el hook PermissionRequest al settings.json de Claude Code (antes se guarda un respaldo) en:")
                    + Environment.NewLine + Environment.NewLine + list + Environment.NewLine + Environment.NewLine
                    + L.T("Así los permisos y las preguntas de Claude Code se responden en la isla. Si la app está cerrada o nadie "
                    + "responde en 110 s, Claude Code pregunta en la terminal como siempre. ¿Continuar?"), _theme))
                return;
            foreach (var dir in dirs)
            {
                try
                {
                    var backup = HookInstaller.InstallApprovals(dir, ExePath);
                    lines.Add(backup is null ? L.T("{0}: listo.", Name(dir)) : L.T("{0}: listo (con respaldo).", Name(dir)));
                }
                catch (Exception ex) { lines.Add(L.T("{0}: no se pudo ({1}).", Name(dir), ex.Message)); }
            }
            lines.Add(L.T("Funciona en las sesiones de Claude Code que se abran desde ahora."));
        }
        PushSettings();
        await Dialogs.Info(string.Join(Environment.NewLine, lines), _theme);
    }

    /// <summary>Un interruptor de la isla: se guarda y la isla se rehace con lo nuevo.</summary>
    void FlipIsland(Action<AppSettings> flip)
    {
        flip(_settings);
        SettingsStore.Save(_settings);
        UpdateIsland();
        UpdateVoice(); // sin chat ni isla, el micrófono no tiene a quién hablarle
    }
}
