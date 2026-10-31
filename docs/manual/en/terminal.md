# ⌨️ Claupet Terminal

**English** · [Español](../es/terminal.md)

*New in 2.5.0.* Work with your agents in blocks instead of on a terminal screen: your prompt, what it's
thinking, each step with its change, permissions and the end of the turn, all in view.

**On this page:** [What it is](#what-it-is) · [Open a session](#how-to-open-a-session) ·
[The blocks](#the-blocks) · [Permissions and questions](#permissions-and-questions) ·
[Change model, effort and permissions](#how-to-change-the-model-effort-or-permissions-mid-session) ·
[Other agents](#codex-gemini-cli-and-copilot-cli) · [Shells](#shells-in-blocks) ·
[Projects](#projects) · [Branches, Changes and Processes](#the-branches-changes-and-processes-panel) ·
[Settings › Terminal](#settings--terminal) · [Limits](#limits)

## What it is

Claupet Terminal is a view in [Claupet Console](console.md) (the **Terminal** button on the rail). It opens
coding agents and shells in tabs and shows each thing in its own block:

- **Claude Code**, **Codex**, **Gemini CLI** and **Copilot CLI**: whichever are installed on your computer.
- **Shells**: PowerShell, Git Bash and WSL bash on Windows; bash, zsh and PowerShell 7 (if installed) on Linux
  and macOS. Each command with its output, and full-screen programs (vim, less, claude) across the whole session.
- **Several sessions at once**, grouped by project, each with its own account, model, effort and
  permissions.

<p align="center">
  <img src="../../capturas/manual/terminal-sesion.png" alt="A Claude Code session in blocks in Claupet Terminal" width="720">
</p>

## How to open a tab

1. In Claupet Console, press **Terminal** on the rail.
2. Press **+** (**New tab**), above the list, or **Ctrl+T**. A small menu opens:

   | Part | What it sets |
   |---|---|
   | **Open** | Claude Code, another installed agent (Codex, Gemini CLI, Copilot CLI) or one of your computer's terminals. |
   | **With the account** | *(Claude Code)* This tab's account; the green dot means it's signed in. |
   | **Model and effort** | *(Claude Code)* `haiku`, `sonnet`, `opus`, `fable` or "default", and the effort: low, medium, high, very high or max. The variants (`opus[1m]`, `sonnet[1m]`, `opusplan`) are chosen inside the session. |
   | **The folder** | Bottom left: where it opens. Tap it to choose another (linked projects, recent ones or **Choose another folder…**). |

3. Press **Open** (or **Enter**). Tapping the item that's already selected also opens it.

Claupet remembers what you opened last, with its account, model and effort. Permissions start as they were
last time and can be changed in the session.

Claude Code's **Permissions** modes:

| Mode | What it does |
|---|---|
| **asks first** | Asks for permission for whatever needs it (the usual one). |
| **accepts edits** | Edits files without asking; for anything else, it asks. |
| **plans only** | Thinks and proposes a plan, without changing anything. |
| **automatic** | Decides on its own when to ask. |

> [!TIP]
> You can have several sessions open at once, each with a different account: for example, one with your
> personal account and another with your work one, in the same project.

### How to write to it

Type in the box at the bottom ("Write to Claude Code…"). **Enter** sends and **Shift+Enter** adds a new line.
While it's working, **Interrupt** (the two bars at the top, or **Esc**) stops the turn.

At the top of the session you see the folder, its branch and what's left to push, and how much of the context it
has used (a bar with its percentage; hovering also shows the account's 5-hour session usage).

### How to copy

- **Your prompt or a whole answer:** hover over the block and tap the **Copy** icon on the right.
- **Part of it:** select it with the mouse and press **Ctrl+C**, or right-click › **Copy** (**Select all**
  picks the whole block).

## The blocks

| Block | What it shows |
|---|---|
| **Your prompt** | What you wrote, in bold with **›** in front. |
| **Thinking…** | While it thinks; when it's done, "Thought · 1,234 tokens". |
| **The answer** | The text, with code blocks set apart. |
| **A step** | What it did ("read", "edited", "created", "ran", "searched", "delegated"…) and on what, with lines added and removed (`+12 −3`). A click opens its diff or what it returned. |
| **The task list** | ☑ done, ◐ in progress, ☐ pending. |
| **A permission or a question** | Amber border until you answer. |
| **End of turn** | ✓ and how long it took, the turns and the cost if the agent reports them; or ✕ with the error. |

## Permissions and questions

<p align="center">
  <img src="../../capturas/manual/terminal-permiso.png" alt="A Claude Code permission in its block" width="720">
</p>

When the agent needs permission, its block says what it wants to do ("Wants to run", "Wants to edit"…), on
what, and the beginning of the command or text. Choose:

- **Allow**: just this once.
- **Always here**: always allows it in this project (shows up when the agent offers it).
- **Reject**.

<p align="center">
  <img src="../../capturas/manual/terminal-pregunta.png" alt="A Claude Code question with its options" width="720">
</p>

When it asks you something, the block shows each question ("Question 1 of 2") with its options and their
explanation. Pick one (or several, if it says "you can pick several"), or type your own in **Another
answer…**, and press **Answer**. With a single question that takes one choice, the click answers right away.
**Don't answer** declines it.

> [!TIP]
> **On the island too.** If the [island](isla.md) is on, the permission or question shows up there as well,
> so you can answer without going back to the Console. The first answer wins: the block then says "Allowed
> on the island" or "Answered on the island".

In the list on the left, a tab waiting for permission gets an amber dot.

## How to change the model, effort or permissions mid-session

In the box at the bottom, on the right (or below it, if the window is narrow), there are the account, the model
with its effort ("opus · high") and the permissions.

1. Tap the one you want to change.
2. Pick another value.
3. It applies from the next turn.

The account can't be changed in an open session: if you pick another one, a new tab opens with that account,
in the same folder and with the same model.

## Codex, Gemini CLI and Copilot CLI

If they're installed (and on the `PATH`), they show up in the new tab menu. They're shown in blocks just like
Claude Code, with their permissions here and on the island.

- The model, effort and permissions are chosen **inside the session**, in the box at the bottom, from what the
  agent reports when it opens.
- The tab's mark tells you what it is: **✻** Claude Code, **cx** Codex, **✦** Gemini CLI, **gh** Copilot CLI
  and **>_** a terminal.

### How to sign in to the other agents

Each agent has to be signed in. If it isn't, its session tells you, along with the command you need. Run it
once in any terminal and then type in the session again:

| Agent | What to run |
|---|---|
| **Gemini CLI** | `gemini` (and follow the steps to sign in) |
| **Copilot CLI** | `copilot login` |
| **Codex** | `codex login`, or sign in to the Codex extension in your editor (VS Code, Cursor or Windsurf): if `codex` isn't on the PATH, Claupet uses the one that ships with that extension |

> [!NOTE]
> If an agent doesn't show up or says "… was not found. Is it installed and on the PATH?", install it and
> check that it opens when you type its name in a terminal.

## Shells in blocks

In the new tab menu, choose a terminal: Claupet offers the ones it finds on your computer.

| System | Terminals |
|---|---|
| **Windows 10 (1809) or newer** | PowerShell (7 if available; otherwise Windows PowerShell), Git Bash and WSL bash |
| **Linux and macOS** | zsh, bash and PowerShell 7 (if installed); your user's shell first |

zsh and bash load your usual files (`.zshrc`, `.zprofile`, `.bashrc` or `.bash_profile`); Claupet only adds
the block marks.

<p align="center">
  <img src="../../capturas/manual/terminal-shell.png" alt="PowerShell in blocks, each command with its output" width="720">
</p>

Each command stays in its own block, with its output, how it ended ("Finished fine (code 0)" or "Finished
with code 1") and how long it took.

| Key | What it does |
|---|---|
| **Enter** | Runs the command. |
| **↑** / **↓** | Go through previous commands. |
| **Ctrl+C** | Interrupts the one that's running (with nothing running, clears what you typed). |

- While a command is running, what you type goes to it (for example, to answer "Continue? [Y/N]").
  With the box empty, the arrows, **Tab**, **Enter** and **Esc** go straight to it: that's how you pick an
  option in a question, like `claude`'s trust prompt.
- Hovering over a block shows the **Copy** and **Repeat** icons.

### Full-screen programs

Programs that use the whole screen (`vim`, `nano`, `less`, the `git log` pager, `claude`, `htop`) fill the
session, with their colors and sized to the panel, like in any terminal. Keys, text, the mouse wheel and
**Ctrl+V** (paste) go straight to them. When the program exits, the blocks come back.
- A block shows the first 30 lines; **Show more lines** opens the rest. The last 400 are kept.

## Projects

The list on the left groups tabs by project (the folder). A project is linked automatically when you open a
session in its folder.

- **Expanded:** its branch and the state of the repository (for example, "↑2 · 3 uncommitted"), with its
  tabs below.
- **Collapsed:** how many tabs it has and a dot for each one.
- **Collapse all projects** / **Expand all projects**: the button at the top of the list.
- **The "⋯" menu** on each project: **New terminal here** (the default terminal), **Open an agent or another
  tab here…** (the new tab menu, in that folder), **Open the folder** and **Unlink**.

> [!NOTE]
> Unlinking only removes the project from the list: it doesn't delete the folder. If it had open sessions,
> it closes them.

Drag the edge of the list to widen it, and the button at the top switches it to **Icons only**.

## The Branches, Changes and Processes panel

The **Branches, changes and processes** button, at the top of the session, opens a panel on the right with
three tabs:

| Tab | What it shows |
|---|---|
| **Branches** | The repository's branches, which agent is working on each one, whether it has a worktree, what's left to commit and whether its remote is gone. Tap the current branch or one with a worktree to open a session there. |
| **Changes** | Uncommitted files with their added and removed lines. Tap one to see its diff. |
| **Processes** | What each session started, with its memory. **Stop** ends the process and whatever it started. Refreshes every 3 seconds. |

<p align="center">
  <img src="../../capturas/manual/terminal-panel-ramas.png" alt="The panel's Branches tab" width="48%">
  <img src="../../capturas/manual/terminal-panel-cambios.png" alt="The panel's Changes tab, with a diff" width="48%">
</p>

> [!NOTE]
> Branches and Changes need the folder to be a git repository and git to be installed.

## Settings › Terminal

How sessions look. Open it from **Settings › Terminal** in the Console. It's saved on this computer and applies to every tab.

<p align="center">
  <img src="../../capturas/manual/terminal-ajustes.png" alt="The Terminal settings with their preview" width="720">
</p>

In three groups:

**Tabs**

| Option | Values |
|---|---|
| **Tab list** | With description or Icons only (also with the button at the top of the list). |
| **Default terminal** | The one **New terminal here** opens, in each project's menu. |

**Appearance** (at the top, a preview that changes as you pick)

| Option | Values |
|---|---|
| **Font** | "The usual one" or an installed monospaced font (Cascadia Mono, Consolas, JetBrains Mono, Fira Code…). |
| **Size** | From 11.5 to 18 pt, with **−** and **+**. |
| **Line spacing** | Tight, Normal or Wide. |
| **Text color** | The theme's, Amber, Green or Ice. |
| **Terminal palette** | Claupet, Soft or Vivid: the 16 colors full-screen programs use. |
| **Cursor** | Block, Bar or Underline (for full-screen programs). |
| **Background** | The theme's, Darker or Black. |
| **Animations** | Full or None ("Thinking", loading, sending and fading in). Without them, it uses less. |
| **Ligatures** | Off, `->` and `!=` look as typed; on, as a single sign (if the font has them). |

**Linked projects**, each with its folder, how many tabs it has and **Unlink**. **Defaults**, at the top, goes
back to the original values.

## Limits

- **Closing Claupet Console closes every Terminal session**: no agent is left running.
- The other agents need to be installed and signed in.

---

[← Claupet Console](console.md) · [Index](README.md) · [The pets →](mascotas.md)
