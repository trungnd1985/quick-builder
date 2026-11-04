using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuickBuilder.Shared.Infrastructure.Commands;
using QuickBuilder.Shared.Infrastructure.Modules;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Infrastructure.Outbox;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.Events;
using QuickBuilder.Shared.Kernel.Results;
using QuickBuilder.Shared.Kernel.Validation;
using QuickBuilder.Sites.Contracts;
using QuickBuilder.Sites.Contracts.Events;
using QuickBuilder.Sites.Features.AddHostName;
using QuickBuilder.Sites.Features.ChangeAvailability;
using QuickBuilder.Sites.Features.CreateSite;
using QuickBuilder.Sites.Features.GetSite;
using QuickBuilder.Sites.Features.ListLanguages;
using QuickBuilder.Sites.Features.ListSites;
using QuickBuilder.Sites.Features.RemoveHostName;
using QuickBuilder.Sites.Contracts.Transfer;
using QuickBuilder.Sites.Features.Transfer;
using QuickBuilder.Sites.Features.Transfer.Engine;
using QuickBuilder.Sites.Features.UpdateSearchSettings;
using QuickBuilder.Sites.Features.UpdateSearchVisibility;
using QuickBuilder.Sites.Features.UpdateSettings;
using QuickBuilder.Sites.Infrastructure;

namespace QuickBuilder.Sites;

/// <summary>
/// The module's single registration entry point (FR-004).
/// </summary>
/// <remarks>
/// The host calls <see cref="AddSitesModule"/> and nothing else. Adding a module is one
/// line in the host and no edit to any other module — the property SC-009 verifies.
/// <para>
/// Everything below is <c>internal</c> apart from this class and the controllers. That is
/// not style: it is what makes FR-005 enforceable by the compiler rather than only by the
/// architecture tests.
/// </para>
/// </remarks>
public static class SitesModule
{
    /// <summary>Registers the Sites module: data, handlers, validators and controllers.</summary>
    public static IServiceCollection AddSitesModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Default");

        services.AddDbContext<SitesDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
                // Each module keeps its own migrations history table inside its own schema
                // (FR-022), so two modules can never contend over one history table.
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", SitesDbContext.Schema));

            // 020 research ST-07: content writes pause while an import applies to the site.
            options.AddInterceptors(provider.GetRequiredService<QuickBuilder.Shared.Infrastructure.Data.SiteWriteGateInterceptor>());
        });

        // Lets the host migrate and health-check this module without knowing anything
        // about its internals.
        services.AddSingleton<IModuleDatabaseProvider, SitesDatabaseProvider>();

        // Hosting settings this module reads (base path, admin host names), taken from the
        // final configuration when first needed rather than at registration.
        services.AddSingleton(sp => SitesOptions.From(sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton<SiteDtoMapper>();

        // 020: site export and import settings, validated at startup naming the key.
        services
            .AddOptions<SiteTransferOptions>()
            .Bind(configuration.GetSection(SiteTransferOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SiteTransferOptions>, SiteTransferOptionsValidator>();

        // The module's public read surface. Scoped: it remembers the sites loaded during
        // one request and nothing longer (research S-01, S-02). The write paths use the
        // concrete type to forget a site they just changed.
        services.AddScoped<SiteDirectory>();
        services.AddScoped<ISiteDirectory>(sp => sp.GetRequiredService<SiteDirectory>());
        services.AddScoped<ICurrentSite, CurrentSite>();

        // What this module lets people do (002 contracts/authorization.md §2). Identity
        // enforces them; this module only declares them.
        foreach (var capability in SitesCapabilities.All)
        {
            services.AddSingleton(capability);
        }

        // Feature slices. Each is registered here and nowhere else.
        services.AddScoped<ICommandHandler<CreateSiteCommand, Result<SiteResponse>>, CreateSiteHandler>();
        services.AddScoped<IValidator<CreateSiteCommand>, CreateSiteValidator>();

        services.AddScoped<IQueryHandler<ListSitesQuery, IReadOnlyList<SiteResponse>>, ListSitesHandler>();

        services.AddScoped<IQueryHandler<GetSiteQuery, Result<SiteResponse>>, GetSiteHandler>();

        services.AddScoped<ICommandHandler<AddHostNameCommand, Result<AddHostNameResponse>>, AddHostNameHandler>();
        services.AddScoped<IValidator<AddHostNameCommand>, AddHostNameValidator>();

        services.AddScoped<ICommandHandler<RemoveHostNameCommand, Result>, RemoveHostNameHandler>();

        services.AddScoped<ICommandHandler<UpdateSettingsCommand, Result<SiteResponse>>, UpdateSettingsHandler>();
        services.AddScoped<IValidator<UpdateSettingsCommand>, UpdateSettingsValidator>();

        services.AddScoped<ICommandHandler<ChangeAvailabilityCommand, Result<SiteResponse>>, ChangeAvailabilityHandler>();

        // 017: search settings and visibility, two slices by capability (research SE-03, SE-08).
        services.AddScoped<SiteSettingsChange>();
        services.AddScoped<ICommandHandler<UpdateSearchSettingsCommand, Result<SiteResponse>>, UpdateSearchSettingsHandler>();
        services.AddScoped<ICommandHandler<UpdateSearchVisibilityCommand, Result<SiteResponse>>, UpdateSearchVisibilityHandler>();

        services.AddScoped<IQueryHandler<ListLanguagesQuery, ListLanguagesResponse>, ListLanguagesHandler>();

        // 020: site export and import (research ST-15, ST-16).
        services.AddScoped<TransferJobStore>();
        services.AddScoped<ISiteArchivePart, SitesArchivePart>();
        services.AddScoped<ExportRunner>();
        services.AddScoped<ITransferRunner>(sp => sp.GetRequiredService<ExportRunner>());
        services.AddScoped<CheckRunner>();
        services.AddScoped<ITransferRunner>(sp => sp.GetRequiredService<CheckRunner>());
        services.AddScoped<ImportRunner>();
        services.AddScoped<ITransferRunner>(sp => sp.GetRequiredService<ImportRunner>());
        services.AddScoped<NewSiteOptions>();
        services.AddScoped<ICommandHandler<CreateUploadCommand, Result<TransferResponse>>, CreateUploadHandler>();
        services.AddScoped<ICommandHandler<UploadArchiveCommand, Result>, UploadArchiveHandler>();
        services.AddScoped<ICommandHandler<CheckArchiveCommand, Result<TransferResponse>>, CheckArchiveHandler>();
        services.AddScoped<ICommandHandler<ConfirmImportCommand, Result<TransferResponse>>, ConfirmImportHandler>();
        services.AddScoped<ICommandHandler<StartExportCommand, Result<TransferResponse>>, StartExportHandler>();
        services.AddScoped<ICommandHandler<DeleteArchiveCommand, Result>, DeleteArchiveHandler>();
        services.AddScoped<IQueryHandler<DownloadArchiveQuery, Result<ArchiveDownload>>, DownloadArchiveHandler>();
        services.AddScoped<IQueryHandler<GetTransferQuery, Result<TransferResponse>>, GetTransferHandler>();
        services.AddScoped<IQueryHandler<ListTransfersQuery, IReadOnlyList<TransferResponse>>, ListTransfersHandler>();
        services.AddScoped<ICommandHandler<CancelTransferCommand, Result<TransferResponse>>, CancelTransferHandler>();
        services.AddSingleton<TransferWorker>();

        // 020 FR-032: sites export | check | import for operators (contracts/operator-commands.md).
        services.AddSingleton<IHostCommand, SitesCommand>();
        services.AddHostedService(sp => sp.GetRequiredService<TransferWorker>());
        services.AddSingleton<TransferRetentionService>();
        services.AddHostedService(sp => sp.GetRequiredService<TransferRetentionService>());
        foreach (var transferEvent in TransferEventTypes)
        {
            services.AddSingleton(transferEvent);
        }

        // The public placeholder that reported the resolved site (001) is gone: since 012 the
        // Rendering module answers the public surface with published pages.

        // Integration event handlers, plus the type registration that lets the dispatcher
        // read a stored payload back into a CLR type.
        services.AddScoped<IIntegrationEventHandler<SiteCreated>, SiteCreatedLogHandler>();
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<SiteCreated>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<HostNameAdded>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<HostNameRemoved>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<SiteDisabled>());
        services.AddSingleton<IEventTypeRegistration>(new EventTypeRegistration<SiteEnabled>());

        return services;
    }

    private static readonly IEventTypeRegistration[] TransferEventTypes =
    [
        new EventTypeRegistration<SiteExportFinished>(),
        new EventTypeRegistration<SiteImportFinished>(),
        new EventTypeRegistration<SiteArchiveDownloaded>(),
        new EventTypeRegistration<SiteArchiveDeleted>(),
    ];
}

/// <summary>Describes the module's database to the host (FR-024, D-04).</summary>
internal sealed class SitesDatabaseProvider : IModuleDatabaseProvider
{
    public ModuleDatabase Describe() =>
        new("Sites", SitesDbContext.Schema, typeof(SitesDbContext));

    public DbContext CreateContext(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Built directly rather than resolved from the request scope: migration and health
        // checks run outside any request, where no site is resolved. The empty site
        // context is correct there — these paths touch no site-owned data.
        var options = services.GetRequiredService<DbContextOptions<SitesDbContext>>();
        return new SitesDbContext(options, NoSiteContext.Instance);
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
            // A probe answers rather than throws. Whatever went wrong is already logged by
            // whichever component hit it first.
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
