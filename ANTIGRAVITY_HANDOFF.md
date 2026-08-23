# Handoff: Metis — for Antigravity

This file is written for a coding assistant that has never seen this project
before. It explains what Metis is, exactly where the code stands right now
(including a broken build with a known fix), and what to do next. It was
written by Claude Code because the session's weekly quota ran out mid-task —
the work below is real, uncommitted, in-progress code, not a plan that was
never started.

Read this whole file before touching anything. The "Immediate next step"
section at the bottom is the very first thing to do.

## What Metis is

Metis is a Windows-native desktop companion, built in C#/.NET 8/WPF. It is
**a learning instrument, not a control instrument**: it teaches the user how
to use software and explains concepts (maths, physics, biology, etc.) through
on-screen annotations, drawing, and narrated step-by-step guidance. As of the
most recent commit (`52fb66e`, see below), Metis **no longer operates the
computer** — there is no clicking, typing, or automated control of any kind.
It only ever points, draws, and talks. This is a deliberate, recent product
decision, not a limitation to work around.

A small vector "companion" character follows the cursor around the screen and
is Metis's visual presence. A "notch" — a small pill-shaped always-on-top
window — is the single control surface: it is where chat happens, where
sign-in happens, and where settings are reached. There is no separate
assistant window anymore (see "What this session was doing" below).

Metis talks to a hosted Supabase backend for accounts/auth (see
`src/Metis.Core/Services/MetisBackend.cs`) and to one of several LLM
providers (Gemini, OpenAI, Claude, OpenClaw, Ollama) for reasoning, chosen in
Setup. There is also a marketing/waitlist website in `website/` (React +
Vite) and a `supabase/` directory with the backend's schema migrations.

**Important:** the top-level `README.md` is stale. It still describes an
older 4-mode (Learn/Guide/Assist/Autopilot) desktop-automation design that
was deliberately removed in commit `52fb66e`. Do not trust `README.md` for
current behavior — trust the code and this file instead. Updating the README
to match the new learning-only design would be a reasonable follow-up task,
but nobody has done it yet.

## Repository layout (the parts that matter)

- `src/Metis.App/` — the WPF application (windows, tray icon, startup).
- `src/Metis.Core/` — models and pure/testable services (no WPF, no Win32).
- `src/Metis.Windows/` — Win32/UIA interop (window resolution, annotation
  placement on real screens).
- `src/Metis.AI/`, `src/Metis.Data/` — provider clients and data access.
- `tests/Metis.Tests/` — unit tests, mostly for `Metis.Core` pure logic.
- `website/` — the public marketing + waitlist site (separate npm project).
- `supabase/` — SQL migrations for the hosted backend.
- `installer/` — Inno Setup script and PowerShell build script for the
  Windows installer.
- `docs/` — design docs. Some of these (e.g. `docs/OPERATING_MODES.md`) may
  also describe the old mode system and could be stale for the same reason
  as the README; check the code before trusting them.

Build/test commands (PowerShell, Windows only — this is a WPF app):

```powershell
dotnet restore Metis.sln
dotnet build Metis.sln -c Debug
dotnet test Metis.sln -c Debug
dotnet run --project src/Metis.App/Metis.App.csproj
```

## Current git state

Branch: `feat/launch-website` (tracking `origin/feat/launch-website`, up to
date with the remote as of this session).

```
Staged (already `git add`ed, not committed):
  deleted:  src/Metis.App/Windows/AssistantWindow.xaml
  deleted:  src/Metis.App/Windows/AssistantWindow.xaml.cs

Modified, unstaged:
  installer/build-installer.ps1
  sound effects/app started.mp3        (re-recorded audio asset, binary)
  src/Metis.App/App.xaml.cs
  src/Metis.App/Runtime/MetisRuntime.cs
  src/Metis.App/Windows/CompanionWindow.xaml.cs
  src/Metis.App/Windows/GuidanceOverlayWindow.xaml.cs
  src/Metis.App/Windows/NotchWindow.xaml
  src/Metis.App/Windows/NotchWindow.xaml.cs
  src/Metis.Core/Models/AppSettings.cs
  src/Metis.Core/Models/LessonModels.cs
  src/Metis.Core/Services/AnnotationDuration.cs
  src/Metis.Core/Services/CompanionFlight.cs
  src/Metis.Core/Services/CompanionShapes.cs
  src/Metis.Core/Services/CompanionSpeech.cs
  src/Metis.Core/Services/DiagramStepDuration.cs
  src/Metis.Core/Services/LessonStepRouting.cs
  src/Metis.Windows/WindowsAnnotationResolver.cs
  tests/Metis.Tests/AnnotationSystemTests.cs

Untracked (new files, not yet `git add`ed):
  src/Metis.App/Windows/NotchAuth.xaml
  src/Metis.App/Windows/NotchAuth.xaml.cs
  src/Metis.App/Windows/NotchChat.xaml
  src/Metis.App/Windows/NotchChat.xaml.cs
  src/Metis.App/Windows/TopmostGuard.cs
  src/Metis.Core/Services/GuidanceTuning.cs
  src/Metis.Core/Services/MetisBackend.cs
  src/Metis.Core/Services/ScreenBoundsClamp.cs
  src/Metis.Core/Services/StartupAuthGate.cs
  tests/Metis.Tests/CanvasVersusScreenTests.cs
  tests/Metis.Tests/CompanionPlacementTests.cs
  tests/Metis.Tests/MetisBackendTests.cs
  tests/Metis.Tests/ScreenBoundsClampTests.cs
  tests/Metis.Tests/StartupAuthGateTests.cs
  .claude/            (this tool's own config — irrelevant to the app)
  agent/metis_agent/models/  (unrelated to this session's work; leave alone
                               unless you know what it is)
```

Nothing above has been committed yet. All of it is one coherent piece of
work (explained next) and should probably land as a single commit once it
builds and tests pass, following the existing commit-message style (see
`git log` — descriptive body paragraphs, first line under ~70 chars, ends
with an AI co-author trailer).

## What this in-progress session was doing

The prior commit (`52fb66e`, "Make Metis a learning instrument") stripped
out desktop automation. This uncommitted work is a **separate, later change**:
it replaces the old free-floating `AssistantWindow` (chat window that folded
in and out of the notch) with chat and first-run sign-in built directly
*into* the notch itself, and adds startup gating so nothing appears until the
user has signed in.

Concretely:

1. **`AssistantWindow` is deleted.** It's gone from `App.xaml.cs` entirely.
2. **`NotchChat` (new)** — `src/Metis.App/Windows/NotchChat.xaml[.cs]` — is a
   `UserControl` that *is* the chat UI, hosted inside `NotchWindow` rather
   than as its own top-level window. `NotchWindow` gained `ConnectChat()`,
   `OpenChat()`, `CloseChat()`, and `IsChatOpen` to manage it (all already
   implemented — see `src/Metis.App/Windows/NotchWindow.xaml.cs`).
3. **`NotchAuth` (new)** — `src/Metis.App/Windows/NotchAuth.xaml[.cs]` — is
   a `UserControl`, also hosted inside `NotchWindow`, that is the first-run
   sign-in panel. `NotchWindow` gained `ConnectAuth()` and an `Auth` member
   to manage it. **This file does not currently compile — see below.**
4. **`StartupAuthGate` (new)** —
   `src/Metis.Core/Services/StartupAuthGate.cs` — is a pure, well-documented
   decision function: given whether a backend is configured, whether a
   session token is stored, whether refreshing it succeeded, and whether the
   backend was reachable at all, it decides `Allow` or `HoldForSignIn`. It
   deliberately gives a 30-day offline grace period to someone who signed in
   before but can't reach the backend right now (e.g. the free Supabase plan
   pauses after a week of inactivity, and a user with no network shouldn't
   be locked out). Read the doc comment in that file — it's short and
   explains the reasoning well. Has its own test file,
   `tests/Metis.Tests/StartupAuthGateTests.cs`.
5. **`MetisBackend` (new)** —
   `src/Metis.Core/Services/MetisBackend.cs` — holds the compiled-in default
   Supabase project URL and *publishable* (safe, public) key, so a fresh
   install has something to authenticate against without the user having to
   type in a server address. Settings still override these when set. Has
   `tests/Metis.Tests/MetisBackendTests.cs`.
6. **`TopmostGuard` (new)** —
   `src/Metis.App/Windows/TopmostGuard.cs` — re-asserts `HWND_TOPMOST` on a
   timer for a declared stacking order of windows (overlay, then companion,
   then notch on top). `Topmost="True"` in WPF is a one-time claim, not a
   standing one, so other always-on-top surfaces (taskbar flyouts, other
   apps) can end up above Metis and make it look like it vanished; this
   fixes that.
7. **`ScreenBoundsClamp` (new)** —
   `src/Metis.Core/Services/ScreenBoundsClamp.cs` — pure geometry that pins
   an annotation mark inside the actual visible bounds of whichever monitor
   it belongs to, since upstream code works in one big virtual-desktop
   coordinate space where a valid point can still land in a gap between
   monitors or off the edge of the only one. Has
   `tests/Metis.Tests/ScreenBoundsClampTests.cs`.
8. **`GuidanceTuning` (new)** —
   `src/Metis.Core/Services/GuidanceTuning.cs` — a single multiplier
   (currently 0.83, i.e. ~17% faster) that every lesson-pacing duration
   passes through, so overall guidance speed is one number instead of a
   dozen separately-tuned durations. Deliberately does not touch spoken
   audio length. Tested via `GuidanceTuingTests` inside
   `ScreenBoundsClampTests.cs`.
9. **`App.xaml.cs` startup sequence changed:** it now builds a
   `TopmostGuard`, wires `NotchWindow.Auth`/`.Chat`, and calls
   `HoldForFirstRunAsync(e.Args)` — a new method that, if `StartupAuthGate`
   says to hold, shows *only* the notch's auth panel and returns early
   (returns `true`), skipping the rest of startup including showing the
   companion window. The companion window is deliberately **not** shown
   until first-run/sign-in is done — see the comment block in
   `App.xaml.cs` around where `_companionWindow.Show()` used to be
   unconditional.
10. Assorted smaller diffs in `CompanionWindow.xaml.cs`,
    `GuidanceOverlayWindow.xaml.cs`, `MetisRuntime.cs`,
    `WindowsAnnotationResolver.cs`, and the `Metis.Core` duration/model files
    are supporting changes for the above (topmost wiring, guidance tuning
    plumbing, screen-bounds clamping) — read their diffs
    (`git diff -- <path>`) if you need details; they're smaller and more
    self-explanatory than the new files.
11. New test files (`CanvasVersusScreenTests.cs`, `CompanionPlacementTests.cs`)
    cover companion/annotation placement math related to the screen-bounds
    and coordinate-space work.

None of this has been run end-to-end in the app yet in this session — the
build broke before that was possible (see next section).

## Immediate next step: fix the build

`dotnet build Metis.sln -c Debug` currently **fails** with exactly two
errors, both in the same new file:

```
src\Metis.App\Windows\NotchAuth.xaml.cs(31,34): error CS0104:
  'UserControl' is an ambiguous reference between
  'System.Windows.Controls.UserControl' and 'System.Windows.Forms.UserControl'

src\Metis.App\Windows\NotchAuth.xaml.cs(127,56): error CS0104:
  'KeyEventArgs' is an ambiguous reference between
  'System.Windows.Forms.KeyEventArgs' and 'System.Windows.Input.KeyEventArgs'
```

**Why this happens:** `Metis.App.csproj` sets both `<UseWPF>true</UseWPF>`
and `<UseWindowsForms>true</UseWindowsForms>` (the tray icon and its menu are
WinForms; everything else is WPF). With both enabled, several type names
exist in both `System.Windows.Controls`/`System.Windows.Input` (WPF) and
`System.Windows.Forms` (WinForms), and the compiler can't pick one. The
project's existing convention for handling this — see the comment already at
the top of `src/Metis.App/Windows/NotchChat.xaml.cs` — is to add explicit
`using` aliases for every ambiguous name a file actually uses. `NotchChat.xaml.cs`
already does this correctly (aliases `Color`, `ColorConverter`, `Brush`,
`Brushes`). `NotchAuth.xaml.cs` is missing the same treatment for
`UserControl` and `KeyEventArgs`.

**The fix** — add these two lines to the `using` block at the top of
`src/Metis.App/Windows/NotchAuth.xaml.cs` (near the other usings, following
the same pattern as `NotchChat.xaml.cs`):

```csharp
using UserControl = System.Windows.Controls.UserControl;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
```

After adding those, re-run `dotnet build Metis.sln -c Debug`. It's possible
(though not confirmed) that fixing these reveals further errors elsewhere in
the same file or in files that depend on it — the compiler stopped after
reporting these two. Keep iterating on `dotnet build` until it's clean, then
run `dotnet test Metis.sln -c Debug` and make sure everything passes,
including the five new test files listed above.

## After the build is green

1. **Actually run the app and drive it**, per this project's standing
   testing expectation: build the self-contained exe
   (`.\scripts\build.ps1 -Configuration Release -Publish`, output in
   `artifacts\win-x64\Metis.exe`) or just `dotnet run --project
   src/Metis.App/Metis.App.csproj`, and manually exercise:
   - **First run / no stored session:** delete or rename
     `%LOCALAPPDATA%\Metis` (back it up first if it has real data) to
     simulate a clean install, launch, and confirm the notch shows *only*
     the `NotchAuth` sign-in panel — no companion, no chat — until sign-in
     completes, then confirm the companion appears and chat becomes
     reachable.
   - **Chat inside the notch:** open chat from the tray icon / notch, send
     a message, confirm it behaves like the old `AssistantWindow` did
     (same conversation, same provider wiring) but now lives inside the
     notch and toggles open/closed rather than folding a separate window.
   - **Topmost behavior:** with another always-on-top app or the taskbar
     interacted with, confirm the notch/companion/overlay stay above it
     (this is what `TopmostGuard` is for).
   - **Multi-monitor / DPI:** if more than one monitor is available, check
     that annotation marks stay clamped onto the correct monitor's visible
     bounds (`ScreenBoundsClamp`), especially near monitor edges/gaps.
   - **Offline grace:** hard to simulate fully, but at minimum confirm a
     signed-in session survives an app restart with network available, and
     read `StartupAuthGateTests.cs` to understand the exact rules being
     tested so manual testing can target the edge cases unit tests can't
     reach (real Win32 windows, real timers).
   This project has been burned before by changes that compile and pass
   unit tests but are broken in the actual running app (frozen animations,
   invisible toolbars, unreachable Escape key) — WPF window behavior, Win32
   z-order, and animations are exactly the kind of thing unit tests don't
   cover. Don't report this work as done without having launched it.

2. **Stage and commit** once verified. The two staged deletions
   (`AssistantWindow.*`) plus all the modified/untracked files described
   above are one coherent change (notch-hosted chat + auth + startup
   gating). Write a commit message in the style of the existing log (see
   `git log` on this branch) — descriptive, explains *why*, not just what.
   Do not commit `.claude/` (tooling config, unrelated) or
   `agent/metis_agent/models/` unless you've separately confirmed what that
   is and that it belongs in this change.

3. **Consider fixing the stale `README.md`** (and possibly
   `docs/OPERATING_MODES.md`) to describe the current learning-only,
   notch-hosted design instead of the removed 4-mode automation system.
   This wasn't part of the in-progress work above, but it's a known gap.

## Standing project conventions worth knowing

- **Metis is learning-only.** Do not reintroduce automated clicking,
  typing, or any form of computer control. If a request seems to want that,
  it's likely a misunderstanding of the current product direction — Metis
  points, draws, and narrates; it never acts on the user's behalf.
- **The website is light-themed only** — no dark mode. Don't add one.
- Doc comments in this codebase are unusually long-form and explain *why*,
  not just *what* — match that style if adding new services, but keep
  regular code comments minimal per normal practice.
- When something is asked for, finish all of it in one pass rather than
  reporting part of it as remaining work — this project's owner has pushed
  back on partial delivery before.
- UI/window changes must be verified by actually launching the app, not
  just by a clean build and passing unit tests.
