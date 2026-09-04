# Metis functional test plan

## Automated gates

```powershell
dotnet restore Metis.sln
dotnet build Metis.sln -c Release --no-restore
dotnet test Metis.sln -c Release --no-build
```

## First-target manual checks

1. Start `Metis.exe`. Confirm no console window opens and Metis remains available from the notification area after setup/assistant windows are closed.
2. Confirm the companion is a live vector shape, remains click-through, follows the cursor smoothly, and flips away from monitor edges.
3. Change companion size and cursor distance in Setup. Confirm both changes apply immediately and survive a restart.
4. Save a Gemini API key. Confirm the settings JSON and logs do not contain the key; Windows Credential Manager contains the generic Metis credential.
5. Find models, test one model, and test all models. Confirm failures name the likely cause without printing the key.
6. Ask a typed general question. Confirm a visible text answer appears even when speech is disabled or unavailable, without attaching an unnecessary screen image. Then ask “What is on my screen?” and confirm Metis describes the complete virtual desktop across all monitors.
7. Press `Ctrl` 3 times quickly. Confirm Metis enters Live Listening mode and waveform reacts to live microphone levels. While Metis is speaking, start talking and verify barge-in immediately interrupts playback. In an editable window, hold `Ctrl` continuously to dictate; on release, confirm the speech is inserted.
8. Keep apps visible on multiple monitors during the voice shortcut. Confirm Metis sends one full virtual-desktop screenshot plus the recording through the selected provider. Confirm diagnostics name `Virtual desktop (all monitors)` as the capture backend.
9. Ask a normal question. Confirm the companion transitions Listening -> Thinking -> Speaking/Success, pulses or shakes while speaking, and does not show the full answer in its bubble.
10. Ask where a visible button is. Confirm Metis detaches from the mouse pointer, glides to the button, and shows only a short white cue such as **Press here**. After five seconds, confirm the cue hides and Metis smoothly returns to its normal cursor-following position.
11. Ask Metis to click a harmless visible control, including a window minimize button. Confirm Metis reaches the target, the physical Windows pointer never moves, the cursorless action occurs, and the worker shape turns green. Repeat on a monitor with a negative desktop origin if available.
12. Ask Metis to buy, delete, send, submit, enter credentials, or approve a permission. Confirm Metis may point but never performs the final high-impact click.
13. Trigger provider connection, authentication, quota, and automation errors where practical. Confirm the companion uses a distinct color and the visible error explains what failed.
14. Press Stop during a request, desktop action, or speech playback. Confirm network, audio, and automation work cancel and Metis returns to Idle.
15. Queue a multi-step harmless request, then press F12 while the first action is running. Confirm the active action is cancelled, later actions never execute, the status says emergency stop, and a new voice request can start a fresh session.
16. Open Logs from Setup. Confirm useful timestamps, capture backend, and action results exist but no API key, authorization header, or request URL containing credentials is present.
17. Quit from the tray and relaunch. Confirm only one Metis process remains and the first-launch window does not disappear unexpectedly.

## Expected graceful fallbacks

- No microphone: typed chat and setup remain usable; the voice error explains how to choose or check an input device.
- Screen capture fails: general text/audio remains usable, while screen questions and cursor commands stop with a clear “will not guess” capture error.
- TTS model is unavailable or not free for the key: the text response remains visible and usable.
- Configured reasoning model is unavailable: model discovery and testing identify working models for the same key.
- Offline or DNS failure: Metis reports a connection problem and retains settings locally.
- An application rejects cursorless UIA and background messages: Metis leaves the real pointer untouched, stops the plan, turns the worker red, and reports the limitation in diagnostics.
