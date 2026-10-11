using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Cms.Domain;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms.Features.ListPreview;

/// <summary>What the editor asks to preview (contracts/templates-api.yaml, <c>list-preview</c>).</summary>
/// <param name="List">The <c>collection-list</c> props, without children.</param>
/// <param name="Scopes">Enclosing scope items by scope name: the sample item for <c>page</c>, a card's item for a list id.</param>
/// <param name="Page">The page of a paginated list, from 1.</param>
/// <param name="Via">Reference fields whose referenced items come back too.</param>
public sealed record ListPreviewRequest(JsonElement List, IReadOnlyDictionary<string, Guid>? Scopes, int? Page, IReadOnlyList<string>? Via);

internal sealed record ListPreviewQuery(Guid SiteId, ListPreviewRequest Request) : IQuery<Result<ListPreviewResponse>>;

/// <summary>One previewed item, with its address when its collection's template is live.</summary>
public sealed record ListPreviewItemResponse(
    Guid Id,
    string Slug,
    string? Name,
    JsonElement Values,
    string? Address,
    DateTimeOffset FirstPublishedAt,
    DateTimeOffset LastPublishedAt);

/// <summary>The listed items, how many match, the items they reference, and the schemas involved.</summary>
public sealed record ListPreviewResponse(
    IReadOnlyList<ListPreviewItemResponse> Items,
    int Total,
    IReadOnlyDictionary<Guid, ListPreviewItemResponse> Referenced,
    IReadOnlyDictionary<Guid, JsonElement> Schemas);

/// <summary>
/// Runs a Collection list against published data for the editor's canvas (016 US4, research
/// TB-14): the cards a designer sees are the items visitors would see.
/// </summary>
/// <remarks>
/// <para>
/// The list is read by <see cref="ListQueryBuilder"/>, as the public renderer reads it, and run
/// through the public path's reader, for the named site (<see cref="SitePublishedItems"/>).
/// Like rules 14 and 15, a source that is not a collection of the site and a scope the request
/// does not supply are refused (<c>cms.invalid_list</c>); a filter that no longer fits matches
/// nothing, as on the site.
/// </para>
/// <para>
/// The <c>page</c> scope is the template's sample item, read from its <b>draft</b> values (016
/// clarification: sample item draft values), so a designer sees the item they are shaping. The
/// items listed are always published.
/// </para>
/// </remarks>
internal sealed class ListPreviewHandler(
    CmsDbContext database,
    CmsAccess access,
    SitePublishedItems published,
    ITemplateReader templates)
    : IQueryHandler<ListPreviewQuery, Result<ListPreviewResponse>>
{
    public const int MaxFilters = 10;
    public const int MaxSorts = 3;
    public const int MaxVia = 20;
    public const int MaxPage = 10_000;

    public async Task<Result<ListPreviewResponse>> HandleAsync(ListPreviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (siteId, request) = query;

        if (!await access.SiteExistsAsync(siteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<ListPreviewResponse>(CmsErrors.SiteNotFound);
        }

        var list = request.List;
        if (list.ValueKind != JsonValueKind.Object
            || Count(list, "filters") > MaxFilters
            || Count(list, "sort") > MaxSorts
            || (request.Via?.Count ?? 0) > MaxVia
            || request.Page is < 1 or > MaxPage)
        {
            return Invalid("invalid");
        }

        var scopeItems = request.Scopes ?? new Dictionary<string, Guid>();
        foreach (var name in ScopesRead(list))
        {
            if (!scopeItems.ContainsKey(name))
            {
                return Invalid("unknown_scope");
            }
        }

        await using var reader = published.For(siteId);

        if (list.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object
            && source.TryGetProperty("collectionId", out var collection))
        {
            if (!Guid.TryParse(collection.GetString(), out var collectionId)
                || !(await reader.Reader.GetSchemasAsync(siteId, [collectionId], cancellationToken).ConfigureAwait(false)).ContainsKey(collectionId))
            {
                return Invalid("unknown_collection");
            }
        }

        var scopes = await ScopesAsync(siteId, scopeItems, reader.Reader, cancellationToken).ConfigureAwait(false);
        var listQuery = ListQueryBuilder.Build(list, scopes, request.Page ?? 1);
        var page = listQuery is null
            ? PublishedItemPage.Empty
            : await reader.Reader.QueryAsync(siteId, listQuery, cancellationToken).ConfigureAwait(false);

        var referencedIds = page.Items
            .SelectMany(item => (request.Via ?? []).SelectMany(field => ReferencedIds(item.Values, field)))
            .ToHashSet();
        var referenced = await reader.Reader.GetAsync(siteId, referencedIds, cancellationToken).ConfigureAwait(false);

        var collections = page.Items.Concat(referenced.Values).Select(i => i.CollectionId).ToHashSet();
        if (listQuery is not null)
        {
            collections.Add(listQuery.CollectionId);
        }

        var schemas = await reader.Reader.GetSchemasAsync(siteId, collections, cancellationToken).ConfigureAwait(false);
        var patterns = await templates.GetTemplatesAsync(siteId, collections, cancellationToken).ConfigureAwait(false);

        ListPreviewItemResponse ToResponse(PublishedItem item) => new(
            item.Id,
            item.Slug,
            item.Name,
            item.Values,
            patterns.TryGetValue(item.CollectionId, out var template) && template.IsLive ? template.AddressOf(item.Slug) : null,
            item.FirstPublishedAtUtc,
            item.LastPublishedAtUtc);

        return Result.Success(new ListPreviewResponse(
            [.. page.Items.Select(ToResponse)],
            page.Total,
            referenced.ToDictionary(r => r.Key, r => ToResponse(r.Value)),
            schemas));
    }

    /// <summary>
    /// The request's scopes, outermost first: the sample item (draft values) for <c>page</c>,
    /// then each card's published item. A scope whose item cannot be read is left out, so what
    /// reads it matches nothing.
    /// </summary>
    private async Task<List<ListScope>> ScopesAsync(
        Guid siteId, IReadOnlyDictionary<string, Guid> scopeItems, PublishedItemReader reader, CancellationToken cancellationToken)
    {
        var scopes = new List<ListScope>();

        if (scopeItems.TryGetValue(ListScope.Page, out var sampleId))
        {
            var sample = await database.ForSite<Item>(siteId)
                .AsNoTracking()
                .Where(i => i.Id == sampleId)
                .Select(i => new { i.CollectionId, i.Slug, i.Draft })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (sample is not null)
            {
                using var draft = sample.Draft;
                var values = draft.RootElement.TryGetProperty("values", out var v) ? v.Clone() : default;
                var targets = await TargetsAsync(siteId, sample.CollectionId, reader, cancellationToken).ConfigureAwait(false);
                scopes.Add(new ListScope(ListScope.Page, sample.Slug, field => Read(values, field), field => targets.GetValueOrDefault(field)));
            }
        }

        var cardItems = scopeItems.Where(s => s.Key != ListScope.Page).ToList();
        var cards = await reader.GetAsync(siteId, [.. cardItems.Select(s => s.Value)], cancellationToken).ConfigureAwait(false);
        foreach (var (name, itemId) in cardItems)
        {
            if (cards.TryGetValue(itemId, out var item))
            {
                var values = item.Values.TryGetProperty("values", out var v) ? v : default;
                var targets = await TargetsAsync(siteId, item.CollectionId, reader, cancellationToken).ConfigureAwait(false);
                scopes.Add(new ListScope(name, item.Slug, field => Read(values, field), field => targets.GetValueOrDefault(field)));
            }
        }

        return scopes;
    }

    /// <summary>The target collections of a collection's multi-reference fields, by field id.</summary>
    private static async Task<Dictionary<string, Guid?>> TargetsAsync(
        Guid siteId, Guid collectionId, PublishedItemReader reader, CancellationToken cancellationToken)
    {
        var targets = new Dictionary<string, Guid?>(StringComparer.Ordinal);
        var schemas = await reader.GetSchemasAsync(siteId, [collectionId], cancellationToken).ConfigureAwait(false);
        if (schemas.TryGetValue(collectionId, out var schema) && schema.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
        {
            foreach (var field in fields.EnumerateArray())
            {
                if (field.TryGetProperty("type", out var type) && type.GetString() == "multiReference"
                    && field.TryGetProperty("id", out var id) && id.GetString() is { } fieldId
                    && field.TryGetProperty("target", out var target) && Guid.TryParse(target.GetString(), out var targetId))
                {
                    targets[fieldId] = targetId;
                }
            }
        }

        return targets;
    }

    private static JsonElement? Read(JsonElement values, string fieldId) =>
        values.ValueKind == JsonValueKind.Object && values.TryGetProperty(fieldId, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;

    /// <summary>The ids a reference or multi-reference value holds.</summary>
    private static IEnumerable<Guid> ReferencedIds(JsonElement document, string fieldId)
    {
        if (!document.TryGetProperty("values", out var values) || Read(values, fieldId) is not { } value)
        {
            yield break;
        }

        IEnumerable<JsonElement> candidates = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [value];
        foreach (var candidate in candidates)
        {
            if (candidate.ValueKind == JsonValueKind.String && Guid.TryParse(candidate.GetString(), out var id))
            {
                yield return id;
            }
        }
    }

    /// <summary>Every scope the list's source and filters name.</summary>
    private static IEnumerable<string> ScopesRead(JsonElement list)
    {
        if (list.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object)
        {
            if (source.TryGetProperty("scope", out var scope) && scope.GetString() is { } name)
            {
                yield return name;
            }
            else if (source.TryGetProperty("field", out _))
            {
                // A field source with no scope reads the nearest one, which must exist.
                yield return ListScope.Page;
            }
        }

        if (list.TryGetProperty("filters", out var filters) && filters.ValueKind == JsonValueKind.Array)
        {
            foreach (var filter in filters.EnumerateArray())
            {
                if (filter.ValueKind == JsonValueKind.Object && filter.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Object
                    && value.TryGetProperty("scope", out var scope) && scope.GetString() is { } name)
                {
                    yield return name;
                }
            }
        }
    }

    private static int Count(JsonElement list, string property) =>
        list.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array ? array.GetArrayLength() : 0;

    private static Result<ListPreviewResponse> Invalid(string reason) => Result.Failure<ListPreviewResponse>(CmsErrors.InvalidList(reason));
}

/// <summary>Administrative surface: preview a Collection list on the editor's canvas.</summary>
[ApiController]
[Route("api/v1/cms/sites/{siteId:guid}/list-preview")]
public sealed class ListPreviewController(IDispatcher dispatcher) : ControllerBase
{
    /// <remarks>
    /// Asks for page draft editing, which Designers, Editors and Administrators hold: only
    /// people editing a page or a component need it (016 research TB-17).
    /// </remarks>
    [HttpPost]
    [RequireCapability(PageContentCapabilities.DraftsEdit)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PreviewAsync(Guid siteId, ListPreviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.QueryAsync(new ListPreviewQuery(siteId, request), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? CmsErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
