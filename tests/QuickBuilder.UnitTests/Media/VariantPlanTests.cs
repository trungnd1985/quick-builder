using QuickBuilder.Media.Domain;

namespace QuickBuilder.UnitTests.Media;

/// <summary>Which files have variants, at which widths (013 FR-021, research MV-11, data-model.md §3.1).</summary>
public sealed class VariantPlanTests
{
    private static readonly int[] Configured = [320, 640, 960, 1280, 1920, 2560];

    [Theory]
    [InlineData("image/jpeg", false, (int)MediaMetadataState.Recorded, (int)MediaVariantsState.Available, true, true)]
    [InlineData("image/png", false, (int)MediaMetadataState.Recorded, (int)MediaVariantsState.Available, true, true)]
    [InlineData("image/webp", false, (int)MediaMetadataState.Recorded, (int)MediaVariantsState.Available, true, true)]
    [InlineData("image/avif", false, (int)MediaMetadataState.Recorded, (int)MediaVariantsState.Available, true, true)]
    [InlineData("image/avif", false, (int)MediaMetadataState.Recorded, (int)MediaVariantsState.Available, false, false)]
    [InlineData("image/gif", false, (int)MediaMetadataState.Recorded, (int)MediaVariantsState.Available, true, false)]
    [InlineData("image/webp", true, (int)MediaMetadataState.Recorded, (int)MediaVariantsState.Available, true, false)]
    [InlineData("image/jpeg", false, (int)MediaMetadataState.Pending, (int)MediaVariantsState.Available, true, false)]
    [InlineData("image/jpeg", false, (int)MediaMetadataState.OverLimit, (int)MediaVariantsState.Available, true, false)]
    [InlineData("image/jpeg", false, (int)MediaMetadataState.Unreadable, (int)MediaVariantsState.Available, true, false)]
    [InlineData("image/jpeg", false, (int)MediaMetadataState.Recorded, (int)MediaVariantsState.Unavailable, true, false)]
    [InlineData("application/pdf", false, (int)MediaMetadataState.NotApplicable, (int)MediaVariantsState.Available, true, false)]
    public void Eligibility_follows_the_data_model(
        string type, bool animated, int metadata, int variants, bool avif, bool expected) =>
        Assert.Equal(expected, VariantPlan.IsEligible(type, animated, (MediaMetadataState)metadata, (MediaVariantsState)variants, avif));

    [Theory]
    [InlineData(6_000, new[] { 320, 640, 960, 1280, 1920, 2560 })]
    [InlineData(1_000, new[] { 320, 640, 960 })]
    [InlineData(960, new[] { 320, 640 })]
    [InlineData(320, new int[0])]
    [InlineData(64, new int[0])]
    public void Only_widths_strictly_narrower_than_the_image_are_offered(int width, int[] expected) =>
        Assert.Equal(expected, VariantPlan.Widths(width, Configured));

    [Fact]
    public void An_image_without_dimensions_has_no_widths() => Assert.Empty(VariantPlan.Widths(null, Configured));

    [Theory]
    [InlineData(2_560, true)]
    [InlineData(1_000, true)]
    [InlineData(2_561, false)]
    [InlineData(6_000, false)]
    public void The_original_is_offered_only_when_no_wider_than_the_largest_width(int width, bool expected) =>
        Assert.Equal(expected, VariantPlan.OfferOriginal(width, Configured));
}
