using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.Identity.Domain;

/// <summary>
/// One role held by one user, with an explicit scope (spec FR-020, FR-020a).
/// </summary>
/// <remarks>
/// Administrator is installation-wide and never names a site; Designer and Editor always
/// name exactly one. The database enforces the same rule with a CHECK constraint, and at
/// most one role per user per site with a partial unique index (data-model.md).
/// <para>
/// <see cref="SiteId"/> refers to a site owned by the Sites module. There is no foreign
/// key across schemas (001 FR-022); the site is validated through <c>ISiteDirectory</c>
/// when a grant is written.
/// </para>
/// </remarks>
[InstallationScoped("Grants are installation data managed from the admin surface, which has no resolved site; the site is a reference, not an owner.")]
internal sealed class RoleGrant
{
    private RoleGrant()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public SystemRole Role { get; private set; }

    /// <summary>Null for Administrator (installation scope); the site otherwise.</summary>
    public Guid? SiteId { get; private set; }

    public DateTimeOffset GrantedAtUtc { get; private set; }

    /// <summary>Null for the grant created by setup.</summary>
    public Guid? GrantedByUserId { get; private set; }

    public bool IsAdministrator => Role == SystemRole.Administrator;

    public static RoleGrant Administrator(Guid userId, Guid? grantedByUserId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Role = SystemRole.Administrator,
            SiteId = null,
            GrantedAtUtc = now,
            GrantedByUserId = grantedByUserId,
        };

    public static RoleGrant ForSite(Guid userId, Guid siteId, SystemRole role, Guid? grantedByUserId, DateTimeOffset now)
    {
        if (role == SystemRole.Administrator)
        {
            throw new ArgumentException(
                "Administrator is installation-wide and cannot be granted for one site (FR-020).",
                nameof(role));
        }

        if (siteId == Guid.Empty)
        {
            throw new ArgumentException("A site role must name a site.", nameof(siteId));
        }

        return new RoleGrant
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Role = role,
            SiteId = siteId,
            GrantedAtUtc = now,
            GrantedByUserId = grantedByUserId,
        };
    }
}
