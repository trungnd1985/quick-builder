using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Shared.Infrastructure.Caching;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.Migrations;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Infrastructure.Startup;

namespace QuickBuilder.Shared.Infrastructure;

/// <summary>
/// Registers everything shared across modules.
/// </summary>
/// <remarks>
/// Deliberately free of module-specific concepts (FR-003). Nothing here knows that a
/// module called Sites exists — an architecture test asserts that shared code references
/// no module.
/// </remarks>
public static class SharedInfrastructureRegistration
{
    public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<StartupState>();
        services.AddSingleton<ModuleMigrationRunner>();

        services.AddMemoryCache();
        services.AddSingleton<ICacheProvider, MemoryCacheProvider>();

        // Scoped: the site is a property of one request, not of the process.
        services.AddScoped<SiteContext>();
        services.AddScoped<ISiteContext>(sp => sp.GetRequiredService<SiteContext>());
        services.AddScoped<ISiteContextSetter>(sp => sp.GetRequiredService<SiteContext>());

        services.AddScoped<IDispatcher, Dispatcher>();

        // Outbox runtime (FR-016, FR-020). Singleton dispatcher because it owns the event
        // type registry; the background services create their own scopes per pass.
        services.AddSingleton<IIntegrationEventDispatcher, IntegrationEventDispatcher>();
        services.AddHostedService<OutboxDispatcher>();
        services.AddHostedService<OutboxRetentionService>();

        return services;
    }
}
