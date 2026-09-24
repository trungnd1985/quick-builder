using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Domain.Details;
using QuickBuilder.Shared.Kernel.Authorization;
using Xunit;

namespace QuickBuilder.UnitTests.Identity;

/// <summary>
/// The invariants the Identity entities enforce for themselves (002 data-model.md).
/// </summary>
public sealed class DomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_installation_is_claimed_once_and_loses_its_code()
    {
        var installation = Installation.CreateUnclaimed("ABCDEFGHJKMN", Now);

        installation.Claim(Now);

        Assert.True(installation.IsClaimed);
        Assert.Null(installation.SetupCode);
        Assert.Throws<InvalidOperationException>(() => installation.Claim(Now));
    }

    [Fact]
    public void A_new_user_is_active_without_a_password_and_cannot_sign_in()
    {
        var user = User.Create("  Ada  ", "  ada@example.test ", Now).Value;

        Assert.Equal("Ada", user.DisplayName);
        Assert.Equal("ada@example.test", user.Email);
        Assert.True(user.IsActive);
        Assert.False(user.HasPassword);
        Assert.False(user.CanSignIn);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-at-sign")]
    [InlineData("@example.test")]
    [InlineData("ada@")]
    [InlineData("ada@@example.test")]
    [InlineData("ada lovelace@example.test")]
    public void Malformed_email_addresses_are_rejected(string email)
    {
        Assert.True(User.Create("Ada", email, Now).IsFailure);
    }

    [Fact]
    public void Only_an_active_user_with_a_password_can_sign_in()
    {
        var user = User.Create("Ada", "ada@example.test", Now).Value;
        user.SetPasswordHash("$pbkdf2-sha512$i=1$c2FsdA==$aGFzaA==", Now);
        Assert.True(user.CanSignIn);

        user.Deactivate(Now);
        Assert.False(user.CanSignIn);
        Assert.NotNull(user.DeactivatedAtUtc);

        user.Reactivate(Now);
        Assert.True(user.CanSignIn);
        Assert.Null(user.DeactivatedAtUtc);
    }

    [Fact]
    public void Administrator_is_installation_wide_and_site_roles_name_a_site()
    {
        var userId = Guid.NewGuid();
        var siteId = Guid.NewGuid();

        Assert.Null(RoleGrant.Administrator(userId, null, Now).SiteId);
        Assert.Equal(siteId, RoleGrant.ForSite(userId, siteId, SystemRole.Editor, null, Now).SiteId);
        Assert.Throws<ArgumentException>(() => RoleGrant.ForSite(userId, siteId, SystemRole.Administrator, null, Now));
        Assert.Throws<ArgumentException>(() => RoleGrant.ForSite(userId, Guid.Empty, SystemRole.Designer, null, Now));
    }

    [Fact]
    public void A_session_expires_when_idle_or_at_its_absolute_limit_whichever_is_first()
    {
        var idle = TimeSpan.FromHours(2);
        var session = Session.Start(Guid.NewGuid(), [1, 2, 3], Now, TimeSpan.FromHours(12), null);

        Assert.True(session.IsValid(Now.AddHours(1), idle));
        Assert.False(session.IsValid(Now.AddHours(2), idle));

        // Activity keeps it alive, but never past the absolute limit.
        for (var t = Now; t < Now.AddHours(12); t = t.AddHours(1))
        {
            session.Touch(t);
        }

        Assert.False(session.IsValid(Now.AddHours(12), idle));
    }

    [Fact]
    public void Activity_is_written_at_most_once_a_minute()
    {
        var session = Session.Start(Guid.NewGuid(), [1], Now, TimeSpan.FromHours(12), null);

        Assert.False(session.Touch(Now.AddSeconds(30)));
        Assert.True(session.Touch(Now.AddSeconds(61)));
    }

    [Fact]
    public void A_revoked_session_is_invalid_and_keeps_its_first_reason()
    {
        var session = Session.Start(Guid.NewGuid(), [1], Now, TimeSpan.FromHours(12), null);

        session.Revoke(Now, SessionRevocationReasons.SignOut);
        session.Revoke(Now.AddMinutes(1), SessionRevocationReasons.Deactivated);

        Assert.False(session.IsValid(Now, TimeSpan.FromHours(2)));
        Assert.Equal(SessionRevocationReasons.SignOut, session.RevocationReason);
    }

    [Fact]
    public void A_link_is_single_use_and_expires()
    {
        var link = SetPasswordLink.Issue(Guid.NewGuid(), [1], Now, TimeSpan.FromDays(1), null);

        Assert.True(link.IsUsable(Now.AddHours(23)));
        Assert.False(link.IsUsable(Now.AddDays(1)));

        link.Consume(Now);
        Assert.False(link.IsLive);
        Assert.Throws<InvalidOperationException>(() => link.Consume(Now));
    }

    [Fact]
    public void An_invalidated_link_cannot_be_used()
    {
        var link = SetPasswordLink.Issue(Guid.NewGuid(), [1], Now, TimeSpan.FromDays(1), null);

        link.Invalidate(Now, LinkInvalidationReasons.Replaced);

        Assert.False(link.IsUsable(Now));
        Assert.Equal(LinkInvalidationReasons.Replaced, link.InvalidationReason);
    }

    [Fact]
    public void A_known_device_is_revoked_after_repeated_failures_and_reset_by_success()
    {
        var device = KnownDevice.Remember(Guid.NewGuid(), [1], Now);

        Assert.False(device.RecordFailure(3, Now));
        Assert.False(device.RecordFailure(3, Now));
        device.RecordSuccess(Now);
        Assert.False(device.RecordFailure(3, Now));
        Assert.False(device.RecordFailure(3, Now));
        Assert.True(device.RecordFailure(3, Now));

        Assert.False(device.IsValid(Now, TimeSpan.FromDays(90)));
    }

    [Fact]
    public void A_known_device_expires_after_a_period_without_use()
    {
        var device = KnownDevice.Remember(Guid.NewGuid(), [1], Now);

        Assert.True(device.IsValid(Now.AddDays(89), TimeSpan.FromDays(90)));
        Assert.False(device.IsValid(Now.AddDays(90), TimeSpan.FromDays(90)));
    }

    [Fact]
    public void Security_events_accept_only_catalogued_types_and_outcomes()
    {
        Assert.Throws<ArgumentException>(() =>
            SecurityEvent.Create("made_up", SecurityEventOutcomes.Succeeded, Now, null, null, null));
        Assert.Throws<ArgumentException>(() =>
            SecurityEvent.Create(SecurityEventTypes.SignIn, "maybe", Now, null, null, null));

        var created = SecurityEvent.Create(
            SecurityEventTypes.EmailChanged,
            SecurityEventOutcomes.Succeeded,
            Now,
            null,
            null,
            null,
            new EmailChangedDetails("old@example.test", "new@example.test"));

        Assert.Equal("""{"oldEmail":"old@example.test","newEmail":"new@example.test"}""", created.Details);
    }

    [Fact]
    public void Capability_definitions_reject_bad_names_and_an_explicit_administrator()
    {
        Assert.Throws<ArgumentException>(() => new CapabilityDefinition("Sites.Create", CapabilityScope.Installation, []));
        Assert.Throws<ArgumentException>(() => new CapabilityDefinition("sites", CapabilityScope.Installation, []));
        Assert.Throws<ArgumentException>(() =>
            new CapabilityDefinition("sites.view", CapabilityScope.Site, [SystemRole.Administrator]));
    }
}
