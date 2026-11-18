# Data Model: Draft Preview Links

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

Three migrations, all additive and automatic:

| Module | Migration | Change |
|---|---|---|
| Editor | `AddPreviewLinks` | **+** `editor.preview_links`, **+** `editor.preview_link_failures` |
| Pages | `AddPreviewLinkLogActions` | **~** the `publish_log` action check gains `preview_link_created` and `preview_link_revoked`; **+** nullable `expires_at_utc` |
| Cms | `AddPreviewLinkHistoryKinds` | **~** the `item_history` kind check gains `previewLinkCreated` and `previewLinkRevoked` |

No document schema changes (`docs/contracts/` untouched) and no data rewrite.

---

## 1. `editor.preview_links` (entity `PreviewLink`, aggregate root)

One row per link ever created, until swept (PV-19).

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | `uuid` | no | PK, UUID v7 |
| `site_id` | `uuid` | no | site filter (`ISiteOwned`) |
| `document_kind` | `text` | no | `page` or `item` (`EditingDocumentKinds`); check constraint. Components are previewed through pages (spec Out of Scope). |
| `document_id` | `uuid` | no | the page or item |
| `version_number` | `integer` | yes | null: the draft; else that published version |
| `sample_item_id` | `uuid` | yes | template pages only: the item shown in the template (spec edge case). Null for other pages and items. If it is no longer published or present, the bar says so and the template's own draft fallback rule applies (PV-03). |
| `secret_hash` | `bytea` | no | SHA-256 of the secret (PV-07), 32 bytes; **unique** |
| `label` | `text` | yes | up to 80 characters; content, never logged and not in history |
| `created_by` | `uuid` | no | the creator; checked on every use (PV-08) |
| `created_at_utc` | `timestamptz` | no | |
| `expires_at_utc` | `timestamptz` | no | `created_at_utc` < this ≤ `created_at_utc + 30 days` (check constraint) |
| `last_opened_at_utc` | `timestamptz` | yes | updated at most once a minute (PV-08) |
| `revoked_at_utc` | `timestamptz` | yes | |
| `revoked_by` | `uuid` | yes | set with `revoked_at_utc` (check: both null or both set) |
| `xmin` | — | — | concurrency token |

**Indexes**:
- `ux_preview_links_secret_hash` (`secret_hash`): the per-request lookup.
- `ix_preview_links_document` (`site_id`, `document_kind`, `document_id`), partial
  `WHERE revoked_at_utc IS NULL`: the target's active list and the 20-link count.
- `ix_preview_links_site_active` (`site_id`, `expires_at_utc`), partial
  `WHERE revoked_at_utc IS NULL`: the site-wide list (FR-015).
- `ix_preview_links_sweep` (`expires_at_utc`): the sweep.

**Invariants (domain, `PreviewLink`)**:
- `Create(site, kind, documentId, version?, label?, expiresAt, actor, clock)`:
  - the expiry must be after now and at most 30 days ahead;
  - the label is trimmed, empty becomes null, and it allows at most 80 characters with no
    control characters;
  - returns the entity and the secret, which is never stored.
- `Revoke(actor, clock)`: does nothing on an already revoked link (idempotent), and raises
  `PreviewLinkRevoked` only the first time.
- `IsActive(now, validFrom)`: not revoked, `now < expires_at_utc`, and
  `created_at_utc >= validFrom` (PV-07).

**States**:

```text
            create                 revoke (member/Administrator)
  (none) ─────────▶ Active ───────────────────────────────▶ Revoked
                      │  now ≥ expires_at_utc                   │
                      ├────────────────────────▶ Expired        │
                      │  created before Preview:LinksValidFrom  │
                      ├────────────────────────▶ Invalidated    │
                      │                                         │
                      └── creator lost access / target gone ──▶ (Active row, resolves to "no longer available")
  Expired, Revoked: swept 30 days after expires_at_utc or revoked_at_utc (PV-19)
```

The creator losing access and the target going away are **not** stored states. They are
checked on every request (PV-08), so a restore from the trash or a regained role makes an
unexpired link work again, as the spec's edge cases say.

## 2. `editor.preview_link_failures` (PV-18)

| Column | Type | Notes |
|---|---|---|
| `client_key` | `bytea` | SHA-256 of `ClientAddress.Group(address)` |
| `window_start_utc` | `timestamptz` | the start of the minute or of the hour |
| `window_kind` | `smallint` | 1 = minute, 2 = hour |
| `count` | `integer` | incremented with `INSERT … ON CONFLICT DO UPDATE` |

PK (`client_key`, `window_kind`, `window_start_utc`). Installation data, not site-owned: a
prober is not tied to a site. The entity carries the `SiteReference` marker reason "rate
windows of anonymous clients, not site content". Swept hourly.

## 3. History (PV-16)

| Module | Table | New values | Fields used |
|---|---|---|---|
| Pages | `pages.publish_log` | action `preview_link_created`, `preview_link_revoked` | `subject_kind = page`, `subject_id`, `subject_label` and `subject_path` (as now, taken at the moment), `actor_user_id`, `version_number` (null for a draft link), `expires_at_utc` (new nullable column, set only on `preview_link_created`), `occurred_at_utc` |
| Cms | `cms.item_history` | kind `previewLinkCreated`, `previewLinkRevoked` | `item_id`, `by`, `version_number`, `due_at_utc` (reused for the link's expiry on `previewLinkCreated`), `at_utc` |

Entry ids are UUID v5 of the event id, which makes them idempotent. No secret, label or
link id is shown.

## 4. Read models (contracts, no tables)

### 4.1 `IPreviewContentReader` (Pages.Contracts.Publishing)

| Method | Returns |
|---|---|
| `GetPageDraftAsync(siteId, pageId)` | `PreviewPage?`: id, title, path (null for a template), `IsTemplate`, `CollectionId?`, `SchemaVersion`, `Document`, `SavedAtUtc`, `SavedBy`, `PendingScheduleAtUtc?`, `IsLive` |
| `GetPageVersionAsync(siteId, pageId, number)` | `PreviewPageVersion?`: number, title, document, `PublishedAtUtc`, `PublishedBy`; null when the page is absent or the version content is no longer retained |
| `GetUnpublishedComponentDraftsAsync(siteId, ids)` | drafts of the given components **that have no live version** |
| `GetUnpublishedMenuDraftsAsync(siteId, ids)` | the same for menus, with the menu name |
| `GetTemplateDraftAsync(siteId, collectionId)` | the collection's template page (`PreviewPage`), live or not; null when there is none |

### 4.2 `IPreviewItemReader` (Cms.Contracts.Publishing)

| Method | Returns |
|---|---|
| `GetDraftAsync(siteId, itemId)` | `PreviewItem?`: `Item` (a `PublishedItem` built from draft values and draft slug), `SavedAtUtc`, `SavedBy`, `NeedsAttention`, `PendingScheduleAtUtc?`, `IsPublished` |
| `GetVersionAsync(siteId, itemId, number)` | `PreviewItemVersion?`: `Item` from the version's values and slug, `PublishedAtUtc`, `PublishedBy` |

### 4.3 `IPreviewLinks` (Editor.Contracts)

| Method | Returns |
|---|---|
| `ResolveAsync(secret, clientAddress)` | `PreviewLinkResolution`, one of: `Found(LinkId, SiteId, Kind, DocumentId, VersionNumber?, ExpiresAtUtc)`, `NotAvailable` (unknown, revoked, expired, invalidated or creator without access) or `Throttled(RetryAfter)`. It counts failures and touches last-opened (PV-08, PV-18). |

### 4.4 `IMemberAccess` (Identity.Contracts)

`HoldsCapabilityAsync(userId, capability, siteId)`: the user is active and holds the
capability at the site, or is an Administrator. It reads grants on every call and is never
cached.

### 4.5 `IMediaContent` (Media.Contracts)

`OpenAsync(address)`: `MediaContent?` with the stream, content type, length, ETag and
last-modified, for the site in `ISiteContext`. It uses the same rules as `/_media`
(canonical address, current version, trash filter).

### 4.6 Rendering internals

- `PreviewContext`:
  - `Mode`: member or link;
  - `Target`: page, item or template with a sample item; draft or version;
  - `Bar` (`PreviewBarModel`);
  - `Addresses` (`PreviewAddresses`: media prefix, stylesheet prefix, page link base or
    none, pager base).
- `PreviewBarModel`:
  - what is shown: draft or version, time, author, version number;
  - the sample item name;
  - the parts shown from drafts (kind and name);
  - pending schedule;
  - needs attention;
  - expiry (links only);
  - editor and share addresses (members only).

## 5. Errors

| Code | HTTP | When |
|---|---|---|
| `preview.not_found` | 404 | the target is missing, trashed, of another site, or the caller is not a member (member preview, admin endpoints) |
| `preview.version_not_retained` | 404 | the version's content is gone |
| `preview.no_template` | 409 | the item's collection has no template page (spec edge case) |
| `preview.invalid_draft` | 422 | the page draft fails the page schema; carries the validator's reasons and locations (members only) |
| `preview.link_limit` | 409 | the target already has 20 active links |
| `preview.invalid_expiry` | 400 | the expiry is in the past or more than 30 days ahead |
| `preview.invalid_label` | 400 | the label is longer than 80 characters or has control characters |
| `preview.link_not_found` | 404 | revoking a link that is not the target's or not on the site |

Link viewers never see these codes. They get the PV-10 pages only: "no longer available"
(404), "not available right now" (503) or throttled (429).
