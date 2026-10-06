using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Storage;

namespace QuickBuilder.UnitTests.Media;

/// <summary>Originals and variants of every version have grammar-valid, site-first keys (013 research MV-14).</summary>
public sealed class MediaStorageKeysTests
{
    private static readonly Guid Site = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid File = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public void Version_one_keeps_the_003_key()
    {
        Assert.Equal(MediaStorageKeys.For(Site, File), MediaStorageKeys.For(Site, File, 1));
        Assert.Equal($"{Site:N}/media/{File:N}", MediaStorageKeys.For(Site, File, 1).Value);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(99_999)]
    public void Later_versions_are_siblings_that_still_belong_to_the_site(int version)
    {
        var key = MediaStorageKeys.For(Site, File, version);

        Assert.True(StorageKey.IsValid(key.Value));
        Assert.Equal($"{Site:N}/media/{File:N}-v{version}", key.Value);
        Assert.Equal(Site, MediaStorageKeys.SiteOf(key));
    }

    [Theory]
    [InlineData(1, 320)]
    [InlineData(99_999, 4096)]
    public void Variant_keys_round_trip(int version, int width)
    {
        var key = MediaStorageKeys.Variant(Site, File, version, width);

        Assert.True(StorageKey.IsValid(key.Value));
        Assert.StartsWith(MediaStorageKeys.VariantSitePrefix(Site), key.Value, StringComparison.Ordinal);
        Assert.Equal(new VariantKey(Site, File, version, width), MediaStorageKeys.ParseVariant(key));
    }

    [Fact]
    public void Variants_are_never_taken_for_originals_and_the_reverse()
    {
        var variant = MediaStorageKeys.Variant(Site, File, 3, 640);
        var original = MediaStorageKeys.For(Site, File, 3);

        Assert.Null(MediaStorageKeys.SiteOf(variant));
        Assert.Null(MediaStorageKeys.ParseVariant(original));
    }

    [Theory]
    [InlineData("11111111222233334444555555555555/media/aaaaaaaabbbbccccddddeeeeeeeeeeee-v0")]
    [InlineData("11111111222233334444555555555555/media/aaaaaaaabbbbccccddddeeeeeeeeeeee-v1-w320")]
    [InlineData("11111111222233334444555555555555/other/aaaaaaaabbbbccccddddeeeeeeeeeeee")]
    [InlineData("11111111222233334444555555555555/media/aaaaaaaabbbbccccddddeeeeeeeeeeee/x")]
    public void Other_shapes_are_not_originals(string key) =>
        Assert.Null(MediaStorageKeys.SiteOf(new StorageKey(key)));
}
