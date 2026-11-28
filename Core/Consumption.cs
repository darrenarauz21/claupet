namespace Claupet;

/// <summary>
/// Lo que se gastó: respuestas y tokens de cada tipo, con el mismo significado para todos los agentes.
/// - Input: la entrada nueva, sin la que vino de caché. CacheRead y CacheWrite: la leída de caché y la que se escribió en
///   ella. Lo que entró en total es AllInput. (Codex y Gemini cuentan la caché dentro de su entrada: se resta.)
/// - Output: lo que generó el modelo, con el razonamiento; Reasoning es la parte de Output que fue razonamiento (solo la
///   separan Codex, Gemini y OpenCode).
/// - Cost: dólares, solo si el agente los escribe (OpenCode). Premium: solicitudes premium de Copilot (su «cost» no son
///   dólares). Ninguno se estima: si el agente no lo escribe, queda en null.
/// </summary>
sealed class UsageSums
{
    public int Responses { get; set; }
    public long Input { get; set; }
    public long CacheRead { get; set; }
    public long CacheWrite { get; set; }
    public long Output { get; set; }
    public long Reasoning { get; set; }
    public double? Cost { get; set; }
    public double? Premium { get; set; }

    public long AllInput => Input + CacheRead + CacheWrite;

    public void Add(UsageSums o)
    {
        Responses += o.Responses;
        Input += o.Input;
        CacheRead += o.CacheRead;
        CacheWrite += o.CacheWrite;
        Output += o.Output;
        Reasoning += o.Reasoning;
        if (o.Cost is { } cost) Cost = (Cost ?? 0) + cost;
        if (o.Premium is { } premium) Premium = (Premium ?? 0) + premium;
    }

    public UsageSums Copy()
    {
        var copy = new UsageSums();
        copy.Add(this);
        return copy;
    }

    /// <summary>Lo que se mide en el gráfico: los tokens de salida, los de entrada (todos) o las respuestas.</summary>
    public double Of(UsageMetric metric) => metric switch
    {
        UsageMetric.Input => AllInput,
        UsageMetric.Responses => Responses,
        _ => Output,
    };
}

enum UsageMetric { Output, Input, Responses }

enum UsagePeriod { Today, Days7, Days30, Month }

/// <summary>Lo gastado en una hora por un modelo de un agente, en una sesión y un proyecto.</summary>
sealed record UsageRecord(string Agent, string Account, string? Session, string Model, string? Provider, string? Project,
    DateTimeOffset Hour, UsageSums Sums);

/// <summary>Un límite de uso que el agente escribe (Claude Code por su cuenta, Codex en sus sesiones).</summary>
sealed record UsageLimit(string Agent, string Account, string Label, double Percent, DateTimeOffset? ResetsAt);

/// <summary>Una fila de la tabla por modelo: sus sumas y su tendencia (la salida en 14 tramos del periodo).</summary>
sealed record UsageModelRow(string Agent, string Model, string? Provider, UsageSums Sums, double[] Trend);

sealed record UsageAgentRow(string Agent, UsageSums Sums, int Sessions, string? TopModel);

sealed record UsageProjectRow(string Project, UsageSums Sums);

/// <summary>
/// El consumo de un periodo, ya filtrado y sumado para Ajustes › Consumo. Slots: los tramos del gráfico (horas de hoy o
/// días); Heat: las respuestas de los últimos 7 días por día de la semana (lunes primero) y hora local.
/// </summary>
sealed class UsageReport
{
    public const int TrendPoints = 14;

    public required UsagePeriod Period { get; init; }
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public UsageSums Totals { get; } = new();
    public int Sessions { get; set; }
    public List<UsageModelRow> Models { get; } = [];
    public List<UsageAgentRow> Agents { get; } = [];
    public List<UsageProjectRow> Projects { get; } = [];
    /// <summary>El comienzo de cada tramo del gráfico.</summary>
    public List<DateTimeOffset> Slots { get; } = [];
    /// <summary>Por agente y por modelo («agente|modelo»), lo de cada tramo.</summary>
    public Dictionary<string, UsageSums[]> ByAgentSlot { get; } = [];
    public Dictionary<string, UsageSums[]> ByModelSlot { get; } = [];
    public int[,] Heat { get; } = new int[7, 24];
    /// <summary>Los agentes que aparecen en el periodo (antes de apagar ninguno), para los filtros.</summary>
    public List<string> AgentsSeen { get; } = [];

    public bool HasCost => Totals.Cost is not null;
    public bool HasPremium => Totals.Premium is not null;

    /// <summary>Dónde empieza el periodo y cuántos tramos lleva el gráfico (horas de hoy; si no, días).</summary>
    public static (DateTimeOffset From, int Slots, bool Hours) Range(UsagePeriod period, DateTimeOffset now)
    {
        var local = now.ToLocalTime();
        var today = new DateTimeOffset(local.Date, local.Offset);
        return period switch
        {
            UsagePeriod.Today => (today, 24, true),
            UsagePeriod.Days7 => (today.AddDays(-6), 7, false),
            UsagePeriod.Days30 => (today.AddDays(-29), 30, false),
            _ => (today.AddDays(1 - local.Day), local.Day, false),
        };
    }

    /// <summary>
    /// Suma los registros del periodo. off: los agentes apagados en los filtros. claudeAccount: solo esa cuenta de Claude
    /// Code (los otros agentes no tienen cuentas de Claude); null, todas.
    /// </summary>
    public static UsageReport Build(IReadOnlyList<UsageRecord> records, UsagePeriod period, DateTimeOffset now,
        IReadOnlySet<string>? off = null, string? claudeAccount = null)
    {
        var (from, slots, hours) = Range(period, now);
        var report = new UsageReport { Period = period, From = from, To = now };
        for (var i = 0; i < slots; i++) report.Slots.Add(hours ? from.AddHours(i) : from.AddDays(i));
        var models = new Dictionary<string, (UsageModelRow Row, UsageSums Sums)>();
        var agents = new Dictionary<string, (UsageSums Sums, HashSet<string> Sessions)>();
        var projects = new Dictionary<string, UsageSums>(StringComparer.OrdinalIgnoreCase);
        var sessions = new HashSet<string>();
        var heatFrom = new DateTimeOffset(now.ToLocalTime().Date, now.ToLocalTime().Offset).AddDays(-6);
        var span = (now - from).TotalSeconds;
        var seen = new HashSet<string>();

        foreach (var r in records)
        {
            if (r.Hour < from || r.Hour > now) continue;
            seen.Add(r.Agent);
            if (off?.Contains(r.Agent) == true) continue;
            if (claudeAccount is not null && r.Agent == UsageAgents.Claude && r.Account != claudeAccount) continue;

            report.Totals.Add(r.Sums);
            var modelKey = r.Agent + "|" + r.Model;
            if (!models.TryGetValue(modelKey, out var m))
                models[modelKey] = m = (new UsageModelRow(r.Agent, r.Model, r.Provider, new UsageSums(), new double[TrendPoints]), new UsageSums());
            m.Row.Sums.Add(r.Sums);
            var point = span <= 0 ? 0 : (int)Math.Clamp((r.Hour - from).TotalSeconds / span * TrendPoints, 0, TrendPoints - 1);
            m.Row.Trend[point] += r.Sums.Output;

            if (!agents.TryGetValue(r.Agent, out var a)) agents[r.Agent] = a = (new UsageSums(), []);
            a.Sums.Add(r.Sums);
            if (r.Session is { } s)
            {
                a.Sessions.Add(s);
                sessions.Add(r.Agent + "|" + s);
            }

            var project = r.Project is { Length: > 0 } p ? p : "";
            if (!projects.TryGetValue(project, out var ps)) projects[project] = ps = new UsageSums();
            ps.Add(r.Sums);

            var local = r.Hour.ToLocalTime();
            var slot = hours ? (int)Math.Floor((r.Hour - from).TotalHours) : (int)Math.Floor((new DateTimeOffset(local.Date, local.Offset) - from).TotalDays);
            if (slot >= 0 && slot < slots)
            {
                Slot(report.ByAgentSlot, r.Agent, slots)[slot].Add(r.Sums);
                Slot(report.ByModelSlot, modelKey, slots)[slot].Add(r.Sums);
            }
        }

        // El mapa de calor no depende del periodo: siempre los últimos 7 días (con los filtros de agente y cuenta).
        foreach (var r in records)
        {
            if (r.Hour < heatFrom || r.Hour > now || off?.Contains(r.Agent) == true) continue;
            if (claudeAccount is not null && r.Agent == UsageAgents.Claude && r.Account != claudeAccount) continue;
            var local = r.Hour.ToLocalTime();
            report.Heat[((int)local.DayOfWeek + 6) % 7, local.Hour] += r.Sums.Responses;
        }

        report.Models.AddRange(models.Values.Select(m => m.Row).OrderByDescending(m => m.Sums.Output));
        report.Agents.AddRange(agents.Select(a => new UsageAgentRow(a.Key, a.Value.Sums, a.Value.Sessions.Count,
            report.Models.Where(m => m.Agent == a.Key).OrderByDescending(m => m.Sums.Output).FirstOrDefault()?.Model))
            .OrderByDescending(a => a.Sums.Output));
        report.Projects.AddRange(projects.Select(p => new UsageProjectRow(p.Key, p.Value)).OrderByDescending(p => p.Sums.Output));
        report.AgentsSeen.AddRange(UsageAgents.All.Where(seen.Contains));
        report.Sessions = sessions.Count;
        return report;
    }

    static UsageSums[] Slot(Dictionary<string, UsageSums[]> map, string key, int slots)
    {
        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = new UsageSums[slots];
            for (var i = 0; i < slots; i++) list[i] = new UsageSums();
        }
        return list;
    }
}

/// <summary>Los agentes, en el orden en que se muestran, con su nombre y su color (los de la paleta de Claupet).</summary>
static class UsageAgents
{
    public const string Claude = "claude";
    public static readonly string[] All = [Claude, Agents.Codex, Agents.Gemini, Agents.OpenCode, Agents.Copilot];

    public static string Name(string agent) => agent switch
    {
        Claude => "Claude Code",
        Agents.Codex => "Codex",
        Agents.Gemini => "Gemini CLI",
        Agents.OpenCode => "OpenCode",
        Agents.Copilot => "Copilot CLI",
        _ => agent,
    };

    public static string Color(string agent) => agent switch
    {
        Claude => "#D97757",
        Agents.Codex => "#5BA3E0",
        Agents.Gemini => "#A48BE0",
        Agents.OpenCode => "#6DB283",
        Agents.Copilot => "#E0A84B",
        _ => "#9A988F",
    };

    /// <summary>El nombre de un modelo para mostrar: los de Claude, como en el resto de la app («Opus 5.5»); los demás, tal cual.</summary>
    public static string ModelName(string agent, string model) =>
        agent == Claude && model.StartsWith("claude-", StringComparison.Ordinal) ? Format.ModelChoice(model) : model;
}
