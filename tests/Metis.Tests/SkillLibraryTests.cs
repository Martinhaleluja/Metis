using Metis.Core.Models;
using Metis.Core.Services;

namespace Metis.Tests;

public sealed class SkillLibraryTests
{
    private const string BlenderSkill = """
        # Blender basics
        description: Where Blender hides its modelling tools
        applies-to: blender, .blend

        Tab switches between Object Mode and Edit Mode.
        The N panel on the right holds transform values.
        """;

    [Fact]
    public void A_skill_file_is_parsed_into_name_description_and_body()
    {
        var skill = SkillLibrary.Parse("blender.md", BlenderSkill)!;

        Assert.Equal("Blender basics", skill.Name);
        Assert.Equal("Where Blender hides its modelling tools", skill.Description);
        Assert.Contains("Object Mode", skill.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("applies-to:", skill.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_skill_fires_on_a_real_window_title()
    {
        var skill = SkillLibrary.Parse("blender.md", BlenderSkill)!;

        // The user writes "blender"; the window says something far messier.
        Assert.True(skill.Matches("untitled.blend - Blender 4.2", null));
        Assert.True(skill.Matches(null, "how do I extrude in blender"));
        Assert.False(skill.Matches("Microsoft Excel", "sum a column"));
    }

    [Fact]
    public void Without_an_applies_to_line_the_name_becomes_the_trigger()
    {
        var skill = SkillLibrary.Parse("FL Studio.md", "How the mixer routing works.")!;

        Assert.Equal("FL Studio", skill.Name);
        Assert.True(skill.Matches("FL Studio 21 - project.flp", null));
    }

    [Fact]
    public void A_file_with_no_body_is_not_a_skill() =>
        Assert.Null(SkillLibrary.Parse("empty.md", "# Title only\ndescription: nothing here"));

    [Fact]
    public void A_runaway_skill_file_is_truncated_so_it_cannot_crowd_out_the_screen()
    {
        var huge = "# Big\n\n" + new string('x', SkillLibrary.MaxSkillCharacters * 2);

        var skill = SkillLibrary.Parse("big.md", huge)!;

        Assert.True(skill.Content.Length <= SkillLibrary.MaxSkillCharacters + 2);
    }

    [Fact]
    public void Only_a_few_skills_load_for_one_turn()
    {
        var skills = Enumerable.Range(0, 8)
            .Select(index => SkillLibrary.Parse($"excel{index}.md", $"# Excel {index}\napplies-to: excel\n\nBody {index}")!)
            .ToArray();

        var selected = SkillLibrary.Select(skills, "Microsoft Excel", "sum a column");

        Assert.Equal(SkillLibrary.MaxActiveSkills, selected.Count);
    }

    [Fact]
    public void A_skill_matching_the_application_outranks_one_matching_only_the_words()
    {
        var appSkill = SkillLibrary.Parse("excel.md", "# Excel\napplies-to: excel\n\nApp knowledge")!;
        var wordSkill = SkillLibrary.Parse("formulas.md", "# Formulas\napplies-to: formula\n\nWord knowledge")!;

        var selected = SkillLibrary.Select([wordSkill, appSkill], "Microsoft Excel", "fix my formula");

        Assert.Equal("Excel", selected[0].Name);
    }

    [Fact]
    public void Nothing_is_described_when_no_skill_applies() =>
        Assert.Null(SkillLibrary.Describe([]));

    [Fact]
    public void The_description_labels_skills_as_reference_not_permission()
    {
        var skill = SkillLibrary.Parse("blender.md", BlenderSkill)!;

        var described = SkillLibrary.Describe([skill])!;

        Assert.Contains("not as permission", described, StringComparison.Ordinal);
        Assert.Contains("Blender basics", described, StringComparison.Ordinal);
    }
}

public sealed class ChatRecallTests
{
    private static ChatSession SessionAbout(string title, string application, params string[] userLines)
    {
        var session = ChatSession.Start(application);
        foreach (var line in userLines)
        {
            session = session.Append(new ChatTurn("user", line, DateTimeOffset.Now));
        }

        return session with { Title = title };
    }

    [Fact]
    public void A_chat_names_itself_from_the_first_thing_the_user_said()
    {
        var session = ChatSession.Start()
            .Append(new ChatTurn("user", "help me export a wav from FL Studio", DateTimeOffset.Now));

        Assert.Equal("help me export a wav from FL Studio", session.Title);
    }

    [Fact]
    public void A_long_first_message_is_shortened_into_a_title()
    {
        var session = ChatSession.Start().Append(new ChatTurn(
            "user",
            "I need help with a really long and rambling description of something complicated I am doing today",
            DateTimeOffset.Now));

        Assert.True(session.Title.Length <= 49);
        Assert.EndsWith("…", session.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void A_conversation_about_the_same_work_is_recalled()
    {
        var past = SessionAbout("Exporting audio", "FL Studio", "how do I export a wav from the mixer");

        var digest = ChatRecall.Describe([past], "other", "export wav mixer settings", "FL Studio");

        Assert.NotNull(digest);
        Assert.Contains("Exporting audio", digest!, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unrelated_conversation_is_not_recalled()
    {
        var past = SessionAbout("Holiday photos", "Photoshop", "how do I crop a picture");

        Assert.Null(ChatRecall.Describe([past], "other", "sum a column of numbers", "Excel"));
    }

    [Fact]
    public void The_current_chat_is_never_recalled_into_itself()
    {
        var current = SessionAbout("Exporting audio", "FL Studio", "how do I export a wav");

        Assert.Null(ChatRecall.Describe([current], current.Id, "export a wav", "FL Studio"));
    }

    [Fact]
    public void A_change_of_subject_starts_a_new_chat()
    {
        var current = SessionAbout("Audio", "FL Studio", "how do I export a wav from the mixer");

        Assert.True(ChatRecall.StartsNewSubject(current, "book me a dentist appointment"));
        Assert.False(ChatRecall.StartsNewSubject(current, "and how do I export it louder"));
    }

    [Fact]
    public void An_empty_chat_is_never_treated_as_a_change_of_subject() =>
        Assert.False(ChatRecall.StartsNewSubject(ChatSession.Start(), "anything at all"));

    [Fact]
    public void Old_turns_are_dropped_so_a_chat_cannot_grow_without_limit()
    {
        var session = ChatSession.Start();
        for (var index = 0; index < ChatSession.MaxTurnsKept + 20; index++)
        {
            session = session.Append(new ChatTurn("user", $"line {index}", DateTimeOffset.Now));
        }

        Assert.Equal(ChatSession.MaxTurnsKept, session.Turns.Count);
        Assert.Equal($"line {ChatSession.MaxTurnsKept + 19}", session.Turns[^1].Text);
    }
}
