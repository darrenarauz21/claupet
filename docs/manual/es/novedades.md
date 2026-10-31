# 🆕 Novedades

**Español** · [English](../en/novedades.md)

Lo que trajo cada versión, de la más nueva a la primera. Las notas completas y las descargas están en
[Releases](https://github.com/darrenarauz21/claupet/releases).

| Versión | Fecha | En una línea |
|---|---|---|
| [2.5.0](#250) | 31 de octubre de 2026 | Claupet Terminal y .NET 10. |
| [2.4.1](#241) | 30 de octubre de 2026 | Correcciones: Gemini CLI, otros agentes, espacio en disco. |
| [2.4.0](#240) | 30 de octubre de 2026 | Una sola app en los tres sistemas, instalador en Windows, temas en tarjetas. |
| [2.3.0](#230) | 29 de octubre de 2026 | Claupet Console, respuestas sin internet, extensiones y actualizaciones automáticas. |
| [2.2.1](#221) | 25 de octubre de 2026 | Corrige el brillo. |
| [2.2.0](#220) | 25 de octubre de 2026 | Capturas, calendario, notas, recordatorios y permisos. |
| [2.1.0](#210) | 24 de octubre de 2026 | La mascota habla, busca en internet y tiene su voz. |
| [2.0.0](#200) | 22 de octubre de 2026 | Chibi, la mascota en el escritorio y apariencia a su gusto. |
| [1.0.0](#100) | 12 de octubre de 2026 | La primera versión. |

## 2.5.0

*31 de octubre de 2026.* Llega Claupet Terminal, y Claupet pasa a .NET 10.

**Nuevo**

- **[Claupet Terminal](terminal.md).** Una vista nueva en Claupet Console para trabajar con agentes en
  bloques: el pedido, lo que piensa, cada paso con su diff, la lista de tareas y el cierre del turno.
  - Claude Code, Codex, Gemini CLI y Copilot CLI, los que estén instalados. Varias sesiones a la vez,
    agrupadas por proyecto.
  - Por sesión: la cuenta de Claude Code, el modelo, el esfuerzo y los permisos.
  - Permisos y preguntas en su bloque y en la isla: vale la primera respuesta.
  - Shells en bloques: PowerShell, Git Bash y WSL, cada comando con su salida, su código de salida y lo que
    tardó.
  - Proyectos plegables y un panel con las ramas, los cambios y los procesos de cada sesión.
  - **Ajustes › Terminal:** letra, tamaño, interlineado, color del texto, fondo, animaciones y una lista
    solo con íconos.
- **Ícono propio de Claupet** en la app, sus ventanas y los paquetes.

**Mejorado**

- **.NET 10.** .NET 8 deja de tener soporte el 10 de noviembre de 2026. Los paquetes siguen trayendo todo: no
  hay que instalar nada.
- **Micrófono.** Si está silenciado o no da ningún sonido, Claupet lo avisa y dice dónde revisarlo, en vez de
  quedarse escuchando sin oír.
- **Modelo local.** En equipos con dos gráficas, muestra la que de verdad usa.

> [!NOTE]
> Codex, Gemini CLI y Copilot CLI tienen que tener la sesión iniciada. Si no, la sesión lo avisa y dice cómo
> hacerlo.

## 2.4.1

*30 de octubre de 2026.* Correcciones sobre la 2.4.0.

- **Gemini CLI.** Lee también el formato nuevo de sus sesiones (`.jsonl`), suma los subagentes y muestra en
  Uso las respuestas y los tokens de hoy.
- **Otros agentes en Uso.** Solo salen los que hoy hicieron algo o tienen un límite que todavía corre.
- **Espacio en disco (Windows).** Borra las bibliotecas que las versiones anteriores dejaban en la carpeta
  temporal (unos 32 MB cada una).
- **Extensiones.** Si una se cierra mientras Claupet le habla, avisa que ya no está corriendo.

## 2.4.0

*30 de octubre de 2026.* Una sola app en Windows, macOS y Linux.

**Nuevo**

- **Una sola app.** Windows usa la misma app que macOS y Linux.
- **Se instala en su carpeta (Windows).** El asistente de configuración suma «Términos y condiciones» e
  «Instalación», con su desinstalador en Configuración › Aplicaciones.
- **Temas propios en tarjetas,** con vista previa, animación al crearlos, calidad y exportar, importar y
  eliminar.
- **Mascotas nuevas** para elegir.
- **Modelo local con la tarjeta gráfica (Windows),** de cualquier marca. Si no arranca con ella, sigue con
  el procesador.

**Mejor**

- **Mucha menos memoria:** unos 220 MB al abrirse (antes, unos 2 GB) y unos 185 MB en reposo.
- **Menos batería:** la mascota, la isla y los avisos solo trabajan cuando algo cambia.
- **El micrófono** carga su modelo solo al oír voz.
- **Uso:** nunca más «Demasiadas consultas».
- **Isla:** las preguntas de Claude Code ya no salen con texto roto.

## 2.3.0

*29 de octubre de 2026.* Claupet Console, respuestas sin internet y extensiones.

- **[Claupet Console](console.md):** una ventana grande con todos los ajustes y el chat.
- **Respuestas sin internet:** primero la app, después el modelo local y, si hace falta, la nube.
- **Modelo local** (Qwen3.5 0.8B, 2B o 4B), a pedido. Solo en Windows.
- **Modelo «Automático»:** elige el modelo y el esfuerzo según la pregunta.
- **[Extensiones](extensiones.md):** paquetes `.claupet` firmados, revisados antes de instalarlos.
- **Versiones nuevas:** Claupet avisa y, con su permiso, las instala sola.
- **Asistente de configuración** por pasos.
- **La mascota** está en todos los escritorios y vuelve al desbloquear o encender la pantalla.
- **macOS y Linux** tienen casi todo lo de Windows, y estrenan los temas propios y los otros agentes
  (Codex CLI, Gemini CLI y GitHub Copilot CLI).

> [!NOTE]
> Los archivos pasaron a llamarse `Claupet-…` en vez de `UsoDeClaude-…`. Los ajustes, los hooks y el inicio
> con el sistema se pasaron solos al nombre nuevo.

## 2.2.1

*25 de octubre de 2026.* El brillo se puede cambiar las veces que haga falta, y si una acción en el equipo
falla, el detalle queda para el diagnóstico.

## 2.2.0

*25 de octubre de 2026.* La mascota le da una mano con Windows y pide permiso antes de hacerlo.

- **Capturas** de la pantalla o de un recorte, y Claude las mira si usted pregunta.
- **Calendario, notas y recordatorios,** aunque la app esté cerrada.
- **Más cosas de Windows:** ventanas, el equipo, el brillo, la batería, el portapapeles y sus carpetas.
- **Pide permiso** la primera vez: «Permitir siempre», «Solo esta vez» o «No».
- **La isla** se pliega con un clic fuera y su chat tiene barra.
- **En español e inglés.**
- **Hecha para redes reales:** sigue el proxy de Windows y renueva sola las sesiones vencidas.

## 2.1.0

*24 de octubre de 2026.* La mascota hace cosas en su equipo, busca en internet y tiene su propia voz.

- **Hablar con Claude** desde la mascota y la isla, por escrito o de viva voz. Suéltele un archivo y se lo
  resume.
- **Internet y clima.**
- **Acciones en el equipo:** fondo, modo oscuro, color, volumen, música y abrir apps.
- **Encarga a Claude Code** lo que no sabe hacer.
- **Su propia voz** (Supertonic 3) y el **micrófono** con Ctrl derecho.
- **La mascota muestra lo que hace cada sesión:** lee, busca, escribe, teclea o construye.

## 2.0.0

*22 de octubre de 2026.* Todavía con el nombre «Uso de Claude».

- **Chibi,** la mascota con hoodie de cangrejo.
- **La mascota en el escritorio:** baila, piensa, duerme y toca maracas cuando se renueva un límite.
- **Hablar:** le cuenta cada aviso en una nubecita.
- **Estilo de sonido «Chibi».**
- **Apariencia:** paletas de color y la animación al actualizar.
- **Sesiones con más detalle** y **Nueva sesión** desde la bandeja.

## 1.0.0

*12 de octubre de 2026.* La primera versión, con el nombre «Uso de Claude»: el uso de Claude por cuenta, las
sesiones de Claude Code y su estado, un calendario de actividad y avisos cuando una sesión termina, pregunta,
pide permiso o falla.

---

[← Preguntas frecuentes](preguntas-frecuentes.md) · [Índice](../README.md)
