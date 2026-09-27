using System.Reflection;
using NetArchTest.Rules;
using QuickBuilder.Shared.Kernel.Authorization;
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

    /// <summary>Every module implementation assembly. Add a module, add it here.</summary>
    internal static readonly Assembly[] ModuleImplementations =
    [
        typeof(Sites.SitesModule).Assembly,
        typeof(Identity.IdentityModule).Assembly,
        typeof(Media.MediaModule).Assembly,
        typeof(Pages.PagesModule).Assembly,
    ];

    /// <summary>Every module contracts assembly.</summary>
    internal static readonly Assembly[] ModuleContracts =
    [
        typeof(Sites.Contracts.SiteDto).Assembly,
        typeof(Identity.Contracts.IIdentitySetupStatus).Assembly,
        typeof(Media.Contracts.MediaFileDto).Assembly,
        typeof(Pages.Contracts.PageSchema).Assembly,
    ];

    /// <summary>The file-system types only a storage provider may use (003 FR-019).</summary>
    internal static readonly string[] FileSystemTypes =
    [
        "System.IO.File",
        "System.IO.FileInfo",
        "System.IO.FileStream",
        "System.IO.FileStreamOptions",
        "System.IO.Directory",
        "System.IO.DirectoryInfo",
    ];

    /// <summary>The one namespace allowed to touch the file system: the local storage provider.</summary>
    internal const string LocalStorageNamespace = "QuickBuilder.Shared.Infrastructure.Storage.Local";

    /// <summary>
    /// A module may reference another module's Contracts, never its implementation.
    /// </summary>
    /// <remarks>
    /// Checked on the assembly references the compiler actually recorded, by exact name.
    /// The earlier NetArchTest form matched by name prefix, so "QuickBuilder.Sites" also
    /// matched "QuickBuilder.Sites.Contracts" — harmless while there was one module, and a
    /// false alarm the moment Identity legitimately used Sites.Contracts (002).
    /// </remarks>
    [Fact]
    public void Module_implementations_are_not_referenced_by_other_modules()
    {
        var implementationNames = ModuleImplementations.Select(a => a.GetName().Name!).ToHashSet(StringComparer.Ordinal);

        var offenders = ModuleImplementations
            .SelectMany(module => ReferencedImplementations(module, implementationNames)
                .Select(referenced => $"{module.GetName().Name} -> {referenced}"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Architecture rule broken — module references another module's implementation:\n  " +
            string.Join("\n  ", offenders));
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
        foreach (var contracts in ModuleContracts)
        {
            var result = Types.InAssembly(contracts)
                .ShouldNot()
                .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql")
                .GetResult();

            Assert.True(result.IsSuccessful, Describe(result, "a contracts type depends on EF Core or ASP.NET Core"));
        }
    }

    /// <summary>
    /// The page schema contract depends on nothing but the kernel (010 research P-09).
    /// </summary>
    /// <remarks>
    /// Rendering (#12) will take Pages.Contracts for the validator and the schema. If it
    /// pulled in another module's contracts, depending on the page schema would quietly mean
    /// depending on that module too.
    /// </remarks>
    [Fact]
    public void Pages_contracts_reference_only_the_kernel()
    {
        var references = typeof(Pages.Contracts.PageSchema).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("QuickBuilder.", StringComparison.Ordinal))
            .ToList();

        Assert.Equal([Kernel.GetName().Name!], references);
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
        var moduleNames = ModuleImplementations
            .Concat(ModuleContracts)
            .Select(a => a.GetName().Name!)
            .ToArray();

        foreach (var shared in new[] { Kernel, SharedInfrastructure })
        {
            var result = Types.InAssembly(shared)
                .ShouldNot()
                .HaveDependencyOnAny(moduleNames)
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
        var offenders = ModuleImplementations
            .SelectMany(PublicNonSurfaceTypes)
            .Select(t => t.FullName!)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These module types are public but should be internal:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Every entity carrying a SiteId implements <see cref="ISiteOwned"/> (FR-013), unless
    /// it is explicitly marked <see cref="InstallationScopedAttribute"/> with a reason.
    /// </summary>
    /// <remarks>
    /// This is the rule that prevents the quiet failure. A site-owned entity that forgets
    /// the marker gets no query filter, so it silently returns other sites' rows — which
    /// looks like working software until it is a data leak.
    /// <para>
    /// The exemption exists for data that <em>refers to</em> a site without being owned by
    /// one, such as a role grant (002 data-model.md). It must carry a written reason; a
    /// reviewer who does not find the reason convincing is looking at a bug.
    /// </para>
    /// </remarks>
    [Fact]
    public void Entities_with_a_site_id_are_site_owned()
    {
        var offenders = ModuleImplementations
            .SelectMany(UnmarkedSiteReferencingEntities)
            .Select(t => t.FullName!)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These types have a SiteId but neither implement ISiteOwned nor carry a reasoned " +
            "[InstallationScoped]; they get no query filter and would leak across sites:\n  " +
            string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The one exemption in use is deliberate: role grants, and only role grants.
    /// </summary>
    /// <remarks>
    /// A dedicated test for each exemption, as constitution Principle V asks of anything
    /// that escapes site scoping. A new [InstallationScoped] entity fails here until someone
    /// adds it on purpose.
    /// </remarks>
    [Fact]
    public void Installation_scoped_entities_are_exactly_the_reviewed_ones()
    {
        var exempted = ModuleImplementations
            .SelectMany(a => a.GetTypes())
            .Where(t => t.GetCustomAttribute<InstallationScopedAttribute>() is not null)
            .Select(t => t.FullName!)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["QuickBuilder.Identity.Domain.RoleGrant"], exempted);
        Assert.All(
            exempted,
            name => Assert.False(string.IsNullOrWhiteSpace(
                ModuleImplementations.Select(a => a.GetType(name)).First(t => t is not null)!
                    .GetCustomAttribute<InstallationScopedAttribute>()!.Reason)));
    }

    /// <summary>
    /// Stored bytes are reached only through <c>IFileStorage</c> (003 FR-019, analysis C2).
    /// </summary>
    /// <remarks>
    /// No module touches the file system directly, and within shared code only the local
    /// storage provider does. A Media handler that reached for <c>File.OpenRead</c> "because
    /// the local provider is the only one" is exactly how the abstraction would erode into a
    /// file-system assumption that the S3 and Azure providers then cannot honour.
    /// </remarks>
    [Fact]
    public void Only_the_local_storage_provider_touches_the_file_system()
    {
        var offenders = ModuleImplementations
            .Concat(ModuleContracts)
            .SelectMany(a => FileSystemUsers(a, _ => true))
            .Concat(FileSystemUsers(SharedInfrastructure, t => t.Namespace != LocalStorageNamespace))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These types use the file system directly; go through IFileStorage instead:\n  " +
            string.Join("\n  ", offenders));
    }

    /// <summary>Types in <paramref name="assembly"/>, among those <paramref name="inScope"/> selects, that use a file-system type.</summary>
    internal static IEnumerable<string> FileSystemUsers(Assembly assembly, Func<Type, bool> inScope)
    {
        var failing = Types.InAssembly(assembly)
            // Coverlet injects a hit tracker that writes to disk and cannot be loaded by name.
            .That().DoNotResideInNamespace("Coverlet")
            .ShouldNot()
            .HaveDependencyOnAny(FileSystemTypes)
            .GetResult()
            .FailingTypes ?? [];

        return failing
            .Where(inScope)
            // Compiler-generated state machines are reported alongside their owner.
            .Where(t => !t.Name.Contains('<', StringComparison.Ordinal))
            .Select(t => t.FullName!);
    }

    internal static IEnumerable<string> ReferencedImplementations(Assembly module, IReadOnlySet<string> implementationNames) =>
        module.GetReferencedAssemblies()
            .Select(r => r.Name!)
            .Where(name => implementationNames.Contains(name) && name != module.GetName().Name);

    internal static IEnumerable<Type> PublicNonSurfaceTypes(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsNested)
            // EF scaffolds migration classes as public and we do not hand-edit generated
            // code; they are schema history, not module surface.
            .Where(t => t.Namespace?.Contains(".Migrations", StringComparison.Ordinal) != true)
            .Where(t => !t.Name.EndsWith("Controller", StringComparison.Ordinal))
            .Where(t => !t.Name.EndsWith("Module", StringComparison.Ordinal))
            .Where(t => !t.Name.EndsWith("Request", StringComparison.Ordinal))
            .Where(t => !t.Name.EndsWith("Response", StringComparison.Ordinal));

    private static IEnumerable<Type> UnmarkedSiteReferencingEntities(Assembly assembly) =>
        UnmarkedSiteReferencingEntities(assembly, ".Domain");

    /// <remarks>
    /// Scoped to one namespace suffix: <c>.Domain</c>, where the module contract says
    /// entities live, because a command or a DTO may legitimately name a site without being
    /// a stored row. The fires-when-broken tests pass <c>.Fixtures</c>.
    /// </remarks>
    internal static IEnumerable<Type> UnmarkedSiteReferencingEntities(Assembly assembly, string namespaceSuffix) =>
        assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.Namespace?.EndsWith(namespaceSuffix, StringComparison.Ordinal) == true)
            .Where(t => t.GetProperty("SiteId", BindingFlags.Public | BindingFlags.Instance) is not null)
            .Where(t => !typeof(ISiteOwned).IsAssignableFrom(t))
            .Where(t => string.IsNullOrWhiteSpace(t.GetCustomAttribute<InstallationScopedAttribute>()?.Reason));

    private static string Describe(TestResult result, string rule)
    {
        var failing = result.FailingTypeNames ?? [];
        return $"Architecture rule broken — {rule}:\n  " + string.Join("\n  ", failing);
    }
}
