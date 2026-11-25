using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.CreateCollection;

internal sealed record CreateCollectionCommand(Guid SiteId, string? Name, string? Slug, string? Description)
    : ICommand<Result<CollectionResponse>>;

/// <summary>Creates an empty collection at revision 1 (US1, FR-001, FR-002).</summary>
/// <remarks>
/// Address names are unique per site; the unique index decides, so two people creating
/// "blog" at once cannot both succeed. The limit of 100 collections is counted first; two
/// creates racing past it may briefly make 101, which the plan accepts (research CS-18).
/// </remarks>
internal sealed class CreateCollectionHandler(
    CmsDbContext database,
    CollectionReads reads,
    CmsAccess access,
    TimeProvider clock) : ICommandHandler<CreateCollectionCommand, Result<CollectionResponse>>
{
    public async Task<Result<CollectionResponse>> HandleAsync(CreateCollectionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<CollectionResponse>(CmsErrors.SiteNotFound);
        }

        var details = CollectionDetailsRules.Normalise(command.Name, command.Slug, command.Description);
        if (CollectionDetailsRules.Check(details) is { } broken)
        {
            return Result.Failure<CollectionResponse>(CmsErrors.Invalid("cms.invalid_schema", broken.Rule, broken.Pointer, broken.Message));
        }

        var count = await database.ForSite<Collection>(command.SiteId).CountAsync(cancellationToken).ConfigureAwait(false);
        if (count >= CollectionSchemaLimits.MaxCollectionsPerSite)
        {
            return Result.Failure<CollectionResponse>(CmsErrors.TooManyCollections());
        }

        var actor = access.Actor;
        var collection = Collection.Create(command.SiteId, details, actor, clock);
        database.Collections.Add(collection);
        database.SchemaChanges.Add(SchemaChangeEntry.Created(collection, actor, collection.CreatedAtUtc));
        CmsOutbox.AddFrom(database, collection, actor);

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsSlugTaken(ex))
        {
            return Result.Failure<CollectionResponse>(CmsErrors.SlugTaken);
        }

        return Result.Success(await reads.ToResponseAsync(collection, 0, new Dictionary<string, string>(), cancellationToken).ConfigureAwait(false));
    }

    internal static bool IsSlugTaken(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_collections_site_id_slug",
        };
}

/// <summary>Administrative surface: create a collection.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/collections")]
public sealed class CreateCollectionController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(CmsCapabilities.CollectionsManage)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(Guid siteId, CollectionDetailsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(
            new CreateCollectionCommand(siteId, request.Name, request.Slug, request.Description), cancellationToken).ConfigureAwait(false);

        return result.IsFailure
            ? CmsErrorResults.ToActionResult(result.Error)
            : Created(new Uri($"{Request.PathBase}/api/v1/cms/sites/{siteId}/collections/{result.Value.Id}", UriKind.Relative), result.Value);
    }
}
