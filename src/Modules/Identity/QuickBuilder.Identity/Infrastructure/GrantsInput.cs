using QuickBuilder.Identity.Domain;
using QuickBuilder.Identity.Domain.Details;
using QuickBuilder.Identity.Features.ReplaceGrants;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Shared.Kernel.Validation;
using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Identity.Infrastructure;

/// <summary>
/// Validating and applying a requested set of grants (spec FR-020; data-model.md,
/// role_grants). Shared by account creation and grant replacement.
/// </summary>
internal static class GrantsInput
{
    private static readonly Dictionary<string, SystemRole> SiteRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["designer"] = SystemRole.Designer,
        ["editor"] = SystemRole.Editor,
    };

    /// <summary>Shape only: known roles, at most one per site. Site existence is checked separately.</summary>
    public static IEnumerable<ValidationFailure> ValidateShape(GrantsRequest? grants, string field)
    {
        if (grants is null)
        {
            yield return new ValidationFailure(field, "Grants are required.");
            yield break;
        }

        var siteRoles = grants.SiteRoles ?? [];
        for (var i = 0; i < siteRoles.Count; i++)
        {
            var role = siteRoles[i];
            if (role is null || role.SiteId == Guid.Empty)
            {
                yield return new ValidationFailure($"{field}.SiteRoles[{i}].SiteId", "Choose a site.");
            }
            else if (role.Role is null || !SiteRoles.ContainsKey(role.Role))
            {
                yield return new ValidationFailure($"{field}.SiteRoles[{i}].Role", "The role must be designer or editor.");
            }
        }

        var duplicated = siteRoles
            .Where(r => r is not null)
            .GroupBy(r => r.SiteId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var siteId in duplicated)
        {
            yield return new ValidationFailure($"{field}.SiteRoles", $"A user holds one role per site; site {siteId} is listed twice.");
        }
    }

    /// <summary>
    /// Every named site must exist in the Sites module (there is no foreign key across
    /// schemas, 001 FR-022). Fails the request with field errors, as validation would.
    /// </summary>
    public static async Task EnsureSitesExistAsync(GrantsRequest grants, ISiteDirectory sites, string field, CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();
        var siteRoles = grants.SiteRoles ?? [];

        for (var i = 0; i < siteRoles.Count; i++)
        {
            if (await sites.FindByIdAsync(siteRoles[i].SiteId, cancellationToken).ConfigureAwait(false) is null)
            {
                failures.Add(new ValidationFailure($"{field}.SiteRoles[{i}].SiteId", "No such site."));
            }
        }

        if (failures.Count > 0)
        {
            // A validation failure that needs the database: reported exactly as the
            // dispatcher reports shape failures, before anything is written.
            throw new RequestValidationException(ValidationResult.Invalid(failures));
        }
    }

    public static IEnumerable<RoleGrant> ToGrants(GrantsRequest grants, Guid userId, Guid? grantedBy, DateTimeOffset now)
    {
        if (grants.Administrator)
        {
            yield return RoleGrant.Administrator(userId, grantedBy, now);
        }

        foreach (var role in grants.SiteRoles ?? [])
        {
            yield return RoleGrant.ForSite(userId, role.SiteId, SiteRoles[role.Role!], grantedBy, now);
        }
    }

    public static IReadOnlyList<GrantSummary> Summarise(IEnumerable<RoleGrant> grants) =>
        [.. grants.Select(g => new GrantSummary(g.Role.ToString().ToLowerInvariant(), g.SiteId))];
}
