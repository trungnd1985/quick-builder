using Microsoft.Extensions.Options;

namespace QuickBuilder.Identity;

/// <summary>
/// The <c>Identity</c> configuration section (research, "Configuration keys introduced").
/// </summary>
/// <remarks>
/// Validated at startup by <see cref="IdentityOptionsValidator"/>: an invalid value stops
/// the process before it listens and names the key (001 FR-028). The defaults here match
/// appsettings.json and docs/configuration.md.
/// </remarks>
internal sealed class IdentityOptions
{
    public const string SectionName = "Identity";

    public SessionSettings Session { get; set; } = new();

    public SignInSettings SignIn { get; set; } = new();

    public PasswordSettings Passwords { get; set; } = new();

    public SetPasswordLinkSettings SetPasswordLinks { get; set; } = new();

    public SecurityEventSettings SecurityEvents { get; set; } = new();

    /// <summary>
    /// Accept credentials over plain HTTP from non-loopback clients (research R-04). Off by
    /// default; a warning is logged at every startup while it is on.
    /// </summary>
    public bool AllowInsecureHttp { get; set; }

    internal sealed class SessionSettings
    {
        public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromHours(2);

        public TimeSpan AbsoluteLifetime { get; set; } = TimeSpan.FromHours(12);
    }

    internal sealed class SignInSettings
    {
        public int ClientFailureThreshold { get; set; } = 5;

        public TimeSpan ClientBlockDuration { get; set; } = TimeSpan.FromMinutes(15);

        public int AccountFailureThreshold { get; set; } = 3;

        public TimeSpan AccountMaxDelay { get; set; } = TimeSpan.FromMinutes(1);

        public int KnownDeviceFailureThreshold { get; set; } = 5;

        public TimeSpan KnownDeviceLifetime { get; set; } = TimeSpan.FromDays(90);
    }

    internal sealed class PasswordSettings
    {
        /// <summary>The OWASP figure for PBKDF2-HMAC-SHA512 (research R-03). Never lower.</summary>
        public const int MinimumIterations = 210_000;

        public int Pbkdf2Iterations { get; set; } = MinimumIterations;
    }

    internal sealed class SetPasswordLinkSettings
    {
        public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(1);
    }

    internal sealed class SecurityEventSettings
    {
        public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(365);
    }
}

/// <summary>
/// <c>Hosting:PublicUrl</c>, read by Identity for the recovery command only (research R-10).
/// </summary>
/// <remarks>
/// The host validates the same key in its own <c>HostingOptions</c>; Identity binds it
/// separately because a module must not depend on the host's types.
/// </remarks>
internal sealed class PublicUrlOptions
{
    public const string SectionName = "Hosting";

    public string PublicUrl { get; set; } = string.Empty;
}

internal sealed class IdentityOptionsValidator : IValidateOptions<IdentityOptions>
{
    public ValidateOptionsResult Validate(string? name, IdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        void Range(TimeSpan value, TimeSpan min, TimeSpan max, string key)
        {
            if (value < min || value > max)
            {
                failures.Add($"Identity:{key} must be between {min} and {max} (was {value}).");
            }
        }

        void IntRange(int value, int min, int max, string key)
        {
            if (value < min || value > max)
            {
                failures.Add($"Identity:{key} must be between {min} and {max} (was {value}).");
            }
        }

        var session = options.Session;
        Range(session.IdleTimeout, TimeSpan.FromMinutes(5), TimeSpan.FromHours(24), "Session:IdleTimeout");
        Range(session.AbsoluteLifetime, TimeSpan.FromMinutes(5), TimeSpan.FromDays(30), "Session:AbsoluteLifetime");
        if (session.AbsoluteLifetime < session.IdleTimeout)
        {
            failures.Add("Identity:Session:AbsoluteLifetime must be at least Identity:Session:IdleTimeout.");
        }

        var signIn = options.SignIn;
        IntRange(signIn.ClientFailureThreshold, 3, 100, "SignIn:ClientFailureThreshold");
        Range(signIn.ClientBlockDuration, TimeSpan.FromMinutes(1), TimeSpan.FromHours(24), "SignIn:ClientBlockDuration");
        IntRange(signIn.AccountFailureThreshold, 1, 20, "SignIn:AccountFailureThreshold");
        Range(signIn.AccountMaxDelay, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(10), "SignIn:AccountMaxDelay");
        IntRange(signIn.KnownDeviceFailureThreshold, 1, 20, "SignIn:KnownDeviceFailureThreshold");
        Range(signIn.KnownDeviceLifetime, TimeSpan.FromDays(1), TimeSpan.FromDays(365), "SignIn:KnownDeviceLifetime");

        if (options.Passwords.Pbkdf2Iterations < IdentityOptions.PasswordSettings.MinimumIterations)
        {
            failures.Add(
                $"Identity:Passwords:Pbkdf2Iterations must be at least " +
                $"{IdentityOptions.PasswordSettings.MinimumIterations} (current OWASP guidance); " +
                "lowering it weakens every stored password.");
        }

        Range(options.SetPasswordLinks.Lifetime, TimeSpan.FromMinutes(10), TimeSpan.FromDays(7), "SetPasswordLinks:Lifetime");
        Range(options.SecurityEvents.RetentionPeriod, TimeSpan.FromDays(1), TimeSpan.FromDays(3650), "SecurityEvents:RetentionPeriod");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
