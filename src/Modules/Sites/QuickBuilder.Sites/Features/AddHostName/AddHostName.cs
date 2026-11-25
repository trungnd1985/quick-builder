using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.MultiSite;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;
using QuickBuilder.Sites.Contracts.Events;
using QuickBuilder.Sites.Features.CreateSite;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.AddHostName;

/// <summary>Request body for adding a host name.</summary>
public sealed record AddHostNameRequest(string HostName);

/// <summary>
/// The 001 shape (<c>id</c>, <c>siteId</c>, <c>hostName</c>) plus the readable form and
/// whether it is primary — always false for a name just added (research S-17).
/// </summary>
public sealed record AddHostNameResponse(Guid Id, Guid SiteId, string HostName, string DisplayName, bool IsPrimary);

internal sealed record AddHostNameCommand(Guid SiteId, string HostName) : ICommand<Result<AddHostNameResponse>>;

internal sealed class AddHostNameValidator : IValidator<AddHostNameCommand>
{
    public ValidationResult Validate(AddHostNameCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return HostNames.TryNormalise(instance.HostName, out _)
            ? ValidationResult.Valid()
            : ValidationResult.Invalid(new ValidationFailure(
                nameof(instance.HostName),
                $"Host name '{instance.HostName}' is not a valid host name."));
    }
}

internal sealed class AddHostNameHandler(
    SitesDbContext database,
    TimeProvider clock,
    SitesOptions options,
    IAccessEvaluator access,
    SiteDirectory directory)
    : ICommandHandler<AddHostNameCommand, Result<AddHostNameResponse>>
{
    public async Task<Result<AddHostNameResponse>> HandleAsync(
        AddHostNameCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        HostNames.TryNormalise(command.HostName, out var normalised);
        if (options.IsAdminHostName(normalised))
        {
            return Result.Failure<AddHostNameResponse>(CreateSiteHandler.HostNameTaken());
        }

        return await ConcurrencyRetry
            .ExecuteAsync(database, ct => AttemptAsync(command, normalised, ct), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<AddHostNameResponse>> AttemptAsync(
        AddHostNameCommand command,
        string normalised,
        CancellationToken cancellationToken)
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
            return Result.Failure<AddHostNameResponse>(Error.NotFound("sites.not_found", "No such site."));
        }

        var added = site.AddHostName(command.HostName, clock);
        if (added.IsFailure)
        {
            return Result.Failure<AddHostNameResponse>(added.Error);
        }

        database.MarkAggregateChanged(site);
        SitesOutbox.Add(database, new HostNameAdded(
            Guid.CreateVersion7(), clock.GetUtcNow(), site.Id, normalised, access.UserId));

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (CreateSiteHandler.IsHostNameConflict(ex))
        {
            database.ChangeTracker.Clear();
            return Result.Failure<AddHostNameResponse>(CreateSiteHandler.HostNameTaken());
        }

        // Saved, so the next request on any instance resolves the name (research S-01).
        directory.Forget(site.Id);

        var created = site.HostNames.First(h => string.Equals(h.HostName, normalised, StringComparison.Ordinal));
        return Result.Success(new AddHostNameResponse(
            created.Id, site.Id, created.HostName, HostNames.ToDisplay(created.HostName), IsPrimary: false));
    }
}

/// <summary>Administrative surface.</summary>
[ApiController]
[Route("api/v1/sites/{siteId:guid}/host-names")]
public sealed class AddHostNameController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Adds a host name to a site.</summary>
    [HttpPost]
    [RequireCapability(SitesCapabilities.ManageHostNames)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddAsync(
        Guid siteId,
        AddHostNameRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher
            .SendAsync(new AddHostNameCommand(siteId, request.HostName), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? ErrorResults.ToActionResult(result.Error)
            : Created($"{Request.PathBase}/api/v1/sites/{siteId}/host-names/{Uri.EscapeDataString(result.Value.HostName)}", result.Value);
    }
}
