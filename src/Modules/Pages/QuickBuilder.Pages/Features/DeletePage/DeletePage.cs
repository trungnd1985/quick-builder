using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.DeletePage;

internal sealed record DeletePageCommand(Guid SiteId, Guid PageId, int Version) : ICommand<Result>;

/// <summary>
/// Permanently deletes a page without children (FR-012 to FR-014, clarification Q2).
/// </summary>
/// <remarks>
/// Its draft and usage rows go with it through their foreign keys. There is no trash in
/// this feature (#21); the address is free for reuse at once.
/// </remarks>
internal sealed class DeletePageHandler(
    PageTreeWrites writes,
    PagesAccess access,
    PagesDbContext database,
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

        var deleted = await writes.ChangeAsync(
            command.SiteId,
            startIfMissing: false,
            (tree, _, clock) => tree.Delete(command.PageId, command.Version, clock),
            (_, page) => database.Pages.Remove(page),
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
    public async Task<IActionResult> DeleteAsync(Guid siteId, Guid pageId, [FromQuery] int version, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new DeletePageCommand(siteId, pageId, version), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : NoContent();
    }
}
