using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms;
using QuickBuilder.ArchitectureTests.Fixtures;
using QuickBuilder.Editor;
using QuickBuilder.Identity;
using QuickBuilder.Media;
using QuickBuilder.Pages;
using QuickBuilder.Shared.Kernel.Authorization;
using QuickBuilder.Sites;
using Xunit;

namespace QuickBuilder.ArchitectureTests;

/// <summary>
/// Every action declares who may call it, and the build fails otherwise (002 FR-025,
/// SC-002; research R-06; contracts/authorization.md).
/// </summary>
/// <remarks>
/// The fallback policy already makes an undeclared action require sign-in at runtime, so
/// forgetting is not a hole. This test makes it a build failure as well, because "signed
/// in" is almost never the intended rule, and the right one should be written down.
/// </remarks>
public sealed class AuthorizationDeclarationTests
{
    /// <summary>contracts/authorization.md §4, as Controller.Action names.</summary>
    internal static readonly HashSet<string> AnonymousAllowList = new(StringComparer.Ordinal)
    {
        "HealthController.Live",
        "HealthController.ReadyAsync",
        "GetStatusController.GetAsync",
        "CompleteSetupController.SetUpAsync",
        "SignInController.SignInAsync",
        "CheckSetPasswordLinkController.CheckAsync",
        "SetPasswordController.SetAsync",
        // 003 FR-026: media files on each site's public surface. 013 MV-08: the same action serves
        // later versions and resized variants (`{id}-v{n}.{ext}`, `{id}-v{n}-w{width}.{ext}`).
        "ServePublicFileController.GetAsync",
        // 012 FR-022: published pages, their stylesheet, and the 405 for other methods.
        "RenderPageController.GetAsync",
        "RenderPageController.OtherMethod",
        "ServeBlockStylesheetController.GetAsync",
    };

    private static readonly Assembly[] ControllerAssemblies =
    [
        typeof(Program).Assembly,
        .. ModuleBoundaryTests.ModuleImplementations,
    ];

    [Fact]
    public void Every_action_declares_its_access_correctly()
    {
        var violations = DeclarationRules.Violations(
            ControllerAssemblies.SelectMany(a => a.GetTypes()).Where(DeclarationRules.IsController),
            RegisteredCapabilities(),
            AnonymousAllowList);

        Assert.True(violations.Count == 0, "Access declaration problems:\n  " + string.Join("\n  ", violations));
    }

    /// <summary>The allow-list names only actions that exist, so it cannot rot silently.</summary>
    [Fact]
    public void The_anonymous_allow_list_names_real_actions()
    {
        var actions = ControllerAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(DeclarationRules.IsController)
            .SelectMany(c => DeclarationRules.Actions(c).Select(m => $"{c.Name}.{m.Name}"))
            .ToHashSet(StringComparer.Ordinal);

        var missing = AnonymousAllowList.Except(actions).ToList();

        Assert.True(missing.Count == 0, "Allow-list entries with no matching action:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>Every capability every module registers, by name.</summary>
    internal static IReadOnlyDictionary<string, CapabilityScope> RegisteredCapabilities()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection()
            .AddSitesModule(configuration)
            .AddIdentityModule(configuration)
            .AddMediaModule(configuration)
            .AddPagesModule(configuration)
            .AddEditorModule(configuration)
            .AddCmsModule(configuration);

        return services
            .Where(d => d.ServiceType == typeof(CapabilityDefinition))
            .Select(d => (CapabilityDefinition)d.ImplementationInstance!)
            .ToDictionary(c => c.Name, c => c.Scope, StringComparer.Ordinal);
    }
}

/// <summary>The rules, shared by the real test and the fires-when-broken fixtures.</summary>
internal static class DeclarationRules
{
    public static bool IsController(Type type) =>
        type is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(type);

    public static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());

    public static List<string> Violations(
        IEnumerable<Type> controllers,
        IReadOnlyDictionary<string, CapabilityScope> capabilities,
        IReadOnlySet<string> anonymousAllowList)
    {
        var violations = new List<string>();

        foreach (var controller in controllers)
        {
            foreach (var action in Actions(controller))
            {
                var name = $"{controller.Name}.{action.Name}";
                var capability = action.GetCustomAttribute<RequireCapabilityAttribute>() ?? controller.GetCustomAttribute<RequireCapabilityAttribute>();
                var signedIn = action.GetCustomAttribute<RequireSignedInAttribute>() ?? controller.GetCustomAttribute<RequireSignedInAttribute>();
                var anonymous = action.GetCustomAttribute<AllowAnonymousAttribute>() ?? controller.GetCustomAttribute<AllowAnonymousAttribute>();

                var declared = new object?[] { capability, signedIn, anonymous }.Count(d => d is not null);
                if (declared != 1)
                {
                    violations.Add(declared == 0
                        ? $"{name} declares no access: add [RequireCapability], [RequireSignedIn] or (rarely) [AllowAnonymous]."
                        : $"{name} declares access more than once; exactly one declaration is allowed.");
                    continue;
                }

                if (anonymous is not null && !anonymousAllowList.Contains(name))
                {
                    violations.Add($"{name} is [AllowAnonymous] but not on the anonymous allow-list (contracts/authorization.md §4).");
                }

                if (capability is null)
                {
                    continue;
                }

                if (!capabilities.TryGetValue(capability.Name, out var scope))
                {
                    violations.Add($"{name} requires capability '{capability.Name}', which no module registers.");
                }
                else if (scope == CapabilityScope.Site && !RouteOf(controller, action).Contains("{siteId", StringComparison.Ordinal))
                {
                    violations.Add($"{name} requires site-scoped '{capability.Name}' but its route has no {{siteId}}.");
                }
            }
        }

        return violations;
    }

    private static string RouteOf(Type controller, MethodInfo action)
    {
        var prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
        var template = action.GetCustomAttributes<HttpMethodAttribute>().FirstOrDefault()?.Template ?? string.Empty;
        return $"{prefix}/{template}";
    }
}
