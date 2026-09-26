using Microsoft.EntityFrameworkCore;

namespace QuickBuilder.ArchitectureTests.Fixtures;

// Deliberate architecture violations, one per rule (SC-004).
//
// These exist ONLY to be caught by RulesFireWhenBrokenTests. Nothing in production
// references them, and nothing should. If you are reading this because a rule flagged one
// of these types in a real run, the rule is being applied to the wrong assembly — these
// live in the test project on purpose.
//
// The point: a rule that passes on a clean tree might be passing because it works, or
// because its predicate is broken. Only a known violation tells the two apart.

/// <summary>
/// Violates module isolation: references another module's implementation directly instead
/// of going through its Contracts project.
/// </summary>
public sealed class LeaksAcrossModules
{
    // QuickBuilder.Sites is a module implementation. Only QuickBuilder.Sites.Contracts is
    // referenceable from outside the module.
    private readonly Type _leaked = typeof(Sites.SitesModule);

    public Type Leaked => _leaked;
}

/// <summary>
/// Does NOT violate module isolation: another module's Contracts are the sanctioned way in.
/// Keeps the fixture assembly referencing Sites.Contracts, so the isolation rule is proven
/// not to confuse it with Sites.
/// </summary>
public sealed class UsesOnlyAContract
{
    private readonly Type _contract = typeof(Sites.Contracts.SiteDto);

    public Type Contract => _contract;
}

/// <summary>
/// Violates contracts purity: a type standing in for a Contracts type that has dragged in
/// the data-access stack.
/// </summary>
public sealed class PretendContractWithEfCore
{
    public DbContextOptions? Options { get; init; }
}

/// <summary>
/// Violates site ownership: carries a SiteId but does not implement ISiteOwned, so it
/// would receive no global query filter and return every site's rows.
/// </summary>
public sealed class ForgotTheSiteOwnedMarker
{
    public Guid Id { get; init; }

    public Guid SiteId { get; init; }

    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// Does NOT violate site ownership: it references a site on purpose and says why, as a role
/// grant does (002 data-model.md). The rule must let it through — otherwise the exemption
/// is broken and every legitimate reference would need a workaround.
/// </summary>
[QuickBuilder.Shared.Kernel.Authorization.InstallationScoped("Fixture: installation data that names a site without belonging to it.")]
public sealed class ReferencesASiteDeliberately
{
    public Guid Id { get; init; }

    public Guid? SiteId { get; init; }
}

/// <summary>
/// Violates the visibility rule: a module-internal concept left public.
/// </summary>
public sealed class ShouldHaveBeenInternal
{
    public string Secret { get; init; } = string.Empty;
}

/// <summary>
/// Violates 003 FR-019: reads stored bytes straight off the disk instead of through
/// <c>IFileStorage</c>.
/// </summary>
public sealed class ReadsTheDiskDirectly
{
    public static byte[] Read(string path) => System.IO.File.ReadAllBytes(path);
}

/// <summary>
/// The control. Breaks nothing, and must not be flagged — otherwise the predicates are too
/// broad and the tests above would pass for the wrong reason.
/// </summary>
public sealed class WellBehaved
{
    public string Value { get; init; } = string.Empty;
}

/// <summary>
/// Violates the resolution rule (009 research S-03): resolves a host name outside the
/// host's site-resolution middleware, where a disabled site's absence could be mistaken
/// for something else or the result reused out of context.
/// </summary>
public sealed class ResolvesHostNamesItself(Sites.Contracts.ISiteDirectory directory)
{
    public Task<Sites.Contracts.SiteDto?> ResolveAsync(string hostName) => directory.FindByHostNameAsync(hostName);
}
