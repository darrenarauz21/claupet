<div align="center">

<img src="docs/capturas/mascota-voz.png" alt="Claupet escucha, piensa y responde" width="420">

# Claupet

**Su uso, sus sesiones de Claude Code y una mascota de escritorio, junto al reloj.**

[English](README.md) · **Español**

![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-D97757)
![macOS](https://img.shields.io/badge/macOS-Apple%20%7C%20Intel-D97757)
![Linux](https://img.shields.io/badge/Linux-x64-D97757)
![Licencia](https://img.shields.io/badge/licencia-MIT-2A1F1A)

[Descargar](#descargar) · [Instalar](#instalar) · [Para desarrolladores](#para-desarrolladores)

</div>

---

Claude Code hace el trabajo. Claupet le mantiene al tanto: cuánto le queda de su plan, qué sesión
le necesita y en qué anda cada una, sin abrir una terminal.

## Qué hace

- 📊 **Su uso, de un vistazo.** La sesión de 5 horas y la semana de cada cuenta, con la hora en
  que se reinician, y seis meses de uso en un calendario. El ícono de la bandeja muestra el
  porcentaje de su sesión.
- 🧭 **Sesiones en vivo.** Qué sesiones de Claude Code están trabajando, esperándole o terminadas,
  y una sesión nueva con la cuenta, la carpeta y el modelo que usted elija.
- 🎬 **Actividad.** Cada sesión paso a paso, en vivo. Todo sale de archivos locales: no gasta
  tokens.
- 🔔 **Avisos amables.** Un mensajito, con sonido, cuando una sesión termina, pregunta algo,
  necesita permiso, falla o se acerca a un límite.
- 🦀 **Una mascota de escritorio.** La criaturita clásica o una chibi con hoodie de
  cangrejo que lee, busca, teclea y construye al ritmo de sus sesiones, baila cuando se renueva un
  límite y se enoja si la molestan a clics.
- 💬 **Hable con Claupet** *(Windows)*. Pregúntele a la mascota, o a la isla de arriba de la
  pantalla, por escrito o de viva voz, y escuche la respuesta con la voz propia de Claupet. Suéltele
  un archivo y se lo resume. También cambia el fondo, el tema, el volumen o la música, y le encarga
  a Claude Code lo que sea más grande.
- 🧰 **Una mano con Windows** *(Windows)*. Captura o recorta la pantalla (y la mira, si usted quiere),
  agenda en su calendario, guarda notas, le recuerda cosas aunque la app esté cerrada y se encarga de
  ventanas, brillo, portapapeles, carpetas y del equipo. Cada cosa pide permiso la primera vez.
- 📶 **Hecha para redes reales.** Sigue el proxy de Windows, renueva sola las sesiones vencidas y,
  sin internet, igual saluda y le dice la hora.
- 🎨 **A su gusto.** Paletas de color, mascotas, animaciones y estilos de sonido.
- 🌎 **En español e inglés.** Arranca en el idioma de su sistema y se cambia cuando quiera en
  **Ajustes › Sistema**.

<p align="center">
  <img src="docs/capturas/isla-chat.png" alt="La isla con una respuesta" width="48%">
  <img src="docs/capturas/caja.png" alt="La mascota lleva un archivo en una caja hasta la isla, que lo resume" width="48%">
</p>

## Descargar

| Sistema | Archivo |
|---|---|
| Windows 10 y 11 | [Claupet-windows-x64.exe](https://github.com/darrenarauz21/claupet/releases/latest/download/Claupet-windows-x64.exe) |
| macOS con chip Apple (M1, M2, M3…) | [Claupet-macos-arm64.zip](https://github.com/darrenarauz21/claupet/releases/latest/download/Claupet-macos-arm64.zip) |
| macOS con Intel | [Claupet-macos-x64.zip](https://github.com/darrenarauz21/claupet/releases/latest/download/Claupet-macos-x64.zip) |
| Linux (x64) | [Claupet-linux-x64.tar.gz](https://github.com/darrenarauz21/claupet/releases/latest/download/Claupet-linux-x64.tar.gz) |

Cada archivo trae todo lo que necesita. Solo hace falta tener
[Claude Code](https://claude.com/product/claude-code) instalado y con la sesión iniciada. Las versiones
anteriores están en [Releases](https://github.com/darrenarauz21/claupet/releases).

## Instalar

**Windows.** Guarde el `.exe` en una carpeta fija y ábralo con doble clic. Si Windows dice que
protegió su PC, elija **Más información › Ejecutar de todas formas** (la app no está firmada).
Busque el ícono naranja junto al reloj o en la flechita **^**.

**macOS.** Descomprima, abra **Terminal**, escriba `sh ` (con un espacio), arrastre
`preparar-mac.sh` a la ventana y pulse Enter. Claupet queda en **Aplicaciones** y se abre en la
barra de menús. Si macOS pide leer «Claude Code-credentials», elija **Permitir siempre**.

**Linux.** Ejecute `tar -xzf Claupet-linux-x64.tar.gz` y, dentro de la carpeta,
`sh instalar.sh`. También queda en el menú de aplicaciones. En GNOME, el ícono de la bandeja
necesita la extensión AppIndicator (Ubuntu ya la trae).

La primera vez, Claupet ofrece configurar los avisos de permisos, el inicio con el sistema y las
notificaciones. **Aplicar** acepta todo.

## Bueno saber

- **Privada por diseño.** Claupet lee lo que Claude Code ya guarda en su equipo. No guarda
  contraseñas ni sus conversaciones.
- **El chat usa su plan.** Las preguntas a la mascota van por Claude Code con la cuenta que usted
  elija, como cualquier mensaje.
- **¿Algo no va?** **Ajustes › Copiar diagnóstico** reúne lo necesario para ayudarle.

## Para desarrolladores

### Correrla desde el código

Necesita el [SDK de .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0) y Claude Code con la
sesión iniciada. Python 3 solo hace falta para armar los paquetes.

```powershell
git clone https://github.com/darrenarauz21/claupet.git
cd claupet
dotnet run --project Claupet.csproj                # Windows (WinForms)
dotnet run --project Claupet.Avalonia              # Linux y macOS (también corre en Windows)
python herramientas/empaquetar.py                  # paquetes autocontenidos en paquetes/
```

### Estructura

```text
Core/                     lógica común: uso, sesiones, actividad, avisos, asistente y red
*.cs                      app de Windows (WinForms)
Claupet.Avalonia/         app de Linux y macOS (Avalonia)
herramientas/             empaquetado y scripts de instalación
```

### Hecha con

| | |
|---|---|
| Plataforma | .NET 8 (C#) |
| Interfaz en Windows | WinForms |
| Interfaz en Linux y macOS | Avalonia 11 |
| Voz a texto | Whisper.net (whisper.cpp) |
| Texto a voz | ONNX Runtime, y System.Speech para las voces de Windows |
| Claude | CLI de Claude Code (`claude -p`), con su propia cuenta |

### Modelos

| Modelo | Para qué | Dónde corre |
|---|---|---|
| Claude Haiku, Sonnet, Opus o Fable | Las respuestas de la mascota y de la isla (la mascota usa Haiku de entrada) | Anthropic, a través de Claude Code |
| Whisper `ggml-small` | El micrófono: voz a texto | Su equipo (488 MB, se descarga la primera vez) |
| Supertonic 3 | La voz de Claupet: texto a voz | Su equipo (unos 400 MB, se descarga la primera vez) |

Opciones útiles al desarrollar: `--preview` abre una segunda ventana, `--dump` escribe una
consulta completa de uso y `--preguntar "<texto>"` le pregunta a la mascota desde la consola. Todos
los detalles técnicos están en [DETALLES.md](DETALLES.md).

## Licencia

[MIT](LICENSE). Las bibliotecas incluidas y los modelos de voz que se descargan tienen sus propias
licencias: vea [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Claupet es un proyecto personal y no
oficial, sin relación con Anthropic. «Claude» y «Claude Code» son marcas de Anthropic, PBC.
