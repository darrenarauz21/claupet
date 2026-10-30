# Claupet: detalles técnicos

Lo que el [README](README.md) no cuenta: de dónde sale cada dato, dónde se guarda cada cosa, cómo
se compila y cuáles son los límites conocidos.

## Estructura

```text
Core/                    núcleo común, sin interfaz: uso, sesiones, actividad, avisos, sonidos,
                         hook, credenciales, inicio con el sistema, diagnóstico y la voz (Voice/)
Console/                 Claupet Console (Avalonia 11), la ventana grande: el mismo Claupet.exe con --console
Claupet.Avalonia/        la app (Avalonia 11), la misma en Windows, Linux y macOS: bandeja, mascota, isla, voz
Claupet.Tests/           pruebas (xUnit)
herramientas/            empaquetado (empaquetar.py), scripts de instalación y Info.plist de macOS
app.ico                  ícono de la mascota
```

Hasta la 2.3.0, Windows tenía su propia app en WinForms; desde la 2.4 es la misma de Linux y macOS, y lo que es de
un sistema va con `OperatingSystem.IsWindows()` y compañía.

## Idiomas

Español e inglés (`Core/Language.cs`). El texto en español del código es la clave: `L.T("Actualizar")`
da su traducción del idioma elegido (`Core/Idiomas/en.json`, dentro del ejecutable) o el mismo texto si
es español o falta; con valores, `L.T("Reinicia en {0}", x)`, como `string.Format`. El idioma sale de
`Language` en `settings.json` o, sin elegir, del sistema (español si está en español; si no, inglés), y
se fija al abrir la app: al cambiarlo en **Ajustes › Sistema**, la app se reinicia (`--reiniciar`). El
idioma también decide la cultura (fechas y horas), lo que se le pide a Claude, el micrófono (Whisper),
la voz de Claupet (Supertonic, `<es>`/`<en>`) y la voz de Windows. Los nombres de las acciones de la
mascota (`[ACCION]`) son del protocolo y van siempre en español. Para otro idioma: un
`Core/Idiomas/<código>.json` con las mismas claves y una entrada en `L.Choices`.

## Dónde se guarda cada cosa

| Qué | Windows | Linux y macOS |
|---|---|---|
| Configuración de la app | `%APPDATA%\Claupet\settings.json` | `~/.config/Claupet/settings.json` |
| Historial diario (actividad) | `…\Claupet\history.json` | `…/Claupet/history.json` |
| Eventos del hook | `…\Claupet\events.jsonl` | `…/Claupet/events.jsonl` |
| Sonidos | `…\Claupet\sonidos` | `…/Claupet/sonidos` |
| Modelo de voz (Whisper) | `…\Claupet\voz\ggml-small.bin` | — |
| Voz de Claupet (Supertonic 3) | `…\Claupet\voz\supertonic-3` | — |
| Modelo local (llama-server y el GGUF elegido) | `…\Claupet\modelo-local` | — |
| Respuestas sin la nube de la semana (Ajustes › Respuestas › Ahorro) | `…\Claupet\respuestas.json` | — |
| Consultas del asistente (prompts de sistema) | `…\Claupet\asistente` | — |
| Permisos de lo que la mascota hace en el equipo | `Permissions` en `settings.json` | — |
| Notas | `Documentos\Claupet\Notas`, un `.md` por nota | — |
| Recordatorios | `…\Claupet\recordatorios.json` y, por cada uno, la tarea `\Claupet\Recordatorio <id>` del Programador de tareas | — |
| Eventos para el calendario | `…\Claupet\calendario` (los 10 últimos `.ics`) | — |
| Capturas de pantalla | la carpeta Capturas de pantalla de Windows; la que mira Claude, sola en `…\Claupet\capturas` | — |
| Credenciales de Claude Code (solo se leen) | `.credentials.json` de cada carpeta | Linux: igual; macOS: el Llavero |
| Iniciar con el sistema | registro, `HKCU\…\Run` | Linux: `~/.config/autostart/claupet.desktop`; macOS: `~/Library/LaunchAgents/local.claupet.plist` |
| Versión nueva bajada (hasta instalarla) | `…\Claupet\actualizacion\<versión>` | `…/Claupet/actualizacion/<versión>` |
| La app instalada | `%LOCALAPPDATA%\Programs\Claupet\Claupet.exe` (o la carpeta elegida) y `claupet.ico` | Linux: `~/.local/share/claupet`; macOS: Aplicaciones |
| Entrada de Configuración › Aplicaciones | registro, `HKCU\…\Uninstall\Claupet` | — |
| Términos aceptados | `TermsAccepted` en `settings.json` (la versión) | igual |

En macOS, Claude Code guarda las credenciales en el Llavero: «Claude Code-credentials» para
`~/.claude` y, desde Claude Code 2.1.144, «Claude Code-credentials-» seguido de los 8 primeros
caracteres del SHA-256 de la ruta para cada `CLAUDE_CONFIG_DIR`. La app las lee con
`security find-generic-password`; macOS puede pedir permiso la primera vez.

## Uso

Un botón por cuenta y, para la elegida, la sesión (5 h), la semana y la semana por modelo, con su
barra y cuándo reinicia. Al pie, los tokens de hoy en este equipo. Al actualizar (a mano o cada
5 minutos), la animación de lo elegido en **Ajustes › Apariencia › Mascota** ocupa el ícono y la
hora del pie. La de siempre es la mascota clásica: abre los ojos, mira a los lados mientras llega la respuesta y hace `> <`
antes de mostrar el estado nuevo. Con la mascota chibi, lo mismo lo hace su cabeza, que termina con
`> <` y boca de gatito (`:3`).

**Sesión vencida.** La app no toca las credenciales de Claude Code (para no pisarlas): cuando el token
de una cuenta venció, corre una consulta mínima de Claude Code con esa cuenta (`claude -p` con Haiku,
`--safe-mode`, sin herramientas ni sesión guardada) y es Claude Code quien lo renueva; después vuelve
a consultar (`SessionRenewal`). Se hace al abrir la app, con todas las cuentas vencidas (si no hay
conexión, en la actualización siguiente), y con **Actualizar** sobre la cuenta que se está viendo.
`claude auth status` no sirve para esto: no renueva el token.

**Proxy.** Las consultas salen por el proxy de cada momento (`SystemProxy`): `HTTPS_PROXY` si está y,
si no, en Windows el proxy manual de Opciones de Internet, leído en cada conexión (el de .NET se fija
al arrancar y no ve un proxy que se activa después). Claude Code no lee el proxy de Windows: la app
se lo pasa como `HTTPS_PROXY` al chat, a la renovación y a las sesiones que abre. La configuración
automática (PAC) no se lee.

## Apariencia

En **Ajustes**, con el mismo patrón que el estilo de sonido: una lista, la elegida marcada y una
muestra al elegir.

- **Color:** paletas fijas, cada una con su tono para el modo claro y el oscuro (el modo sigue al
  sistema): Terracota (`#D97757`, la predeterminada), Océano, Bosque, Lavanda, Rosa y Grafito. Cambia
  el acento: barras, interruptores, enlaces, el número del ícono y la mascota del ícono, del pie y
  de los avisos. Fondos, textos y colores de estado (peligro, aviso, éxito) no cambian. Están en
  `Theme.Palettes` (`Core/Theme.cs`). Elegir una quita el tema propio, si había (ver «Temas propios»).
- **Mascota:** junta la mascota y la animación al actualizar en una sola lista (`Mascots`, en
  `Core/ChibiSprite.cs`). Clásica (de un color, el acento) o Chibi (ver abajo): esa mascota anima el
  ícono y sale en los avisos. Asterisco (el ✻ de Claude Code, que crece y se encoge), Puntos (una ola
  que termina en sonrisa) o Ninguna (el ícono sigue con el porcentaje y el pie dice «Actualizando…»):
  ese es el estilo al actualizar y los avisos llevan la clásica. Todos usan los mismos tiempos de
  `RefreshAnimator`; los dibujos están en `RefreshSprite` (`Core/Sprites.cs`). Al elegir uno, la app
  actualiza para mostrarlo.
- **Mascota en el escritorio:** la mascota (el chibi o la clásica) en su propia ventana; se enciende
  sola al elegir el chibi.
- **Hablar:** con la mascota en el escritorio, cuenta cada aviso en una nubecita, a su manera
  (`MascotLines`: de tú y con caritas), además del aviso de siempre.
- **Un interruptor por comportamiento** de la mascota del escritorio, todos encendidos al empezar:
  gestos del flujo (si se apaga, baila mientras trabaja, como antes), bailar con música, gesto de los
  avisos, saludar al aparecer, enojarse con muchos clics (si se apaga, siempre saluda) y dormir sin
  sesiones (si se apaga, se queda quieta). En Linux y macOS no está «Bailar con música»: la música
  sale del control multimedia de Windows.

Se guardan en `Palette`, `Animation` (el estilo al actualizar), `Mascot` (la de los avisos),
`DesktopMascot`, `Speak`, `FlowGestures`, `DanceToMusic`, `NoticeGestures`, `GreetOnAppear`,
`AngryOnClicks`, `SleepWithoutSessions` y `MascotAllDesktops` del `settings.json` de la app.

## Chibi

Una mascota con pelo naranja y hoodie de cangrejo (ojitos sobre antenas), en pixeles y con colores
propios: no cambia con la paleta. Está en `ChibiSprite` (`Core/ChibiSprite.cs`): el cuerpo se
escribe como su mitad izquierda, una letra por color, y se espeja; cada cuadro cambia ojos, boca,
ojitos de cangrejo y brazos, mueve la cabeza o el cuerpo y agrega efectos. El lienzo es de 40 × 52
casillas. La cabeza, para la bandeja y el pie, es la del cuerpo (32 × 32); en íconos de menos de
32 px (la bandeja de Windows al 100 o 150 %) va una de 16 × 16, que se ve nítida.

| Movimiento | Cuándo |
|---|---|
| Quieta (parpadea y mira) | hay sesiones, ninguna trabaja |
| Leer (un libro; los ojos van por los renglones y pasa la página) | la sesión lee (Read) |
| Buscar (mira por la lupa con un ojo) | busca (Grep, Glob, ToolSearch) |
| Editar (escribe en la libreta, renglón por renglón) | edita (Edit, Write, NotebookEdit) |
| Ejecutar (teclea detrás de la laptop, con la pantalla en los ojos) | ejecuta (Bash, PowerShell) |
| Web (el globo gira y los ojitos de cangrejo hacen de antenas) | busca o lee en la web |
| Agentes (señala, aparece un ayudante con un «puf» y lo observa) | delega en un subagente |
| Pensar el siguiente paso (una nube con tres puntos) | trabaja y el último paso ya volvió, hasta 8 s |
| Construir una casita con el mazo (de rodillas, de pie, de espaldas y del otro lado) | otra herramienta (MCP, skills, la lista de tareas…); también si pasan 8 s pensando sin otro paso (escribe un archivo largo o piensa mucho), hasta que llega el siguiente |
| Festejar la casita (lista; salta contenta) | esa otra herramienta terminó |
| Bailar | alguna sesión trabaja, con «Gestos del flujo» apagado; sin trabajo y con música |
| Pensar (manito en la boca) | alguna espera su respuesta; en los avisos de pregunta, permiso, reintento, uso alto y sesión vencida |
| Dormida | no hay sesiones; aviso de sesión cerrada |
| Festejar | terminó |
| Chachachá con maracas | se renovó un límite (sesión o semana) |
| Mareada | falló; uso crítico |
| Saludar | al aparecer y con un clic |
| Puchero | 3 a 5 clics seguidos |
| Brazos cruzados («¡hmph!») | 6 a 8 clics seguidos |
| Furia (roja, brazos en Λ, humo y 💢) | 9 clics seguidos o más; se le pasa a los 4 s sin clics |
| Se calma (suspira y mira de reojo a los lados, buscando quién fue) | al pasársele el puchero, los brazos cruzados o la furia; después vuelve a lo que corresponda |

Con varias sesiones, `ChibiDirector` elige primero por importancia y, dentro del mismo nivel, la
sesión con el último paso: una pregunta o un permiso en cualquiera (interrumpe enseguida); lo que
cambia cosas (editar, ejecutar, agentes); lo que consulta (leer, buscar, web, otras herramientas);
pensar. Cada gesto del flujo dura al menos una vuelta (la casita, unos 3 s), así que se ven también
los pasos de milisegundos: la app lee las sesiones cada 1,5 a 3 s y de cada una mira solo el último
paso. Los gestos del flujo están en `ChibiFlows` y la casita, con las vistas de perfil y de espaldas
que solo usa ella, en `ChibiBuild`.

Con música (algo reproduciéndose en el control multimedia de Windows, `Core/Music.cs`: Spotify,
YouTube en el navegador…), el gesto del flujo cabecea al compás y suelta notitas; al ejecutar se pone
audífonos y mece la cabecita. Sin trabajo, baila. Solo se mira si hay algo reproduciéndose, no qué
suena, y solo con «Bailar con música» y la mascota en el escritorio.

Con un aviso, la mascota del escritorio hace el mismo gesto del aviso unos segundos y después vuelve
a lo que corresponda (con «Gesto de los avisos» encendido).

En los avisos asoma desde abajo (de la cabeza al ruedo). En el escritorio vive en una ventana sin
marco ni fondo, siempre encima, que no quita el foco: se arrastra para moverla, sin salirse de la
pantalla (la posición queda en `DesktopMascotX` y `DesktopMascotY`), y el clic derecho la abre o la
oculta. Con **En todos los escritorios** (`MascotAllDesktops`, encendido de entrada) sigue a la vista al cambiar de
escritorio virtual: en Windows la ventana va sin dueña y con `WS_EX_TOOLWINDOW` (`WinStyles.cs`), fuera de la barra y
de Alt+Tab; en macOS pide `NSWindow.collectionBehavior` con todos los Espacios. En Windows, apagarlo todavía no la ata
a un solo escritorio. Al volver de bloquear o suspender, o si cambian las
pantallas (apagarse un monitor lo desconecta y Windows mueve las ventanas), vuelve a su lugar o al más cercano que se
vea, arriba de todo y repintada: enseguida y otra vez a los 2 y 6 s, mientras los monitores se reconectan. `ChibiDirector` decide qué hace según las sesiones, que la app sigue leyendo mientras la
mascota esté a la vista aunque las notificaciones estén apagadas. La mascota clásica también puede
estar en el escritorio, con los mismos movimientos armados con sus dibujos de los avisos
(`ToastSprite.PaintMove`; los gestos del flujo, en `ToastFlows`, con el objeto al costado o encima, y
para otras herramientas martilla). La nubecita de «Hablar» es de pixeles (`BubbleSprite`), va arriba de la
mascota (o abajo, si no cabe), la sigue al arrastrarla y se va a los 8 s; un clic abre lo mismo que
el aviso.

### Personajes

Además del chibi (el de Claude) hay otros personajes sobre la misma base, que se eligen en **Ajustes › Apariencia ›
Mascota** como el chibi: **Dokki** (DeepSeek), **Gemma** (Gemini) y **Ciel** (Codex). Cada uno es un `ChibiSkin`
(`Core/ChibiSkins.cs`, y su dibujo en `Core/Pets/`) con el mismo esqueleto: la cara, los ojos, la boca, los brazos y las
piernas en las mismas casillas, así que hace todos los movimientos de arriba. Lo propio de cada uno:

| Parte | Qué es |
|---|---|
| `Body`, `Head`, `Armless` | su mitad izquierda con las letras de siempre (O la ropa, R el pelo, S la piel…), que se espeja |
| `Colors` | los colores de sus letras; las de los efectos, la caja y los objetos del flujo son las de la base |
| `Accent` | lo que reemplaza a los ojitos de cangrejo según hacia dónde mira: el rizo de Dokki, las cejas tsundere de Gemma, el brillo del gancho de Ciel |
| `Overlay` | lo que no es simétrico, de frente: la ballena del delantal de Dokki, el gancho de Ciel |
| `Behind` | lo que va detrás del cuerpo y puede salirse de él: el pelo largo, las aletas y la cola de Dokki, las coletas de Gemma |
| `ArmTint` | el color del puño en los brazos levantados |
| `Lid` | la tapa de la laptop al ejecutar con el logo de su IA (el cangrejito de Claude es solo del chibi) |

Todavía no tienen las vistas de perfil y de espaldas de la casita: en «otra herramienta» escriben en la libreta y al
terminar festejan. Los cuadros se arman la primera vez que se pinta cada personaje y quedan guardados.

**Gestos propios** (`Moves` de cada uno, que reemplazan los de la base):

| Personaje | Gestos |
|---|---|
| Dokki | saluda y termina con una reverencia (las manos en la falda); festeja con el chorrito de ballena; duerme soltando burbujas; quieta, a veces bosteza |
| Gemma | quieta, a ratos se cruza de brazos, mira a otro lado con los cachetes rojos y resopla («¡hmph!»); festeja y enseguida disimula; saluda de mala gana |
| Ciel | piensa con un «</>» verde; festeja sin saltar, con un brazo arriba y el visto bueno |

**Voces.** Cada uno tiene su estilo de sonido (`Sounds`, «dokki», «gemma» y «ciel»), sintetizado como el «oi» del
chibi: la altura sube a un pico y cae, y la vocal pasa de una a otra. Cambia el timbre: Dokki, grave y con aire, con un
vibrato lento; Gemma, aguda y cortante; Ciel, clara y serena. Si suena la voz de un personaje y se elige otro, pasa a
sonar la suya. El asistente también toma su personalidad (`AssistantRequest.Mascot`).

**Una mascota por agente** (`TrayController.AgentPets.cs`, Ajustes › Apariencia › Mascota en la Console): junto a la de
Claude Code, una por cada otro agente que se sigue y está en el equipo, con el personaje elegido (`AgentPet`; si no,
Ciel para Codex y Gemma para Gemini, y Copilot sin mascota). De esos agentes solo se sabe cuándo escribieron en sus
sesiones: cada 15 s se mira la fecha del archivo más nuevo (`Agents.LastWrite`, sin leerlo). La mascota teclea si fue
hace menos de 45 s, queda quieta hasta los 10 min y después duerme (`ChibiDirector.SetActivity`). Cada una guarda dónde
quedó (`AgentPetSpots`); «Ocultar del escritorio» deja a ese agente sin mascota.

## Sesiones

| Estado | Señal | De dónde sale |
|---|---|---|
| Trabajando | asterisco animado y su carril de pasos | `status: busy` en `sessions/<pid>.json` |
| Esperando su respuesta | punto ámbar que late | una pregunta (`AskUserQuestion`) sin responder |
| Falló | cruz roja y el texto del error | un error de la API (`isApiErrorMessage`) después del último pedido |
| Terminó | marca verde, «hace 5 min · 2 min 25 s» | `status: idle` y el último turno hace menos de 30 min |
| Inactiva | punto gris, agrupadas y plegadas | `status: idle` desde hace más de 30 min |

Las sesiones se ven en la vista **Actividad**. Mientras trabaja, cada una muestra su carpeta, el modelo, el
tiempo del turno y, con lista de tareas (`TodoWrite`), «3 de 5 tareas»; debajo, su carril de pasos. Las
inactivas van plegadas al final. Clic: el detalle de la sesión (ver Actividad). Clic derecho: nueva sesión en
esa carpeta, abrir la carpeta o copiar el ID. Solo se cuentan los procesos vivos: `procStart` (no documentado) se compara
con la hora de inicio del proceso en los formatos conocidos y, si el formato no se reconoce, basta
con que el proceso exista.

La línea «Modelo» sale de la última respuesta del `.jsonl`: `message.model`, `effort` y
`advisorModel` (no documentados). Un `/model` o `/effort` se ve recién en la respuesta siguiente.

## Vistas y ajustes

La cabecera muestra 3 vistas: **Uso** (los límites de la cuenta elegida), **Actividad** (las sesiones, cada
una con su carril) e **Historial** (el mapa de calor de 6 meses). El engranaje del pie abre los **Ajustes rápidos**, en cinco pestañas: Avisos (notificaciones y sonido), Apariencia
(color, mascota y su tamaño), Asistente (chat, micrófono y leer en voz alta), Isla y Sistema (idioma, inicio con
Windows, diagnóstico y la versión nueva, si hay); la última abierta queda en `SettingsTab`. Todas miden lo que la más alta (con lo que esté
encendido), así que al cambiar de pestaña el cuadro no cambia de tamaño. El menú del ícono queda en abrir, actualizar, nueva
sesión, ajustes y salir.

Las filas de cada pestaña no están escritas en la ventana: salen de `SettingsPages.Build` (`Core/SettingsPages.cs`),
la misma lista que muestra Claupet Console. Cada fila dice su tipo (interruptor, lista, dato, nota, enlace), su
acción (la que atiende `OnSettingAsync` en `Claupet.Avalonia/TrayController.cs`) y dónde va: en las dos o solo en la Console
(`RowPlace.Console`). Los rápidos llevan solo lo de todos los días; lo demás (los gestos de la mascota, la cuenta
y la carpeta del asistente, los permisos, las teclas, el resto de la isla, el hook) está en la Console. Las
secciones sin pestaña (`Tab` nulo: Respuestas, Extensiones y Cuentas) solo están en la Console. Al pie de los
rápidos, la tarjeta **Todos los ajustes › Abrir** abre la Console.

La app Avalonia arma sus rápidos igual (`UsageView.Views.cs`), con el estado que le da a la Console
(`IConsoleApp.SettingsState`, sin lo que ese sistema no tiene). Cada fila hace lo mismo que en la Console
(`IConsoleApp.Act`); solo cambia dónde sale la lista para elegir: junto al cursor, en el popup (`OnQuickSetting`).

## Claupet Console

La ventana grande (Windows por ahora). A la izquierda, un riel con Chat, Editor y Ajustes (el editor llega en una
próxima etapa). En **Chat**, la lista de conversaciones (la **Conversación rápida**, que es la de la isla y la
nubecita, y las guardadas, agrupadas en Hoy, Ayer, Esta semana y Antes) y la conversación abierta: el modelo, leer
en voz alta, los bloques de código con «Copiar» y de dónde vino cada respuesta; Intro envía y Mayús+Intro hace otra
línea. Las guardadas van una por archivo en `conversaciones\<id>.json`, junto a los ajustes, y no se escriben hasta
su primer mensaje; responde el mismo asistente de la isla, con respuestas más completas. En **Ajustes**, las
secciones (General, Avisos, Apariencia, Mascota, Asistente, Respuestas, Extensiones, Isla, Cuentas —las de Claude Code
con su memoria y los otros agentes— y Sistema).
Las barras de desplazamiento y los globos de ayuda son los del diseño (`Console/Themes.cs`); las listas para elegir y
los menús, una tarjeta flotante dentro de la ventana con la elegida marcada y las acciones («Elegir otra carpeta…»)
separadas abajo, que se maneja también con las flechas, Intro y Esc (`Console/DropList.cs`). Es el mismo `Claupet.exe`
en otro proceso (`Claupet.exe --console`, código en `Console/`, Avalonia 11): se abre solo cuando se pide y se
cierra con su ventana. La bandeja no carga Avalonia ni Skia; solo ese proceso. Una sola ventana a la vez:
abrirla de nuevo la trae al frente. Tiene su propia barra de arriba (la cabeza del chibi, minimizar, maximizar y cerrar) y las esquinas
redondeadas de Windows 11; sigue el modo claro u oscuro de Windows y el color elegido en Apariencia.

Solo la bandeja guarda los ajustes. Las dos se hablan por una tubería con nombre, `Claupet.Console.<usuario>`,
que solo abre el mismo usuario de Windows (`PipeOptions.CurrentUserOnly`), con un mensaje JSON por línea
(`Core/ConsoleLink.cs`):

| Mensaje | De | Qué lleva |
|---|---|---|
| `estado` | bandeja | las secciones ya armadas, el idioma y la paleta; se manda al conectarse y con cada cambio |
| `accion` | Console | la acción de la fila que se tocó (la misma de los ajustes rápidos); con lo escrito en un cuadro o el archivo elegido |
| `opciones` | bandeja | la lista que esa acción mostraría como menú, con la elegida y un número |
| `elegir` | Console | la opción elegida de la lista con ese número; sin valor, se cerró sin elegir |
| `mostrar` | bandeja | traer la ventana al frente, en una sección o, sin ella, en el chat |
| `chats` | bandeja | la lista de conversaciones: la rápida y las guardadas |
| `chat` | bandeja | la conversación abierta, con lo que va llegando de la respuesta |
| `chat-abrir`, `chat-nuevo` | Console | abrir una conversación o crear una (con el id que elige la Console) |
| `chat-enviar`, `chat-parar` | Console | mandar un mensaje o detener la respuesta |
| `chat-borrar`, `chat-limpiar` | Console | borrar una guardada o vaciar la rápida |

Lo que todavía abre una ventana de la bandeja (grabar una tecla, escribir la ciudad, el hook) se abre desde
Claupet, al frente. Si Claupet no está abierto, la Console lo dice y ofrece abrirlo.
`Claupet.exe --console` abre en el chat (también la entrada «Claupet Console» del menú del ícono) y
`Claupet.exe --console --seccion=<id>`, en esa sección de Ajustes (`general`, `avisos`, `apariencia`, `mascota`,
`asistente`, `respuestas`, `extensiones`, `isla`, `cuentas` o `sistema`) o en una página de adentro (`ext:<id>`, el
detalle de una extensión).

Además de interruptores, listas, datos, notas y enlaces, una fila puede ser un cuadro para escribir (`Input`: se manda
con Intro o al salir del cuadro, y antes de cualquier otra acción de la página; `Secret` no muestra lo escrito) o llevar
a otra página (`Page`: la Console va sola, sin pedirle nada a la bandeja). Una página con `Parent` es de adentro de
esa sección: no va en la lista de la izquierda y arriba tiene «‹ sección» para volver; si deja de existir (se
desinstaló la extensión), la Console vuelve a la sección. Un enlace con `File` pide primero un archivo con el selector
del sistema (en el proceso de la Console) y manda su ruta; uno con `Copy` copia ese texto sin pasar por la bandeja.

**Extensiones** (`Core/Extensions/ConsolePanel.cs`). Ajustes › Extensiones las muestra y las maneja todas en la Console,
igual en cualquier bandeja que tenga un `ExtensionManager`: la bandeja implementa `IExtensionsHost` (el gestor, lo que
hace después de instalar y la API local) y llama una vez a `ConsoleHost.UseExtensions`; `ConsoleHost` cambia la sección
de resumen por las páginas de `ExtensionsPanel` y le pasa las acciones que empiezan con `ext-`.

| Página | Qué tiene |
|---|---|
| `extensiones` | las instaladas (cada una lleva a su página, con su versión, qué trae, su firma y su estado), «Instalar desde archivo…», los paquetes que no se pueden leer y los comandos repetidos, las ranuras exclusivas (si alguna instalada puede ocupar una) y la API local con sus tokens («Revocar» pide confirmarlo) |
| `ext:<id>` | encendida o apagada (y por qué se apagó), versión, autor, firma, qué trae, estado, comandos, de qué se entera, licencia, sitio, permisos, sus ajustes (interruptor, lista o cuadro según el tipo; la clave nunca vuelve a la Console: se escribe otra o se borra), su registro, su carpeta de datos y «Desinstalar…» (borrando o guardando sus datos) |
| `ext-revision` | antes de instalar: versión (o si actualiza, vuelve atrás o reinstala), autor, qué trae, la firma y lo que significa, si corre un programa, los permisos (en una actualización, solo los nuevos), las ranuras y la mascota a usar, e «Instalar»; una firma inválida, revocada o de otra clave solo se explica |
| `ext-token` | un token nuevo de la API local: para qué programa y qué puede pedir; ya creado, se muestra una vez con «Copiar» |

Soltar un `.claupet` sobre la mascota o la isla, o elegirlo en el asistente de configuración, abre la Console en la
revisión (la abre si estaba cerrada); nada se instala sin «Instalar».

**En Linux y macOS** la Console es la misma (`Claupet --console`, la entrada «Claupet Console» del menú del ícono o
abrir Claupet otra vez). Lo que no depende del sistema (la tubería, las conversaciones, las listas para elegir) lo hace
`Core/ConsoleHost.cs`; la bandeja le da lo suyo con `IConsoleApp` (`Claupet.Avalonia/TrayController.Console.cs`): sus
ajustes, sus acciones y el asistente del chat.
Las filas de lo que un sistema todavía no tiene no aparecen (`PlatformFeatures` en `SettingsState`); una sección que
queda sin nada dice «Todavía no está disponible en este sistema.». En la app Avalonia el chat responde como en
Respuestas, con las acciones en el equipo; con la isla, la conversación rápida es la suya (ver Asistente en Linux y
macOS).

Una sola bandeja y una sola Console por persona, con mutex con nombre (`Core/ConsoleLink.cs`). En Windows van en
`Local\` (la sesión de Windows). En Linux y macOS, .NET limita `Local\` a la sesión del proceso (cada terminal tiene
la suya) y la Console abierta desde el menú no vería la bandeja abierta al iniciar: allí van en `Global\`, con el
nombre del usuario. La Console nueva le pasa los archivos a la abierta, y la trae al frente, por la tubería
`Claupet.ConsoleWake.<usuario>` (los eventos con nombre solo existen en Windows).

**Editor** (etapa 1). AvaloniaEdit es solo el motor; el aspecto es el del diseño (`Console/EditorPane.cs` y
`Console/EditorLook.cs`). Arriba, el botón de la lista de archivos (abiertos y recientes, en `editor-recientes.json`
junto a los ajustes), las pestañas con un punto si hay cambios sin guardar, **Documento** o **Código** y más opciones
(Nuevo, Abrir, Guardar, Guardar como y Cerrar, con Ctrl+N, O, S, Mayús+S y W). En Documento, el Markdown se ve sin sus
marcas salvo en la línea del cursor (títulos grandes, negritas, cursivas, código en línea, enlaces sin la dirección,
viñetas «•» y citas), en una columna de 640 al centro; Markdown y texto se abren así. En Código, números de línea y los
colores del diseño, iguales en todos los lenguajes que trae AvaloniaEdit (C#, JavaScript, Python, PowerShell, SQL,
HTML, CSS, JSON, XML y otros). Guarda en UTF-8, primero a un archivo de al lado. Al cerrar un archivo o la ventana con
cambios, pregunta. `Claupet.exe --console archivo…` los abre en el editor (si la Console ya está abierta, se los pasa).
La IA en el editor, el texto propuesto, leer en voz alta y su sección de ajustes vienen en las etapas 2 y 3.

Como cualquier app, Claupet tiene su acceso directo en el menú Inicio (encendido por defecto) y, si se pide, en
el escritorio (Ajustes › General › Accesos directos; `StartMenuShortcut` y `DesktopShortcut`). Abren
`Claupet.exe --console` con la cabeza del chibi como ícono (`claupet.ico`, junto a los ajustes); si Claupet no
estaba abierto, la Console lo abre y se conecta cuando está listo. Al arrancar, la bandeja vuelve a crear los
encendidos si faltan o apuntan a otra carpeta (`Core/Shortcuts.cs`, que usan las dos apps); una copia de prueba
(`--preview`) no los toca.
Abrir `Claupet.exe` otra vez, sin argumentos, con Claupet ya abierto, también trae la Console.

## Temas propios

Los temas van aparte de las seis paletas, en Ajustes › Apariencia › **Temas** de la Console: una tarjeta por tema, con
bordes redondeados y la vista previa de ese tema en el modo de la Console (sus colores, «Aa» en su letra y su escena
animada), y la tarjeta **Claupet**, que es no usar ninguno. Tocar una tarjeta la elige y la elegida lleva el borde del
acento y su marca; el menú «⋯» de cada tema propio lo exporta a un `.json` o lo elimina, con confirmación en la misma
tarjeta (`Console/ThemeGallery.cs`). Elegir una paleta en **Color** quita el tema; el tema sigue guardado en su tarjeta.

En **Tema nuevo** se escribe un prompt («un tema de playa con el fondo animado», «algo otoñal con una letra más
redonda») y **Crear** (o Intro) se lo pide a Claude con la cuenta del asistente. Mientras tanto aparece su tarjeta, con
un brillo que la cruza y chispas en pixel art, y se puede cancelar; al terminar, el tema queda elegido. **Calidad**
elige el modelo: rápida, Sonnet; media, Opus, y alta, Fable. Si la cuenta no tiene permiso para ese modelo, se prueba
con el siguiente y se avisa con cuál se hizo; con el asistente de una extensión, va con el modelo de ese proveedor.
**Importar un tema** trae un `.json` exportado (o escrito a mano), que pasa por la misma lectura y las mismas
correcciones que uno creado con Claude y se guarda con un id libre.

Cada tema es un JSON en `temas\<id>.json`, junto a los ajustes, y el elegido está en `CustomTheme` de los ajustes
(antes se guardaba en `Palette` como `tema:<id>`, y al leer se pasa solo); la bandeja y la Console lo leen del disco por
ese id (`Core/CustomThemes.cs`). La mascota los sigue nombrando junto a las paletas, como `tema:<id>`. Si el archivo
falta o está roto, `Theme.ForDark` e `Ink.For` vuelven a la paleta, sin error.

El JSON lleva el formato (`claupet-theme/1`), el id, el nombre, una línea sobre su ambiente, los colores de `Theme` para
el modo claro y el oscuro, una familia de letra opcional y un fondo animado opcional. Al pedirlo, Claude recibe qué pinta
cada color, los rangos de luz de cada superficie, las letras instaladas en el equipo, el catálogo de escenas con lo que
pinta cada color y un ejemplo completo. Igual, el diseño se respeta por código y no por confianza en el modelo:

- Se lee con tolerancia (el primer objeto JSON de la respuesta, aunque venga en un bloque de código o con comas de más); lo que no
  es un color se queda con el de Claupet.
- Superficies sobrias: el fondo, Raised, Hover, el borde y la pista, con la luz de su modo y poco color (croma ≤ 0,10).
- Contraste WCAG contra el fondo y contra Raised: 4,5:1 el texto, 3:1 los secundarios, el acento y los de estado; si
  no llega, se oscurece (claro) o se aclara (oscuro) hasta que llegue. Peligro, aviso y éxito fuera de su tono (rojo,
  ámbar, verde) vuelven a los de Claupet.
- La letra es solo un nombre de familia; si está instalada (`FontManager`), va delante de la cadena de `Ink.Sans` y
  `Ink.Display`. Mono no cambia.
- El id se limpia (minúsculas, cifras y guiones; nada de barras, puntos ni nombres reservados) y la ruta siempre
  queda dentro de `temas\`.

El fondo animado sale de un catálogo cerrado de escenas en pixel art (`playa`, `atardecer`, `lluvia`, `nieve`,
`estrellas`, `hojas`, `sakura`, `burbujas`, `luciernagas`, `aurora`, `ciudad` y `brasas`); el modelo elige la escena,
sus colores, la densidad (0,1–1) y la velocidad (0,25–2). Las partículas van en dos planos (las lejanas, más chicas,
lentas y tenues). Se dibuja solo en la Console, detrás del contenido y con poca opacidad, a 12 cuadros por segundo, y se
pausa con la ventana minimizada u oculta (`Console/SceneLayer.cs`); las vistas previas usan la misma escena con un
píxel más chico. La isla, las burbujas y la mascota solo toman los colores.

Para probar sin ventanas: `Claupet.exe --tema "<prompt>"` crea el tema, lo guarda e imprime el JSON validado (no lo
elige).

## Actividad

Un carril por sesión activa (los nodos los dibuja `LaneNodes.cs`, que usa también la isla): «Pedido», «+N» con lo anterior y los últimos grupos de pasos del
turno (los seguidos de la misma herramienta se juntan: «Leyendo ×3»). Mientras la sesión trabaja
siempre hay algo animado: si el último paso sigue en curso, late; si ya volvió (leer o buscar duran
milisegundos), aparece al final «Pensando…» con el asterisco que gira. Un punto viaja hacia el
último nodo, cada paso nuevo entra creciendo y el ícono de actualizar gira mientras actualiza; en
ámbar si la sesión le espera. Debajo, el último subagente y su estado. Clic en un carril: el detalle
de esa sesión, con su estado, el último pedido, la carpeta, el modelo y las interacciones; el mapa con la cuenta,
la sesión y cuántas veces usó cada familia de herramientas en el turno (línea animada hacia la herramienta en
uso, o de vuelta a la sesión mientras piensa) y el subagente; los últimos 5 pasos con su hora, y los botones
«Nueva sesión aquí…», «Abrir carpeta» y «Copiar ID». Sale del mismo `.jsonl` que el estado de la sesión: cada `tool_use`
es un paso y su `tool_result` lo cierra; del subagente se toman `agentType` y `totalToolUseCount`
del resultado. La lógica común está en `Core/Flows.cs`.

## Otros agentes y la memoria de las cuentas

Además de las cuentas de Claude Code, la app Avalonia sigue a otros agentes de este equipo, en una lista aparte
(`Agents` en `settings.json`, `Core/Agents.cs`): se agregan solos si está su carpeta de siempre y en **Ajustes ›
Cuentas** (Claupet Console) se elige cuáles seguir. Se leen al actualizar, fuera del hilo de la interfaz, solo sus 30
sesiones más recientes; nada se escribe en sus carpetas, no se usa su cuenta ni se gastan tokens. En **Uso**, debajo de
la cuenta elegida, solo los que hoy hicieron algo o tienen un límite que todavía corre (`Agents.ShowsInUsage`), con esos
límites y lo de hoy: uno sin actividad, o con límites de hace días ya reiniciados, no sale, y si no queda ninguno tampoco
el título «Otros agentes». Uno que no se sigue no se lee. En **Actividad**, sus sesiones de las últimas 24 horas, con la
carpeta y el modelo si se saben.

| Agente | Carpeta | Qué se lee |
|---|---|---|
| Codex CLI | `$CODEX_HOME` o `~/.codex` | `sessions/AAAA/MM/DD/rollout-*.jsonl`. Los límites, del último evento `event_msg` con `payload.type` `token_count`: `payload.rate_limits.primary` y `.secondary` (`used_percent`, `window_minutes`: 299 es la sesión de 5 h y 10079 la semana; `resets_in_seconds`, contado desde el `timestamp` del evento; si ya pasó, vuelve a 0). La carpeta (`cwd`) y el id de `session_meta` y el modelo de `turn_context`, si están. Los `.jsonl.zst` no se leen |
| Gemini CLI | `~/.gemini` | `tmp/<proyecto>/chats/session-*.json`: `sessionId`, `projectHash`, `startTime`, `lastUpdated` y `messages` (se cuentan los `user` y `gemini`; el último `model`). La carpeta del proyecto, de `projects.json` si allí está su hash. No escribe límites |
| GitHub Copilot CLI | `$COPILOT_HOME` o `~/.copilot` | `session-state/<id>/events.jsonl`: su formato no está documentado, así que solo se cuentan sus eventos (líneas JSON) y la hora del archivo |

Los formatos salen de lo que documenta la comunidad y se probaron con archivos armados a mano (`Claupet.Tests/AgentsTests.cs`),
no con los de un equipo real: lo que no se entiende se salta.

**Memoria de una cuenta de Claude nueva.** Al agregarla (con el + de Uso o «Agregar cuenta…» en la Console), si todavía no
tiene memoria propia y hay otra cuenta, se elige: **compartida** con la elegida en Uso (su carpeta `projects`, con las
conversaciones y la memoria de cada proyecto, pasa a ser un enlace a la de la otra: un junction en Windows, que no pide
administrador, o un enlace simbólico en Linux y macOS; `CLAUDE.md`, skills, agentes, ajustes y credenciales siguen siendo
propios) o **separada**. Una cuenta con su carpeta `projects` llena no se enlaza, para no perderla. Ajustes › Cuentas dice
de cada una si su memoria es propia o compartida (`AccountMemory`).

## Historial de uso

En la vista Historial: una casilla por día de los últimos 6 meses, de todas las cuentas; el color
sube con los tokens (entrada + escritura en caché + salida) y con el cursor encima se ve la cifra del día.
Debajo, las del período: tokens, días activos, racha actual y más larga, el día más activo y el modelo
favorito. Sale de los `.jsonl` de Claude Code de este equipo; como Claude Code borra los registros de más de 30
días, la app guarda un resumen diario (solo cifras) en `history.json`.

## Nueva sesión

Desde el menú del ícono o el de una sesión. Abre una terminal en la carpeta elegida, fija
`CLAUDE_CONFIG_DIR` de la cuenta (o lo quita para `~/.claude`) y ejecuta
`claude --model … --effort … --advisor …`:

- **Windows:** Windows Terminal, o PowerShell si no está.
- **macOS:** Terminal, con `osascript` (`do script`), en el shell de inicio de la persona.
- **Linux:** la primera instalada entre gnome-terminal, konsole, xfce4-terminal, kitty, alacritty,
  wezterm, tilix, xterm y x-terminal-emulator, con `bash -lc` (para que `claude` esté en el PATH);
  al salir de Claude Code queda abierto el shell de la persona.

Esas opciones valen solo para esa sesión: no tocan el `settings.json` de la cuenta.
«Predeterminado» omite la opción; el asesor «Ninguno» se apaga con
`CLAUDE_CODE_DISABLE_ADVISOR_TOOL=1`, porque `--advisor` no acepta `off`. `--advisor` no aparece
en `claude --help`; se comprobó con Claude Code 2.1.296. Lo último elegido se guarda en
`settings.json` de la app (`Launch`). Los scripts están en `Core/Launcher.cs`.

## Asistente: chat, isla, voz y teclas (Windows)

Inspirado en [coucou](https://github.com/Louis-CFM/coucou), pero con Claude Code. Todo viene
apagado: `Chat`, `Island` y `Mic` en el `settings.json` de la app.

**Cómo responde.** Cada pregunta corre `claude -p` con la cuenta elegida (`AssistantAccount`; si no,
la de Uso) en `…\Claupet\asistente`, con `--safe-mode` (sin el CLAUDE.md de la persona,
hooks ni MCP: la entrada baja de unos 7.400 a 1.000 tokens), `--no-session-persistence` y
`--output-format stream-json`, para mostrar la respuesta mientras llega. La pregunta va por la
entrada estándar y el prompt de sistema en un archivo. El chat solo puede buscar en internet
(`WebSearch` y `WebFetch`; el clima sale de `wttr.in`); el análisis de un archivo solo puede leer su
carpeta (`Read`, `Glob`, `Grep`). La mascota usa `MascotModel` (Haiku, de entrada) y se le pide
responder en 220 caracteres como mucho; la isla y las conversaciones de Claupet Console, `IslandModel` (vacío:
el modelo de sus sesiones). Con **Automático** (`auto`), cada pregunta va al modelo y al esfuerzo que pide
(`Core/AutoModel.cs`, reglas sobre el texto, sin gastar tokens): lo corto y suelto a Haiku con `--effort low`;
explicar, escribir, traducir o un ejemplo de código, a Sonnet con `medium`; diseñar, analizar, comparar a fondo
o código que falla (una traza de error), a Opus con `high`. Repreguntar sobre una respuesta con código o
analizar un archivo no baja de Sonnet. El chip de quién respondió dice también el esfuerzo.
**Sin internet.** Mientras arranca `claude`, la app comprueba la conexión (`Connectivity`, una
petición corta a la API por el proxy del momento). Si no hay, lo cierra enseguida y responde ella:
el saludo, la hora y la fecha se contestan en el equipo, y a lo demás se le dice que no hay
conexión. Si Claude Code pasa 90 segundos sin decir nada, también se cierra.

La primera línea de la respuesta puede traer una marca, que no se muestra:

| Marca | Qué pasa |
|---|---|
| `[ACCION] {"accion":"…","valor":"…"}` | la app hace esa acción en el equipo (tabla de abajo) y la mascota dice la frase que sigue, o lo que falló |
| `[TAREA] <pedido>` | lo que Claude no puede hacer con sus herramientas ni con las acciones: se ofrece abrir una sesión de Claude Code con ese pedido, en `TaskFolder` (si no se eligió otra, la propia del asistente, `~\Claupet\Tareas`, que se crea al abrir la primera tarea; no va en Documentos porque suele estar en OneDrive) o, si vino de un archivo soltado que no pudo abrir (un Excel, por ejemplo), en la carpeta del archivo |

Las acciones son una lista cerrada (`Core/Actions/SystemActions.cs`), para que una frase mal entendida no pueda
hacer otra cosa; lo demás se encarga a Claude Code. Lo que cambia de un sistema a otro está detrás de `IDesktop`
(`Core/Actions/Desktop.cs`): en Windows, `Core/Actions/WindowsDesktop.cs`, que usan las dos apps (cada una pone su
recorte y su portapapeles: `DesktopUi`); en Linux y macOS, ver Asistente en Linux y macOS. El
prompt y las reglas sin IA solo ofrecen las acciones que el sistema sabe hacer (`SystemActions.Supports`):

| Acción | Valor | Cómo |
|---|---|---|
| `fondo` | ruta de una imagen o URL https de un JPEG, PNG o BMP (hasta 25 MB) | `SystemParametersInfo`; las bajadas quedan en `…\Claupet\fondos` (las 5 últimas) |
| `tema` | `claro` u `oscuro` | `AppsUseLightTheme` y `SystemUsesLightTheme` del registro, y aviso a las ventanas |
| `color` | una paleta de la app | como elegirla en **Ajustes** |
| `volumen` | 0 a 100, `subir`, `bajar` (de a 10), `silenciar` o `activar` | Core Audio, en la salida predeterminada |
| `musica` | `reproducir`, `pausar`, `siguiente` o `anterior` | teclas multimedia; antes de reproducir o pausar mira si algo suena (`Music.PlayingNow`) |
| `abrir` | URL `https`, `http`, `spotify:` o `ms-settings:`, o el nombre de una app del menú Inicio | se abre con el programa de Windows; las apps, por su acceso directo |
| `captura` | `pantalla` o `recorte`; con `"pregunta"`, Claude la mira | ver **Captura** abajo |
| `agendar` | `{"titulo","inicio","fin","lugar","nota"}` (`inicio` sin hora: todo el día) | un `.ics` que se abre con la app de calendario de Windows de la persona, que lo muestra para guardarlo; avisa 15 min antes |
| `nota` | `{"titulo","texto"}` o el texto solo | un Markdown en `Documentos\Claupet\Notas` (con OneDrive, se sincroniza) |
| `notas` | `leer`, `abrir` o palabras para buscar | las últimas 8 (o las que tengan todas esas palabras) vuelven a Claude para que responda |
| `recordar` | `{"texto","cuando"}` | ver **Recordatorios** abajo |
| `recordatorios` | `leer` o `cancelar <palabras>` | los pendientes vuelven a Claude; cancelar borra también su tarea programada |
| `ventana` | `cerrar`, `minimizar`, `maximizar` o `traer <app>`, o `escritorio` | la ventana de esa app más al frente, por el nombre del ejecutable, la descripción del programa o el título; cerrar es como la X (la app puede preguntar si guarda); `escritorio` minimiza todo |
| `equipo` | `bloquear`, `suspender`, `reiniciar` o `apagar` | reiniciar y apagar se confirman cada vez y no fuerzan el cierre de las apps; los tres últimos esperan 2 s para que se vea la respuesta |
| `brillo` | 0 a 100, `subir` o `bajar` (de a 10) | WMI (`WmiMonitorBrightnessMethods`): solo la pantalla del portátil |
| `estado` | `bateria`, `disco`, `memoria` o `todo` | los datos vuelven a Claude para que responda |
| `copiar` | el texto | al portapapeles |
| `portapapeles` | `leer` | el texto copiado (hasta 8.000 caracteres) vuelve a Claude; si es una imagen, Claude la mira |
| `carpeta` | `descargas`, `documentos`, `escritorio`, `imagenes`, `musica`, `videos`, `capturas`, `notas` o una ruta | se abre en el Explorador; nunca un archivo |

**Datos que vuelven a Claude.** Con `notas`, `recordatorios leer`, `estado` y `portapapeles`, Claude no
sabe la respuesta al pedir la acción: la app la hace y le vuelve a preguntar lo mismo con lo que encontró
(`AssistantRequest.Data`), marcado como datos y no instrucciones; en esa respuesta no se acepta otra
marca, para que un texto copiado o una nota no puedan pedir nada.

**Permisos.** Las seis primeras acciones se hacen sin preguntar; cada una de las demás pertenece a una
capacidad (`Core/Capabilities.cs`: capturar la pantalla, agendar, notas, recordatorios, ventanas,
equipo, brillo, estado, portapapeles y carpetas) que pide permiso la primera vez, donde se está
conversando: la isla muestra una tarjeta y la nubecita, botones (sin ninguna de las dos, un cuadro de
Windows). «Permitir siempre» queda en `Permissions` del `settings.json`; «Solo esta vez», no; «No» o
cerrar la nubecita no la hacen. En la isla, como un permiso de Claude Code, salir de la tarjeta (otra
pestaña, un clic fuera, plegarla) no la responde: la compacta dice «Claupet le pregunta algo · clic para
verlo» y la vuelve a abrir, igual que Ctrl+Alt+A. Se descarta si se cancela el pedido que la hizo: otra
pregunta en la isla, «Nueva conversación» o el botón de cancelar. **Ajustes › Asistente › Permisos** abre
la lista de capacidades, con una marca en las permitidas: un clic la da o la quita, sin cerrar la lista.

**Captura.** `pantalla` toma la pantalla donde está el cursor, en sus pixeles reales (el hilo pasa a
«PerMonitorV2» mientras tanto, porque la app es «SystemAware» y en un monitor con otra escala recibiría
una imagen reescalada). `recorte` muestra esa pantalla congelada y oscurecida para marcar la parte con el
mouse (Esc o el clic derecho cancelan). La mascota, las nubecitas, la isla y los avisos se esconden con
`DWMWA_CLOAK` mientras se toma. Queda como PNG en Capturas de pantalla, con el nombre de Windows
(«Captura de pantalla 2026-10-24 141503.png») y en el portapapeles. Si se pidió mirarla, una copia queda
sola en `…\Claupet\capturas` y se analiza como un archivo soltado: Claude solo puede leer esa
carpeta, no las demás capturas.

**Recordatorios.** Quedan en `recordatorios.json`; la app los mira cada 15 s y, a su hora, muestra un aviso
que se queda hasta cerrarlo (con su sonido, la mascota saluda y lo dice y, con la voz encendida, lo lee).
Por si la app está cerrada, cada uno crea una tarea del Programador de tareas, `\Claupet\Recordatorio
<id>`, del usuario y sin administrador (`schtasks /Create /XML`), que abre la app con `--recordatorio
<id>` a esa hora; si el equipo estaba apagado, al encenderlo («StartWhenAvailable»). Si la app ya estaba
abierta, la nueva se cierra enseguida y avisa la otra. Los que pasaron con la app cerrada se avisan al
abrirla («se le pasó»). La tarea se borra al avisar o cancelar y, si no, sola un día después.

Las consultas dejan un `sessions/<pid>.json` mientras corren; la vista Actividad las ignora por su
carpeta. El código está en `Core/Assistant.cs`; las acciones, en `Core/Actions/` (qué se pide, los permisos y los
mensajes, igual en los tres sistemas; en Windows, `WindowsDesktop.cs`, `WinScreen.cs` y `ReminderTasks.cs`),
`CropWindow.cs` (el recorte) y `Core/Agenda.cs` (notas, `.ics` y recordatorios, sin interfaz).

**La isla** (`IslandWindow.cs`) es una ventana siempre oscura, con el acento de la paleta. Oculta, una
franja invisible de 320 × 6 px en el centro del borde de arriba la hace asomar compacta (300 × 36);
con el cursor encima (o un clic, según `IslandHover`) se abre con Actividad (cada sesión con los nodos de
su carril, como en la vista Actividad de la bandeja, con `LaneNodes.cs`; el nodo de la edición de un archivo
abre su diff; ir a la terminal), Chat y «+ Sesión». Se pliega al irse el cursor, salvo si se está
escribiendo, hay una pregunta en curso o acaba de llegar una respuesta: esa queda a la vista de 5 a
15 s, según su largo. Un clic en cualquier otra parte (otra
ventana, el escritorio, la barra de tareas) la pliega: un gancho de mouse de bajo nivel
(`MouseDownHook`), puesto solo mientras está abierta. No cuentan los menús que abrió la isla; un permiso
de Claude Code o una pregunta de Claupet siguen esperando en la compacta y, si estaba respondiendo, se abre de nuevo al llegar la
respuesta. Con `IslandWhileBusy` queda compacta a la vista mientras alguna sesión trabaja; con
`WeeklyRecap`, los lunes muestra el resumen de la semana. Lo saca de los tokens por día (entrada + escritura en
caché + salida) de los `.jsonl` de Claude Code de este equipo; como Claude Code borra los registros de más de 30
días, la app guarda un resumen diario (solo cifras) en `history.json`, aunque la ventana esté cerrada.

El chat muestra toda la conversación (se guardan las últimas 40 y se olvida a los 10 minutos sin
escribir, o con «Nueva conversación») en un marco de 300 px como mucho: lo que no cabe se ve con la rueda
del mouse o con una barra fina a la derecha, que se aclara con el cursor encima y se arrastra; arriba de
todo se dice cuándo se renueva. Al enviar algo vuelve a lo último; si se subió a leer mientras llega una
respuesta, lo que se lee no se mueve.

**Permisos y preguntas en la isla** (opcional, en **Ajustes › Isla**). Agrega a Claude Code el hook
`PermissionRequest`, con un respaldo `settings.json.respaldo-<fecha>` del archivo anterior:

```json
{ "hooks": { "PermissionRequest": [ { "hooks": [ { "type": "command", "command": "\"<ruta de la app>\" --hook --permiso", "timeout": 120 } ] } ] } }
```

El hook le pasa el pedido a la app por un pipe con nombre (solo del usuario) y espera hasta 110 s.
La respuesta es la documentada: `hookSpecificOutput.decision.behavior` `allow` o `deny` y, para una
`AskUserQuestion`, `allow` con `updatedInput` y sus respuestas. Si la app está cerrada o nadie
responde a tiempo, el hook no escribe nada y Claude Code pregunta en la terminal como siempre
(`Core/Approvals.cs`).

**Un archivo soltado** sobre la mascota: lo atrapa en una caja, camina hacia la isla según dónde
esté, la lanza (`BoxFlight.cs`) y la isla muestra cómo lo abre, lo lee y lo resume, con «Preguntar
algo más», «Copiar» y «Seguir en una sesión». La mascota cuenta lo esencial en su nubecita y vuelve
a su lugar. Sin la isla, no camina: piensa con la caja en las manos. Se apaga con
`AnalyzeDrops`.

![La isla escuchando](docs/capturas/isla-escucha.png)

**Voz** (`Voice.cs`; Whisper y la descarga, en `Core/Voice/Transcriber.cs`). El micrófono se graba a 16 kHz y se pasa a texto en el equipo con
[Whisper.net](https://github.com/sandrohanea/whisper.net) 1.9.1 y el modelo `ggml-small`
multilingüe (488 MB, de `huggingface.co/ggerganov/whisper.cpp`). Se descarga una vez, al encender el
micrófono; si se corta, se retoma, y solo se usa si su SHA-256 coincide. La ventana del codificador
se ajusta al largo de lo dicho (`WithAudioContextSize`) en vez de los 30 s de siempre: una frase de
3 s pasa a texto en menos de 1 s (antes, unos 5 s), con el mismo acierto (medido con 4 frases). Mientras
se habla, cada 0,7 s de audio nuevo se muestra lo entendido hasta ahí. El modelo cargado ocupa unos
850 MB de memoria y se suelta a los 3 minutos sin usarlo.

**La voz de Claupet** (`Core/Voice/Supertonic.cs`, `Core/Voice/ClaupetVoice.cs`). Con «Leer las respuestas en voz alta» (no necesita el
micrófono: también lee lo que responde a un mensaje escrito), las
lee [Supertonic 3](https://github.com/supertone-inc/supertonic) en el equipo, con ONNX Runtime 1.20.1
en la CPU: la voz femenina (F4) o la masculina (M1), en `ReadAloudVoice`. El texto entra tal cual, sin
pasar a fonemas. La inferencia está adaptada de su ejemplo en C# (MIT); el modelo es OpenRAIL-M y su
`LICENSE` se baja con él.

| Qué | Cómo |
|---|---|
| Modelo | los 9 archivos que se usan (unos 400 MB), de una versión fija de Hugging Face y con su SHA-256, en `…\Claupet\voz\supertonic-3`; se bajan al elegir una voz de Claupet, retomando como el de Whisper |
| Velocidad | 8 pasos, como el ejemplo: una frase de 5 s tarda unos 2 s en un Ryzen 7 5700U. Se habla frase por frase: la primera va sola (sale en menos de 1 s) y las demás se generan mientras suena la anterior |
| Memoria | unos 550 MB con el modelo cargado; se suelta a los 3 minutos sin hablar |
| Sonido | `waveOut`, aparte de los sonidos de los avisos (que van por `PlaySound` y se cortarían entre sí) |
| Expresiones | con la voz de Claupet, se le permite a Claude poner `<laugh>`, `<sigh>` o `<breath>`, como mucho una por respuesta: se oyen y no se muestran (`Expressions`, también cuando llegan partidas) |
| Sin el modelo | mientras se baja, o con «La de Windows», lee una voz de Windows en español, sin las etiquetas |

Las caritas de texto, como `(^_^)`, no se leen.

**Modelo local** (`Core/LocalModel.cs`; en la bandeja, `TrayController.LocalModel.cs`). Responde en la mascota o la isla sin internet
ni tokens cuando se elige «Local: …» como su modelo (`AssistantModels.Local`), o con «La mascota responde
con un modelo local» en el asistente de configuración. Recibe el mismo prompt de sistema que Claude, sin
las herramientas que no tiene, y su respuesta pasa por las mismas reglas (marcas, acciones y permisos),
como el asistente de una extensión.

| Qué | Cómo |
|---|---|
| Modelos | Qwen3.5 0.8B (533 MB), 2B (1,3 GB, el propuesto) o 4B (2,7 GB), Apache 2.0, en GGUF Q4_K_M de Unsloth fijado a un commit de Hugging Face y con su SHA-256 (`LocalModel.Choices`, `LocalModel` en `settings.json`) |
| Programa | `llama-server` de llama.cpp b11913 con Vulkan para x64 (37 MB, MIT), de su Release de GitHub y con su SHA-256; del zip se sacan solo el servidor, sus DLL y las de ggml (Vulkan y cada CPU). Trae también el motor del procesador: sirve sin tarjeta gráfica. Quien ya tenía la versión para CPU (24 MB) la sigue usando hasta bajar esta; las dos quedan (otra copia de la app, de antes, puede usar la vieja) |
| Tarjeta gráfica | **Ajustes › Respuestas › Tarjeta gráfica** (`LocalGpu`): Automático (de entrada; si con la gráfica no arranca, sigue con el procesador), Siempre o Nunca. Con la gráfica carga todas las capas en ella (`-ngl 99`); sin, `--device none`. Sirve con cualquier marca, dedicada o integrada; cuál usa lo dice `llama-server --list-devices` (una vez por sesión), y se ve en la fila. Medido en una AMD Radeon integrada (Ryzen 7 5700U) con el 2B y 762 tokens: lee a 215 tokens/s contra 124–135 con el procesador y escribe a 21,6 contra 15–17; arranca 0,3 s más lento. En una integrada la memoria es la misma RAM: gana velocidad y deja libre el procesador, no memoria. `Claupet --modelo-local <id> <pregunta> --gpu` (o `--cpu`) lo prueba sin ventanas |
| Descarga | a pedido, en `…\Claupet\modelo-local`, retomando como la de Whisper; al terminar se borran los otros modelos. **Ajustes › Respuestas › Motores** muestra el avance, cancela o borra |
| Arranque | la primera pregunta lo arranca en `127.0.0.1`, en un puerto libre y con una clave nueva en cada arranque (`--api-key`): otro programa del equipo no puede usarlo. Va en un Job que lo cierra si Claupet termina; tras los minutos sin uso de «Liberar la memoria tras» (5 por defecto) suelta la memoria (`--sleep-idle-seconds`) |
| Respuestas | por `/v1/chat/completions` con stream, como una extensión `openai-chat`; sin razonamiento (`--reasoning-budget 0`), contexto de 4.096 y una conversación a la vez |
| Solo conversa | no recibe la lista de acciones ni puede encargar tareas a Claude Code: con ella, el 0.8B y el 2B inventaban acciones (ante un «Hola», minimizar todo) o las armaban mal (`ISmallBrain`). Las acciones las elige aparte, con un JSON de esquema cerrado (ver Respuestas) |
| Identidad | el prompt le dice que es Claupet y solo Claupet; si le preguntan qué modelo usa, dice con cuál piensa. Qwen3.5 a veces abre con `<think></think>` vacío: no se muestra (`ThinkFilter`) |
| Velocidad | en un Ryzen 7 5700U arranca en 2 s; el 2B responde a la mascota en 3 a 5 s (el 0.8B escribe unos 35 tokens por segundo) |
| Memoria | unos 1, 2 o 3,5 GB mientras responde, según el tamaño (estimado) |

### Respuestas: quién responde cada pregunta

**Ajustes › Respuestas** reparte las preguntas de la mascota y de la isla entre tres motores: **sin IA** (la
app sola), el **modelo local** y la **nube** (Claude Code con el modelo elegido, o la extensión del
asistente). `Answers.AskAsync` (`Core/Answers.Router.cs`) decide en cada pregunta:

1. **Las reglas sin IA** (`AnswerRules`, `Core/Answers.cs`) reconocen frases cortas y claras, enteras: «¿qué
   hora es (en Tokio)?», «¿qué día es hoy?», «¿va a llover mañana en Boquete?», «¿cuánta batería me queda?»,
   «¿cuánto me queda de Claude?», «sube el volumen», «pon el brillo al 40», «modo oscuro», «abre Spotify»,
   «recuérdame X en 20 minutos», «anota X», «25 × 4 + 10», «15 % de 200», «10 km a millas», «hola», «gracias».
   Si sobra algo («sube el volumen y abre Spotify»), no adivinan: pasa al siguiente.
2. **Si ninguna regla sirve y el modelo local está descargado y se usa** (es el modelo elegido, o el reparto manda
   algún tipo de pregunta al local), él entiende la pregunta; si todo va a la nube, no se arranca solo para esto. Responde un JSON con
   el tipo, la acción, el lugar y el día, restringido por un esquema (`response_format` con `json_schema`, que
   llama-server convierte en gramática). Solo puede elegir tipos y acciones de la lista; las que llevan un
   JSON propio (notas, recordatorios, agendar) no las elige él.
3. **Cada tipo de pregunta tiene quién responde primero** (`AnswerEngines`), y si no entiende o no está
   disponible pasa al siguiente motor, sin IA → local → nube, hasta donde diga «Si no entiende»
   (`AnswerFallback`: hasta la nube, hasta el modelo local o no pasar).
4. **Modos ya armados** (`AnswerPresets`): **Por defecto** (la tabla de abajo, hasta la nube), **Ahorro de
   tokens** (sin IA en todo lo que la app sabe, el modelo local en lo demás, hasta la nube) y **Solo en este
   equipo** (lo mismo, pero sin pasar a la nube). **Personalizado** muestra las filas de cada tipo. El modo no se
   guarda: es el que coincide con lo elegido; solo «Personalizado» elegido a mano queda en `AnswerPreset`.

| Tipo | Primero, por defecto | Sin IA hace |
|---|---|---|
| Hora y fecha | Sin IA | la hora o la fecha de este equipo; en otra ciudad, por su zona horaria (geocodificación de Open-Meteo) |
| Clima | Sin IA | el de ahora y el de hoy, mañana o pasado mañana, de Open-Meteo |
| Estado del equipo | Sin IA | la acción «estado» (con su permiso) y sus datos tal cual |
| Uso de Claude | Sin IA | cada límite con su porcentaje y cuándo se reinicia |
| Cuentas y conversiones | Nube | la cuenta o la conversión; con el modelo local, él la dice con el resultado ya hecho |
| Acciones del equipo, Abrir apps y sitios, Notas y recordatorios | Nube | la acción de la regla (abrir solo si existe la app, la carpeta o la URL) |
| Saludo y charla | Nube | un saludo según la hora, «de nada», «hasta luego» |
| Preguntas generales | Nube | — (sin IA no se puede; con el modelo local se avisa que puede inventar) |
| Internet, archivos y tareas | Siempre la nube | — |

Los de «sin IA» por defecto son los acordados (hora, fecha, estado del equipo y uso de Claude) más el clima,
que ya no gasta tokens con Open-Meteo. Con **«Local» como modelo de la mascota o la isla**, la nube no se usa:
lo que no responde la app, lo responde el modelo local. Un archivo soltado va siempre al asistente.

| Detalle | Cómo |
|---|---|
| Quién respondió | `AssistantResult.By`: debajo de la respuesta en la nubecita y en la isla («Sin IA · Open-Meteo», «Modelo local · Qwen3.5 2B», «Nube · Haiku 4.5»), si «Mostrar quién respondió» está encendido |
| Mi ciudad | por la IP (ipwho.is, por defecto), la ubicación de Windows (PowerShell con `System.Device.Location`; se prueba al elegirla) o una ciudad escrita, buscada en Open-Meteo. La ubicación se guarda 6 horas y el pronóstico 15 minutos |
| Ahorro | cuántas respuestas de esta semana (desde el lunes) no usaron la nube: sin IA y con el modelo local (`AnswerStats`) |
| Privacidad | sin IA nada sale del equipo, salvo el clima: el nombre de la ciudad o las coordenadas van a Open-Meteo, y la IP a ipwho.is si Mi ciudad es automática |

| Para hablar | Qué pasa |
|---|---|
| Mantener la tecla (Ctrl derecho, `MicKey`) | escucha mientras está pulsada; si se pulsa otra tecla con ella, era un atajo y se cancela |
| Tocarla dos veces, o el micrófono de la isla | escucha sin mantenerla; envía con otro toque o al callar 1,8 s; si en 8 s no se oye nada, termina («no te entendí») |
| Esc | cancela |

**Teclas** (`Hotkeys.cs`). Con el chat o la isla encendidos:

| Tecla | Qué hace |
|---|---|
| Ctrl+Alt+Espacio (`ChatKey`) | abre el chat (en la isla, si está encendida; si no, en la nubecita) |
| Ctrl+Alt+A | va al permiso o la pregunta pendiente (de Claude Code o de Claupet) |
| Ctrl+Alt+T | trae la terminal de la sesión que trabaja o espera |
| Ctrl+Alt+N | abre o pliega la isla |
| Ctrl+Alt+S | silencia o vuelve a sonar |
| Tecla Copilot (`CopilotKey`) | con el micrófono: un toque escucha (envía al callar o con otro toque), mantenerla es como la tecla para hablar; sin micrófono, abre el chat |

Los Ctrl+Alt son atajos registrados (`RegisterHotKey`); si otra app ya tiene uno, la lista de
**Atajos de teclado** lo marca «en uso». La tecla Copilot llega a Windows como **Win+Mayús+F23**; la
app la toma con un gancho de teclado de bajo nivel (`KeyHook`) para que Copilot no se abra y, al
soltar Win, «toca» una tecla sin uso (0xE8, como AutoHotkey) para que no se abra Inicio. Solo
funciona con la app abierta: la opción de Windows para cambiar lo que abre esa tecla solo acepta
apps de la Store o empaquetadas en MSIX firmado. **Grabar…** (en **Ajustes › Asistente › Teclas**)
espera la tecla o la combinación con todas las teclas tomadas, para que Win o Copilot no hagan lo
suyo. Las que escriben solo valen con Ctrl, Alt o Win; solas valen F13 a F24, Pausa, Bloq Despl y
las de navegador y aplicaciones de los teclados multimedia, y para hablar también Ctrl, Alt o Mayús
derechos. Una tecla para
hablar grabada la toma la app como la Copilot: un toque escucha sin mantenerla.

![Ajustes › Asistente, con la sección Teclas](docs/capturas/ajustes-teclas.png)
![Grabar una tecla](docs/capturas/grabar-tecla.png)

## Asistente en Linux y macOS

La app Avalonia (`Claupet.Avalonia/`) trae el asistente por partes, con lo mismo que en Windows salvo lo que dice
cada una. Lo que ya tiene está en `Features` (`TrayController.Console.cs`): sus filas aparecen en Claupet Console y
en el asistente de configuración; lo demás sigue bajo «Próximamente».

**Chat con la mascota** (`ChatBubbleWindow.cs`, `TrayController.Chat.cs`). La nubecita, con el selector de modelo,
la respuesta mientras llega y las tareas para Claude Code. Es la conversación rápida de Claupet Console y la del chat
de la isla. Si se le pide algo en el equipo, lo hace como en Windows (ver Acciones, más abajo).

**Isla** (`IslandWindow.cs`, `IslandWindow.Pages.cs`, `TrayController.Island.cs`). Las mismas páginas (sesiones,
chat, análisis de un archivo, permisos, preguntas, cambios y resumen de la semana), en una ventana transparente que
siempre está encima y no toma el foco. Para asomar al acercar el cursor al borde y plegarse con un clic fuera, mira el
cursor de todo el escritorio (`GlobalPointer.cs`): Windows lo da directo; en Linux lo pregunta a X11 y en macOS a
`NSEvent`. Los permisos llegan como en Windows: el pipe con nombre de `Core/Approvals.cs` es, en Linux y macOS, un
socket de Unix que .NET crea solo. Una copia de prueba (`--preview`) no responde los permisos: los sigue
respondiendo la app de siempre.

**Asistente de configuración** (`SetupWindow.cs`). Los mismos pasos que en Windows, salvo «Instalación» (en Linux y
macOS instalan `instalar.sh` y `preparar-mac.sh`): también pide aceptar los términos. «Aplicar» solo está en el
último, no es el botón por defecto y no responde a Enter.

**Voz** (`Audio.cs`, `Voice.cs`, `GlobalKeys.cs`, `TrayController.Voice.cs`). Whisper, la voz de Claupet y sus
descargas son las de Windows (`Core/Voice/`, que compilan las dos apps); cambian el audio y la tecla:

| Qué | Cómo |
|---|---|
| Micrófono y parlantes | [PortAudio](https://www.portaudio.com/) por [PortAudioSharp2](https://github.com/csukuangfj/PortAudioSharp2) 1.0.6, que trae su biblioteca para Windows, Linux (x64 y arm64) y macOS (x64 y arm64). El dispositivo de siempre, en mono; si no acepta 16 kHz (el micrófono) o 44,1 kHz (la voz), se abre a su frecuencia y se convierte (`Core/Voice/PcmResampler.cs`, el de las extensiones que escuchan) |
| Tecla para hablar | sin gancho de teclado: se mira el teclado cada 30 ms (`GlobalKeys`). Windows con `GetAsyncKeyState`; Linux con `XQueryKeymap` de X11 (con Wayland, solo mientras el foco está en una app de X11); macOS con `CGEventSourceKeyState`, que puede pedir el permiso de **Monitoreo de entrada**. Solo mira: la tecla sigue llegando a la app que tiene el foco |
| Teclas | Ctrl, Alt o Mayús derechos, que se mantienen (o dos toques), como en Windows. Grabar otra combinación todavía no: una grabada en Windows vuelve a Ctrl derecho. Sin forma de mirar el teclado, se habla con el botón del micrófono de la isla |
| Sin la voz de Claupet | la del sistema: la de Windows (System.Speech), `say` en macOS y, en Linux, `spd-say` (speech-dispatcher) o `espeak-ng`, si están instalados; si no, no lee |
| Permiso en macOS | `Info.plist` lleva `NSMicrophoneUsageDescription`: macOS lo pregunta la primera vez que se abre el micrófono |
| Copia de prueba | con `--preview` en Windows no mira la tecla (la mira la app de siempre, con los mismos ajustes): se habla con el botón del micrófono |

Para macOS, `empaquetar.py` deja fuera del `.app` las bibliotecas de Whisper y de ONNX Runtime para otros sistemas
(unos 24 MB), que los paquetes copian siempre.

**Acciones** (`MacDesktop.cs`, `LinuxDesktop.cs`, `UnixDesktop.cs`, `TrayController.Actions.cs`). Lo que se pide, los
permisos por capacidad y la confirmación de reiniciar o apagar son los de Windows (`Core/Actions/`); cambia cómo se
hace cada cosa. Se hacen con los programas del sistema, sin shell (cada argumento va aparte, y lo que dice la persona
llega a AppleScript como dato, nunca dentro del script), y fuera del hilo de la interfaz: un programa que tarda o un
permiso que pregunta el sistema no la congela. Si falta un programa, la respuesta dice cuál instalar.

| Acción | macOS | Linux |
|---|---|---|
| fondo, tema | AppleScript a System Events | `gsettings` (GNOME, Cinnamon, MATE) o las herramientas de Plasma |
| volumen | AppleScript (`set volume`) | `wpctl` (PipeWire) o `pactl` |
| musica | Spotify o Música, el que esté abierto | MPRIS: `playerctl` o `dbus-send` (Spotify, navegadores…) |
| abrir | `open`; las apps, de `/Applications` y las del sistema | `xdg-open`; las apps, de sus `.desktop` (con `gio` o `gtk-launch`) |
| captura | `screencapture` (la pantalla principal, o el recorte con la herramienta de macOS) | `spectacle`, `gnome-screenshot`, `grim` y `slurp`, `maim`, `scrot` o `import`; al portapapeles con `wl-copy` o `xclip` |
| agendar | el `.ics` se abre en Calendario | el `.ics` se abre con `xdg-open` |
| recordar | un LaunchAgent que abre la app a esa hora (se quita al avisar) | `systemd-run --user --on-calendar` (no sobrevive a un reinicio: avisa la app al abrirse) |
| ventana | AppleScript; maximizar y ocultar piden Accesibilidad | `wmctrl` (y `xdotool` para minimizar), solo en X11 |
| equipo | `pmset` para bloquear y suspender; reiniciar y apagar por System Events | `loginctl`, `systemctl`; reiniciar y apagar por GNOME o Plasma (cada app puede pedir guardar), si no `systemctl` |
| brillo | no (no hay forma sin bibliotecas privadas): no se ofrece | `brightnessctl` o el servicio de energía de GNOME |
| estado | `pmset -g batt`, `vm_stat` y `sysctl` | `/sys/class/power_supply` y `/proc/meminfo` |
| portapapeles | el de Avalonia; una imagen copiada, con AppleScript | el de Avalonia; una imagen, con `wl-paste` o `xclip` |
| carpeta | las de siempre en la carpeta de usuario | las de `xdg-user-dirs` («Descargas», «Imágenes»…) |

Mientras se captura, la mascota, las nubecitas, los avisos y la isla se vuelven transparentes. En Linux con Wayland no
hay «ventana»: Wayland no deja que una app mueva las de otras. `Claupet --accion <acción> <valor>` las prueba sin abrir
ventanas (sin recorte y, en Linux y macOS, sin portapapeles de texto, que necesitan la app abierta).

En Windows, las acciones van con `Core/Actions/WindowsDesktop.cs` y sin interfaz: la captura con BitBlt en los pixeles
reales del monitor (`WinScreen`), el PNG con `PngImage.Save` y el portapapeles con la API de Windows (`WinClipboard`,
texto e imágenes); el recorte es una ventana de Avalonia (`CropWindow.cs`).

**Teclas** (`Hotkeys.cs`, `TrayController.Hotkeys.cs`), por ahora solo en Windows: los atajos globales
(`RegisterHotKey` en una ventana solo de mensajes), la tecla Copilot y la combinación grabada para hablar con un
gancho de teclado de bajo nivel, y «Grabar…» en una ventanita propia. Las teclas van por el nombre que guarda Ajustes
(«Ctrl+Alt+Space») y una tabla con su código (`VirtualKeys`, la de `System.Windows.Forms.Keys`). Una copia con
`--preview` no toma las teclas: las tiene la app de siempre. En Linux (X11) y macOS, los atajos globales quedan para
después; ahí la tecla para hablar es la que se mantiene.

**Modelo local** (`TrayController.LocalModel.cs`), por ahora solo en Windows, porque llama-server se baja compilado
para Windows (`Core/LocalModel.cs`): su descarga, su fila en Ajustes › Respuestas, su lugar en las listas de modelos
de la mascota y la isla, y su fila en el asistente de configuración.

**Lo demás**: en los tres sistemas, el tamaño de la mascota (pixeles reales enteros por casilla, con la escala de la
pantalla) y soltarle un archivo encima: lo atrapa, camina, lanza la caja a la isla (`BoxFlight.cs`,
`TrayController.Drops.cs`) y cuenta lo esencial. Solo en Windows: los accesos directos del menú Inicio y del
escritorio (`Core/Shortcuts.cs`, sin `System.Drawing`: la cabeza del ícono se pinta a mano y se guarda con
`PngImage.Save`), la ubicación de Windows para el clima (`Core/WindowsLocation.cs`), la mascota de vuelta al
desbloquear o al volver de suspender (`TrayController.Screens.cs`, con `Microsoft.Win32.SystemEvents`) y que la
mascota, la nubecita de los avisos y los avisos no le quiten el foco a la app en uso (`WinStyles.cs`).

**Extensiones** (`TrayController.Extensions.cs`), por ahora solo en Windows: el gestor, Ajustes › Extensiones en
Claupet Console (`ExtensionsPanel`), las ranuras (con el nombre de la extensión en sus filas), los comandos «/…» del
chat de la mascota, la isla y la Console, los eventos (chat, avisos, archivos soltados y sesiones), la mascota de un
paquete, el audio para las que escuchan (`SharedAudio.cs`: el micrófono y, con WASAPI, lo que suena en el equipo), la
API local y sus tokens, y las claves cifradas con DPAPI (`Core/Extensions/Secrets.cs`). Un paquete `.claupet` soltado
sobre la mascota o la isla, o elegido con «Instalar…» en el asistente de configuración, abre la revisión en la Console.

## Instalación en Windows

Se descarga un solo `Claupet-windows-x64.exe`; al abrirlo, el asistente de configuración hace de instalador
(`Core/Installation.cs`, `SetupAssistant.cs` y `SetupWindow.cs`), sin permisos de administrador:

| Paso | Qué pasa |
|---|---|
| Términos | el texto de `Core/Terminos/<idioma>.txt` (dentro del ejecutable, `Terms`); sin aceptarlo no se pasa al paso siguiente, y cerrar el asistente cierra la app. Se guarda la versión aceptada (`TermsAccepted`): al subir `Terms.Version`, la app vuelve a preguntar al abrirse |
| Instalación | la carpeta por defecto es `%LOCALAPPDATA%\Programs\Claupet`; con «Cambiar…», si la elegida no se llama `Claupet`, se usa una `Claupet` dentro. Tiene que ser de este equipo, sin `%` y con permiso de escritura sin administrador (las versiones nuevas cambian el .exe ahí): si no, no se pasa al paso siguiente. Si esta copia ya es la instalada, el paso solo lo dice |
| «Aplicar» | copia el .exe a la carpeta como `Claupet.exe` (sin la marca de «descargado de internet»; si había otra versión, la cambia como una actualización), la registra en `HKCU\…\Uninstall\Claupet` y apunta ahí los hooks (también el de permisos en la isla), el inicio con Windows y los accesos directos; luego abre la instalada (`--reiniciar`, y `--descargar-modelos` si quedaron modelos por bajar) y se cierra |

Al abrirse, la copia instalada pone al día la versión y el tamaño de su entrada (`Installation.Refresh`). Abrir otra
vez el archivo descargado, si es el mismo que se instaló (mismo tamaño y fecha), abre la instalada; otro .exe (otra
versión o compilación) abre el asistente, que ofrece reemplazarla.

**Desinstalar** (`Claupet.exe --desinstalar`, desde Configuración › Aplicaciones): pregunta; cierra los Claupet que
corren desde la carpeta instalada; quita los hooks (con respaldo), el inicio con Windows, los accesos directos, las
tareas de los recordatorios pendientes y la entrada; si la persona quiere, borra también la carpeta de datos. El .exe
no puede borrarse a sí mismo: después del último mensaje, `cmd` espera unos segundos y borra solo lo que puso la
instalación (`Claupet.exe`, `.anterior`, `.nuevo` y `claupet.ico`) y la carpeta si quedó vacía.

## Versiones nuevas

Con **Ajustes › Sistema › Buscar actualizaciones** (`CheckUpdates`, encendido de entrada), una vez al día la app lee
`https://api.github.com/repos/darrenarauz21/claupet/releases/latest`, sin sesión, y compara su etiqueta (`vX.Y.Z`) con
la versión que corre (`Core/Updates.cs`). Mira cada hora si toca (`UpdatesChecked`), la primera vez al minuto de abrirse;
sin conexión no dice nada y lo intenta la hora siguiente. De cada versión nueva avisa una vez (`UpdateNotified`) y la
fila «Claupet X está disponible» queda en Ajustes › Sistema (también en los rápidos). Una copia de prueba (`--preview`)
no busca.

| Paso | Qué pasa |
|---|---|
| Sin la clave de las Releases (`Updates.ReleaseKey` vacía, como ahora) o sin archivo para este sistema | la fila lleva a la página de la Release; no se instala nada |
| Con la clave, «Descargar e instalar…» | pregunta (con el tamaño); baja `<archivo>.firma` y comprueba que la firma sea de esa clave, de ese archivo y de esa versión; baja el archivo de este sistema (retomando si se corta) y comprueba su tamaño y su SHA-256; si algo no coincide, lo borra y no instala |
| Lista | del `.tar.gz` de Linux saca `Claupet`; del `.zip` de macOS, `Claupet.app`; el `.exe` de Windows va tal cual. «Reiniciar ahora» o salir de la app la cambia |
| Cambiarla | la que corre pasa a `<nombre>.anterior` y la nueva toma su lugar (Windows, Linux y macOS dejan renombrar lo que corre); si falla, vuelve la de antes. En macOS se cambia el `.app` entero y se le quita la cuarentena y se firma para el equipo (`codesign --sign -`), como `preparar-mac.sh`. Al abrir la nueva se borran la anterior y lo bajado, y en Windows también las bibliotecas que extrajeron las versiones de antes (`%TEMP%\.net\Claupet\<id>`, unos 32 MB cada una): cada carpeta se renombra antes de borrarla, y si otra versión abierta la está usando, Windows no deja y queda para la próxima vez (`Updates.DeleteOldExtractions`) |

La firma de las Releases es la de los paquetes de extensiones (ES256, `ExtensionSigner`/`ExtensionKeys`) sobre el
nombre del archivo, la versión, el tamaño y el SHA-256 (`ReleaseSignature`): una firma vieja no sirve para otra versión.
La clave se crea con `Claupet --extension-keygen <clave.pem>` y su privada nunca va en el repositorio; la pública (el
`.pub.pem` sin las líneas BEGIN y END) va en `Updates.ReleaseKey`. `empaquetar.py --clave <clave.pem>` firma cada
paquete (`Claupet --release-sign <clave.pem> <versión> <archivo>…`, que deja `<archivo>.firma`): las firmas se suben a la
Release junto a los paquetes.

## Avisos

| Aviso | Cuándo | Se va solo |
|---|---|---|
| Terminó | la sesión cierra su turno (`turn_duration`) | a los 6 s |
| Le está preguntando | queda una `AskUserQuestion` sin responder | no |
| Necesita su permiso | Claude Code pide permiso para una herramienta (hook) | no |
| Falló | error de la API ya sin reintentos | no |
| Reintentando | van 3 reintentos por error de la API | a los 6 s |
| Se cerró mientras trabajaba | el proceso terminó con la sesión trabajando | no |
| Uso al 80 % / 95 % | la sesión de 5 h o la semana cruzan ese umbral | a los 6 s |
| Límite reiniciado | un límite que estaba en 95 % o más vuelve a bajar | a los 6 s |
| Sesión vencida | el token de una cuenta venció | no |

Salen abajo a la derecha (en macOS, arriba, bajo la barra de menús), hasta cuatro a la vez. Con
el mouse encima no se van. Al arrancar no se avisa de lo que ya había pasado. Con las
notificaciones encendidas, las sesiones se leen cada 3 s aunque la ventana esté cerrada.

**Permisos.** Que una sesión espere permiso no queda en el registro: lo cuenta el hook
`Notification` de Claude Code, que el asistente agrega al `settings.json` de cada carpeta (con
respaldo) y que solo guarda el evento y termina:

```json
{ "hooks": { "Notification": [ { "hooks": [ { "type": "command", "command": "\"<ruta de la app>\" --hook" } ] } ] } }
```

## Sonido

Tres estilos generados por la app, **Clásico**, **Risitas** y **Chibi** (un «¡oi!», con la «o» que
se desliza a «i», y un «¡uwaaa!» si algo falla; voz femenina adulta sintetizada, como una voz de
lectura en japonés), con una variante por caso. Carpetas `clasico/`,
`risitas/` y `chibi/` (se regeneran si se borran o si cambia la versión del estilo) y `propios/`,
donde un `.wav` o `.mp3` con el mismo nombre (`terminado`, `pregunta`, `permiso`, `fallo`, `uso`,
`aviso`) manda sobre el estilo. Se reproducen con winmm en Windows, `afplay` en macOS y `paplay`,
`pw-play` o `aplay` en Linux.

## Compilar

Requiere el SDK de .NET 8.

La app vive todo el día en la bandeja, así que cuida la memoria: lee las conversaciones de Claude Code línea por
línea y en UTF-8 (`Core/JsonLines.cs`), sin cargar un archivo entero (al abrirse las recorre todas para Actividad y el
historial), y el recolector de basura va con `System.GC.ConserveMemory` en 7 (`Claupet.Avalonia.csproj`). Medido en
Windows con 1,7 GB de conversaciones: unos 185 MB privados estables y 220 MB de pico al abrirse (antes, 2 GB al abrirse
y unos 680 MB después).

```powershell
dotnet build Claupet.Avalonia -c Release                # la app, para Windows, Linux y macOS
dotnet test Claupet.Tests                               # las pruebas (en Windows)
python herramientas/empaquetar.py                       # los paquetes de los tres sistemas en paquetes/
python herramientas/empaquetar.py --clave <clave.pem>   # y la firma de cada uno (<paquete>.firma), ver Versiones nuevas
```

`empaquetar.py` (solo biblioteca estándar) genera en `paquetes/` `Claupet-windows-x64.exe`,
`Claupet-linux-x64.tar.gz` y `Claupet-macos-{arm64,x64}.zip`, con los permisos de ejecución
y el ícono de cada sistema. Todos son autocontenidos: no piden instalar .NET. Los nombres no llevan
versión porque el README enlaza a `releases/latest/download/<archivo>`: en cada versión se crea una
Release en GitHub (etiqueta `vX.Y.Z`) y se adjuntan esos cuatro archivos con el mismo nombre (y, con `--clave`, sus
cuatro `.firma`, que la app necesita para instalarse sola).
En Linux la app lleva su propia ICU (no todas las distribuciones la traen). En macOS no se puede
firmar desde Windows: `preparar-mac.sh` la firma solo para ese equipo (`codesign --sign -`) y le
quita la cuarentena.

Opciones de línea de comandos (los modos de prueba sin ventanas están en `DevCommands.cs`; `--modelo-local`, por ahora,
solo en Windows):

- `--show`: abre la ventana al arrancar; `--preview`: igual, aunque haya otra instancia.
- `--sessions`, `--activity`: abre en esa vista. `--setup`: con `--preview`, muestra el asistente.
- `--demo`: un aviso de cada tipo.
- `--mascota <chibi|dokki|gemma|ciel>`: ese personaje solo en esta ejecución (en el escritorio, los avisos, la isla y
  el ícono), sin tocar los ajustes: para verlos con `--preview`.
- `--hook`: modo hook de Claude Code; lee el evento por la entrada estándar y lo guarda.
  `--hook --permiso`: el hook de permisos y preguntas de la isla (Windows).
- `--diag`: el diagnóstico (lo mismo que **Copiar diagnóstico**). `--dump`: una consulta completa.

Solo en Windows, para probar el asistente:

- `--settings`: abre en **Ajustes**, en la última pestaña usada.
- `--soltar <archivo>`: como si se soltara ese archivo sobre la mascota (la caja, la isla y el resumen).
- `--hablar <archivo.wav>`: la voz entera con ese audio (16 kHz, mono, 16 bits) en vez del
  micrófono, como si se mantuviera la tecla mientras suena. Necesita «Micrófono» encendido.
- `--transcribir <archivo.wav>`: solo lo pasa a texto, como el micrófono, y escribe qué entendió y en
  cuánto. Sirve para comprobar que el paquete de un solo archivo encuentra las DLL de Whisper.
- `--accion <acción> <valor> [pregunta]`: hace esa acción de la mascota sin pasar por Claude ni pedir
  permiso (por ejemplo, `--accion volumen 30`) y escribe «listo», lo que falló, lo que diría o los datos
  que le devolvería a Claude. Con una pregunta, una captura o una imagen copiada se la pasa a Claude
  para que la mire (`--accion portapapeles leer "¿qué dice?"`).
- `--preguntar <texto>`: se lo pregunta a la mascota (con su cuenta y su modelo) y escribe la
  respuesta y la acción o la tarea que pediría. Con `--hacer`, además hace la acción (sin pedir
  permiso) y, si trajo datos, le vuelve a preguntar con ellos, como el chat. Con `HTTPS_PROXY`
  apuntando a un puerto cerrado sirve para probar las respuestas sin internet. Con `--modelo <id>`
  usa ese modelo (`auto` para ver qué elige el modo automático) y escribe cuál respondió.
- `--idioma <es|en>`: ese idioma solo en esta ejecución, sin tocar los ajustes (para probar las
  traducciones con `--accion` o `--preguntar`).
- `--recordatorio <id>`: la usa la tarea programada de un recordatorio para abrir la app.
- `--descargar-voz`: baja la voz de Claupet como desde **Ajustes**, con el avance.
- `--decir <F4|M1> <texto>`: lo dice con esa voz, frase por frase, y escribe cuánto tardó cada una.
- `--responder <pregunta>`: la reparte como la mascota (Ajustes › Respuestas) y dice quién respondió, en
  cuánto tiempo y qué (la acción no se hace). Usa el modelo local si está descargado.
- `--settings --pestana <Avisos|Apariencia|Asistente|Isla|Sistema>`: abre Ajustes en esa pestaña
  sin cambiar la guardada (con `--preview`, para revisarla junto a la que está abierta).
- `--modelo-local <id> [pregunta]`: baja ese modelo local (`qwen3.5-0.8b`, `qwen3.5-2b` o `qwen3.5-4b`) y
  llama-server como desde **Ajustes**, con el avance; con una pregunta, se la hace como la mascota y
  escribe cuánto tardó en arrancar y en responder.

## Si no salen avisos

**Copiar diagnóstico**, en **Ajustes**, copia un resumen sin correos ni tokens: versiones,
si las notificaciones están encendidas y cada carpeta `~/.claude*` con sus credenciales, sesiones
registradas y vivas, y el hook. Lo habitual:

- «credenciales no»: la app no ve la cuenta y no lee sus sesiones.
- «sin carpeta sessions»: esa versión de Claude Code no registra el estado de las sesiones.
- «sin hook»: no habrá avisos de permisos.
- Solo Claude Desktop o claude.ai, sin Claude Code: la app no tiene de dónde leer.

## Límites conocidos

- Los porcentajes salen de `https://api.anthropic.com/api/oauth/usage`, el mismo endpoint que usa
  `/usage` en Claude Code. **No está documentado**: si cambia, la cuenta muestra el error y se
  mantienen los últimos datos buenos.
- Ese endpoint limita mucho las consultas (las de `/usage` de Claude Code cuentan también) y su `retry-after` llega
  en 0 o no llega. La app pregunta como mucho una vez por minuto por cuenta y, si responde «demasiadas consultas»
  (429), no lo muestra: sigue con los últimos datos buenos (la hora del pie es la de esos datos) y espera 2, 4, 8, 16
  y luego 30 minutos. La espera y la última respuesta quedan en `uso.json`, así que reiniciar la app no vuelve a
  preguntar antes de tiempo. Sin datos previos, la cuenta dice «Consultando…» hasta que se pueda.
- La app **no renueva** el token, para no pisar la sesión de Claude Code: si una cuenta no se usa
  en horas, su token vence y la ventana pide abrir Claude Code con esa cuenta.
- El estado de las sesiones sale de archivos internos de Claude Code, que no están documentados.
- Linux: Avalonia 11 necesita `libICE`, `libSM` y `fontconfig` (vienen en los escritorios
  habituales). En GNOME, el ícono de la bandeja necesita la extensión AppIndicator.
- Windows 11 deja los íconos nuevos en el desbordamiento (^).
- Las extensiones y el modelo local son solo de Windows por ahora, y los atajos (el del chat y la tecla Copilot) aún no
  están en Linux ni en macOS. Ahí ya están Claupet Console, el chat con la mascota, la isla, la voz y las acciones en el
  equipo; lo demás, en las etapas siguientes.
- Comparada con la app WinForms de hasta la 2.3.0, la de Windows no tiene todavía: las transiciones de la ventana de la
  bandeja, el menú del ícono con los colores del tema, ni abrir la ventana en la pantalla del cursor (abre en la
  principal). La ventana de la bandeja y los avisos sí son tarjetas como antes: sombra y, en Windows 11, esquinas
  redondeadas con el borde del tema (`WinStyles.Card`).
- Los formatos de los otros agentes (Codex CLI, Gemini CLI, GitHub Copilot CLI) no se comprobaron con archivos reales.
- Las acciones de Linux se probaron con `--accion` en WSL (Ubuntu, sin escritorio): estado, rechazos y avisos de
  programas que faltan. Con un escritorio real (GNOME, Plasma) y en macOS, sin probar todavía.
- En Linux con Wayland, la isla y la tecla para hablar solo ven el cursor y el teclado mientras están sobre una app
  de X11 (X11 no ve las demás): puede que la isla no asome al acercar el cursor y que la tecla no responda. Se sigue
  hablando con el botón del micrófono de la isla. Sin probar todavía en un escritorio Linux.
- Con ruido de fondo, Whisper a veces «oye» frases que nadie dijo; las conocidas («subtítulos
  realizados por…») se descartan.
- La tecla Copilot se probó simulándola (Win+Mayús+F23); en algunos equipos, como los Surface, puede
  comportarse distinto.
- El brillo solo cambia en la pantalla del portátil: los monitores externos no aceptan WMI.
- El calendario no se escribe directo: el nuevo Outlook no tiene una forma local de hacerlo, así que el
  evento se abre para guardarlo con un clic. Outlook clásico podría (COM), pero solo con un perfil
  configurado.
- Las tareas de los recordatorios dejan la carpeta `\Claupet\` en el Programador de tareas, aunque
  quede vacía.

## Comprobar en macOS

Lo que no se pudo probar sin una Mac. Para quien la pruebe por primera vez:

1. `sh preparar-mac.sh` termina sin errores y la app aparece en la barra de menús (no en el Dock).
2. El número del ícono coincide con `/usage` de Claude Code. Si sale «Sin credenciales», revisar
   en **Acceso a Llaveros** que exista «Claude Code-credentials» (o «…-credentials-xxxxxxxx» si se
   usa `CLAUDE_CONFIG_DIR`) y responder **Permitir siempre** cuando macOS lo pregunte.
3. **Actividad** muestra las sesiones abiertas de Claude Code y su estado.
4. **Ajustes › Ver un aviso de ejemplo** muestra el aviso arriba a la derecha y suena.
5. **Ajustes › Configurar…** agrega el hook; al pedir permiso una sesión nueva, sale «Necesita su permiso».
6. **Iniciar con el sistema** crea `~/Library/LaunchAgents/local.claupet.plist`.
7. **Ajustes › Copiar diagnóstico**: pegar el resultado si algo falla.
8. Con la isla encendida, al acercar el cursor al borde de arriba asoma, y un clic fuera la pliega (la posición
   pasa de puntos a pixeles con la escala de la pantalla principal: con dos pantallas, revisar las dos).
9. Con el micrófono encendido, mantener Ctrl derecho escucha. macOS pregunta por el micrófono la primera vez; si
   la tecla no responde, dar a Claupet el permiso de **Monitoreo de entrada** en Privacidad y seguridad.
10. Con «Leer las respuestas en voz alta» y la voz de Claupet descargada, la respuesta suena; con «La del
    sistema», la lee `say`.
11. Acciones, desde la terminal y sin abrir la app: `Claupet.app/Contents/MacOS/Claupet --accion estado todo`
    (batería, disco y memoria), `--accion volumen 30`, `--accion tema oscuro` (macOS pregunta por la
    Automatización de System Events la primera vez) y `--accion captura recorte` (pide Grabación de pantalla).
12. Con la app abierta, «recuérdame en 2 minutos tomar agua» avisa a los 2 minutos; cerrando la app antes, el
    LaunchAgent la abre y avisa.
