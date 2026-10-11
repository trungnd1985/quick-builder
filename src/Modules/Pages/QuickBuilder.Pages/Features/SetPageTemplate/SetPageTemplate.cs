using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Features.UpdatePage;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.SetPageTemplate;

/// <summary>The collection the page becomes the template of, and the page <see cref="Version"/> the caller last saw.</summary>
public sealed record SetPageTemplateRequest(Guid CollectionId, int Version);

/// <summary>A warning about what a change did to item addresses (016 data-model §4).</summary>
public sealed record TemplateWarningResponse(string Code, int? Count = null, Guid? ItemId = null);

/// <summary>The page after a template change, and what it did to item addresses.</summary>
public sealed record PageWithWarningsResponse(PageDetailResponse Page, IReadOnlyList<TemplateWarningResponse> Warnings);

/// <summary>What a template change would do, nothing changed (contracts/templates-api.yaml).</summary>
public sealed record TemplateImpactResponse(
    string? Pattern,
    string? Refusal,
    bool LiveStaticAddressLost,
    int ItemsLosingAddress,
    int ItemsGainingAddress,
    int BrokenBindings);

internal sealed record SetPageTemplateCommand(Guid SiteId, Guid PageId, Guid? CollectionId, int Version)
    : ICommand<Result<PageWithWarningsResponse>>;

internal sealed record GetTemplateImpactQuery(Guid SiteId, Guid PageId, Guid? CollectionId)
    : IQuery<Result<TemplateImpactResponse>>;

/// <summary>
/// Makes a page a collection's template, changes its collection, or turns it back into an
/// ordinary page (016 FR-002, FR-003, FR-008, FR-009; research TB-02).
/// </summary>
/// <remarks>
/// Whether the collection is one of the site's is asked of Cms first; every rule about where a
/// template may be is the tree's. The page's render generation moves with the change, so its
/// item pages and its own address reach visitors at their next request.
/// </remarks>
internal sealed class SetPageTemplateHandler(
    PageTreeWrites writes,
    PageReads reads,
    PagesAccess access,
    TemplateImpact impact,
    ICollectionSchemaReader collections,
    GenerationBump bump)
    : ICommandHandler<SetPageTemplateCommand, Result<PageWithWarningsResponse>>
{
    public async Task<Result<PageWithWarningsResponse>> HandleAsync(SetPageTemplateCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<PageWithWarningsResponse>(PagesErrors.SiteNotFound);
        }

        // Another site's page is not found, whatever collection is named (FR-037).
        if (await reads.PageAsync(command.SiteId, command.PageId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<PageWithWarningsResponse>(PagesErrors.NotFound);
        }

        if (command.CollectionId is { } collectionId
            && !(await collections.ExistingAsync(command.SiteId, [collectionId], cancellationToken).ConfigureAwait(false)).Contains(collectionId))
        {
            return Result.Failure<PageWithWarningsResponse>(PagesErrors.UnknownCollection);
        }

        // Counted before the change: what it does to addresses visitors use today.
        var before = await impact.AssessAsync(command.SiteId, command.PageId, command.CollectionId, cancellationToken).ConfigureAwait(false);

        var changed = await writes.ChangeAsync(
            command.SiteId,
            startIfMissing: false,
            (tree, actor, clock) => UpdatePageHandler.ToUnit(tree.SetTemplate(command.PageId, command.CollectionId, command.Version, actor, clock)),
            beforeSave: (_, _, ct) => bump.BumpPagesAsync(command.SiteId, [command.PageId], ct),
            cancellationToken).ConfigureAwait(false);

        if (changed.IsFailure)
        {
            return Result.Failure<PageWithWarningsResponse>(changed.Error);
        }

        var page = await reads.PageAsync(command.SiteId, command.PageId, cancellationToken).ConfigureAwait(false);
        return Result.Success(new PageWithWarningsResponse(page!, Warnings(before)));
    }

    private static List<TemplateWarningResponse> Warnings(TemplateImpactResponse? impact)
    {
        var warnings = new List<TemplateWarningResponse>();
        if (impact is { ItemsLosingAddress: > 0 } losing)
        {
            warnings.Add(new TemplateWarningResponse("items_lose_address", losing.ItemsLosingAddress));
        }

        return warnings;
    }
}

/// <summary>What making a page a template, changing or removing it would do (FR-008, contracts/templates-api.yaml).</summary>
internal sealed class GetTemplateImpactHandler(PagesAccess access, TemplateImpact impact)
    : IQueryHandler<GetTemplateImpactQuery, Result<TemplateImpactResponse>>
{
    public async Task<Result<TemplateImpactResponse>> HandleAsync(GetTemplateImpactQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<TemplateImpactResponse>(PagesErrors.SiteNotFound);
        }

        var assessed = await impact.AssessAsync(query.SiteId, query.PageId, query.CollectionId, cancellationToken).ConfigureAwait(false);
        return assessed is null
            ? Result.Failure<TemplateImpactResponse>(PagesErrors.NotFound)
            : Result.Success(assessed);
    }
}

/// <summary>
/// Works out what a template change would do, on a copy of the tree nothing tracks, so nothing
/// is ever saved (FR-008).
/// </summary>
internal sealed class TemplateImpact(PagesDbContext database, ICollectionSchemaReader collections, PagesAccess access, TimeProvider clock)
{
    public async Task<TemplateImpactResponse?> AssessAsync(Guid siteId, Guid pageId, Guid? collectionId, CancellationToken cancellationToken)
    {
        var tree = await database.ForSite<PageTree>(siteId)
            .AsNoTracking()
            .Include(t => t.Pages)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (tree?.Pages.FirstOrDefault(p => p.Id == pageId) is not { } page)
        {
            return null;
        }

        var previous = page.TemplateCollectionId;
        var isLive = await database.ForSite<Publication>(siteId)
            .AnyAsync(p => p.SubjectKind == SubjectKinds.Page && p.SubjectId == pageId && p.LiveVersionId != null, cancellationToken)
            .ConfigureAwait(false);

        string? refusal = null;
        if (collectionId is { } wanted
            && !(await collections.ExistingAsync(siteId, [wanted], cancellationToken).ConfigureAwait(false)).Contains(wanted))
        {
            refusal = PagesErrors.UnknownCollection.Code;
        }
        else if (tree.SetTemplate(pageId, collectionId, page.Version, access.Actor, clock) is { IsFailure: true } refused)
        {
            refusal = refused.Error.Code;
        }

        var pattern = refusal is null && collectionId is not null ? page.Path : null;

        var losing = isLive && previous is { } old && previous != collectionId
            ? await collections.CountPublishedItemsAsync(siteId, old, cancellationToken).ConfigureAwait(false)
            : 0;
        var gaining = isLive && collectionId is { } next && previous != collectionId
            ? await collections.CountPublishedItemsAsync(siteId, next, cancellationToken).ConfigureAwait(false)
            : 0;

        var draft = await database.ForSite<PageDraft>(siteId)
            .AsNoTracking()
            .Where(d => d.PageId == pageId)
            .Select(d => d.Document)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var broken = 0;
        if (draft is not null && previous != collectionId)
        {
            IReadOnlySet<string>? fields = null;
            if (collectionId is { } target)
            {
                var schemas = await collections.GetSchemasAsync(siteId, [target], cancellationToken).ConfigureAwait(false);
                fields = schemas.TryGetValue(target, out var schema) ? FieldIds(schema) : new HashSet<string>();
            }

            broken = PageScopeReaders.Breaking(draft, fields);
        }

        return new TemplateImpactResponse(
            pattern,
            refusal,
            LiveStaticAddressLost: isLive && previous is null && collectionId is not null,
            ItemsLosingAddress: losing,
            ItemsGainingAddress: gaining,
            BrokenBindings: broken);
    }

    private static HashSet<string> FieldIds(JsonElement schema) =>
        schema.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array
            ? [.. fields.EnumerateArray().Where(f => f.TryGetProperty("id", out _)).Select(f => f.GetProperty("id").GetString()!)]
            : [];
}

/// <summary>Administrative surface: a page as a collection's template (contracts/templates-api.yaml).</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/template")]
public sealed class PageTemplateController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(PagesCapabilities.StructureManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ImpactAsync(Guid siteId, Guid pageId, [FromQuery] Guid? collectionId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetTemplateImpactQuery(siteId, pageId, collectionId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    [HttpPut]
    [RequireCapability(PagesCapabilities.StructureManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetAsync(Guid siteId, Guid pageId, SetPageTemplateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new SetPageTemplateCommand(siteId, pageId, request.CollectionId, request.Version), cancellationToken)
            .ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    [HttpDelete]
    [RequireCapability(PagesCapabilities.StructureManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveAsync(Guid siteId, Guid pageId, [FromQuery] int version, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new SetPageTemplateCommand(siteId, pageId, null, version), cancellationToken)
            .ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
