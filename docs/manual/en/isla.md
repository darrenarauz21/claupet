# 🏝️ Notifications and the island

**English** · [Español](../es/isla.md)

Claupet lets you know when a session finishes or needs you, and the island at the top edge lets you answer
permissions and questions without hunting for the terminal.

**On this page:** [Notifications](#notifications) · [The island](#the-island) ·
[Permissions and questions on the island](#permissions-and-questions-on-the-island) · [The island chat](#the-island-chat) ·
[Files in a box](#files-in-a-box) · [Weekly recap](#weekly-recap)

## Notifications

A little note with the pet and its sound, at the bottom right (on macOS, at the top, under the menu bar).

| Notification | When | Goes away on its own |
|---|---|---|
| **Done** | A session finished its turn. | After 6 s |
| **Asking you something** | Claude Code asked you a question. | No |
| **Needs your permission** | A session is asking permission to use a tool. | No |
| **Failed** | An API error, with no retries left. | No |
| **Retrying** | 3 retries so far because of an API error. | After 6 s |
| **Closed while working** | The session ended in the middle of a turn. | No |
| **Usage at 80% / 95%** | The 5-hour session or the week crosses that threshold. | After 6 s |
| **Limit reset** | A limit that was at 95% or more drops again. | After 6 s |
| **Session expired** | An account's access expired. | No |

Up to four at a time, and they stay put while the cursor is over them. A click opens whatever fits.

**How to set them up:** in **Settings › Alerts**, turn **Notifications** and **Sound** on or off, and pick the
**Sound style**. **Show a sample notification** shows you one.

> [!IMPORTANT]
> The **Needs your permission** notification depends on a hook Claupet adds to Claude Code (with a backup of
> your `settings.json`). If you don't get them, use **Settings › System › Permission notifications (hook) ›
> Set up…**.

## The island

A small dark window in the middle of the top edge of the screen. Turn it on in
**Settings › Island › Island at the top edge**.

<p align="center">
  <img src="../../capturas/isla-chat.png" alt="The island with an answer" width="560">
</p>

1. Move the cursor to the middle of the top edge: the island peeks out, compact.
2. With the cursor over it (or with a click, if you turned off **Open on hover**) it opens with three tabs:

   | Tab | What's in it |
   |---|---|
   | **Activity** | Each session with its lane of steps. A step that edited a file opens its diff. |
   | **Chat** | A quick conversation with Claupet. |
   | **+ Session** | Opens a new Claude Code session. |

3. It folds away when the cursor leaves or when you click anywhere else, unless you're typing, waiting for an
   answer or one has just arrived.

> [!TIP]
> With **Visible while something is working**, the island stays compact and in view while any session is
> working: "2 working", "1 waiting". On Windows, **Ctrl+Alt+N** opens or folds it.

## Permissions and questions on the island

Answer Claude Code's permissions and questions from the island, without going to the terminal.

### How to turn it on

1. In Claupet Console, open **Settings › Island**.
2. Under **Island permissions and questions**, press **Set up…**.
3. Claupet adds the `PermissionRequest` hook to Claude Code (saving a backup of your `settings.json` first).

It works in Claude Code sessions opened from then on.

### How to answer

When a session asks for permission, the compact island says "Needs your permission · click to see". Open it
and choose:

- **Allow**
- **Always in this session**
- **Deny**

Questions ("Asking you something · click to see") show their options: tap one. If you can pick several, check
the ones you want and press **Next** (or **Send**, on the last one). **Answer in the terminal** lets Claude
Code ask there, as usual.

> [!NOTE]
> If Claupet is closed or nobody answers in time (about two minutes), Claude Code asks in the terminal as
> usual. Leaving the card doesn't answer it: it stays waiting on the compact island.

> [!TIP]
> On Windows, **Ctrl+Alt+A** jumps straight to the pending permission or question. Permissions from
> [Claupet Terminal](terminal.md#permissions-and-questions) sessions reach the island too.

## The island chat

In the **Chat** tab, type your question and press Enter, or use the microphone. It's the same
**Quick conversation** as in the pet's bubble and in Claupet Console.

- It starts over after 10 minutes without typing, or with **New conversation**.
- If you ask for something that's a job for Claude Code, it offers to open a session with that request.
- **Island model**, in **Settings › Island**, picks who answers ("Same as your sessions", by default).

What you can ask it and have it do is in [Assistant and voice](asistente-y-voz.md).

## Files in a box

Drop a file on the pet: it catches it in a box, walks to the island and tosses it in, and the island opens
it, reads it and summarizes it for you as it writes.

<p align="center">
  <img src="../../capturas/caja.png" alt="The pet carries a file in a box to the island, which summarizes it" width="560">
</p>

Below the summary:

- **Ask something else** about the file.
- **Copy** the summary.
- **Continue in a session**: opens Claude Code in the file's folder to work with it.

You can also drop the file straight onto the island. To summarize it, Claude can only read that file's
folder.

> [!NOTE]
> Without the island, the pet doesn't walk: it thinks with the box in its hands and tells you the gist in its
> bubble. To keep it from analyzing files, turn off **Analyze files you drop on it** in
> **Settings › Assistant**.

## Weekly recap

With **Weekly recap on Mondays** on (in **Settings › Island**), every Monday the island shows you how the
previous week went: tokens, sessions, prompts, steps, your busiest day and your favorite model.
**This week's recap › View now** shows it whenever you like.

---

[← The pets](mascotas.md) · [Index](README.md) · [Assistant and voice →](asistente-y-voz.md)
