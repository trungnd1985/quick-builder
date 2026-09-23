# Phase 1 Data Model: Project Foundation and Skeleton

**Feature**: `001-foundation-skeleton` | **Date**: 2026-09-22

One PostgreSQL database. Each module owns one schema, one `DbContext` and one migrations
history table. This feature creates exactly one module schema: `sites`.

---

## Schema `sites`

### `sites.sites`

The site aggregate. **Not** site-owned — a site *is* the scope — so it carries no global
query filter. Administrative reads list across all sites, which is the deliberate bypass
described in FR-013a and D-07.

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK | Application-generated (UUIDv7) so inserts stay index-friendly |
| `name` | `text` | NOT NULL, 1–200 chars | Display name; not unique — two customers may both have "Blog" |
| `created_at_utc` | `timestamptz` | NOT NULL | |
| `updated_at_utc` | `timestamptz` | NOT NULL | |

**Validation rules** (enforced in the `CreateSite` validator, FR-008):
- `name` trimmed, non-empty, at most 200 characters.
- At least one host name supplied at creation, each valid per the rules below.

**Lifecycle**: created only. No rename, disable or delete in this feature — all deferred
by the spec's Out of Scope. The table therefore has no status column; adding one later is
a forward-only migration.

---

### `sites.site_host_names`

Site-owned. This is the worked example of automatic site scoping (FR-013): it carries
`site_id`, and the global query filter applies to it.

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK | |
| `site_id` | `uuid` | NOT NULL, FK → `sites.sites(id)` ON DELETE CASCADE | The filter column |
| `host_name` | `citext` | NOT NULL, **UNIQUE across the table** | Case-insensitive; uniqueness is global, not per site |
| `created_at_utc` | `timestamptz` | NOT NULL | |

**Indexes**: unique on `host_name`; non-unique on `site_id`.

**Why uniqueness is global**: resolution goes host name → site. If two sites could claim
one host name the mapping would be ambiguous, which is the edge case the spec calls out
("Two sites claim the same host name"). The unique constraint makes ambiguity
unrepresentable; the handler turns the constraint violation into an expected failure with
a machine-readable reason (FR-009), never an unhandled exception.

**Validation rules**:
- Lowercased and trimmed before storage.
- Valid host name shape: labels of letters, digits and hyphens, no leading or trailing
  hyphen, at most 253 characters total. An optional port is rejected — ports are the
  proxy's concern (D-12).
- Rejected as a duplicate if already present on any site.

**The `citext` decision**: host names are case-insensitive by definition. `citext` puts
that in the column rather than relying on every caller lowercasing correctly. Requires the
`citext` extension, created in the first migration.

---

### `sites.outbox_messages`

Per-module outbox (D-01). Lives in the module's own schema and is mapped on
`SitesDbContext`, so writing an event and the state change that caused it is one
`SaveChanges` in one transaction (FR-015, FR-021).

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK | Also the event id carried to handlers |
| `event_type` | `text` | NOT NULL | Stable contract name, e.g. `Sites.SiteCreated` |
| `payload` | `jsonb` | NOT NULL | Serialized event |
| `occurred_at_utc` | `timestamptz` | NOT NULL | When the state change happened |
| `available_at_utc` | `timestamptz` | NOT NULL | Next eligible attempt; equals `occurred_at_utc` initially, pushed out by backoff |
| `attempts` | `int` | NOT NULL, default 0 | |
| `completed_at_utc` | `timestamptz` | NULL | Set on success; retention sweeps on this |
| `failed_at_utc` | `timestamptz` | NULL | Set when attempts are exhausted (FR-018) |
| `last_error` | `text` | NULL | Message and type only — never payload contents (FR-032) |
| `trace_id` | `text` | NULL | Trace id of the originating request, so dispatch correlates back (D-10) |

**Indexes**:
- Partial index on `(available_at_utc)` `WHERE completed_at_utc IS NULL AND failed_at_utc IS NULL` — the claim query's only index, kept small because completed rows drop out of it.
- Index on `(completed_at_utc)` `WHERE completed_at_utc IS NOT NULL` — for the retention sweep.

**Claim query** (D-02): select the pending batch ordered by `available_at_utc` with
`FOR UPDATE SKIP LOCKED`, process, set `completed_at_utc`, commit. A crashed instance
rolls back and the rows unlock, satisfying FR-019's reclaim requirement with no lease
bookkeeping.

**State transitions**:

```text
pending ──claim──> in-flight ──success──> completed ──retention sweep──> deleted
                        │
                        └──failure──> pending (attempts+1, available_at_utc pushed out)
                                          │
                                          └── attempts exhausted ──> failed (terminal)
```

`failed` is terminal in this feature: no automatic retry and no admin requeue screen.
Failed rows are retained indefinitely and are the thing an operator greps for. A requeue
capability is deliberately not built (Principle XI) — no spec needs it yet.

---

### `sites.processed_events`

Idempotency ledger (D-03, FR-017). Written in the same transaction as the handler's
effect, so there is no window where the effect lands and the marker does not.

| Column | Type | Constraints |
|---|---|---|
| `event_id` | `uuid` | PK part 1 |
| `handler_name` | `text` | PK part 2 |
| `processed_at_utc` | `timestamptz` | NOT NULL |

Composite primary key `(event_id, handler_name)` — one event may have several handlers
and each is deduplicated independently. A duplicate-key violation on insert means "already
processed": the transaction rolls back and the message is marked completed without running
the effect again.

---

### `sites.__EFMigrationsHistory`

Each module keeps its own migrations history table inside its own schema (FR-022,
constitution Principle I). Applied under a per-module advisory lock (D-04).

---

## Cross-cutting types (no table)

These live in `Shared.Kernel` / `Shared.Infrastructure` and shape the code rather than the
database.

- **`Result` / `Result<T>`** — success, expected failure (with a machine-readable error
  code and message), or unexpected failure. FR-009's three-way distinction; entry points
  map it to HTTP without inspecting exceptions.
- **`ISiteOwned`** — marker interface carrying `SiteId`. Every implementing entity gets
  the global query filter automatically; an architecture test asserts that every entity
  with a `SiteId` property implements it, so the filter cannot be forgotten by omission.
- **`ISiteContext`** — scoped, nullable `SiteId`. Populated by public-surface middleware,
  left null on the administrative surface (D-08).
- **`StartupState`** — `WaitingForDatabase` | `Migrating` | `Ready` | `Failed`, read by
  the readiness probe (D-17).

---

## Entity relationships

```text
Site (1) ──────< (many) SiteHostName
  │                        │
  │                        └── unique host_name across all sites
  │
  └── creation writes SiteCreated into sites.outbox_messages
      in the same transaction

OutboxMessage ──dispatch──> handler ──writes──> ProcessedEvent
                                                (same transaction as the effect)
```

---

## What this feature deliberately does not model

- No users, roles or permissions — authentication is out of scope.
- No site status, deletion or rename — deferred by the spec.
- No `jsonb` content of any kind — pages, collections and schemas belong to later modules.
- No cross-module foreign keys, ever. A later module referencing a site stores the
  `SiteId` value and reads through `ISiteDirectory` in `Sites.Contracts`; PostgreSQL-level
  foreign keys across module schemas would couple their migrations together.
