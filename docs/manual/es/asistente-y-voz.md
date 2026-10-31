# 💬 Asistente y voz

**Español** · [English](../en/asistente-y-voz.md)

Pregúntele a Claupet por escrito o de viva voz, escuche la respuesta y pídale que le dé una mano con el
equipo. Aquí verá quién responde cada pregunta y cómo ahorrar tokens.

**En esta página:** [Hablar con Claupet](#cómo-hablar-con-claupet) · [Qué puede pedirle](#qué-puede-pedirle) ·
[Quién responde](#quién-responde-cada-pregunta) · [Modelo local](#el-modelo-local) ·
[Acciones en el equipo](#acciones-en-el-equipo) · [Voz](#voz) · [Teclas](#teclas)

> [!IMPORTANT]
> Las preguntas que van a la nube pasan por Claude Code con la cuenta que usted elija, y cuentan en su plan
> como cualquier mensaje. Lo que responde la app sola o el modelo local no gasta tokens.

## Cómo hablar con Claupet

Hay tres lugares, y los tres comparten la misma **Conversación rápida**:

| Dónde | Cómo |
|---|---|
| **La nubecita de la mascota** | Encienda **Ajustes › Asistente › Chat con la mascota** y haga clic en la mascota, o pulse **Ctrl+Alt+Espacio** *(Windows)*. |
| **La isla** | Pestaña **Chat** (ver [Avisos e isla](isla.md#el-chat-de-la-isla)). |
| **Claupet Console** | **Chat**, con conversaciones que se guardan (ver [Claupet Console](console.md#chat)). |

La mascota responde corto, a su manera y con su personalidad; la isla y la Console, más completo.

<p align="center">
  <img src="../../capturas/mascota-voz.png" alt="Chibi escucha la pregunta, piensa y responde cuánto queda del límite semanal" width="720">
</p>

## Qué puede pedirle

- **Preguntas:** lo que quiera. Puede buscar y leer páginas en internet para responderle.
- **Su uso:** «¿cuánto me queda de Claude?».
- **El clima, la hora, cuentas y conversiones:** «¿va a llover mañana?», «15 % de 200», «10 km a millas».
- **Un archivo:** suéltelo sobre la mascota o la isla y se lo resume.
- **Cosas del equipo:** el volumen, la música, el fondo, el modo oscuro, capturas, notas, recordatorios…
  (ver [Acciones en el equipo](#acciones-en-el-equipo)).
- **Tareas grandes:** lo que no puede hacer solo se lo encarga a Claude Code. Le pregunta «¿Abro Claude
  Code en … con esta tarea?» y abre una sesión con ese pedido.

> [!TIP]
> **Sin internet** igual le saluda y le dice la hora y la fecha. A lo demás le avisa que no hay conexión.

## Quién responde cada pregunta

En Claupet Console, **Ajustes › Respuestas** reparte las preguntas entre tres motores:

| Motor | Qué es | Gasta tokens |
|---|---|:---:|
| **Sin IA** | La app sola: reconoce frases cortas y claras («sube el volumen», «¿qué hora es en Tokio?»). Siempre activo. | No |
| **Modelo local** | Un modelo pequeño en su equipo *(Windows)*. | No |
| **Nube** | Claude Code con el modelo elegido. | Sí |

**Modo** elige el reparto:

| Modo | Qué hace |
|---|---|
| **Por defecto** | Sin IA la hora, la fecha, el clima, el estado del equipo y el uso de Claude; lo demás, la nube. |
| **Ahorro de tokens** | Sin IA todo lo que la app sabe, el modelo local en lo demás, y la nube si hace falta. |
| **Solo en este equipo** | Igual, pero nunca llega a la nube, salvo internet, archivos y tareas. |
| **Personalizado** | Usted elige el primer motor de cada tipo de pregunta. |

Debajo:

- **Si no entiende › Pasar al siguiente motor:** hasta dónde sigue una pregunta que el primero no entendió.
- **Mostrar quién respondió:** un chip debajo de cada respuesta («Sin IA · Open-Meteo», «Nube · Haiku»).
- **Mi ciudad:** para el clima. «Automática (por la IP)», la ubicación de Windows o «Escribir una ciudad…».
- **Ahorro:** cuántas respuestas de esta semana no usaron la nube.

> [!NOTE]
> Sin IA nada sale de su equipo, salvo el clima: el nombre de la ciudad (o las coordenadas) va a Open-Meteo
> y, si **Mi ciudad** es automática, su IP a ipwho.is. Internet, archivos y tareas van siempre a la nube.

### El modelo de la mascota y de la isla

**Modelo de la mascota** (en **Ajustes › Asistente**; Haiku de entrada) y **Modelo de la isla** (en
**Ajustes › Isla**; «El de sus sesiones» de entrada) eligen el modelo de la nube. **Automático** elige el
modelo y el esfuerzo según la pregunta: lo corto a Haiku, explicar o escribir a Sonnet, analizar a fondo o
un error a Opus. **Cuenta que responde** elige con qué cuenta de Claude Code.

## El modelo local

*Solo en Windows.* Un modelo pequeño (Qwen3.5) que corre en su equipo, responde lo simple sin gastar tokens y
funciona sin internet.

1. En **Ajustes › Respuestas › Modelo local**, elija el tamaño:

   | Modelo | Descarga | Memoria al responder |
   |---|---|---|
   | Qwen3.5 0.8B | 533 MB | Unos 1 GB |
   | Qwen3.5 2B *(el propuesto)* | 1,3 GB | Unos 2 GB |
   | Qwen3.5 4B | 2,7 GB | Unos 3,5 GB |

2. Pulse el enlace de descarga de la fila de abajo. Se ve el avance y se puede cancelar.
3. Elija un modo que lo use (**Ahorro de tokens** o **Solo en este equipo**), o elija «Local» como modelo
   de la mascota o de la isla.

| Opción | Qué hace |
|---|---|
| **Tarjeta gráfica** | **Automático** (la usa si arranca con ella), **Siempre** o **Nunca** (solo el procesador). Sirve con cualquier marca, dedicada o integrada. |
| **Liberar la memoria tras** | Los minutos sin uso tras los que suelta la memoria (5, de entrada). |

> [!WARNING]
> El modelo local puede inventar datos en las preguntas generales. Úselo para lo simple y deje lo
> importante a la nube.

## Acciones en el equipo

Claupet hace cosas en su equipo cuando se lo pide en el chat o en la isla. Es una lista cerrada, para que una
frase mal entendida no pueda hacer otra cosa:

| Qué | Por ejemplo |
|---|---|
| Fondo, modo claro u oscuro, color de la app | «pon modo oscuro» |
| Volumen y música | «sube el volumen», «pausa la música» |
| Abrir apps, sitios y ajustes | «abre Spotify» |
| Capturas de pantalla o un recorte (y mirarlas) | «captura la pantalla y dime qué dice el error» |
| Agendar en su calendario | «agenda una reunión mañana a las 10» |
| Notas | «anota comprar café», «¿qué anoté de la reunión?» |
| Recordatorios, aunque la app esté cerrada | «recuérdame tomar agua en 20 minutos» |
| Ventanas | «minimiza todo», «trae el navegador» |
| El equipo | bloquear, suspender, reiniciar o apagar |
| Brillo | «pon el brillo al 40» (solo la pantalla del portátil) |
| Estado | batería, disco y memoria |
| Portapapeles y carpetas | «copia esto», «abre Descargas» |

### Permisos

Cambiar el fondo, el tema, el color, el volumen, la música o abrir algo no pide permiso. Lo demás pide
permiso **la primera vez**, en la isla o en la nubecita:

- **Permitir siempre**: no vuelve a preguntar.
- **Solo esta vez**.
- **No**.

Se dan o se quitan en Claupet Console, **Ajustes › Asistente › Permisos**: un interruptor por capacidad.
**Reiniciar** y **apagar** se confirman cada vez.

> [!NOTE]
> En macOS y Linux las acciones usan los programas del sistema; si falta uno, la respuesta dice cuál
> instalar. En macOS no se cambia el brillo, y en Linux con Wayland no se manejan ventanas.

## Voz

### Cómo hablarle

1. Encienda **Ajustes › Asistente › Micrófono**. La primera vez se descarga el modelo de voz (Whisper,
   488 MB). La voz se pasa a texto en su equipo.
2. **Mantenga Ctrl derecho**, hable y suelte. O **tóquelo dos veces** para hablar sin mantenerlo: envía al
   callar o con otro toque.
3. **Esc** cancela.

En la isla también hay un botón de micrófono. **Tecla para hablar** cambia la tecla (Ctrl, Alt o Mayús
derechos, u otra grabada en Windows).

<p align="center">
  <img src="../../capturas/isla-escucha.png" alt="La isla escuchando" width="560">
</p>

> [!TIP]
> **¿El micrófono no le oye?** Si está silenciado o no da ningún sonido (una entrada virtual, por ejemplo),
> Claupet se lo dice y le indica dónde revisarlo. Vea [Preguntas frecuentes](preguntas-frecuentes.md#el-micrófono-no-me-oye).

### Cómo escuchar las respuestas

1. Encienda **Ajustes › Asistente › Leer las respuestas en voz alta**. No necesita el micrófono: también
   lee lo que responde a un mensaje escrito.
2. En **Voz**, elija **Femenina** o **Masculina** (la voz de Claupet, que se descarga una vez, unos
   400 MB) o la del sistema.

La voz de Claupet corre en su equipo y a veces se ríe o suspira. Mientras se descarga, lee la voz del
sistema.

## Teclas

*Solo en Windows*, con el chat o la isla encendidos:

| Tecla | Qué hace |
|---|---|
| **Ctrl+Alt+Espacio** | Abre el chat (en la isla, si está encendida; si no, en la nubecita). |
| **Ctrl+Alt+A** | Va al permiso o la pregunta pendiente. |
| **Ctrl+Alt+T** | Trae la terminal de la sesión que trabaja o espera. |
| **Ctrl+Alt+N** | Abre o pliega la isla. |
| **Ctrl+Alt+S** | Silencia los avisos o los vuelve a hacer sonar. |
| **Tecla Copilot** | Con el micrófono, un toque escucha; sin él, abre el chat. |

<p align="center">
  <img src="../../capturas/ajustes-teclas.png" alt="Ajustes › Asistente, con la sección Teclas" width="560">
</p>

En **Ajustes › Asistente › Teclas** se cambia el **Atajo del chat**, se enciende la **Tecla Copilot** y se
ven los **Atajos de teclado** (si otra app ya usa uno, aparece «en uso»). Para grabar una tecla nueva, elija
**Grabar otra combinación…** y púlsela.

<p align="center">
  <img src="../../capturas/grabar-tecla.png" alt="Grabar una tecla" width="420">
</p>

---

[← Avisos e isla](isla.md) · [Índice](../README.md) · [Ajustes →](ajustes.md)
