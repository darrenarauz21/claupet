# 🛟 Preguntas frecuentes

**Español** · [English](../en/preguntas-frecuentes.md)

Respuestas rápidas a las dudas más comunes y qué hacer cuando algo no va.

**En esta página:** [Privacidad y tokens](#privacidad-y-tokens) · [El ícono y las cuentas](#el-ícono-y-las-cuentas) ·
[Avisos y permisos](#avisos-y-permisos) · [Voz y teclas](#voz-y-teclas) · [Claupet Terminal](#claupet-terminal) ·
[Actualizaciones](#actualizaciones) · [Otros](#otros) · [Cómo pedir ayuda](#cómo-pedir-ayuda)

## Privacidad y tokens

### ¿Claupet gasta mis tokens?

Mirar su uso, la actividad de sus sesiones, el historial y los otros agentes **no gasta tokens**: todo sale de
archivos de su equipo y de la misma consulta que usa `/usage` en Claude Code. Sí gastan, como cualquier
mensaje, las preguntas que van a la nube (el chat, la isla, crear un tema) y lo que hacen las sesiones que
usted abre. Para ahorrar, vea [Quién responde cada pregunta](asistente-y-voz.md#quién-responde-cada-pregunta).

### ¿Qué sabe Claupet de mí?

- Lee lo que Claude Code ya guarda en su equipo. **No guarda contraseñas** y no toca sus credenciales: solo
  las lee.
- Sus ajustes, el historial (solo cifras) y las conversaciones del chat de la Console quedan en su equipo.
- Las respuestas «sin IA» no salen del equipo, salvo el clima (Open-Meteo y, si su ciudad es automática,
  ipwho.is para la ubicación por la IP).
- Para buscar versiones nuevas consulta GitHub, sin iniciar sesión.
- **Copiar diagnóstico** no incluye correos ni tokens.

### ¿Dónde guarda sus datos?

| Sistema | Carpeta |
|---|---|
| Windows | `%APPDATA%\Claupet` |
| macOS y Linux | `~/.config/Claupet` |

Las notas que le pide guardar van, en Windows, en `Documentos\Claupet\Notas`, un `.md` por nota.

## El ícono y las cuentas

### No veo el ícono

- **Windows 11** deja los íconos nuevos en la flechita **^** de la barra de tareas. Arrástrelo afuera.
- **macOS:** está en la barra de menús, arriba a la derecha, no en el Dock.
- **Linux con GNOME:** necesita la extensión AppIndicator (Ubuntu ya la trae).

### Una cuenta dice «Sin credenciales» o no aparece

1. Compruebe que Claude Code tenga la sesión iniciada con esa cuenta.
2. Si usa `CLAUDE_CONFIG_DIR`, agregue esa carpeta con **+** en la vista Uso (ver
   [Primeros pasos](primeros-pasos.md#cómo-agregar-una-cuenta)).
3. **macOS:** Claude Code guarda las credenciales en el Llavero. Cuando macOS pregunte si Claupet puede leer
   «Claude Code-credentials», elija **Permitir siempre**.

> [!NOTE]
> Claupet necesita Claude Code. Si solo usa Claude Desktop o claude.ai, no tiene de dónde leer.

### Dice «Sesión vencida»

El acceso de esa cuenta venció porque no se usó en horas. Pulse **Actualizar** en el menú del ícono: Claupet
le pide a Claude Code que lo renueve y vuelve a consultar.

### Los números no cambian o dice «Consultando…»

Anthropic limita mucho las consultas de uso (las de `/usage` en Claude Code también cuentan). Claupet pregunta
como mucho una vez por minuto por cuenta y, si le piden esperar, muestra los últimos datos buenos: el pie de
la ventana dice de qué hora son. Se pone al día solo.

### Uso en el trabajo detrás de un proxy

Claupet sigue `HTTPS_PROXY` o, en Windows, el proxy manual de Opciones de Internet, y se lo pasa a Claude Code.
La configuración automática (PAC) no se lee.

## Avisos y permisos

### No me llegan los avisos de permisos

1. Revise que **Ajustes › Avisos › Notificaciones** esté encendido.
2. En Claupet Console, use **Ajustes › Sistema › Avisos de permisos (hook) › Configurar…** y aplique.
3. Si sigue igual, use **Copiar diagnóstico** y busque en el resultado:

| Dice | Qué significa |
|---|---|
| «credenciales no» | Claupet no ve esa cuenta y no lee sus sesiones. |
| «sin carpeta sessions» | Esa versión de Claude Code no registra el estado de sus sesiones. |
| «sin hook» | Falta el hook: no habrá avisos de permisos. |

### Los permisos no llegan a la isla

Encienda **Ajustes › Isla › Permisos y preguntas en la isla › Configurar…**. Funciona en las sesiones que se
abran después. Si Claupet está cerrada o nadie responde a tiempo, Claude Code pregunta en la terminal como
siempre.

## Voz y teclas

### El micrófono no me oye

Claupet le avisa si el micrófono está silenciado o no da ningún sonido. Revise:

- **Windows:** **Configuración › Sistema › Sonido › Entrada**: que el micrófono elegido sea el correcto y no
  esté silenciado (o quítele el silencio con la tecla del micrófono). Si dice que no lo encuentra, revise
  **Configuración › Privacidad › Micrófono**.
- **macOS:** **Ajustes del Sistema › Sonido** y **Privacidad y seguridad › Micrófono**.
- **Linux:** que la entrada del sistema sea la correcta y no esté silenciada.

> [!TIP]
> Una entrada virtual (la de un programa de juegos o de streaming, por ejemplo) puede no dar ningún sonido.
> Elija el micrófono de verdad como entrada predeterminada.

### La tecla para hablar no responde

- **macOS:** dé a Claupet el permiso de **Monitoreo de entrada** en **Privacidad y seguridad**.
- **Linux con Wayland:** la tecla solo se ve mientras el foco está en una app de X11. Hable con el botón del
  micrófono de la isla.
- **Windows:** si otra app usa el mismo atajo, **Atajos de teclado** lo marca «en uso». Grabe otro.

### Me entendió frases que no dije

Con ruido de fondo, Whisper a veces «oye» frases que nadie dijo. Acérquese al micrófono o use la tecla para
hablar solo mientras habla.

## Claupet Terminal

### Codex, Gemini CLI o Copilot CLI no aparecen

Tienen que estar instalados y en el `PATH`. Compruébelo escribiendo su nombre en una terminal.

### Un agente dice que necesita iniciar sesión

Ejecute una vez en una terminal el comando que indica (`gemini`, `copilot login`…), inicie sesión y vuelva a
escribir en la sesión. Lo explica [Claupet Terminal](terminal.md#cómo-iniciar-sesión-en-los-otros-agentes).

### Cerré la Console y se cerraron mis sesiones

Es así por diseño: al cerrar Claupet Console no queda ningún agente corriendo. Si quiere que una sesión siga,
deje la Console abierta (puede minimizarla).

### No puedo elegir un shell

Los shells en bloques necesitan Windows 10 (1809) o más nuevo. En macOS y Linux todavía no están.

## Actualizaciones

### No me avisa de las versiones nuevas

- Revise que **Ajustes › Sistema › Buscar actualizaciones** esté encendido. Mira una vez al día.
- Las versiones anteriores a la 2.3.0 no buscan solas: instale la última a mano desde
  [Releases](https://github.com/darrenarauz21/claupet/releases).

### Dice «No se pudo instalar»

Si la firma o el archivo no coinciden, Claupet borra lo descargado y no instala nada: es una protección.
Vuelva a intentarlo más tarde o descargue la versión a mano. Si dice «Esta copia de Claupet no se puede
reemplazar sola», instale la versión nueva a mano.

### Windows dice «Windows protegió su PC»

Claupet no está firmada por Microsoft. Elija **Más información › Ejecutar de todas formas**.

## Otros

### El brillo no cambia

Solo cambia en la pantalla de un portátil: los monitores externos se ajustan con sus botones. En macOS no se
puede cambiar.

### El evento no se guardó en mi calendario

Claupet arma el evento y lo abre en su app de calendario: hace falta un clic para guardarlo.

### La isla no asoma en Linux

Con Wayland, la isla solo ve el cursor mientras está sobre una app de X11, y puede no asomar al acercarlo.

## Cómo pedir ayuda

1. En **Ajustes › Sistema**, pulse **Copiar diagnóstico**.
2. Abra un [issue en GitHub](https://github.com/darrenarauz21/claupet/issues) y pegue el resultado, con lo
   que pasó y su sistema.

> [!IMPORTANT]
> El diagnóstico no lleva correos ni tokens, pero revíselo antes de pegarlo: el repositorio es público.

---

[← Extensiones](extensiones.md) · [Índice](../README.md) · [Novedades →](novedades.md)
