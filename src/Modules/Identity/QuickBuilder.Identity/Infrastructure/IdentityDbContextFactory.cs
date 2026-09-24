using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QuickBuilder.Identity.Infrastructure;

/// <summary>
/// Lets <c>dotnet ef</c> build the context without starting the application.
/// </summary>
/// <remarks>
/// Design time only. The connection string is a placeholder: generating a migration needs
/// the model, not a reachable database, and a real connection string in source is what
/// 001 FR-038 forbids.
/// </remarks>
internal sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=quickbuilder_design_time;Username=postgres",
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema))
            .Options;

        return new IdentityDbContext(options);
    }
}
