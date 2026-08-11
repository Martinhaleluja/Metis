# Operating modes, activation, memory, and visual guidance

This describes the systems added on top of Metis's existing sense-plan-act
pipeline. The capture, planning, and execution layers documented in
`AUTOMATION_ARCHITECTURE.md` are unchanged; these systems decide *how much*
Metis teaches versus does, *what* context it gathers, and *what* it remembers.

## Modes

`OperatingMode` has four values: Learn, Guide, Assist, and Autopilot. The mode
is a user setting (`AppSettings.OperatingMode`, default Guide), changeable from
the tray **Mode** submenu or the Setup **Operating Mode** section.

`Metis.Core.Services.ModePolicy` is the single definition of what each mode
means. Both halves of the system read from it:

- **The prompt.** `ModePolicy.BuildInstruction` produces the mode block that
  `ReasoningProviderSupport.BuildSystemInstruction` appends to the base system
  instruction for every provider.
- **The filter.** `MetisRuntime.ApplyModeAndSafety` re-checks the returned plan
  against the same mode through `SafetyPolicyEngine`.

Because both derive from `ModePolicy`, the prompt and the enforcement can never
disagree. The prompt shapes the answer; it grants nothing. A provider that
returns a click in Learn mode simply has that click dropped.

| | Learn | Guide | Assist | Autopilot |
|---|---|---|---|---|
| Acts unprompted | no | no | yes | yes |
| Acts when asked | no | yes | yes | yes |
| Steps per batch | 2 | 3 | 4 | 6 |
| Explains concepts | yes | no | no | no |
| Tracks skills | yes | yes | yes | no |

Non-mutating steps — `move_pointer`, `observe`, `verify`, the `wait` family, and
`finish` — are permitted in every mode. Showing the user where to go is not a
computer action, so Learn and Guide still point at the exact control.

Every mode withholds high-risk actions. `SafetyPolicyEngine.ClassifyRisk` reads
the action's text, label, key, and id for purchase, deletion, credential,
security, and administrator intent, and Autopilot is not exempt.

The filter is re-applied to every closed-loop replan, so a long task cannot
drift into performing steps the current mode forbids.

## Activation

Three shortcuts reach Metis, all handled by the one existing low-level keyboard
hook:

- `Ctrl+Shift+1` — the original hold-to-talk chord, unchanged.
- `Ctrl+Alt` — context activation. Metis captures the desktop and listens.
- `Ctrl+Alt+Shift` — inspect activation. Adds the control under the pointer.
- `F12` — emergency stop, unchanged.

`ContextActivationKeyState` tracks the Ctrl+Alt chord. Shift may be pressed at
any point during the hold, so a user can start speaking and then add precision;
the activation kind is decided on release. Hold-to-talk takes precedence: while
`Ctrl+Shift+1` is active the context state is suppressed and reset, so the two
shortcuts cannot fire overlapping requests.

Ctrl+Alt is also what AltGr sends on many keyboard layouts. A brief AltGr tap
produces a recording under the 250 ms floor, which the existing voice path
already discards, so ordinary typing does not raise a request.

For an inspect activation the pointer position is captured at *press* time,
before the user moves the mouse, and `FlaUiAutomationService.DescribeElementAtAsync`
walks the UI Automation tree down to the smallest element containing that point.
The result is sent as `pointer_target` (for example
`Pane "Toolbar" > Button "Bold"`), which is what lets "what does this do?"
resolve without the user describing anything. When nothing identifiable is
there, Metis says so instead of guessing.

## Task context

`TaskContextTracker` keeps one goal alive across steps and across applications.
A request that reads as a continuation ("now open the editor", "keep going")
extends the existing task rather than starting a new one, so switching from a
browser to a video editor is a step inside one goal. Tasks go stale after 20
minutes of inactivity. The digest is sent as `ongoing_task`.

## Memory

`JsonMemoryStore` writes `%LOCALAPPDATA%\Metis\memory.json` — structured data,
not a transcript. Screen content never reaches it; only skill names, goals, and
preferences.

Skill memory is deliberately separate from task memory: what the user can do
outlives what the user is doing. `SkillMemoryEngine` advances a skill only on
*unguided* successes, so guidance shrinks because the user stopped needing it
rather than because time passed. A skill never regresses; forgetting is the
user's to declare with **Clear memory** in Setup.

The digest for the active application is sent as `user_skills`, and the Learn
instruction tells the provider to skip anything already advanced or mastered.

Memory writes are disabled entirely by `AppSettings.MemoryEnabled`.

## Visual guidance

`GuidanceOverlayWindow` is a click-through, no-activate, topmost window
stretched over the whole Windows virtual desktop. It draws focus rings, boxes,
arrows, labels, and numbered step badges in screen pixels.

Each frame replaces the previous one and expires on a timer, so guidance cannot
accumulate or outlive its step. The overlay never touches the target
application, and Learn mode additionally dims everything outside the marked
regions because attention matters more than context while learning.

Overlays are controlled by `AppSettings.VisualGuidanceEnabled`.

## Audio feedback

`SoundCueFactory` synthesises two interaction cues as raw PCM rather than
shipping audio files, for the same reason the tray icon is drawn in code: there
is nothing to install, lose, or resolve a path to.

- **Pop** — a 75 ms rising blip when the microphone opens. Short enough to read
  as a click rather than a tone, and quiet enough that the recording starting
  behind it barely picks it up.
- **Woosh** — a 260 ms filtered-noise sweep when the request leaves for the
  provider. It fires only after the recording passes the 250 ms length check, so
  a stray Ctrl+Alt tap stays silent instead of chirping twice.

The woosh uses a fixed random seed, so it sounds identical every time. Cues are
fire-and-forget and their failures are swallowed: a missing sound is never worth
interrupting the user over. Controlled by `AppSettings.ActivationSoundsEnabled`.

### Sound packs

`AppSettings.SoundPackPath` points at a folder of sound files, defaulting to
`sound effects` beside the executable. The pack ships with the build, so a
published Metis has its sounds without any absolute path.

Files are matched to moments by **keyword**, not by exact filename, so a pack
author does not have to match a string table character for character.
`SoundPackNaming` recognises:

| Moment | Matches names containing |
|---|---|
| `AppStarted` | start, launch, ready |
| `RecordingStarted` | record, listen |
| `InspectPressed` | inspect (without a release word) |
| `InspectReleased` | inspect + release / relese / relesed / realese / up |
| `RequestSent` | sent, send, woosh, whoosh, thinking |
| `TaskComplete` | complete, done, finish |
| `SettingsSaved` | setting, saved |
| `Stopped` | stop, cancel |
| `Error` | error, fail |

Order matters where names overlap: "audio recording started" and "app started"
both contain "started", so the more specific subject is tested first. Digits are
stripped during matching, which is what makes `error 1` through `error 4`
variants of one moment rather than four unrecognised names.

When a moment has several files, one is chosen at random **excluding the
previous pick**, so a repeated failure does not sound like a stuck record.

`SoundCueFile` validates every file before it is played, because a cue
interrupts whatever is currently playing:

- 20 MB and 6 seconds are hard ceilings — anything longer would still be
  playing while the user is talking or while Metis is answering.
- WAV, MP3, WMA, and AIFF all decode, and each keeps its own sample rate and
  channel count so nothing is resampled.
- Decoded audio is cached per pack, including failures, so a broken file costs
  one read rather than one per activation.
- A missing or unreadable file falls back to the synthesised cue for the two
  moments that have one, and otherwise stays silent, so a partial pack loses
  one sound instead of breaking the set.

The error sound and the spoken error play **strictly in sequence**. Starting
playback stops whatever is already playing, so overlapping them would let the
sound truncate the sentence explaining what actually went wrong.

## Spoken errors

Every call to `ReportError` also speaks a one-sentence version of the failure.

This deliberately uses the offline Piper voice, not the configured
text-to-speech provider. The errors most worth hearing are exactly the ones
where the cloud provider is unreachable, unauthorised, or out of quota — and a
cloud voice would fail for the same reason.

`SpokenErrorSummarizer` prepares the text: it keeps the first sentence, replaces
Windows paths with "the saved path" and URLs with "the address" since neither
survives being read aloud, and caps the result at 120 characters on a word
boundary. Punctuation only ends a sentence when followed by whitespace, so
version numbers such as `0.5.1` do not cut the message in half.

The speech runs on a background task and logs its own failures rather than
reporting them, because reporting would call back into `ReportError` and start
an endless spoken-error loop. Controlled by `AppSettings.SpeakErrorsAloud`.

## Piper setup

Metis uses the standalone Piper binary at `tools\piper-standalone\piper\piper.exe`
rather than a Python virtualenv, because a venv hard-codes both its interpreter
path and its original location and breaks if either moves.

The text is passed on **stdin**, not as a command-line argument. The standalone
binary only reads stdin, and the Python CLI accepts stdin too, so one call site
serves both. Piper treats each line as a separate utterance, so the text is
flattened onto one line before it is written.
