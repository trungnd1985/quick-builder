using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuickBuilder.Shared.Infrastructure.Versioning;

namespace QuickBuilder.Shared.Infrastructure.Migrations.Platform;

/// <summary>Registers the <c>platform</c> schema's context (019 research RL-07).</summary>
public static class PlatformDatabaseRegistration
{
    public static IServiceCollection AddPlatformDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Default");

        services.AddDbContext<PlatformDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.Schema)));

        services.AddScoped<IInstallationVersion, InstallationVersion>();

        return services;
    }
}
