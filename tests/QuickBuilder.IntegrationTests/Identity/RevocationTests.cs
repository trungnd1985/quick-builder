using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.Shared.Kernel.Authorization;
using Xunit;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>
/// SC-004: every revocation takes effect on the user's very next request, on another
/// instance (spec FR-017). Also covers known-device revocation (research R-07; moved here
/// from T067 because it needs these endpoints).
/// </summary>
/// <remarks>
/// Two application instances share one database. Each change is made through instance 1 and
/// the user's next request goes to instance 2, which has no in-memory knowledge of the
/// change — exactly the situation behind a load balancer.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class RevocationTests(DatabaseFixture database) : IAsyncLifetime
{
    private string _connectionString = string.Empty;
    private QuickBuilderApp _first = null!;
    private QuickBuilderApp _second = null!;
    private HttpClient _admin = null!;
    private int _client;

    public async Task InitializeAsync()
    {
        _connectionString = await database.CreateDatabaseAsync("revoke").ConfigureAwait(false);
        _first = new QuickBuilderApp(_connectionString);
        _second = new QuickBuilderApp(_connectionString);
        _admin = await _first.CreateAdminClientAsync().ConfigureAwait(false);
        _ = _second.Services;
    }

    public Task DisposeAsync()
    {
        _first.Dispose();
        _second.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>One session, reached through either instance, as a load balancer would route it.</summary>
    [Fact]
    public async Task Sign_out_on_one_instance_is_refused_by_the_other()
    {
        await IdentitySeed.SeedUserAsync(_first.Services, email: "signout@example.test").ConfigureAwait(true);
        var signIn = await SignInHelpers.SignInAsync(Browser(_first), "signout@example.test", IdentitySeed.Password).ConfigureAwait(true);
        var cookie = SignInHelpers.Cookie(signIn, IdentityCookies.Session);

        var viaFirst = SignInHelpers.Browser(_first, "192.0.2.250", cookie);
        var viaSecond = SignInHelpers.Browser(_second, "192.0.2.250", cookie);
        Assert.Equal(HttpStatusCode.OK, (await UsersApi.MeAsync(viaSecond).ConfigureAwait(true)).StatusCode);

        await viaFirst.PostAsync(new Uri("/api/v1/identity/sign-out", UriKind.Relative), null).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Unauthorized, (await UsersApi.MeAsync(viaSecond).ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task Sign_out_everywhere_applies_on_the_other_instance_and_forgets_the_device()
    {
        var (_, onSecond, userId) = await SignedInEverywhereAsync("everywhere@example.test").ConfigureAwait(true);

        await UsersApi.RevokeSessionsAsync(_admin, userId).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Unauthorized, (await UsersApi.MeAsync(onSecond).ConfigureAwait(true)).StatusCode);
        Assert.True(await AllDevicesRevokedAsync(userId).ConfigureAwait(true));
    }

    [Fact]
    public async Task Deactivation_applies_on_the_other_instance_and_forgets_the_device()
    {
        var (_, onSecond, userId) = await SignedInEverywhereAsync("deactivated@example.test").ConfigureAwait(true);

        await UsersApi.DeactivateAsync(_admin, userId).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Unauthorized, (await UsersApi.MeAsync(onSecond).ConfigureAwait(true)).StatusCode);
        Assert.True(await AllDevicesRevokedAsync(userId).ConfigureAwait(true));
    }

    [Fact]
    public async Task A_password_change_applies_on_the_other_instance()
    {
        var (onFirst, onSecond, _) = await SignedInEverywhereAsync("changes@example.test").ConfigureAwait(true);

        await UsersApi.ChangeOwnPasswordAsync(onFirst, IdentitySeed.Password, "an entirely new passphrase").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Unauthorized, (await UsersApi.MeAsync(onSecond).ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task An_email_change_applies_on_the_other_instance_and_forgets_the_device()
    {
        var (_, onSecond, userId) = await SignedInEverywhereAsync("oldaddress@example.test").ConfigureAwait(true);

        await UsersApi.UpdateAsync(_admin, userId, new { email = "newaddress@example.test" }).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Unauthorized, (await UsersApi.MeAsync(onSecond).ConfigureAwait(true)).StatusCode);
        Assert.True(await AllDevicesRevokedAsync(userId).ConfigureAwait(true));
    }

    [Fact]
    public async Task Using_a_reset_link_forgets_the_device()
    {
        var (_, onSecond, userId) = await SignedInEverywhereAsync("resetting@example.test").ConfigureAwait(true);
        var token = await UsersApi.IssueLinkTokenAsync(_admin, userId).ConfigureAwait(true);

        await UsersApi.SetPasswordAsync(Browser(_second), token, "reset to this passphrase").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Unauthorized, (await UsersApi.MeAsync(onSecond).ConfigureAwait(true)).StatusCode);
        Assert.True(await AllDevicesRevokedAsync(userId).ConfigureAwait(true));
    }

    /// <summary>A grant change needs no sign-out: the next request is judged by the new grants.</summary>
    [Fact]
    public async Task A_grant_change_applies_on_the_other_instance()
    {
        var siteId = await UsersApi.CreateSiteAsync(_admin, "granted.test").ConfigureAwait(true);
        var (_, onSecond, userId) = await SignedInEverywhereAsync("granted@example.test", (siteId, SystemRole.Editor)).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, (await onSecond.GetAsync(new Uri($"/api/v1/sites/{siteId}", UriKind.Relative)).ConfigureAwait(true)).StatusCode);

        await UsersApi.ReplaceGrantsAsync(_admin, userId, UsersApi.Grants()).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NotFound, (await onSecond.GetAsync(new Uri($"/api/v1/sites/{siteId}", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UsersApi.MeAsync(onSecond).ConfigureAwait(true)).StatusCode);
    }

    /// <summary>
    /// A user signed in through both instances with real sign-ins, so each browser also holds
    /// a known-device cookie.
    /// </summary>
    private async Task<(HttpClient OnFirst, HttpClient OnSecond, Guid UserId)> SignedInEverywhereAsync(
        string email,
        params (Guid SiteId, SystemRole Role)[] siteRoles)
    {
        var user = await IdentitySeed.SeedUserAsync(_first.Services, email: email, siteRoles: siteRoles).ConfigureAwait(false);

        async Task<HttpClient> SignInThroughAsync(QuickBuilderApp app)
        {
            var address = $"192.0.2.{++_client}";
            var response = await SignInHelpers.SignInAsync(SignInHelpers.Browser(app, address), email, IdentitySeed.Password).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return SignInHelpers.Browser(app, address, SignInHelpers.Cookie(response, IdentityCookies.Session));
        }

        return (await SignInThroughAsync(_first).ConfigureAwait(false), await SignInThroughAsync(_second).ConfigureAwait(false), user.Id);
    }

    private HttpClient Browser(QuickBuilderApp app) => SignInHelpers.Browser(app, $"192.0.2.{++_client}");

    private async Task<bool> AllDevicesRevokedAsync(Guid userId)
    {
        await using var scope = _first.Services.CreateAsyncScope();
        var devices = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .KnownDevices.Where(d => d.UserId == userId).ToListAsync().ConfigureAwait(false);
        return devices.Count > 0 && devices.TrueForAll(d => d.RevokedAtUtc is not null);
    }
}
