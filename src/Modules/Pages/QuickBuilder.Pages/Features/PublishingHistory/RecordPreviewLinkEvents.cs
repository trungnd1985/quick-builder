using Microsoft.EntityFrameworkCore;
using Npgsql;
using QuickBuilder.Editor.Contracts.Events;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Pages.Features.PublishingHistory;

/// <summary>
/// Records preview links created and revoked for a page in its history (022 FR-016, research
/// PV-16).
/// </summary>
/// <remarks>
/// Editor owns the links and cannot write this module's log, so it publishes the events and
/// this records them. Item links are Cms's to record and are ignored here.
/// <para>
/// The entry carries the version number and, for a created link, its expiry: never the secret
/// and never the label. As for <c>RecordDesignPublishHandler</c>, the entry's id is the event's,
/// so a redelivery records nothing.
/// </para>
/// </remarks>
internal sealed class RecordPreviewLinkEvents(PagesDbContext database)
    : IIntegrationEventHandler<PreviewLinkCreated>, IIntegrationEventHandler<PreviewLinkRevoked>
{
    private const string PageKind = "page";

    public Task HandleAsync(PreviewLinkCreated integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return integrationEvent.DocumentKind != PageKind
            ? Task.CompletedTask
            : RecordAsync(
                integrationEvent.Id,
                integrationEvent.SiteId,
                integrationEvent.DocumentId,
                LogActions.PreviewLinkCreated,
                integrationEvent.OccurredAtUtc,
                integrationEvent.By,
                integrationEvent.VersionNumber,
                integrationEvent.ExpiresAtUtc,
                cancellationToken);
    }

    public Task HandleAsync(PreviewLinkRevoked integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return integrationEvent.DocumentKind != PageKind
            ? Task.CompletedTask
            : RecordAsync(
                integrationEvent.Id,
                integrationEvent.SiteId,
                integrationEvent.DocumentId,
                LogActions.PreviewLinkRevoked,
                integrationEvent.OccurredAtUtc,
                integrationEvent.By,
                integrationEvent.VersionNumber,
                expiresAtUtc: null,
                cancellationToken);
    }

    private async Task RecordAsync(
        Guid eventId,
        Guid siteId,
        Guid pageId,
        string action,
        DateTimeOffset at,
        Guid by,
        int? versionNumber,
        DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken)
    {
        if (await database.ForSite<PublishLogEntry>(siteId).AnyAsync(e => e.Id == eventId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // The label and address as they are now, as every log entry takes them (012 data-model
        // §1.5). A page meanwhile moved to the trash is recorded without them.
        var page = await database.ForSite<Page>(siteId)
            .Where(p => p.Id == pageId)
            .Select(p => new { p.Title, p.Path })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        database.PublishLog.Add(new PublishLogEntry(eventId, siteId, SubjectKinds.Page, pageId, page?.Title ?? string.Empty, page?.Path, action, at)
        {
            ActorUserId = by,
            VersionNumber = versionNumber,
            ExpiresAtUtc = expiresAtUtc,
        });

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two deliveries raced past the check; the other one recorded it.
            database.ChangeTracker.Clear();
        }
    }
}
