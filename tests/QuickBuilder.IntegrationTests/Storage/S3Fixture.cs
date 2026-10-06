using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using QuickBuilder.Shared.Infrastructure.Storage;

namespace QuickBuilder.IntegrationTests.Storage;

/// <summary>
/// An S3-compatible service for the S3 provider's tests (013 tasks T071): one LocalStack
/// container per test run, and a fresh bucket per test class.
/// </summary>
/// <remarks>
/// <para>
/// The container is shared across classes and collections and started on first use, so a
/// run that touches no S3 test never starts it. Like the PostgreSQL container, it needs
/// Docker.
/// </para>
/// <para>
/// <b>Why LocalStack.</b> The provider needs conditional writes on both single and
/// multipart uploads. Probed on 2026-10-06: LocalStack 4.9 refuses both with 412 and lists
/// pending uploads by prefix, as AWS does. MinIO behaves correctly but no longer publishes
/// container images, so CI cannot pull it; S3Mock answers a refused multipart completion
/// with 304 instead of 412. The image is pinned; LocalStack accepts any credentials.
/// </para>
/// </remarks>
public sealed class S3Fixture : IAsyncLifetime
{
    public const string Image = "localstack/localstack:4.9";

    public const string AccessKey = "test";

    public const string SecretKey = "test-secret-access-key";

    private const int Port = 4566;

    private static readonly Lazy<Task<IContainer>> Shared = new(StartContainerAsync);

    private IContainer _container = null!;

    /// <summary>This class's bucket.</summary>
    public string Bucket { get; } = $"qb-{Guid.NewGuid():N}";

    /// <summary>The service URL as the test host reaches it.</summary>
    public string ServiceUrl => UrlOf(_container);

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
    public Dictionary<string, string> Configuration(string? keyPrefix = null) =>
        ConfigurationFor(ServiceUrl, Bucket, keyPrefix ?? $"t-{Guid.NewGuid():N}/");

    /// <summary>Configuration keys for a bucket on any S3 service started by this fixture.</summary>
    public static Dictionary<string, string> ConfigurationFor(string serviceUrl, string bucket, string keyPrefix = "") => new()
    {
        ["Storage:Provider"] = "S3",
        ["Storage:S3:Bucket"] = bucket,
        ["Storage:S3:Region"] = "us-east-1",
        ["Storage:S3:ServiceUrl"] = serviceUrl,
        ["Storage:S3:ForcePathStyle"] = "true",
        ["Storage:S3:AccessKeyId"] = AccessKey,
        ["Storage:S3:SecretAccessKey"] = SecretKey,
        ["Storage:S3:KeyPrefix"] = keyPrefix,
    };

    /// <summary>A plain client for inspecting the bucket from tests.</summary>
    public AmazonS3Client CreateClient() => ClientFor(ServiceUrl);

    public static AmazonS3Client ClientFor(string serviceUrl) => new(
        new BasicAWSCredentials(AccessKey, SecretKey),
        new AmazonS3Config { ServiceURL = serviceUrl, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });

    /// <summary>
    /// A service of its own, for a test that pauses or stops it: doing that to the shared one
    /// would stall every other class using it. The caller disposes it.
    /// </summary>
    public static async Task<IContainer> StartDedicatedAsync() => await StartContainerAsync().ConfigureAwait(false);

    public static string UrlOf(IContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        return $"http://{container.Hostname}:{container.GetMappedPublicPort(Port)}";
    }

    private static async Task<IContainer> StartContainerAsync()
    {
        var container = new ContainerBuilder(Image)
            .WithEnvironment("SERVICES", "s3")
            .WithPortBinding(Port, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
                .ForPort(Port)
                .ForPath("/_localstack/health")
                .ForResponseMessageMatching(async response =>
                    (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Contains("\"s3\": \"available\"", StringComparison.Ordinal)
                    || (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Contains("\"s3\": \"running\"", StringComparison.Ordinal))))
            .Build();
        await container.StartAsync().ConfigureAwait(false);
        return container;
    }
}
