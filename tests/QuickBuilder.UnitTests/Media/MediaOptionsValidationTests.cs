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

    private static MediaOptions Defaulted(MediaOptions options)
    {
        new MediaOptionsDefaults().PostConfigure(null, options);
        return options;
    }
}
