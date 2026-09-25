using Microsoft.Extensions.Options;

namespace QuickBuilder.Shared.Infrastructure.Storage.Local;

/// <summary>
/// Startup validation for <see cref="StorageOptions"/> (003 FR-022, FR-023, research M-05).
/// </summary>
/// <remarks>
/// Runs through <c>ValidateOnStart</c> like every other option, so a bad location stops the
/// process before it listens and the message names the key. The directory rules are what
/// "never served from an executable path" means for the local provider: the location must
/// not be anywhere the application's own files, its static-file middleware or a runtime
/// loader looks.
/// </remarks>
internal sealed class LocalStorageOptionsValidator : IValidateOptions<StorageOptions>
{
    private const string ProviderKey = "Storage:Provider";
    private const string PathKey = "Storage:Local:Path";

    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!StorageOptions.SupportedProviders.Contains(options.Provider, StringComparer.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                $"{ProviderKey} '{options.Provider}' is not supported. Supported providers: " +
                $"{string.Join(", ", StorageOptions.SupportedProviders)}.");
        }

        if (!options.IsLocal)
        {
            return ValidateOptionsResult.Success;
        }

        var configured = options.Local.Path;
        if (!Path.IsPathFullyQualified(configured))
        {
            return ValidateOptionsResult.Fail(
                $"{PathKey} '{configured}' must be an absolute path.");
        }

        // The location is checked against the application's own directories before anything
        // is created: refusing a path inside /app must never leave a directory behind there,
        // and the operator must hear the real reason, not "cannot be created".
        var canonical = LocalStoragePaths.Canonicalize(configured);
        foreach (var forbidden in options.ForbiddenRoots)
        {
            var root = LocalStoragePaths.Canonicalize(forbidden);
            if (LocalStoragePaths.IsSameOrUnder(canonical, root))
            {
                return ValidateOptionsResult.Fail(
                    $"{PathKey} '{configured}' is inside the application's own directory '{root}'. " +
                    "Uploaded files must live outside every location the application runs code or serves " +
                    "static files from.");
            }
        }

        try
        {
            Directory.CreateDirectory(canonical);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return ValidateOptionsResult.Fail(
                $"{PathKey} '{configured}' cannot be created: {ex.Message}");
        }

        // Synchronous by necessity: options validation has no async entry point, and this
        // runs once, before the host listens.
        var problem = LocalStoragePaths.ProbeAsync(canonical, CancellationToken.None).GetAwaiter().GetResult();
        return problem is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{PathKey} '{configured}' cannot be written to: {problem}");
    }
}
