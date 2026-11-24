using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.MultiSite;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;
using QuickBuilder.Sites.Contracts.Events;
using QuickBuilder.Sites.Domain;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.CreateSite;

// One feature, one folder (FR-010): entry point, request/response, command, handler and
// validator together. A new feature is a new folder and touches nothing existing.

/// <summary>Request body for creating a site.</summary>
/// <remarks>
/// 009 added the three optional settings; a 001-shaped body still creates an enabled,
/// HTTPS, English site whose first host name is primary.
/// </remarks>
public sealed record CreateSiteRequest(
    string Name,
    IReadOnlyList<string> HostNames,
    string? PrimaryHostName = null,
    string? Scheme = null,
    string? DefaultLanguage = null);

/// <summary>Command carrying the create-site intent.</summary>
internal sealed record CreateSiteCommand(
    string Name,
    IReadOnlyList<string> HostNames,
    string? PrimaryHostName = null,
    string? Scheme = null,
    string? DefaultLanguage = null) : ICommand<Result<SiteResponse>>;

/// <summary>
/// Reports every invalid input at once (FR-008). Uniqueness is the database's job.
/// </summary>
internal sealed class CreateSiteValidator : IValidator<CreateSiteCommand>
{
    public ValidationResult Validate(CreateSiteCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (string.IsNullOrWhiteSpace(instance.Name))
        {
            failures.Add(new ValidationFailure(nameof(instance.Name), "A site name is required."));
        }
        else if (instance.Name.Trim().Length > Site.MaxNameLength)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.Name),
                $"A site name must be at most {Site.MaxNameLength} characters."));
        }

        var normalisedHostNames = new List<string>();
        if (instance.HostNames is null || instance.HostNames.Count == 0)
        {
            failures.Add(new ValidationFailure(nameof(instance.HostNames), "At least one host name is required."));
        }
        else
        {
            foreach (var hostName in instance.HostNames)
            {
                if (HostNames.TryNormalise(hostName, out var normalised))
                {
                    normalisedHostNames.Add(normalised);
                }
                else
                {
                    failures.Add(new ValidationFailure(
                        nameof(instance.HostNames),
                        $"Host name '{hostName}' is not a valid host name."));
                }
            }
        }

        if (instance.PrimaryHostName is not null
            && (!HostNames.TryNormalise(instance.PrimaryHostName, out var primary)
                || !normalisedHostNames.Contains(primary, StringComparer.Ordinal)))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.PrimaryHostName),
                "The primary host name must be one of the site's host names."));
        }

        if (instance.Scheme is not null && !SiteScheme.TryParse(instance.Scheme, out _))
        {
            failures.Add(new ValidationFailure(nameof(instance.Scheme), "The scheme must be https or http."));
        }

        if (instance.DefaultLanguage is not null && !SupportedLanguages.TryCanonicalise(instance.DefaultLanguage, out _))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.DefaultLanguage),
                "That language is not on the supported list."));
        }

        return failures.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid(failures);
    }
}

internal sealed class CreateSiteHandler(
    SitesDbContext database,
    TimeProvider clock,
    SitesOptions options,
    SiteDtoMapper mapper,
    IAccessEvaluator access)
    : ICommandHandler<CreateSiteCommand, Result<SiteResponse>>
{
    public async Task<Result<SiteResponse>> HandleAsync(CreateSiteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var created = Site.Create(
            command.Name,
            command.HostNames,
            command.PrimaryHostName,
            command.Scheme,
            command.DefaultLanguage,
            clock);
        if (created.IsFailure)
        {
            return Result.Failure<SiteResponse>(created.Error);
        }

        var site = created.Value;

        // An admin host name belongs to the administrative surface; a site claiming it
        // would make that host ambiguous (009 research S-13). Refused like any taken name,
        // naming no holder.
        if (site.HostNames.Any(h => options.IsAdminHostName(h.HostName)))
        {
            return Result.Failure<SiteResponse>(HostNameTaken());
        }

        database.Sites.Add(site);

        // FR-015: the event and the state change are the same SaveChanges, so either both
        // survive or neither does. No second transaction, no window where one landed and
        // the other did not.
        SitesOutbox.Add(database, new SiteCreated(
            Guid.CreateVersion7(),
            clock.GetUtcNow(),
            site.Id,
            site.Name,
            site.HostNames.Select(h => h.HostName).ToList()));

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsHostNameConflict(ex))
        {
            // The unique index is the only place that can see other sites' host names, so
            // this is where a cross-site collision surfaces. Translating it into an
            // expected failure keeps it out of the unexpected-error path (FR-009).
            return Result.Failure<SiteResponse>(HostNameTaken());
        }

        // No cache to invalidate: resolution reads the database on every request, so the
        // new host names resolve on every instance from the next request (research S-01).
        return Result.Success(await SiteResponses
            .BuildAsync(mapper.ToDto(site), access, cancellationToken)
            .ConfigureAwait(false));
    }

    internal static Error HostNameTaken() => Error.Conflict(
        "sites.host_name_taken",
        "The host name is already in use.");

    internal static bool IsHostNameConflict(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException { SqlState: "23505" } postgres
        && postgres.ConstraintName?.Contains("host_name", StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>
/// Administrative surface (FR-011b): needs no resolved site, so it works on a fresh
/// installation with none.
/// </summary>
[ApiController]
[Route("api/v1/sites")]
public sealed class CreateSiteController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Creates a site with at least one host name.</summary>
    [HttpPost]
    [RequireCapability(SitesCapabilities.Create)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(CreateSiteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // FR-007: translate, dispatch, translate back. No rules, no data access.
        var result = await dispatcher
            .SendAsync(
                new CreateSiteCommand(
                    request.Name,
                    request.HostNames,
                    request.PrimaryHostName,
                    request.Scheme,
                    request.DefaultLanguage),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return ErrorResults.ToActionResult(result.Error);
        }

        return Created($"{Request.PathBase}/api/v1/sites/{result.Value.Id}", result.Value);
    }
}
