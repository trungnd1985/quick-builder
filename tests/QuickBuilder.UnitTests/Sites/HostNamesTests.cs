using QuickBuilder.Shared.Kernel.MultiSite;
using Xunit;

namespace QuickBuilder.UnitTests.Sites;

/// <summary>
/// The one definition of a host name (009 research S-04), used for storage, comparison,
/// resolution and admin host names alike.
/// </summary>
/// <remarks>
/// SC-004 asks for at least 40 host names across equivalent spellings and invalid forms.
/// The two theories below hold 46.
/// </remarks>
public sealed class HostNamesTests
{
    /// <summary>Spellings that must all mean the same stored name.</summary>
    [Theory]
    [InlineData("example.test", "example.test")]
    [InlineData("EXAMPLE.TEST", "example.test")]
    [InlineData("Example.Test", "example.test")]
    [InlineData("  example.test  ", "example.test")]
    [InlineData("\texample.test\n", "example.test")]
    [InlineData("example.test.", "example.test")]
    [InlineData("EXAMPLE.TEST.", "example.test")]
    [InlineData("bücher.example", "xn--bcher-kva.example")]
    [InlineData("BÜCHER.example", "xn--bcher-kva.example")]
    [InlineData("xn--bcher-kva.example", "xn--bcher-kva.example")]
    [InlineData("XN--BCHER-KVA.EXAMPLE", "xn--bcher-kva.example")]
    [InlineData("bücher.example.", "xn--bcher-kva.example")]
    [InlineData("localhost", "localhost")]
    [InlineData("LocalHost", "localhost")]
    [InlineData("a.b", "a.b")]
    [InlineData("blog.example.co.uk", "blog.example.co.uk")]
    [InlineData("a-b.example", "a-b.example")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("127.0.0.1.", "127.0.0.1")]
    [InlineData("10.0.0.255", "10.0.0.255")]
    [InlineData("::1", "::1")]
    [InlineData("[::1]", "::1")]
    [InlineData("0:0:0:0:0:0:0:1", "::1")]
    [InlineData("[2001:DB8::1]", "2001:db8::1")]
    public void Equivalent_spellings_normalise_to_one_value(string input, string expected)
    {
        Assert.True(HostNames.TryNormalise(input, out var normalised), $"'{input}' was refused");
        Assert.Equal(expected, normalised);
    }

    /// <summary>Forms FR-010 refuses: structure that is not a host name, or out of bounds.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("http://example.test")]
    [InlineData("example.test:8080")]
    [InlineData("example.test/path")]
    [InlineData("example.test?query")]
    [InlineData("example.test#fragment")]
    [InlineData("user@example.test")]
    [InlineData("*.example.test")]
    [InlineData("exa mple.test")]
    [InlineData("a..b")]
    [InlineData(".example.test")]
    [InlineData("-leading.test")]
    [InlineData("trailing-.test")]
    [InlineData("under_score.test")]
    [InlineData("example.test..")]
    [InlineData("127.0.0.1:80")]
    [InlineData("[::1]:8080")]
    [InlineData("999.1.1.1")]
    [InlineData("1.2.3")]
    [InlineData("fe80::1%eth0")]
    public void Structural_and_out_of_bounds_forms_are_refused(string? input)
    {
        Assert.False(HostNames.TryNormalise(input, out _), $"'{input}' was accepted");
    }

    [Fact]
    public void A_label_may_be_63_characters_but_not_64()
    {
        Assert.True(HostNames.TryNormalise(new string('a', 63) + ".test", out _));
        Assert.False(HostNames.TryNormalise(new string('a', 64) + ".test", out _));
    }

    [Fact]
    public void A_name_may_be_253_characters_but_not_254()
    {
        // Four 62-character labels and dots make 251; add a 1-character label for 253.
        var label = new string('a', 62);
        var at253 = $"{label}.{label}.{label}.{label}.b";
        Assert.Equal(253, at253.Length);

        Assert.True(HostNames.TryNormalise(at253, out _));
        Assert.False(HostNames.TryNormalise(at253 + "b", out _));
    }

    [Theory]
    [InlineData("xn--bcher-kva.example", "bücher.example")]
    [InlineData("example.test", "example.test")]
    [InlineData("::1", "::1")]
    public void Display_form_is_the_readable_one(string normalised, string display)
    {
        Assert.Equal(display, HostNames.ToDisplay(normalised));
    }

    /// <summary>
    /// A stored name that cannot be decoded is still shown, as it is, rather than failing
    /// the screen that lists it.
    /// </summary>
    [Fact]
    public void Display_falls_back_to_the_ascii_form_when_it_cannot_be_decoded()
    {
        Assert.Equal("xn--zz.test", HostNames.ToDisplay("xn--zz.test"));
    }
}
