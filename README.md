# Metis

**A teacher for your computer.** Ask Metis what is on your screen, or how to do
something, and it explains while marking the screen where to look — then you do
it yourself.

Metis is a Windows desktop assistant built with C# and .NET 8. It lives in a
notch at the top of your screen, reads the screen when you ask it to, and
answers in text, in speech, and by drawing on what you are looking at.

> **Source-available, not open source.** The code is public so you can read what
> an application that photographs your screen actually does. It is licensed
> under a proprietary end-user agreement — see [LICENSE](LICENSE).

---

## What it does

**It teaches, it does not take over.** Metis cannot click, type, move your
pointer or open anything. Ask "how do I change my default browser" and it walks
you through it a step at a time, drawing on each control as you reach it, while
you do the clicking. That is a structural property, not a setting: there is no
mode that operates the computer for you. Metis had one once, and it was removed,
because a tool that does the task for you cannot also be the thing that teaches
you to do it.

**It answers about what is in front of you.** Hold `Ctrl+Alt` and ask about the
screen; hold `Ctrl+Alt+Shift` and point at one control to ask about that exact
thing. It reads the real names of controls from Windows rather than guessing
from pixels, so when it says "the Save button" it means the one actually called
that.

**It remembers what you have learned.** Per application, so its guidance gets
shorter as you get better at something.

**It can also run background tasks.** Separately from the teaching assistant,
Metis can start an autonomous agent that *does* create and edit files, run
commands and drive a visible browser window. This is opt-in, it runs in its own
folder unless you grant access to yours, it pauses for approval before anything
destructive, and it is refused access to credential stores outright. It is a
different thing from the assistant, and it is documented as such because the
distinction matters.

---

## Privacy, in one paragraph

Metis photographs your **whole desktop** and sends it to an AI provider **you**
choose and pay for with **your own key** — there is no Metis server in the
middle. It captures only when you ask, never in between. Content an application
marks as private (banking apps, password managers, view-once photos in WhatsApp
and Signal) is blacked out before anything is sent, password boxes are never
read, and you can name other apps to hide. Chats and memory are encrypted on
your machine; keys live in Windows Credential Manager. Point it at a local model
through Ollama and nothing leaves the machine at all.

The full detail is in **[PRIVACY.md](PRIVACY.md)**, including what each provider
receives and how to delete everything.

---

## Install

Download the latest `Metis-Setup-*.exe` from
[Releases](https://github.com/Martinhaleluja/Metis/releases) and run it.

It installs per-user under `%LOCALAPPDATA%\Programs\Metis`, so it needs no
administrator rights. The installer is not code-signed yet, so Windows
SmartScreen will warn about it — **verify the SHA-256 published in the release
notes** against your download before running it:

```powershell
Get-FileHash .\Metis-Setup-3.15.0-win-x64.exe -Algorithm SHA256
```

Metis checks for updates itself and verifies that checksum before installing
one.

---

## Providers

Metis needs one AI provider. You supply the key; the account is yours.

| Provider | Reasoning | Voice in | Voice out | Notes |
|---|---|---|---|---|
| **Gemini** | ✅ | ✅ | ✅ | Free tier available; the default |
| **Claude** | ✅ | — | — | Pair with AssemblyAI for voice input |
| **OpenAI** | ✅ | ✅ | ✅ | API billing is separate from ChatGPT Plus |
| **OpenRouter** | ✅ | — | — | Many models behind one key, including free ones |
| **Ollama** | ✅ | — | — | Local. Needs a vision-capable model |
| **OpenClaw** | ✅ | — | — | Self-hosted gateway |
| **Automatic** | ✅ | ✅ | ✅ | Gemini, then OpenAI, then Claude |

Optional: **AssemblyAI** or **Whisper.cpp** for speech-to-text, **ElevenLabs**
or **Piper** for speech. Piper and Whisper.cpp are offline.

Every provider has a **Test** button in Setup that makes a real request and
reports exactly what went wrong if it fails.

---

## Shortcuts

| Keys | What it does |
|---|---|
| `Ctrl+Alt` (hold) | Ask about what is on screen |
| `Ctrl+Alt+Shift` (hold) | Point at one control and ask about it |
| `Ctrl+Shift+1` (hold) | Push to talk |
| `Ctrl+Shift+A` (hold) | Start a background agent by voice |
| `Esc` | Close the chat |

---

## Building it

Requires Windows 10 build 19041 or newer and the .NET 8 SDK.

```powershell
dotnet restore Metis.sln
dotnet build Metis.sln -c Debug
dotnet test Metis.sln -c Debug
dotnet run --project src/Metis.App/Metis.App.csproj
```

A self-contained build, written to `artifacts\win-x64`:

```powershell
.\scripts\build.ps1 -Configuration Release -Publish
```

The installer, which also prints the SHA-256 to put in the release notes:

```powershell
.\installer\build-installer.ps1
```

### Layout

| Project | What lives there |
|---|---|
| `Metis.App` | WPF shell, the notch, the companion, the turn orchestrator |
| `Metis.AI` | Provider clients and the reply parser |
| `Metis.Core` | Models, contracts, the teaching policy, the agent loop |
| `Metis.Data` | Settings, chats, memory, the diagnostic log |
| `Metis.Windows` | Capture, UI Automation, audio, hotkeys |
| `Metis.Api` | An HTTP gateway, currently unused by the desktop app |

---

## Security

Report a vulnerability privately — see **[SECURITY.md](SECURITY.md)**, which
also lists Metis's known limitations honestly.

## Licence

Proprietary. See [LICENSE](LICENSE). Third-party components and their licences
are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
