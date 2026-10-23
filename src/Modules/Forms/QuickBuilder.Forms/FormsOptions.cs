using Microsoft.Extensions.Options;

namespace QuickBuilder.Forms;

/// <summary>The <c>Forms</c> settings (018 module-surface.md §5).</summary>
internal sealed class FormsOptions
{
    public const string SectionName = "Forms";

    public RateLimitSettings RateLimit { get; set; } = new();

    /// <summary>
    /// How many submit requests one visitor address may send to one site (018 FR-017; research
    /// FS-13). Every request counts, valid or not.
    /// </summary>
    public sealed class RateLimitSettings
    {
        public int PerMinute { get; set; } = 5;

        public int PerHour { get; set; } = 30;
    }
}

/// <summary>Stops startup on limits that cannot work, naming the key (constitution Principle II).</summary>
internal sealed class FormsOptionsValidator : IValidateOptions<FormsOptions>
{
    public ValidateOptionsResult Validate(string? name, FormsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var limits = options.RateLimit;
        var failures = new List<string>();

        if (limits.PerMinute is < 1 or > 1000)
        {
            failures.Add($"Forms:RateLimit:PerMinute must be between 1 and 1000; it is {limits.PerMinute}.");
        }

        if (limits.PerHour is < 1 or > 10000)
        {
            failures.Add($"Forms:RateLimit:PerHour must be between 1 and 10000; it is {limits.PerHour}.");
        }
        else if (limits.PerHour < limits.PerMinute)
        {
            failures.Add($"Forms:RateLimit:PerHour ({limits.PerHour}) must be at least Forms:RateLimit:PerMinute ({limits.PerMinute}).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
