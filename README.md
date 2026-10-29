<div align="center">

<img src="docs/capturas/mascota-voz.png" alt="Claupet listens, thinks and answers" width="420">

# Claupet

**Your usage, your Claude Code sessions and a tiny desktop pet, right next to the clock.**

**English** · [Español](README.es.md)

![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-D97757)
![macOS](https://img.shields.io/badge/macOS-Apple%20%7C%20Intel-D97757)
![Linux](https://img.shields.io/badge/Linux-x64-D97757)
![License](https://img.shields.io/badge/license-MIT-2A1F1A)

[Download](#download) · [Install](#install) · [For developers](#for-developers)

</div>

---

Claude Code does the work. Claupet keeps you in the loop: how much of your plan is left, which
session needs you and what each one is up to, without opening a terminal.

## What it does

- 📊 **Usage at a glance.** Your 5-hour session and weekly limits for every account, with reset
  times, and six months of use in a calendar. The tray icon shows your session percentage.
- 🧭 **Live sessions.** See which Claude Code sessions are working, waiting for you or done, and
  start a new one with the account, folder and model you want.
- 🎬 **Activity.** Follow each session step by step, live. It all comes from local files: zero
  tokens.
- 🔔 **Friendly heads-ups.** A little note, with sound, when a session finishes, asks something,
  needs permission, fails, or you're close to a limit.
- 🦀 **A desktop pet.** The classic critter or a chibi in a crab hoodie who reads, searches,
  types and builds along with your sessions, dances when a limit resets and gets grumpy if you poke
  her too much.
- 💬 **Talk to Claupet** *(Windows)*. Ask the pet, or the island at the top of your screen, by typing
  or out loud, and hear the answer in Claupet's own voice. Drop a file on the pet and get a summary.
  It can also change your wallpaper, theme, volume or music, and hands bigger jobs to Claude Code.
- 🧰 **A hand with Windows** *(Windows)*. It takes or crops screenshots (and looks at them, if you
  want), adds events to your calendar, saves notes, reminds you of things even with the app closed and
  handles windows, brightness, the clipboard, folders and the computer. Each one asks first.
- 📶 **Built for real networks.** Follows your Windows proxy, renews expired sessions on its own
  and, offline, still says hi and tells you the time.
- 🎨 **Make it yours.** Color palettes, pets, animations and sound packs.
- 🌎 **English and Spanish.** It starts in your system's language; switch it any time in
  **Settings › System**.

<p align="center">
  <img src="docs/capturas/isla-chat.png" alt="The island with an answer" width="48%">
  <img src="docs/capturas/caja.png" alt="The pet carries a file to the island, which summarizes it" width="48%">
</p>

## Download

| System | File |
|---|---|
| Windows 10 and 11 | [Claupet-windows-x64.exe](https://github.com/darrenarauz21/claupet/releases/latest/download/Claupet-windows-x64.exe) |
| macOS, Apple chip (M1, M2, M3…) | [Claupet-macos-arm64.zip](https://github.com/darrenarauz21/claupet/releases/latest/download/Claupet-macos-arm64.zip) |
| macOS, Intel | [Claupet-macos-x64.zip](https://github.com/darrenarauz21/claupet/releases/latest/download/Claupet-macos-x64.zip) |
| Linux (x64) | [Claupet-linux-x64.tar.gz](https://github.com/darrenarauz21/claupet/releases/latest/download/Claupet-linux-x64.tar.gz) |

Every file is self-contained. You only need [Claude Code](https://claude.com/product/claude-code)
installed and signed in. Older versions are in [Releases](https://github.com/darrenarauz21/claupet/releases).

## Install

**Windows.** Save the `.exe` in a folder of its own and double-click it. If Windows says it
protected your PC, choose **More info › Run anyway** (the app isn't signed). Look for the orange
icon by the clock, or under the **^** arrow.

**macOS.** Unzip, open **Terminal**, type `sh ` (with a space), drag `preparar-mac.sh` into the
window and press Enter. Claupet moves to **Applications** and opens in the menu bar. If macOS asks
to read "Claude Code-credentials", choose **Always Allow**.

**Linux.** Run `tar -xzf Claupet-linux-x64.tar.gz`, then `sh instalar.sh` inside the folder.
It also shows up in your applications menu. On GNOME, the tray icon needs the AppIndicator
extension (Ubuntu ships with it).

The first time, Claupet offers to set up permission alerts, start with your computer and
notifications. **Apply** takes them all.

## Good to know

- **Private by design.** Claupet reads what Claude Code already keeps on your computer. It never
  stores passwords or your conversations.
- **Chatting uses your plan.** Questions to the pet go through Claude Code with the account you
  pick, like any other message.
- **Something off?** **Settings › Copy diagnostics** gathers what's needed to help you.

## For developers

### Run it from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and Claude Code signed
in. Python 3 is only needed to build the release packages.

```powershell
git clone https://github.com/darrenarauz21/claupet.git
cd claupet
dotnet run --project Claupet.Avalonia              # Windows, Linux and macOS
dotnet test Claupet.Tests                          # tests (on Windows)
python herramientas/empaquetar.py                  # self-contained packages in paquetes/
```

### Project layout

```text
Core/                     shared logic: usage, sessions, activity, notices, assistant, network
Console/                  Claupet Console, the big window
Claupet.Avalonia/         the app on Windows, Linux and macOS: tray, mascot, island, voice
Claupet.Tests/            tests
herramientas/             packaging and install scripts
```

### Built with

| | |
|---|---|
| Runtime | .NET 8 (C#) |
| UI | Avalonia 11, the same on Windows, Linux and macOS |
| Speech to text | Whisper.net (whisper.cpp) |
| Text to speech | ONNX Runtime, plus System.Speech for Windows voices |
| Claude | Claude Code CLI (`claude -p`), with your own account |

### Models

| Model | What for | Where it runs |
|---|---|---|
| Claude Haiku, Sonnet, Opus or Fable | The pet's and the island's answers (the pet uses Haiku by default) | Anthropic, through Claude Code |
| Whisper `ggml-small` | Microphone, speech to text | Your computer (488 MB, downloaded on first use) |
| Supertonic 3 | Claupet's voice, text to speech | Your computer (about 400 MB, downloaded on first use) |

Useful flags while developing: `--preview` opens a second window, `--dump` prints a full usage
check, `--preguntar "<text>"` asks the pet from the console. The full technical notes (in Spanish)
are in [DETALLES.md](DETALLES.md).

## License

[MIT](LICENSE). Bundled libraries and downloaded voice models keep their own licenses: see
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Claupet is a personal, unofficial project, not
affiliated with Anthropic. "Claude" and "Claude Code" are trademarks of Anthropic, PBC.
