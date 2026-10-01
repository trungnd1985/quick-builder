using System.Security.Cryptography;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Infrastructure.Storage.AzureBlob;

namespace QuickBuilder.IntegrationTests.Storage;

/// <summary>What the Azure Blob provider does beyond the conformance suite (013 contracts/storage-providers.md §4, §7).</summary>
public sealed class AzureBlobFileStorageTests(AzuriteFixture azurite) : IClassFixture<AzuriteFixture>
{
    [Fact]
    public async Task The_key_prefix_is_added_on_write_and_removed_from_listings()
    {
        var storage = new AzureBlobFileStorage(azurite.Settings("tenant-one/"));
        await storage.WriteAsync(new StorageKey("site-a/media/one"), new MemoryStream([1]));

        var names = new List<string>();
        await foreach (var blob in azurite.CreateClient().GetBlobsAsync(BlobTraits.None, BlobStates.None, "tenant-one/", default))
        {
            names.Add(blob.Name);
        }

        Assert.Equal(["tenant-one/site-a/media/one"], names);
        Assert.Equal(["site-a/media/one"], await KeysAsync(storage));
    }

    [Fact]
    public async Task Probe_objects_are_not_listed()
    {
        var storage = new AzureBlobFileStorage(azurite.Settings());
        Assert.True(await storage.CheckAsync());

        await azurite.CreateClient().GetBlobClient(storage.KeyPrefix + RemoteProbe.Folder + "left-behind").UploadAsync(new BinaryData([1]));

        Assert.Empty(await KeysAsync(storage));
    }

    [Fact]
    public async Task A_missing_container_reads_as_missing_rather_than_failing()
    {
        var settings = azurite.Settings();
        settings.Container = $"missing-{Guid.NewGuid():N}";
        var storage = new AzureBlobFileStorage(settings);

        Assert.Null(await storage.OpenReadAsync(new StorageKey("site-a/media/one")));
        Assert.False(await storage.ExistsAsync(new StorageKey("site-a/media/one")));
        Assert.False(await storage.CheckAsync());
    }

    [Fact]
    public async Task A_large_upload_is_staged_in_blocks_and_committed_once()
    {
        var storage = new AzureBlobFileStorage(azurite.Settings());
        var bytes = RandomNumberGenerator.GetBytes((AzureBlobFileStorage.BlockSize * 2) + 12_345);
        var key = new StorageKey("site-a/media/large");

        await storage.WriteAsync(key, new MemoryStream(bytes));

        var blocks = await azurite.CreateClient().GetBlockBlobClient(storage.KeyPrefix + key.Value).GetBlockListAsync();
        Assert.Equal(3, blocks.Value.CommittedBlocks.Count());

        await using var stored = await storage.OpenReadAsync(key);
        using var copy = new MemoryStream();
        await stored!.Content.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());

        // A second large write to the same name is refused at the commit.
        await Assert.ThrowsAsync<StorageKeyExistsException>(() => storage.WriteAsync(key, new MemoryStream(bytes)));
    }

    private static async Task<List<string>> KeysAsync(AzureBlobFileStorage storage)
    {
        var keys = new List<string>();
        await foreach (var entry in storage.ListAsync(string.Empty))
        {
            keys.Add(entry.Key.Value);
        }

        return keys;
    }
}
