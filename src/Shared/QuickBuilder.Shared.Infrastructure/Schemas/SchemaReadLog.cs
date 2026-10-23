using Microsoft.Extensions.Logging;

namespace QuickBuilder.Shared.Infrastructure.Schemas;

/// <summary>
/// Logs a stored document that cannot be used (019 FR-025, FR-026): its contract, what it
/// belongs to and its version, never its content (constitution Principle IX).
/// </summary>
public static partial class SchemaReadLog
{
    /// <summary>Logs <paramref name="read"/> when it is <see cref="SchemaRead.TooNew"/> or <see cref="SchemaRead.Failed"/>.</summary>
    public static void Report(ILogger logger, SchemaRead read, string contract, string subjectKind, Guid subjectId)
    {
        ArgumentNullException.ThrowIfNull(logger);

        switch (read)
        {
            case SchemaRead.TooNew tooNew:
                TooNew(logger, contract, subjectKind, subjectId, tooNew.StoredVersion);
                break;
            case SchemaRead.Failed failed:
                Failed(logger, contract, subjectKind, subjectId, failed.StoredVersion, failed.Reason);
                break;
        }
    }

    [LoggerMessage(
        EventName = "schema_too_new",
        Level = LogLevel.Error,
        Message = "A {Contract} document of {SubjectKind} {SubjectId} is stored at version {StoredVersion}, newer than this release reads. It is left as it is.")]
    private static partial void TooNew(ILogger logger, string contract, string subjectKind, Guid subjectId, int storedVersion);

    [LoggerMessage(
        EventName = "schema_upgrade_failed",
        Level = LogLevel.Error,
        Message = "A {Contract} document of {SubjectKind} {SubjectId} at version {StoredVersion} could not be upgraded: {Reason}")]
    private static partial void Failed(ILogger logger, string contract, string subjectKind, Guid subjectId, int storedVersion, string reason);
}
