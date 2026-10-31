namespace Claupet.Tests;

/// <summary>La cuenta de cada sesión: qué cuentas se ofrecen y cómo dice que están.</summary>
public sealed class SessionAccountTests
{
    static Account SignedIn(TempDir dir, string name)
    {
        dir.Write($"{name}/.credentials.json", "{}");
        return new Account { Label = name, ConfigDir = dir[name] };
    }

    static UsageSnapshot Usage(double session, double weekly = 10, DateTimeOffset? weeklyReset = null) => new()
    {
        Limits =
        [
            new LimitRow("session", "Sesión", session, null, DateTimeOffset.Now.AddHours(2)),
            new LimitRow("weekly_all", "Semana", weekly, null, weeklyReset ?? DateTimeOffset.Now.AddDays(3)),
        ],
    };

    [Fact]
    public void SoloOfreceLasCuentasConSesionYPrimeroLasQueTienenMargen()
    {
        using var dir = new TempDir();
        var full = SignedIn(dir, "llena");
        var free = SignedIn(dir, "libre");
        var outside = new Account { Label = "sin-sesion", ConfigDir = dir["sin-sesion"] };
        var settings = new AppSettings { Accounts = [full, outside, free] };
        var snapshots = new Dictionary<string, UsageSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            [full.ConfigDir] = Usage(100),
            [free.ConfigDir] = Usage(40),
        };

        var offered = AssistantAccounts.Available(settings, snapshots).ToList();

        Assert.Equal([free.ConfigDir, full.ConfigDir], offered.Select(o => o.Id));
        Assert.StartsWith("libre · ", offered[0].Label);
    }

    [Fact]
    public void DiceSiEstaAlLimiteVencidaOSinSesion()
    {
        using var dir = new TempDir();
        var account = SignedIn(dir, "trabajo");
        var snapshots = new Dictionary<string, UsageSnapshot>(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(AccountReady.Ready, AssistantAccounts.StatusOf(account, snapshots).State);
        snapshots[account.ConfigDir] = Usage(30, weekly: 100);
        Assert.Equal(AccountReady.AtLimit, AssistantAccounts.StatusOf(account, snapshots).State);
        // Un límite lleno que ya reinició no cuenta.
        snapshots[account.ConfigDir] = Usage(30, weekly: 100, weeklyReset: DateTimeOffset.Now.AddMinutes(-1));
        Assert.Equal(AccountReady.Ready, AssistantAccounts.StatusOf(account, snapshots).State);
        snapshots[account.ConfigDir] = Usage(30).WithError("401", sessionExpired: true);
        Assert.Equal(AccountReady.Expired, AssistantAccounts.StatusOf(account, snapshots).State);
        Assert.Equal(AccountReady.SignedOut, AssistantAccounts.StatusOf(new Account { Label = "x", ConfigDir = dir["x"] }, snapshots).State);
    }
}
