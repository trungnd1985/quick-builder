using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Features.Trash;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.Trash;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.DeletePage;

internal sealed record DeletePageCommand(Guid SiteId, Guid PageId, int Version, Guid? RedirectToPageId = null) : ICommand<Result<TrashReceiptResponse>>;

/// <summary>
/// Moves a page without children to the site's trash (010 FR-012 to FR-014; 021 FR-001).
/// </summary>
/// <remarks>
/// The page keeps its row, draft, versions and identity (021 FR-003), and frees its address
/// (FR-004, TR-04). Its guard rows go (TR-06), its schedules are cancelled (TR-07), and the
/// existing <c>PageDeleted</c> event tells the rest of the product it is gone (TR-08). A live
/// page is still refused until it is unpublished (012 FR-006).
/// </remarks>
internal sealed class DeletePageHandler(
    PageTreeWrites writes,
    PagesAccess access,
    PagesDbContext database,
    PublishingWrites publishing,
    RedirectWrites redirectWrites,
    RedirectOffers redirectOffers,
    TrashRetention retention,
    IEditingLocks locks,
    ILogger<DeletePageHandler> logger)
    : ICommandHandler<DeletePageCommand, Result<TrashReceiptResponse>>
{
    public async Task<Result<TrashReceiptResponse>> HandleAsync(DeletePageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<TrashReceiptResponse>(PagesErrors.SiteNotFound);
        }

        // 021 FR-008, TR-09: not while another member is editing it.
        if (await locks.FindActiveHolderAsync(command.SiteId, EditingDocumentKinds.Page, command.PageId, access.Actor, cancellationToken).ConfigureAwait(false) is { } holder)
        {
            var names = await access.NamesAsync([holder.UserId], cancellationToken).ConfigureAwait(false);
            return Result.Failure<TrashReceiptResponse>(PagesErrors.DeleteLockHeld(holder.UserId, names.GetValueOrDefault(holder.UserId) ?? string.Empty, holder.Since));
        }

        // 012 FR-006: visitors can reach a live page, so it is taken down first, on purpose.
        var live = await database.ForSite<Publication>(command.SiteId)
            .AnyAsync(p => p.SubjectKind == SubjectKinds.Page && p.SubjectId == command.PageId && p.LiveVersionId != null, cancellationToken)
            .ConfigureAwait(false);
        if (live)
        {
            return Result.Failure<TrashReceiptResponse>(PagesErrors.PagePublished);
        }

        // 017 FR-032: the address the page had may still be linked to; checked before anything changes.
        if (command.RedirectToPageId is { } target
            && RedirectOffers.CheckRemovedTarget(await redirectWrites.LoadAsync(command.SiteId, cancellationToken).ConfigureAwait(false), command.PageId, target) is { } refused)
        {
            return Result.Failure<TrashReceiptResponse>(refused);
        }

        var trashed = await writes.ChangeAsync(
            command.SiteId,
            startIfMissing: false,
            (tree, actor, clock) => tree.Trash(command.PageId, command.Version, hadSchedule: false, actor, clock),
            async (_, page, ct) =>
            {
                // The tree let go of the page, which EF would take for an orphan of the tree and
                // delete. It is kept: the row is the trash entry (plan Risks).
                database.ChangeTracker.DetectChanges();
                database.Entry(page).State = EntityState.Modified;

                if (command.RedirectToPageId is { } redirectTo)
                {
                    await redirectOffers.RedirectRemovedPageAsync(command.SiteId, page.Id, redirectTo, access.Actor, ct).ConfigureAwait(false);
                }

                var shelved = await publishing.ShelveAsync(
                        new PublishingWrites.Subject(SubjectKinds.Page, page.Id, command.SiteId, page.Title, page.TrashedPath),
                        access.Actor,
                        ct)
                    .ConfigureAwait(false);

                // A live page was refused above, so shelving cannot fail on publishing grounds.
                page.RecordTrashedSchedule(shelved.IsSuccess && shelved.Value.HadSchedule);
            },
            cancellationToken).ConfigureAwait(false);

        if (trashed.IsFailure)
        {
            return Result.Failure<TrashReceiptResponse>(trashed.Error);
        }

        var trashedAt = trashed.Value.TrashedAtUtc!.Value;

        // By id only: a title or an address can itself be content (FR-037).
        logger.LogInformation("Page {PageId} moved to the trash of site {SiteId}.", command.PageId, command.SiteId);
        return Result.Success(new TrashReceiptResponse(trashedAt, retention.PurgeOn(trashedAt)));
    }
}

/// <summary>Administrative surface: delete a page.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}")]
public sealed class DeletePageController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Moves the page to the trash, where it can be restored until the receipt's date (021 FR-002).</summary>
    [HttpDelete]
    [RequireCapability(PagesCapabilities.StructureManage)]
    [ProducesResponseType<TrashReceiptResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(
        Guid siteId, Guid pageId, [FromQuery] int version, [FromQuery] Guid? redirectToPageId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new DeletePageCommand(siteId, pageId, version, redirectToPageId), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
