using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.DeletePage;

internal sealed record DeletePageCommand(Guid SiteId, Guid PageId, int Version, Guid? RedirectToPageId = null) : ICommand<Result>;

/// <summary>
/// Permanently deletes a page without children (FR-012 to FR-014, clarification Q2).
/// </summary>
/// <remarks>
/// Its draft and usage rows go with it through their foreign keys. There is no trash in
/// this feature (#21); the address is free for reuse at once. A live page is refused until it
/// is unpublished (012 FR-006).
/// </remarks>
internal sealed class DeletePageHandler(
    PageTreeWrites writes,
    PagesAccess access,
    PagesDbContext database,
    PublishingWrites publishing,
    RedirectWrites redirectWrites,
    RedirectOffers redirectOffers,
    ILogger<DeletePageHandler> logger)
    : ICommandHandler<DeletePageCommand, Result>
{
    public async Task<Result> HandleAsync(DeletePageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure(PagesErrors.SiteNotFound);
        }

        // 012 FR-006: visitors can reach a live page, so it is taken down first, on purpose.
        var live = await database.ForSite<Publication>(command.SiteId)
            .AnyAsync(p => p.SubjectKind == SubjectKinds.Page && p.SubjectId == command.PageId && p.LiveVersionId != null, cancellationToken)
            .ConfigureAwait(false);
        if (live)
        {
            return Result.Failure(PagesErrors.PagePublished);
        }

        // 017 FR-032: the address the page had may still be linked to; checked before anything changes.
        if (command.RedirectToPageId is { } target
            && RedirectOffers.CheckRemovedTarget(await redirectWrites.LoadAsync(command.SiteId, cancellationToken).ConfigureAwait(false), command.PageId, target) is { } refused)
        {
            return Result.Failure(refused);
        }

        var deleted = await writes.ChangeAsync(
            command.SiteId,
            startIfMissing: false,
            (tree, _, clock) => tree.Delete(command.PageId, command.Version, clock),
            async (_, page, ct) =>
            {
                if (command.RedirectToPageId is { } redirectTo)
                {
                    await redirectOffers.RedirectRemovedPageAsync(command.SiteId, page.Id, redirectTo, access.Actor, ct).ConfigureAwait(false);
                }

                // 012 FR-017: its publishing records go with it; the publish log keeps its
                // entries and gains a "deleted" one.
                await publishing.ForgetAsync(
                        new PublishingWrites.Subject(SubjectKinds.Page, page.Id, command.SiteId, page.Title, page.Path),
                        access.Actor,
                        ct)
                    .ConfigureAwait(false);
                database.Pages.Remove(page);
            },
            cancellationToken).ConfigureAwait(false);

        if (deleted.IsFailure)
        {
            return Result.Failure(deleted.Error);
        }

        // By id only: a title or an address can itself be content (FR-037).
        logger.LogInformation("Page {PageId} deleted from site {SiteId}.", command.PageId, command.SiteId);
        return Result.Success();
    }
}

/// <summary>Administrative surface: delete a page.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}")]
public sealed class DeletePageController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Deletes the page. Permanent: there is no restore (FR-014).</summary>
    [HttpDelete]
    [RequireCapability(PagesCapabilities.StructureManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(
        Guid siteId, Guid pageId, [FromQuery] int version, [FromQuery] Guid? redirectToPageId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new DeletePageCommand(siteId, pageId, version, redirectToPageId), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : NoContent();
    }
}
