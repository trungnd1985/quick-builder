namespace QuickBuilder.Shared.Kernel.Authorization;

// These are plain attributes with no ASP.NET Core dependency. Modules declare *what* an
// action requires; the Identity module is the only place that knows *how* to enforce it
// (research R-05). That split is what lets Sites and every later module declare access
// without referencing Identity, and lets contracts projects stay free of the web stack.

/// <summary>
/// The action requires a signed-in user holding the named capability — at the site named
/// by the route's <c>{siteId}</c> when the capability is site-scoped
/// (contracts/authorization.md §1).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequireCapabilityAttribute : Attribute
{
    public RequireCapabilityAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }
}

/// <summary>
/// The action requires any signed-in user; the handler narrows what they see. Used for
/// self-service actions and for lists filtered per user (contracts/authorization.md §1).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequireSignedInAttribute : Attribute;

/// <summary>
/// Marks a domain entity that <em>refers to</em> a site without being <em>owned</em> by
/// one, so it carries no site query filter (data-model.md, role_grants).
/// </summary>
/// <remarks>
/// The architecture rule "every entity with a SiteId is site-owned" accepts this marker
/// only with a non-empty reason. The reason is written for a reviewer: if it does not
/// obviously justify why the entity is installation data, the entity is probably
/// site-owned and the marker is a bug.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class InstallationScopedAttribute : Attribute
{
    public InstallationScopedAttribute(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    public string Reason { get; }
}
