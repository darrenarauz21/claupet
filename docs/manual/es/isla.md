# 🏝️ Avisos e isla

**Español** · [English](../en/isla.md)

Claupet le avisa cuando una sesión termina o le necesita, y la isla del borde de arriba le deja responder
permisos y preguntas sin buscar la terminal.

**En esta página:** [Los avisos](#los-avisos) · [La isla](#la-isla) ·
[Permisos y preguntas en la isla](#permisos-y-preguntas-en-la-isla) · [El chat de la isla](#el-chat-de-la-isla) ·
[Archivos en caja](#archivos-en-caja) · [Resumen de la semana](#resumen-de-la-semana)

## Los avisos

Un mensajito con la mascota y su sonido, abajo a la derecha (en macOS, arriba, bajo la barra de menús).

| Aviso | Cuándo | Se va solo |
|---|---|---|
| **Terminó** | Una sesión cerró su turno. | A los 6 s |
| **Le está preguntando** | Claude Code le hizo una pregunta. | No |
| **Necesita su permiso** | Una sesión pide permiso para una herramienta. | No |
| **Falló** | Un error de la API, ya sin reintentos. | No |
| **Reintentando** | Van 3 reintentos por un error de la API. | A los 6 s |
| **Se cerró mientras trabajaba** | La sesión terminó a mitad de un turno. | No |
| **Uso al 80 % / 95 %** | La sesión de 5 horas o la semana cruzan ese umbral. | A los 6 s |
| **Límite reiniciado** | Un límite que estaba al 95 % o más vuelve a bajar. | A los 6 s |
| **Sesión vencida** | El acceso de una cuenta venció. | No |

Hay hasta cuatro a la vez, y con el cursor encima no se van. Un clic abre lo que corresponde.

**Cómo configurarlos:** en **Ajustes › Avisos**, encienda o apague **Notificaciones** y **Sonido**, y elija el
**Estilo de sonido**. **Ver un aviso de ejemplo** le muestra uno.

> [!IMPORTANT]
> El aviso **Necesita su permiso** depende de un hook que Claupet agrega a Claude Code (con un respaldo de su
> `settings.json`). Si no le llegan, use **Ajustes › Sistema › Avisos de permisos (hook) › Configurar…**.

## La isla

Una ventanita oscura en el centro del borde de arriba de la pantalla. Enciéndala en
**Ajustes › Isla › Isla en el borde de arriba**.

<p align="center">
  <img src="../../capturas/isla-chat.png" alt="La isla con una respuesta" width="560">
</p>

1. Acerque el cursor al centro del borde de arriba: la isla asoma, compacta.
2. Con el cursor encima (o con un clic, si apagó **Abrir al pasar el cursor**) se abre con tres pestañas:

   | Pestaña | Qué tiene |
   |---|---|
   | **Actividad** | Cada sesión con su carril de pasos. Un paso que editó un archivo abre su diff. |
   | **Chat** | Una conversación rápida con Claupet. |
   | **+ Sesión** | Abre una sesión nueva de Claude Code. |

3. Se pliega al irse el cursor o con un clic en cualquier otra parte, salvo si está escribiendo, esperando
   una respuesta o acaba de llegar una.

> [!TIP]
> Con **A la vista mientras algo trabaja**, la isla queda compacta a la vista mientras alguna sesión
> trabaja: «2 trabajando», «1 le espera». En Windows, **Ctrl+Alt+N** la abre o la pliega.

## Permisos y preguntas en la isla

Responda los permisos y las preguntas de Claude Code desde la isla, sin ir a la terminal.

### Cómo encenderlo

1. En Claupet Console, abra **Ajustes › Isla**.
2. En **Permisos y preguntas en la isla**, pulse **Configurar…**.
3. Claupet agrega a Claude Code el hook `PermissionRequest` (antes guarda un respaldo de su `settings.json`).

Funciona en las sesiones de Claude Code que se abran desde ese momento.

### Cómo responder

Cuando una sesión pide permiso, la isla compacta dice «Necesita su permiso · clic para ver». Ábrala y elija:

- **Permitir**
- **Siempre en esta sesión**
- **Denegar**

Las preguntas («Le está preguntando · clic para ver») muestran sus opciones: toque una. Si se pueden elegir
varias, marque las que quiera y pulse **Siguiente** (o **Enviar**, en la última). **Responder en la
terminal** deja que Claude Code pregunte allí, como siempre.

> [!NOTE]
> Si Claupet está cerrada o nadie responde a tiempo (unos dos minutos), Claude Code pregunta en la
> terminal como siempre. Salir de la tarjeta no la responde: queda esperando en la isla compacta.

> [!TIP]
> En Windows, **Ctrl+Alt+A** va directo al permiso o la pregunta pendiente. Los permisos de las sesiones de
> [Claupet Terminal](terminal.md#permisos-y-preguntas) también llegan a la isla.

## El chat de la isla

En la pestaña **Chat**, escriba su pregunta y pulse Intro, o use el micrófono. Es la misma
**Conversación rápida** de la nubecita de la mascota y de Claupet Console.

- Se renueva a los 10 minutos sin escribir, o con **Nueva conversación**.
- Si le pide algo que es una tarea para Claude Code, le ofrece abrir una sesión con ese pedido.
- **Modelo de la isla**, en **Ajustes › Isla**, elige quién responde («El de sus sesiones», de entrada).

Lo que puede preguntarle y hacer está en [Asistente y voz](asistente-y-voz.md).

## Archivos en caja

Suelte un archivo sobre la mascota: lo atrapa en una caja, camina hacia la isla, la lanza y la isla lo abre,
lo lee y se lo resume mientras escribe.

<p align="center">
  <img src="../../capturas/caja.png" alt="La mascota lleva un archivo en una caja hasta la isla, que lo resume" width="560">
</p>

Debajo del resumen:

- **Preguntar algo más** sobre el archivo.
- **Copiar** el resumen.
- **Seguir en una sesión**: abre Claude Code en la carpeta del archivo para trabajar con él.

También puede soltar el archivo directamente sobre la isla. Para resumirlo, Claude solo puede leer la
carpeta de ese archivo.

> [!NOTE]
> Sin la isla, la mascota no camina: piensa con la caja en las manos y cuenta lo esencial en su nubecita.
> Para que no analice archivos, apague **Analizar los archivos que le suelte** en **Ajustes › Asistente**.

## Resumen de la semana

Con **Resumen de la semana, los lunes** encendido (en **Ajustes › Isla**), la isla le muestra cada lunes
cómo le fue la semana anterior: tokens, sesiones, pedidos, pasos, el día más activo y su modelo favorito. **El de esta
semana › Ver ahora** lo muestra cuando quiera.

---

[← Las mascotas](mascotas.md) · [Índice](../README.md) · [Asistente y voz →](asistente-y-voz.md)
