using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Editor.Features.GetDesignTokens;

/// <summary>Who last saved the token set.</summary>
public sealed record UserReferenceResponse(Guid Id, string? DisplayName);

/// <summary>What the caller may do with the token set. A courtesy for the UI; the server checks every save.</summary>
public sealed record TokenPermissionsResponse(bool CanManage, bool CanPublish);

/// <summary>A site's token set (contracts/editor-api.yaml, <c>DesignTokens</c>).</summary>
public sealed record DesignTokensResponse(
    Guid SiteId,
    uint Version,
    bool IsDefault,
    JsonElement Document,
    DateTimeOffset? UpdatedAtUtc,
    UserReferenceResponse? UpdatedBy,
    TokenPermissionsResponse Permissions,
    DesignPublishing.DesignPublicationResponse Publication);

internal sealed record GetDesignTokensQuery(Guid SiteId) : IQuery<Result<DesignTokensResponse>>;

/// <summary>
/// A site's design tokens: what it saved, or the defaults with version 0 (research ED-14).
/// </summary>
internal sealed class GetDesignTokensHandler(DesignTokenReader tokens, EditorAccess access, DesignPublishing.DesignPublicationReads publication)
    : IQueryHandler<GetDesignTokensQuery, Result<DesignTokensResponse>>
{
    public async Task<Result<DesignTokensResponse>> HandleAsync(GetDesignTokensQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<DesignTokensResponse>(EditorErrors.SiteNotFound);
        }

        // 019 FR-021: at the current schema version; a set that cannot be used is refused.
        var set = await tokens.ReadCurrentAsync(query.SiteId, cancellationToken).ConfigureAwait(false);
        return set.IsFailure
            ? Result.Failure<DesignTokensResponse>(set.Error)
            : Result.Success(await DesignTokensResponses.FromAsync(set.Value, access, publication, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Builds the response both the read and the save return.</summary>
internal static class DesignTokensResponses
{
    public static async Task<DesignTokensResponse> FromAsync(
        DesignTokenSetDto set,
        EditorAccess access,
        DesignPublishing.DesignPublicationReads publication,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(access);

        var updatedBy = set.UpdatedBy is { } userId
            ? new UserReferenceResponse(userId, await access.NameOfAsync(userId, cancellationToken).ConfigureAwait(false))
            : null;

        return new DesignTokensResponse(
            set.SiteId,
            set.Version,
            set.IsDefault,
            set.Document,
            set.UpdatedAtUtc,
            updatedBy,
            new TokenPermissionsResponse(
                await access.CanManageAsync(set.SiteId, cancellationToken).ConfigureAwait(false),
                await access.CanPublishAsync(set.SiteId, cancellationToken).ConfigureAwait(false)),
            await publication.SummaryAsync(set.SiteId, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Administrative surface: read a site's design tokens.</summary>
[ApiController]
[Route("api/v1/editor/sites/{siteId:guid}/design-tokens")]
public sealed class GetDesignTokensController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(EditorCapabilities.DesignTokensView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetDesignTokensQuery(siteId), cancellationToken).ConfigureAwait(false);

        return result.IsFailure ? EditorErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
