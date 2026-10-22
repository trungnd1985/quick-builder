using System.Net;
using Microsoft.Extensions.Options;
using QuickBuilder.Shared.Kernel.Network;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Identity.Infrastructure.Security;

/// <summary>
/// Client identity for throttling, and the rule that credentials only travel over HTTPS
/// unless the client is on this machine (spec FR-018; research R-04).
/// </summary>
/// <remarks>
/// Both depend on forwarded headers being trusted only from configured proxies (001 D-12).
/// Behind an unlisted proxy every request looks like plain HTTP from the proxy's address:
/// credentials are refused with <c>identity.https_required</c>, which makes the
/// misconfiguration visible before it silently merges every client into one for
/// throttling (plan Risks).
/// </remarks>
internal sealed class TransportSecurity(IOptions<IdentityOptions> options)
{
    public static readonly Error HttpsRequired = new(
        "identity.https_required",
        "Sign-in requires HTTPS. Reach this installation through HTTPS, or from the same machine.");

    /// <summary>
    /// The throttling key for a client: the full IPv4 address, or the /64 prefix of an IPv6
    /// address, since one IPv6 subscriber typically controls a whole /64 (research R-07).
    /// </summary>
    public static string ClientKey(IPAddress? address) => ClientAddress.Group(address);

    public static bool IsLoopback(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var remote = context.Connection.RemoteIpAddress;
        return remote is not null && IPAddress.IsLoopback(remote);
    }

    /// <summary>
    /// Null when credentials may be accepted on this request; the error otherwise.
    /// </summary>
    public Error? RequireSecureTransport(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Request.IsHttps || IsLoopback(context) || options.Value.AllowInsecureHttp)
        {
            return null;
        }

        return HttpsRequired;
    }
}
