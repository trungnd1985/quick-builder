using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Identity.Domain;

internal enum UserStatus
{
    Active,
    Deactivated,
}

/// <summary>
/// A person who can sign in to the administrative surface (data-model.md, <c>users</c>).
/// </summary>
/// <remarks>
/// States: Active or Deactivated, each with or without a password. Only an active account
/// with a password can sign in; every other state fails sign-in exactly like a wrong
/// password (research R-08). Accounts are never deleted (spec FR-010), so past actions stay
/// attributable.
/// </remarks>
internal sealed class User
{
    public const int MaxEmailLength = 254;
    public const int MaxDisplayNameLength = 200;

    private User()
    {
        Email = string.Empty;
        DisplayName = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Trimmed; compared case-insensitively by the database.</summary>
    public string Email { get; private set; }

    public string DisplayName { get; private set; }

    /// <summary>Research R-03 format. Null until the first set-password link is used.</summary>
    public string? PasswordHash { get; private set; }

    public UserStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? LastSignInAtUtc { get; private set; }

    public DateTimeOffset? DeactivatedAtUtc { get; private set; }

    public bool HasPassword => PasswordHash is not null;

    public bool IsActive => Status == UserStatus.Active;

    public bool CanSignIn => IsActive && HasPassword;

    public static Result<User> Create(string displayName, string email, DateTimeOffset now)
    {
        var name = NormaliseDisplayName(displayName);
        var nameError = ValidateDisplayName(name);
        if (nameError is not null)
        {
            return Result.Failure<User>(nameError);
        }

        var normalisedEmail = NormaliseEmail(email);
        var emailError = ValidateEmail(normalisedEmail);
        if (emailError is not null)
        {
            return Result.Failure<User>(emailError);
        }

        return Result.Success(new User
        {
            Id = Guid.CreateVersion7(),
            Email = normalisedEmail,
            DisplayName = name,
            Status = UserStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
    }

    /// <summary>
    /// Trim only. Case-insensitivity comes from the <c>citext</c> column. There is no
    /// Unicode normalisation: under <c>InvariantGlobalization</c> it would be a silent
    /// no-op (research R-03).
    /// </summary>
    public static string NormaliseEmail(string? email) => (email ?? string.Empty).Trim();

    public static string NormaliseDisplayName(string? displayName) => (displayName ?? string.Empty).Trim();

    /// <summary>
    /// Syntactic check only: one <c>@</c>, non-empty local part and domain, no whitespace.
    /// Ownership is never verified — email is optional in this product (spec Assumptions).
    /// </summary>
    public static Error? ValidateEmail(string normalisedEmail)
    {
        if (normalisedEmail.Length == 0)
        {
            return Error.Validation("identity.email_required", "An email address is required.");
        }

        if (normalisedEmail.Length > MaxEmailLength)
        {
            return Error.Validation("identity.email_invalid", $"An email address must be at most {MaxEmailLength} characters.");
        }

        var at = normalisedEmail.IndexOf('@', StringComparison.Ordinal);
        var valid = at > 0
            && at == normalisedEmail.LastIndexOf('@')
            && at < normalisedEmail.Length - 1
            && !normalisedEmail.Any(char.IsWhiteSpace);

        return valid ? null : Error.Validation("identity.email_invalid", "That is not a valid email address.");
    }

    public static Error? ValidateDisplayName(string normalisedName)
    {
        if (normalisedName.Length == 0)
        {
            return Error.Validation("identity.display_name_required", "A name is required.");
        }

        return normalisedName.Length > MaxDisplayNameLength
            ? Error.Validation("identity.display_name_invalid", $"A name must be at most {MaxDisplayNameLength} characters.")
            : null;
    }

    public void SetPasswordHash(string passwordHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
        UpdatedAtUtc = now;
    }

    public Result ChangeEmail(string email, DateTimeOffset now)
    {
        var normalised = NormaliseEmail(email);
        var error = ValidateEmail(normalised);
        if (error is not null)
        {
            return Result.Failure(error);
        }

        Email = normalised;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result Rename(string displayName, DateTimeOffset now)
    {
        var normalised = NormaliseDisplayName(displayName);
        var error = ValidateDisplayName(normalised);
        if (error is not null)
        {
            return Result.Failure(error);
        }

        DisplayName = normalised;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    /// <summary>Idempotent. The caller revokes sessions, devices and links (data-model.md).</summary>
    public Result Deactivate(DateTimeOffset now)
    {
        if (Status == UserStatus.Deactivated)
        {
            return Result.Success();
        }

        Status = UserStatus.Deactivated;
        DeactivatedAtUtc = now;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    /// <summary>Idempotent. The existing password, if any, works again; old sessions do not.</summary>
    public Result Reactivate(DateTimeOffset now)
    {
        if (Status == UserStatus.Active)
        {
            return Result.Success();
        }

        Status = UserStatus.Active;
        DeactivatedAtUtc = null;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public void RecordSignIn(DateTimeOffset now) => LastSignInAtUtc = now;
}
