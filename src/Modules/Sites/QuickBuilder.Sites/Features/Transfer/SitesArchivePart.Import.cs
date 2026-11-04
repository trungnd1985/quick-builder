using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Kernel.MultiSite;
using QuickBuilder.Sites.Contracts;
using QuickBuilder.Sites.Contracts.Events;
using QuickBuilder.Sites.Contracts.Transfer;
using QuickBuilder.Sites.Domain;
using QuickBuilder.Sites.Features.Transfer.Engine;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.Transfer;

/// <summary>Checking and applying the Sites part (020 FR-020; research ST-10, ST-12).</summary>
internal sealed partial class SitesArchivePart
{
    public async Task CheckAsync(PartCheckContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var settings = await SettingsAsync(context.Archive, cancellationToken).ConfigureAwait(false);
        if (settings is null)
        {
            context.Report.Error("transfer.document_invalid", subject: null, "The archive has no site settings (parts/sites/settings.jsonl).");
            return;
        }

        var hostNames = await Lines<HostNameRow>(context.Archive, "host-names", cancellationToken).ConfigureAwait(false);
        context.Report.Count(ArchiveKinds.HostName, hostNames.Count);

        if (context.Mode == ImportMode.NewSite && context.Report is CheckReport report)
        {
            // FR-020: a host name another site holds is left out; the others are taken.
            var directory = services.GetRequiredService<ISiteDirectory>();
            foreach (var hostName in hostNames)
            {
                if (!HostNames.TryNormalise(hostName.HostName, out var normalised))
                {
                    context.Report.Error("transfer.document_invalid", new TransferSubject(ArchiveKinds.HostName, hostName.Id, hostName.HostName), "Not a valid host name.");
                }
                else if (await directory.IsHostNameTakenAsync(normalised, cancellationToken).ConfigureAwait(false) || hosting.IsAdminHostName(normalised))
                {
                    context.Report.Warning("transfer.host_name_taken", new TransferSubject(ArchiveKinds.HostName, hostName.Id, normalised), "Used by another site here; it will not be added.");
                    context.Report.HostNameLeftOut(normalised);
                }
                else
                {
                    report.HostNameKept(normalised);
                }
            }
        }
        else if (context.Mode == ImportMode.Merge && context.TargetSiteId is { } target)
        {
            // FR-027: a merge never adds or changes host names; the settings change only under
            // replace, which the check lists as a conflict with the site itself.
            context.Report.Conflict(new TransferSubject(ArchiveKinds.Site, context.Manifest.Source.SiteId, settings.Name), target, MatchedBy.Identity);
        }
    }

    public async Task ApplyAsync(PartApplyContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        database.JoinTransfer(context.Session.Connection, context.Session.Transaction);
        if (context.Mode != ImportMode.NewSite)
        {
            await ApplyToExistingAsync(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        var options = services.GetRequiredService<NewSiteOptions>();
        var directory = services.GetRequiredService<ISiteDirectory>();
        var clock = services.GetRequiredService<TimeProvider>();

        var settings = await SettingsAsync(context.Archive, cancellationToken).ConfigureAwait(false)
            ?? throw new ArchiveFormatException("The archive has no site settings.");
        var search = (await Lines<SearchRow>(context.Archive, "search", cancellationToken).ConfigureAwait(false)).SingleOrDefault();

        var kept = new List<string>();
        foreach (var hostName in await Lines<HostNameRow>(context.Archive, "host-names", cancellationToken).ConfigureAwait(false))
        {
            if (HostNames.TryNormalise(hostName.HostName, out var normalised)
                && !hosting.IsAdminHostName(normalised)
                && !await directory.IsHostNameTakenAsync(normalised, cancellationToken).ConfigureAwait(false))
            {
                kept.Add(normalised);
            }
            else
            {
                context.Report.LeftOut(new TransferSubject(ArchiveKinds.HostName, hostName.Id, hostName.HostName), "host_name_taken");
            }
        }

        if (options.HostName is { } extra && HostNames.TryNormalise(extra, out var added) && !kept.Contains(added))
        {
            kept.Add(added);
        }

        var primary = HostNames.TryNormalise(settings.PrimaryHostName, out var p) && kept.Contains(p) ? p : null;
        var actor = context.ActorUserId ?? Guid.Empty;
        var created = Site.Import(
            context.TargetSiteId,
            options.Name ?? settings.Name,
            kept,
            primary,
            settings.Scheme,
            settings.DefaultLanguage,
            settings.Enabled,
            new ImportedSearchSettings(
                search?.TitleSuffix,
                search?.DefaultImageId is { } image ? context.Ids.Remap(ArchiveKinds.MediaFile, image) : null,
                search?.OrganisationName,
                search?.OrganisationLogoId is { } logo ? context.Ids.Remap(ArchiveKinds.MediaFile, logo) : null,
                search?.Discouraged ?? false,
                search?.RobotsRules,
                search?.RedirectOtherHosts ?? true),
            actor,
            clock);
        if (created.IsFailure)
        {
            throw new ArchiveFormatException($"The site cannot be created: {created.Error!.Message}");
        }

        var site = created.Value;
        database.Sites.Add(site);
        SitesOutbox.Add(database, new SiteCreated(Guid.CreateVersion7(), context.NowUtc, site.Id, site.Name, [.. site.HostNames.Select(h => h.HostName)]));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Report.Imported(ArchiveKinds.HostName, kept.Count);
    }

    /// <summary>
    /// A restore takes the archive's name, scheme, language and search settings; host names and
    /// the enabled state are never changed (FR-027c). Either way the site row changes, which moves
    /// every public cache key of the site, so visitors see the imported state at once (FR-028).
    /// </summary>
    private async Task ApplyToExistingAsync(PartApplyContext context, CancellationToken cancellationToken)
    {
        var clock = services.GetRequiredService<TimeProvider>();
        var site = await database.Sites
            .AcrossAllSites("Import into the site named by the transfer job (020 FR-027c).")
            .Include(s => s.HostNames)
            .SingleAsync(s => s.Id == context.TargetSiteId, cancellationToken)
            .ConfigureAwait(false);

        if (context.Mode == ImportMode.Restore || context.Policy == ConflictPolicy.Replace)
        {
            var settings = await SettingsAsync(context.Archive, cancellationToken).ConfigureAwait(false)
                ?? throw new ArchiveFormatException("The archive has no site settings.");
            var search = (await Lines<SearchRow>(context.Archive, "search", cancellationToken).ConfigureAwait(false)).SingleOrDefault();
            var actor = context.ActorUserId ?? Guid.Empty;

            var changed = site.ChangeSettings(settings.Name, settings.Scheme, settings.DefaultLanguage, site.PrimaryHostName, clock);
            if (changed.IsFailure)
            {
                throw new ArchiveFormatException($"The site settings cannot be applied: {changed.Error!.Message}");
            }

            if (search is not null)
            {
                site.ChangeSearchSettings(
                    search.TitleSuffix,
                    search.DefaultImageId is { } image ? context.Ids.Remap(ArchiveKinds.MediaFile, image) : null,
                    search.OrganisationName,
                    search.OrganisationLogoId is { } logo ? context.Ids.Remap(ArchiveKinds.MediaFile, logo) : null,
                    actor,
                    clock);
                site.ChangeSearchVisibility(search.Discouraged, search.RobotsRules, search.RedirectOtherHosts, actor, clock);
            }
        }

        database.MarkAggregateChanged(site);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        services.GetRequiredService<SiteDirectory>().Forget(site.Id);
    }

    private static async Task<SiteSettingsRow?> SettingsAsync(IArchiveReader archive, CancellationToken cancellationToken) =>
        (await Lines<SiteSettingsRow>(archive, "settings", cancellationToken).ConfigureAwait(false)).SingleOrDefault();

    private static async Task<List<T>> Lines<T>(IArchiveReader archive, string entry, CancellationToken cancellationToken)
    {
        var rows = new List<T>();
        await foreach (var row in archive.ReadLinesAsync<T>(PartName, entry, cancellationToken).ConfigureAwait(false))
        {
            rows.Add(row);
        }

        return rows;
    }
}
