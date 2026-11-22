using System.Security.Cryptography;
using System.Text;
using QuickBuilder.Editor.Contracts.Events;
using QuickBuilder.Editor.Domain;

namespace QuickBuilder.UnitTests.Editor;

/// <summary>The preview link aggregate (022 data-model §1, research PV-07).</summary>
public sealed class PreviewLinkTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Site = Guid.NewGuid();
    private static readonly Guid Page = Guid.NewGuid();
    private static readonly Guid Lan = Guid.NewGuid();

    private static (PreviewLink Link, string Secret) Create(DateTimeOffset? expires = null, string? label = null)
    {
        var result = PreviewLink.Create(Site, "page", Page, null, null, label, expires ?? Now.AddDays(7), Lan, Now);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    [Fact]
    public void An_expiry_must_be_after_now_and_at_most_thirty_days_ahead()
    {
        Assert.True(PreviewLink.Create(Site, "page", Page, null, null, null, Now.AddDays(30), Lan, Now).IsSuccess);
        Assert.True(PreviewLink.Create(Site, "page", Page, null, null, null, Now.AddHours(1), Lan, Now).IsSuccess);

        foreach (var expiry in new[] { Now, Now.AddSeconds(-1), Now.AddDays(30).AddSeconds(1) })
        {
            var refused = PreviewLink.Create(Site, "page", Page, null, null, null, expiry, Lan, Now);
            Assert.Equal("preview.invalid_expiry", refused.Error!.Code);
        }
    }

    [Fact]
    public void A_label_is_trimmed_empty_is_none_and_long_or_control_characters_are_refused()
    {
        Assert.Equal("Client review", Create(label: "  Client review ").Link.Label);
        Assert.Null(Create(label: "   ").Link.Label);
        Assert.Equal(80, Create(label: new string('a', 80)).Link.Label!.Length);

        foreach (var label in new[] { new string('a', 81), "Client\nreview", "Tab\there" })
        {
            Assert.Equal("preview.invalid_label", PreviewLink.Create(Site, "page", Page, null, null, label, Now.AddDays(1), Lan, Now).Error!.Code);
        }
    }

    [Fact]
    public void The_secret_is_43_url_safe_characters_and_only_its_hash_is_kept()
    {
        var (link, secret) = Create();

        Assert.Equal(43, secret.Length);
        Assert.True(PreviewSecrets.IsWellFormed(secret));
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(secret)), link.SecretHash);
        Assert.NotEqual(secret, Create().Secret);
    }

    [Fact]
    public void A_link_is_active_until_it_expires_is_revoked_or_predates_the_operators_switch()
    {
        var (link, _) = Create(expires: Now.AddDays(1));

        Assert.True(link.IsActive(Now, validFrom: null));
        Assert.True(link.IsActive(Now, validFrom: Now));
        Assert.False(link.IsActive(Now.AddDays(1), validFrom: null));
        Assert.False(link.IsActive(Now, validFrom: Now.AddSeconds(1)));

        link.Revoke(Lan, Now.AddMinutes(1));
        Assert.False(link.IsActive(Now.AddMinutes(2), validFrom: null));
    }

    [Fact]
    public void Creating_raises_one_event_and_revoking_twice_raises_one_more()
    {
        var (link, _) = Create();
        var created = Assert.IsType<PreviewLinkCreated>(Assert.Single(link.Events));
        Assert.Equal(link.Id, created.LinkId);
        Assert.Equal(Now.AddDays(7), created.ExpiresAtUtc);

        link.Revoke(Lan, Now.AddMinutes(1));
        var revokedAt = link.RevokedAtUtc;
        link.Revoke(Guid.NewGuid(), Now.AddMinutes(2));

        Assert.Equal(revokedAt, link.RevokedAtUtc);
        Assert.Equal(Lan, link.RevokedBy);
        Assert.Equal(2, link.Events.Count);
        Assert.IsType<PreviewLinkRevoked>(link.Events[1]);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("short", false)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/", false)]
    public void Only_secret_shaped_values_are_looked_up(string? secret, bool expected) =>
        Assert.Equal(expected, PreviewSecrets.IsWellFormed(secret));
}
