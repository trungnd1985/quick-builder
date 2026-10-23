using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Rendering.Domain;
using QuickBuilder.Rendering.Features.NotFound;
using QuickBuilder.Rendering.Features.RenderPage;
using QuickBuilder.Rendering.Rendering;
using QuickBuilder.Rendering.Rendering.Blocks;
using QuickBuilder.Shared.Kernel.Dispatch;

namespace QuickBuilder.Rendering;

/// <summary>
/// The Rendering module's single registration entry point (specs/012-publishing-rendering).
/// </summary>
/// <remarks>
/// Rendering owns no tables (research PR-01). It turns published documents into HTML, caches
/// the result and answers the public surface, reading only through other modules' Contracts
/// readers, which run on read-only connections (PR-06).
/// </remarks>
public static class RenderingModule
{
    /// <summary>Registers the Rendering module.</summary>
    public static IServiceCollection AddRenderingModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // One renderer per block type (FR-023). BlockRendererCoverageTests holds this list to
        // the page schema.
        services.AddSingleton<IBlockRenderer, SectionRenderer>();
        services.AddSingleton<IBlockRenderer, ContainerRenderer>();
        services.AddSingleton<IBlockRenderer, HeadingRenderer>();
        services.AddSingleton<IBlockRenderer, TextRenderer>();
        services.AddSingleton<IBlockRenderer, RichTextBlockRenderer>();
        services.AddSingleton<IBlockRenderer, ImageRenderer>();
        services.AddSingleton<IBlockRenderer, ButtonRenderer>();
        services.AddSingleton<IBlockRenderer, SpacerRenderer>();
        services.AddSingleton<IBlockRenderer, DividerRenderer>();
        services.AddSingleton<IBlockRenderer, EmbedRenderer>();
        services.AddSingleton<IBlockRenderer, ComponentRefRenderer>();
        services.AddSingleton<IBlockRenderer, CollectionListRenderer>();
        services.AddSingleton<IBlockRenderer, ListCardRenderer>();
        services.AddSingleton<IBlockRenderer, ListEmptyRenderer>();
        // 024: a site's menu, with active state and the one navigation script.
        services.AddSingleton<IBlockRenderer, NavigationRenderer>();
        // 018: a form visitors submit to /_forms.
        services.AddSingleton<IBlockRenderer, FormRenderer>();
        services.AddSingleton<PageRenderer>();

        services.AddSingleton<RenderMetrics>();
        services.AddScoped<PageResponder>();
        services.AddScoped<RedirectResponder>();

        // 018 research FS-09: the page a refused form submission is shown on, never cached.
        services.AddScoped<IFormResponsePage, Features.FormResponse.FormResponsePage>();
        services.AddSingleton<IFormNotificationTexts, Features.NotFound.FormNotificationTextCatalogue>();

        // 017 research SE-14 to SE-16: the sitemap and robots.txt.
        services.AddScoped<Features.Sitemap.SitemapBuilder>();
        services.AddScoped<IQueryHandler<Features.Sitemap.SitemapQuery, PublicResponse>, Features.Sitemap.SitemapHandler>();
        services.AddScoped<IQueryHandler<Features.Robots.RobotsQuery, PublicResponse>, Features.Robots.RobotsHandler>();

        // 016 research TB-09: what bindings read, fetched before rendering.
        services.AddScoped<QuickBuilder.Rendering.Binding.BindingFetcher>();
        services.AddScoped<IQueryHandler<RenderPageQuery, PublicResponse>, RenderPageHandler>();
        services.AddScoped<IPublicNotFoundPage, NotFoundPage>();

        // Research PR-08: the page route never matches the administrative prefixes.
        services.Configure<RouteOptions>(options => options.ConstraintMap[PublicPathConstraint.Name] = typeof(PublicPathConstraint));

        return services;
    }
}
