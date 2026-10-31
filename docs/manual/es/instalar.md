# 📦 Instalar Claupet

**Español** · [English](../en/instalar.md)

Descargue el archivo de su sistema, instálelo en un par de pasos y deje que las versiones nuevas
lleguen solas.

**En esta página:** [Antes de empezar](#antes-de-empezar) · [Descargar](#descargar) ·
[Windows](#instalar-en-windows) · [macOS](#instalar-en-macos) · [Linux](#instalar-en-linux) ·
[Actualizaciones](#actualizaciones-automáticas) · [Desinstalar](#desinstalar)

## Antes de empezar

- Tenga [Claude Code](https://claude.com/product/claude-code) instalado y con la sesión iniciada.
  Claupet lee lo que Claude Code ya guarda en su equipo; sin él, no tiene de dónde leer.
- No hace falta instalar .NET ni nada más: cada archivo trae todo lo que necesita.

## Descargar

| Sistema | Archivo |
|---|---|
| Windows 10 y 11 | [Claupet-windows-x64.exe](https://github.com/darrenarauz21/claupet/releases/latest/download/Claupet-windows-x64.exe) |
| macOS con chip Apple (M1, M2, M3…) | [Claupet-macos-arm64.zip](https://github.com/darrenarauz21/claupet/releases/latest/download/Claupet-macos-arm64.zip) |
| macOS con Intel | [Claupet-macos-x64.zip](https://github.com/darrenarauz21/claupet/releases/latest/download/Claupet-macos-x64.zip) |
| Linux (x64) | [Claupet-linux-x64.tar.gz](https://github.com/darrenarauz21/claupet/releases/latest/download/Claupet-linux-x64.tar.gz) |

Las versiones anteriores están en [Releases](https://github.com/darrenarauz21/claupet/releases). Junto a
cada archivo hay otro terminado en `.firma`: Claupet lo usa para comprobar las versiones nuevas y no hace
falta descargarlo.

## Instalar en Windows

El mismo `.exe` que descarga es el instalador: al abrirlo, el asistente de configuración le guía.

1. Abra con doble clic `Claupet-windows-x64.exe`.
2. Si Windows dice que protegió su PC, elija **Más información › Ejecutar de todas formas**. Aparece
   porque la app no está firmada por Microsoft.
3. En **Términos**, léalos y encienda **Acepto los términos y condiciones**. Sin aceptarlos no se puede
   seguir, y cerrar el asistente cierra la app.
4. En **Instalación**, revise la carpeta. Por defecto es `%LOCALAPPDATA%\Programs\Claupet`, que no pide
   permisos de administrador. Con **Cambiar…** puede elegir otra; si no se llama `Claupet`, se crea una
   `Claupet` dentro.
5. Siga los demás pasos (los explica [Primeros pasos](primeros-pasos.md)) y pulse **Aplicar** en el
   último.
6. Claupet se copia a su carpeta, queda en el menú Inicio y en **Configuración › Aplicaciones**, y se abre
   la copia instalada. Ya puede borrar el archivo que descargó.

Busque el ícono junto al reloj o en la flechita **^** de la barra de tareas.

> [!TIP]
> Windows 11 deja los íconos nuevos escondidos en la flechita **^**. Arrastre el de Claupet a la barra
> para tenerlo siempre a la vista.

> [!IMPORTANT]
> La carpeta de instalación tiene que ser de este equipo y poder escribirse sin permisos de
> administrador: ahí se cambia el `.exe` cuando llega una versión nueva. Si la elegida no sirve, el
> asistente no deja pasar al paso siguiente.

Sus ajustes y modelos no van en esa carpeta, sino en `%APPDATA%\Claupet`. Si abre otra vez el archivo
descargado y es el mismo que instaló, se abre la copia instalada; si es otra versión, el asistente
ofrece reemplazarla.

## Instalar en macOS

1. Descomprima el `.zip` que descargó.
2. Abra la app **Terminal**.
3. Escriba `sh ` (con un espacio), arrastre `preparar-mac.sh` a la ventana y pulse Intro.
4. Claupet queda en **Aplicaciones** y se abre sola en la barra de menús, arriba a la derecha (no en el
   Dock).
5. Si macOS pregunta si permite leer «Claude Code-credentials» del Llavero, elija **Permitir siempre**.

> [!NOTE]
> La app no está firmada por Apple. `preparar-mac.sh` la firma solo para su Mac y le quita la cuarentena:
> sin ese paso, macOS no la deja abrir. Se hace una sola vez.

## Instalar en Linux

1. Descomprima el archivo: `tar -xzf Claupet-linux-x64.tar.gz`
2. Dentro de la carpeta, ejecute `sh instalar.sh` (no pide `sudo`).
3. Claupet queda en `~/.local/share/claupet` y en el menú de aplicaciones. Busque su ícono en la bandeja.

> [!NOTE]
> En GNOME, el ícono de la bandeja necesita la extensión AppIndicator (Ubuntu ya la trae). Claupet
> también necesita `libICE`, `libSM` y `fontconfig`, que vienen en los escritorios habituales.

## La primera vez

Al abrirse por primera vez, en cualquier sistema, Claupet le pide aceptar sus
[términos y condiciones](../../../Core/Terminos/es.txt) y le ofrece configurar los avisos de permisos, el
inicio con el sistema y las notificaciones. Lo explica [Primeros pasos](primeros-pasos.md).

## Actualizaciones automáticas

Desde la versión 2.3.0, Claupet busca versiones nuevas por su cuenta.

1. Una vez al día mira la última versión publicada en GitHub (sin iniciar sesión en ningún lado).
2. Si hay una más nueva, le avisa una vez: «Claupet X está disponible». La fila queda también en
   **Ajustes › Sistema**.
3. Elija **Descargar e instalar…**. Claupet le dice cuánto pesa y le pide confirmarlo.
4. Descarga primero la firma y después el archivo, y comprueba que la firma sea de Claupet, de ese
   archivo y de esa versión. Si algo no coincide, lo borra y no instala nada.
5. Cuando dice «Claupet X está lista: se instala al reiniciar», pulse **Reiniciar ahora** o simplemente
   salga de la app: la versión nueva toma el lugar de la anterior.

> [!TIP]
> Si prefiere actualizar a mano, apague **Buscar actualizaciones** en **Ajustes › Sistema** (en Claupet
> Console). Nada se instala nunca sin preguntarle.

> [!NOTE]
> Si la instalación falla, se vuelve a la versión anterior. Al abrir la nueva, Claupet borra la anterior
> y lo descargado.

## Desinstalar

**Windows**

1. Abra **Configuración › Aplicaciones**, busque **Claupet** y elija **Desinstalar**.
2. Confirme. Claupet se cierra, quita los hooks de Claude Code (con un respaldo), el inicio con Windows,
   los accesos directos y los recordatorios pendientes.
3. Si quiere, borre también su carpeta de datos (ajustes, historial, modelos descargados).

**macOS y Linux**

1. En Claupet Console, abra **Ajustes › Sistema** y use **Quitar hook e inicio con el sistema › Quitar…**.
2. Salga de Claupet desde el menú del ícono.
3. Borre la app:
   - **macOS:** `Claupet` de **Aplicaciones**.
   - **Linux:** `~/.local/share/claupet` y `~/.local/share/applications/claupet.desktop`.

Los ajustes quedan en `~/.config/Claupet`; bórrela si no piensa volver.

---

[Índice](../README.md) · [Primeros pasos →](primeros-pasos.md)
