using System.Text.Json;
using QuickBuilder.Identity.Contracts;
using QuickBuilder.Sites.Domain;

namespace QuickBuilder.Sites.Features.Transfer;

/// <summary>A transfer as the admin sees it (020 contracts/transfer-api.yaml, <c>Transfer</c>).</summary>
public sealed record TransferResponse(
    Guid Id,
    string Kind,
    string Trigger,
    string? Mode,
    bool StartFresh,
    string? ConflictPolicy,
    Guid? SiteId,
    string? NewSiteName,
    string State,
    string? Step,
    int Percent,
    bool CancelRequested,
    long? ArchiveBytes,
    DateTimeOffset? ArchiveExpiresAtUtc,
    bool Downloadable,
    JsonElement? CheckResult,
    string? CheckFingerprint,
    DateTimeOffset? CheckedAtUtc,
    JsonElement? Summary,
    JsonElement? Report,
    string? FailureCode,
    Guid? PreRestoreTransferId,
    UserSummaryDto? StartedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? FinishedAtUtc);

internal static class TransferResponses
{
    public static async Task<TransferResponse> BuildAsync(
        TransferJob job,
        IUserDirectory users,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(users);

        var names = await NamesAsync([job], users, cancellationToken).ConfigureAwait(false);
        return Map(job, names, nowUtc);
    }

    public static async Task<IReadOnlyList<TransferResponse>> BuildManyAsync(
        IReadOnlyList<TransferJob> jobs,
        IUserDirectory users,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(users);

        var names = await NamesAsync(jobs, users, cancellationToken).ConfigureAwait(false);
        return [.. jobs.Select(j => Map(j, names, nowUtc))];
    }

    private static async Task<IReadOnlyDictionary<Guid, UserSummaryDto>> NamesAsync(
        IEnumerable<TransferJob> jobs,
        IUserDirectory users,
        CancellationToken cancellationToken)
    {
        var ids = jobs.Select(j => j.StartedBy).OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, UserSummaryDto>();
        }

        var found = await users.FindManyAsync(ids, cancellationToken).ConfigureAwait(false);
        return found.ToDictionary(u => u.Id);
    }

    private static TransferResponse Map(TransferJob job, IReadOnlyDictionary<Guid, UserSummaryDto> names, DateTimeOffset nowUtc) => new(
        job.Id,
        job.Kind,
        job.Trigger,
        job.Mode,
        job.StartFresh,
        job.ConflictPolicy,
        job.SiteId,
        job.NewSiteName,
        job.State,
        job.Step,
        job.Percent,
        job.CancelRequested,
        job.ArchiveBytes,
        job.ArchiveExpiresAtUtc,
        job.IsDownloadable(nowUtc),
        Parse(job.CheckResult),
        job.CheckFingerprint,
        job.CheckedAtUtc,
        Parse(job.Summary),
        Parse(job.Report),
        job.FailureCode,
        job.PreRestoreJobId,
        job.StartedBy is { } by ? names.GetValueOrDefault(by) : null,
        job.CreatedAtUtc,
        job.FinishedAtUtc);

    private static JsonElement? Parse(string? json) =>
        json is null ? null : JsonDocument.Parse(json).RootElement.Clone();
}
