using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Infrastructure.Storage.AzureBlob;
using QuickBuilder.Shared.Infrastructure.Storage.S3;

namespace QuickBuilder.IntegrationTests.Storage;

/// <summary>
/// 003's conformance suite, unchanged, against the S3 provider on LocalStack (013 FR-030, SC-010).
/// Each test gets its own key prefix in the class's bucket: a fresh, empty store.
/// </summary>
public sealed class S3FileStorageConformanceTests(S3Fixture s3) : FileStorageConformanceTests, IClassFixture<S3Fixture>
{
    private S3FileStorage? _storage;

    protected override IFileStorage CreateStorage() => _storage = new S3FileStorage(s3.Settings());

    protected override void Cleanup() => _storage?.Dispose();
}

/// <summary>
/// 003's conformance suite, unchanged, against the Azure Blob provider on Azurite (013 FR-030,
/// SC-010). Each test gets its own key prefix in the class's container.
/// </summary>
public sealed class AzureBlobFileStorageConformanceTests(AzuriteFixture azurite) : FileStorageConformanceTests, IClassFixture<AzuriteFixture>
{
    protected override IFileStorage CreateStorage() => new AzureBlobFileStorage(azurite.Settings());
}
