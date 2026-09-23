using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;
using QuickBuilder.Sites.Contracts;
using QuickBuilder.Sites.Domain;
using QuickBuilder.Sites.Features.CreateSite;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.AddHostName;

/// <summary>Request body for adding a host name.</summary>
public sealed record AddHostNameRequest(string HostName);

internal sealed record AddHostNameCommand(Guid SiteId, string HostName) : ICommand<Result<SiteHostNameDto>>;

internal sealed class AddHostNameValidator : IValidator<AddHostNameCommand>
{
    public ValidationResult Validate(AddHostNameCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return SiteHostName.IsValid(SiteHostName.Normalise(instance.HostName))
            ? ValidationResult.Valid()
            : ValidationResult.Invalid(new ValidationFailure(
                nameof(instance.HostName),
                $"Host name '{instance.HostName}' is not a valid host name."));
    }
}

internal sealed class AddHostNameHandler(
    SitesDbContext database,
    TimeProvider clock,
    SiteDirectory directory)
    : ICommandHandler<AddHostNameCommand, Result<SiteHostNameDto>>
{
    public async Task<Result<SiteHostNameDto>> HandleAsync(
        AddHostNameCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Cross-site: the admin surface has no resolved site, and the caller named this
        // one explicitly in the path.
        var site = await database.Sites
            .AcrossAllSites("Admin operation on a site named explicitly in the request path (FR-011b).")
            .Include(s => s.HostNames)
            .FirstOrDefaultAsync(s => s.Id == command.SiteId, cancellationToken)
            .ConfigureAwait(false);

        if (site is null)
        {
            return Result.Failure<SiteHostNameDto>(Error.NotFound("sites.not_found", "No such site."));
        }

        var added = site.AddHostName(command.HostName, clock);
        if (added.IsFailure)
        {
            return Result.Failure<SiteHostNameDto>(added.Error);
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (CreateSiteHandler.IsHostNameConflict(ex))
        {
            return Result.Failure<SiteHostNameDto>(Error.Conflict(
                "sites.host_name_taken",
                "The host name is already assigned to another site."));
        }

        var normalised = SiteHostName.Normalise(command.HostName);
        directory.InvalidateHostName(normalised);

        var created = site.HostNames.First(h => string.Equals(h.HostName, normalised, StringComparison.Ordinal));
        return Result.Success(new SiteHostNameDto(created.Id, site.Id, created.HostName));
    }
}

/// <summary>Administrative surface.</summary>
[ApiController]
[Route("api/v1/sites/{siteId:guid}/host-names")]
public sealed class AddHostNameController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Adds a host name to a site.</summary>
    [HttpPost]
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
            : Created($"/api/v1/sites/{siteId}/host-names/{result.Value.Id}", result.Value);
    }
}
