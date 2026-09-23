using System.Collections.ObjectModel;

namespace QuickBuilder.Shared.Kernel.Validation;

/// <summary>One thing wrong with one input.</summary>
public sealed record ValidationFailure(string Field, string Message);

/// <summary>
/// The outcome of validating a request: either nothing is wrong, or a list of what is.
/// </summary>
public sealed class ValidationResult
{
    private static readonly ReadOnlyCollection<ValidationFailure> NoFailures =
        new([]);

    private ValidationResult(IReadOnlyList<ValidationFailure> failures) => Failures = failures;

    public IReadOnlyList<ValidationFailure> Failures { get; }

    public bool IsValid => Failures.Count == 0;

    public static ValidationResult Valid() => new(NoFailures);

    public static ValidationResult Invalid(params ValidationFailure[] failures) =>
        new(new ReadOnlyCollection<ValidationFailure>([.. failures]));

    public static ValidationResult Invalid(IEnumerable<ValidationFailure> failures) =>
        new(new ReadOnlyCollection<ValidationFailure>([.. failures]));

    /// <summary>Groups failures by field, which is the shape the HTTP contract wants.</summary>
    public IDictionary<string, string[]> ToDictionary() =>
        Failures
            .GroupBy(f => f.Field, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(f => f.Message).ToArray(), StringComparer.Ordinal);
}

/// <summary>
/// Validates one request type before its handler runs (FR-008, D-05).
/// </summary>
/// <remarks>
/// In-house rather than FluentValidation: research.md D-05 records the reasoning —
/// dependency minimisation and redistribution-licence risk, not a quality judgement.
/// A command with no registered validator is valid by default.
/// </remarks>
public interface IValidator<in T>
{
    ValidationResult Validate(T instance);
}
