using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Features.SetPageTemplate;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Pages.Features.UpdatePage;

/// <summary>A page's new title and slug, and the <see cref="Version"/> the caller last saw.</summary>
/// <param name="RedirectOldAddresses">017 FR-028: redirect the live addresses the change moves to their new place.</param>
public sealed record UpdatePageRequest(string Title, string Slug, int Version, bool RedirectOldAddresses = false);

internal sealed record UpdatePageCommand(Guid SiteId, Guid PageId, string Title, string Slug, int Version, bool RedirectOldAddresses = false)
    : ICommand<Result<PageDetailResponse>>;

internal sealed class UpdatePageValidator : IValidator<UpdatePageCommand>
{
    public ValidationResult Validate(UpdatePageCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();
        PageRequestRules.CheckTitleAndSlug(instance.Title, instance.Slug, failures);
        return failures.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid(failures);
    }
}

/// <summary>
/// Changes a page's title and slug (FR-008). A new slug recomputes every descendant's
/// address in the same change, or the whole change is refused.
/// </summary>
internal sealed class UpdatePageHandler(PageTreeWrites writes, PageReads reads, PagesAccess access, AddressWarnings addresses)
    : ICommandHandler<UpdatePageCommand, Result<PageDetailResponse>>
{
    public async Task<Result<PageDetailResponse>> HandleAsync(UpdatePageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<PageDetailResponse>(PagesErrors.SiteNotFound);
        }

        var before = (await reads.PageAsync(command.SiteId, command.PageId, cancellationToken).ConfigureAwait(false))?.Path;
        var renamed = await writes.ChangeOfferingRedirectsAsync(
            command.SiteId,
            (tree, actor, clock) => ToUnit(tree.Rename(command.PageId, command.Title, command.Slug, command.Version, actor, clock)),
            beforeSave: null,
            command.RedirectOldAddresses,
            cancellationToken).ConfigureAwait(false);

        if (renamed.IsFailure)
        {
            return Result.Failure<PageDetailResponse>(renamed.Error);
        }

        // 016 TB-12: a new slug moves the item pages of templates beneath, and may hide an item's.
        var page = (await reads.PageAsync(command.SiteId, command.PageId, cancellationToken).ConfigureAwait(false))!;
        var warnings = new List<TemplateWarningResponse>();
        if (!string.Equals(before, page.Path, StringComparison.Ordinal))
        {
            warnings.AddRange(await addresses.AddressesChangedAsync(command.SiteId, [command.PageId], cancellationToken).ConfigureAwait(false));
            warnings.AddRange(await addresses.AddressHiddenAsync(command.SiteId, command.PageId, cancellationToken).ConfigureAwait(false));
            warnings.AddRange(await addresses.RedirectHiddenAsync(command.SiteId, command.PageId, cancellationToken).ConfigureAwait(false));
        }

        return Result.Success(page with { Warnings = warnings, Redirects = renamed.Value.Redirects });
    }

    internal static Result<bool> ToUnit(Result result) =>
        result.IsFailure ? Result.Failure<bool>(result.Error) : Result.Success(true);
}

/// <summary>Administrative surface: rename a page or change its slug.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}")]
public sealed class UpdatePageController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPut]
    [RequireCapability(PagesCapabilities.StructureManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(Guid siteId, Guid pageId, UpdatePageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(
            new UpdatePageCommand(siteId, pageId, request.Title, request.Slug, request.Version, request.RedirectOldAddresses),
            cancellationToken).ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
