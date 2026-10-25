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
        typeof(Editor.EditorModule).Assembly,
        typeof(Rendering.RenderingModule).Assembly,
        typeof(Cms.CmsModule).Assembly,
        typeof(Forms.FormsModule).Assembly,
    ];

    /// <summary>Every module contracts assembly.</summary>
    internal static readonly Assembly[] ModuleContracts =
    [
        typeof(Sites.Contracts.SiteDto).Assembly,
        typeof(Identity.Contracts.IIdentitySetupStatus).Assembly,
        typeof(Media.Contracts.MediaFileDto).Assembly,
        typeof(Pages.Contracts.PageSchema).Assembly,
        typeof(Editor.Contracts.DesignTokens).Assembly,
        typeof(Rendering.Contracts.RenderingRoutes).Assembly,
        typeof(Cms.Contracts.CollectionSchema).Assembly,
        typeof(Forms.Contracts.FormsRoutes).Assembly,
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
    /// The one other type allowed to touch the file system (013 research MV-18): the S3
    /// provider's upload spool. S3 needs a content length, and an upload arrives as a stream
    /// of unknown length, so the provider buffers it in a delete-on-close temporary file. It
    /// never reads or writes stored media, only its own scratch file.
    /// </summary>
    internal const string S3UploadSpoolType = "QuickBuilder.Shared.Infrastructure.Storage.S3.S3UploadSpool";

    /// <summary>The native image library (013 research MV-09).</summary>
    internal static readonly string[] ImageLibraryNamespaces = ["NetVips"];

    /// <summary>Where the Media module wraps it; nothing else may use it.</summary>
    internal const string ImagingNamespace = "QuickBuilder.Media.Imaging";

    /// <summary>The cloud storage SDKs (013 research MV-18, MV-19).</summary>
    internal static readonly string[] CloudStorageNamespaces = ["Amazon", "Azure"];

    /// <summary>The storage provider namespaces allowed to use them.</summary>
    internal static readonly string[] RemoteStorageNamespaces =
    [
        "QuickBuilder.Shared.Infrastructure.Storage.S3",
        "QuickBuilder.Shared.Infrastructure.Storage.AzureBlob",
    ];

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

    /// <summary>
    /// The design token contract depends on nothing but the kernel (011 research ED-14).
    /// </summary>
    /// <remarks>
    /// Rendering (#12) will take Editor.Contracts to read token sets, for the same reason it
    /// takes Pages.Contracts: depending on the contract must not mean depending on a third
    /// module.
    /// </remarks>
    [Fact]
    public void Editor_contracts_reference_only_the_kernel()
    {
        var references = typeof(Editor.Contracts.DesignTokens).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("QuickBuilder.", StringComparison.Ordinal))
            .ToList();

        // Nothing but the kernel; today it needs not even that.
        Assert.All(references, name => Assert.Equal(Kernel.GetName().Name, name));
    }

    /// <summary>
    /// The collection schema contract depends on nothing but the kernel (014 contracts/module-surface.md §1).
    /// </summary>
    /// <remarks>
    /// The item editor (#15) and the renderer (#16) will take Cms.Contracts; depending on the
    /// contract must not mean depending on a third module.
    /// </remarks>
    [Fact]
    public void Cms_contracts_reference_only_the_kernel()
    {
        var references = typeof(Cms.Contracts.CollectionSchema).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("QuickBuilder.", StringComparison.Ordinal))
            .ToList();

        Assert.All(references, name => Assert.Equal(Kernel.GetName().Name, name));
    }

    /// <summary>
    /// Cms reads other modules only through the contracts its plan names (014 module-surface.md §1).
    /// Only Media, Pages and Rendering depend on Cms, and only on its Contracts: Media to read
    /// items' media usage (015 module-surface.md §1-§2), Pages for templates, lists and refreshes,
    /// Rendering for published items (016 module-surface.md §1). Nothing references the Cms
    /// implementation.
    /// </summary>
    [Fact]
    public void Cms_references_only_the_contracts_it_declares_and_only_media_references_cms_contracts()
    {
        string[] allowed =
        [
            "QuickBuilder.Shared.Kernel",
            "QuickBuilder.Shared.Infrastructure",
            "QuickBuilder.Cms.Contracts",
            "QuickBuilder.Pages.Contracts",
            "QuickBuilder.Media.Contracts",
            "QuickBuilder.Sites.Contracts",
            "QuickBuilder.Identity.Contracts",
            // 023 research CE-07, CE-21: item draft saves are fenced by editing sessions.
            "QuickBuilder.Editor.Contracts",
        ];

        var references = typeof(Cms.CmsModule).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("QuickBuilder.", StringComparison.Ordinal))
            .ToList();

        Assert.All(references, name => Assert.Contains(name, allowed));

        var dependents = ModuleImplementations
            .Concat(ModuleContracts)
            .Where(a => a.GetName().Name is not ("QuickBuilder.Cms" or "QuickBuilder.Cms.Contracts"))
            .SelectMany(a => a.GetReferencedAssemblies()
                .Where(r => r.Name is "QuickBuilder.Cms" or "QuickBuilder.Cms.Contracts")
                .Select(r => $"{a.GetName().Name} -> {r.Name}"))
            .ToList();

        string[] allowedDependents =
        [
            "QuickBuilder.Media -> QuickBuilder.Cms.Contracts",
            "QuickBuilder.Pages -> QuickBuilder.Cms.Contracts",
            "QuickBuilder.Rendering -> QuickBuilder.Cms.Contracts",
            // 023 research CE-21: editing sessions end when items or collections are deleted.
            "QuickBuilder.Editor -> QuickBuilder.Cms.Contracts",
        ];

        Assert.All(dependents, edge => Assert.Contains(edge, allowedDependents));
    }

    /// <summary>The Forms contract depends on nothing but the kernel (018 module-surface.md §1).</summary>
    [Fact]
    public void Forms_contracts_reference_only_the_kernel()
    {
        var references = typeof(Forms.Contracts.FormsRoutes).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("QuickBuilder.", StringComparison.Ordinal))
            .ToList();

        Assert.All(references, name => Assert.Equal(Kernel.GetName().Name, name));
    }

    /// <summary>
    /// Forms reads other modules only through the contracts its plan names (018 module-surface.md
    /// §1): Pages for published forms, Rendering for the page shown after a refusal, Sites and
    /// Identity for names. Only the host references the Forms implementation, and no module
    /// references Forms at all yet.
    /// </summary>
    [Fact]
    public void Forms_references_only_the_contracts_it_declares_and_nothing_references_forms()
    {
        string[] allowed =
        [
            "QuickBuilder.Shared.Kernel",
            "QuickBuilder.Shared.Infrastructure",
            "QuickBuilder.Forms.Contracts",
            "QuickBuilder.Pages.Contracts",
            "QuickBuilder.Rendering.Contracts",
            "QuickBuilder.Sites.Contracts",
            "QuickBuilder.Identity.Contracts",
        ];

        var references = typeof(Forms.FormsModule).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("QuickBuilder.", StringComparison.Ordinal))
            .ToList();

        Assert.All(references, name => Assert.Contains(name, allowed));

        var dependents = ModuleImplementations
            .Concat(ModuleContracts)
            .Where(a => a.GetName().Name is not ("QuickBuilder.Forms" or "QuickBuilder.Forms.Contracts"))
            .SelectMany(a => a.GetReferencedAssemblies()
                .Where(r => r.Name is "QuickBuilder.Forms" or "QuickBuilder.Forms.Contracts")
                .Select(r => $"{a.GetName().Name} -> {r.Name}"))
            .ToList();

        Assert.Empty(dependents);
    }

    /// <summary>
    /// No Contracts project references another module's Contracts (016 module-surface.md §1),
    /// so the edges between modules can never form a cycle through their contracts.
    /// </summary>
    [Fact]
    public void Contracts_do_not_reference_other_modules_contracts()
    {
        var contractNames = ModuleContracts.Select(a => a.GetName().Name!).ToHashSet(StringComparer.Ordinal);

        // Sites.Contracts is the one shared vocabulary of site identity: Media and Rendering
        // contracts name sites through it, as they always have (012 readers.md §1.4).
        var offenders = ModuleContracts
            .SelectMany(a => a.GetReferencedAssemblies()
                .Where(r => contractNames.Contains(r.Name!) && r.Name is not ("QuickBuilder.Sites.Contracts" or "QuickBuilder.Identity.Contracts"))
                .Select(r => $"{a.GetName().Name} -> {r.Name}"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Architecture rule broken — a Contracts project references another module's Contracts:\n  " +
            string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Rendering owns no data and reads only through other modules' Contracts (012 research
    /// PR-01, PR-06).
    /// </summary>
    /// <remarks>
    /// This is half of what makes the public path read-only (constitution Principle VII): a
    /// module with no <c>DbContext</c> of its own can only read through the readers other
    /// modules hand it, and those run on read-only connections. The other half is
    /// <c>PublicPathReadOnlyTests</c>.
    /// </remarks>
    [Fact]
    public void Rendering_has_no_data_access_of_its_own()
    {
        var rendering = typeof(Rendering.RenderingModule).Assembly;

        var result = Types.InAssembly(rendering)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result, "Rendering touches EF Core or Npgsql"));
    }

    /// <summary>Rendering's contract names sites and nothing else (012 contracts/readers.md §1.4).</summary>
    [Fact]
    public void Rendering_contracts_reference_only_the_kernel_and_sites_contracts()
    {
        var references = typeof(Rendering.Contracts.RenderingRoutes).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("QuickBuilder.", StringComparison.Ordinal))
            .ToList();

        Assert.All(
            references,
            name => Assert.Contains(name, new[] { Kernel.GetName().Name!, typeof(Sites.Contracts.SiteDto).Assembly.GetName().Name! }));
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
    /// Only the migration runner reaches the <c>platform</c> record (019 research RL-07,
    /// contracts/installation-version.md §2).
    /// </summary>
    /// <remarks>
    /// The context is internal, so the compiler already keeps modules and the host out; this
    /// keeps the rest of shared code out too. Everything else reads the record through
    /// <c>IInstallationVersion</c>, whose implementation lives beside the runner.
    /// </remarks>
    [Fact]
    public void Only_the_migration_runner_touches_the_platform_record()
    {
        var result = Types.InAssembly(SharedInfrastructure)
            .That()
            .DoNotResideInNamespace("QuickBuilder.Shared.Infrastructure.Migrations")
            .ShouldNot()
            .HaveDependencyOn("QuickBuilder.Shared.Infrastructure.Migrations.Platform.PlatformDbContext")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result, "shared code outside the migration runner uses PlatformDbContext"));
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
            .Concat(FileSystemUsers(
                SharedInfrastructure,
                t => t.Namespace != LocalStorageNamespace && t.FullName != S3UploadSpoolType))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These types use the file system directly; go through IFileStorage instead:\n  " +
            string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Native image code is reached only through the Media module's <c>Imaging</c> wrappers
    /// (013 research MV-09).
    /// </summary>
    /// <remarks>
    /// libvips decodes untrusted uploads. Keeping every call behind three small classes is
    /// what makes the hardening in <c>NetVipsSetup</c> (blocked loaders, no cache, bounded
    /// threads) a guarantee rather than a convention: a handler that opened an image itself
    /// could bypass the pixel limit or the loader allow-list.
    /// </remarks>
    [Fact]
    public void Only_media_imaging_uses_the_native_image_library()
    {
        var offenders = ModuleImplementations
            .Concat(ModuleContracts)
            .Append(SharedInfrastructure)
            .Append(Kernel)
            .Append(Host)
            .SelectMany(assembly => Types.InAssembly(assembly)
                .That().DoNotResideInNamespace("Coverlet")
                .ShouldNot()
                .HaveDependencyOnAny(ImageLibraryNamespaces)
                .GetResult()
                .FailingTypes ?? [])
            .Where(t => !(t.Namespace ?? string.Empty).StartsWith(ImagingNamespace, StringComparison.Ordinal))
            .Where(t => !t.Name.Contains('<', StringComparison.Ordinal))
            .Select(t => t.FullName!)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These types use NetVips outside QuickBuilder.Media.Imaging:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The cloud storage SDKs are used only by the providers that wrap them (013 research
    /// MV-18, MV-19, MV-20).
    /// </summary>
    /// <remarks>
    /// Consumers reach bytes through <c>IFileStorage</c> and never know which provider is
    /// configured. An SDK type anywhere else would be a provider assumption leaking out, and
    /// a network call a default installation might make (FR-042).
    /// </remarks>
    [Fact]
    public void Only_the_remote_storage_providers_use_cloud_sdks()
    {
        var offenders = ModuleImplementations
            .Concat(ModuleContracts)
            .Append(SharedInfrastructure)
            .Append(Kernel)
            .Append(Host)
            .SelectMany(assembly => Types.InAssembly(assembly)
                .That().DoNotResideInNamespace("Coverlet")
                .ShouldNot()
                .HaveDependencyOnAny(CloudStorageNamespaces)
                .GetResult()
                .FailingTypes ?? [])
            .Where(t => !RemoteStorageNamespaces.Contains(t.Namespace, StringComparer.Ordinal))
            .Where(t => !t.Name.Contains('<', StringComparison.Ordinal))
            .Select(t => t.FullName!)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These types use a cloud storage SDK outside the S3 and Azure Blob providers:\n  " +
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
