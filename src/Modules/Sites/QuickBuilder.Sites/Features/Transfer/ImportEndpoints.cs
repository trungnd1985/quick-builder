using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using QuickBuilder.Identity.Contracts;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.Storage;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Sites.Contracts;
using QuickBuilder.Sites.Domain;
using QuickBuilder.Sites.Features.Transfer.Engine;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites.Features.Transfer;

internal sealed record CreateUploadCommand(string Trigger) : ICommand<Result<TransferResponse>>;

/// <summary>Reserves an upload; the archive's bytes follow with <c>PUT</c> (020 research ST-17).</summary>
internal sealed class CreateUploadHandler(TransferJobStore store, IAccessEvaluator access, IUserDirectory users, TimeProvider clock)
    : ICommandHandler<CreateUploadCommand, Result<TransferResponse>>
{
    public async Task<Result<TransferResponse>> HandleAsync(CreateUploadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = clock.GetUtcNow();
        var job = TransferJob.ReserveUpload(command.Trigger, access.UserId, now);
        await store.SaveAsync(job, null, cancellationToken).ConfigureAwait(false);
        return Result.Success(await TransferResponses.BuildAsync(job, users, now, cancellationToken).ConfigureAwait(false));
    }
}

internal sealed record UploadArchiveCommand(Guid TransferId, Stream Body) : ICommand<Result>;

/// <summary>
/// Streams an archive into storage, unbuffered, up to <c>SiteTransfer:MaxUploadSizeBytes</c>
/// (020 research ST-17). Storage keeps nothing of an upload that fails or is too large.
/// </summary>
internal sealed class UploadArchiveHandler(TransferJobStore store, IFileStorage storage, IOptions<SiteTransferOptions> options, TimeProvider clock)
    : ICommandHandler<UploadArchiveCommand, Result>
{
    public async Task<Result> HandleAsync(UploadArchiveCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var job = await store.FindAsync(command.TransferId, cancellationToken).ConfigureAwait(false);
        if (job is null || job.Kind != TransferKinds.Import)
        {
            return Result.Failure(TransferErrors.NotFound);
        }

        if (job.State != TransferStates.Uploading)
        {
            return Result.Failure(TransferErrors.NotCheckable);
        }

        var key = TransferStorageKeys.ForInstallation(job.Id);
        var limited = new LimitedReadStream(command.Body, options.Value.MaxUploadSizeBytes);
        try
        {
            await storage.WriteAsync(key, limited, cancellationToken).ConfigureAwait(false);
        }
        catch (UploadTooLargeException)
        {
            return Result.Failure(TransferErrors.TooLarge);
        }

        job.Uploaded(key.Value, limited.Total, clock.GetUtcNow());
        if (await store.SaveAsync(job, null, cancellationToken).ConfigureAwait(false) != SaveOutcome.Saved)
        {
            await storage.DeleteAsync(key, CancellationToken.None).ConfigureAwait(false);
            return Result.Failure(TransferErrors.Changed);
        }

        return Result.Success();
    }

    /// <summary>Counts what is read, and fails once more than the limit has arrived.</summary>
    private sealed class LimitedReadStream(Stream inner, long limit) : Stream
    {
        public long Total { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => Total; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private int Count(int read)
        {
            Total += read;
            return Total > limit ? throw new UploadTooLargeException() : read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class UploadTooLargeException : IOException;
}

internal sealed record CheckArchiveCommand(Guid TransferId, string Mode, Guid? TargetSiteId) : ICommand<Result<TransferResponse>>;

/// <summary>Queues a check of an uploaded archive against a target (020 FR-011).</summary>
internal sealed class CheckArchiveHandler(TransferJobStore store, ISiteDirectory sites, IUserDirectory users, TimeProvider clock)
    : ICommandHandler<CheckArchiveCommand, Result<TransferResponse>>
{
    public async Task<Result<TransferResponse>> HandleAsync(CheckArchiveCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var job = await store.FindAsync(command.TransferId, cancellationToken).ConfigureAwait(false);
        if (job is null || job.Kind != TransferKinds.Import)
        {
            return Result.Failure<TransferResponse>(TransferErrors.NotFound);
        }

        if (command.TargetSiteId is { } target && await sites.FindByIdAsync(target, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<TransferResponse>(Error.NotFound("sites.not_found", "No such site."));
        }

        var now = clock.GetUtcNow();
        var requested = job.RequestCheck(command.Mode, command.TargetSiteId, now);
        if (requested.IsFailure)
        {
            return Result.Failure<TransferResponse>(requested.Error!);
        }

        return await store.SaveAsync(job, null, cancellationToken).ConfigureAwait(false) == SaveOutcome.Saved
            ? Result.Success(await TransferResponses.BuildAsync(job, users, now, cancellationToken).ConfigureAwait(false))
            : Result.Failure<TransferResponse>(TransferErrors.Changed);
    }
}

internal sealed record ConfirmImportCommand(
    Guid TransferId,
    string? NewSiteName,
    string? NewSiteHostName,
    bool StartFresh,
    string? ConflictPolicy,
    bool AcceptWarnings,
    string? ConfirmSiteName,
    string? CheckFingerprint) : ICommand<Result<TransferResponse>>;

/// <summary>
/// Confirms a checked import (020 FR-011, FR-016, FR-020, FR-027b). Warnings must be accepted; a
/// new site needs a host name when the archive leaves it none; a restore needs the site's name
/// typed. Confirming takes the site's one active slot, so a busy site answers
/// <c>transfer.busy</c> (analysis I2).
/// </summary>
internal sealed class ConfirmImportHandler(TransferJobStore store, ISiteDirectory sites, IUserDirectory users, TimeProvider clock)
    : ICommandHandler<ConfirmImportCommand, Result<TransferResponse>>
{
    public async Task<Result<TransferResponse>> HandleAsync(ConfirmImportCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var job = await store.FindAsync(command.TransferId, cancellationToken).ConfigureAwait(false);
        if (job is null || job.Kind != TransferKinds.Import)
        {
            return Result.Failure<TransferResponse>(TransferErrors.NotFound);
        }

        if (job.State == TransferStates.AwaitingConfirmation && job.CheckResult is { } json)
        {
            using var check = JsonDocument.Parse(json);
            var root = check.RootElement;
            if (!command.AcceptWarnings && root.GetProperty("problems").EnumerateArray().Any(p => p.GetProperty("severity").GetString() == "warning"))
            {
                return Result.Failure<TransferResponse>(TransferErrors.WarningsNotAccepted);
            }

            if (job.Mode == TransferModes.NewSite && root.GetProperty("hostNamesKept").GetArrayLength() == 0 && string.IsNullOrWhiteSpace(command.NewSiteHostName))
            {
                return Result.Failure<TransferResponse>(TransferErrors.InvalidOptions(
                    "Every host name of the archive is used by another site here. Give the new site a host name."));
            }

            if (job.Mode == TransferModes.NewSite && command.NewSiteHostName is { Length: > 0 } host
                && await sites.IsHostNameTakenAsync(host, cancellationToken).ConfigureAwait(false))
            {
                return Result.Failure<TransferResponse>(Error.Conflict("sites.host_name_taken", "That host name is already used by another site."));
            }

            if (job.Mode == TransferModes.Restore)
            {
                var site = await sites.FindByIdAsync(job.SiteId!.Value, cancellationToken).ConfigureAwait(false);
                if (site is null || !string.Equals(command.ConfirmSiteName?.Trim(), site.Name, StringComparison.Ordinal))
                {
                    return Result.Failure<TransferResponse>(TransferErrors.ConfirmationMismatch);
                }
            }
        }

        var now = clock.GetUtcNow();
        var confirmed = job.Confirm(
            new ImportOptions(command.NewSiteName, command.StartFresh, command.ConflictPolicy, command.CheckFingerprint, command.NewSiteHostName),
            now);
        if (confirmed.IsFailure)
        {
            return Result.Failure<TransferResponse>(confirmed.Error!);
        }

        return await store.SaveAsync(job, null, cancellationToken).ConfigureAwait(false) switch
        {
            SaveOutcome.Saved => Result.Success(await TransferResponses.BuildAsync(job, users, now, cancellationToken).ConfigureAwait(false)),
            SaveOutcome.SiteBusy => Result.Failure<TransferResponse>(TransferErrors.Busy),
            _ => Result.Failure<TransferResponse>(TransferErrors.Changed),
        };
    }
}

/// <summary>Request body of <c>POST /api/v1/transfers/{id}/check</c>.</summary>
public sealed record CheckArchiveRequest(string Mode, Guid? TargetSiteId);

/// <summary>Request body of <c>POST /api/v1/transfers/{id}/confirm</c>.</summary>
public sealed record ConfirmImportRequest(
    string? NewSiteName,
    string? NewSiteHostName,
    bool StartFresh,
    string? ConflictPolicy,
    bool AcceptWarnings,
    string? ConfirmSiteName,
    string? CheckFingerprint);

/// <summary>Administrative surface: uploading, checking and confirming imports (020 contracts/transfer-api.yaml).</summary>
[ApiController]
[Route("api/v1/transfers")]
public sealed class TransferImportController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Reserves an upload; PUT the archive to <c>/uploads/{id}</c>.</summary>
    [HttpPost("uploads")]
    [RequireCapability(SitesCapabilities.TransferImport)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateUploadAsync(CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new CreateUploadCommand(TransferTriggers.Admin), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? ErrorResults.ToActionResult(result.Error) : StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>Streams the archive into storage. The body is not buffered.</summary>
    [HttpPut("uploads/{transferId:guid}")]
    [RequireCapability(SitesCapabilities.TransferImport)]
    [DisableRequestSizeLimit]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> UploadAsync(Guid transferId, CancellationToken cancellationToken)
    {
        // The limit is the handler's, from configuration, and counted as the body arrives.
        if (HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
        {
            size.MaxRequestBodySize = null;
        }

        var result = await dispatcher.SendAsync(new UploadArchiveCommand(transferId, Request.Body), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? ErrorResults.ToActionResult(result.Error) : NoContent();
    }

    /// <summary>Checks the uploaded archive against a target; follow the transfer for the result.</summary>
    [HttpPost("{transferId:guid}/check")]
    [RequireCapability(SitesCapabilities.TransferImport)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> CheckAsync(Guid transferId, CheckArchiveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await dispatcher.SendAsync(new CheckArchiveCommand(transferId, request.Mode, request.TargetSiteId), cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? ErrorResults.ToActionResult(result.Error) : Accepted(result.Value);
    }

    /// <summary>Confirms a checked import; it then runs in the background.</summary>
    [HttpPost("{transferId:guid}/confirm")]
    [RequireCapability(SitesCapabilities.TransferImport)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ConfirmAsync(Guid transferId, ConfirmImportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await dispatcher.SendAsync(
            new ConfirmImportCommand(transferId, request.NewSiteName, request.NewSiteHostName, request.StartFresh, request.ConflictPolicy,
                request.AcceptWarnings, request.ConfirmSiteName, request.CheckFingerprint),
            cancellationToken).ConfigureAwait(false);
        return result.IsFailure ? ErrorResults.ToActionResult(result.Error) : Accepted(result.Value);
    }
}
