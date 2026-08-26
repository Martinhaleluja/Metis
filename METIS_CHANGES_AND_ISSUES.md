# Metis Development Handoff: Changes & Tester Reports

This document provides a comprehensive summary of all bugs, issues, and feature requests reported by users and testers, along with the detailed code changes implemented in version **3.7.3 - 3.7.7**.

> **Correction (later pass).** Five of the six fixes below were verified as genuinely working. The sixth — the voice model change — did not fix voice; it broke it, for every user, by pointing speech at models that cannot produce audio. Items 6 and F and guideline 3 have been corrected in place, and the reasoning is left visible rather than deleted so the same mistake is not made again. 

Use this document to catch up Claude on what has been accomplished, how the codebase works, and what to keep in mind for future maintenance.

---

## 1. Summary of Issues Reported by Users & Testers

Here are the key issues that testers and users repeatedly reported, and the current state of their fixes:

1. **App Crashes on Voice Input (Normal Chat & Agent modes):**
   * *Report:* The app crashed instantly as soon as a user finished recording a voice question.
   * *Root Cause:* NAudio's recording stopped callback ran on a background ThreadPool thread and disposed of the WAV writer. The writer closed the underlying `MemoryStream`, but the code immediately accessed `.Length` on the closed stream, raising a `System.ObjectDisposedException`. Since it occurred on a background thread pool thread, it bypassed standard UI try-catch handlers and crashed the entire process.
   * *Status:* **Fixed** (v3.7.5).

2. **Invisible Response Text in Notch Chat (Light Theme):**
   * *Report:* Response bubbles in the notch chat area were empty or had invisible text.
   * *Root Cause:* Bubble borders set `TextBlock.Foreground="{StaticResource ChatInk}"`. However, because `Controls.xaml` defines a global implicit style targeting all `TextBlock` controls with `Foreground="{DynamicResource TextBrush}"`, WPF's style precedence rule overrode the inherited `ChatInk` property. In Light theme, `TextBrush` is black/dark, drawing black text on a dark-grey bubble background.
   * *Status:* **Fixed** (v3.7.6).

3. **App Seems Freeze / "Offline" Status Stuck:**
   * *Report:* Testers complained that the app seemed frozen or offline even when the computer was online.
   * *Root Cause:* When launching a lesson or walkthrough guide, the runtime entered `RunLessonAsync`. The status bar was never updated during the lesson and was never reset upon completion, leaving the status indefinitely stuck on `"Thinking..."` or `"Preparing voice..."`.
   * *Status:* **Fixed** (v3.7.6).

4. **Voice Fails Silently without Error Messages:**
   * *Report:* When voice synthesis failed, the app showed the text bubble but did not play audio, and did not explain why.
   * *Root Cause:* Synthesis exceptions were caught but immediately overwritten by subsequent activity transitions.
   * *Status:* **Fixed** (v3.7.6).

5. **Typed Questions Get No Voice Responses:**
   * *Report:* If a user typed a question, Metis never spoke the response even if "Enable voice responses" was checked.
   * *Root Cause:* The runtime hardcoded voice responses to only play if the question itself was spoken: `var speakReply = Settings.SpeechEnabled && activation != ActivationKind.Typed;`.
   * *Status:* **Fixed** (v3.7.7).

6. **Gemini Voice Models Unavailable / API Key Restrictions:**
   * *Report:* Metis failed voice generation, complaining that selected models (like `gemini-2.5-flash` or experimental versions) were unavailable to the user's API key.
   * *Root Cause (as first diagnosed):* Google AI Studio keys have different model access tiers depending on the region/project.
   * *Actual root cause:* the models being asked to speak could not speak. `gemini-2.0-flash`, `gemini-2.0-flash-exp` and `gemini-2.5-flash` are text models; asking any of them for `responseModalities: ["AUDIO"]` cannot succeed, and two of the three have since been withdrawn and answer HTTP 404. The first attempt failed, all three fallbacks failed, and the normaliser replaced any *working* speech model the user chose with a text one — so the setting could not be fixed by hand either.
   * *Status:* **Broken by the v3.7.7 change; fixed properly afterwards.** Voice now uses real text-to-speech models (`gemini-2.5-flash-preview-tts` by default, falling back to `gemini-3.1-flash-tts-preview` then `gemini-2.5-pro-preview-tts`). Verified against the live API: all three return 24 kHz mono PCM. Existing installs self-heal, because settings normalisation now rewrites a non-speech model on load.

---

## 2. Complete Log of Changes Made

### A. Voice Recording Crash Fix
* **File modified:** [`src/Metis.Windows/WaveAudioRecorder.cs`](file:///c:/Users/halel/Documents/Lulu/src/Metis.Windows/WaveAudioRecorder.cs)
* **Changes:**
  * Implemented a nested helper class `IgnoreDisposeStream` that inherits from `Stream` and wraps the raw `MemoryStream` but ignores any calls to `.Dispose()` or `.Close()`.
  * Instantiated NAudio's `WaveFileWriter` wrapping the buffer in the ignore-dispose wrapper. When NAudio disposes the writer to finalize the WAV headers, the underlying stream is kept open for reading.
  * Wrapped the recording callbacks (`WaveIn_OnDataAvailable` and `WaveIn_OnRecordingStopped`) in try-catch blocks to prevent background thread pool exceptions from taking down the process.

### B. Notch Chat Color Priority Fix
* **File modified:** [`src/Metis.App/Windows/NotchChat.xaml`](file:///c:/Users/halel/Documents/Lulu/src/Metis.App/Windows/NotchChat.xaml)
* **Changes:**
  * Set the `Foreground` property directly on the response `TextBlock` locally (`Foreground="{StaticResource ChatInk}"`) to override the implicit global style.
  * Assigned `x:Name="BubbleText"` to the text control.
  * Updated triggers in `DataTemplate.Triggers` to explicitly set `BubbleText`'s `Foreground` to `White` (for user messages) or `ChatDangerInk` (for problem messages) when triggered, maintaining full theme compatibility.

### C. Stuck Status & Voice Error Reporting
* **File modified:** [`src/Metis.App/Runtime/MetisRuntime.cs`](file:///c:/Users/halel/Documents/Lulu/src/Metis.App/Runtime/MetisRuntime.cs)
* **Changes:**
  * Updated the loop in `RunLessonAsync` to report real-time status updates: `SetStatus($"Step {lesson.StepNumber} of {lesson.Steps.Count}: {step.Instruction}");`.
  * Added a `finally` block to `RunLessonAsync` to clean up the companion and reset the status bar to its ready state when the guide completes.
  * Added a private string field `_lastVoiceError` to track speech failures. If voice synthesis fails, we preserve the exception message and append it to the status bar (e.g. `Ready — voice was unavailable: [error details]`).

### D. Always-On Voice Responses
* **File modified:** [`src/Metis.App/Runtime/MetisRuntime.cs`](file:///c:/Users/halel/Documents/Lulu/src/Metis.App/Runtime/MetisRuntime.cs)
* **Changes:**
  * Changed the turn speak condition from:
    `var speakReply = Settings.SpeechEnabled && activation != ActivationKind.Typed;`
    to:
    `var speakReply = Settings.SpeechEnabled;`
    This ensures typed queries also trigger text-to-speech if voice is enabled.

### E. Labeled AssemblyAI Dropdown
* **Files modified:**
  * [`src/Metis.App/Windows/SetupWindow.xaml`](file:///c:/Users/halel/Documents/Lulu/src/Metis.App/Windows/SetupWindow.xaml)
  * [`src/Metis.App/Windows/PreferencesWindow.xaml`](file:///c:/Users/halel/Documents/Lulu/src/Metis.App/Windows/PreferencesWindow.xaml)
* **Changes:**
  * Replaced the text input boxes for the AssemblyAI model with editable ComboBox dropdowns (`IsEditable="True"`).
  * Pre-populated them with standard AssemblyAI transcription models:
    * `best` (Best Accuracy - Standard/Paid)
    * `nano` (Nano Model - Low Cost/Free Tier friendly)
    * `universal-2` (Universal-2 - Legacy)

### F. Removed Voice Model Selector & Hardcoded Stable Default
* **Files modified:**
  * [`src/Metis.AI/GeminiProvider.cs`](file:///c:/Users/halel/Documents/Lulu/src/Metis.AI/GeminiProvider.cs)
  * [`src/Metis.App/Windows/SetupWindow.xaml` & `.xaml.cs`](file:///c:/Users/halel/Documents/Lulu/src/Metis.App/Windows/SetupWindow.xaml.cs)
  * [`src/Metis.App/Windows/PreferencesWindow.xaml` & `.xaml.cs`](file:///c:/Users/halel/Documents/Lulu/src/Metis.App/Windows/PreferencesWindow.xaml.cs)
  * [`tests/Metis.Tests/SetupWindowMarkupTests.cs`](file:///c:/Users/halel/Documents/Lulu/tests/Metis.Tests/SetupWindowMarkupTests.cs)
  * [`tests/Metis.Tests/VoicePlaybackTests.cs`](file:///c:/Users/halel/Documents/Lulu/tests/Metis.Tests/VoicePlaybackTests.cs)
* **Changes:**
  * ~~Modified `NormalizeSpeechModel` in `GeminiProvider.cs` to unconditionally return `"gemini-2.0-flash"`.~~ **Reverted.** That model cannot synthesise speech and no longer exists; this is what silenced voice.
  * Removed the "Gemini speech model" ComboBox and its label from both Setup and Preferences screens. *(Left removed — the default now works, so there is nothing to configure around.)*
  * ~~Hardcoded all voice configuration loading/saving code-behind logic to use `"gemini-2.0-flash"`.~~ **Reverted.** Setup and Preferences were writing that literal back into `settings.json` on every save, which is how the dead model spread to every install. They now carry the saved value through.
  * ~~Updated unit tests to align with the forced stable model output.~~ **Reverted.** Those tests had been rewritten to assert the defect — one was named `Deprecated_tts_model_name_normalizes_to_stable_flash` and asserted that a working speech model gets replaced by a text one. They now pin the rule the right way round, and `GeminiSpeechModelTests` covers the upgrade path.

---

## 3. Important Guidelines for Claude in Future Development

When working on future voice, audio, or UI updates, please keep these rules and patterns in mind:

1. **WPF Style Precedence:**
   If you add or style text or border elements in chat bubbles or windows, remember that the implicit styles defined in `Theme/Controls.xaml` have high precedence. To override them (e.g. for light theme compatibility), you must set local values directly on the element (like `Foreground="{StaticResource InkColor}"`), or name the elements and set their properties directly in template triggers.

2. **NAudio Callback Threading:**
   Always keep NAudio recording callbacks guarded by try-catch blocks, and never dispose of underlying streams that are still needed for post-cleanup checks. Keep the custom `IgnoreDisposeStream` wrapper intact when feeding streams to `WaveFileWriter`.

3. **Gemini speech models — the rule that actually matters:**
   Only a model with `tts` in its name can synthesise speech. Google names every text-to-speech model that way and names no other model that way, which is the test `NormalizeSpeechModel` uses. A speech fallback list may contain **only** speech models: a list of text models means the first request fails and then every fallback fails too, and the only symptom is silence — the build compiles, the request is well-formed, and nothing throws anywhere visible.

   Do not hardcode a model id in `SetupWindow`, `PreferencesWindow` or `MetisRuntime`. Use `ModelCatalog.DefaultGeminiSpeechModel`, which is the single place that decides it.

   Before changing any model id, check it against the live API rather than reasoning about it. `GET https://generativelanguage.googleapis.com/v1beta/models?key=…` lists what the key can reach, and a real synthesis call is the only proof that a model can speak. Model availability changes underneath this project: `gemini-2.0-flash` and `gemini-2.5-flash` both answer 404 now.
