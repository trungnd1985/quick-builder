namespace QuickBuilder.Rendering.Contracts;

/// <summary>
/// A complete public answer: what the host writes, byte for byte (contracts/readers.md §1.4).
/// </summary>
/// <param name="StatusCode">The HTTP status.</param>
/// <param name="ContentType">The <c>Content-Type</c> header.</param>
/// <param name="Headers">Every other header to set.</param>
/// <param name="Body">The body; written only for methods that carry one.</param>
public sealed record PublicResponse(
    int StatusCode,
    string ContentType,
    IReadOnlyDictionary<string, string> Headers,
    ReadOnlyMemory<byte> Body);
