using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Domain.Redirects;
using QuickBuilder.Pages.Infrastructure;

namespace QuickBuilder.Pages.Features.Redirects;

/// <summary>A redirect as the admin sees it (seo-api.yaml, <c>Redirect</c>).</summary>
public sealed record RedirectResponse(
    Guid Id,
    string Source,
    string Match,
    RedirectTargetResponse Target,
    string? TargetAddress,
    int StatusCode,
    bool Enabled,
    string? Note,
    string State,
    RedirectHiddenByResponse? HiddenBy,
    string Origin,
    long Hits,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version)
{
    /// <summary>Create and update responses: the target was replaced by the end of a chain.</summary>
    public bool? Shortened { get; init; }

    /// <summary>Create and update responses: things to know, never refusals (data-model §4).</summary>
    public IReadOnlyList<string>? Warnings { get; init; }

    /// <summary>Get responses: the last audit entries (SE-13a).</summary>
    public IReadOnlyList<RedirectHistoryResponse>? History { get; init; }
}

/// <summary>Where a redirect sends visitors, in the shape it was given.</summary>
public sealed record RedirectTargetResponse(string Kind, Guid? PageId = null, Guid? CollectionId = null, Guid? ItemId = null, string? Path = null, string? Url = null);

/// <summary>The live content that answers a redirect's source first (FR-016).</summary>
public sealed record RedirectHiddenByResponse(string Kind, Guid Id);

/// <summary>One audit entry: ids, codes and times only (FR-021).</summary>
public sealed record RedirectHistoryResponse(string Action, string Origin, Guid? ActorUserId, DateTimeOffset At);

/// <summary>One page of redirects (seo-api.yaml).</summary>
public sealed record RedirectListResponse(IReadOnlyList<RedirectResponse> Items, int Total, int Limit);

/// <summary>Request shape of a redirect's target.</summary>
public sealed record RedirectTargetRequest(string Kind, Guid? PageId, Guid? CollectionId, Guid? ItemId, string? Path, string? Url);

/// <summary>Derived states of a redirect (data-model §3.2).</summary>
internal static class RedirectStates
{
    public const string Active = "active";

    public const string Off = "off";

    public const string Hidden = "hidden";

    public const string Broken = "broken";
}

/// <summary>Warnings on a redirect (data-model §4).</summary>
internal static class RedirectWarnings
{
    public const string SourceHoldsContent = "source_holds_content";

    public const string SourceUsesPageParam = "source_uses_page_param";
}

/// <summary>
/// Builds <see cref="RedirectResponse"/>s: the target's current address, the derived state,
/// usage and warnings (017 research SE-13).
/// </summary>
internal sealed class RedirectViews(PagesDbContext database, ICollectionSchemaReader items)
{
    /// <summary>Responses for <paramref name="redirects"/>, in their order.</summary>
    public async Task<List<RedirectResponse>> BuildAsync(RedirectSiteState state, IReadOnlyList<Redirect> redirects, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(redirects);

        var ids = redirects.Select(r => r.Id).ToList();
        var usage = await database.ForSite<RedirectUsage>(state.SiteId)
            .AsNoTracking()
            .Where(u => ids.Contains(u.RedirectId))
            .ToDictionaryAsync(u => u.RedirectId, cancellationToken)
            .ConfigureAwait(false);

        var responses = new List<RedirectResponse>(redirects.Count);
        foreach (var redirect in redirects)
        {
            var hiddenBy = redirect.SourceQuery.Length == 0 ? await HiddenByAsync(state, redirect.SourcePath, cancellationToken).ConfigureAwait(false) : null;
            var address = TargetAddress(state, redirect.Target);
            var broken = address is null;
            var stateName = !redirect.Enabled ? RedirectStates.Off
                : hiddenBy is not null ? RedirectStates.Hidden
                : broken ? RedirectStates.Broken
                : RedirectStates.Active;

            usage.TryGetValue(redirect.Id, out var used);
            responses.Add(new RedirectResponse(
                redirect.Id,
                Source(redirect),
                redirect.MatchKind,
                TargetOf(redirect.Target),
                address,
                redirect.StatusCode,
                redirect.Enabled,
                redirect.Note,
                stateName,
                hiddenBy,
                redirect.Origin,
                used?.Hits ?? 0,
                used?.LastUsedAtUtc,
                redirect.CreatedAtUtc,
                redirect.UpdatedAtUtc,
                redirect.Version));
        }

        return responses;
    }

    /// <summary>The live page or item page that answers <paramref name="path"/> before any redirect.</summary>
    public async Task<RedirectHiddenByResponse?> HiddenByAsync(RedirectSiteState state, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.LivePaths.Contains(path) && state.PageByPath.TryGetValue(path, out var pageId))
        {
            return new RedirectHiddenByResponse("page", pageId);
        }

        var slash = path.LastIndexOf('/');
        if (slash <= 0)
        {
            return null;
        }

        var pattern = path[..slash] + "/{slug}";
        var template = state.LivePatterns.FirstOrDefault(p => string.Equals(p.Value, pattern, StringComparison.Ordinal));
        if (template.Value is null)
        {
            return null;
        }

        var item = await items.PublishedItemWithSlugAsync(state.SiteId, template.Key, path[(slash + 1)..], cancellationToken).ConfigureAwait(false);
        return item is { } itemId ? new RedirectHiddenByResponse("item", itemId) : null;
    }

    /// <summary>The warnings of FR-017a for a query source; none for other sources.</summary>
    public async Task<IReadOnlyList<string>> WarningsAsync(RedirectSiteState state, Redirect redirect, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(redirect);

        if (redirect.SourceQuery.Length == 0)
        {
            return [];
        }

        var warnings = new List<string>();
        if (await HiddenByAsync(state, redirect.SourcePath, cancellationToken).ConfigureAwait(false) is not null)
        {
            warnings.Add(RedirectWarnings.SourceHoldsContent);
        }

        var names = redirect.SourceQuery.Split('&').Select(p => Uri.UnescapeDataString(p.Split('=', 2)[0])).ToHashSet(StringComparer.Ordinal);
        var pageParams = await PageParamsAtAsync(state, redirect.SourcePath, cancellationToken).ConfigureAwait(false);
        if (pageParams.Any(names.Contains))
        {
            warnings.Add(RedirectWarnings.SourceUsesPageParam);
        }

        return warnings;
    }

    /// <summary>The paginated lists' parameters of the live page or template answering <paramref name="path"/>.</summary>
    private async Task<IReadOnlyList<string>> PageParamsAtAsync(RedirectSiteState state, string path, CancellationToken cancellationToken)
    {
        Guid? pageId = state.PageByPath.TryGetValue(path, out var page) && state.LivePaths.Contains(path) ? page : null;
        if (pageId is null && path.LastIndexOf('/') is var slash and > 0)
        {
            var pattern = path[..slash] + "/{slug}";
            pageId = state.Pages.FirstOrDefault(p => p.Value.IsTemplate && p.Value.IsLive && p.Value.Path == pattern).Key;
        }

        if (pageId is not { } id || id == Guid.Empty)
        {
            return [];
        }

        return await database.ForSite<Publication>(state.SiteId)
            .AsNoTracking()
            .Where(p => p.SubjectKind == SubjectKinds.Page && p.SubjectId == id)
            .Select(p => p.PageParamNames)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? [];
    }

    /// <summary>The source as members write it: the path, and its query when it has one.</summary>
    public static string Source(Redirect redirect) =>
        redirect.SourceQuery.Length == 0 ? redirect.SourcePath : $"{redirect.SourcePath}?{redirect.SourceQuery}";

    /// <summary>Where the redirect sends visitors now; null when its page or item has no live address.</summary>
    public static string? TargetAddress(RedirectSiteState state, RedirectTarget target) => target.Kind switch
    {
        RedirectTargetKinds.Page => target.PageId is { } id && state.Pages.TryGetValue(id, out var page) && page.IsLive && !page.IsTemplate ? page.Path : null,
        RedirectTargetKinds.Item => state.AddressOf(target),
        RedirectTargetKinds.Path => target.Path,
        _ => target.Url,
    };

    public static RedirectTargetResponse TargetOf(RedirectTarget target) =>
        new(target.Kind, target.PageId, target.CollectionId, target.ItemId, target.Path, target.Url);

    public static RedirectTargetInput InputOf(RedirectTargetRequest? target) =>
        target is null ? new RedirectTargetInput(string.Empty) : new RedirectTargetInput(target.Kind ?? string.Empty, target.PageId, target.CollectionId, target.ItemId, target.Path, target.Url);
}
