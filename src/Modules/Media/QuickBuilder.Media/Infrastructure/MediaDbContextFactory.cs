using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using QuickBuilder.Shared.Infrastructure.MultiSite;

namespace QuickBuilder.Media.Infrastructure;

/// <summary>
/// Lets <c>dotnet ef</c> build the context without starting the application. Design time
/// only; the connection string is a placeholder, as in <c>SitesDbContextFactory</c>.
/// </summary>
internal sealed class MediaDbContextFactory : IDesignTimeDbContextFactory<MediaDbContext>
{
    public MediaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=quickbuilder_design_time;Username=postgres",
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MediaDbContext.Schema))
            .Options;

        return new MediaDbContext(options, DesignTimeSiteContext.Instance);
    }

    private sealed class DesignTimeSiteContext : ISiteContext
    {
        internal static readonly DesignTimeSiteContext Instance = new();

        public Guid? SiteId => null;

        public bool HasSite => false;
    }
}
