# Metis

An AI companion for your computer.

Metis is a Windows-native desktop companion rebuilt with C# and .NET 8/WPF.

Metis lives in the tray, follows the cursor as a vector companion, records while `Ctrl+Shift+1` is held, captures the complete virtual desktop, and sends the request to the selected reasoning provider. For screen-aware requests, the provider can return a small structured desktop plan that moves Metis independently and performs compatible cursorless actions.

Reasoning, transcription, and spoken output are selected independently:

- **Gemini** keeps the free-first Google AI Studio flow.
- **OpenAI** uses the OpenAI Platform Responses, transcription, and speech APIs. OpenAI API billing is separate from ChatGPT Plus.
- **Claude** uses Anthropic's Messages API for reasoning and screen understanding.
- **OpenClaw** connects to a self-hosted OpenClaw Gateway and uses it as Metis's agent/orchestration layer.
- **Ollama** runs an installed local model through Ollama's native API.
- **Automatic** tries configured cloud providers in the order Gemini, OpenAI, then Claude.
- **AssemblyAI** is an optional speech-to-text provider for voice requests.
- **ElevenLabs** is an optional text-to-speech provider for Metis's voice.

## Development

Requirements:

- Windows 10 19041 or newer
- .NET 8 SDK
- At least one configured reasoning provider; OpenClaw and Ollama can run locally without a cloud API key

```powershell
dotnet restore Metis.sln
dotnet build Metis.sln -c Debug
dotnet test Metis.sln -c Debug
dotnet run --project src/Metis.App/Metis.App.csproj
```

For a Windows build that does not require .NET to be installed on the target PC:

```powershell
.\scripts\build.ps1 -Configuration Release -Publish
```

The self-contained `Metis.exe` is written to `artifacts\win-x64`.

API keys are stored as separate entries in Windows Credential Manager. Settings and diagnostic logs live under `%LOCALAPPDATA%\Metis`; secret values are never written to settings or logs.

## Provider setup

- Select **Claude**, save an Anthropic API key, and use **Test**. The default model is `claude-sonnet-5`; the model field remains editable.
- Select **OpenClaw**, enter the Gateway address (default `http://127.0.0.1:18789`), and optionally save its bearer token. Metis accepts plain HTTP only for a loopback address; remote gateways must use HTTPS.
- Select **Ollama**, enter the Ollama address (default `http://127.0.0.1:11434`) and the exact name of a model already installed with Ollama. A vision-capable model is required for screenshot understanding.
- Under **Speech to text**, choose **AssemblyAI** and save its key when Claude, OpenClaw, or Ollama should handle push-to-talk requests. Gemini and OpenAI can keep using their native recording path.
- Under **Text to speech**, choose **ElevenLabs**, save its key, enter a model and voice ID, then select **Test**. Metis verifies the account, loads available voices, and fills the first voice ID when the field is empty.
- For an offline voice, choose **Piper**. Metis expects the standalone Piper binary at `tools\piper-standalone\piper\piper.exe` rather than a Python virtualenv, since a venv breaks as soon as its interpreter or its original folder moves. Download `piper_windows_amd64.zip` from the Piper releases page and extract it there.

The provider Test buttons make live requests and display authentication, model, quota, endpoint, network, and service errors directly in Setup.

## OpenAI setup

1. Create an API key at `https://platform.openai.com/api-keys`.
2. Enable API billing separately from ChatGPT Plus and set the spending controls you want.
3. Open Metis Setup, paste the key into **OpenAI Platform API key**, and choose **Test**.
4. Select **OpenAI** to use it directly or **Automatic** to keep Gemini as the primary provider.

The defaults use `gpt-5-mini` for reasoning and screenshots, `gpt-4o-mini-transcribe` for push-to-talk transcription, and `tts-1` for spoken responses. Every field is editable so models can be changed without rebuilding Metis.

## How Metis helps

Metis is a learning instrument. You do the work; it teaches while you do it — explaining what to do and why, pointing at the control, drawing over the screen to show you where to look. It never clicks, types, or moves the pointer for you, and there is no setting that lets it.

That is the whole design rather than one option among several. Metis had modes once, up to and including one that operated the computer on your behalf; they were removed, because a tool that does the task for you cannot also be the thing that teaches you to do it.

Metis remembers what you have already learned, per application, and shortens its guidance as your skill level rises. Memory is structured data at `%LOCALAPPDATA%\Metis\memory.json`, holds no screen content, and can be erased with **Clear memory** in Setup.

## Desktop assistance

- Hold `Ctrl+Alt` while another app is active and ask about what you see, then release the keys.
- Hold `Ctrl+Alt+Shift` and point at a control to ask about that exact element — "what does this do?", "why is this red?". Metis resolves "this" from the UI element under your pointer rather than from the window as a whole.
- Hold `Ctrl+Shift+1` while another app is active, ask Metis to point to or click a visible control, then release the keys.
- When visual guidance is enabled, Metis draws a temporary click-through highlight, arrow, and numbered step over the real control. The marks expire on their own and never alter the application underneath.
- A short pop plays when the microphone opens and a woosh when the request is sent, so you know Metis heard you without looking away from your work. Both are synthesised in code, and the woosh only fires once a recording is long enough to be real.
- To use your own sounds, drop WAV or MP3 files into the `sound effects` folder beside the executable and name them after the moment they mark: `app started`, `audio recording started`, `inspect keys pressed`, `inspect keys released`, `request sent`, `task complete`, `saved settings`, `stop metis`, and `error`. Numbered variants such as `error 1` and `error 2` are picked at random without repeating the previous one. Matching is by keyword, so case, numbering, and separators do not matter. Files must be under 6 seconds; anything missing falls back to a built-in cue or stays silent.
- Errors are also spoken aloud, shortened to one sentence, using the offline Piper voice. That is deliberate: the failures most worth hearing are the ones where the cloud provider is unreachable or out of quota, and a cloud voice would fail for the same reason.
- Metis captures the complete virtual desktop across every monitor, preserves its original bounds, and sends a compact JPEG to the selected vision model.
- The inference side returns bounded structured JSON. All actions are written to a bounded `Channel<T>` immediately and a single action worker executes them in order, independently from provider and speech latency.
- Every action must contain normalized `x` and `y` coordinates relative to the whole virtual desktop. FlaUI UIA3 first invokes the control under Metis; unsupported controls receive background window messages.
- Metis can move independently, request background hover, left-click, double-click, right-click, and wait without moving the physical Windows pointer. Each response is limited to six actions.
- Normal spoken replies animate the companion instead of showing the full answer. A short white cue such as **Press here** appears only when visual guidance is useful.
- During a screen action, Metis temporarily detaches from the mouse pointer, glides to the target control, and places the cue on the side with available screen space. Five seconds after the completed action, the cue closes and Metis smoothly returns to normal cursor-following mode.
- Each planned step gets its own small vector worker shape. Green means the step completed; red means it failed.
- Companion error colors distinguish general, connection, authentication, quota, and Windows automation failures.
- Metis does not automatically click purchases, deletion, sending/submission, credentials, security, privacy, permission, administrator, or similar high-impact controls. It can point to those controls and leaves the final click to the user.
- Press **F12** at any time for the emergency stop. The low-level keyboard hook cancels the active action, drains the queued actions, and stops the current request. A new explicit voice request starts a fresh automation session.

Desktop actions require **Capture the entire desktop when I ask** to be enabled. Screen-aware typed prompts and push-to-talk requests both receive current desktop context.

Coordinates are normalized to the whole captured virtual desktop rather than the primary monitor. This preserves accuracy on secondary monitors, mixed-DPI desktops, and monitors with negative Windows coordinates. Some applications do not support cursorless UI Automation or background mouse messages; Metis reports that limitation instead of taking control of the real pointer. Metis does not attempt to bypass protected applications, elevated windows, anti-cheat systems, or game input protections.
