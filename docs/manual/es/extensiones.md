# 🧩 Extensiones

**Español** · [English](../en/extensiones.md)

Sume a Claupet voces, mascotas, sonidos, habilidades y asistentes de otros, en paquetes `.claupet` firmados.

**En esta página:** [Qué puede traer](#qué-puede-traer-una-extensión) · [Instalar](#cómo-instalar-una-extensión) ·
[Firmas](#qué-significa-la-firma) · [Manejarlas](#cómo-manejar-sus-extensiones) · [API local](#la-api-local) ·
[Para autores](#para-autores)

> [!NOTE]
> Por ahora, las extensiones son solo de Windows. Sin extensiones, Claupet funciona igual que siempre.

## Qué puede traer una extensión

| Qué | Para qué |
|---|---|
| **Habilidades** | Comandos `/…` en el chat de la mascota, la isla y la Console, y reacciones a avisos, sesiones o archivos soltados. |
| **Mascotas** | Un personaje nuevo para elegir en **Apariencia › Mascota**. |
| **Voces y sonidos** | Otro estilo de voz o de sonidos para los avisos. |
| **Voz a texto** | Otro motor para el micrófono, en lugar de Whisper. |
| **Texto a voz** | Otra voz para leer las respuestas, en lugar de la de Claupet. |
| **Asistente** | Otro modelo para la mascota y la isla (DeepSeek, Ollama, LM Studio…), en lugar de Claude Code. |

Voz a texto, texto a voz y asistente son **ranuras exclusivas**: solo una cosa a la vez ocupa cada una.
Instalar una extensión no la pone en esas ranuras; se elige al instalarla y se cambia después.

## Cómo instalar una extensión

1. Consiga el archivo `.claupet` de alguien en quien confíe.
2. Suéltelo sobre la mascota o la isla. (O, en Claupet Console, **Ajustes › Extensiones › Instalar desde
   archivo…**; o **Instalar…** en el asistente de configuración.)
3. Claupet Console abre la **revisión**: qué versión es (o si actualiza, vuelve atrás o reinstala), quién
   la firmó, qué trae, si corre un programa, los permisos que pide y las ranuras que puede ocupar.
4. Si está de acuerdo, pulse **Instalar**. Nada se instala sin ese paso.

> [!WARNING]
> Una extensión que corre un programa o una DLL lo hace con sus permisos de Windows. Claupet controla lo que
> la extensión le pide a la app (la mascota, los avisos, el micrófono, Claude…), pero no lo que el programa
> hace por su cuenta. **Instale solo paquetes de quien confíe.**

### Los permisos que puede pedir

| Permiso | Qué deja hacer |
|---|---|
| Mover y hacer hablar a la mascota | Animarla y ponerle frases. |
| Mostrar avisos | Avisos como los de Claupet. |
| Mostrar tarjetas en la isla | Tarjetas propias en la isla. |
| Hablar con la voz de Claupet | Decir algo en voz alta. |
| Escuchar el micrófono | Recibir lo que entra por el micrófono. |
| Escuchar lo que suena en el equipo | Recibir el audio del equipo. |
| Preguntarle a Claude con su cuenta (gasta de su uso) | Usar Claude Code con su plan. |
| Ver sus sesiones de Claude Code | Enterarse de las sesiones y sus pasos. |
| Leer y escribir el portapapeles | Usar el portapapeles. |
| Conectarse a internet (o a un sitio) | Salir a la red. |
| Usar archivos en … | Leer o escribir en esa ruta. |

Si una actualización pide permisos nuevos, la revisión los muestra antes de instalarla.

## Qué significa la firma

| La revisión dice | Qué pasa |
|---|---|
| **Firma oficial de Claupet** | Se instala. |
| **Firmada por un autor en quien ya confía** | Se instala. |
| **Firmada por un autor nuevo** | Se muestra su clave. Al instalar, confía en sus próximas versiones. |
| **Sin firma** | Se instala con un aviso: no hay forma de saber quién la hizo ni si alguien la cambió. |
| **Firma inválida, revocada o de otra clave** | No se instala. Una actualización debe venir con la misma clave que la instalada. |

## Cómo manejar sus extensiones

En Claupet Console, **Ajustes › Extensiones** lista las instaladas. Toque una para ver su página:

- Si está **Encendida** o **Apagada** (y por qué se apagó).
- Su versión, autor, firma, licencia, sitio, qué trae, sus comandos y sus permisos.
- **Sus ajustes.** Las claves se guardan cifradas y no se vuelven a mostrar: se escribe otra o se borra.
- **Abrir su registro** y **Abrir su carpeta de datos**, para ver qué pasó.
- **Desinstalar…**, guardando sus datos o borrándolos con sus claves.

> [!NOTE]
> Una extensión que falla 3 veces en 10 minutos se apaga sola, su ranura vuelve a lo de Claupet y se le
> avisa por qué.

## La API local

Algunos programas de su equipo pueden pedirle cosas a Claupet a través de la **API local**, en
**Ajustes › Extensiones**. Cada programa necesita su **token**:

1. Encienda la API local y cree un token: escriba para qué programa es y qué puede pedir.
2. Cópielo en ese momento: no se vuelve a mostrar.
3. Para quitarle el acceso, use **Revocar** (pide confirmarlo).

## Para autores

¿Quiere escribir una extensión? La guía completa, con el formato del paquete, los permisos, el protocolo,
cómo firmarla y ejemplos en Python, Node.js y C, está en
[docs/extensiones/README.md](../../extensiones/README.md).

---

[← Ajustes](ajustes.md) · [Índice](../README.md) · [Preguntas frecuentes →](preguntas-frecuentes.md)
