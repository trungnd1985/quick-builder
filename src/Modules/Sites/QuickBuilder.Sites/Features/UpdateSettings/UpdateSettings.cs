using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;
using QuickBuilder.Sites.Domain;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.UpdateSettings;

/// <summary>
/// Every editable setting, plus the version the caller last saw (009 research S-06).
/// </summary>
public sealed record UpdateSettingsRequest(
    string Name,
    string Scheme,
    string DefaultLanguage,
    string PrimaryHostName,
    uint Version);

internal sealed record UpdateSettingsCommand(
    Guid SiteId,
    string Name,
    string Scheme,
    string DefaultLanguage,
    string PrimaryHostName,
    uint Version) : ICommand<Result<SiteResponse>>;

/// <summary>
/// Reports every invalid input at once (001 FR-008). Whether the primary is one of the
/// site's host names needs the site, so the aggregate checks that.
/// </summary>
internal sealed class UpdateSettingsValidator : IValidator<UpdateSettingsCommand>
{
    public ValidationResult Validate(UpdateSettingsCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        var name = instance.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > Site.MaxNameLength)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Name),
                $"A site name must be between 1 and {Site.MaxNameLength} characters."));
        }

        if (!SiteScheme.TryParse(instance.Scheme, out _))
        {
            failures.Add(new ValidationFailure(nameof(instance.Scheme), "The scheme must be https or http."));
        }

        if (!SupportedLanguages.TryCanonicalise(instance.DefaultLanguage, out _))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.DefaultLanguage),
                "That language is not on the supported list."));
        }

        if (string.IsNullOrWhiteSpace(instance.PrimaryHostName))
        {
            failures.Add(new ValidationFailure(nameof(instance.PrimaryHostName), "A primary host name is required."));
        }

        return failures.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid(failures);
    }
}

/// <summary>
/// Replaces a site's settings when the caller's version is still current (FR-005).
/// </summary>
/// <remarks>
/// No retry here, unlike the host-name actions: the caller decided based on the settings
/// they saw, so if someone changed them since, the honest answer is "reload" rather than
/// silently applying a decision made against state that no longer exists. Publishes no
/// event — renames and language changes do not change what the public can reach (S-11).
/// </remarks>
internal sealed class UpdateSettingsHandler(
    SitesDbContext database,
    TimeProvider clock,
    SiteDtoMapper mapper,
    IAccessEvaluator access,
    SiteDirectory directory)
    : ICommandHandler<UpdateSettingsCommand, Result<SiteResponse>>
{
    public async Task<Result<SiteResponse>> HandleAsync(UpdateSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Cross-site: the admin surface has no resolved site, and the caller named this
        // one explicitly in the path. Proven by SiteSettingsTests.
        var site = await database.Sites
            .AcrossAllSites("Admin operation on a site named explicitly in the request path (FR-011b).")
            .Include(s => s.HostNames)
            .FirstOrDefaultAsync(s => s.Id == command.SiteId, cancellationToken)
            .ConfigureAwait(false);

        if (site is null)
        {
            return Result.Failure<SiteResponse>(Error.NotFound("sites.not_found", "No such site."));
        }

        // Compare against the version the caller saw, not the one just loaded: that is
        // what makes a stale form fail even when nobody is saving at this very moment.
        if (site.Version != command.Version)
        {
            return Result.Failure<SiteResponse>(ConcurrencyRetry.OutOfDate());
        }

        var changed = site.ChangeSettings(
            command.Name, command.Scheme, command.DefaultLanguage, command.PrimaryHostName, clock);
        if (changed.IsFailure)
        {
            return Result.Failure<SiteResponse>(changed.Error!);
        }

        database.MarkAggregateChanged(site);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone saved between our read and our write.
            return Result.Failure<SiteResponse>(ConcurrencyRetry.OutOfDate());
        }

        directory.Forget(site.Id);
        return Result.Success(await SiteResponses
            .BuildAsync(mapper.ToDto(site), access, cancellationToken)
            .ConfigureAwait(false));
    }
}

/// <summary>Administrative surface.</summary>
[ApiController]
[Route("api/v1/sites/{siteId:guid}/settings")]
public sealed class UpdateSettingsController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Replaces a site's editable settings.</summary>
    [HttpPut]
    [RequireCapability(SitesCapabilities.SettingsManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        Guid siteId,
        UpdateSettingsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher
            .SendAsync(
                new UpdateSettingsCommand(
                    siteId, request.Name, request.Scheme, request.DefaultLanguage, request.PrimaryHostName, request.Version),
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? ErrorResults.ToActionResult(result.Error)
            : Ok(result.Value);
    }
}
