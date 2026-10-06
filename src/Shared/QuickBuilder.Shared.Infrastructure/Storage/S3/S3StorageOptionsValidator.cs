using Microsoft.Extensions.Options;

namespace QuickBuilder.Shared.Infrastructure.Storage.S3;

/// <summary>
/// Start-up validation for the S3-compatible provider (013 contracts/storage-providers.md §5).
/// </summary>
/// <remarks>
/// The settings rules first, then a probe against the service with a 30-second budget,
/// including the conditional-write check every create-only write depends on. Each failure
/// names the key at fault and never contains a secret value (FR-033).
/// </remarks>
internal sealed class S3StorageOptionsValidator : IValidateOptions<StorageOptions>
{
    private const string Section = "Storage:S3:";

    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsS3)
        {
            return ValidateOptionsResult.Success;
        }

        var settings = options.S3;
        var failures = Rules(settings).ToList();
        if (failures.Count > 0)
        {
            return ValidateOptionsResult.Fail(failures);
        }

        using var storage = new S3FileStorage(settings);

        // Synchronous by necessity: options validation has no async entry point, and this
        // runs once, before the host listens.
        var failure = RemoteProbe.RunAsync(storage, RemoteProbe.ValidationBudget, checkConditionalWrite: true, CancellationToken.None)
            .GetAwaiter().GetResult();

        return failure == ProbeFailure.None ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(Message(failure, settings));
    }

    /// <summary>The rules that need no network.</summary>
    internal static IEnumerable<string> Rules(S3StorageSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Bucket))
        {
            yield return $"{Section}Bucket is required with Storage:Provider S3.";
        }

        if (string.IsNullOrWhiteSpace(settings.Region))
        {
            yield return $"{Section}Region is required with Storage:Provider S3 (any value for services that ignore regions).";
        }

        if ((settings.AccessKeyId.Length > 0) != (settings.SecretAccessKey.Length > 0))
        {
            yield return $"{Section}AccessKeyId and {Section}SecretAccessKey must be set together, or neither to use the environment's credentials.";
        }

        if (settings.ServiceUrl.Length > 0 && !RemoteProbe.IsHttpUrl(settings.ServiceUrl))
        {
            yield return $"{Section}ServiceUrl '{settings.ServiceUrl}' must be an absolute http or https URL.";
        }

        if (!RemoteProbe.IsValidKeyPrefix(settings.KeyPrefix))
        {
            yield return $"{Section}KeyPrefix '{settings.KeyPrefix}' must be segments of lower-case letters, digits and '-', ending in '/', for example 'quickbuilder/'.";
        }
    }

    private static string Message(ProbeFailure failure, S3StorageSettings settings) => failure switch
    {
        ProbeFailure.Credentials =>
            $"{Section}AccessKeyId: the service rejected the credentials. Check {Section}AccessKeyId and {Section}SecretAccessKey, or the environment's credentials.",
        ProbeFailure.ContainerMissing =>
            $"{Section}Bucket '{settings.Bucket}' does not exist. QuickBuilder never creates it.",
        ProbeFailure.AccessDenied =>
            $"{Section}Bucket '{settings.Bucket}': access denied. The credentials need s3:PutObject, s3:GetObject, s3:DeleteObject and " +
            $"s3:AbortMultipartUpload on '{settings.KeyPrefix}*', and s3:ListBucket and s3:ListBucketMultipartUploads on the bucket.",
        ProbeFailure.NoConditionalWrites =>
            $"{Section}ServiceUrl: the service does not support conditional writes (If-None-Match), which QuickBuilder requires.",
        ProbeFailure.Mismatch =>
            $"{Section}ServiceUrl: an object read back from the service differs from what was written.",
        _ => settings.ServiceUrl.Length > 0
            ? $"{Section}ServiceUrl '{settings.ServiceUrl}': the service could not be reached within {RemoteProbe.ValidationBudget.TotalSeconds:0} seconds."
            : $"{Section}Region '{settings.Region}': AWS S3 could not be reached within {RemoteProbe.ValidationBudget.TotalSeconds:0} seconds.",
    };
}
