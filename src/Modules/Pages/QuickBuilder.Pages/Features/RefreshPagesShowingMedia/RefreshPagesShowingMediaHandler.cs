using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Contracts.Media;
using QuickBuilder.Media.Contracts.Events;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Kernel.Events;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Pages.Features.RefreshPagesShowingMedia;

/// <summary>
/// Refreshes the live pages that show a media file, directly or through a component, when the
/// file is deleted (012 data-model §3.2) or changes in a way pages show (013 research MV-17:
/// replaced, dimensions recorded, variants withdrawn), so they render the change at their
/// next request on every instance. When the file is the site's default sharing image or
/// logo, every live page of the site is refreshed (017 research SE-20).
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
internal sealed class RefreshPagesShowingMediaHandler(IServiceScopeFactory scopes, IItemMediaUsageReader media, ISiteDirectory sites)
    : IIntegrationEventHandler<FileDeleted>, IIntegrationEventHandler<FileChanged>, IIntegrationEventHandler<FileRestored>
{
    private const string DeletedHandlerName = nameof(RefreshPagesShowingMediaHandler);

    private const string ChangedHandlerName = nameof(RefreshPagesShowingMediaHandler) + ".FileChanged";

    private const string RestoredHandlerName = nameof(RefreshPagesShowingMediaHandler) + ".FileRestored";

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

    /// <summary>021 FR-026: live pages that show a restored file show it again from their next request.</summary>
    public Task HandleAsync(FileRestored integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RefreshAsync(
            integrationEvent.Id, integrationEvent.OccurredAtUtc, integrationEvent.SiteId, integrationEvent.FileId, RestoredHandlerName, cancellationToken);
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
                // 017 research SE-20: every page may show the site's default sharing image, and the
                // home page its logo, without referencing either.
                if (await sites.FindByIdAsync(siteId, ct).ConfigureAwait(false) is { } site
                    && (site.Search.DefaultImageId == fileId || site.Search.OrganisationLogoId == fileId))
                {
                    await bump.BumpAllLivePagesAsync(siteId, ct).ConfigureAwait(false);
                    return;
                }

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
