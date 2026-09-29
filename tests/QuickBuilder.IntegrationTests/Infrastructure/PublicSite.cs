using System.Net;
using System.Text.RegularExpressions;
using QuickBuilder.IntegrationTests.Pages;
using QuickBuilder.IntegrationTests.Publishing;

namespace QuickBuilder.IntegrationTests.Infrastructure;

/// <summary>
/// Observing site resolution through the public surface, as a visitor would (012).
/// </summary>
/// <remarks>
/// Until 012 the public surface was one placeholder that reported which site a request
/// resolved to. Published pages replaced it, so tests now tell sites apart by what they serve:
/// <see cref="MarkAsync"/> publishes a home page whose heading is the site's id, and
/// <see cref="ResolvedSiteAsync"/> reads it back from <c>/</c>. A host that resolves to no site
/// gets 009's <c>sites.not_resolved</c> answer; a site with nothing published at an address
/// gets its own not-found page instead, which is how <see cref="IsResolvedAsync"/> tells them
/// apart without publishing anything.
/// </remarks>
internal static partial class PublicSite
{
    /// <summary>Publishes a home page on <paramref name="siteId"/> whose heading is the site id.</summary>
    public static async Task MarkAsync(HttpClient admin, Guid siteId)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var tree = await PagesApi.TreeOkAsync(admin, siteId).ConfigureAwait(false);
        var home = tree.GetProperty("pages").EnumerateArray().FirstOrDefault(p => p.GetProperty("isHome").GetBoolean());
        var homeId = home.ValueKind == System.Text.Json.JsonValueKind.Object
            ? home.GetProperty("id").GetGuid()
            : (await PagesApi.CreateOkAsync(admin, siteId, "marker", isHome: true).ConfigureAwait(false)).GetProperty("id").GetGuid();

        var path = PagesApi.PagePath(siteId, homeId);
        await PublishingApi.SaveAsync(admin, path, PublishingApi.HeadingDocument(siteId.ToString())).ConfigureAwait(false);
        await PublishingApi.PublishCurrentAsync(admin, path).ConfigureAwait(false);
    }

    /// <summary>The site a response from <c>/</c> came from, when that site was marked; null otherwise.</summary>
    public static async Task<Guid?> ResolvedSiteAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        var match = Marker().Match(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return match.Success ? Guid.Parse(match.Groups["id"].Value) : null;
    }

    /// <summary>Whether the request reached a site at all, whatever it serves there.</summary>
    public static async Task<bool> IsResolvedAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return response.StatusCode == HttpStatusCode.NotFound && !body.Contains("sites.not_resolved", StringComparison.Ordinal);
    }

    [GeneratedRegex("<h1[^>]*>(?<id>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})</h1>", RegexOptions.CultureInvariant)]
    private static partial Regex Marker();
}
