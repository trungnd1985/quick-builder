# Module Surface: Navigation and Menus

What each module exposes to the others in this feature. Public interfaces live in the module's
`Contracts` project (constitution Principle I). Decisions: [research.md](../research.md).

## 1. Reference edges

**No new edges.** Every use runs over an existing implementation-to-Contracts reference:

| From (implementation) | To (Contracts) | New use |
|---|---|---|
| `QuickBuilder.Rendering` | `Pages.Contracts` | `GetLiveMenusAsync`; `PageLinkTarget.LiveTitle` |
| `QuickBuilder.Rendering` | `Cms.Contracts` | existing `IPublishedItemReader.GetAsync` for item targets |
| `QuickBuilder.Pages` | `Cms.Contracts` | existing `GetItemAddressPartsAsync` (save check, target states); handlers for `ItemPublished`, `ItemUnpublished`, `ItemDeleted` |

Cms and Editor gain nothing. The admin calls the menu `references` endpoint for the item
warnings (NV-19), so Cms needs no server read of menus.

## 2. Pages.Contracts

```csharp
public interface IPublishedContentReader
{
    // … existing members …

    /// Live versions of the given menus, with the name each had when published (024 NV-05, NV-09);
    /// ids without a live version are absent. Read-only connection, one query.
    Task<IReadOnlyDictionary<Guid, LiveMenu>> GetLiveMenusAsync(
        Guid siteId, IReadOnlyCollection<Guid> menuIds, CancellationToken cancellationToken = default);
}

/// A live menu document and the name it was published with.
public sealed record LiveMenu(int SchemaVersion, JsonElement Document, string Name);

/// A page a link or menu entry points at (012; 024 adds LiveTitle).
/// LiveTitle: the live version's title, for menu entries without their own label; null when not live.
public sealed record PageLinkTarget(string Path, bool IsLive, string? LiveTitle = null);

/// The menu document contract, embedded like PageSchema (024 NV-03).
public static class MenuSchema
{
    public const int CurrentVersion = 1;
    public static ReadOnlySpan<byte> V1 { get; }
}
```

`PageLinkTarget` gains a trailing optional parameter, so existing callers compile unchanged.

## 3. Cms.Contracts

Unchanged. `ItemAddressParts` already gives the collection and published slug; the admin's
target states (data-model §4.3) use it plus the live template patterns. The item name shown in
the admin comes from `PublishedItem.Name` on the public path, and from the Cms item API in the
admin.

## 4. Events

| Event | Producer | New consumer | Effect |
|---|---|---|---|
| `Cms.ItemPublished` | Cms | `Pages.RefreshPagesShowingMenuItemsHandler` | bumps `(item, ItemId)` |
| `Cms.ItemUnpublished` | Cms | same | same |
| `Cms.ItemDeleted` (`WasPublished`) | Cms | same | same |

No event is produced for menu changes (data-model §6). `specs/001-foundation-skeleton/contracts/integration-events.md`
gains the consumer rows and the note that menu publishes emit no event.

## 5. Site export obligations (#20)

A site export MUST carry, per menu: id, name, draft document, live version number, every
retained version (document, number, kind, published by and at) and pending schedules. On
import:
- menu documents' `pageId`, `itemId` and `collectionId` are remapped with the page, item and
  collection id maps;
- Navigation blocks' `menuId` in page and component documents (drafts and versions) is
  remapped with the menu id map;
- `menu_targets` and `menu_usages` are rebuilt from the imported documents, not imported.

Recorded in `docs/backlog.md` under #20.

## 6. Capabilities

| Capability | Holder | Grants |
|---|---|---|
| `pages.menus.manage` (new) | Designer, Administrator | create, rename, duplicate, delete menus; save menu drafts |
| `pages.content.view` (existing) | every site role | list, get, preview, usages, references, history, versions |
| `pages.publishing.manage` (existing) | Designer, Administrator | publish, unpublish, rollback, schedule menus |

Copying a menu version into the draft needs `pages.menus.manage`, as copying a component
version needs `pages.components.manage`.

`specs/002-identity-setup/contracts/authorization.md` §2 gains the new row.
