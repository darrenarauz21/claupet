using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Claupet;

/// <summary>Qué le pasó a una tarea: «new» (apareció), «state» (cambió de estado) o «removed» (se quitó).</summary>
sealed record PlanEvent(DateTime At, string? Who, string Kind, string Task, string Title, PlanState? State);

/// <summary>
/// La actividad de un plan. El archivo solo dice cómo está cada tarea ahora, así que Claupet compara cada lectura con la
/// anterior y guarda lo que cambió, aparte (en la carpeta de datos, no en el proyecto). La primera vez que ve un plan no
/// inventa nada: solo toma nota de cómo está. Quién fue: lo que dice la tarea («por») o, si se cambió desde Claupet, quien se indique.
/// </summary>
sealed class PlanActivity(string? folder = null)
{
    const int MaxEvents = 400;

    sealed class Known
    {
        public string Title { get; set; } = "";
        public PlanState State { get; set; }
    }

    sealed class Saved
    {
        public string Path { get; set; } = "";
        public Dictionary<string, Known> Tasks { get; set; } = [];
        public List<PlanEvent> Events { get; set; } = [];
    }

    readonly string _folder = folder ?? DataFolder.In("planes-actividad");

    string FileOf(string plan)
    {
        var key = Path.GetFullPath(plan);
        if (OperatingSystem.IsWindows()) key = key.ToLowerInvariant();
        return Path.Combine(_folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24] + ".json");
    }

    Saved? Load(string plan)
    {
        try { return JsonSerializer.Deserialize<Saved>(File.ReadAllText(FileOf(plan))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    /// <summary>Lo que se sabe de ese plan, de lo más nuevo a lo más viejo.</summary>
    public IReadOnlyList<PlanEvent> Read(string plan) => Load(plan)?.Events ?? [];

    /// <summary>Compara el plan con lo último que se vio de él, guarda lo que cambió y devuelve toda su actividad.</summary>
    public IReadOnlyList<PlanEvent> Update(string path, Plan plan, DateTime at, string? who = null)
    {
        var saved = Load(path);
        var first = saved is null;
        saved ??= new Saved { Path = path };
        var found = new List<PlanEvent>();
        foreach (var task in plan.Tasks)
        {
            var by = who ?? task.Editor;
            if (!saved.Tasks.TryGetValue(task.Id, out var known))
            {
                if (!first) found.Add(new PlanEvent(at, by, "new", task.Id, task.Title, task.State));
            }
            else if (known.State != task.State) found.Add(new PlanEvent(at, by, "state", task.Id, task.Title, task.State));
        }
        var ids = plan.Tasks.Select(t => t.Id).ToHashSet();
        foreach (var (id, known) in saved.Tasks.Where(t => !ids.Contains(t.Key)))
            found.Add(new PlanEvent(at, who, "removed", id, known.Title, null));

        var changed = first || found.Count > 0 || plan.Tasks.Any(t => saved.Tasks.GetValueOrDefault(t.Id)?.Title != t.Title);
        if (!changed) return saved.Events;
        saved.Tasks = plan.Tasks.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => new Known { Title = g.First().Title, State = g.First().State });
        saved.Events = [.. found, .. saved.Events.Take(MaxEvents - found.Count)];
        try
        {
            Directory.CreateDirectory(_folder);
            var file = FileOf(path);
            File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(saved));
            File.Move(file + ".tmp", file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return saved.Events;
    }
}

/// <summary>
/// El avance de un plan a lo largo del tiempo: cuántos días de trabajo había terminados cada día, contra los del plan entero.
/// Una tarea cuenta el día que Claupet la vio terminar; si no lo vio, el día en que debía terminar (nunca después de hoy).
/// </summary>
sealed class PlanBurn
{
    readonly List<(DateOnly Day, int Days)> _done;

    public DateOnly From { get; }
    public DateOnly To { get; }
    public int Total { get; }

    public PlanBurn(Plan plan, DateOnly today, IEnumerable<PlanEvent>? events = null)
    {
        (From, To) = plan.Span();
        if (today >= To) To = today.AddDays(1);
        Total = plan.Tasks.Sum(t => t.Days);
        var seen = new Dictionary<string, DateOnly>();
        foreach (var e in events ?? [])
            if (e is { Kind: "state", State: PlanState.Done } && !seen.ContainsKey(e.Task)) seen[e.Task] = DateOnly.FromDateTime(e.At);
        _done = plan.Tasks.Where(t => t.State == PlanState.Done)
            .Select(t =>
            {
                var day = seen.TryGetValue(t.Id, out var at) ? at : t.End.AddDays(-1) < today ? t.End.AddDays(-1) : today;
                return (Day: day, t.Days);
            })
            .OrderBy(d => d.Day).ToList();
    }

    /// <summary>Los días en que se terminó algo, sin repetir y en orden.</summary>
    public IEnumerable<DateOnly> Steps => _done.Select(d => d.Day).Distinct();

    /// <summary>Los días de trabajo terminados hasta ese día, inclusive.</summary>
    public int DoneOn(DateOnly day) => _done.Where(d => d.Day <= day).Sum(d => d.Days);

    /// <summary>Lo que debería llevarse hecho ese día si todo avanzara parejo.</summary>
    public double IdealOn(DateOnly day)
    {
        var length = To.DayNumber - From.DayNumber;
        return length <= 0 ? Total : Math.Clamp((day.DayNumber - From.DayNumber + 1.0) / length, 0, 1) * Total;
    }
}
