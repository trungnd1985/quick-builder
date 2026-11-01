using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.CopyPage;

/// <summary>
/// "Save my version as a copy" (023 FR-020, research CE-11): the member's document, when the
/// version they had loaded was saved, and the copy's localised title (or name, for a component).
/// </summary>
public sealed record CopyDocumentRequest(JsonElement Document, DateTimeOffset BaseSavedAtUtc, string? Title, string? Name);

internal sealed record CopyPageCommand(Guid SiteId, Guid PageId, JsonElement Document, DateTimeOffset BaseSavedAtUtc, string? Title)
    : ICommand<Result<PageDetailResponse>>;

/// <summary>
/// Saves the member's version of a page as a new, unpublished page right after it, under the
/// same parent, with a slug made unique (023 FR-020 to FR-022). The page and its draft commit
/// together: the document is checked first, as a draft save checks it, so a refused copy
/// creates nothing.
/// </summary>
internal sealed class CopyPageHandler(
    PageTreeWrites writes,
    DraftWrites drafts,
    PageReads reads,
    PagesAccess access,
    PagesDbContext database)
    : ICommandHandler<CopyPageCommand, Result<PageDetailResponse>>
{
    public async Task<Result<PageDetailResponse>> HandleAsync(CopyPageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<PageDetailResponse>(PagesErrors.SiteNotFound);
        }

        if (!await database.ForSite<Page>(command.SiteId).AnyAsync(p => p.Id == command.PageId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<PageDetailResponse>(PagesErrors.NotFound);
        }

        var checkedCopy = await drafts.CheckCopyAsync(command.SiteId, command.Document, DocumentKind.Page, cancellationToken).ConfigureAwait(false);
        if (checkedCopy.IsFailure)
        {
            return Result.Failure<PageDetailResponse>(checkedCopy.Error);
        }

        var created = await writes.ChangeAsync(
            command.SiteId,
            startIfMissing: false,
            (tree, actor, time) =>
            {
                var source = tree.Pages.FirstOrDefault(p => p.Id == command.PageId);
                if (source is null)
                {
                    return Result.Failure<Page>(PagesErrors.NotFound);
                }

                var siblings = tree.Pages.Where(p => p.ParentId == source.ParentId).Select(p => p.Slug).ToList();
                var title = CopyNaming.Text(command.Title, Page.MaxTitleLength, fallback: source.Title);
                var page = tree.CreatePage(title, CopyNaming.Slug(source.Slug, siblings), source.ParentId, Placement.After(source.Id), isHome: false, actor, time);
                if (page.IsSuccess)
                {
                    page.Value.MarkCopyOf(new CopyOrigin(source.Id, command.BaseSavedAtUtc, actor, time.GetUtcNow()));
                }

                return page;
            },
            (_, page, ct) => drafts.StageCopyDraftAsync(command.SiteId, SubjectKinds.Page, page.Id, checkedCopy.Value, ct),
            cancellationToken).ConfigureAwait(false);

        if (created.IsFailure)
        {
            return Result.Failure<PageDetailResponse>(created.Error);
        }

        return Result.Success((await reads.PageAsync(command.SiteId, created.Value.Id, cancellationToken).ConfigureAwait(false))!);
    }
}

/// <summary>Administrative surface: save a version of a page as a new page.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/copies")]
public sealed class CopyPageController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [DraftSizeLimit]
    [RequireCapability(PagesCapabilities.StructureManage)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CopyAsync(Guid siteId, Guid pageId, CopyDocumentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(
            new CopyPageCommand(siteId, pageId, request.Document, request.BaseSavedAtUtc, request.Title), cancellationToken).ConfigureAwait(false);
        return result.IsFailure
            ? PagesErrorResults.ToActionResult(result.Error)
            : Created(new Uri($"/api/v1/pages/sites/{siteId}/pages/{result.Value.Id}", UriKind.Relative), result.Value);
    }
}
