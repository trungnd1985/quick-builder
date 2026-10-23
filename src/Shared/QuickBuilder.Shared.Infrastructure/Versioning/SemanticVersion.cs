using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace QuickBuilder.Shared.Infrastructure.Versioning;

/// <summary>
/// A Semantic Versioning 2.0 version: <c>major.minor.patch[-preRelease][+build]</c>
/// (019 research RL-01).
/// </summary>
/// <remarks>
/// Build metadata is accepted and dropped: SemVer §10 says it plays no part in precedence, and
/// the revision a build carries is read separately by <see cref="ApplicationVersion"/>.
/// <para>
/// The release tool links this file rather than referencing Shared.Infrastructure, so the
/// server and the release pipeline order versions identically. Keep it free of dependencies.
/// </para>
/// </remarks>
public readonly record struct SemanticVersion : IComparable<SemanticVersion>
{
    public SemanticVersion(int major, int minor, int patch, string? preRelease = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(patch);

        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = string.IsNullOrEmpty(preRelease) ? null : preRelease;
    }

    /// <summary><c>0.0.0</c>: no release, and the floor of every 0.x build.</summary>
    public static SemanticVersion Zero { get; } = new(0, 0, 0);

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>The pre-release label without its hyphen (<c>dev</c>, <c>rc.1</c>), or null.</summary>
    public string? PreRelease { get; }

    public bool IsPreRelease => PreRelease is not null;

    public static SemanticVersion Parse(string text) =>
        TryParse(text, out var version)
            ? version
            : throw new FormatException($"'{text}' is not a semantic version (major.minor.patch[-pre][+build]).");

    public static bool TryParse([NotNullWhen(true)] string? text, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var plus = text.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            if (!AreIdentifiers(text[(plus + 1)..], numericLeadingZerosAllowed: true))
            {
                return false;
            }

            text = text[..plus];
        }

        string? preRelease = null;
        var hyphen = text.IndexOf('-', StringComparison.Ordinal);
        if (hyphen >= 0)
        {
            preRelease = text[(hyphen + 1)..];
            if (!AreIdentifiers(preRelease, numericLeadingZerosAllowed: false))
            {
                return false;
            }

            text = text[..hyphen];
        }

        var parts = text.Split('.');
        if (parts.Length != 3
            || !TryParseNumber(parts[0], out var major)
            || !TryParseNumber(parts[1], out var minor)
            || !TryParseNumber(parts[2], out var patch))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, preRelease);
        return true;
    }

    /// <summary>
    /// Compares major.minor only. The startup check judges "newer" this way: a patch never
    /// changes stored data, so 1.4.2 may run on a database 1.4.3 migrated (clarification Q1).
    /// </summary>
    public int CompareMajorMinor(SemanticVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major) : Minor.CompareTo(other.Minor);

    public int CompareTo(SemanticVersion other)
    {
        if (Major != other.Major)
        {
            return Major.CompareTo(other.Major);
        }

        if (Minor != other.Minor)
        {
            return Minor.CompareTo(other.Minor);
        }

        if (Patch != other.Patch)
        {
            return Patch.CompareTo(other.Patch);
        }

        // SemVer §11.3: a pre-release sorts below the release it leads to.
        return (PreRelease, other.PreRelease) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            _ => ComparePreRelease(PreRelease, other.PreRelease),
        };
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    public static SemanticVersion Max(SemanticVersion left, SemanticVersion right) => left >= right ? left : right;

    public override string ToString() =>
        PreRelease is null
            ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}")
            : string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}-{PreRelease}");

    /// <summary>SemVer §11.4: identifier by identifier; numbers numerically and below text.</summary>
    private static int ComparePreRelease(string left, string right)
    {
        var a = left.Split('.');
        var b = right.Split('.');

        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aIsNumber = int.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out var aNumber);
            var bIsNumber = int.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out var bNumber);

            var result = (aIsNumber, bIsNumber) switch
            {
                (true, true) => aNumber.CompareTo(bNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(a[i], b[i]),
            };

            if (result != 0)
            {
                return result;
            }
        }

        return a.Length.CompareTo(b.Length);
    }

    private static bool TryParseNumber(string text, out int value)
    {
        value = 0;
        return text.Length > 0
            && (text.Length == 1 || text[0] != '0')
            && text.All(char.IsAsciiDigit)
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool AreIdentifiers(string text, bool numericLeadingZerosAllowed)
    {
        if (text.Length == 0)
        {
            return false;
        }

        foreach (var identifier in text.Split('.'))
        {
            if (identifier.Length == 0 || !identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                return false;
            }

            if (!numericLeadingZerosAllowed
                && identifier.Length > 1
                && identifier[0] == '0'
                && identifier.All(char.IsAsciiDigit))
            {
                return false;
            }
        }

        return true;
    }
}
