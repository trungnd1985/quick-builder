using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Contracts.Media;
using QuickBuilder.Media.Contracts.Events;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Pages.Features.RefreshPagesShowingMedia;

/// <summary>
/// Refreshes the live pages that show a media file, directly or through a component, when the
/// file is deleted (012 data-model §3.2) or changes in a way pages show (013 research MV-17:
/// replaced, dimensions recorded, variants withdrawn), so they render the change at their
/// next request on every instance.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deduplication is this handler's own</b>, through <see cref="RefreshOnce"/>: the
/// dispatcher marks the event processed in Media's schema, the generations move in Pages'.
/// </para>
/// <para>
/// Every <see cref="FileChanged"/> reason gets the same bump, including reasons added after
/// this was written: re-rendering a page that did not need it costs one render, while
/// missing one would leave visitors with stale images.
/// </para>
/// </remarks>
internal sealed class RefreshPagesShowingMediaHandler(IServiceScopeFactory scopes, IItemMediaUsageReader media)
    : IIntegrationEventHandler<FileDeleted>, IIntegrationEventHandler<FileChanged>
{
    private const string DeletedHandlerName = nameof(RefreshPagesShowingMediaHandler);

    private const string ChangedHandlerName = nameof(RefreshPagesShowingMediaHandler) + ".FileChanged";

    public Task HandleAsync(FileDeleted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RefreshAsync(
            integrationEvent.Id, integrationEvent.OccurredAtUtc, integrationEvent.SiteId, integrationEvent.FileId, DeletedHandlerName, cancellationToken);
    }

    public Task HandleAsync(FileChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RefreshAsync(
            integrationEvent.Id, integrationEvent.OccurredAtUtc, integrationEvent.SiteId, integrationEvent.FileId, ChangedHandlerName, cancellationToken);
    }

    private Task RefreshAsync(
        Guid eventId, DateTimeOffset occurredAtUtc, Guid siteId, Guid fileId, string handlerName, CancellationToken cancellationToken) =>
        RefreshOnce.RunAsync(
            scopes,
            eventId,
            occurredAtUtc,
            handlerName,
            async (bump, ct) =>
            {
                await bump.BumpForTargetsAsync(siteId, [(DependencyKinds.Media, fileId)], exceptPageId: null, ct).ConfigureAwait(false);

                // 016 TB-10: lists of collections whose published items show the file. Their item
                // pages refresh through the render stamp Cms moves.
                var collections = await media.CollectionsShowingPublishedAsync(siteId, fileId, ct).ConfigureAwait(false);
                if (collections.Count > 0)
                {
                    await bump.BumpForCollectionsAsync(siteId, [.. collections], includeTemplates: false, exceptPageId: null, ct).ConfigureAwait(false);
                }
            },
            cancellationToken);
}
