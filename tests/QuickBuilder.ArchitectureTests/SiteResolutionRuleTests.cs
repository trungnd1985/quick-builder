using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;
using QuickBuilder.ArchitectureTests.Fixtures;
using QuickBuilder.Shared.Kernel.MultiSite;
using Xunit;

namespace QuickBuilder.ArchitectureTests;

/// <summary>
/// Rules that keep site resolution in one place (009 research S-03, S-04).
/// </summary>
/// <remarks>
/// <c>ISiteDirectory.FindByHostNameAsync</c> answers only for enabled sites, so a disabled
/// site's host names look exactly like unknown ones. That is right for deciding which site
/// a public request belongs to and wrong almost anywhere else — which is why only the
/// host's resolution middleware may call it. Type-dependency rules cannot see a method
/// call, so this reads the IL.
/// </remarks>
public sealed class SiteResolutionRuleTests
{
    private const string Directory = "QuickBuilder.Sites.Contracts.ISiteDirectory";
    private const string Resolve = "FindByHostNameAsync";

    private static readonly string[] AllowedCallers =
    [
        "QuickBuilder.Host.Middleware.SiteResolutionMiddleware",
        "QuickBuilder.Host.Middleware.AdminHostConfinementMiddleware",
    ];

    private static readonly Assembly[] Product =
    [
        typeof(Program).Assembly,
        .. ModuleBoundaryTests.ModuleImplementations,
        .. ModuleBoundaryTests.ModuleContracts,
        typeof(HostNames).Assembly,
        typeof(Shared.Infrastructure.MultiSite.CrossSiteQuery).Assembly,
    ];

    [Fact]
    public void Only_the_site_resolution_middleware_resolves_host_names()
    {
        var offenders = Product
            .SelectMany(a => CallersOf(a, Directory, Resolve))
            .Where(caller => !AllowedCallers.Contains(caller, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{Resolve} returns enabled sites only and is for the host's resolution middleware. " +
            "Use FindByIdAsync or ICurrentSite instead. Called from:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_rule_catches_a_violation()
    {
        var callers = CallersOf(typeof(ResolvesHostNamesItself).Assembly, Directory, Resolve).ToList();

        Assert.Contains(typeof(ResolvesHostNamesItself).FullName, callers);
    }

    [Fact]
    public void The_host_name_rules_depend_on_nothing_but_the_base_library()
    {
        var result = Types.InAssembly(typeof(HostNames).Assembly)
            .That().HaveName(nameof(HostNames))
            .ShouldNot().HaveDependencyOnAny("QuickBuilder.Shared.Infrastructure", "QuickBuilder.Sites", "Microsoft")
            .GetResult();

        Assert.True(result.IsSuccessful, "HostNames must stay a plain BCL function so Host and Sites can share it.");
    }

    /// <summary>The outermost types whose methods call <paramref name="method"/> on <paramref name="declaringType"/>.</summary>
    internal static IEnumerable<string> CallersOf(Assembly assembly, string declaringType, string method)
    {
        using var definition = AssemblyDefinition.ReadAssembly(assembly.Location);

        foreach (var type in definition.MainModule.GetTypes())
        {
            foreach (var candidate in type.Methods.Where(m => m.HasBody))
            {
                var calls = candidate.Body.Instructions.Any(i =>
                    (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                    && i.Operand is MethodReference target
                    && target.Name == method
                    && target.DeclaringType.FullName == declaringType);

                if (calls)
                {
                    yield return Outermost(type).FullName;
                }
            }
        }
    }

    // Async methods compile to nested state machines; report the type a person wrote.
    private static TypeDefinition Outermost(TypeDefinition type)
    {
        while (type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }

        return type;
    }
}
