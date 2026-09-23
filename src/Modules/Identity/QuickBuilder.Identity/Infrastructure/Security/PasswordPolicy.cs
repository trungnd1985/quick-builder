using System.Reflection;
using QuickBuilder.Shared.Kernel.Validation;

namespace QuickBuilder.Identity.Infrastructure.Security;

/// <summary>
/// The password rules of spec FR-008, following NIST SP 800-63B: length and a
/// breached/common-password check, and no composition rules.
/// </summary>
internal static class PasswordPolicy
{
    public const int MinCodePoints = 12;

    private static readonly Lazy<HashSet<string>> CommonPasswords = new(LoadCommonPasswords);

    /// <summary>
    /// Returns the first rule the password breaks, naming <paramref name="field"/>; empty when
    /// it is acceptable. One message at a time is easier to act on than a list.
    /// </summary>
    public static IReadOnlyList<ValidationFailure> Check(
        string? password,
        string? email,
        string? displayName,
        string field = "password")
    {
        if (string.IsNullOrEmpty(password))
        {
            return [new ValidationFailure(field, "A password is required.")];
        }

        var length = Pbkdf2PasswordHasher.CountCodePoints(password);
        if (length < MinCodePoints)
        {
            return [new ValidationFailure(field, $"A password must be at least {MinCodePoints} characters.")];
        }

        if (length > Pbkdf2PasswordHasher.MaxCodePoints)
        {
            return [new ValidationFailure(field, $"A password must be at most {Pbkdf2PasswordHasher.MaxCodePoints} characters.")];
        }

        if (CommonPasswords.Value.Contains(password))
        {
            return [new ValidationFailure(field, "That password is too common. Choose one that is harder to guess.")];
        }

        // NIST "context-specific words": the account's own identifiers.
        if (EqualsIgnoringCase(password, email) || EqualsIgnoringCase(password, displayName))
        {
            return [new ValidationFailure(field, "A password must not be your email address or name.")];
        }

        return [];
    }

    private static bool EqualsIgnoringCase(string password, string? other) =>
        !string.IsNullOrWhiteSpace(other)
        && string.Equals(password.Trim(), other.Trim(), StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> LoadCommonPasswords()
    {
        // Embedded in the assembly (research R-15): the check works offline.
        using var stream = typeof(PasswordPolicy).Assembly.GetManifestResourceStream("QuickBuilder.Identity.CommonPasswords")
            ?? throw new InvalidOperationException(
                "The common-password list is missing from the assembly; the password policy cannot run.");
        using var reader = new StreamReader(stream);

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                set.Add(line);
            }
        }

        return set;
    }

    /// <summary>Exposed for tests: the number of entries loaded.</summary>
    internal static int CommonPasswordCount => CommonPasswords.Value.Count;
}
