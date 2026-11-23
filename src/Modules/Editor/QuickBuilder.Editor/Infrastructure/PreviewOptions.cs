using Microsoft.Extensions.Options;
using QuickBuilder.Shared.Kernel.Settings;

namespace QuickBuilder.Editor.Infrastructure;

/// <summary>The <c>Preview</c> settings (022 research PV-07; docs/operators/reference/configuration.md).</summary>
[SettingsSection(SectionName, Area = "preview", Title = "Preview links")]
internal sealed class PreviewOptions
{
    public const string SectionName = "Preview";

    /// <summary>
    /// The operator's emergency switch (spec FR-019): a preview link created before this UTC
    /// time no longer works, as if revoked. Absent by default. Setting it to "now" and
    /// restarting ends every existing link; links created afterwards work.
    /// </summary>
    [Setting("Preview links created before this time stop working, as if revoked. Set it to the current time and restart to end every existing link at once; links created afterwards work.", Allowed = "a UTC time at most one day ahead")]
    public DateTimeOffset? LinksValidFrom { get; set; }
}

/// <summary>
/// Stops startup on a switch set far in the future, which would silently disable every new
/// link too (constitution Principle II). A day of leeway allows for clock skew and time zones
/// typed by hand.
/// </summary>
internal sealed class PreviewOptionsValidator(TimeProvider clock) : IValidateOptions<PreviewOptions>
{
    public ValidateOptionsResult Validate(string? name, PreviewOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.LinksValidFrom is { } from && from > clock.GetUtcNow().AddDays(1))
        {
            return ValidateOptionsResult.Fail(
                $"Preview:LinksValidFrom must not be more than one day in the future; it is {from:O}. Set it to the current UTC time to end every existing preview link.");
        }

        return ValidateOptionsResult.Success;
    }
}
