using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Forms.Domain;
using QuickBuilder.Forms.Infrastructure;
using QuickBuilder.Pages.Contracts.Events;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Forms.Features.RefreshFormRegistry;

/// <summary>
/// Keeps the form registry in step with what is live (018 research FS-05): which forms each
/// page and component shows, and every form's last published settings.
/// </summary>
/// <remarks>
/// <para>
/// It reads the subject's live document now, not the event's payload, so a repeated or late
/// event writes the rows the current state calls for: handling is idempotent without a
/// processed-events row of its own.
/// </para>
/// <para>
/// Two instances may handle two publishes of one page at once. Each takes a transaction lock on
/// the subject and reads the live document inside it. The later publish's event cannot be
/// handled before that publish has committed, so whichever handler runs last reads the latest
/// version, and an earlier event's handler can never overwrite it with an older one.
/// </para>
/// </remarks>
internal sealed class RefreshFormRegistryHandler(IServiceScopeFactory scopes, TimeProvider clock)
    : IIntegrationEventHandler<PagePublished>,
      IIntegrationEventHandler<PageUnpublished>,
      IIntegrationEventHandler<ComponentPublished>,
      IIntegrationEventHandler<ComponentUnpublished>
{
    public Task HandleAsync(PagePublished integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RefreshAsync(integrationEvent.SiteId, FormSubjectKinds.Page, integrationEvent.PageId, integrationEvent.OccurredAtUtc, cancellationToken);
    }

    public Task HandleAsync(PageUnpublished integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RefreshAsync(integrationEvent.SiteId, FormSubjectKinds.Page, integrationEvent.PageId, integrationEvent.OccurredAtUtc, cancellationToken);
    }

    public Task HandleAsync(ComponentPublished integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RefreshAsync(integrationEvent.SiteId, FormSubjectKinds.Component, integrationEvent.ComponentId, integrationEvent.OccurredAtUtc, cancellationToken);
    }

    public Task HandleAsync(ComponentUnpublished integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RefreshAsync(integrationEvent.SiteId, FormSubjectKinds.Component, integrationEvent.ComponentId, integrationEvent.OccurredAtUtc, cancellationToken);
    }

    private async Task RefreshAsync(Guid siteId, string subjectKind, Guid subjectId, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken)
    {
        // A scope of its own, resolved to the event's site, as a visitor's request to that site
        // would be: the published-content reader is the public path's, filtered by the
        // resolved site, and it must see exactly what that site's visitors are served.
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ISiteContextSetter>().SetSite(siteId);
        var database = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IPublishedContentReader>();

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var lockKey = $"forms.registry:{siteId:N}:{subjectKind}:{subjectId:N}";
        await database.Database
            .ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken)
            .ConfigureAwait(false);

        var forms = await LiveFormsAsync(reader, siteId, subjectKind, subjectId, cancellationToken).ConfigureAwait(false);

        var placements = await database.ForSite<FormPlacement>(siteId)
            .Where(p => p.SubjectKind == subjectKind && p.SubjectId == subjectId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        database.FormPlacements.RemoveRange(placements.Where(p => !forms.Any(f => f.Id == p.FormId)));

        var ids = forms.Select(f => f.Id).ToList();
        var records = await database.ForSite<FormRecord>(siteId)
            .Where(f => ids.Contains(f.FormId))
            .ToDictionaryAsync(f => f.FormId, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var now = clock.GetUtcNow();
        foreach (var form in forms.DistinctBy(f => f.Id, StringComparer.Ordinal))
        {
            if (!records.TryGetValue(form.Id, out var record))
            {
                record = new FormRecord(siteId, form.Id);
                database.Forms.Add(record);
            }

            record.Apply(form.ToSettings(), occurredAtUtc, now);

            if (!placements.Any(p => p.FormId == form.Id))
            {
                database.FormPlacements.Add(new FormPlacement(siteId, subjectKind, subjectId, form.Id));
            }
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The forms of the subject's live version; none when it is not live.</summary>
    private static async Task<IReadOnlyList<FormDefinition>> LiveFormsAsync(
        IPublishedContentReader reader, Guid siteId, string subjectKind, Guid subjectId, CancellationToken cancellationToken)
    {
        if (subjectKind == FormSubjectKinds.Page)
        {
            var page = await reader.GetLivePageAsync(siteId, subjectId, cancellationToken).ConfigureAwait(false);
            return page is null ? [] : FormDefinition.FindAll(page.Document);
        }

        var components = await reader.GetLiveComponentsAsync(siteId, [subjectId], cancellationToken).ConfigureAwait(false);
        return components.TryGetValue(subjectId, out var component) ? FormDefinition.FindAll(component.Document) : [];
    }
}
