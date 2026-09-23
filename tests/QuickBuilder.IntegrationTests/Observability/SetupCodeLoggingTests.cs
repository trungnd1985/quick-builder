using QuickBuilder.IntegrationTests.Identity;
using QuickBuilder.IntegrationTests.Infrastructure;
using Serilog.Events;
using Xunit;

namespace QuickBuilder.IntegrationTests.Observability;

/// <summary>
/// The setup code is the one secret allowed into the logs, and only in one place, only
/// before setup (spec FR-004, FR-004a; research R-09).
/// </summary>
/// <remarks>
/// These tests keep background services running, because printing the code is what the
/// application does at startup.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class SetupCodeLoggingTests(DatabaseFixture database)
{
    [Fact]
    public async Task The_code_is_printed_once_at_startup_and_nowhere_else()
    {
        var connectionString = await database.CreateDatabaseAsync("setuplog").ConfigureAwait(true);
        using var app = new QuickBuilderApp(connectionString, runBackgroundServices: true);
        _ = app.Services;

        var code = await SetupHelpers.GetSetupCodeAsync(app).ConfigureAwait(true);
        var formatted = $"{code[..4]}-{code[4..8]}-{code[8..]}";

        var banners = SetupCodeEvents(app).ToList();
        var banner = Assert.Single(banners);
        Assert.Equal(LogEventLevel.Warning, banner.Level);
        Assert.Equal(formatted, ((ScalarValue)banner.Properties["SetupCode"]).Value);
        Assert.StartsWith("SETUP CODE", banner.MessageTemplate.Text, StringComparison.Ordinal);

        // Complete setup, then look at every other event this instance wrote.
        (await SetupHelpers.PostSetupAsync(app.CreateAnonymousClient(), code).ConfigureAwait(true)).EnsureSuccessStatusCode();

        var elsewhere = app.Logs.Events
            .Where(e => !e.Properties.ContainsKey("SetupCode"))
            .Select(LogCapture.Render)
            .Where(text => text.Contains(code, StringComparison.OrdinalIgnoreCase)
                || text.Contains(formatted, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(elsewhere);
    }

    [Fact]
    public async Task Every_instance_prints_the_same_code_while_unclaimed()
    {
        var connectionString = await database.CreateDatabaseAsync("setuplog2").ConfigureAwait(true);
        using var first = new QuickBuilderApp(connectionString, runBackgroundServices: true);
        _ = first.Services;
        using var second = new QuickBuilderApp(connectionString, runBackgroundServices: true);
        _ = second.Services;

        var printedByFirst = Assert.Single(SetupCodeEvents(first)).Properties["SetupCode"].ToString();
        var printedBySecond = Assert.Single(SetupCodeEvents(second)).Properties["SetupCode"].ToString();

        Assert.Equal(printedByFirst, printedBySecond);
    }

    [Fact]
    public async Task No_code_is_printed_once_the_installation_is_claimed()
    {
        var connectionString = await database.CreateDatabaseAsync("setuplog3").ConfigureAwait(true);

        using (var first = new QuickBuilderApp(connectionString, runBackgroundServices: true))
        {
            _ = first.Services;
            var code = await SetupHelpers.GetSetupCodeAsync(first).ConfigureAwait(true);
            (await SetupHelpers.PostSetupAsync(first.CreateAnonymousClient(), code).ConfigureAwait(true)).EnsureSuccessStatusCode();
        }

        using var restarted = new QuickBuilderApp(connectionString, runBackgroundServices: true);
        _ = restarted.Services;

        Assert.Empty(SetupCodeEvents(restarted));
    }

    private static IEnumerable<LogEvent> SetupCodeEvents(QuickBuilderApp app) =>
        app.Logs.Events.Where(e => e.Properties.ContainsKey("SetupCode"));
}
