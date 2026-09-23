namespace QuickBuilder.Shared.Kernel.Results;

/// <summary>
/// An expected failure, carrying a machine-readable reason (FR-009).
/// </summary>
/// <remarks>
/// <see cref="Code"/> is a stable dotted identifier such as <c>sites.host_name_taken</c>.
/// It is part of the HTTP contract — clients branch on it — so changing one is a breaking
/// change. <see cref="Message"/> is for humans and may change freely.
/// </remarks>
public sealed record Error(string Code, string Message)
{
    /// <summary>The request was understood but a rule rejected it.</summary>
    public static Error Conflict(string code, string message) => new(code, message);

    /// <summary>The thing being addressed does not exist.</summary>
    public static Error NotFound(string code, string message) => new(code, message);

    /// <summary>The request itself was malformed or failed validation.</summary>
    public static Error Validation(string code, string message) => new(code, message);

    public override string ToString() => $"{Code}: {Message}";
}
