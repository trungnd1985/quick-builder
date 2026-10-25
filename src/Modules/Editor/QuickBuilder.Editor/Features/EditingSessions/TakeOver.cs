using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using QuickBuilder.Editor.Domain;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Editor.Features.EditingSessions;

internal sealed record TakeOverCommand(Guid SiteId, Guid SessionId, Guid HolderSessionId) : ICommand<Result<PresenceResponse>>;

/// <summary>
/// Takes over another member's stale lock (023 FR-017, FR-018, research CE-05). Only when the
/// holder the member saw is still the holder and has done nothing for the stale period; the
/// transfer and the takeover record commit together, and the former holder learns who took it
/// on their next heartbeat.
/// </summary>
internal sealed partial class TakeOverHandler(
    EditingSessionStore sessions,
    EditingAccess editing,
    EditorDbContext database,
    TimeProvider clock,
    ILogger<TakeOverHandler> logger)
    : ICommandHandler<TakeOverCommand, Result<PresenceResponse>>
{
    public async Task<Result<PresenceResponse>> HandleAsync(TakeOverCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.GetUtcNow();
        var session = await sessions.OwnAsync(command.SiteId, command.SessionId, editing.Actor, now, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return Result.Failure<PresenceResponse>(EditorErrors.SessionGone);
        }

        if (!await editing.MayEditAsync(session.SiteId, session.DocumentKind, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<PresenceResponse>(EditorErrors.EditingForbidden);
        }

        var holder = await sessions.HolderAsync(session.SiteId, session.DocumentKind, session.DocumentId, now, cancellationToken).ConfigureAwait(false);
        if (holder is null)
        {
            // Nobody holds it any more: there is nothing to take over, so it is simply claimed.
            await sessions.ClaimAsync(session, now, cancellationToken).ConfigureAwait(false);
            return await AnswerAsync(command, now, cancellationToken).ConfigureAwait(false);
        }

        if (!EditingRules.CanTakeOver(holder.AsHolder(), command.HolderSessionId, session.UserId, now) || holder.LastActivityAt is not { } lastActivity)
        {
            return Result.Failure<PresenceResponse>(await NotStaleAsync(holder, now, cancellationToken).ConfigureAwait(false));
        }

        // The transfer and the record are one transaction: a takeover is never left unrecorded.
        var moved = false;
        await using (var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await sessions.TransferAsync(session, holder.Id, EditingSession.LostReasons.Takeover, now - EditingRules.StalePeriod, now, cancellationToken).ConfigureAwait(false))
            {
                database.EditingTakeovers.Add(EditingTakeover.Record(holder, session.UserId, lastActivity, now));
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                moved = true;
            }
        }

        if (!moved)
        {
            // The holder came back, or someone else took it first.
            var current = await sessions.HolderAsync(session.SiteId, session.DocumentKind, session.DocumentId, now, cancellationToken).ConfigureAwait(false);
            return current is null
                ? await AnswerAsync(command, now, cancellationToken).ConfigureAwait(false)
                : Result.Failure<PresenceResponse>(await NotStaleAsync(current, now, cancellationToken).ConfigureAwait(false));
        }

        LogTakenOver(logger, session.SiteId, session.DocumentKind, session.DocumentId, holder.UserId, session.UserId, (int)(now - lastActivity).TotalSeconds);
        return await AnswerAsync(command, now, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<PresenceResponse>> AnswerAsync(TakeOverCommand command, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var mine = await sessions.OwnAsync(command.SiteId, command.SessionId, editing.Actor, now, cancellationToken).ConfigureAwait(false);
        return mine is null
            ? Result.Failure<PresenceResponse>(EditorErrors.SessionGone)
            : Result.Success(await sessions.PresenceAsync(mine, mayEdit: true, now, cancellationToken).ConfigureAwait(false));
    }

    private async Task<Error> NotStaleAsync(EditingSession holder, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (id, name, since, _) = await sessions.DescribeAsync(holder, now, cancellationToken).ConfigureAwait(false);
        var inactive = holder.LastActivityAt is { } activity ? (int)Math.Max(0, (now - activity).TotalSeconds) : 0;
        return EditorErrors.NotStale(id, name, since, inactive);
    }

    // Ids, the kind and a duration only (FR-024).
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Editing lock taken over: site {SiteId}, {Kind} {DocumentId}, from {FromUserId} to {ToUserId}, inactive for {InactiveSeconds} s")]
    private static partial void LogTakenOver(ILogger logger, Guid siteId, string kind, Guid documentId, Guid fromUserId, Guid toUserId, int inactiveSeconds);
}

/// <summary>Administrative surface: take over a stale editing lock.</summary>
[ApiController]
[Route("api/v1/editor/sites/{siteId:guid}/editing-sessions/{sessionId:guid}/takeover")]
public sealed class TakeOverController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PageContentCapabilities.View)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TakeOverAsync(Guid siteId, Guid sessionId, HolderSessionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await dispatcher.SendAsync(new TakeOverCommand(siteId, sessionId, request.HolderSessionId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? EditorErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }
}
