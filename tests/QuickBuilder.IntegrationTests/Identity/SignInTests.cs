using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuickBuilder.Identity;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.IntegrationTests.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>
/// User Story 2: signing in and out (spec FR-009, FR-014, FR-016, FR-017, FR-018).
/// </summary>
/// <remarks>
/// Every test uses its own client addresses: throttling state is shared by all tests in this
/// class's database, and a refusal in one must not leak into another.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class SignInTests(DatabaseFixture database) : IAsyncLifetime
{
    private readonly ManualClock _clock = new();
    private QuickBuilderApp _app = null!;

    public async Task InitializeAsync()
    {
        _app = new QuickBuilderApp(
            await database.CreateDatabaseAsync("signin").ConfigureAwait(false),
            configureTestServices: services => services.AddSingleton<TimeProvider>(_clock));
        _ = _app.Services;
        await IdentitySeed.ClaimInstallationAsync(_app.Services).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>US2 scenario 1: the right password starts a session in a safe cookie.</summary>
    [Fact]
    public async Task The_right_password_signs_in()
    {
        var user = await IdentitySeed.SeedUserAsync(_app.Services, email: "ada@example.test").ConfigureAwait(true);

        var response = await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, "192.0.2.1"), "ada@example.test", IdentitySeed.Password).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
        Assert.Equal(user.Id, me.GetProperty("id").GetGuid());

        var session = SetupHelpers.SetCookieHeader(response, IdentityCookies.Session)!;
        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", session, StringComparison.OrdinalIgnoreCase);

        var signedIn = SignInHelpers.Browser(_app, "192.0.2.1", SignInHelpers.Cookie(response, IdentityCookies.Session));
        Assert.Equal(HttpStatusCode.OK, (await signedIn.GetAsync(new Uri("/api/v1/identity/me", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
    }

    /// <summary>
    /// US2 scenario 2, FR-014: unknown email, wrong password, an account without a password
    /// and a deactivated account are refused identically.
    /// </summary>
    [Fact]
    public async Task Every_kind_of_failure_gets_the_same_answer()
    {
        await IdentitySeed.SeedUserAsync(_app.Services, email: "real@example.test").ConfigureAwait(true);
        await IdentitySeed.SeedUserAsync(_app.Services, email: "nopassword@example.test", password: null).ConfigureAwait(true);
        var gone = await IdentitySeed.SeedUserAsync(_app.Services, email: "gone@example.test").ConfigureAwait(true);
        await DeactivateAsync(gone.Id).ConfigureAwait(true);

        var attempts = new[]
        {
            ("nobody@example.test", IdentitySeed.Password),
            ("real@example.test", "wrong but long enough"),
            ("nopassword@example.test", IdentitySeed.Password),
            ("gone@example.test", IdentitySeed.Password),
        };

        var bodies = new List<string>();
        for (var i = 0; i < attempts.Length; i++)
        {
            var response = await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, $"192.0.2.{10 + i}"), attempts[i].Item1, attempts[i].Item2).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(SetupHelpers.CookieValue(response, IdentityCookies.Session));
            bodies.Add(await SignInHelpers.BodyWithoutTraceIdAsync(response).ConfigureAwait(true));
        }

        Assert.Single(bodies.Distinct());
        Assert.Contains("identity.invalid_credentials", bodies[0], StringComparison.Ordinal);
    }

    /// <summary>US2 scenario 4: signing out ends the session, even for a replayed cookie.</summary>
    [Fact]
    public async Task Signing_out_ends_the_session_for_good()
    {
        await IdentitySeed.SeedUserAsync(_app.Services, email: "leaver@example.test").ConfigureAwait(true);
        var signIn = await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, "192.0.2.20"), "leaver@example.test", IdentitySeed.Password).ConfigureAwait(true);
        var cookie = SignInHelpers.Cookie(signIn, IdentityCookies.Session);

        var signOut = await SignInHelpers.Browser(_app, "192.0.2.20", cookie)
            .PostAsync(new Uri("/api/v1/identity/sign-out", UriKind.Relative), null).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);

        var replayed = await SignInHelpers.Browser(_app, "192.0.2.20", cookie)
            .GetAsync(new Uri("/api/v1/identity/me", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);
    }

    /// <summary>FR-016: an idle session ends after the idle timeout (default 2 hours).</summary>
    [Fact]
    public async Task An_idle_session_expires()
    {
        await IdentitySeed.SeedUserAsync(_app.Services, email: "idle@example.test").ConfigureAwait(true);
        var signIn = await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, "192.0.2.30"), "idle@example.test", IdentitySeed.Password).ConfigureAwait(true);
        var browser = SignInHelpers.Browser(_app, "192.0.2.30", SignInHelpers.Cookie(signIn, IdentityCookies.Session));

        _clock.Advance(TimeSpan.FromMinutes(119));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(new Uri("/api/v1/identity/me", UriKind.Relative)).ConfigureAwait(true)).StatusCode);

        // Activity just now restarted the idle window.
        _clock.Advance(TimeSpan.FromMinutes(119));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(new Uri("/api/v1/identity/me", UriKind.Relative)).ConfigureAwait(true)).StatusCode);

        _clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync(new Uri("/api/v1/identity/me", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
    }

    /// <summary>FR-016: however active, a session ends at its absolute lifetime (default 12 hours).</summary>
    [Fact]
    public async Task An_active_session_still_ends_at_its_absolute_lifetime()
    {
        await IdentitySeed.SeedUserAsync(_app.Services, email: "busy@example.test").ConfigureAwait(true);
        var signIn = await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, "192.0.2.40"), "busy@example.test", IdentitySeed.Password).ConfigureAwait(true);
        var browser = SignInHelpers.Browser(_app, "192.0.2.40", SignInHelpers.Cookie(signIn, IdentityCookies.Session));

        for (var hour = 1; hour < 12; hour++)
        {
            _clock.Advance(TimeSpan.FromHours(1));
            Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(new Uri("/api/v1/identity/me", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
        }

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync(new Uri("/api/v1/identity/me", UriKind.Relative)).ConfigureAwait(true)).StatusCode);
    }

    /// <summary>
    /// Spec edge cases: the email address is matched ignoring case and surrounding spaces;
    /// the password is matched exactly.
    /// </summary>
    [Fact]
    public async Task Email_ignores_case_and_spaces_but_the_password_does_not()
    {
        await IdentitySeed.SeedUserAsync(_app.Services, email: "Mixed.Case@Example.test").ConfigureAwait(true);

        var relaxed = await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, "192.0.2.50"), "  mixed.case@EXAMPLE.test ", IdentitySeed.Password).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, relaxed.StatusCode);

        var padded = await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, "192.0.2.51"), "mixed.case@example.test", " " + IdentitySeed.Password).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Unauthorized, padded.StatusCode);
    }

    /// <summary>FR-009: a hash stored at a lower cost is upgraded at the next sign-in.</summary>
    [Fact]
    public async Task A_weaker_stored_hash_is_upgraded_at_sign_in()
    {
        var user = await IdentitySeed.SeedUserAsync(_app.Services, email: "legacy@example.test").ConfigureAwait(true);
        var weak = new IdentityOptions();
        weak.Passwords.Pbkdf2Iterations = 1_000;
        await SetHashAsync(user.Id, new Pbkdf2PasswordHasher(Options.Create(weak)).Hash(IdentitySeed.Password)).ConfigureAwait(true);

        var response = await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, "192.0.2.60"), "legacy@example.test", IdentitySeed.Password).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("$pbkdf2-sha512$i=210000$", await GetHashAsync(user.Id).ConfigureAwait(true), StringComparison.Ordinal);
    }

    private async Task DeactivateAsync(Guid userId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await context.Users.SingleAsync(u => u.Id == userId).ConfigureAwait(false);
        user.Deactivate(DateTimeOffset.UtcNow);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    private async Task SetHashAsync(Guid userId, string hash)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await context.Users.SingleAsync(u => u.Id == userId).ConfigureAwait(false);
        user.SetPasswordHash(hash, DateTimeOffset.UtcNow);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    private async Task<string?> GetHashAsync(Guid userId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .Users.Where(u => u.Id == userId).Select(u => u.PasswordHash).SingleAsync().ConfigureAwait(false);
    }
}
