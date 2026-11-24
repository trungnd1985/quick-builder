using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Configuration;

/// <summary>
/// #50: <c>Admin:PathPrefix</c> is deprecated and ignored, and startup says so while it is set.
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Startup")]
public sealed class DeprecatedAdminPathPrefixTests(DatabaseFixture database) : IAsyncLifetime
{
    private const string Warning = "Admin:PathPrefix is deprecated and ignored";

    private string _connectionString = string.Empty;

    public async Task InitializeAsync() =>
        _connectionString = await database.CreateDatabaseAsync("deprecated-admin-prefix").ConfigureAwait(false);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void Startup_warns_while_the_key_is_set_and_names_where_the_admin_is()
    {
        using var app = Start(new Dictionary<string, string> { ["Admin:PathPrefix"] = "/manage", ["Hosting:BasePath"] = "/cms" });

        var warning = Assert.Single(app.Logs.Events.Select(LogCapture.Render), text => text.Contains(Warning, StringComparison.Ordinal));
        Assert.Contains("/cms/admin", warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Startup_is_quiet_without_the_key(string? value)
    {
        var settings = new Dictionary<string, string>();
        if (value is not null)
        {
            settings["Admin:PathPrefix"] = value;
        }

        using var app = Start(settings);

        Assert.DoesNotContain(app.Logs.Events.Select(LogCapture.Render), text => text.Contains(Warning, StringComparison.Ordinal));
    }

    private QuickBuilderApp Start(Dictionary<string, string> settings)
    {
        var app = new QuickBuilderApp(_connectionString, settings: settings);
        try
        {
            _ = app.Services; // builds and starts the host
            return app;
        }
        catch
        {
            app.Dispose();
            throw;
        }
    }
}
