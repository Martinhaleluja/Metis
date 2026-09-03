# Changelog

All notable changes to Metis are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and Metis uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added — plans, and an AI gateway that pays for two of them

- **Three plans: Free at $0, Pro at $20, Max at $50.** The middle tier was
  called Plus while it was being built; it is Pro now, and the top one is Max.
  Nothing is on sale until `billing_state.billing_is_live` says otherwise, and
  that switch is a row update rather than a release.
- **The plans meter what Metis pays a provider for, never what the software can
  do.** Anyone running Metis on their own API key keeps screen vision,
  automation and agents, on Free, signed out, for as long as they like. Those
  requests never reach a Metis server, so there is nothing for a plan to apply
  to. The rule lives in one function, `ProviderRouting.Decide`, above every
  question about accounts and billing.
- **Metis can answer on its own AI** for people who have not brought a key.
  Prompt, screenshot and all, through the gateway, streamed back as it arrives.
- **Checkout.** The website can take money: the gateway creates the payment
  session so the processor's token and the product ids never reach a browser,
  and the account it is for comes from the signed-in token rather than from the
  request, because a checkout that will accept somebody else's account id is a
  way to move somebody else's plan.
- **Bring your own provider on Max.** Connect an OpenAI, Anthropic, Gemini,
  Mistral or OpenRouter account from the website; the key is tested against the
  provider, stored encrypted in Supabase Vault, and never returned to a browser.
  That provider bills you for model usage, separately from the $50.
- **An Account & plan page in the notch**, with what the plan includes and how
  much of the month's included AI is left. Anything that costs money opens the
  website, which is where a payment page belongs. Entitlements refresh on a
  timer and when the page is opened, so buying a plan on the website no longer
  needs the app restarted before it arrives.
- **A plan banner in the notch** for the refusals that are not faults — an
  allowance spent, or something a plan does not cover — and cues for both.
- **Cost protection**: an emergency switch that degrades or refuses managed AI
  when it costs more than Metis can carry. Bring-your-own and local models are
  unaffected by construction rather than by exception.

### Added — running it in production

- **The cold start no longer looks like a hang.** The gateway sleeps after
  fifteen minutes on a free instance and takes the better part of a minute to
  wake. The entitlement refresh used a twenty second timeout, so it gave up
  every time and carried on against a stale cached plan without saying so.
  Timeouts now sit above the wake, waking calls retry with backoff on 502, 503
  and 504 only, and a call that has not answered in three seconds says so in
  the notch through the channels the interface already listens on.
- **Get Help & Support and Report a Bug**, in the tray menu. Both open a mail
  draft with the version, the Windows build, whether the gateway answered and
  the plan already written in — and nothing else. No key, no conversation, no
  screenshot, no access token, and the block is fenced and announced so the
  person can read it and delete it before sending.
- **Crash reporting**, off unless `METIS_SENTRY_DSN` is set, which it is on no
  build that ships. What makes it safe to turn on is the scrubbing rather than
  the sampling: the rule lives in `SecretRedaction` with tests, matches key
  shapes rather than a list of known secrets, and takes the user's name out of
  every path.
- **Updates are verified again.** The updater compares the download against a
  SHA-256 from the release notes, and v3.15.0 shipped without one, so the check
  was being skipped. GitHub's own digest is now the fallback when nobody pasted
  a hash.
- **`docs/OPERATIONS.md`** — the free-tier map, the keep-alive and the
  instance-hour arithmetic behind it, the Resend SMTP setup, and the two SQL
  brakes. **`docs/CODE_SIGNING.md`** — honest that there is no free signing
  route for a proprietary app from Namibia today, and what to do instead.

### Fixed — things that were quietly wrong

- **The terms promised the plans were free.** `billing_is_live` is true and a
  subscription has been taken, but the terms still said no plan was on sale and
  that bringing your own AI key was free for everyone. Now corrected, with the
  merchant-of-record clause naming Polar, a liability cap, an AI-output
  disclaimer, and every processor named with what it receives.
- **`my_usage_this_period()` threw on every call.** It declared four columns
  after `usage_this_period` grew a fifth, so the account page's usage meters
  had been in their error branch for every signed-in user.
- **The top plan was refused six capabilities it had paid for**, and bringing
  your own key was granted to the middle one. `my_feature` was still asking
  about plan names that the remap had removed.
- **The pricing page could never show a buy button.** It reads whether billing
  is live signed out, and the row was readable only by authenticated users, so
  it concluded the shop was shut whatever the database said.
- **Anyone could read anyone else's usage** — the function took the account as
  an argument and was callable by anonymous visitors.

### Changed

- **`PRIVACY.md` now says where screen content goes per route, and the claim
  that Metis has no server in the middle no longer stands unqualified.** It is
  still true on your own key, on Pro's connected account, and for local models.
  It is not true for the AI Metis pays for, and saying so plainly is the only
  honest version.
- The website gains pricing, providers, a bring-your-own explainer, an FAQ,
  sign-in, an account page and legal pages — all in the same Windows 95 chrome.
- Provider errors gain a `PlanLimited` kind, so "your plan is small" stops being
  reported as "your credential is wrong" and sending people to replace a working
  key.
- Metis never falls back to your own API key after its own AI refuses on plan or
  allowance grounds. Spending someone's money because Metis ran out of its own is
  not a thing to do quietly.

### Removed

- The Account window, which referenced a brush no theme defined — so opening it
  threw and the tray entry silently did nothing. Preferences supersedes it.
- `SetupWindow`, 1,950 lines that nothing had constructed since Preferences
  replaced it. Its markup tests now check the window people actually open.

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
