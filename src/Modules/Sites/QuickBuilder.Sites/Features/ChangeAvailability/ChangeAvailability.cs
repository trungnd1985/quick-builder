using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts.Events;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.ChangeAvailability;

internal sealed record ChangeAvailabilityCommand(Guid SiteId, bool Enable) : ICommand<Result<SiteResponse>>;

/// <summary>
/// Disables or enables a site (009 US4, research S-10).
/// </summary>
/// <remarks>
/// Idempotent: asking for the state a site is already in answers with the site and
/// publishes nothing, so a double click or a retried request records one security event,
/// not two. Nothing else about the site changes — host names stay taken, grants and data
/// stay — which is the whole difference between disabling and deleting.
/// </remarks>
internal sealed class ChangeAvailabilityHandler(
    SitesDbContext database,
    TimeProvider clock,
    SiteDtoMapper mapper,
    IAccessEvaluator access,
    SiteDirectory directory)
    : ICommandHandler<ChangeAvailabilityCommand, Result<SiteResponse>>
{
    public async Task<Result<SiteResponse>> HandleAsync(
        ChangeAvailabilityCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await ConcurrencyRetry
            .ExecuteAsync(database, ct => AttemptAsync(command, ct), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result.Failure<SiteResponse>(result.Error!);
        }

        return Result.Success(await SiteResponses
            .BuildAsync(result.Value, access, cancellationToken)
            .ConfigureAwait(false));
    }

    private async Task<Result<Contracts.SiteDto>> AttemptAsync(
        ChangeAvailabilityCommand command,
        CancellationToken cancellationToken)
    {
        // Cross-site: the admin surface has no resolved site, and the caller named this
        // one explicitly in the path. Proven by SiteAvailabilityTests.
        var site = await database.Sites
            .AcrossAllSites("Admin operation on a site named explicitly in the request path (FR-011b).")
            .Include(s => s.HostNames)
            .FirstOrDefaultAsync(s => s.Id == command.SiteId, cancellationToken)
            .ConfigureAwait(false);

        if (site is null)
        {
            return Result.Failure<Contracts.SiteDto>(Error.NotFound("sites.not_found", "No such site."));
        }

        var changed = command.Enable ? site.Enable(clock) : site.Disable(clock);
        if (!changed)
        {
            return Result.Success(mapper.ToDto(site));
        }

        database.MarkAggregateChanged(site);
        if (command.Enable)
        {
            SitesOutbox.Add(database, new SiteEnabled(Guid.CreateVersion7(), clock.GetUtcNow(), site.Id, access.UserId));
        }
        else
        {
            SitesOutbox.Add(database, new SiteDisabled(Guid.CreateVersion7(), clock.GetUtcNow(), site.Id, access.UserId));
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Committed: the next public request on any instance sees the new state (S-01).
        directory.Forget(site.Id);
        return Result.Success(mapper.ToDto(site));
    }
}

/// <summary>Administrative surface.</summary>
[ApiController]
[Route("api/v1/sites/{siteId:guid}")]
public sealed class ChangeAvailabilityController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Takes a site off the public surface, keeping everything.</summary>
    [HttpPost("disable")]
    [RequireCapability(SitesCapabilities.AvailabilityManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> DisableAsync(Guid siteId, CancellationToken cancellationToken) =>
        ChangeAsync(siteId, enable: false, cancellationToken);

    /// <summary>Puts a disabled site back on the public surface.</summary>
    [HttpPost("enable")]
    [RequireCapability(SitesCapabilities.AvailabilityManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> EnableAsync(Guid siteId, CancellationToken cancellationToken) =>
        ChangeAsync(siteId, enable: true, cancellationToken);

    private async Task<IActionResult> ChangeAsync(Guid siteId, bool enable, CancellationToken cancellationToken)
    {
        var result = await dispatcher
            .SendAsync(new ChangeAvailabilityCommand(siteId, enable), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? ErrorResults.ToActionResult(result.Error)
            : Ok(result.Value);
    }
}
