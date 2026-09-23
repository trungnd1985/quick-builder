using System.Reflection;
using NetArchTest.Rules;
using QuickBuilder.ArchitectureTests.Fixtures;
using QuickBuilder.Shared.Kernel.MultiSite;
using Xunit;

namespace QuickBuilder.ArchitectureTests;

/// <summary>
/// Proves each architecture rule fails when broken (SC-004).
/// </summary>
/// <remarks>
/// <see cref="ModuleBoundaryTests"/> asserts the real tree obeys the rules. That is only
/// half of what SC-004 asks for: a rule with a bug in its predicate also passes on a clean
/// tree, and would keep passing while the boundary it is supposed to defend quietly
/// erodes.
/// <para>
/// Each test below points the same predicate at a fixture that deliberately violates the
/// rule and asserts it is caught. The fixtures live in <c>Fixtures/</c> and are never
/// referenced by production code — they exist only to be found.
/// </para>
/// </remarks>
public sealed class RulesFireWhenBrokenTests
{
    private static readonly Assembly Fixtures = typeof(LeaksAcrossModules).Assembly;

    /// <summary>The module-isolation predicate catches a cross-module implementation reference.</summary>
    /// <remarks>
    /// The fixture assembly references QuickBuilder.Sites through <see cref="LeaksAcrossModules"/>,
    /// so the real rule's predicate must report it.
    /// </remarks>
    [Fact]
    public void Module_isolation_rule_catches_a_violation()
    {
        var implementationNames = ModuleBoundaryTests.ModuleImplementations
            .Select(a => a.GetName().Name!)
            .ToHashSet(StringComparer.Ordinal);

        var referenced = ModuleBoundaryTests.ReferencedImplementations(Fixtures, implementationNames).ToList();

        Assert.True(
            referenced.Contains("QuickBuilder.Sites"),
            "The module-isolation rule did not flag a reference to QuickBuilder.Sites. The rule is not doing its job.");
    }

    /// <summary>
    /// The module-isolation predicate does not mistake a contracts reference for an
    /// implementation one.
    /// </summary>
    /// <remarks>
    /// The bug this rule had before 002: matching by name prefix made
    /// "QuickBuilder.Sites.Contracts" look like "QuickBuilder.Sites".
    /// </remarks>
    [Fact]
    public void Module_isolation_rule_allows_a_contracts_reference()
    {
        var implementationNames = ModuleBoundaryTests.ModuleImplementations
            .Select(a => a.GetName().Name!)
            .ToHashSet(StringComparer.Ordinal);

        // The fixture assembly references Sites.Contracts (UsesOnlyAContract) as well as
        // Sites itself; only the implementation may be reported.
        Assert.Contains(Fixtures.GetReferencedAssemblies(), r => r.Name == "QuickBuilder.Sites.Contracts");
        Assert.DoesNotContain(
            "QuickBuilder.Sites.Contracts",
            ModuleBoundaryTests.ReferencedImplementations(Fixtures, implementationNames));
    }

    /// <summary>The contracts-purity predicate catches an EF Core dependency.</summary>
    [Fact]
    public void Contracts_purity_rule_catches_a_violation()
    {
        var result = Types.InAssembly(Fixtures)
            .That()
            .HaveNameStartingWith(nameof(PretendContractWithEfCore))
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql")
            .GetResult();

        Assert.False(
            result.IsSuccessful,
            "The contracts-purity rule did not flag a type depending on EF Core.");
    }

    /// <summary>
    /// The site-ownership predicate catches an entity that forgot the marker.
    /// </summary>
    /// <remarks>
    /// The most valuable of these. An entity with a SiteId and no <see cref="ISiteOwned"/>
    /// gets no query filter, so it returns every site's rows — and it looks like working
    /// software right up until it is a cross-tenant data leak.
    /// </remarks>
    [Fact]
    public void Site_ownership_rule_catches_an_unmarked_entity()
    {
        var offenders = ModuleBoundaryTests.UnmarkedSiteReferencingEntities(Fixtures, ".Fixtures").ToList();

        Assert.Contains(offenders, t => t == typeof(ForgotTheSiteOwnedMarker));
    }

    /// <summary>
    /// The site-ownership predicate honours a reasoned <c>[InstallationScoped]</c>, and only
    /// that: the unmarked entity beside it is still caught.
    /// </summary>
    [Fact]
    public void Site_ownership_rule_accepts_a_reasoned_exemption()
    {
        var offenders = ModuleBoundaryTests.UnmarkedSiteReferencingEntities(Fixtures, ".Fixtures").ToList();

        Assert.DoesNotContain(offenders, t => t == typeof(ReferencesASiteDeliberately));
        Assert.Contains(offenders, t => t == typeof(ForgotTheSiteOwnedMarker));
    }

    /// <summary>An exemption without a reason cannot even be written.</summary>
    [Fact]
    public void An_installation_scoped_marker_requires_a_reason()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Shared.Kernel.Authorization.InstallationScopedAttribute("  "));
    }

    /// <summary>The visibility predicate catches a public type that should be internal.</summary>
    [Fact]
    public void Internal_visibility_rule_catches_a_public_type()
    {
        var offenders = Fixtures.GetTypes()
            .Where(t => t.IsPublic && !t.IsNested)
            .Where(t => t.Namespace?.Contains(".Migrations", StringComparison.Ordinal) != true)
            .Where(t => !t.Name.EndsWith("Controller", StringComparison.Ordinal))
            .Where(t => !t.Name.EndsWith("Module", StringComparison.Ordinal))
            .Where(t => !t.Name.EndsWith("Request", StringComparison.Ordinal))
            .Where(t => !t.Name.EndsWith("Response", StringComparison.Ordinal))
            .ToList();

        Assert.Contains(offenders, t => t == typeof(ShouldHaveBeenInternal));
    }

    /// <summary>
    /// A control: the predicates do not simply fail on everything.
    /// </summary>
    /// <remarks>
    /// Without this, a rule that always returns "violation" would pass every test above
    /// while being useless. Cheap insurance against the fixtures proving nothing.
    /// </remarks>
    [Fact]
    public void Rules_pass_on_a_type_that_obeys_them()
    {
        var result = Types.InAssembly(Fixtures)
            .That()
            .HaveNameStartingWith(nameof(WellBehaved))
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "QuickBuilder.Sites")
            .GetResult();

        Assert.True(result.IsSuccessful, "A compliant fixture was flagged; the predicate is too broad.");
    }
}
