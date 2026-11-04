namespace QuickBuilder.Identity.Contracts;

/// <summary>Who someone is, as another module may show it.</summary>
/// <param name="IsHistorical">
/// 020 research ST-14: a member of an imported site who is not a user here; shown with
/// "(imported)".
/// </param>
public sealed record UserSummaryDto(Guid Id, string DisplayName, bool IsHistorical = false);

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

    /// <summary>
    /// The address of the signed-in user, for a message they asked to receive themselves, such
    /// as a test email (018 FR-037). Null for an unknown id. Other people's addresses are never
    /// given out.
    /// </summary>
    Task<string?> OwnEmailAsync(Guid userId, CancellationToken cancellationToken = default);
}
