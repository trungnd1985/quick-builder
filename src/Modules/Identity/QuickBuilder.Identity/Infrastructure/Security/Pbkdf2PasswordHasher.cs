using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace QuickBuilder.Identity.Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA512 password hashing through the BCL (research R-03; spec FR-009).
/// </summary>
/// <remarks>
/// Format: <c>$pbkdf2-sha512$i=&lt;iterations&gt;$&lt;salt-b64&gt;$&lt;hash-b64&gt;</c>. The algorithm
/// and cost travel with each hash, so raising the iteration count later upgrades stored
/// hashes transparently at the next sign-in (<see cref="NeedsRehash"/>), and a future
/// algorithm can coexist with this one.
/// <para>
/// Passwords are hashed exactly as typed. Unicode normalisation was planned but is a silent
/// no-op under the solution's invariant globalization (research R-03, revised).
/// </para>
/// </remarks>
internal sealed class Pbkdf2PasswordHasher
{
    public const int MaxCodePoints = 128;

    private const string Prefix = "$pbkdf2-sha512$";
    private const int SaltSize = 16;
    private const int KeySize = 64;

    private readonly int _iterations;
    private readonly Lazy<string> _dummyHash;

    public Pbkdf2PasswordHasher(IOptions<IdentityOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _iterations = options.Value.Passwords.Pbkdf2Iterations;

        // Computed once, lazily, with the configured cost: verifying against it takes the
        // same time as verifying a real account (research R-08).
        _dummyHash = new Lazy<string>(() => Hash("not a password; used to equalise timing"));
    }

    public int Iterations => _iterations;

    public static int CountCodePoints(string password) =>
        password.EnumerateRunes().Count();

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (CountCodePoints(password) > MaxCodePoints)
        {
            // Never truncate silently (FR-008). The policy rejects these before they get here.
            throw new ArgumentException($"A password may be at most {MaxCodePoints} characters.", nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Derive(password, salt, _iterations);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Prefix}i={_iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}");
    }

    public static bool Verify(string password, string storedHash)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (!TryParse(storedHash, out var iterations, out var salt, out var expected))
        {
            return false;
        }

        if (CountCodePoints(password) > MaxCodePoints)
        {
            // Cannot match any stored password, and hashing arbitrarily long input on request
            // is a free CPU sink for an attacker.
            return false;
        }

        var actual = Derive(password, salt, iterations);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>
    /// Runs a full verification against a hash no password matches, and returns false. Used
    /// when the account is missing or cannot sign in, so both cases take the same time.
    /// </summary>
    public bool VerifyAgainstDummy(string password)
    {
        Verify(password, _dummyHash.Value);
        return false;
    }

    /// <summary>True when the stored hash uses fewer iterations than configured.</summary>
    public bool NeedsRehash(string storedHash) =>
        !TryParse(storedHash, out var iterations, out _, out _) || iterations < _iterations;

    /// <summary>Times one hash at the configured cost (research R-03 safeguard 1).</summary>
    public TimeSpan MeasureOnce()
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        Derive("timing check", RandomNumberGenerator.GetBytes(SaltSize), _iterations);
        return System.Diagnostics.Stopwatch.GetElapsedTime(started);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA512, KeySize);

    private static bool TryParse(string? stored, out int iterations, out byte[] salt, out byte[] key)
    {
        iterations = 0;
        salt = [];
        key = [];

        if (stored is null || !stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        // "i=<n>$<salt>$<hash>"
        var parts = stored[Prefix.Length..].Split('$');
        if (parts.Length != 3
            || !parts[0].StartsWith("i=", StringComparison.Ordinal)
            || !int.TryParse(parts[0].AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
            || iterations <= 0)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[1]);
            key = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length > 0 && key.Length == KeySize;
    }
}
