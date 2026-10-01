using System.Net;
using Azure.Storage.Blobs.Models;
using QuickBuilder.IntegrationTests.Infrastructure;

namespace QuickBuilder.IntegrationTests.Storage;

/// <summary>
/// SC-011: when storage fails after start-up, readiness reports it without the secret
/// appearing anywhere (013 FR-033). The start-up failures are covered, case by case, by
/// <see cref="S3StartupValidationTests"/> and <see cref="AzureStartupValidationTests"/>.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SecretRedactionTests(DatabaseFixture database, MinioFixture minio, AzuriteFixture azurite)
    : IClassFixture<MinioFixture>, IClassFixture<AzuriteFixture>
{
    [Fact]
    public async Task S3_readiness_fails_when_the_bucket_disappears_without_logging_the_secret()
    {
        var connection = await database.CreateDatabaseAsync("s3-redaction");
        using var app = new QuickBuilderApp(connection, settings: minio.Configuration());
        using var client = IdentitySeed.CreateAnonymousClient(app);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/health/ready", UriKind.Relative))).StatusCode);

        using (var s3 = minio.CreateClient())
        {
            await s3.DeleteBucketAsync(minio.Bucket);
        }

        var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        var body = await ready.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.DoesNotContain(minio.SecretKey, body, StringComparison.Ordinal);
        Assert.DoesNotContain(minio.SecretKey, Logs(app), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Azure_readiness_fails_when_the_container_disappears_without_logging_the_connection_string()
    {
        var connection = await database.CreateDatabaseAsync("azure-redaction");
        using var app = new QuickBuilderApp(connection, settings: azurite.Configuration());
        using var client = IdentitySeed.CreateAnonymousClient(app);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/health/ready", UriKind.Relative))).StatusCode);

        await azurite.CreateClient().DeleteAsync(new BlobRequestConditions());

        var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        var body = await ready.Content.ReadAsStringAsync();
        var accountKey = azurite.ConnectionString.Split(';').Single(p => p.StartsWith("AccountKey=", StringComparison.Ordinal))["AccountKey=".Length..];

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        foreach (var secret in new[] { azurite.ConnectionString, accountKey })
        {
            Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, Logs(app), StringComparison.Ordinal);
        }
    }

    private static string Logs(QuickBuilderApp app) =>
        string.Join('\n', app.Logs.Events.Select(e => e.RenderMessage() + " " + e.Exception));
}
