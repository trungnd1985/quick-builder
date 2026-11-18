using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace QuickBuilder.Editor.Domain;

/// <summary>
/// Preview link secrets and their stored form (022 research PV-07), as Identity keeps its bearer
/// credentials (002 research R-04).
/// </summary>
/// <remarks>
/// 32 random bytes, base64url without padding: 43 characters that cannot be guessed and that
/// carry nothing about the site, page or item. The database holds only the SHA-256, so reading
/// it yields no usable link.
/// </remarks>
internal static class PreviewSecrets
{
    public const int Length = 43;

    public static string New() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    /// <summary>Whether <paramref name="secret"/> has the shape of a secret: worth a lookup at all.</summary>
    public static bool IsWellFormed(string? secret) =>
        secret is { Length: Length } && secret.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
