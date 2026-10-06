using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using CheckRow = QuickBuilder.Media.Domain.StorageCheck;

namespace QuickBuilder.Media.Features.StorageCheck;

/// <summary>A library file whose current original is missing from storage. Ids only, never names.</summary>
internal sealed record StorageCheckMissingDto(Guid SiteId, Guid FileId);

/// <summary>One consistency check (contracts/media-api.yaml <c>StorageCheck</c>).</summary>
internal sealed record StorageCheckDto(
    Guid Id,
    string Trigger,
    string Provider,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    int FilesChecked,
    int MissingCount,
    IReadOnlyList<StorageCheckMissingDto> Missing)
{
    public static StorageCheckDto From(CheckRow check, IReadOnlyList<StorageCheckMissingDto> missing) => new(
        check.Id,
        check.Trigger,
        check.Provider,
        check.Status,
        check.StartedAtUtc,
        check.FinishedAtUtc,
        check.FilesChecked,
        check.MissingCount,
        missing);
}

/// <summary>The configured provider and the latest check (contracts/media-api.yaml <c>StorageStatus</c>).</summary>
internal sealed record StorageStatusDto(string Provider, StorageCheckDto? LatestCheck);

internal sealed record GetStorageCheckQuery : IQuery<Result<StorageStatusDto>>;

internal sealed record StartStorageCheckCommand(Guid UserId) : ICommand<Result<StorageCheckDto>>;

internal sealed class GetStorageCheckHandler(MediaDbContext database, StorageCheckRunner runner)
    : IQueryHandler<GetStorageCheckQuery, Result<StorageStatusDto>>
{
    public async Task<Result<StorageStatusDto>> HandleAsync(GetStorageCheckQuery query, CancellationToken cancellationToken)
    {
        var latest = await database.StorageChecks
            .AsNoTracking()
            .OrderByDescending(c => c.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (latest is null)
        {
            return Result.Success(new StorageStatusDto(runner.Provider, null));
        }

        var missing = await database.StorageCheckMissing
            .AcrossAllSites("An installation-level check lists files of every site to Administrators (013 MV-21).")
            .AsNoTracking()
            .Where(m => m.CheckId == latest.Id)
            .OrderBy(m => m.SiteId).ThenBy(m => m.FileId)
            .Take(CheckRow.MaxListedMissing)
            .Select(m => new StorageCheckMissingDto(m.SiteId, m.FileId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new StorageStatusDto(runner.Provider, StorageCheckDto.From(latest, missing)));
    }
}

internal sealed class StartStorageCheckHandler(StorageCheckRunner runner)
    : ICommandHandler<StartStorageCheckCommand, Result<StorageCheckDto>>
{
    public async Task<Result<StorageCheckDto>> HandleAsync(StartStorageCheckCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var check = await runner.TryClaimAsync(Domain.StorageCheckTrigger.Admin, command.UserId, cancellationToken).ConfigureAwait(false);
        if (check is null)
        {
            return Result.Failure<StorageCheckDto>(MediaErrors.StorageCheckRunning);
        }

        // The run outlives the request: the singleton runner owns it until the application stops.
        runner.Start(check.Id);
        return Result.Success(StorageCheckDto.From(check, []));
    }
}

/// <summary>Administrative surface: the storage consistency check (013 research MV-21).</summary>
[ApiController]
[Route("api/v1/media/storage-check")]
public sealed class StorageCheckController(IDispatcher dispatcher, IAccessEvaluator access) : ControllerBase
{
    /// <summary>The configured provider and the latest check, with up to 1,000 missing files.</summary>
    [HttpGet]
    [RequireCapability(MediaCapabilities.StorageCheck)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new GetStorageCheckQuery(), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? MediaErrorResults.ToActionResult(result.Error) : Ok(result.Value);
    }

    /// <summary>Starts a check now; it runs in the background.</summary>
    [HttpPost]
    [RequireCapability(MediaCapabilities.StorageCheck)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartAsync(CancellationToken cancellationToken)
    {
        var command = new StartStorageCheckCommand(
            access.UserId ?? throw new InvalidOperationException("A storage check was started without a signed-in user."));

        var result = await dispatcher.SendAsync(command, cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? MediaErrorResults.ToActionResult(result.Error) : Accepted(result.Value);
    }
}
