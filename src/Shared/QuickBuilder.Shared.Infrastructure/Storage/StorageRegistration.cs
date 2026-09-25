using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuickBuilder.Shared.Infrastructure.Storage.Local;

namespace QuickBuilder.Shared.Infrastructure.Storage;

/// <summary>
/// Registers the configured storage provider (constitution Principle IV; 003 research M-05).
/// </summary>
public static class StorageRegistration
{
    /// <summary>
    /// Binds <c>Storage</c>, validates it at startup, and registers the provider behind
    /// <see cref="IFileStorage"/> and <see cref="IFileStorageHealthCheck"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="forbiddenRoots">
    /// Directories storage must never be placed in or under — the application, content and
    /// web roots. Supplied by the host so this project needs no ASP.NET Core reference. Null
    /// or empty entries are ignored (a host without a web root passes null).
    /// </param>
    public static IServiceCollection AddFileStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        IReadOnlyList<string?> forbiddenRoots)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(forbiddenRoots);

        // Read by key rather than through the configuration binder: two strings do not justify
        // adding Microsoft.Extensions.Options.ConfigurationExtensions to this project
        // (Principle XI). A provider with richer settings can bind its own sub-section.
        var section = configuration.GetSection(StorageOptions.SectionName);

        services
            .AddOptions<StorageOptions>()
            .Configure(options =>
            {
                options.Provider = section["Provider"] ?? options.Provider;
                options.Local.Path = section["Local:Path"] ?? options.Local.Path;
            })
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.Local.Path))
                {
                    options.Local.Path = LocalStorageSettings.DefaultPath;
                }

                options.ForbiddenRoots.AddRange(forbiddenRoots.Where(r => !string.IsNullOrWhiteSpace(r))!);
            })
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<StorageOptions>, LocalStorageOptionsValidator>();

        // Local is the only provider in this release, and validation refuses any other name
        // before the first request, so it is registered unconditionally. A second provider
        // changes these three lines into a switch on StorageOptions.Provider.
        services.AddSingleton<LocalFileStorage>();
        services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<LocalFileStorage>());
        services.AddSingleton<IFileStorageHealthCheck>(sp => sp.GetRequiredService<LocalFileStorage>());
        services.AddHostedService<LocalIncomingCleanupService>();

        return services;
    }
}
