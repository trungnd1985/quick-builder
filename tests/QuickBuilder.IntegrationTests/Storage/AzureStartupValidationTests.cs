using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Storage;

/// <summary>
/// Each Azure Blob misconfiguration stops the host before it listens, naming the key, and
/// neither the message nor any log line carries the connection string or its account key
/// (013 contracts/storage-providers.md §5; FR-033, SC-011).
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Startup")]
public sealed class AzureStartupValidationTests(DatabaseFixture database, AzuriteFixture azurite) : IAsyncLifetime, IClassFixture<AzuriteFixture>
{
    // Valid base64, but not the emulator's key.
    private const string WrongAccountKey = "d3JvbmctYWNjb3VudC1rZXktd3JvbmctYWNjb3VudC1rZXktd3JvbmctYWNjb3VudC1rZXk=";

    private string _connectionString = string.Empty;

    public async Task InitializeAsync() =>
        _connectionString = await database.CreateDatabaseAsync("azure-startup").ConfigureAwait(false);

    public Task DisposeAsync() => Task.CompletedTask;

    public static TheoryData<string, string> Misconfigurations => new()
    {
        { "both", "Storage:AzureBlob:AccountUrl" },
        { "neither", "Storage:AzureBlob:ConnectionString" },
        { "no-container", "Storage:AzureBlob:Container" },
        { "wrong-key", "Storage:AzureBlob:ConnectionString" },
        { "missing-container", "Storage:AzureBlob:Container" },
        { "unreachable", "Storage:AzureBlob:ConnectionString" },
        { "malformed", "Storage:AzureBlob:ConnectionString" },
        { "no-identity", "Storage:AzureBlob:AccountUrl" },
    };

    [Theory]
    [MemberData(nameof(Misconfigurations))]
    public void A_misconfiguration_stops_startup_naming_the_key_without_secrets(string problem, string key)
    {
        var settings = azurite.Configuration();
        var secrets = new List<string> { azurite.ConnectionString, AccountKeyOf(azurite.ConnectionString) };

        switch (problem)
        {
            case "both":
                settings["Storage:AzureBlob:AccountUrl"] = "https://account.blob.core.windows.net";
                break;
            case "neither":
                settings.Remove("Storage:AzureBlob:ConnectionString");
                break;
            case "no-container":
                settings.Remove("Storage:AzureBlob:Container");
                break;
            case "wrong-key":
                settings["Storage:AzureBlob:ConnectionString"] = azurite.ConnectionString.Replace(AccountKeyOf(azurite.ConnectionString), WrongAccountKey, StringComparison.Ordinal);
                secrets.Add(WrongAccountKey);
                break;
            case "missing-container":
                settings["Storage:AzureBlob:Container"] = $"missing-{Guid.NewGuid():N}";
                break;
            case "unreachable":
                settings["Storage:AzureBlob:ConnectionString"] =
                    $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={AccountKeyOf(azurite.ConnectionString)};BlobEndpoint=http://127.0.0.1:1/devstoreaccount1;";
                break;
            case "malformed":
                settings["Storage:AzureBlob:ConnectionString"] = $"AccountKey={WrongAccountKey} and nothing else that parses";
                secrets.Add(WrongAccountKey);
                break;
            case "no-identity":
                settings.Remove("Storage:AzureBlob:ConnectionString");
                settings["Storage:AzureBlob:AccountUrl"] = "https://127.0.0.1:1/devstoreaccount1";
                break;
        }

        var (message, logs) = RemoteStartup.Fail(_connectionString, settings);

        Assert.Contains(key, message, StringComparison.Ordinal);
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, message, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, logs, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_working_configuration_starts()
    {
        using var app = new QuickBuilderApp(_connectionString, settings: azurite.Configuration());

        Assert.NotNull(app.Services);
    }

    private static string AccountKeyOf(string connectionString) =>
        connectionString.Split(';').Single(p => p.StartsWith("AccountKey=", StringComparison.Ordinal))["AccountKey=".Length..];
}
