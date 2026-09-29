using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Media.Contracts.Events;
using QuickBuilder.Media.Features.DeleteFile;
using QuickBuilder.Media.Features.GetFileContent;
using QuickBuilder.Media.Features.ListFiles;
using QuickBuilder.Media.Features.ServePublicFile;
using QuickBuilder.Media.Features.UploadFile;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.Modules;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Media;

/// <summary>
/// The Media module's single registration entry point (specs/003-media-storage).
/// </summary>
/// <remarks>
/// Adding the module is one line in the host. Everything below is <c>internal</c> apart from
/// this class and the controllers, so the boundary is enforced by the compiler as well as by
/// the architecture tests. Bytes are reached only through <c>IFileStorage</c>, registered by
/// the host with <c>AddFileStorage</c>.
/// </remarks>
public static class MediaModule
{
    /// <summary>Registers the Media module: data, options, capabilities, slices and background work.</summary>
    public static IServiceCollection AddMediaModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Default");

        services.AddDbContext<MediaDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
                // Each module keeps its own migrations history table in its own schema.
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MediaDbContext.Schema));
        });

        services.AddSingleton<IModuleDatabaseProvider, MediaDatabaseProvider>();

        // 012 research PR-06: the public path resolves images on a connection PostgreSQL keeps
        // read-only. Never migrated; MediaDbContext owns the table.
        services.AddDbContext<PublicMediaReadDbContext>(options => options
            .UseNpgsql(ReadOnlyConnection.From(connectionString))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        services.AddScoped<IPublicMediaReader, PublicMediaReader>();

        services
            .AddOptions<MediaOptions>()
            .Bind(configuration.GetSection(MediaOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IPostConfigureOptions<MediaOptions>, MediaOptionsDefaults>();
        services.AddSingleton<IValidateOptions<MediaOptions>, MediaOptionsValidator>();

        // What this module lets people do. Identity enforces them; this module only declares them.
        foreach (var capability in MediaCapabilities.All)
        {
            services.AddSingleton(capability);
        }

        services.AddScoped<MediaFileMapper>();
        services.AddSingleton<UploadGate>();

        // FR-012: bytes left without a library entry are removed after the grace period.
        services.AddSingleton<MediaOrphanSweepService>();
        services.AddHostedService(sp => sp.GetRequiredService<MediaOrphanSweepService>());

        // Feature slices. Each is registered here and nowhere else.
        services.AddScoped<ICommandHandler<UploadFileCommand, Result<MediaFileDto>>, UploadFileHandler>();
        services.AddScoped<IQueryHandler<ListFilesQuery, Result<MediaFilePageDto>>, ListFilesHandler>();
        services.AddScoped<IQueryHandler<GetFileContentQuery, Result<MediaContent>>, GetFileContentHandler>();
        services.AddScoped<IQueryHandler<ServePublicFileQuery, Result<MediaContent>>, ServePublicFileHandler>();
        services.AddScoped<ICommandHandler<DeleteFileCommand, Result>, DeleteFileHandler>();
        // 012: pages that show a deleted file refresh (data-model §3.2).
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<FileDeleted>());

        return services;
    }
}

/// <summary>Describes the module's database to the host, for migration and health checks.</summary>
internal sealed class MediaDatabaseProvider : IModuleDatabaseProvider
{
    public ModuleDatabase Describe() =>
        new("Media", MediaDbContext.Schema, typeof(MediaDbContext));

    public DbContext CreateContext(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Built directly: migration, health and background sweeps run outside any request,
        // where no site is resolved. The empty site context fails closed.
        var options = services.GetRequiredService<DbContextOptions<MediaDbContext>>();
        return new MediaDbContext(options, NoSiteContext.Instance);
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
