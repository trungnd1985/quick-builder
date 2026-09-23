using Microsoft.Extensions.Logging;
using QuickBuilder.Shared.Kernel.Events;
using QuickBuilder.Sites.Contracts.Events;

namespace QuickBuilder.Sites.Features.CreateSite;

/// <summary>
/// The worked example of an integration event handler.
/// </summary>
/// <remarks>
/// Logging a line is deliberately the least interesting thing a handler could do. Its job
/// is to demonstrate the shape every later handler copies — registered in the module's
/// entry point, short, idempotent, and reachable only through the outbox — without
/// inventing a side effect no spec asks for (Principle XI).
/// <para>
/// Idempotency comes for free here: the dispatcher writes this handler's processed-events
/// row in the same transaction as any effect it causes, so a redelivery is a no-op. A
/// handler causing effects outside that transaction would have to deduplicate for itself
/// and say so in a comment.
/// </para>
/// </remarks>
internal sealed class SiteCreatedLogHandler(ILogger<SiteCreatedLogHandler> logger)
    : IIntegrationEventHandler<SiteCreated>
{
    public Task HandleAsync(SiteCreated integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        logger.LogInformation(
            "Site {SiteId} ({SiteName}) was created with {HostNameCount} host name(s).",
            integrationEvent.SiteId,
            integrationEvent.Name,
            integrationEvent.HostNames.Count);

        return Task.CompletedTask;
    }
}
