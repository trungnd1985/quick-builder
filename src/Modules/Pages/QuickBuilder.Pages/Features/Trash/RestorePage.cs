using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.Trash;

internal sealed record PageRestorePlanQuery(Guid SiteId, Guid PageId, string? Placement, Guid? ParentId) : IQuery<Result<RestorePlanResponse>>;

internal sealed record RestorePageCommand(Guid SiteId, Guid PageId, RestoreRequest Request) : ICommand<Result<RestoreResultResponse>>;

/// <summary>
/// Plans and performs a page's restore (021 FR-014 to FR-017, research TR-10, TR-11).
/// </summary>
/// <remarks>
/// The member sees the plan first, then confirms the address they saw. The restore computes
/// the plan again in its own transaction, under the tree's concurrency token, and refuses with
/// <c>trash.plan_changed</c> and the new plan when it no longer matches, so nothing happens
/// that the member was not told. "Restore and publish" runs 012's rollback to the last live
/// version in the same transaction (TR-11).
/// </remarks>
internal sealed class RestorePageHandler(
    PagesTrash trash,
    PagesAccess access,
    PagesDbContext database,
    PageTreeWrites writes,
    PublishingWrites publishing,
    TimeProvider clock,
    ILogger<RestorePageHandler> logger)
    : IQueryHandler<PageRestorePlanQuery, Result<RestorePlanResponse>>, ICommandHandler<RestorePageCommand, Result<RestoreResultResponse>>
{
    private const string Reason = "Restoring a page from the site's trash (021 TR-10).";

    public async Task<Result<RestorePlanResponse>> HandleAsync(PageRestorePlanQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<RestorePlanResponse>(PagesErrors.SiteNotFound);
        }

        var page = await trash.TrashedPages(query.SiteId).AsNoTracking().FirstOrDefaultAsync(p => p.Id == query.PageId, cancellationToken).ConfigureAwait(false);
        if (page is null)
        {
            return Result.Failure<RestorePlanResponse>(PagesErrors.NotInTrash);
        }

        var tree = await database.ForSite<PageTree>(query.SiteId).AsNoTracking().Include(t => t.Pages).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? PageTree.Start(query.SiteId, clock);

        var plan = await trash.PlanPageAsync(tree, page, query.Placement, query.ParentId, tracked: false, cancellationToken).ConfigureAwait(false);
        return plan.IsFailure ? Result.Failure<RestorePlanResponse>(plan.Error) : Result.Success(plan.Value.Plan);
    }

    public async Task<Result<RestoreResultResponse>> HandleAsync(RestorePageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<RestoreResultResponse>(PagesErrors.SiteNotFound);
        }

        // TR-11: publishing needs its own capability, checked before anything changes.
        if (request.Publish && !await trash.MayAsync(PublishingCapabilities.Manage, command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<RestoreResultResponse>(PagesErrors.Forbidden);
        }

        IReadOnlyList<Page> ancestors = [];
        var restored = await writes.ChangeReadingAsync(
            command.SiteId,
            async (tree, actor, treeClock, ct) =>
            {
                // Re-read under the tree's token: a restore or purge that won leaves nothing (FR-020).
                var page = await trash.TrashedPages(command.SiteId).FirstOrDefaultAsync(p => p.Id == command.PageId, ct).ConfigureAwait(false);
                if (page is null)
                {
                    return Result.Failure<Page>(PagesErrors.NotInTrash);
                }

                var planned = await trash.PlanPageAsync(tree, page, request.Placement, request.ParentId, tracked: true, ct).ConfigureAwait(false);
                if (planned.IsFailure)
                {
                    return Result.Failure<Page>(planned.Error);
                }

                if (!string.Equals(planned.Value.Plan.Address, request.ConfirmedAddress, StringComparison.Ordinal))
                {
                    return Result.Failure<Page>(PagesErrors.PlanChanged(planned.Value.Plan));
                }

                ancestors = planned.Value.Ancestors;
                var done = tree.Restore(page, planned.Value.Outcome, actor, treeClock);
                return done.IsFailure ? Result.Failure<Page>(done.Error) : Result.Success(page);
            },
            async (_, page, ct) =>
            {
                foreach (var ancestor in ancestors)
                {
                    await publishing.UnshelveAsync(new PublishingWrites.Subject(SubjectKinds.Page, ancestor.Id, command.SiteId, ancestor.Title, ancestor.Path, ancestor.TemplateCollectionId), access.Actor, ct).ConfigureAwait(false);
                }

                var subject = new PublishingWrites.Subject(SubjectKinds.Page, page.Id, command.SiteId, page.Title, page.Path, page.TemplateCollectionId);
                await publishing.UnshelveAsync(subject, access.Actor, ct).ConfigureAwait(false);

                if (request.Publish)
                {
                    await database.SaveChangesAsync(ct).ConfigureAwait(false);
                    var version = await trash.LastLiveVersionAsync(command.SiteId, SubjectKinds.Page, page.Id, ct).ConfigureAwait(false);
                    var published = version is { } number
                        ? await publishing.RollbackContentAsync(subject, number, clock.GetUtcNow(), ct).ConfigureAwait(false)
                        : Result.Failure(PagesErrors.VersionNotFound);
                    if (published.IsFailure)
                    {
                        throw new RestoreAndPublishFailedException(published.Error);
                    }
                }
            },
            cancellationToken).ConfigureAwait(false);

        if (restored.IsFailure)
        {
            return Result.Failure<RestoreResultResponse>(restored.Error);
        }

        logger.LogInformation("Page {PageId} restored from the trash of site {SiteId} (published: {Published}).", command.PageId, command.SiteId, request.Publish);
        return Result.Success(new RestoreResultResponse(
            [.. ancestors.Select(a => new RestoredResponse(TrashKinds.Page, a.Id, a.Path)), new RestoredResponse(TrashKinds.Page, command.PageId, restored.Value.Path)],
            request.Publish));
    }
}

/// <summary>
/// "Restore and publish" could not publish, so the whole restore is rolled back (TR-11): the
/// member asked for both in one confirmed step.
/// </summary>
internal sealed class RestoreAndPublishFailedException(Error error) : Exception(error.Message)
{
    public Error Error { get; } = error;
}

/// <summary>Administrative surface: restore a trashed page.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/trash/pages/{pageId:guid}")]
public sealed class RestorePageController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>What restoring the page would do (TR-10).</summary>
    [HttpGet("restore-plan")]
    [RequireCapability(PagesCapabilities.StructureManage)]
    [ProducesResponseType<RestorePlanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> PlanAsync(Guid siteId, Guid pageId, [FromQuery] string? placement, [FromQuery] Guid? parentId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new PageRestorePlanQuery(siteId, pageId, placement, parentId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    /// <summary>Restores the page as planned, optionally publishing its last live version.</summary>
    [HttpPost("restore")]
    [RequireCapability(PagesCapabilities.StructureManage)]
    [ProducesResponseType<RestoreResultResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RestoreAsync(Guid siteId, Guid pageId, [FromBody] RestoreRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await dispatcher.SendAsync(new RestorePageCommand(siteId, pageId, request), cancellationToken).ConfigureAwait(false);
            return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
        }
        catch (RestoreAndPublishFailedException failed)
        {
            return PagesErrorResults.ToActionResult(failed.Error);
        }
    }
}
