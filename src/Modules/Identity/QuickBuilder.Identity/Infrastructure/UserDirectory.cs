using Microsoft.EntityFrameworkCore;
using QuickBuilder.Identity.Contracts;

namespace QuickBuilder.Identity.Infrastructure;

/// <summary>
/// Identity's side of <see cref="IUserDirectory"/>: display names of anyone, and an address only
/// for the user asking about themselves.
/// </summary>
internal sealed class UserDirectory(IdentityDbContext database) : IUserDirectory
{
    public async Task<IReadOnlyList<UserSummaryDto>> FindManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        if (userIds.Count == 0)
        {
            return [];
        }

        return await database.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new UserSummaryDto(u.Id, u.DisplayName))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<string?> OwnEmailAsync(Guid userId, CancellationToken cancellationToken = default) =>
        database.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (string?)u.Email)
            .SingleOrDefaultAsync(cancellationToken);
}
