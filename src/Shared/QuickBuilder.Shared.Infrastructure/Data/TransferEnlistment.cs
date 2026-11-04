using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace QuickBuilder.Shared.Infrastructure.Data;

/// <summary>
/// Joins a module's context to the one connection and transaction of a site export or import
/// (constitution Principle I, as amended in 2.6.0; 020 research ST-04, ST-05).
/// </summary>
/// <remarks>
/// The exception the amendment allows is narrow: the parts of one transfer share a transaction,
/// and each part still reads and writes only its own module's tables through its own context.
/// <c>TransferSessionUsageTests</c> allows calls to <see cref="JoinTransfer"/> only from archive
/// parts and the transfer engine, so this cannot become a general way to span modules.
/// <para>
/// The context must not have opened a connection of its own yet, and it must not be used after
/// the transfer ends: it does not own the connection and will not reopen one.
/// </para>
/// </remarks>
public static class TransferEnlistment
{
    /// <summary>Points <paramref name="context"/> at the transfer's connection and transaction.</summary>
    public static void JoinTransfer(this DbContext context, DbConnection connection, DbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        var database = context.Database;
        if (database.CurrentTransaction?.GetDbTransaction() == transaction)
        {
            return;
        }

        if (database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                $"{context.GetType().Name} already has a transaction of its own and cannot join a site transfer.");
        }

        database.SetDbConnection(connection, contextOwnsConnection: false);
        database.UseTransaction(transaction);
    }
}
