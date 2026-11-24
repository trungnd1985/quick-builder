using Microsoft.Extensions.Options;
using QuickBuilder.Shared.Kernel.Settings;

namespace QuickBuilder.Identity;

/// <summary>
/// The <c>Identity</c> configuration section (research, "Configuration keys introduced").
/// </summary>
/// <remarks>
/// Validated at startup by <see cref="IdentityOptionsValidator"/>: an invalid value stops
/// the process before it listens and names the key (001 FR-028). The defaults here match
/// appsettings.json and docs/operators/reference/configuration.md.
/// </remarks>
[SettingsSection(SectionName, Area = "identity", Title = "Identity")]
internal sealed class IdentityOptions
{
    public const string SectionName = "Identity";

    [SettingsGroup("Sessions")]
    public SessionSettings Session { get; set; } = new();

    [SettingsGroup("Sign-in throttling")]
    public SignInSettings SignIn { get; set; } = new();

    [SettingsGroup("Passwords")]
    public PasswordSettings Passwords { get; set; } = new();

    [SettingsGroup("Set-password links")]
    public SetPasswordLinkSettings SetPasswordLinks { get; set; } = new();

    [SettingsGroup("Security events")]
    public SecurityEventSettings SecurityEvents { get; set; } = new();

    /// <summary>
    /// Accept credentials over plain HTTP from non-loopback clients (research R-04). Off by
    /// default; a warning is logged at every startup while it is on.
    /// </summary>
    [Setting("Accept credentials over plain HTTP from other machines; the app logs a warning at every startup while it is on. The reference compose file turns it on because, inside Docker, requests from your own machine do not arrive from loopback. Safe only while the port stays bound to `127.0.0.1`.")]
    public bool AllowInsecureHttp { get; set; }

    internal sealed class SessionSettings
    {
        [Setting("A session unused for this long ends.", Allowed = "`00:05:00` â `1.00:00:00`")]
        public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromHours(2);

        [Setting("A session ends this long after sign-in, however active.", Allowed = "`00:05:00` â `30.00:00:00`, and at least `Identity:Session:IdleTimeout`")]
        public TimeSpan AbsoluteLifetime { get; set; } = TimeSpan.FromHours(12);
    }

    internal sealed class SignInSettings
    {
        [Setting("Failed sign-ins from one address before it is blocked.", Allowed = "3 â 100")]
        public int ClientFailureThreshold { get; set; } = 5;

        [Setting("How long a blocked address waits.", Allowed = "`00:01:00` â `1.00:00:00`")]
        public TimeSpan ClientBlockDuration { get; set; } = TimeSpan.FromMinutes(15);

        [Setting("Failed sign-ins on one account before each further attempt must wait. The wait doubles with each failure.", Allowed = "1 â 20")]
        public int AccountFailureThreshold { get; set; } = 3;

        [Setting("The longest an account's wait grows to. There is no hard lockout.", Allowed = "`00:00:01` â `00:10:00`")]
        public TimeSpan AccountMaxDelay { get; set; } = TimeSpan.FromMinutes(1);

        [Setting("Failures from a remembered browser before it stops being exempt from the account wait.", Allowed = "1 â 20")]
        public int KnownDeviceFailureThreshold { get; set; } = 5;

        [Setting("How long a browser that signed in successfully is remembered.", Allowed = "`1.00:00:00` â `365.00:00:00`")]
        public TimeSpan KnownDeviceLifetime { get; set; } = TimeSpan.FromDays(90);
    }

    internal sealed class PasswordSettings
    {
        /// <summary>The OWASP figure for PBKDF2-HMAC-SHA512 (research R-03). Never lower.</summary>
        public const int MinimumIterations = 210_000;

        [Setting("Password hashing cost. Raising it upgrades stored hashes as people sign in; it can never be lowered. If the startup warning says hashing is slow, give the app more CPU rather than lowering this.", Allowed = "at least 210000")]
        public int Pbkdf2Iterations { get; set; } = MinimumIterations;
    }

    internal sealed class SetPasswordLinkSettings
    {
        [Setting("How long a set-password link stays valid.", Allowed = "`00:10:00` â `7.00:00:00`")]
        public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(1);
    }

    internal sealed class SecurityEventSettings
    {
        [Setting("How long security events are kept; an hourly sweep removes older ones.", Allowed = "`1.00:00:00` â `3650.00:00:00`")]
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

    [NotASetting("Declared by the Host's HostingOptions; Identity reads the same key.")]
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
