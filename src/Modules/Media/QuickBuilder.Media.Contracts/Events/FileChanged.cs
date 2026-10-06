using QuickBuilder.Shared.Kernel.Events;

namespace QuickBuilder.Media.Contracts.Events;

/// <summary>
/// Something about a media file that published pages show has changed (013 research MV-17,
/// contracts/readers.md §1.2). Published in the same transaction as the change (001 FR-015).
/// </summary>
/// <remarks>
/// The Pages module refreshes the live pages that show the file, directly or through a
/// component, exactly as for <see cref="FileDeleted"/>, so they render the change at their
/// next request on every instance. Carries ids and a reason only, never the file's name,
/// alternative text or content.
/// <para>
/// <c>Media.FileChanged</c> is a published contract name. Renaming it is a breaking change.
/// A new <see cref="Reason"/> value is additive: subscribers treat unknown reasons like
/// <see cref="FileChangeReasons.Replaced"/>.
/// </para>
/// </remarks>
/// <param name="Reason">One of <see cref="FileChangeReasons"/>.</param>
public sealed record FileChanged(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid SiteId,
    Guid FileId,
    string Reason) : IIntegrationEvent
{
    public static string EventType => "Media.FileChanged";
}

/// <summary>Why a <see cref="FileChanged"/> was published.</summary>
public static class FileChangeReasons
{
    /// <summary>New contents, at a new address (013 FR-019).</summary>
    public const string Replaced = "replaced";

    /// <summary>An existing image's dimensions were recorded after the upgrade (013 MV-10).</summary>
    public const string Dimensions = "dimensions";

    /// <summary>Resized copies of the file stopped being offered (013 MV-12).</summary>
    public const string Variants = "variants";
}
