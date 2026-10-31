# 🐾 The pets

**English** · [Español](../es/mascotas.md)

Chibi, Dokki, Gemma and Ciel live on your desktop and react to what your sessions are doing. Here's how to
pick one, what each gesture means and how to change colors, themes and sounds.

**On this page:** [Pick your pet](#how-to-pick-your-pet) · [On the desktop](#the-pet-on-the-desktop) ·
[What each gesture means](#what-each-gesture-means) · [One per agent](#one-pet-per-agent) ·
[Sounds](#sounds) · [Colors and themes](#colors-and-themes)

<p align="center">
  <img src="../../capturas/mascotas.png" alt="Chibi, Dokki, Gemma and Ciel dancing together" width="560">
</p>

## The characters

<table>
  <tr>
    <td align="center" width="25%"><img src="../../capturas/mascota-chibi.png" alt="Chibi with maracas" width="140"></td>
    <td align="center" width="25%"><img src="../../capturas/mascota-dokki.png" alt="Dokki cheering with a whale spout" width="140"></td>
    <td align="center" width="25%"><img src="../../capturas/mascota-gemma.png" alt="Gemma cheering, then pretending she didn't" width="140"></td>
    <td align="center" width="25%"><img src="../../capturas/mascota-ciel.png" alt="Ciel thinking in code" width="140"></td>
  </tr>
  <tr>
    <td align="center"><b>Chibi</b><br><sub>Claude</sub></td>
    <td align="center"><b>Dokki</b><br><sub>DeepSeek</sub></td>
    <td align="center"><b>Gemma</b><br><sub>Gemini</sub></td>
    <td align="center"><b>Ciel</b><br><sub>Codex</sub></td>
  </tr>
  <tr>
    <td valign="top">Crab hoodie with eyes on stalks. Builds a little house with a mallet and shakes maracas when a limit resets.</td>
    <td valign="top">A whale maid, sweet and calm. Curtsies to say hi, cheers with a spout and sleeps blowing bubbles.</td>
    <td valign="top">Tsundere with twin tails. Cheers… and quickly pretends she didn't. Says hi grudgingly. Hmph!</td>
    <td valign="top">Serene. Thinks in a green <code>&lt;/&gt;</code> and celebrates without jumping, with a calm checkmark.</td>
  </tr>
</table>

Each one has their own gestures and voice, and the assistant takes on their personality when it talks to you.
There's also the **Classic**, the one-color little critter that follows the app's color.

## How to pick your pet

1. Open **Settings › Appearance** (in quick settings or in Claupet Console).
2. Under **Pet**, choose **Classic**, **Chibi**, **Dokki**, **Gemma** or **Ciel**.
3. If you like, change the **Pet size**: Small, Medium, Normal or Large.

The pet you pick also animates the icon while refreshing and shows up in notifications.

> [!TIP]
> Prefer an icon without a pet? The same list has **Asterisk** (Claude Code's ✻), **Dots** (a wave that ends
> in a smile) and **None**. They're only the refresh animation; notifications use the Classic.

## The pet on the desktop

Turn on **Settings › Pet › Desktop pet** (it turns on by itself when you pick a character).

- **Drag it** wherever you like; it stays on screen and remembers its spot.
- **Right-click:** **Open Claupet** or **Hide from desktop**.
- **One click:** it waves. Lots of clicks in a row… better not.
- **Drop a file** on it and it carries it in a box to the island to summarize it (see
  [Notifications and the island](isla.md#files-in-a-box)).

With **Talk (notifications in a bubble)**, it tells you each notification its own way, in a bubble above its
head. Clicking the bubble opens the same thing as the notification.

## What each gesture means

| Gesture | When |
|---|---|
| Reads a book | The session is reading files. |
| Looks through a magnifying glass | Searching the project. |
| Writes in a notebook | Editing or creating files. |
| Types on a laptop | Running commands. |
| Spins a globe | Searching or reading the web. |
| A helper appears with a "poof" | Delegating to a subagent. |
| A cloud with three dots | Thinking about the next step. |
| Builds a little house | Using some other tool, or it's been thinking for a while. |
| Cheers | Done. |
| Hand over its mouth | Waiting for you: a question or a permission. |
| Sleeps | No sessions. |
| Cha-cha-cha with maracas | A limit reset (session or week). |
| Dizzy | Something failed, or usage is at the limit. |
| Waves | When it appears and when you click it. |
| Pouts, crosses its arms, fumes | 3, 6 and 9 clicks in a row. It gets over it in a few seconds. |

With several sessions, it shows what matters most: first a question or a permission, then whatever changes
things (editing, running) and then whatever just looks.

> [!NOTE]
> With music playing on Windows (Spotify, YouTube in the browser…), it bobs its head to the beat and, when
> there's no work going on, dances. It only checks whether something is playing, not what.

### How to turn off a behavior

In Claupet Console, **Settings › Pet** has a switch for each one, all on by default:

| Switch | If you turn it off |
|---|---|
| **On every desktop** | It stops following you when you switch virtual desktops (Spaces on macOS). |
| **Flow gestures (what the session is doing)** | It dances while working, instead of showing each step. |
| **Dance to music** *(Windows)* | It doesn't react to music. |
| **Notification gesture** | It doesn't repeat each notification's gesture. |
| **Wave when appearing** | It appears without waving. |
| **Get angry at too many clicks** | It always waves. |
| **Sleep with no sessions** | It stays still. |

## One pet per agent

Following Codex, Gemini or Copilot too? In **Settings › Pet › Other agents**, turn on **One pet per agent**:
each one gets its own pet next to Claude Code's.

- Pick each one's character in **…'s pet**. Any character works for any agent.
- It types while that agent is working, stays still if it worked recently and then sleeps.
- **Hide from desktop**, in its right-click menu, leaves that agent without a pet.

## Sounds

In **Settings › Alerts**:

- **Sound style:** Classic, Giggles, Chibi (an "oi!", and an "uwaaa!" if something fails), Dokki, Gemma or
  Ciel. Each character has its own tone.
- **Show a sample notification** shows one and plays it.
- **Open sounds folder** opens the `sonidos` folder.

> [!TIP]
> **Your own sounds.** Inside `sonidos`, in the `propios` folder, put a `.wav` or `.mp3` named `terminado`
> (done), `pregunta` (question), `permiso` (permission), `fallo` (failed), `uso` (usage) or `aviso`
> (notice): it plays instead of the chosen style.

## Colors and themes

**Color.** In **Settings › Appearance › Color**, pick a palette: Terracotta (the default), Ocean, Forest,
Lavender, Rose or Graphite. It changes the accent of bars, switches, links, the icon and the Classic pet.
Light or dark mode follows your system.

**Your own themes.** In Claupet Console, **Settings › Appearance › Themes** shows a card per theme, with its
preview. To create one:

1. In **New theme**, describe what you want: "a beach theme with an animated background", "something
   autumnal with a rounder font".
2. Choose the **Quality**: Fast (Sonnet), Medium (Opus) or High (Fable).
3. Press **Create** (or Enter). While Claude designs it, its card glows and you can cancel.
4. When it's done, the theme is selected.

| To… | Do this |
|---|---|
| Pick a theme | Tap its card. The **Claupet** card means no theme. |
| Export or delete it | Use the **⋯** menu on its card. |
| Bring one in | **Import a theme › Choose file…** (an exported `.json`). |
| Go back to a palette | Pick a **Color**: the theme is removed, but stays saved in its card. |

> [!NOTE]
> A theme can bring its own font and an animated pixel-art background (beach, rain, snow, stars, sakura…),
> shown in Claupet Console. Claupet fixes the contrast by itself so everything stays readable. Creating a
> theme uses the assistant's account, like any message.

---

[← Claupet Terminal](terminal.md) · [Index](README.md) · [Notifications and the island →](isla.md)
