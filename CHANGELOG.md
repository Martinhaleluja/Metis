# Changelog

All notable changes to Metis are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and Metis uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added — privacy and trust

- **Metis cannot see content an application marks as private.** Windows lets a
  program set a display affinity on its own windows, and WhatsApp and Signal use
  it for view-once media, as banking apps, password managers and video players
  do for themselves. Metis finds those windows and paints them black on the
  full-resolution frame before anything is scaled or encoded, so the pixels
  never exist in a buffer that could be uploaded.
- **Password fields are never read.** The one being typed into is blacked out of
  the screenshot; no password field's contents, name or identifier reaches the
  accessibility snapshot in any mode.
- **A never-look-at list** in Settings, matched on process name or window title.
- **The model is told when something was withheld**, so it says it cannot see
  rather than describing a black rectangle as a dark panel.
- **Metis is no longer in its own screenshots.** Its windows are excluded from
  screen capture, so the chat transcript stopped being uploaded inside the very
  picture it was being asked about — and Metis no longer appears in anyone
  else's screen recording.
- **Chats and memory are encrypted at rest** with DPAPI, readable only by the
  Windows account that wrote them. Documents from earlier versions still open
  and are re-encrypted on their next save.
- **Updates are verified.** The build script prints a SHA-256 for the release
  notes, and Metis refuses to run an installer that does not match it.
- **Agents are refused credential stores outright** — SSH and cloud keys,
  browser profiles, saved-password stores, `.env` files, Metis's own records —
  whatever permission the task was given.
- **Wider secret redaction in the log**, now covering Anthropic, OpenAI,
  OpenRouter, AssemblyAI, ElevenLabs and JWT shapes.
- `LICENSE`, `PRIVACY.md`, `SECURITY.md` and `THIRD-PARTY-NOTICES.md`. The
  licence is now shown and accepted by the installer.

### Changed

- Onboarding says the screenshot can contain **other people's** messages and
  documents, not only that a picture is taken.

## [3.15.0] — 2026-08-27

### Changed — speed

Median turn time fell from **13.5 s to about 7 s**, and Metis accepts the next
question the moment the answer appears rather than 8–10 s later.

- **Replies appear as they are written.** Every provider now streams. The
  sentence is read out of the partially-arrived JSON, so it reaches the screen
  before the lesson steps and coordinates that follow it.
- **The turn lock releases when the text lands**, not when speech ends. Asking
  something new interrupts what Metis was saying.
- **Three artificial delays removed.** The companion re-typed finished answers
  at 240 ms/word when there was no audio — a 60-word reply took another 14
  seconds to appear after it had fully arrived. Audio length was waited out
  twice on three paths, doubling every pause in a walkthrough. Three 996 ms tail
  delays held the turn open for a "Done" indicator.
- **A lighter look at the screen.** Ordinary questions use a 1280×720 capture
  instead of the full desktop resolution; pointing at a control still gets full
  detail. Capture and the accessibility scan now run at the same time instead of
  one after the other.
- **A thinking budget is set**, so a quick question no longer pays for
  deliberation the user cannot see.
- **Failures fail fast.** A request the provider rejected no longer walks the
  whole provider chain. Connecting has its own short timeout, and connections
  are pooled across turns.
- **Agents carry their conversation forward** as a real message array with
  prompt caching, instead of re-sending their whole history every step, and
  default to a current model.
- **Every turn writes a timing line** to the log: capture, screen names, time to
  first word, total, and token counts.

> The commit tagged v3.14.0 also carried the first half of this work — the
> streaming path, the capture profile and the incremental chat bubble — because
> it was committed mid-session before the release was cut. Recorded here so the
> history is not misleading.

## [3.14.0] — 2026-08-27

### Added
- Light mode reaches the notch and the chat inside it.
- Virtual desktop capture improvements.

### Fixed
- Notch and chat stayed black when the rest of Metis went light, taking any
  theme-coloured text with them.

## [3.13.0] — 2026-08-26

### Added
- Agents that finish the job, with a verification step before completion.
- A visible browser an agent drives, with a banner saying so.
- Working Windows notifications, carrying Approve and Deny buttons.
- Per-agent workspaces.

### Fixed
- Notifications had never worked — Windows was dropping every one, invisibly.
- Speech was pointed at a model that cannot produce audio and no longer exists.

[Unreleased]: https://github.com/Martinhaleluja/Metis/compare/main...feat/launch-website
[3.15.0]: https://github.com/Martinhaleluja/Metis/releases/tag/v3.15.0
[3.14.0]: https://github.com/Martinhaleluja/Metis/releases/tag/v3.14.0
[3.13.0]: https://github.com/Martinhaleluja/Metis/releases/tag/v3.13.0
