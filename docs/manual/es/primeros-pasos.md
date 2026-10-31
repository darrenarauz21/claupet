# 🚀 Primeros pasos

**Español** · [English](../en/primeros-pasos.md)

El asistente de configuración, el ícono junto al reloj y sus cuentas de Claude Code: lo que conviene
conocer el primer día.

**En esta página:** [El asistente de configuración](#el-asistente-de-configuración) ·
[El ícono y su ventana](#el-ícono-y-su-ventana) · [Sus cuentas de Claude Code](#sus-cuentas-de-claude-code)

## El asistente de configuración

La primera vez que abre Claupet aparece **Configurar Claupet**, un asistente por pasos. Nada cambia hasta
que pulse **Aplicar**, en el último paso.

<p align="center">
  <img src="../../capturas/manual/asistente-configuracion.png" alt="El asistente de configuración de Claupet" width="720">
</p>

| Paso | Qué hace |
|---|---|
| **Bienvenida** | Elige el idioma (español o inglés) y muestra las novedades. |
| **Términos** | Pide aceptar los términos y condiciones. Sin aceptarlos, Claupet se cierra. |
| **Instalación** | *Solo en Windows.* La carpeta donde se instala Claupet y si quiere un acceso directo en el escritorio (el del menú Inicio se crea siempre). Lo explica [Instalar Claupet](instalar.md#instalar-en-windows). |
| **Avisos de permisos** | Agrega un hook al `settings.json` de cada carpeta de Claude Code para avisarle cuando una sesión pide permiso. Antes de cambiarlo guarda un respaldo. |
| **Su compañero** | La mascota, si vive en el escritorio, el chat con la mascota y la isla del borde de arriba. |
| **Modelos y voz** | Hablarle con el micrófono, leer las respuestas en voz alta, instalar extensiones y el modelo local (según el sistema). |
| **Inicio y avisos** | Iniciar Claupet con el sistema y las notificaciones. Aquí está **Aplicar**. |

> [!TIP]
> ¿Quiere volver a verlo? En Claupet Console, **Ajustes › Sistema › Avisos de permisos (hook) ›
> Configurar…** abre el asistente otra vez.

> [!NOTE]
> Los modelos de voz y el modelo local se descargan aparte y solo si los enciende: la voz del micrófono
> (Whisper, unos 488 MB), la voz de Claupet (unos 400 MB) y el modelo local (de 533 MB a 2,7 GB). Se
> bajan una vez, en segundo plano.

## El ícono y su ventana

Claupet vive en la bandeja de Windows o de Linux, o en la barra de menús de macOS.

<p align="center">
  <img src="../../capturas/manual/bandeja.png" alt="La ventana de Claupet junto al reloj" width="360">
</p>

- **El ícono** muestra el porcentaje usado de la sesión de 5 horas de la cuenta elegida. Mientras
  actualiza, lo anima la mascota que eligió.
- **Un clic** abre o cierra la ventana, con tres vistas arriba: **Uso**, **Actividad** e **Historial**
  (ver [Uso, actividad e historial](uso.md)).
- **El engranaje** del pie abre los **Ajustes rápidos**, en cinco pestañas: Avisos, Apariencia,
  Asistente, Isla y Sistema. Al final, **Todos los ajustes › Abrir** lleva a Claupet Console.
- **El menú del ícono** (clic derecho) es corto:

| Opción | Qué hace |
|---|---|
| **Abrir** | Muestra la ventana. |
| **Actualizar** | Vuelve a consultar el uso ahora. Si la sesión de la cuenta venció, la renueva. |
| **Nueva sesión…** | Abre Claude Code en una terminal, con la cuenta, la carpeta y el modelo que elija (ver [Uso](uso.md#abrir-una-sesión-nueva)). |
| **Claupet Console** | Abre la ventana grande (ver [Claupet Console](console.md)). |
| **Ajustes…** | Abre los ajustes rápidos. |
| **Salir** | Cierra Claupet. |

## Sus cuentas de Claude Code

Cada cuenta de Claude Code es una carpeta de configuración: `~/.claude` o la que usted indique con
`CLAUDE_CONFIG_DIR`. Claupet encuentra solas las carpetas `~/.claude*` que tienen la sesión iniciada.

### Cómo agregar una cuenta

1. Abra la ventana del ícono, en la vista **Uso**, y pulse **+** (o, en Claupet Console,
   **Ajustes › Cuentas › Agregar cuenta…**).
2. Elija la carpeta de configuración de esa cuenta (la de `CLAUDE_CONFIG_DIR`).
3. Si todavía no tiene memoria propia y ya hay otra cuenta, elija cómo será su memoria:
   - **Compartida:** su carpeta `projects` (las conversaciones y la memoria de cada proyecto) se enlaza
     con la de la cuenta elegida. `CLAUDE.md`, skills, agentes, ajustes y credenciales siguen siendo
     propios.
   - **Separada:** cada cuenta con lo suyo.

> [!NOTE]
> Una cuenta que ya tiene su carpeta `projects` con contenido no se enlaza, para no perder nada.
> **Ajustes › Cuentas** dice de cada una si su memoria es propia o compartida.

### Cómo renombrar o quitar una cuenta

Haga clic derecho en el botón de la cuenta, en la vista **Uso**: **Renombrar**, **Abrir carpeta** o
**Quitar**. Quitarla de Claupet no toca nada de Claude Code.

> [!IMPORTANT]
> Claupet no guarda contraseñas ni toca las credenciales de Claude Code: solo las lee. Si el acceso de una
> cuenta vence, Claupet le pide a Claude Code que lo renueve con una consulta mínima (con Haiku y sin
> herramientas).

---

[← Instalar Claupet](instalar.md) · [Índice](../README.md) · [Uso, actividad e historial →](uso.md)
