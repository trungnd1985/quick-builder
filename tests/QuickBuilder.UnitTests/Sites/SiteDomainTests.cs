using QuickBuilder.Sites.Domain;
using Xunit;

namespace QuickBuilder.UnitTests.Sites;

/// <summary>
/// The invariants <c>Site</c> and <c>SiteHostName</c> enforce for themselves.
/// </summary>
/// <remarks>
/// These rules exist in the domain rather than in a validator so that they hold no matter
/// which caller creates a site — a future import path or seeding routine gets them too.
/// Testing them directly proves that, where an endpoint test would only prove the one
/// caller behaves.
/// </remarks>
public sealed class SiteDomainTests
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    [Fact]
    public void A_site_is_created_with_its_host_names()
    {
        var created = Site.Create("Example", ["example.test", "www.example.test"], Clock);

        Assert.True(created.IsSuccess);
        Assert.Equal("Example", created.Value.Name);
        Assert.Equal(2, created.Value.HostNames.Count);
    }

    [Fact]
    public void Site_names_are_trimmed()
    {
        var created = Site.Create("   Example   ", ["example.test"], Clock);

        Assert.Equal("Example", created.Value.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_site_needs_a_name(string name)
    {
        var created = Site.Create(name, ["example.test"], Clock);

        Assert.True(created.IsFailure);
        Assert.Equal("sites.invalid_name", created.Error.Code);
    }

    [Fact]
    public void A_site_name_has_a_maximum_length()
    {
        var created = Site.Create(new string('x', Site.MaxNameLength + 1), ["example.test"], Clock);

        Assert.True(created.IsFailure);
        Assert.Equal("sites.invalid_name", created.Error.Code);
    }

    /// <summary>
    /// A site with no host name could never be reached, so it is not a site.
    /// </summary>
    [Fact]
    public void A_site_needs_at_least_one_host_name()
    {
        var created = Site.Create("Example", [], Clock);

        Assert.True(created.IsFailure);
        Assert.Equal("sites.no_host_names", created.Error.Code);
    }

    [Fact]
    public void Adding_a_host_name_the_site_already_has_is_a_conflict()
    {
        var site = Site.Create("Example", ["example.test"], Clock).Value;

        var added = site.AddHostName("example.test", Clock);

        Assert.True(added.IsFailure);
        Assert.Equal("sites.host_name_taken", added.Error.Code);
    }

    /// <summary>
    /// Host names are case-insensitive, so differing case is the same name.
    /// </summary>
    /// <remarks>
    /// Storage is <c>citext</c> and the unique index would catch this, but catching it in
    /// the domain means the caller gets a clear conflict rather than a database exception
    /// translated after the fact.
    /// </remarks>
    [Fact]
    public void Host_names_differing_only_in_case_are_the_same_name()
    {
        var site = Site.Create("Example", ["example.test"], Clock).Value;

        var added = site.AddHostName("EXAMPLE.TEST", Clock);

        Assert.True(added.IsFailure);
        Assert.Equal("sites.host_name_taken", added.Error.Code);
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("blog.example.co.uk")]
    [InlineData("a.b")]
    [InlineData("xn--bcher-kva.example")]
    public void Valid_host_names_are_accepted(string hostName)
    {
        var created = Site.Create("Example", [hostName], Clock);

        Assert.True(created.IsSuccess);
    }

    [Theory]
    [InlineData("not a host")]          // spaces
    [InlineData("-leading.test")]       // leading hyphen
    [InlineData("trailing-.test")]      // trailing hyphen
    [InlineData("example.com:8080")]    // a port is the proxy's concern, not a site's
    [InlineData("http://example.com")]  // a scheme is not a host name
    [InlineData("")]
    public void Invalid_host_names_are_rejected(string hostName)
    {
        var created = Site.Create("Example", [hostName], Clock);

        Assert.True(created.IsFailure);
    }

    [Fact]
    public void Host_names_are_normalised_to_lower_case()
    {
        var site = Site.Create("Example", ["  EXAMPLE.TEST  "], Clock).Value;

        Assert.Equal("example.test", site.HostNames.Single().HostName);
    }

    /// <summary>
    /// A site is not itself site-owned.
    /// </summary>
    /// <remarks>
    /// Stated as a test because it is the reason listing sites needs the cross-site
    /// bypass. If <c>Site</c> ever implemented <c>ISiteOwned</c>, the admin list would
    /// filter itself down to nothing and the cause would be far from obvious.
    /// </remarks>
    [Fact]
    public void A_site_is_the_scope_not_something_within_one()
    {
        Assert.False(typeof(Shared.Kernel.MultiSite.ISiteOwned).IsAssignableFrom(typeof(Site)));
        Assert.True(typeof(Shared.Kernel.MultiSite.ISiteOwned).IsAssignableFrom(typeof(SiteHostName)));
    }
}
