using Microsoft.Extensions.Options;
using QuickBuilder.Identity;
using QuickBuilder.Identity.Infrastructure.Security;
using Xunit;

namespace QuickBuilder.UnitTests.Identity;

/// <summary>
/// The password hasher (002 research R-03; spec FR-008, FR-009).
/// </summary>
public sealed class Pbkdf2PasswordHasherTests
{
    private static readonly Pbkdf2PasswordHasher Hasher = Create(IdentityOptions.PasswordSettings.MinimumIterations);

    [Fact]
    public void A_hashed_password_verifies_and_a_different_one_does_not()
    {
        var hash = Hasher.Hash("correct horse battery staple");

        Assert.True(Pbkdf2PasswordHasher.Verify("correct horse battery staple", hash));
        Assert.False(Pbkdf2PasswordHasher.Verify("correct horse battery stapler", hash));
    }

    [Fact]
    public void Two_hashes_of_one_password_differ_because_each_has_its_own_salt()
    {
        Assert.NotEqual(Hasher.Hash("same password twice"), Hasher.Hash("same password twice"));
    }

    [Fact]
    public void The_hash_carries_its_algorithm_and_cost()
    {
        var hash = Hasher.Hash("self-describing format");

        Assert.StartsWith("$pbkdf2-sha512$i=210000$", hash, StringComparison.Ordinal);
        Assert.Equal(5, hash.Split('$').Length);
    }

    /// <remarks>
    /// Documents the revised decision in research R-03: under InvariantGlobalization
    /// normalisation is a no-op, so the password is hashed exactly as typed.
    /// </remarks>
    [Fact]
    public void Passwords_are_hashed_exactly_as_typed()
    {
        var precomposed = "café au lait please";
        var decomposed = "café au lait please";

        var hash = Hasher.Hash(precomposed);

        Assert.True(Pbkdf2PasswordHasher.Verify(precomposed, hash));
        Assert.False(Pbkdf2PasswordHasher.Verify(decomposed, hash));
    }

    [Fact]
    public void A_128_character_password_verifies_and_is_never_truncated()
    {
        // Astral-plane characters are two UTF-16 units but one code point each: the limit
        // counts code points, as FR-008 counts characters.
        var password = string.Concat(Enumerable.Repeat("\U0001F600", 128));
        var hash = Hasher.Hash(password);

        Assert.True(Pbkdf2PasswordHasher.Verify(password, hash));
        Assert.False(Pbkdf2PasswordHasher.Verify(password[..^2], hash));
    }

    [Fact]
    public void A_129_character_password_is_rejected_rather_than_truncated()
    {
        var password = new string('a', 129);

        Assert.Throws<ArgumentException>(() => Hasher.Hash(password));
        Assert.False(Pbkdf2PasswordHasher.Verify(password, Hasher.Hash(new string('a', 128))));
    }

    [Fact]
    public void A_lower_cost_hash_needs_rehashing_and_a_current_one_does_not()
    {
        var old = Create(1_000).Hash("hashed long ago");

        Assert.True(Hasher.NeedsRehash(old));
        Assert.False(Hasher.NeedsRehash(Hasher.Hash("hashed today")));
        Assert.True(Pbkdf2PasswordHasher.Verify("hashed long ago", old));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("$pbkdf2-sha512$i=abc$c2FsdA==$aGFzaA==")]
    [InlineData("$pbkdf2-sha512$i=210000$not base64!$aGFzaA==")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$c2FsdA$aGFzaA")]
    public void A_malformed_stored_hash_never_verifies(string stored)
    {
        Assert.False(Pbkdf2PasswordHasher.Verify("anything at all", stored));
        Assert.True(Hasher.NeedsRehash(stored));
    }

    [Fact]
    public void Verifying_against_the_dummy_hash_always_fails()
    {
        Assert.False(Hasher.VerifyAgainstDummy("not a password; used to equalise timing"));
    }

    private static Pbkdf2PasswordHasher Create(int iterations)
    {
        var options = new IdentityOptions();
        options.Passwords.Pbkdf2Iterations = iterations;
        return new Pbkdf2PasswordHasher(Options.Create(options));
    }
}
