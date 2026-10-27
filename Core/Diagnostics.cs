using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Claupet;

/// <summary>
/// Resumen para saber por qué no salen avisos en otro equipo. Sin correos, tokens ni texto de las
/// conversaciones: solo carpetas, conteos y versiones.
/// </summary>
static class Diagnostics
{
    public static string Report(AppSettings settings, IReadOnlyList<SessionInfo>? sessions, int noticesShown)
    {
        var sb = new StringBuilder();
        var version = typeof(Diagnostics).Assembly.GetName().Version?.ToString(3) ?? "?";
        sb.AppendLine($"Claupet {version} · {RuntimeInformation.OSDescription} · {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine(L.T("Notificaciones: {0} · avisos mostrados desde que se abrió: {1}",
            settings.Notifications ? L.T("encendidas") : L.T("apagadas"), noticesShown));
        var envDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        sb.AppendLine($"CLAUDE_CONFIG_DIR: {(string.IsNullOrEmpty(envDir) ? L.T("(sin definir)") : envDir)}");

        sb.AppendLine(L.T("Carpetas de Claude Code:"));
        var dirs = new List<string>();
        try { dirs.AddRange(Directory.EnumerateDirectories(Account.Home, ".claude*")); } catch (Exception) { }
        dirs.AddRange(settings.Accounts.Select(a => a.ConfigDir));
        if (!string.IsNullOrEmpty(envDir)) dirs.Add(envDir);
        foreach (var dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d => d))
            sb.AppendLine("  " + DescribeDir(dir, settings));
        if (dirs.Count == 0) sb.AppendLine("  " + L.T("(ninguna)"));

        sb.AppendLine(L.T("Cuentas en la app: {0}", settings.Accounts.Count == 0 ? L.T("ninguna") : string.Join(", ", settings.Accounts.Select(a => $"{a.Label} ({a.ConfigDir.Replace(Account.Home, "~")})"))));

        if (sessions is null) sb.AppendLine(L.T("Sesiones leídas por la app: todavía ninguna lectura"));
        else
        {
            var states = sessions.GroupBy(s => s.State).Select(g => $"{g.Key} {g.Count()}");
            sb.AppendLine(L.T("Sesiones leídas por la app: {0}", sessions.Count) + (sessions.Count == 0 ? "" : " (" + string.Join(", ", states) + ")"));
        }

        sb.AppendLine(L.T("Eventos del hook: {0}", File.Exists(NoticeDetector.EventsPath) ? L.T("hay archivo de eventos") : L.T("ninguno recibido")));
        return sb.ToString();
    }

    static string DescribeDir(string dir, AppSettings settings)
    {
        var name = dir.Replace(Account.Home, "~");
        if (!Directory.Exists(dir)) return L.T("{0}: no existe", name);

        var credentials = Credentials.Describe(dir);
        var ignored = settings.Ignored.Any(i => Account.SamePath(i, dir)) ? " · " + L.T("quitada de la lista") : "";

        var sessionsDir = Path.Combine(dir, "sessions");
        string sessions;
        string? claudeVersion = null;
        if (!Directory.Exists(sessionsDir)) sessions = L.T("sin carpeta sessions");
        else
        {
            var files = Directory.EnumerateFiles(sessionsDir, "*.json").ToList();
            var alive = 0;
            foreach (var file in files)
            {
                try
                {
                    using var doc = JsonDocument.Parse(Json.ReadShared(file));
                    var r = doc.RootElement;
                    var pid = r.TryGetProperty("pid", out var p) && p.TryGetInt32(out var value) ? value : 0;
                    if (pid != 0 && SessionMonitor.IsAlive(pid, Json.Str(r, "procStart"))) alive++;
                    claudeVersion ??= Json.Str(r, "version");
                }
                catch (Exception) { }
            }
            sessions = L.T("{0} registradas, {1} vivas", files.Count, alive);
        }

        var hook = L.T("sin settings.json");
        var settingsFile = Path.Combine(dir, "settings.json");
        if (File.Exists(settingsFile))
        {
            try
            {
                var text = Json.ReadShared(settingsFile);
                hook = HookInstaller.NamesApp(text) && text.Contains("--hook")
                    ? L.T("con hook") : L.T("sin hook");
            }
            catch (Exception) { hook = L.T("settings.json ilegible"); }
        }

        var projects = Directory.Exists(Path.Combine(dir, "projects")) ? L.T("sí") : L.T("no");
        return L.T("{0}: credenciales {1} · sesiones {2} · projects {3} · {4}", name, credentials, sessions, projects, hook)
            + (claudeVersion is null ? "" : $" · Claude Code {claudeVersion}") + ignored;
    }
}

/// <summary>Los errores inesperados de la app, en errores.log junto a la configuración (los últimos 200 KB).</summary>
static class ErrorLog
{
    public static string FilePath => DataFolder.In("errores.log");

    public static void Write(Exception? error)
    {
        if (error is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 200_000) File.Delete(FilePath);
            File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} {error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception) { }
    }
}
