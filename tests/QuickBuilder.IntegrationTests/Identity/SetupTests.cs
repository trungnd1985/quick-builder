using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Identity.Infrastructure.Security;
using QuickBuilder.IntegrationTests.Infrastructure;
using Xunit;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>
/// User Story 1: the operator claims a fresh installation through the setup wizard
/// (spec FR-002 to FR-005, SC-011; research R-09, R-12).
/// </summary>
/// <remarks>
/// Each test that is refused uses its own client address, because throttling state is
/// shared by every test in this class's database.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class SetupTests(DatabaseFixture database) : IAsyncLifetime
{
    private string _connectionString = string.Empty;
    private QuickBuilderApp _app = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await database.CreateDatabaseAsync("setup").ConfigureAwait(false);
        _app = new QuickBuilderApp(_connectionString);
        _ = _app.Services;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>FR-005: nothing is seeded — no account, no grant, no default credential.</summary>
    [Fact]
    public async Task A_fresh_installation_has_no_accounts_at_all()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        Assert.Equal(0, await context.Users.CountAsync().ConfigureAwait(true));
        Assert.Equal(0, await context.RoleGrants.CountAsync().ConfigureAwait(true));
    }

    /// <summary>US1 scenario 2: the right code creates the first administrator and signs them in.</summary>
    [Fact]
    public async Task The_setup_code_creates_the_first_administrator_and_signs_them_in()
    {
        var code = await SetupHelpers.GetSetupCodeAsync(_app).ConfigureAwait(true);
        var client = SetupHelpers.ClientFrom(_app, "198.51.100.1");

        var response = await SetupHelpers.PostSetupAsync(client, Tokens.FormatSetupCode(code).ToLowerInvariant()).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var me = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
        Assert.True(me.GetProperty("isAdministrator").GetBoolean());
        Assert.Equal(SetupHelpers.AdminEmail, me.GetProperty("email").GetString());

        // FR-018: the session cookie is invisible to scripts and never sent cross-site.
        var setCookie = SetupHelpers.SetCookieHeader(response, IdentityCookies.Session);
        Assert.NotNull(setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);

        // The browser that claimed the installation is remembered (research R-07).
        Assert.NotNull(SetupHelpers.CookieValue(response, IdentityCookies.Device));

        var signedIn = IdentitySeed.CreateAnonymousClient(_app);
        signedIn.DefaultRequestHeaders.Add("Cookie", $"{IdentityCookies.Session}={SetupHelpers.CookieValue(response, IdentityCookies.Session)}");
        var meAgain = await signedIn.GetAsync(new Uri("/api/v1/identity/me", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, meAgain.StatusCode);

        Assert.Equal(1, await SetupHelpers.CountEventsAsync(_app, SecurityEventTypes.SetupCompleted).ConfigureAwait(true));
        Assert.Equal(1, await SetupHelpers.CountUsersAsync(_app).ConfigureAwait(true));

        // US1 scenario 3: setup is now closed for good.
        var again = await SetupHelpers.PostSetupAsync(
                SetupHelpers.ClientFrom(_app, "198.51.100.2"), code, email: "second@example.test")
            .ConfigureAwait(true);
        var problem = await again.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("identity.setup_already_completed", problem.GetProperty("errorCode").GetString());
        Assert.Equal(1, await SetupHelpers.CountUsersAsync(_app).ConfigureAwait(true));

        var status = await IdentitySeed.CreateAnonymousClient(_app)
            .GetFromJsonAsync<JsonElement>(new Uri("/api/v1/identity/status", UriKind.Relative))
            .ConfigureAwait(true);
        Assert.False(status.GetProperty("setupRequired").GetBoolean());

        await using var scope = _app.Services.CreateAsyncScope();
        var installation = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .Installation.AsNoTracking().SingleAsync().ConfigureAwait(true);
        Assert.Null(installation.SetupCode);
    }

    /// <summary>US1 scenario 5: a missing or wrong code creates nothing and is recorded.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AAAA-BBBB-CCCC")]
    public async Task A_missing_or_wrong_code_is_refused(string? code)
    {
        var client = SetupHelpers.ClientFrom(_app, $"198.51.100.{10 + (code?.Length ?? 0)}");
        var before = await SetupHelpers.CountEventsAsync(_app, SecurityEventTypes.SetupCodeRejected).ConfigureAwait(true);

        var response = await SetupHelpers.PostSetupAsync(client, code).ConfigureAwait(true);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("identity.setup_code_invalid", problem.GetProperty("errorCode").GetString());
        Assert.Equal(0, await SetupHelpers.CountUsersAsync(_app).ConfigureAwait(true));
        Assert.Equal(before + 1, await SetupHelpers.CountEventsAsync(_app, SecurityEventTypes.SetupCodeRejected).ConfigureAwait(true));
    }

    /// <summary>
    /// US1 scenario 5: guessing is rate-limited. After five wrong codes the client is
    /// blocked, even for the right code (research R-07).
    /// </summary>
    [Fact]
    public async Task Repeated_wrong_codes_block_the_client()
    {
        var code = await SetupHelpers.GetSetupCodeAsync(_app).ConfigureAwait(true);
        var client = SetupHelpers.ClientFrom(_app, "198.51.100.50");

        for (var i = 0; i < 5; i++)
        {
            var wrong = await SetupHelpers.PostSetupAsync(client, "WRNG-CODE-0000").ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        }

        var blocked = await SetupHelpers.PostSetupAsync(client, code).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(0, await SetupHelpers.CountUsersAsync(_app).ConfigureAwait(true));
        Assert.Equal(1, await SetupHelpers.CountEventsAsync(_app, SecurityEventTypes.ClientBlocked).ConfigureAwait(true));

        // Another client is unaffected.
        var other = await SetupHelpers.PostSetupAsync(SetupHelpers.ClientFrom(_app, "198.51.100.51"), code).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
    }

    /// <summary>US1 scenario 4, FR-003: simultaneous submissions produce exactly one account.</summary>
    [Fact]
    public async Task Parallel_submissions_create_exactly_one_account()
    {
        var code = await SetupHelpers.GetSetupCodeAsync(_app).ConfigureAwait(true);

        var attempts = Enumerable.Range(0, 10)
            .Select(i => SetupHelpers.PostSetupAsync(
                SetupHelpers.ClientFrom(_app, $"198.51.100.{100 + i}"),
                code,
                email: $"racer{i}@example.test"));

        var responses = await Task.WhenAll(attempts).ConfigureAwait(true);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(
            responses.Where(r => r.StatusCode != HttpStatusCode.Created),
            r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(1, await SetupHelpers.CountUsersAsync(_app).ConfigureAwait(true));
    }

    /// <summary>Spec edge case "several instances": one installation, one code.</summary>
    [Fact]
    public async Task Two_instances_on_one_database_share_one_setup_code()
    {
        using var second = new QuickBuilderApp(_connectionString);

        var fromFirst = await SetupHelpers.GetSetupCodeAsync(_app).ConfigureAwait(true);
        var fromSecond = await SetupHelpers.GetSetupCodeAsync(second).ConfigureAwait(true);

        Assert.Equal(fromFirst, fromSecond);
    }

    /// <summary>
    /// FR-018 / research R-04: credentials never cross the network in clear text, but the
    /// operator's own machine may use plain HTTP.
    /// </summary>
    [Fact]
    public async Task Plain_http_is_refused_from_another_machine_and_allowed_from_this_one()
    {
        var code = await SetupHelpers.GetSetupCodeAsync(_app).ConfigureAwait(true);

        var remote = await SetupHelpers.PostSetupAsync(SetupHelpers.ClientFrom(_app, "203.0.113.9", https: false), code)
            .ConfigureAwait(true);
        var problem = await remote.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Forbidden, remote.StatusCode);
        Assert.Equal("identity.https_required", problem.GetProperty("errorCode").GetString());
        Assert.Equal(0, await SetupHelpers.CountUsersAsync(_app).ConfigureAwait(true));

        var local = await SetupHelpers.PostSetupAsync(SetupHelpers.ClientFrom(_app, "127.0.0.1", https: false), code)
            .ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Created, local.StatusCode);
    }
}
