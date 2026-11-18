namespace QuickBuilder.Editor.Infrastructure;

/// <summary>
/// How many unknown preview links one address group tried in one window (022 data-model §2,
/// research PV-18). The address is kept only as a SHA-256 of its group, and windows are swept
/// within the hour.
/// </summary>
internal sealed class PreviewLinkFailureWindow
{
    public const short Minute = 1;

    public const short Hour = 2;

    public byte[] ClientKey { get; set; } = [];

    /// <summary><see cref="Minute"/> or <see cref="Hour"/>.</summary>
    public short WindowKind { get; set; }

    public DateTimeOffset WindowStartUtc { get; set; }

    public int Count { get; set; }
}
