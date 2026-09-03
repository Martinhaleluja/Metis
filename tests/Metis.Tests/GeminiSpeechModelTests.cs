using Metis.Core.Models;

namespace Metis.Tests;

/// <summary>
/// Metis was silent for every user because the model it asked to speak could
/// not speak.
///
/// The setting was hardcoded to a text model, the fallback list held three more
/// text models, and the normaliser actively replaced a real speech model with a
/// text one — so the first attempt failed, all three fallbacks failed, and
/// anyone who set it correctly by hand had it overwritten on the next save.
/// Two of those models have since been withdrawn entirely and answer 404.
///
/// Nothing about that failure was visible from a passing build: the code
/// compiled, the request was well-formed, and the only symptom was silence.
/// These tests exist so it cannot come back the same way.
/// </summary>
public sealed class GeminiSpeechModelTests
{
    [Fact]
    public void The_default_speech_model_is_one_that_can_speak()
    {
        Assert.Contains("tts", ModelCatalog.DefaultGeminiSpeechModel, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ModelCatalog.DefaultGeminiSpeechModel, new AppSettings().SpeechModel);
    }

    [Theory]
    [InlineData("gemini-2.0-flash")]
    [InlineData("gemini-2.0-flash-exp")]
    [InlineData("gemini-2.5-flash")]
    [InlineData("gemini-3.6-flash")]
    [InlineData("")]
    [InlineData(null)]
    public void A_model_that_cannot_speak_is_replaced_on_load(string? stored)
    {
        // This is the upgrade path that matters: copies of Metis in the field
        // have a text model saved in settings.json, so a new default alone
        // would leave every one of them silent.
        var settings = new AppSettings { SpeechModel = stored! }.Normalize();

        Assert.Contains("tts", settings.SpeechModel, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("gemini-2.5-flash-preview-tts")]
    [InlineData("gemini-3.1-flash-tts-preview")]
    [InlineData("gemini-2.5-pro-preview-tts")]
    public void A_real_speech_model_is_left_alone(string stored)
    {
        // The old rule ran the other way round and threw these away.
        var settings = new AppSettings { SpeechModel = stored }.Normalize();

        Assert.Equal(stored, settings.SpeechModel);
    }

    [Fact]
    public void Normalising_twice_changes_nothing_the_second_time()
    {
        var once = new AppSettings { SpeechModel = "gemini-2.0-flash" }.Normalize();
        var twice = once.Normalize();

        Assert.Equal(once.SpeechModel, twice.SpeechModel);
    }

    [Fact]
    public void Every_offered_speech_model_can_actually_speak()
    {
        Assert.NotEmpty(ModelCatalog.GeminiSpeechModels);
        Assert.All(
            ModelCatalog.GeminiSpeechModels,
            model => Assert.Contains("tts", model.Id, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_default_is_one_of_the_offered_models_and_is_free()
    {
        var match = Assert.Single(
            ModelCatalog.GeminiSpeechModels,
            model => model.Id == ModelCatalog.DefaultGeminiSpeechModel);

        // A paid default would fail on the free key most testers have.
        Assert.Equal(ModelTier.Free, match.Tier);
    }
}
