using System.Data.Common;

namespace QuickBuilder.Sites.Contracts.Transfer;

/// <summary>
/// The one connection and transaction that every part of an export or import joins
/// (constitution Principle I, as amended in 2.6.0; research ST-04, ST-05).
/// </summary>
/// <remarks>
/// Export: <c>REPEATABLE READ, READ ONLY</c>, so every part reads the same committed state while
/// members keep editing. Import: <c>READ COMMITTED</c>, committed once by the Sites module after
/// every part has applied, so a failure, a cancel or a crash leaves the target as it was.
/// <para>
/// A part joins it with <c>context.JoinTransfer(session.Connection, session.Transaction)</c> from
/// Shared.Infrastructure. Only archive parts may (<c>TransferSessionUsageTests</c>). A part never
/// commits, rolls back or disposes either object.
/// </para>
/// </remarks>
public interface ISiteTransferSession
{
    DbConnection Connection { get; }

    DbTransaction Transaction { get; }

    /// <summary>True for an export's snapshot and for a check; false for an import's apply.</summary>
    bool IsReadOnly { get; }
}

