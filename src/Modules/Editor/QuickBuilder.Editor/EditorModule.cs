using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuickBuilder.Editor.Contracts;
using QuickBuilder.Editor.Contracts.Events;
using QuickBuilder.Editor.Features.DesignPublishing;
using QuickBuilder.Editor.Features.GetDesignTokens;
using QuickBuilder.Editor.Features.SaveDesignTokens;
using QuickBuilder.Editor.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.Modules;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Editor;

/// <summary>
/// The Editor module's single registration entry point (specs/011-editor-core).
/// </summary>
/// <remarks>
/// The visual editor itself is part of the admin app. This module stores what the editor and,
/// later, the renderer share: each site's design tokens (research ED-14). Everything below is
/// <c>internal</c> apart from this class, the controllers and the shapes they bind.
/// </remarks>
public static class EditorModule
{
    /// <summary>Registers the Editor module: data, capabilities and slices.</summary>
    public static IServiceCollection AddEditorModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Default");

        services.AddDbContext<EditorDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
                // Each module keeps its own migrations history table in its own schema.
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", EditorDbContext.Schema));
        });

        services.AddSingleton<IModuleDatabaseProvider, EditorDatabaseProvider>();

        // 012 research PR-06: the public path reads the published design on a connection
        // PostgreSQL keeps read-only. Never migrated; EditorDbContext owns the tables.
        services.AddDbContext<PublishedDesignReadDbContext>(options => options
            .UseNpgsql(ReadOnlyConnection.From(connectionString))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        services.AddScoped<IPublishedDesignReader, PublishedDesignReader>();

        // What this module lets people do. Identity enforces them; this module only declares them.
        foreach (var capability in EditorCapabilities.All)
        {
            services.AddSingleton(capability);
        }

        services.AddScoped<EditorAccess>();
        // 019 RL-19: token sets are read at the current schema version. A replaceable
        // singleton, so tests can give the chain a step that fails.
        services.AddSingleton(sp => new DesignTokenUpgrades(DesignTokenUpgrades.CreateChain(), sp.GetRequiredService<ILogger<DesignTokenUpgrades>>()));
        services.AddScoped<DesignTokenReader>();
        services.AddScoped<IDesignTokenReader>(sp => sp.GetRequiredService<DesignTokenReader>());

        // Stateless; loads the token schema once.
        services.AddSingleton<TokenSetValidator>();

        // Feature slices. Each is registered here and nowhere else.
        services.AddScoped<IQueryHandler<GetDesignTokensQuery, Result<DesignTokensResponse>>, GetDesignTokensHandler>();
        services.AddScoped<ICommandHandler<SaveDesignTokensCommand, Result<DesignTokensResponse>>, SaveDesignTokensHandler>();
        services.AddScoped<IValidator<SaveDesignTokensCommand>, SaveDesignTokensValidator>();

        // 012 research PR-17: publishing the design.
        services.AddScoped<Infrastructure.DesignPublishing>();
        services.AddScoped<DesignPublicationReads>();
        services.AddScoped<ICommandHandler<PublishDesignCommand, Result<DesignPublicationResponse>>, PublishDesignHandler>();
        services.AddScoped<ICommandHandler<RollbackDesignCommand, Result<DesignPublicationResponse>>, RollbackDesignHandler>();
        services.AddScoped<IQueryHandler<ListDesignVersionsQuery, Result<DesignVersionsResponse>>, ListDesignVersionsHandler>();
        services.AddScoped<IQueryHandler<GetDesignVersionQuery, Result<DesignVersionResponse>>, GetDesignVersionHandler>();
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<DesignPublished>());

        return services;
    }
}

/// <summary>Describes the module's database to the host, for migration and health checks.</summary>
internal sealed class EditorDatabaseProvider : IModuleDatabaseProvider
{
    public ModuleDatabase Describe() =>
        new("Editor", EditorDbContext.Schema, typeof(EditorDbContext));

    public DbContext CreateContext(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Built directly: migration, health and background work run outside any request,
        // where no site is resolved. The empty site context fails closed.
        var options = services.GetRequiredService<DbContextOptions<EditorDbContext>>();
        return new EditorDbContext(options, NoSiteContext.Instance);
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
