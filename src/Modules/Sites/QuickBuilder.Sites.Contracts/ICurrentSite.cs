namespace QuickBuilder.Sites.Contracts;

/// <summary>
/// The site the current public request resolved to, with its details (009 FR-015).
/// </summary>
/// <remarks>
/// Filters only need the id, which <c>ISiteContext</c> carries. This is for everything
/// else a module may need about the site — its name, base URL, default language — without
/// reading request headers or querying again: the site resolution middleware already
/// loaded it, and this returns that same instance.
/// <para>
/// Null on the administrative surface, where no site is resolved from the host. There,
/// the site is named in the route; use <see cref="ISiteDirectory.FindByIdAsync"/>.
/// </para>
/// </remarks>
public interface ICurrentSite
{
    SiteDto? Site { get; }
}
