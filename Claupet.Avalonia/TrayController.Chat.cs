using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Claupet;

/// <summary>
/// El chat con la mascota (la nubecita), como en Windows: el clic del saludo abre el campo para escribir y responde el
/// asistente de Respuestas con el modelo de la mascota y lo que sabe del momento. Las tareas se abren en Claude Code;
/// las acciones en el equipo todavía no están en esta app (lo dice). Es la conversación rápida de Claupet Console y la
/// del chat de la isla.
/// </summary>
sealed partial class TrayController
{
    ChatBubbleWindow? _chatBubble;
    CancellationTokenSource? _asking;
    TaskProposal? _pendingTask;

    /// <summary>El modelo de la mascota: donde esta app no tiene el modelo local, en su lugar responde el primero de Claude.</summary>
    string MascotModel => _settings.MascotModel == AssistantModels.Local && !HasLocal ? AssistantModels.Choices[0].Id : _settings.MascotModel;

    /// <summary>Lo que la mascota sabe del momento (el uso y las sesiones abiertas), para responder sobre eso.</summary>
    string AssistantContext() => MascotContext.Build(_settings, _snapshots, _profiles, _lastSessions);

    string? WhoAnswered(AssistantResult result) => _settings.ShowWhoAnswered ? result.By?.Label : null;

    /// <summary>El uso de Claude dicho sin IA: cada límite con su porcentaje y cuándo se reinicia (null si aún no se sabe).</summary>
    string? UsageAnswer()
    {
        var parts = new List<string>();
        var known = _settings.Accounts.Where(a => _snapshots.TryGetValue(a.ConfigDir, out var s) && s.Limits.Count > 0).ToList();
        foreach (var account in known)
        {
            var snap = _snapshots[account.ConfigDir];
            var limits = string.Join(", ", snap.Limits.Select(l => L.T("{0} al {1:0} %", l.Label, l.Percent)
                + (Format.Reset(l.ResetsAt) is { Length: > 0 } r ? $" ({r.ToLowerInvariant()})" : "")));
            parts.Add(known.Count == 1 ? limits + "." : L.T("«{0}»: {1}.", account.Label, limits));
        }
        return parts.Count == 0 ? null : string.Join(" ", parts) + " (^_^)";
    }

    /// <summary>El clic del saludo abre el chat (el primero; los demás son para enojarla).</summary>
    void OnMascotPoked()
    {
        if (_settings.Chat && _director.Pokes == 1) OpenChat();
    }

    /// <summary>El campo para escribirle, en la nubecita, con la mascota escuchando.</summary>
    void OpenChat()
    {
        if (_mascotWindow is not { } mascot) return;
        _bubble.Hide();
        _asking?.Cancel(); // si estaba respondiendo, deja de esperar esa respuesta
        _pendingTask = null;
        ChatBubbleFor().AskInput(mascot.ScreenBounds, MascotModelChoices(), MascotModel);
        _director.SetMood(ChibiMove.Listen);
    }

    ChatBubbleWindow ChatBubbleFor()
    {
        if (_chatBubble is { } existing) return existing;
        var bubble = new ChatBubbleWindow();
        bubble.Sent += text => _ = AskMascotAsync(text);
        bubble.ModelPicked += id =>
        {
            _settings.MascotModel = id;
            SettingsStore.Save(_settings);
            ModelPicked(id);
            PushSettings();
        };
        bubble.TaskChosen += here => _ = OnTaskChosenAsync(here);
        bubble.IslandClicked += () => _island?.ShowPage(IslandWindow.Page.Chat);
        bubble.DoneTalking += () => _director.SetMood(_pendingTask is null ? null : ChibiMove.Think);
        bubble.Dismissed += () =>
        {
            _asking?.Cancel();
            _pendingTask = null;
            _director.SetMood(null);
        };
        return _chatBubble = bubble;
    }

    /// <summary>Le pregunta al asistente (con el modelo de la mascota) y va mostrando la respuesta en la nubecita.</summary>
    async Task AskMascotAsync(string text)
    {
        if (_mascotWindow is not { } mascot || _chatBubble is not { } bubble) return;
        _asking?.Cancel();
        var cancel = _asking = new CancellationTokenSource();
        bubble.Think(mascot.ScreenBounds, text);
        _director.SetMood(ChibiMove.Think);
        var request = new AssistantRequest
        {
            Prompt = text,
            Voice = AssistantVoice.Mascot,
            Model = MascotModel,
            ConfigDir = AssistantAccounts.ConfigDir(_settings),
            Context = AssistantContext(),
            History = _quick.Turns.ToList(),
            Chibi = Mascots.IsChibi,
            Expressive = Expressive,
        };
        _quick.Add(new ChatTurn(true, text));
        QuickUpdated();
        // Con la voz encendida, la nubecita sigue pensando hasta que la voz empieza: el texto sale con ella.
        var speaking = _settings.ReadAloud;
        void Stream(string piece) => Dispatcher.UIThread.Post(() =>
        {
            if (cancel.IsCancellationRequested || speaking) return;
            bubble.Append(piece);
            _director.SetMood(ChibiMove.Talk);
        });
        // «/comando …»: lo atiende la habilidad de una extensión, no el asistente.
        if (SkillCommand(text) is { } command)
        {
            var reply = await RunSkillCommandAsync(command, Stream, cancel.Token);
            if (cancel.IsCancellationRequested) return;
            if (reply.Length == 0) reply = L.T("Listo. (^_^)");
            _quick.Add(new ChatTurn(false, reply));
            QuickUpdated();
            ReadAloudIfOn(reply, () =>
            {
                if (cancel.IsCancellationRequested) return;
                bubble.Finish(reply, island: false);
                _director.SetMood(null);
            });
            return;
        }
        AssistantResult result;
        try { result = await Answers.AskAsync(request, _settings, Stream, cancel: cancel.Token); }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            result = new AssistantResult("", null, null, null, ex.Message);
        }
        if (cancel.IsCancellationRequested) return;

        if (result.Failed)
        {
            bubble.Finish($"{result.Error} (._.)", island: false, error: true);
            _director.SetMood(null);
            _director.Gesture(ChibiMove.Dizzy, 1500);
            return;
        }
        if (result.Task is { } task)
        {
            _quick.Add(new ChatTurn(false, L.T("(Propuse abrir una sesión de Claude Code con: {0})", task.Prompt)));
            QuickUpdated();
            _pendingTask = task;
            bubble.Finish("", island: false, task.Prompt, TaskFolders.Name(task.Folder ?? TaskFolders.For(_settings)));
            _director.SetMood(ChibiMove.Think);
            return;
        }
        // Si pidió algo en el equipo, lo hace (con su permiso); si trajo datos, la nubecita vuelve a pensar con ellos.
        var answer = await RunActionAsync(result, request, island: false, Stream, () =>
        {
            bubble.Think(mascot.ScreenBounds);
            _director.SetMood(ChibiMove.Think);
        }, cancel.Token);
        if (cancel.IsCancellationRequested) return;
        _quick.Add(new ChatTurn(false, answer, WhoAnswered(result)));
        QuickUpdated();
        ReadAloudIfOn(result, answer, () =>
        {
            if (cancel.IsCancellationRequested) return;
            if (speaking) _director.SetMood(ChibiMove.Talk);
            bubble.Finish(answer, island: _island is not null, who: WhoAnswered(result));
        });
    }

    /// <summary>La conversación rápida cambió desde la nubecita: la isla y Claupet Console, si están, la vuelven a mostrar.</summary>
    void QuickUpdated() => QuickTarget.Show(QuickTarget.Asking);

    /// <summary>La tarea que propuso: se abre en su carpeta (la del archivo, si vino de uno), en la de las tareas o en otra que se elija.</summary>
    async Task OnTaskChosenAsync(bool here)
    {
        if (_pendingTask is not { } task || _mascotWindow is not { } mascot || _chatBubble is not { } bubble) return;
        var folder = here ? task.Folder ?? TaskFolders.For(_settings) : null;
        if (folder is null)
        {
            bubble.HoldOpen = true;
            try { folder = await PickFolderAsync(bubble, L.T("Carpeta donde se abre la sesión de Claude Code con la tarea.")); }
            finally { bubble.HoldOpen = false; }
            if (folder is null) return;
            _settings.TaskFolder = folder;
            SettingsStore.Save(_settings);
            PushSettings();
        }
        _pendingTask = null;
        try
        {
            LaunchTask(task, folder);
            bubble.Say(mascot.ScreenBounds, L.T("¡Va! Abrí la sesión. Bailo mientras trabaja y te aviso al terminar \\(^o^)/"));
            _director.SetMood(null);
            _director.Gesture(ChibiMove.Cheer, 1200);
        }
        catch (Exception ex)
        {
            bubble.Finish(L.T("No pude abrir la sesión: {0} (._.)", ex.Message), island: false, error: true);
            _director.SetMood(null);
        }
    }

    /// <summary>Elegir una carpeta, empezando en la de las tareas; null si se cerró sin elegir.</summary>
    async Task<string?> PickFolderAsync(TopLevel top, string title)
    {
        IStorageFolder? start = null;
        try { start = await top.StorageProvider.TryGetFolderFromPathAsync(TaskFolders.For(_settings)); }
        catch (Exception) { } // la del asistente puede no existir todavía
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    /// <summary>La carpeta de las tareas elegida en Ajustes (vacía: la del asistente; «…»: elegir otra).</summary>
    async Task PickTaskFolderAsync(string folder)
    {
        if (folder == TaskFolders.Other)
        {
            _popup.HoldOpen = true;
            try { folder = await PickFolderAsync(_popup, L.T("Carpeta donde se abren las tareas que le pida a la mascota.")) ?? ""; }
            finally { _popup.HoldOpen = false; }
            if (folder.Length == 0) return;
        }
        _settings.TaskFolder = folder.Length == 0 ? null : folder;
        SettingsStore.Save(_settings);
        PushSettings();
    }

    void ToggleChat()
    {
        _settings.Chat = !_settings.Chat;
        SettingsStore.Save(_settings);
        if (!_settings.Chat) _chatBubble?.Dismiss(notify: true);
        UpdateVoice();
    }
}
