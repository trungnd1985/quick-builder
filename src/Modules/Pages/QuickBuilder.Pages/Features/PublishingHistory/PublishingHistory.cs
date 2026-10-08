using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain;
using QuickBuilder.Pages.Infrastructure;
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

internal sealed class GetVersionHandler(PagesDbContext database, PagesAccess access, PublishingWrites publishing)
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

        var names = await access.NamesAsync([version.PublishedBy], cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(version.Document);

        return Result.Success(new VersionContentResponse(
            version.Number,
            version.SchemaVersion,
            document.RootElement.Clone(),
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

        // Saved with the draft, so the entry exists exactly when the copy does.
        publishing.Log(command.SiteId, subject, LogActions.CopiedToDraft, clock.GetUtcNow(), access.Actor, versionNumber: version.Number);

        using var document = JsonDocument.Parse(version.Document);
        var saved = subject.IsPage
            ? await drafts.SavePageDraftAsync(command.SiteId, subject.Id, document.RootElement, command.DraftVersion, clientKeepsEveryFormat: true, cancellationToken).ConfigureAwait(false)
            : await drafts.SaveComponentDraftAsync(command.SiteId, subject.Id, document.RootElement, command.DraftVersion, clientKeepsEveryFormat: true, cancellationToken).ConfigureAwait(false);

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
    public static Error NotFound(string kind) => kind == SubjectKinds.Page ? PagesErrors.NotFound : PagesErrors.ComponentNotFound;

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
