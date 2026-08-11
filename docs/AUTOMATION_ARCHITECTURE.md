# Metis automation architecture

Metis uses a bounded, event-driven sense-plan-act pipeline. It samples the screen only for an explicit screen-aware typed or push-to-talk request; it does not continuously stream the desktop to an AI provider.

## Sense

`VirtualDesktopCaptureService` captures the complete Windows virtual desktop, including every monitor and negative desktop origins, into one image. It preserves the original desktop bounds for coordinate mapping. Cloud providers receive a copy scaled to at most 2560x1440 at JPEG quality 80; the fully local Ollama profile uses a 1280x720 quality-68 JPEG to reduce Gemma visual processing while retaining the whole desktop.

`FlaUiAutomationService` independently reads a bounded desktop accessibility snapshot. Up to 120 useful non-Metis elements are included, with name, control type, Automation ID, enabled state, and coordinates normalized to the virtual desktop.

## Plan

Each reasoning provider receives explicit screenshot MIME type and dimensions, optional audio, and the accessibility snapshot. Independent AssemblyAI transcription starts concurrently with screen capture. Providers must return strict JSON containing `screen_observed`, `spoken_text`, an optional short `bubble_cue`, and no more than six actions. Every non-wait action must contain normalized capture-relative `x` and `y`; an `automation_id` is supplemental and never replaces the coordinate fallback.

`AssistantPlanParser` treats provider output as untrusted. It validates action names, clamps coordinates and waits, limits text and IDs, removes every pointer action without both coordinates, and blocks sensitive click intents. The runtime refuses screen answers when no capture exists, rejects ungrounded `screen_observed: false` plans, and shows a specific error when an action request returns no usable coordinates.

## Act

`DesktopAutomationPipeline` writes the complete plan to a bounded `Channel<T>`. Inference and speech remain asynchronous while one channel reader preserves input order. Each action carries the originating screenshot bounds and cancellation token.

`DesktopAutomationService` first uses UI Automation, then cursorless window messages, and finally full Windows input when the first two mechanisms are rejected. This lets Metis control taskbar and modern application surfaces. The input layer also executes validated `type_text`, `key_press`, `open_app`, and HTTP(S)-only `open_url` actions. Full-control pointer movement is restored after five seconds unless the user moves the pointer first.

## Emergency stop

The existing `WH_KEYBOARD_LL` hook also watches F12. The first F12 key-down is swallowed and immediately cancels the session token, completes the channel writer, drains queued work, and cancels the active request. UI and audio cleanup is scheduled off the hook callback so the Windows hook returns promptly. A later explicit request creates a fresh action session.

## Mode policy

Between Plan and Act, `MetisRuntime.ApplyModeAndSafety` filters the returned plan against the active operating mode and `SafetyPolicyEngine`. Non-mutating steps survive in every mode; mutating steps require a mode that permits them, and high-risk intent is withheld in all four modes. The same `ModePolicy` definition also produces the mode block in the system instruction, so the prompt and the enforcement cannot disagree. The filter is re-applied to every closed-loop replan. See `OPERATING_MODES.md`.

## Grounding on replans

The first turn and every replan must confirm `screen_observed` before their
actions run. An unconfirmed replan ends the loop instead of throwing: the steps
already executed were grounded and verified, so discarding only the unconfirmed
next batch keeps that work while still refusing to act on a screen the provider
will not say it inspected. Throwing reported the whole task as failed and lost
the completed steps with it.

The replan prompt states that a fresh screenshot is attached and that
`screen_observed` must therefore be true, because a replan that silently
answered `false` stalled every multi-step task after its first action.

## Boundaries

- Screen and accessibility content never grants permission; only the user's request does.
- The operating mode is a user setting. Nothing a provider returns can change it or widen what it allows.
- Purchases, deletion, submission/sending, credentials, permissions, security, and administrator controls are not auto-clicked.
- Metis does not bypass UIPI, elevated-window boundaries, anti-cheat software, protected content, or game input protections.
- Coordinates are relative to the entire captured virtual desktop, not the primary monitor, so every monitor shares one safe mapping.
