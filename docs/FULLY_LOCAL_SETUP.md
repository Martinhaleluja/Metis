# Metis fully local profile

This profile keeps reasoning, screenshots, transcription, and speech on the Windows PC.

## Runtime choices

- Reasoning and vision: Ollama with `qwen3-vl:2b-instruct-q4_K_M` on 8 GB PCs. The 4.3 GB `gemma4:e2b-it-qat` model needs substantially more free memory once its vision and audio projectors are loaded.
- Context: 2048 tokens on an 8 GB PC (Metis clamps local settings to 2048-4096)
- Screen: complete virtual desktop, resized to a maximum of 1280x720 and encoded as quality-68 JPEG
- Speech-to-text: `whisper.cpp` with `ggml-tiny.bin`
- Text-to-speech: Piper by default
- Optional text-to-speech: a loopback-only OpenAI-compatible Chatterbox-Nano server

## Expected local paths

Paths may be absolute or relative to `Metis.exe`. The preset uses:

```text
tools\whisper.cpp\Release\whisper-cli.exe
models\whisper\ggml-tiny.bin
tools\piper-standalone\piper\piper.exe
models\piper\en_US-lessac-medium.onnx
```

The Piper model's matching `.onnx.json` file must be beside the `.onnx` file.

## Setup sequence

1. Install Ollama for Windows and run `ollama pull qwen3-vl:2b-instruct-q4_K_M` on an 8 GB PC.
2. Install/build whisper.cpp, place `whisper-cli.exe` at the configured path, and download the multilingual Tiny GGML model as `ggml-tiny.bin`.
3. Extract the standalone `piper_windows_amd64.zip` build and an English voice model. Point Metis to the Piper executable and `.onnx` voice. Use the standalone binary rather than a Python virtualenv, which breaks as soon as its interpreter or original folder moves.
4. Open Metis Setup, choose Ollama, then click **Use fully local preset**.
5. Use the Ollama, whisper.cpp, and Piper **Test** buttons. Each missing component reports its exact path or connection error.
6. Save setup.

Chatterbox-Nano is optional. Run an OpenAI-compatible local server and select **Chatterbox-Nano** for text-to-speech. Metis rejects non-loopback Chatterbox addresses so this mode cannot silently send speech text to another computer.
