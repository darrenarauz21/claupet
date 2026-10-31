# ⌨️ Claupet Terminal

**Español** · [English](../en/terminal.md)

*Nuevo en la 2.5.0.* Trabaje con sus agentes en bloques, no en la pantalla de una terminal: el pedido, lo
que piensa, cada paso con su cambio, los permisos y el cierre del turno, todo a la vista.

**En esta página:** [Qué es](#qué-es) · [Abrir una sesión](#cómo-abrir-una-sesión) ·
[Los bloques](#los-bloques) · [Permisos y preguntas](#permisos-y-preguntas) ·
[Cambiar modelo, esfuerzo y permisos](#cómo-cambiar-el-modelo-el-esfuerzo-o-los-permisos-a-mitad-de-sesión) ·
[Otros agentes](#codex-gemini-cli-y-copilot-cli) · [Shells](#shells-en-bloques) ·
[Proyectos](#proyectos) · [Ramas, Cambios y Procesos](#el-panel-ramas-cambios-y-procesos) ·
[Ajustes › Terminal](#ajustes--terminal) · [Límites](#límites)

## Qué es

Claupet Terminal es una vista de [Claupet Console](console.md) (el botón **Terminal** del riel). Abre
agentes de código y shells en pestañas y muestra cada cosa en su bloque:

- **Claude Code**, **Codex**, **Gemini CLI** y **Copilot CLI**: los que estén instalados en su equipo.
- **Shells**: PowerShell, Git Bash y bash de WSL en Windows; bash, zsh y PowerShell 7 (si está) en Linux y
  macOS. Cada comando con su salida, y los programas de pantalla completa (vim, less, claude) en la sesión entera.
- **Varias sesiones a la vez**, agrupadas por proyecto, cada una con su cuenta, su modelo, su esfuerzo y
  sus permisos.

<p align="center">
  <img src="../../capturas/manual/terminal-sesion.png" alt="Una sesión de Claude Code en bloques en Claupet Terminal" width="720">
</p>

## Cómo abrir una pestaña

1. En Claupet Console, pulse **Terminal** en el riel.
2. Pulse **+** (**Nueva pestaña**), arriba de la lista, o **Ctrl+T**. Se abre un menú chico:

   | Parte | Qué elige |
   |---|---|
   | **Abrir** | Claude Code, otro agente instalado (Codex, Gemini CLI, Copilot CLI) o una de las terminales de su equipo. |
   | **Con la cuenta** | *(Claude Code)* La cuenta de esta pestaña; el punto verde dice que tiene la sesión iniciada. |
   | **Modelo y esfuerzo** | *(Claude Code)* `haiku`, `sonnet`, `opus`, `fable` o «por defecto», y el esfuerzo: bajo, medio, alto, muy alto o máximo. Las variantes (`opus[1m]`, `sonnet[1m]`, `opusplan`) se eligen dentro de la sesión. |
   | **La carpeta** | Abajo, a la izquierda: dónde se abre. Tóquela para elegir otra (los proyectos vinculados, las recientes o **Elegir otra carpeta…**). |

3. Pulse **Abrir** (o **Intro**). Tocar de nuevo lo que ya está elegido también lo abre.

Claupet recuerda lo último que abrió, con su cuenta, su modelo y su esfuerzo. Los permisos empiezan como la
última vez y se cambian en la sesión.

Los modos de **Permisos** de Claude Code:

| Modo | Qué hace |
|---|---|
| **pregunta antes** | Pide permiso para lo que lo necesita (el de siempre). |
| **acepta ediciones** | Edita archivos sin preguntar; lo demás, pregunta. |
| **solo planifica** | Piensa y propone un plan, sin cambiar nada. |
| **automático** | Decide solo cuándo preguntar. |

> [!TIP]
> Puede tener varias sesiones abiertas a la vez, cada una con otra cuenta: por ejemplo, una con su cuenta
> personal y otra con la del trabajo, en el mismo proyecto.

### Cómo escribirle

Escriba en la caja de abajo («Escriba a Claude Code…»). **Intro** envía y **Mayús+Intro** hace otra línea.
Mientras trabaja, **Interrumpir** (las dos barras de arriba, o **Esc**) corta el turno.

Arriba de la sesión se ven la carpeta, su rama y lo que falta subir, y cuánto del contexto lleva (una barra
con su porcentaje; al pasar el mouse, también el uso de la sesión de 5 horas de la cuenta).

### Cómo copiar

- **Lo pedido o una respuesta entera:** pase el mouse por el bloque y toque el ícono de **Copiar**, a la derecha.
- **Una parte:** selecciónela con el mouse y pulse **Ctrl+C**, o haga clic derecho › **Copiar**
  (**Seleccionar todo** elige el bloque entero).

## Los bloques

| Bloque | Qué muestra |
|---|---|
| **El pedido** | Lo que usted escribió, con **›** adelante y en negrita. |
| **Pensando** | Tres puntos que se mueven mientras piensa; al terminar, «Pensó · 1.234 tokens». |
| **La respuesta** | El texto, con los bloques de código aparte. |
| **Un paso** | Qué hizo («Leyó», «Editó», «Creó», «Ejecutó», «Buscó», «Delegó»…) y sobre qué, con las líneas sumadas y quitadas (`+12 −3`). Un clic abre su diff o lo que devolvió. |
| **La lista de tareas** | En una línea, la que hace y cuántas van («2 de 5»). Un clic la abre: ☑ hecha, ◐ en curso, ☐ pendiente. |
| **Un permiso o una pregunta** | Con **?** en ámbar y «pide permiso» hasta que responda. |
| **El cierre del turno** | ✓ y cuánto tardó, los turnos y el costo si el agente lo informa; o ✕ con el error. |

## Permisos y preguntas

<p align="center">
  <img src="../../capturas/manual/terminal-permiso.png" alt="Un permiso de Claude Code en su bloque" width="720">
</p>

Cuando el agente necesita permiso, su bloque dice qué quiere hacer («Quiere ejecutar», «Quiere editar»…),
sobre qué, y el comienzo del comando o del texto. Elija:

- **Permitir**: solo esta vez.
- **Siempre aquí**: lo permite siempre en este proyecto (aparece cuando el agente lo ofrece).
- **Rechazar**.

<p align="center">
  <img src="../../capturas/manual/terminal-pregunta.png" alt="Una pregunta de Claude Code con sus opciones" width="720">
</p>

Cuando le hace una pregunta, el bloque muestra cada pregunta («Pregunta 1 de 2») con sus opciones y su
explicación. Elija una (o varias, si dice «puede elegir varias»), o escriba la suya en **Otra
respuesta…**, y pulse **Responder**. Con una sola pregunta de una opción, el clic ya responde. **No
responder** la rechaza.

> [!TIP]
> **También en la isla.** Si la [isla](isla.md) está encendida, el permiso o la pregunta aparecen también
> allí, para responder sin volver a la Console. Vale la primera respuesta: el bloque dice después
> «Permitido en la isla» o «Respondido en la isla».

En la lista de la izquierda, una pestaña que espera permiso lleva el punto en ámbar.

## Cómo cambiar el modelo, el esfuerzo o los permisos a mitad de sesión

En la caja de abajo, a la derecha (o debajo, si la ventana es angosta), están la cuenta, el modelo con el
esfuerzo («opus · alto») y los permisos.

1. Toque el que quiere cambiar.
2. Elija otro valor.
3. Vale desde el turno siguiente.

La cuenta no se cambia en una sesión abierta: si elige otra, se abre una pestaña nueva con esa cuenta, en la
misma carpeta y con el mismo modelo.

## Codex, Gemini CLI y Copilot CLI

Si están instalados (y en el `PATH`), aparecen en el menú de la pestaña nueva. Se ven en bloques igual que
Claude Code, con sus permisos aquí y en la isla.

- El modelo, el esfuerzo y los permisos se eligen **dentro de la sesión**, en la caja de abajo, entre lo que
  el agente dice tener al abrirse.
- La marca de la pestaña dice qué es: **✻** Claude Code, **cx** Codex, **✦** Gemini CLI, **gh** Copilot CLI y
  **>_** una terminal.

### Cómo iniciar sesión en los otros agentes

Cada agente tiene que tener su sesión iniciada. Si no la tiene, su sesión lo avisa con el comando que hace
falta. Hágalo una vez en una terminal cualquiera y vuelva a escribir en la sesión:

| Agente | Qué ejecutar |
|---|---|
| **Gemini CLI** | `gemini` (y siga los pasos para iniciar sesión) |
| **Copilot CLI** | `copilot login` |
| **Codex** | `codex login`, o inicie sesión en la extensión de Codex de su editor (VS Code, Cursor o Windsurf): si `codex` no está en el PATH, Claupet usa el que trae esa extensión |

> [!NOTE]
> Si un agente no aparece o dice «No encontré … ¿Está instalado y en el PATH?», instálelo y compruebe que
> se abre escribiendo su nombre en una terminal.

## Shells en bloques

En el menú de la pestaña nueva, elija una terminal: Claupet ofrece las que encuentra en su equipo.

| Sistema | Terminales |
|---|---|
| **Windows 10 (1809) o más nuevo** | PowerShell (la 7 si está; si no, Windows PowerShell), Git Bash y bash de WSL |
| **Linux y macOS** | zsh, bash y PowerShell 7 (si está instalado); primero, la de su usuario |

En zsh y bash se cargan sus archivos de siempre (`.zshrc`, `.zprofile`, `.bashrc` o `.bash_profile`); Claupet
solo agrega las marcas de los bloques.

<p align="center">
  <img src="../../capturas/manual/terminal-shell.png" alt="PowerShell en bloques, cada comando con su salida" width="720">
</p>

Cada comando queda en su bloque, con su salida, cómo terminó («Terminó bien (código 0)» o «Terminó con el
código 1») y cuánto tardó.

| Tecla | Qué hace |
|---|---|
| **Intro** | Ejecuta el comando. |
| **↑** / **↓** | Recorren los comandos anteriores. |
| **Ctrl+C** | Interrumpe el que corre (sin nada corriendo, borra lo escrito). |

- Mientras un comando corre, lo que escriba le llega a él (por ejemplo, para responder «¿Continuar? [S/N]»).
  Con el cuadro vacío, las flechas, **Tab**, **Intro** y **Esc** le llegan directo: así se elige una opción
  de una pregunta, como la de confianza de `claude`.
- Al pasar el cursor sobre un bloque aparecen los íconos de **Copiar** y **Repetir**.

### Programas de pantalla completa

Los programas que usan toda la pantalla (`vim`, `nano`, `less`, el paginador de `git log`, `claude`, `htop`)
se ven en la sesión entera, con sus colores y del tamaño del panel, como en cualquier terminal. Las teclas, el
texto, la rueda del mouse y **Ctrl+V** (pegar) les llegan directo. Al salir del programa vuelven los bloques.
- Un bloque muestra las primeras 30 líneas; **Ver más líneas** abre el resto. Se guardan las últimas 400.

## Proyectos

La lista de la izquierda agrupa las pestañas por proyecto (la carpeta). Un proyecto se vincula solo al abrir
una sesión en su carpeta.

- **Desplegado:** su rama y cómo está el repositorio (por ejemplo, «↑2 · 3 sin commit»), con sus
  pestañas debajo.
- **Plegado:** cuántas pestañas tiene y un punto por cada una.
- **Plegar todos los proyectos** / **Desplegar todos los proyectos**: el botón de arriba de la lista.
- **El menú «⋯»** de cada proyecto: **Nueva terminal aquí** (la terminal por defecto), **Abrir un agente u
  otra pestaña aquí…** (el menú de la pestaña nueva, en esa carpeta), **Abrir la carpeta** y **Desvincular**.

> [!NOTE]
> Desvincular solo quita el proyecto de la lista: no borra la carpeta. Si tenía sesiones abiertas, las
> cierra.

El borde de la lista se arrastra para ensancharla, y el botón de arriba la cambia a **Solo íconos**.

## El panel Ramas, Cambios y Procesos

El botón **Ramas, cambios y procesos**, arriba de la sesión, abre un panel a la derecha con tres pestañas:

| Pestaña | Qué muestra |
|---|---|
| **Ramas** | Las ramas del repositorio, qué agente trabaja en cada una, si tiene worktree, lo que falta por commit y si su remota ya no está. Toque la rama actual o una con worktree para abrir ahí una sesión. |
| **Cambios** | Los archivos sin commit con sus líneas sumadas y quitadas. Toque uno para ver su diff. |
| **Procesos** | Lo que abrió cada sesión, con su memoria. **Detener** corta el proceso y lo que abrió. Se actualiza cada 3 segundos. |

<p align="center">
  <img src="../../capturas/manual/terminal-panel-ramas.png" alt="La pestaña Ramas del panel" width="48%">
  <img src="../../capturas/manual/terminal-panel-cambios.png" alt="La pestaña Cambios del panel, con un diff" width="48%">
</p>

> [!NOTE]
> Ramas y Cambios necesitan que la carpeta sea un repositorio de git y que git esté instalado.

## Ajustes › Terminal

Cómo se ven las sesiones. Se abre desde **Ajustes › Terminal** en la Console. Se guarda en este equipo y vale para todas las pestañas.

<p align="center">
  <img src="../../capturas/manual/terminal-ajustes.png" alt="Los ajustes de la Terminal con su vista previa" width="720">
</p>

En tres grupos:

**Pestañas**

| Opción | Valores |
|---|---|
| **Lista de pestañas** | Con descripción o Solo íconos (también con el botón de arriba de la lista). |
| **Terminal por defecto** | La que abre **Nueva terminal aquí**, en el menú de cada proyecto. |

**Apariencia** (arriba, una vista previa que cambia al elegir)

| Opción | Valores |
|---|---|
| **Fuente** | «La de siempre» o una de ancho fijo instalada (Cascadia Mono, Consolas, JetBrains Mono, Fira Code…). |
| **Tamaño** | De 11,5 a 18 pt, con **−** y **+**. |
| **Interlineado** | Junto, Normal o Amplio. |
| **Color del texto** | El del tema, Ámbar, Verde o Hielo. |
| **Paleta de la terminal** | Claupet, Suave o Viva: los 16 colores que usan los programas de pantalla completa. |
| **Cursor** | Bloque, Barra o Subrayado (el de los programas de pantalla completa). |
| **Fondo** | Del tema, Más oscuro o Negro. |
| **Animaciones** | Completas o Ninguna («Pensando», cargando, enviando y al aparecer). Sin ellas, gasta menos. |
| **Ligaduras** | Apagadas, `->` y `!=` se ven como se escribieron; encendidas, en un solo signo (si la letra los tiene). |

**Proyectos vinculados**, cada uno con su carpeta, cuántas pestañas tiene y **Desvincular**. **Lo de siempre**,
arriba, vuelve a los valores de entrada.

## Límites

- **Cerrar Claupet Console cierra todas las sesiones** de la Terminal: ningún agente queda corriendo.
- Los otros agentes tienen que estar instalados y con la sesión iniciada.

---

[← Claupet Console](console.md) · [Índice](../README.md) · [Las mascotas →](mascotas.md)
