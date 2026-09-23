using System.Reflection;
using NetArchTest.Rules;
using QuickBuilder.Shared.Kernel.MultiSite;
using Xunit;

namespace QuickBuilder.ArchitectureTests;

/// <summary>
/// The rules from contracts/module-contract.md, enforced by the build (FR-025, FR-026).
/// </summary>
/// <remarks>
/// These run in the same stage as every other test and fail the build rather than warn.
/// A rule that only warns is a rule that gets ignored on a busy afternoon.
/// </remarks>
public sealed class ModuleBoundaryTests
{
    private static readonly Assembly Host = typeof(Program).Assembly;
    private static readonly Assembly Kernel = typeof(ISiteOwned).Assembly;
    private static readonly Assembly SharedInfrastructure =
        typeof(Shared.Infrastructure.Telemetry).Assembly;
    private static readonly Assembly SitesModule = typeof(Sites.SitesModule).Assembly;
    private static readonly Assembly SitesContracts = typeof(Sites.Contracts.SiteDto).Assembly;

    /// <summary>
    /// A module may reference another module's Contracts, never its implementation.
    /// </summary>
    /// <remarks>
    /// With one module this is close to vacuous, which is the point of asserting it now:
    /// the rule exists before there is anything to untangle, so the second module cannot
    /// be written the wrong way.
    /// </remarks>
    [Fact]
    public void Module_implementations_are_not_referenced_by_other_modules()
    {
        var moduleImplementations = new[] { SitesModule };

        foreach (var module in moduleImplementations)
        {
            var others = moduleImplementations.Where(m => m != module).ToArray();
            if (others.Length == 0)
            {
                continue;
            }

            var result = Types.InAssembly(module)
                .ShouldNot()
                .HaveDependencyOnAny(others.Select(o => o.GetName().Name!).ToArray())
                .GetResult();

            Assert.True(result.IsSuccessful, Describe(result, "module references another module's implementation"));
        }
    }

    /// <summary>
    /// Contracts stay free of the data-access and web stacks (FR-005).
    /// </summary>
    /// <remarks>
    /// A contracts project that drags in EF Core stops being a contract: callers end up
    /// depending on the storage technology of a module they are supposed to be decoupled
    /// from.
    /// </remarks>
    [Fact]
    public void Contracts_do_not_depend_on_infrastructure()
    {
        var result = Types.InAssembly(SitesContracts)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result, "a contracts type depends on EF Core or ASP.NET Core"));
    }

    /// <summary>The host composes; it holds no business logic or data access (FR-001).</summary>
    [Fact]
    public void Host_contains_no_data_access()
    {
        var result = Types.InAssembly(Host)
            .That()
            .DoNotHaveName("Program")
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore.DbContext")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result, "the host touches a DbContext directly"));
    }

    /// <summary>Shared code stays generic and knows about no module (FR-003).</summary>
    [Fact]
    public void Shared_code_does_not_depend_on_any_module()
    {
        foreach (var shared in new[] { Kernel, SharedInfrastructure })
        {
            var result = Types.InAssembly(shared)
                .ShouldNot()
                .HaveDependencyOnAny(
                    SitesModule.GetName().Name!,
                    SitesContracts.GetName().Name!)
                .GetResult();

            Assert.True(result.IsSuccessful, Describe(result, "shared code depends on a module"));
        }
    }

    /// <summary>
    /// A module's internals stay invisible outside it (FR-005).
    /// </summary>
    /// <remarks>
    /// Only the registration entry point, the controllers and the request/response shapes
    /// they bind are public. Everything else — handlers, entities, the DbContext — is
    /// internal, so the boundary is enforced by the compiler as well as by this test.
    /// </remarks>
    [Fact]
    public void Module_internals_are_not_public()
    {
        var offenders = SitesModule.GetTypes()
            .Where(t => t.IsPublic && !t.IsNested)
            // EF scaffolds migration classes as public and we do not hand-edit generated
            // code; they are schema history, not module surface.
            .Where(t => t.Namespace?.Contains(".Migrations", StringComparison.Ordinal) != true)
            .Where(t => !t.Name.EndsWith("Controller", StringComparison.Ordinal))
            .Where(t => !t.Name.EndsWith("Module", StringComparison.Ordinal))
            .Where(t => !t.Name.EndsWith("Request", StringComparison.Ordinal))
            .Where(t => !t.Name.EndsWith("Response", StringComparison.Ordinal))
            .Select(t => t.FullName!)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These module types are public but should be internal:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Every entity carrying a SiteId implements <see cref="ISiteOwned"/> (FR-013).
    /// </summary>
    /// <remarks>
    /// This is the rule that prevents the quiet failure. A site-owned entity that forgets
    /// the marker gets no query filter, so it silently returns other sites' rows — which
    /// looks like working software until it is a data leak.
    /// </remarks>
    [Fact]
    public void Entities_with_a_site_id_are_site_owned()
    {
        // Scoped to Domain/, where the module contract says entities live. A command or a
        // DTO may legitimately name a site without being a stored row, and matching on
        // "has a SiteId property" alone sweeps those in too.
        var offenders = SitesModule.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.Namespace?.EndsWith(".Domain", StringComparison.Ordinal) == true)
            .Where(t => t.GetProperty("SiteId", BindingFlags.Public | BindingFlags.Instance) is not null)
            .Where(t => !typeof(ISiteOwned).IsAssignableFrom(t))
            .Select(t => t.FullName!)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These types have a SiteId but do not implement ISiteOwned, so they get no " +
            "query filter and would leak across sites:\n  " + string.Join("\n  ", offenders));
    }

    private static string Describe(TestResult result, string rule)
    {
        var failing = result.FailingTypeNames ?? [];
        return $"Architecture rule broken — {rule}:\n  " + string.Join("\n  ", failing);
    }
}
