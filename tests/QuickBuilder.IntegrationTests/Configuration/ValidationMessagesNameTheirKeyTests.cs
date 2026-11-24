using QuickBuilder.IntegrationTests.Infrastructure;
using QuickBuilder.IntegrationTests.Storage;

namespace QuickBuilder.IntegrationTests.Configuration;

/// <summary>
/// Every value startup refuses is refused with a message that names the setting, so an
/// operator finds it in the configuration reference by the key the message gives (026 T097;
/// FR-031, SC-007).
/// </summary>
/// <remarks>
/// Each case starts the real host with one invalid value and reads the validation failure it
/// stops with, as an operator reads it from the log. Remote storage settings are covered by
/// <see cref="S3StartupValidationTests"/> and <see cref="AzureStartupValidationTests"/>.
/// </remarks>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Startup")]
public sealed class ValidationMessagesNameTheirKeyTests(DatabaseFixture database) : IAsyncLifetime
{
    private string _connectionString = string.Empty;

    public async Task InitializeAsync() =>
        _connectionString = await database.CreateDatabaseAsync("validation-messages").ConfigureAwait(false);

    public Task DisposeAsync() => Task.CompletedTask;

    public static TheoryData<string, string> InvalidValues => new()
    {
        { "Database:ConnectionRetryTimeout", "01:00:00" },
        { "Database:ConnectionRetryDelay", "00:05:00" },
        { "Database:MigrationTimeout", "01:00:00" },
        { "Hosting:BasePath", "cms" },
        { "Hosting:PublicUrl", "cms.example.com" },
        { "Hosting:AdminHostNames:0", "https://admin.example.com/" },
        { "Identity:Session:IdleTimeout", "00:01:00" },
        { "Identity:Session:AbsoluteLifetime", "31.00:00:00" },
        { "Identity:SignIn:ClientFailureThreshold", "1" },
        { "Identity:SignIn:ClientBlockDuration", "2.00:00:00" },
        { "Identity:SignIn:AccountFailureThreshold", "0" },
        { "Identity:SignIn:AccountMaxDelay", "01:00:00" },
        { "Identity:SignIn:KnownDeviceFailureThreshold", "21" },
        { "Identity:SignIn:KnownDeviceLifetime", "400.00:00:00" },
        { "Identity:Passwords:Pbkdf2Iterations", "1000" },
        { "Identity:SetPasswordLinks:Lifetime", "00:01:00" },
        { "Identity:SecurityEvents:RetentionPeriod", "5000.00:00:00" },
        { "Storage:Provider", "Ftp" },
        { "Storage:Local:Path", "relative/media" },
        { "Media:MaxFileSizeBytes", "10" },
        { "Media:AllowedTypes:0", "image/svg+xml" },
        { "Media:OrphanGracePeriod", "00:01:00" },
        { "Media:MaxImagePixels", "5" },
        { "Media:Variants:Widths:0", "10" },
        { "Media:Variants:MaxConcurrent", "0" },
        { "Media:Variants:Timeout", "00:05:00" },
        { "SiteTransfer:MaxUploadSizeBytes", "10" },
        { "Outbox:PollInterval", "00:00:00" },
        { "Outbox:BatchSize", "0" },
        { "Outbox:HandlerTimeout", "00:00:00" },
        { "Outbox:MaxAttempts", "0" },
        { "Outbox:RetentionPeriod", "00:00:01" },
        { "Trash:RetentionDays", "0" },
        { "Preview:LinksValidFrom", "2999-01-01T00:00:00Z" },
        { "Email:Smtp:Port", "0" },
        { "Email:Smtp:Timeout", "00:00:01" },
        { "Email:Smtp:FromName", new string('x', 101) },
        { "Forms:RateLimit:PerMinute", "0" },
        { "Forms:RateLimit:PerHour", "0" },
        { "Cache:MemoryLimitMegabytes", "1" },
    };

    [Theory]
    [MemberData(nameof(InvalidValues))]
    public void An_invalid_value_stops_startup_with_a_message_naming_the_setting(string key, string value)
    {
        var settings = new Dictionary<string, string> { [key] = value };
        if (key.StartsWith("Email:Smtp:", StringComparison.Ordinal))
        {
            // Email settings are checked when email is on.
            settings["Email:Smtp:Host"] = "smtp.example.test";
            settings["Email:Smtp:From"] = "site@example.test";
        }

        var (message, _) = RemoteStartup.Fail(_connectionString, settings);

        // Lists are reported by their own key, without the entry's index.
        var named = key.EndsWith(":0", StringComparison.Ordinal) ? key[..^2] : key;
        Assert.Contains(named, message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_connection_string_names_the_setting()
    {
        var (message, _) = RemoteStartup.Fail(string.Empty, new Dictionary<string, string>());

        Assert.Contains("ConnectionStrings:Default", message, StringComparison.Ordinal);
    }
}
