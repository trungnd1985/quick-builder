using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Pages.Domain.Redirects;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Pages.Features.Redirects;

/// <summary>A redirect as a member writes it (seo-api.yaml, <c>RedirectInput</c>).</summary>
public sealed record RedirectRequest(string Source, string Match, RedirectTargetRequest Target, int StatusCode, bool Enabled, string? Note, uint? Version = null);

internal sealed record CreateRedirectCommand(Guid SiteId, RedirectRequest Request) : ICommand<Result<RedirectResponse>>;

internal sealed record UpdateRedirectCommand(Guid SiteId, Guid RedirectId, RedirectRequest Request) : ICommand<Result<RedirectResponse>>;

internal sealed record DeleteRedirectCommand(Guid SiteId, Guid RedirectId, uint Version) : ICommand<Result>;

internal sealed record GetRedirectQuery(Guid SiteId, Guid RedirectId) : IQuery<Result<RedirectResponse>>;

/// <summary>
/// Creates, changes, deletes and reads one redirect (017 FR-015, FR-017 to FR-018, FR-021;
/// research SE-09, SE-13, SE-13a). Every check is <see cref="RedirectWrites"/>'s.
/// </summary>
internal sealed class ManageRedirectHandlers(
    PagesDbContext database,
    RedirectWrites writes,
    RedirectViews views,
    PagesAccess access,
    TimeProvider clock)
    : ICommandHandler<CreateRedirectCommand, Result<RedirectResponse>>,
      ICommandHandler<UpdateRedirectCommand, Result<RedirectResponse>>,
      ICommandHandler<DeleteRedirectCommand, Result>,
      IQueryHandler<GetRedirectQuery, Result<RedirectResponse>>
{
    private const int HistoryLength = 20;

    public async Task<Result<RedirectResponse>> HandleAsync(CreateRedirectCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<RedirectResponse>(PagesErrors.SiteNotFound);
        }

        var state = await writes.LoadAsync(command.SiteId, cancellationToken).ConfigureAwait(false);
        if (!RedirectWrites.HasRoomFor(state, 1))
        {
            return Result.Failure<RedirectResponse>(PagesErrors.RedirectLimitReached(RedirectWrites.MaxPerSite));
        }

        var request = command.Request;
        var target = await writes.ResolveTargetAsync(state, RedirectViews.InputOf(request.Target), cancellationToken).ConfigureAwait(false);
        if (target.IsFailure)
        {
            return Result.Failure<RedirectResponse>(target.Error);
        }

        var check = RedirectWrites.Check(state, null, request.Match, request.Source, target.Value, request.StatusCode, request.Note);
        if (check.IsFailure)
        {
            return Result.Failure<RedirectResponse>(check.Error);
        }

        var created = writes.Add(state, check.Value, request.StatusCode, request.Enabled, request.Note, RedirectOrigins.Manual, access.Actor);
        var saved = await writes.SaveAsync(cancellationToken).ConfigureAwait(false);
        return saved.IsFailure
            ? Result.Failure<RedirectResponse>(saved.Error!)
            : Result.Success(await RespondAsync(state, created, check.Value.Shortened, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<RedirectResponse>> HandleAsync(UpdateRedirectCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<RedirectResponse>(PagesErrors.SiteNotFound);
        }

        var redirect = await database.ForSite<Redirect>(command.SiteId)
            .FirstOrDefaultAsync(r => r.Id == command.RedirectId, cancellationToken)
            .ConfigureAwait(false);
        if (redirect is null)
        {
            return Result.Failure<RedirectResponse>(PagesErrors.RedirectNotFound);
        }

        var request = command.Request;
        if (request.Version is not { } version || version != redirect.Version)
        {
            return Result.Failure<RedirectResponse>(PagesErrors.OutOfDate);
        }

        var state = await writes.LoadAsync(command.SiteId, cancellationToken).ConfigureAwait(false);
        var target = await writes.ResolveTargetAsync(state, RedirectViews.InputOf(request.Target), cancellationToken).ConfigureAwait(false);
        if (target.IsFailure)
        {
            return Result.Failure<RedirectResponse>(target.Error);
        }

        var check = RedirectWrites.Check(state, redirect.Id, request.Match, request.Source, target.Value, request.StatusCode, request.Note);
        if (check.IsFailure)
        {
            return Result.Failure<RedirectResponse>(check.Error);
        }

        var wasEnabled = redirect.Enabled;
        var actor = access.Actor;
        var now = clock.GetUtcNow();
        var checkedRedirect = check.Value;
        redirect.Change(checkedRedirect.Match, checkedRedirect.Path, checkedRedirect.Query, checkedRedirect.Target, request.StatusCode, request.Enabled, request.Note, actor, now);
        writes.Record(
            redirect,
            wasEnabled == request.Enabled ? RedirectChangeActions.Changed : request.Enabled ? RedirectChangeActions.TurnedOn : RedirectChangeActions.TurnedOff,
            actor,
            now);
        database.Entry(redirect).Property(r => r.Version).OriginalValue = version;

        var saved = await writes.SaveAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
        {
            return Result.Failure<RedirectResponse>(saved.Error!);
        }

        state.Add(RedirectWrites.Entry(redirect));
        return Result.Success(await RespondAsync(state, redirect, checkedRedirect.Shortened, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result> HandleAsync(DeleteRedirectCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.SiteExistsAsync(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure(PagesErrors.SiteNotFound);
        }

        var redirect = await database.ForSite<Redirect>(command.SiteId)
            .FirstOrDefaultAsync(r => r.Id == command.RedirectId, cancellationToken)
            .ConfigureAwait(false);
        if (redirect is null)
        {
            return Result.Failure(PagesErrors.RedirectNotFound);
        }

        if (redirect.Version != command.Version)
        {
            return Result.Failure(PagesErrors.OutOfDate);
        }

        writes.Record(redirect, RedirectChangeActions.Deleted, access.Actor);
        database.Redirects.Remove(redirect);
        database.Entry(redirect).Property(r => r.Version).OriginalValue = command.Version;
        return await writes.SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RedirectResponse>> HandleAsync(GetRedirectQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.SiteExistsAsync(query.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<RedirectResponse>(PagesErrors.SiteNotFound);
        }

        var redirect = await database.ForSite<Redirect>(query.SiteId)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == query.RedirectId, cancellationToken)
            .ConfigureAwait(false);
        if (redirect is null)
        {
            return Result.Failure<RedirectResponse>(PagesErrors.RedirectNotFound);
        }

        var state = await writes.LoadAsync(query.SiteId, cancellationToken).ConfigureAwait(false);
        var response = (await views.BuildAsync(state, [redirect], cancellationToken).ConfigureAwait(false))[0];

        var history = await database.ForSite<RedirectChange>(query.SiteId)
            .AsNoTracking()
            .Where(c => c.RedirectId == redirect.Id)
            .OrderByDescending(c => c.AtUtc)
            .ThenByDescending(c => c.Id)
            .Take(HistoryLength)
            .Select(c => new RedirectHistoryResponse(c.Action, c.Origin, c.ActorUserId, c.AtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response with { History = history });
    }

    private async Task<RedirectResponse> RespondAsync(RedirectSiteState state, Redirect redirect, bool shortened, CancellationToken cancellationToken)
    {
        var response = (await views.BuildAsync(state, [redirect], cancellationToken).ConfigureAwait(false))[0];
        return response with
        {
            Shortened = shortened,
            Warnings = await views.WarningsAsync(state, redirect, cancellationToken).ConfigureAwait(false),
        };
    }
}

/// <summary>Administrative surface: one site's redirects.</summary>
[ApiController]
[Route("api/v1/pages/sites/{siteId:guid}/redirects")]
public sealed class RedirectsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpPost]
    [RequireCapability(PagesCapabilities.RedirectsManage)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(Guid siteId, RedirectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await dispatcher.SendAsync(new CreateRedirectCommand(siteId, request), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpGet("{redirectId:guid}")]
    [RequireCapability(PagesCapabilities.RedirectsManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid siteId, Guid redirectId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetRedirectQuery(siteId, redirectId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    [HttpPut("{redirectId:guid}")]
    [RequireCapability(PagesCapabilities.RedirectsManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(Guid siteId, Guid redirectId, RedirectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await dispatcher.SendAsync(new UpdateRedirectCommand(siteId, redirectId, request), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    [HttpDelete("{redirectId:guid}")]
    [RequireCapability(PagesCapabilities.RedirectsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(Guid siteId, Guid redirectId, [FromQuery] uint version, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new DeleteRedirectCommand(siteId, redirectId, version), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? PagesErrorResults.ToActionResult(result.Error!) : NoContent();
    }
}
