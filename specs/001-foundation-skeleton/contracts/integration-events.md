# Contract: Integration Events and the Outbox

**Feature**: `001-foundation-skeleton` | **Date**: 2026-09-22

This is the contract every module inherits for cross-module side effects. Getting it
right here is the point of User Story 3 — later modules do not redesign it, they publish
into it.

---

## Event envelope

Every event is stored and delivered with the same envelope:

| Field | Type | Meaning |
|---|---|---|
| `id` | uuid | Event identity. Stable across redeliveries — this is what idempotency keys on. |
| `eventType` | string | Stable contract name, `<Module>.<EventName>`, e.g. `Sites.SiteCreated` |
| `occurredAtUtc` | timestamp | When the state change happened, not when it was dispatched |
| `payload` | object | The event body, versioned by `eventType` |
| `traceId` | string \| null | Trace id of the originating request, so the dispatch correlates back to it |

**Naming rule**: `eventType` is a published contract. Renaming one is a breaking change;
introduce `Sites.SiteCreatedV2` alongside and retire the old name once no handler
subscribes to it.

---

## Events published by this feature

### `Sites.SiteCreated`

Published in the same transaction as the site insert (FR-015).

```json
{
  "id": "0195f3c2-8a1e-7c3d-9b44-2f1a6d5e8c90",
  "eventType": "Sites.SiteCreated",
  "occurredAtUtc": "2026-09-22T10:31:04.221Z",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "payload": {
    "siteId": "0195f3c2-8a1e-7c3d-9b44-2f1a6d5e8c90",
    "name": "Example",
    "hostNames": ["example.com"]
  }
}
```

**Payload rule**: carry identifiers and the few fields a handler needs to act, not the
whole aggregate. A handler that needs more reads through `Sites.Contracts`
(`ISiteDirectory`) — it must not read the `sites` schema directly.

**Who consumes it in this feature**: one test handler, which exists to prove delivery,
retry, and exactly-once effects. No production consumer yet — later modules subscribe when
they need to react to a new site.

---

## Events added by later features

Every event below follows the envelope, the payload rule and the handler contract of this
document. Payloads carry ids and numbers; a page's `path` is the one exception, because a
subscriber refreshing a page by address needs it.

### 012 Publishing and rendering (#12)

| Event | Published by | Payload | When |
|---|---|---|---|
| `Pages.PagePublished` | Pages | `siteId`, `pageId`, `versionNumber`, `kind` (`publish`, `scheduled`, `rollback`), `path`, `actorUserId` | A page version became live |
| `Pages.PageUnpublished` | Pages | `siteId`, `pageId`, `path`, `scheduled`, `actorUserId` | A page was taken down |
| `Pages.ComponentPublished` | Pages | `siteId`, `componentId`, `versionNumber`, `kind`, `actorUserId` | A component version became live |
| `Pages.ComponentUnpublished` | Pages | `siteId`, `componentId`, `actorUserId` | A component was taken down |
| `Editor.DesignPublished` | Editor | `siteId`, `versionNumber`, `kind` (`publish`, `rollback`), `restoredFromNumber`, `actorUserId` | A design version became live. Pages records it in the publish log. |
| `Media.FileDeleted` | Media | `siteId`, `fileId`, `actorUserId` | A media file was deleted. Pages refreshes the live pages that showed it. |
| `Media.FileChanged` (013) | Media | `siteId`, `fileId`, `reason` (`replaced`, `dimensions`, `variants`) | Something published pages show about a file changed: new contents at a new address, dimensions recorded after the upgrade, or resized copies withdrawn. Pages refreshes the live pages that show it, directly or through a component, with the same idempotency as `Media.FileDeleted`. Unknown reasons are treated like `replaced`. See 013 research MV-17. |

`actorUserId` is null when a schedule acted.

### 014 Collections and the schema builder (#14)

No subscriber yet; templates and binding (#16) will refresh live pages from them. See
[014 research CS-14](../../014-cms-collections-schema/research.md#cs-14-integration-events).

| Event | Published by | Payload | When |
|---|---|---|---|
| `Cms.CollectionCreated` | Cms | `siteId`, `collectionId`, `revision`, `actorUserId` | A collection was created, empty, at revision 1 |
| `Cms.CollectionChanged` | Cms | `siteId`, `collectionId`, `revision`, `schemaChanged`, `itemsRewritten`, `actorUserId` | A save produced a new revision. `schemaChanged` is false when only the name, address name or description changed; `itemsRewritten` says whether any stored item value, draft or published, was rewritten. |
| `Cms.CollectionDeleted` | Cms | `siteId`, `collectionId`, `actorUserId` | A collection and all its items were deleted |

### 015 Collection items (#15)

No subscriber yet; templates and binding (#16) will refresh live pages from the three that
change published data. Ids, numbers and flags only, never values, names or slugs. See
[015 research IT-17](../../015-cms-items/research.md#it-17-integration-events).

| Event | Published by | Payload | When |
|---|---|---|---|
| `Cms.ItemCreated` | Cms | `siteId`, `collectionId`, `itemId`, `actorUserId` | An item was created, including as a duplicate |
| `Cms.ItemDraftSaved` | Cms | `siteId`, `collectionId`, `itemId`, `actorUserId` | A draft was saved, or replaced by a kept version. Published data did not change. |
| `Cms.ItemPublished` | Cms | `siteId`, `collectionId`, `itemId`, `versionNumber`, `kind`, `slugChanged`, `actorUserId` | A publish, scheduled publish (`kind: scheduled`) or rollback made a new live version |
| `Cms.ItemUnpublished` | Cms | `siteId`, `collectionId`, `itemId`, `scheduled`, `actorUserId` | Published data was removed, by hand or by a schedule |
| `Cms.ItemDeleted` | Cms | `siteId`, `collectionId`, `itemId`, `wasPublished`, `actorUserId` | An item was deleted with its versions, schedules and history |

For a scheduled action, `actorUserId` is the member who set the schedule. Cms also consumes
`Media.FileDeleted` and `Media.FileChanged` to re-check the items that use the file.

**Module-internal message**: `Cms.ItemScheduleDue` (`siteId`, `scheduleId`, `revision`) is
written to the Cms outbox with `available_at_utc` set to the due time, as Pages does below;
only Cms handles it (015 research IT-09).

### 016 Collection templates and data binding (#16)

Pages now subscribes to the Cms events above that change what visitors see: `Cms.ItemPublished`,
`Cms.ItemUnpublished` and `Cms.ItemDeleted` (with `wasPublished`) refresh the pages listing the
item's collection; `Cms.CollectionChanged` (with `schemaChanged` or `itemsRewritten`) and
`Cms.CollectionDeleted` refresh every page that binds to or lists the collection, item pages
included. Each handler is idempotent through `pages.processed_events`. `Media.FileDeleted` and
`Media.FileChanged` also refresh the pages listing collections whose published items show the
file. See [016 research TB-10](../../016-collection-templates-binding/research.md#tb-10-caching-and-invalidation-generations-for-pages-render-stamps-for-items).

| Event | Published by | Payload | When |
|---|---|---|---|
| `Pages.PageTemplateChanged` | Pages | `siteId`, `pageId`, `collectionId`, `previousCollectionId`, `actorUserId` | A page became a collection's template, changed collection, or stopped being one. Its address change is announced by `Pages.PageAddressesChanged`, which now also covers template patterns. |

**Module-internal message**: `Pages.PublishScheduleDue` (`siteId`, `scheduleId`, `revision`)
is written to the Pages outbox with `available_at_utc` set to the schedule's due time, and
only Pages handles it (012 research PR-14). It is not a contract: no other module may
subscribe to it.

### 017 SEO, sitemap and redirects (#17)

Cms asks Pages for a redirect when a member accepts the offer for an item's old address: on
a publish, scheduled publish or rollback that changes the published slug (`moved`), and on an
unpublish, scheduled unpublish or delete with a page chosen (`removed`). The event is written
to the Cms outbox in the transaction of that change. Pages builds the old address from the
collection's live template pattern, creates the redirect (origin `offer`), retargets `path`
redirects to the old address, turns off any that would loop, and records each action in
`pages.redirect_changes`. It is idempotent through `pages.processed_events` and the unique
source index. See [017 research SE-11](../../017-seo/research.md).

| Event | Published by | Payload | When |
|---|---|---|---|
| `Cms.ItemRedirectRequested` | Cms | `siteId`, `collectionId`, `itemId`, `oldSlug`, `reason` (`moved`, `removed`), `targetPageId`, `statusCode` (`301`), `actorUserId` | A member accepted a redirect offer for an item's old address. |

- **`oldSlug`** is the only slug any event carries. It is an address, and it is sent only
  because the member asked for the redirect. It is content: handlers must never log it.
- **New subscriptions in Pages**:
  - `Cms.ItemRedirectRequested` creates the redirect.
  - `Cms.ItemPublished`, `Cms.ItemUnpublished`, `Cms.ItemDeleted`, `Cms.CollectionChanged` and
    `Cms.CollectionDeleted` now also move the site's sitemap generation (SE-15).
  - `Media.FileChanged` and `Media.FileDeleted` also refresh every live page when the file is
    the site's default sharing image or organisation logo (SE-20, SE-22).


### 023 Concurrent editing safety (#23)

No new event. Editing sessions are runtime state in the Editor module.

- **New subscriptions in Editor**: `Pages.PageDeleted`, `Pages.ComponentDeleted`,
  `Cms.ItemDeleted` and `Cms.CollectionDeleted` end the editing sessions of the deleted
  document (`ReleaseDeletedDocumentsHandler`); deleting what is already gone changes nothing, so
  the handler is idempotent without a processed-events table.

### 024 Navigation and menus (#24)

No new event. Menus publish as a third Pages subject (`menu`), but a menu publish or unpublish
writes nothing to the outbox: no other module needs to know, and the pages that show the menu
are refreshed through the dependency graph in the transaction of the change (024 research NV-07).

- **New subscriptions in Pages**: `Cms.ItemPublished`, `Cms.ItemUnpublished` and
  `Cms.ItemDeleted` (with `wasPublished`) also refresh the live pages that show a menu pointing at
  the item, directly or through a component (`RefreshPagesShowingMenuItemsHandler`). The handler
  is idempotent through `pages.processed_events`, under handler names of its own.

### 018 Forms and submissions (#18)

No new event: nothing needs to know about submissions yet (webhooks are out of scope).

- **New subscriptions in Forms**: `Pages.PagePublished`, `Pages.PageUnpublished`,
  `Pages.ComponentPublished` and `Pages.ComponentUnpublished` keep the form registry
  (`forms.forms`, `forms.form_placements`) in step with what is live
  (`RefreshFormRegistryHandler`, 018 research FS-05). The handler reads the subject's live
  document when it runs rather than the payload, under a transaction lock per subject, so it
  is idempotent without a processed-events row and indifferent to delivery order. It runs in a
  scope resolved to the event's site, because it reads through the public path's reader.

### 020 Site export and import (#20)

Four new events, published by Sites when a transfer ends or its archive is used. They carry
ids, the mode, the outcome and a failure code only, never a name, title or content
(Principle IX). See [020 research ST-19](../../020-site-export-import/research.md).

| Event | When | Payload beyond the envelope |
|---|---|---|
| `Sites.SiteExportFinished` | an export ends, whatever the outcome | `transferId`, `siteId`, `outcome` (`succeeded`, `failed`, `cancelled`), `trigger` (`admin`, `cli`, `pre_restore`), `actorUserId` (null for the command), `failureCode` |
| `Sites.SiteImportFinished` | an import ends, whatever the outcome | `transferId`, `siteId` (null when a new site was never committed), `mode` (`new_site`, `merge`, `restore`), `outcome`, `trigger`, `actorUserId`, `failureCode` |
| `Sites.SiteArchiveDownloaded` | an Administrator downloads an export | `transferId`, `siteId`, `actorUserId` |
| `Sites.SiteArchiveDeleted` | a stored archive is deleted, by an Administrator or by retention (`actorUserId` null) | `transferId`, `siteId`, `actorUserId` |

- **New subscriptions in Identity**: all four are recorded as security events
  (`site_exported`, `site_imported`, `site_restored`, `site_transfer_failed`,
  `site_transfer_cancelled`, `site_archive_downloaded`, `site_archive_deleted`) by
  `RecordSiteSecurityEvents`.
- **Existing events raised by an import**: each module's part writes the events its own writes
  would write, in the import's single transaction, so they are dispatched only after the commit
  and the usual consumers rebuild their state (cache, sitemap, form registry, list refresh,
  media variants). See [020 contracts/archive-parts.md](../../020-site-export-import/contracts/archive-parts.md) §2.

---

## Delivery guarantees

| Guarantee | How | Requirement |
|---|---|---|
| Atomic with the state change | Outbox row written on the module's own `DbContext`, one `SaveChanges` | FR-015 |
| At-least-once delivery | Row survives restart; not completed until the handler succeeded | FR-016 |
| Effectively-once effect | Handler's effect and its `processed_events` row commit together | FR-017 |
| One claimant at a time | `FOR UPDATE SKIP LOCKED`; a dead instance's rows unlock on rollback | FR-019 |
| Failures isolated | Backoff, then terminal `failed` state; other rows keep flowing | FR-018 |
| Bounded growth | Completed rows swept after the retention window | FR-020 |
| No cross-module transaction | Each module dispatches from its own outbox, its own context | FR-021 |

**Explicitly not guaranteed**: ordering. Events are claimed in `available_at_utc` order
but processed concurrently and retried independently, so a handler must not assume it sees
two events in the order they were published. Say so in any handler that would care.

---

## Handler contract

A handler implements:

```csharp
public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}
```

Rules every handler must satisfy — each is enforced by the dispatcher, a test, or both:

1. **Idempotent.** It may be called again with the same event id. The dispatcher's
   `processed_events` insert covers effects written through the same `DbContext`; a
   handler causing effects outside that transaction is responsible for its own
   deduplication and must say so in a comment.
2. **Short.** It runs inside the claim transaction (D-02), so it holds a row lock and a
   connection. The handler timeout applies; work that cannot finish quickly should enqueue
   rather than block.
3. **Self-contained.** It must not reach into another module's schema. Cross-module reads
   go through that module's `Contracts` project.
4. **Quiet about content.** Errors recorded in `last_error` carry the exception type and
   message only — never the payload (FR-032).

---

## Failure semantics

```text
attempt fails → attempts += 1
              → available_at_utc = now + backoff(attempts)
              → retry until attempts = MaxAttempts (default 5)
              → then failed_at_utc set, last_error recorded, row is terminal
```

A terminal `failed` row needs operator attention: it is logged at error level with its
event id, event type and trace id. There is no automatic requeue and no admin requeue
screen in this feature — deliberately, because no spec needs one yet (Principle XI).
Recovery today is a database update; a proper requeue path arrives with whichever spec
first needs it.

---

## Configuration keys

All documented in `docs/configuration.md`, all overridable by environment variable.

| Key | Default | Effect |
|---|---|---|
| `Outbox:PollInterval` | `00:00:05` | How often an idle dispatcher looks for work |
| `Outbox:BatchSize` | `50` | Rows claimed per pass |
| `Outbox:HandlerTimeout` | `00:00:30` | Per-event handler timeout |
| `Outbox:MaxAttempts` | `5` | Attempts before terminal failure |
| `Outbox:RetentionPeriod` | `7.00:00:00` | How long completed rows are kept |
| `Outbox:Enabled` | `true` | Turns the dispatcher off (useful in tests) |

## Trash and restore (021)

**Changed meaning.** These four are now published when content is moved to the trash, because their
consumers must react then. Their shape is unchanged. A later purge publishes nothing.

| Event | Now means |
|---|---|
| `Pages.PageDeleted` | Moved to the trash |
| `Pages.ComponentDeleted` | Moved to the trash |
| `Cms.ItemDeleted` | Moved to the trash; `WasPublished` says it was live |
| `Media.FileDeleted` | Moved to the trash |

**New**, each carrying ids only:

| Event | Owner | Payload | Meaning and consumers |
|---|---|---|---|
| `Pages.PageRestored` | Pages | `siteId`, `pageId`, `actorUserId` | Taken out of the trash, unpublished. No consumers yet. |
| `Pages.ComponentRestored` | Pages | `siteId`, `componentId`, `actorUserId` | As above. |
| `Cms.ItemRestored` | Cms | `siteId`, `collectionId`, `itemId`, `actorUserId` | As above. |
| `Media.FileRestored` | Media | `siteId`, `fileId`, `actorUserId` | Served again at once. Pages refreshes the live pages showing it; Cms re-checks the items using it. |
