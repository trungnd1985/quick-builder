using QuickBuilder.Media;

namespace QuickBuilder.UnitTests.Media;

/// <summary>Media settings are validated at startup, naming the key (003 FR-007, FR-008).</summary>
public sealed class MediaOptionsValidationTests
{
    private readonly MediaOptionsValidator _validator = new();

    [Fact]
    public void The_defaults_are_valid_and_allow_all_six_types()
    {
        var options = Defaulted(new MediaOptions());

        Assert.True(_validator.Validate(null, options).Succeeded);
        Assert.Equal(6, options.AllowedTypes.Length);
        Assert.Equal(20 * 1024 * 1024, options.MaxFileSizeBytes);
    }

    [Fact]
    public void A_narrowed_list_is_valid()
    {
        var options = Defaulted(new MediaOptions { AllowedTypes = ["image/png", "image/jpeg"] });

        Assert.True(_validator.Validate(null, options).Succeeded);
        Assert.Equal(2, options.AllowedFileTypes.Count);
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("application/x-msdownload")]
    public void A_type_outside_the_list_fails_naming_the_key(string type)
    {
        var result = _validator.Validate(null, Defaulted(new MediaOptions { AllowedTypes = ["image/png", type] }));

        Assert.True(result.Failed);
        Assert.Contains("Media:AllowedTypes", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains(type, result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1023)]
    [InlineData(1_073_741_825)]
    public void An_out_of_range_size_fails_naming_the_key(long size)
    {
        var result = _validator.Validate(null, Defaulted(new MediaOptions { MaxFileSizeBytes = size }));

        Assert.True(result.Failed);
        Assert.Contains("Media:MaxFileSizeBytes", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("00:59:00")]
    [InlineData("31.00:00:00")]
    public void An_out_of_range_grace_period_fails_naming_the_key(string period)
    {
        var result = _validator.Validate(null, Defaulted(new MediaOptions { OrphanGracePeriod = TimeSpan.Parse(period, System.Globalization.CultureInfo.InvariantCulture) }));

        Assert.True(result.Failed);
        Assert.Contains("Media:OrphanGracePeriod", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void The_variant_defaults_are_valid()
    {
        var options = Defaulted(new MediaOptions());

        Assert.True(_validator.Validate(null, options).Succeeded);
        Assert.Equal([320, 640, 960, 1280, 1920, 2560], options.Variants.Widths);
        Assert.Equal(320, options.Variants.ThumbnailWidth);
        Assert.Equal(100_000_000, options.MaxImagePixels);
        Assert.InRange(options.Variants.MaxConcurrent, 1, 64);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Variants.Timeout);
    }

    [Fact]
    public void A_configured_width_list_replaces_the_default() =>
        Assert.Equal([400, 800], Defaulted(new MediaOptions { Variants = new VariantOptions { Widths = [400, 800] } }).Variants.Widths);

    [Theory]
    [InlineData(new[] { 640, 320 })]
    [InlineData(new[] { 320, 320 })]
    [InlineData(new[] { 32 })]
    [InlineData(new[] { 5000 })]
    [InlineData(new[] { 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000, 1100 })]
    public void A_bad_width_list_fails_naming_the_key(int[] widths)
    {
        var result = _validator.Validate(null, Defaulted(new MediaOptions { Variants = new VariantOptions { Widths = widths } }));

        Assert.True(result.Failed);
        Assert.Contains("Media:Variants:Widths", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(65, 10)]
    [InlineData(2, 0)]
    [InlineData(2, 61)]
    public void Concurrency_and_timeout_are_bounded(int concurrency, int timeoutSeconds)
    {
        var result = _validator.Validate(null, Defaulted(new MediaOptions
        {
            Variants = new VariantOptions { MaxConcurrent = concurrency, Timeout = TimeSpan.FromSeconds(timeoutSeconds) },
        }));

        Assert.True(result.Failed);
        Assert.Contains("Media:Variants:", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(999_999)]
    [InlineData(1_000_000_001)]
    public void The_pixel_limit_is_bounded(long pixels)
    {
        var result = _validator.Validate(null, Defaulted(new MediaOptions { MaxImagePixels = pixels }));

        Assert.True(result.Failed);
        Assert.Contains("Media:MaxImagePixels", result.FailureMessage, StringComparison.Ordinal);
    }

    private static MediaOptions Defaulted(MediaOptions options)
    {
        new MediaOptionsDefaults().PostConfigure(null, options);
        return options;
    }
}
