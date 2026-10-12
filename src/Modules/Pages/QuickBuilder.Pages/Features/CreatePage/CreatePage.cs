using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Pages.Features.CreatePage;

/// <summary>
/// A new page. <see cref="AfterPageId"/> is absent for "last", <c>null</c> for "first", or
/// the sibling to follow (contracts/pages-api.yaml).
/// </summary>
public sealed record CreatePageRequest(string Title, string Slug, Guid? ParentId, JsonElement AfterPageId, bool IsHome);

internal sealed record CreatePageCommand(
    Guid SiteId,
    string Title,
    string Slug,
    Guid? ParentId,
    Placement Placement,
    bool IsHome) : ICommand<Result<PageDetailResponse>>;

internal sealed class CreatePageValidator : IValidator<CreatePageCommand>
{
    public ValidationResult Validate(CreatePageCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();
        PageRequestRules.CheckTitleAndSlug(instance.Title, instance.Slug, failures);
        return failures.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid(failures);
    }
}

/// <summary>
/// Adds a page to a site's tree, with an empty draft (FR-002, FR-003, FR-015).
/// </summary>
/// <remarks>
/// The site's first page also creates its tree. Two first pages racing both try to insert
/// the tree's root row; the loser retries against the winner's tree (research P-05).
/// </remarks>
internal sealed class CreatePageHandler(
    PageTreeWrites writes,
    PageReads reads,
    PagesAccess access,
    PagesDbContext database,
    TimeProvider clock,
    AddressWarnings addresses)
    : ICommandHandler<CreatePageCommand, Result<PageDetailResponse>>
{
    public async Task<Result<PageDetailResponse>> HandleAsync(CreatePageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<PageDetailResponse>(PagesErrors.SiteNotFound);
        }

        var created = await writes.ChangeAsync(
            command.SiteId,
            startIfMissing: true,
            (tree, actor, time) => tree.CreatePage(
                command.Title, command.Slug, command.ParentId, command.Placement, command.IsHome, actor, time),
            (_, page) => database.PageDrafts.Add(PageDraft.CreateEmpty(page.SiteId, page.Id, page.CreatedBy, clock)),
            cancellationToken).ConfigureAwait(false);

        if (created.IsFailure)
        {
            return Result.Failure<PageDetailResponse>(created.Error);
        }

        // 016 TB-12: a page beside a template may take an item's address.
        var page = (await reads.PageAsync(command.SiteId, created.Value.Id, cancellationToken).ConfigureAwait(false))!;
        return Result.Success(page with { Warnings = await addresses.AddressHiddenAsync(command.SiteId, page.Id, cancellationToken).ConfigureAwait(false) });
    }
}

/// <summary>Administrative surface: add a page.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/pages")]
public sealed class CreatePageController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PagesCapabilities.StructureManage)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(Guid siteId, CreatePageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(
            new CreatePageCommand(
                siteId,
                request.Title,
                request.Slug,
                request.ParentId,
                PageRequestRules.ToPlacement(request.AfterPageId),
                request.IsHome),
            cancellationToken).ConfigureAwait(false);

        return result.IsFailure
            ? PagesErrorResults.ToActionResult(result.Error)
            : Created(new Uri($"/api/v1/pages/sites/{siteId}/pages/{result.Value.Id}", UriKind.Relative), result.Value);
    }
}
