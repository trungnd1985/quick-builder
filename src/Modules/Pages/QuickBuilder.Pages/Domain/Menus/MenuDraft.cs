using QuickBuilder.Pages.Contracts;
using QuickBuilder.Shared.Kernel.MultiSite;

namespace QuickBuilder.Pages.Domain.Menus;

/// <summary>
/// The editable entries of one menu: a menu document (docs/contracts/menu.md), one per menu,
/// created with it (024 research NV-02).
/// </summary>
/// <remarks>
/// Like a component's draft, a separate row from its menu, so a rename never changes the
/// draft's <c>xmin</c>, which publishing compares to tell whether the draft has unpublished
/// changes. Not a <see cref="Draft"/>: that base type holds page-schema documents. It trusts
/// its input; the save handler validates first.
/// </remarks>
internal sealed class MenuDraft : ISiteOwned
{
    private MenuDraft(Guid siteId, Guid menuId, string document, int schemaVersion, Guid actor, DateTimeOffset now)
    {
        SiteId = siteId;
        MenuId = menuId;
        Document = document;
        SchemaVersion = schemaVersion;
        UpdatedAtUtc = now;
        UpdatedBy = actor;
    }

    // EF Core materialisation.
    private MenuDraft()
    {
        Document = MenuSchema.EmptyDocument;
    }

    public Guid MenuId { get; private set; }

    public Guid SiteId { get; private set; }

    /// <summary>Always equal to the document's own <c>schemaVersion</c>.</summary>
    public int SchemaVersion { get; private set; }

    /// <summary>The validated menu document, as JSON text (<c>jsonb</c>).</summary>
    public string Document { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid UpdatedBy { get; private set; }

    /// <summary>The concurrency token, mapped to <c>xmin</c>, sent to clients as <c>draftVersion</c>.</summary>
    public uint Version { get; private set; }

    /// <summary>A new menu's draft: no entries.</summary>
    public static MenuDraft CreateEmpty(Guid siteId, Guid menuId, Guid actor, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return new MenuDraft(siteId, menuId, MenuSchema.EmptyDocument, MenuSchema.CurrentVersion, actor, clock.GetUtcNow());
    }

    /// <summary>A copy of another menu's draft, for a duplicate (FR-001).</summary>
    public static MenuDraft CopyOf(MenuDraft source, Guid menuId, Guid actor, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(clock);
        return new MenuDraft(source.SiteId, menuId, source.Document, source.SchemaVersion, actor, clock.GetUtcNow());
    }

    /// <summary>Replaces the document with one that has already passed validation.</summary>
    public void Replace(string document, int schemaVersion, Guid actor, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrEmpty(document);
        ArgumentNullException.ThrowIfNull(clock);

        Document = document;
        SchemaVersion = schemaVersion;
        UpdatedAtUtc = clock.GetUtcNow();
        UpdatedBy = actor;
    }
}
