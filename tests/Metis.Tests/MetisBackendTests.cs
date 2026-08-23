using Metis.Core.Services;

namespace Metis.Tests;

/// <summary>
/// A fresh install has no server address in its settings, so the built-in one
/// has to be what gets used. These fix that fallback, and fix that an explicit
/// setting still wins over it.
/// </summary>
public sealed class MetisBackendTests
{
    [Fact]
    public void An_empty_setting_falls_back_to_the_built_in_project()
    {
        Assert.Equal(MetisBackend.DefaultUrl, MetisBackend.ResolveUrl(null));
        Assert.Equal(MetisBackend.DefaultUrl, MetisBackend.ResolveUrl(string.Empty));
        Assert.Equal(MetisBackend.DefaultUrl, MetisBackend.ResolveUrl("   "));
    }

    [Fact]
    public void A_configured_setting_wins_so_a_dev_build_can_be_pointed_elsewhere()
    {
        Assert.Equal(
            "https://example.supabase.co",
            MetisBackend.ResolveUrl("  https://example.supabase.co  "));
    }

    [Fact]
    public void The_key_follows_the_same_rule()
    {
        Assert.Equal(MetisBackend.DefaultPublishableKey, MetisBackend.ResolveKey(" "));
        Assert.Equal("sb_publishable_other", MetisBackend.ResolveKey("sb_publishable_other"));
    }

    [Fact]
    public void A_stock_build_counts_as_having_a_backend()
    {
        Assert.True(MetisBackend.IsConfigured(null, null));
    }

    [Fact]
    public void The_built_in_key_is_a_publishable_one_and_not_a_service_key()
    {
        // The service role key bypasses row-level security entirely. Shipping
        // one inside the desktop app would hand every user full read and write
        // over everyone else's rows, so this asserts the shape of what is
        // compiled in rather than trusting it never gets pasted over.
        Assert.StartsWith("sb_publishable_", MetisBackend.DefaultPublishableKey, StringComparison.Ordinal);
        Assert.DoesNotContain("service_role", MetisBackend.DefaultPublishableKey, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", MetisBackend.DefaultPublishableKey, StringComparison.OrdinalIgnoreCase);
    }
}
