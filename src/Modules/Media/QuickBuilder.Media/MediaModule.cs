using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using QuickBuilder.Media.Contracts;
using QuickBuilder.Media.Contracts.Events;
using QuickBuilder.Media.Features.DeleteFile;
using QuickBuilder.Media.Features.Folders;
using QuickBuilder.Media.Features.GetFile;
using QuickBuilder.Media.Features.GetFileContent;
using QuickBuilder.Media.Features.GetFileUsage;
using QuickBuilder.Media.Features.GetFileVariant;
using QuickBuilder.Media.Features.ListFiles;
using QuickBuilder.Media.Features.MoveFiles;
using QuickBuilder.Media.Features.ReplaceFile;
using QuickBuilder.Media.Features.ServePublicFile;
using QuickBuilder.Media.Features.StorageCheck;
using QuickBuilder.Media.Features.Trash;
using QuickBuilder.Media.Features.UpdateFile;
using QuickBuilder.Media.Features.UploadFile;
using QuickBuilder.Media.Imaging;
using QuickBuilder.Media.Infrastructure;
using QuickBuilder.Media.Variants;
using QuickBuilder.Shared.Infrastructure.Commands;
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

            // 020 research ST-07: content writes pause while an import applies to the site.
            options.AddInterceptors(provider.GetRequiredService<QuickBuilder.Shared.Infrastructure.Data.SiteWriteGateInterceptor>());
        });

        services.AddSingleton<IModuleDatabaseProvider, MediaDatabaseProvider>();

        // 012 research PR-06: the public path resolves images on a connection PostgreSQL keeps
        // read-only. Never migrated; MediaDbContext owns the table.
        services.AddDbContext<PublicMediaReadDbContext>(options => options
            .UseNpgsql(ReadOnlyConnection.From(connectionString))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        services.AddScoped<IPublicMediaReader, PublicMediaReader>();
        // 022 research PV-11: media for previews, served by /_media's own handler.
        services.AddScoped<IMediaContent, MediaContentReader>();

        // 014 research CS-12: content types for other modules' admin work (Cms schema changes).
        services.AddScoped<IMediaContentTypes, MediaContentTypes>();

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
        services.AddScoped<MediaUsage>();
        services.AddSingleton<UploadGate>();

        // 013: the shared upload and replace pipeline, folders, and eager thumbnails (MV-13).
        services.AddScoped<UploadPipeline>();
        services.AddScoped<FolderWrites>();
        services.AddSingleton<ThumbnailQueue>();
        services.AddSingleton<ImagingRuntime>();
        services.AddScoped<IImageMetadataProbe, NetVipsImageMetadataProbe>();

        // 013 MV-12, MV-13: variants produced once, stored, and thumbnails right after upload.
        services.AddSingleton<IVariantEncoder, VariantEncoder>();
        services.AddSingleton<VariantClaims>();
        services.AddSingleton<VariantService>();
        services.AddScoped<VariantResponder>();
        services.AddSingleton<ThumbnailWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<ThumbnailWorker>());

        // 013 MV-10: dimensions of images uploaded before 013.
        services.AddSingleton<ImageMetadataBackfillService>();
        services.AddHostedService(sp => sp.GetRequiredService<ImageMetadataBackfillService>());

        // FR-012: bytes left without a library entry are removed after the grace period.
        services.AddSingleton<MediaOrphanSweepService>();
        services.AddHostedService(sp => sp.GetRequiredService<MediaOrphanSweepService>());

        // 013 MV-26: accent-folded search text for files uploaded before 013.
        services.AddSingleton<SearchTextBackfillService>();
        services.AddHostedService(sp => sp.GetRequiredService<SearchTextBackfillService>());

        // 013 MV-21: the storage consistency check, after start-up, from the admin and the CLI.
        services.AddSingleton<StorageCheckRunner>();
        services.AddSingleton<StorageConsistencyCheckService>();
        services.AddHostedService(sp => sp.GetRequiredService<StorageConsistencyCheckService>());
        services.AddSingleton<IHostCommand, MediaCommands>();

        // Feature slices. Each is registered here and nowhere else.
        services.AddScoped<ICommandHandler<UploadFileCommand, Result<MediaFileDto>>, UploadFileHandler>();
        services.AddScoped<IQueryHandler<ListFilesQuery, Result<MediaFilePageDto>>, ListFilesHandler>();
        services.AddScoped<IQueryHandler<GetFileContentQuery, Result<MediaContent>>, GetFileContentHandler>();
        services.AddScoped<IQueryHandler<ServePublicFileQuery, Result<PublicMediaContent>>, ServePublicFileHandler>();
        services.AddScoped<ICommandHandler<DeleteFileCommand, DeleteFileOutcome>, DeleteFileHandler>();

        // 021: the media trash.
        services.AddScoped<MediaTrash>();
        services.AddScoped<ListTrashHandler>();
        services.AddScoped<IQueryHandler<ListTrashQuery, Result<TrashListResponse>>>(sp => sp.GetRequiredService<ListTrashHandler>());
        services.AddScoped<IQueryHandler<GetTrashEntryQuery, Result<TrashEntryResponse>>>(sp => sp.GetRequiredService<ListTrashHandler>());
        services.AddScoped<RestoreFileHandler>();
        services.AddScoped<IQueryHandler<FileRestorePlanQuery, Result<RestorePlanResponse>>>(sp => sp.GetRequiredService<RestoreFileHandler>());
        services.AddScoped<ICommandHandler<RestoreFileCommand, Result<RestoreResultResponse>>>(sp => sp.GetRequiredService<RestoreFileHandler>());
        services.AddScoped<ICommandHandler<BulkRestoreCommand, Result<BulkOutcomeResponse>>, BulkRestoreHandler>();
        services.AddScoped<MediaTrashPurge>();
        services.AddScoped<PurgeHandler>();
        services.AddScoped<ICommandHandler<PurgeFileCommand, Result>>(sp => sp.GetRequiredService<PurgeHandler>());
        services.AddScoped<ICommandHandler<PurgeFilesCommand, Result<BulkOutcomeResponse>>>(sp => sp.GetRequiredService<PurgeHandler>());
        services.AddScoped<ICommandHandler<EmptyFileTrashCommand, Result<EmptyTrashResponse>>>(sp => sp.GetRequiredService<PurgeHandler>());

        // 021 TR-13: the hourly purge of trashed files past the retention period.
        services.AddSingleton<MediaTrashPurgeService>();
        services.AddHostedService(sp => sp.GetRequiredService<MediaTrashPurgeService>());

        // 013 (contracts/media-api.yaml).
        services.AddScoped<IQueryHandler<GetFileQuery, Result<MediaFileDto>>, GetFileHandler>();
        services.AddScoped<ICommandHandler<UpdateFileCommand, Result<MediaFileDto>>, UpdateFileHandler>();
        services.AddScoped<ICommandHandler<MoveFilesCommand, Result>, MoveFilesHandler>();
        services.AddScoped<IQueryHandler<ListFoldersQuery, Result<MediaFolderTreeDto>>, ListFoldersHandler>();
        services.AddScoped<ICommandHandler<CreateFolderCommand, Result<MediaFolderDto>>, CreateFolderHandler>();
        services.AddScoped<ICommandHandler<UpdateFolderCommand, Result<MediaFolderDto>>, UpdateFolderHandler>();
        services.AddScoped<ICommandHandler<DeleteFolderCommand, Result>, DeleteFolderHandler>();
        services.AddScoped<IQueryHandler<GetFileVariantQuery, Result<AdminVariantContent>>, GetFileVariantHandler>();
        services.AddScoped<IQueryHandler<GetFileUsageQuery, Result<MediaUsageDto>>, GetFileUsageHandler>();
        services.AddScoped<ICommandHandler<ReplaceFileCommand, Result<MediaFileDto>>, ReplaceFileHandler>();
        services.AddScoped<IQueryHandler<GetStorageCheckQuery, Result<StorageStatusDto>>, GetStorageCheckHandler>();
        services.AddScoped<ICommandHandler<StartStorageCheckCommand, Result<StorageCheckDto>>, StartStorageCheckHandler>();
        // 012: pages that show a deleted file refresh (data-model §3.2).
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<FileDeleted>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<FileRestored>());
        // 013 MV-17: replaced files, recorded dimensions and failed variants refresh them too.
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<FileChanged>());

        // 020: this module's share of a site archive (contracts/archive-parts.md).
        services.AddScoped<QuickBuilder.Sites.Contracts.Transfer.ISiteArchivePart, QuickBuilder.Media.Features.Transfer.MediaArchivePart>();

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
