# 🆕 What's new

**English** · [Español](../es/novedades.md)

What each version brought, from newest to first. Full notes and downloads are in
[Releases](https://github.com/darrenarauz21/claupet/releases).

| Version | Date | In one line |
|---|---|---|
| [2.5.0](#250) | October 31, 2026 | Claupet Terminal and .NET 10. |
| [2.4.1](#241) | October 30, 2026 | Fixes: Gemini CLI, other agents, disk space. |
| [2.4.0](#240) | October 30, 2026 | One app on all three systems, a Windows installer, themes in cards. |
| [2.3.0](#230) | October 29, 2026 | Claupet Console, offline answers, extensions and automatic updates. |
| [2.2.1](#221) | October 25, 2026 | Fixes brightness. |
| [2.2.0](#220) | October 25, 2026 | Screenshots, calendar, notes, reminders and permissions. |
| [2.1.0](#210) | October 24, 2026 | The pet talks, searches the web and has its own voice. |
| [2.0.0](#200) | October 22, 2026 | Chibi, the desktop pet and appearance your way. |
| [1.0.0](#100) | October 12, 2026 | The first version. |

## 2.5.0

*October 31, 2026.* Claupet Terminal arrives, and Claupet moves to .NET 10.

**New**

- **[Claupet Terminal](terminal.md).** A new view in Claupet Console for working with agents in blocks: your
  prompt, what it's thinking, each step with its diff, the task list and the end of the turn.
  - Claude Code, Codex, Gemini CLI and Copilot CLI, whichever are installed. Several sessions at once,
    grouped by project.
  - Per session: the Claude Code account, the model, the effort and the permissions.
  - Permissions and questions in their block and on the island: the first answer wins.
  - Shells in blocks: PowerShell, Git Bash and WSL, each command with its output, its exit code and how long
    it took.
  - Collapsible projects and a panel with each session's branches, changes and processes.
  - **Settings › Terminal:** font, size, line spacing, text color, background, animations and an icons-only
    list.
- **Claupet's own icon** in the app, its windows and the packages.

**Improved**

- **.NET 10.** .NET 8 reaches end of support on November 10, 2026. The packages still bring everything:
  there's nothing to install.
- **Microphone.** If it's muted or gives no sound at all, Claupet tells you and says where to check, instead
  of sitting there listening without hearing anything.
- **Local model.** On computers with two graphics cards, it shows the one it actually uses.

> [!NOTE]
> Codex, Gemini CLI and Copilot CLI need to be signed in. If they aren't, the session tells you and explains
> how.

## 2.4.1

*October 30, 2026.* Fixes on top of 2.4.0.

- **Gemini CLI.** Also reads its new session format (`.jsonl`), counts subagents and shows today's responses
  and tokens in Usage.
- **Other agents in Usage.** Only the ones that did something today or have a limit still running show up.
- **Disk space (Windows).** Deletes the libraries earlier versions left in the temp folder (about 32 MB
  each).
- **Extensions.** If one closes while Claupet is talking to it, it reports that it's no longer running.

## 2.4.0

*October 30, 2026.* One app on Windows, macOS and Linux.

**New**

- **One app.** Windows uses the same app as macOS and Linux.
- **Installs in its own folder (Windows).** The setup assistant adds "Terms and conditions" and
  "Installation", with an uninstaller in Settings › Apps.
- **Your own themes in cards,** with a preview, an animation while they're created, quality, and export,
  import and delete.
- **New pets** to choose from.
- **Local model on the graphics card (Windows),** any brand. If it won't start with it, it carries on with
  the processor.

**Better**

- **Much less memory:** about 220 MB when it opens (it used to be about 2 GB) and about 185 MB at rest.
- **Less battery:** the pet, the island and notifications only work when something changes.
- **The microphone** loads its model only when it hears a voice.
- **Usage:** no more "Too many requests".
- **Island:** Claude Code's questions no longer show up with garbled text.

## 2.3.0

*October 29, 2026.* Claupet Console, offline answers and extensions.

- **[Claupet Console](console.md):** a big window with every setting and the chat.
- **Offline answers:** first the app, then the local model and, if needed, the cloud.
- **Local model** (Qwen3.5 0.8B, 2B or 4B), on request. Windows only.
- **"Automatic" model:** picks the model and effort to fit the question.
- **[Extensions](extensiones.md):** signed `.claupet` packages, reviewed before you install them.
- **New versions:** Claupet lets you know and, with your permission, installs them itself.
- **Step-by-step setup assistant.**
- **The pet** is on every desktop and comes back when you unlock or wake the screen.
- **macOS and Linux** get almost everything Windows has, and debut your own themes and the other agents
  (Codex CLI, Gemini CLI and GitHub Copilot CLI).

> [!NOTE]
> Files are now named `Claupet-…` instead of `UsoDeClaude-…`. Settings, hooks and starting with the system
> moved over to the new name on their own.

## 2.2.1

*October 25, 2026.* Brightness can be changed as many times as you need, and if an action on your computer
fails, the details are kept for diagnostics.

## 2.2.0

*October 25, 2026.* The pet gives you a hand with Windows, and asks permission before doing so.

- **Screenshots** of the screen or of a snip, and Claude looks at them if you ask.
- **Calendar, notes and reminders,** even with the app closed.
- **More Windows things:** windows, the computer, brightness, battery, the clipboard and your folders.
- **Asks permission** the first time: "Always allow", "Just this once" or "No".
- **The island** folds away with a click outside, and its chat has a scrollbar.
- **In Spanish and English.**
- **Built for real networks:** follows the Windows proxy and renews expired sessions on its own.

## 2.1.0

*October 24, 2026.* The pet does things on your computer, searches the web and has its own voice.

- **Talk to Claude** from the pet and the island, by typing or out loud. Drop a file on it and it summarizes
  it for you.
- **Internet and weather.**
- **Actions on your computer:** wallpaper, dark mode, color, volume, music and opening apps.
- **Hands off to Claude Code** whatever it can't do.
- **Its own voice** (Supertonic 3) and the **microphone** with right Ctrl.
- **The pet shows what each session is doing:** reading, searching, writing, typing or building.

## 2.0.0

*October 22, 2026.* Still under the name "Uso de Claude" (Claude Usage).

- **Chibi,** the pet in the crab hoodie.
- **The desktop pet:** dances, thinks, sleeps and shakes maracas when a limit resets.
- **Talk:** tells you each notification in a bubble.
- **"Chibi" sound style.**
- **Appearance:** color palettes and the refresh animation.
- **More detailed sessions** and **New session** from the tray.

## 1.0.0

*October 12, 2026.* The first version, named "Uso de Claude" (Claude Usage): Claude usage per account, Claude
Code sessions and their status, an activity calendar and notifications when a session finishes, asks, needs
permission or fails.

---

[← FAQ](preguntas-frecuentes.md) · [Index](README.md)
