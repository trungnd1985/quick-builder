using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using QuickBuilder.Shared.Infrastructure.MultiSite;

namespace QuickBuilder.Sites.Infrastructure;

/// <summary>
/// Lets <c>dotnet ef</c> build the context without starting the application.
/// </summary>
/// <remarks>
/// Design time only — never used at runtime. The connection string here is a placeholder:
/// generating a migration needs the model, not a reachable database, and putting a real
/// connection string in source would be exactly the kind of thing FR-038 forbids.
/// <para>
/// The site context is empty because migrations describe schema, which is not site-scoped.
/// </para>
/// </remarks>
internal sealed class SitesDbContextFactory : IDesignTimeDbContextFactory<SitesDbContext>
{
    public SitesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SitesDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=quickbuilder_design_time;Username=postgres",
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", SitesDbContext.Schema))
            .Options;

        return new SitesDbContext(options, DesignTimeSiteContext.Instance);
    }

    private sealed class DesignTimeSiteContext : ISiteContext
    {
        internal static readonly DesignTimeSiteContext Instance = new();

        public Guid? SiteId => null;

        public bool HasSite => false;
    }
}
