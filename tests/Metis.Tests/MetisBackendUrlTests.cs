using Metis.Core.Services;
using Xunit;

namespace Metis.Tests;

/// <summary>
/// The links a person follows to give Metis money.
///
/// These exist because both of them pointed at metis.software for a while, a
/// domain that answers 404, so every Upgrade and Manage plan button in the
/// application led to a dead page. Nothing failed, nothing was logged, and the
/// only symptom was that nobody could pay.
/// </summary>
public sealed class MetisBackendUrlTests
{
    [Fact]
    public void The_site_url_has_no_trailing_slash()
    {
        // The account and pricing helpers append a path to it.
        Assert.False(MetisBackend.SiteUrl.EndsWith('/'));
    }

    [Theory]
    [InlineData("/account")]
    [InlineData("/pricing")]
    public void The_pages_are_absolute_https_urls_under_the_site(string path)
    {
        var url = path == "/account" ? MetisBackend.AccountPageUrl : MetisBackend.PricingPageUrl;

        Assert.True(System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri));
        Assert.Equal(System.Uri.UriSchemeHttps, uri!.Scheme);
        Assert.Equal(path, uri.AbsolutePath);
        Assert.StartsWith(MetisBackend.SiteUrl, url);
    }

    [Fact]
    public void The_site_is_not_the_domain_that_answers_404()
    {
        // metis.software is not served. If it is ever bought and pointed at the
        // site, change this test deliberately rather than by accident.
        Assert.DoesNotContain("metis.software", MetisBackend.SiteUrl);
    }

    [Fact]
    public void The_gateway_and_the_site_are_different_hosts()
    {
        // Sending a person to the API to buy something is its own kind of dead
        // link, and the two constants sit next to each other.
        Assert.NotEqual(
            new System.Uri(MetisBackend.DefaultGatewayUrl).Host,
            new System.Uri(MetisBackend.SiteUrl).Host);
    }
}
