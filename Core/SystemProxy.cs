using System.Net;
using Microsoft.Win32;

namespace Claupet;

/// <summary>
/// El proxy de la red de este momento, leído en cada conexión: el de .NET se fija la primera vez que se
/// usa y no ve un proxy que se activa después (al pasar a una red que lo necesita con la app ya abierta).
/// Manda HTTPS_PROXY (o ALL_PROXY); si no está, en Windows el proxy manual de Opciones de Internet. La
/// configuración automática (PAC) no se lee. Claude Code no mira el proxy de Windows, solo HTTPS_PROXY:
/// se lo pasa la app con ForClaude.
/// </summary>
sealed class SystemProxy : IWebProxy
{
    public static readonly SystemProxy Instance = new();

    const string InternetSettings = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    /// <summary>Un HttpClient que sale por el proxy de cada momento.</summary>
    public static HttpClient Client(TimeSpan timeout) =>
        new(new SocketsHttpHandler { Proxy = Instance }) { Timeout = timeout };

    /// <summary>El proxy para HTTPS, o null si se va directo.</summary>
    public static Uri? Current()
    {
        var variable = Environment.GetEnvironmentVariable("HTTPS_PROXY") ?? Environment.GetEnvironmentVariable("https_proxy")
            ?? Environment.GetEnvironmentVariable("ALL_PROXY") ?? Environment.GetEnvironmentVariable("all_proxy");
        if (!string.IsNullOrWhiteSpace(variable)) return Parse(variable);
        if (!OperatingSystem.IsWindows()) return null;

        using var key = Registry.CurrentUser.OpenSubKey(InternetSettings);
        if (key?.GetValue("ProxyEnable") is not int enabled || enabled == 0) return null;
        if (key.GetValue("ProxyServer") is not string server) return null;
        // «host:puerto» para todo, o «http=host:puerto;https=host:puerto;…» por protocolo.
        var entries = server.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var chosen = entries.FirstOrDefault(e => e.StartsWith("https=", StringComparison.OrdinalIgnoreCase))
            ?? entries.FirstOrDefault(e => e.StartsWith("http=", StringComparison.OrdinalIgnoreCase))
            ?? entries.FirstOrDefault(e => !e.Contains('='));
        return chosen is null ? null : Parse(chosen[(chosen.IndexOf('=') + 1)..]);
    }

    /// <summary>El valor de HTTPS_PROXY para Claude Code («http://host:puerto»), o null si va directo.</summary>
    public static string? ForClaude() => Current()?.GetLeftPart(UriPartial.Authority);

    static Uri? Parse(string value) =>
        Uri.TryCreate(value.Contains("://") ? value.Trim() : "http://" + value.Trim(), UriKind.Absolute, out var uri) ? uri : null;

    public ICredentials? Credentials { get; set; }

    public Uri? GetProxy(Uri destination) => IsBypassed(destination) ? null : Current();

    /// <summary>Va directo: el propio equipo, lo de NO_PROXY y las excepciones del proxy de Windows.</summary>
    public bool IsBypassed(Uri host) => host.IsLoopback || Exceptions().Any(rule => Matches(host.Host, rule));

    static IEnumerable<string> Exceptions()
    {
        var variable = Environment.GetEnvironmentVariable("NO_PROXY") ?? Environment.GetEnvironmentVariable("no_proxy") ?? "";
        foreach (var rule in variable.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return rule;
        if (!OperatingSystem.IsWindows()) yield break;
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettings);
        if (key?.GetValue("ProxyOverride") is not string overrides) yield break;
        foreach (var rule in overrides.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return rule;
    }

    /// <summary>«&lt;local&gt;» (nombres sin punto), «*», «*.dominio» o «.dominio», «10.*» o el nombre exacto.</summary>
    static bool Matches(string host, string rule)
    {
        const StringComparison Ignore = StringComparison.OrdinalIgnoreCase;
        if (rule == "<local>") return !host.Contains('.');
        if (rule == "*") return true;
        if (rule.StartsWith('*') || rule.StartsWith('.'))
        {
            var domain = rule.TrimStart('*').TrimStart('.');
            return host.Equals(domain, Ignore) || host.EndsWith("." + domain, Ignore);
        }
        if (rule.EndsWith('*')) return host.StartsWith(rule[..^1], Ignore);
        return host.Equals(rule, Ignore);
    }
}
