namespace QuickBuilder.Cms.Contracts;

/// <summary>
/// The Cms capabilities other modules ask about, by name (023 research CE-16).
/// </summary>
/// <remarks>
/// Registered by the Cms module (014 research CS-15). They live in the contract because the
/// Editor module decides who may view or edit an item in an editing session. The names have
/// not changed since 014 and 015, and never will: they are part of what a role means.
/// </remarks>
public static class ItemCapabilities
{
    /// <summary>See collections, their schemas and items. Every site role.</summary>
    public const string CollectionsView = "cms.collections.view";

    /// <summary>Create, save, duplicate and delete items. Every site role.</summary>
    public const string ItemsEdit = "cms.items.edit";
}
