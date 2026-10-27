using System.Diagnostics;

namespace Claupet;

/// <summary>
/// Renueva la sesión vencida de una cuenta sin tocar sus credenciales (para no pisar las de Claude Code):
/// corre una consulta mínima de Claude Code (Haiku, sin herramientas, sin el CLAUDE.md ni sesión
/// guardada) y Claude Code renueva el token él mismo. «claude auth status» no lo renueva (comprobado). La
/// app lo hace al abrirse (hasta lograrlo con conexión) y con «Actualizar» sobre la cuenta que se ve.
/// </summary>
static class SessionRenewal
{
    static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Renueva las sesiones vencidas de las cuentas que pide wanted y deja en results su consulta nueva.
    /// Devuelve true si no había conexión para renovarlas (conviene reintentar más tarde).
    /// </summary>
    public static async Task<bool> RenewExpiredAsync(IReadOnlyList<Account> accounts, UsageSnapshot[] results, Func<Account, bool> wanted)
    {
        var expired = Enumerable.Range(0, accounts.Count).Where(i => results[i].SessionExpired && wanted(accounts[i])).ToList();
        if (expired.Count == 0) return false;
        if (!await Connectivity.IsOnlineAsync())
        {
            foreach (var i in expired) results[i] = Problem(L.T("Sesión vencida: sin conexión para renovarla."));
            return true;
        }
        await Task.WhenAll(expired.Select(async i =>
        {
            var problem = await RunAsync(accounts[i]);
            var fresh = problem is null ? await UsageClient.FetchAsync(accounts[i]) : null;
            // Si sigue vencida, el token de renovación ya no sirve: hay que iniciar sesión de nuevo.
            results[i] = fresh is { SessionExpired: false } ? fresh
                : Problem(problem ?? L.T("Sesión vencida: abra Claude Code con esta cuenta e inicie sesión."));
        }));
        return false;
    }

    static UsageSnapshot Problem(string message) => new() { Error = message, SessionExpired = true };

    /// <summary>La consulta mínima con la cuenta; null si Claude Code terminó, o lo que falló.</summary>
    static async Task<string?> RunAsync(Account account)
    {
        if (Assistant.FindClaude() is not { } claude) return L.T("Sesión vencida: no encontré Claude Code para renovarla.");
        var start = Assistant.ClaudeStart(claude, account.ConfigDir,
            ["-p", "--safe-mode", "--no-session-persistence", "--model", "haiku", "--tools", "", "--system-prompt", "Responde solo: ok"]);
        using var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (Exception ex) { return L.T("Sesión vencida: no se pudo abrir Claude Code ({0}).", ex.Message); }
        await process.StandardInput.WriteAsync("ok");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var limit = new CancellationTokenSource(Limit);
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException)
        {
            Assistant.Kill(process);
            return L.T("Sesión vencida: Claude Code no respondió a tiempo al renovarla.");
        }
        await Task.WhenAll(output, errors);
        return null;
    }
}
