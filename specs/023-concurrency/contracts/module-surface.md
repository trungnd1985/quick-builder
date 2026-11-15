# Module Surface: Concurrent Editing Safety

What each module exposes to the others in this feature. Public interfaces live in the
module's `Contracts` project (constitution Principle I). Decisions: [research.md](../research.md).

## 1. Reference edges

| From (implementation) | To (Contracts) | Use | New edge? |
|---|---|---|---|
| `QuickBuilder.Pages` | `Editor.Contracts` | `IEditingLocks.CheckWriteAsync` in `DraftWrites` | No (exists) |
| `QuickBuilder.Cms` | `Editor.Contracts` | `IEditingLocks.CheckWriteAsync` in `ItemWrites` | **Yes** |
| `QuickBuilder.Editor` | `Pages.Contracts` | `PageDeleted`, `ComponentDeleted` handlers; `PageContentCapabilities` | No (exists) |
| `QuickBuilder.Editor` | `Cms.Contracts` | `ItemDeleted`, `CollectionDeleted` handlers; `ItemCapabilities` | **Yes** |
| `QuickBuilder.Editor` | `Identity.Contracts` | `IUserDirectory` for names | No (exists) |
| `QuickBuilder.Pages`, `QuickBuilder.Cms` | `Identity.Contracts` | `IUserDirectory` for `changedBy` | No (exist) |

`ModuleBoundaryTests` gains the two new allowed edges. `Editor.Contracts` still references
only the kernel (`Editor_contracts_reference_only_the_kernel`).

No transaction spans modules. The fencing check (CE-07) is a read in Editor's context made
before the owning module's own transaction. Its race with a takeover is harmless because
the version check still refuses the later save.

## 2. Editor.Contracts

```csharp
namespace QuickBuilder.Editor.Contracts;

/// The documents editing sessions are about (023 CE-01).
public static class EditingDocumentKinds
{
    public const string Page = "page";
    public const string Component = "component";
    public const string Item = "item";
}

/// The header the admin sends on draft saves (023 CE-07).
public static class EditingHeaders
{
    public const string Session = "X-QB-Editing-Session";
}

/// Whether a draft write from an editing session may proceed (023 CE-07, FR-018, FR-026).
public interface IEditingLocks
{
    /// Allowed when sessionId is null, when no live session holds the lock, or when
    /// sessionId is the holder. HeldByOther otherwise. One indexed read; never throws for
    /// an unknown session (it is simply not the holder).
    Task<EditingWriteCheck> CheckWriteAsync(
        Guid siteId, string documentKind, Guid documentId, Guid? sessionId,
        CancellationToken cancellationToken = default);
}

public sealed record EditingWriteCheck(bool Allowed, Guid? HolderUserId, DateTimeOffset? HolderSince)
{
    public static EditingWriteCheck Yes { get; } = new(true, null, null);
}
```

The calling module turns `HeldByOther` into `409 editing.lock_held` with the holder's display
name, which it resolves through `IUserDirectory`.

## 3. Cms.Contracts

```csharp
/// Item capabilities other modules check by name (023 CE-16); moved from CmsCapabilities.
public static class ItemCapabilities
{
    public const string CollectionsView = "cms.collections.view";
    public const string ItemsEdit = "cms.items.edit";
}
```

`CmsCapabilities.CollectionsView` and `ItemsEdit` become aliases of these, as
`PagesCapabilities` does for `PageContentCapabilities`.

## 4. Integration events consumed (no new events)

| Event | Producer | Editor's reaction |
|---|---|---|
| `PageDeleted` | Pages | Delete sessions of kind `page` for the page. |
| `ComponentDeleted` | Pages | Delete sessions of kind `component`. |
| `ItemDeleted` | Cms | Delete sessions of kind `item`. |
| `CollectionDeleted` | Cms | Delete item sessions with that `collection_id`. |

The handlers are idempotent (deleting what is not there is a no-op). They are registered in
`specs/001-foundation-skeleton/contracts/integration-events.md` as new consumers.

## 5. Site export (#20) obligations

- Copies are ordinary pages, components and items, so they are exported with their
  `copyOrigin`.
- `editing_sessions` and `editing_takeovers` are not exported. They are runtime and audit
  data, not content.

## 6. Added by 021

`IEditingLocks.FindActiveHolderAsync(siteId, kind, documentId, userId)` returns the member holding a live,
not stale lock when that member is not `userId`, as `EditingHolder(UserId, Since)`. Pages and Cms call it
before a delete and refuse with `editing.lock_held`. One indexed read, made before their transaction.
