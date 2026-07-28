# Lulu

Lulu is a Windows-native desktop companion rebuilt with C# and .NET 8/WPF.

Lulu lives in the tray, follows the cursor as a vector companion, records while `Ctrl+Shift+1` is held, captures the complete virtual desktop, and sends the request to the selected reasoning provider. For screen-aware requests, the provider can return a small structured desktop plan that moves Lulu independently and performs compatible cursorless actions.

Reasoning, transcription, and spoken output are selected independently:

- **Gemini** keeps the free-first Google AI Studio flow.
- **OpenAI** uses the OpenAI Platform Responses, transcription, and speech APIs. OpenAI API billing is separate from ChatGPT Plus.
- **Claude** uses Anthropic's Messages API for reasoning and screen understanding.
- **OpenClaw** connects to a self-hosted OpenClaw Gateway and uses it as Lulu's agent/orchestration layer.
- **Ollama** runs an installed local model through Ollama's native API.
- **Automatic** tries configured cloud providers in the order Gemini, OpenAI, then Claude.
- **AssemblyAI** is an optional speech-to-text provider for voice requests.
- **ElevenLabs** is an optional text-to-speech provider for Lulu's voice.

## Development

Requirements:

- Windows 10 19041 or newer
- .NET 8 SDK
- At least one configured reasoning provider; OpenClaw and Ollama can run locally without a cloud API key

```powershell
dotnet restore Lulu.sln
dotnet build Lulu.sln -c Debug
dotnet test Lulu.sln -c Debug
dotnet run --project src/Lulu.App/Lulu.App.csproj
```

For a Windows build that does not require .NET to be installed on the target PC:

```powershell
.\scripts\build.ps1 -Configuration Release -Publish
```

The self-contained `Lulu.exe` is written to `artifacts\win-x64`.

API keys are stored as separate entries in Windows Credential Manager. Settings and diagnostic logs live under `%LOCALAPPDATA%\Lulu`; secret values are never written to settings or logs.

## Provider setup

- Select **Claude**, save an Anthropic API key, and use **Test**. The default model is `claude-sonnet-5`; the model field remains editable.
- Select **OpenClaw**, enter the Gateway address (default `http://127.0.0.1:18789`), and optionally save its bearer token. Lulu accepts plain HTTP only for a loopback address; remote gateways must use HTTPS.
- Select **Ollama**, enter the Ollama address (default `http://127.0.0.1:11434`) and the exact name of a model already installed with Ollama. A vision-capable model is required for screenshot understanding.
- Under **Speech to text**, choose **AssemblyAI** and save its key when Claude, OpenClaw, or Ollama should handle push-to-talk requests. Gemini and OpenAI can keep using their native recording path.
- Under **Text to speech**, choose **ElevenLabs**, save its key, enter a model and voice ID, then select **Test**. Lulu verifies the account, loads available voices, and fills the first voice ID when the field is empty.

The provider Test buttons make live requests and display authentication, model, quota, endpoint, network, and service errors directly in Setup.

## OpenAI setup

1. Create an API key at `https://platform.openai.com/api-keys`.
2. Enable API billing separately from ChatGPT Plus and set the spending controls you want.
3. Open Lulu Setup, paste the key into **OpenAI Platform API key**, and choose **Test**.
4. Select **OpenAI** to use it directly or **Automatic** to keep Gemini as the primary provider.

The defaults use `gpt-5-mini` for reasoning and screenshots, `gpt-4o-mini-transcribe` for push-to-talk transcription, and `tts-1` for spoken responses. Every field is editable so models can be changed without rebuilding Lulu.

## Desktop assistance

- Hold `Ctrl+Shift+1` while another app is active, ask Lulu to point to or click a visible control, then release the keys.
- Lulu captures the complete virtual desktop across every monitor, preserves its original bounds, and sends a compact JPEG to the selected vision model.
- The inference side returns bounded structured JSON. All actions are written to a bounded `Channel<T>` immediately and a single action worker executes them in order, independently from provider and speech latency.
- Every action must contain normalized `x` and `y` coordinates relative to the whole virtual desktop. FlaUI UIA3 first invokes the control under Lulu; unsupported controls receive background window messages.
- Lulu can move independently, request background hover, left-click, double-click, right-click, and wait without moving the physical Windows pointer. Each response is limited to six actions.
- Normal spoken replies animate the companion instead of showing the full answer. A short white cue such as **Press here** appears only when visual guidance is useful.
- During a screen action, Lulu temporarily detaches from the mouse pointer, glides to the target control, and places the cue on the side with available screen space. Five seconds after the completed action, the cue closes and Lulu smoothly returns to normal cursor-following mode.
- Each planned step gets its own small vector worker shape. Green means the step completed; red means it failed.
- Companion error colors distinguish general, connection, authentication, quota, and Windows automation failures.
- Lulu does not automatically click purchases, deletion, sending/submission, credentials, security, privacy, permission, administrator, or similar high-impact controls. It can point to those controls and leaves the final click to the user.
- Press **F12** at any time for the emergency stop. The low-level keyboard hook cancels the active action, drains the queued actions, and stops the current request. A new explicit voice request starts a fresh automation session.

Desktop actions require **Capture the entire desktop when I ask** to be enabled. Screen-aware typed prompts and push-to-talk requests both receive current desktop context.

Coordinates are normalized to the whole captured virtual desktop rather than the primary monitor. This preserves accuracy on secondary monitors, mixed-DPI desktops, and monitors with negative Windows coordinates. Some applications do not support cursorless UI Automation or background mouse messages; Lulu reports that limitation instead of taking control of the real pointer. Lulu does not attempt to bypass protected applications, elevated windows, anti-cheat systems, or game input protections.
