# Completed work — and what is left

A live checklist for this stretch of work, kept current as each piece lands
rather than written at the end. It exists so another assistant (Antigravity)
can pick the work up without being re-briefed: read this file, then start at
**What to do next**.

Branch: `fix/ui-account-ai-and-redesign` (off `main`).
Approved plan: `C:\Users\halel\.claude\plans\i-noticed-metis-ui-wise-bentley.md`.

---

## The reports this came from

Twelve symptoms, reported together after a fresh install:

1. Onboarding never appeared on first install.
2. "Metis is already running" whenever the shortcut was clicked.
3. Could not read all of onboarding; its details were out of date.
4. The UI "goes out of line" and breaks when Account is opened.
5. Scrollbars at the edge in a UI that should fit itself.
6. UI elements cut off.
7. The companion could not be changed in Settings.
8. The default companion should be the other character, not the cursor.
9. The companion switch did not take effect on save.
10. No way to tell whether dictation works.
11. The AI refused every question, even on Free.
12. The website showed a different account than the app.

Plus: move the website back to a modern look (keeping the doodles), give the
notch real glass, and write this file.

---

## Done

### 1. First run, single instance, onboarding — `e6e6d4a`

- **`--background` no longer skips onboarding.** It returned from
  `App.xaml.cs` *before* auth and onboarding were wired, and `--background` is
  what the Run key uses at login — so anyone who had ticked "start Metis when I
  sign in" got a Metis that had never introduced itself. It now suppresses only
  the surfaces that assume someone is watching; a genuine first run overrides it.
- **A second launch hands itself to the running copy** instead of showing a
  message box (`src/Metis.App/Services/SingleInstance.cs`, new). If the holder
  is wedged — which swallowed dispatcher exceptions make possible — it says so
  in the log and starts anyway, because a duplicate beats a program that cannot
  be opened.
- **Uninstall cleans up** (`installer/Metis.iss`): the Run key goes via
  `uninsdeletevalue`, and an opt-in prompt (defaulting to *keep*) removes
  `%LOCALAPPDATA%\Metis` and the Credential Manager session. A reinstall is now
  genuinely a fresh install.

### 2. Notch layout — `e6e6d4a`

Reports 4, 5 and 6 were one bug in three places.

- Deleted `MaxHeight="520"` from `NotchSettings.xaml` — the last copy of the
  number `NotchGeometry` was written to remove. It capped every section on every
  monitor *and* reported 520 as its desired height, so the shell could never
  discover there was more to show.
- Dropped the nested `ScrollViewer`; the shell's single `PageScroll` scrolls.
- Removed `Width="640"` from the `NotchSettings` root, which had defeated the
  shell's re-measure.
- **`NotchGeometry` now owns the horizontal axis** as it already owned the
  vertical. The window width was a chain of per-page tests that omitted first
  run, so the 640pt welcome wizard was drawn in a 560pt window. There is no
  chain left to forget a page from.

**Verified by running it.** At rest the window is 128pt wide (was 560) — it hugs
the pill. Settings reaches `target=741 max=741` in the app's own log, where the
520 cap previously pinned it at 543. One scrollbar, not two. All ten sections
render without clipping.

### 3. Companion — `e6e6d4a`

- Default is now **Blob**, not Cursor (`CompanionShapes.cs`). A companion shaped
  like the pointer you are already moving reads as your cursor having gone wrong.
- **Character picker added** to the Companion settings page. Both forms have
  existed since the companion did, but the only picker was in
  `PreferencesWindow`, which the app built and never showed.
- **`PreferencesWindow` deleted** — unreachable, and carrying a duplicate
  companion UI that would keep drifting. Its markup tests had been passing
  against a file no user could open (the second time that has happened here);
  they now point at `NotchSettings`.
- `ApplySettings` now calls `ApplyPresence`, so the "keep it on screen" switch
  takes effect on save instead of at the next state change.

**Verified by running it:** picker selected, saved, `companionShape = Blob`
persisted to `settings.json`, "Saved at 16:12." shown, process healthy.

### 4. Dictation — `84d7e9b`

- The three voice handlers hardcoded *"Add a Gemini API key in Setup"* whatever
  the real reason was. They now use `CanAnswer`, which already produced the
  right sentence and was sitting unused.
- `TranscribeAsync` branched on AssemblyAI and **fell through to whisper.cpp for
  everything else**, silently making "Native" mean "Whisper.cpp" on the
  continuous-listening path — and whisper.cpp is not in the installer payload,
  so the default provider failed on every segment. Native now says what it is up
  front; it genuinely cannot do this, because the wake word must be found in
  text before any turn starts.
- The pre-flight check tested `!SpeechEnabled` (whether spoken *responses* play
  — unrelated), so anyone with responses on walked past it into the failing loop.
- **The Voice page had no speech-to-text settings at all.** Added the provider
  selector, AssemblyAI key + model, whisper paths, and a **Test dictation**
  button that records three seconds and shows what came back.

### 5. AI refusal and account identity — `92ca246`, `74eb65f`

- **Every non-2xx from Google became one sentence.** An expired key, a spent
  quota, a withdrawn model and an outage all read identically. The gateway now
  classifies the status (`src/Metis.Api/ProviderFailures.cs`, tested) and logs
  Google's own words server-side, where they cannot leak the Cloud project or
  key prefix they sometimes carry.
- The kind is load-bearing, not just wording: a **plan** refusal must never fall
  back to the user's own key, but a **provider** fault is exactly when it should.
- **Boot-time key check.** A broken `GOOGLE_API_KEY` looked like a healthy
  deploy — `/health` answered 200 and every turn failed. One request at startup
  now puts the answer in the deploy log, without blocking startup.
- **`Account.Email` was never populated.** The address was in the sign-in
  response and dropped; `LoadAccountAsync` (which reads only role/plan/verified)
  overwrote it. `MetisAccount.WithIdentityFrom` merges the two. Verified: the
  log now reports "address present".
- **`SignOut` left the profile behind**, so the next person to sign in adopted
  the previous one's name and avatar. It now clears them.
- **"Manage on Web" was a plain link.** The app and site keep separate Supabase
  sessions, so it opened whichever account the browser last used. It now carries
  a single-use token (`POST /v1/web-session` then `redeemDesktopHandoff` on the
  site), minted only for the address behind the caller's own verified token —
  taking an email as a parameter would have made it an account-takeover
  endpoint. It rides in the URL fragment, so it never reaches a server log or a
  `Referer`, and is spent and stripped before anything else runs.

**Tests:** 1368 passing (1247 `Metis.Tests` + 121 `Metis.ApiTests`).

---

## Not done, and why

### Blocked on you

- **Steps 6 and 7 — notch glass and the website's modern redesign.** You chose
  to authorise the Figma connector first, so these are waiting on that. Nothing
  else depends on them.
- **The gateway's `GOOGLE_API_KEY`.** If the new boot check reports it refused,
  that is a value in the Render dashboard and cannot be changed from here.
  Deploy this branch and read the startup log — it will name which of the four
  cases it is.

### Deliberately left alone

- **The dictation meter reads zero and always will.** `usage_events` has no
  duration column and `RecordUsageAsync` writes no seconds, so
  `dictation_seconds` can never move. `ManagedAccess.Decide`'s `isDictation`
  branch and the 300-minute Free allowance on the pricing page are both wired to
  nothing. Passing `isDictation: true` alone would leave the meter at zero *and*
  exempt those turns from the talk cap — a billing loophole, not a fix. **This
  needs a product decision:** what counts as dictation, given there is currently
  no feature distinct from asking a question by voice?
- **Bundling whisper.cpp** (20 MB plus a 75 MB model) in the installer. The
  Voice page now lets a user point at their own copy; bundling is a 95 MB
  installer decision that is yours to make.
- **A general settings-write race.** `SaveSettingsAsync` assigns `Settings` only
  after its file write completes, so two saves close together can clobber each
  other. It bit the email persistence (worked around — see `74eb65f`) and could
  bite anything else. Fixing it properly means assigning before the IO, which
  trades a lost write for possible in-memory/disk divergence on failure.

---

## What to do next

1. **Deploy the gateway** from this branch and read the startup log for the
   `GOOGLE_API_KEY` verdict. This is the single highest-value action left —
   report 11 (the AI refusing everything) is very likely a bad key, and the app
   changes only make the reason *visible*; they cannot fix the key itself.
2. **Once Figma is authorised**, do Step 6 (notch acrylic — note it depends on
   the window now hugging its content, which step 2 delivered) and Step 7
   (website: delete `index.css:160-342`, rewrite `Nav.tsx` and `Footer.tsx`,
   recolour `RetroBackground.tsx`'s 45 doodles rather than removing them, and
   replace the ~116 raw hex literals with the tokens that already exist).
3. **Test the web handoff end to end** — it is written and builds, but has not
   been exercised against a live gateway: sign in on the app, click "Manage on
   Web", confirm the browser lands as the same account.

## How to verify anything here

```powershell
dotnet test Metis.sln -c Debug
```

The app must be verified by **running** it — its most fragile code (layered
windows, z-order, the keyboard hook, animations) is what tests cannot reach:

```powershell
dotnet publish src/Metis.App/Metis.App.csproj -c Debug -r win-x64 --self-contained false -o artifacts/win-x64
```

Then launch `artifacts\win-x64\Metis.exe` and read
`%LOCALAPPDATA%\Metis\logs\metis.log`, which reports notch geometry directly.

**Screenshots do not work on this app** — it calls `KeepOutOfScreenCaptures`
(`WDA_EXCLUDEFROMCAPTURE`), so `CopyFromScreen` captures whatever is *behind*
the notch. Drive and inspect it through **UI Automation** instead
(`System.Windows.Automation`), which reads real element bounds. Two gotchas
found the hard way: the notch's 2.2-second retract timer fires in the gaps
between separate tool calls, so hover-and-click must happen inside **one**
script; and `BoundingRectangle` is unreliable for elements inside the notch's
`ClipToBounds` containers, so drive by re-querying fresh coordinates
immediately before each click.
