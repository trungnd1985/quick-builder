using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Pages.Infrastructure.Schema;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.PublishingHistory;

// 012 FR-013 to FR-016, research PR-15: a page's or component's history, its retained
// versions, and copying one back into the draft. contracts/publishing-api.yaml.

/// <summary>The draft version the caller saw.</summary>
public sealed record CopyToDraftRequest(uint DraftVersion);

internal sealed record GetHistoryQuery(Guid SiteId, string SubjectKind, Guid SubjectId) : IQuery<Result<SubjectHistoryResponse>>;

internal sealed record GetVersionQuery(Guid SiteId, string SubjectKind, Guid SubjectId, int Number) : IQuery<Result<VersionContentResponse>>;

internal sealed record CopyVersionToDraftCommand(Guid SiteId, string SubjectKind, Guid SubjectId, int Number, uint DraftVersion)
    : ICommand<Result<DraftResponse>>;

/// <summary>Log entries as the API shows them, with the people named.</summary>
internal sealed class LogEntryReads(PagesAccess access)
{
    public async Task<IReadOnlyList<LogEntryResponse>> MapAsync(
        IReadOnlyList<PublishLogEntry> entries, Func<PublishLogEntry, bool> exists, CancellationToken cancellationToken)
    {
        var names = await access.NamesAsync(
                entries.SelectMany(e => new[] { e.ActorUserId, e.ScheduleOwnerId }).OfType<Guid>(),
                cancellationToken)
            .ConfigureAwait(false);

        return [.. entries.Select(e => new LogEntryResponse(
            e.Id,
            e.SubjectKind,
            e.SubjectId,
            e.SubjectLabel,
            e.SubjectPath,
            exists(e),
            e.Action,
            e.VersionNumber,
            e.RestoredFromNumber,
            e.ActorUserId is { } actor ? PagesAccess.Reference(actor, names) : null,
            e.ScheduleOwnerId is { } owner ? PagesAccess.Reference(owner, names) : null,
            e.ReasonCode,
            e.OccurredAtUtc))];
    }
}

internal sealed class GetHistoryHandler(
    PagesDbContext database,
    PagesAccess access,
    PublishingWrites publishing,
    PublicationReads publications,
    LogEntryReads entries)
    : IQueryHandler<GetHistoryQuery, Result<SubjectHistoryResponse>>
{
    // A subject's own history is short next to the site log; this bounds a pathological one.
    private const int MaxEntries = 500;

    public async Task<Result<SubjectHistoryResponse>> HandleAsync(GetHistoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var missing = await Subjects.CheckAsync(access, publishing, query.SiteId, query.SubjectKind, query.SubjectId, cancellationToken)
            .ConfigureAwait(false);
        if (missing is not null)
        {
            return Result.Failure<SubjectHistoryResponse>(missing);
        }

        var retained = await database.ForSite<PublishedVersion>(query.SiteId)
            .Where(v => v.SubjectKind == query.SubjectKind && v.SubjectId == query.SubjectId)
            .OrderByDescending(v => v.Number)
            .Select(v => v.Number)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var log = await database.ForSite<PublishLogEntry>(query.SiteId)
            .AsNoTracking()
            .Where(e => e.SubjectKind == query.SubjectKind && e.SubjectId == query.SubjectId)
            .OrderByDescending(e => e.OccurredAtUtc)
            .ThenByDescending(e => e.Id)
            .Take(MaxEntries)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new SubjectHistoryResponse(
            await publications.SummaryAsync(query.SiteId, query.SubjectKind, query.SubjectId, cancellationToken).ConfigureAwait(false),
            retained,
            await entries.MapAsync(log, _ => true, cancellationToken).ConfigureAwait(false)));
    }
}

internal sealed class GetVersionHandler(PagesDbContext database, PagesAccess access, PublishingWrites publishing, StoredDocuments stored)
    : IQueryHandler<GetVersionQuery, Result<VersionContentResponse>>
{
    public async Task<Result<VersionContentResponse>> HandleAsync(GetVersionQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var missing = await Subjects.CheckAsync(access, publishing, query.SiteId, query.SubjectKind, query.SubjectId, cancellationToken)
            .ConfigureAwait(false);
        if (missing is not null)
        {
            return Result.Failure<VersionContentResponse>(missing);
        }

        var version = await Subjects.VersionAsync(database, query.SiteId, query.SubjectKind, query.SubjectId, query.Number, cancellationToken)
            .ConfigureAwait(false);
        if (version is null)
        {
            return Result.Failure<VersionContentResponse>(PagesErrors.VersionNotFound);
        }

        // 019 FR-021: a retained version is shown at the current schema version; the row keeps
        // the version it was published with (FR-023a).
        var document = stored.ForEditing(query.SubjectKind, query.SubjectId, version.Document, version.SchemaVersion);
        if (document.IsFailure)
        {
            return Result.Failure<VersionContentResponse>(document.Error);
        }

        var names = await access.NamesAsync([version.PublishedBy], cancellationToken).ConfigureAwait(false);

        return Result.Success(new VersionContentResponse(
            version.Number,
            StoredDocuments.CurrentVersion(query.SubjectKind),
            document.Value,
            version.Title,
            version.Kind,
            version.RestoredFromNumber,
            version.PublishedAtUtc,
            PagesAccess.Reference(version.PublishedBy, names)));
    }
}

/// <summary>
/// Replaces the draft with a retained version's content through 010's draft rules, so
/// validation, component usages and the out-of-date check are the same as a save (FR-015).
/// Nothing public changes.
/// </summary>
internal sealed class CopyVersionToDraftHandler(
    PagesDbContext database,
    PagesAccess access,
    PublishingWrites publishing,
    DraftWrites drafts,
    MenuWrites menus,
    StoredDocuments stored,
    TimeProvider clock)
    : ICommandHandler<CopyVersionToDraftCommand, Result<DraftResponse>>
{
    public async Task<Result<DraftResponse>> HandleAsync(CopyVersionToDraftCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<DraftResponse>(PagesErrors.SiteNotFound);
        }

        var subject = await publishing.SubjectAsync(command.SiteId, command.SubjectKind, command.SubjectId, cancellationToken)
            .ConfigureAwait(false);
        if (subject is null)
        {
            return Result.Failure<DraftResponse>(Subjects.NotFound(command.SubjectKind));
        }

        var version = await Subjects.VersionAsync(database, command.SiteId, command.SubjectKind, command.SubjectId, command.Number, cancellationToken)
            .ConfigureAwait(false);
        if (version is null)
        {
            return Result.Failure<DraftResponse>(PagesErrors.VersionNotFound);
        }

        // 019 FR-022: an older version becomes a current-version draft; one that cannot be used
        // is refused before anything changes.
        var content = stored.ForEditing(subject.Kind, subject.Id, version.Document, version.SchemaVersion);
        if (content.IsFailure)
        {
            return Result.Failure<DraftResponse>(content.Error);
        }

        // Saved with the draft, so the entry exists exactly when the copy does.
        publishing.Log(command.SiteId, subject, LogActions.CopiedToDraft, clock.GetUtcNow(), access.Actor, versionNumber: version.Number);

        if (subject.IsMenu)
        {
            // 024 NV-04: through the menu's own save path, so rule 8 and the target index hold.
            var replaced = await menus.ReplaceDraftAsync(command.SiteId, subject.Id, content.Value.GetRawText(), command.DraftVersion, cancellationToken)
                .ConfigureAwait(false);
            if (replaced.IsFailure)
            {
                database.ChangeTracker.Clear();
                return Result.Failure<DraftResponse>(replaced.Error);
            }

            return Result.Success(await menus.DraftResponseAsync(command.SiteId, subject.Id, cancellationToken).ConfigureAwait(false));
        }

        var saved = subject.IsPage
            ? await drafts.SavePageDraftAsync(command.SiteId, subject.Id, content.Value, command.DraftVersion, clientKeepsEveryFormat: true, cancellationToken).ConfigureAwait(false)
            : await drafts.SaveComponentDraftAsync(command.SiteId, subject.Id, content.Value, command.DraftVersion, clientKeepsEveryFormat: true, cancellationToken).ConfigureAwait(false);

        if (saved.IsFailure)
        {
            database.ChangeTracker.Clear();
        }

        return saved;
    }
}

/// <summary>What the history slices share.</summary>
internal static class Subjects
{
    public static Error NotFound(string kind) => PublishingWrites.NotFoundFor(kind);

    /// <summary>The refusal when the site or subject does not exist; null when both do.</summary>
    public static async Task<Error?> CheckAsync(
        PagesAccess access, PublishingWrites publishing, Guid siteId, string kind, Guid subjectId, CancellationToken cancellationToken)
    {
        if (!await access.SiteExistsAsync(siteId, cancellationToken).ConfigureAwait(false))
        {
            return PagesErrors.SiteNotFound;
        }

        return await publishing.SubjectAsync(siteId, kind, subjectId, cancellationToken).ConfigureAwait(false) is null
            ? NotFound(kind)
            : null;
    }

    public static Task<PublishedVersion?> VersionAsync(
        PagesDbContext database, Guid siteId, string kind, Guid subjectId, int number, CancellationToken cancellationToken) =>
        database.ForSite<PublishedVersion>(siteId)
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.SubjectKind == kind && v.SubjectId == subjectId && v.Number == number, cancellationToken);
}

/// <summary>Administrative surface: a page's publishing history and retained versions.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}")]
public sealed class PageHistoryController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("history")]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> HistoryAsync(Guid siteId, Guid pageId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetHistoryQuery(siteId, SubjectKinds.Page, pageId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    [HttpGet("versions/{number:int}")]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VersionAsync(Guid siteId, Guid pageId, int number, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetVersionQuery(siteId, SubjectKinds.Page, pageId, number), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}

/// <summary>Administrative surface: copy a retained page version into the page's draft.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/pages/{pageId:guid}/versions/{number:int}/copy-to-draft")]
public sealed class CopyPageVersionToDraftController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PagesCapabilities.PageDraftsEdit)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CopyAsync(Guid siteId, Guid pageId, int number, CopyToDraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher
            .SendAsync(new CopyVersionToDraftCommand(siteId, SubjectKinds.Page, pageId, number, request.DraftVersion), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}

/// <summary>Administrative surface: a shared component's publishing history and retained versions.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}")]
public sealed class ComponentHistoryController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("history")]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> HistoryAsync(Guid siteId, Guid componentId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetHistoryQuery(siteId, SubjectKinds.Component, componentId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    [HttpGet("versions/{number:int}")]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VersionAsync(Guid siteId, Guid componentId, int number, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetVersionQuery(siteId, SubjectKinds.Component, componentId, number), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}

/// <summary>Administrative surface: copy a retained component version into the component's draft.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/components/{componentId:guid}/versions/{number:int}/copy-to-draft")]
public sealed class CopyComponentVersionToDraftController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PagesCapabilities.ComponentsManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CopyAsync(Guid siteId, Guid componentId, int number, CopyToDraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher
            .SendAsync(new CopyVersionToDraftCommand(siteId, SubjectKinds.Component, componentId, number, request.DraftVersion), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}

/// <summary>Administrative surface: a menu's publishing history and retained versions (024 FR-004a).</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}")]
public sealed class MenuHistoryController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("history")]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> HistoryAsync(Guid siteId, Guid menuId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetHistoryQuery(siteId, SubjectKinds.Menu, menuId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    [HttpGet("versions/{number:int}")]
    [RequireCapability(PagesCapabilities.ContentView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VersionAsync(Guid siteId, Guid menuId, int number, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetVersionQuery(siteId, SubjectKinds.Menu, menuId, number), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}

/// <summary>Administrative surface: copy a retained menu version into the menu's draft.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/menus/{menuId:guid}/versions/{number:int}/copy-to-draft")]
public sealed class CopyMenuVersionToDraftController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PagesCapabilities.MenusManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CopyAsync(Guid siteId, Guid menuId, int number, CopyToDraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher
            .SendAsync(new CopyVersionToDraftCommand(siteId, SubjectKinds.Menu, menuId, number, request.DraftVersion), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
