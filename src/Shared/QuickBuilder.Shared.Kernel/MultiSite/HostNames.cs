using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace QuickBuilder.Shared.Kernel.MultiSite;

/// <summary>
/// The one definition of a host name (009 research S-04).
/// </summary>
/// <remarks>
/// Storing, comparing and resolving host names all go through <see cref="TryNormalise"/>,
/// so the name an administrator types, the name the unique index compares and the name a
/// request resolves by can never disagree. Before this existed the rules were spread over
/// the Sites domain and the resolution middleware.
/// <para>
/// It lives in the kernel because both the host (resolution, admin host names) and the
/// Sites module need it, and it depends on nothing but the BCL.
/// </para>
/// <para>
/// <b>Invariant globalization.</b> The product runs with <c>InvariantGlobalization</c>, in
/// which <see cref="IdnMapping"/> converts to punycode but neither lowercases nor applies
/// Unicode normalisation. Lowercasing is done here explicitly. Normalisation is not
/// available in that mode, so a decomposed name (a "u" followed by a combining diaeresis)
/// produces a different ASCII form than the composed "ü". Browsers send the ASCII form in
/// <c>Host</c> anyway, and the admin shell normalises what an administrator types before
/// sending it; only other API clients sending decomposed Unicode are affected.
/// </para>
/// </remarks>
public static partial class HostNames
{
    /// <summary>The longest host name DNS allows, in ASCII form.</summary>
    public const int MaxLength = 253;

    private static readonly IdnMapping Idn = new() { UseStd3AsciiRules = true };

    private static readonly SearchValues<char> Structural = SearchValues.Create("/?#@*\\");

    /// <summary>
    /// Normalises a host name to its stored form: trimmed, lowercased, without a trailing
    /// dot, internationalised names in ASCII, IP literals in canonical form.
    /// </summary>
    /// <returns>
    /// False when the input is not a host name: it carries a scheme, port, path, query,
    /// user info or wildcard, contains whitespace, or breaks the label rules.
    /// </returns>
    public static bool TryNormalise(string? input, out string normalised)
    {
        normalised = string.Empty;

        var trimmed = input?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Any(char.IsWhiteSpace))
        {
            return false;
        }

        // Structure a host name never has. ':' is handled below, because IPv6 needs it.
        if (trimmed.AsSpan().IndexOfAny(Structural) >= 0)
        {
            return false;
        }

        if (trimmed.Contains(':', StringComparison.Ordinal) || trimmed.StartsWith('['))
        {
            return TryNormaliseIPv6(trimmed, out normalised);
        }

        // One trailing dot is the fully qualified spelling of the same name.
        if (trimmed.EndsWith('.'))
        {
            trimmed = trimmed[..^1];
        }

        // A name whose last label is numeric is read as an IPv4 address by browsers
        // (WHATWG URL), so it must be a valid one or nothing.
        var lastDot = trimmed.LastIndexOf('.');
        var lastLabel = lastDot < 0 ? trimmed : trimmed[(lastDot + 1)..];
        if (lastLabel.Length > 0 && lastLabel.All(char.IsAsciiDigit))
        {
            return TryNormaliseIPv4(trimmed, out normalised);
        }

        string ascii;
        try
        {
            // Lowercase first: in invariant mode IdnMapping does not case-map, so
            // "BÜCHER" and "bücher" would otherwise encode differently.
            ascii = Idn.GetAscii(trimmed.ToLowerInvariant());
        }
        catch (ArgumentException)
        {
            return false;
        }

        ascii = ascii.ToLowerInvariant();
        if (ascii.Length > MaxLength || !DnsName().IsMatch(ascii))
        {
            return false;
        }

        normalised = ascii;
        return true;
    }

    /// <summary>
    /// The readable form of a normalised host name, for display only. Falls back to the
    /// stored form when it cannot be decoded, so a screen listing it never fails.
    /// </summary>
    public static string ToDisplay(string normalised)
    {
        ArgumentNullException.ThrowIfNull(normalised);

        if (!normalised.Contains("xn--", StringComparison.Ordinal))
        {
            return normalised;
        }

        try
        {
            return Idn.GetUnicode(normalised);
        }
        catch (ArgumentException)
        {
            return normalised;
        }
    }

    private static bool TryNormaliseIPv4(string candidate, out string normalised)
    {
        normalised = string.Empty;

        // IPAddress.TryParse also accepts shorthand such as "1" or "1.2.3"; only the
        // dotted-quad form is a host name anyone types on purpose.
        if (!DottedQuad().IsMatch(candidate)
            || !IPAddress.TryParse(candidate, out var address)
            || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        normalised = address.ToString();
        return true;
    }

    private static bool TryNormaliseIPv6(string candidate, out string normalised)
    {
        normalised = string.Empty;

        var bracketed = candidate.StartsWith('[');
        if (bracketed)
        {
            // "[::1]" is the URL spelling; anything after the bracket would be a port.
            if (!candidate.EndsWith(']'))
            {
                return false;
            }

            candidate = candidate[1..^1];
        }

        // A zone id ("%eth0") is meaningful only on one machine, never in a Host header.
        if (candidate.Contains('%', StringComparison.Ordinal)
            || !IPAddress.TryParse(candidate, out var address)
            || address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        normalised = address.ToString().ToLowerInvariant();
        return true;
    }

    // Letters, digits and hyphens; 1–63 characters per label; no leading or trailing
    // hyphen. The overall 253-character limit is checked separately.
    [GeneratedRegex(
        @"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex DnsName();

    [GeneratedRegex(@"^\d{1,3}(\.\d{1,3}){3}$", RegexOptions.CultureInvariant)]
    private static partial Regex DottedQuad();
}
