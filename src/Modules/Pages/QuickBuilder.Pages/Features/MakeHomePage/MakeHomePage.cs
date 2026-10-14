using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Features.UpdatePage;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.MakeHomePage;

/// <summary>The <see cref="Version"/> of the page the caller last saw.</summary>
/// <param name="RedirectOldAddresses">017 FR-028: redirect the page's former address to the home page.</param>
public sealed record MakeHomePageRequest(int Version, bool RedirectOldAddresses = false);

internal sealed record MakeHomePageCommand(Guid SiteId, Guid PageId, int Version, bool RedirectOldAddresses = false) : ICommand<Result<PageTreeResponse>>;

/// <summary>
/// Makes a top-level page the site's home page (FR-003, research P-07). The former home
/// page gets its address back from the slug it kept.
/// </summary>
internal sealed class MakeHomePageHandler(PageTreeWrites writes, PageReads reads, PagesAccess access)
    : ICommandHandler<MakeHomePageCommand, Result<PageTreeResponse>>
{
    public async Task<Result<PageTreeResponse>> HandleAsync(MakeHomePageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<PageTreeResponse>(PagesErrors.SiteNotFound);
        }

        var changed = await writes.ChangeOfferingRedirectsAsync(
            command.SiteId,
            (tree, actor, clock) => UpdatePageHandler.ToUnit(tree.MakeHome(command.PageId, command.Version, actor, clock)),
            beforeSave: null,
            command.RedirectOldAddresses,
            cancellationToken).ConfigureAwait(false);

        return changed.IsFailure
            ? Result.Failure<PageTreeResponse>(changed.Error)
            : Result.Success(await reads.TreeAsync(command.SiteId, cancellationToken).ConfigureAwait(false) with { Redirects = changed.Value.Redirects });
    }
}

/// <summary>Administrative surface: choose the home page.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/make-home")]
public sealed class MakeHomePageController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PagesCapabilities.StructureManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MakeHomeAsync(Guid siteId, Guid pageId, MakeHomePageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new MakeHomePageCommand(siteId, pageId, request.Version, request.RedirectOldAddresses), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
