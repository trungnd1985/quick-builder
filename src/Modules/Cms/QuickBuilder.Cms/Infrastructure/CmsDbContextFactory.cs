using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using QuickBuilder.Shared.Infrastructure.MultiSite;

namespace QuickBuilder.Cms.Infrastructure;

/// <summary>
/// Lets <c>dotnet ef</c> build the context without starting the application. Design time
/// only; the connection string is a placeholder, as in <c>PagesDbContextFactory</c>.
/// </summary>
internal sealed class CmsDbContextFactory : IDesignTimeDbContextFactory<CmsDbContext>
{
    public CmsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=quickbuilder_design_time;Username=postgres",
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", CmsDbContext.Schema))
            .Options;

        return new CmsDbContext(options, DesignTimeSiteContext.Instance);
    }

    private sealed class DesignTimeSiteContext : ISiteContext
    {
        internal static readonly DesignTimeSiteContext Instance = new();

        public Guid? SiteId => null;

        public bool HasSite => false;
    }
}
