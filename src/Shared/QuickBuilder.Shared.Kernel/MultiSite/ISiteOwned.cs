namespace QuickBuilder.Shared.Kernel.MultiSite;

/// <summary>
/// An entity belonging to exactly one site (FR-013).
/// </summary>
/// <remarks>
/// Implementing this is what gets an entity filtered automatically: the EF convention in
/// Shared.Infrastructure applies a global query filter comparing <see cref="SiteId"/>
/// against the ambient site context. No handler ever writes that comparison by hand.
/// <para>
/// An architecture test asserts that every entity with a SiteId property implements this
/// interface, so the filter cannot be forgotten by omission — forgetting it is how
/// multi-tenant data leaks happen, and it fails silently rather than loudly.
/// </para>
/// <para>
/// A Site itself is NOT site-owned: a site is the scope, not a thing within one.
/// </para>
/// </remarks>
public interface ISiteOwned
{
    Guid SiteId { get; }
}
