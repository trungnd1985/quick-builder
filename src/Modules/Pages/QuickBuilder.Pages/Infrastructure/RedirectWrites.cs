using Microsoft.EntityFrameworkCore;
using QuickBuilder.Cms.Contracts;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Domain.Redirects;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Pages.Infrastructure;

/// <summary>
/// Creating, changing and deleting a site's redirects with every check of 017 FR-017 to
/// FR-019 and FR-033 (research SE-09, SE-13a). The slices and the redirect offers go through
/// here, so no path can skip a check or the audit record.
/// </summary>
internal sealed class RedirectWrites(
    PagesDbContext database,
    ICollectionSchemaReader items,
    ISiteDirectory sites,
    TimeProvider clock)
{
    /// <summary>The most redirects one site may hold (FR-020).</summary>
    public const int MaxPerSite = 10_000;

    /// <summary>Loads what checking redirect changes on the site needs.</summary>
    public async Task<RedirectSiteState> LoadAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var site = await sites.FindByIdAsync(siteId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Redirects were loaded for a site that does not exist.");

        var pages = await (
                from page in database.ForSite<Page>(siteId)
                join publication in database.ForSite<Publication>(siteId).Where(p => p.SubjectKind == SubjectKinds.Page)
                    on page.Id equals publication.SubjectId into publications
                from publication in publications.DefaultIfEmpty()
                select new { page.Id, page.Path, page.TemplateCollectionId, IsLive = publication != null && publication.LiveVersionId != null })
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var redirects = await database.ForSite<Redirect>(siteId)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var state = new RedirectSiteState(
            siteId,
            site.BaseUrl,
            site.HostNames,
            pages.ToDictionary(p => p.Id, p => new PageAddress(p.Path, p.IsLive, p.TemplateCollectionId is not null)),
            pages.Where(p => p.IsLive && p.TemplateCollectionId is not null).ToDictionary(p => p.TemplateCollectionId!.Value, p => p.Path),
            redirects.Select(Entry));

        await LoadItemsAsync(state, redirects.Where(r => r.TargetItemId is not null).Select(r => r.TargetItemId!.Value), cancellationToken).ConfigureAwait(false);
        return state;
    }

    /// <summary>Makes sure the published slugs of <paramref name="itemIds"/> are known to <paramref name="state"/>.</summary>
    public async Task LoadItemsAsync(RedirectSiteState state, IEnumerable<Guid> itemIds, CancellationToken cancellationToken)
    {
        var missing = itemIds.Where(id => !state.Items.ContainsKey(id)).Distinct().ToList();
        if (missing.Count == 0)
        {
            return;
        }

        foreach (var (id, parts) in await items.GetItemAddressPartsAsync(state.SiteId, missing, cancellationToken).ConfigureAwait(false))
        {
            state.Items[id] = parts;
        }
    }

    /// <summary>
    /// The target an input names, after the conversions of SE-09: a path that is a page's
    /// address becomes that page, and an absolute address on the site's primary host name a
    /// path, so both follow later changes.
    /// </summary>
    public async Task<Result<RedirectTarget>> ResolveTargetAsync(RedirectSiteState state, RedirectTargetInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);

        switch (input.Kind)
        {
            case RedirectTargetKinds.Page:
                return input.PageId is { } pageId && state.Pages.TryGetValue(pageId, out var page) && !page.IsTemplate
                    ? Result.Success(RedirectTarget.ToPage(pageId))
                    : Result.Failure<RedirectTarget>(PagesErrors.RedirectTargetInvalid("that page is not one of this site's"));

            case RedirectTargetKinds.Item:
                if (input.ItemId is not { } itemId || input.CollectionId is not { } collectionId)
                {
                    return Result.Failure<RedirectTarget>(PagesErrors.RedirectTargetInvalid("name the item and its collection"));
                }

                await LoadItemsAsync(state, [itemId], cancellationToken).ConfigureAwait(false);
                return state.Items.TryGetValue(itemId, out var parts) && parts.CollectionId == collectionId
                    ? Result.Success(RedirectTarget.ToItem(collectionId, itemId))
                    : Result.Failure<RedirectTarget>(PagesErrors.RedirectTargetInvalid("that item is not one of this site's"));

            case RedirectTargetKinds.Path:
                var path = RedirectTargets.SitePath(input.Path);
                return path.IsFailure ? path : Result.Success(AsPage(state, path.Value));

            case RedirectTargetKinds.Url:
                var url = RedirectTargets.Url(input.Url);
                if (url.IsFailure)
                {
                    return url;
                }

                return Result.Success(OnOwnSite(state, url.Value.Url!) is { } own ? AsPage(state, own) : url.Value);

            default:
                return Result.Failure<RedirectTarget>(PagesErrors.RedirectTargetInvalid("choose a page, an item, a path or an address"));
        }
    }

    /// <summary>
    /// Every check of a redirect against the site, before it is stored; on success, the source
    /// and the target to store (the end of an exact chain, FR-017).
    /// </summary>
    public static Result<CheckedRedirect> Check(RedirectSiteState state, Guid? self, string matchKind, string source, RedirectTarget target, int statusCode, string? note)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (matchKind is not (RedirectMatchKinds.Exact or RedirectMatchKinds.Prefix))
        {
            return Result.Failure<CheckedRedirect>(PagesErrors.RedirectSourceInvalid("choose exact or prefix"));
        }

        if (statusCode is not (301 or 302))
        {
            return Result.Failure<CheckedRedirect>(PagesErrors.RedirectTargetInvalid("choose permanent (301) or temporary (302)"));
        }

        if (note is { Length: > Redirect.MaxNoteLength })
        {
            return Result.Failure<CheckedRedirect>(PagesErrors.RedirectTargetInvalid($"a note may be at most {Redirect.MaxNoteLength} characters"));
        }

        var parsed = RedirectSource.Parse(source, matchKind);
        if (parsed.IsFailure)
        {
            return Result.Failure<CheckedRedirect>(parsed.Error);
        }

        var (path, query) = parsed.Value;
        var forMatch = RedirectTargets.CheckForMatch(target, matchKind);
        if (forMatch.IsFailure)
        {
            return Result.Failure<CheckedRedirect>(forMatch.Error!);
        }

        if (state.SourceTakenBy(matchKind, path, query) is { } taken && taken != self)
        {
            return Result.Failure<CheckedRedirect>(PagesErrors.RedirectDuplicate(taken));
        }

        var chain = RedirectRules.CheckChain(self, matchKind, path, query, target, state);
        if (chain.IsLoop)
        {
            return Result.Failure<CheckedRedirect>(PagesErrors.RedirectLoop(chain.LoopWith));
        }

        // A shortened chain may end on a target a prefix cannot take.
        var final = chain.FinalTarget!;
        if (RedirectTargets.CheckForMatch(final, matchKind).IsFailure)
        {
            final = target;
        }

        return Result.Success(new CheckedRedirect(matchKind, path, query, final, chain.Shortened && final != target));
    }

    /// <summary>Adds a checked redirect to the context and its audit row; the caller saves.</summary>
    public Redirect Add(RedirectSiteState state, CheckedRedirect redirect, int statusCode, bool enabled, string? note, string origin, Guid? actor)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(redirect);

        var now = clock.GetUtcNow();
        var created = Redirect.Create(state.SiteId, redirect.Match, redirect.Path, redirect.Query, redirect.Target, statusCode, enabled, note, origin, actor, now);
        database.Redirects.Add(created);
        Record(created, origin switch
        {
            RedirectOrigins.Import => RedirectChangeActions.Imported,
            RedirectOrigins.Offer => RedirectChangeActions.Offered,
            _ => RedirectChangeActions.Created,
        }, actor, now);
        state.Add(Entry(created));
        return created;
    }

    /// <summary>Records an action on a redirect (017 FR-021, research SE-13a): ids, codes and times only.</summary>
    public void Record(Redirect redirect, string action, Guid? actor, DateTimeOffset? at = null)
    {
        ArgumentNullException.ThrowIfNull(redirect);
        database.RedirectChanges.Add(new RedirectChange(redirect.SiteId, redirect.Id, action, redirect.Origin, actor, at ?? clock.GetUtcNow()));
    }

    /// <summary>Whether the site can take <paramref name="more"/> redirects (FR-020).</summary>
    public static bool HasRoomFor(RedirectSiteState state, int more) => state.Count + more <= MaxPerSite;

    /// <summary>
    /// Saves, turning a unique-index race into the duplicate it is: two members adding the same
    /// source at once get one redirect and one refusal.
    /// </summary>
    public async Task<Result> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(PagesErrors.OutOfDate);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            return Result.Failure(PagesErrors.RedirectDuplicate(Guid.Empty));
        }
    }

    public static RedirectEntry Entry(Redirect redirect)
    {
        ArgumentNullException.ThrowIfNull(redirect);
        return new RedirectEntry(redirect.Id, redirect.MatchKind, redirect.SourcePath, redirect.SourceQuery, redirect.Target, redirect.Enabled);
    }

    /// <summary>A path that is a page's current address, as that page; any other path as it is.</summary>
    private static RedirectTarget AsPage(RedirectSiteState state, RedirectTarget path)
    {
        var parts = path.Path!.Split('?', 2);
        return parts.Length == 1 && RedirectSource.NormalisePath(Uri.UnescapeDataString(parts[0])) is { } normalised
            && state.PageByPath.TryGetValue(normalised, out var pageId)
            ? RedirectTarget.ToPage(pageId)
            : path;
    }

    /// <summary>
    /// An absolute address on the site's primary host name and base path, as a site path
    /// (SE-09); null for any other address, including another host name of the site.
    /// </summary>
    private static RedirectTarget? OnOwnSite(RedirectSiteState state, string url)
    {
        var baseUrl = state.BaseUrl.TrimEnd('/');
        if (!url.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = url[baseUrl.Length..];
        if (rest.Length == 0)
        {
            return RedirectTarget.ToPath("/");
        }

        return rest[0] is '/' or '?' ? RedirectTarget.ToPath(rest[0] == '?' ? "/" + rest : rest) : null;
    }
}

/// <summary>What a member asked a redirect to send visitors to.</summary>
internal sealed record RedirectTargetInput(string Kind, Guid? PageId = null, Guid? CollectionId = null, Guid? ItemId = null, string? Path = null, string? Url = null);

/// <summary>A redirect that passed every check, ready to store.</summary>
/// <param name="Shortened">Its target was replaced by the end of a chain.</param>
internal sealed record CheckedRedirect(string Match, string Path, string Query, RedirectTarget Target, bool Shortened);
