# 📦 Install Claupet

**English** · [Español](../es/instalar.md)

Download the file for your system, install it in a couple of steps and let new versions arrive on their
own.

**On this page:** [Before you start](#before-you-start) · [Download](#download) ·
[Windows](#install-on-windows) · [macOS](#install-on-macos) · [Linux](#install-on-linux) ·
[Updates](#automatic-updates) · [Uninstall](#uninstall)

## Before you start

- Have [Claude Code](https://claude.com/product/claude-code) installed and signed in. Claupet reads what
  Claude Code already keeps on your computer; without it, there's nothing to read.
- You don't need to install .NET or anything else: each file brings everything it needs.

## Download

| System | File |
|---|---|
| Windows 10 and 11 | [Claupet-windows-x64.exe](https://github.com/darrenarauz21/claupet/releases/latest/download/Claupet-windows-x64.exe) |
| macOS, Apple chip (M1, M2, M3…) | [Claupet-macos-arm64.zip](https://github.com/darrenarauz21/claupet/releases/latest/download/Claupet-macos-arm64.zip) |
| macOS, Intel | [Claupet-macos-x64.zip](https://github.com/darrenarauz21/claupet/releases/latest/download/Claupet-macos-x64.zip) |
| Linux (x64) | [Claupet-linux-x64.tar.gz](https://github.com/darrenarauz21/claupet/releases/latest/download/Claupet-linux-x64.tar.gz) |

Older versions are in [Releases](https://github.com/darrenarauz21/claupet/releases). Next to each file
there's another one ending in `.firma` (its signature): Claupet uses it to verify new versions, and you
don't need to download it.

## Install on Windows

The `.exe` you download is the installer itself: open it and the setup assistant walks you through.

1. Double-click `Claupet-windows-x64.exe`.
2. If Windows says it protected your PC, choose **More info › Run anyway**. It shows up because the app
   isn't signed by Microsoft.
3. In **Terms**, read them and turn on **I accept the terms and conditions**. You can't go on without
   accepting them, and closing the assistant closes the app.
4. In **Installation**, check the folder. By default it's `%LOCALAPPDATA%\Programs\Claupet`, which doesn't
   need administrator rights. **Change…** lets you pick another one; if it isn't called `Claupet`, a
   `Claupet` folder is created inside it.
5. Go through the other steps (explained in [Getting started](primeros-pasos.md)) and press **Apply** on
   the last one.
6. Claupet copies itself to its folder, shows up in the Start menu and in **Settings › Apps**, and the
   installed copy opens. You can now delete the file you downloaded.

Look for the icon by the clock or under the **^** arrow on the taskbar.

> [!TIP]
> Windows 11 hides new icons under the **^** arrow. Drag Claupet's onto the taskbar to keep it always in
> view.

> [!IMPORTANT]
> The install folder must be on this computer and writable without administrator rights: that's where the
> `.exe` gets replaced when a new version arrives. If the one you pick won't work, the assistant won't let
> you move on to the next step.

Your settings and models don't go in that folder, but in `%APPDATA%\Claupet`. If you open the downloaded
file again and it's the same one you installed, the installed copy opens; if it's another version, the
assistant offers to replace it.

## Install on macOS

1. Unzip the `.zip` you downloaded.
2. Open the **Terminal** app.
3. Type `sh ` (with a space), drag `preparar-mac.sh` into the window and press Return.
4. Claupet lands in **Applications** and opens by itself in the menu bar, top right (not in the Dock).
5. If macOS asks whether to allow reading "Claude Code-credentials" from the Keychain, choose
   **Always Allow**.

> [!NOTE]
> The app isn't signed by Apple. `preparar-mac.sh` signs it just for your Mac and removes the quarantine:
> without that step, macOS won't let it open. You only do this once.

## Install on Linux

1. Extract the file: `tar -xzf Claupet-linux-x64.tar.gz`
2. Inside the folder, run `sh instalar.sh` (no `sudo` needed).
3. Claupet goes to `~/.local/share/claupet` and into your applications menu. Look for its icon in the
   tray.

> [!NOTE]
> On GNOME, the tray icon needs the AppIndicator extension (Ubuntu ships with it). Claupet also needs
> `libICE`, `libSM` and `fontconfig`, which come with the usual desktops.

## The first time

The first time it opens, on any system, Claupet asks you to accept its
[terms and conditions](../../../Core/Terminos/en.txt) and offers to set up permission notifications,
starting with your computer and notifications. [Getting started](primeros-pasos.md) explains it.

## Automatic updates

Since version 2.3.0, Claupet looks for new versions on its own.

1. Once a day it checks the latest version published on GitHub (without signing in anywhere).
2. If there's a newer one, it tells you once: "Claupet X is available". The row also stays in
   **Settings › System**.
3. Choose **Download and install…**. Claupet tells you how big it is and asks you to confirm.
4. It downloads the signature first and then the file, and checks that the signature is Claupet's, for
   that file and that version. If anything doesn't match, it deletes it and installs nothing.
5. When it says "Claupet X is ready: it's installed on restart", press **Restart now** or just quit the
   app: the new version takes the old one's place.

> [!TIP]
> If you'd rather update by hand, turn off **Check for updates** in **Settings › System** (in Claupet
> Console). Nothing is ever installed without asking you.

> [!NOTE]
> If the install fails, it goes back to the previous version. When the new one opens, Claupet deletes the
> old one and the download.

## Uninstall

**Windows**

1. Open **Settings › Apps**, find **Claupet** and choose **Uninstall**.
2. Confirm. Claupet closes and removes the Claude Code hooks (keeping a backup), starting with Windows,
   the shortcuts and any pending reminders.
3. If you like, also delete its data folder (settings, history, downloaded models).

**macOS and Linux**

1. In Claupet Console, open **Settings › System** and use **Remove hook and start at login › Remove…**.
2. Quit Claupet from the icon's menu.
3. Delete the app:
   - **macOS:** `Claupet` from **Applications**.
   - **Linux:** `~/.local/share/claupet` and `~/.local/share/applications/claupet.desktop`.

Settings stay in `~/.config/Claupet`; delete it if you're not coming back.

---

[Index](README.md) · [Getting started →](primeros-pasos.md)
