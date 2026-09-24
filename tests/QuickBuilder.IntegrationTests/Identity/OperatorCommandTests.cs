using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Startup;
using QuickBuilder.Shared.Infrastructure.Commands;
using Xunit;

namespace QuickBuilder.IntegrationTests.Identity;

/// <summary>
/// User Story 5: the operator regains access from the command line (spec FR-028, FR-004;
/// contracts/operator-commands.md).
/// </summary>
/// <remarks>
/// Commands are driven through the host's real <see cref="HostCommandRunner"/>, in process,
/// with standard output captured. Integration tests run one class at a time, so redirecting
/// the console here does not interleave with another test.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class OperatorCommandTests(DatabaseFixture database) : IAsyncLifetime
{
    private string _connectionString = string.Empty;
    private QuickBuilderApp _app = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await database.CreateDatabaseAsync("operator").ConfigureAwait(false);
        _app = new QuickBuilderApp(_connectionString);
        _ = _app.Services;
    }

    public Task DisposeAsync()
    {
        _app.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Setup_code_prints_the_code_while_unclaimed()
    {
        var code = await SetupHelpers.GetSetupCodeAsync(_app).ConfigureAwait(true);

        var (exit, output) = await RunAsync(_app, "identity", "setup-code").ConfigureAwait(true);

        Assert.Equal(0, exit);
        Assert.Contains($"{code[..4]}-{code[4..8]}-{code[8..]}", output, StringComparison.Ordinal);
        Assert.Equal(1, await CountRecoveryEventsAsync(SecurityEventOutcomes.Succeeded).ConfigureAwait(true));
    }

    [Fact]
    public async Task Setup_code_explains_when_setup_is_already_done()
    {
        await IdentitySeed.ClaimInstallationAsync(_app.Services).ConfigureAwait(true);

        var (exit, output) = await RunAsync(_app, "identity", "setup-code").ConfigureAwait(true);

        Assert.Equal(2, exit);
        Assert.Contains("Setup is complete", output, StringComparison.Ordinal);
    }

    /// <summary>US5 scenario 1: the operator gets a working link for the named administrator.</summary>
    [Fact]
    public async Task Recovery_link_restores_access_for_an_administrator()
    {
        await IdentitySeed.ClaimInstallationAsync(_app.Services).ConfigureAwait(true);
        await IdentitySeed.SeedUserAsync(_app.Services, email: "boss@example.test", administrator: true).ConfigureAwait(true);

        var (exit, output) = await RunAsync(_app, "identity", "recovery-link", "--email", "BOSS@example.test").ConfigureAwait(true);

        Assert.Equal(0, exit);
        Assert.Contains("/admin/set-password#token=", output, StringComparison.Ordinal);
        Assert.Contains("Prepend the address you use to reach the admin", output, StringComparison.Ordinal);

        var token = output.Split("#token=")[1].Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries)[0];
        var browser = SignInHelpers.Browser(_app, "192.0.2.77");
        Assert.Equal(HttpStatusCode.NoContent, (await UsersApi.SetPasswordAsync(browser, Uri.UnescapeDataString(token), "recovered passphrase 2026").ConfigureAwait(true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SignInHelpers.SignInAsync(browser, "boss@example.test", "recovered passphrase 2026").ConfigureAwait(true)).StatusCode);

        // US5 scenario 2: recorded, as issued by the operator.
        Assert.Equal(1, await CountRecoveryEventsAsync(SecurityEventOutcomes.Succeeded).ConfigureAwait(true));
        await using var scope = _app.Services.CreateAsyncScope();
        var issued = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .SecurityEvents.Where(e => e.EventType == SecurityEventTypes.SetPasswordLinkIssued)
            .Select(e => e.Details).SingleAsync().ConfigureAwait(true);
        using var details = System.Text.Json.JsonDocument.Parse(issued);
        Assert.Equal("operator", details.RootElement.GetProperty("issuedBy").GetString());
    }

    /// <summary>With a public address configured, the whole link is printed.</summary>
    [Fact]
    public async Task Recovery_link_is_complete_when_the_public_address_is_known()
    {
        using var app = new PublicUrlApp(_connectionString, "https://cms.example.com");
        await IdentitySeed.ClaimInstallationAsync(app.Services).ConfigureAwait(true);
        await IdentitySeed.SeedUserAsync(app.Services, email: "chief@example.test", administrator: true).ConfigureAwait(true);

        var (exit, output) = await RunAsync(app, "identity", "recovery-link", "--email", "chief@example.test").ConfigureAwait(true);

        Assert.Equal(0, exit);
        Assert.Contains("https://cms.example.com/admin/set-password#token=", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nobody@example.test")]
    [InlineData("editor@example.test")]
    public async Task Recovery_link_is_only_for_an_active_administrator(string email)
    {
        await IdentitySeed.ClaimInstallationAsync(_app.Services).ConfigureAwait(true);
        await IdentitySeed.SeedUserAsync(_app.Services, email: "editor@example.test").ConfigureAwait(true);

        var (exit, output) = await RunAsync(_app, "identity", "recovery-link", "--email", email).ConfigureAwait(true);

        Assert.Equal(4, exit);
        Assert.Contains("No active administrator has that email address.", output, StringComparison.Ordinal);
        Assert.Equal(1, await CountRecoveryEventsAsync(SecurityEventOutcomes.Refused).ConfigureAwait(true));
    }

    [Fact]
    public async Task Recovery_link_points_to_setup_while_unclaimed()
    {
        var (exit, output) = await RunAsync(_app, "identity", "recovery-link", "--email", "anyone@example.test").ConfigureAwait(true);

        Assert.Equal(2, exit);
        Assert.Contains("identity setup-code", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Commands_refuse_to_run_before_migrations()
    {
        var empty = await database.CreateDatabaseAsync("operatorempty").ConfigureAwait(true);
        using var app = new NoAutoMigrateApp(empty);

        var (exit, output) = await RunAsync(app, "identity", "setup-code").ConfigureAwait(true);

        Assert.Equal(3, exit);
        Assert.Contains("pending migrations", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_commands_and_missing_arguments_are_usage_errors()
    {
        Assert.Equal(HostCommandRunner.UnknownCommandExitCode, (await RunAsync(_app, "no-such-command").ConfigureAwait(true)).Exit);
        Assert.Equal(64, (await RunAsync(_app, "identity", "recovery-link").ConfigureAwait(true)).Exit);
        Assert.Equal(64, (await RunAsync(_app, "identity", "make-me-admin").ConfigureAwait(true)).Exit);
    }

    /// <summary>The token goes to standard output only — never into the log stream.</summary>
    [Fact]
    public async Task The_token_never_reaches_the_logs()
    {
        await IdentitySeed.ClaimInstallationAsync(_app.Services).ConfigureAwait(true);
        await IdentitySeed.SeedUserAsync(_app.Services, email: "quiet@example.test", administrator: true).ConfigureAwait(true);

        var (_, output) = await RunAsync(_app, "identity", "recovery-link", "--email", "quiet@example.test").ConfigureAwait(true);
        var token = output.Split("#token=")[1].Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries)[0];

        Assert.DoesNotContain(_app.Logs.Events.Select(LogCapture.Render), text => text.Contains(token, StringComparison.Ordinal));
    }

    private static async Task<(int Exit, string Output)> RunAsync(WebApplicationFactory<Program> app, params string[] args)
    {
        var original = Console.Out;
        await using var captured = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var originalError = Console.Error;
        Console.SetOut(captured);
        Console.SetError(captured);

        try
        {
            var exit = await HostCommandRunner.TryRunAsync(args, app.Services).ConfigureAwait(false);
            return (exit ?? -1, captured.ToString());
        }
        finally
        {
            Console.SetOut(original);
            Console.SetError(originalError);
        }
    }

    private async Task<int> CountRecoveryEventsAsync(string outcome)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .SecurityEvents.CountAsync(e => e.EventType == SecurityEventTypes.OperatorRecoveryUsed && e.Outcome == outcome)
            .ConfigureAwait(false);
    }

    /// <summary>The application with <c>Hosting:PublicUrl</c> set.</summary>
    private sealed class PublicUrlApp(string connectionString, string publicUrl) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Outbox:Enabled", "false");
            builder.UseSetting("Hosting:PublicUrl", publicUrl);
        }
    }
}
