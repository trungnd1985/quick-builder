using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Cms.Contracts.Events;
using QuickBuilder.Editor.Contracts.Events;
using QuickBuilder.Media.Contracts.Events;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Contracts.Events;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Pages.Contracts.Redirects;
using QuickBuilder.Pages.Features;
using QuickBuilder.Pages.Features.CopyComponent;
using QuickBuilder.Pages.Features.CopyPage;
using QuickBuilder.Pages.Features.CreateComponent;
using QuickBuilder.Pages.Features.CreatePage;
using QuickBuilder.Pages.Features.DeleteComponent;
using QuickBuilder.Pages.Features.DeletePage;
using QuickBuilder.Pages.Features.GetComponent;
using QuickBuilder.Pages.Features.GetComponentDraft;
using QuickBuilder.Pages.Features.GetPage;
using QuickBuilder.Pages.Features.GetPageDraft;
using QuickBuilder.Pages.Features.GetPageTree;
using QuickBuilder.Pages.Features.GetPublishLog;
using QuickBuilder.Pages.Features.GetTokenUsage;
using QuickBuilder.Pages.Features.ListComponents;
using QuickBuilder.Pages.Features.ListComponentUsages;
using QuickBuilder.Pages.Features.MakeHomePage;
using QuickBuilder.Pages.Features.Menus;
using QuickBuilder.Pages.Features.MovePage;
using QuickBuilder.Pages.Features.Publish;
using QuickBuilder.Pages.Features.PublishingHistory;
using QuickBuilder.Pages.Features.RecordDesignPublish;
using QuickBuilder.Pages.Features.Redirects;
using QuickBuilder.Pages.Features.RefreshPagesListingItems;
using QuickBuilder.Pages.Features.RefreshPagesShowingMedia;
using QuickBuilder.Pages.Features.RefreshPagesShowingMenuItems;
using QuickBuilder.Pages.Features.RefreshPagesUsingCollection;
using QuickBuilder.Pages.Features.RenameComponent;
using QuickBuilder.Pages.Features.Rollback;
using QuickBuilder.Pages.Features.RunDueSchedule;
using QuickBuilder.Pages.Features.SaveComponentDraft;
using QuickBuilder.Pages.Features.SavePageDraft;
using QuickBuilder.Pages.Features.Schedules;
using QuickBuilder.Pages.Features.SetPageTemplate;
using QuickBuilder.Pages.Features.Trash;
using QuickBuilder.Pages.Features.Unpublish;
using QuickBuilder.Pages.Features.UpdatePage;
using QuickBuilder.Pages.Infrastructure;
using QuickBuilder.Pages.Infrastructure.Schema;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.Modules;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Events;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Pages;

/// <summary>
/// The Pages module's single registration entry point (specs/010-page-tree-components).
/// </summary>
/// <remarks>
/// Adding the module is one line in the host. Everything below is <c>internal</c> apart from
/// this class, the controllers and the request/response shapes they bind, so the boundary is
/// enforced by the compiler as well as by the architecture tests.
/// </remarks>
public static class PagesModule
{
    /// <summary>Registers the Pages module: data, capabilities, events and slices.</summary>
    public static IServiceCollection AddPagesModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Default");

        services.AddDbContext<PagesDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
                // Each module keeps its own migrations history table in its own schema.
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", PagesDbContext.Schema));

            // 020 research ST-07: content writes pause while an import applies to the site.
            options.AddInterceptors(provider.GetRequiredService<QuickBuilder.Shared.Infrastructure.Data.SiteWriteGateInterceptor>());
        });

        services.AddSingleton<IModuleDatabaseProvider, PagesDatabaseProvider>();

        // 012 research PR-06: the public path reads published content through its own context,
        // on a connection PostgreSQL keeps read-only. Never migrated; PagesDbContext owns the tables.
        services.AddDbContext<PublishedReadDbContext>(options => options
            .UseNpgsql(ReadOnlyConnection.From(connectionString))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        // 019 RL-19: every stored document is read at the current version before it leaves
        // the module. Replaceable singletons, so tests can give a chain a step that fails.
        services.AddSingleton(Infrastructure.Schema.PageSchemaUpgrades.Create());
        services.AddSingleton(Infrastructure.Schema.MenuSchemaUpgrades.Create());
        services.AddSingleton<Infrastructure.Schema.StoredDocuments>();
        services.AddScoped<IPublishedContentReader, PublishedContentReader>();

        // 017 research SE-10, SE-12: redirects on the public path, and their use, counted in
        // memory and saved in the background.
        services.AddScoped<IRedirectReader, RedirectReader>();
        services.AddScoped<ISitemapReader, SitemapReader>();
        services.AddSingleton<RedirectHitCounter>();
        services.AddSingleton<IRedirectHits>(sp => sp.GetRequiredService<RedirectHitCounter>());
        services.AddSingleton<RedirectUsageFlusher>();
        services.AddHostedService(sp => sp.GetRequiredService<RedirectUsageFlusher>());

        // 021 TR-13: the hourly purge of trashed pages and components past the retention period.
        services.AddSingleton<PagesTrashPurgeService>();
        services.AddHostedService(sp => sp.GetRequiredService<PagesTrashPurgeService>());

        // 013 research MV-05: where media files are used, for the Media module.
        services.AddScoped<QuickBuilder.Pages.Contracts.Media.IMediaUsageReader, MediaUsageReader>();

        // 016 research TB-12: which page is a collection's template, for Cms.
        services.AddScoped<ITemplateReader, TemplateReader>();

        // What this module lets people do. Identity enforces them; this module only declares them.
        foreach (var capability in PagesCapabilities.All)
        {
            services.AddSingleton(capability);
        }

        // Research P-14: published here, consumed by later features (#12, #17). The dispatcher
        // needs each type to read a stored payload back.
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<PageCreated>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<PageDeleted>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<PageRestored>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<PageAddressesChanged>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<HomePageChanged>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<ComponentCreated>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<ComponentDeleted>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<ComponentRestored>());

        // 012 research PR-19: publishing, for later features (#17) to react to.
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<PagePublished>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<PageUnpublished>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<ComponentPublished>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<ComponentUnpublished>());

        // Shared by the slices: the document validator (stateless, loads the schema once),
        // and the read and write paths every slice goes through.
        services.AddSingleton<IPageDocumentValidator, PageDocumentValidator>();
        services.AddSingleton<Infrastructure.Schema.MenuDocumentValidator>();
        services.AddScoped<PagesAccess>();
        services.AddScoped<PageReads>();
        services.AddScoped<PageTreeWrites>();
        services.AddScoped<DraftWrites>();
        services.AddScoped<CollectionUseRows>();
        services.AddScoped<PageParamRules>();
        services.AddScoped<AddressWarnings>();

        // 017: redirects (research SE-09 to SE-13a).
        services.AddScoped<SitemapGeneration>();
        services.AddScoped<RedirectWrites>();
        services.AddScoped<RedirectOffers>();
        services.AddScoped<RedirectViews>();
        services.AddScoped<ManageRedirectHandlers>();
        services.AddScoped<ICommandHandler<CreateRedirectCommand, Result<RedirectResponse>>>(sp => sp.GetRequiredService<ManageRedirectHandlers>());
        services.AddScoped<ICommandHandler<UpdateRedirectCommand, Result<RedirectResponse>>>(sp => sp.GetRequiredService<ManageRedirectHandlers>());
        services.AddScoped<ICommandHandler<DeleteRedirectCommand, Result>>(sp => sp.GetRequiredService<ManageRedirectHandlers>());
        services.AddScoped<IQueryHandler<GetRedirectQuery, Result<RedirectResponse>>>(sp => sp.GetRequiredService<ManageRedirectHandlers>());
        services.AddScoped<IQueryHandler<ListRedirectsQuery, Result<RedirectListResponse>>, ListRedirectsHandler>();
        services.AddScoped<RedirectFileHandlers>();
        services.AddScoped<ICommandHandler<ImportRedirectsCommand, Result<ImportRedirectsResponse>>>(sp => sp.GetRequiredService<RedirectFileHandlers>());
        services.AddScoped<IQueryHandler<ExportRedirectsQuery, Result<string>>>(sp => sp.GetRequiredService<RedirectFileHandlers>());
        services.AddScoped<ICollectionUsageReader, CollectionUsageReader>();
        services.AddScoped<ComponentReads>();

        // 012: publishing — the reads every response shares, the dependency bump and the writes.
        services.AddScoped<PublicationReads>();
        services.AddScoped<GenerationBump>();
        services.AddScoped<PublishingWrites>();

        // Feature slices. Each is registered here and nowhere else.
        services.AddScoped<IQueryHandler<GetPageTreeQuery, Result<PageTreeResponse>>, GetPageTreeHandler>();
        services.AddScoped<ICommandHandler<CreatePageCommand, Result<PageDetailResponse>>, CreatePageHandler>();
        services.AddScoped<IValidator<CreatePageCommand>, CreatePageValidator>();
        services.AddScoped<IQueryHandler<GetPageQuery, Result<PageDetailResponse>>, GetPageHandler>();
        services.AddScoped<ICommandHandler<UpdatePageCommand, Result<PageDetailResponse>>, UpdatePageHandler>();
        services.AddScoped<IValidator<UpdatePageCommand>, UpdatePageValidator>();
        services.AddScoped<ICommandHandler<MovePageCommand, Result<PageTreeResponse>>, MovePageHandler>();
        services.AddScoped<ICommandHandler<MakeHomePageCommand, Result<PageTreeResponse>>, MakeHomePageHandler>();

        // 016 research TB-02: a page as a collection's template.
        services.AddScoped<TemplateImpact>();
        services.AddScoped<ICommandHandler<SetPageTemplateCommand, Result<PageWithWarningsResponse>>, SetPageTemplateHandler>();
        services.AddScoped<IQueryHandler<GetTemplateImpactQuery, Result<TemplateImpactResponse>>, GetTemplateImpactHandler>();
        services.AddScoped<ICommandHandler<DeletePageCommand, Result<QuickBuilder.Pages.Features.Trash.TrashReceiptResponse>>, DeletePageHandler>();

        services.AddScoped<IQueryHandler<GetPageDraftQuery, Result<DraftResponse>>, GetPageDraftHandler>();
        services.AddScoped<ICommandHandler<SavePageDraftCommand, Result<DraftResponse>>, SavePageDraftHandler>();
        services.AddScoped<IValidator<SavePageDraftCommand>, SavePageDraftValidator>();

        services.AddScoped<IQueryHandler<ListComponentsQuery, Result<ComponentListResponse>>, ListComponentsHandler>();
        services.AddScoped<ICommandHandler<CreateComponentCommand, Result<ComponentResponse>>, CreateComponentHandler>();
        // 023 research CE-11: "save my version as a copy".
        services.AddScoped<ICommandHandler<CopyPageCommand, Result<PageDetailResponse>>, CopyPageHandler>();
        services.AddScoped<ICommandHandler<CopyComponentCommand, Result<ComponentResponse>>, CopyComponentHandler>();
        services.AddScoped<IValidator<CreateComponentCommand>, CreateComponentValidator>();
        services.AddScoped<IQueryHandler<GetComponentQuery, Result<ComponentResponse>>, GetComponentHandler>();
        services.AddScoped<ICommandHandler<RenameComponentCommand, Result<ComponentResponse>>, RenameComponentHandler>();
        services.AddScoped<IValidator<RenameComponentCommand>, RenameComponentValidator>();
        services.AddScoped<ICommandHandler<DeleteComponentCommand, Result<QuickBuilder.Pages.Features.Trash.TrashReceiptResponse>>, DeleteComponentHandler>();

        // 021: the trash of pages and components.
        services.AddScoped<PagesTrash>();
        services.AddScoped<ListTrashHandler>();
        services.AddScoped<IQueryHandler<ListTrashQuery, Result<TrashListResponse>>>(sp => sp.GetRequiredService<ListTrashHandler>());
        services.AddScoped<IQueryHandler<GetTrashEntryQuery, Result<TrashEntryDetailResponse>>>(sp => sp.GetRequiredService<ListTrashHandler>());
        services.AddScoped<RestorePageHandler>();
        services.AddScoped<IQueryHandler<PageRestorePlanQuery, Result<RestorePlanResponse>>>(sp => sp.GetRequiredService<RestorePageHandler>());
        services.AddScoped<ICommandHandler<RestorePageCommand, Result<RestoreResultResponse>>>(sp => sp.GetRequiredService<RestorePageHandler>());
        services.AddScoped<RestoreComponentHandler>();
        services.AddScoped<IQueryHandler<ComponentRestorePlanQuery, Result<RestorePlanResponse>>>(sp => sp.GetRequiredService<RestoreComponentHandler>());
        services.AddScoped<ICommandHandler<RestoreComponentCommand, Result<RestoreResultResponse>>>(sp => sp.GetRequiredService<RestoreComponentHandler>());
        services.AddScoped<ICommandHandler<BulkRestoreCommand, Result<BulkOutcomeResponse>>, BulkRestoreHandler>();
        services.AddScoped<PagesTrashPurge>();
        services.AddScoped<PurgeHandler>();
        services.AddScoped<ICommandHandler<PurgeEntryCommand, Result>>(sp => sp.GetRequiredService<PurgeHandler>());
        services.AddScoped<ICommandHandler<PurgeManyCommand, Result<BulkOutcomeResponse>>>(sp => sp.GetRequiredService<PurgeHandler>());
        services.AddScoped<ICommandHandler<EmptyTrashCommand, Result<EmptyTrashResponse>>>(sp => sp.GetRequiredService<PurgeHandler>());
        services.AddScoped<IQueryHandler<ListComponentUsagesQuery, Result<IReadOnlyList<PageReferenceResponse>>>, ListComponentUsagesHandler>();
        services.AddScoped<IQueryHandler<GetComponentDraftQuery, Result<DraftResponse>>, GetComponentDraftHandler>();
        services.AddScoped<ICommandHandler<SaveComponentDraftCommand, Result<DraftResponse>>, SaveComponentDraftHandler>();
        services.AddScoped<IValidator<SaveComponentDraftCommand>, SaveComponentDraftValidator>();

        // 012: publishing slices.
        services.AddScoped<ICommandHandler<PublishCommand, Result<PublicationResponse>>, PublishHandler>();
        services.AddScoped<ICommandHandler<UnpublishCommand, Result<PublicationResponse>>, UnpublishHandler>();
        services.AddScoped<ICommandHandler<RollbackCommand, Result<PublicationResponse>>, RollbackHandler>();
        services.AddScoped<LogEntryReads>();
        services.AddScoped<IQueryHandler<GetHistoryQuery, Result<SubjectHistoryResponse>>, GetHistoryHandler>();
        services.AddScoped<IQueryHandler<GetVersionQuery, Result<VersionContentResponse>>, GetVersionHandler>();
        services.AddScoped<ICommandHandler<CopyVersionToDraftCommand, Result<DraftResponse>>, CopyVersionToDraftHandler>();
        services.AddScoped<IQueryHandler<GetPublishLogQuery, Result<PublishLogResponse>>, GetPublishLogHandler>();

        // 012 research PR-14: schedules, run through delayed messages in the Pages outbox.
        services.AddScoped<ScheduleWrites>();
        services.AddScoped<ICommandHandler<SetScheduleCommand, Result<ScheduleResponse>>, SetScheduleHandler>();
        services.AddScoped<ICommandHandler<CancelScheduleCommand, Result>, CancelScheduleHandler>();
        services.AddScoped<IIntegrationEventHandler<PublishScheduleDue>, RunDueScheduleHandler>();
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<PublishScheduleDue>());

        // 012 research PR-16: design publishes, recorded in the site's publish log.
        services.AddScoped<IIntegrationEventHandler<DesignPublished>, RecordDesignPublishHandler>();

        // 012 data-model §3.2: pages that showed a deleted media file refresh.
        services.AddScoped<IIntegrationEventHandler<FileDeleted>, RefreshPagesShowingMediaHandler>();
        // 013 MV-17: a replaced file, recorded dimensions or withdrawn variants refresh them too.
        services.AddScoped<IIntegrationEventHandler<FileChanged>, RefreshPagesShowingMediaHandler>();
        services.AddScoped<IIntegrationEventHandler<FileRestored>, RefreshPagesShowingMediaHandler>();

        // 016 research TB-10: pages listing or showing a collection refresh with its items and fields.
        services.AddScoped<IIntegrationEventHandler<ItemPublished>, RefreshPagesListingItemsHandler>();
        services.AddScoped<IIntegrationEventHandler<ItemUnpublished>, RefreshPagesListingItemsHandler>();
        services.AddScoped<IIntegrationEventHandler<ItemDeleted>, RefreshPagesListingItemsHandler>();

        // 024 NV-07: an item a live menu points at refreshes the pages showing the menu.
        services.AddScoped<IIntegrationEventHandler<ItemPublished>, RefreshPagesShowingMenuItemsHandler>();
        services.AddScoped<IIntegrationEventHandler<ItemUnpublished>, RefreshPagesShowingMenuItemsHandler>();
        services.AddScoped<IIntegrationEventHandler<ItemDeleted>, RefreshPagesShowingMenuItemsHandler>();

        services.AddScoped<IIntegrationEventHandler<ItemRedirectRequested>, Features.CreateItemRedirect.CreateItemRedirectHandler>();
        services.AddScoped<IIntegrationEventHandler<CollectionChanged>, RefreshPagesUsingCollectionHandler>();
        services.AddScoped<IIntegrationEventHandler<CollectionDeleted>, RefreshPagesUsingCollectionHandler>();

        // 024: menus, their drafts and their reads.
        services.AddScoped<MenuTargetStates>();
        services.AddScoped<MenuReads>();
        services.AddScoped<MenuWrites>();
        services.AddScoped<IQueryHandler<ListMenusQuery, Result<MenuListResponse>>, ListMenusHandler>();
        services.AddScoped<IQueryHandler<GetMenuQuery, Result<MenuDetailResponse>>, GetMenuHandler>();
        services.AddScoped<ICommandHandler<CreateMenuCommand, Result<MenuDetailResponse>>, CreateMenuHandler>();
        services.AddScoped<IValidator<CreateMenuCommand>, CreateMenuValidator>();
        services.AddScoped<ICommandHandler<RenameMenuCommand, Result<MenuDetailResponse>>, RenameMenuHandler>();
        services.AddScoped<IValidator<RenameMenuCommand>, RenameMenuValidator>();
        services.AddScoped<ICommandHandler<SaveMenuDraftCommand, Result<MenuDetailResponse>>, SaveMenuDraftHandler>();
        services.AddScoped<IValidator<SaveMenuDraftCommand>, SaveMenuDraftValidator>();
        services.AddScoped<ICommandHandler<DeleteMenuCommand, Result>, DeleteMenuHandler>();
        services.AddScoped<IQueryHandler<PreviewMenuQuery, Result<MenuPreviewResponse>>, PreviewMenuHandler>();
        services.AddScoped<IQueryHandler<MenuUsagesQuery, Result<MenuUsageListResponse>>, MenuUsagesHandler>();
        services.AddScoped<IQueryHandler<MenuReferencesQuery, Result<MenuReferenceListResponse>>, MenuReferencesHandler>();

        // 011 research ED-16: the token usage count the admin asks before deleting a token.
        services.AddScoped<IQueryHandler<GetTokenUsageQuery, Result<TokenUsageResponse>>, GetTokenUsageHandler>();

        // 020: this module's share of a site archive (contracts/archive-parts.md).
        services.AddScoped<QuickBuilder.Sites.Contracts.Transfer.ISiteArchivePart, QuickBuilder.Pages.Features.Transfer.PagesArchivePart>();

        return services;
    }
}

/// <summary>Describes the module's database to the host, for migration and health checks.</summary>
internal sealed class PagesDatabaseProvider : IModuleDatabaseProvider
{
    public ModuleDatabase Describe() =>
        new("Pages", PagesDbContext.Schema, typeof(PagesDbContext));

    public DbContext CreateContext(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Built directly: migration, health and background work run outside any request,
        // where no site is resolved. The empty site context fails closed.
        var options = services.GetRequiredService<DbContextOptions<PagesDbContext>>();
        return new PagesDbContext(options, NoSiteContext.Instance);
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
