namespace Claupet;

/// <summary>
/// Dónde abre el asistente las tareas que propone en Claude Code: la carpeta elegida en Ajustes o la suya propia,
/// ~/Claupet/Tareas. No va en Documentos porque suele estar en OneDrive, que sincronizaría todo lo que generen las tareas.
/// </summary>
static class TaskFolders
{
    public static string Assistant => Path.Combine(Account.Home, "Claupet", "Tareas");

    /// <summary>La elegida o, si no hay (o ya no existe), la del asistente.</summary>
    public static string For(AppSettings settings) =>
        settings.TaskFolder is { Length: > 0 } chosen && Directory.Exists(chosen) ? chosen : Assistant;

    /// <summary>Lo que dice su fila de Ajustes.</summary>
    public static string Label(AppSettings settings)
    {
        var folder = For(settings);
        return folder == Assistant ? L.T("La de Claupet") : Name(folder) is { Length: > 0 } name ? name : folder;
    }

    /// <summary>El nombre de la carpeta donde se abriría una tarea, para la nubecita.</summary>
    public static string Name(string? folder) => folder is null ? L.T("una carpeta") : Path.GetFileName(folder.TrimEnd('\\', '/'));

    /// <summary>La carpeta del asistente se crea recién al abrir una tarea en ella (no al mostrar los ajustes).</summary>
    public static void Prepare(string folder)
    {
        try { Directory.CreateDirectory(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
    }

    /// <summary>Elegir otra carpeta (en la lista de Ajustes).</summary>
    public const string Other = "…";

    /// <summary>La del asistente primero; después la elegida, las de las sesiones abiertas y la última de «Nueva sesión»; y elegir otra.</summary>
    public static IEnumerable<(string Id, string Label)> Choices(AppSettings settings, IEnumerable<SessionInfo>? sessions)
    {
        var folders = (sessions ?? []).Select(s => s.Cwd)
            .Prepend(settings.Launch.Folder ?? "").Prepend(settings.TaskFolder ?? "")
            .Where(f => f.Length > 0 && Directory.Exists(f) && !Account.SamePath(f, Assistant))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(8);
        return folders.Select(f => (f, f.Replace(Account.Home, "~")))
            .Prepend(("", L.T("La de Claupet ({0})", Assistant.Replace(Account.Home, "~"))))
            .Append((Other, L.T("Elegir otra carpeta…")));
    }
}

/// <summary>La cuenta de Claude Code que responde al asistente: la elegida en Ajustes o, si no, la de Uso.</summary>
static class AssistantAccounts
{
    public static string? ConfigDir(AppSettings settings) => settings.AssistantAccount ?? settings.Selected;

    public static string Label(AppSettings settings) =>
        settings.AssistantAccount is { } dir && settings.Accounts.FirstOrDefault(a => Account.SamePath(a.ConfigDir, dir)) is { } account
            ? account.Label : L.T("La elegida en Uso");

    public static IEnumerable<(string Id, string Label)> Choices(AppSettings settings) =>
        settings.Accounts.Select(a => (a.ConfigDir, a.Label)).Prepend(("", L.T("La elegida en Uso")));
}

/// <summary>
/// Lo que la mascota sabe del momento, para responder sobre el uso y las sesiones: el uso de cada cuenta y las sesiones
/// abiertas (título, cuenta, carpeta y estado). Sin correos ni tokens; la fecha y la hora van en el prompt de sistema.
/// </summary>
static class MascotContext
{
    public static string Build(AppSettings settings, IReadOnlyDictionary<string, UsageSnapshot> snapshots,
        IReadOnlyDictionary<string, Profile> profiles, IEnumerable<SessionInfo>? lastSessions)
    {
        var text = new System.Text.StringBuilder();
        foreach (var account in settings.Accounts)
        {
            if (!snapshots.TryGetValue(account.ConfigDir, out var snap) || snap.Limits.Count == 0) continue;
            profiles.TryGetValue(account.ConfigDir, out var profile);
            var limits = snap.Limits.Select(l => $"{l.Label} al {l.Percent:0} %" + (Format.Reset(l.ResetsAt) is { Length: > 0 } r ? $" ({r.ToLowerInvariant()})" : ""));
            text.AppendLine($"Cuenta «{account.Label}»{(profile?.Plan is { Length: > 0 } plan ? $" (plan {plan})" : "")}: {string.Join("; ", limits)}.");
        }
        var sessions = (lastSessions ?? []).Where(s => s.State != SessionState.Idle).Take(8).ToList();
        if (sessions.Count == 0) text.AppendLine("No hay sesiones de Claude Code activas.");
        foreach (var s in sessions)
        {
            var state = s.State switch
            {
                SessionState.Working => "trabajando" + (s.Action is { } a ? $" ({a})" : "") + (s.TodoTotal > 0 ? $", {s.TodoDone} de {s.TodoTotal} tareas" : ""),
                SessionState.Waiting => s.AwaitingAnswer ? "esperando su respuesta" : "esperando su permiso",
                SessionState.Failed => "falló: " + (s.ErrorText ?? "error de la API"),
                _ => "terminó " + Format.Ago(s.TurnEndedAt ?? s.StatusAt),
            };
            text.AppendLine($"Sesión «{s.DisplayTitle}» ({s.Account.Label}, carpeta {Path.GetFileName(s.Cwd.TrimEnd('\\', '/'))}): {state}.");
        }
        return text.ToString().TrimEnd();
    }
}
