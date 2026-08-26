namespace Metis.Core.Models;

/// <summary>
/// What a model costs the user to run.
/// </summary>
public enum ModelTier
{
    /// <summary>Usable on the provider's free tier, within its rate limits.</summary>
    Free,

    /// <summary>Billed per token. Needs a provider account with billing enabled.</summary>
    Paid,

    /// <summary>Runs on the user's own machine. No key, no quota, no network.</summary>
    Local
}

/// <summary>
/// One model a user can pick, with what they need to decide between them.
///
/// The limits are what the provider publishes for its free tier, and they
/// change without notice. They are shown as guidance rather than as a promise,
/// and where the provider reports a real figure — as Gemini does for context
/// windows — the live value replaces the one recorded here.
/// </summary>
public sealed record ModelOption(
    string Provider,
    string Id,
    string DisplayName,
    ModelTier Tier,

    /// <summary>Context window in tokens, or 0 when unknown.</summary>
    int ContextTokens = 0,

    /// <summary>Requests per minute on the free tier, or 0 when not published.</summary>
    int RequestsPerMinute = 0,

    /// <summary>Requests per day on the free tier, or 0 when not published.</summary>
    int RequestsPerDay = 0,

    /// <summary>True when the model can read a screenshot. Metis needs this.</summary>
    bool SupportsVision = true,

    string? Note = null)
{
    /// <summary>
    /// A short label for the picker: "Free · 1M context · 250/day".
    /// </summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>
            {
                Tier switch
                {
                    ModelTier.Free => "Free",
                    ModelTier.Local => "On this PC",
                    _ => "Paid"
                }
            };

            if (ContextTokens > 0)
            {
                parts.Add(ContextTokens >= 1_000_000
                    ? $"{ContextTokens / 1_000_000}M context"
                    : $"{ContextTokens / 1000}K context");
            }

            if (RequestsPerDay > 0)
            {
                parts.Add($"{RequestsPerDay}/day");
            }

            if (!SupportsVision)
            {
                parts.Add("no screenshots");
            }

            return string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// Every model Metis can be pointed at, across every provider.
///
/// Kept as data rather than scattered through the settings window so the list
/// is one edit to extend, and so the free ones can be marked as such. That
/// marking is the point: a user choosing a model is choosing what it will cost
/// them, and a picker that hides which entries are billed is one that bills
/// people by accident.
///
/// Where Google still publishes a free-tier limit — it does for the 2.5 line,
/// and points at AI Studio for the 3.x line — the entry records it. An
/// unknown allowance is left at zero and simply not shown, rather than guessed
/// at, and <c>Merge</c> exists to let the live model list correct the rest at
/// runtime.
/// </summary>
public static class ModelCatalog
{
    private const int Million = 1_000_000;

    public static IReadOnlyList<string> GeminiVoices { get; } =
    [
        "Kore",
        "Puck",
        "Charon",
        "Fenrir",
        "Aoede"
    ];

    /// <summary>
    /// The model Metis speaks with when nothing else is chosen. Named here so
    /// the one place that decides it is not four hardcoded strings across Setup,
    /// Preferences and the runtime, which is how a wrong one spread last time.
    /// </summary>
    public const string DefaultGeminiSpeechModel = "gemini-2.5-flash-preview-tts";

    /// <summary>
    /// Gemini models that can genuinely synthesise speech.
    ///
    /// This list held gemini-2.0-flash, gemini-2.5-flash and gemini-2.0-flash-exp,
    /// described as audio generators. None of them can produce audio — they are
    /// text models — and two have since been withdrawn and answer 404. Every
    /// entry below was verified against the live API and returns 24 kHz mono PCM.
    ///
    /// Google puts "tts" in the name of every speech model and in no others,
    /// which is the test the provider uses to reject a wrong one.
    /// </summary>
    public static IReadOnlyList<ModelOption> GeminiSpeechModels { get; } =
    [
        new("Gemini", "gemini-2.5-flash-preview-tts", "Gemini 2.5 Flash TTS", ModelTier.Free, 8_192,
            Note: "Natural, quick, and available on an ordinary API key. The default."),
        new("Gemini", "gemini-3.1-flash-tts-preview", "Gemini 3.1 Flash TTS", ModelTier.Free, 8_192,
            Note: "Newer voice model. Slightly warmer delivery."),
        new("Gemini", "gemini-2.5-pro-preview-tts", "Gemini 2.5 Pro TTS", ModelTier.Paid, 8_192,
            Note: "Most expressive, and the first to run out of quota on a free key.")
    ];

    public static IReadOnlyList<ModelOption> Gemini { get; } =
    [
        new("Gemini", "gemini-3.1-pro-preview", "Gemini 3.1 Pro", ModelTier.Paid, Million,
            Note: "The most capable. No free tier at all — every request is billed."),
        new("Gemini", "gemini-3.7-flash", "Gemini 3.7 Flash", ModelTier.Free, 1_048_576,
            Note: "The newest Flash, and the best balance for Metis."),
        new("Gemini", "gemini-3.6-flash", "Gemini 3.6 Flash", ModelTier.Free, 1_048_576,
            Note: "High performance multimodal Flash model."),
        new("Gemini", "gemini-3.5-flash", "Gemini 3.5 Flash", ModelTier.Free, 1_048_576),
        new("Gemini", "gemini-3.5-flash-lite", "Gemini 3.5 Flash-Lite", ModelTier.Free, 1_048_576,
            Note: "Quicker and cheaper than Flash. Less accurate on cluttered screens."),
        new("Gemini", "gemini-3.1-flash-lite", "Gemini 3.1 Flash-Lite", ModelTier.Free, 1_048_576),

        // Gemma is Google's open model served through the same Gemini key, and
        // is free of charge even on a billed account.
        new("Gemini", "gemma-4-31b-it", "Gemma 4 31B", ModelTier.Free, 256_000,
            Note: "Open model, free of charge on the same Gemini key."),
        new("Gemini", "gemma-4-26b-a4b-it", "Gemma 4 26B", ModelTier.Free, 256_000,
            Note: "A lighter Gemma 4, free on the same Gemini key."),

        // The 1.5, 2.0 and 2.5 generations were all offered here until every one
        // of them started answering 404: Google withdrew them. Listing a model
        // the user can select and then cannot use is worse than a shorter list,
        // because the failure arrives later and looks like a broken app rather
        // than a retired model.
    ];

    public static IReadOnlyList<ModelOption> OpenAi { get; } =
    [
        new("OpenAI", "gpt-5", "GPT-5", ModelTier.Paid, 400_000),
        new("OpenAI", "gpt-5-mini", "GPT-5 mini", ModelTier.Paid, 400_000),
        new("OpenAI", "gpt-4.1", "GPT-4.1", ModelTier.Paid, Million),
        new("OpenAI", "gpt-4.1-mini", "GPT-4.1 mini", ModelTier.Paid, Million)
    ];

    public static IReadOnlyList<ModelOption> Claude { get; } =
    [
        new("Claude", "claude-sonnet-5", "Claude Sonnet 5", ModelTier.Paid, 200_000),
        new("Claude", "claude-opus-5", "Claude Opus 5", ModelTier.Paid, 200_000),
        new("Claude", "claude-haiku-4-5-20251001", "Claude Haiku 4.5", ModelTier.Paid, 200_000)
    ];

    /// <summary>
    /// OpenRouter's free-tier models. The ":free" suffix is the provider's own
    /// marking, so a model without it is billed and is shown that way.
    /// </summary>
    public static IReadOnlyList<ModelOption> OpenRouter { get; } =
    [
        new("OpenRouter", "google/gemma-4-31b-it:free", "Gemma 4 31B (free)", ModelTier.Free, 256_000),
        new("OpenRouter", "google/gemini-2.0-flash-exp:free", "Gemini 2.0 Flash (free)", ModelTier.Free, Million),
        new("OpenRouter", "meta-llama/llama-4-scout:free", "Llama 4 Scout (free)", ModelTier.Free, 128_000),
        new("OpenRouter", "qwen/qwen2.5-vl-72b-instruct:free", "Qwen2.5 VL 72B (free)", ModelTier.Free, 128_000),
        new("OpenRouter", "anthropic/claude-sonnet-4.5", "Claude Sonnet 4.5", ModelTier.Paid, 200_000),
        new("OpenRouter", "openai/gpt-5", "GPT-5", ModelTier.Paid, 400_000)
    ];

    /// <summary>
    /// Models that run on the user's own machine. Free in the sense that
    /// matters most — no key, no quota, and nothing leaves the computer.
    /// </summary>
    public static IReadOnlyList<ModelOption> Ollama { get; } =
    [
        new("Ollama", "qwen3-vl:2b-instruct-q4_K_M", "Qwen3 VL 2B", ModelTier.Local, 32_000,
            Note: "Small and quick. The default; runs on modest hardware."),
        new("Ollama", "qwen2.5vl:7b", "Qwen2.5 VL 7B", ModelTier.Local, 128_000,
            Note: "More accurate. Wants a decent graphics card."),
        new("Ollama", "llama3.2-vision:11b", "Llama 3.2 Vision 11B", ModelTier.Local, 128_000),
        new("Ollama", "llava:13b", "LLaVA 13B", ModelTier.Local, 32_000),
        new("Ollama", "gemma3:4b", "Gemma 3 4B", ModelTier.Local, 128_000)
    ];

    /// <summary>Every model, for the provider currently selected.</summary>
    public static IReadOnlyList<ModelOption> For(string? provider) => provider?.Trim() switch
    {
        "OpenAI" => OpenAi,
        "Claude" => Claude,
        "OpenRouter" => OpenRouter,
        "Ollama" => Ollama,
        _ => Gemini
    };

    /// <summary>Every model Metis knows about, whatever the provider.</summary>
    public static IReadOnlyList<ModelOption> All { get; } =
        Gemini.Concat(OpenAi).Concat(Claude).Concat(OpenRouter).Concat(Ollama).ToArray();

    /// <summary>
    /// Folds the provider's live model list into the catalogue.
    ///
    /// A model the provider reports but this file has never heard of is added
    /// rather than hidden, marked Paid, because assuming a stranger is free is
    /// the guess that costs the user money. Known models keep their curated
    /// name and limits, and take the live context window if one was reported.
    /// </summary>
    public static IReadOnlyList<ModelOption> Merge(
        string provider,
        IEnumerable<(string Id, string? DisplayName, int ContextTokens)> live)
    {
        ArgumentNullException.ThrowIfNull(live);
        var known = For(provider).ToDictionary(model => model.Id, StringComparer.OrdinalIgnoreCase);
        var merged = new List<ModelOption>();

        foreach (var (id, displayName, contextTokens) in live)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (known.TryGetValue(id, out var curated))
            {
                merged.Add(contextTokens > 0 ? curated with { ContextTokens = contextTokens } : curated);
                known.Remove(id);
                continue;
            }

            merged.Add(new ModelOption(
                provider,
                id,
                string.IsNullOrWhiteSpace(displayName) ? id : displayName!,
                ModelTier.Paid,
                contextTokens,
                Note: "Reported by the provider. Check its pricing before relying on it."));
        }

        // Anything curated but not reported stays listed: a provider that omits
        // a model from one response has not necessarily withdrawn it.
        merged.AddRange(known.Values);
        return merged;
    }
}
