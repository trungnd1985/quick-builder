using Azure.Storage.Blobs;
using QuickBuilder.Shared.Infrastructure.Storage;
using Testcontainers.Azurite;

namespace QuickBuilder.IntegrationTests.Storage;

/// <summary>
/// The Azure Storage emulator for the Azure Blob provider's tests (013 tasks T071): one
/// Azurite container per test run, and a fresh blob container per test class.
/// </summary>
/// <remarks>
/// Shared and started on first use, like <see cref="MinioFixture"/>. The API version check is
/// skipped because the client library may be newer than the emulator.
/// </remarks>
public sealed class AzuriteFixture : IAsyncLifetime
{
    public const string Image = "mcr.microsoft.com/azure-storage/azurite:3.35.0";

    private static readonly Lazy<Task<AzuriteContainer>> Shared = new(StartAsync);

    private AzuriteContainer _container = null!;

    /// <summary>This class's blob container.</summary>
    public string Container { get; } = $"qb-{Guid.NewGuid():N}";

    /// <summary>The emulator's well-known development connection string.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        _container = await Shared.Value.ConfigureAwait(false);
        await new BlobContainerClient(ConnectionString, Container).CreateAsync().ConfigureAwait(false);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Provider settings for this container, with a key prefix of its own.</summary>
    public AzureBlobStorageSettings Settings(string? keyPrefix = null) => new()
    {
        Container = Container,
        ConnectionString = ConnectionString,
        KeyPrefix = keyPrefix ?? $"t-{Guid.NewGuid():N}/",
    };

    /// <summary>The same settings as configuration keys, for a host.</summary>
    public Dictionary<string, string> Configuration(string? keyPrefix = null)
    {
        var settings = Settings(keyPrefix);
        return new Dictionary<string, string>
        {
            ["Storage:Provider"] = "AzureBlob",
            ["Storage:AzureBlob:Container"] = settings.Container,
            ["Storage:AzureBlob:ConnectionString"] = settings.ConnectionString,
            ["Storage:AzureBlob:KeyPrefix"] = settings.KeyPrefix,
        };
    }

    public BlobContainerClient CreateClient() => new(ConnectionString, Container);

    private static async Task<AzuriteContainer> StartAsync()
    {
        var container = new AzuriteBuilder(Image)
            .WithCommand("--skipApiVersionCheck", "--loose")
            .Build();
        await container.StartAsync().ConfigureAwait(false);
        return container;
    }
}
