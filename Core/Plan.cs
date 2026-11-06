using System.Text.Json;

namespace Claupet;

/// <summary>En qué está una tarea del plan.</summary>
enum PlanState { Pending, Doing, Review, Done, Blocked }

/// <summary>Una etapa del plan: agrupa tareas y les da su color.</summary>
sealed record PlanStage(string Id, string Name, string? Color);

/// <summary>
/// Una tarea (Editor: quién la cambió por última vez, si fue una IA: «Claude Code», «Codex»…): cuándo empieza (la escrita o, si no hay, al terminar de lo que depende) y cuántos días dura. End es el día
/// siguiente al último (Start + Days), para dibujarla sin restar uno.
/// </summary>
sealed record PlanTask(string Id, string Title, string? Stage, PlanState State, DateOnly Start, int Days, IReadOnlyList<string> Depends,
    double? Hours, double? HoursSpent, string? Owner, string? Notes, string? Editor = null)
{
    public DateOnly End => Start.AddDays(Days);
    public bool LateOn(DateOnly today) => State != PlanState.Done && End <= today;
}

/// <summary>Un hito: un día que importa (una entrega), sin duración.</summary>
sealed record PlanMilestone(string Id, string Title, DateOnly Date, IReadOnlyList<string> Depends);

/// <summary>
/// Un plan de proyecto de Claupet: el formato que las IAs escriben (un archivo *.plan.json) y la tarjeta de archivos
/// dibuja como resumen, tablero y cronograma en vez de mostrar el texto. Las claves están en español:
/// <code>
/// { "claupet": "plan/1", "titulo": "…", "inicio": "2026-11-03",
///   "etapas": [ { "id": "e1", "nombre": "…", "color": "azul" } ],
///   "tareas": [ { "id": "t1", "titulo": "…", "etapa": "e1", "estado": "en-curso", "inicio": "2026-11-03", "dias": 2,
///                 "depende": ["t0"], "horas": 6, "horasReales": 4, "responsable": "…", "notas": "…",
///                 "por": "Codex" } ],
///   "hitos":  [ { "id": "h1", "titulo": "…", "fecha": "2026-11-21", "depende": ["t1"] } ] }
/// </code>
/// Se lee con tolerancia: lo que falta toma un valor razonable y lo que no se entiende queda en Warnings.
/// </summary>
sealed class Plan
{
    /// <summary>La extensión de un plan, para reconocerlo por su nombre.</summary>
    public const string Extension = ".plan.json";
    /// <summary>Lo que dice «claupet» en un plan de este formato.</summary>
    public const string Format = "plan/1";

    public string Title { get; private init; } = "";
    public string? Description { get; private init; }
    public DateOnly Start { get; private init; }
    public IReadOnlyList<PlanStage> Stages { get; private init; } = [];
    public IReadOnlyList<PlanTask> Tasks { get; private init; } = [];
    public IReadOnlyList<PlanMilestone> Milestones { get; private init; } = [];
    /// <summary>Lo que no se entendió (una dependencia que no existe, un estado desconocido…): se muestra, no rompe.</summary>
    public IReadOnlyList<string> Warnings { get; private init; } = [];

    public static bool IsPlanFile(string path) => path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>El primer día del plan (el de la tarea o el hito más temprano) y el día siguiente al último.</summary>
    public (DateOnly From, DateOnly To) Span()
    {
        var from = Start;
        var to = Start.AddDays(1);
        foreach (var t in Tasks)
        {
            if (t.Start < from) from = t.Start;
            if (t.End > to) to = t.End;
        }
        foreach (var m in Milestones)
        {
            if (m.Date < from) from = m.Date;
            if (m.Date.AddDays(1) > to) to = m.Date.AddDays(1);
        }
        return (from, to);
    }

    /// <summary>Cuánto se avanzó, de 0 a 1: los días de las tareas hechas sobre los de todas (una tarea de 5 días pesa más).</summary>
    public static double Progress(IEnumerable<PlanTask> tasks)
    {
        double all = 0, done = 0;
        foreach (var t in tasks)
        {
            all += t.Days;
            if (t.State == PlanState.Done) done += t.Days;
            else if (t.State == PlanState.Review) done += t.Days * 0.8;
        }
        return all == 0 ? 0 : done / all;
    }

    /// <summary>Lee un plan. Si el texto no es un plan (no es JSON, o no es un objeto), lanza FormatException con el motivo.</summary>
    public static Plan Parse(string json, DateOnly? today = null)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
        catch (JsonException ex) { throw new FormatException(L.T("No es JSON válido: {0}", ex.Message), ex); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new FormatException(L.T("Un plan es un objeto JSON ({ … })."));
            var warnings = new List<string>();
            if (Str(root, "claupet") is { } format && format != Format)
                warnings.Add(L.T("Formato «{0}»: esta versión de Claupet entiende «{1}».", format, Format));
            var start = Date(root, "inicio") ?? today ?? DateOnly.FromDateTime(DateTime.Today);

            var stages = new List<PlanStage>();
            foreach (var e in Array(root, "etapas"))
            {
                var id = Str(e, "id") ?? $"e{stages.Count + 1}";
                stages.Add(new PlanStage(id, Str(e, "nombre") ?? Str(e, "titulo") ?? id, Str(e, "color")));
            }

            // Primero lo escrito; las fechas que faltan se calculan después, cuando ya se conocen todas.
            var raw = new List<(string Id, JsonElement E)>();
            foreach (var e in Array(root, "tareas"))
            {
                var id = Str(e, "id") ?? $"t{raw.Count + 1}";
                if (raw.Any(r => r.Id == id)) warnings.Add(L.T("Hay dos tareas con el id «{0}».", id));
                raw.Add((id, e));
            }
            var ids = raw.Select(r => r.Id).ToHashSet();
            var ends = new Dictionary<string, DateOnly>();
            var tasks = new Dictionary<string, PlanTask>();
            var visiting = new HashSet<string>();

            PlanTask Build(string id, JsonElement e)
            {
                if (tasks.TryGetValue(id, out var built)) return built;
                visiting.Add(id);
                var depends = Strings(e, "depende").Where(d =>
                {
                    if (ids.Contains(d)) return true;
                    warnings.Add(L.T("La tarea «{0}» depende de «{1}», que no existe.", id, d));
                    return false;
                }).ToList();
                var begin = Date(e, "inicio");
                if (begin is null)
                {
                    // Sin fecha: empieza cuando termina lo último de lo que depende (o al principio del plan).
                    begin = start;
                    foreach (var d in depends)
                    {
                        if (visiting.Contains(d))
                        {
                            warnings.Add(L.T("Las tareas «{0}» y «{1}» dependen una de la otra.", id, d));
                            continue;
                        }
                        var other = Build(d, raw.First(r => r.Id == d).E);
                        if (other.End > begin) begin = other.End;
                    }
                }
                var stateText = Str(e, "estado");
                var state = State(stateText);
                if (state is null && stateText is not null) warnings.Add(L.T("La tarea «{0}» tiene un estado desconocido: «{1}».", id, stateText));
                var stage = Str(e, "etapa");
                if (stage is not null && stages.All(s => s.Id != stage)) warnings.Add(L.T("La tarea «{0}» es de la etapa «{1}», que no existe.", id, stage));
                var task = new PlanTask(id, Str(e, "titulo") ?? id, stage, state ?? PlanState.Pending, begin.Value,
                    Math.Max(1, (int)Math.Round(Num(e, "dias") ?? 1)), depends, Num(e, "horas"), Num(e, "horasReales"),
                    Str(e, "responsable"), Str(e, "notas"), Str(e, "por"));
                visiting.Remove(id);
                tasks[id] = task;
                ends[id] = task.End;
                return task;
            }

            foreach (var (id, e) in raw) Build(id, e);

            var milestones = new List<PlanMilestone>();
            foreach (var e in Array(root, "hitos"))
            {
                var id = Str(e, "id") ?? $"h{milestones.Count + 1}";
                var depends = Strings(e, "depende").Where(ids.Contains).ToList();
                // Sin fecha: el día que termina lo último de lo que depende.
                var date = Date(e, "fecha") ?? (depends.Count > 0 ? depends.Max(d => ends[d]) : start);
                milestones.Add(new PlanMilestone(id, Str(e, "titulo") ?? id, date, depends));
            }

            return new Plan
            {
                Title = Str(root, "titulo") ?? "",
                Description = Str(root, "descripcion"),
                Start = start,
                Stages = stages,
                // En el orden escrito (no en el que se calcularon).
                Tasks = raw.Select(r => tasks[r.Id]).Distinct().ToList(),
                Milestones = milestones.OrderBy(m => m.Date).ToList(),
                Warnings = warnings.Distinct().ToList(),
            };
        }
    }

    /// <summary>El estado escrito; null si no se entiende. Acepta también algunas formas en inglés.</summary>
    static PlanState? State(string? text) => text?.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-') switch
    {
        null or "" or "por-hacer" or "pendiente" or "todo" or "pending" => PlanState.Pending,
        "en-curso" or "haciendo" or "doing" or "in-progress" => PlanState.Doing,
        "revision" or "revisión" or "review" => PlanState.Review,
        "hecho" or "hecha" or "listo" or "lista" or "done" => PlanState.Done,
        "bloqueado" or "bloqueada" or "blocked" => PlanState.Blocked,
        _ => null,
    };

    static IEnumerable<JsonElement> Array(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).ToList() : [];

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() is { Length: > 0 } s ? s.Trim() : null,
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        } : null;

    static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && d >= 0 ? d : null;

    static DateOnly? Date(JsonElement e, string name) =>
        Str(e, name) is { } s && DateOnly.TryParseExact(s.Length > 10 ? s[..10] : s, "yyyy-MM-dd", out var d) ? d : null;

    static IEnumerable<string> Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.Array => v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim()).Where(x => x.Length > 0).ToList(),
            JsonValueKind.String => [v.GetString()!.Trim()],
            _ => [],
        } : [];
}
