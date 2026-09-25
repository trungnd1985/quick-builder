# Data Model: Sites and Host Name Resolution

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

All tables live in the `sites` schema, which the Sites module owns (001 FR-022). This
feature adds columns and one index. It creates no tables. Changes are marked **new**.

## `sites.sites`: the Site aggregate root

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `id` | `uuid` | no | | Version-7 GUID assigned by the domain. Never changes. |
| `name` | `varchar(200)` | no | | Trimmed. 1–200 characters. Not unique (see Validation). |
| `scheme` **new** | `text` | no | `'https'` | `CHECK (scheme IN ('https','http'))`. S-08. |
| `default_language` **new** | `text` | no | `'en'` | One of the 40 tags in S-09, stored in canonical case. Validated in the domain, no database check, so the list can grow without a migration. |
| `status` **new** | `text` | no | `'enabled'` | `CHECK (status IN ('enabled','disabled'))`. S-10. |
| `created_at_utc` | `timestamptz` | no | | |
| `updated_at_utc` | `timestamptz` | no | | Set on **every** aggregate change, including host-name add and remove. This is what makes the `xmin` check below cover the whole aggregate. |
| `xmin` (system) | `xid` | | | Mapped as `uint Version` with `IsRowVersion()`, and used as the optimistic concurrency token (S-05). Not a real column, so no migration is needed. |

`Site` is not `ISiteOwned`: a site is the scope, not something inside one. Admin reads
cross sites through `AcrossAllSites` with a reason, as before.

## `sites.site_host_names`: Site Host Name (`ISiteOwned`)

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `id` | `uuid` | no | | |
| `site_id` | `uuid` | no | | FK → `sites.id`, `ON DELETE CASCADE` (unchanged). Query filter `site_id = CurrentSiteId`. |
| `host_name` | `citext` (≤ 253) | no | | The normalised ASCII form (S-04). |
| `is_primary` **new** | `boolean` | no | `false` | Exactly one per site (see invariants). |
| `created_at_utc` | `timestamptz` | no | | Orders the upgrade's choice of primary. |

**Indexes**

| Name | Definition | Purpose |
|---|---|---|
| `ux_site_host_names_host_name` | `UNIQUE (host_name)` | Existing. Makes names unique across the installation, whatever the site's status (FR-008). It is also the resolution probe (S-01). |
| `ix_site_host_names_site_id` | `(site_id)` | Existing. |
| `ux_site_host_names_primary` **new** | `UNIQUE (site_id) WHERE is_primary` | At most one primary per site, enforced by the database. |

## Invariants of the `Site` aggregate

Enforced in `Site` and re-checked on every retry after a concurrency conflict (S-05):

1. A site has **at least one** host name.
2. **Exactly one** host name is primary.
3. `primaryHostName` in a settings change must be one of the site's host names.
4. The primary host name can't be removed (`sites.primary_host_name_removal`). Nor can the
   only host name (`sites.last_host_name`).
5. A host name is unique across the installation, and it may not equal a configured admin
   host name (S-13). Uniqueness within the site is checked in the domain, across sites
   by the unique index, and against admin host names from configuration.
6. Every change updates `updated_at_utc`, and so moves `xmin`.

## Normalisation (`Shared.Kernel/MultiSite/HostNames`)

`TryNormalise(input) → ascii | refused`. The steps are in S-04: trim, refuse structural
characters, handle IP literals, remove a trailing dot, apply IDNA ASCII, then the label
grammar. `ToDisplay(ascii)` returns the Unicode form for the admin shell. The same
function is used for storage, comparison, request resolution and admin host names.

## State: site availability

```text
           disable (sites.availability.manage)
 ┌─────────┐ ─────────────────────────────▶ ┌──────────┐
 │ enabled │                                 │ disabled │
 └─────────┘ ◀───────────────────────────── └──────────┘
           enable (sites.availability.manage)
```

- **Created**: every site is created `enabled`. Upgraded sites become `enabled` too (the
  column default).
- **Idempotent**: disabling a disabled site, or enabling an enabled one, changes nothing
  and publishes nothing.
- **What a disabled site keeps**: host names, which stay taken, plus grants, settings and
  every module's data.
- **What changes when disabled**: public resolution returns nothing (S-03), and the admin
  shell shows the state.
- **Deletion**: there is no deleted state in this feature. #21 will extend `status`.

## Derived values (never stored)

| Value | Rule |
|---|---|
| `baseUrl` | `{scheme}://{primary host name}{Hosting:BasePath}`, or `…/` when the base path is empty (S-07) |
| `displayName` of a host name | `HostNames.ToDisplay(host_name)` |
| `version` | `xmin` of the `sites` row, as an unsigned 32-bit integer |
| `permissions` | Per request: `IAccessEvaluator.HasCapabilityAsync` for `sites.settings.manage`, `sites.host_names.manage` and `sites.availability.manage` on the site. HTTP response only, not on `SiteDto`. |

## Contract types (`QuickBuilder.Sites.Contracts`)

`SiteDto` gains properties. Its existing ones are unchanged:

```text
SiteDto(
  Id, Name, HostNames,               // existing (HostNames: ASCII strings)
  CreatedAtUtc,                      // existing
  PrimaryHostName, Scheme, BaseUrl,  // new
  DefaultLanguage, Status, Version,  // new
  UpdatedAtUtc)                      // new
```

`ICurrentSite` (**new**) has one member, `SiteDto? Site`: the site resolved for this
public request. It is null on the administrative surface. See
[contracts/site-context.md](./contracts/site-context.md).

## Integration events (**new**, all published through the Sites outbox)

| Event type | Payload (besides `Id` and `OccurredAtUtc`) | Published when |
|---|---|---|
| `Sites.HostNameAdded` | `SiteId`, `HostName`, `ActorUserId` | A host name is added to an existing site. Creation still publishes only `Sites.SiteCreated`, which already lists its host names. |
| `Sites.HostNameRemoved` | `SiteId`, `HostName`, `ActorUserId` | A host name is removed |
| `Sites.SiteDisabled` | `SiteId`, `ActorUserId` | The status changes to `disabled` |
| `Sites.SiteEnabled` | `SiteId`, `ActorUserId` | The status changes to `enabled` |

Event type names are permanent contract names, as with `Sites.SiteCreated`.

## Identity: security event catalogue additions

| Type | Actor | Subject | Details |
|---|---|---|---|
| `site_host_name_added` | `ActorUserId` | none | `{ siteId, hostName }` |
| `site_host_name_removed` | `ActorUserId` | none | `{ siteId, hostName }` |
| `site_disabled` | `ActorUserId` | none | `{ siteId }` |
| `site_enabled` | `ActorUserId` | none | `{ siteId }` |

Outcome is always `succeeded`. Refused attempts are ordinary validation or authorization
failures, and aren't recorded, which is consistent with how 002 treats refusals of
content actions. The rows are appended by Identity's handlers for the events above, and
deduplicated through Identity's `processed_events`.

## Validation and error codes

| Code | Status | When |
|---|---|---|
| `sites.invalid_name` | 400 | Empty or longer than 200 characters after trimming |
| `sites.invalid_host_name` | 400 | Fails `HostNames.TryNormalise` |
| `sites.host_name_taken` | 409 | Held by any site, including this one or a disabled one, or configured as an admin host name. The response never names the holder. |
| `sites.host_name_not_found` | 404 | Removing a host name the site doesn't hold |
| `sites.no_host_names` | 400 | Create with an empty list (existing) |
| `sites.primary_host_name_invalid` | 400 | A primary that isn't among the site's host names |
| `sites.primary_host_name_removal` | 409 | Removing the primary |
| `sites.last_host_name` | 409 | Removing the only host name |
| `sites.unsupported_language` | 400 | A tag not in S-09 |
| `sites.invalid_scheme` | 400 | Anything but `https` or `http` |
| `sites.out_of_date` | 409 | Stale `version` on settings, or 3 lost races on a host-name or availability action |
| `public.not_found` | 404 | Public surface: no such path on the resolved site, or an admin path on a host not in `Hosting:AdminHostNames` (S-13) |
| `sites.not_found` | 404 | An unknown site, or one the caller may not see (002 FR-022) |

Site names aren't unique. Two sites may share a name, and the admin shell distinguishes
them by primary host name. This was left open in clarification as low impact, and nothing
depends on uniqueness.
