# Extensiones de Claupet

Sin extensiones, Claupet funciona igual que siempre. Una extensión es un paquete `.claupet` que puede:

- **ocupar una ranura exclusiva**: voz a texto, texto a voz o el asistente. Al ocuparla, lo que trae
  Claupet para esa ranura (Whisper, Supertonic y la voz de Windows, o `claude -p`) deja de cargarse;
  así no chocan dos modelos;
- **sumar** habilidades (comandos `/…` y eventos), mascotas, voces y estilos de sonido.

Una habilidad o un proveedor puede estar escrito en cualquier lenguaje. Hay tres formas de correrlo:

| Forma | Qué es | Cómo habla |
|---|---|---|
| `process` | Un programa (del paquete o del sistema: `python`, `node`, `codex`…) | JSON-RPC por stdin/stdout, o texto plano (`protocol: "text"`, solo asistentes) |
| `native` | Una biblioteca con la ABI de C (`.dll`, `.so`, `.dylib`): C, C++, Rust, Go, Zig, .NET NativeAOT… | La ABI de [`claupet.h`](claupet.h), cargada en un proceso aparte |
| `http` | Un servidor | Los formatos de OpenAI (DeepSeek, Ollama, LM Studio, OpenRouter, Groq, vLLM…) o JSON-RPC por HTTP |

Hay ejemplos en [`ejemplos/`](ejemplos): [Python](ejemplos/python), [Node.js](ejemplos/node) y [C](ejemplos/c).

> **Seguridad.** Un programa o una DLL corre con los permisos de Windows de la persona. Claupet
> controla lo que la extensión le pide a la app (la mascota, los avisos, el micrófono, Claude…), pero no lo
> que el programa hace por su cuenta. Por eso, al instalar, Claupet muestra quién firmó el paquete, los
> permisos y si corre código. Instale solo paquetes de quien confíe.

---

## 1. Ranuras

| Ranura (`provides`) | Exclusiva | Reemplaza a | Necesita `runtime` |
|---|---|---|---|
| `speech-to-text` | Sí | Whisper | Sí |
| `text-to-speech` | Sí | La voz de Claupet (Supertonic) y la de Windows | Sí |
| `assistant` | Sí | Claude Code (`claude -p`) en la mascota y la isla | Sí |
| `skill` | No | — | Sí |
| `pet` | No (se elige una mascota) | El chibi, si la persona elige la del paquete | No |
| `voice-style` | No | — | No |
| `sounds` | No (se elige un estilo) | — | No |

Una extensión puede traer varias cosas a la vez (el ejemplo de Node trae `skill`, `speech-to-text`,
`text-to-speech` y `assistant`). Que la instale no significa que ocupe las ranuras exclusivas: al instalarla
se ofrecen casillas para usarla en cada una, y se cambia después en **Ajustes › Sistema › Extensiones**.
Una extensión que falla **3 veces en 10 minutos** se apaga sola; su ranura vuelve al integrado y se avisa
por qué.

---

## 2. El paquete `.claupet`

Es un ZIP con `manifest.json` en la raíz:

```
mi-extension.claupet
├── manifest.json
├── main.py                  (o bin/…, la DLL, las tiras PNG, los .wav…)
└── _claupet/signature.json  (la firma; la agrega --extension-pack)
```

- Las rutas van con `/`, relativas, sin `..`, sin unidades ni enlaces simbólicos. `_claupet/` está reservado.
- Límites: 5000 archivos, 2 GB por archivo y 4 GB en total, descomprimidos. Un archivo que se
  descomprime más de 200 veces su tamaño se rechaza (bombas ZIP).
- Instalar y actualizar es atómico: lo nuevo se extrae aparte y reemplaza a lo anterior de una vez.
- Se instala en `%APPDATA%\Claupet\extensiones\`:
  - `paquetes\<id>\`: lo instalado, tal cual se firmó, de **solo lectura** para la extensión. Antes de cada
    arranque se comprueba que no haya cambiado; si cambió, la extensión se apaga.
  - `datos\<id>\`: lo que la extensión guarda. También es su carpeta de trabajo y donde queda su registro
    (`registro.log`, lo que escribe en stderr, los últimos 512 KB).
  - `claves.json`: las claves (`secret`) de los ajustes, cifradas con DPAPI para el usuario de Windows.
  - `confianza.json`: los autores aceptados, la clave fijada de cada extensión y las revocaciones.

Se instala soltando el `.claupet` sobre la mascota o la isla, o con **Instalar desde archivo…** en
Claupet Console (**Ajustes › Extensiones**). En los dos casos la Console muestra antes la revisión (qué es,
su firma, sus permisos y sus ranuras) y se instala solo si se acepta allí.

---

## 3. `manifest.json`

```json
{
  "schema": 1,
  "id": "autor.nombre",
  "name": { "es": "Notas", "en": "Notes" },
  "description": { "es": "…", "en": "…" },
  "version": "1.0.0",
  "author": "Su nombre",
  "license": "MIT",
  "homepage": "https://…",
  "claupet": ">=2.3.0",
  "api": 1,
  "provides": ["skill"],
  "runtime": { "type": "process", "program": "python", "args": ["-u", "{package}/main.py"] },
  "commands": [ { "name": "nota", "description": { "es": "Guarda una nota", "en": "Saves a note" } } ],
  "events": ["notice"],
  "permissions": ["pet"],
  "settings": [ { "key": "maximo", "type": "number", "label": "Notas que se guardan", "default": 50 } ]
}
```

Se valida entero al instalar y al cargar. Lo que no se entiende en una lista cerrada (ranuras, permisos,
formatos, eventos) rechaza el paquete; las claves desconocidas se ignoran, para que un manifiesto con
campos opcionales nuevos siga sirviendo. Se permiten comentarios y comas finales.

| Campo | Obligatorio | Qué es |
|---|---|---|
| `schema` | Sí | `1`, la versión de este formato |
| `id` | Sí | `autor.nombre`: minúsculas, números y guiones (autor hasta 39 caracteres; nombre hasta 63) |
| `name` | Sí | Hasta 60 caracteres. Un texto o uno por idioma: `{"es": "…", "en": "…"}` |
| `description` | No | Hasta 400 caracteres, igual que `name` |
| `version` | Sí | Versión semántica: `1.0.0`, `1.2.0-beta.1` |
| `author`, `license` | No | Hasta 80 caracteres |
| `homepage` | No | Una dirección `https` |
| `claupet` | No | La versión mínima de Claupet: `>=2.3.0` |
| `api` | No | La versión del protocolo (por defecto `1`) |
| `provides` | Sí | Las ranuras de la [sección 1](#1-ranuras) |
| `runtime` | Si trae `skill` o una ranura exclusiva | Cómo corre (sección 4) |
| `commands` | No | Solo con `skill`: hasta 32, `name` como `^[a-z][a-z0-9-]{0,31}$` y `description` de hasta 120 caracteres |
| `events` | No | Solo con `skill`: `notice`, `toolUse`, `session`, `drop` y `chat` (sección 7) |
| `permissions` | No | Sección 5 |
| `settings` | No | Hasta 32 ajustes (abajo) |
| `voices` | Con `voice-style` | Sección 10 |
| `pet` | Con `pet` | Sección 10 |
| `sounds` | Con `sounds` | Sección 10 |

Un texto por idioma se muestra en el idioma de la app; si falta ese idioma, en inglés, y si tampoco está,
el primero.

### Ajustes

```json
{ "key": "apiKey", "type": "secret", "label": { "es": "Clave", "en": "Key" } }
```

| `type` | Se edita como | Notas |
|---|---|---|
| `string` (por defecto) | Texto | |
| `number` | Número | Se guarda con punto decimal |
| `bool` | Casilla | `"true"` o `"false"` |
| `choice` | Lista | Necesita `choices: ["a", "b"]` |
| `secret` | Clave oculta | Cifrada con DPAPI; nunca va en `settings.json` ni tiene `default` |

`key` sigue el patrón `^[a-zA-Z][a-zA-Z0-9_]{0,31}$`. La extensión recibe los valores **como texto** (o
`null`), con las claves incluidas, en `initialize` y en `settings/changed`. Si una extensión tiene un ajuste
llamado `model`, ese es el modelo que se usa (en vez de `runtime.model`).

---

## 4. `runtime`

### `process`: un programa

```json
{ "type": "process", "program": "python", "args": ["-u", "{package}/main.py"] }
{ "type": "process", "command": { "win-x64": "bin/notas.exe", "linux-x64": "bin/notas" } }
```

- `program` es un programa del sistema, **solo el nombre** (`python`, `node`, `codex`); se busca en el PATH
  como lo haría la consola. `command` es un archivo del paquete: una ruta, o una por plataforma
  (`win-x64`, `win-x86`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64` o `any`). Lleva uno
  de los dos, no ambos. En Windows, un `.cmd` o `.bat` corre con `cmd.exe`.
- En `args`, `{package}` es la carpeta del paquete y `{data}` la de sus datos.
- Corre en `datos\<id>\`, con estas variables de entorno: `CLAUPET_API` (`1`), `CLAUPET_EXTENSION_ID`,
  `CLAUPET_PACKAGE`, `CLAUPET_DATA`, `CLAUPET_LANGUAGE` y, si la API local está encendida, `CLAUPET_API_URL` y
  `CLAUPET_API_TOKEN`. No hereda `ANTHROPIC_API_KEY` ni las variables de Claude Code. Si Windows tiene un
  proxy, recibe `HTTPS_PROXY`.
- En Windows corre en un *Job* que la cierra si Claupet termina, aunque sea de golpe: un programa de una
  extensión nunca queda corriendo solo.
- stdout es del protocolo. Lo que se escriba en **stderr** queda en `registro.log`, que se lee como UTF-8.
- Arranca la primera vez que hace falta, no al abrir la app, salvo las habilidades que escuchan eventos.

> En Windows, `python` puede ser el alias de Microsoft Store, que no corre nada si Python no se instaló
> desde la tienda. Si su extensión es para Windows, considere `py` (el lanzador de python.org) o traer su
> propio ejecutable en `command`.

#### `protocol: "text"`: un asistente de consola

Para usar como asistente un programa de consola cualquiera (`codex exec`, `ollama run`, `llm`…), sin
escribir código:

```json
{
  "provides": ["assistant"],
  "runtime": { "type": "process", "protocol": "text", "program": "ollama", "args": ["run", "{model}"], "model": "llama3.2" }
}
```

Con `codex exec`, la respuesta puede salir de un archivo: `"args": ["exec", "-o", "{output}", …]`.
Revise las opciones de su versión de Codex para que lea la pregunta por stdin.

Por cada pregunta se corre una vez. Claupet escribe la conversación por stdin: primero las instrucciones,
luego cada turno como `Persona: …` y `Tú: …` y, al final, `Tú:`. En inglés son `Person:` y `You:`. Lee la
respuesta de stdout mientras llega. Si
los argumentos nombran `{output}`, la respuesta es lo que el programa deja en ese archivo. `{model}` es el
modelo elegido. Tiene 5 minutos; un código de salida distinto de 0 es un error, con la última línea de
stderr.

### `native`: una DLL con la ABI de C

```json
{ "type": "native", "library": { "win-x64": "bin/eco.dll", "win-arm64": "bin/eco-arm64.dll" }, "threading": "single" }
```

Claupet **no** carga la biblioteca en su proceso: arranca otra vez su propio ejecutable con
`--anfitrion-extension <dll>`, que la carga y la traduce a JSON-RPC. Si la DLL se cuelga o revienta, cae ese
proceso y Claupet sigue. Por eso la DLL debe ser **de la misma arquitectura que Claupet** (x64 o arm64):
una DLL x86 se rechaza con un mensaje que lo dice.

La ABI completa está en [`claupet.h`](claupet.h). En resumen:

```c
int32_t claupet_api_version(void);                 /* devuelve 1 */
int32_t claupet_init(const claupet_host* host);    /* 0: lista */
int32_t claupet_call(int64_t id, const char* method, const char* params_json, char** result_json);
void    claupet_free(char* p);
void    claupet_cancel(int64_t id);                /* opcional */
void    claupet_shutdown(void);
```

Los métodos y sus parámetros son los mismos de JSON-RPC (sección 6). `host->call` le pide algo a Claupet
(la API de la sección 8) y `host->notify` manda notificaciones, como `$/progress`. Con `"threading": "single"`
Claupet no llama a `claupet_call` desde dos hilos a la vez; con `"multi"` (por defecto), sí.

### `http`: un servidor

```json
{
  "provides": ["assistant"],
  "runtime": { "type": "http", "format": "openai-chat", "url": "https://api.deepseek.com/v1",
               "model": "deepseek-chat", "auth": "bearer", "authSetting": "apiKey" },
  "permissions": ["network:api.deepseek.com"],
  "settings": [ { "key": "apiKey", "type": "secret", "label": "Clave de DeepSeek" } ]
}
```

| `format` | Ranura | Lo que se llama |
|---|---|---|
| `openai-chat` | `assistant` | `POST {url}/chat/completions` con `stream: true` (SSE); también acepta la respuesta entera en JSON |
| `openai-transcription` | `speech-to-text` | `POST {url}/audio/transcriptions`, multipart con el WAV (16 kHz, mono) y `response_format: json` |
| `openai-speech` | `text-to-speech` (y `voice-style`) | `POST {url}/audio/speech` con `response_format: wav`; la voz sale del ajuste `voice` |
| `claupet-rpc` | Cualquiera | `POST {url}` con un mensaje JSON-RPC por pedido (sección 6), sin avances: solo cuenta la respuesta final. El servidor no puede llamar a la API de Claupet |

- `url` es `https`; `http` se acepta solo para este equipo (`localhost`, `127.0.0.1`), como Ollama o LM
  Studio. Fuera de este equipo, el host debe estar en `permissions` (`network:api.deepseek.com`,
  `network:*.groq.com` o `network`, que permite cualquier host).
- La dirección puede venir de un ajuste: `"url": "{setting:baseUrl}"` o `"https://{setting:region}.api.com"`.
  Con un valor nuevo se vuelve a comprobar el destino: https y un host declarado.
- `auth`: `none`, `bearer` (`Authorization: Bearer <clave>`) o `header:<Nombre>` (por ejemplo
  `header:x-api-key`). La clave sale del ajuste `secret` que nombra `authSetting`.

Un modelo local con Ollama:

```json
{
  "schema": 1, "id": "ejemplo.ollama", "name": "Ollama", "version": "1.0.0", "provides": ["assistant"],
  "runtime": { "type": "http", "format": "openai-chat", "url": "{setting:baseUrl}" },
  "settings": [
    { "key": "baseUrl", "type": "string", "label": "Dirección", "default": "http://localhost:11434/v1" },
    { "key": "model", "type": "string", "label": "Modelo", "default": "llama3.2" }
  ],
  "permissions": ["network"]
}
```

---

## 5. Permisos

| Permiso | Para qué | Lo hace cumplir Claupet |
|---|---|---|
| `pet` | `pet/animate`, `pet/say` | Sí |
| `notices` | `notice/show` | Sí |
| `island` | `island/card` | Sí |
| `voice` | `voice/speak` | Sí |
| `microphone` | `audio/subscribe` con `source: "microphone"` | Sí |
| `audio:system` | `audio/subscribe` con `source: "system"` (lo que suena en el equipo) | Sí |
| `claude` | `claude/ask` (gasta del uso de la persona) | Sí |
| `sessions` | `sessions/list` y los eventos `session` y `toolUse` | Sí |
| `clipboard` | `clipboard/read`, `clipboard/write` | Sí |
| `network`, `network:<host>` | Conectarse a internet, o a ese host (`*.dominio` vale) | Solo en `http`; en un programa, se declara y se muestra |
| `files:<ruta>` | Usar archivos en esa ruta | Se declara y se muestra |

Un método sin su permiso responde el error `-32001`. Si una actualización pide permisos nuevos, se
muestran al instalarla.

---

## 6. JSON-RPC

Es JSON-RPC 2.0 en UTF-8. Por stdio, cada mensaje lleva el encabezado `Content-Length`, como en LSP:

```
Content-Length: 52\r\n
\r\n
{"jsonrpc":"2.0","id":1,"method":"shutdown","params":{}}
```

Un mensaje puede pesar hasta 64 MB. Los dos lados piden y responden a la vez: mientras atiende un pedido
de Claupet, la extensión puede pedirle algo a Claupet y esperar su respuesta (los ejemplos muestran cómo).

### Lo que Claupet le pide a la extensión

| Método | Cuándo | `params` | Respuesta | Tiempo |
|---|---|---|---|---|
| `initialize` | Al arrancar | `claupetApi`, `claupet` (versión), `language`, `platform`, `extension {id, version, packageFolder, dataFolder}`, `permissions` (los concedidos), `settings` | Un objeto (puede ir vacío) | 20 s |
| `shutdown` | Al detenerla | `{}` | `null` | 3 s |
| `exit` (notificación) | Después de `shutdown` | — | Terminar el proceso. A los 3 s, Claupet lo cierra | — |
| `settings/changed` (notificación) | La persona cambió un ajuste | Los ajustes, como en `initialize` | — | — |
| `skill/command` | La persona escribió `/comando texto` | `command`, `text`, `language` | `{"reply": "…"}` | 5 min |
| `skill/event` (notificación) | Un evento que escucha (sección 7) | `event`, `data` | — | — |
| `speechToText/transcribe` | Pasar la voz a texto | `audio` (PCM de 16 bits, mono, en base64), `sampleRate` (16000), `language` | `{"text": "…"}` | 2 min |
| `textToSpeech/synthesize` | Leer en voz alta | `text`, `voice` (o `null`), `language`, `stream: true` | `{}`, o `{"audio", "sampleRate"}` con lo que no mandó por avances | 5 min |
| `assistant/respond` | Una pregunta a la mascota o la isla | `system`, `messages [{role: "user"/"assistant", text}]` (los últimos 8 turnos; la pregunta va al final), `model`, `language`, `stream: true` | `{"text": "…", "model": "…"}` | 5 min |

**Avances.** Durante un pedido, la extensión manda `$/progress` con `{"id": <id del pedido>, "value": …}`:

- `skill/command` y `assistant/respond`: `{"text": "trozo"}`. Se muestra mientras llega; la respuesta final
  lo reemplaza (en `assistant/respond`, si la respuesta no trae `text`, vale lo que llegó en trozos).
- `textToSpeech/synthesize`: `{"audio": "<PCM de 16 bits, mono, en base64>", "sampleRate": 22050}`. Suena
  en cuanto llega cada trozo (entre 8000 y 192000 Hz).

**Cancelar.** Si la persona cancela o se acaba el tiempo, Claupet manda `$/cancelRequest {"id"}` y deja de
esperar. La extensión puede dejar de trabajar en ese pedido.

**Errores.** Un error de JSON-RPC (`{"code", "message"}`) se le muestra a la persona con su mensaje. Los
códigos: `-32700` JSON inválido, `-32600` pedido inválido, `-32601` método desconocido, `-32602`
parámetros inválidos, `-32603` error interno, `-32800` cancelado, `-32001` sin permiso, `-32002` conexión
cerrada y `-32003` tiempo agotado.

Una habilidad responde a los comandos que declaró en `commands`. Si dos habilidades declaran el mismo
comando, lo atiende la primera y Ajustes avisa del choque.

---

## 7. Eventos

Una habilidad que declara `events` arranca al abrir la app y recibe `skill/event {event, data}`:

| `event` | Permiso | `data` |
|---|---|---|
| `notice` | — | `kind` (`done`, `question`, `permission`, `failed`, `retrying`, `closed`, `usagehigh`, `usagecritical`, `usagereset`, `expired`, `reminder`, `extension`), `title`, `text`, `session` |
| `session` | `sessions` | La sesión que cambió de estado, como en `sessions/list` |
| `toolUse` | `sessions` | `session`, `title`, `action` (lo que está haciendo la sesión) |
| `drop` | — | `file` (la ruta), `where` (`pet` o `island`): un archivo soltado que no es un `.claupet` |
| `chat` | — | `from` (`person` o `claupet`), `text`: cada mensaje del chat de la mascota y la isla |

Sin el permiso `sessions`, los eventos `session` y `toolUse` no se mandan. `drop` y `chat` no piden permiso,
pero se muestran al instalar («Se entera de…»).

---

## 8. La API del anfitrión: lo que la extensión le pide a Claupet

Por el mismo canal (o `host->call` en una DLL, o la API local de la sección 9):

| Método | Permiso | `params` | Respuesta |
|---|---|---|---|
| `host/info` | — | — | `claupet`, `language`, `platform`, `permissions` |
| `settings/get` | — | — | Sus ajustes |
| `settings/set` | — | `key`, `value` (texto, número, booleano o `null`) | `null` |
| `log` (también como notificación) | — | `message` (hasta 2000 caracteres) | `null` |
| `pet/animate` | `pet` | `move`, `ms` (100 a 60000) | `null` |
| `pet/say` | `pet` | `text` (hasta 300) | `null` |
| `notice/show` | `notices` | `title` (hasta 80), `text` (hasta 300) | `null` |
| `island/card` | `island` | `title` (hasta 80), `text` (hasta 4000) | `null` |
| `voice/speak` | `voice` | `text` (hasta 4000), `voice` | `null`, cuando termina de hablar |
| `claude/ask` | `claude` | `prompt` (hasta 20000), `model` | `{"text"}` |
| `sessions/list` | `sessions` | — | `[{id, title, state, folder, account, model, action, awaitingAnswer, startedAt}]` |
| `clipboard/read` | `clipboard` | — | `{"text"}` |
| `clipboard/write` | `clipboard` | `text` (hasta 100000) | `null` |
| `audio/subscribe` | `microphone` o `audio:system` | `source`: `microphone` o `system` | `{"subscription"}` |
| `audio/unsubscribe` | — | `subscription` | `null` |

- `pet/animate`: `move` es un gesto: `idle`, `dance`, `wave`, `cheer`, `think`, `dizzy`, `sleep`,
  `maracas`, `pout`, `sulk`, `fury`, `calm`, `listen`, `talk`, `read`, `search`, `edit`, `run`, `web`,
  `agent`, `ponder`, `tool` o `built`.
- `notice/show` respeta los ajustes de avisos y sonido de la persona. El aviso dice de qué extensión viene.
- `claude/ask` corre `claude -p` con la cuenta de la persona, sin herramientas, sin servidores MCP
  (`--safe-mode`) y sin guardar la sesión, con un límite de 3 minutos.
- `state` de una sesión: `working`, `waiting`, `failed`, `done` o `idle`.
- **Audio.** Tras `audio/subscribe`, llegan notificaciones `audio/chunk` con `subscription`, `source`,
  `sampleRate` (16000) y `audio`: PCM de 16 bits, mono, en base64, en trozos de 100 ms. Con `system` se
  captura lo que suena en el equipo (WASAPI loopback) y, si no suena nada, llega silencio. Mientras una
  extensión escucha, Claupet lo avisa. La suscripción termina con `audio/unsubscribe` o al detener la
  extensión.

`settings/get`, `settings/set`, `log` y `audio/*` son solo para extensiones, no para la API local.

---

## 9. La API local

Otros programas del equipo (un script, una macro, Stream Deck…) pueden pedirle cosas a Claupet con los
mismos métodos de la sección 8. Está **apagada por defecto**: se enciende en Claupet Console
(**Ajustes › Extensiones › API local**), donde también se crean los tokens, cada uno con sus permisos.

```
POST http://127.0.0.1:47615/v1/rpc
Authorization: Bearer cpt_…
Content-Type: application/json

{"jsonrpc":"2.0","id":1,"method":"pet/say","params":{"text":"¡Terminó la compilación!"}}
```

```powershell
$token = "cpt_…"
Invoke-RestMethod http://127.0.0.1:47615/v1/rpc -Method Post -ContentType application/json `
  -Headers @{ Authorization = "Bearer $token" } `
  -Body '{"jsonrpc":"2.0","id":1,"method":"pet/say","params":{"text":"Hola"}}'
```

- Solo escucha en `127.0.0.1`. Rechaza los pedidos con `Origin` (los navegadores siempre lo mandan) y
  los de un `Host` que no sea este equipo (contra el DNS rebinding), y exige `Content-Type:
  application/json`. Una página web no puede usarla.
- El token se muestra una sola vez; se guarda su hash. Se revoca en la misma ventana.
- Una extensión que corre recibe su propio token en `CLAUPET_API_TOKEN` (con sus permisos). Cambia en
  cada arranque.
- Respuestas: `401` sin token válido, `403` desde un navegador o con otro `Host`, `404` otra ruta, `413`
  más de 1 MB, `415` otro `Content-Type`, `429` más de 20 pedidos por segundo.

---

## 10. Paquetes de datos: mascotas, voces y sonidos

No llevan `runtime`. Se verifican una vez por sesión antes de usarse; si sus archivos cambiaron, se
apagan.

### Mascota

```json
{
  "provides": ["pet"],
  "pet": {
    "name": { "es": "Gatito", "en": "Kitty" },
    "width": 32, "height": 40,
    "animations": {
      "idle":  { "file": "pet/idle.png",  "ms": 250 },
      "dance": { "file": "pet/dance.png", "ms": 120 },
      "talk":  { "file": "pet/talk.png" }
    }
  }
}
```

- Cada animación es una **tira PNG** horizontal de cuadros de `width` × `height` (de 8 a 128 pixeles): el
  alto de la tira es `height` y el ancho, un múltiplo de `width`. `ms` es lo que dura cada cuadro (de 30 a
  5000; por defecto 200).
- `idle` es obligatoria. Las demás se llaman como los gestos de `pet/animate` y los que hace Claupet sola:
  además de los de la sección 8, `hear`, `catch`, `carry`, `carryLeft`, `carryRight`, `throw`, `walkLeft` y
  `walkRight`. La que falte se reemplaza por `idle`.
- PNG de 8 bits por canal, sin entrelazado (escala de grises, RGB, paleta, con o sin alfa). Un pixel con
  alfa menor a 128 es transparente: la ventana de la mascota no tiene medias transparencias.
- Se muestra al mismo alto en pantalla que el chibi, según el tamaño elegido. Se elige en **Ajustes ›
  Apariencia › Mascota**.

### Voces

```json
{
  "provides": ["voice-style"],
  "voices": [ { "id": "abuela", "name": { "es": "Abuela", "en": "Grandma" }, "file": "voces/abuela.json", "language": "es" } ]
}
```

`file` es un estilo de voz de **Supertonic** (el JSON del mismo formato que sus `voice_styles`). Se elige en
**Ajustes › Asistente › Voz** y habla con el modelo de Claupet (hay que tenerlo descargado). Con un
`text-to-speech` de una extensión ocupando la ranura, la voz la elige esa extensión. Con `openai-speech`,
`voice-style` puede ir junto a `text-to-speech`.

### Sonidos

```json
{ "provides": ["sounds"], "sounds": { "terminado": "s/fin.wav", "pregunta": "s/pregunta.wav", "fallo": "s/fallo.wav" } }
```

Los nombres son los de los avisos: `terminado`, `pregunta`, `permiso`, `fallo`, `uso` y `aviso`. Archivos
`.wav`; el que falte suena con el estilo clásico. Se elige en **Ajustes › Avisos › Estilo de sonido**. Los sonidos propios de
la persona (la carpeta `propios`) siguen teniendo prioridad.

---

## 11. Firmar y publicar

Las herramientas vienen en la misma app. Es de ventanas, así que su salida se ve si se redirige:

```powershell
# Una clave de firma (guárdela fuera de cualquier repositorio). La contraseña, en CLAUPET_KEY_PASSWORD.
$env:CLAUPET_KEY_PASSWORD = "…"
Claupet.exe --extension-keygen C:\claves\mi-clave.pem | more

# Empaquetar (sin --clave, queda sin firma)
Claupet.exe --extension-pack .\mi-extension .\mi-extension.claupet --clave C:\claves\mi-clave.pem --firmante "Su nombre" | more

# Qué es, si sirve y quién lo firmó
Claupet.exe --extension-verify .\mi-extension.claupet | more
```

**Qué se firma.** Una lista con una línea por archivo (`sha256 tamaño ruta`), ordenada por ruta, que
empieza con `claupet-package-v1`. Su SHA-256 es el *digest*. La firma es ECDSA P-256 con SHA-256 (ES256)
sobre `claupet-signature-v1\n<digest>\n<firmante>\n<fecha UTC>\n`, y va en `_claupet/signature.json` con
`format`, `algorithm`, `signer`, `signedAt`, `digest`, `publicKey` (SubjectPublicKeyInfo en base64) y
`signature`. Como el manifiesto es un archivo más, también queda firmado.

**Confianza al instalar**, de mejor a peor:

| Nivel | Qué pasa |
|---|---|
| Oficial | Firmada con una clave de Claupet |
| Autor de confianza | Firmada por un autor que la persona ya aceptó |
| Autor nuevo | Se muestra la clave; al instalar, la persona confía en sus próximas versiones |
| Sin firma | Se instala con un aviso: no hay forma de saber quién la hizo ni si alguien la cambió |
| Firma inválida | No se instala |
| Revocada | No se instala |
| Otra clave | No se instala: una actualización debe venir con la misma clave que la instalada (o seguir sin firma si se instaló sin ella). Para cambiar de autor, se desinstala primero |

**Revocaciones.** Una lista firmada con una clave oficial revoca claves de autores y versiones de
extensiones (esa versión y las anteriores dejan de cargarse). Lleva un número de secuencia y no se puede
volver a una lista más vieja. La app la verifica y la aplica; su descarga automática se activará cuando
se publique la dirección de la lista oficial.

```powershell
Claupet.exe --extension-revocations C:\claves\oficial.pem 2 revocaciones.json --clave-revocada <keyId> --version-revocada autor.nombre=1.2.0 | more
```

---

## 12. Escribir una extensión paso a paso

1. Copie un ejemplo de [`ejemplos/`](ejemplos) y cambie `id`, `name` y `version`.
2. Atienda `initialize` y `shutdown`, y los métodos de lo que declara en `provides`.
3. Escriba sus mensajes de diagnóstico en stderr: se ven en **Extensiones › Registro**.
4. Empaquétela con `--extension-pack` y revísela con `--extension-verify`.
5. Instálela soltando el `.claupet` sobre la mascota.

Para probar el protocolo sin Claupet, basta con un programa que escriba mensajes con `Content-Length` en el
stdin de su extensión y lea su stdout. Las pruebas de Claupet (`Claupet.Tests`) usan el ejemplo de Node
así, y el de C compilado con Zig.
