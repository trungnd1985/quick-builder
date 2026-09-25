using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.MultiSite;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts.Events;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.RemoveHostName;

internal sealed record RemoveHostNameCommand(Guid SiteId, string HostName) : ICommand<Result>;

/// <summary>
/// Removes a host name that is neither the site's primary nor its only one (009 FR-011).
/// </summary>
/// <remarks>
/// The aggregate decides; this handler only loads, saves and retries. A concurrent change
/// that makes this name primary moves the site's version, so this save fails and the retry
/// sees the name as primary and refuses (research S-05).
/// </remarks>
internal sealed class RemoveHostNameHandler(
    SitesDbContext database,
    TimeProvider clock,
    IAccessEvaluator access,
    SiteDirectory directory)
    : ICommandHandler<RemoveHostNameCommand, Result>
{
    public async Task<Result> HandleAsync(RemoveHostNameCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await ConcurrencyRetry
            .ExecuteAsync(database, ct => AttemptAsync(command, ct), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? Result.Failure(result.Error) : Result.Success();
    }

    private async Task<Result<bool>> AttemptAsync(RemoveHostNameCommand command, CancellationToken cancellationToken)
    {
        // Cross-site: the admin surface has no resolved site, and the caller named this
        // one explicitly in the path. Proven by HostNameManagementTests.
        var site = await database.Sites
            .AcrossAllSites("Admin operation on a site named explicitly in the request path (FR-011b).")
            .Include(s => s.HostNames)
            .FirstOrDefaultAsync(s => s.Id == command.SiteId, cancellationToken)
            .ConfigureAwait(false);

        if (site is null)
        {
            return Result.Failure<bool>(Error.NotFound("sites.not_found", "No such site."));
        }

        var removed = site.RemoveHostName(command.HostName, clock);
        if (removed.IsFailure)
        {
            return Result.Failure<bool>(removed.Error);
        }

        HostNames.TryNormalise(command.HostName, out var normalised);
        database.MarkAggregateChanged(site);
        SitesOutbox.Add(database, new HostNameRemoved(
            Guid.CreateVersion7(), clock.GetUtcNow(), site.Id, normalised, access.UserId));

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Committed: the next public request for this name, on any instance, no longer
        // reaches the site (research S-01).
        directory.Forget(site.Id);
        return Result.Success(true);
    }
}

/// <summary>Administrative surface.</summary>
[ApiController]
[Route("api/v1/sites/{siteId:guid}/host-names")]
public sealed class RemoveHostNameController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Removes a host name. Any equivalent spelling of the name works.</summary>
    [HttpDelete("{hostName}")]
    [RequireCapability(SitesCapabilities.ManageHostNames)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveAsync(Guid siteId, string hostName, CancellationToken cancellationToken)
    {
        var result = await dispatcher
            .SendAsync(new RemoveHostNameCommand(siteId, hostName), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? ErrorResults.ToActionResult(result.Error)
            : NoContent();
    }
}
