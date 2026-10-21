using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using QuickBuilder.Shared.Infrastructure.MultiSite;

namespace QuickBuilder.Forms.Infrastructure;

/// <summary>
/// Lets <c>dotnet ef</c> build the context without starting the application. Design time
/// only; the connection string is a placeholder, as in <c>CmsDbContextFactory</c>.
/// </summary>
internal sealed class FormsDbContextFactory : IDesignTimeDbContextFactory<FormsDbContext>
{
    public FormsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FormsDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=quickbuilder_design_time;Username=postgres",
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", FormsDbContext.Schema))
            .Options;

        return new FormsDbContext(options, DesignTimeSiteContext.Instance);
    }

    private sealed class DesignTimeSiteContext : ISiteContext
    {
        internal static readonly DesignTimeSiteContext Instance = new();

        public Guid? SiteId => null;

        public bool HasSite => false;
    }
}
