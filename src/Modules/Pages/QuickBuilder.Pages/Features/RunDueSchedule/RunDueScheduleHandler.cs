using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Pages.Features.RunDueSchedule;

/// <summary>
/// Runs a schedule when its due message is delivered (012 FR-020, research PR-14).
/// </summary>
/// <remarks>
/// Each delivery gets its own scope, so its database context and transaction are its own:
/// two dispatchers delivering at once never share one. Running exactly once does not depend
/// on the delivery: the schedule is claimed by a guarded update in the same transaction as
/// the publish, so a redelivery, a second dispatcher or an older revision's message finds
/// nothing due.
/// </remarks>
internal sealed class RunDueScheduleHandler(IServiceScopeFactory scopes) : IIntegrationEventHandler<PublishScheduleDue>
{
    public async Task HandleAsync(PublishScheduleDue integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        await using var scope = scopes.CreateAsyncScope();
        var schedules = scope.ServiceProvider.GetRequiredService<ScheduleWrites>();
        await schedules.RunAsync(integrationEvent.SiteId, integrationEvent.ScheduleId, integrationEvent.Revision, cancellationToken)
            .ConfigureAwait(false);
    }
}
