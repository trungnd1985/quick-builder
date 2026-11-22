using Microsoft.EntityFrameworkCore;
using Npgsql;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Editor.Contracts.Events;
using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Cms.Features.Items.GetItemHistory;

/// <summary>
/// Records preview links created and revoked for an item in its history (022 FR-016, research
/// PV-16). Page links are Pages's to record and are ignored here.
/// </summary>
/// <remarks>
/// Ids, the version number and a created link's expiry only, as every item history entry holds
/// (015 FR-032). The entry's id is the event's, so a redelivery records nothing; an item deleted
/// permanently before the event arrived has no history to add to.
/// </remarks>
internal sealed class RecordPreviewLinkEvents(CmsDbContext database)
    : IIntegrationEventHandler<PreviewLinkCreated>, IIntegrationEventHandler<PreviewLinkRevoked>
{
    private const string ItemKind = "item";

    public Task HandleAsync(PreviewLinkCreated integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return integrationEvent.DocumentKind != ItemKind
            ? Task.CompletedTask
            : RecordAsync(ItemHistoryEntry.ForPreviewLink(
                integrationEvent.Id,
                integrationEvent.SiteId,
                integrationEvent.DocumentId,
                ItemHistoryKinds.PreviewLinkCreated,
                integrationEvent.By,
                integrationEvent.OccurredAtUtc,
                integrationEvent.VersionNumber,
                integrationEvent.ExpiresAtUtc), cancellationToken);
    }

    public Task HandleAsync(PreviewLinkRevoked integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return integrationEvent.DocumentKind != ItemKind
            ? Task.CompletedTask
            : RecordAsync(ItemHistoryEntry.ForPreviewLink(
                integrationEvent.Id,
                integrationEvent.SiteId,
                integrationEvent.DocumentId,
                ItemHistoryKinds.PreviewLinkRevoked,
                integrationEvent.By,
                integrationEvent.OccurredAtUtc,
                integrationEvent.VersionNumber,
                expiresAtUtc: null), cancellationToken);
    }

    private async Task RecordAsync(ItemHistoryEntry entry, CancellationToken cancellationToken)
    {
        if (await database.ForSite<ItemHistoryEntry>(entry.SiteId).AnyAsync(e => e.Id == entry.Id, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        database.ItemHistory.Add(entry);
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation,
        })
        {
            // Recorded by a racing delivery, or the item is gone for good.
            database.ChangeTracker.Clear();
        }
    }
}
