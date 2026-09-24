using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.IntegrationTests.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>
/// Throttling without lockout (spec FR-015, FR-015a, SC-005; research R-07).
/// </summary>
/// <remarks>
/// The clock is moved by hand, so the waits — 1 s, 2 s, 4 s … up to 60 s — are asserted
/// exactly rather than slept through. Every scenario uses its own accounts and client
/// addresses, because counters are shared by all tests in this class's database.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class ThrottlingTests(DatabaseFixture database) : IAsyncLifetime
{
    private const string Wrong = "definitely the wrong password";

    private readonly ManualClock _clock = new();
    private string _connectionString = string.Empty;
    private QuickBuilderApp _app = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await database.CreateDatabaseAsync("throttle").ConfigureAwait(false);
        _app = CreateApp();
        _ = _app.Services;
        await IdentitySeed.ClaimInstallationAsync(_app.Services).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// US2 scenario 5: five failures from one client block that client for 15 minutes,
    /// even with the right password.
    /// </summary>
    [Fact]
    public async Task A_client_that_keeps_failing_is_blocked()
    {
        await IdentitySeed.SeedUserAsync(_app.Services, email: "target1@example.test").ConfigureAwait(true);
        var attacker = SignInHelpers.Browser(_app, "203.0.113.1");
        var blocksBefore = await SetupHelpers.CountEventsAsync(_app, SecurityEventTypes.ClientBlocked).ConfigureAwait(true);

        // Different unknown addresses each time, so the per-account wait never engages and
        // every failure counts against the client.
        for (var i = 0; i < 5; i++)
        {
            var failed = await SignInHelpers.SignInAsync(attacker, $"guess{i}@example.test", Wrong).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var blocked = await SignInHelpers.SignInAsync(attacker, "target1@example.test", IdentitySeed.Password).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.InRange(blocked.Headers.RetryAfter!.Delta!.Value, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
        Assert.Equal(blocksBefore + 1, await SetupHelpers.CountEventsAsync(_app, SecurityEventTypes.ClientBlocked).ConfigureAwait(true));

        _clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));
        var afterBlock = await SignInHelpers.SignInAsync(attacker, "target1@example.test", IdentitySeed.Password).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, afterBlock.StatusCode);
    }

    /// <summary>
    /// FR-015: from the third failure on one account the wait doubles, is capped at 60 s,
    /// and attempts inside the wait are not even evaluated.
    /// </summary>
    [Fact]
    public async Task An_account_under_attack_slows_down_but_never_locks()
    {
        await IdentitySeed.SeedUserAsync(_app.Services, email: "target2@example.test").ConfigureAwait(true);
        var ip = 0;
        HttpClient Fresh() => SignInHelpers.Browser(_app, $"203.0.113.{100 + ip++}");

        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInHelpers.SignInAsync(Fresh(), "target2@example.test", Wrong).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInHelpers.SignInAsync(Fresh(), "target2@example.test", Wrong).ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInHelpers.SignInAsync(Fresh(), "target2@example.test", Wrong).ConfigureAwait(true)).StatusCode);

        foreach (var expectedWait in new[] { 1, 2, 4, 8, 16, 32, 60, 60 })
        {
            // Inside the wait: refused without evaluating, even the right password.
            var early = await SignInHelpers.SignInAsync(Fresh(), "target2@example.test", IdentitySeed.Password).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.TooManyRequests, early.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(expectedWait), early.Headers.RetryAfter!.Delta);

            _clock.Advance(TimeSpan.FromSeconds(expectedWait));
            Assert.Equal(HttpStatusCode.Unauthorized, (await SignInHelpers.SignInAsync(Fresh(), "target2@example.test", Wrong).ConfigureAwait(true)).StatusCode);
        }

        // Never locked: once the (capped) wait passes, the right password works.
        _clock.Advance(TimeSpan.FromSeconds(60));
        var owner = await SignInHelpers.SignInAsync(Fresh(), "target2@example.test", IdentitySeed.Password).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
    }

    /// <summary>FR-015a: an address with no account is throttled exactly like a real one.</summary>
    [Fact]
    public async Task Unknown_addresses_are_throttled_like_real_ones()
    {
        await IdentitySeed.SeedUserAsync(_app.Services, email: "target3@example.test").ConfigureAwait(true);
        var ip = 0;
        HttpClient Fresh() => SignInHelpers.Browser(_app, $"203.0.113.{200 + ip++}");

        async Task<List<HttpStatusCode>> SequenceAsync(string email)
        {
            var statuses = new List<HttpStatusCode>();
            for (var i = 0; i < 4; i++)
            {
                statuses.Add((await SignInHelpers.SignInAsync(Fresh(), email, Wrong).ConfigureAwait(true)).StatusCode);
            }

            return statuses;
        }

        var real = await SequenceAsync("target3@example.test").ConfigureAwait(true);
        var unknown = await SequenceAsync("nobody3@example.test").ConfigureAwait(true);

        Assert.Equal(real, unknown);
        Assert.Equal(HttpStatusCode.TooManyRequests, real[3]);
    }

    /// <summary>FR-015: two instances share one set of counters.</summary>
    [Fact]
    public async Task Counters_are_shared_across_instances()
    {
        await IdentitySeed.SeedUserAsync(_app.Services, email: "target4@example.test").ConfigureAwait(true);
        using var second = CreateApp();
        _ = second.Services;

        for (var i = 0; i < 3; i++)
        {
            await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, $"198.18.0.{i + 1}"), "target4@example.test", Wrong).ConfigureAwait(true);
        }

        var onSecond = await SignInHelpers.SignInAsync(SignInHelpers.Browser(second, "198.18.0.9"), "target4@example.test", IdentitySeed.Password).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.TooManyRequests, onSecond.StatusCode);
    }

    /// <summary>
    /// SC-005: while strangers keep the account's wait running, the owner signs in at once
    /// from a browser they have used before, and within 60 s from a new one.
    /// </summary>
    [Fact]
    public async Task The_owner_is_never_locked_out()
    {
        await IdentitySeed.SeedUserAsync(_app.Services, email: "owner5@example.test").ConfigureAwait(true);

        // The owner has signed in before from this browser.
        var first = await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, "192.0.2.200"), "owner5@example.test", IdentitySeed.Password).ConfigureAwait(true);
        var device = SignInHelpers.Cookie(first, IdentityCookies.Device);

        await KeepAccountUnderAttackAsync("owner5@example.test", "203.0.114").ConfigureAwait(true);

        var knownBrowser = await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, "192.0.2.201", device), "owner5@example.test", IdentitySeed.Password).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, knownBrowser.StatusCode);

        await KeepAccountUnderAttackAsync("owner5@example.test", "203.0.115").ConfigureAwait(true);

        var newBrowser = SignInHelpers.Browser(_app, "192.0.2.202");
        var waited = TimeSpan.Zero;
        HttpResponseMessage attempt;
        while ((attempt = await SignInHelpers.SignInAsync(newBrowser, "owner5@example.test", IdentitySeed.Password).ConfigureAwait(true)).StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = attempt.Headers.RetryAfter!.Delta!.Value;
            waited += retryAfter;
            _clock.Advance(retryAfter);
        }

        Assert.Equal(HttpStatusCode.OK, attempt.StatusCode);
        Assert.True(waited <= TimeSpan.FromSeconds(60), $"The owner waited {waited} on a new browser.");
    }

    /// <summary>A device cookie never gets past a blocked client.</summary>
    [Fact]
    public async Task A_known_device_does_not_bypass_a_client_block()
    {
        await IdentitySeed.SeedUserAsync(_app.Services, email: "owner6@example.test").ConfigureAwait(true);
        var first = await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, "192.0.2.210"), "owner6@example.test", IdentitySeed.Password).ConfigureAwait(true);
        var device = SignInHelpers.Cookie(first, IdentityCookies.Device);

        var sharedAddress = "192.0.2.211";
        for (var i = 0; i < 5; i++)
        {
            await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, sharedAddress), $"other{i}@example.test", Wrong).ConfigureAwait(true);
        }

        var blocked = await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, sharedAddress, device), "owner6@example.test", IdentitySeed.Password).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    /// <summary>
    /// A stolen device cookie cannot grind passwords: after five failures the device loses
    /// its exemption and the revocation is recorded.
    /// </summary>
    [Fact]
    public async Task A_device_that_keeps_failing_loses_its_exemption()
    {
        await IdentitySeed.SeedUserAsync(_app.Services, email: "owner7@example.test").ConfigureAwait(true);
        var first = await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, "192.0.2.220"), "owner7@example.test", IdentitySeed.Password).ConfigureAwait(true);
        var device = SignInHelpers.Cookie(first, IdentityCookies.Device);
        var revokedBefore = await SetupHelpers.CountEventsAsync(_app, SecurityEventTypes.KnownDeviceRevoked).ConfigureAwait(true);

        for (var i = 0; i < 5; i++)
        {
            var failed = await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, $"192.0.3.{i + 1}", device), "owner7@example.test", Wrong).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        Assert.Equal(revokedBefore + 1, await SetupHelpers.CountEventsAsync(_app, SecurityEventTypes.KnownDeviceRevoked).ConfigureAwait(true));

        // The account's wait is running from those failures; without the exemption the
        // right password now waits too.
        var afterRevocation = await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, "192.0.3.99", device), "owner7@example.test", IdentitySeed.Password).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.TooManyRequests, afterRevocation.StatusCode);
    }

    /// <summary>A device remembered for one account gives nothing on another.</summary>
    [Fact]
    public async Task A_device_is_known_only_for_its_own_account()
    {
        await IdentitySeed.SeedUserAsync(_app.Services, email: "owner8@example.test").ConfigureAwait(true);
        await IdentitySeed.SeedUserAsync(_app.Services, email: "other8@example.test").ConfigureAwait(true);
        var first = await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, "192.0.2.230"), "owner8@example.test", IdentitySeed.Password).ConfigureAwait(true);
        var device = SignInHelpers.Cookie(first, IdentityCookies.Device);

        await KeepAccountUnderAttackAsync("other8@example.test", "203.0.116").ConfigureAwait(true);

        var elsewhere = await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, "192.0.2.231", device), "other8@example.test", IdentitySeed.Password).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.TooManyRequests, elsewhere.StatusCode);
    }

    /// <summary>Fails against the account from several fresh clients until its wait is running.</summary>
    private async Task KeepAccountUnderAttackAsync(string email, string network)
    {
        for (var i = 0; i < 4; i++)
        {
            var attempt = await SignInHelpers.SignInAsync(SignInHelpers.Browser(_app, $"{network}.{i + 1}"), email, Wrong).ConfigureAwait(true);
            if (attempt.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return;
            }
        }
    }

    private QuickBuilderApp CreateApp() =>
        new(_connectionString, configureTestServices: services => services.AddSingleton<TimeProvider>(_clock));
}
