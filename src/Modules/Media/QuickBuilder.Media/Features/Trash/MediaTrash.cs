using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Contracts;
using QuickBuilder.Media.Domain;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Trash;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.Media.Features.Trash;

/// <summary>A filter over the media trash (021 FR-010, TR-19).</summary>
internal sealed record TrashQueryOptions(string? Search, Guid? TrashedBy, DateTimeOffset? From, DateTimeOffset? To, Guid? TrashedFolderId, string? After, int Limit);

/// <summary>
/// What the Media trash slices share: reading trashed files, describing them, and planning a
/// restore (021 TR-10, TR-19).
/// </summary>
/// <remarks>
/// Restore authority mirrors delete (FR-013, 013 FR-016): <c>media.files.delete</c>, or
/// <c>media.own_files.delete</c> for one's own upload, plus publishing when a live page shows
/// the file. Every read names its reason (TR-03).
/// </remarks>
internal sealed class MediaTrash(MediaDbContext database, IAccessEvaluator access, IUserDirectory users, TrashRetention retention)
{
    internal const int PageSize = 50;
    private const string Reason = "The site's media trash: list, preview and restore (021 TR-01).";

    internal IQueryable<MediaFile> TrashedFiles(Guid siteId) =>
        database.MediaFiles
            .AcrossAllSites("Admin read of a site named explicitly in the route (001 FR-011b).")
            .OnlyTrash(Reason)
            .Where(f => f.SiteId == siteId);

    internal Task<bool> MayAsync(string capability, Guid siteId, CancellationToken cancellationToken) =>
        access.HasCapabilityAsync(capability, siteId, cancellationToken);

    /// <summary>Whether the signed-in member may restore <paramref name="file"/>, ignoring live use.</summary>
    internal async Task<bool> MayRestoreAsync(MediaFile file, CancellationToken cancellationToken) =>
        file.UploadedBy == access.UserId
        || await MayAsync(MediaCapabilities.Delete, file.SiteId, cancellationToken).ConfigureAwait(false);

    internal async Task<TrashListResponse> ListAsync(Guid siteId, TrashQueryOptions options, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(options.Limit, 1, PageSize);
        var query = TrashedFiles(siteId);

        if (!string.IsNullOrWhiteSpace(options.Search))
        {
            // The same normalised text the library searches (013 MV-26).
            var normalised = Shared.Kernel.Text.SearchText.Normalise(options.Search);
            query = query.Where(f => f.SearchText.Contains(normalised));
        }

        if (options.TrashedBy is { } by)
        {
            query = query.Where(f => f.TrashedBy == by);
        }

        if (options.From is { } from)
        {
            query = query.Where(f => f.TrashedAtUtc >= from);
        }

        if (options.To is { } to)
        {
            query = query.Where(f => f.TrashedAtUtc <= to);
        }

        if (options.TrashedFolderId is { } folder)
        {
            query = query.Where(f => f.TrashedFolderId == folder);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        // FR-012: the storage the whole media trash takes, whatever the filter.
        var totalBytes = await TrashedFiles(siteId).SumAsync(f => (long?)f.SizeBytes, cancellationToken).ConfigureAwait(false) ?? 0;

        if (Cursor.Parse(options.After) is { } c)
        {
            query = query.Where(f => f.TrashedAtUtc < c.At || (f.TrashedAtUtc == c.At && f.Id.CompareTo(c.Id) < 0));
        }

        var files = await query
            .AsNoTracking()
            .OrderByDescending(f => f.TrashedAtUtc).ThenByDescending(f => f.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var more = files.Count > limit;
        files = [.. files.Take(limit)];
        var entries = await DescribeAsync(siteId, files, cancellationToken).ConfigureAwait(false);
        return new TrashListResponse(entries, more ? new Cursor(files[^1].TrashedAtUtc!.Value, files[^1].Id).ToString() : null, total, totalBytes);
    }

    internal async Task<IReadOnlyList<TrashEntryResponse>> DescribeAsync(Guid siteId, IReadOnlyList<MediaFile> files, CancellationToken cancellationToken)
    {
        var mayDeleteAny = await MayAsync(MediaCapabilities.Delete, siteId, cancellationToken).ConfigureAwait(false);
        var canPurge = await MayAsync(MediaCapabilities.TrashPurge, siteId, cancellationToken).ConfigureAwait(false);

        var folderIds = files.Where(f => f.TrashedFolderId is not null).Select(f => f.TrashedFolderId!.Value).Distinct().ToList();
        var folders = await FolderNamesAsync(siteId, folderIds, cancellationToken).ConfigureAwait(false);

        var userIds = files.Select(f => f.TrashedBy!.Value).Distinct().ToList();
        var names = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await users.FindManyAsync(userIds, cancellationToken).ConfigureAwait(false)).ToDictionary(u => u.Id, u => u.DisplayName);

        return [.. files.Select(f => new TrashEntryResponse(
            TrashKinds.File,
            f.Id,
            f.FileName,
            f.FileName,
            f.TrashedFolderId is not { } folderId
                ? new TrashPlaceResponse(null, null, "none")
                : folders.TryGetValue(folderId, out var name)
                    ? new TrashPlaceResponse(folderId, name, "live")
                    : new TrashPlaceResponse(folderId, null, "gone"),
            f.TrashedAtUtc!.Value,
            new TrashActorResponse(f.TrashedBy!.Value, names.GetValueOrDefault(f.TrashedBy!.Value) ?? string.Empty),
            retention.PurgeOn(f.TrashedAtUtc!.Value),
            f.SizeBytes,
            mayDeleteAny || f.UploadedBy == access.UserId,
            canPurge))];
    }

    /// <summary>Where restoring puts the file: its folder when it still exists, else the top level or a chosen one.</summary>
    internal async Task<RestorePlanResponse?> PlanAsync(MediaFile file, string? placement, Guid? chosenFolderId, CancellationToken cancellationToken)
    {
        var original = file.TrashedFolderId;
        var originalName = original is { } o ? (await FolderNamesAsync(file.SiteId, [o], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(o) : null;
        var gone = original is not null && originalName is null;
        List<string> options = gone ? ["top", "chosen"] : ["original", "top", "chosen"];

        switch (placement ?? (gone ? "top" : "original"))
        {
            case "original" when !gone:
                return new RestorePlanResponse("original", options, original, originalName, false, Address(original));
            case "original":
            case "top":
                return new RestorePlanResponse("top", options, null, null, gone, Address(null));
            case "chosen" when chosenFolderId is { } chosen:
                var chosenName = (await FolderNamesAsync(file.SiteId, [chosen], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(chosen);
                return chosenName is null ? null : new RestorePlanResponse("chosen", options, chosen, chosenName, gone, Address(chosen));
            default:
                return null;
        }
    }

    /// <summary>The token a member confirms for a file: the folder it goes to, or <c>top</c>.</summary>
    internal static string Address(Guid? folderId) => folderId?.ToString("N") ?? "top";

    private Task<Dictionary<Guid, string>> FolderNamesAsync(Guid siteId, List<Guid> folderIds, CancellationToken cancellationToken) =>
        folderIds.Count == 0
            ? Task.FromResult(new Dictionary<Guid, string>())
            : database.Folders
                .AcrossAllSites("Admin read of a site named explicitly in the route (001 FR-011b).")
                .Where(f => f.SiteId == siteId && folderIds.Contains(f.Id))
                .ToDictionaryAsync(f => f.Id, f => f.Name, cancellationToken);

    /// <summary>A keyset position: the last entry's trash time and id.</summary>
    private readonly record struct Cursor(DateTimeOffset At, Guid Id)
    {
        public static Cursor? Parse(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            var parts = text.Split('_');
            return parts.Length == 2
                   && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                   && Guid.TryParse(parts[1], out var id)
                ? new Cursor(new DateTimeOffset(ticks, TimeSpan.Zero), id)
                : null;
        }

        public override string ToString() => $"{At.UtcTicks.ToString(CultureInfo.InvariantCulture)}_{Id:N}";
    }
}
