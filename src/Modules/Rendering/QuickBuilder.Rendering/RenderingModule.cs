using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Rendering.Contracts;
using QuickBuilder.Rendering.Features.NotFound;

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

        services.AddScoped<IPublicNotFoundPage, NotFoundPage>();

        return services;
    }
}
