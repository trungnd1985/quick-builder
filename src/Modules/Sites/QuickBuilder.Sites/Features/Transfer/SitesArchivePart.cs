using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Sites.Contracts.Transfer;
using QuickBuilder.Sites.Domain;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.Transfer;

/// <summary>
/// The Sites module's share of an archive: settings, host names and search settings
/// (020 contracts/archive-parts.md §3; FR-001).
/// </summary>
internal sealed partial class SitesArchivePart(SitesDbContext database, IServiceProvider services, SitesOptions hosting) : ISiteArchivePart
{
    public const string PartName = "sites";

    public string Name => PartName;

    public int Order => 10;

    public int Version => 1;

    public IReadOnlyDictionary<string, int> Contracts { get; } = new Dictionary<string, int>();

    public async Task ExportAsync(PartExportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        database.JoinTransfer(context.Session.Connection, context.Session.Transaction);

        var site = await database.Sites
            .AcrossAllSites("Export of the site named by the transfer job (020 FR-001).")
            .AsNoTracking()
            .Include(s => s.HostNames)
            .SingleAsync(s => s.Id == context.SiteId, cancellationToken)
            .ConfigureAwait(false);

        context.Authors.Add(site.SearchUpdatedBy);

        await context.Archive.WriteLinesAsync(PartName, "settings", One(new SiteSettingsRow(
            site.Id,
            site.Name,
            site.Scheme,
            site.DefaultLanguage,
            site.IsEnabled,
            site.PrimaryHostName,
            site.CreatedAtUtc)), cancellationToken).ConfigureAwait(false);

        await context.Archive.WriteLinesAsync(PartName, "host-names", Many(site.HostNames
            .OrderBy(h => h.CreatedAtUtc)
            .Select(h => new HostNameRow(h.Id, h.HostName, h.HostName == site.PrimaryHostName))), cancellationToken).ConfigureAwait(false);

        await context.Archive.WriteLinesAsync(PartName, "search", One(new SearchRow(
            site.SearchTitleSuffix,
            site.SearchDefaultImage,
            site.SearchOrganisationName,
            site.SearchOrganisationLogo,
            site.SearchDiscouraged,
            site.SearchRobotsRules,
            site.RedirectOtherHosts,
            site.SearchUpdatedBy)), cancellationToken).ConfigureAwait(false);

        context.Count("host_names", site.HostNames.Count);
    }

    private static async IAsyncEnumerable<T> One<T>(T row)
    {
        yield return row;
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<T> Many<T>(IEnumerable<T> rows)
    {
        foreach (var row in rows)
        {
            yield return row;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}

/// <summary><c>parts/sites/settings.jsonl</c>: the site (one line).</summary>
internal sealed record SiteSettingsRow(
    Guid Id,
    string Name,
    string Scheme,
    string DefaultLanguage,
    bool Enabled,
    string PrimaryHostName,
    DateTimeOffset CreatedAtUtc);

/// <summary><c>parts/sites/host-names.jsonl</c>: one host name.</summary>
internal sealed record HostNameRow(Guid Id, string HostName, bool IsPrimary);

/// <summary><c>parts/sites/search.jsonl</c>: the site's search settings (017 FR-006, FR-014; one line).</summary>
internal sealed record SearchRow(
    string? TitleSuffix,
    Guid? DefaultImageId,
    string? OrganisationName,
    Guid? OrganisationLogoId,
    bool Discouraged,
    string? RobotsRules,
    bool RedirectOtherHosts,
    Guid? UpdatedBy);
