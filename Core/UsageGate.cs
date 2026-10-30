using System.Text.Json;

namespace Claupet;

/// <summary>
/// Cuándo se puede volver a preguntar el uso de una cuenta. El endpoint limita mucho (unas pocas consultas por token,
/// las de /usage de Claude Code incluidas) y su «retry-after» llega en 0 o no llega: entre consultas buenas pasa al
/// menos un minuto, y si responde 429 se espera cada vez más (2, 4, 8, 16 y luego 30 min) sin mostrarlo como error.
/// Mientras tanto se usa la última respuesta buena. Todo se guarda en uso.json, así que los reinicios (asistente,
/// idioma, instalación, actualización) no vuelven a preguntar.
/// </summary>
static class UsageGate
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(1);
    static readonly TimeSpan FirstPause = TimeSpan.FromMinutes(2);
    static readonly TimeSpan MaxPause = TimeSpan.FromMinutes(30);
    static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(1);

    static string? _filePath;

    /// <summary>Las pruebas lo apuntan a una carpeta temporal.</summary>
    internal static string FilePath
    {
        get => _filePath ?? DataFolder.In("uso.json");
        set => _filePath = value;
    }

    static readonly object Lock = new();

    sealed class Entry
    {
        /// <summary>La última respuesta buena, tal cual llegó.</summary>
        public string? Body { get; set; }
        public DateTimeOffset? FetchedAt { get; set; }
        public DateTimeOffset? PausedUntil { get; set; }
        /// <summary>429 seguidos: cada uno duplica la pausa.</summary>
        public int Strikes { get; set; }
    }

    /// <summary>
    /// Lo que hay si todavía no toca preguntar: los últimos datos buenos, o «en espera» si el servicio pidió esperar y
    /// nunca los hubo. null: se puede consultar.
    /// </summary>
    public static UsageSnapshot? Hold(string configDir, DateTimeOffset now)
    {
        var entry = Read(configDir);
        if (entry is null) return null;
        var paused = entry.PausedUntil > now;
        if (!paused && !(entry.FetchedAt + MinInterval > now)) return null;
        return Cached(entry) ?? (paused ? new UsageSnapshot { WaitingUntil = entry.PausedUntil } : null);
    }

    /// <summary>Guarda la respuesta buena; asked es cuando salió la consulta.</summary>
    public static void Success(string configDir, string body, DateTimeOffset asked) =>
        Update(configDir, e =>
        {
            e.Body = body;
            e.FetchedAt = asked;
            e.PausedUntil = null;
            e.Strikes = 0;
        });

    /// <summary>El servicio respondió 429: pausa la cuenta y devuelve lo que hay para mostrar mientras tanto.</summary>
    public static UsageSnapshot Limited(string configDir, TimeSpan? retryAfter, DateTimeOffset now)
    {
        var entry = Update(configDir, e =>
        {
            e.Strikes = Math.Min(e.Strikes + 1, 10);
            var pause = Pause(e.Strikes);
            if (retryAfter is { } r && r > pause) pause = r < MaxRetryAfter ? r : MaxRetryAfter;
            e.PausedUntil = now + pause;
        });
        return Cached(entry) ?? new UsageSnapshot { WaitingUntil = entry.PausedUntil };
    }

    internal static TimeSpan Pause(int strikes)
    {
        var pause = FirstPause * Math.Pow(2, Math.Max(0, strikes - 1));
        return pause < MaxPause ? pause : MaxPause;
    }

    static UsageSnapshot? Cached(Entry entry)
    {
        if (entry.Body is null) return null;
        try
        {
            var parsed = UsageClient.Parse(entry.Body);
            return new UsageSnapshot
            {
                Limits = parsed.Limits, Breakdown = parsed.Breakdown, ExtraUsageEnabled = parsed.ExtraUsageEnabled,
                FetchedAt = entry.FetchedAt,
            };
        }
        catch (JsonException) { return null; }
    }

    static Entry? Read(string configDir)
    {
        lock (Lock) return Load().GetValueOrDefault(Key(configDir));
    }

    /// <summary>Lee el archivo de nuevo cada vez: otra copia de la app (o la de antes de reiniciar) puede haberlo cambiado.</summary>
    static Entry Update(string configDir, Action<Entry> change)
    {
        lock (Lock)
        {
            var all = Load();
            var key = Key(configDir);
            var entry = all.GetValueOrDefault(key) ?? new Entry();
            change(entry);
            all[key] = entry;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(all));
                File.Move(temp, FilePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return entry;
        }
    }

    static Dictionary<string, Entry> Load()
    {
        try
        {
            if (File.Exists(FilePath)
                && JsonSerializer.Deserialize<Dictionary<string, Entry>>(Json.ReadShared(FilePath)) is { } all)
                return new(all, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    static string Key(string configDir) => Account.Normalize(configDir);
}
