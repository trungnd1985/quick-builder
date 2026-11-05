using Microsoft.Extensions.Options;

namespace QuickBuilder.Sites;

/// <summary>
/// Operator settings for site export and import (020 research ST-17).
/// </summary>
internal sealed class SiteTransferOptions
{
    public const string SectionName = "SiteTransfer";

    internal const long MinimumUploadSize = 1_048_576;
    internal const long MaximumUploadSize = 107_374_182_400;

    /// <summary>
    /// Largest archive the admin accepts as an upload, in bytes. 10 GiB by default. The
    /// operator command reads archives from disk and has no limit (spec edge case).
    /// </summary>
    public long MaxUploadSizeBytes { get; set; } = 10L * 1024 * 1024 * 1024;
}

/// <summary>Startup validation for <see cref="SiteTransferOptions"/>, naming the offending key.</summary>
internal sealed class SiteTransferOptionsValidator : IValidateOptions<SiteTransferOptions>
{
    public ValidateOptionsResult Validate(string? name, SiteTransferOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.MaxUploadSizeBytes is < SiteTransferOptions.MinimumUploadSize or > SiteTransferOptions.MaximumUploadSize
            ? ValidateOptionsResult.Fail(
                $"SiteTransfer:MaxUploadSizeBytes must be between {SiteTransferOptions.MinimumUploadSize} and " +
                $"{SiteTransferOptions.MaximumUploadSize}; it is {options.MaxUploadSizeBytes}.")
            : ValidateOptionsResult.Success;
    }
}
