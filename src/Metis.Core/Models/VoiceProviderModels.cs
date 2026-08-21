namespace Metis.Core.Models;

public sealed record TranscriptionResult(
    string Text,
    string Provider,
    string Model,
    TimeSpan Duration);

public sealed record SpeechVoiceInfo(
    string Id,
    string Name,
    string? Category = null,
    string? Description = null);

