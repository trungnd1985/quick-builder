namespace QuickBuilder.Identity.Contracts;

/// <summary>Who someone is, as another module may show it.</summary>
public sealed record UserSummaryDto(Guid Id, string DisplayName);

/// <summary>
/// How other modules name the people behind recorded actions — "published by …" — without
/// reading Identity's tables (001 FR-005, FR-022).
/// </summary>
/// <remarks>
/// Accounts are never deleted (spec FR-010), so an id recorded today still resolves to a
/// name later, deactivated or not.
/// </remarks>
public interface IUserDirectory
{
    Task<IReadOnlyList<UserSummaryDto>> FindManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);
}
