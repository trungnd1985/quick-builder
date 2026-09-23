using System.Diagnostics.CodeAnalysis;

namespace QuickBuilder.Shared.Kernel.Results;

/// <summary>
/// The outcome of a handler: success, or an expected failure carrying a reason (FR-009).
/// </summary>
/// <remarks>
/// Unexpected failures are exceptions and are not modelled here — they propagate to the
/// host's exception middleware, which logs them with the trace id and returns a response
/// revealing nothing internal (FR-034). The point of this type is that controllers can
/// map an outcome to a status code without catching anything (FR-007).
/// </remarks>
public class Result
{
    protected Result(Error? error) => Error = error;

    /// <summary>Null when the operation succeeded.</summary>
    public Error? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => Error is not null;

    public static Result Success() => new(null);

    public static Result Failure(Error error) => new(error);

    public static Result<T> Success<T>(T value) => Result<T>.FromValue(value);

    public static Result<T> Failure<T>(Error error) => Result<T>.FromError(error);
}

/// <summary>A <see cref="Result"/> that carries a value when it succeeded.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, Error? error) : base(error) => _value = value;

    /// <summary>The value. Throws when the result is a failure — check first.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"Cannot read the value of a failed result ({Error}).");

    internal static Result<T> FromValue(T value) => new(value, null);

    internal static Result<T> FromError(Error error) => new(default, error);

    public static implicit operator Result<T>(T value) => FromValue(value);
}
