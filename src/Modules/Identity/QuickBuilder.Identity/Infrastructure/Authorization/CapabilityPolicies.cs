using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using QuickBuilder.Identity.Infrastructure.Authentication;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.Identity.Infrastructure.Authorization;

/// <summary>Policy names produced from the kernel's declarations (research R-05).</summary>
internal static class IdentityPolicies
{
    public const string SignedIn = "signed-in";
    public const string CapabilityPrefix = "capability:";

    public static string ForCapability(string name) => CapabilityPrefix + name;
}

/// <summary>
/// Turns the kernel's plain <see cref="RequireCapabilityAttribute"/> and
/// <see cref="RequireSignedInAttribute"/> into ASP.NET Core authorization metadata.
/// </summary>
/// <remarks>
/// This is how modules declare access without referencing ASP.NET Core authorization:
/// they place a kernel attribute, and this convention adds the matching
/// <see cref="AuthorizeAttribute"/> to each endpoint. Enforcement then runs through the
/// framework's authorization middleware, the constitution's "policy-based checks".
/// </remarks>
internal sealed class CapabilityConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        ArgumentNullException.ThrowIfNull(application);

        foreach (var controller in application.Controllers)
        {
            foreach (var action in controller.Actions)
            {
                var policy = PolicyFor(action.Attributes) ?? PolicyFor(controller.Attributes);
                if (policy is null)
                {
                    continue;
                }

                foreach (var selector in action.Selectors)
                {
                    selector.EndpointMetadata.Add(new AuthorizeAttribute(policy));
                }
            }
        }
    }

    private static string? PolicyFor(IEnumerable<object> attributes)
    {
        foreach (var attribute in attributes)
        {
            switch (attribute)
            {
                case RequireCapabilityAttribute capability:
                    return IdentityPolicies.ForCapability(capability.Name);
                case RequireSignedInAttribute:
                    return IdentityPolicies.SignedIn;
            }
        }

        return null;
    }
}

/// <summary>Builds <c>capability:&lt;name&gt;</c> policies on demand.</summary>
internal sealed class CapabilityPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        ArgumentNullException.ThrowIfNull(policyName);

        if (!policyName.StartsWith(IdentityPolicies.CapabilityPrefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName).ConfigureAwait(false);
        }

        return new AuthorizationPolicyBuilder(SessionAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser()
            .AddRequirements(new CapabilityRequirement(policyName[IdentityPolicies.CapabilityPrefix.Length..]))
            .Build();
    }
}

internal sealed class CapabilityRequirement(string name) : IAuthorizationRequirement
{
    public string Name { get; } = name;
}

/// <summary>
/// Decides a <see cref="CapabilityRequirement"/>, reading the site from the route's
/// <c>{siteId}</c> for site-scoped capabilities (contracts/authorization.md §2).
/// </summary>
/// <remarks>
/// A failure records <em>why</em>, because the two refusals look different to the caller
/// (FR-022, FR-023): a user who cannot see the site gets exactly the "no such site"
/// response, while a user who can see it but may not do this gets 403.
/// </remarks>
internal sealed class CapabilityRequirementHandler(
    IAccessEvaluator access,
    CapabilityCatalog catalog) : AuthorizationHandler<CapabilityRequirement>
{
    public const string SiteNotVisible = "site_not_visible";
    public const string MissingCapability = "missing_capability";

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CapabilityRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (!access.IsSignedIn)
        {
            return;
        }

        var definition = catalog.Get(requirement.Name);
        Guid? siteId = null;

        if (definition.Scope == CapabilityScope.Site)
        {
            var httpContext = context.Resource as HttpContext;
            if (!Guid.TryParse(httpContext?.GetRouteValue("siteId")?.ToString(), out var parsed))
            {
                // A site-scoped capability on a route without {siteId} is a wiring error the
                // architecture test catches; refuse rather than guess.
                context.Fail(new AuthorizationFailureReason(this, MissingCapability));
                return;
            }

            siteId = parsed;

            var visible = await access.GetVisibleSitesAsync().ConfigureAwait(false);
            if (!visible.CanSee(parsed))
            {
                context.Fail(new AuthorizationFailureReason(this, SiteNotVisible));
                return;
            }
        }

        if (await access.HasCapabilityAsync(requirement.Name, siteId).ConfigureAwait(false))
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail(new AuthorizationFailureReason(this, MissingCapability));
        }
    }
}
