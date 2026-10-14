# Data Model: SEO, Sitemap and Redirects

**Feature**: `017-seo` | **Date**: 2026-10-13

Tables, document additions, derived records, error codes, events and cache keys. Decisions
are in [research.md](./research.md) (`SE-xx`). Symbols: **+** new, **~** changed.

---

## 1. Tables

### 1.1 `pages.redirects` + (Pages, SE-09)

| Column | Type | Rule |
|---|---|---|
| `id` | `uuid` pk | `Guid.CreateVersion7()` |
| `site_id` | `uuid not null` | global site filter (Principle V) |
| `match_kind` | `text not null` | `exact` or `prefix` |
| `source_path` | `text not null` | normalised: lower-case, no trailing slash, ≤1,024 |
| `source_query` | `text not null` | canonical query (SE-09), ≤512; empty when the source has none (so one plain unique index works); non-empty only when `match_kind = exact` |
| `status_code` | `smallint not null` | `301` or `302` |
| `target_kind` | `text not null` | `page`, `item`, `path`, `url`; `prefix` allows `page` and `path` only |
| `target_page_id` | `uuid null` | set when `target_kind = page` |
| `target_collection_id`, `target_item_id` | `uuid null` | set together when `target_kind = item` |
| `target_path` | `text null` | site path with optional query when `target_kind = path` |
| `target_url` | `text null` | absolute `http`/`https`, ≤2,048, no user info, when `target_kind = url` |
| `enabled` | `boolean not null` | default `true` |
| `note` | `text null` | ≤500 |
| `origin` | `text not null` | `manual`, `import`, `offer` |
| `created_at`, `created_by`, `updated_at`, `updated_by` | | as other admin records |
| `xmin` | | concurrency token (`version` in the API) |

| Index | Purpose |
|---|---|
| unique `(site_id, match_kind, source_path, source_query)` | one redirect per source (FR-017) |
| `(site_id, target_page_id) where target_page_id is not null` | breaking and retargeting by page |
| `(site_id, target_path) where target_path is not null` | retargeting after address changes (SE-11) |

Check constraints hold the target-kind columns together and the `prefix` restrictions.
Count per site ≤10,000, checked by the handlers.

### 1.2 `pages.redirect_usage` + (Pages, SE-12)

| Column | Type | Rule |
|---|---|---|
| `redirect_id` | `uuid` pk, fk → `redirects` on delete cascade | |
| `site_id` | `uuid not null` | site filter |
| `hits` | `bigint not null` | added to by the flusher |
| `last_used_at` | `timestamptz not null` | `greatest(old, new)` |

### 1.3 `pages.sitemap_state` + (Pages, SE-15)

| Column | Type | Rule |
|---|---|---|
| `site_id` | `uuid` pk | site filter |
| `generation` | `bigint not null` | bumped in the transaction of each change in SE-15 |

### 1.4 `sites.sites` ~ (Sites, SE-07, SE-08)

| Column | Type | Rule |
|---|---|---|
| **+** `search_title_suffix` | `text null` | ≤60 characters; no control characters |
| **+** `search_default_image` | `uuid null` | media id; not checked by Sites (SE-08) |
| **+** `search_org_name` | `text null` | ≤200 |
| **+** `search_org_logo` | `uuid null` | media id |
| **+** `search_discouraged` | `boolean not null default false` | FR-027 |
| **+** `search_robots_rules` | `text null` | ≤10 KB, lines ≤500, `\n` line ends |
| **+** `redirect_other_hosts` | `boolean not null default false` | `true` for sites created after the upgrade (SE-07) |

| **+** `search_updated_by` | `uuid null` | who last changed the search settings or search visibility (FR-021); `UpdatedAtUtc` gives when |

Every change moves `xmin`, which is `SiteVersion` in every public cache key.

### 1.5 `cms.item_schedules` ~ (Cms, SE-11)

| Column | Type | Rule |
|---|---|---|
| **+** `redirect_old_address` | `boolean not null default false` | applied when a scheduled publish changes the slug |
| **+** `redirect_to_page_id` | `uuid null` | applied when a scheduled unpublish runs (FR-032) |

### 1.6 `pages.publish_schedules` ~ (Pages, SE-11)

| Column | Type | Rule |
|---|---|---|
| **+** `redirect_to_page_id` | `uuid null` | page unpublish schedules only; applied by `RunDueScheduleHandler` when the page was live |

### 1.7 `pages.redirect_changes` + (Pages, SE-13a)

| Column | Type | Rule |
|---|---|---|
| `id` | `uuid` pk | |
| `site_id` | `uuid not null` | site filter |
| `redirect_id` | `uuid not null` | no foreign key: the record outlives the redirect |
| `action` | `text not null` | `created`, `changed`, `turned_on`, `turned_off`, `deleted`, `imported`, `offered`, `retargeted` |
| `origin` | `text not null` | `manual`, `import`, `offer` |
| `actor_user_id` | `uuid null` | null when a schedule acted |
| `at` | `timestamptz not null` | |

Ids, codes and times only. No source, target or note (FR-021). Index `(site_id, at desc)`.
Written in the same transaction as the redirect change. Read by the redirect detail
response (`history`, the last 20 entries).

---

## 2. Page document additions (page schema v1, additive)

`root.props` gains an optional `seo` object (SE-02). Its shape:

| Key | Type | Rule |
|---|---|---|
| `title` | string ≤200 | rule 25 |
| `description` | string ≤500 | rule 25 |
| `ogTitle` | string ≤200 | |
| `ogDescription` | string ≤500 | |
| `ogImage` | `{ mediaId: uuid }` | media dependency (SE-20) |
| `canonical` | link (§2a) | rule 24; page targets are link dependencies |
| `noindex` | boolean | restricted (SE-03) |
| `noSitemap` | boolean | |
| `fallback` | `{ title?, description?, ogTitle?, ogDescription? }`, strings ≤500 | used when a binding is empty |
| `bind` | `{ title?, description?, ogTitle?, ogDescription?, ogImage?, noindex? }` of `binding` | binding-expression §2; `empty` must be `fallback`; `noindex` binding restricted (SE-03) |
| `structuredData` | `{ type: "Article" \| "Product" \| "Event", bind?: { <property>: binding } }` | properties from structured-data.md (SE-19) |

**Rules** (page-schema.md §3):

| # | Code | Rule |
|---|---|---|
| 22 | `seo_in_component` | component documents have no `root.props.seo` |
| 23 | (broken, not refused) | `seo.bind` and `seo.structuredData` on a page that is not a template are broken bindings |
| 24 | (schema, `props_invalid`) | a `url` canonical is absolute `http`/`https` without user info |
| 25 | (schema, `props_invalid`) | metadata strings contain no control characters |

A document with `seo.bind` or `seo.structuredData.bind` carries `bindingVersion: 1` (rule 13).

---

## 3. Domain rules and states

### 3.1 Indexable (FR-022)

```text
indexable(page)      = site.enabled ∧ ¬site.search_discouraged ∧ page.live ∧ ¬template
                       ∧ ¬seo.noindex ∧ ¬seo.noSitemap ∧ seo.canonical = ∅
indexable(item page) = site.enabled ∧ ¬site.search_discouraged ∧ template.live ∧ item.published
                       ∧ ¬template.seo.noindex ∧ ¬template.seo.noSitemap ∧ template.seo.canonical = ∅
                       ∧ ¬(published value of seo.bind.noindex field = true)
robots meta written  = site.search_discouraged ∨ seo.noindex ∨ bound hide value = true
```

### 3.2 Redirect states (derived, in admin responses)

| State | When |
|---|---|
| `active` | enabled, target resolvable, source not answered by content |
| `off` | `enabled = false` |
| `hidden` | a live page or item page answers the source (no query sources only) |
| `broken` | `page` or `item` target not live |
| `shortened` | on a create/update response: the target was replaced by a chain's final target |

### 3.3 Redirect matching (public path, SE-10)

```text
q := canonical(query)
if q ≠ ∅ and exact(path, q) → redirect
if live page(path)          → page (or 301 to normalised form)
if live item page(path)     → item page (or 301 to normalised form)
if exact(path, ∅)           → redirect
if prefix(longest P with path starts with P + "/") → redirect to target + remainder
→ not found
```

### 3.4 Offered redirects (SE-11)

| Change | Offered redirect(s) |
|---|---|
| Slug change, move, new home page (live pages) | exact: each changed live old path → `page` |
| Live template's pattern changes (its own or an ancestor's move/slug) | prefix: old prefix → new prefix (`path`) |
| Template turned back into a page | none for its items (a prefix keeps the rest of the path, so old prefix → parent would loop; decided during implementation) |
| Page unpublished or deleted (`redirectTo`) | exact: its address → chosen `page` |
| Item slug changes on publish, scheduled publish, rollback | exact: old item address → `item` (via `Cms.ItemRedirectRequested`) |
| Item unpublished or deleted (`redirectTo`) | exact: item address → chosen `page` |

In every case, old addresses that now hold live content are skipped (FR-031). Then `path`
redirects targeting an old address are retargeted, and redirects this would loop are turned
off.

---

## 4. Error codes (new)

| Code | HTTP | When |
|---|---|---|
| `pages.seo_restricted` | 403 | an Editor changed `seo.canonical`, `seo.noindex` or its binding (SE-03) |
| `seo_in_component` | 400 | document rule 22 (rules 24 and 25 fail the schema as `props_invalid`) |
| `redirects.source_invalid` | 400 | bad path, reserved, home without query, query on prefix |
| `redirects.target_invalid` | 400 | bad scheme, user info, prefix with `url`/`item`, unknown page or item |
| `redirects.duplicate` | 409 | source already has a redirect (names it) |
| `redirects.loop` | 409 | would loop (names the redirect closing it) |
| `redirects.limit_reached` | 409 | over 10,000 for the site |
| `redirects.import_encoding` | 400 | the file is not UTF-8 |
| `redirects.import_too_large` | 413 | over 1 MiB or 5,000 rows |
| `sites.search_invalid` | 400 | suffix, organisation or robots rules out of bounds |
| `sites.out_of_date` | 409 | stale site `version` (existing) |

Import line reasons reuse the `redirects.*` codes, plus `redirects.import_row_malformed`.

**Warnings** (in `200`/`201` responses, never refusals):

| Code | Where | When |
|---|---|---|
| `redirect_hidden` | page create, slug change, move, home change, template removal, publish | the page's live address is the source of an enabled redirect without a query (spec edge case); names the redirect |
| `source_holds_content` | redirect create, update | a query source whose path is a live page or item page (FR-017a) |
| `source_uses_page_param` | redirect create, update | a query source with a parameter named like a paginated list's parameter on the page at that path (FR-017a) |

---

## 5. Events

| Event | Published by | Payload | Subscribers |
|---|---|---|---|
| **+** `Cms.ItemRedirectRequested` | Cms | `siteId`, `collectionId`, `itemId`, `oldSlug`, `reason` (`moved`, `removed`), `targetPageId`, `statusCode` (301), `actorUserId` | Pages (creates the redirect, idempotent through the unique index and `processed_events`) |

The existing Cms item and collection events are now also handled for the sitemap generation.
The existing Media events are also handled for site-wide images (SE-20, SE-22).

---

## 6. Cache keys

| Response | Key | Moves when |
|---|---|---|
| page (012) | `page:{pageId}:{generation}:{design}:{siteVersion}:{base}:{params}:{stamp}` | unchanged shape; `generation` now also moves for ancestor title changes (SE-18) |
| item page (016) | unchanged | unchanged |
| **+** sitemap file | `sitemap:{generation}:{siteVersion}:{base}:{file}:{stamp}` | `pages.sitemap_state.generation`, site settings, build |
| **+** robots.txt | `robots:{siteVersion}:{base}:{stamp}` | site settings, build |
| redirects | none | read per request |
