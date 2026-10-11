using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Editor.Contracts.Events;
using QuickBuilder.Media.Contracts.Events;
using QuickBuilder.Pages.Contracts;
using QuickBuilder.Pages.Contracts.Events;
using QuickBuilder.Pages.Contracts.Publishing;
using QuickBuilder.Pages.Features;
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
using QuickBuilder.Pages.Features.SetPageTemplate;
using QuickBuilder.Pages.Features.MovePage;
using QuickBuilder.Pages.Features.Publish;
using QuickBuilder.Pages.Features.PublishingHistory;
using QuickBuilder.Pages.Features.RecordDesignPublish;
using QuickBuilder.Pages.Features.RefreshPagesShowingMedia;
using QuickBuilder.Pages.Features.RenameComponent;
using QuickBuilder.Pages.Features.Rollback;
using QuickBuilder.Pages.Features.RunDueSchedule;
using QuickBuilder.Pages.Features.SaveComponentDraft;
using QuickBuilder.Pages.Features.SavePageDraft;
using QuickBuilder.Pages.Features.Schedules;
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
        });

        services.AddSingleton<IModuleDatabaseProvider, PagesDatabaseProvider>();

        // 012 research PR-06: the public path reads published content through its own context,
        // on a connection PostgreSQL keeps read-only. Never migrated; PagesDbContext owns the tables.
        services.AddDbContext<PublishedReadDbContext>(options => options
            .UseNpgsql(ReadOnlyConnection.From(connectionString))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        services.AddScoped<IPublishedContentReader, PublishedContentReader>();

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
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<PageAddressesChanged>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<HomePageChanged>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<ComponentCreated>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<ComponentDeleted>());

        // 012 research PR-19: publishing, for later features (#17) to react to.
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<PagePublished>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<PageUnpublished>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<ComponentPublished>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<ComponentUnpublished>());

        // Shared by the slices: the document validator (stateless, loads the schema once),
        // and the read and write paths every slice goes through.
        services.AddSingleton<IPageDocumentValidator, PageDocumentValidator>();
        services.AddScoped<PagesAccess>();
        services.AddScoped<PageReads>();
        services.AddScoped<PageTreeWrites>();
        services.AddScoped<DraftWrites>();
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
        services.AddScoped<ICommandHandler<DeletePageCommand, Result>, DeletePageHandler>();

        services.AddScoped<IQueryHandler<GetPageDraftQuery, Result<DraftResponse>>, GetPageDraftHandler>();
        services.AddScoped<ICommandHandler<SavePageDraftCommand, Result<DraftResponse>>, SavePageDraftHandler>();
        services.AddScoped<IValidator<SavePageDraftCommand>, SavePageDraftValidator>();

        services.AddScoped<IQueryHandler<ListComponentsQuery, Result<ComponentListResponse>>, ListComponentsHandler>();
        services.AddScoped<ICommandHandler<CreateComponentCommand, Result<ComponentResponse>>, CreateComponentHandler>();
        services.AddScoped<IValidator<CreateComponentCommand>, CreateComponentValidator>();
        services.AddScoped<IQueryHandler<GetComponentQuery, Result<ComponentResponse>>, GetComponentHandler>();
        services.AddScoped<ICommandHandler<RenameComponentCommand, Result<ComponentResponse>>, RenameComponentHandler>();
        services.AddScoped<IValidator<RenameComponentCommand>, RenameComponentValidator>();
        services.AddScoped<ICommandHandler<DeleteComponentCommand, Result>, DeleteComponentHandler>();
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

        // 011 research ED-16: the token usage count the admin asks before deleting a token.
        services.AddScoped<IQueryHandler<GetTokenUsageQuery, Result<TokenUsageResponse>>, GetTokenUsageHandler>();

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
