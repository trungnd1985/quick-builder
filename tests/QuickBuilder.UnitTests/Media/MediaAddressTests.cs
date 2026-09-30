using QuickBuilder.Media.Domain;

namespace QuickBuilder.UnitTests.Media;

/// <summary>One canonical address per content version and width (013 research MV-08, contracts/public-media.md §1).</summary>
public sealed class MediaAddressTests
{
    private const string Id = "7d41c0de5a2b4f6e8a9b0c1d2e3f4a5b";

    private static readonly HashSet<int> Widths = [320, 640, 960, 1280, 1920, 2560];

    [Fact]
    public void Version_one_original_keeps_the_003_form()
    {
        var address = MediaAddress.TryParse($"{Id}.jpg", Widths);

        Assert.Equal(new MediaAddress(Guid.ParseExact(Id, "N"), 1, null, "jpg"), address);
        Assert.Equal($"/_media/{Id}.jpg", address!.Value.ToPath());
        Assert.Equal(Id, address.Value.ETagValue);
    }

    [Fact]
    public void Later_originals_carry_their_version()
    {
        var address = MediaAddress.TryParse($"{Id}-v2.png", Widths)!.Value;

        Assert.Equal(2, address.Version);
        Assert.False(address.IsVariant);
        Assert.Equal($"/_media/{Id}-v2.png", address.ToPath());
        Assert.Equal($"{Id}-v2", address.ETagValue);
    }

    [Theory]
    [InlineData(1, 320)]
    [InlineData(7, 2560)]
    [InlineData(99_999, 640)]
    public void Variants_always_name_their_version(int version, int width)
    {
        var file = $"{Id}-v{version}-w{width}.webp";
        var address = MediaAddress.TryParse(file, Widths)!.Value;

        Assert.Equal(version, address.Version);
        Assert.Equal(width, address.Width);
        Assert.Equal("/_media/" + file, address.ToPath());
        Assert.Equal(file[..^5], address.ETagValue);
    }

    [Theory]
    [InlineData(Id + "-v1.jpg")]                     // version 1's original has one address
    [InlineData(Id + "-v01.jpg")]                    // leading zero
    [InlineData(Id + "-v0.jpg")]
    [InlineData(Id + "-v100000.jpg")]                // above 99,999
    [InlineData(Id + "-w320.jpg")]                   // a variant must name its version
    [InlineData(Id + "-v1-w700.jpg")]                // not a configured width
    [InlineData(Id + "-v1-w0320.jpg")]
    [InlineData(Id + "-v1-w320-v2.jpg")]
    [InlineData("7D41C0DE5A2B4F6E8A9B0C1D2E3F4A5B.jpg")] // upper case
    [InlineData(Id + ".JPG")]
    [InlineData(Id + ".")]
    [InlineData(Id)]
    [InlineData(Id + ".jpeg.jpg")]
    [InlineData("../" + Id + ".jpg")]
    [InlineData(Id + "/x.jpg")]
    [InlineData(Id + "%2e.jpg")]
    [InlineData("7d41c0de-5a2b-4f6e-8a9b-0c1d2e3f4a5b.jpg")]
    [InlineData("")]
    [InlineData(null)]
    public void Non_canonical_forms_do_not_parse(string? file) =>
        Assert.Null(MediaAddress.TryParse(file, Widths));

    [Fact]
    public void An_unconfigured_width_is_refused_even_when_well_formed() =>
        Assert.Null(MediaAddress.TryParse($"{Id}-v3-w1000.avif", Widths));

    [Theory]
    [InlineData(1, null)]
    [InlineData(4, null)]
    [InlineData(4, 960)]
    public void Formatting_round_trips(int version, int? width)
    {
        var address = new MediaAddress(Guid.ParseExact(Id, "N"), version, width, "avif");

        Assert.Equal(address, MediaAddress.TryParse(address.FileName, Widths));
    }
}
