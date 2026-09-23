# Data Model: Identity, First-Run Setup and Authorization

**Feature**: `002-identity-setup` | **Date**: 2026-09-23

Everything below lives in the new `identity` schema. It is owned by the `Identity`
module's `IdentityDbContext`, with the migration history table in its own schema, as
001's FR-022 requires. Column names are snake_case through 001's naming pass. Timestamps
are `timestamptz` in UTC, identifiers are UUIDv7, and case-insensitive text uses
`citext` (the extension is already enabled by 001).

No table here is **site-owned** in 001's sense (`ISiteOwned`, global query filter).
`role_grants.site_id` *refers to* a site, but grants are installation data managed by
administrators, who have no resolved site on the admin surface. That is why
`RoleGrant` carries the kernel's `[InstallationScoped("…")]` marker. It exempts the
entity from the "entities with a `SiteId` are site-owned" architecture rule, the reason
string is required, and a dedicated test covers the exemption (see
[plan.md](./plan.md), Constitution Check V).

No foreign key crosses into the `sites` schema (001 FR-022). Site identifiers in grants
are validated through `ISiteDirectory` when a grant is written.

```text
installation (1 row)

users 1───* role_grants        (site_id → sites.sites.id, validated by ISiteDirectory, no FK)
  │ 1
  ├───* sessions
  ├───* set_password_links     (at most one live per user)
  ├───* known_devices          (browsers exempt from the per-account wait)
  └───* security_events        (as actor or as subject; no FK: events outlive nothing but retention)

sign_in_throttles              (keyed by client address or email hash; no FK by design)
```

---

## `installation`

Holds the setup state. Exactly one row, created at startup when missing (research R-12).

| Column | Type | Rules |
|---|---|---|
| `id` | `smallint` PK | `CHECK (id = 1)` |
| `claimed_at_utc` | `timestamptz` null | Null means setup mode (FR-001). Set once and never cleared. |
| `setup_code` | `varchar(12)` null | Crockford base32 without dashes. Non-null only while unclaimed (R-09). |
| `setup_code_created_at_utc` | `timestamptz` null | |

**Invariant**: `claimed_at_utc IS NULL` ⇔ `setup_code IS NOT NULL`, enforced by a
`CHECK`. **Transition**: Unclaimed → Claimed, by the single conditional update in R-12.
It is irreversible.

---

## `users`

| Column | Type | Rules |
|---|---|---|
| `id` | `uuid` PK | UUIDv7 |
| `email` | `citext` | Trimmed and NFC-normalised before storage. At most 254 characters (RFC 5321). Unique: `ux_users_email`. |
| `display_name` | `varchar(200)` | Trimmed, 1–200 characters |
| `password_hash` | `varchar(200)` null | R-03 format. Null until the first set-password link is used. |
| `status` | `varchar(16)` | `active` \| `deactivated`, enforced by a `CHECK` |
| `created_at_utc` | `timestamptz` | |
| `updated_at_utc` | `timestamptz` | |
| `last_sign_in_at_utc` | `timestamptz` null | |
| `deactivated_at_utc` | `timestamptz` null | Non-null ⇔ `status = 'deactivated'` |

**Validation**:
- **Email**: syntactically valid (one `@`, non-empty local part and domain, no
  whitespace), unique case-insensitively (FR-007). A uniqueness violation surfaces as
  `409 identity.email_taken`.
- **Password** (FR-008), checked before hashing:
  - Length is 12–128 code points after NFKC normalisation.
  - It is not on the common-password list (R-15).
  - It is not equal to the account's email address or display name, compared
    case-insensitively.
  - There are no composition rules.

**States**:

```text
            create (admin)                 set-password link used
  ──────▶ Active, no password ─────────────────────────────▶ Active, has password
                 │  ▲                                              │  ▲
     deactivate  │  │ reactivate                        deactivate │  │ reactivate
                 ▼  │                                              ▼  │
          Deactivated, no password                     Deactivated, has password
```

- **Who can sign in**: only *Active, has password* accounts. Every other state gets the
  same generic `401` as a wrong password (R-08).
- **Deactivation**: revokes all of the user's sessions and invalidates any live
  set-password link, in the same transaction.
- **Reactivation**: restores the ability to sign in with the existing password, if the
  account has one. Past sessions stay revoked.
- **Deletion**: none (FR-010).
- **Email change** (FR-010a): administrators only. Revokes all sessions, invalidates the
  live link, and records `email_changed` with both addresses.

---

## `role_grants`

| Column | Type | Rules |
|---|---|---|
| `id` | `uuid` PK | |
| `user_id` | `uuid` FK → `users.id` | `ON DELETE RESTRICT` (users are never deleted) |
| `role` | `varchar(16)` | `administrator` \| `designer` \| `editor` |
| `site_id` | `uuid` null | The site the grant applies to. There is no FK because the site lives in another module. |
| `granted_at_utc` | `timestamptz` | |
| `granted_by_user_id` | `uuid` null | Null for the grant created by setup |

**Constraints** (FR-020, FR-020a):
- `CHECK ((role = 'administrator') = (site_id IS NULL))`: Administrator is
  installation-wide only; Designer and Editor always name a site.
- A unique partial index on `(user_id)` where `role = 'administrator'` allows at most one
  Administrator grant per user.
- A unique partial index on `(user_id, site_id)` where `site_id IS NOT NULL` allows one
  role per user per site.
- `site_id` must resolve through `ISiteDirectory.FindByIdAsync` when written. An unknown
  site gives `400` with a field error on `siteRoles[n].siteId`.

**Scope is explicit** (FR-020a): `site_id IS NULL` means installation scope and non-null
means site scope. A later per-site administrative role only needs a new `role` value and
an adjusted `CHECK`. Existing rows and their evaluation stay the same.

**Changes are whole replacements**: `PUT …/grants` replaces a user's full grant set in
one transaction under the last-administrator lock (R-13).

**Future site deletion**: grants for a deleted site will be removed by an Identity
handler for a `SiteDeleted` integration event. Deleting sites is out of scope for this
feature, so neither the event nor the handler exists yet. The grant model needs no
change for it.

---

## `sessions`

| Column | Type | Rules |
|---|---|---|
| `id` | `uuid` PK | |
| `user_id` | `uuid` FK → `users.id` | Index `ix_sessions_user_id` for revoking all of a user's sessions |
| `token_hash` | `bytea` (32) | SHA-256 of the cookie token. Unique: `ux_sessions_token_hash`. |
| `created_at_utc` | `timestamptz` | |
| `last_seen_at_utc` | `timestamptz` | Written at most once a minute (R-04) |
| `absolute_expires_at_utc` | `timestamptz` | `created_at + AbsoluteLifetime`, fixed when the session is created |
| `revoked_at_utc` | `timestamptz` null | |
| `revocation_reason` | `varchar(32)` null | `sign_out` \| `sign_out_everywhere` \| `sign_out_others` \| `password_changed` \| `password_set` \| `email_changed` \| `deactivated` |
| `client_address` | `inet` null | For security events, not for authentication |

**A session is valid when** all of these hold: `revoked_at_utc IS NULL`,
`now < absolute_expires_at_utc`, `now < last_seen_at_utc + IdleTimeout`, and the user's
`status = 'active'`. This is one indexed lookup by `token_hash` joined to `users`.

**States**: Active → Revoked (with a reason) or Expired (derived, never written).
Expired and revoked rows are removed by the hourly sweep once they are older than
`AbsoluteLifetime`. They serve no purpose after that, because the security event is the
durable record.

---

## `set_password_links`

| Column | Type | Rules |
|---|---|---|
| `id` | `uuid` PK | |
| `user_id` | `uuid` FK → `users.id` | |
| `token_hash` | `bytea` (32) | SHA-256 of the token. Unique. |
| `issued_at_utc` | `timestamptz` | |
| `expires_at_utc` | `timestamptz` | `issued_at + SetPasswordLinks:Lifetime` |
| `issued_by_user_id` | `uuid` null | Null means issued by the operator recovery command (R-16) |
| `used_at_utc` | `timestamptz` null | |
| `invalidated_at_utc` | `timestamptz` null | |
| `invalidation_reason` | `varchar(32)` null | `replaced` \| `deactivated` \| `email_changed` |

**One live link per user**: a unique partial index on `(user_id)` where
`used_at_utc IS NULL AND invalidated_at_utc IS NULL` (FR-011a).

**A link is usable when** it is not used, not invalidated, `now < expires_at_utc`, and
the user is `active`. Every other case gives `410 identity.link_invalid`, with nothing
revealed about the account.

**States**: Live → Used | Invalidated | Expired (derived). Rows are removed by the sweep
30 days after they expire or stop being live.

---

## `sign_in_throttles`

| Column | Type | Rules |
|---|---|---|
| `kind` | `varchar(16)` | `client` \| `account` |
| `key` | `varchar(64)` | For `client`: the normalised address (IPv6 as its /64 prefix). For `account`: the hex SHA-256 of the normalised email. |
| `consecutive_failures` | `integer` | Reset to 0 on success (account) or when the block expires (client) |
| `blocked_until_utc` | `timestamptz` null | `client` only |
| `next_attempt_at_utc` | `timestamptz` null | `account` only: the earliest time an attempt will be evaluated |
| `updated_at_utc` | `timestamptz` | |

**Primary key** `(kind, key)`. Every change is a single
`INSERT … ON CONFLICT (kind, key) DO UPDATE … RETURNING` (R-07). Rows untouched for 24
hours are removed by the sweep.

---

## `known_devices`

A browser that has signed in successfully to an account (FR-015a, research R-07).

| Column | Type | Rules |
|---|---|---|
| `id` | `uuid` PK | |
| `user_id` | `uuid` FK → `users.id` | Index `ix_known_devices_user_id` |
| `token_hash` | `bytea` (32) | SHA-256 of one token in the `qb_device` cookie. Unique. |
| `created_at_utc` | `timestamptz` | |
| `last_used_at_utc` | `timestamptz` | Updated on each successful sign-in from the device |
| `consecutive_failures` | `integer` | Reset on success. Reaching `KnownDeviceFailureThreshold` revokes the device. |
| `revoked_at_utc` | `timestamptz` null | Set on deactivation, email change, set-password link use and administrator "sign out everywhere" |

**Valid when** `revoked_at_utc IS NULL` and `now < last_used_at_utc + KnownDeviceLifetime`.
Invalid rows are removed by the hourly sweep.

---

## `security_events`

Append-only (R-14).

| Column | Type | Rules |
|---|---|---|
| `id` | `uuid` PK | UUIDv7 |
| `occurred_at_utc` | `timestamptz` | Index `ix_security_events_occurred_at` (serves retention and future browsing) |
| `event_type` | `varchar(48)` | See the catalogue below |
| `outcome` | `varchar(16)` | `succeeded` \| `failed` \| `refused` |
| `actor_user_id` | `uuid` null | Who acted. Null for anonymous callers and the operator command. |
| `subject_user_id` | `uuid` null | The account affected |
| `client_address` | `inet` null | Null for the operator command |
| `details` | `jsonb` | Small and typed per event: `{ "oldEmail", "newEmail" }`, `{ "grants": [...] }`, `{ "reason": "..." }`. Never passwords, tokens, setup codes or session identifiers. |

### Event catalogue (FR-029)

| `event_type` | Outcomes | `details` |
|---|---|---|
| `setup_completed` | succeeded | — |
| `setup_code_rejected` | refused | — |
| `sign_in` | succeeded, failed, refused (throttled) | `{ "reason": "invalid_credentials" \| "client_blocked" \| "account_delay" }` on failure, plus `{ "knownDevice": true }` when the exemption applied. `subject_user_id` is set when the address matches an existing account and is null otherwise. The attempted email is **not** stored, so a mistyped password pasted into the email field never persists. |
| `known_device_revoked` | succeeded | `{ "reason": "failures" \| "deactivated" \| "email_changed" \| "password_set" \| "sign_out_everywhere" }` |
| `client_blocked` | refused | `{ "until": … }` |
| `sign_out` | succeeded | — |
| `sessions_revoked` | succeeded | `{ "scope": "everywhere" \| "others", "by": "self" \| "administrator" }` |
| `password_changed` | succeeded, failed | — |
| `account_created` | succeeded | `{ "email", "grants" }` |
| `email_changed` | succeeded | `{ "oldEmail", "newEmail" }` |
| `display_name_changed` | succeeded | — |
| `account_deactivated` / `account_reactivated` | succeeded, refused (last administrator) | — |
| `grants_changed` | succeeded, refused (last administrator) | `{ "before": [...], "after": [...] }` |
| `set_password_link_issued` | succeeded | `{ "expiresAt", "issuedBy": "administrator" \| "operator" }` |
| `set_password_link_used` | succeeded, refused | `{ "reason" }` on refusal |
| `operator_recovery_used` | succeeded, refused | `{ "command": "recovery-link" \| "setup-code" }` |

**Retention**: rows where `occurred_at_utc < now − RetentionPeriod` are deleted hourly in
batches (FR-029a). No other code path updates or deletes them.

---

## Shared-kernel additions

These are plain C# types in `QuickBuilder.Shared.Kernel/Authorization` (research R-05).
None is stored.

| Type | Shape |
|---|---|
| `SystemRole` | enum: `Administrator`, `Designer`, `Editor` |
| `CapabilityScope` | enum: `Installation`, `Site` |
| `CapabilityDefinition` | `record (string Name, CapabilityScope Scope, IReadOnlyList<SystemRole> GrantedTo)`. `Administrator` is implicit and never listed. |
| `RequireCapabilityAttribute` | `(string name)`, placed on actions |
| `RequireSignedInAttribute` | Marker for self-service actions |
| `InstallationScopedAttribute` | `(string reason)`, placed on domain entities that reference a site without being site-owned |
| `IAccessEvaluator` | `bool IsSignedIn`, `Guid? UserId`, `bool IsAdministrator`, `Task<bool> HasCapabilityAsync(string name, Guid? siteId, CancellationToken)`, `Task<SiteVisibility> GetVisibleSitesAsync(CancellationToken)` |
| `SiteVisibility` | `record (bool All, IReadOnlySet<Guid> SiteIds)` |

### Capabilities registered in this feature

| Capability | Scope | Designer | Editor | Registered by |
|---|---|---|---|---|
| `identity.users.manage` | Installation | — | — | Identity |
| `sites.create` | Installation | — | — | Sites |
| `sites.view` | Site | ✓ | ✓ | Sites |
| `sites.host_names.manage` | Site | — | — | Sites |

Administrators hold all four everywhere. Designer and Editor are identical in this
feature (spec Assumptions). They start to differ when the Pages, CMS and Media modules
register their own capabilities.
