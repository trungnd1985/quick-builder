using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.GetTokenUsage;

/// <summary>How many blocks, in how many drafts, refer to a design token.</summary>
public sealed record TokenUsageResponse(string TokenId, int BlockCount, int DocumentCount);

internal sealed record GetTokenUsageQuery(Guid SiteId, string TokenId) : IQuery<Result<TokenUsageResponse>>;

/// <summary>
/// Counts the blocks in a site's page and component drafts that refer to a design token
/// (011 research ED-16), so the admin can warn before the token is deleted (clarification Q4).
/// </summary>
/// <remarks>
/// <para>
/// Counted at the time of the request; nothing is stored. Pages owns the drafts, so Pages
/// answers, and the admin asks it directly — no module calls another.
/// </para>
/// <para>
/// <b>Raw SQL</b>, as the Technology Constraints allow: one parameterised
/// <c>jsonb_path_query</c> inside PostgreSQL rather than every draft of the site over the
/// wire. The module's query filters do not apply to raw SQL, so the site is filtered
/// explicitly in both halves; <c>TokenUsageSiteFilterTests</c> proves another site's drafts
/// are never counted. A node counts once however many of its properties use the token, and a
/// text value that happens to equal the id is never counted, because only a <c>token</c> key
/// is compared (page-schema.md §2a).
/// </para>
/// </remarks>
internal sealed partial class GetTokenUsageHandler(PagesDbContext database, PagesAccess access)
    : IQueryHandler<GetTokenUsageQuery, Result<TokenUsageResponse>>
{
    public async Task<Result<TokenUsageResponse>> HandleAsync(GetTokenUsageQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!TokenId().IsMatch(query.TokenId))
        {
            return Result.Failure<TokenUsageResponse>(PagesErrors.InvalidTokenId);
        }

        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<TokenUsageResponse>(PagesErrors.SiteNotFound);
        }

        var siteId = query.SiteId;
        var tokenId = query.TokenId;
        var rows = await database.Database
            .SqlQuery<UsageRow>($"""
                SELECT count(*)::int AS "BlockCount", count(DISTINCT m.document_id)::int AS "DocumentCount"
                FROM (
                    SELECT d.page_id AS document_id,
                           jsonb_path_query(d.document, 'strict $.** ? (exists(@.props.** ? (@.token == $id)))', jsonb_build_object('id', {tokenId}::text)) AS node
                    FROM pages.page_drafts d
                    JOIN pages.pages p ON p.id = d.page_id AND p.trashed_at_utc IS NULL
                    WHERE d.site_id = {siteId}
                    UNION ALL
                    SELECT c.component_id,
                           jsonb_path_query(c.document, 'strict $.** ? (exists(@.props.** ? (@.token == $id)))', jsonb_build_object('id', {tokenId}::text))
                    FROM pages.component_drafts c
                    JOIN pages.components o ON o.id = c.component_id AND o.trashed_at_utc IS NULL
                    WHERE c.site_id = {siteId}
                ) m
                """)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var row = rows.Single();
        return Result.Success(new TokenUsageResponse(tokenId, row.BlockCount, row.DocumentCount));
    }

    // The design token id grammar (docs/contracts/design-tokens.md §2), checked before the id
    // reaches the query.
    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    private static partial Regex TokenId();

    private sealed record UsageRow(int BlockCount, int DocumentCount);
}

/// <summary>Administrative surface: count the blocks that use a design token.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/token-usages/{tokenId}")]
public sealed class GetTokenUsageController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, string tokenId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetTokenUsageQuery(siteId, tokenId), cancellationToken).ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error!) : Ok(result.Value);
    }
}
