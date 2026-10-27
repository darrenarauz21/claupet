namespace Claupet;

static class Format
{
    public static string Reset(DateTimeOffset? resetsAt)
    {
        if (resetsAt is null) return "";
        var local = resetsAt.Value.LocalDateTime;
        var span = local - DateTime.Now;
        if (span <= TimeSpan.Zero) return L.T("Reinicia ahora");
        if (span.TotalHours < 24)
            return L.T("Reinicia en {0}", span.TotalHours >= 1
                ? $"{(int)span.TotalHours} h {span.Minutes} min"
                : $"{Math.Max(1, span.Minutes)} min");
        return L.T("Reinicia el {0:dddd}, {0:t}", local);
    }

    public static string Ago(DateTimeOffset? at)
    {
        if (at is null) return "";
        var span = DateTimeOffset.Now - at.Value;
        if (span.TotalMinutes < 1) return L.T("ahora");
        if (span.TotalMinutes < 60) return L.T("hace {0} min", (int)span.TotalMinutes);
        if (span.TotalHours < 24) return L.T("hace {0} h", (int)span.TotalHours);
        return L.T("hace {0} d", (int)span.TotalDays);
    }

    public static string Duration(TimeSpan span)
    {
        if (span.TotalSeconds < 60) return $"{Math.Max(0, (int)span.TotalSeconds)} s";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min {span.Seconds:00} s";
        return $"{(int)span.TotalHours} h {span.Minutes} min";
    }

    public static string Tokens(long n) => n switch
    {
        >= 1_000_000_000 => $"{n / 1e9:0.#} G",
        >= 1_000_000 => $"{n / 1e6:0.#} M",
        >= 1_000 => $"{n / 1e3:0.#} k",
        _ => n.ToString(),
    };

    public static string Breakdown(IEnumerable<BreakdownRow> rows) =>
        string.Join(" · ", rows.Where(r => r.Percent > 0).OrderByDescending(r => r.Percent).Select(r => $"{r.Name} {r.Percent:0} %"));

    /// <summary>«claude-opus-5-5» → «Opus 5.5»; «claude-haiku-4-5-20251001» → «Haiku 4.5». Lo que no se reconoce queda igual.</summary>
    public static string Model(string id)
    {
        var name = id.StartsWith("claude-", StringComparison.OrdinalIgnoreCase) ? id[7..] : id;
        var oneMillion = name.EndsWith("[1m]", StringComparison.OrdinalIgnoreCase);
        if (oneMillion) name = name[..^4];
        var parts = name.Split('-');
        if (parts[0].Length == 0 || !char.IsLetter(parts[0][0])) return id;
        var version = string.Join('.', parts.Skip(1).TakeWhile(p => p.Length is > 0 and <= 2 && p.All(char.IsDigit)));
        if (version.Length == 0) return id;
        var label = char.ToUpperInvariant(parts[0][0]) + parts[0][1..] + " " + version;
        return oneMillion ? label + " (1M)" : label;
    }

    /// <summary>«Opus 5.5 · esfuerzo xhigh · asesor Opus 5.5», o vacío si la sesión aún no respondió.</summary>
    public static string ModelLine(SessionInfo s)
    {
        if (s.Model is null) return "";
        var parts = new List<string> { Model(s.Model) };
        if (s.Effort is { } effort) parts.Add(L.T("esfuerzo {0}", effort));
        if (s.Advisor is { } advisor) parts.Add(L.T("asesor {0}", Model(advisor)));
        return string.Join(" · ", parts);
    }

    public static string Local(LocalTotals? t) => t switch
    {
        null => "",
        { Responses: 0 } => L.T("Hoy: sin actividad"),
        _ => L.T("Hoy: {0} respuestas · {1} tokens", t.Responses, Tokens(t.Output)),
    };
}
