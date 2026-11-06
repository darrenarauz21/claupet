# 🪟 Claupet Console

**English** · [Español](../es/console.md)

Claupet's big window: the chat, the Terminal, the editor, Canvas and every setting, all in one place.

**On this page:** [How to open it](#how-to-open-it) · [The rail](#the-rail) · [Chat](#chat) ·
[Editor](#editor) · [Canvas](#canvas) · [Settings](#settings) · [Shortcuts](#shortcuts)

## How to open it

Any of these opens the Console (or brings it to the front, if it's already open):

- **Claupet Console** in the icon's menu.
- **All settings › Open**, at the bottom of quick settings.
- Its Start menu or desktop shortcut (Windows).
- Opening Claupet again while it's already running.

There's only one Console at a time. It follows the system's light or dark mode and the color or theme you
picked.

> [!NOTE]
> The chat and settings are handled by Claupet, the one in the tray. If it isn't running, the Console says
> so and offers **Open Claupet**. The Terminal and the editor work the same without it.

## The rail

On the left, a rail with five buttons:

| Button | What for |
|---|---|
| 💬 **Chat** | Talk to Claupet and keep your conversations. |
| ⌨️ **Terminal** | Agent sessions and shells in blocks. It has its own page: [Claupet Terminal](terminal.md). |
| 📝 **Editor** | Open and write documents and code. |
| 🎨 **Canvas** | Live documents you and any AI edit together. |
| ⚙️ **Settings** | All of Claupet's options, by section. |

## Chat

On the left, the list of conversations; on the right, the open one.

- **Quick conversation:** the same one as on the island and in the pet's bubble.
- **Saved ones:** each with its title, grouped into Today, Yesterday, This week and Earlier. They're saved on
  this computer.

### How to chat

1. Press **New chat** (or pick a conversation from the list).
2. Type in "Write to Claupet". **Enter** sends and **Shift+Enter** adds a new line.
3. If you like, use **Attach a file** or **Talk** (with the microphone turned on).
4. While it's answering, **Stop** cuts it off.

Next to the box you can **Change model** (**Automatic**, which picks the model and effort to fit the
question; "Same as your sessions"; Haiku, Sonnet, Opus or Fable) and turn on **Read answers aloud**. Code
blocks come with **Copy**, and below each answer you can see who answered.

> [!TIP]
> It's the same assistant as the pet's and the island's, with fuller answers.
> [Assistant and voice](asistente-y-voz.md) explains it.

To delete a saved conversation, use **Delete the conversation…** (it asks you to confirm). The quick one is
emptied with **Clear the conversation**.

## Editor

A simple editor for documents and code.

| What | How |
|---|---|
| **Text** | Markdown shows without its marks (headings, bold, links, bullets), except on the cursor's line. |
| **Code** | Line numbers and colors for C#, JavaScript, Python, PowerShell, SQL, HTML, CSS, JSON, XML and more. |
| **Files** | **Show files** lists the open and recent ones. Tabs get a dot when there are unsaved changes. |

| Key | What it does |
|---|---|
| Ctrl+N | New |
| Ctrl+O | Open… |
| Ctrl+S | Save |
| Ctrl+Shift+S | Save as… |
| Ctrl+W | Close |

It saves in UTF-8 and, if you close a file with changes, asks first.

## Canvas

Live documents you and any AI edit together, canvas style. A canvas is a list of sections, each with its kind:

| Kind | What it is |
|---|---|
| **Text** | Markdown: headings, lists, tables, links. |
| **Code** | In any language. |
| **HTML** | A whole page or app (with its CSS and JavaScript). It shows in the browser, sandboxed. |
| **Diagram** | Mermaid. Drawn in the app; those it can't draw show as code, and drawn in the browser and the PDF. |
| **SVG**, **Data** (JSON or CSV, shown as a table or chart) and **Image** | Graphics, tables and results, images. |
| **Plan** | Stages and tasks with a summary, a board and a timeline. |

**With agents.** Ask an agent in the [Terminal](terminal.md) to "do it in a canvas": Claude Code, Codex, Gemini CLI and
Copilot get Claupet Canvas when they start (it's an MCP server), whatever model they use. The canvas shows up in the
Console and updates while the agent edits it; what just changed is highlighted for a moment. The agent doesn't rewrite
everything: it changes the piece it needs, so you can keep writing in another section.

**Comments.** Each section's comment button leaves a note (for the AI or for yourself). Agents read them, reply and mark
them resolved.

**Versions.** Every change, yours or an agent's, is a new version. **Versions** shows who changed what and lets you go
back to an earlier one (the current one is kept).

**Edit.** Each section's pencil opens it for typing. Ctrl+Enter or a click outside saves it, and so does switching canvas
or closing the window; **Esc discards what you typed**. Ctrl+K searches and inserts, Ctrl+N creates a canvas and Ctrl+J
asks the AI for a change.

**Export.** PDF, Word, Markdown, an HTML page that opens on its own in any browser, or JSON. Also **View in the browser**.
PDF needs Microsoft Edge, Google Chrome or Chromium. In Word, diagrams and HTML go in as text.

**From other apps.** **⋯ › Use from other apps** copies the MCP configuration to paste into Claude Desktop, Cursor,
VS Code or any app with MCP: their models will see and edit the same canvases. By hand, the command is
`Claupet --canvas-mcp`.

They're kept in Claupet's data folder, under `Canvas` (on Windows, `%APPDATA%\Claupet\Canvas`).

## Settings

All of Claupet's options, in sections: **General**, **Alerts**, **Appearance**, **Pet**, **Assistant**,
**Answers**, **Extensions**, **Island**, **Accounts** and **System**, plus **Terminal**. The icon's quick
settings only have the everyday ones; everything is here.

Dropdown lists work with the keyboard too: the arrows, **Enter** to choose and **Esc** to close.

Every option is explained in [Settings](ajustes.md).

## Shortcuts

*Windows only.* In **Settings › General › Shortcuts**:

- **In the Start menu** (on by default).
- **On the desktop**.

Both open Claupet Console with Claupet's icon. If Claupet wasn't running, they start it too.

---

[← Usage, activity and history](uso.md) · [Index](README.md) · [Claupet Terminal →](terminal.md)
