using Metis.Core.Services;
using Metis.Windows;

namespace Metis.Tests;

/// <summary>
/// The wake word is matched against transcribed speech, not audio, and a
/// transcriber given one short name in isolation gets it wrong constantly.
/// Requiring an exact match means the user says the name and nothing happens,
/// which is the failure that makes people abandon a wake word — so matching is
/// deliberately loose, and these cases pin how loose.
/// </summary>
public sealed class WakeWordListenerTests
{
    [Theory]
    [InlineData("Metis open my email")]
    [InlineData("metis open my email")]
    [InlineData("Metis, open my email")]
    [InlineData("METIS open my email")]
    public void The_name_is_heard_however_it_was_written(string transcript)
    {
        var match = WakeWordListener.Listen(transcript, "Metis");

        Assert.True(match.Heard);
        Assert.Equal("open my email", match.Request);
    }

    /// <summary>
    /// What a transcriber actually returns for a short proper noun it does not
    /// know. Each of these is one or two letters from the real thing.
    /// </summary>
    [Theory]
    [InlineData("meetis")]
    [InlineData("metus")]
    [InlineData("medis")]
    [InlineData("mettis")]
    public void A_near_miss_still_counts(string heard) =>
        Assert.True(WakeWordListener.Listen($"{heard} what is this", "Metis").Heard);

    [Theory]
    [InlineData("tennis what is this")]
    [InlineData("notice what is this")]
    [InlineData("what is this")]
    [InlineData("")]
    public void Ordinary_speech_does_not_wake_it(string transcript) =>
        Assert.False(WakeWordListener.Listen(transcript, "Metis").Heard);

    /// <summary>
    /// Saying only the name is a normal way to start — the user is about to ask
    /// something. Metis has to know it was called without inventing a request.
    /// </summary>
    [Fact]
    public void The_name_on_its_own_wakes_it_with_nothing_to_do()
    {
        var match = WakeWordListener.Listen("Metis?", "Metis");

        Assert.True(match.Heard);
        Assert.Empty(match.Request);
        Assert.False(match.HasRequest);
    }

    /// <summary>
    /// Continuous listening transcribes overlapping stretches, so an earlier
    /// mention can be one that was already answered. The most recent is the one
    /// the user is waiting on.
    /// </summary>
    [Fact]
    public void The_most_recent_call_is_the_one_that_counts()
    {
        var match = WakeWordListener.Listen("Metis open email and then Metis close it", "Metis");

        Assert.Equal("close it", match.Request);
    }

    [Fact]
    public void A_wake_word_of_several_words_works()
    {
        var match = WakeWordListener.Listen("hey computer what is this", "hey computer");

        Assert.True(match.Heard);
        Assert.Equal("what is this", match.Request);
    }

    /// <summary>
    /// A name this short has too little information to allow edits without
    /// matching half the language.
    /// </summary>
    [Fact]
    public void A_very_short_name_must_be_exact()
    {
        Assert.True(WakeWordListener.Listen("ada open email", "ada").Heard);
        Assert.False(WakeWordListener.Listen("aida open email", "ada").Heard);
    }

    [Theory]
    [InlineData(null, "Metis")]
    [InlineData("", "Metis")]
    [InlineData("   ", "Metis")]
    [InlineData("Jarvis", "Jarvis")]
    [InlineData("  Jarvis  ", "Jarvis")]
    public void An_unusable_wake_word_falls_back_to_the_default(string? configured, string expected) =>
        Assert.Equal(expected, WakeWordListener.Normalize(configured));

    [Fact]
    public void An_absurdly_long_wake_word_falls_back() =>
        Assert.Equal("Metis", WakeWordListener.Normalize(new string('a', 60)));

    [Fact]
    public void An_empty_setting_still_listens_for_the_default_name() =>
        Assert.True(WakeWordListener.Listen("Metis what is this", null).Heard);
}

/// <summary>
/// Ctrl+Space toggles listening. The thing that must not happen is the key
/// repeat Windows sends while a key is held switching it on and off dozens of
/// times a second.
/// </summary>
public sealed class ActiveListeningKeyStateTests
{
    private const uint Space = ActiveListeningKeyState.Space;

    [Fact]
    public void Control_and_space_toggles_once() =>
        Assert.True(new ActiveListeningKeyState().Update(Space, isDown: true, controlHeld: true));

    [Fact]
    public void Holding_the_chord_does_not_toggle_repeatedly()
    {
        var state = new ActiveListeningKeyState();

        Assert.True(state.Update(Space, true, true));
        Assert.False(state.Update(Space, true, true));
        Assert.False(state.Update(Space, true, true));
    }

    [Fact]
    public void Releasing_and_pressing_again_toggles_again()
    {
        var state = new ActiveListeningKeyState();
        Assert.True(state.Update(Space, true, true));

        state.Update(Space, false, true);

        Assert.True(state.Update(Space, true, true));
    }

    [Fact]
    public void Space_on_its_own_is_left_alone() =>
        Assert.False(new ActiveListeningKeyState().Update(Space, isDown: true, controlHeld: false));

    /// <summary>
    /// The bug this class was rewritten for. Ctrl is asked of Windows at the
    /// moment Space arrives, so a Ctrl key-up the hook never saw cannot turn
    /// every space in a sentence into a toggle.
    /// </summary>
    [Fact]
    public void Typing_a_sentence_after_the_chord_does_not_keep_toggling()
    {
        var state = new ActiveListeningKeyState();
        Assert.True(state.Update(Space, true, controlHeld: true));
        state.Update(Space, false, controlHeld: true);

        // Ctrl is now genuinely up, whatever any earlier hook callback implied.
        foreach (var _ in Enumerable.Range(0, 6))
        {
            Assert.False(state.Update(Space, true, controlHeld: false));
            state.Update(Space, false, controlHeld: false);
        }
    }

    [Fact]
    public void Reset_forgets_a_held_chord()
    {
        var state = new ActiveListeningKeyState();
        state.Update(Space, true, true);

        state.Reset();

        Assert.True(state.Update(Space, true, true));
    }
}
