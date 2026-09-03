using Metis.Core.Agents;

namespace Metis.Tests;

/// <summary>
/// This is the boundary between "an agent tidied its own folder" and "an agent
/// deleted something in Documents". Every file tool routes through it, so the
/// escapes matter more than the happy path — and the escapes are the ones that
/// worked before it existed: an absolute path, and a relative path with enough
/// ".." in it.
/// </summary>
public sealed class AgentWorkspaceTests
{
    private const string Root = @"C:\Users\someone\AppData\Local\Metis\agents\workspace\agent-1234";

    [Fact]
    public void A_relative_path_lands_inside_the_workspace()
    {
        var decision = AgentWorkspace.Resolve(Root, "src/index.html");

        Assert.True(decision.Allowed);
        Assert.Equal(Path.Combine(Root, "src", "index.html"), decision.FullPath);
    }

    [Fact]
    public void The_workspace_itself_is_allowed()
    {
        Assert.True(AgentWorkspace.Resolve(Root, Root).Allowed);
        Assert.True(AgentWorkspace.Resolve(Root, ".").Allowed);
    }

    [Fact]
    public void An_absolute_path_elsewhere_is_refused()
    {
        // This is what an agent could do freely before: name any path on the
        // machine and have it resolved as written.
        var decision = AgentWorkspace.Resolve(Root, @"C:\Windows\System32\drivers\etc\hosts");

        Assert.False(decision.Allowed);
        Assert.Contains("outside this agent's workspace", decision.DenialReason);
    }

    [Theory]
    [InlineData(@"..\..\..\..\Documents\taxes.xlsx")]
    [InlineData("../../../secrets.txt")]
    [InlineData(@"src\..\..\..\..\..\Windows\notepad.exe")]
    public void Climbing_out_with_dot_dot_is_refused(string rawPath)
    {
        // Path.Combine does not normalise, so before this check these resolved
        // to real places well outside the workspace.
        var decision = AgentWorkspace.Resolve(Root, rawPath);

        Assert.False(decision.Allowed);
    }

    [Fact]
    public void Dot_dot_that_stays_inside_is_fine()
    {
        // Refusing every ".." would break ordinary relative work like
        // "src/../README.md", which never leaves the folder.
        var decision = AgentWorkspace.Resolve(Root, @"src\..\README.md");

        Assert.True(decision.Allowed);
        Assert.Equal(Path.Combine(Root, "README.md"), decision.FullPath);
    }

    [Fact]
    public void A_sibling_folder_with_the_same_prefix_is_not_inside()
    {
        // The classic way this check is written wrong: a bare StartsWith would
        // accept agent-12345 as living inside agent-1234.
        var sibling = Root + "5";

        Assert.False(AgentWorkspace.Resolve(Root, Path.Combine(sibling, "file.txt")).Allowed);
        Assert.False(AgentWorkspace.IsUnder(Root, sibling));
    }

    [Fact]
    public void A_task_granted_wider_access_may_leave_the_workspace()
    {
        var decision = AgentWorkspace.Resolve(Root, @"C:\Users\someone\Downloads\report.pdf", allowOutside: true);

        Assert.True(decision.Allowed);
        Assert.Equal(@"C:\Users\someone\Downloads\report.pdf", decision.FullPath);
    }

    [Fact]
    public void Wider_access_still_normalises_the_path()
    {
        // Even when leaving is permitted, what comes back is absolute and free
        // of "..", so the rest of the system never handles a path that means
        // something different from what it looks like.
        var decision = AgentWorkspace.Resolve(Root, @"C:\Users\someone\Downloads\..\Documents\a.txt", allowOutside: true);

        Assert.True(decision.Allowed);
        Assert.Equal(@"C:\Users\someone\Documents\a.txt", decision.FullPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_path_is_refused_rather_than_becoming_the_workspace_itself(string? rawPath)
    {
        var decision = AgentWorkspace.Resolve(Root, rawPath);

        Assert.False(decision.Allowed);
        Assert.Null(decision.FullPath);
    }

    [Fact]
    public void An_agent_with_no_workspace_can_resolve_nothing()
    {
        Assert.False(AgentWorkspace.Resolve("", "anything.txt").Allowed);
    }

    [Fact]
    public void A_path_the_operating_system_cannot_parse_is_refused_not_thrown()
    {
        var decision = AgentWorkspace.Resolve(Root, "bad\0name.txt");

        Assert.False(decision.Allowed);
        Assert.NotNull(decision.DenialReason);
    }

    [Fact]
    public void A_trailing_separator_does_not_change_the_answer()
    {
        Assert.True(AgentWorkspace.IsUnder(Root + @"\", Path.Combine(Root, "a.txt")));
        Assert.True(AgentWorkspace.IsUnder(Root, Path.Combine(Root, "sub") + @"\"));
    }

    [Fact]
    public void Each_task_gets_its_own_folder()
    {
        var first = AgentWorkspace.RootFor("agent-aaaa1111");
        var second = AgentWorkspace.RootFor("agent-bbbb2222");

        Assert.NotEqual(first, second);
        Assert.EndsWith("agent-aaaa1111", first);
        Assert.False(AgentWorkspace.IsUnder(first, second));
    }
}
