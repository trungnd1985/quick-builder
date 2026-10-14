using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using QuickBuilder.Cms.Contracts.Events;
using QuickBuilder.Pages.Domain.Redirects;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Pages.Features.CreateItemRedirect;

/// <summary>
/// Creates the redirect a member accepted for an item's old address (017 FR-029, FR-032;
/// research SE-11): to the item when its slug changed, or to the chosen page when it stopped
/// being live.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The old address is the collection's live template pattern with the old slug; without a
/// live template the item had no address, and nothing is created.</item>
/// <item>Applied as any offer is (<see cref="RedirectOffers"/>): an address live content holds is
/// skipped, <c>path</c> redirects to it follow, loops are turned off, and every action is
/// recorded in <c>pages.redirect_changes</c>.</item>
/// <item>Exactly once: saved with a row in Pages' <c>processed_events</c>, so a redelivery
/// creates nothing more.</item>
/// <item>The old slug is content: only ids are logged (integration-events.md, 017 section).</item>
/// </list>
/// </remarks>
internal sealed class CreateItemRedirectHandler(IServiceScopeFactory scopes, ILogger<CreateItemRedirectHandler> logger)
    : IIntegrationEventHandler<ItemRedirectRequested>
{
    internal const string HandlerName = nameof(CreateItemRedirectHandler) + ".ItemRedirectRequested";

    public async Task HandleAsync(ItemRedirectRequested integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PagesDbContext>();
        var writes = scope.ServiceProvider.GetRequiredService<RedirectWrites>();
        var offers = scope.ServiceProvider.GetRequiredService<RedirectOffers>();

        var alreadyDone = await database.ProcessedEvents
            .AnyAsync(p => p.EventId == integrationEvent.Id && p.HandlerName == HandlerName, cancellationToken)
            .ConfigureAwait(false);
        if (alreadyDone)
        {
            return;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var outcome = await CreateAsync(integrationEvent, writes, offers, cancellationToken).ConfigureAwait(false);

        database.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = integrationEvent.Id,
            HandlerName = HandlerName,
            ProcessedAtUtc = integrationEvent.OccurredAtUtc,
        });

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another delivery, or a member adding the same source, got there first.
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        logger.LogInformation(
            "Redirect for item {ItemId} of collection {CollectionId} on site {SiteId} ({Reason}): {Created} created, {Skipped} skipped, {Retargeted} retargeted, {TurnedOff} turned off.",
            integrationEvent.ItemId,
            integrationEvent.CollectionId,
            integrationEvent.SiteId,
            integrationEvent.Reason,
            outcome.Created,
            outcome.Skipped,
            outcome.Retargeted,
            outcome.TurnedOff.Count);
    }

    private static async Task<RedirectOutcomeResponse> CreateAsync(
        ItemRedirectRequested request, RedirectWrites writes, RedirectOffers offers, CancellationToken cancellationToken)
    {
        var state = await writes.LoadAsync(request.SiteId, cancellationToken).ConfigureAwait(false);
        if (!state.LivePatterns.TryGetValue(request.CollectionId, out var pattern))
        {
            return RedirectOutcomeResponse.None;
        }

        RedirectTarget target;
        if (request.Reason == ItemRedirectReasons.Moved)
        {
            await writes.LoadItemsAsync(state, [request.ItemId], cancellationToken).ConfigureAwait(false);
            target = RedirectTarget.ToItem(request.CollectionId, request.ItemId);
        }
        else if (request.TargetPageId is { } pageId && state.Pages.TryGetValue(pageId, out var page) && !page.IsTemplate)
        {
            target = RedirectTarget.ToPage(pageId);
        }
        else
        {
            return RedirectOutcomeResponse.None;
        }

        var source = RedirectSiteState.ItemAddress(pattern, request.OldSlug);
        return await offers.ApplyAsync(state, [new OfferedRedirect(RedirectMatchKinds.Exact, source, target)], request.ActorUserId, cancellationToken)
            .ConfigureAwait(false);
    }
}
