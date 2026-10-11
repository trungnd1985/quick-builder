using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Cms.Contracts.Events;
using QuickBuilder.Cms.Features;
using QuickBuilder.Cms.Features.CreateCollection;
using QuickBuilder.Cms.Features.DeleteCollection;
using QuickBuilder.Cms.Features.GetCollection;
using QuickBuilder.Cms.Features.GetSchemaHistory;
using QuickBuilder.Cms.Features.Items;
using QuickBuilder.Cms.Features.Items.CheckSlug;
using QuickBuilder.Cms.Features.Items.CreateItem;
using QuickBuilder.Cms.Features.Items.DeleteItem;
using QuickBuilder.Cms.Features.Items.DuplicateItem;
using QuickBuilder.Cms.Features.Items.GetItem;
using QuickBuilder.Cms.Features.Items.GetItemHistory;
using QuickBuilder.Cms.Features.Items.ListItemReferrers;
using QuickBuilder.Cms.Features.Items.ListItems;
using QuickBuilder.Cms.Features.Items.LookupItems;
using QuickBuilder.Cms.Features.Items.PublishItem;
using QuickBuilder.Cms.Features.Items.ReindexItemsForMedia;
using QuickBuilder.Cms.Features.Items.RollbackItem;
using QuickBuilder.Cms.Features.Items.RunItemSchedule;
using QuickBuilder.Cms.Features.Items.SaveItemDraft;
using QuickBuilder.Cms.Features.Items.ScheduleItem;
using QuickBuilder.Cms.Features.Items.UnpublishItem;
using QuickBuilder.Cms.Features.ListCollections;
using QuickBuilder.Cms.Features.PreviewCollection;
using QuickBuilder.Cms.Features.SaveCollection;
using QuickBuilder.Cms.Infrastructure;
using QuickBuilder.Cms.Infrastructure.Schema;
using QuickBuilder.Media.Contracts.Events;
using QuickBuilder.Shared.Infrastructure.Modules;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Events;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Cms;

/// <summary>
/// The Cms module's single registration entry point (specs/014-cms-collections-schema).
/// </summary>
/// <remarks>
/// Adding the module is one line in the host. Everything below is <c>internal</c> apart from
/// this class, the controllers and the request/response shapes they bind, so the boundary is
/// enforced by the compiler as well as by the architecture tests.
/// </remarks>
public static class CmsModule
{
    /// <summary>Registers the Cms module: data, capabilities, events and slices.</summary>
    public static IServiceCollection AddCmsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Default");

        services.AddDbContext<CmsDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
                // Each module keeps its own migrations history table in its own schema.
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", CmsDbContext.Schema));
        });

        services.AddSingleton<IModuleDatabaseProvider, CmsDatabaseProvider>();

        // What this module lets people do. Identity enforces them; this module only declares them.
        foreach (var capability in CmsCapabilities.All)
        {
            services.AddSingleton(capability);
        }

        // Research CS-14: published here for #15 and #16. The dispatcher needs each type to
        // read a stored payload back.
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<CollectionCreated>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<CollectionChanged>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<CollectionDeleted>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<ItemCreated>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<ItemDraftSaved>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<ItemPublished>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<ItemUnpublished>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<ItemDeleted>());

        // Shared by the slices: the validators (stateless, load the contract files once), the
        // access helper, the row locks and the migration observer (a no-op outside tests).
        services.AddSingleton<CollectionSchemaValidator>();
        services.AddSingleton<ItemValueValidator>();
        services.AddScoped<CmsAccess>();
        services.AddScoped<CollectionLocks>();
        services.AddSingleton<ISchemaMigrationObserver, NoSchemaMigrationObserver>();

        // The save engine (research CS-06 … CS-10): reads, evaluation, impact, migration, the gate.
        services.AddScoped<CollectionReads>();
        services.AddScoped<MediaKinds>();
        services.AddSingleton<ValueMigration>();
        services.AddScoped<ImpactCalculator>();
        services.AddScoped<ItemMigrator>();
        services.AddScoped<ProposalEvaluator>();
        services.AddScoped<CollectionWriteGate>();

        // Items (015 research IT-05, IT-06, IT-11): validation, the indexer and the one write path.
        services.AddScoped<IItemMediaLookup, ItemMediaLookup>();
        services.AddScoped<IItemReferenceLookup, ItemReferenceLookup>();
        services.AddScoped<ItemValidation>();
        services.AddScoped<ItemIndexer>();
        services.AddScoped<SlugAvailability>();
        services.AddScoped<ItemWrites>();
        services.AddScoped<ItemReads>();
        services.AddScoped<QuickBuilder.Cms.Contracts.Media.IItemMediaUsageReader, ItemMediaUsageReader>();

        // 016 research TB-08: published items for the public path, over a read-only connection,
        // and schemas for other modules' admin-side checks.
        services.AddDbContext<CmsPublishedReadDbContext>(options => options
            .UseNpgsql(ReadOnlyConnection.From(connectionString))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        services.AddScoped<QuickBuilder.Cms.Contracts.Publishing.IPublishedItemReader, PublishedItemReader>();
        services.AddScoped<QuickBuilder.Cms.Contracts.ICollectionSchemaReader, CollectionSchemaReader>();

        // Research IT-11, IT-22: indexes items stored before 015, and any after a future indexer change.
        services.AddSingleton<ItemIndexBackfillService>();
        services.AddHostedService(sp => sp.GetRequiredService<ItemIndexBackfillService>());

        // Feature slices. Each is registered here and nowhere else.
        services.AddScoped<IQueryHandler<ListCollectionsQuery, Result<CollectionListResponse>>, ListCollectionsHandler>();
        services.AddScoped<ICommandHandler<CreateCollectionCommand, Result<CollectionResponse>>, CreateCollectionHandler>();
        services.AddScoped<IQueryHandler<GetCollectionQuery, Result<CollectionResponse>>, GetCollectionHandler>();
        services.AddScoped<ICommandHandler<SaveCollectionCommand, Result<CollectionResponse>>, SaveCollectionHandler>();
        services.AddScoped<IQueryHandler<PreviewCollectionQuery, Result<PreviewResponse>>, PreviewCollectionHandler>();
        services.AddScoped<ICommandHandler<DeleteCollectionCommand, Result>, DeleteCollectionHandler>();
        services.AddScoped<IQueryHandler<GetSchemaHistoryQuery, Result<SchemaHistoryResponse>>, GetSchemaHistoryHandler>();

        // Item slices (015).
        services.AddScoped<ItemListQuery>();
        services.AddScoped<IQueryHandler<ListItemsQuery, Result<ItemListResponse>>, ListItemsHandler>();
        services.AddScoped<ICommandHandler<CreateItemCommand, Result<ItemResponse>>, CreateItemHandler>();
        services.AddScoped<IQueryHandler<GetItemQuery, Result<ItemResponse>>, GetItemHandler>();
        services.AddScoped<ICommandHandler<SaveItemDraftCommand, Result<ItemResponse>>, SaveItemDraftHandler>();
        services.AddScoped<IQueryHandler<CheckSlugQuery, Result<SlugAvailabilityResponse>>, CheckSlugHandler>();
        services.AddScoped<ItemReferrers>();
        services.AddScoped<ICommandHandler<PublishItemCommand, Result<ItemResponse>>, PublishItemHandler>();
        services.AddScoped<ICommandHandler<UnpublishItemCommand, Result<ItemResponse>>, UnpublishItemHandler>();
        services.AddScoped<IQueryHandler<ListItemReferrersQuery, Result<ReferrerListResponse>>, ListItemReferrersHandler>();
        services.AddScoped<ICommandHandler<DeleteItemCommand, Result>, DeleteItemHandler>();
        services.AddScoped<ICommandHandler<DuplicateItemCommand, Result<ItemResponse>>, DuplicateItemHandler>();
        services.AddScoped<IQueryHandler<LookupItemsQuery, Result<ItemLookupResponse>>, LookupItemsHandler>();
        services.AddScoped<IQueryHandler<GetItemHistoryQuery, Result<ItemHistoryResponse>>, GetItemHistoryHandler>();
        services.AddScoped<ICommandHandler<RollbackItemCommand, Result<ItemResponse>>, RollbackItemHandler>();
        services.AddScoped<ICommandHandler<CopyVersionToDraftCommand, Result<ItemResponse>>, CopyVersionToDraftHandler>();
        services.AddScoped<ICommandHandler<ScheduleItemCommand, Result<ItemResponse>>, ScheduleItemHandler>();
        services.AddScoped<ICommandHandler<CancelItemScheduleCommand, Result<ItemResponse>>, CancelItemScheduleHandler>();

        // Research IT-09: due schedules arrive as delayed messages in the Cms outbox.
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<ItemScheduleDue>());
        services.AddScoped<IIntegrationEventHandler<ItemScheduleDue>, RunItemScheduleHandler>();

        // Research IT-11: items using a deleted or replaced media file are checked again.
        services.AddScoped<IIntegrationEventHandler<FileDeleted>, ReindexItemsForMediaHandler>();
        services.AddScoped<IIntegrationEventHandler<FileChanged>, ReindexItemsForMediaHandler>();

        return services;
    }
}

/// <summary>Describes the module's database to the host, for migration and health checks.</summary>
internal sealed class CmsDatabaseProvider : IModuleDatabaseProvider
{
    public ModuleDatabase Describe() =>
        new("Cms", CmsDbContext.Schema, typeof(CmsDbContext));

    public DbContext CreateContext(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Built directly: migration, health and background work run outside any request,
        // where no site is resolved. The empty site context fails closed.
        var options = services.GetRequiredService<DbContextOptions<CmsDbContext>>();
        return new CmsDbContext(options, NoSiteContext.Instance);
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
