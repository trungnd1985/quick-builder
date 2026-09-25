using QuickBuilder.Shared.Kernel.Authorization;

namespace QuickBuilder.Media;

/// <summary>
/// What the Media module lets people do, by name (003 research M-18; 002 contracts/authorization.md §2).
/// </summary>
/// <remarks>
/// Registered from <see cref="MediaModule.AddMediaModule"/>; Identity enforces them without
/// knowing this module exists. Administrator holds all of them everywhere and is never
/// listed. The names never change once shipped: they are part of what a role means.
/// </remarks>
internal static class MediaCapabilities
{
    /// <summary>See a site's library and preview its files. Every site role.</summary>
    public const string View = "media.files.view";

    /// <summary>Upload to a site's library. Every site role.</summary>
    public const string Upload = "media.files.upload";

    /// <summary>Delete any file in a site's library. Designer (and Administrator).</summary>
    public const string Delete = "media.files.delete";

    /// <summary>
    /// Delete files one uploaded oneself. Every site role. The delete endpoint declares this
    /// weaker capability; the handler requires <see cref="Delete"/> for anyone else's file
    /// (clarification Q4).
    /// </summary>
    public const string DeleteOwn = "media.own_files.delete";

    public static IEnumerable<CapabilityDefinition> All =>
    [
        new(View, CapabilityScope.Site, [SystemRole.Designer, SystemRole.Editor]),
        new(Upload, CapabilityScope.Site, [SystemRole.Designer, SystemRole.Editor]),
        new(Delete, CapabilityScope.Site, [SystemRole.Designer]),
        new(DeleteOwn, CapabilityScope.Site, [SystemRole.Designer, SystemRole.Editor]),
    ];
}
