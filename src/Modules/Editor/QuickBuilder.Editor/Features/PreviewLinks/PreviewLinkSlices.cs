using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuickBuilder.Cms.Contracts.Publishing;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Editor.Contracts.Events;
using QuickBuilder.Editor.Domain;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.Identity.Contracts;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Editor.Features.PreviewLinks;

/// <summary>What the preview link slices share: who may act, what exists, how links are shown (022 PV-09).</summary>
internal sealed class PreviewLinkAccess(
    EditorDbContext database,
    EditingAccess editing,
    IAccessEvaluator access,
    ISiteContextSetter siteContext,
    IPreviewContentReader pages,
    IPreviewItemReader items,
    ITemplateReader templates,
    IUserDirectory users,
    IOptionsMonitor<PreviewOptions> options,
    TimeProvider clock)
{
    public Guid Actor => editing.Actor;

    public DateTimeOffset Now => clock.GetUtcNow();

    /// <summary>Whether the caller may edit documents of <paramref name="kind"/> on the site (clarification 1).</summary>
    public Task<bool> MayEditAsync(Guid siteId, string kind, CancellationToken cancellationToken) =>
        editing.MayEditAsync(siteId, kind, cancellationToken);

    /// <summary>Whether the caller may list and revoke every link of the site (FR-015).</summary>
    public Task<bool> MayManageAsync(Guid siteId, CancellationToken cancellationToken) =>
        access.HasCapabilityAsync(PreviewCapabilities.Manage, siteId, cancellationToken);

    /// <summary>
    /// Whether the page or item exists on the site, readable as a preview would read it: not in
    /// the trash, not another site's. Refusals use the preview codes of data-model §5.
    /// </summary>
    public async Task<Result> CheckTargetAsync(
        Guid siteId, string kind, Guid documentId, int? versionNumber, CancellationToken cancellationToken)
    {
        // The preview readers are filtered by the site context; the admin surface sets none.
        siteContext.SetSite(siteId);

        if (kind == EditingDocumentKinds.Page)
        {
            if (await pages.GetPageDraftAsync(siteId, documentId, cancellationToken).ConfigureAwait(false) is null)
            {
                return Result.Failure(PreviewLinkErrors.NotFound);
            }

            return versionNumber is { } number
                && await pages.GetPageVersionAsync(siteId, documentId, number, cancellationToken).ConfigureAwait(false) is null
                    ? Result.Failure(PreviewLinkErrors.VersionNotRetained)
                    : Result.Success();
        }

        var draft = await items.GetDraftAsync(siteId, documentId, cancellationToken).ConfigureAwait(false);
        if (draft is null)
        {
            return Result.Failure(PreviewLinkErrors.NotFound);
        }

        if (await templates.GetTemplateAsync(siteId, draft.Item.CollectionId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure(PreviewLinkErrors.NoTemplate);
        }

        return versionNumber is { } version
            && await items.GetVersionAsync(siteId, documentId, version, cancellationToken).ConfigureAwait(false) is null
                ? Result.Failure(PreviewLinkErrors.VersionNotRetained)
                : Result.Success();
    }

    /// <summary>The site's links that work now: not revoked, not expired, not invalidated (PV-07).</summary>
    public IQueryable<PreviewLink> Active(Guid siteId)
    {
        var now = Now;
        var from = options.CurrentValue.LinksValidFrom ?? DateTimeOffset.MinValue;
        return database.ForSite<PreviewLink>(siteId)
            .Where(l => l.RevokedAtUtc == null && l.ExpiresAtUtc > now && l.CreatedAtUtc >= from);
    }

    /// <summary>The links as responses, their creators named.</summary>
    public async Task<List<PreviewLinkResponse>> ToResponsesAsync(IReadOnlyList<PreviewLink> links, CancellationToken cancellationToken)
    {
        var names = (await users.FindManyAsync([.. links.Select(l => l.CreatedBy).Distinct()], cancellationToken).ConfigureAwait(false))
            .ToDictionary(u => u.Id, u => u.DisplayName);
        return [.. links.Select(l => ToResponse(l, names.GetValueOrDefault(l.CreatedBy) ?? string.Empty))];
    }

    public static PreviewLinkResponse ToResponse(PreviewLink link, string creatorName) => new(
        link.Id,
        link.DocumentKind,
        link.DocumentId,
        link.VersionNumber,
        link.SampleItemId,
        link.Label,
        new PreviewLinkUserResponse(link.CreatedBy, creatorName),
        link.CreatedAtUtc,
        link.ExpiresAtUtc,
        link.LastOpenedAtUtc);

    /// <summary>Writes the links' raised events to the outbox, in the caller's <c>SaveChanges</c> (PV-16).</summary>
    public void Publish(IEnumerable<PreviewLink> links)
    {
        foreach (var raised in links.SelectMany(l => l.Events))
        {
            database.OutboxMessages.Add(raised switch
            {
                PreviewLinkCreated created => OutboxMessage.From(created),
                PreviewLinkRevoked revoked => OutboxMessage.From(revoked),
                _ => throw new InvalidOperationException($"Unexpected preview link event {raised.GetType().Name}."),
            });
        }
    }
}

// ---------------------------------------------------------------- create (FR-005, FR-018)

internal sealed record CreatePreviewLinkCommand(Guid SiteId, CreatePreviewLinkRequest Request, string RequestOrigin)
    : ICommand<Result<CreatedPreviewLinkResponse>>;

/// <summary>
/// Creates a link for a page or item, draft or retained version (022 FR-005, research PV-17):
/// at most 20 active links per target, counted under a per-target advisory lock so two
/// concurrent creates cannot both see 19.
/// </summary>
internal sealed class CreatePreviewLinkHandler(EditorDbContext database, PreviewLinkAccess links, ISiteDirectory sites)
    : ICommandHandler<CreatePreviewLinkCommand, Result<CreatedPreviewLinkResponse>>
{
    public async Task<Result<CreatedPreviewLinkResponse>> HandleAsync(CreatePreviewLinkCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;
        var kind = request.DocumentKind;
        if (kind is not (EditingDocumentKinds.Page or EditingDocumentKinds.Item))
        {
            return Result.Failure<CreatedPreviewLinkResponse>(PreviewLinkErrors.NotFound);
        }

        if (!await links.MayEditAsync(command.SiteId, kind, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<CreatedPreviewLinkResponse>(PreviewLinkErrors.NotFound);
        }

        if (await sites.FindByIdAsync(command.SiteId, cancellationToken).ConfigureAwait(false) is not { } site)
        {
            return Result.Failure<CreatedPreviewLinkResponse>(EditorErrors.SiteNotFound);
        }

        var target = await links.CheckTargetAsync(command.SiteId, kind, request.DocumentId, request.VersionNumber, cancellationToken).ConfigureAwait(false);
        if (target.IsFailure)
        {
            return Result.Failure<CreatedPreviewLinkResponse>(target.Error);
        }

        var created = PreviewLink.Create(
            command.SiteId,
            kind,
            request.DocumentId,
            request.VersionNumber,
            kind == EditingDocumentKinds.Page ? request.SampleItemId : null,
            request.Label,
            request.ExpiresAtUtc,
            links.Actor,
            links.Now);
        if (created.IsFailure)
        {
            return Result.Failure<CreatedPreviewLinkResponse>(created.Error);
        }

        var (link, secret) = created.Value;
        await using (var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
        {
            // PV-17: one target's creates take turns; the lock ends with the transaction.
            var lockKey = $"preview:{command.SiteId}:{kind}:{request.DocumentId}";
            await database.Database
                .ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({lockKey}))", cancellationToken)
                .ConfigureAwait(false);

            var active = await links.Active(command.SiteId)
                .CountAsync(l => l.DocumentKind == kind && l.DocumentId == request.DocumentId, cancellationToken)
                .ConfigureAwait(false);
            if (active >= PreviewLink.MaxActivePerDocument)
            {
                return Result.Failure<CreatedPreviewLinkResponse>(PreviewLinkErrors.LinkLimit);
            }

            database.PreviewLinks.Add(link);
            links.Publish([link]);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        // PV-05: on the site's own host name when it has one, else where the member is now.
        var origin = site.HostNames.Count > 0 ? site.BaseUrl : command.RequestOrigin;
        var url = $"{origin.TrimEnd('/')}/_preview/l/{secret}";
        var responses = await links.ToResponsesAsync([link], cancellationToken).ConfigureAwait(false);
        return Result.Success(new CreatedPreviewLinkResponse(responses[0], url));
    }
}

// ---------------------------------------------------------------- list one target's links (FR-014)

internal sealed record ListPreviewLinksQuery(Guid SiteId, string? DocumentKind, Guid DocumentId) : IQuery<Result<IReadOnlyList<PreviewLinkResponse>>>;

internal sealed class ListPreviewLinksHandler(PreviewLinkAccess links)
    : IQueryHandler<ListPreviewLinksQuery, Result<IReadOnlyList<PreviewLinkResponse>>>
{
    public async Task<Result<IReadOnlyList<PreviewLinkResponse>>> HandleAsync(ListPreviewLinksQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.DocumentKind is not (EditingDocumentKinds.Page or EditingDocumentKinds.Item)
            || !await links.MayEditAsync(query.SiteId, query.DocumentKind, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<IReadOnlyList<PreviewLinkResponse>>(PreviewLinkErrors.NotFound);
        }

        var found = await links.Active(query.SiteId)
            .Where(l => l.DocumentKind == query.DocumentKind && l.DocumentId == query.DocumentId)
            .OrderByDescending(l => l.CreatedAtUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<PreviewLinkResponse>>(await links.ToResponsesAsync(found, cancellationToken).ConfigureAwait(false));
    }
}

// ---------------------------------------------------------------- the site's links (FR-015)

internal sealed record ListSitePreviewLinksQuery(Guid SiteId, string? Cursor, int Limit) : IQuery<Result<SitePreviewLinksResponse>>;

/// <summary>Every active link of the site, soonest to expire first, keyset-paged (022 FR-015).</summary>
internal sealed class ListSitePreviewLinksHandler(PreviewLinkAccess links, IPreviewContentReader pages, IPreviewItemReader items, ISiteContextSetter siteContext)
    : IQueryHandler<ListSitePreviewLinksQuery, Result<SitePreviewLinksResponse>>
{
    public async Task<Result<SitePreviewLinksResponse>> HandleAsync(ListSitePreviewLinksQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await links.MayManageAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<SitePreviewLinksResponse>(PreviewLinkErrors.NotFound);
        }

        var limit = Math.Clamp(query.Limit, 1, 100);
        var active = links.Active(query.SiteId).AsNoTracking();
        if (Cursor.TryRead(query.Cursor) is { } after)
        {
            active = active.Where(l => l.ExpiresAtUtc > after.ExpiresAtUtc || (l.ExpiresAtUtc == after.ExpiresAtUtc && l.Id.CompareTo(after.Id) > 0));
        }

        var page = await active.OrderBy(l => l.ExpiresAtUtc).ThenBy(l => l.Id).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        var shown = page.Take(limit).ToList();

        siteContext.SetSite(query.SiteId);
        var titles = await pages.GetTitlesAsync(
                query.SiteId, [.. shown.Where(l => l.DocumentKind == EditingDocumentKinds.Page).Select(l => l.DocumentId)], cancellationToken)
            .ConfigureAwait(false);
        var names = await items.GetNamesAsync(
                query.SiteId, [.. shown.Where(l => l.DocumentKind == EditingDocumentKinds.Item).Select(l => l.DocumentId)], cancellationToken)
            .ConfigureAwait(false);

        var responses = await links.ToResponsesAsync(shown, cancellationToken).ConfigureAwait(false);
        var result = responses.Select((response, i) =>
        {
            var link = shown[i];
            return link.DocumentKind == EditingDocumentKinds.Page
                ? new SitePreviewLinkResponse(response, titles.GetValueOrDefault(link.DocumentId), null)
                : new SitePreviewLinkResponse(response, names.GetValueOrDefault(link.DocumentId)?.Name, names.GetValueOrDefault(link.DocumentId)?.CollectionId);
        }).ToList();

        var next = page.Count > limit ? Cursor.Write(shown[^1]) : null;
        return Result.Success(new SitePreviewLinksResponse(result, next));
    }

    /// <summary>The keyset position after a link: its expiry and id, as text.</summary>
    private static class Cursor
    {
        public static string Write(PreviewLink link) =>
            $"{link.ExpiresAtUtc.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture)}_{link.Id:N}";

        public static (DateTimeOffset ExpiresAtUtc, Guid Id)? TryRead(string? cursor)
        {
            var parts = cursor?.Split('_');
            return parts is [var ticks, var id]
                && long.TryParse(ticks, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value)
                && value is >= 0 and <= 3155378975999999999
                && Guid.TryParseExact(id, "N", out var guid)
                    ? (new DateTimeOffset(value, TimeSpan.Zero), guid)
                    : null;
        }
    }
}

// ---------------------------------------------------------------- revoke (FR-014, FR-015)

internal sealed record RevokePreviewLinkCommand(Guid SiteId, Guid LinkId) : ICommand<Result>;

/// <summary>Ends one link (022 FR-014). Revoking it again is a success that records nothing (PV-16).</summary>
internal sealed class RevokePreviewLinkHandler(EditorDbContext database, PreviewLinkAccess links)
    : ICommandHandler<RevokePreviewLinkCommand, Result>
{
    public async Task<Result> HandleAsync(RevokePreviewLinkCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var link = await database.ForSite<PreviewLink>(command.SiteId)
            .FirstOrDefaultAsync(l => l.Id == command.LinkId, cancellationToken)
            .ConfigureAwait(false);
        if (link is null
            || !(await links.MayEditAsync(command.SiteId, link.DocumentKind, cancellationToken).ConfigureAwait(false)
                || await links.MayManageAsync(command.SiteId, cancellationToken).ConfigureAwait(false)))
        {
            return Result.Failure(PreviewLinkErrors.LinkNotFound);
        }

        link.Revoke(links.Actor, links.Now);
        links.Publish([link]);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

internal sealed record RevokeAllPreviewLinksCommand(Guid SiteId, string? DocumentKind, Guid? DocumentId) : ICommand<Result<RevokedPreviewLinksResponse>>;

/// <summary>
/// Ends every active link of one page or item (its editors), or of the whole site
/// (Administrators), with one history event per link (022 FR-014, FR-015, PV-16).
/// </summary>
internal sealed class RevokeAllPreviewLinksHandler(EditorDbContext database, PreviewLinkAccess links)
    : ICommandHandler<RevokeAllPreviewLinksCommand, Result<RevokedPreviewLinksResponse>>
{
    public async Task<Result<RevokedPreviewLinksResponse>> HandleAsync(RevokeAllPreviewLinksCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var active = links.Active(command.SiteId);
        if (command.DocumentKind is null && command.DocumentId is null)
        {
            if (!await links.MayManageAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
            {
                return Result.Failure<RevokedPreviewLinksResponse>(PreviewLinkErrors.NotFound);
            }
        }
        else if (command.DocumentKind is not (EditingDocumentKinds.Page or EditingDocumentKinds.Item)
                 || command.DocumentId is not { } documentId
                 || !await links.MayEditAsync(command.SiteId, command.DocumentKind, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<RevokedPreviewLinksResponse>(PreviewLinkErrors.NotFound);
        }
        else
        {
            active = active.Where(l => l.DocumentKind == command.DocumentKind && l.DocumentId == documentId);
        }

        var found = await active.ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var link in found)
        {
            link.Revoke(links.Actor, links.Now);
        }

        links.Publish(found);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(new RevokedPreviewLinksResponse(found.Count));
    }
}

// ---------------------------------------------------------------- HTTP (contracts/preview-api.yaml)

/// <summary>Administrative surface: preview links (022 contracts/preview-api.yaml).</summary>
[ApiController]
[Route("api/v1/editor/sites/{siteId:guid}/preview-links")]
public sealed class PreviewLinksController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PageContentCapabilities.View)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(Guid siteId, CreatePreviewLinkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var origin = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        var result = await dispatcher.SendAsync(new CreatePreviewLinkCommand(siteId, request, origin), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? EditorErrorResults.ToActionResult(result.Error) : StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpGet]
    [RequireCapability(PageContentCapabilities.View)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync(Guid siteId, [FromQuery] string? documentKind, [FromQuery] Guid documentId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new ListPreviewLinksQuery(siteId, documentKind, documentId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? EditorErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    [HttpGet("all")]
    [RequireCapability(PreviewCapabilities.Manage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSiteAsync(Guid siteId, [FromQuery] string? cursor, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new ListSitePreviewLinksQuery(siteId, cursor, limit ?? 50), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? EditorErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    [HttpPost("{linkId:guid}/revoke")]
    [RequireCapability(PageContentCapabilities.View)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeAsync(Guid siteId, Guid linkId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new RevokePreviewLinkCommand(siteId, linkId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? EditorErrorResults.ToActionResult(result.Error) : NoContent();
    }

    [HttpPost("revoke-all")]
    [RequireCapability(PageContentCapabilities.View)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeAllAsync(Guid siteId, RevokeAllPreviewLinksRequest? request, CancellationToken cancellationToken)
    {
        var result = await dispatcher
            .SendAsync(new RevokeAllPreviewLinksCommand(siteId, request?.DocumentKind, request?.DocumentId), cancellationToken)
            .ConfigureAwait(false);
        return result.IsFailure ? EditorErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
