using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Sites.Infrastructure;

/// <summary>
/// A site as the HTTP API returns it (contracts/sites-api.yaml, <c>Site</c>).
/// </summary>
/// <remarks>
/// Every field 001–003 returned keeps its name and shape (research S-17). The additions
/// that only make sense over HTTP live here rather than on <see cref="SiteDto"/>:
/// display forms of host names for the admin shell, and what the caller may do, which
/// depends on who is asking.
/// </remarks>
public sealed record SiteResponse(
    Guid Id,
    string Name,
    IReadOnlyList<string> HostNames,
    DateTimeOffset CreatedAtUtc,
    string PrimaryHostName,
    IReadOnlyList<HostNameResponse> HostNameDetails,
    string Scheme,
    string BaseUrl,
    string DefaultLanguage,
    string Status,
    uint Version,
    DateTimeOffset UpdatedAtUtc,
    SitePermissionsResponse Permissions,
    SiteSearchResponse Search,
    SiteSearchVisibilityResponse SearchVisibility);

/// <summary>The site's search settings (017 FR-006, seo-api.yaml <c>SiteSearch</c>).</summary>
/// <param name="UpdatedBy">Who last changed the search settings or visibility (FR-021).</param>
public sealed record SiteSearchResponse(
    string? TitleSuffix,
    Guid? DefaultImageId,
    string? OrganisationName,
    Guid? OrganisationLogoId,
    Guid? UpdatedBy);

/// <summary>Whether search engines may index the site, and how its host names answer (017 FR-014, FR-025, FR-027).</summary>
public sealed record SiteSearchVisibilityResponse(bool DiscourageSearchEngines, string? RobotsRules, bool RedirectOtherHostNames);

/// <summary>One host name, in stored and readable form.</summary>
public sealed record HostNameResponse(string HostName, string DisplayName, bool IsPrimary);

/// <summary>
/// What the calling user may do on the site, worked out on the server for this request.
/// </summary>
/// <remarks>
/// The admin shell hides or disables controls from it, following Media's per-file
/// <c>canDelete</c>. It is a convenience for the interface, never the enforcement: every
/// action declares its capability and is checked again when it arrives.
/// </remarks>
public sealed record SitePermissionsResponse(
    bool CanManageSettings,
    bool CanManageHostNames,
    bool CanManageAvailability,
    bool CanManageSearch = false,
    bool CanManageSearchVisibility = false);

internal static class SiteResponses
{
    internal static async Task<SiteResponse> BuildAsync(
        SiteDto site,
        IAccessEvaluator access,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(access);

        var permissions = new SitePermissionsResponse(
            await access.HasCapabilityAsync(SitesCapabilities.SettingsManage, site.Id, cancellationToken).ConfigureAwait(false),
            await access.HasCapabilityAsync(SitesCapabilities.ManageHostNames, site.Id, cancellationToken).ConfigureAwait(false),
            await access.HasCapabilityAsync(SitesCapabilities.AvailabilityManage, site.Id, cancellationToken).ConfigureAwait(false),
            await access.HasCapabilityAsync(SitesCapabilities.SearchManage, site.Id, cancellationToken).ConfigureAwait(false),
            await access.HasCapabilityAsync(SitesCapabilities.SearchVisibilityManage, site.Id, cancellationToken).ConfigureAwait(false));

        return new SiteResponse(
            site.Id,
            site.Name,
            site.HostNames,
            site.CreatedAtUtc,
            site.PrimaryHostName,
            site.HostNames
                .Select(h => new HostNameResponse(
                    h,
                    SiteDtoMapper.Display(h),
                    string.Equals(h, site.PrimaryHostName, StringComparison.Ordinal)))
                .ToList(),
            site.Scheme,
            site.BaseUrl,
            site.DefaultLanguage,
            site.Status,
            site.Version,
            site.UpdatedAtUtc,
            permissions,
            new SiteSearchResponse(
                site.Search.TitleSuffix,
                site.Search.DefaultImageId,
                site.Search.OrganisationName,
                site.Search.OrganisationLogoId,
                site.Search.UpdatedBy),
            new SiteSearchVisibilityResponse(site.Search.Discouraged, site.Search.RobotsRules, site.RedirectOtherHostNames));
    }

    internal static async Task<IReadOnlyList<SiteResponse>> BuildAsync(
        IEnumerable<SiteDto> sites,
        IAccessEvaluator access,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sites);

        var responses = new List<SiteResponse>();
        foreach (var site in sites)
        {
            responses.Add(await BuildAsync(site, access, cancellationToken).ConfigureAwait(false));
        }

        return responses;
    }
}
