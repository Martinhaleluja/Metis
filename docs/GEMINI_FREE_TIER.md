# Gemini free-tier behavior

Lulu's core ask/reply path uses the standard Gemini `generateContent` HTTPS API. It does not require Gemini Live.

- Default reasoning model: `gemini-3.5-flash`
- Automatic fallback preference: current Flash/Flash Lite models exposed by the same key
- Voice input: a locally recorded WAV is sent as inline `audio/wav`
- Screen context: the complete virtual desktop is resized and sent as a compact inline JPEG (`image/jpeg`)
- Speech output: a separate, optional TTS request; failure never removes the text answer

Google controls which models and free quotas are available to each project, key, region, and date. Lulu therefore discovers `generateContent` models from the Models API and lets the user test one or all of them with the same key instead of assuming every listed model will work.

The API key is sent only in the `x-goog-api-key` request header. It is stored in Windows Credential Manager and is excluded from settings, model results, diagnostics, and request URLs.

Official references:

- https://ai.google.dev/gemini-api/docs/models
- https://ai.google.dev/gemini-api/docs/generate-content/audio
- https://ai.google.dev/gemini-api/docs/generate-content/speech-generation
- https://ai.google.dev/gemini-api/docs/pricing
