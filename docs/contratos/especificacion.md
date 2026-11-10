# Contratos Claupet: especificación v1

> Método para que los agentes programen bien sin leer todo el proyecto. Se usa con `cpt` (o `claupet`), el comando de Claupet.

## Qué es

Un método para que los agentes programen bien **sin leer todo el proyecto**. Las reglas viven en minicontratos pequeños, el agente lee solo los que importan para cada tarea, el motor verifica lo que se puede verificar, y el sistema aprende de los errores y del feedback.

**Principios**
1. **Los archivos son la fuente de verdad.** Markdown y JSON versionados con git. Cualquier índice o base de datos se puede regenerar.
2. **Poco contexto, bien elegido.** El agente lee solo lo que importa para la tarea.
3. **Nada cambia en silencio.** El agente propone y la persona aprueba, tanto el código como los contratos.
4. **Una regla que se puede comprobar, se comprueba.**
5. **Alcance estricto.** Nunca se hacen cambios de más salvo que se pidan.
6. **Portable.** Funciona con cualquier agente, con o sin Claupet.

Versión del formato: `formato: 1` (va en `base.md`).

## Capas

| Orden | Capa | Dónde | Quién la cambia |
|---|---|---|---|
| 1 | Claupet | Dentro del motor (paquetes por defecto) | Las actualizaciones |
| 2 | Perfil | `%APPDATA%\Claupet\contratos\` (macOS y Linux: `~/.config/claupet/contratos/`) | La persona |
| 3 | Proyecto | `.claupet/contratos/` en el repo | El equipo, por git |

Gana la capa más alta (el proyecto sobre el perfil, el perfil sobre Claupet). Un contrato de una capa alta con el **mismo id** que uno de abajo no lo duplica: lo modifica según su `modo`:

| `modo` | Efecto |
|---|---|
| `nuevo` (por defecto) | Contrato propio; error si el id ya existe abajo |
| `amplia` | Suma sus reglas a las de abajo; puede cambiar campos de la cabecera |
| `reemplaza` | Sustituye el contrato entero |
| `apaga` | Lo desactiva en esa capa y las de arriba |

Además, `apaga_reglas: [id, …]` desactiva reglas sueltas sin tocar el resto.

Resultado: el motor arma una vista **resuelta**, con cada regla marcada por su capa de origen. Es la que se ve en la Console.

**Paquetes por defecto**
- Viven **dentro del motor** y de la skill, así que se actualizan solos y también funcionan en el modo simple, sin motor.
- En `init`, el agente los **adapta al proyecto** según el stack detectado. Escribe contratos `modo: amplia` en `.claupet/contratos/` que solo guardan la diferencia, y los propone para aprobar. Cada proyecto queda ajustado sin perder las mejoras de Claupet.
- `copiar` / `eject` trae un contrato completo al proyecto. Desde ahí es del proyecto y deja de recibir actualizaciones; el motor avisa si la versión de Claupet cambió después.

## Carpeta del proyecto

```
.claupet/contratos/
  base.md                 contrato base (obligatorio)
  calidad/                cómo debe ser algo bien hecho: api, esquema-bd, backend, frontend, ui, ux, animaciones, arquitectura, pruebas
  estilo/                 cómo se escribe y se ve: escritura, diseno
  capacidades/            una función del sistema: login, pagos, exportar-pdf
  mapa/                   cómo navegar el código
  lecciones/              AAAA-MM-DD-tema.md
  cambios/
    AAAA-MM-DD-tema/      propuesta.md, etapas.md, delta.md
    archivo/              los cambios terminados
  propuestas/             mejoras de contratos esperando aprobación
  ajustes.json            umbrales y preferencias del proyecto
  .generado/              indice.json, cachés (en .gitignore)
```

El perfil tiene la misma forma, sin `cambios/` y con `perfil/como-trabajo.md`.

## Minicontrato

Un archivo Markdown con cabecera YAML corta y reglas en una lista. Cada regla lleva un id corto entre corchetes, que sirve para apagarla, citarla y verificarla.

```markdown
---
id: estilo.escritura
tipo: estilo
titulo: Escritura
resumen: Cómo se escribe en la interfaz, el código y los commits
aplica: ["**/*"]
etiquetas: [texto, ui, commits]
prioridad: obligatoria
actualizado: 2026-11-08
---

## Reglas
- [sin-emojis] Sin emojis en la interfaz, el código ni los commits. `check: sin-emojis`
- [sin-guion-largo] Sin guiones largos. `check: sin-guion-largo`
- [frases-cortas] Frases cortas, en el idioma del usuario.

## Por qué
Texto libre opcional: contexto para el agente, nunca reglas.

## Ejemplos
Opcional: bien y mal, cortos.
```

**Campos de la cabecera**

| Campo | Obligatorio | Valores |
|---|---|---|
| `id` | Sí | `tipo.nombre`, en minúsculas y con guiones |
| `tipo` | Sí | `base`, `calidad`, `estilo`, `capacidad`, `mapa`, `leccion`, `perfil` |
| `titulo`, `resumen` | Sí | El resumen cabe en una línea: va en el índice |
| `aplica` | Sí | Patrones de rutas (glob) |
| `etiquetas` | No | Palabras para buscar por tema |
| `prioridad` | No | `obligatoria` o `recomendada` (por defecto) |
| `modo`, `apaga_reglas` | No | Ver capas |
| `idioma` | No | `es`, `en`… El motor elige la traducción según la persona |
| `actualizado` | Sí | Fecha; la actualiza el motor al aprobar un cambio |

**Límites**: menos de 40 reglas y unos 1.500 tokens por contrato. Si crece más, el motor propone dividirlo.

## Contrato base

Siempre se lee. Tiene que ser corto (menos de 600 tokens):
- propósito del proyecto en dos líneas;
- stack y comandos (compilar, probar, ejecutar);
- reglas mínimas que valen para todo;
- cómo usar el índice y las herramientas `contrato_*`.

Lleva `formato: 1` en su cabecera. Lo crea `cpt contracts init`, que detecta el stack y propone un primer borrador.

## Selector: qué lee el agente

**Entradas**: el prompt, los archivos involucrados (los que nombra el prompt, los de la tarea del plan, los que señala el mapa) y el tipo de tarea.

**Cuánto lee**: por defecto **decide el modelo**. El selector entrega lo mínimo (base, obligatorios y lo que coincide) más el índice corto, y el modelo pide el resto según lo que necesite y su ventana de contexto. Si se quiere, se fija un límite a mano en `ajustes.json` (`selector.limite`), con `cpt config set selector.limite 4000` o en cada llamada.

**Orden de carga** (si hay límite, hasta llenarlo):
1. `base.md`.
2. Los obligatorios cuyo `aplica` cubre todo el proyecto.
3. Los que coinciden con las rutas de los archivos involucrados.
4. Las lecciones que coinciden con esas rutas, las más recientes primero.
5. Los que coinciden por etiqueta con palabras del prompt.

Todo lo demás va en un **índice corto** (id y resumen, una línea cada uno). El agente pide lo que le falte con `contrato_leer`.

El selector es determinista: la misma entrada da siempre la misma salida, sin IA ni costo. La búsqueda por significado queda para la extensión del índice semántico.

## Mapa del código

- Si **Graphify** está instalado, `mapa.navegacion` es obligatorio: antes de leer archivos, el agente consulta el mapa (`graphify query` o su MCP) y lee solo lo que el mapa señala.
- El check `mapa-consultado` verifica en el registro de la sesión que se consultó el mapa antes de la primera lectura.
- Sin Graphify, el motor genera un **índice simple**: archivos, símbolos públicos por archivo y carpetas, en `.generado/mapa.json`.
- El mapa también da al selector los archivos involucrados.

## Tamaño y flujo

| Tamaño | Regla por defecto | Flujo |
|---|---|---|
| Pequeño | Hasta 3 archivos y unas 80 líneas, sin tocar áreas de riesgo | Directo, con contratos y checks |
| Mediano | Hasta 10 archivos, o un área de riesgo | Propuesta y etapas |
| Grande | Más de 10 archivos, esquema de BD, API pública o arquitectura | Propuesta, etapas y revisión independiente |

El agente estima el tamaño antes de empezar y lo declara. Si al trabajar lo supera, se detiene y propone pasar al flujo del tamaño siguiente.

**Un cambio** (`cambios/AAAA-MM-DD-tema/`):
- `propuesta.md`: qué y por qué, tamaño, riesgo, **alcance** (rutas permitidas), contratos que aplica y tarea del plan.
- `etapas.md`: etapas pequeñas, cada una con su objetivo y su prueba. Una etapa cabe en un prompt corto.
- `delta.md`: cómo cambia el contrato de la capacidad (reglas nuevas, modificadas o quitadas).

Al terminar, `archivar` aplica el delta al contrato de la capacidad (con aprobación) y mueve el cambio a `archivo/`.

## Checks

Una regla con `` `check: nombre` `` se verifica. Los checks corren sobre los cambios (diff), no sobre todo el repo.

**Incorporados en el motor**

| Check | Qué revisa |
|---|---|
| `sin-emojis` | Emojis en el texto añadido (código, interfaz, Markdown, commits) |
| `sin-guion-largo` | Guiones largos en el texto añadido y en el mensaje del commit |
| `alcance` | Archivos tocados fuera del alcance declarado |
| `sin-coautoria` | Líneas de coautoría de IA en commits y PRs |
| `mapa-consultado` | Que se consultó el mapa antes de leer archivos |
| `tamano` | Que el cambio no supera el tamaño declarado |

**De afuera**: linters, pruebas y otros llegan como comandos en `ajustes.json` o como extensiones de Claupet (ranura `check`).

**Resultado**, en JSON: `{check, regla, archivo, linea, mensaje, severidad}`.

Si un check falla: con **avisar** (por defecto), vuelve al agente para corregir y se ve en Revisión; con **bloquear**, no se aprueba ni se hace commit hasta corregirlo. En los dos casos se propone una lección si es la segunda vez que pasa.

## Riesgo y pruebas

El motor suma puntos y elige el esfuerzo de las pruebas:

| Señal | Puntos |
|---|---|
| Toca auth, pagos, permisos o secretos | +3 |
| Toca el esquema de BD o migraciones | +3 |
| Cambia una API pública | +2 |
| Tamaño mediano / grande | +1 / +2 |
| Hay lecciones sobre esas rutas | +1 cada una (hasta 2) |
| Solo documentación o estilos | -2 |

| Puntos | Esfuerzo | Qué se pide |
|---|---|---|
| 0 a 1 | Bajo | Compila y pasan las pruebas que ya existen |
| 2 a 4 | Medio | Pruebas nuevas para lo cambiado |
| 5 o más | Alto | Casos límite y errores, más revisión independiente |

Las áreas de riesgo se definen en `ajustes.json` con patrones de rutas. La persona puede subir o bajar el nivel en cada cambio.

## Lecciones

```markdown
---
id: leccion.pruebas-de-otra-sesion
tipo: leccion
titulo: No tocar pruebas de otra sesión
resumen: Con varias sesiones, cada una en su worktree
aplica: ["Claupet.Tests/**"]
origen: check-fallido | revision-rechazada | correccion | feedback
visto_en: 1
actualizado: 2026-11-06
---
- **Síntoma:** …
- **Causa:** …
- [worktree-propio] **Regla:** …
```

- Nacen como **propuesta** (en `propuestas/`) y entran al aprobarlas.
- Viven en el proyecto. Si una lección equivalente aparece en dos o más proyectos, el motor propone llevarla al perfil (`visto_en` cuenta los proyectos).
- Una lección que no aplica en 90 días se propone para archivar.

## Feedback, automejora y perfil

**Feedback**: íconos útil / no útil, con comentario opcional, sobre una respuesta, un cambio, un contrato o una lección. **Local por defecto**: se guarda en `.generado/feedback.jsonl`, fuera de git, y un resumen en el perfil. Si se activa `feedback.compartir` en `ajustes.json`, pasa a `feedback/` dentro de `.claupet/contratos/`, versionado con git, y el equipo comparte las señales de la automejora.

**Automejora**: el motor junta señales por contrato y por regla:
- veces leído y veces que sirvió;
- checks fallidos;
- feedback no útil y sus comentarios;
- correcciones que hace la persona después del agente.

Cuando una regla acumula 3 señales del mismo tipo, el agente redacta una **propuesta** como diff en `propuestas/` y explica por qué. Se aprueba, se edita o se descarta. Lo descartado no se vuelve a proponer igual.

**Perfil «cómo trabajo»**: con las mismas señales y la forma de pedir (detalle, idioma, lo que se corrige seguido), el agente propone reglas para `perfil/como-trabajo.md`. Siempre como propuesta.

## El comando `cpt`

Los contratos no tienen un comando propio: son un grupo de **`cpt`**, el comando único de Claupet. `cpt` y `claupet` son el mismo comando y aceptan exactamente lo mismo. Es un ejecutable de consola aparte, pequeño y sin runtime (.NET NativeAOT), para Windows, macOS y Linux. La forma es la de Conda: `cpt <grupo> <acción> [opciones]`.

**Quién lo usa**
- **La persona**, en la terminal: sobre todo `init`, `check`, `validate` y la instalación de la skill y los hooks.
- **El modelo**, por las herramientas MCP (`cpt mcp`). En general no escribe comandos.
- **Claupet**, desde los botones de la vista Contratos.

**Idiomas**: grupos y acciones aceptan su nombre en español y en inglés (son alias). La ayuda y los mensajes salen en el idioma del sistema o en el de `--lang`. Las herramientas MCP tienen **un solo nombre, en inglés**, con la descripción traducida.

**Opciones comunes**: `--json` (salida para scripts), `--lang es|en`, `-y` / `--yes`, `--dry-run`, `-q` / `--quiet`, `--help` / `--ayuda`.

**Grupo `contracts` / `contratos` / `c`**

| Acción (en / es) | Herramienta MCP | Uso habitual | Qué hace |
|---|---|---|---|
| `init` | | Persona | Crea `base.md` y `ajustes.json`, detecta el stack, adapta los paquetes y escribe el puntero |
| `select` / `seleccionar` | `contract_select` | Modelo | Lo que debe leer el agente para un prompt y unos archivos |
| `search` / `buscar` | `contract_search` | Modelo | Busca por id, etiqueta o texto |
| `read` / `leer` | `contract_read` | Modelo | Un contrato ya resuelto por capas |
| `check` / `revisar` | `contract_check` | Los tres | Corre los checks sobre el diff o el commit |
| `change new\|step\|archive` / `cambio nuevo\|etapa\|archivar` | `change_*` | Modelo, Claupet | El flujo de propuesta y etapas |
| `lesson` / `leccion` | `lesson_propose` | Modelo | Propone una lección |
| `feedback` | `feedback_record` | Claupet | Registra útil / no útil |
| `propose` / `proponer` | `contract_propose` | Modelo | Propone una mejora como diff |
| `approve` / `aprobar`, `reject` / `descartar` | | Persona, Claupet | Acepta o descarta una propuesta |
| `eject` / `copiar` | | Persona | Trae un contrato por defecto completo al proyecto |
| `index` / `indice` | | Automático | Regenera `.generado/` |
| `validate` / `validar` | | Persona | Revisa el formato de todos los contratos |

**Los demás grupos de `cpt`**

| Grupo | Ejemplos |
|---|---|
| `plan` | `cpt plan list`, `cpt plan show <archivo>` |
| `canvas` | `cpt canvas list`, `cpt canvas export <id> --pdf` |
| `ext` / `extension` | `cpt ext install x.claupet`, `cpt ext pack <carpeta> <salida>`, `cpt ext verify`, `cpt ext keygen` |
| `skill` | `cpt skill install --agent claude\|codex\|gemini` |
| `hooks` | `cpt hooks install`, `cpt hooks remove` |
| `mcp` | `cpt mcp` (contratos, canvas y planes en un servidor) |
| *(una ruta)* | `cpt .` o `claupet .` abre Claupet en esa carpeta; `cpt archivo.cs:42` abre el archivo en esa línea. `open` / `abrir` existe pero no hace falta |
| `info`, `doctor`, `update`, `config` | Estado, diagnóstico, actualizar y ajustes (`cpt config set selector.limite 4000`) |

**Cómo se interpreta el primer argumento**: si es un grupo conocido, es ese grupo; si no, y es una ruta (`.`, `..`, algo con `/` o `\`, o una carpeta o archivo que existe), se abre en Claupet; si no es ninguna de las dos, error con sugerencias («¿quisiste decir `cpt contracts`?»). Para abrir una carpeta que se llama como un grupo: `cpt ./plan`. Sin argumentos, `cpt` muestra la ayuda.

Las opciones sueltas de `Claupet.exe` (`--extension-pack`, `--canvas-mcp`…) siguen funcionando por compatibilidad, pero la forma recomendada pasa a ser `cpt`.

Las herramientas del modelo que proponen solo escriben en `propuestas/`. **Aprobar es solo de la persona**, por eso no existe como herramienta MCP.

## Cómo se instala y se ejecuta

**Instalación**
- **Con Claupet**: `cpt` viene incluido y queda en el PATH con sus dos nombres, `cpt` y `claupet`.
- **Sin Claupet**: `winget install Claupet.Cpt` (Windows), `brew install cpt-claupet` (macOS), un script de una línea (Linux) o descarga desde GitHub Releases. Los binarios van firmados. Sin la app, `canvas` y abrir una ruta (`cpt .`) avisan que necesitan Claupet; `contracts`, `skill`, `hooks` y `mcp` funcionan completos.
- Después, `cpt skill install` deja la skill lista en los agentes que encuentre.

**Cuatro formas de usarlo**
1. **Terminal**, cualquiera: `cpt .` para abrir el proyecto, `cpt contracts check`, `claupet contratos revisar`, `cpt --help`.
2. **Dentro de un agente**: pidiéndolo en lenguaje normal, con el comando de la skill `/contratos <acción>`, o con `!cpt <grupo> <acción>` en Claude Code.
3. **Claupet**: los botones de la vista Contratos y Ctrl+K.
4. **Automático**: hooks de git y de Claude Code.

`cpt` busca `.claupet/contratos/` desde la carpeta actual hacia arriba, como git, así que funciona desde cualquier subcarpeta del proyecto.

## Skill, puntero y hooks

**Skill `claupet-contratos`** (formato Agent Skills): un `SKILL.md` que explica el método en pocas líneas y le dice al agente que use el motor. Si el motor no está, el **modo simple** lee `base.md` y el índice directamente de los archivos. Se instala con `cpt skill install --agent claude|codex|gemini`.

**Puntero**: una sola línea, entre marcas, en `AGENTS.md` y `CLAUDE.md`:
```
<!-- claupet-contratos -->Este proyecto usa Contratos Claupet: lee .claupet/contratos/base.md antes de empezar.<!-- /claupet-contratos -->
```

**Hooks**, opcionales, con `cpt hooks install`:
- `pre-commit` y `commit-msg` de git: corren `check` sobre lo que se va a commitear.
- Hook de Claude Code (después de editar): corre `check` sobre el archivo cambiado y devuelve los avisos al agente.

## Multiagente e idiomas

**Orquestador + especialistas**
- El orquestador lee la propuesta, divide las etapas y asigna cada una a un especialista (front, back, BD, pruebas).
- Cada especialista trabaja en su propio worktree y recibe solo sus contratos: el selector filtra por las rutas de su etapa.
- Al final, un **revisor independiente** (otra sesión, sin el contexto de quien hizo el trabajo) revisa contra los contratos y los checks.
- En Claupet se usan los Especialistas; fuera de Claupet, los subagentes de cada herramienta.

**Idiomas**: un contrato puede tener traducciones (`escritura.md`, `escritura.en.md`). El motor entrega la del idioma de la persona y, si no existe, la original. Los ids y los nombres de los checks no se traducen.

**Lenguajes**: los paquetes por defecto son generales. Los perfiles por lenguaje (C#, TypeScript, Python, Go…) amplían `calidad.*` con reglas propias y se activan solos al detectar el stack.

## Decisiones tomadas

1. **Comando**: uno solo para todo Claupet, `cpt` o `claupet` (son el mismo), `cpt .` abre la carpeta, con grupos y acciones al estilo de Conda, en español y en inglés. Herramientas MCP con nombre en inglés.
2. **Contexto del selector**: lo decide el modelo; se puede limitar a mano.
3. **Feedback**: local por defecto; se puede compartir por git.
4. **Paquetes por defecto**: dentro del motor y de la skill. `init` los adapta al proyecto con ampliaciones, y `copiar` / `eject` trae uno completo si se quiere.

No quedan decisiones abiertas para empezar el motor.
