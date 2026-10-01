using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuickBuilder.Shared.Infrastructure.Storage.AzureBlob;
using QuickBuilder.Shared.Infrastructure.Storage.Local;
using QuickBuilder.Shared.Infrastructure.Storage.S3;

namespace QuickBuilder.Shared.Infrastructure.Storage;

/// <summary>
/// Registers the configured storage provider (constitution Principle IV; 003 research M-05;
/// 013 research MV-20).
/// </summary>
public static class StorageRegistration
{
    /// <summary>
    /// Binds <c>Storage</c>, validates it at startup, and registers the selected provider
    /// behind <see cref="IFileStorage"/> and <see cref="IFileStorageHealthCheck"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="forbiddenRoots">
    /// Directories local storage must never be placed in or under — the application, content
    /// and web roots. Supplied by the host so this project needs no ASP.NET Core reference.
    /// Null or empty entries are ignored (a host without a web root passes null).
    /// </param>
    /// <remarks>
    /// Exactly one provider is registered, so no other provider ever makes a network call
    /// (003 §3.4). The provider name is read here, at registration; an unknown name registers
    /// Local only so that its validator can refuse the name, before anything listens.
    /// </remarks>
    public static IServiceCollection AddFileStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        IReadOnlyList<string?> forbiddenRoots)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(forbiddenRoots);

        var section = configuration.GetSection(StorageOptions.SectionName);

        services
            .AddOptions<StorageOptions>()
            .Configure(options => section.Bind(options))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.Local.Path))
                {
                    options.Local.Path = LocalStorageSettings.DefaultPath;
                }

                options.ForbiddenRoots.AddRange(forbiddenRoots.Where(r => !string.IsNullOrWhiteSpace(r))!);
            })
            .ValidateOnStart();

        // Checks the provider name whichever provider is chosen, and Local's own settings.
        services.AddSingleton<IValidateOptions<StorageOptions>, LocalStorageOptionsValidator>();

        var selected = new StorageOptions { Provider = section["Provider"] ?? StorageOptions.LocalProvider };
        if (selected.IsS3)
        {
            services.AddSingleton<IValidateOptions<StorageOptions>, S3StorageOptionsValidator>();
            services.AddSingleton<S3FileStorage>();
            services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<S3FileStorage>());
            services.AddSingleton<IFileStorageHealthCheck>(sp => sp.GetRequiredService<S3FileStorage>());
            services.AddHostedService<S3AbandonedUploadCleanupService>();
        }
        else if (selected.IsAzureBlob)
        {
            // No clean-up service: uncommitted blocks are never visible and expire on their own.
            services.AddSingleton<IValidateOptions<StorageOptions>, AzureBlobStorageOptionsValidator>();
            services.AddSingleton<AzureBlobFileStorage>();
            services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<AzureBlobFileStorage>());
            services.AddSingleton<IFileStorageHealthCheck>(sp => sp.GetRequiredService<AzureBlobFileStorage>());
        }
        else
        {
            services.AddSingleton<LocalFileStorage>();
            services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<LocalFileStorage>());
            services.AddSingleton<IFileStorageHealthCheck>(sp => sp.GetRequiredService<LocalFileStorage>());
            services.AddHostedService<LocalIncomingCleanupService>();
        }

        return services;
    }
}
