using System.Net;
using System.Net.Http.Json;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.IntegrationTests.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>
/// A user's own account: changing their password, ending their other sessions, and an
/// administrator signing someone out everywhere (spec FR-012, FR-012a; US3 scenarios 8, 11, 12).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OwnAccountTests(DatabaseFixture database) : IAsyncLifetime
{
    private const string NewPassword = "my new passphrase for today";

    private QuickBuilderApp _app = null!;
    private int _client;

    public async Task InitializeAsync()
    {
        _app = new QuickBuilderApp(await database.CreateDatabaseAsync("ownaccount").ConfigureAwait(false));
        _ = _app.Services;
        await IdentitySeed.ClaimInstallationAsync(_app.Services).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>US3 scenario 8: the current password is required; other sessions end, this one stays.</summary>
    [Fact]
    public async Task Changing_my_password_keeps_this_session_and_ends_the_others()
    {
        var (current, other) = await TwoSessionsAsync("changer@example.test").ConfigureAwait(true);

        var wrong = await UsersApi.ChangeOwnPasswordAsync(current, "not my password at all", NewPassword).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal("identity.current_password_incorrect", await UsersApi.ErrorCodeAsync(wrong).ConfigureAwait(true));

        var changed = await UsersApi.ChangeOwnPasswordAsync(current, IdentitySeed.Password, NewPassword).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await UsersApi.MeAsync(current).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await UsersApi.MeAsync(other).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SignInHelpers.SignInAsync(Browser(), "changer@example.test", NewPassword).ConfigureAwait(true)).StatusCode);
    }

    [Fact]
    public async Task A_weak_new_password_is_refused()
    {
        var (current, _) = await TwoSessionsAsync("weakling@example.test").ConfigureAwait(true);

        var response = await UsersApi.ChangeOwnPasswordAsync(current, IdentitySeed.Password, "short").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Guessing the current password from a stolen session is throttled per account.</summary>
    [Fact]
    public async Task Guessing_the_current_password_is_throttled()
    {
        var (current, _) = await TwoSessionsAsync("guarded@example.test").ConfigureAwait(true);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await UsersApi.ChangeOwnPasswordAsync(current, $"guess number {i} here", NewPassword).ConfigureAwait(true)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await UsersApi.ChangeOwnPasswordAsync(current, IdentitySeed.Password, NewPassword).ConfigureAwait(true)).StatusCode);
    }

    /// <summary>US3 scenario 12: "sign out my other sessions" keeps the one that asked.</summary>
    [Fact]
    public async Task Signing_out_my_other_sessions_keeps_this_one()
    {
        var (current, other) = await TwoSessionsAsync("tidy@example.test").ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NoContent, (await UsersApi.RevokeOtherSessionsAsync(current).ConfigureAwait(true)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await UsersApi.MeAsync(current).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await UsersApi.MeAsync(other).ConfigureAwait(true)).StatusCode);
    }

    /// <summary>US3 scenario 11: an administrator signs a user out everywhere; the account stays active.</summary>
    [Fact]
    public async Task An_administrator_can_sign_a_user_out_everywhere()
    {
        var (current, other) = await TwoSessionsAsync("lostlaptop@example.test").ConfigureAwait(true);
        var admin = await _app.CreateAdminClientAsync().ConfigureAwait(true);
        var userId = await UserIdAsync(current).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.NoContent, (await UsersApi.RevokeSessionsAsync(admin, userId).ConfigureAwait(true)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await UsersApi.MeAsync(current).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await UsersApi.MeAsync(other).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SignInHelpers.SignInAsync(Browser(), "lostlaptop@example.test", IdentitySeed.Password).ConfigureAwait(true)).StatusCode);
    }

    /// <summary>Two real sign-ins for one new user, from two browsers.</summary>
    private async Task<(HttpClient Current, HttpClient Other)> TwoSessionsAsync(string email)
    {
        await IdentitySeed.SeedUserAsync(_app.Services, email: email).ConfigureAwait(false);

        async Task<HttpClient> SignedInAsync()
        {
            var address = $"192.0.2.{++_client}";
            var response = await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, address), email, IdentitySeed.Password).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return SignInHelpers.Browser(_app, address, SignInHelpers.Cookie(response, IdentityCookies.Session));
        }

        return (await SignedInAsync().ConfigureAwait(false), await SignedInAsync().ConfigureAwait(false));
    }

    private HttpClient Browser() => SignInHelpers.Browser(_app, $"192.0.2.{++_client}");

    private static async Task<Guid> UserIdAsync(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(new Uri("/api/v1/identity/me", UriKind.Relative)).ConfigureAwait(false);
        return me.GetProperty("id").GetGuid();
    }
}
