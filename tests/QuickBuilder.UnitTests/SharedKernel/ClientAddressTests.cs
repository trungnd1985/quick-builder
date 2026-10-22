using System.Net;
using QuickBuilder.Shared.Kernel.Network;

namespace QuickBuilder.UnitTests.SharedKernel;

/// <summary>How clients are grouped for per-client limits (002 research R-07; 018 research FS-13).</summary>
public sealed class ClientAddressTests
{
    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1:2::10", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2:ffff:ffff:ffff:ffff", "2001:db8:1:2::/64")]
    public void Addresses_are_grouped_as_one_client(string address, string expected) =>
        Assert.Equal(expected, ClientAddress.Group(IPAddress.Parse(address)));

    [Fact]
    public void No_address_is_one_unknown_client() => Assert.Equal("unknown", ClientAddress.Group(null));
}
