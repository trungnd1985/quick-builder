using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace QuickBuilder.Identity.Infrastructure.Security;

/// <summary>
/// Random credentials and their stored form (research R-04, R-09, R-10).
/// </summary>
/// <remarks>
/// Every bearer credential — session, device and set-password tokens — is 32 random bytes,
/// base64url-encoded for the browser. The database only ever holds its SHA-256, so a
/// database read does not yield a usable credential.
/// </remarks>
internal static class Tokens
{
    /// <summary>Crockford base32: no I, L, O or U, so a code read aloud is unambiguous.</summary>
    private const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public const int SetupCodeLength = 12;

    public static string NewSessionToken() => NewToken();

    public static string NewLinkToken() => NewToken();

    public static string NewDeviceToken() => NewToken();

    public static byte[] Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    /// <summary>12 Crockford base32 characters: 60 bits of entropy (research R-09).</summary>
    public static string NewSetupCode() =>
        string.Create(SetupCodeLength, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = CrockfordAlphabet[RandomNumberGenerator.GetInt32(CrockfordAlphabet.Length)];
            }
        });

    /// <summary>
    /// Accepts what an operator is likely to type: any case, dashes and spaces, and the
    /// letters Crockford maps to digits (O→0, I and L→1).
    /// </summary>
    public static string NormaliseSetupCode(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (c is '-' or ' ')
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(c) switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                var other => other,
            });
        }

        return builder.ToString();
    }

    /// <summary>Formats a normalised code for display: <c>XXXX-XXXX-XXXX</c>.</summary>
    public static string FormatSetupCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return code.Length != SetupCodeLength
            ? code
            : $"{code[..4]}-{code[4..8]}-{code[8..]}";
    }

    /// <summary>Constant-time comparison of two normalised setup codes.</summary>
    public static bool SetupCodesMatch(string expected, string supplied) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied));

    private static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
}
