---
id: base
tipo: base
titulo: Contrato base
resumen: Propósito, stack y reglas mínimas de este proyecto
aplica: ["**/*"]
prioridad: obligatoria
formato: 1
actualizado: 2026-11-20
---

## Propósito

Claupet: una app de escritorio para Windows, macOS y Linux que muestra el uso de Claude Code y otros agentes, con
Claupet Console (Terminal con agentes, Canvas, editor, Proyectos y Especialistas), las extensiones y el comando `cpt`.
El repositorio es público.

## Stack

- .NET 10 (C#), con el SDK 10.0.404 o posterior (lo fija `global.json`)
- Avalonia para la interfaz
- Python 3 solo para armar los paquetes (`herramientas/empaquetar.py`)

## Comandos

- Compilar: `dotnet build Claupet.Tests/Claupet.Tests.csproj`
- Probar: `dotnet test Claupet.Tests/Claupet.Tests.csproj`

## Reglas

- [alcance] Nada fuera del alcance pedido, salvo que se pida. `check: alcance`
- [archivos-verdad] Los archivos son la fuente de verdad; los índices se regeneran.
- [ramas] Cada cambio va en su propia rama (`fix/…`, `feature/…`, `docs/…`) que sale de `development`, en su propio git worktree, y su PR va a `development`. `main` solo recibe `development` por PR, para publicar.
- [ramas-compartidas] Nunca hagas push ni merge directo a `development` ni a `main`, ni reescribas su historia: los PR los fusiona la persona.
- [carpeta-compartida] Otras sesiones (Claude Code, Codex, Gemini CLI, Copilot, OpenCode u otros agentes) pueden trabajar en la misma carpeta, y la persona puede tener trabajo sin guardar: los cambios sin commit que no hiciste son de otro.
- [git-seguro] Nunca uses `git stash`, `git switch` o `git checkout` de otra rama, `git checkout --` o `git restore` sobre archivos que no cambiaste, `git reset --hard` ni `git clean` sin preguntar antes.
- [commits-propios] Al hacer commit, agrega solo los archivos que cambiaste (`git add <rutas>`); nunca `git add -A`, `git add .` ni `git commit -a`.
- [otra-rama] Si necesitas otra rama, abre tu propio git worktree en vez de cambiar la rama de esta carpeta.
- [publico] El repositorio es público: nada de datos de la persona ni de su equipo en el código, los commits, los PR ni los documentos.
- [sin-atribucion] Sin atribución a la IA («Co-Authored-By», «Generated with…») en commits, PR ni documentos.

## Cómo usar los contratos

No leas todo: busca con `cpt contracts search <tema>` y lee con `cpt contracts read <id>` (o las herramientas MCP `contract_search` y `contract_read`). El índice está en `.claupet/contratos/.generado/indice.json`.
