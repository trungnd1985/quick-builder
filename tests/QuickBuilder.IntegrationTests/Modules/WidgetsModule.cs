using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Shared.Infrastructure.Data;
using QuickBuilder.Shared.Infrastructure.Dispatch;
using QuickBuilder.Shared.Infrastructure.Modules;
using QuickBuilder.Shared.Infrastructure.MultiSite;
using QuickBuilder.Shared.Kernel.Dispatch;
using QuickBuilder.Shared.Kernel.MultiSite;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.IntegrationTests.Modules;

// A complete second module, written only to prove SC-009: adding one requires changes to
// no existing module and no more than one line of host registration.
//
// Deliberately built by following docs/contributing.md step by step and nothing else. If
// this file needs something the guide does not mention, the guide is wrong — that is part
// of what the test measures.
//
// It lives in the test assembly rather than as real projects because SC-009 is a claim
// about coupling, not about csproj files. What matters is that it owns its own schema,
// registers through one entry point, and touches nothing in Sites.

/// <summary>A site-owned entity, so the module exercises site scoping too.</summary>
internal sealed class Widget : ISiteOwned
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public required string Name { get; set; }
}

/// <summary>The module's own context, owning the <c>widgets</c> schema and nothing else.</summary>
internal sealed class WidgetsDbContext(DbContextOptions<WidgetsDbContext> options, ISiteContext siteContext)
    : DbContext(options)
{
    internal const string Schema = "widgets";

    internal DbSet<Widget> Widgets => Set<Widget>();

    // Per research.md D-08: a property on the context, referenced through `this`. A filter
    // closing over the injected ISiteContext would capture the model-building instance and
    // silently match nothing.
    internal Guid CurrentSiteId => siteContext.SiteId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Widget>(entity =>
        {
            entity.ToTable("widgets", Schema);
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.HasQueryFilter(w => w.SiteId == CurrentSiteId);
        });

        modelBuilder.UseSnakeCaseColumns();

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed record CountWidgetsQuery : IQuery<Result<int>>;

internal sealed class CountWidgetsHandler(WidgetsDbContext database)
    : IQueryHandler<CountWidgetsQuery, Result<int>>
{
    public async Task<Result<int>> HandleAsync(CountWidgetsQuery query, CancellationToken cancellationToken) =>
        Result.Success(await database.Widgets.AsNoTracking().CountAsync(cancellationToken).ConfigureAwait(false));
}

/// <summary>Follows FR-010a: /api/v1/&lt;module&gt;/...</summary>
[ApiController]
[Route("api/v1/widgets")]
public sealed class WidgetsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("count")]
    public async Task<IActionResult> CountAsync(CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(new CountWidgetsQuery(), cancellationToken).ConfigureAwait(false);
        return Ok(new { count = result.Value });
    }
}

/// <summary>The single registration entry point (FR-004) — the one line the host adds.</summary>
public static class WidgetsModule
{
    public static IServiceCollection AddWidgetsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<WidgetsDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Default"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", WidgetsDbContext.Schema)));

        services.AddSingleton<IModuleDatabaseProvider, WidgetsDatabaseProvider>();
        services.AddScoped<IQueryHandler<CountWidgetsQuery, Result<int>>, CountWidgetsHandler>();

        return services;
    }
}

internal sealed class WidgetsDatabaseProvider : IModuleDatabaseProvider
{
    public ModuleDatabase Describe() => new("Widgets", WidgetsDbContext.Schema, typeof(WidgetsDbContext));

    public DbContext CreateContext(IServiceProvider services)
    {
        var options = services.GetRequiredService<DbContextOptions<WidgetsDbContext>>();
        return new WidgetsDbContext(options, NoSite.Instance);
    }

    public async Task<bool> CanConnectAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var context = CreateContext(services);
        return await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class NoSite : ISiteContext
    {
        internal static readonly NoSite Instance = new();

        public Guid? SiteId => null;

        public bool HasSite => false;
    }
}
