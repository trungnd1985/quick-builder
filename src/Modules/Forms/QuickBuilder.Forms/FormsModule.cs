using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Forms.Features.RefreshFormRegistry;
using QuickBuilder.Forms.Features.Submit;
using QuickBuilder.Forms.Infrastructure;
using QuickBuilder.Pages.Contracts.Events;
using QuickBuilder.Shared.Infrastructure.Modules;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Forms;

/// <summary>
/// The Forms module's single registration entry point (specs/018-forms-submissions).
/// </summary>
/// <remarks>
/// Adding the module is one line in the host. Everything below is <c>internal</c> apart from
/// this class, the controllers and the request/response shapes they bind, so the boundary is
/// enforced by the compiler as well as by the architecture tests.
/// </remarks>
public static class FormsModule
{
    /// <summary>Registers the Forms module: data, capabilities, events, workers and slices.</summary>
    public static IServiceCollection AddFormsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Default");

        services.AddDbContext<FormsDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
                // Each module keeps its own migrations history table in its own schema.
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", FormsDbContext.Schema));
        });

        services.AddSingleton<IModuleDatabaseProvider, FormsDatabaseProvider>();

        // What this module lets people do. Identity enforces them; this module only declares them.
        foreach (var capability in FormsCapabilities.All)
        {
            services.AddSingleton(capability);
        }

        // Research FS-05: the registry follows Pages' publishing events. Pages registers the
        // event types; this module only handles them.
        services.AddScoped<IIntegrationEventHandler<PagePublished>, RefreshFormRegistryHandler>();
        services.AddScoped<IIntegrationEventHandler<PageUnpublished>, RefreshFormRegistryHandler>();
        services.AddScoped<IIntegrationEventHandler<ComponentPublished>, RefreshFormRegistryHandler>();
        services.AddScoped<IIntegrationEventHandler<ComponentUnpublished>, RefreshFormRegistryHandler>();

        // Research FS-06 to FS-09: the public submit endpoint.
        services.AddScoped<LiveFormLocator>();
        services.AddScoped<ICommandHandler<SubmitCommand, SubmitResult>, SubmitHandler>();

        return services;
    }
}

/// <summary>Lets the host migrate and health-check the <c>forms</c> schema.</summary>
internal sealed class FormsDatabaseProvider : IModuleDatabaseProvider
{
    public ModuleDatabase Describe() =>
        new("Forms", FormsDbContext.Schema, typeof(FormsDbContext));

    public DbContext CreateContext(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Built directly: migration, health and background work run outside any request,
        // where no site is resolved. The empty site context fails closed.
        var options = services.GetRequiredService<DbContextOptions<FormsDbContext>>();
        return new FormsDbContext(options, NoSiteContext.Instance);
    }

    public async Task<bool> CanConnectAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = CreateContext(services);
            return await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private sealed class NoSiteContext : ISiteContext
    {
        internal static readonly NoSiteContext Instance = new();

        public Guid? SiteId => null;

        public bool HasSite => false;
    }
}
