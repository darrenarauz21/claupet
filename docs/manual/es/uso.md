# 📊 Uso, actividad e historial

**Español** · [English](../en/uso.md)

Cuánto le queda de cada cuenta, qué hacen sus sesiones ahora mismo y cómo usó Claude Code en los últimos
seis meses: las tres vistas de la ventana del ícono.

**En esta página:** [Uso](#uso) · [Otros agentes](#otros-agentes-codex-gemini-y-copilot) ·
[Actividad](#actividad) · [Historial](#historial) · [Abrir una sesión nueva](#abrir-una-sesión-nueva)

> [!NOTE]
> Todo lo de esta página sale de archivos de su equipo y de la misma consulta que usa `/usage` en Claude
> Code. Mirarlo **no gasta tokens**.

## Uso

<p align="center">
  <img src="../../capturas/manual/console-uso.png" alt="La vista Uso con la sesión de 5 horas y la semana de una cuenta" width="360">
</p>

Arriba hay un botón por cuenta. Para la elegida se ve:

| Fila | Qué muestra |
|---|---|
| **Sesión** | El uso de la ventana de 5 horas, con su barra y cuándo se reinicia («Reinicia en 2 h 10 min»). |
| **Semana** | El límite semanal, con el día y la hora en que se reinicia. |
| **Semana · modelo** | El límite semanal de un modelo, si su plan lo tiene. |
| **Pie** | Los tokens de hoy en este equipo y la hora de los datos que se ven. |

Claupet actualiza sola cada 5 minutos. Para hacerlo ahora, use **Actualizar** en el menú del ícono.

> [!TIP]
> El número del ícono es el porcentaje de la sesión de 5 horas de la cuenta elegida. Le sirve para ver
> cuánto le queda sin abrir nada.

**Si ve «Consultando…» o datos de hace un rato.** Anthropic limita mucho estas consultas. Claupet pregunta
como mucho una vez por minuto por cuenta y, si le piden esperar, sigue mostrando los últimos datos buenos
(el pie dice de qué hora son) y vuelve a intentarlo más tarde.

**Si ve «Sesión vencida: actualice para renovarla».** Pulse **Actualizar**: Claupet le pide a Claude
Code que renueve el acceso de esa cuenta y vuelve a consultar.

## Otros agentes: Codex, Gemini y Copilot

Si en este equipo usa también Codex CLI, Gemini CLI o GitHub Copilot CLI, Claupet los encuentra solos y
los muestra debajo de la cuenta, bajo **Otros agentes**:

| Agente | Qué se ve en Uso |
|---|---|
| **Codex CLI** | Sus límites de 5 horas y de la semana, con su porcentaje, cuando ya respondió alguna vez. |
| **Gemini CLI** | Las respuestas y los tokens de hoy. Gemini CLI no anota cuánto le queda de su cuota, así que no hay porcentaje. |
| **GitHub Copilot CLI** | Su actividad de hoy (su formato no está documentado, así que se ve menos). |

Solo salen los que hoy hicieron algo o tienen un límite que todavía corre. Se eligen cuáles seguir en
**Ajustes › Cuentas** de Claupet Console. Claupet solo lee sus archivos: no usa sus cuentas ni gasta
tokens.

> [!NOTE]
> Desde el 18 de junio de 2026, las cuentas gratuitas y de Google One de Gemini usan Antigravity CLI, que
> Claupet todavía no lee.

## Actividad

<p align="center">
  <img src="../../capturas/manual/console-actividad.png" alt="La vista Actividad con varias sesiones y sus pasos" width="360">
  <img src="../../capturas/manual/console-actividad-detalle.png" alt="El detalle de una sesión, con su mapa de pasos" width="360">
</p>

Cada sesión de Claude Code abierta en su equipo, con su estado y su carril de pasos en vivo («Leyendo ×3»,
«Pensando…», el subagente que trabaja):

| Estado | Señal |
|---|---|
| **Trabajando** | Asterisco animado y sus pasos. Con lista de tareas, «3 de 5 tareas». |
| **Esperando su respuesta** | Punto ámbar que late: le hizo una pregunta. |
| **Falló** | Cruz roja y el texto del error. |
| **Terminó** | Marca verde y hace cuánto («hace 5 min»). |
| **Inactiva** | Punto gris. Las de hace más de 30 minutos van plegadas al final. |

- **Clic en una sesión:** su detalle, con el último pedido, la carpeta, el modelo, el mapa de las
  herramientas que usó en el turno y los últimos pasos con su hora.
- **Clic derecho:** **Nueva sesión en esta carpeta…**, **Abrir carpeta del proyecto** o
  **Copiar ID de la sesión**.

Las sesiones de Codex, Gemini y Copilot de las últimas 24 horas salen también, con su carpeta y su modelo
si se saben.

## Historial

<p align="center">
  <img src="../../capturas/manual/console-historial.png" alt="El calendario de seis meses de uso" width="360">
</p>

Un calendario con una casilla por día de los últimos 6 meses, de todas sus cuentas. El color sube con los
tokens del día; con el cursor encima se ve la cifra. Debajo: tokens del período, días activos, racha actual
y más larga, el día más activo y su modelo favorito.

> [!NOTE]
> Claude Code borra sus registros de más de 30 días. Para no perder el historial, Claupet guarda un resumen
> diario, solo con cifras, en su propia carpeta.

## Abrir una sesión nueva

Desde **Nueva sesión…** en el menú del ícono (o el clic derecho de una sesión en Actividad):

1. Elija la **Cuenta**, la **Carpeta** (con **Examinar…**), el **Modelo**, el **Esfuerzo** y el
   **Asesor**. «Predeterminado de la cuenta» deja lo que la cuenta tenga.
2. Pulse **Abrir**.
3. Claude Code se abre en una terminal: Windows Terminal (o PowerShell) en Windows, Terminal en macOS y la
   primera que encuentre en Linux (gnome-terminal, konsole, kitty…).

Lo que elija vale solo para esa sesión: no cambia los ajustes de la cuenta.

> [!TIP]
> ¿Prefiere ver la sesión en bloques, con sus permisos y sus cambios a la vista? Ábrala en
> [Claupet Terminal](terminal.md).

---

[← Primeros pasos](primeros-pasos.md) · [Índice](../README.md) · [Claupet Console →](console.md)
