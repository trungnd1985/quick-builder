using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Features.SetPageTemplate;
using QuickBuilder.Pages.Features.UpdatePage;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.MovePage;

/// <summary>
/// Where a page goes: under <see cref="ParentId"/> (null for the top level), after
/// <see cref="AfterPageId"/> (null for first), given the <see cref="Version"/> the caller saw.
/// </summary>
/// <param name="RedirectOldAddresses">017 FR-028: redirect the live addresses the move changes to their new place.</param>
public sealed record MovePageRequest(Guid? ParentId, Guid? AfterPageId, int Version, bool RedirectOldAddresses = false);

internal sealed record MovePageCommand(Guid SiteId, Guid PageId, Guid? ParentId, Guid? AfterPageId, int Version, bool RedirectOldAddresses = false)
    : ICommand<Result<PageTreeResponse>>;

/// <summary>
/// Moves a page and its subtree, or reorders it among its siblings (FR-009, FR-010).
/// </summary>
/// <remarks>
/// Returns the whole tree: positions change around both the old and the new place, and
/// addresses anywhere in the moved subtree.
/// </remarks>
internal sealed class MovePageHandler(PageTreeWrites writes, PageReads reads, PagesAccess access, AddressWarnings addresses)
    : ICommandHandler<MovePageCommand, Result<PageTreeResponse>>
{
    public async Task<Result<PageTreeResponse>> HandleAsync(MovePageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<PageTreeResponse>(PagesErrors.SiteNotFound);
        }

        var before = (await reads.PageAsync(command.SiteId, command.PageId, cancellationToken).ConfigureAwait(false))?.Path;
        var moved = await writes.ChangeOfferingRedirectsAsync(
            command.SiteId,
            (tree, actor, clock) => UpdatePageHandler.ToUnit(
                tree.Move(command.PageId, command.ParentId, command.AfterPageId, command.Version, actor, clock)),
            beforeSave: null,
            command.RedirectOldAddresses,
            cancellationToken).ConfigureAwait(false);

        if (moved.IsFailure)
        {
            return Result.Failure<PageTreeResponse>(moved.Error);
        }

        // 016 TB-12: item pages under a moved template change address; a page may hide an item's.
        var after = (await reads.PageAsync(command.SiteId, command.PageId, cancellationToken).ConfigureAwait(false))?.Path;
        var warnings = new List<TemplateWarningResponse>();
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            warnings.AddRange(await addresses.AddressesChangedAsync(command.SiteId, [command.PageId], cancellationToken).ConfigureAwait(false));
            warnings.AddRange(await addresses.AddressHiddenAsync(command.SiteId, command.PageId, cancellationToken).ConfigureAwait(false));
            warnings.AddRange(await addresses.RedirectHiddenAsync(command.SiteId, command.PageId, cancellationToken).ConfigureAwait(false));
        }

        return Result.Success(await reads.TreeAsync(command.SiteId, cancellationToken).ConfigureAwait(false) with { Warnings = warnings, Redirects = moved.Value.Redirects });
    }
}

/// <summary>Administrative surface: move or reorder a page.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/move")]
public sealed class MovePageController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PagesCapabilities.StructureManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MoveAsync(Guid siteId, Guid pageId, MovePageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(
            new MovePageCommand(siteId, pageId, request.ParentId, request.AfterPageId, request.Version, request.RedirectOldAddresses),
            cancellationToken).ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
