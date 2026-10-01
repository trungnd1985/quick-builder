using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using QuickBuilder.Shared.Infrastructure.Storage;
using Testcontainers.Minio;

namespace QuickBuilder.IntegrationTests.Storage;

/// <summary>
/// An S3-compatible service for the S3 provider's tests (013 tasks T071): one MinIO container
/// per test run, and a fresh bucket per test class.
/// </summary>
/// <remarks>
/// The container is shared across classes and collections and started on first use, so a
/// run that touches no S3 test never starts it. Like the PostgreSQL container, it needs
/// Docker. The image is pinned to a release that supports conditional writes, which the
/// provider requires.
/// </remarks>
public sealed class MinioFixture : IAsyncLifetime
{
    public const string Image = "minio/minio:latest";

    private static readonly Lazy<Task<MinioContainer>> Shared = new(StartAsync);

    private MinioContainer _container = null!;

    /// <summary>This class's bucket.</summary>
    public string Bucket { get; } = $"qb-{Guid.NewGuid():N}";

    /// <summary>The service URL as the test host reaches it.</summary>
    public string ServiceUrl => _container.GetConnectionString();

    public string AccessKey => _container.GetAccessKey();

    public string SecretKey => _container.GetSecretKey();

    public async Task InitializeAsync()
    {
        _container = await Shared.Value.ConfigureAwait(false);
        using var client = CreateClient();
        await client.PutBucketAsync(new PutBucketRequest { BucketName = Bucket }).ConfigureAwait(false);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Provider settings for this bucket, with a key prefix of its own.</summary>
    public S3StorageSettings Settings(string? keyPrefix = null) => new()
    {
        Bucket = Bucket,
        Region = "us-east-1",
        ServiceUrl = ServiceUrl,
        ForcePathStyle = true,
        AccessKeyId = AccessKey,
        SecretAccessKey = SecretKey,
        KeyPrefix = keyPrefix ?? $"t-{Guid.NewGuid():N}/",
    };

    /// <summary>The same settings as configuration keys, for a host.</summary>
    public Dictionary<string, string> Configuration(string? keyPrefix = null)
    {
        var settings = Settings(keyPrefix);
        return new Dictionary<string, string>
        {
            ["Storage:Provider"] = "S3",
            ["Storage:S3:Bucket"] = settings.Bucket,
            ["Storage:S3:Region"] = settings.Region,
            ["Storage:S3:ServiceUrl"] = settings.ServiceUrl,
            ["Storage:S3:ForcePathStyle"] = "true",
            ["Storage:S3:AccessKeyId"] = settings.AccessKeyId,
            ["Storage:S3:SecretAccessKey"] = settings.SecretAccessKey,
            ["Storage:S3:KeyPrefix"] = settings.KeyPrefix,
        };
    }

    /// <summary>A plain client for inspecting the bucket from tests.</summary>
    public AmazonS3Client CreateClient() => new(
        new BasicAWSCredentials(AccessKey, SecretKey),
        new AmazonS3Config { ServiceURL = ServiceUrl, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });

    /// <summary>The MinIO container itself, for pausing it.</summary>
    public string ContainerId => _container.Id;

    private static async Task<MinioContainer> StartAsync()
    {
        var container = new MinioBuilder(Image).Build();
        await container.StartAsync().ConfigureAwait(false);
        return container;
    }
}
