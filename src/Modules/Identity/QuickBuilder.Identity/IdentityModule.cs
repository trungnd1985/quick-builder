using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuickBuilder.Identity.Infrastructure;
using QuickBuilder.Shared.Infrastructure.Modules;
using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.Identity;

/// <summary>
/// The Identity module's registration entry points (001 FR-004).
/// </summary>
/// <remarks>
/// The host calls <see cref="AddIdentityModule"/> with the other modules, and
/// <see cref="UseIdentityModule"/> once in the pipeline between authentication and
/// authorization. Everything else in this assembly is internal apart from the controllers.
/// </remarks>
public static class IdentityModule
{
    /// <summary>The capability that guards every user-management operation.</summary>
    internal const string ManageUsers = "identity.users.manage";

    /// <summary>Registers the Identity module: data, options, services and controllers.</summary>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Default");

        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                // Own migrations history table inside the identity schema (001 FR-022).
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema)));

        services.AddSingleton<IModuleDatabaseProvider, IdentityDatabaseProvider>();

        services
            .AddOptions<IdentityOptions>()
            .Bind(configuration.GetSection(IdentityOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IdentityOptions>, IdentityOptionsValidator>();
        services
            .AddOptions<PublicUrlOptions>()
            .Bind(configuration.GetSection(PublicUrlOptions.SectionName));

        services.AddSingleton(new CapabilityDefinition(ManageUsers, CapabilityScope.Installation, []));

        return services;
    }

    /// <summary>
    /// Adds the Identity module's request pipeline pieces. Called by the host after
    /// <c>UseAuthentication()</c> and before <c>UseAuthorization()</c>.
    /// </summary>
    public static IApplicationBuilder UseIdentityModule(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app;
    }
}

/// <summary>Describes the module's database to the host (001 FR-024, D-04).</summary>
internal sealed class IdentityDatabaseProvider : IModuleDatabaseProvider
{
    public ModuleDatabase Describe() =>
        new("Identity", IdentityDbContext.Schema, typeof(IdentityDbContext));

    public DbContext CreateContext(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.GetRequiredService<DbContextOptions<IdentityDbContext>>();
        return new IdentityDbContext(options);
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
            // A probe answers rather than throws; the component that hit the failure logs it.
            return false;
        }
    }
}
