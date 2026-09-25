using QuickBuilder.Sites.Domain;
using Xunit;

namespace QuickBuilder.UnitTests.Sites;

/// <summary>
/// The Site aggregate's settings, primary host name and availability (009 data-model.md,
/// "Invariants of the Site aggregate").
/// </summary>
public sealed class SiteAggregateTests
{
    private readonly SteppedClock _clock = new(new DateTimeOffset(2026, 9, 26, 9, 0, 0, TimeSpan.Zero));

    private Site NewSite(params string[] hostNames) =>
        Site.Create("Example", hostNames, _clock).Value;

    [Fact]
    public void A_new_site_is_enabled_https_english_with_its_first_host_name_primary()
    {
        var site = NewSite("example.test", "www.example.test");

        Assert.Equal(SiteStatus.Enabled, site.Status);
        Assert.Equal(SiteScheme.Https, site.Scheme);
        Assert.Equal("en", site.DefaultLanguage);
        Assert.Equal("example.test", site.PrimaryHostName);
    }

    [Fact]
    public void Create_accepts_a_primary_scheme_and_language_in_any_equivalent_spelling()
    {
        var created = Site.Create(
            "Example", ["example.test", "www.example.test"], "WWW.Example.Test.", "HTTP", "PT-br", _clock);

        Assert.True(created.IsSuccess);
        Assert.Equal("www.example.test", created.Value.PrimaryHostName);
        Assert.Equal(SiteScheme.Http, created.Value.Scheme);
        Assert.Equal("pt-BR", created.Value.DefaultLanguage);
    }

    [Theory]
    [InlineData("other.test", "https", "en", "sites.primary_host_name_invalid")]
    [InlineData(null, "ftp", "en", "sites.invalid_scheme")]
    [InlineData(null, "https", "xx", "sites.unsupported_language")]
    public void Create_refuses_bad_settings(string? primary, string scheme, string language, string code)
    {
        var created = Site.Create("Example", ["example.test"], primary, scheme, language, _clock);

        Assert.True(created.IsFailure);
        Assert.Equal(code, created.Error.Code);
    }

    [Fact]
    public void Settings_change_every_editable_value_and_move_the_update_time()
    {
        var site = NewSite("example.test", "www.example.test");
        _clock.Advance(TimeSpan.FromMinutes(1));

        var changed = site.ChangeSettings("  Renamed  ", "http", "de", "www.example.test", _clock);

        Assert.True(changed.IsSuccess);
        Assert.Equal("Renamed", site.Name);
        Assert.Equal(SiteScheme.Http, site.Scheme);
        Assert.Equal("de", site.DefaultLanguage);
        Assert.Equal("www.example.test", site.PrimaryHostName);
        Assert.Equal(_clock.GetUtcNow(), site.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("", "https", "en", "example.test", "sites.invalid_name")]
    [InlineData("Ok", "gopher", "en", "example.test", "sites.invalid_scheme")]
    [InlineData("Ok", "https", "klingon", "example.test", "sites.unsupported_language")]
    [InlineData("Ok", "https", "en", "elsewhere.test", "sites.primary_host_name_invalid")]
    [InlineData("Ok", "https", "en", "not a host", "sites.primary_host_name_invalid")]
    public void Settings_refuse_bad_values_and_change_nothing(
        string name, string scheme, string language, string primary, string code)
    {
        var site = NewSite("example.test");

        var changed = site.ChangeSettings(name, scheme, language, primary, _clock);

        Assert.True(changed.IsFailure);
        Assert.Equal(code, changed.Error.Code);
        Assert.Equal("Example", site.Name);
        Assert.Equal(SiteScheme.Https, site.Scheme);
    }

    [Fact]
    public void An_added_host_name_is_never_made_primary()
    {
        var site = NewSite("example.test");

        Assert.True(site.AddHostName("www.example.test", _clock).IsSuccess);

        Assert.Equal("example.test", site.PrimaryHostName);
    }

    [Fact]
    public void A_host_name_that_is_not_primary_can_be_removed_in_any_spelling()
    {
        var site = NewSite("example.test", "www.example.test");
        _clock.Advance(TimeSpan.FromMinutes(1));

        var removed = site.RemoveHostName("WWW.EXAMPLE.TEST.", _clock);

        Assert.True(removed.IsSuccess);
        Assert.Equal(["example.test"], site.HostNames.Select(h => h.HostName));
        Assert.Equal(_clock.GetUtcNow(), site.UpdatedAtUtc);
    }

    [Fact]
    public void The_primary_host_name_cannot_be_removed()
    {
        var site = NewSite("example.test", "www.example.test");

        var removed = site.RemoveHostName("example.test", _clock);

        Assert.Equal("sites.primary_host_name_removal", removed.Error!.Code);
        Assert.Equal(2, site.HostNames.Count);
    }

    /// <summary>
    /// The only host name is also the primary; the more specific reason wins, because
    /// "make another primary first" is impossible advice when there is no other.
    /// </summary>
    [Fact]
    public void The_only_host_name_cannot_be_removed()
    {
        var site = NewSite("example.test");

        var removed = site.RemoveHostName("example.test", _clock);

        Assert.Equal("sites.last_host_name", removed.Error!.Code);
    }

    [Theory]
    [InlineData("other.test")]
    [InlineData("not a host")]
    public void Removing_a_host_name_the_site_does_not_hold_is_not_found(string hostName)
    {
        var site = NewSite("example.test", "www.example.test");

        var removed = site.RemoveHostName(hostName, _clock);

        Assert.Equal("sites.host_name_not_found", removed.Error!.Code);
    }

    [Fact]
    public void Disabling_and_enabling_report_whether_anything_changed()
    {
        var site = NewSite("example.test");

        Assert.True(site.Disable(_clock));
        Assert.Equal(SiteStatus.Disabled, site.Status);
        Assert.False(site.Disable(_clock));

        Assert.True(site.Enable(_clock));
        Assert.Equal(SiteStatus.Enabled, site.Status);
        Assert.False(site.Enable(_clock));
    }

    [Fact]
    public void Disabling_keeps_host_names_and_settings()
    {
        var site = NewSite("example.test", "www.example.test");

        site.Disable(_clock);

        Assert.Equal(2, site.HostNames.Count);
        Assert.Equal("example.test", site.PrimaryHostName);
        Assert.Equal("en", site.DefaultLanguage);
    }
}

/// <summary>A clock that moves only when told to, so update times can be asserted exactly.</summary>
internal sealed class SteppedClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
