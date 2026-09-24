using System.Diagnostics;
using System.Net;
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
/// Sign-in reveals nothing about which accounts exist — not in the answer, not in the time
/// it takes (spec FR-014, SC-006; research R-08).
/// </summary>
/// <remarks>
/// This test swaps in a cheaper password hasher. What it measures is whether the two paths
/// do the same work — an unknown account still runs a full verification against a dummy
/// hash — which does not depend on how expensive one verification is. At the production
/// cost, 1,000 attempts would take minutes; the SC-013 performance test covers that cost.
/// Throttle rows are cleared between attempts, outside the timed section, so every attempt
/// is evaluated rather than refused.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class CredentialOracleTests(DatabaseFixture database) : IAsyncLifetime
{
    private const int Attempts = 1_000;
    private const int KnownAccounts = 20;

    private QuickBuilderApp _app = null!;

    public async Task InitializeAsync()
    {
        _app = new QuickBuilderApp(
            await database.CreateDatabaseAsync("oracle").ConfigureAwait(false),
            configureTestServices: services =>
            {
                var cheaper = new IdentityOptions();
                cheaper.Passwords.Pbkdf2Iterations = 20_000;
                services.AddSingleton(new Pbkdf2PasswordHasher(Options.Create(cheaper)));
            });
        _ = _app.Services;
        await IdentitySeed.ClaimInstallationAsync(_app.Services).ConfigureAwait(false);

        for (var i = 0; i < KnownAccounts; i++)
        {
            await IdentitySeed.SeedUserAsync(_app.Services, email: $"known{i}@example.test").ConfigureAwait(false);
        }
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Unknown_accounts_and_wrong_passwords_look_and_take_the_same()
    {
        var unknownTimes = new List<double>();
        var wrongTimes = new List<double>();
        var bodies = new HashSet<string>(StringComparer.Ordinal);

        // Warm up both paths so JIT and connection pooling do not count against either.
        await AttemptAsync($"warmup@example.test", 0).ConfigureAwait(true);
        await AttemptAsync($"known0@example.test", 1).ConfigureAwait(true);

        for (var i = 0; i < Attempts; i++)
        {
            // Interleaved, so drift over the run affects both groups alike.
            var unknown = i % 2 == 0;
            var email = unknown ? $"nobody{i}@example.test" : $"known{i % KnownAccounts}@example.test";

            var (elapsed, status, body) = await AttemptAsync(email, i).ConfigureAwait(true);

            Assert.Equal(HttpStatusCode.Unauthorized, status);
            bodies.Add(body);
            (unknown ? unknownTimes : wrongTimes).Add(elapsed);
        }

        Assert.Single(bodies);

        var unknownMedian = Median(unknownTimes);
        var wrongMedian = Median(wrongTimes);
        var difference = Math.Abs(unknownMedian - wrongMedian) / Math.Max(unknownMedian, wrongMedian);

        Assert.True(
            difference < 0.10,
            $"Median sign-in time differs by {difference:P1}: unknown {unknownMedian:F1} ms, wrong password {wrongMedian:F1} ms.");
    }

    private async Task<(double ElapsedMs, HttpStatusCode Status, string Body)> AttemptAsync(string email, int index)
    {
        var client = SignInHelpers.Browser(_app, $"10.{(index >> 16) & 255}.{(index >> 8) & 255}.{index & 255}");

        var stopwatch = Stopwatch.StartNew();
        var response = await SignInHelpers.SignInAsync(client, email, "not the password at all").ConfigureAwait(false);
        stopwatch.Stop();

        var body = await SignInHelpers.BodyWithoutTraceIdAsync(response).ConfigureAwait(false);

        await using var scope = _app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .Database.ExecuteSqlRawAsync("DELETE FROM identity.sign_in_throttles").ConfigureAwait(false);

        return (stopwatch.Elapsed.TotalMilliseconds, response.StatusCode, body);
    }

    private static double Median(List<double> values)
    {
        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
    }
}
