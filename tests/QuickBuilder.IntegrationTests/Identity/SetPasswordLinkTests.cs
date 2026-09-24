using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>
/// One-time set-password links (spec FR-011, FR-011a, SC-011; US3 scenarios 2–3).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SetPasswordLinkTests(DatabaseFixture database) : IAsyncLifetime
{
    private const string NewPassword = "a brand new passphrase 42";

    private readonly ManualClock _clock = new();
    private QuickBuilderApp _app = null!;
    private HttpClient _admin = null!;
    private int _client;

    public async Task InitializeAsync()
    {
        _app = new QuickBuilderApp(
            await database.CreateDatabaseAsync("links").ConfigureAwait(false),
            configureTestServices: services => services.AddSingleton<TimeProvider>(_clock));
        _admin = await _app.CreateAdminClientAsync().ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>US3 scenario 1: the link lets a new user choose a password and sign in — once.</summary>
    [Fact]
    public async Task A_link_sets_the_first_password_once()
    {
        var (_, token) = await UsersApi.CreateUserAsync(_admin, "first@example.test").ConfigureAwait(true);

        var check = await UsersApi.CheckLinkAsync(Anonymous(), token).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        Assert.True((await check.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).TryGetProperty("expiresAtUtc", out _));

        Assert.Equal(HttpStatusCode.NoContent, (await UsersApi.SetPasswordAsync(Anonymous(), token, NewPassword).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SignInHelpers.SignInAsync(Anonymous(), "first@example.test", NewPassword).ConfigureAwait(true)).StatusCode);

        Assert.Equal(HttpStatusCode.Gone, (await UsersApi.SetPasswordAsync(Anonymous(), token, "yet another passphrase 7").ConfigureAwait(true)).StatusCode);
    }

    /// <summary>US3 scenario 2: a reset link ends every session and retires the old password.</summary>
    [Fact]
    public async Task A_reset_link_ends_every_session_and_the_old_password()
    {
        var user = await IdentitySeed.SeedUserAsync(_app.Services, email: "forgetful@example.test").ConfigureAwait(true);
        var session = await IdentitySeed.CreateSignedInClientAsync(_app, user.Id).ConfigureAwait(true);
        var token = await UsersApi.IssueLinkTokenAsync(_admin, user.Id).ConfigureAwait(true);

        // FR-011a: the old password keeps working until the link is used.
        Assert.Equal(HttpStatusCode.OK, (await UsersApi.MeAsync(session).ConfigureAwait(true)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await UsersApi.SetPasswordAsync(Anonymous(), token, NewPassword).ConfigureAwait(true)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await UsersApi.MeAsync(session).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInHelpers.SignInAsync(Anonymous(), "forgetful@example.test", IdentitySeed.Password).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SignInHelpers.SignInAsync(Anonymous(), "forgetful@example.test", NewPassword).ConfigureAwait(true)).StatusCode);
    }

    /// <summary>
    /// US3 scenario 3, SC-011: used, expired, replaced, deactivated-account and
    /// post-email-change links are all refused identically.
    /// </summary>
    [Fact]
    public async Task Every_unusable_link_is_refused_identically()
    {
        var (_, used) = await UsersApi.CreateUserAsync(_admin, "used@example.test").ConfigureAwait(true);
        await UsersApi.SetPasswordAsync(Anonymous(), used, NewPassword).ConfigureAwait(true);

        var (replacedUser, replaced) = await UsersApi.CreateUserAsync(_admin, "replaced@example.test").ConfigureAwait(true);
        await UsersApi.IssueLinkTokenAsync(_admin, replacedUser).ConfigureAwait(true);

        var (goneUser, deactivated) = await UsersApi.CreateUserAsync(_admin, "gone@example.test").ConfigureAwait(true);
        await UsersApi.DeactivateAsync(_admin, goneUser).ConfigureAwait(true);

        var (movedUser, emailChanged) = await UsersApi.CreateUserAsync(_admin, "moved@example.test").ConfigureAwait(true);
        await UsersApi.UpdateAsync(_admin, movedUser, new { email = "moved-again@example.test" }).ConfigureAwait(true);

        var (_, expired) = await UsersApi.CreateUserAsync(_admin, "late@example.test").ConfigureAwait(true);
        _clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));

        var bodies = new List<string>();
        foreach (var token in new[] { used, replaced, deactivated, emailChanged, expired, "not-a-real-token" })
        {
            var check = await UsersApi.CheckLinkAsync(Anonymous(), token).ConfigureAwait(true);
            var set = await UsersApi.SetPasswordAsync(Anonymous(), token, NewPassword).ConfigureAwait(true);

            Assert.Equal(HttpStatusCode.Gone, check.StatusCode);
            Assert.Equal(HttpStatusCode.Gone, set.StatusCode);
            bodies.Add(await SignInHelpers.BodyWithoutTraceIdAsync(check).ConfigureAwait(true));
            bodies.Add(await SignInHelpers.BodyWithoutTraceIdAsync(set).ConfigureAwait(true));
        }

        Assert.Single(bodies.Distinct());
        Assert.Contains("identity.link_invalid", bodies[0], StringComparison.Ordinal);
    }

    /// <summary>FR-008 still applies: a weak password is refused and the link stays usable.</summary>
    [Fact]
    public async Task A_weak_password_is_refused_without_spending_the_link()
    {
        var (_, token) = await UsersApi.CreateUserAsync(_admin, "careful@example.test").ConfigureAwait(true);

        var weak = await UsersApi.SetPasswordAsync(Anonymous(), token, "password1234").ConfigureAwait(true);
        var body = await weak.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.True(body.GetProperty("errors").TryGetProperty("NewPassword", out _));
        Assert.Equal(HttpStatusCode.OK, (await UsersApi.CheckLinkAsync(Anonymous(), token).ConfigureAwait(true)).StatusCode);
    }

    /// <summary>Guessing links is throttled per client like guessing passwords.</summary>
    [Fact]
    public async Task Guessing_links_blocks_the_client()
    {
        var guesser = SignInHelpers.Browser(_app, "203.0.113.150");

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Gone, (await UsersApi.CheckLinkAsync(guesser, $"guess-{i}").ConfigureAwait(true)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await UsersApi.CheckLinkAsync(guesser, "guess-5").ConfigureAwait(true)).StatusCode);
    }

    /// <summary>FR-029: opening an unusable link is recorded as a refusal, like using one.</summary>
    [Fact]
    public async Task Opening_an_unusable_link_is_recorded()
    {
        Assert.Equal(HttpStatusCode.Gone, (await UsersApi.CheckLinkAsync(Anonymous(), "not-a-link").ConfigureAwait(true)).StatusCode);

        await using var scope = _app.Services.CreateAsyncScope();
        var refusals = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .SecurityEvents
            .Where(e => e.EventType == SecurityEventTypes.SetPasswordLinkUsed && e.Outcome == SecurityEventOutcomes.Refused)
            .ToListAsync()
            .ConfigureAwait(true);

        var refusal = Assert.Single(refusals);
        Assert.Null(refusal.SubjectUserId);
        Assert.Contains("link_invalid", refusal.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("not-a-link", refusal.Details, StringComparison.Ordinal);
    }

    private HttpClient Anonymous() => SignInHelpers.Browser(_app, $"192.0.2.{++_client}");
}
