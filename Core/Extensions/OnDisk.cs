using System.Collections.Concurrent;

namespace Claupet;

/// <summary>
/// Las extensiones encendidas, leídas del disco. La Console es otro proceso y no tiene el ExtensionManager de la bandeja:
/// para sumar sus servidores MCP y sus especialistas a los agentes que abre, lee lo mismo que la bandeja (los ajustes,
/// los paquetes y las claves del usuario). No cambia nada: un paquete tocado solo se salta (la bandeja es quien lo apaga).
/// </summary>
static class ExtensionsOnDisk
{
    /// <summary>Los paquetes ya verificados en este proceso (id|digest): se verifica una vez por sesión.</summary>
    static readonly ConcurrentDictionary<string, bool> Verified = new();

    /// <summary>Las extensiones encendidas que traen esa ranura y el valor de cada uno de sus ajustes.</summary>
    public static IReadOnlyList<(InstalledExtension Extension, Func<string, string?> Value)> Enabled(string slot)
    {
        if (!OperatingSystem.IsWindows()) return [];
        try
        {
            var settings = SettingsStore.Load();
            var store = new ExtensionStore();
            if (!Directory.Exists(store.Root)) return [];
            var secrets = new DpapiSecretStore(DpapiSecretStore.DefaultPath);
            var list = new List<(InstalledExtension, Func<string, string?>)>();
            foreach (var ext in store.List())
            {
                if (!settings.Extensions.States.TryGetValue(ext.Id, out var state) || !state.Enabled || !ext.Manifest.Provides.Contains(slot)) continue;
                var key = $"{ext.Id}|{ext.Record.Digest}";
                if (!Verified.ContainsKey(key))
                {
                    if (store.Verify(ext) is { } problem)
                    {
                        ErrorLog.Write(new InvalidOperationException($"{ext.Id}: {problem}"));
                        continue;
                    }
                    Verified[key] = true;
                }
                var values = state.Values;
                list.Add((ext, k =>
                {
                    var spec = ext.Manifest.Settings.FirstOrDefault(s => s.Key == k);
                    if (spec is null) return null;
                    if (spec.Type == SettingSpec.Secret) return OperatingSystem.IsWindows() ? secrets.Get(ext.Id, k) : null;
                    return values.TryGetValue(k, out var v) ? v : spec.Default;
                }));
            }
            return list;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ErrorLog.Write(ex);
            return [];
        }
    }

    /// <summary>Los servidores MCP de las extensiones encendidas (lo que usa la Console).</summary>
    public static IReadOnlyList<McpServer> McpServers()
    {
        var list = new List<McpServer>();
        foreach (var (ext, value) in Enabled(ExtensionSlots.Mcp))
            foreach (var spec in ext.Manifest.Mcp)
            {
                if (list.Any(s => s.Name == spec.Name)) continue;
                try { list.Add(ExtensionMcp.Resolve(ext, spec, value, ProgramResolver.Find)); }
                catch (ExtensionUnavailableException ex) { ErrorLog.Write(ex); }
            }
        return list;
    }
}
