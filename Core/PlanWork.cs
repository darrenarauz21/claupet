using System.Text;

namespace Claupet;

/// <summary>
/// El pedido con que una sesión de Claude Code trabaja en un plan de Claupet: el plan, la etapa, las tareas y lo que ya
/// está hecho de lo que necesitan, con las reglas para marcar su estado. Es una tarea («Trabajar en esto») o varias (una
/// etapa o un grupo): la sesión arranca con él ya enviado.
/// </summary>
static class PlanWork
{
    /// <summary>El pedido para trabajar en esas tareas del plan (path: el archivo del plan).</summary>
    public static string Prompt(Plan plan, string path, IReadOnlyList<PlanTask> tasks)
    {
        var text = new StringBuilder();
        // La ruta completa: plan_read y plan_update la encuentran sea cual sea la carpeta de la sesión.
        text.AppendLine(L.T("Trabaja en el plan «{0}» ({1}).", plan.Title, path));
        if (plan.Description is { Length: > 0 } description) text.AppendLine(description);
        text.AppendLine();
        text.AppendLine(tasks.Count == 1 ? L.T("Tu tarea:") : L.T("Tus tareas, en este orden:"));
        foreach (var task in tasks)
        {
            var stage = plan.Stages.FirstOrDefault(s => s.Id == task.Stage)?.Name;
            text.AppendLine(stage is null ? $"- «{task.Title}» (id {task.Id})" : L.T("- «{0}» (id {1}), de la etapa «{2}»", task.Title, task.Id, stage));
            if (task.Notes is { Length: > 0 } notes) text.AppendLine("  " + notes.ReplaceLineEndings("\n  "));
        }

        // Lo que necesitan y ya está hecho: el punto de partida (lo de otras tareas de este mismo pedido no cuenta).
        var ids = tasks.Select(t => t.Id).ToHashSet();
        var done = tasks.SelectMany(t => t.Depends).Where(d => !ids.Contains(d)).Distinct()
            .Select(d => plan.Tasks.FirstOrDefault(t => t.Id == d)).OfType<PlanTask>().Where(t => t.State == PlanState.Done).ToList();
        if (done.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(L.T("Ya está hecho y puedes partir de ahí: {0}.", string.Join(", ", done.Select(t => $"«{t.Title}»"))));
        }

        text.AppendLine();
        text.AppendLine(L.T("Cómo trabajar:"));
        text.AppendLine(L.T("- Si necesitas el contexto completo, lee el plan con plan_read."));
        text.AppendLine(tasks.Count == 1
            ? L.T("- La tarea ya está en curso. Al terminarla y comprobar que funciona, márcala «hecho» con plan_update; si hace falta que la revise, «revision»; si te bloqueas, «bloqueado» con una nota de por qué.")
            : L.T("- Marca cada tarea «en-curso» con plan_update al empezarla y «hecho» al terminarla y comprobar que funciona; si hace falta que la revise, «revision»; si te bloqueas, «bloqueado» con una nota de por qué."));
        text.AppendLine(L.T("- Haz solo lo que se pide aquí: no empieces otras tareas del plan."));
        text.Append(L.T("- Al final, dime en pocas líneas qué hiciste y cómo probarlo."));
        return text.ToString();
    }
}
