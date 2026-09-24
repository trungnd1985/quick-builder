using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;
using QuickBuilder.Sites.Contracts;
using QuickBuilder.Sites.Contracts.Events;
using QuickBuilder.Sites.Domain;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.CreateSite;

// One feature, one folder (FR-010): entry point, request/response, command, handler and
// validator together. A new feature is a new folder and touches nothing existing.

/// <summary>Request body for creating a site.</summary>
public sealed record CreateSiteRequest(string Name, IReadOnlyList<string> HostNames);

/// <summary>Command carrying the create-site intent.</summary>
internal sealed record CreateSiteCommand(string Name, IReadOnlyList<string> HostNames)
    : ICommand<Result<SiteDto>>;

/// <summary>Validates shape only — uniqueness is the database's job (FR-008).</summary>
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

        if (instance.HostNames is null || instance.HostNames.Count == 0)
        {
            failures.Add(new ValidationFailure(nameof(instance.HostNames), "At least one host name is required."));
        }
        else
        {
            foreach (var hostName in instance.HostNames)
            {
                if (!SiteHostName.IsValid(SiteHostName.Normalise(hostName)))
                {
                    failures.Add(new ValidationFailure(
                        nameof(instance.HostNames),
                        $"Host name '{hostName}' is not a valid host name."));
                }
            }
        }

        return failures.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid(failures);
    }
}

internal sealed class CreateSiteHandler(
    SitesDbContext database,
    TimeProvider clock,
    SiteDirectory directory)
    : ICommandHandler<CreateSiteCommand, Result<SiteDto>>
{
    public async Task<Result<SiteDto>> HandleAsync(CreateSiteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var created = Site.Create(command.Name, command.HostNames, clock);
        if (created.IsFailure)
        {
            return Result.Failure<SiteDto>(created.Error);
        }

        var site = created.Value;
        database.Sites.Add(site);

        var hostNames = site.HostNames.Select(h => h.HostName).ToList();

        // FR-015: the event and the state change are the same SaveChanges, so either both
        // survive or neither does. No second transaction, no window where one landed and
        // the other did not.
        var occurredAt = clock.GetUtcNow();
        database.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            EventType = SiteCreated.EventType,
            Payload = JsonSerializer.Serialize(new SiteCreated(
                Guid.CreateVersion7(), occurredAt, site.Id, site.Name, hostNames)),
            OccurredAtUtc = occurredAt,
            AvailableAtUtc = occurredAt,
            TraceId = System.Diagnostics.Activity.Current?.TraceId.ToString(),
        });

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsHostNameConflict(ex))
        {
            // The unique index is the only place that can see other sites' host names, so
            // this is where a cross-site collision surfaces. Translating it into an
            // expected failure keeps it out of the unexpected-error path (FR-009).
            return Result.Failure<SiteDto>(Error.Conflict(
                "sites.host_name_taken",
                "The host name is already assigned to another site."));
        }

        foreach (var hostName in hostNames)
        {
            directory.InvalidateHostName(hostName);
        }

        return Result.Success(new SiteDto(site.Id, site.Name, hostNames, site.CreatedAtUtc));
    }

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
            .SendAsync(new CreateSiteCommand(request.Name, request.HostNames), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return ErrorResults.ToActionResult(result.Error);
        }

        return Created($"/api/v1/sites/{result.Value.Id}", result.Value);
    }
}
