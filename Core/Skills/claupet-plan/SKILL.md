---
name: claupet-plan
description: Plan, schedule and track a project as a Claupet plan file (*.plan.json) that Claupet draws as a summary, board, timeline and dependency graph. Use when the user asks to plan or break down work, make a roadmap or schedule, track tasks, stages, milestones or progress, or works on a task from a .plan.json (mark it en-curso when starting and hecho when done).
---

# Claupet plans

Claupet shows every `*.plan.json` in the project as a live plan (summary, board, timeline and dependency graph) and
refreshes it as soon as the file changes. Keep plans in that format, never as loose text or a markdown list.

## Tools first

If the `claupet-canvas` MCP server is available, always use its plan tools:

1. `plan_list` to find the plans in the project folder.
2. `plan_read` before changing one: it returns the resolved dates, states and ids.
3. `plan_create` for a new plan (default path `planes/<title>.plan.json`).
4. `plan_update` with small ops for every change (all or nothing):
   - `{op: "task", id, ...fields}` creates the task if the id is new, otherwise changes only the given fields (`null` removes one).
   - `{op: "stage", id, nombre?, color?}`, `{op: "milestone", id, titulo?, fecha?, depende?}`, `{op: "plan", titulo?, descripcion?, inicio?}`.
   - `{op: "delete", kind: "task" | "stage" | "milestone", id}`.

Without the tools, edit the JSON file directly in the same format and keep it valid.

## Format

Keys are in Spanish:

```json
{
  "titulo": "Launch 2.4",
  "descripcion": "Optional summary.",
  "inicio": "2026-11-02",
  "etapas": [{ "id": "e1", "nombre": "Design", "color": "#7c6cf0" }],
  "tareas": [
    { "id": "t1", "titulo": "Mockups", "etapa": "e1", "estado": "por-hacer", "dias": 2, "horas": 6 },
    { "id": "t2", "titulo": "Build", "etapa": "e1", "depende": ["t1"], "dias": 3, "responsable": "Ana", "notas": "Dark theme too." }
  ],
  "hitos": [{ "id": "h1", "titulo": "Beta", "depende": ["t2"] }]
}
```

- `estado`: `por-hacer` | `en-curso` | `revision` | `hecho` | `bloqueado`.
- A task without `inicio` (`YYYY-MM-DD`) starts when the tasks in `depende` end; `dias` is its length (default 1).
- `horas` is the estimate and `horasReales` the time spent.
- A milestone without `fecha` falls on the day its dependencies end.
- Use short, stable ids (`e1`, `t1`, `h1`) and never reuse a deleted one. No dependency cycles.

## Working on a task

When asked to work on a task of a plan:

1. Set its `estado` to `en-curso` with `plan_update` before starting.
2. Do the work.
3. Set it to `hecho` when finished (or `revision` if it needs the user's review, `bloqueado` with a `notas` reason
   if it cannot go on). Add `horasReales` if you know the time spent.

## Replying

Keep the plan in the file, not in the chat: answer with a short summary and the plan's path so the user can open it
in Claupet.
