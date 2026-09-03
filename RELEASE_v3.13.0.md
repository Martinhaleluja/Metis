# Metis v3.13.0 — release handoff

**For Gemini / Antigravity to publish. Claude prepared and verified this; it has not been committed or pushed.**

Everything below is in the working tree at `C:\Users\halel\Documents\Lulu` on branch `feat/launch-website`. The build compiles, 848 tests pass, and each headline change was verified by running the real application — not by a clean build alone. Where something was checked only once, or not at all, it says so.

---

## What this release is

The agent system existed and was good — an orchestrator, a ReAct loop, tools, an approval gate. It could not finish long work, and almost nothing it produced was reachable from the interface. This release is mostly about closing that gap, plus fixing three things that had been silently broken.

**Do not publish an older build.** This is the one with the agent work, the browser, the notification fix and the voice fix.

---

## 1. Version and build

Set to **3.13.0** in `installer/build-installer.ps1` (the `$Version` default). Build the installer with:

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

That stamps `Version`, `AssemblyVersion`, `FileVersion` and `InformationalVersion`, and produces `Metis-Setup-3.13.0-win-x64.exe`.

`WhatsNewWindow.Version` is also `"3.13.0"` and **must match**. It is a separate constant on purpose, so that changing one without the other is visible in review rather than silent.

---

## 2. What changed

### Agents can be asked for in plain language
Spawning was gated behind a regex that ran *before* the model saw the message. When it missed — on "use an agent to…", "have an agent…", "kick off an agent", or any two-turn exchange — the request fell through to a teaching prompt that forbids claiming to act, so Metis described an agent it could not start. The model now decides, through a `spawn_agents` field in its reply schema, and the conversation so far travels with the request so a follow-up answer works. `/spawn` and `/agent` remain as a zero-latency path.

*Verified against the live model:* six previously-failing phrasings all spawn, "spawn agents to X and Y" spawns two, two ordinary teaching questions spawn none, and the two-turn flow works.

### Agents can actually build things
Three tools that did not exist: `search_content` (there was no way to search inside files — only filenames), `edit_file` (whole-file rewrites made anything over ~10 KB uneditable), and `start_process` / `check_process` / `stop_process` (nothing an agent started could outlive the tool call, so a dev server was impossible and "does it run?" was unreachable).

Also: tool output truncation now keeps error lines first — a failing build previously showed the banner and the summary and hid the errors; the turn cap **pauses** instead of reporting failure; the model call retries on transient errors; and an unparseable reply retries instead of being treated as `is_done: true`, which used to end tasks as successes.

*Verified live:* an agent wrote a file, searched it, edited it, read it back and self-verified in six turns.

### Agents are confined to a workspace
Nine tools resolved paths with no containment check, defaulting to the entire user profile. Every one now routes through `AgentWorkspace.Resolve`; each task gets `%LOCALAPPDATA%\Metis\agents\workspace\<taskId>\` unless the user picks a folder, and picking a folder is what grants access outside it.

### A browser the user can watch
Playwright driving the **installed Chrome** (falling back to Edge, then bundled Chromium). Headed, with a banner injected into the page that survives navigation and is excluded from what the agent reads and clicks. Tools: `browser_open`, `browser_read`, `browser_click`, `browser_type`.

**Hand-over gate:** at a login, sign-up, payment page or CAPTCHA the agent stops and gives the browser to the user. It will not enter passwords or card details, and CAPTCHAs are handed over rather than solved. *Verified live against `github.com/login`:* typing was refused with `handover=SignIn`, and an ordinary page produced no false positive.

The agent prompt now also states that anything a tool returns — page text, file contents, command output — is information and never an instruction, because this agent reads the open web while holding `execute_powershell` and `delete_file`.

### Notifications now arrive
They had never worked. The process set an AppUserModelID but no Start Menu shortcut carried it, so Windows dropped every toast, and the failure was swallowed into a `Debug.WriteLine`. Registration is now repaired from the app (so existing installs heal, not only new ones) and only when actually broken. Toasts use `ToastGeneric` and carry working **Approve / Deny / View** buttons.

*Verified live:* `Notifications: Windows notifications are enabled.` in the log, and the shortcut is left untouched on subsequent launches.

### Voice fixed
Speech had been pointed at `gemini-2.0-flash` — a text model that cannot produce audio and which now returns 404 — with three more dead models as fallbacks, and a normaliser that replaced any *working* TTS model with the broken one. Now uses `gemini-2.5-flash-preview-tts`, and a stale saved setting is corrected on load.

### The agent panel shows what happened
Artifacts are listed and open on click (previously unreachable from the UI entirely). An approval names the tool, its arguments and its risk level instead of asking the user to allow something unspecified. Plus duration, origin, working directory, a Clear button for finished tasks, cached/frozen brushes, and a Retry that replaces the failed attempt rather than leaving it in the list forever.

### Teaching
Walkthroughs check the screen between steps and re-point once if the step has not happened, instead of advancing purely on a timer. It never blocks: anything unreadable falls back to the previous behaviour. And when Metis cannot confirm what it is being asked about, it looks the control up through Windows and says it cannot see it, rather than marking a confident wrong spot.

### What's New window
`src/Metis.App/Windows/WhatsNewWindow.xaml(.cs)` — shown once after an update, never on a first install. Keyed on the new `AppSettings.LastSeenVersion`.

---

## 3. Publishing

```bash
git add -A
git commit -m "Metis v3.13.0: agents that finish the job, a browser you can watch, working notifications"
git push metis feat/launch-website
```

Then a GitHub release on `Martinhaleluja/Metis` tagged **`v3.13.0`**, with `Metis-Setup-3.13.0-win-x64.exe` attached. The asset name matters: `UpdateService` looks for `Metis-Setup-*.exe`, and the tag is what the version comparison reads.

Release notes: the ten entries in `WhatsNewWindow.Changes` are already written for a user rather than for a developer, and can be lifted directly.

---

## 4. Please check before publishing

Claude verified the items marked *verified live* above by running the app. These are the ones it did **not** confirm, and they are worth a look rather than a trust:

1. **The installer has not been built at 3.13.0.** Only `dotnet publish` was run. Build it and install it once on a clean machine.
2. **First-run notification registration.** On a brand-new install the Start Menu shortcut is created microseconds before Windows is asked for a notifier, and Windows may not have indexed it yet — the first launch can silently have no toasts, and the second is fine. The code retries lazily on first use, which should cover it, but it has only been observed on this machine.
3. **The What's New window on a real update.** It was rendered in isolation and looks right; it has not been seen appearing after an actual in-place upgrade.
4. **The browser banner's Stop button is not clickable.** The banner is `pointer-events:none` so the agent cannot press its own controls — which also blocks the user. Stopping works from the drawer's Cancel. This should be fixed by making only the Stop control interactive.
5. **`AccountWindow.xaml` references `{StaticResource SurfaceBrush}`, which is not defined anywhere in the theme.** Pre-existing, not from this release, but a `StaticResource` miss throws at load — that window may be broken. Worth confirming.
6. **Playwright adds ~13 MB to the install** and, on a machine with neither Chrome nor Edge, wants a Chromium download. The bundled Chromium also needs the Visual C++ runtime; without it the only symptom is `spawn UNKNOWN`. The channel fallback avoids this wherever Chrome or Edge exists.

---

## 5. Known limits, stated plainly

- Agents attach to the **Chrome application**, not to an existing signed-in profile — so they start logged out. Attaching to a live profile needs Chrome relaunched with a debugging port and is not implemented.
- The agent list has no Active/History tabs, search or date grouping. Items can now be cleared individually.
- The spawn panel still cannot set per-task autonomy or turn budget, though `SpawnTask` supports both.
- `NotchAgentDrawer` and `NotchSpawnAgentPanel` still carry separate, slightly divergent colour palettes.
- The free Supabase project pauses after 7 days idle. The offline grace period covers it for signed-in users; account features degrade until it wakes.
