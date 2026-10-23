using System.Net;
using System.Net.Sockets;

namespace QuickBuilder.Shared.Kernel.Network;

/// <summary>
/// Who a request comes from, for limits that count per client (002 research R-07; 018
/// research FS-13): the full IPv4 address, or the /64 prefix of an IPv6 address, since one
/// IPv6 subscriber typically controls a whole /64.
/// </summary>
/// <remarks>
/// The address is the one ASP.NET Core resolved, which trusts forwarded headers only from the
/// proxies named in <c>Hosting:KnownProxies</c> (001 D-12). Behind an unlisted proxy every
/// visitor looks like the proxy and shares one limit.
/// </remarks>
public static class ClientAddress
{
    /// <summary>The grouping key; <c>unknown</c> when the server has no address.</summary>
    public static string Group(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }
}
