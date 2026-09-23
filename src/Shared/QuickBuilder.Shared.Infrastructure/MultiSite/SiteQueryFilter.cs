namespace QuickBuilder.Shared.Infrastructure.MultiSite;

/// <summary>
/// Why site filters are declared per module rather than applied generically from here.
/// </summary>
/// <remarks>
/// This file deliberately contains no helper. An earlier version offered
/// <c>modelBuilder.ApplySiteFilters(siteContext)</c>, which looped over every
/// <see cref="Kernel.MultiSite.ISiteOwned"/> entity and built a filter closing over the
/// injected <see cref="ISiteContext"/>. It was wrong, and wrong in the worst way — it
/// compiled, it read correctly, and it silently matched nothing.
/// <para>
/// <b>The trap</b>: EF Core builds and caches the model once per context type. A filter
/// closing over an injected service captures whichever instance happened to build the
/// model — in practice the one from startup migration, where no site is resolved. Every
/// later request then filtered against a stale empty site and returned zero rows. Nothing
/// threw. It was only caught because a scoping test configured two sites and asserted
/// each one could see its own data.
/// </para>
/// <para>
/// <b>What to do instead</b>: expose a <c>CurrentSiteId</c> property on the module's
/// DbContext that reads the ambient site, and write the filter to reference it through
/// <c>this</c>:
/// </para>
/// <code>
/// internal Guid CurrentSiteId => siteContext.SiteId ?? Guid.Empty;
///
/// entity.HasQueryFilter(h =&gt; h.SiteId == CurrentSiteId);
/// </code>
/// <para>
/// EF lifts a context property into a query parameter and re-evaluates it per query,
/// against the context actually running it. Falling back to <see cref="Guid.Empty"/> makes
/// an unresolved site match nothing, which fails closed.
/// </para>
/// <para>
/// <b>The guard</b>: <c>SiteFilterCoverageTests</c> asserts every <c>ISiteOwned</c> entity
/// in every module's model actually has a query filter, so declaring them by hand cannot
/// be silently forgotten.
/// </para>
/// </remarks>
internal static class SiteQueryFilterGuidance;
