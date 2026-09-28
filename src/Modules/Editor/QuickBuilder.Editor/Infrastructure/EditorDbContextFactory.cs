using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using QuickBuilder.Shared.Infrastructure.MultiSite;

namespace QuickBuilder.Editor.Infrastructure;

/// <summary>
/// Lets <c>dotnet ef</c> build the context without starting the application. Design time
/// only; the connection string is a placeholder, as in <c>PagesDbContextFactory</c>.
/// </summary>
internal sealed class EditorDbContextFactory : IDesignTimeDbContextFactory<EditorDbContext>
{
    public EditorDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<EditorDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=quickbuilder_design_time;Username=postgres",
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", EditorDbContext.Schema))
            .Options;

        return new EditorDbContext(options, DesignTimeSiteContext.Instance);
    }

    private sealed class DesignTimeSiteContext : ISiteContext
    {
        internal static readonly DesignTimeSiteContext Instance = new();

        public Guid? SiteId => null;

        public bool HasSite => false;
    }
}
