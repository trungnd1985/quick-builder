using Microsoft.Extensions.Options;

namespace QuickBuilder.Shared.Infrastructure.Storage.AzureBlob;

/// <summary>
/// Start-up validation for the Azure Blob provider (013 contracts/storage-providers.md §5).
/// </summary>
/// <remarks>
/// The settings rules first, then a probe with a 30-second budget, including the
/// conditional-write check. Messages name the key and never contain the connection string
/// or the account key inside it (FR-033).
/// </remarks>
internal sealed class AzureBlobStorageOptionsValidator : IValidateOptions<StorageOptions>
{
    private const string Section = "Storage:AzureBlob:";

    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsAzureBlob)
        {
            return ValidateOptionsResult.Success;
        }

        var settings = options.AzureBlob;
        var failures = Rules(settings).ToList();
        if (failures.Count > 0)
        {
            return ValidateOptionsResult.Fail(failures);
        }

        AzureBlobFileStorage storage;
        try
        {
            storage = new AzureBlobFileStorage(settings);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        {
            // Never the parser's message: it may quote the string.
            return ValidateOptionsResult.Fail($"{Section}ConnectionString is not a valid Azure Storage connection string.");
        }

        // Synchronous by necessity: options validation has no async entry point, and this
        // runs once, before the host listens.
        var failure = RemoteProbe.RunAsync(storage, RemoteProbe.ValidationBudget, checkConditionalWrite: true, CancellationToken.None)
            .GetAwaiter().GetResult();

        return failure == ProbeFailure.None ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(Message(failure, settings));
    }

    /// <summary>The rules that need no network.</summary>
    internal static IEnumerable<string> Rules(AzureBlobStorageSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Container))
        {
            yield return $"{Section}Container is required with Storage:Provider AzureBlob.";
        }

        if ((settings.ConnectionString.Length > 0) == (settings.AccountUrl.Length > 0))
        {
            yield return $"Set exactly one of {Section}ConnectionString and {Section}AccountUrl.";
        }

        if (settings.AccountUrl.Length > 0 && !(Uri.TryCreate(settings.AccountUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps))
        {
            // HTTPS because a token credential is only ever sent over TLS. Not echoed: a URL
            // with a SAS query would carry a secret.
            yield return $"{Section}AccountUrl must be an absolute https URL, for example https://account.blob.core.windows.net.";
        }

        if (!RemoteProbe.IsValidKeyPrefix(settings.KeyPrefix))
        {
            yield return $"{Section}KeyPrefix '{settings.KeyPrefix}' must be segments of lower-case letters, digits and '-', ending in '/', for example 'quickbuilder/'.";
        }
    }

    private static string Message(ProbeFailure failure, AzureBlobStorageSettings settings)
    {
        var access = settings.ConnectionString.Length > 0 ? $"{Section}ConnectionString" : $"{Section}AccountUrl";
        return failure switch
        {
            ProbeFailure.Credentials => settings.ConnectionString.Length > 0
                ? $"{Section}ConnectionString: the service rejected the credentials in the connection string."
                : $"{Section}AccountUrl: the environment's identity could not authenticate (managed or workload identity, environment variables, or the Azure CLI).",
            ProbeFailure.ContainerMissing =>
                $"{Section}Container '{settings.Container}' does not exist. QuickBuilder never creates it.",
            // The emulator answers a wrong account key this way too, so the credential's key is named as well.
            ProbeFailure.AccessDenied =>
                $"{Section}Container '{settings.Container}': access denied for the credentials in {access}. Check them, and grant the " +
                "Storage Blob Data Contributor role on the container, or use a SAS with read, add, create, write, delete and list permissions.",
            ProbeFailure.NoConditionalWrites =>
                $"{access}: the service does not support conditional writes (If-None-Match), which QuickBuilder requires.",
            ProbeFailure.Mismatch =>
                $"{access}: a blob read back from the service differs from what was written.",
            _ => $"{access}: the service could not be reached within {RemoteProbe.ValidationBudget.TotalSeconds:0} seconds.",
        };
    }
}
