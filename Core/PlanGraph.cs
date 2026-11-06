namespace Claupet;

/// <summary>Una tarea o un hito en el diagrama: su columna y su fila (puede caer entre dos, para quedar centrado).</summary>
sealed record PlanGraphNode(string Id, string Title, int Column, double Row, PlanTask? Task, PlanMilestone? Milestone);

/// <summary>
/// Una dependencia: From tiene que terminar antes que To. Through: por dónde pasa si salta columnas (así no atraviesa
/// otras tareas). Loop: cierra un ciclo (una espera a la otra), algo que no se puede cumplir.
/// </summary>
sealed record PlanGraphEdge(string From, string To, bool Loop, IReadOnlyList<(int Column, double Row)> Through);

/// <summary>
/// Dónde va cada cosa en el diagrama de dependencias de un plan: en la primera columna lo que no espera a nadie y cada
/// tarea, a la derecha de todo lo que espera. Dentro de la columna se ordena para que las líneas se crucen poco. Un
/// plan con dependencias en círculo no lo rompe: la que cierra el círculo queda marcada.
/// </summary>
sealed class PlanGraph
{
    /// <summary>Con qué prefijo se distingue un hito de una tarea (pueden tener el mismo id).</summary>
    public const string MilestonePrefix = "◆";

    const int Sweeps = 4;

    public IReadOnlyList<PlanGraphNode> Nodes { get; private init; } = [];
    public IReadOnlyList<PlanGraphEdge> Edges { get; private init; } = [];
    public int Columns { get; private init; }
    public int Rows { get; private init; }

    public static PlanGraph Build(Plan plan)
    {
        var tasks = plan.Tasks.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
        var milestones = plan.Milestones.GroupBy(m => m.Id).ToDictionary(g => MilestonePrefix + g.Key, g => g.First());
        var order = plan.Tasks.Select(t => t.Id).Distinct().Concat(milestones.Keys).ToList();

        // De quién depende cada uno, sin lo que no existe, sin repetidos y sin depender de sí mismo.
        var depends = new Dictionary<string, List<string>>();
        foreach (var id in order)
        {
            var written = tasks.TryGetValue(id, out var task) ? task.Depends : milestones[id].Depends;
            depends[id] = written.Where(d => d != id && tasks.ContainsKey(d)).Distinct().ToList();
        }

        // La columna: una más que lo último que espera. Lo que vuelve a algo que se está recorriendo cierra un círculo.
        var columns = new Dictionary<string, int>();
        var walking = new HashSet<string>();
        var loops = new HashSet<(string From, string To)>();
        void Walk(string id)
        {
            if (columns.ContainsKey(id)) return;
            walking.Add(id);
            var column = 0;
            foreach (var d in depends[id])
            {
                if (walking.Contains(d))
                {
                    loops.Add((d, id));
                    continue;
                }
                Walk(d);
                column = Math.Max(column, columns[d] + 1);
            }
            walking.Remove(id);
            columns[id] = column;
        }
        foreach (var id in order) Walk(id);
        var count = order.Count == 0 ? 0 : columns.Values.Max() + 1;

        // Cada columna con sus lugares; una línea que salta columnas ocupa uno en cada una, para pasar sin pisar nada.
        var slots = Enumerable.Range(0, count).Select(_ => new List<string>()).ToList();
        foreach (var id in order) slots[columns[id]].Add(id);
        var before = order.ToDictionary(id => id, _ => new List<string>());
        var after = order.ToDictionary(id => id, _ => new List<string>());
        var links = new List<(string From, string To, bool Loop, List<string> Through)>();
        foreach (var to in order)
            foreach (var from in depends[to])
            {
                var loop = loops.Contains((from, to));
                var through = new List<string>();
                var last = from;
                for (var c = columns[from] + 1; !loop && c < columns[to]; c++)
                {
                    var stop = $"{from}→{to}@{c}";
                    slots[c].Add(stop);
                    before[stop] = [last];
                    after[stop] = [];
                    after[last].Add(stop);
                    through.Add(stop);
                    last = stop;
                }
                if (!loop)
                {
                    after[last].Add(to);
                    before[to].Add(last);
                }
                links.Add((from, to, loop, through));
            }

        // Lo suelto va abajo de lo conectado; luego, cada uno cerca del promedio de sus vecinos, ida y vuelta.
        foreach (var column in slots)
        {
            var sorted = column.OrderBy(id => before[id].Count + after[id].Count == 0 ? 1 : 0).ToList();
            column.Clear();
            column.AddRange(sorted);
        }
        for (var sweep = 0; sweep < Sweeps; sweep++)
        {
            for (var c = 1; c < count; c++) Sort(slots[c], slots[c - 1], before);
            for (var c = count - 2; c >= 0; c--) Sort(slots[c], slots[c + 1], after);
        }

        // Las columnas cortas, centradas respecto de la más alta.
        var rows = slots.Count == 0 ? 0 : slots.Max(s => s.Count);
        var places = new Dictionary<string, (int Column, double Row)>();
        for (var c = 0; c < count; c++)
            for (var i = 0; i < slots[c].Count; i++)
                places[slots[c][i]] = (c, i + (rows - slots[c].Count) / 2.0);

        return new PlanGraph
        {
            Columns = count,
            Rows = rows,
            Nodes = order.Select(id => new PlanGraphNode(id, tasks.TryGetValue(id, out var t) ? t.Title : milestones[id].Title,
                places[id].Column, places[id].Row, t, milestones.GetValueOrDefault(id))).ToList(),
            Edges = links.Select(l => new PlanGraphEdge(l.From, l.To, l.Loop, l.Through.Select(s => places[s]).ToList())).ToList(),
        };
    }

    /// <summary>Ordena una columna por el lugar promedio de sus vecinos en la de al lado; quien no tiene, se queda donde está.</summary>
    static void Sort(List<string> column, List<string> neighbors, Dictionary<string, List<string>> links)
    {
        var at = new Dictionary<string, int>();
        for (var i = 0; i < neighbors.Count; i++) at[neighbors[i]] = i;
        var keys = new Dictionary<string, double>();
        for (var i = 0; i < column.Count; i++)
        {
            var near = links[column[i]].Where(at.ContainsKey).Select(n => at[n]).ToList();
            keys[column[i]] = near.Count > 0 ? near.Average() : i;
        }
        var sorted = column.OrderBy(id => keys[id]).ToList();
        column.Clear();
        column.AddRange(sorted);
    }

    /// <summary>Su cadena: ese, todo lo que tiene que terminar antes y todo lo que lo está esperando.</summary>
    public IReadOnlySet<string> Chain(string id)
    {
        var chain = new HashSet<string> { id };
        void Follow(string from, Func<PlanGraphEdge, string> near, Func<PlanGraphEdge, string> far)
        {
            foreach (var edge in Edges.Where(e => near(e) == from))
                if (chain.Add(far(edge))) Follow(far(edge), near, far);
        }
        Follow(id, e => e.To, e => e.From);
        Follow(id, e => e.From, e => e.To);
        return chain;
    }

    /// <summary>Si la tarea está por hacer y ya no espera a nadie: se puede empezar.</summary>
    public static bool Ready(Plan plan, PlanTask task) =>
        task.State == PlanState.Pending && task.Depends.All(d => plan.Tasks.FirstOrDefault(t => t.Id == d) is not { } other || other.State == PlanState.Done);
}
