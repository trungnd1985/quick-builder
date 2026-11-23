using Microsoft.Extensions.Options;
using QuickBuilder.Shared.Kernel.Settings;

namespace QuickBuilder.Sites;

/// <summary>
/// Operator settings for site export and import (020 research ST-17).
/// </summary>
[SettingsSection(SectionName, Area = "transfer", Title = "Site export and import")]
internal sealed class SiteTransferOptions
{
    public const string SectionName = "SiteTransfer";

    internal const long MinimumUploadSize = 1_048_576;
    internal const long MaximumUploadSize = 107_374_182_400;

    /// <summary>
    /// Largest archive the admin accepts as an upload, in bytes. 10 GiB by default. The
    /// operator command reads archives from disk and has no limit (spec edge case).
    /// </summary>
    [Setting("The largest site archive the admin accepts as an upload for import, in bytes. The `sites import` command reads archives from disk and has no limit. Raise the reverse proxy's request size limit for `/api/v1/transfers/uploads` to match.", Allowed = "1048576 â 107374182400")]
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
