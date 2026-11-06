# 🪟 Claupet Console

**Español** · [English](../en/console.md)

La ventana grande de Claupet: el chat, la Terminal, el editor, Canvas, Proyectos y todos los ajustes, en un solo lugar.

**En esta página:** [Cómo abrirla](#cómo-abrirla) · [El riel](#el-riel) · [Chat](#chat) ·
[Editor](#editor) · [Canvas](#canvas) · [Proyectos](#proyectos) · [Ajustes](#ajustes) · [Accesos directos](#accesos-directos)

## Cómo abrirla

Cualquiera de estas formas abre la Console (o la trae al frente, si ya está abierta):

- **Claupet Console** en el menú del ícono.
- **Todos los ajustes › Abrir**, al pie de los ajustes rápidos.
- Su acceso directo del menú Inicio o del escritorio (Windows).
- Abrir Claupet otra vez mientras ya está abierta.

Hay una sola Console a la vez. Sigue el modo claro u oscuro del sistema y el color o tema que haya elegido.

> [!NOTE]
> El chat y los ajustes los atiende Claupet, la del ícono. Si no está abierta, la Console lo dice y ofrece
> **Abrir Claupet**. La Terminal y el editor funcionan igual sin ella.

## El riel

A la izquierda, un riel con seis botones:

| Botón | Para qué |
|---|---|
| 💬 **Chat** | Conversar con Claupet y guardar las conversaciones. |
| ⌨️ **Terminal** | Sesiones de agentes y shells en bloques. Tiene su propia página: [Claupet Terminal](terminal.md). |
| 📝 **Editor** | Abrir y escribir documentos y código. |
| 🎨 **Canvas** | Documentos vivos que usted y cualquier IA editan a la vez. |
| 📊 **Proyectos** | Los planes de sus proyectos: tablero, cronograma, diagrama y métricas. |
| ⚙️ **Ajustes** | Todas las opciones de Claupet, por secciones. |

## Chat

A la izquierda, la lista de conversaciones; a la derecha, la abierta.

- **Conversación rápida:** es la misma de la isla y de la nubecita de la mascota.
- **Las guardadas:** cada una con su título, agrupadas en Hoy, Ayer, Esta semana y Antes. Se guardan en este
  equipo.

### Cómo conversar

1. Pulse **Chat nuevo** (o elija una conversación de la lista).
2. Escriba en «Escríbale a Claupet». **Intro** envía y **Mayús+Intro** hace otra línea.
3. Si quiere, use **Adjuntar un archivo** o **Hablar** (con el micrófono encendido).
4. Mientras responde, **Detener** la corta.

Junto al cuadro puede **Cambiar de modelo** (**Automático**, que elige el modelo y el esfuerzo según la
pregunta; «El de sus sesiones»; Haiku, Sonnet, Opus o Fable) y encender **Leer en voz alta las respuestas**. Los bloques de
código traen **Copiar**, y debajo de cada respuesta se ve quién respondió.

> [!TIP]
> Responde el mismo asistente de la mascota y la isla, con respuestas más completas. Lo explica
> [Asistente y voz](asistente-y-voz.md).

Para borrar una conversación guardada, use **Borrar la conversación…** (pide confirmarlo). La rápida se
vacía con **Vaciar la conversación**.

## Editor

Un editor sencillo para documentos y código.

| Qué | Cómo |
|---|---|
| **Texto** | El Markdown se ve sin sus marcas (títulos, negritas, enlaces, viñetas), salvo en la línea del cursor. |
| **Código** | Números de línea y colores para C#, JavaScript, Python, PowerShell, SQL, HTML, CSS, JSON, XML y otros. |
| **Archivos** | **Mostrar los archivos** lista los abiertos y los recientes. Las pestañas llevan un punto si hay cambios sin guardar. |
| **Cambios de afuera** | Si un agente u otro programa cambia un archivo abierto, se recarga solo (Ctrl+Z lo devuelve). Si usted tiene cambios sin guardar, pregunta qué conservar; guardar tampoco pisa nada sin preguntar. |

| Tecla | Qué hace |
|---|---|
| Ctrl+N | Nuevo |
| Ctrl+O | Abrir… |
| Ctrl+S | Guardar |
| Ctrl+Mayús+S | Guardar como… |
| Ctrl+W | Cerrar |

Guarda en UTF-8 y, si cierra un archivo con cambios, pregunta antes.

## Canvas

Documentos vivos que usted y cualquier IA editan a la vez, al estilo de un canvas. Un canvas es una lista de secciones,
cada una con su tipo:

| Tipo | Qué es |
|---|---|
| **Texto** | Markdown: títulos, listas, tablas, enlaces. |
| **Código** | En cualquier lenguaje. |
| **HTML** | Una página o una app entera (con su CSS y JavaScript). Se ve en el navegador, aislada. |
| **Diagrama** | Mermaid. Se dibuja en la app; los que no sabe dibujar se ven como código, y dibujados en el navegador y el PDF. |
| **SVG**, **Datos** (JSON o CSV, que se ve como tabla o gráfico) e **Imagen** | Gráficos, tablas y resultados, imágenes. |
| **Plan** | Etapas y tareas con resumen, tablero y cronograma. |

**Con los agentes.** Pídale a un agente de la [Terminal](terminal.md) «hazlo en un canvas»: Claude Code, Codex, Gemini
CLI y Copilot reciben Claupet Canvas al abrirse (es un servidor MCP), con el modelo que usen. El canvas aparece en la
Console y se actualiza mientras el agente lo edita; lo que acaba de cambiar se marca un momento. El agente no reescribe
todo: cambia el pedazo que hace falta, así que puede seguir escribiendo usted en otra sección.

**Comentarios.** El botón de comentario de cada sección deja una nota (para la IA o para usted). Los agentes los leen,
los responden y los marcan como resueltos.

**Versiones.** Cada cambio, suyo o de un agente, es una versión nueva. **Versiones** muestra quién cambió qué y permite
volver a una anterior (la de ahora queda guardada).

**Editar.** El lápiz de cada sección la abre para escribir. Ctrl+Intro o un clic afuera la guarda, y también cambiar de
canvas o cerrar la ventana; **Esc descarta lo escrito**. Ctrl+K busca e inserta, Ctrl+N crea un canvas y Ctrl+J le pide un
cambio a la IA.

**Ligar a un plan.** El botón de enlace de la barra liga el canvas a un plan. Se ve su avance en la barra y, al tocarlo,
sus detalles en [Proyectos](#proyectos). Un canvas se liga a un solo plan; un plan puede tener varios canvas. Un agente
también puede ligarlos.

**Exportar.** PDF, Word, Markdown, una página HTML que se abre sola en cualquier navegador, o JSON. También **Ver en el
navegador**. El PDF necesita Microsoft Edge, Google Chrome o Chromium. En Word, los diagramas y el HTML van como texto.

**Desde otras apps.** **⋯ › Usar desde otras apps** copia la configuración MCP para pegarla en Claude Desktop, Cursor,
VS Code o cualquier app con MCP: sus modelos verán y editarán los mismos canvas. A mano, el comando es
`Claupet --canvas-mcp`.

Se guardan en la carpeta de datos de Claupet, en `Canvas` (en Windows, `%APPDATA%\Claupet\Canvas`).

## Proyectos

Los planes (`*.plan.json`) de las carpetas vinculadas en la Terminal, cada uno con su avance y sus atrasos. Los escriben las
IAs (con las herramientas `plan_*`) o usted: el archivo sigue en la carpeta del proyecto, y lo que cambia ahí se ve solo. El
**+** crea un plan o abre uno de otra carpeta.

| Vista | Qué muestra |
|---|---|
| **Resumen** | El avance, lo atrasado y bloqueado, lo que se puede hacer ahora y cada etapa. |
| **Tablero** | Las tareas por estado. Arrastre una tarjeta a otra columna para cambiar su estado. |
| **Cronograma** | Las barras por día, las dependencias, los hitos y la línea de hoy. |
| **Diagrama** | Qué depende de qué. Al pasar el mouse se resalta la cadena de una tarea; Ctrl+rueda acerca o aleja. |
| **Métricas** | El avance real contra el ideal y las horas reales contra las estimadas. |
| **Actividad** | Quién hizo qué y cuándo, desde que Claupet ve el plan. |

**Buscar.** Arriba de la lista se busca por proyecto o por tarea; la lupa de la barra atenúa lo que no coincide dentro del
plan, y **Ctrl+K** busca en todos. El embudo filtra por atrasadas, bloqueadas, listas o en curso. Las teclas 1 a 6 cambian de vista.

**Una tarea.** Al tocarla se abre su detalle: su estado (se guarda en el archivo), sus horas, lo que necesita y lo que la
espera. **Trabajar en esto** abre una sesión de Claude Code en la carpeta del proyecto con la tarea ya escrita, para que usted la
envíe. La barra de abajo le pide un cambio a una sesión de esa misma carpeta.

**Canvas del plan.** El botón de canvas de la barra abre los que están ligados a este plan o crea uno nuevo, ya ligado.

**Exportar a PDF.** El botón de la barra guarda la vista que se ve o todo el plan como un informe: hojas carta en blanco,
con el mismo contenido en tablas y gráficos pensados para leer o imprimir, sea cual sea el tema. Lo que no cabe sigue en
la hoja siguiente.

## Ajustes

Todas las opciones de Claupet, en secciones: **General**, **Avisos**, **Apariencia**, **Mascota**,
**Asistente**, **Respuestas**, **Extensiones**, **Isla**, **Cuentas** y **Sistema**, más **Terminal**. Los
ajustes rápidos del ícono traen solo lo de todos los días; aquí está todo.

Las listas para elegir se manejan también con el teclado: flechas, **Intro** para elegir y **Esc** para
cerrar.

Cada opción está explicada en [Ajustes](ajustes.md).

## Accesos directos

*Solo en Windows.* En **Ajustes › General › Accesos directos**:

- **En el menú Inicio** (encendido de entrada).
- **En el escritorio**.

Los dos abren Claupet Console con el ícono de Claupet. Si Claupet no estaba abierta, la abren también.

---

[← Uso, actividad e historial](uso.md) · [Índice](../README.md) · [Claupet Terminal →](terminal.md)
