# 🛟 FAQ

**English** · [Español](../es/preguntas-frecuentes.md)

Quick answers to the most common questions, and what to do when something isn't working.

**On this page:** [Privacy and tokens](#privacy-and-tokens) · [The icon and accounts](#the-icon-and-accounts) ·
[Notifications and permissions](#notifications-and-permissions) · [Voice and keys](#voice-and-keys) · [Claupet Terminal](#claupet-terminal) ·
[Updates](#updates) · [Other](#other) · [How to get help](#how-to-get-help)

## Privacy and tokens

### Does Claupet spend my tokens?

Looking at your usage, your sessions' activity, the history and the other agents **costs no tokens**: it all
comes from files on your computer and from the same request `/usage` makes in Claude Code. What does use
tokens, like any message, is questions that go to the cloud (the chat, the island, creating a theme) and
whatever the sessions you open do. To save, see [Who answers each question](asistente-y-voz.md#who-answers-each-question).

### What does Claupet know about me?

- It reads what Claude Code already keeps on your computer. **It doesn't store passwords** and doesn't touch
  your credentials: it only reads them.
- Your settings, the history (numbers only) and the Console's chat conversations stay on your computer.
- "No AI" answers don't leave your computer, except the weather (Open-Meteo and, if your city is automatic,
  ipwho.is for the location by IP).
- To look for new versions it checks GitHub, without signing in.
- **Copy diagnostics** doesn't include emails or tokens.

### Where does it keep its data?

| System | Folder |
|---|---|
| Windows | `%APPDATA%\Claupet` |
| macOS and Linux | `~/.config/Claupet` |

Notes you ask it to save go, on Windows, to `Documents\Claupet\Notas`, one `.md` per note.

## The icon and accounts

### I don't see the icon

- **Windows 11** puts new icons under the **^** arrow on the taskbar. Drag it out.
- **macOS:** it's in the menu bar, top right, not in the Dock.
- **Linux with GNOME:** it needs the AppIndicator extension (Ubuntu ships with it).

### An account says "No credentials" or doesn't show up

1. Check that Claude Code is signed in with that account.
2. If you use `CLAUDE_CONFIG_DIR`, add that folder with **+** in the Usage view (see
   [Getting started](primeros-pasos.md#how-to-add-an-account)).
3. **macOS:** Claude Code keeps its credentials in the Keychain. When macOS asks whether Claupet can read
   "Claude Code-credentials", choose **Always Allow**.

> [!NOTE]
> Claupet needs Claude Code. If you only use Claude Desktop or claude.ai, there's nothing for it to read.

### It says "Session expired"

That account's access expired because it went unused for hours. Press **Refresh** in the icon's menu:
Claupet asks Claude Code to renew it and checks again.

### The numbers don't change or it says "Loading…"

Anthropic limits usage requests heavily (the ones `/usage` makes in Claude Code count too). Claupet asks at
most once a minute per account and, if it's told to wait, shows the last good data: the window's footer says
what time it's from. It catches up on its own.

### Using it at work behind a proxy

Claupet follows `HTTPS_PROXY` or, on Windows, the manual proxy in Internet Options, and passes it on to
Claude Code. Automatic configuration (PAC) isn't read.

## Notifications and permissions

### Permission notifications don't reach me

1. Check that **Settings › Alerts › Notifications** is on.
2. In Claupet Console, use **Settings › System › Permission notifications (hook) › Set up…** and apply.
3. If nothing changes, use **Copy diagnostics** and look in the result for:

| It says | What it means |
|---|---|
| "credentials no" | Claupet can't see that account and doesn't read its sessions. |
| "no sessions folder" | That version of Claude Code doesn't record its sessions' state. |
| "no hook" | The hook is missing: there won't be any permission notifications. |

### Permissions don't reach the island

Turn on **Settings › Island › Island permissions and questions › Set up…**. It works in sessions opened
afterward. If Claupet is closed or nobody answers in time, Claude Code asks in the terminal as usual.

## Voice and keys

### The microphone can't hear me

Claupet tells you if the microphone is muted or gives no sound at all. Check:

- **Windows:** **Settings › System › Sound › Input**: that the selected microphone is the right one and isn't
  muted (or unmute it with the microphone key). If it says it can't find it, check
  **Settings › Privacy › Microphone**.
- **macOS:** **System Settings › Sound** and **Privacy & Security › Microphone**.
- **Linux:** that the system input is the right one and isn't muted.

> [!TIP]
> A virtual input (from a gaming or streaming program, for example) may give no sound at all. Pick your real
> microphone as the default input.

### The push-to-talk key doesn't respond

- **macOS:** give Claupet the **Input Monitoring** permission in **Privacy & Security**.
- **Linux with Wayland:** the key is only seen while focus is on an X11 app. Talk with the island's
  microphone button instead.
- **Windows:** if another app uses the same shortcut, **Keyboard shortcuts** marks it "in use". Record
  another one.

### It heard things I didn't say

With background noise, Whisper sometimes "hears" phrases nobody said. Get closer to the microphone, or use
the push-to-talk key only while you're speaking.

## Claupet Terminal

### Codex, Gemini CLI or Copilot CLI don't show up

They need to be installed and on the `PATH`. Check by typing their name in a terminal.

### An agent says it needs to sign in

Run the command it shows once in a terminal (`gemini`, `copilot login`…), sign in and type in the session
again. [Claupet Terminal](terminal.md#how-to-sign-in-to-the-other-agents) explains it.

### I closed the Console and my sessions closed

That's by design: when you close Claupet Console, no agent is left running. If you want a session to keep
going, leave the Console open (you can minimize it).

### I can't choose a shell

Shells in blocks need Windows 10 (1809) or newer. They're not available on macOS and Linux yet.

## Updates

### It doesn't tell me about new versions

- Check that **Settings › System › Check for updates** is on. It checks once a day.
- Versions before 2.3.0 don't check on their own: install the latest one by hand from
  [Releases](https://github.com/darrenarauz21/claupet/releases).

### It says "Couldn't install"

If the signature or the file don't match, Claupet deletes the download and installs nothing: it's a
safeguard. Try again later or download the version by hand. If it says "This copy of Claupet can't replace
itself", install the new version by hand.

### Windows says "Windows protected your PC"

Claupet isn't signed by Microsoft. Choose **More info › Run anyway**.

## Other

### The brightness doesn't change

It only changes on a laptop screen: external monitors are adjusted with their own buttons. On macOS it can't
be changed.

### The event wasn't saved to my calendar

Claupet puts the event together and opens it in your calendar app: it takes one click to save it.

### The island doesn't peek out on Linux

With Wayland, the island only sees the cursor while it's over an X11 app, and may not peek out when you move
the cursor close.

## How to get help

1. In **Settings › System**, press **Copy diagnostics**.
2. Open an [issue on GitHub](https://github.com/darrenarauz21/claupet/issues) and paste the result, along with
   what happened and your system.

> [!IMPORTANT]
> Diagnostics don't include emails or tokens, but check them before pasting: the repository is public.

---

[← Extensions](extensiones.md) · [Index](README.md) · [What's new →](novedades.md)
