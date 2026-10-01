using Microsoft.Extensions.Options;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Startup;

/// <summary>
/// SC-010: every storage and media misconfiguration stops startup with a message naming the
/// setting (003 FR-007, FR-022, FR-023, research M-05).
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Startup")]
public sealed class StorageStartupValidationTests(DatabaseFixture database) : IAsyncLifetime
{
    private string _connectionString = string.Empty;

    public async Task InitializeAsync() =>
        _connectionString = await database.CreateDatabaseAsync("storage-startup").ConfigureAwait(false);

    public Task DisposeAsync() => Task.CompletedTask;

    public static TheoryData<string, string, string> Misconfigurations => new()
    {
        { "Storage:Provider", "Dropbox", "Local, S3, AzureBlob" },
        { "Storage:Local:Path", "relative/path", "Storage:Local:Path" },
        { "Storage:Local:Path", Path.Combine(AppContext.BaseDirectory, "media"), "Storage:Local:Path" },
        { "Media:AllowedTypes:0", "image/svg+xml", "Media:AllowedTypes" },
        { "Media:MaxFileSizeBytes", "10", "Media:MaxFileSizeBytes" },
        { "Media:OrphanGracePeriod", "00:01:00", "Media:OrphanGracePeriod" },
    };

    [Theory]
    [MemberData(nameof(Misconfigurations))]
    public void A_bad_setting_stops_startup_naming_the_key(string key, string value, string named)
    {
        using var app = new QuickBuilderApp(_connectionString, settings: new Dictionary<string, string> { [key] = value });

        var failure = Assert.ThrowsAny<Exception>(() => app.Services);

        var validation = Unwrap(failure);
        Assert.Contains(named, validation.Message, StringComparison.Ordinal);
    }

    [SkippableFact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void A_location_that_cannot_be_written_stops_startup()
    {
        Skip.If(OperatingSystem.IsWindows(), "Unix file modes only.");
        Skip.If(Environment.UserName == "root", "Root ignores permission bits.");

        var readOnly = QuickBuilderApp.CreateTemporaryStorage();
        File.SetUnixFileMode(readOnly, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            using var app = new QuickBuilderApp(_connectionString, storagePath: readOnly);

            var failure = Assert.ThrowsAny<Exception>(() => app.Services);

            Assert.Contains("Storage:Local:Path", Unwrap(failure).Message, StringComparison.Ordinal);
        }
        finally
        {
            File.SetUnixFileMode(readOnly, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            QuickBuilderApp.TryDeleteDirectory(readOnly);
        }
    }

    /// <summary>The validation failure, however the host wraps it.</summary>
    private static OptionsValidationException Unwrap(Exception failure)
    {
        for (Exception? current = failure; current is not null; current = current.InnerException)
        {
            if (current is OptionsValidationException validation)
            {
                return validation;
            }

            if (current is AggregateException aggregate && aggregate.InnerExceptions.OfType<OptionsValidationException>().FirstOrDefault() is { } inner)
            {
                return inner;
            }
        }

        throw new Xunit.Sdk.XunitException($"Startup failed, but not with an options validation failure: {failure}");
    }
}
