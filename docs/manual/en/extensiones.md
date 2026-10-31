# 🧩 Extensions

**English** · [Español](../es/extensiones.md)

Add voices, pets, sounds, skills and assistants made by others to Claupet, in signed `.claupet` packages.

**On this page:** [What one can bring](#what-an-extension-can-bring) · [Install](#how-to-install-an-extension) ·
[Signatures](#what-the-signature-means) · [Manage them](#how-to-manage-your-extensions) · [Local API](#the-local-api) ·
[For authors](#for-authors)

> [!NOTE]
> For now, extensions are Windows only. Without extensions, Claupet works just as it always has.

## What an extension can bring

| What | What for |
|---|---|
| **Skills** | `/…` commands in the pet's chat, the island and the Console, and reactions to notifications, sessions or dropped files. |
| **Pets** | A new character to pick in **Appearance › Pet**. |
| **Voices and sounds** | Another voice style or set of notification sounds. |
| **Speech to text** | Another engine for the microphone, instead of Whisper. |
| **Text to speech** | Another voice to read answers, instead of Claupet's. |
| **Assistant** | Another model for the pet and the island (DeepSeek, Ollama, LM Studio…), instead of Claude Code. |

Speech to text, text to speech and assistant are **exclusive slots**: only one thing at a time can fill each
one. Installing an extension doesn't put it in those slots; you choose when you install it and can change it
later.

## How to install an extension

1. Get the `.claupet` file from someone you trust.
2. Drop it on the pet or the island. (Or, in Claupet Console, **Settings › Extensions › Install from
   file…**; or **Install…** in the setup assistant.)
3. Claupet Console opens the **review**: which version it is (or whether it updates, rolls back or
   reinstalls), who signed it, what it brings, whether it runs a program, the permissions it asks for and
   the slots it can fill.
4. If you agree, press **Install**. Nothing is installed without that step.

> [!WARNING]
> An extension that runs a program or a DLL does so with your Windows permissions. Claupet controls what the
> extension asks the app for (the pet, notifications, the microphone, Claude…), but not what the program does
> on its own. **Only install packages from people you trust.**

### The permissions it can ask for

| Permission | What it allows |
|---|---|
| Move the pet and make it talk | Animate it and give it lines. |
| Show notices | Notifications like Claupet's. |
| Show cards on the island | Its own cards on the island. |
| Speak with Claupet's voice | Say something out loud. |
| Listen to the microphone | Receive what comes in through the microphone. |
| Listen to what's playing on the computer | Receive the computer's audio. |
| Ask Claude with your account (uses your usage) | Use Claude Code with your plan. |
| See your Claude Code sessions | Know about your sessions and their steps. |
| Read and write the clipboard | Use the clipboard. |
| Connect to the internet (or to a site) | Go online. |
| Use files in … | Read or write in that path. |

If an update asks for new permissions, the review shows them before you install it.

## What the signature means

| The review says | What happens |
|---|---|
| **Official Claupet signature** | It installs. |
| **Signed by an author you already trust** | It installs. |
| **Signed by a new author** | Their key is shown. By installing it, you trust their future versions. |
| **Unsigned** | It installs with a warning: there's no way to know who made it or whether someone changed it. |
| **Invalid or revoked signature, or a different key** | It doesn't install. An update must come with the same key as the installed version. |

## How to manage your extensions

In Claupet Console, **Settings › Extensions** lists the installed ones. Tap one to see its page:

- Whether it's **On** or **Off** (and why it was turned off).
- Its version, author, signature, license, site, what it brings, its commands and its permissions.
- **Its settings.** Keys are stored encrypted and never shown again: you type a new one or delete it.
- **Open its log** and **Open its data folder**, to see what happened.
- **Uninstall…**, keeping its data or deleting it along with its keys.

> [!NOTE]
> An extension that fails 3 times in 10 minutes turns itself off, its slot goes back to Claupet's own, and
> you're told why.

## The local API

Some programs on your computer can ask Claupet for things through the **Local API**, in
**Settings › Extensions**. Each program needs its own **token**:

1. Turn on the local API and create a token: write which program it's for and what it can ask for.
2. Copy it right then: it won't be shown again.
3. To take its access away, use **Revoke** (it asks you to confirm).

## For authors

Want to write an extension? The full guide (in Spanish), with the package format, permissions, the protocol,
how to sign it and examples in Python, Node.js and C, is in
[docs/extensiones/README.md](../../extensiones/README.md).

---

[← Settings](ajustes.md) · [Index](README.md) · [FAQ →](preguntas-frecuentes.md)
