using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QuickBuilder.Shared.Infrastructure.Migrations.Platform;

/// <summary>Lets <c>dotnet ef</c> build the context without starting the application.</summary>
/// <remarks>
/// Design time only. The connection string is a placeholder: generating a migration needs the
/// model, not a reachable database.
/// </remarks>
internal sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=quickbuilder_design_time;Username=postgres",
                npgsql => npgsql.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.Schema))
            .Options);
}
