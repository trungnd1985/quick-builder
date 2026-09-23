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
/// Violates the visibility rule: a module-internal concept left public.
/// </summary>
public sealed class ShouldHaveBeenInternal
{
    public string Secret { get; init; } = string.Empty;
}

/// <summary>
/// The control. Breaks nothing, and must not be flagged — otherwise the predicates are too
/// broad and the tests above would pass for the wrong reason.
/// </summary>
public sealed class WellBehaved
{
    public string Value { get; init; } = string.Empty;
}
